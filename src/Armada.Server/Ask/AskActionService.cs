namespace Armada.Server.Ask
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Core.BlackBox;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Ask;
    using Armada.Core.Settings;
    using SyslogLogging;
    using Voltaic.Core;

    /// <summary>
    /// The Ask Armada approval gate and action executor.
    /// <para>
    /// Every MCP tool handler is registered through <see cref="WrapTool"/>, which keeps the original handler in a
    /// registry and returns a gated handler. A call made with a thread-scoped token (the MCP transport publishes the
    /// token's <c>askThreadId</c> claim on <see cref="RpcCallContext.Current"/>) is a thread call: a tool on the
    /// <see cref="AskToolPolicy"/> read-only allowlist runs normally; any other tool, unless the thread auto-approves,
    /// is not executed and becomes a Pending proposal with a confirm card; with auto-approve it runs immediately and is
    /// recorded as an Executed proposal. Calls without the claim (normal MCP clients, the stdio server) run unchanged.
    /// </para>
    /// <para>
    /// Approved proposals and quick actions execute in-process by invoking the same registered handler under an ambient
    /// caller context for the approving user, so validation and tenant scoping are exactly those of a direct MCP call.
    /// Results post an ActionResult message and start tracking the work the action created.
    /// </para>
    /// </summary>
    /// <remarks>Thread safety: the registry is a concurrent dictionary; decisions use compare-and-set transitions so a
    /// proposal executes at most once even under concurrent approvals.</remarks>
    public class AskActionService
    {
        #region Public-Members

        /// <summary>
        /// Text returned to the captain when its call became a proposal. {0} is the proposal id.
        /// </summary>
        public const string ProposedResultFormat = "Proposed as {0} and waiting for the user's approval in this conversation. Do not retry; tell the user what you proposed.";

        /// <summary>
        /// Called after work is linked to a thread (new or refreshed tracked item), so the work tracker can snapshot it.
        /// </summary>
        public Func<AskTrackedWork, Task>? OnWorkLinked { get; set; } = null;

        /// <summary>
        /// Called after a captain proposal was approved and executed, so the thread's captain can continue the
        /// conversation with a short follow-up turn.
        /// </summary>
        public Func<AskThread, AskActionProposal, Task>? OnProposalApproved { get; set; } = null;

        /// <summary>
        /// Black box service used to link the cards of missions a conversation mentions. Null links none.
        /// </summary>
        public IBlackBoxService? BlackBox { get; set; } = null;

        /// <summary>
        /// Maximum characters of a tool result stored on a proposal. Default 65536, minimum 1024, maximum 1048576.
        /// </summary>
        public int MaxResultChars
        {
            get => _MaxResultChars;
            set => _MaxResultChars = value < 1024 ? 1024 : (value > 1048576 ? 1048576 : value);
        }

        /// <summary>
        /// Names of every registered tool.
        /// </summary>
        public IReadOnlyCollection<string> ToolNames => _Handlers.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();

        #endregion

        #region Private-Members

        private readonly string _Header = "[AskActionService] ";
        private static readonly JsonSerializerOptions _DescribeJsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        private readonly DatabaseDriver _Database;
        private readonly AskThreadService _Threads;
        private readonly ArmadaSettings _Settings;
        private readonly LoggingModule _Logging;
        private readonly ConcurrentDictionary<string, Func<JsonElement?, Task<object>>> _Handlers = new ConcurrentDictionary<string, Func<JsonElement?, Task<object>>>(StringComparer.Ordinal);
        private int _MaxResultChars = 65536;

        // Tools whose approval in Ask is the user's confirmation: the approved proposal runs the dry run, then confirms it
        // with the dry run's token (MISSION_BLACKBOX_PLAN.md section 9 and 10.3).
        private static readonly HashSet<string> _DryRunThenConfirmTools = new HashSet<string>(StringComparer.Ordinal)
        {
            "run_blackbox_recovery",
            "apply_blackbox_recommendation"
        };

        private static readonly JsonSerializerOptions _ResultJson = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        };

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="threads">Thread service.</param>
        /// <param name="settings">Application settings.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public AskActionService(DatabaseDriver database, AskThreadService threads, ArmadaSettings settings, LoggingModule logging)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Threads = threads ?? throw new ArgumentNullException(nameof(threads));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register an MCP tool handler and return the gated handler to expose on the MCP server.
        /// </summary>
        /// <param name="name">Tool name.</param>
        /// <param name="handler">The tool's handler.</param>
        /// <returns>The gated handler.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public Func<JsonElement?, Task<object>> WrapTool(string name, Func<JsonElement?, Task<object>> handler)
        {
            if (String.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            _Handlers[name] = handler;
            return (JsonElement? args) => HandleCallAsync(name, args, handler);
        }

        /// <summary>
        /// Whether a tool is registered.
        /// </summary>
        /// <param name="name">Tool name.</param>
        /// <returns>True when registered.</returns>
        public bool HasTool(string? name)
        {
            return !String.IsNullOrEmpty(name) && _Handlers.ContainsKey(name!);
        }

        /// <summary>
        /// Execute a registered tool in-process through its MCP handler, as the given caller (same validation and tenant
        /// scoping as a direct MCP call by that caller). The call is never gated.
        /// </summary>
        /// <param name="toolName">Tool name.</param>
        /// <param name="argumentsJson">Arguments as a JSON object text, or null.</param>
        /// <param name="caller">Caller identity.</param>
        /// <returns>The outcome.</returns>
        /// <exception cref="ArgumentException">Thrown when the tool is not registered or the arguments are not a JSON object.</exception>
        public async Task<AskExecutionOutcome> ExecuteAsync(string toolName, string? argumentsJson, AuthContext caller)
        {
            if (caller == null) throw new ArgumentNullException(nameof(caller));
            if (!_Handlers.TryGetValue(toolName ?? String.Empty, out Func<JsonElement?, Task<object>>? handler))
                throw new ArgumentException("Unknown tool: " + toolName, nameof(toolName));

            JsonElement? args = ParseArguments(argumentsJson);
            Dictionary<string, string> claims = new Dictionary<string, string>
            {
                ["tenantId"] = caller.TenantId ?? String.Empty,
                ["userId"] = caller.UserId ?? String.Empty,
                ["isAdmin"] = caller.IsAdmin ? "true" : "false",
                ["isTenantAdmin"] = caller.IsTenantAdmin ? "true" : "false",
                ["authMethod"] = "AskThread"
            };

            using (RpcCallContext.Push(new RpcCallContext(caller.UserId, claims)))
            {
                return await InvokeAsync(handler, args).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Run a quick action in one of the caller's threads. The form submission is the confirmation, so the action is
        /// recorded as a proposal already approved by the caller (source QuickAction) and executes immediately through
        /// the tool's MCP handler; the outcome is posted as an ActionResult message and created work is tracked.
        /// </summary>
        /// <param name="auth">Caller.</param>
        /// <param name="threadId">Thread identifier.</param>
        /// <param name="request">Tool name and arguments.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The decision: 200 with the executed (or failed) proposal, or the Pending proposal when the request has
        /// Propose set; 400 for an unknown tool; 404 when the thread is not found.</returns>
        public async Task<AskProposalDecision> SubmitQuickActionAsync(AuthContext auth, string threadId, AskActionRequest? request, CancellationToken token = default)
        {
            AskThread? thread = await _Threads.GetThreadAsync(auth, threadId, token).ConfigureAwait(false);
            if (thread == null) return new AskProposalDecision(null, 404, "Thread not found");
            if (request == null || String.IsNullOrWhiteSpace(request.ToolName)) return new AskProposalDecision(null, 400, "ToolName is required");
            CollectionBounds.Validate(request);

            string tool = AskToolPolicy.NormalizeToolName(request.ToolName);
            if (!HasTool(tool)) return new AskProposalDecision(null, 400, "Unknown tool: " + tool);

            JsonObject? toolArguments = AskQuickActionCatalog.ToolArguments(tool, request.Arguments);
            string argumentsText = toolArguments != null ? toolArguments.ToJsonString() : "{}";
            AskActionProposal proposal = new AskActionProposal();
            proposal.ToolName = tool;
            proposal.ArgumentsText = argumentsText;
            proposal.SummaryText = await DescribeAsync(thread, tool, argumentsText, token).ConfigureAwait(false);
            proposal.Source = AskProposalSourceEnum.QuickAction;

            if (request.Propose)
            {
                // Proposed from a card button (Fix it, Apply): wait for the user's approval like a captain proposal.
                proposal.Status = AskProposalStatusEnum.Pending;
                proposal = await _Threads.CreateProposalAsync(thread, proposal, true, token).ConfigureAwait(false);
                _Logging.Info(_Header + "thread " + thread.Id + ": " + tool + " proposed from a card as " + proposal.Id);
                return new AskProposalDecision(proposal, 200, null);
            }

            proposal.Status = AskProposalStatusEnum.Approved;
            proposal.DecidedByUserId = auth.UserId;
            proposal.DecidedUtc = DateTime.UtcNow;
            proposal = await _Threads.CreateProposalAsync(thread, proposal, false, token).ConfigureAwait(false);

            proposal = await ExecuteProposalAsync(thread, proposal, auth, token).ConfigureAwait(false);
            return new AskProposalDecision(proposal, 200, null);
        }

        /// <summary>
        /// Approve a pending proposal of one of the caller's threads and execute it through the tool's MCP handler.
        /// </summary>
        /// <param name="auth">Caller (the thread owner).</param>
        /// <param name="threadId">Thread identifier.</param>
        /// <param name="proposalId">Proposal identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The decision: 200 with the executed (or failed) proposal, 404 when not found, 409 when the proposal is
        /// no longer pending (including when it has just expired).</returns>
        public async Task<AskProposalDecision> ApproveAsync(AuthContext auth, string threadId, string proposalId, CancellationToken token = default)
        {
            AskThread? thread = await _Threads.GetThreadAsync(auth, threadId, token).ConfigureAwait(false);
            if (thread == null) return new AskProposalDecision(null, 404, "Thread not found");
            AskActionProposal? proposal = await ReadThreadProposalAsync(thread, proposalId, token).ConfigureAwait(false);
            if (proposal == null) return new AskProposalDecision(null, 404, "Proposal not found");

            if (proposal.Status == AskProposalStatusEnum.Pending && IsExpired(proposal))
            {
                await ExpireAsync(thread, proposal, token).ConfigureAwait(false);
                proposal = await ReadThreadProposalAsync(thread, proposalId, token).ConfigureAwait(false);
                return new AskProposalDecision(proposal, 409, "Proposal expired");
            }

            bool won = await _Database.AskActionProposals.TryTransitionAsync(thread.TenantId!, proposal.Id, AskProposalStatusEnum.Pending, AskProposalStatusEnum.Approved, auth.UserId, token).ConfigureAwait(false);
            if (!won)
            {
                proposal = await ReadThreadProposalAsync(thread, proposalId, token).ConfigureAwait(false);
                return new AskProposalDecision(proposal, 409, "Proposal is not pending (status " + proposal?.Status + ")");
            }

            proposal = (await ReadThreadProposalAsync(thread, proposalId, token).ConfigureAwait(false))!;
            _Threads.AnnounceProposal(thread, proposal);
            proposal = await ExecuteProposalAsync(thread, proposal, auth, token).ConfigureAwait(false);

            Func<AskThread, AskActionProposal, Task>? followUp = OnProposalApproved;
            if (followUp != null && proposal.Source == AskProposalSourceEnum.Captain)
            {
                try { await followUp(thread, proposal).ConfigureAwait(false); }
                catch (Exception ex) { _Logging.Warn(_Header + "follow-up turn for " + proposal.Id + " failed to start: " + ex.Message); }
            }

            return new AskProposalDecision(proposal, 200, null);
        }

        /// <summary>
        /// Reject a pending proposal of one of the caller's threads (it never executes).
        /// </summary>
        /// <param name="auth">Caller (the thread owner).</param>
        /// <param name="threadId">Thread identifier.</param>
        /// <param name="proposalId">Proposal identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The decision: 200 with the rejected proposal, 404 when not found, 409 when not pending.</returns>
        public async Task<AskProposalDecision> RejectAsync(AuthContext auth, string threadId, string proposalId, CancellationToken token = default)
        {
            AskThread? thread = await _Threads.GetThreadAsync(auth, threadId, token).ConfigureAwait(false);
            if (thread == null) return new AskProposalDecision(null, 404, "Thread not found");
            AskActionProposal? proposal = await ReadThreadProposalAsync(thread, proposalId, token).ConfigureAwait(false);
            if (proposal == null) return new AskProposalDecision(null, 404, "Proposal not found");

            bool won = await _Database.AskActionProposals.TryTransitionAsync(thread.TenantId!, proposal.Id, AskProposalStatusEnum.Pending, AskProposalStatusEnum.Rejected, auth.UserId, token).ConfigureAwait(false);
            proposal = await ReadThreadProposalAsync(thread, proposalId, token).ConfigureAwait(false);
            if (!won) return new AskProposalDecision(proposal, 409, "Proposal is not pending (status " + proposal?.Status + ")");

            _Threads.AnnounceProposal(thread, proposal!);
            await PostCardUpdateAsync(thread, proposal!, token).ConfigureAwait(false);
            return new AskProposalDecision(proposal, 200, null);
        }

        /// <summary>
        /// Link the black box cards of the missions a conversation message mentions (plan 10.3): each mentioned mission
        /// whose current black box entry the thread's owner can see gets a BlackBox card, once. Errors are logged, never
        /// thrown.
        /// </summary>
        /// <param name="thread">Thread.</param>
        /// <param name="text">Message text.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Number of cards linked.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="thread"/> is null.</exception>
        public async Task<int> LinkMentionedBlackBoxesAsync(AskThread thread, string? text, CancellationToken token = default)
        {
            if (thread == null) throw new ArgumentNullException(nameof(thread));
            IBlackBoxService? service = BlackBox;
            if (service == null) return 0;
            List<string> missionIds = AskWorkLinker.MentionedMissionIds(text);
            if (missionIds.Count < 1) return 0;

            int linked = 0;
            try
            {
                AuthContext? owner = await _Threads.ReadOwnerContextAsync(thread, token).ConfigureAwait(false);
                if (owner == null) return 0;
                foreach (string missionId in missionIds)
                {
                    BlackBoxEntry entry;
                    try
                    {
                        entry = await service.GetMissionEntryAsync(owner, missionId, false, token).ConfigureAwait(false);
                    }
                    catch (BlackBoxException)
                    {
                        // No entry, or one the owner cannot see: nothing to link.
                        continue;
                    }

                    AskWorkLink link = new AskWorkLink(AskTrackedEntityTypeEnum.BlackBox, entry.Id, false);
                    string title = String.IsNullOrWhiteSpace(entry.MissionTitle) ? String.Empty : " (\"" + entry.MissionTitle + "\")";
                    if (await LinkCardAsync(thread, link, "Black box of mission " + missionId + title + ".", token).ConfigureAwait(false)) linked++;
                }
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                _Logging.Warn(_Header + "black box links for thread " + thread.Id + " failed: " + ex.Message);
            }

            return linked;
        }

        /// <summary>
        /// Expire every pending proposal older than Ask.ProposalExpiryMinutes (it never executes).
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Number of proposals expired.</returns>
        public async Task<int> ExpireDueAsync(CancellationToken token = default)
        {
            DateTime cutoff = DateTime.UtcNow.AddMinutes(-_Settings.Ask.ProposalExpiryMinutes);
            List<AskActionProposal> due = await _Database.AskActionProposals.EnumeratePendingBeforeAsync(cutoff, token).ConfigureAwait(false);
            int expired = 0;
            foreach (AskActionProposal proposal in due)
            {
                token.ThrowIfCancellationRequested();
                AskThread? thread = await _Threads.ReadThreadInternalAsync(proposal.ThreadId, token).ConfigureAwait(false);
                if (thread == null) continue;
                if (await ExpireAsync(thread, proposal, token).ConfigureAwait(false)) expired++;
            }

            return expired;
        }

        /// <summary>
        /// One-line human description of a tool call, used as the proposal summary.
        /// </summary>
        /// <param name="toolName">Tool name.</param>
        /// <param name="argumentsJson">Arguments JSON text.</param>
        /// <returns>The description.</returns>
        public static string Describe(string toolName, string? argumentsJson)
        {
            return Describe(toolName, argumentsJson, null);
        }

        /// <summary>
        /// One-line human description of a tool call, used as the proposal summary, with vessel ids shown as vessel
        /// names where the lookup knows them.
        /// </summary>
        /// <param name="toolName">Tool name.</param>
        /// <param name="argumentsJson">Arguments JSON text.</param>
        /// <param name="vesselNames">Vessel id to name lookup, or null to show ids.</param>
        /// <returns>The description.</returns>
        public static string Describe(string toolName, string? argumentsJson, IReadOnlyDictionary<string, string>? vesselNames)
        {
            JsonObject? args = null;
            try { args = String.IsNullOrWhiteSpace(argumentsJson) ? null : JsonNode.Parse(argumentsJson) as JsonObject; }
            catch (JsonException) { args = null; }

            string Str(string key)
            {
                if (args == null) return String.Empty;
                JsonNode? node = args.FirstOrDefault(kvp => String.Equals(kvp.Key, key, StringComparison.OrdinalIgnoreCase)).Value;
                return node is JsonValue value ? value.ToString() : String.Empty;
            }

            int Count(string key)
            {
                if (args == null) return 0;
                JsonNode? node = args.FirstOrDefault(kvp => String.Equals(kvp.Key, key, StringComparison.OrdinalIgnoreCase)).Value;
                return node is JsonArray array ? array.Count : 0;
            }

            string Vessel(string key)
            {
                string id = Str(key);
                if (id.Length > 0 && vesselNames != null && vesselNames.TryGetValue(id, out string? name) && !String.IsNullOrWhiteSpace(name)) return name;
                return id;
            }

            string BlackBoxTarget()
            {
                if (Str("entryId").Length > 0) return "entry " + Str("entryId");
                if (Str("missionId").Length > 0) return "mission " + Str("missionId");
                if (Str("voyageId").Length > 0) return "voyage " + Str("voyageId");
                return "the entry";
            }

            string tool = AskToolPolicy.NormalizeToolName(toolName);
            switch (tool)
            {
                case "dispatch":
                    return "Dispatch voyage \"" + Str("title") + "\"" + (Str("vesselId").Length > 0 ? " to vessel " + Vessel("vesselId") : String.Empty) + " with " + Count("missions") + " mission(s)";
                case "cancel_voyage":
                    return "Cancel voyage " + Str("voyageId");
                case "cancel_mission":
                    return "Cancel mission " + Str("missionId");
                case "restart_mission":
                    return "Restart mission " + Str("missionId");
                case "create_mission":
                    return "Create mission \"" + Str("title") + "\"" + (Str("vesselId").Length > 0 ? " on vessel " + Vessel("vesselId") : String.Empty);
                case "run_fleet_action":
                    return "Run fleet action " + (Str("actionId").Length > 0 ? Str("actionId") : "\"" + Str("name") + "\"") + " on " + Count("vesselIds") + " vessel(s)";
                case "cancel_fleet_action_run":
                    return "Cancel fleet action run " + Str("runId");
                case "evaluate_vessel_health":
                    return "Evaluate vessel health" + (Count("vesselIds") > 0 ? " for " + Count("vesselIds") + " vessel(s)" : (Str("fleetId").Length > 0 ? " for fleet " + Str("fleetId") : " for every vessel"));
                case "discover_vessels":
                    return "Discover repositories under " + (Count("roots") + Count("directories")) + " path(s)";
                case "import_vessels":
                    return "Import repositories from batch " + Str("batchId");
                case "status":
                    return "Show Armada status";
                case "get_blackbox":
                    return "Show the black box of " + BlackBoxTarget();
                case "run_blackbox_recovery":
                    return "Run recovery path " + Str("pathId") + " from the black box of " + BlackBoxTarget()
                        + (String.Equals(Str("dryRun"), "true", StringComparison.OrdinalIgnoreCase) ? " (dry run only)" : " (dry run, then confirm)")
                        + (String.Equals(Str("acknowledgeDataLoss"), "true", StringComparison.OrdinalIgnoreCase) ? "; this path risks data loss" : String.Empty);
                case "apply_blackbox_recommendation":
                    return "Apply recommendation " + Str("recommendationId") + " from the black box of " + BlackBoxTarget()
                        + (String.Equals(Str("dryRun"), "true", StringComparison.OrdinalIgnoreCase) ? " (dry run only)" : " (dry run, then apply)");
                case "resolve_blackbox":
                    return "Mark the black box of " + BlackBoxTarget() + " " + Str("resolution");
                case "annotate_blackbox":
                    string body = Str("body");
                    return "Add a note to the black box of " + BlackBoxTarget() + (body.Length > 0 ? ": \"" + (body.Length > 120 ? body.Substring(0, 120) + "..." : body) + "\"" : String.Empty);
            }

            StringBuilder builder = new StringBuilder("Run " + tool);
            if (args != null && args.Count > 0)
            {
                List<string> parts = new List<string>();
                foreach (KeyValuePair<string, JsonNode?> kvp in args)
                {
                    string value = kvp.Value is JsonValue v ? v.ToString() : (kvp.Value is JsonArray a ? "[" + a.Count + " item(s)]" : "{...}");
                    if (value.Length > 40) value = value.Substring(0, 40) + "...";
                    parts.Add(kvp.Key + "=" + value);
                }

                builder.Append(" (" + String.Join(", ", parts) + ")");
            }

            string text = builder.ToString();
            return text.Length > 300 ? text.Substring(0, 300) + "..." : text;
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Describe a tool call with the vessel it targets shown by name (looked up in the thread's tenant).
        /// </summary>
        private async Task<string> DescribeAsync(AskThread thread, string toolName, string argumentsText, CancellationToken token = default)
        {
            Dictionary<string, string> vesselNames = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                VesselReferenceArgs? reference = JsonSerializer.Deserialize<VesselReferenceArgs>(argumentsText, _DescribeJsonOptions);
                string? vesselId = reference?.VesselId;
                if (!String.IsNullOrWhiteSpace(vesselId) && !String.IsNullOrEmpty(thread.TenantId))
                {
                    Vessel? vessel = await _Database.Vessels.ReadAsync(thread.TenantId!, vesselId!, token).ConfigureAwait(false);
                    if (vessel != null) vesselNames[vesselId!] = vessel.Name;
                }
            }
            catch (JsonException)
            {
                // Unparseable arguments: describe with ids.
            }

            return Describe(toolName, argumentsText, vesselNames);
        }

        private async Task<object> HandleCallAsync(string name, JsonElement? args, Func<JsonElement?, Task<object>> handler)
        {
            RpcCallContext? context = RpcCallContext.Current;
            string? threadId = null;
            if (context?.Claims != null) context.Claims.TryGetValue("askThreadId", out threadId);
            if (String.IsNullOrEmpty(threadId)) return await handler(args).ConfigureAwait(false);

            // A thread call. Resolve the thread and check it belongs to the token's user.
            context!.Claims.TryGetValue("tenantId", out string? tenantId);
            context.Claims.TryGetValue("userId", out string? userId);
            AskThread? thread = await _Threads.ReadThreadInternalAsync(threadId!).ConfigureAwait(false);
            if (thread == null
                || !String.Equals(thread.TenantId, tenantId, StringComparison.Ordinal)
                || !String.Equals(thread.UserId, userId, StringComparison.Ordinal))
            {
                return McpToolError.NotFound("The conversation for this session no longer exists.");
            }

            if (AskToolPolicy.IsGateExempt(name)) return await handler(args).ConfigureAwait(false);
            if (AskToolPolicy.IsThreadForbidden(name))
                return McpToolError.Forbidden("Tool " + name + " cannot be used from an Ask conversation; CLI permission decisions, rules, and policies are made by people in Armada.");
            string argumentsText = args.HasValue ? args.Value.GetRawText() : "{}";
            if (AskToolPolicy.IsReadOnly(name))
            {
                object read = await handler(args).ConfigureAwait(false);
                if (AskWorkLinker.LinkedReadTools().Contains(name) && !(read is McpToolError))
                {
                    foreach (AskWorkLink link in AskWorkLinker.Resolve(name, argumentsText, Serialize(read)))
                    {
                        try { await LinkCardAsync(thread, link, "Black box entry " + link.EntityId + ".", CancellationToken.None).ConfigureAwait(false); }
                        catch (Exception ex) when (!(ex is OperationCanceledException)) { _Logging.Warn(_Header + "card link for " + name + " failed: " + ex.Message); }
                    }
                }

                return read;
            }

            AskActionProposal proposal = new AskActionProposal();
            proposal.ToolName = name;
            proposal.ArgumentsText = argumentsText;
            proposal.SummaryText = await DescribeAsync(thread, name, argumentsText).ConfigureAwait(false);
            proposal.Source = AskProposalSourceEnum.Captain;

            if (!thread.AutoApprove)
            {
                proposal.Status = AskProposalStatusEnum.Pending;
                proposal = await _Threads.CreateProposalAsync(thread, proposal, true).ConfigureAwait(false);
                _Logging.Info(_Header + "thread " + thread.Id + ": " + name + " proposed as " + proposal.Id);
                return String.Format(ProposedResultFormat, proposal.Id);
            }

            // Auto-approve: run now (still under the caller's ambient context) and record what ran.
            proposal.Status = AskProposalStatusEnum.Approved;
            proposal.DecidedByUserId = thread.UserId;
            proposal.DecidedUtc = DateTime.UtcNow;
            proposal = await _Threads.CreateProposalAsync(thread, proposal, false).ConfigureAwait(false);
            AskExecutionOutcome outcome = RunsDryRunThenConfirm(name, argumentsText)
                ? await DryRunThenConfirmAsync(argumentsText, a => InvokeAsync(handler, a)).ConfigureAwait(false)
                : await InvokeAsync(handler, args).ConfigureAwait(false);
            await RecordOutcomeAsync(thread, proposal, outcome, CancellationToken.None).ConfigureAwait(false);
            if (outcome.Result != null) return outcome.Result;
            return new McpToolError(outcome.ErrorCode ?? McpToolErrorCodeEnum.Failed, outcome.ErrorText ?? "The tool failed.");
        }

        private async Task<AskActionProposal> ExecuteProposalAsync(AskThread thread, AskActionProposal proposal, AuthContext caller, CancellationToken token)
        {
            AskExecutionOutcome outcome;
            try
            {
                outcome = RunsDryRunThenConfirm(proposal.ToolName, proposal.ArgumentsText)
                    ? await DryRunThenConfirmAsync(proposal.ArgumentsText, a => ExecuteAsync(proposal.ToolName, ArgumentsText(a), caller)).ConfigureAwait(false)
                    : await ExecuteAsync(proposal.ToolName, proposal.ArgumentsText, caller).ConfigureAwait(false);
            }
            catch (ArgumentException ex)
            {
                outcome = new AskExecutionOutcome { Ok = false, ErrorText = ex.Message };
            }

            return await RecordOutcomeAsync(thread, proposal, outcome, token).ConfigureAwait(false);
        }

        private async Task<AskActionProposal> RecordOutcomeAsync(AskThread thread, AskActionProposal proposal, AskExecutionOutcome outcome, CancellationToken token)
        {
            proposal.Status = outcome.Ok ? AskProposalStatusEnum.Executed : AskProposalStatusEnum.Failed;
            proposal.ResultText = outcome.ResultText;
            proposal.ErrorText = outcome.ErrorText;
            proposal.ExecutedUtc = DateTime.UtcNow;
            proposal = await _Threads.SaveProposalAsync(thread, proposal, token).ConfigureAwait(false);

            // Link the work the action created (or refresh the work it affected).
            AskTrackedWork? primary = null;
            if (outcome.Ok) primary = await LinkAsync(thread, proposal.ToolName, proposal.ArgumentsText, proposal.ResultText, proposal.Id, token).ConfigureAwait(false);

            AskMessage result = new AskMessage();
            result.Role = AskMessageRoleEnum.System;
            result.Kind = outcome.Ok ? AskMessageKindEnum.ActionResult : AskMessageKindEnum.Error;
            result.ContentText = outcome.Ok
                ? (proposal.SummaryText + ": done.")
                : (proposal.SummaryText + ": failed. " + (outcome.ErrorText ?? String.Empty)).Trim();
            if (!outcome.Ok && outcome.ErrorCode == McpToolErrorCodeEnum.NotFound && String.Equals(proposal.ToolName, "get_blackbox", StringComparison.Ordinal))
                result.ContentText += " The mission has no black box entry; ask the captain for the mission's FailureReason (mission_status) instead.";
            result.ProposalId = proposal.Id;
            result.TrackedWorkId = primary?.Id;
            await _Threads.AppendMessageAsync(thread, result, true, null, token).ConfigureAwait(false);
            return proposal;
        }

        private async Task<AskTrackedWork?> LinkAsync(AskThread thread, string toolName, string? argumentsText, string? resultText, string context, CancellationToken token)
        {
            AskTrackedWork? primary = null;
            foreach (AskWorkLink link in AskWorkLinker.Resolve(toolName, argumentsText, resultText))
            {
                try
                {
                    AskTrackedWork? work = link.RefreshOnly
                        ? await _Threads.FindTrackedWorkAsync(thread, link, token).ConfigureAwait(false)
                        : await _Threads.TrackWorkAsync(thread, link, token).ConfigureAwait(false);
                    if (work == null) continue;
                    if (primary == null && !link.RefreshOnly) primary = work;
                    Func<AskTrackedWork, Task>? linked = OnWorkLinked;
                    if (linked != null) await linked(work).ConfigureAwait(false);
                }
                catch (Exception ex) when (!(ex is OperationCanceledException))
                {
                    _Logging.Warn(_Header + "work linking for " + context + " failed: " + ex.Message);
                }
            }

            return primary;
        }

        /// <summary>
        /// Link a card that no action created (a read, or a mission the conversation mentioned): track it once and post a
        /// system message that hosts the card in the conversation. Returns false when the thread already tracks it.
        /// </summary>
        private async Task<bool> LinkCardAsync(AskThread thread, AskWorkLink link, string text, CancellationToken token)
        {
            if (await _Threads.FindTrackedWorkAsync(thread, link, token).ConfigureAwait(false) != null) return false;
            AskTrackedWork work = await _Threads.TrackWorkAsync(thread, link, token).ConfigureAwait(false);
            AskMessage card = new AskMessage();
            card.Role = AskMessageRoleEnum.System;
            card.Kind = AskMessageKindEnum.WorkUpdate;
            card.ContentText = text;
            card.TrackedWorkId = work.Id;
            await _Threads.AppendMessageAsync(thread, card, false, null, token).ConfigureAwait(false);
            Func<AskTrackedWork, Task>? linked = OnWorkLinked;
            if (linked != null) await linked(work).ConfigureAwait(false);
            return true;
        }

        private static bool RunsDryRunThenConfirm(string? toolName, string? argumentsText)
        {
            if (!_DryRunThenConfirmTools.Contains(AskToolPolicy.NormalizeToolName(toolName))) return false;
            BlackBoxConfirmArgs confirm = new BlackBoxConfirmArgs();
            try
            {
                if (!String.IsNullOrWhiteSpace(argumentsText)) confirm = JsonSerializer.Deserialize<BlackBoxConfirmArgs>(argumentsText, _DescribeJsonOptions) ?? new BlackBoxConfirmArgs();
            }
            catch (JsonException)
            {
                return false;
            }

            // An explicit dry run stays a dry run, and a proposal that already carries a token runs as proposed.
            return confirm.DryRun != true && String.IsNullOrWhiteSpace(confirm.ConfirmToken);
        }

        /// <summary>
        /// Run an approved black box recovery or recommendation the way the tool asks: a dry run first, then the run with
        /// the dry run's confirmation token. The user's approval of the proposal is the confirmation.
        /// </summary>
        private async Task<AskExecutionOutcome> DryRunThenConfirmAsync(string? argumentsText, Func<JsonElement?, Task<AskExecutionOutcome>> invoke)
        {
            JsonObject arguments = ParseObject(argumentsText);
            arguments["dryRun"] = true;
            arguments.Remove("confirmToken");
            AskExecutionOutcome dry = await invoke(ParseArguments(arguments.ToJsonString())).ConfigureAwait(false);
            if (!dry.Ok) return dry;

            string? confirmToken = null;
            if (dry.Result is BlackBoxRecoveryRunResult recovery) confirmToken = recovery.ConfirmToken;
            else if (dry.Result is BlackBoxRecommendationApplyResult recommendation) confirmToken = recommendation.ConfirmToken;
            if (String.IsNullOrWhiteSpace(confirmToken))
            {
                dry.Ok = false;
                dry.ErrorText = "The dry run returned no confirmation token, so nothing was run.";
                return dry;
            }

            arguments["dryRun"] = false;
            arguments["confirmToken"] = confirmToken;
            AskExecutionOutcome run = await invoke(ParseArguments(arguments.ToJsonString())).ConfigureAwait(false);
            if (!run.Ok) return run;

            // A run whose step failed is a failed action, not a successful call.
            if (run.Result is BlackBoxRecoveryRunResult ran && !ran.Succeeded)
            {
                run.Ok = false;
                run.ErrorText = String.IsNullOrWhiteSpace(ran.Message) ? "A recovery step failed." : ran.Message;
            }
            else if (run.Result is BlackBoxRecommendationApplyResult applied && !applied.Succeeded)
            {
                run.Ok = false;
                run.ErrorText = String.IsNullOrWhiteSpace(applied.Message) ? "The recommendation could not be applied." : applied.Message;
            }

            return run;
        }

        private static JsonObject ParseObject(string? argumentsText)
        {
            if (String.IsNullOrWhiteSpace(argumentsText)) return new JsonObject();
            try { return JsonNode.Parse(argumentsText) as JsonObject ?? new JsonObject(); }
            catch (JsonException) { return new JsonObject(); }
        }

        private static string ArgumentsText(JsonElement? args)
        {
            return args.HasValue ? args.Value.GetRawText() : "{}";
        }

        private async Task<AskExecutionOutcome> InvokeAsync(Func<JsonElement?, Task<object>> handler, JsonElement? args)
        {
            AskExecutionOutcome outcome = new AskExecutionOutcome();
            try
            {
                object result = await handler(args).ConfigureAwait(false);
                outcome.Result = result;
                outcome.ResultText = Truncate(Serialize(result));

                // Success is decided from the typed result object before it is serialized (and possibly truncated): a
                // tool reports failure by returning McpToolError, never by a property name in its serialized text.
                if (result is McpToolError toolError)
                {
                    outcome.Ok = false;
                    outcome.ErrorCode = toolError.ErrorCode;
                    outcome.ErrorText = String.IsNullOrWhiteSpace(toolError.Error) ? toolError.ErrorCode.ToString() : toolError.Error;
                }
                else
                {
                    outcome.Ok = true;
                }
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                outcome.Ok = false;
                outcome.ErrorText = ex.Message;
            }

            return outcome;
        }

        private async Task<bool> ExpireAsync(AskThread thread, AskActionProposal proposal, CancellationToken token)
        {
            bool won = await _Database.AskActionProposals.TryTransitionAsync(thread.TenantId!, proposal.Id, AskProposalStatusEnum.Pending, AskProposalStatusEnum.Expired, null, token).ConfigureAwait(false);
            if (!won) return false;
            AskActionProposal? expired = await _Database.AskActionProposals.ReadAsync(thread.TenantId!, proposal.Id, token).ConfigureAwait(false);
            if (expired != null)
            {
                _Threads.AnnounceProposal(thread, expired);
                await PostCardUpdateAsync(thread, expired, token).ConfigureAwait(false);
            }

            return true;
        }

        private async Task PostCardUpdateAsync(AskThread thread, AskActionProposal proposal, CancellationToken token)
        {
            // Re-announce the confirm card's message so open conversations re-render it with the decided status.
            if (String.IsNullOrEmpty(proposal.MessageId)) return;
            AskMessage? card = await _Database.AskMessages.ReadAsync(thread.TenantId!, proposal.MessageId!, token).ConfigureAwait(false);
            if (card == null) return;
            _Threads.Decorate(proposal);
            card.Proposal = proposal;
            _Threads.Emit(thread, "ask.message", new { threadId = thread.Id, message = card });
        }

        private async Task<AskActionProposal?> ReadThreadProposalAsync(AskThread thread, string proposalId, CancellationToken token)
        {
            if (String.IsNullOrWhiteSpace(proposalId)) return null;
            AskActionProposal? proposal = await _Database.AskActionProposals.ReadAsync(thread.TenantId!, proposalId, token).ConfigureAwait(false);
            if (proposal == null || !String.Equals(proposal.ThreadId, thread.Id, StringComparison.Ordinal)) return null;
            _Threads.Decorate(proposal);
            return proposal;
        }

        private bool IsExpired(AskActionProposal proposal)
        {
            return proposal.CreatedUtc.AddMinutes(_Settings.Ask.ProposalExpiryMinutes) < DateTime.UtcNow;
        }

        private static JsonElement? ParseArguments(string? argumentsJson)
        {
            if (String.IsNullOrWhiteSpace(argumentsJson)) return JsonDocument.Parse("{}").RootElement.Clone();
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(argumentsJson))
                {
                    if (!argumentsJson.TrimStart().StartsWith("{", StringComparison.Ordinal)) throw new ArgumentException("Tool arguments must be a JSON object.");
                    return doc.RootElement.Clone();
                }
            }
            catch (JsonException ex)
            {
                throw new ArgumentException("Tool arguments are not valid JSON: " + ex.Message);
            }
        }

        private static string Serialize(object? result)
        {
            if (result == null) return "null";
            if (result is string text) return JsonSerializer.Serialize(text);
            try { return JsonSerializer.Serialize(result, result.GetType(), _ResultJson); }
            catch (Exception) { return JsonSerializer.Serialize(result.ToString()); }
        }

        private string? Truncate(string? value)
        {
            if (value == null || value.Length <= _MaxResultChars) return value;
            return value.Substring(0, _MaxResultChars) + "... (truncated)";
        }

        #endregion
    }
}
