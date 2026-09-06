namespace Armada.Server
{
    using System.Diagnostics;
    using System.Text;
    using System.Text.Json;
    using Armada.Core;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;
    using Armada.Runtimes;
    using Armada.Server.WebSocket;
    using SyslogLogging;

    /// <summary>
    /// Coordinates planning session lifecycle, captain reservation, dock ownership, turn execution, and dispatch conversion.
    /// </summary>
    public class PlanningSessionCoordinator
    {
        #region Private-Members

        private sealed class TurnState
        {
            public bool StopRequested { get; set; } = false;

            /// <summary>
            /// Whether to surface the model's reasoning for this turn (Ask-Armada parity).
            /// </summary>
            public bool ShowThinking { get; set; } = false;

            /// <summary>
            /// Whether to stream the reply incrementally. When false the reply is broadcast once at the end.
            /// </summary>
            public bool Stream { get; set; } = true;
        }

        private readonly string _Header = "[PlanningSessionCoordinator] ";
        private readonly LoggingModule _Logging;
        private readonly DatabaseDriver _Database;
        private readonly ArmadaSettings _Settings;
        private readonly IDockService _Docks;
        private readonly IAdmiralService _Admiral;
        private readonly AgentRuntimeFactory _RuntimeFactory;
        private readonly Func<string, string, string?, string?, string?, string?, string?, string?, Task> _EmitEventAsync;
        private readonly ArmadaWebSocketHub? _WebSocketHub;
        private readonly PlaybookService _Playbooks;
        private const int _MaxPlanningOutputChars = 131072;
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, TurnState> _ActiveTurns =
            new System.Collections.Concurrent.ConcurrentDictionary<string, TurnState>(StringComparer.Ordinal);
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Task<PlanningSession>> _StopOperations =
            new System.Collections.Concurrent.ConcurrentDictionary<string, Task<PlanningSession>>(StringComparer.Ordinal);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public PlanningSessionCoordinator(
            LoggingModule logging,
            DatabaseDriver database,
            ArmadaSettings settings,
            IDockService docks,
            IAdmiralService admiral,
            AgentRuntimeFactory runtimeFactory,
            Func<string, string, string?, string?, string?, string?, string?, string?, Task> emitEventAsync,
            ArmadaWebSocketHub? webSocketHub = null)
        {
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Docks = docks ?? throw new ArgumentNullException(nameof(docks));
            _Admiral = admiral ?? throw new ArgumentNullException(nameof(admiral));
            _RuntimeFactory = runtimeFactory ?? throw new ArgumentNullException(nameof(runtimeFactory));
            _EmitEventAsync = emitEventAsync ?? throw new ArgumentNullException(nameof(emitEventAsync));
            _WebSocketHub = webSocketHub;
            _Playbooks = new PlaybookService(_Database, _Logging);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Create and provision a planning session.
        /// </summary>
        public async Task<PlanningSession> CreateAsync(
            string? tenantId,
            string? userId,
            Captain captain,
            Vessel vessel,
            PlanningSessionCreateRequest request,
            CancellationToken token = default)
        {
            if (captain == null) throw new ArgumentNullException(nameof(captain));
            if (vessel == null) throw new ArgumentNullException(nameof(vessel));
            if (request == null) throw new ArgumentNullException(nameof(request));

            if (!captain.SupportsPlanningSessions)
                throw new InvalidOperationException(captain.PlanningSessionSupportReason ?? "This captain runtime is not supported for planning sessions.");

            if (captain.State != CaptainStateEnum.Idle)
                throw new InvalidOperationException("Captain " + captain.Name + " is not idle.");

            List<PlanningSession> captainSessions = await _Database.PlanningSessions
                .EnumerateByCaptainAsync(captain.Id, token)
                .ConfigureAwait(false);
            if (captainSessions.Any(s => s.Status == PlanningSessionStatusEnum.Active || s.Status == PlanningSessionStatusEnum.Responding || s.Status == PlanningSessionStatusEnum.Stopping))
                throw new InvalidOperationException("Captain " + captain.Name + " already has an active planning session.");

            if (!String.IsNullOrEmpty(tenantId) && request.SelectedPlaybooks.Count > 0)
            {
                await _Playbooks.ResolveSelectionsAsync(tenantId, request.SelectedPlaybooks, token).ConfigureAwait(false);
            }

            PlanningSession session = new PlanningSession
            {
                TenantId = tenantId,
                UserId = userId,
                CaptainId = captain.Id,
                VesselId = vessel.Id,
                FleetId = request.FleetId ?? vessel.FleetId,
                Title = !String.IsNullOrWhiteSpace(request.Title) ? request.Title.Trim() : "Planning: " + vessel.Name,
                Status = PlanningSessionStatusEnum.Created,
                PipelineId = request.PipelineId,
                SelectedPlaybooks = request.SelectedPlaybooks ?? new List<SelectedPlaybook>(),
                CreatedUtc = DateTime.UtcNow,
                LastUpdateUtc = DateTime.UtcNow
            };

            session = await _Database.PlanningSessions.CreateAsync(session, token).ConfigureAwait(false);

            try
            {
                string branchName = Constants.BranchPrefix + "planning/" + session.Id;
                Dock? dock = await _Docks.ProvisionAsync(vessel, captain, branchName, session.Id, token).ConfigureAwait(false);
                if (dock == null)
                    throw new InvalidOperationException("Dock provisioning failed for planning session " + session.Id + ".");

                session.DockId = dock.Id;
                session.BranchName = branchName;
                session.Status = PlanningSessionStatusEnum.Active;
                session.StartedUtc = DateTime.UtcNow;
                session.LastUpdateUtc = DateTime.UtcNow;
                await _Database.PlanningSessions.UpdateAsync(session, token).ConfigureAwait(false);

                captain.State = CaptainStateEnum.Planning;
                captain.CurrentMissionId = null;
                captain.CurrentDockId = dock.Id;
                captain.ProcessId = null;
                captain.LastHeartbeatUtc = DateTime.UtcNow;
                captain.LastUpdateUtc = DateTime.UtcNow;
                await _Database.Captains.UpdateAsync(captain, token).ConfigureAwait(false);
                _WebSocketHub?.BroadcastCaptainChange(captain.Id, captain.State.ToString(), captain.Name);

                BroadcastSessionChanged(session);
                await _EmitEventAsync(
                    "planning-session.created",
                    "Planning session " + session.Id + " created for captain " + captain.Name,
                    "planning-session",
                    session.Id,
                    captain.Id,
                    null,
                    vessel.Id,
                    null).ConfigureAwait(false);

                return session;
            }
            catch (Exception ex)
            {
                if (!String.IsNullOrWhiteSpace(session.DockId))
                {
                    try
                    {
                        await _Docks.ReclaimAsync(session.DockId, session.TenantId, token).ConfigureAwait(false);
                    }
                    catch
                    {
                    }
                }

                try
                {
                    Captain? failedCaptain = await _Database.Captains.ReadAsync(captain.Id, token).ConfigureAwait(false);
                    if (failedCaptain != null)
                    {
                        failedCaptain.State = CaptainStateEnum.Idle;
                        failedCaptain.CurrentMissionId = null;
                        failedCaptain.CurrentDockId = null;
                        failedCaptain.ProcessId = null;
                        failedCaptain.LastHeartbeatUtc = DateTime.UtcNow;
                        failedCaptain.LastUpdateUtc = DateTime.UtcNow;
                        await _Database.Captains.UpdateAsync(failedCaptain, token).ConfigureAwait(false);
                        _WebSocketHub?.BroadcastCaptainChange(failedCaptain.Id, failedCaptain.State.ToString(), failedCaptain.Name);
                    }
                }
                catch
                {
                }

                session.Status = PlanningSessionStatusEnum.Failed;
                session.FailureReason = ex.Message;
                session.CompletedUtc = DateTime.UtcNow;
                session.LastUpdateUtc = DateTime.UtcNow;
                await _Database.PlanningSessions.UpdateAsync(session, token).ConfigureAwait(false);
                BroadcastSessionChanged(session);
                throw;
            }
        }

        /// <summary>
        /// Append a user message and start a background planning turn.
        /// </summary>
        public async Task<PlanningSessionMessage> SendMessageAsync(PlanningSession session, string content, bool showThinking = false, bool stream = true, CancellationToken token = default)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (String.IsNullOrWhiteSpace(content)) throw new ArgumentNullException(nameof(content));

            session = await RequireSessionAsync(session.Id, token).ConfigureAwait(false);
            if (session.Status != PlanningSessionStatusEnum.Active)
                throw new InvalidOperationException("Planning session " + session.Id + " is not ready for a new message.");

            TurnState turnState = new TurnState { ShowThinking = showThinking, Stream = stream };
            if (!_ActiveTurns.TryAdd(session.Id, turnState))
                throw new InvalidOperationException("Planning session " + session.Id + " is already generating a response.");

            List<PlanningSessionMessage> existingMessages = await _Database.PlanningSessionMessages
                .EnumerateBySessionAsync(session.Id, token)
                .ConfigureAwait(false);
            int nextSequence = existingMessages.Count == 0 ? 1 : existingMessages.Max(m => m.Sequence) + 1;

            PlanningSessionMessage userMessage = new PlanningSessionMessage
            {
                PlanningSessionId = session.Id,
                TenantId = session.TenantId,
                UserId = session.UserId,
                Role = "User",
                Sequence = nextSequence,
                Content = content.Trim(),
                CreatedUtc = DateTime.UtcNow,
                LastUpdateUtc = DateTime.UtcNow
            };
            userMessage = await _Database.PlanningSessionMessages.CreateAsync(userMessage, token).ConfigureAwait(false);
            BroadcastMessageCreated(userMessage);

            PlanningSessionMessage assistantMessage = new PlanningSessionMessage
            {
                PlanningSessionId = session.Id,
                TenantId = session.TenantId,
                UserId = session.UserId,
                Role = "Assistant",
                Sequence = nextSequence + 1,
                Content = String.Empty,
                CreatedUtc = DateTime.UtcNow,
                LastUpdateUtc = DateTime.UtcNow
            };
            assistantMessage = await _Database.PlanningSessionMessages.CreateAsync(assistantMessage, token).ConfigureAwait(false);
            BroadcastMessageCreated(assistantMessage);

            session.Status = PlanningSessionStatusEnum.Responding;
            session.ProcessId = null;
            session.FailureReason = null;
            session.LastUpdateUtc = DateTime.UtcNow;
            session = await _Database.PlanningSessions.UpdateAsync(session, token).ConfigureAwait(false);
            BroadcastSessionChanged(session);

            _ = Task.Run(() => ExecuteTurnAsync(session.Id, assistantMessage.Id), CancellationToken.None);
            return userMessage;
        }

        /// <summary>
        /// Stop a planning session and release its resources.
        /// </summary>
        public async Task<PlanningSession> StopAsync(PlanningSession session, CancellationToken token = default)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));

            session = await PrepareStopAsync(session, token).ConfigureAwait(false);
            if (session.Status == PlanningSessionStatusEnum.Stopped || session.Status == PlanningSessionStatusEnum.Failed)
                return session;

            Task<PlanningSession> stopTask = EnsureStopOperation(session.Id);
            return await stopTask.ConfigureAwait(false);
        }

        /// <summary>
        /// Mark a planning session as stopping and let cleanup continue in the background.
        /// </summary>
        public async Task<PlanningSession> RequestStopAsync(PlanningSession session, CancellationToken token = default)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));

            session = await PrepareStopAsync(session, token).ConfigureAwait(false);
            if (session.Status == PlanningSessionStatusEnum.Stopped || session.Status == PlanningSessionStatusEnum.Failed)
                return session;

            _ = EnsureStopOperation(session.Id);
            return session;
        }

        /// <summary>
        /// Abort the in-flight planning turn without ending the session. Kills the current turn's captain
        /// runtime process; the background turn task then finalizes the assistant reply with whatever streamed
        /// so far and returns the session to <see cref="PlanningSessionStatusEnum.Active"/> on its own (no
        /// session-level stop is requested, so the completion path re-activates it). No-op unless the session
        /// is currently <see cref="PlanningSessionStatusEnum.Responding"/>. Mirrors Ask Armada's Stop, which
        /// cancels the runtime while leaving the chat usable.
        /// </summary>
        public async Task<PlanningSession> AbortTurnAsync(PlanningSession session, CancellationToken token = default)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));

            session = await RequireSessionAsync(session.Id, token).ConfigureAwait(false);
            if (session.Status != PlanningSessionStatusEnum.Responding)
                return session;

            if (session.ProcessId.HasValue)
            {
                Captain? captain = await _Database.Captains.ReadAsync(session.CaptainId, token).ConfigureAwait(false);
                if (captain != null)
                {
                    try
                    {
                        Armada.Runtimes.Interfaces.IAgentRuntime runtime = CreatePlanningRuntime(captain);
                        await runtime.StopAsync(session.ProcessId.Value, token).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _Logging.Warn(_Header + "error aborting planning turn process " + session.ProcessId.Value + " for session " + session.Id + ": " + ex.Message);
                    }
                }
            }

            return session;
        }

        /// <summary>
        /// Create a voyage from a planning session.
        /// </summary>
        public async Task<Voyage> DispatchAsync(PlanningSession session, PlanningSessionDispatchRequest request, CancellationToken token = default)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (request == null) throw new ArgumentNullException(nameof(request));

            session = await RequireSessionAsync(session.Id, token).ConfigureAwait(false);
            PlanningSessionMessage sourceMessage = await ResolveSourceMessageAsync(session, request.MessageId, token).ConfigureAwait(false);

            string title = !String.IsNullOrWhiteSpace(request.Title)
                ? request.Title.Trim()
                : BuildDefaultDispatchTitle(session, sourceMessage);
            string description = !String.IsNullOrWhiteSpace(request.Description)
                ? request.Description.Trim()
                : sourceMessage.Content.Trim();

            List<MissionDescription> missions = new List<MissionDescription>
            {
                new MissionDescription(title, description)
            };

            Voyage voyage = await _Admiral.DispatchVoyageAsync(
                title,
                "Created from planning session " + session.Id,
                session.VesselId,
                missions,
                session.PipelineId,
                session.SelectedPlaybooks,
                null,
                token).ConfigureAwait(false);

            voyage.SourcePlanningSessionId = session.Id;
            voyage.SourcePlanningMessageId = sourceMessage.Id;
            voyage = await _Database.Voyages.UpdateAsync(voyage, token).ConfigureAwait(false);

            _WebSocketHub?.BroadcastEvent(
                "planning-session.dispatch.created",
                "Dispatch created from planning session " + session.Id,
                new
                {
                    sessionId = session.Id,
                    voyageId = voyage.Id,
                    messageId = sourceMessage.Id
                });

            await _EmitEventAsync(
                "planning-session.dispatch.created",
                "Planning session " + session.Id + " created voyage " + voyage.Id,
                "planning-session",
                session.Id,
                session.CaptainId,
                null,
                session.VesselId,
                voyage.Id).ConfigureAwait(false);

            // The planning session has served its purpose once the work is dispatched, so release the reserved
            // captain and dock automatically instead of leaving them pinned until the operator ends the session
            // by hand. Best-effort: a release failure must not fail the dispatch that already succeeded.
            try
            {
                await RequestStopAsync(session, token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "error releasing planning session " + session.Id + " after dispatch: " + ex.Message);
            }

            return voyage;
        }

        /// <summary>
        /// Generate a dispatch-ready draft from a planning session without launching the voyage yet.
        /// </summary>
        public async Task<PlanningSessionSummaryResponse> SummarizeAsync(
            PlanningSession session,
            PlanningSessionSummaryRequest request,
            CancellationToken token = default)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (request == null) throw new ArgumentNullException(nameof(request));

            session = await RequireSessionAsync(session.Id, token).ConfigureAwait(false);
            if (!_ActiveTurns.TryAdd(session.Id, new TurnState()))
            {
                bool available = await WaitForTurnAvailabilityAsync(session.Id, TimeSpan.FromSeconds(10), token).ConfigureAwait(false);
                session = await RequireSessionAsync(session.Id, token).ConfigureAwait(false);
                if (!available || !_ActiveTurns.TryAdd(session.Id, new TurnState()))
                    throw new InvalidOperationException("Planning session " + session.Id + " is already generating a response.");
            }
            try
            {
                PlanningSessionMessage sourceMessage = await ResolveSourceMessageAsync(session, request.MessageId, token).ConfigureAwait(false);
                Captain captain = await RequireCaptainAsync(session.CaptainId, token).ConfigureAwait(false);
                Vessel vessel = await RequireVesselAsync(session.VesselId, token).ConfigureAwait(false);
                Dock dock = await RequireDockAsync(session.DockId, token).ConfigureAwait(false);
                List<PlanningSessionMessage> messages = await _Database.PlanningSessionMessages.EnumerateBySessionAsync(session.Id, token).ConfigureAwait(false);

                string promptFilePath = await WritePromptFileAsync(session, captain, vessel, dock, messages, token).ConfigureAwait(false);
                string preferredTitle = !String.IsNullOrWhiteSpace(request.Title) ? request.Title.Trim() : BuildDefaultDispatchTitle(session, sourceMessage);
                string prompt =
                    "Read `" + promptFilePath + "` and the selected planning message below.\n" +
                    "Produce a dispatch draft for Armada.\n" +
                    "Return JSON only with keys `title` and `description`.\n" +
                    "- `title` must be concise, actionable, and 80 characters or less.\n" +
                    "- `description` must be mission-ready markdown that tells a captain exactly what to do.\n" +
                    "- Preserve the user's concrete intent and constraints.\n" +
                    "- Do not mention that this was generated from a planning session.\n" +
                    "- If the existing session title is already strong, keep it close to `" + preferredTitle.Replace("`", "'") + "`.\n\n" +
                    "Selected planning message:\n" +
                    sourceMessage.Content.Trim();

                string turnDir = Path.Combine(_Settings.LogDirectory, "planning-sessions", session.Id);
                Directory.CreateDirectory(turnDir);
                string finalMessageFilePath = Path.Combine(turnDir, "summary-" + Guid.NewGuid().ToString("N") + ".txt");
                string logFilePath = Path.Combine(turnDir, "summary.log");

                PlanningSessionSummaryResponse draft = BuildFallbackSummary(session, sourceMessage, request.Title);
                try
                {
                    string runtimeOutput = await RunRuntimePromptAsync(session, captain, vessel, prompt, logFilePath, finalMessageFilePath, token).ConfigureAwait(false);

                    if (TryParseSummaryResponse(runtimeOutput, out PlanningSessionSummaryResponse? parsed) && parsed != null)
                    {
                        parsed.SessionId = session.Id;
                        parsed.MessageId = sourceMessage.Id;
                        if (String.IsNullOrWhiteSpace(parsed.Title))
                            parsed.Title = draft.Title;
                        if (String.IsNullOrWhiteSpace(parsed.Description))
                            parsed.Description = draft.Description;
                        draft = parsed;
                        draft.Method = "runtime-json";
                    }
                }
                catch (Exception ex)
                {
                    _Logging.Warn(_Header + "planning summary fallback for session " + session.Id + ": " + ex.Message);
                }

                _WebSocketHub?.BroadcastEvent(
                    "planning-session.summary.created",
                    "Dispatch draft summarized from planning session " + session.Id,
                    new
                    {
                        sessionId = session.Id,
                        messageId = sourceMessage.Id,
                        draft
                    });

                await _EmitEventAsync(
                    "planning-session.summary.created",
                    "Planning session " + session.Id + " generated a dispatch draft",
                    "planning-session",
                    session.Id,
                    session.CaptainId,
                    null,
                    session.VesselId,
                    null).ConfigureAwait(false);

                return draft;
            }
            finally
            {
                _ActiveTurns.TryRemove(session.Id, out _);
            }
        }

        /// <summary>
        /// Delete a planning session and its transcript.
        /// Active sessions are stopped first.
        /// </summary>
        public async Task DeleteAsync(PlanningSession session, CancellationToken token = default)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));

            session = await RequireSessionAsync(session.Id, token).ConfigureAwait(false);
            if (session.Status == PlanningSessionStatusEnum.Active ||
                session.Status == PlanningSessionStatusEnum.Responding ||
                session.Status == PlanningSessionStatusEnum.Stopping)
            {
                session = await StopAsync(session, token).ConfigureAwait(false);
            }

            await _Database.PlanningSessions.DeleteAsync(session.Id, token).ConfigureAwait(false);
            _ActiveTurns.TryRemove(session.Id, out _);

            _WebSocketHub?.BroadcastEvent(
                "planning-session.deleted",
                "Planning session deleted",
                new { sessionId = session.Id });

            await _EmitEventAsync(
                "planning-session.deleted",
                "Planning session " + session.Id + " deleted",
                "planning-session",
                session.Id,
                session.CaptainId,
                null,
                session.VesselId,
                null).ConfigureAwait(false);
        }

        /// <summary>
        /// Apply planning-session inactivity and retention maintenance.
        /// </summary>
        public async Task MaintainSessionsAsync(CancellationToken token = default)
        {
            if (_Settings.PlanningSessionInactivityTimeoutMinutes <= 0
                && _Settings.PlanningSessionAbandonmentTimeoutMinutes <= 0
                && _Settings.PlanningSessionRetentionDays <= 0)
                return;

            List<PlanningSession> sessions = await _Database.PlanningSessions.EnumerateAsync(token).ConfigureAwait(false);
            DateTime nowUtc = DateTime.UtcNow;
            HashSet<string> sessionsToStop = new HashSet<string>(StringComparer.Ordinal);

            if (_Settings.PlanningSessionInactivityTimeoutMinutes > 0)
            {
                DateTime inactivityCutoff = nowUtc.AddMinutes(-_Settings.PlanningSessionInactivityTimeoutMinutes);
                foreach (PlanningSession session in sessions.Where(s => ShouldStopForInactivity(s, inactivityCutoff)))
                {
                    sessionsToStop.Add(session.Id);
                }
            }

            if (_Settings.PlanningSessionAbandonmentTimeoutMinutes > 0)
            {
                DateTime abandonmentCutoff = nowUtc.AddMinutes(-_Settings.PlanningSessionAbandonmentTimeoutMinutes);
                foreach (PlanningSession session in sessions.Where(s => ShouldStopForAbandonment(s, abandonmentCutoff)))
                {
                    sessionsToStop.Add(session.Id);
                }
            }

            foreach (PlanningSession session in sessions.Where(s => sessionsToStop.Contains(s.Id)))
            {
                try
                {
                    await StopAsync(session, token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _Logging.Warn(_Header + "planning inactivity stop failed for " + session.Id + ": " + ex.Message);
                }
            }

            if (_Settings.PlanningSessionRetentionDays > 0)
            {
                DateTime retentionCutoff = nowUtc.AddDays(-_Settings.PlanningSessionRetentionDays);
                foreach (PlanningSession session in sessions.Where(s =>
                    (s.Status == PlanningSessionStatusEnum.Stopped || s.Status == PlanningSessionStatusEnum.Failed) &&
                    (s.CompletedUtc ?? s.LastUpdateUtc) < retentionCutoff))
                {
                    try
                    {
                        await DeleteAsync(session, token).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _Logging.Warn(_Header + "planning retention delete failed for " + session.Id + ": " + ex.Message);
                    }
                }
            }
        }

        /// <summary>
        /// Recover persisted planning sessions on server start.
        /// </summary>
        public async Task RecoverSessionsAsync(CancellationToken token = default)
        {
            List<PlanningSession> active = await _Database.PlanningSessions
                .EnumerateAsync(token)
                .ConfigureAwait(false);
            DateTime nowUtc = DateTime.UtcNow;

            foreach (PlanningSession session in active.Where(s =>
                s.Status == PlanningSessionStatusEnum.Active ||
                s.Status == PlanningSessionStatusEnum.Responding ||
                s.Status == PlanningSessionStatusEnum.Stopping))
            {
                try
                {
                    Captain? captain = await _Database.Captains.ReadAsync(session.CaptainId, token).ConfigureAwait(false);
                    if (captain == null)
                    {
                        session.Status = PlanningSessionStatusEnum.Failed;
                        session.FailureReason = "Captain not found during recovery.";
                        session.ProcessId = null;
                        session.CompletedUtc = DateTime.UtcNow;
                        await _Database.PlanningSessions.UpdateAsync(session, token).ConfigureAwait(false);
                        continue;
                    }

                    if (session.Status == PlanningSessionStatusEnum.Stopping)
                    {
                        await StopAsync(session, token).ConfigureAwait(false);
                        continue;
                    }

                    if (ShouldStopOnRecovery(session, nowUtc))
                    {
                        await StopAsync(session, token).ConfigureAwait(false);
                        continue;
                    }

                    if (session.Status == PlanningSessionStatusEnum.Responding && session.ProcessId.HasValue)
                    {
                        bool running = false;
                        try
                        {
                            Process process = Process.GetProcessById(session.ProcessId.Value);
                            running = !process.HasExited;
                        }
                        catch
                        {
                            running = false;
                        }

                        if (running)
                        {
                            try
                            {
                                Armada.Runtimes.Interfaces.IAgentRuntime runtime = CreatePlanningRuntime(captain);
                                await runtime.StopAsync(session.ProcessId.Value, token).ConfigureAwait(false);
                            }
                            catch
                            {
                            }
                        }

                        List<PlanningSessionMessage> messages = await _Database.PlanningSessionMessages
                            .EnumerateBySessionAsync(session.Id, token)
                            .ConfigureAwait(false);
                        int nextSequence = messages.Count == 0 ? 1 : messages.Max(m => m.Sequence) + 1;
                        PlanningSessionMessage interruption = new PlanningSessionMessage
                        {
                            PlanningSessionId = session.Id,
                            TenantId = session.TenantId,
                            UserId = session.UserId,
                            Role = "System",
                            Sequence = nextSequence,
                            Content = "The previous planning response was interrupted during server recovery. You can continue the session from here.",
                            CreatedUtc = DateTime.UtcNow,
                            LastUpdateUtc = DateTime.UtcNow
                        };
                        await _Database.PlanningSessionMessages.CreateAsync(interruption, token).ConfigureAwait(false);
                    }

                    PlanningSession recoveredSession = await RequireSessionAsync(session.Id, token).ConfigureAwait(false);
                    if (ShouldStopOnRecovery(recoveredSession, nowUtc))
                    {
                        await StopAsync(recoveredSession, token).ConfigureAwait(false);
                        continue;
                    }

                    captain.State = CaptainStateEnum.Planning;
                    captain.CurrentMissionId = null;
                    captain.CurrentDockId = recoveredSession.DockId;
                    captain.ProcessId = null;
                    captain.LastHeartbeatUtc = DateTime.UtcNow;
                    captain.LastUpdateUtc = DateTime.UtcNow;
                    await _Database.Captains.UpdateAsync(captain, token).ConfigureAwait(false);
                    _WebSocketHub?.BroadcastCaptainChange(captain.Id, captain.State.ToString(), captain.Name);

                    if (recoveredSession.Status == PlanningSessionStatusEnum.Responding)
                    {
                        recoveredSession.Status = PlanningSessionStatusEnum.Active;
                        recoveredSession.ProcessId = null;
                        recoveredSession.LastUpdateUtc = DateTime.UtcNow;
                        await _Database.PlanningSessions.UpdateAsync(recoveredSession, token).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    _Logging.Warn(_Header + "planning session recovery error for " + session.Id + ": " + ex.Message);
                }
            }
        }

        #endregion

        #region Private-Methods

        private async Task ExecuteTurnAsync(string sessionId, string assistantMessageId)
        {
            try
            {
                PlanningSession session = await RequireSessionAsync(sessionId, CancellationToken.None).ConfigureAwait(false);
                PlanningSessionMessage assistantMessage = await RequireMessageAsync(assistantMessageId, CancellationToken.None).ConfigureAwait(false);
                Captain captain = await RequireCaptainAsync(session.CaptainId, CancellationToken.None).ConfigureAwait(false);
                Vessel vessel = await RequireVesselAsync(session.VesselId, CancellationToken.None).ConfigureAwait(false);
                Dock dock = await RequireDockAsync(session.DockId, CancellationToken.None).ConfigureAwait(false);
                List<PlanningSessionMessage> messages = await _Database.PlanningSessionMessages.EnumerateBySessionAsync(session.Id).ConfigureAwait(false);

                if (IsStopRequested(session.Id))
                    return;

                string promptFilePath = await WritePromptFileAsync(session, captain, vessel, dock, messages, CancellationToken.None).ConfigureAwait(false);
                string prompt = "Read `" + promptFilePath + "` and continue this Armada planning conversation. Stay in planning mode and answer the latest user message directly.";

                string turnDir = Path.Combine(_Settings.LogDirectory, "planning-sessions", session.Id);
                Directory.CreateDirectory(turnDir);
                string logFilePath = Path.Combine(turnDir, "session.log");
                string finalMessageFilePath = Path.Combine(turnDir, "final-" + assistantMessage.Id + ".txt");

                Armada.Runtimes.Interfaces.IAgentRuntime runtime = CreatePlanningRuntime(captain);
                TaskCompletionSource<int?> exitSource = new TaskCompletionSource<int?>(TaskCreationOptions.RunContinuationsAsynchronously);
                object outputLock = new object();
                StringBuilder output = new StringBuilder();
                DateTime turnStartUtc = DateTime.UtcNow;
                DateTime? firstOutputUtc = null;

                bool isMux = captain.Runtime == AgentRuntimeEnum.Mux;

                // Claude Code streams token-by-token only in streaming-JSON mode. Enable it for the interactive
                // planning turn so the reply renders incrementally (matching Ask Armada); the delta events are
                // parsed in ExtractPlanningStreamText. Other planning flows (summaries) keep --print text.
                bool isClaudeStream = captain.Runtime == AgentRuntimeEnum.ClaudeCode;
                if (isClaudeStream && runtime is ClaudeCodeRuntime claudePlanningRuntime)
                {
                    claudePlanningRuntime.StreamJsonOutput = true;
                }
                _ActiveTurns.TryGetValue(session.Id, out TurnState? currentTurn);
                bool showThinking = currentTurn?.ShowThinking ?? false;
                bool streamReply = currentTurn?.Stream ?? true;

                // For non-Mux runtimes (no native thinking channel), ask the model to emit a delimited
                // reasoning block that we lift out of the final reply, mirroring Ask Armada.
                if (showThinking && !isMux)
                {
                    prompt = prompt + " Before your final answer, briefly work through your reasoning inside a single block delimited by <thinking> and </thinking>, then write the answer after the closing </thinking> tag.";
                }

                runtime.OnStdoutReceived += (processId, line) =>
                {
                    // For Mux, parse the structured protocol events: stream assistant_text into the reply and
                    // surface tool-call activity to the transcript. Other runtimes stream plain text lines.
                    // Subscribing to stdout-only keeps CLI stderr banners out of the captured transcript.
                    string? delta = ExtractPlanningStreamText(isMux, isClaudeStream, session.Id, assistantMessage.Id, line);
                    if (String.IsNullOrEmpty(delta)) return;

                    string updatedContent;
                    lock (outputLock)
                    {
                        if (firstOutputUtc == null) firstOutputUtc = DateTime.UtcNow;
                        if (isMux || isClaudeStream)
                        {
                            // Mux assistant_text and Claude text_delta events are partial tokens: append raw
                            // (no line breaks) so the streamed reply reads naturally.
                            output.Append(delta);
                            BoundedTextBuffer.Trim(output, _MaxPlanningOutputChars);
                        }
                        else
                        {
                            BoundedTextBuffer.AppendLine(output, delta, _MaxPlanningOutputChars);
                        }
                        updatedContent = output.ToString();
                    }

                    assistantMessage.Content = updatedContent;
                    assistantMessage.LastUpdateUtc = DateTime.UtcNow;
                    if (streamReply) BroadcastMessageUpdated(assistantMessage);
                };
                runtime.OnProcessExited += (processId, exitCode) => exitSource.TrySetResult(exitCode);

                if (IsStopRequested(session.Id))
                    return;

                int processId = await runtime.StartAsync(
                    dock.WorktreePath ?? vessel.WorkingDirectory ?? vessel.LocalPath ?? Directory.GetCurrentDirectory(),
                    prompt,
                    logFilePath: logFilePath,
                    finalMessageFilePath: finalMessageFilePath,
                    model: captain.Model,
                    captain: captain,
                    showThinking: showThinking).ConfigureAwait(false);

                session.ProcessId = processId;
                session.LastUpdateUtc = DateTime.UtcNow;
                await _Database.PlanningSessions.UpdateAsync(session).ConfigureAwait(false);
                BroadcastSessionChanged(session);

                captain.ProcessId = processId;
                captain.LastHeartbeatUtc = DateTime.UtcNow;
                captain.LastUpdateUtc = DateTime.UtcNow;
                await _Database.Captains.UpdateAsync(captain).ConfigureAwait(false);

                int? exitCode = await exitSource.Task.ConfigureAwait(false);

                string finalContent = assistantMessage.Content ?? String.Empty;
                try
                {
                    if (File.Exists(finalMessageFilePath))
                    {
                        string artifact = await File.ReadAllTextAsync(finalMessageFilePath).ConfigureAwait(false);
                        if (!String.IsNullOrWhiteSpace(artifact))
                            finalContent = artifact.Trim();
                    }
                }
                catch
                {
                }

                if (String.IsNullOrWhiteSpace(finalContent) && exitCode.HasValue && exitCode.Value != 0)
                {
                    finalContent = "Planning turn exited with code " + exitCode.Value + " before producing a final response.";
                }

                // Lift the prompt-instructed reasoning out of a non-Mux reply into the thinking channel.
                if (showThinking && !isMux)
                {
                    string reply = finalContent;
                    string extractedThinking = ExtractThinkingBlock(ref reply);
                    if (!String.IsNullOrEmpty(extractedThinking))
                    {
                        finalContent = reply;
                        EmitPlanningThinking(new { sessionId = session.Id, messageId = assistantMessage.Id, delta = extractedThinking });
                    }
                }

                assistantMessage.Content = finalContent.Trim();
                assistantMessage.LastUpdateUtc = DateTime.UtcNow;
                assistantMessage.Metrics = BuildTurnMetrics(turnStartUtc, firstOutputUtc, DateTime.UtcNow, assistantMessage.Content);

                // Best-effort token accounting for the planning turn (output estimated from the reply).
                await Armada.Core.Services.TokenUsageCapture.CaptureAsync(
                    _Database, _Logging, "planning",
                    model: captain.Model,
                    runtime: captain.Runtime.ToString(),
                    tenantId: session.TenantId,
                    userId: null,
                    vesselId: String.IsNullOrEmpty(session.VesselId) ? null : session.VesselId,
                    captainId: captain.Id,
                    sourceId: session.Id,
                    inputTokens: null,
                    outputTokens: null,
                    cachedTokens: null,
                    inputText: null,
                    outputText: assistantMessage.Content,
                    token: CancellationToken.None).ConfigureAwait(false);

                await _Database.PlanningSessionMessages.UpdateAsync(assistantMessage).ConfigureAwait(false);
                BroadcastMessageUpdated(assistantMessage);

                captain = await RequireCaptainAsync(session.CaptainId, CancellationToken.None).ConfigureAwait(false);
                captain.ProcessId = null;
                captain.LastHeartbeatUtc = DateTime.UtcNow;
                captain.LastUpdateUtc = DateTime.UtcNow;
                await _Database.Captains.UpdateAsync(captain).ConfigureAwait(false);

                session = await RequireSessionAsync(session.Id, CancellationToken.None).ConfigureAwait(false);
                bool stopRequested = _ActiveTurns.TryGetValue(session.Id, out TurnState? turnState) && turnState.StopRequested;
                session.ProcessId = null;
                if (!stopRequested)
                {
                    session.Status = PlanningSessionStatusEnum.Active;
                    session.FailureReason = null;
                }
                session.LastUpdateUtc = DateTime.UtcNow;
                await _Database.PlanningSessions.UpdateAsync(session).ConfigureAwait(false);
                BroadcastSessionChanged(session);
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "planning turn error for session " + sessionId + ": " + ex.Message);
                try
                {
                    PlanningSession? session = await _Database.PlanningSessions.ReadAsync(sessionId).ConfigureAwait(false);
                    PlanningSessionMessage? assistantMessage = await _Database.PlanningSessionMessages.ReadAsync(assistantMessageId).ConfigureAwait(false);
                    if (assistantMessage != null)
                    {
                        assistantMessage.Content = "Planning response failed: " + ex.Message;
                        assistantMessage.LastUpdateUtc = DateTime.UtcNow;
                        await _Database.PlanningSessionMessages.UpdateAsync(assistantMessage).ConfigureAwait(false);
                        BroadcastMessageUpdated(assistantMessage);
                    }

                    if (session != null)
                    {
                        session.ProcessId = null;
                        if (!_ActiveTurns.TryGetValue(session.Id, out TurnState? turnState) || !turnState.StopRequested)
                        {
                            session.Status = PlanningSessionStatusEnum.Active;
                            session.FailureReason = ex.Message;
                        }
                        session.LastUpdateUtc = DateTime.UtcNow;
                        await _Database.PlanningSessions.UpdateAsync(session).ConfigureAwait(false);
                        BroadcastSessionChanged(session);
                    }

                    if (session != null)
                    {
                        Captain? captain = await _Database.Captains.ReadAsync(session.CaptainId).ConfigureAwait(false);
                        if (captain != null)
                        {
                            captain.ProcessId = null;
                            if (captain.State != CaptainStateEnum.Planning)
                                captain.State = CaptainStateEnum.Planning;
                            captain.LastHeartbeatUtc = DateTime.UtcNow;
                            captain.LastUpdateUtc = DateTime.UtcNow;
                            await _Database.Captains.UpdateAsync(captain).ConfigureAwait(false);
                            _WebSocketHub?.BroadcastCaptainChange(captain.Id, captain.State.ToString(), captain.Name);
                        }
                    }
                }
                catch (Exception nestedEx)
                {
                    _Logging.Warn(_Header + "planning turn recovery error for session " + sessionId + ": " + nestedEx.Message);
                }
            }
            finally
            {
                _ActiveTurns.TryRemove(sessionId, out _);
            }
        }

        private async Task<bool> WaitForTurnAvailabilityAsync(string sessionId, TimeSpan timeout, CancellationToken token)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            while (stopwatch.Elapsed < timeout)
            {
                if (!_ActiveTurns.ContainsKey(sessionId))
                    return true;

                await Task.Delay(100, token).ConfigureAwait(false);
            }

            return !_ActiveTurns.ContainsKey(sessionId);
        }

        private bool ShouldStopOnRecovery(PlanningSession session, DateTime nowUtc)
        {
            if (_Settings.PlanningSessionInactivityTimeoutMinutes > 0)
            {
                DateTime inactivityCutoff = nowUtc.AddMinutes(-_Settings.PlanningSessionInactivityTimeoutMinutes);
                if (ShouldStopForInactivity(session, inactivityCutoff))
                    return true;
            }

            if (_Settings.PlanningSessionAbandonmentTimeoutMinutes > 0)
            {
                DateTime abandonmentCutoff = nowUtc.AddMinutes(-_Settings.PlanningSessionAbandonmentTimeoutMinutes);
                if (ShouldStopForAbandonment(session, abandonmentCutoff))
                    return true;
            }

            return false;
        }

        private static bool ShouldStopForInactivity(PlanningSession session, DateTime inactivityCutoffUtc)
        {
            return IsIdleSessionWithoutRunningProcess(session)
                && session.LastUpdateUtc < inactivityCutoffUtc;
        }

        private static bool ShouldStopForAbandonment(PlanningSession session, DateTime abandonmentCutoffUtc)
        {
            return IsIdleSessionWithoutRunningProcess(session)
                && session.LastUpdateUtc < abandonmentCutoffUtc;
        }

        private static bool IsIdleSessionWithoutRunningProcess(PlanningSession session)
        {
            return (session.Status == PlanningSessionStatusEnum.Active || session.Status == PlanningSessionStatusEnum.Responding)
                && !session.ProcessId.HasValue;
        }

        private async Task<string> WritePromptFileAsync(
            PlanningSession session,
            Captain captain,
            Vessel vessel,
            Dock dock,
            List<PlanningSessionMessage> messages,
            CancellationToken token)
        {
            string turnDir = Path.Combine(_Settings.LogDirectory, "planning-sessions", session.Id);
            Directory.CreateDirectory(turnDir);

            string filePath = Path.Combine(turnDir, "context.md");
            string playbookMarkdown = await RenderPlaybooksMarkdownAsync(session, token).ConfigureAwait(false);
            string transcript = RenderTranscript(messages, 24000);

            StringBuilder builder = new StringBuilder();
            builder.AppendLine("# Armada Planning Session");
            builder.AppendLine();
            builder.AppendLine("You are helping a human plan work before dispatching it through Armada.");
            builder.AppendLine("Stay in planning mode. You may inspect the repository and reason concretely, but do not claim that work has been dispatched, landed, or merged unless the transcript explicitly says that already happened.");
            builder.AppendLine();
            builder.AppendLine("## Session Context");
            builder.AppendLine("- Session ID: `" + session.Id + "`");
            builder.AppendLine("- Captain: `" + captain.Name + "` (`" + captain.Runtime + "`)");
            builder.AppendLine("- Vessel: `" + vessel.Name + "`");
            builder.AppendLine("- Branch: `" + (dock.BranchName ?? session.BranchName ?? vessel.DefaultBranch) + "`");
            if (!String.IsNullOrWhiteSpace(session.PipelineId))
                builder.AppendLine("- Pipeline ID: `" + session.PipelineId + "`");
            if (!String.IsNullOrWhiteSpace(captain.SystemInstructions))
            {
                builder.AppendLine();
                builder.AppendLine("## Captain Instructions");
                builder.AppendLine(captain.SystemInstructions.Trim());
            }
            if (!String.IsNullOrWhiteSpace(vessel.ProjectContext))
            {
                builder.AppendLine();
                builder.AppendLine("## Project Context");
                builder.AppendLine(vessel.ProjectContext.Trim());
            }
            if (!String.IsNullOrWhiteSpace(vessel.StyleGuide))
            {
                builder.AppendLine();
                builder.AppendLine("## Style Guide");
                builder.AppendLine(vessel.StyleGuide.Trim());
            }
            if (vessel.EnableModelContext && !String.IsNullOrWhiteSpace(vessel.ModelContext))
            {
                builder.AppendLine();
                builder.AppendLine("## Model Context");
                builder.AppendLine(vessel.ModelContext.Trim());
            }
            if (!String.IsNullOrWhiteSpace(playbookMarkdown))
            {
                builder.AppendLine();
                builder.AppendLine("## Selected Playbooks");
                builder.AppendLine(playbookMarkdown);
            }
            builder.AppendLine();
            builder.AppendLine("## Conversation Transcript");
            builder.AppendLine(transcript);
            builder.AppendLine();
            builder.AppendLine("## Response Guidance");
            builder.AppendLine("- Continue the conversation naturally.");
            builder.AppendLine("- Be concrete about architecture, implementation order, and risk when relevant.");
            builder.AppendLine("- If useful, include a short `Dispatch Draft` section that could be sent to Armada as a mission.");

            await File.WriteAllTextAsync(filePath, builder.ToString(), token).ConfigureAwait(false);
            return filePath;
        }

        private async Task<string> RenderPlaybooksMarkdownAsync(PlanningSession session, CancellationToken token)
        {
            if (String.IsNullOrWhiteSpace(session.TenantId) || session.SelectedPlaybooks.Count == 0)
                return String.Empty;

            try
            {
                List<Playbook> playbooks = await _Playbooks.ResolveSelectionsAsync(session.TenantId!, session.SelectedPlaybooks, token).ConfigureAwait(false);
                List<string> sections = new List<string>();
                for (int i = 0; i < playbooks.Count; i++)
                {
                    Playbook playbook = playbooks[i];
                    SelectedPlaybook selection = session.SelectedPlaybooks[i];
                    sections.Add(
                        "### " + playbook.FileName + "\n" +
                        "- Delivery Mode: `" + selection.DeliveryMode + "`\n" +
                        (!String.IsNullOrWhiteSpace(playbook.Description) ? playbook.Description!.Trim() + "\n\n" : "\n") +
                        playbook.Content.Trim());
                }

                return String.Join("\n\n", sections);
            }
            catch (Exception ex)
            {
                return "_Playbook context could not be loaded: " + ex.Message + "_";
            }
        }

        private static string RenderTranscript(List<PlanningSessionMessage> messages, int maxChars)
        {
            List<string> parts = messages
                .OrderBy(m => m.Sequence)
                .Select(m => "### " + m.Role + "\n" + (m.Content ?? String.Empty).Trim())
                .ToList();
            string transcript = String.Join("\n\n", parts).Trim();
            if (transcript.Length <= maxChars)
                return transcript;

            return "... transcript truncated ...\n\n" + transcript.Substring(transcript.Length - maxChars, maxChars);
        }

        private async Task<string> RunRuntimePromptAsync(
            PlanningSession session,
            Captain captain,
            Vessel vessel,
            string prompt,
            string logFilePath,
            string finalMessageFilePath,
            CancellationToken token)
        {
            Armada.Runtimes.Interfaces.IAgentRuntime runtime = CreatePlanningRuntime(captain);
            TaskCompletionSource<int?> exitSource = new TaskCompletionSource<int?>(TaskCreationOptions.RunContinuationsAsynchronously);
            StringBuilder output = new StringBuilder();
            object outputLock = new object();
            int? processId = null;

            runtime.OnStdoutReceived += (processId, line) =>
            {
                // Drop structured protocol events so only assistant/summary text is captured. Subscribing
                // to stdout-only keeps CLI stderr banners (e.g. Codex's stdin/version preamble) out of the
                // captured reply.
                if (captain.Runtime == AgentRuntimeEnum.Mux && MuxRuntime.IsProtocolEventLine(line)) return;
                if (captain.Runtime == AgentRuntimeEnum.OpenCode && OpenCodeRuntime.IsProtocolEventLine(line))
                {
                    // Surface any human-visible text from the OpenCode event; drop the raw JSON envelope.
                    string? openCodeText = OpenCodeRuntime.TryExtractAssistantText(line);
                    if (!String.IsNullOrEmpty(openCodeText))
                    {
                        lock (outputLock)
                        {
                            BoundedTextBuffer.AppendLine(output, openCodeText!, _MaxPlanningOutputChars);
                        }
                    }
                    return;
                }

                lock (outputLock)
                {
                    BoundedTextBuffer.AppendLine(output, line, _MaxPlanningOutputChars);
                }
            };
            runtime.OnProcessExited += (processId, exitCode) => exitSource.TrySetResult(exitCode);

            try
            {
                processId = await runtime.StartAsync(
                    session.DockId != null
                        ? (await RequireDockAsync(session.DockId, token).ConfigureAwait(false)).WorktreePath ?? vessel.WorkingDirectory ?? vessel.LocalPath ?? Directory.GetCurrentDirectory()
                        : vessel.WorkingDirectory ?? vessel.LocalPath ?? Directory.GetCurrentDirectory(),
                    prompt,
                    logFilePath: logFilePath,
                    finalMessageFilePath: finalMessageFilePath,
                    model: captain.Model,
                    captain: captain,
                    token: token).ConfigureAwait(false);

                session.ProcessId = processId;
                session.LastUpdateUtc = DateTime.UtcNow;
                await _Database.PlanningSessions.UpdateAsync(session, token).ConfigureAwait(false);
                BroadcastSessionChanged(session);

                captain.ProcessId = processId;
                captain.LastHeartbeatUtc = DateTime.UtcNow;
                captain.LastUpdateUtc = DateTime.UtcNow;
                await _Database.Captains.UpdateAsync(captain, token).ConfigureAwait(false);
                _WebSocketHub?.BroadcastCaptainChange(captain.Id, captain.State.ToString(), captain.Name);

                int? exitCode = await exitSource.Task.ConfigureAwait(false);

                string finalOutput;
                lock (outputLock)
                {
                    finalOutput = output.ToString().Trim();
                }

                try
                {
                    if (File.Exists(finalMessageFilePath))
                    {
                        string artifact = await File.ReadAllTextAsync(finalMessageFilePath, token).ConfigureAwait(false);
                        if (!String.IsNullOrWhiteSpace(artifact))
                            finalOutput = artifact.Trim();
                    }
                }
                catch
                {
                }

                if (String.IsNullOrWhiteSpace(finalOutput) && exitCode.HasValue && exitCode.Value != 0)
                    finalOutput = "Planning helper prompt exited with code " + exitCode.Value + ".";

                return finalOutput;
            }
            finally
            {
                if (processId.HasValue)
                {
                    try
                    {
                        PlanningSession? refreshedSession = await _Database.PlanningSessions.ReadAsync(session.Id, CancellationToken.None).ConfigureAwait(false);
                        if (refreshedSession != null && refreshedSession.ProcessId.HasValue)
                        {
                            refreshedSession.ProcessId = null;
                            refreshedSession.LastUpdateUtc = DateTime.UtcNow;
                            await _Database.PlanningSessions.UpdateAsync(refreshedSession, CancellationToken.None).ConfigureAwait(false);
                            BroadcastSessionChanged(refreshedSession);
                        }
                    }
                    catch (Exception ex)
                    {
                        _Logging.Warn(_Header + "planning summary session cleanup failed for " + session.Id + ": " + ex.Message);
                    }

                    try
                    {
                        Captain? refreshedCaptain = await _Database.Captains.ReadAsync(captain.Id, CancellationToken.None).ConfigureAwait(false);
                        if (refreshedCaptain != null && refreshedCaptain.ProcessId.HasValue)
                        {
                            refreshedCaptain.ProcessId = null;
                            refreshedCaptain.LastHeartbeatUtc = DateTime.UtcNow;
                            refreshedCaptain.LastUpdateUtc = DateTime.UtcNow;
                            await _Database.Captains.UpdateAsync(refreshedCaptain, CancellationToken.None).ConfigureAwait(false);
                            _WebSocketHub?.BroadcastCaptainChange(refreshedCaptain.Id, refreshedCaptain.State.ToString(), refreshedCaptain.Name);
                        }
                    }
                    catch (Exception ex)
                    {
                        _Logging.Warn(_Header + "planning summary captain cleanup failed for " + captain.Id + ": " + ex.Message);
                    }
                }
            }
        }

        private async Task<PlanningSessionMessage> ResolveSourceMessageAsync(PlanningSession session, string? messageId, CancellationToken token)
        {
            PlanningSessionMessage? sourceMessage = null;
            if (!String.IsNullOrWhiteSpace(messageId))
            {
                sourceMessage = await _Database.PlanningSessionMessages.ReadAsync(messageId, token).ConfigureAwait(false);
                if (sourceMessage == null || sourceMessage.PlanningSessionId != session.Id)
                    throw new InvalidOperationException("Planning message not found: " + messageId);
            }
            else
            {
                List<PlanningSessionMessage> messages = await _Database.PlanningSessionMessages
                    .EnumerateBySessionAsync(session.Id, token)
                    .ConfigureAwait(false);
                sourceMessage = messages
                    .Where(m => String.Equals(m.Role, "Assistant", StringComparison.OrdinalIgnoreCase))
                    .Where(m => !String.IsNullOrWhiteSpace(m.Content))
                    .OrderByDescending(m => m.Sequence)
                    .FirstOrDefault();
            }

            if (sourceMessage == null)
                throw new InvalidOperationException("No assistant planning output is available.");

            return sourceMessage;
        }

        private string BuildDefaultDispatchTitle(PlanningSession session, PlanningSessionMessage sourceMessage)
        {
            if (!String.IsNullOrWhiteSpace(session.Title))
                return session.Title.Trim();

            string content = sourceMessage.Content.Trim();
            if (String.IsNullOrWhiteSpace(content))
                return "Planning Dispatch";

            string firstLine = content
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault()?
                .Trim() ?? "Planning Dispatch";
            if (firstLine.Length <= 80)
                return firstLine;

            return firstLine.Substring(0, 80).TrimEnd();
        }

        private PlanningSessionSummaryResponse BuildFallbackSummary(PlanningSession session, PlanningSessionMessage sourceMessage, string? preferredTitle)
        {
            string title = !String.IsNullOrWhiteSpace(preferredTitle)
                ? preferredTitle.Trim()
                : BuildDefaultDispatchTitle(session, sourceMessage);

            return new PlanningSessionSummaryResponse
            {
                SessionId = session.Id,
                MessageId = sourceMessage.Id,
                Title = title,
                Description = sourceMessage.Content.Trim(),
                Method = "assistant-fallback"
            };
        }

        private bool TryParseSummaryResponse(string content, out PlanningSessionSummaryResponse? response)
        {
            response = null;
            if (String.IsNullOrWhiteSpace(content))
                return false;

            string candidate = content.Trim();
            int firstBrace = candidate.IndexOf('{');
            int lastBrace = candidate.LastIndexOf('}');
            if (firstBrace >= 0 && lastBrace > firstBrace)
                candidate = candidate.Substring(firstBrace, lastBrace - firstBrace + 1);

            try
            {
                using JsonDocument doc = JsonDocument.Parse(candidate);
                JsonElement root = doc.RootElement;
                response = new PlanningSessionSummaryResponse
                {
                    Title = root.TryGetProperty("title", out JsonElement title) ? title.GetString() ?? String.Empty : String.Empty,
                    Description = root.TryGetProperty("description", out JsonElement description) ? description.GetString() ?? String.Empty : String.Empty,
                };
                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool IsStopRequested(string sessionId)
        {
            return _ActiveTurns.TryGetValue(sessionId, out TurnState? turnState) && turnState.StopRequested;
        }

        private async Task<PlanningSession> PrepareStopAsync(PlanningSession session, CancellationToken token)
        {
            session = await RequireSessionAsync(session.Id, token).ConfigureAwait(false);
            if (session.Status == PlanningSessionStatusEnum.Stopped || session.Status == PlanningSessionStatusEnum.Failed)
                return session;

            if (_ActiveTurns.TryGetValue(session.Id, out TurnState? turnState))
                turnState.StopRequested = true;

            if (session.Status != PlanningSessionStatusEnum.Stopping)
            {
                session.Status = PlanningSessionStatusEnum.Stopping;
                session.LastUpdateUtc = DateTime.UtcNow;
                session = await _Database.PlanningSessions.UpdateAsync(session, token).ConfigureAwait(false);
                BroadcastSessionChanged(session);
            }

            return session;
        }

        private Task<PlanningSession> EnsureStopOperation(string sessionId)
        {
            return _StopOperations.GetOrAdd(
                sessionId,
                id => Task.Run(() => CompleteStopAsync(id), CancellationToken.None));
        }

        private async Task<PlanningSession> CompleteStopAsync(string sessionId)
        {
            try
            {
                PlanningSession session = await RequireSessionAsync(sessionId, CancellationToken.None).ConfigureAwait(false);
                if (session.Status == PlanningSessionStatusEnum.Stopped || session.Status == PlanningSessionStatusEnum.Failed)
                    return session;

                Captain? captain = await _Database.Captains.ReadAsync(session.CaptainId).ConfigureAwait(false);

                if (session.ProcessId.HasValue && captain != null)
                {
                    try
                    {
                        Armada.Runtimes.Interfaces.IAgentRuntime runtime = CreatePlanningRuntime(captain);
                        await runtime.StopAsync(session.ProcessId.Value, CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _Logging.Warn(_Header + "error stopping planning process " + session.ProcessId.Value + " for session " + session.Id + ": " + ex.Message);
                    }
                }

                if (!String.IsNullOrEmpty(session.DockId))
                {
                    try
                    {
                        await _Docks.ReclaimAsync(session.DockId, session.TenantId, CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _Logging.Warn(_Header + "error reclaiming planning dock " + session.DockId + " for session " + session.Id + ": " + ex.Message);
                    }
                }

                if (captain != null)
                {
                    captain.State = CaptainStateEnum.Idle;
                    captain.CurrentMissionId = null;
                    captain.CurrentDockId = null;
                    captain.ProcessId = null;
                    captain.LastHeartbeatUtc = DateTime.UtcNow;
                    captain.LastUpdateUtc = DateTime.UtcNow;
                    await _Database.Captains.UpdateAsync(captain).ConfigureAwait(false);
                    _WebSocketHub?.BroadcastCaptainChange(captain.Id, captain.State.ToString(), captain.Name);
                }

                session = await RequireSessionAsync(session.Id, CancellationToken.None).ConfigureAwait(false);
                session.Status = PlanningSessionStatusEnum.Stopped;
                session.ProcessId = null;
                session.CompletedUtc = DateTime.UtcNow;
                session.LastUpdateUtc = DateTime.UtcNow;
                session = await _Database.PlanningSessions.UpdateAsync(session).ConfigureAwait(false);
                BroadcastSessionChanged(session);

                await _EmitEventAsync(
                    "planning-session.stopped",
                    "Planning session " + session.Id + " stopped",
                    "planning-session",
                    session.Id,
                    session.CaptainId,
                    null,
                    session.VesselId,
                    null).ConfigureAwait(false);

                return session;
            }
            finally
            {
                _StopOperations.TryRemove(sessionId, out _);
            }
        }

        /// <summary>
        /// Parse one line of Claude Code streaming-JSON output and return the incremental assistant text for a
        /// content_block_delta/text_delta event, or null for any other event (init, message framing, result,
        /// tool use). The concatenation of the returned deltas is the full reply.
        /// </summary>
        private string? ExtractClaudeStreamDelta(string line)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(line.Trim());
                JsonElement root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return null;

                if (!root.TryGetProperty("type", out JsonElement ty) || ty.ValueKind != JsonValueKind.String || ty.GetString() != "stream_event")
                    return null;

                if (root.TryGetProperty("event", out JsonElement ev) && ev.ValueKind == JsonValueKind.Object
                    && ev.TryGetProperty("type", out JsonElement evt) && evt.ValueKind == JsonValueKind.String
                    && evt.GetString() == "content_block_delta"
                    && ev.TryGetProperty("delta", out JsonElement delta) && delta.ValueKind == JsonValueKind.Object
                    && delta.TryGetProperty("type", out JsonElement dty) && dty.ValueKind == JsonValueKind.String
                    && dty.GetString() == "text_delta"
                    && delta.TryGetProperty("text", out JsonElement dtx) && dtx.ValueKind == JsonValueKind.String)
                {
                    return dtx.GetString();
                }
            }
            catch (JsonException)
            {
            }

            return null;
        }

        private string? ExtractPlanningStreamText(bool isMux, bool isClaudeStream, string sessionId, string messageId, string line)
        {
            if (isClaudeStream)
                return ExtractClaudeStreamDelta(line);

            if (!isMux || !MuxRuntime.IsProtocolEventLine(line))
                return line;

            try
            {
                using JsonDocument doc = JsonDocument.Parse(line.Trim());
                JsonElement root = doc.RootElement;
                string eventType = root.TryGetProperty("eventType", out JsonElement et) && et.ValueKind == JsonValueKind.String
                    ? et.GetString() ?? String.Empty : String.Empty;

                if (eventType == "assistant_text")
                {
                    if (root.TryGetProperty("text", out JsonElement tx) && tx.ValueKind == JsonValueKind.String)
                        return tx.GetString();
                }
                else if (eventType == "assistant_thinking")
                {
                    // Mux reasoning channel (--show-thinking): stream it as its own event, mirroring Ask Armada.
                    if (root.TryGetProperty("text", out JsonElement think) && think.ValueKind == JsonValueKind.String)
                    {
                        string? thinkingDelta = think.GetString();
                        if (!String.IsNullOrEmpty(thinkingDelta))
                            EmitPlanningThinking(new { sessionId, messageId, delta = thinkingDelta });
                    }
                }
                else if (eventType == "tool_call_proposed" && root.TryGetProperty("toolCall", out JsonElement proposed))
                {
                    string? toolId = proposed.TryGetProperty("id", out JsonElement propId) && propId.ValueKind == JsonValueKind.String ? propId.GetString() : null;
                    string? toolName = proposed.TryGetProperty("name", out JsonElement pnm) && pnm.ValueKind == JsonValueKind.String ? pnm.GetString() : null;
                    string? argsJson = proposed.TryGetProperty("arguments", out JsonElement parg) ? TruncateJson(parg.GetRawText(), 4000) : null;
                    EmitPlanningTool(new { sessionId, messageId, phase = "started", id = toolId, name = toolName, arguments = argsJson });
                }
                else if (eventType == "tool_call_completed")
                {
                    string? toolId = root.TryGetProperty("toolCallId", out JsonElement cid) && cid.ValueKind == JsonValueKind.String ? cid.GetString() : null;
                    string? toolName = root.TryGetProperty("toolName", out JsonElement cnm) && cnm.ValueKind == JsonValueKind.String ? cnm.GetString() : null;
                    double? elapsedMs = root.TryGetProperty("elapsedMs", out JsonElement cel) && cel.ValueKind == JsonValueKind.Number ? cel.GetDouble() : (double?)null;
                    bool? ok = null;
                    string? resultJson = null;
                    if (root.TryGetProperty("result", out JsonElement res))
                    {
                        if (res.TryGetProperty("success", out JsonElement suc) && (suc.ValueKind == JsonValueKind.True || suc.ValueKind == JsonValueKind.False))
                            ok = suc.GetBoolean();
                        JsonElement resultBody = res.TryGetProperty("content", out JsonElement content) ? content : res;
                        resultJson = TruncateJson(resultBody.GetRawText(), 16000);
                    }
                    EmitPlanningTool(new { sessionId, messageId, phase = "completed", id = toolId, name = toolName, ok, elapsedMs, result = resultJson });
                }
            }
            catch (JsonException)
            {
            }

            return null;
        }

        private void EmitPlanningTool(object payload)
        {
            try { _WebSocketHub?.BroadcastEvent("planning-session.tool", String.Empty, payload); }
            catch { }
        }

        private void EmitPlanningThinking(object payload)
        {
            try { _WebSocketHub?.BroadcastEvent("planning-session.thinking", String.Empty, payload); }
            catch { }
        }

        /// <summary>
        /// Lift the prompt-instructed reasoning out of a non-Mux reply: return the concatenated text of
        /// every &lt;thinking&gt;...&lt;/thinking&gt; block and rewrite <paramref name="reply"/> to the answer with
        /// those blocks removed. If removing them would leave no answer, the reply is left intact and empty
        /// is returned.
        /// </summary>
        private static string ExtractThinkingBlock(ref string reply)
        {
            if (String.IsNullOrEmpty(reply)) return String.Empty;

            System.Text.RegularExpressions.MatchCollection matches = System.Text.RegularExpressions.Regex.Matches(
                reply, "<thinking>(.*?)</thinking>",
                System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (matches.Count == 0) return String.Empty;

            StringBuilder collected = new StringBuilder();
            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                string piece = match.Groups[1].Value.Trim();
                if (piece.Length == 0) continue;
                if (collected.Length > 0) collected.AppendLine();
                collected.Append(piece);
            }

            string stripped = System.Text.RegularExpressions.Regex.Replace(
                reply, "<thinking>.*?</thinking>", String.Empty,
                System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
            if (String.IsNullOrWhiteSpace(stripped)) return String.Empty;

            reply = stripped;
            return collected.ToString().Trim();
        }

        private static string? TruncateJson(string? value, int max)
        {
            if (String.IsNullOrEmpty(value) || value!.Length <= max) return value;
            return value.Substring(0, max) + "... (truncated)";
        }

        /// <summary>
        /// Build per-turn statistics for a planning reply from wall-clock timing and a character-based
        /// token estimate. Planning drives a runtime CLI rather than a direct model client, so exact
        /// provider token usage is not available here; time to first token and total time are measured
        /// directly, and tokens (and tokens/sec) are estimated from the reply length (~3.5 chars/token).
        /// </summary>
        /// <param name="startUtc">When the turn was launched.</param>
        /// <param name="firstOutputUtc">When the first non-protocol output line arrived, if any.</param>
        /// <param name="endUtc">When the turn finished.</param>
        /// <param name="content">The final reply text.</param>
        /// <returns>The per-turn metrics.</returns>
        private static CaptainChatMetrics BuildTurnMetrics(DateTime startUtc, DateTime? firstOutputUtc, DateTime endUtc, string content)
        {
            double totalMs = (endUtc - startUtc).TotalMilliseconds;
            double? ttftMs = firstOutputUtc.HasValue
                ? Math.Min((firstOutputUtc.Value - startUtc).TotalMilliseconds, totalMs)
                : (double?)null;

            // Use the shared builder so planning-session and Ask Armada metrics are computed identically.
            return Armada.Core.Services.ChatTurnMetricsBuilder.Build(totalMs, ttftMs, content, null);
        }

        private Armada.Runtimes.Interfaces.IAgentRuntime CreatePlanningRuntime(Captain captain)
        {
            if (!captain.SupportsPlanningSessions)
                throw new InvalidOperationException(captain.PlanningSessionSupportReason ?? "This captain runtime is not supported for planning sessions.");

            Armada.Runtimes.Interfaces.IAgentRuntime runtime = _RuntimeFactory.Create(captain.Runtime);
            if (!runtime.SupportsPlanningSessions)
                throw new InvalidOperationException("Runtime " + runtime.Name + " does not currently support planning sessions.");

            return runtime;
        }

        private void BroadcastSessionChanged(PlanningSession session)
        {
            _WebSocketHub?.BroadcastEvent(
                "planning-session.changed",
                "Planning session updated",
                new { session });
        }

        private void BroadcastMessageCreated(PlanningSessionMessage message)
        {
            _WebSocketHub?.BroadcastEvent(
                "planning-session.message.created",
                "Planning session message created",
                new
                {
                    sessionId = message.PlanningSessionId,
                    message
                });
        }

        private void BroadcastMessageUpdated(PlanningSessionMessage message)
        {
            _WebSocketHub?.BroadcastEvent(
                "planning-session.message.updated",
                "Planning session message updated",
                new
                {
                    sessionId = message.PlanningSessionId,
                    message
                });
        }

        private async Task<PlanningSession> RequireSessionAsync(string sessionId, CancellationToken token)
        {
            PlanningSession? session = await _Database.PlanningSessions.ReadAsync(sessionId, token).ConfigureAwait(false);
            if (session == null) throw new InvalidOperationException("Planning session not found: " + sessionId);
            return session;
        }

        private async Task<PlanningSessionMessage> RequireMessageAsync(string messageId, CancellationToken token)
        {
            PlanningSessionMessage? message = await _Database.PlanningSessionMessages.ReadAsync(messageId, token).ConfigureAwait(false);
            if (message == null) throw new InvalidOperationException("Planning session message not found: " + messageId);
            return message;
        }

        private async Task<Captain> RequireCaptainAsync(string captainId, CancellationToken token)
        {
            Captain? captain = await _Database.Captains.ReadAsync(captainId, token).ConfigureAwait(false);
            if (captain == null) throw new InvalidOperationException("Captain not found: " + captainId);
            return captain;
        }

        private async Task<Vessel> RequireVesselAsync(string vesselId, CancellationToken token)
        {
            Vessel? vessel = await _Database.Vessels.ReadAsync(vesselId, token).ConfigureAwait(false);
            if (vessel == null) throw new InvalidOperationException("Vessel not found: " + vesselId);
            return vessel;
        }

        private async Task<Dock> RequireDockAsync(string? dockId, CancellationToken token)
        {
            if (String.IsNullOrWhiteSpace(dockId))
                throw new InvalidOperationException("Planning session does not have a dock.");
            Dock? dock = await _Database.Docks.ReadAsync(dockId, token).ConfigureAwait(false);
            if (dock == null) throw new InvalidOperationException("Dock not found: " + dockId);
            return dock;
        }

        #endregion
    }
}
