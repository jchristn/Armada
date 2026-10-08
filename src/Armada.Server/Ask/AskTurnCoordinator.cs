namespace Armada.Server.Ask
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Ask;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// Runs captain turns for Ask Armada threads: one running turn per thread, prompt history built server-side from the
    /// thread's persisted messages (last Ask.HistoryTurns messages plus the thread summary), a thread-scoped Armada MCP
    /// token so state-changing tool calls become proposals, owner-scoped streaming (ask.chunk, ask.thinking, ask.tool,
    /// ask.turn), persistence of the reply and its tool calls, cancellation, summaries, follow-up turns after an
    /// approval, and short milestone narration when the captain is idle.
    /// </summary>
    /// <remarks>Thread safety: running turns and captain usage are tracked in concurrent dictionaries; all public
    /// methods may be called concurrently.</remarks>
    public class AskTurnCoordinator
    {
        #region Public-Members

        /// <summary>
        /// Instructions appended to the system prompt of every thread turn.
        /// </summary>
        public const string ThreadInstructions =
            "You are answering in an Ask Armada conversation. Use Armada's MCP tools (server \"armada\") to look things up and to start work. "
            + "Read-only tools run immediately. Any tool that changes state is not executed right away: it becomes a proposal that the user approves or rejects in this conversation. "
            + "When a tool result says \"Proposed as aap_... and waiting for the user's approval\", do not retry the call; tell the user in one or two sentences what you proposed and that it is waiting for their approval. "
            + "Work started from this conversation is tracked here automatically, so you do not need to poll it. Keep answers short and concrete.";

        /// <summary>
        /// CLI permission service: pending permission prompts of a thread are cancelled when its turn ends. Null leaves
        /// them to expire.
        /// </summary>
        public CliPermissionService? CliPermissions { get; set; } = null;

        #endregion

        #region Private-Members

        private readonly string _Header = "[AskTurnCoordinator] ";
        private readonly DatabaseDriver _Database;
        private readonly AskThreadService _Threads;
        private readonly IAskCaptainTurnRunner _Runner;
        private readonly ISessionTokenService? _Tokens;
        private readonly IPromptTemplateService? _Templates;
        private readonly ArmadaSettings _Settings;
        private readonly LoggingModule _Logging;
        private readonly ConcurrentDictionary<string, AskTurnHandle> _Running = new ConcurrentDictionary<string, AskTurnHandle>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, int> _CaptainUsage = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="threads">Thread service.</param>
        /// <param name="runner">Captain turn runner.</param>
        /// <param name="tokens">Session token service used to mint thread-scoped MCP tokens; null disables MCP tools.</param>
        /// <param name="templates">Prompt templates (ask.system); may be null.</param>
        /// <param name="settings">Application settings.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public AskTurnCoordinator(DatabaseDriver database, AskThreadService threads, IAskCaptainTurnRunner runner, ISessionTokenService? tokens, IPromptTemplateService? templates, ArmadaSettings settings, LoggingModule logging)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Threads = threads ?? throw new ArgumentNullException(nameof(threads));
            _Runner = runner ?? throw new ArgumentNullException(nameof(runner));
            _Tokens = tokens;
            _Templates = templates;
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The id of the turn running in a thread, or null.
        /// </summary>
        /// <param name="threadId">Thread identifier.</param>
        /// <returns>Turn identifier, or null.</returns>
        public string? ActiveTurnId(string threadId)
        {
            if (String.IsNullOrEmpty(threadId)) return null;
            return _Running.TryGetValue(threadId, out AskTurnHandle? handle) ? handle.TurnId : null;
        }

        /// <summary>
        /// Persist the caller's message and, when the thread has a captain, start a captain turn in the background.
        /// </summary>
        /// <param name="auth">Caller (thread owner).</param>
        /// <param name="threadId">Thread identifier.</param>
        /// <param name="request">Message content and options.</param>
        /// <param name="token">Cancellation token for the request (the turn itself runs on its own token).</param>
        /// <returns>202 with MessageId and TurnId; 400 for an empty or oversized message; 404 when the thread is not
        /// found; 409 when a turn is already running in the thread.</returns>
        public async Task<AskTurnStart> SendMessageAsync(AuthContext auth, string threadId, AskMessageSendRequest? request, CancellationToken token = default)
        {
            string content = request?.Content?.Trim() ?? String.Empty;
            if (content.Length == 0) return new AskTurnStart(400, null, "Content is required");
            if (content.Length > 32000) return new AskTurnStart(400, null, "Content exceeds 32000 characters");

            AskThread? thread = await _Threads.GetThreadAsync(auth, threadId, token).ConfigureAwait(false);
            if (thread == null) return new AskTurnStart(404, null, "Thread not found");

            AskTurnHandle? handle = null;
            if (!String.IsNullOrEmpty(thread.CaptainId))
            {
                handle = new AskTurnHandle(thread.Id, thread.CaptainId, "Message");
                if (!_Running.TryAdd(thread.Id, handle)) return new AskTurnStart(409, null, "A captain turn is already running in this conversation");
            }

            AskMessage userMessage;
            try
            {
                thread = await _Threads.ApplyAutoTitleAsync(thread, content, token).ConfigureAwait(false);
                AskMessage message = new AskMessage();
                message.Role = AskMessageRoleEnum.User;
                message.Kind = AskMessageKindEnum.Text;
                message.ContentText = content;
                userMessage = await _Threads.AppendMessageAsync(thread, message, false, null, token).ConfigureAwait(false);
            }
            catch
            {
                if (handle != null) Release(handle);
                throw;
            }

            AskMessageSendResponse response = new AskMessageSendResponse();
            response.MessageId = userMessage.Id;
            if (handle == null) return new AskTurnStart(202, response, null);

            response.TurnId = handle.TurnId;
            bool showThinking = request?.ShowThinking == true;
            AskThread turnThread = thread;
            _ = Task.Run(() => RunTurnAsync(turnThread, handle, userMessage.Sequence, null, showThinking));
            return new AskTurnStart(202, response, null);
        }

        /// <summary>
        /// Stop the turn running in one of the caller's threads.
        /// </summary>
        /// <param name="auth">Caller.</param>
        /// <param name="threadId">Thread identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>404 when the thread is not found, 409 when no turn is running, 200 when the turn was asked to stop.</returns>
        public async Task<int> CancelAsync(AuthContext auth, string threadId, CancellationToken token = default)
        {
            AskThread? thread = await _Threads.GetThreadAsync(auth, threadId, token).ConfigureAwait(false);
            if (thread == null) return 404;
            if (!_Running.TryGetValue(thread.Id, out AskTurnHandle? handle)) return 409;
            try { handle.Cancellation.Cancel(); }
            catch (ObjectDisposedException) { }
            return 200;
        }

        /// <summary>
        /// Summarize one of the caller's threads in the background: the captain writes the summary when the thread has
        /// one, otherwise a deterministic summary is posted. Uses the thread's turn slot.
        /// </summary>
        /// <param name="auth">Caller.</param>
        /// <param name="threadId">Thread identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>202 (with TurnId when a captain writes it), 404 when not found, 409 when a turn is running.</returns>
        public async Task<AskTurnStart> SummarizeAsync(AuthContext auth, string threadId, CancellationToken token = default)
        {
            AskThread? thread = await _Threads.GetThreadAsync(auth, threadId, token).ConfigureAwait(false);
            if (thread == null) return new AskTurnStart(404, null, "Thread not found");

            AskTurnHandle handle = new AskTurnHandle(thread.Id, thread.CaptainId, "Summary");
            if (!_Running.TryAdd(thread.Id, handle)) return new AskTurnStart(409, null, "A captain turn is already running in this conversation");

            AskMessageSendResponse response = new AskMessageSendResponse();
            response.TurnId = String.IsNullOrEmpty(thread.CaptainId) ? null : handle.TurnId;
            AskThread summaryThread = thread;
            _ = Task.Run(() => RunSummaryAsync(summaryThread, handle));
            return new AskTurnStart(202, response, null);
        }

        /// <summary>
        /// After the user approved a captain proposal, run a short follow-up turn so the captain can continue the
        /// conversation. Skipped when the thread has no captain or a turn is already running.
        /// </summary>
        /// <param name="thread">Thread.</param>
        /// <param name="proposal">The executed (or failed) proposal.</param>
        /// <returns>Task.</returns>
        public Task StartFollowUpAsync(AskThread thread, AskActionProposal proposal)
        {
            if (thread == null || proposal == null || String.IsNullOrEmpty(thread.CaptainId)) return Task.CompletedTask;
            AskTurnHandle handle = new AskTurnHandle(thread.Id, thread.CaptainId, "FollowUp");
            if (!_Running.TryAdd(thread.Id, handle)) return Task.CompletedTask;

            string outcome = proposal.Status == AskProposalStatusEnum.Executed
                ? "It ran successfully. Result: " + Clip(proposal.ResultText, 2000)
                : "It failed: " + Clip(proposal.ErrorText ?? proposal.ResultText, 1000);
            string note = "The user approved " + proposal.Id + " (" + proposal.SummaryText + "). " + outcome
                + " Continue the conversation: tell the user in one or two sentences what happened and what to expect next. Do not repeat the action.";
            _ = Task.Run(() => RunTurnAsync(thread, handle, null, note, false));
            return Task.CompletedTask;
        }

        /// <summary>
        /// Ask the thread's captain to word a milestone update, only when the captain is idle: Ask.NarrateMilestones is
        /// on, the thread has a captain whose state is Idle, no turn is running in the thread, and no other Ask turn or
        /// narration is using the captain. The narration has no say over the captain's state (missions and planning are
        /// never blocked) and is bounded by Ask.NarrationTimeoutSeconds.
        /// </summary>
        /// <param name="thread">Thread.</param>
        /// <param name="milestoneText">Deterministic milestone sentence.</param>
        /// <param name="snapshot">Current work snapshot.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The captain's wording, or null to use the deterministic sentence.</returns>
        public async Task<string?> NarrateAsync(AskThread thread, string milestoneText, AskWorkSnapshot snapshot, CancellationToken token = default)
        {
            if (!_Settings.Ask.NarrateMilestones) return null;
            if (thread == null || String.IsNullOrEmpty(thread.CaptainId)) return null;
            if (_Running.ContainsKey(thread.Id)) return null;

            Captain? captain = await _Database.Captains.ReadAsync(thread.CaptainId!, token).ConfigureAwait(false);
            if (captain == null || captain.State != CaptainStateEnum.Idle) return null;
            if (!TryReserveCaptain(captain.Id)) return null;

            try
            {
                StringBuilder prompt = new StringBuilder();
                prompt.AppendLine("Rewrite this status update about work the user started, for the user, in one or two short sentences. Do not call any tools and do not add facts that are not given.");
                prompt.AppendLine();
                prompt.AppendLine("Update: " + milestoneText);
                prompt.AppendLine("Work: " + AskMilestoneDetector.Describe(snapshot));

                CaptainChatTurnOptions options = new CaptainChatTurnOptions();
                ApplyCliPermissions(options, thread, captain, false);
                options.Prompt = prompt.ToString();
                options.TenantId = thread.TenantId;
                options.UserId = thread.UserId;
                options.McpSessionToken = MintToken(thread);
                options.TimeoutMs = _Settings.Ask.NarrationTimeoutSeconds * 1000;

                using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(_Settings.Ask.NarrationTimeoutSeconds + 5));
                    CaptainChatTurnResult result = await _Runner.RunTurnAsync(options, timeout.Token).ConfigureAwait(false);
                    if (!result.Response.Success || String.IsNullOrWhiteSpace(result.Response.Reply)) return null;
                    string reply = result.Response.Reply.Trim();
                    return reply.Length > 1000 ? reply.Substring(0, 1000) + "..." : reply;
                }
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (Exception ex)
            {
                _Logging.Debug(_Header + "narration for thread " + thread.Id + " failed: " + ex.Message);
                return null;
            }
            finally
            {
                ReleaseCaptain(captain.Id);
            }
        }

        /// <summary>
        /// Build the prompt for a thread turn: system prompt, thread instructions, summary, recent history, and the new
        /// input (the latest user message or a server note).
        /// </summary>
        /// <param name="thread">Thread.</param>
        /// <param name="captain">Captain.</param>
        /// <param name="history">Recent messages, oldest first (the last user message last when present).</param>
        /// <param name="note">Server note appended after the history (follow-up turns), or null.</param>
        /// <param name="systemPrompt">Ask system prompt, or null.</param>
        /// <returns>The prompt.</returns>
        public static string BuildPrompt(AskThread thread, Captain captain, List<AskMessage> history, string? note, string? systemPrompt)
        {
            return BuildPrompt(thread, captain, history, note, systemPrompt, null);
        }

        /// <summary>
        /// Build the prompt for a thread turn, including the conversation focus: the work this conversation started
        /// and the vessels involved, so follow-up questions such as "what's running?" can be scoped to them.
        /// </summary>
        /// <param name="thread">Thread.</param>
        /// <param name="captain">Captain.</param>
        /// <param name="history">Recent messages, oldest first (the last user message last when present).</param>
        /// <param name="note">Server note appended after the history (follow-up turns), or null.</param>
        /// <param name="systemPrompt">Ask system prompt, or null.</param>
        /// <param name="focus">Conversation focus block (see <see cref="BuildFocusText"/>), or null when the conversation has started no work.</param>
        /// <returns>The prompt.</returns>
        public static string BuildPrompt(AskThread thread, Captain captain, List<AskMessage> history, string? note, string? systemPrompt, string? focus)
        {
            StringBuilder builder = new StringBuilder();
            if (!String.IsNullOrWhiteSpace(systemPrompt)) builder.AppendLine(systemPrompt.Trim()).AppendLine();
            if (captain != null && !String.IsNullOrWhiteSpace(captain.SystemInstructions)) builder.AppendLine(captain.SystemInstructions.Trim()).AppendLine();
            builder.AppendLine(ThreadInstructions).AppendLine();

            if (!String.IsNullOrWhiteSpace(focus)) builder.AppendLine(focus!.Trim()).AppendLine();

            if (!String.IsNullOrWhiteSpace(thread.SummaryText))
            {
                builder.AppendLine("Summary of the conversation so far:");
                builder.AppendLine(thread.SummaryText!.Trim()).AppendLine();
            }

            if (history.Count > 0)
            {
                builder.AppendLine("Conversation:");
                foreach (AskMessage message in history)
                {
                    if (String.IsNullOrWhiteSpace(message.ContentText)) continue;
                    builder.AppendLine(Speaker(message) + ": " + Clip(message.ContentText.Trim(), 8000));
                }

                builder.AppendLine();
            }

            if (!String.IsNullOrWhiteSpace(note)) builder.AppendLine("Armada: " + note!.Trim());
            return builder.ToString().TrimEnd();
        }

        /// <summary>
        /// Render the conversation focus block from the work a conversation started (most recent first) and the
        /// vessels that work touched. Returns null when there is nothing to report.
        /// </summary>
        /// <param name="work">Tracked work, most recent first.</param>
        /// <param name="workVessels">Vessel display text per tracked work id (for example "TUIKit (vsl_...)"), may be empty.</param>
        /// <param name="vessels">Distinct vessels involved, most recent first, as display text.</param>
        /// <returns>The focus block, or null.</returns>
        public static string? BuildFocusText(List<AskTrackedWork> work, Dictionary<string, string> workVessels, List<string> vessels)
        {
            if (work == null || work.Count < 1) return null;
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("Conversation focus (work started in this conversation, most recent first):");
            foreach (AskTrackedWork item in work.Take(10))
            {
                string line = "- " + item.EntityType + " \"" + Clip(item.Title, 120) + "\" (" + item.EntityId + ")";
                if (workVessels != null && workVessels.TryGetValue(item.Id, out string? on) && !String.IsNullOrEmpty(on)) line += " on " + on;
                line += ": " + (String.IsNullOrEmpty(item.Status) ? item.State.ToString() : item.Status) + (item.State == AskTrackedWorkStateEnum.Active ? " (active)" : "");
                builder.AppendLine(line);
            }

            if (vessels != null && vessels.Count > 0)
                builder.AppendLine("Vessels this conversation is working with: " + String.Join(", ", vessels.Take(10)) + ".");
            builder.Append("Scope follow-up questions to this focus unless the user asks about the whole fleet (see Conversation scope).");
            return builder.ToString();
        }

        #endregion

        #region Private-Methods

        private async Task<string?> BuildFocusAsync(AskThread thread)
        {
            try
            {
                List<AskTrackedWork> work = await _Database.AskTrackedWork.EnumerateByThreadAsync(thread.TenantId!, thread.Id).ConfigureAwait(false);
                if (work.Count < 1) return null;
                work = work.OrderByDescending(w => w.CreatedUtc).ToList();

                Dictionary<string, string> vesselNames = new Dictionary<string, string>(StringComparer.Ordinal);
                Dictionary<string, string> workVessels = new Dictionary<string, string>(StringComparer.Ordinal);
                List<string> ordered = new List<string>();

                foreach (AskTrackedWork item in work.Take(10))
                {
                    List<string> ids = new List<string>();
                    if (item.EntityType == AskTrackedEntityTypeEnum.Voyage)
                    {
                        List<Mission> missions = await _Database.Missions.EnumerateByVoyageAsync(thread.TenantId!, item.EntityId).ConfigureAwait(false);
                        ids.AddRange(missions.Where(m => !String.IsNullOrEmpty(m.VesselId)).Select(m => m.VesselId!));
                    }
                    else if (item.EntityType == AskTrackedEntityTypeEnum.Mission)
                    {
                        Mission? mission = await _Database.Missions.ReadAsync(thread.TenantId!, item.EntityId).ConfigureAwait(false);
                        if (mission != null && !String.IsNullOrEmpty(mission.VesselId)) ids.Add(mission.VesselId!);
                    }
                    else if (item.EntityType == AskTrackedEntityTypeEnum.FleetActionRun)
                    {
                        List<FleetActionRunTarget> targets = await _Database.FleetActionRunTargets.ReadAllByRunAsync(item.EntityId).ConfigureAwait(false);
                        ids.AddRange(targets.Where(t => String.Equals(t.TenantId, thread.TenantId, StringComparison.Ordinal)).Select(t => t.VesselId));
                    }

                    List<string> labels = new List<string>();
                    foreach (string id in ids.Distinct(StringComparer.Ordinal).Take(10))
                    {
                        if (!vesselNames.TryGetValue(id, out string? label))
                        {
                            Vessel? vessel = await _Database.Vessels.ReadAsync(thread.TenantId!, id).ConfigureAwait(false);
                            label = vessel != null ? vessel.Name + " (" + id + ")" : id;
                            vesselNames[id] = label;
                            ordered.Add(label);
                        }

                        labels.Add(label);
                    }

                    if (labels.Count > 0) workVessels[item.Id] = String.Join(", ", labels.Take(3)) + (labels.Count > 3 ? " and " + (labels.Count - 3) + " more" : "");
                }

                return BuildFocusText(work, workVessels, ordered);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                _Logging.Debug(_Header + "could not build conversation focus for thread " + thread.Id + ": " + ex.Message);
                return null;
            }
        }

        private async Task RunTurnAsync(AskThread thread, AskTurnHandle handle, int? userSequence, string? note, bool showThinking)
        {
            string state = "failed";
            string? messageId = null;
            string? error = null;
            bool reserved = false;
            AskMessage? placeholder = null;
            try
            {
                Emit(thread, "ask.turn", new { threadId = thread.Id, turnId = handle.TurnId, state = "started", messageId = (string?)null });

                // Reserve the reply's place in the thread now. Confirm cards, approvals, and work updates can be
                // posted while the captain is still writing; reserving the sequence keeps them after the reply
                // that introduced them instead of above it.
                AskMessage reservation = new AskMessage();
                reservation.Role = AskMessageRoleEnum.Assistant;
                reservation.Kind = AskMessageKindEnum.Text;
                reservation.CaptainId = handle.CaptainId;
                reservation.ContentText = String.Empty;
                placeholder = await _Threads.AppendMessageAsync(thread, reservation, true).ConfigureAwait(false);
                messageId = placeholder.Id;

                Captain? captain = await _Database.Captains.ReadAsync(handle.CaptainId!).ConfigureAwait(false);
                if (captain == null) throw new InvalidOperationException("The conversation's captain no longer exists.");
                AcquireCaptain(captain.Id);
                reserved = true;

                AskThread current = await _Threads.ReadThreadInternalAsync(thread.Id).ConfigureAwait(false) ?? thread;
                List<AskMessage> history = await _Threads.ReadRecentMessagesAsync(current, _Settings.Ask.HistoryTurns).ConfigureAwait(false);
                if (placeholder != null) history.RemoveAll(m => String.Equals(m.Id, placeholder.Id, StringComparison.Ordinal));
                string? systemPrompt = await ResolveSystemPromptAsync().ConfigureAwait(false);

                CaptainChatTurnOptions options = new CaptainChatTurnOptions();
                ApplyCliPermissions(options, current, captain, true);
                options.Prompt = BuildPrompt(current, captain, history, note, systemPrompt, await BuildFocusAsync(current).ConfigureAwait(false));
                options.ShowThinking = showThinking;
                options.TenantId = thread.TenantId;
                options.UserId = thread.UserId;
                options.McpSessionToken = MintToken(thread);
                options.TimeoutMs = _Settings.Ask.TurnTimeoutMinutes * 60000;
                string turnId = handle.TurnId;
                options.OnChunk = delta => Emit(thread, "ask.chunk", new { threadId = thread.Id, turnId, delta });
                options.OnThinking = delta => Emit(thread, "ask.thinking", new { threadId = thread.Id, turnId, delta });
                options.OnTool = activity => Emit(thread, "ask.tool", new { threadId = thread.Id, turnId, phase = activity.Phase, id = activity.Id, name = activity.Name, arguments = activity.Arguments, ok = activity.Ok, elapsedMs = activity.ElapsedMs, result = activity.Result, permissionDenied = activity.PermissionDenied });

                CaptainChatTurnResult result = await _Runner.RunTurnAsync(options, handle.Cancellation.Token).ConfigureAwait(false);
                AskMessage reply = new AskMessage();
                reply.CaptainId = captain.Id;
                reply.DurationMs = result.Response.Metrics?.TotalMs.HasValue == true ? (long)Math.Round(result.Response.Metrics.TotalMs!.Value) : (long?)null;
                if (result.Response.Success)
                {
                    reply.Role = AskMessageRoleEnum.Assistant;
                    reply.Kind = AskMessageKindEnum.Text;
                    reply.ContentText = result.Response.Reply;
                    reply.ThinkingText = result.Response.Thinking;
                    reply.Metrics = result.Response.Metrics;
                    state = "completed";
                }
                else
                {
                    reply.Role = AskMessageRoleEnum.System;
                    reply.Kind = AskMessageKindEnum.Error;
                    reply.ContentText = result.Response.Error ?? "The captain did not respond.";
                    error = reply.ContentText;
                }

                reply = await FinishReplyAsync(thread, placeholder, reply, result.ToolCalls).ConfigureAwait(false);
                messageId = reply.Id;
            }
            catch (OperationCanceledException)
            {
                state = "cancelled";
                try
                {
                    if (placeholder != null)
                    {
                        AskMessage stopped = new AskMessage();
                        stopped.Role = AskMessageRoleEnum.System;
                        stopped.Kind = AskMessageKindEnum.Error;
                        stopped.ContentText = "Stopped before the captain finished replying.";
                        await FinishReplyAsync(thread, placeholder, stopped, null).ConfigureAwait(false);
                    }
                }
                catch { }
            }
            catch (Exception ex)
            {
                state = "failed";
                error = ex.Message;
                _Logging.Warn(_Header + "turn " + handle.TurnId + " in thread " + thread.Id + " failed: " + ex.ToString());
                try
                {
                    AskMessage failure = new AskMessage();
                    failure.Role = AskMessageRoleEnum.System;
                    failure.Kind = AskMessageKindEnum.Error;
                    failure.ContentText = ex.Message;
                    failure = placeholder != null
                        ? await FinishReplyAsync(thread, placeholder, failure, null).ConfigureAwait(false)
                        : await _Threads.AppendMessageAsync(thread, failure, true).ConfigureAwait(false);
                    messageId = failure.Id;
                }
                catch { }
            }
            finally
            {
                if (reserved && handle.CaptainId != null) ReleaseCaptain(handle.CaptainId);
                await CancelPendingCliPermissionsAsync(thread.Id).ConfigureAwait(false);
                Release(handle);
                Emit(thread, "ask.turn", new { threadId = thread.Id, turnId = handle.TurnId, state, messageId, error });
                try { await _Threads.EmitThreadAsync(thread.Id).ConfigureAwait(false); }
                catch { }
            }
        }

        private async Task<AskMessage> FinishReplyAsync(AskThread thread, AskMessage? placeholder, AskMessage reply, List<AskMessageToolCall>? toolCalls)
        {
            if (placeholder == null)
                return await _Threads.AppendMessageAsync(thread, reply, true, toolCalls).ConfigureAwait(false);

            placeholder.Role = reply.Role;
            placeholder.Kind = reply.Kind;
            placeholder.ContentText = reply.ContentText;
            placeholder.ThinkingText = reply.ThinkingText;
            placeholder.CaptainId = reply.CaptainId ?? placeholder.CaptainId;
            placeholder.DurationMs = reply.DurationMs;
            placeholder.Metrics = reply.Metrics;
            return await _Threads.CompleteMessageAsync(thread, placeholder, toolCalls).ConfigureAwait(false);
        }

        private async Task RunSummaryAsync(AskThread thread, AskTurnHandle handle)
        {
            string state = "failed";
            string? messageId = null;
            string? error = null;
            try
            {
                Emit(thread, "ask.turn", new { threadId = thread.Id, turnId = handle.TurnId, state = "started", messageId = (string?)null });
                List<AskMessage> history = await _Threads.ReadRecentMessagesAsync(thread, 200).ConfigureAwait(false);

                string? summary = null;
                string? captainId = null;
                if (!String.IsNullOrEmpty(thread.CaptainId))
                {
                    Captain? captain = await _Database.Captains.ReadAsync(thread.CaptainId!).ConfigureAwait(false);
                    if (captain != null)
                    {
                        CaptainChatTurnOptions options = new CaptainChatTurnOptions();
                        ApplyCliPermissions(options, thread, captain, false);
                        options.Prompt = BuildPrompt(thread, captain, history,
                            "Summarize this conversation for the user in 3 to 6 short bullet points: what was asked, what was decided or started (with ids), and what is still open. Do not call any tools.", null);
                        options.TenantId = thread.TenantId;
                        options.UserId = thread.UserId;
                        options.McpSessionToken = MintToken(thread);
                        options.TimeoutMs = _Settings.Ask.TurnTimeoutMinutes * 60000;
                        CaptainChatTurnResult result = await _Runner.RunTurnAsync(options, handle.Cancellation.Token).ConfigureAwait(false);
                        if (result.Response.Success && !String.IsNullOrWhiteSpace(result.Response.Reply))
                        {
                            summary = result.Response.Reply.Trim();
                            captainId = captain.Id;
                        }
                    }
                }

                if (summary == null) summary = BuildDeterministicSummary(history);
                AskMessage message = await _Threads.SetSummaryAsync(thread, summary, captainId).ConfigureAwait(false);
                messageId = message.Id;
                state = "completed";
            }
            catch (OperationCanceledException)
            {
                state = "cancelled";
            }
            catch (Exception ex)
            {
                error = ex.Message;
                _Logging.Warn(_Header + "summary of thread " + thread.Id + " failed: " + ex.Message);
            }
            finally
            {
                Release(handle);
                Emit(thread, "ask.turn", new { threadId = thread.Id, turnId = handle.TurnId, state, messageId, error });
                try { await _Threads.EmitThreadAsync(thread.Id).ConfigureAwait(false); }
                catch { }
            }
        }

        /// <summary>
        /// A summary written without a captain: message counts, the first and latest requests, and the actions taken.
        /// </summary>
        private static string BuildDeterministicSummary(List<AskMessage> history)
        {
            List<AskMessage> userMessages = history.Where(m => m.Role == AskMessageRoleEnum.User).ToList();
            List<AskMessage> results = history.Where(m => m.Kind == AskMessageKindEnum.ActionResult).ToList();
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("- " + history.Count + " message(s), " + userMessages.Count + " from you.");
            if (userMessages.Count > 0) builder.AppendLine("- First request: " + Clip(userMessages.First().ContentText, 200));
            if (userMessages.Count > 1) builder.AppendLine("- Latest request: " + Clip(userMessages.Last().ContentText, 200));
            foreach (AskMessage result in results.TakeLast(5)) builder.AppendLine("- " + Clip(result.ContentText, 200));
            return builder.ToString().TrimEnd();
        }

        private string? MintToken(AskThread thread)
        {
            if (_Tokens == null || String.IsNullOrEmpty(thread.TenantId) || String.IsNullOrEmpty(thread.UserId)) return null;
            try
            {
                AuthenticateResult minted = _Tokens.CreateThreadScopedToken(thread.TenantId!, thread.UserId!, thread.Id, TimeSpan.FromMinutes(_Settings.Ask.TurnTimeoutMinutes + 5));
                return minted.Token;
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "could not mint a thread-scoped token for " + thread.Id + ": " + ex.Message);
                return null;
            }
        }

        private async Task<string?> ResolveSystemPromptAsync()
        {
            if (_Templates == null) return null;
            try
            {
                PromptTemplate? template = await _Templates.ResolveAsync("ask.system").ConfigureAwait(false);
                return template?.Content;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Apply the CLI tool permission policy of an Ask turn (see <see cref="CliPermissionPolicyResolver.ResolveForAsk"/>):
        /// the launched captain is a copy whose auto-approve option is on only for Bypass (so the CLI's own shell and
        /// file tools are never pre-approved for whoever can post to a thread unless an admin chose Bypass, O-02), and
        /// ApproveInArmada sends Claude Code's permission prompts to Armada. Narrations and summaries never prompt
        /// (nobody is waiting for them), so ApproveInArmada runs them as Refuse.
        /// </summary>
        private void ApplyCliPermissions(CaptainChatTurnOptions options, AskThread thread, Captain captain, bool interactive)
        {
            CliPermissionResolution permission = CliPermissionPolicyResolver.ResolveForAsk(_Settings, thread, captain, _Tokens != null);
            CliPermissionPolicyEnum effective = permission.Effective;
            if (!interactive && effective == CliPermissionPolicyEnum.ApproveInArmada) effective = CliPermissionPolicyEnum.Refuse;
            options.Captain = CaptainRuntimeOptions.WithEffectiveAutoApprove(captain, effective == CliPermissionPolicyEnum.Bypass);
            options.CliPermissionPolicy = effective;
            options.PermissionPromptTimeoutSeconds = _Settings.Permissions.PromptTimeoutSeconds;

            // API-endpoint captains run their shell tool in-process: their prompts go straight to the service.
            CliPermissionService? permissions = CliPermissions;
            if (permissions != null && effective == CliPermissionPolicyEnum.ApproveInArmada)
            {
                CliPermissionPromptContext context = new CliPermissionPromptContext
                {
                    TenantId = thread.TenantId,
                    UserId = thread.UserId,
                    ThreadId = thread.Id,
                    CaptainId = captain.Id,
                    Runtime = captain.Runtime
                };
                options.PermissionPrompt = (tool, input, cancel) => permissions.PromptAsync(context, tool, input, cancel);
            }
        }

        private async Task CancelPendingCliPermissionsAsync(string threadId)
        {
            CliPermissionService? permissions = CliPermissions;
            if (permissions == null) return;
            try
            {
                await permissions.CancelPendingAsync(threadId, null).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _Logging.Debug(_Header + "could not cancel CLI permission requests of thread " + threadId + ": " + ex.Message);
            }
        }

                private bool TryReserveCaptain(string captainId)
        {
            while (true)
            {
                int current = _CaptainUsage.GetOrAdd(captainId, 0);
                if (current > 0) return false;
                if (_CaptainUsage.TryUpdate(captainId, 1, 0)) return true;
            }
        }

        private void AcquireCaptain(string captainId)
        {
            _CaptainUsage.AddOrUpdate(captainId, 1, (key, value) => value + 1);
        }

        private void ReleaseCaptain(string captainId)
        {
            _CaptainUsage.AddOrUpdate(captainId, 0, (key, value) => value > 0 ? value - 1 : 0);
        }

        private void Release(AskTurnHandle handle)
        {
            _Running.TryRemove(new KeyValuePair<string, AskTurnHandle>(handle.ThreadId, handle));
            try { handle.Cancellation.Dispose(); }
            catch { }
        }

        private void Emit(AskThread thread, string eventType, object payload)
        {
            _Threads.Emit(thread, eventType, payload);
        }

        private static string Speaker(AskMessage message)
        {
            if (message.Role == AskMessageRoleEnum.User) return "User";
            if (message.Role == AskMessageRoleEnum.Assistant) return "Assistant";
            return "Armada";
        }

        private static string Clip(string? text, int max)
        {
            if (String.IsNullOrEmpty(text)) return String.Empty;
            return text.Length <= max ? text : text.Substring(0, max) + "...";
        }

        #endregion
    }
}
