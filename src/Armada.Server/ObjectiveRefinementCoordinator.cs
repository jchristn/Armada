namespace Armada.Server
{
    using System.Diagnostics;
    using System.Text;
    using System.Text.Json;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Settings;
    using Armada.Runtimes;
    using Armada.Runtimes.Interfaces;
    using Armada.Server.WebSocket;
    using SyslogLogging;

    /// <summary>
    /// Coordinates captain-backed backlog refinement sessions without provisioning a dock.
    /// </summary>
    public class ObjectiveRefinementCoordinator
    {
        private sealed class TurnState
        {
            public bool StopRequested { get; set; } = false;
        }

        private readonly string _Header = "[ObjectiveRefinementCoordinator] ";
        private readonly LoggingModule _Logging;
        private readonly DatabaseDriver _Database;
        private readonly ArmadaSettings _Settings;
        private readonly AgentRuntimeFactory _RuntimeFactory;
        private readonly Func<string, string, string?, string?, string?, string?, string?, string?, Task> _EmitEventAsync;
        private readonly ArmadaWebSocketHub? _WebSocketHub;
        private const int _MaxRefinementOutputChars = 131072;
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, TurnState> _ActiveTurns =
            new System.Collections.Concurrent.ConcurrentDictionary<string, TurnState>(StringComparer.Ordinal);
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Task<ObjectiveRefinementSession>> _StopOperations =
            new System.Collections.Concurrent.ConcurrentDictionary<string, Task<ObjectiveRefinementSession>>(StringComparer.Ordinal);

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ObjectiveRefinementCoordinator(
            LoggingModule logging,
            DatabaseDriver database,
            ArmadaSettings settings,
            AgentRuntimeFactory runtimeFactory,
            Func<string, string, string?, string?, string?, string?, string?, string?, Task> emitEventAsync,
            ArmadaWebSocketHub? webSocketHub = null)
        {
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _RuntimeFactory = runtimeFactory ?? throw new ArgumentNullException(nameof(runtimeFactory));
            _EmitEventAsync = emitEventAsync ?? throw new ArgumentNullException(nameof(emitEventAsync));
            _WebSocketHub = webSocketHub;
        }

        /// <summary>
        /// Create a refinement session for an objective and reserve the selected captain.
        /// </summary>
        public async Task<ObjectiveRefinementSession> CreateAsync(
            string? tenantId,
            string? userId,
            Objective objective,
            Captain captain,
            Vessel? vessel,
            ObjectiveRefinementSessionCreateRequest request,
            CancellationToken token = default)
        {
            if (objective == null) throw new ArgumentNullException(nameof(objective));
            if (captain == null) throw new ArgumentNullException(nameof(captain));
            if (request == null) throw new ArgumentNullException(nameof(request));

            if (!captain.SupportsPlanningSessions)
                throw new InvalidOperationException(captain.PlanningSessionSupportReason ?? "This captain runtime is not supported for refinement sessions.");
            if (captain.State != CaptainStateEnum.Idle)
                throw new InvalidOperationException("Captain " + captain.Name + " is not idle.");

            List<ObjectiveRefinementSession> captainSessions = await _Database.ObjectiveRefinementSessions
                .EnumerateByCaptainAsync(captain.Id, token)
                .ConfigureAwait(false);
            if (captainSessions.Any(s =>
                s.Status == ObjectiveRefinementSessionStatusEnum.Active
                || s.Status == ObjectiveRefinementSessionStatusEnum.Responding
                || s.Status == ObjectiveRefinementSessionStatusEnum.Stopping))
            {
                throw new InvalidOperationException("Captain " + captain.Name + " already has an active refinement session.");
            }

            ObjectiveRefinementSession session = new ObjectiveRefinementSession
            {
                ObjectiveId = objective.Id,
                TenantId = tenantId,
                UserId = userId,
                CaptainId = captain.Id,
                FleetId = !String.IsNullOrWhiteSpace(request.FleetId) ? request.FleetId : vessel?.FleetId,
                VesselId = !String.IsNullOrWhiteSpace(request.VesselId) ? request.VesselId : vessel?.Id,
                Title = !String.IsNullOrWhiteSpace(request.Title) ? request.Title.Trim() : "Refine: " + objective.Title,
                Status = ObjectiveRefinementSessionStatusEnum.Created,
                CreatedUtc = DateTime.UtcNow,
                LastUpdateUtc = DateTime.UtcNow
            };

            session = await _Database.ObjectiveRefinementSessions.CreateAsync(session, token).ConfigureAwait(false);

            try
            {
                session.Status = ObjectiveRefinementSessionStatusEnum.Active;
                session.StartedUtc = DateTime.UtcNow;
                session.LastUpdateUtc = DateTime.UtcNow;
                session = await _Database.ObjectiveRefinementSessions.UpdateAsync(session, token).ConfigureAwait(false);

                captain.State = CaptainStateEnum.Refining;
                captain.CurrentMissionId = null;
                captain.CurrentDockId = null;
                captain.ProcessId = null;
                captain.LastHeartbeatUtc = DateTime.UtcNow;
                captain.LastUpdateUtc = DateTime.UtcNow;
                await _Database.Captains.UpdateAsync(captain, token).ConfigureAwait(false);
                _WebSocketHub?.BroadcastCaptainChange(captain.Id, captain.State.ToString(), captain.Name);

                BroadcastSessionChanged(session);
                await _EmitEventAsync(
                    "objective-refinement-session.created",
                    "Objective refinement session " + session.Id + " created for captain " + captain.Name,
                    "objective-refinement-session",
                    session.Id,
                    captain.Id,
                    null,
                    session.VesselId,
                    null).ConfigureAwait(false);

                if (!String.IsNullOrWhiteSpace(request.InitialMessage))
                {
                    await SendMessageAsync(session, request.InitialMessage, token).ConfigureAwait(false);
                }

                return session;
            }
            catch (Exception ex)
            {
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

                session.Status = ObjectiveRefinementSessionStatusEnum.Failed;
                session.FailureReason = ex.Message;
                session.CompletedUtc = DateTime.UtcNow;
                session.LastUpdateUtc = DateTime.UtcNow;
                await _Database.ObjectiveRefinementSessions.UpdateAsync(session, token).ConfigureAwait(false);
                BroadcastSessionChanged(session);
                throw;
            }
        }

        /// <summary>
        /// Append a user message and start a background refinement turn.
        /// </summary>
        public async Task<ObjectiveRefinementMessage> SendMessageAsync(
            ObjectiveRefinementSession session,
            string content,
            CancellationToken token = default)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (String.IsNullOrWhiteSpace(content)) throw new ArgumentNullException(nameof(content));

            session = await RequireSessionAsync(session.Id, token).ConfigureAwait(false);
            if (session.Status != ObjectiveRefinementSessionStatusEnum.Active)
                throw new InvalidOperationException("Objective refinement session " + session.Id + " is not ready for a new message.");
            if (!_ActiveTurns.TryAdd(session.Id, new TurnState()))
                throw new InvalidOperationException("Objective refinement session " + session.Id + " is already generating a response.");

            List<ObjectiveRefinementMessage> existingMessages = await _Database.ObjectiveRefinementMessages
                .EnumerateBySessionAsync(session.Id, token)
                .ConfigureAwait(false);
            int nextSequence = existingMessages.Count == 0 ? 1 : existingMessages.Max(m => m.Sequence) + 1;

            ObjectiveRefinementMessage userMessage = new ObjectiveRefinementMessage
            {
                ObjectiveRefinementSessionId = session.Id,
                ObjectiveId = session.ObjectiveId,
                TenantId = session.TenantId,
                UserId = session.UserId,
                Role = "User",
                Sequence = nextSequence,
                Content = content.Trim(),
                CreatedUtc = DateTime.UtcNow,
                LastUpdateUtc = DateTime.UtcNow
            };
            userMessage = await _Database.ObjectiveRefinementMessages.CreateAsync(userMessage, token).ConfigureAwait(false);
            BroadcastMessageCreated(userMessage);

            ObjectiveRefinementMessage assistantMessage = new ObjectiveRefinementMessage
            {
                ObjectiveRefinementSessionId = session.Id,
                ObjectiveId = session.ObjectiveId,
                TenantId = session.TenantId,
                UserId = session.UserId,
                Role = "Assistant",
                Sequence = nextSequence + 1,
                Content = String.Empty,
                CreatedUtc = DateTime.UtcNow,
                LastUpdateUtc = DateTime.UtcNow
            };
            assistantMessage = await _Database.ObjectiveRefinementMessages.CreateAsync(assistantMessage, token).ConfigureAwait(false);
            BroadcastMessageCreated(assistantMessage);

            session.Status = ObjectiveRefinementSessionStatusEnum.Responding;
            session.ProcessId = null;
            session.FailureReason = null;
            session.LastUpdateUtc = DateTime.UtcNow;
            session = await _Database.ObjectiveRefinementSessions.UpdateAsync(session, token).ConfigureAwait(false);
            BroadcastSessionChanged(session);

            _ = Task.Run(() => ExecuteTurnAsync(session.Id, assistantMessage.Id), CancellationToken.None);
            return userMessage;
        }

        /// <summary>
        /// Mark a session as stopping and stop it asynchronously.
        /// </summary>
        public async Task<ObjectiveRefinementSession> RequestStopAsync(ObjectiveRefinementSession session, CancellationToken token = default)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));

            session = await PrepareStopAsync(session, token).ConfigureAwait(false);
            if (session.Status == ObjectiveRefinementSessionStatusEnum.Stopped || session.Status == ObjectiveRefinementSessionStatusEnum.Failed)
                return session;

            _ = EnsureStopOperation(session.Id);
            return session;
        }

        /// <summary>
        /// Stop a refinement session and release the captain.
        /// </summary>
        public async Task<ObjectiveRefinementSession> StopAsync(ObjectiveRefinementSession session, CancellationToken token = default)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));

            session = await PrepareStopAsync(session, token).ConfigureAwait(false);
            if (session.Status == ObjectiveRefinementSessionStatusEnum.Stopped || session.Status == ObjectiveRefinementSessionStatusEnum.Failed)
                return session;

            Task<ObjectiveRefinementSession> stopTask = EnsureStopOperation(session.Id);
            return await stopTask.ConfigureAwait(false);
        }

        /// <summary>
        /// Generate a structured objective summary from a refinement session.
        /// </summary>
        public async Task<ObjectiveRefinementSummaryResponse> SummarizeAsync(
            ObjectiveRefinementSession session,
            ObjectiveRefinementSummaryRequest request,
            CancellationToken token = default)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (request == null) throw new ArgumentNullException(nameof(request));

            session = await RequireSessionAsync(session.Id, token).ConfigureAwait(false);
            if (!_ActiveTurns.TryAdd(session.Id, new TurnState()))
                throw new InvalidOperationException("Objective refinement session " + session.Id + " is already generating a response.");

            try
            {
                Objective objective = await RequireObjectiveAsync(session.ObjectiveId, token).ConfigureAwait(false);
                ObjectiveRefinementMessage sourceMessage = await ResolveSourceMessageAsync(session, request.MessageId, token).ConfigureAwait(false);
                Captain captain = await RequireCaptainAsync(session.CaptainId, token).ConfigureAwait(false);
                Vessel? vessel = await TryReadVesselAsync(session.VesselId, token).ConfigureAwait(false);
                List<ObjectiveRefinementMessage> messages = await _Database.ObjectiveRefinementMessages
                    .EnumerateBySessionAsync(session.Id, token)
                    .ConfigureAwait(false);

                string promptFilePath = await WritePromptFileAsync(session, objective, captain, vessel, messages, token).ConfigureAwait(false);
                string prompt =
                    "Read `" + promptFilePath + "` and the selected refinement message below.\n" +
                    "Produce a structured backlog-refinement summary for Armada.\n" +
                    "Return JSON only with keys `summary`, `acceptanceCriteria`, `nonGoals`, `rolloutConstraints`, and `suggestedPipelineId`.\n" +
                    "- `summary` must be concise and implementation-oriented.\n" +
                    "- Each list must be an array of strings.\n" +
                    "- Keep only concrete items that materially clarify future implementation.\n" +
                    "- Use `suggestedPipelineId` only when the transcript strongly supports a specific pipeline id already known to the user.\n" +
                    "- Do not invent IDs or requirements not grounded in the transcript.\n\n" +
                    "Selected refinement message:\n" +
                    sourceMessage.Content.Trim();

                string turnDir = Path.Combine(_Settings.LogDirectory, "objective-refinement-sessions", session.Id);
                Directory.CreateDirectory(turnDir);
                string finalMessageFilePath = Path.Combine(turnDir, "summary-" + Guid.NewGuid().ToString("N") + ".txt");
                string logFilePath = Path.Combine(turnDir, "summary.log");

                ObjectiveRefinementSummaryResponse draft = BuildFallbackSummary(session, sourceMessage);
                try
                {
                    string runtimeOutput = await RunRuntimePromptAsync(session, captain, vessel, prompt, logFilePath, finalMessageFilePath, token).ConfigureAwait(false);
                    if (TryParseSummaryResponse(runtimeOutput, out ObjectiveRefinementSummaryResponse? parsed) && parsed != null)
                    {
                        parsed.SessionId = session.Id;
                        parsed.MessageId = sourceMessage.Id;
                        if (String.IsNullOrWhiteSpace(parsed.Summary))
                            parsed.Summary = draft.Summary;
                        parsed.AcceptanceCriteria = NormalizeLines(parsed.AcceptanceCriteria);
                        parsed.NonGoals = NormalizeLines(parsed.NonGoals);
                        parsed.RolloutConstraints = NormalizeLines(parsed.RolloutConstraints);
                        parsed.SuggestedPipelineId = Normalize(parsed.SuggestedPipelineId);
                        draft = parsed;
                        draft.Method = "runtime-json";
                    }
                }
                catch (Exception ex)
                {
                    _Logging.Warn(_Header + "objective refinement summary fallback for session " + session.Id + ": " + ex.ToString());
                }

                _WebSocketHub?.BroadcastEvent(
                    "objective-refinement-session.summary.created",
                    "Objective refinement summary created",
                    new
                    {
                        sessionId = session.Id,
                        messageId = sourceMessage.Id,
                        summary = draft
                    });

                await _EmitEventAsync(
                    "objective-refinement-session.summary.created",
                    "Objective refinement session " + session.Id + " generated a refinement summary",
                    "objective-refinement-session",
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
        /// Apply a refinement summary back to the objective.
        /// </summary>
        public async Task<(ObjectiveRefinementSummaryResponse Summary, Objective Objective)> ApplyAsync(
            AuthContext auth,
            Objective objective,
            ObjectiveRefinementSession session,
            ObjectiveRefinementApplyRequest request,
            ObjectiveService objectiveService,
            CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (objective == null) throw new ArgumentNullException(nameof(objective));
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (objectiveService == null) throw new ArgumentNullException(nameof(objectiveService));

            ObjectiveRefinementSummaryResponse summary = await SummarizeAsync(
                session,
                new ObjectiveRefinementSummaryRequest { MessageId = request.MessageId },
                token).ConfigureAwait(false);

            if (request.MarkMessageSelected && !String.IsNullOrWhiteSpace(summary.MessageId))
            {
                await SelectMessageAsync(session, summary.MessageId!, token).ConfigureAwait(false);
            }

            ObjectiveUpsertRequest update = new ObjectiveUpsertRequest
            {
                Status = ResolveAppliedStatus(objective.Status),
                BacklogState = request.PromoteBacklogState ? ResolveAppliedBacklogState(objective, session) : null,
                RefinementSummary = summary.Summary,
                AcceptanceCriteria = summary.AcceptanceCriteria.Count > 0 ? summary.AcceptanceCriteria : null,
                NonGoals = summary.NonGoals.Count > 0 ? summary.NonGoals : null,
                RolloutConstraints = summary.RolloutConstraints.Count > 0 ? summary.RolloutConstraints : null,
                SuggestedPipelineId = summary.SuggestedPipelineId,
                RefinementSessionIds = MergeDistinct(objective.RefinementSessionIds, session.Id)
            };

            Objective updated = await objectiveService.UpdateAsync(auth, objective.Id, update, token).ConfigureAwait(false);

            _WebSocketHub?.BroadcastEvent(
                "objective-refinement-session.applied",
                "Objective refinement summary applied",
                new
                {
                    sessionId = session.Id,
                    objectiveId = updated.Id,
                    summary
                });

            await _EmitEventAsync(
                "objective-refinement-session.applied",
                "Objective refinement session " + session.Id + " applied updates to objective " + updated.Id,
                "objective",
                updated.Id,
                session.CaptainId,
                null,
                session.VesselId,
                null).ConfigureAwait(false);

            return (summary, updated);
        }

        /// <summary>
        /// Delete a refinement session and its transcript.
        /// </summary>
        public async Task DeleteAsync(ObjectiveRefinementSession session, CancellationToken token = default)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));

            session = await RequireSessionAsync(session.Id, token).ConfigureAwait(false);
            if (session.Status == ObjectiveRefinementSessionStatusEnum.Active
                || session.Status == ObjectiveRefinementSessionStatusEnum.Responding
                || session.Status == ObjectiveRefinementSessionStatusEnum.Stopping)
            {
                session = await StopAsync(session, token).ConfigureAwait(false);
            }

            await _Database.ObjectiveRefinementMessages.DeleteBySessionAsync(session.Id, token).ConfigureAwait(false);
            await _Database.ObjectiveRefinementSessions.DeleteAsync(session.Id, token).ConfigureAwait(false);
            _ActiveTurns.TryRemove(session.Id, out _);

            _WebSocketHub?.BroadcastEvent(
                "objective-refinement-session.deleted",
                "Objective refinement session deleted",
                new { sessionId = session.Id, objectiveId = session.ObjectiveId });

            await _EmitEventAsync(
                "objective-refinement-session.deleted",
                "Objective refinement session " + session.Id + " deleted",
                "objective-refinement-session",
                session.Id,
                session.CaptainId,
                null,
                session.VesselId,
                null).ConfigureAwait(false);
        }

        /// <summary>
        /// Recover persisted refinement sessions on server start.
        /// </summary>
        public async Task RecoverSessionsAsync(CancellationToken token = default)
        {
            List<ObjectiveRefinementSession> sessions = await _Database.ObjectiveRefinementSessions.EnumerateAsync(token).ConfigureAwait(false);

            foreach (ObjectiveRefinementSession session in sessions.Where(s =>
                s.Status == ObjectiveRefinementSessionStatusEnum.Active
                || s.Status == ObjectiveRefinementSessionStatusEnum.Responding
                || s.Status == ObjectiveRefinementSessionStatusEnum.Stopping))
            {
                try
                {
                    Captain? captain = await _Database.Captains.ReadAsync(session.CaptainId, token).ConfigureAwait(false);
                    if (captain == null)
                    {
                        session.Status = ObjectiveRefinementSessionStatusEnum.Failed;
                        session.FailureReason = "Captain not found during recovery.";
                        session.ProcessId = null;
                        session.CompletedUtc = DateTime.UtcNow;
                        session.LastUpdateUtc = DateTime.UtcNow;
                        await _Database.ObjectiveRefinementSessions.UpdateAsync(session, token).ConfigureAwait(false);
                        BroadcastSessionChanged(session);
                        continue;
                    }

                    if (session.Status == ObjectiveRefinementSessionStatusEnum.Stopping)
                    {
                        await StopAsync(session, token).ConfigureAwait(false);
                        continue;
                    }

                    if (session.Status == ObjectiveRefinementSessionStatusEnum.Responding && session.ProcessId.HasValue)
                    {
                        try
                        {
                            IAgentRuntime runtime = CreateRefinementRuntime(captain);
                            await runtime.StopAsync(session.ProcessId.Value, token).ConfigureAwait(false);
                        }
                        catch
                        {
                        }

                        List<ObjectiveRefinementMessage> messages = await _Database.ObjectiveRefinementMessages
                            .EnumerateBySessionAsync(session.Id, token)
                            .ConfigureAwait(false);
                        int nextSequence = messages.Count == 0 ? 1 : messages.Max(m => m.Sequence) + 1;
                        ObjectiveRefinementMessage interruption = new ObjectiveRefinementMessage
                        {
                            ObjectiveRefinementSessionId = session.Id,
                            ObjectiveId = session.ObjectiveId,
                            TenantId = session.TenantId,
                            UserId = session.UserId,
                            Role = "System",
                            Sequence = nextSequence,
                            Content = "The previous refinement response was interrupted during server recovery. You can continue the session from here.",
                            CreatedUtc = DateTime.UtcNow,
                            LastUpdateUtc = DateTime.UtcNow
                        };
                        interruption = await _Database.ObjectiveRefinementMessages.CreateAsync(interruption, token).ConfigureAwait(false);
                        BroadcastMessageCreated(interruption);
                    }

                    captain.State = CaptainStateEnum.Refining;
                    captain.CurrentMissionId = null;
                    captain.CurrentDockId = null;
                    captain.ProcessId = null;
                    captain.LastHeartbeatUtc = DateTime.UtcNow;
                    captain.LastUpdateUtc = DateTime.UtcNow;
                    await _Database.Captains.UpdateAsync(captain, token).ConfigureAwait(false);
                    _WebSocketHub?.BroadcastCaptainChange(captain.Id, captain.State.ToString(), captain.Name);

                    if (session.Status == ObjectiveRefinementSessionStatusEnum.Responding)
                    {
                        session.Status = ObjectiveRefinementSessionStatusEnum.Active;
                        session.ProcessId = null;
                        session.LastUpdateUtc = DateTime.UtcNow;
                        await _Database.ObjectiveRefinementSessions.UpdateAsync(session, token).ConfigureAwait(false);
                        BroadcastSessionChanged(session);
                    }
                }
                catch (Exception ex)
                {
                    _Logging.Warn(_Header + "objective refinement recovery error for " + session.Id + ": " + ex.ToString());
                }
            }
        }

        /// <summary>
        /// Apply lightweight maintenance for refinement sessions.
        /// </summary>
        public Task MaintainSessionsAsync(CancellationToken token = default)
        {
            return Task.CompletedTask;
        }

        private async Task ExecuteTurnAsync(string sessionId, string assistantMessageId)
        {
            try
            {
                ObjectiveRefinementSession session = await RequireSessionAsync(sessionId, CancellationToken.None).ConfigureAwait(false);
                ObjectiveRefinementMessage assistantMessage = await RequireMessageAsync(assistantMessageId, CancellationToken.None).ConfigureAwait(false);
                Objective objective = await RequireObjectiveAsync(session.ObjectiveId, CancellationToken.None).ConfigureAwait(false);
                Captain captain = await RequireCaptainAsync(session.CaptainId, CancellationToken.None).ConfigureAwait(false);
                Vessel? vessel = await TryReadVesselAsync(session.VesselId, CancellationToken.None).ConfigureAwait(false);
                List<ObjectiveRefinementMessage> messages = await _Database.ObjectiveRefinementMessages
                    .EnumerateBySessionAsync(session.Id)
                    .ConfigureAwait(false);

                if (IsStopRequested(session.Id))
                    return;

                string promptFilePath = await WritePromptFileAsync(session, objective, captain, vessel, messages, CancellationToken.None).ConfigureAwait(false);
                string prompt =
                    "Read `" + promptFilePath + "` and continue this Armada backlog refinement conversation. " +
                    "Clarify future implementation intent, acceptance criteria, non-goals, and constraints. " +
                    "Do not claim that work is already dispatched, coded, landed, or merged unless the transcript explicitly says so.";

                string turnDir = Path.Combine(_Settings.LogDirectory, "objective-refinement-sessions", session.Id);
                Directory.CreateDirectory(turnDir);
                string logFilePath = Path.Combine(turnDir, "session.log");
                string finalMessageFilePath = Path.Combine(turnDir, "final-" + assistantMessage.Id + ".txt");

                IAgentRuntime runtime = CreateRefinementRuntime(captain);
                TaskCompletionSource<int?> exitSource = new TaskCompletionSource<int?>(TaskCreationOptions.RunContinuationsAsynchronously);
                object outputLock = new object();
                StringBuilder output = new StringBuilder();

                runtime.OnStdoutReceived += (processId, line) =>
                {
                    // stdout-only so CLI stderr banners (e.g. Codex's stdin/version preamble) stay out of the
                    // refinement transcript.
                    string updatedContent;
                    lock (outputLock)
                    {
                        BoundedTextBuffer.AppendLine(output, line, _MaxRefinementOutputChars);
                        updatedContent = output.ToString();
                    }

                    assistantMessage.Content = updatedContent;
                    assistantMessage.LastUpdateUtc = DateTime.UtcNow;
                    BroadcastMessageUpdated(assistantMessage);
                };
                runtime.OnProcessExited += (processId, exitCode) => exitSource.TrySetResult(exitCode);

                if (IsStopRequested(session.Id))
                    return;

                int processId = await runtime.StartAsync(
                    ResolveWorkingDirectory(vessel),
                    prompt,
                    logFilePath: logFilePath,
                    finalMessageFilePath: finalMessageFilePath,
                    model: captain.Model,
                    captain: captain).ConfigureAwait(false);

                session.ProcessId = processId;
                session.LastUpdateUtc = DateTime.UtcNow;
                await _Database.ObjectiveRefinementSessions.UpdateAsync(session).ConfigureAwait(false);
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
                    finalContent = "Refinement turn exited with code " + exitCode.Value + " before producing a final response.";
                }

                assistantMessage.Content = finalContent.Trim();
                assistantMessage.LastUpdateUtc = DateTime.UtcNow;
                await _Database.ObjectiveRefinementMessages.UpdateAsync(assistantMessage).ConfigureAwait(false);
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
                    session.Status = ObjectiveRefinementSessionStatusEnum.Active;
                    session.FailureReason = null;
                }
                session.LastUpdateUtc = DateTime.UtcNow;
                await _Database.ObjectiveRefinementSessions.UpdateAsync(session).ConfigureAwait(false);
                BroadcastSessionChanged(session);
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "objective refinement turn error for session " + sessionId + ": " + ex.ToString());
                try
                {
                    ObjectiveRefinementSession? session = await _Database.ObjectiveRefinementSessions.ReadAsync(sessionId).ConfigureAwait(false);
                    ObjectiveRefinementMessage? assistantMessage = await _Database.ObjectiveRefinementMessages.ReadAsync(assistantMessageId).ConfigureAwait(false);
                    if (assistantMessage != null)
                    {
                        assistantMessage.Content = "Refinement response failed: " + ex.Message;
                        assistantMessage.LastUpdateUtc = DateTime.UtcNow;
                        await _Database.ObjectiveRefinementMessages.UpdateAsync(assistantMessage).ConfigureAwait(false);
                        BroadcastMessageUpdated(assistantMessage);
                    }

                    if (session != null)
                    {
                        session.ProcessId = null;
                        if (!_ActiveTurns.TryGetValue(session.Id, out TurnState? turnState) || !turnState.StopRequested)
                        {
                            session.Status = ObjectiveRefinementSessionStatusEnum.Active;
                            session.FailureReason = ex.Message;
                        }
                        session.LastUpdateUtc = DateTime.UtcNow;
                        await _Database.ObjectiveRefinementSessions.UpdateAsync(session).ConfigureAwait(false);
                        BroadcastSessionChanged(session);
                    }

                    if (session != null)
                    {
                        Captain? captain = await _Database.Captains.ReadAsync(session.CaptainId).ConfigureAwait(false);
                        if (captain != null)
                        {
                            captain.ProcessId = null;
                            if (captain.State != CaptainStateEnum.Refining)
                                captain.State = CaptainStateEnum.Refining;
                            captain.LastHeartbeatUtc = DateTime.UtcNow;
                            captain.LastUpdateUtc = DateTime.UtcNow;
                            await _Database.Captains.UpdateAsync(captain).ConfigureAwait(false);
                            _WebSocketHub?.BroadcastCaptainChange(captain.Id, captain.State.ToString(), captain.Name);
                        }
                    }
                }
                catch (Exception nestedEx)
                {
                    _Logging.Warn(_Header + "objective refinement recovery error for session " + sessionId + ": " + nestedEx.Message);
                }
            }
            finally
            {
                _ActiveTurns.TryRemove(sessionId, out _);
            }
        }

        private async Task<string> WritePromptFileAsync(
            ObjectiveRefinementSession session,
            Objective objective,
            Captain captain,
            Vessel? vessel,
            List<ObjectiveRefinementMessage> messages,
            CancellationToken token)
        {
            string turnDir = Path.Combine(_Settings.LogDirectory, "objective-refinement-sessions", session.Id);
            Directory.CreateDirectory(turnDir);

            string filePath = Path.Combine(turnDir, "context.md");
            string transcript = RenderTranscript(messages, 24000);

            StringBuilder builder = new StringBuilder();
            builder.AppendLine("# Armada Objective Refinement Session");
            builder.AppendLine();
            builder.AppendLine("You are helping a human refine future work before repository-aware planning or dispatch begins.");
            builder.AppendLine("Focus on scope, acceptance criteria, constraints, non-goals, sequencing, and implementation intent.");
            builder.AppendLine();
            builder.AppendLine("## Session Context");
            builder.AppendLine("- Session ID: `" + session.Id + "`");
            builder.AppendLine("- Objective ID: `" + objective.Id + "`");
            builder.AppendLine("- Captain: `" + captain.Name + "` (`" + captain.Runtime + "`)`");
            if (vessel != null)
                builder.AppendLine("- Vessel: `" + vessel.Name + "`");
            if (!String.IsNullOrWhiteSpace(session.FleetId))
                builder.AppendLine("- Fleet ID: `" + session.FleetId + "`");
            builder.AppendLine();
            builder.AppendLine("## Objective");
            builder.AppendLine("- Title: " + objective.Title);
            builder.AppendLine("- Status: `" + objective.Status + "`");
            builder.AppendLine("- Backlog State: `" + objective.BacklogState + "`");
            builder.AppendLine("- Kind: `" + objective.Kind + "`");
            builder.AppendLine("- Priority: `" + objective.Priority + "`");
            builder.AppendLine("- Effort: `" + objective.Effort + "`");
            if (!String.IsNullOrWhiteSpace(objective.Owner))
                builder.AppendLine("- Owner: `" + objective.Owner + "`");
            if (!String.IsNullOrWhiteSpace(objective.TargetVersion))
                builder.AppendLine("- Target Version: `" + objective.TargetVersion + "`");
            if (!String.IsNullOrWhiteSpace(objective.Description))
            {
                builder.AppendLine();
                builder.AppendLine("### Description");
                builder.AppendLine(objective.Description.Trim());
            }
            if (objective.AcceptanceCriteria.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine("### Existing Acceptance Criteria");
                foreach (string item in objective.AcceptanceCriteria)
                    builder.AppendLine("- " + item);
            }
            if (objective.NonGoals.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine("### Existing Non-Goals");
                foreach (string item in objective.NonGoals)
                    builder.AppendLine("- " + item);
            }
            if (objective.RolloutConstraints.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine("### Existing Rollout Constraints");
                foreach (string item in objective.RolloutConstraints)
                    builder.AppendLine("- " + item);
            }
            if (!String.IsNullOrWhiteSpace(objective.RefinementSummary))
            {
                builder.AppendLine();
                builder.AppendLine("### Current Refinement Summary");
                builder.AppendLine(objective.RefinementSummary.Trim());
            }
            if (vessel != null)
            {
                if (!String.IsNullOrWhiteSpace(vessel.ProjectContext))
                {
                    builder.AppendLine();
                    builder.AppendLine("## Vessel Project Context");
                    builder.AppendLine(vessel.ProjectContext.Trim());
                }
                if (!String.IsNullOrWhiteSpace(vessel.StyleGuide))
                {
                    builder.AppendLine();
                    builder.AppendLine("## Vessel Style Guide");
                    builder.AppendLine(vessel.StyleGuide.Trim());
                }
                if (vessel.EnableModelContext && !String.IsNullOrWhiteSpace(vessel.ModelContext))
                {
                    builder.AppendLine();
                    builder.AppendLine("## Vessel Model Context");
                    builder.AppendLine(vessel.ModelContext.Trim());
                }
            }
            if (!String.IsNullOrWhiteSpace(captain.SystemInstructions))
            {
                builder.AppendLine();
                builder.AppendLine("## Captain Instructions");
                builder.AppendLine(captain.SystemInstructions.Trim());
            }
            builder.AppendLine();
            builder.AppendLine("## Conversation Transcript");
            builder.AppendLine(transcript);
            builder.AppendLine();
            builder.AppendLine("## Response Guidance");
            builder.AppendLine("- Clarify what should be built before any implementation begins.");
            builder.AppendLine("- Prefer concrete acceptance criteria over generic prose.");
            builder.AppendLine("- Call out missing decisions and meaningful constraints.");
            builder.AppendLine("- Do not pretend the work is already complete.");

            await File.WriteAllTextAsync(filePath, builder.ToString(), token).ConfigureAwait(false);
            return filePath;
        }

        private async Task<string> RunRuntimePromptAsync(
            ObjectiveRefinementSession session,
            Captain captain,
            Vessel? vessel,
            string prompt,
            string logFilePath,
            string finalMessageFilePath,
            CancellationToken token)
        {
            IAgentRuntime runtime = CreateRefinementRuntime(captain);
            TaskCompletionSource<int?> exitSource = new TaskCompletionSource<int?>(TaskCreationOptions.RunContinuationsAsynchronously);
            StringBuilder output = new StringBuilder();
            object outputLock = new object();
            int? processId = null;

            runtime.OnStdoutReceived += (runtimeProcessId, line) =>
            {
                // stdout-only so CLI stderr banners stay out of the captured refinement summary.
                lock (outputLock)
                {
                    BoundedTextBuffer.AppendLine(output, line, _MaxRefinementOutputChars);
                }
            };
            runtime.OnProcessExited += (runtimeProcessId, exitCode) => exitSource.TrySetResult(exitCode);

            try
            {
                processId = await runtime.StartAsync(
                    ResolveWorkingDirectory(vessel),
                    prompt,
                    logFilePath: logFilePath,
                    finalMessageFilePath: finalMessageFilePath,
                    model: captain.Model,
                    captain: captain,
                    token: token).ConfigureAwait(false);

                session.ProcessId = processId;
                session.LastUpdateUtc = DateTime.UtcNow;
                await _Database.ObjectiveRefinementSessions.UpdateAsync(session, token).ConfigureAwait(false);
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
                    finalOutput = "Objective refinement helper prompt exited with code " + exitCode.Value + ".";

                return finalOutput;
            }
            finally
            {
                if (processId.HasValue)
                {
                    try
                    {
                        ObjectiveRefinementSession? refreshedSession = await _Database.ObjectiveRefinementSessions.ReadAsync(session.Id, CancellationToken.None).ConfigureAwait(false);
                        if (refreshedSession != null && refreshedSession.ProcessId.HasValue)
                        {
                            refreshedSession.ProcessId = null;
                            refreshedSession.LastUpdateUtc = DateTime.UtcNow;
                            await _Database.ObjectiveRefinementSessions.UpdateAsync(refreshedSession, CancellationToken.None).ConfigureAwait(false);
                            BroadcastSessionChanged(refreshedSession);
                        }
                    }
                    catch (Exception ex)
                    {
                        _Logging.Warn(_Header + "objective refinement summary session cleanup failed for " + session.Id + ": " + ex.ToString());
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
                        _Logging.Warn(_Header + "objective refinement summary captain cleanup failed for " + captain.Id + ": " + ex.ToString());
                    }
                }
            }
        }

        private async Task<ObjectiveRefinementMessage> ResolveSourceMessageAsync(
            ObjectiveRefinementSession session,
            string? messageId,
            CancellationToken token)
        {
            ObjectiveRefinementMessage? sourceMessage = null;
            if (!String.IsNullOrWhiteSpace(messageId))
            {
                sourceMessage = await _Database.ObjectiveRefinementMessages.ReadAsync(messageId, token).ConfigureAwait(false);
                if (sourceMessage == null || sourceMessage.ObjectiveRefinementSessionId != session.Id)
                    throw new InvalidOperationException("Objective refinement message not found: " + messageId);
            }
            else
            {
                List<ObjectiveRefinementMessage> messages = await _Database.ObjectiveRefinementMessages
                    .EnumerateBySessionAsync(session.Id, token)
                    .ConfigureAwait(false);
                sourceMessage = messages
                    .Where(m => String.Equals(m.Role, "Assistant", StringComparison.OrdinalIgnoreCase))
                    .Where(m => !String.IsNullOrWhiteSpace(m.Content))
                    .OrderByDescending(m => m.IsSelected)
                    .ThenByDescending(m => m.Sequence)
                    .FirstOrDefault();
            }

            if (sourceMessage == null)
                throw new InvalidOperationException("No assistant refinement output is available.");

            return sourceMessage;
        }

        private async Task SelectMessageAsync(ObjectiveRefinementSession session, string messageId, CancellationToken token)
        {
            List<ObjectiveRefinementMessage> messages = await _Database.ObjectiveRefinementMessages
                .EnumerateBySessionAsync(session.Id, token)
                .ConfigureAwait(false);

            foreach (ObjectiveRefinementMessage message in messages)
            {
                bool shouldSelect = String.Equals(message.Id, messageId, StringComparison.OrdinalIgnoreCase);
                if (message.IsSelected == shouldSelect)
                    continue;

                message.IsSelected = shouldSelect;
                message.LastUpdateUtc = DateTime.UtcNow;
                await _Database.ObjectiveRefinementMessages.UpdateAsync(message, token).ConfigureAwait(false);
                BroadcastMessageUpdated(message);
            }
        }

        private async Task<ObjectiveRefinementSession> PrepareStopAsync(ObjectiveRefinementSession session, CancellationToken token)
        {
            session = await RequireSessionAsync(session.Id, token).ConfigureAwait(false);
            if (session.Status == ObjectiveRefinementSessionStatusEnum.Stopped || session.Status == ObjectiveRefinementSessionStatusEnum.Failed)
                return session;

            if (_ActiveTurns.TryGetValue(session.Id, out TurnState? turnState))
                turnState.StopRequested = true;

            if (session.Status != ObjectiveRefinementSessionStatusEnum.Stopping)
            {
                session.Status = ObjectiveRefinementSessionStatusEnum.Stopping;
                session.LastUpdateUtc = DateTime.UtcNow;
                session = await _Database.ObjectiveRefinementSessions.UpdateAsync(session, token).ConfigureAwait(false);
                BroadcastSessionChanged(session);
            }

            return session;
        }

        private Task<ObjectiveRefinementSession> EnsureStopOperation(string sessionId)
        {
            return _StopOperations.GetOrAdd(
                sessionId,
                id => Task.Run(() => CompleteStopAsync(id), CancellationToken.None));
        }

        private async Task<ObjectiveRefinementSession> CompleteStopAsync(string sessionId)
        {
            try
            {
                ObjectiveRefinementSession session = await RequireSessionAsync(sessionId, CancellationToken.None).ConfigureAwait(false);
                if (session.Status == ObjectiveRefinementSessionStatusEnum.Stopped || session.Status == ObjectiveRefinementSessionStatusEnum.Failed)
                    return session;

                Captain? captain = await _Database.Captains.ReadAsync(session.CaptainId).ConfigureAwait(false);
                if (session.ProcessId.HasValue && captain != null)
                {
                    try
                    {
                        IAgentRuntime runtime = CreateRefinementRuntime(captain);
                        await runtime.StopAsync(session.ProcessId.Value, CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _Logging.Warn(_Header + "error stopping objective refinement process " + session.ProcessId.Value + " for session " + session.Id + ": " + ex.ToString());
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
                session.Status = ObjectiveRefinementSessionStatusEnum.Stopped;
                session.ProcessId = null;
                session.CompletedUtc = DateTime.UtcNow;
                session.LastUpdateUtc = DateTime.UtcNow;
                session = await _Database.ObjectiveRefinementSessions.UpdateAsync(session).ConfigureAwait(false);
                BroadcastSessionChanged(session);

                await _EmitEventAsync(
                    "objective-refinement-session.stopped",
                    "Objective refinement session " + session.Id + " stopped",
                    "objective-refinement-session",
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

        private IAgentRuntime CreateRefinementRuntime(Captain captain)
        {
            if (!captain.SupportsPlanningSessions)
                throw new InvalidOperationException(captain.PlanningSessionSupportReason ?? "This captain runtime is not supported for refinement sessions.");

            IAgentRuntime runtime = _RuntimeFactory.Create(captain.Runtime);
            if (!runtime.SupportsPlanningSessions)
                throw new InvalidOperationException("Runtime " + runtime.Name + " does not currently support refinement sessions.");
            return runtime;
        }

        private void BroadcastSessionChanged(ObjectiveRefinementSession session)
        {
            _WebSocketHub?.BroadcastEvent(
                "objective-refinement-session.changed",
                "Objective refinement session updated",
                new { session });
        }

        private void BroadcastMessageCreated(ObjectiveRefinementMessage message)
        {
            _WebSocketHub?.BroadcastEvent(
                "objective-refinement-session.message.created",
                "Objective refinement message created",
                new
                {
                    sessionId = message.ObjectiveRefinementSessionId,
                    objectiveId = message.ObjectiveId,
                    message
                });
        }

        private void BroadcastMessageUpdated(ObjectiveRefinementMessage message)
        {
            _WebSocketHub?.BroadcastEvent(
                "objective-refinement-session.message.updated",
                "Objective refinement message updated",
                new
                {
                    sessionId = message.ObjectiveRefinementSessionId,
                    objectiveId = message.ObjectiveId,
                    message
                });
        }

        private async Task<ObjectiveRefinementSession> RequireSessionAsync(string sessionId, CancellationToken token)
        {
            ObjectiveRefinementSession? session = await _Database.ObjectiveRefinementSessions.ReadAsync(sessionId, token).ConfigureAwait(false);
            if (session == null) throw new InvalidOperationException("Objective refinement session not found: " + sessionId);
            return session;
        }

        private async Task<ObjectiveRefinementMessage> RequireMessageAsync(string messageId, CancellationToken token)
        {
            ObjectiveRefinementMessage? message = await _Database.ObjectiveRefinementMessages.ReadAsync(messageId, token).ConfigureAwait(false);
            if (message == null) throw new InvalidOperationException("Objective refinement message not found: " + messageId);
            return message;
        }

        private async Task<Objective> RequireObjectiveAsync(string objectiveId, CancellationToken token)
        {
            Objective? objective = await _Database.Objectives.ReadAsync(objectiveId, token).ConfigureAwait(false);
            if (objective == null) throw new InvalidOperationException("Objective not found: " + objectiveId);
            return objective;
        }

        private async Task<Captain> RequireCaptainAsync(string captainId, CancellationToken token)
        {
            Captain? captain = await _Database.Captains.ReadAsync(captainId, token).ConfigureAwait(false);
            if (captain == null) throw new InvalidOperationException("Captain not found: " + captainId);
            return captain;
        }

        private async Task<Vessel?> TryReadVesselAsync(string? vesselId, CancellationToken token)
        {
            if (String.IsNullOrWhiteSpace(vesselId))
                return null;
            return await _Database.Vessels.ReadAsync(vesselId, token).ConfigureAwait(false);
        }

        private static string ResolveWorkingDirectory(Vessel? vessel)
        {
            return vessel?.WorkingDirectory
                ?? vessel?.LocalPath
                ?? Directory.GetCurrentDirectory();
        }

        private static string RenderTranscript(List<ObjectiveRefinementMessage> messages, int maxChars)
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

        private static ObjectiveRefinementSummaryResponse BuildFallbackSummary(
            ObjectiveRefinementSession session,
            ObjectiveRefinementMessage sourceMessage)
        {
            string content = sourceMessage.Content.Trim();
            return new ObjectiveRefinementSummaryResponse
            {
                SessionId = session.Id,
                MessageId = sourceMessage.Id,
                Summary = content,
                AcceptanceCriteria = ExtractMarkdownBullets(content, "Acceptance Criteria"),
                NonGoals = ExtractMarkdownBullets(content, "Non-Goals", "Non Goals"),
                RolloutConstraints = ExtractMarkdownBullets(content, "Rollout Constraints", "Constraints"),
                Method = "assistant-fallback"
            };
        }

        private static bool TryParseSummaryResponse(string content, out ObjectiveRefinementSummaryResponse? response)
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
                response = new ObjectiveRefinementSummaryResponse
                {
                    Summary = root.TryGetProperty("summary", out JsonElement summary) ? summary.GetString() ?? String.Empty : String.Empty,
                    AcceptanceCriteria = root.TryGetProperty("acceptanceCriteria", out JsonElement acceptanceCriteria)
                        ? ReadStringArray(acceptanceCriteria)
                        : new List<string>(),
                    NonGoals = root.TryGetProperty("nonGoals", out JsonElement nonGoals)
                        ? ReadStringArray(nonGoals)
                        : new List<string>(),
                    RolloutConstraints = root.TryGetProperty("rolloutConstraints", out JsonElement rolloutConstraints)
                        ? ReadStringArray(rolloutConstraints)
                        : new List<string>(),
                    SuggestedPipelineId = root.TryGetProperty("suggestedPipelineId", out JsonElement pipelineId)
                        ? pipelineId.GetString()
                        : null
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

        private static List<string> ReadStringArray(JsonElement element)
        {
            if (element.ValueKind != JsonValueKind.Array)
                return new List<string>();

            return element.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString() ?? String.Empty)
                .Where(item => !String.IsNullOrWhiteSpace(item))
                .Select(item => item.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static List<string> NormalizeLines(IEnumerable<string>? values)
        {
            if (values == null)
                return new List<string>();

            return values
                .Select(value => Normalize(value))
                .Where(value => !String.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Cast<string>()
                .ToList();
        }

        private static List<string> MergeDistinct(IEnumerable<string> existing, string value)
        {
            List<string> merged = existing?
                .Where(item => !String.IsNullOrWhiteSpace(item))
                .Select(item => item.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
                ?? new List<string>();

            string? normalized = Normalize(value);
            if (!String.IsNullOrWhiteSpace(normalized) && !merged.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                merged.Add(normalized);

            return merged;
        }

        private static ObjectiveStatusEnum? ResolveAppliedStatus(ObjectiveStatusEnum current)
        {
            if (current == ObjectiveStatusEnum.Draft)
                return ObjectiveStatusEnum.Scoped;
            return null;
        }

        private static ObjectiveBacklogStateEnum ResolveAppliedBacklogState(Objective objective, ObjectiveRefinementSession session)
        {
            return !String.IsNullOrWhiteSpace(session.VesselId) || objective.VesselIds.Count > 0
                ? ObjectiveBacklogStateEnum.ReadyForPlanning
                : ObjectiveBacklogStateEnum.Triaged;
        }

        private static string? Normalize(string? value)
        {
            return String.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static List<string> ExtractMarkdownBullets(string content, params string[] headings)
        {
            if (String.IsNullOrWhiteSpace(content) || headings == null || headings.Length == 0)
                return new List<string>();

            string[] lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.None);
            bool inSection = false;
            List<string> values = new List<string>();

            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim();
                if (line.StartsWith("##") || line.StartsWith("###"))
                {
                    string heading = line.TrimStart('#', ' ').Trim().TrimEnd(':');
                    bool matches = headings.Any(candidate => String.Equals(candidate, heading, StringComparison.OrdinalIgnoreCase));
                    if (matches)
                    {
                        inSection = true;
                        continue;
                    }

                    if (inSection)
                        break;
                }

                if (!inSection)
                    continue;

                if (line.StartsWith("- ") || line.StartsWith("* "))
                {
                    string value = line.Substring(2).Trim();
                    if (!String.IsNullOrWhiteSpace(value))
                        values.Add(value);
                }
            }

            return values
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
