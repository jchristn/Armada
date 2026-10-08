namespace Armada.Core.Services
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services.Ask;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// CLI tool permissions: answers the permission prompts CLI captains raise for their own tools (through the
    /// <c>cli_permission_prompt</c> MCP tool), applies remembered rules, holds a prompt until an approver decides or it
    /// expires, and manages rules and the captain and Ask thread policy overrides. Errors are typed:
    /// <see cref="KeyNotFoundException"/> for a missing or invisible entity, <see cref="UnauthorizedAccessException"/>
    /// for a caller who may not act, <see cref="ArgumentException"/> for bad input, and
    /// <see cref="InvalidOperationException"/> for a request that is no longer pending.
    /// </summary>
    /// <remarks>Thread safety: waiting prompts are tracked in a concurrent dictionary and every decision is a database
    /// compare-and-set, so concurrent decisions, expiry, and cancellation are mutually exclusive.</remarks>
    public class CliPermissionService
    {
        #region Public-Members

        /// <summary>
        /// Event type announced when a request starts waiting for an approver.
        /// </summary>
        public const string RequestedEvent = "cli_permission.requested";

        /// <summary>
        /// Event type announced when a waiting request is decided, expires, or is cancelled.
        /// </summary>
        public const string ResolvedEvent = "cli_permission.resolved";

        /// <summary>
        /// Called with an event type (<see cref="RequestedEvent"/> or <see cref="ResolvedEvent"/>) and the request; the
        /// Admiral delivers it to the request's approvers and owner.
        /// </summary>
        public Action<string, CliPermissionRequest>? OnRequestEvent { get; set; } = null;

        /// <summary>
        /// Thread service used to post the permission card into an Ask thread, or null.
        /// </summary>
        public AskThreadService? AskThreads { get; set; } = null;

        /// <summary>
        /// How long a prompt waits for a decision instead of Permissions.PromptTimeoutSeconds, or null (the setting).
        /// Lets hosts and tests use a shorter wait than the setting's 10-second minimum.
        /// </summary>
        public TimeSpan? PromptTimeoutOverride { get; set; } = null;

        /// <summary>
        /// Number of prompts currently waiting in this process.
        /// </summary>
        public int WaitingCount => _Waiters.Count;

        /// <summary>
        /// A prompt whose request and Ask card took longer than this to set up is logged as a warning with its step
        /// timings, so a slow store or card post shows up in the log.
        /// </summary>
        public static readonly TimeSpan SlowSetupThreshold = TimeSpan.FromSeconds(5);

        #endregion

        #region Private-Members

        private readonly string _Header = "[CliPermissionService] ";
        private readonly DatabaseDriver _Database;
        private readonly ArmadaSettings _Settings;
        private readonly LoggingModule _Logging;
        private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> _Waiters = new ConcurrentDictionary<string, TaskCompletionSource<bool>>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, CliPermissionPromptProgress> _InFlight = new ConcurrentDictionary<string, CliPermissionPromptProgress>(StringComparer.Ordinal);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="settings">Admiral settings (Permissions section, read live).</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public CliPermissionService(DatabaseDriver database, ArmadaSettings settings, LoggingModule logging)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Answer one permission prompt: a matching deny rule denies and a matching allow rule allows immediately;
        /// otherwise a pending request is stored, announced, and held until an approver decides, the prompt times out
        /// (Permissions.PromptTimeoutSeconds), or <paramref name="token"/> is cancelled. Every prompt is recorded.
        /// </summary>
        /// <param name="context">The captain session (resolved from its scoped token).</param>
        /// <param name="toolName">Tool name as the CLI reported it.</param>
        /// <param name="inputJson">Tool input JSON object text (never persisted unredacted).</param>
        /// <param name="token">Cancellation token (the MCP call).</param>
        /// <returns>The outcome; never null.</returns>
        /// <exception cref="ArgumentException">Thrown when the tool name is empty.</exception>
        public async Task<CliPermissionPromptOutcome> PromptAsync(CliPermissionPromptContext context, string toolName, string? inputJson, CancellationToken token = default)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (String.IsNullOrWhiteSpace(toolName)) throw new ArgumentException("tool_name is required.", nameof(toolName));
            string trimmedTool = toolName.Trim();
            if (trimmedTool.Length > 256) throw new ArgumentException("tool_name is too long.", nameof(toolName));
            string input = String.IsNullOrWhiteSpace(inputJson) ? "{}" : inputJson!;

            CliPermissionRequest request = new CliPermissionRequest();
            request.TenantId = context.TenantId;
            request.UserId = context.UserId;
            request.CaptainId = context.CaptainId;
            request.MissionId = context.MissionId;
            request.VoyageId = context.VoyageId;
            request.VesselId = context.VesselId;
            request.ThreadId = context.ThreadId;
            request.Runtime = context.Runtime;
            request.ToolName = trimmedTool;
            request.InputText = Clip(SecretRedactor.Redact(input), 16000);
            request.SummaryText = SecretRedactor.Redact(CliPermissionRuleMatcher.Summarize(trimmedTool, input));
            request.SuggestedRule = SecretRedactor.Redact(CliPermissionRuleMatcher.SuggestRule(trimmedTool, input));
            request.CreatedUtc = DateTime.UtcNow;
            TimeSpan timeout = PromptTimeoutOverride ?? TimeSpan.FromSeconds(_Settings.Permissions.PromptTimeoutSeconds);
            request.ExpiresUtc = request.CreatedUtc.Add(timeout);

            CliPermissionPromptProgress progress = new CliPermissionPromptProgress
            {
                RequestId = request.Id,
                ThreadId = request.ThreadId,
                MissionId = request.MissionId,
                ToolName = trimmedTool,
                StartedUtc = request.CreatedUtc,
                StageStartedUtc = request.CreatedUtc
            };
            _InFlight[request.Id] = progress;
            List<string> timings = new List<string>();
            try
            {
                List<CliPermissionRule> rules = await _Database.CliPermissionRules.EnumerateApplicableAsync(context.TenantId, context.CaptainId, context.VesselId, token).ConfigureAwait(false);
                CliPermissionMatchContext matchContext = new CliPermissionMatchContext
                {
                    WorkingDirectory = context.WorkingDirectory,
                    HomeDirectory = SafeHome()
                };
                CliPermissionRuleEvaluation evaluation = CliPermissionRuleMatcher.Evaluate(rules, trimmedTool, input, matchContext);
                if (evaluation.Action.HasValue)
                {
                    bool allow = evaluation.Action.Value == CliPermissionRuleActionEnum.Allow;
                    request.Status = allow ? CliPermissionRequestStatusEnum.Allowed : CliPermissionRequestStatusEnum.Denied;
                    request.DecisionSource = allow ? CliPermissionDecisionSourceEnum.AllowRule : CliPermissionDecisionSourceEnum.DenyRule;
                    request.RuleId = evaluation.Rule?.Id;
                    request.DecidedUtc = request.CreatedUtc;
                    request.DecisionMessage = allow ? null : "Denied by Armada CLI permission rule " + (evaluation.Rule?.Pattern ?? "") + " (" + (evaluation.Rule?.Id ?? "") + ").";
                    request = await _Database.CliPermissionRequests.CreateAsync(request, token).ConfigureAwait(false);
                    _Logging.Info(_Header + request.ToolName + " for captain " + request.CaptainId + " " + request.Status + " by rule " + request.RuleId);
                    return new CliPermissionPromptOutcome { Allowed = allow, Message = request.DecisionMessage ?? String.Empty, Request = request };
                }

                TaskCompletionSource<bool> waiter = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _Waiters[request.Id] = waiter;
                try
                {
                    Advance(progress, CliPermissionPromptStageEnum.Storing, timings);
                    request = await _Database.CliPermissionRequests.CreateAsync(request, token).ConfigureAwait(false);
                    _Logging.Info(_Header + "waiting for a decision on " + request.Id + " (" + request.ToolName + ") for captain " + request.CaptainId);
                    Advance(progress, CliPermissionPromptStageEnum.PostingCard, timings);
                    await PostCardAsync(request, progress).ConfigureAwait(false);
                    Advance(progress, CliPermissionPromptStageEnum.Announcing, timings);
                    Announce(RequestedEvent, request);
                    Advance(progress, CliPermissionPromptStageEnum.Waiting, timings);
                    TimeSpan setup = DateTime.UtcNow - progress.StartedUtc;
                    if (setup > SlowSetupThreshold)
                        _Logging.Warn(_Header + "slow prompt setup for " + request.Id + " (" + request.ToolName + "): " + (int)setup.TotalMilliseconds + " ms before waiting (" + String.Join(", ", timings) + ")");

                    TimeSpan remaining = request.ExpiresUtc - DateTime.UtcNow;
                    if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
                    Task delay = Task.Delay(remaining, token);
                    Task finished = await Task.WhenAny(waiter.Task, delay).ConfigureAwait(false);
                    Advance(progress, CliPermissionPromptStageEnum.Resolving, timings);
                    if (finished != waiter.Task)
                    {
                        bool cancelled = token.IsCancellationRequested;
                        string message = cancelled
                            ? "The permission request was cancelled before an approver decided."
                            : "No approver decided within " + (int)Math.Ceiling(timeout.TotalSeconds) + " seconds; Armada denied the request. Ask an approver to allow it, or add an allow rule.";
                        await _Database.CliPermissionRequests.TryDecideAsync(
                            request.Id,
                            cancelled ? CliPermissionRequestStatusEnum.Cancelled : CliPermissionRequestStatusEnum.Expired,
                            cancelled ? CliPermissionDecisionSourceEnum.Cancelled : CliPermissionDecisionSourceEnum.Timeout,
                            null, null, message, CancellationToken.None).ConfigureAwait(false);
                    }
                }
                finally
                {
                    _Waiters.TryRemove(request.Id, out TaskCompletionSource<bool>? _);
                }

                CliPermissionRequest final = await _Database.CliPermissionRequests.ReadAsync(request.Id, CancellationToken.None).ConfigureAwait(false) ?? request;
                if (final.Status == CliPermissionRequestStatusEnum.Expired || final.Status == CliPermissionRequestStatusEnum.Cancelled)
                {
                    Announce(ResolvedEvent, final);
                    await RefreshCardAsync(final).ConfigureAwait(false);
                }

                bool allowed = final.Status == CliPermissionRequestStatusEnum.Allowed;
                string answer = allowed ? String.Empty : DenialMessage(final);
                return new CliPermissionPromptOutcome { Allowed = allowed, Message = answer, Request = final };
            }
            finally
            {
                _InFlight.TryRemove(request.Id, out CliPermissionPromptProgress? _);
            }
        }

        /// <summary>
        /// Snapshots of the prompts still running in this process (diagnostics): each one's request, current step, and
        /// how long it has been there, so a prompt that stalls before its request or Ask card appears shows the step it
        /// is stuck in.
        /// </summary>
        /// <returns>Snapshots, oldest first; never null.</returns>
        public List<CliPermissionPromptProgress> GetInFlightPrompts()
        {
            List<CliPermissionPromptProgress> list = new List<CliPermissionPromptProgress>();
            foreach (CliPermissionPromptProgress progress in _InFlight.Values)
            {
                lock (progress) list.Add(progress.Clone());
            }

            return list.OrderBy(p => p.StartedUtc).ToList();
        }

        /// <summary>
        /// Decide a pending request.
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="requestId">Request identifier.</param>
        /// <param name="decision">Decision.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The decided request, decorated for the caller.</returns>
        /// <exception cref="KeyNotFoundException">Thrown when the request does not exist or is not visible.</exception>
        /// <exception cref="UnauthorizedAccessException">Thrown when the caller may not decide (or remember).</exception>
        /// <exception cref="ArgumentException">Thrown for an invalid rule pattern or scope.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the request is no longer pending.</exception>
        public async Task<CliPermissionRequest> DecideAsync(AuthContext caller, string requestId, CliPermissionDecisionRequest decision, CancellationToken token = default)
        {
            if (caller == null) throw new ArgumentNullException(nameof(caller));
            if (decision == null) throw new ArgumentNullException(nameof(decision));
            if (CliPermissionAccess.IsCaptainSession(caller))
                throw new UnauthorizedAccessException("A captain session cannot decide CLI permission requests.");

            CliPermissionRequest request = await ReadVisibleAsync(caller, requestId, token).ConfigureAwait(false);
            if (!CliPermissionAccess.CanDecide(caller, request, _Settings.Permissions))
                throw new UnauthorizedAccessException("Only an admin can decide this CLI permission request" + (_Settings.Permissions.AllowOwnerApproval ? "." : " (Permissions.AllowOwnerApproval is off)."));
            if (request.Status != CliPermissionRequestStatusEnum.Pending)
                throw new InvalidOperationException("The CLI permission request " + request.Id + " is no longer pending (" + request.Status + ").");

            string? ruleId = null;
            if (decision.Decision == CliPermissionDecisionEnum.AllowAndRemember)
            {
                if (!CliPermissionAccess.CanRemember(caller, request))
                    throw new UnauthorizedAccessException("Only an admin can remember a CLI permission decision as a rule.");
                CliPermissionRule rule = new CliPermissionRule();
                rule.Pattern = String.IsNullOrWhiteSpace(decision.RulePattern) ? (request.SuggestedRule ?? request.ToolName) : decision.RulePattern!;
                rule.Action = CliPermissionRuleActionEnum.Allow;
                rule.Scope = decision.RuleScope;
                rule.TenantId = request.TenantId;
                rule.VesselId = decision.RuleScope == CliPermissionRuleScopeEnum.Vessel ? request.VesselId : null;
                rule.CaptainId = decision.RuleScope == CliPermissionRuleScopeEnum.Captain ? request.CaptainId : null;
                rule.Description = "Remembered from " + request.Id;
                rule = await CreateRuleAsync(caller, rule, token).ConfigureAwait(false);
                ruleId = rule.Id;
            }

            bool allow = decision.Decision != CliPermissionDecisionEnum.Deny;
            string? message = String.IsNullOrWhiteSpace(decision.Message) ? null : Clip(decision.Message!.Trim(), 2000);
            bool decided = await _Database.CliPermissionRequests.TryDecideAsync(
                request.Id,
                allow ? CliPermissionRequestStatusEnum.Allowed : CliPermissionRequestStatusEnum.Denied,
                CliPermissionDecisionSourceEnum.Approver,
                ruleId,
                caller.UserId,
                message,
                token).ConfigureAwait(false);
            if (!decided)
            {
                // Lost the race with another decision or the expiry: do not leave a remembered rule behind.
                if (ruleId != null) await _Database.CliPermissionRules.DeleteAsync(ruleId, CancellationToken.None).ConfigureAwait(false);
                throw new InvalidOperationException("The CLI permission request " + request.Id + " is no longer pending.");
            }

            CliPermissionRequest updated = await _Database.CliPermissionRequests.ReadAsync(request.Id, token).ConfigureAwait(false) ?? request;
            _Logging.Info(_Header + request.Id + " (" + request.ToolName + ") " + updated.Status + " by " + caller.UserId + (ruleId != null ? " with rule " + ruleId : ""));
            if (_Waiters.TryGetValue(request.Id, out TaskCompletionSource<bool>? waiter)) waiter.TrySetResult(allow);
            Announce(ResolvedEvent, updated);
            await RefreshCardAsync(updated).ConfigureAwait(false);
            await DecorateAsync(caller, updated, token).ConfigureAwait(false);
            return updated;
        }

        /// <summary>
        /// Read one request visible to the caller.
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="requestId">Request identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The request, decorated for the caller.</returns>
        /// <exception cref="KeyNotFoundException">Thrown when missing or not visible.</exception>
        public async Task<CliPermissionRequest> GetAsync(AuthContext caller, string requestId, CancellationToken token = default)
        {
            CliPermissionRequest request = await ReadVisibleAsync(caller, requestId, token).ConfigureAwait(false);
            await DecorateAsync(caller, request, token).ConfigureAwait(false);
            return request;
        }

        /// <summary>
        /// List requests visible to the caller, newest first.
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="query">Filters (the tenant is narrowed to the caller's for non-admins).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Requests decorated for the caller.</returns>
        public async Task<List<CliPermissionRequest>> ListAsync(AuthContext caller, CliPermissionRequestQuery? query, CancellationToken token = default)
        {
            if (caller == null) throw new ArgumentNullException(nameof(caller));
            if (CliPermissionAccess.IsCaptainSession(caller)) return new List<CliPermissionRequest>();
            CliPermissionRequestQuery effective = query ?? new CliPermissionRequestQuery();
            if (!caller.IsAdmin)
            {
                effective.TenantId = caller.TenantId;
                if (!caller.IsTenantAdmin) effective.UserId = caller.UserId;
            }

            List<CliPermissionRequest> rows = await _Database.CliPermissionRequests.EnumerateAsync(effective, token).ConfigureAwait(false);
            List<CliPermissionRequest> visible = rows.Where(r => CliPermissionAccess.CanView(caller, r)).ToList();
            foreach (CliPermissionRequest request in visible) await DecorateAsync(caller, request, token).ConfigureAwait(false);
            return visible;
        }

        /// <summary>
        /// Fill in names, the thread title (for the owner), and the caller's CanDecide/CanRemember flags.
        /// </summary>
        /// <param name="caller">Caller, or null for no caller-specific flags.</param>
        /// <param name="request">Request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task DecorateAsync(AuthContext? caller, CliPermissionRequest request, CancellationToken token = default)
        {
            if (request == null) return;
            try
            {
                if (!String.IsNullOrEmpty(request.CaptainId))
                    request.CaptainName = (await _Database.Captains.ReadAsync(request.CaptainId!, token).ConfigureAwait(false))?.Name;
                if (!String.IsNullOrEmpty(request.VesselId))
                    request.VesselName = (await _Database.Vessels.ReadAsync(request.VesselId!, token).ConfigureAwait(false))?.Name;
                if (!String.IsNullOrEmpty(request.MissionId))
                    request.MissionTitle = (await _Database.Missions.ReadAsync(request.MissionId!, token).ConfigureAwait(false))?.Title;
                if (!String.IsNullOrEmpty(request.ThreadId) && caller != null && String.Equals(caller.UserId, request.UserId, StringComparison.Ordinal))
                    request.ThreadTitle = (await _Database.AskThreads.ReadByIdAsync(request.ThreadId!, token).ConfigureAwait(false))?.Title;
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                _Logging.Debug(_Header + "could not decorate " + request.Id + ": " + ex.Message);
            }

            request.CanDecide = caller != null && request.Status == CliPermissionRequestStatusEnum.Pending && CliPermissionAccess.CanDecide(caller, request, _Settings.Permissions);
            request.CanRemember = request.CanDecide && caller != null && CliPermissionAccess.CanRemember(caller, request);
        }

        /// <summary>
        /// Cancel the pending requests of an Ask thread or a mission (the turn or mission ended).
        /// </summary>
        /// <param name="threadId">Thread, or null.</param>
        /// <param name="missionId">Mission, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Number of requests cancelled.</returns>
        public async Task<int> CancelPendingAsync(string? threadId, string? missionId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(threadId) && String.IsNullOrEmpty(missionId)) return 0;
            CliPermissionRequestQuery query = new CliPermissionRequestQuery { Status = CliPermissionRequestStatusEnum.Pending, ThreadId = threadId, MissionId = missionId, Limit = 1000 };
            List<CliPermissionRequest> pending = await _Database.CliPermissionRequests.EnumerateAsync(query, token).ConfigureAwait(false);
            int cancelled = 0;
            foreach (CliPermissionRequest request in pending)
            {
                if (await CancelOneAsync(request, "The session ended before an approver decided.").ConfigureAwait(false)) cancelled++;
            }

            return cancelled;
        }

        /// <summary>
        /// Resolve pending requests that no call in this process is waiting for (for example after a restart) or whose
        /// expiry has passed. Run at startup and periodically.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Number of requests resolved.</returns>
        public async Task<int> SweepAsync(CancellationToken token = default)
        {
            CliPermissionRequestQuery query = new CliPermissionRequestQuery { Status = CliPermissionRequestStatusEnum.Pending, Limit = 1000 };
            List<CliPermissionRequest> pending = await _Database.CliPermissionRequests.EnumerateAsync(query, token).ConfigureAwait(false);
            int resolved = 0;
            DateTime now = DateTime.UtcNow;
            foreach (CliPermissionRequest request in pending)
            {
                if (_Waiters.ContainsKey(request.Id))
                {
                    // The waiting call resolves its own expiry; only signal a waiter that outlived its expiry.
                    if (request.ExpiresUtc.AddSeconds(30) < now && _Waiters.TryGetValue(request.Id, out TaskCompletionSource<bool>? stuck))
                    {
                        if (await CancelOneAsync(request, "No approver decided in time; Armada denied the request.", CliPermissionRequestStatusEnum.Expired).ConfigureAwait(false)) resolved++;
                        stuck.TrySetResult(false);
                    }

                    continue;
                }

                if (await CancelOneAsync(request, "The Admiral is no longer waiting for this request (it restarted or the session ended).").ConfigureAwait(false)) resolved++;
            }

            return resolved;
        }

        /// <summary>
        /// List rules visible to the caller (the caller's tenant plus rules for every tenant; a global admin sees all).
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="query">Filters.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rules.</returns>
        public async Task<List<CliPermissionRule>> ListRulesAsync(AuthContext caller, CliPermissionRuleQuery? query, CancellationToken token = default)
        {
            if (caller == null) throw new ArgumentNullException(nameof(caller));
            if (CliPermissionAccess.IsCaptainSession(caller)) return new List<CliPermissionRule>();
            CliPermissionRuleQuery effective = query ?? new CliPermissionRuleQuery();
            if (!caller.IsAdmin) effective.TenantId = caller.TenantId;
            return await _Database.CliPermissionRules.EnumerateAsync(effective, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Read one rule visible to the caller.
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="ruleId">Rule identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The rule.</returns>
        /// <exception cref="KeyNotFoundException">Thrown when missing or not visible.</exception>
        public async Task<CliPermissionRule> GetRuleAsync(AuthContext caller, string ruleId, CancellationToken token = default)
        {
            if (caller == null) throw new ArgumentNullException(nameof(caller));
            if (String.IsNullOrWhiteSpace(ruleId) || CliPermissionAccess.IsCaptainSession(caller)) throw new KeyNotFoundException("CLI permission rule not found.");
            CliPermissionRule? rule = await _Database.CliPermissionRules.ReadAsync(ruleId, token).ConfigureAwait(false);
            if (rule == null) throw new KeyNotFoundException("CLI permission rule not found.");
            if (!caller.IsAdmin && rule.TenantId != null && !String.Equals(rule.TenantId, caller.TenantId, StringComparison.Ordinal))
                throw new KeyNotFoundException("CLI permission rule not found.");
            return rule;
        }

        /// <summary>
        /// Create a rule. Global admins may create rules for any tenant or (with no tenant) for every tenant; tenant
        /// admins create rules for their own tenant.
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="rule">Rule (pattern, action, scope, vessel or captain, description, optional tenant).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created rule.</returns>
        /// <exception cref="UnauthorizedAccessException">Thrown when the caller is not an admin.</exception>
        /// <exception cref="ArgumentException">Thrown for an invalid pattern or a missing scope target id.</exception>
        /// <exception cref="KeyNotFoundException">Thrown when the vessel or captain is not visible to the caller.</exception>
        public async Task<CliPermissionRule> CreateRuleAsync(AuthContext caller, CliPermissionRule rule, CancellationToken token = default)
        {
            if (caller == null) throw new ArgumentNullException(nameof(caller));
            if (rule == null) throw new ArgumentException("A rule is required.", nameof(rule));
            if (CliPermissionAccess.IsCaptainSession(caller) || !(caller.IsAdmin || caller.IsTenantAdmin))
                throw new UnauthorizedAccessException("Only an admin can create CLI permission rules.");

            CliPermissionRule created = new CliPermissionRule();
            created.Pattern = CliPermissionRuleParser.Parse(rule.Pattern).Raw;
            created.Action = rule.Action;
            created.Scope = rule.Scope;
            created.Description = String.IsNullOrWhiteSpace(rule.Description) ? null : Clip(rule.Description!.Trim(), 1000);
            created.CreatedByUserId = caller.UserId;
            created.TenantId = caller.IsAdmin ? (String.IsNullOrWhiteSpace(rule.TenantId) ? null : rule.TenantId) : caller.TenantId;

            if (created.Scope == CliPermissionRuleScopeEnum.Vessel)
            {
                if (String.IsNullOrWhiteSpace(rule.VesselId)) throw new ArgumentException("A Vessel rule requires vesselId.", nameof(rule));
                Vessel? vessel = await _Database.Vessels.ReadAsync(rule.VesselId!, token).ConfigureAwait(false);
                if (vessel == null || (!caller.IsAdmin && !String.Equals(vessel.TenantId, caller.TenantId, StringComparison.Ordinal)))
                    throw new KeyNotFoundException("Vessel " + rule.VesselId + " was not found.");
                created.VesselId = vessel.Id;
                created.TenantId = vessel.TenantId;
            }
            else if (created.Scope == CliPermissionRuleScopeEnum.Captain)
            {
                if (String.IsNullOrWhiteSpace(rule.CaptainId)) throw new ArgumentException("A Captain rule requires captainId.", nameof(rule));
                Captain? captain = await _Database.Captains.ReadAsync(rule.CaptainId!, token).ConfigureAwait(false);
                if (captain == null || (!caller.IsAdmin && !String.Equals(captain.TenantId, caller.TenantId, StringComparison.Ordinal)))
                    throw new KeyNotFoundException("Captain " + rule.CaptainId + " was not found.");
                created.CaptainId = captain.Id;
                created.TenantId = captain.TenantId;
            }

            created = await _Database.CliPermissionRules.CreateAsync(created, token).ConfigureAwait(false);
            _Logging.Info(_Header + "rule " + created.Id + " " + created.Action + " " + created.Pattern + " (" + created.Scope + ") created by " + caller.UserId);
            return created;
        }

        /// <summary>
        /// Update a rule's pattern, action, and description.
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="ruleId">Rule identifier.</param>
        /// <param name="update">New values.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated rule.</returns>
        public async Task<CliPermissionRule> UpdateRuleAsync(AuthContext caller, string ruleId, CliPermissionRule update, CancellationToken token = default)
        {
            if (update == null) throw new ArgumentException("A rule is required.", nameof(update));
            CliPermissionRule rule = await GetRuleAsync(caller, ruleId, token).ConfigureAwait(false);
            RequireRuleAdmin(caller, rule);
            rule.Pattern = CliPermissionRuleParser.Parse(update.Pattern).Raw;
            rule.Action = update.Action;
            rule.Description = String.IsNullOrWhiteSpace(update.Description) ? null : Clip(update.Description!.Trim(), 1000);
            return await _Database.CliPermissionRules.UpdateAsync(rule, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Delete a rule.
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="ruleId">Rule identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task DeleteRuleAsync(AuthContext caller, string ruleId, CancellationToken token = default)
        {
            CliPermissionRule rule = await GetRuleAsync(caller, ruleId, token).ConfigureAwait(false);
            RequireRuleAdmin(caller, rule);
            await _Database.CliPermissionRules.DeleteAsync(rule.Id, token).ConfigureAwait(false);
            _Logging.Info(_Header + "rule " + rule.Id + " deleted by " + caller.UserId);
        }

        /// <summary>
        /// Set or clear a captain's policy. Requires a global admin or a tenant admin of the captain's tenant (the same
        /// rule as editing a captain).
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="captainId">Captain identifier.</param>
        /// <param name="policy">Policy, or null to inherit.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The captain as stored.</returns>
        /// <exception cref="KeyNotFoundException">Thrown when the captain is missing or not visible.</exception>
        /// <exception cref="UnauthorizedAccessException">Thrown when the caller is not an admin of the captain's tenant.</exception>
        public async Task<Captain> SetCaptainPolicyAsync(AuthContext caller, string captainId, CliPermissionPolicyEnum? policy, CancellationToken token = default)
        {
            if (caller == null) throw new ArgumentNullException(nameof(caller));
            if (String.IsNullOrWhiteSpace(captainId)) throw new KeyNotFoundException("Captain not found.");
            Captain? captain = await _Database.Captains.ReadAsync(captainId, token).ConfigureAwait(false);
            if (captain == null || (!caller.IsAdmin && !String.Equals(captain.TenantId, caller.TenantId, StringComparison.Ordinal)))
                throw new KeyNotFoundException("Captain not found.");
            if (!CliPermissionAccess.IsAdminFor(caller, captain.TenantId))
                throw new UnauthorizedAccessException("Only an admin can change a captain's CLI tool permission policy.");
            await _Database.Captains.UpdateCliPermissionPolicyAsync(captain.Id, policy, token).ConfigureAwait(false);
            _Logging.Info(_Header + "captain " + captain.Id + " CLI permission policy set to " + (policy?.ToString() ?? "inherit") + " by " + caller.UserId);
            return await _Database.Captains.ReadAsync(captain.Id, token).ConfigureAwait(false) ?? captain;
        }

        /// <summary>
        /// Set or clear an Ask thread's policy override. Only the thread owner may change it, and Bypass requires the
        /// owner to be a global admin or a tenant admin.
        /// </summary>
        /// <param name="caller">Caller (the thread owner).</param>
        /// <param name="threadId">Thread identifier.</param>
        /// <param name="policy">Policy, or null to inherit.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The thread as stored.</returns>
        /// <exception cref="KeyNotFoundException">Thrown when the thread is missing or not owned.</exception>
        /// <exception cref="UnauthorizedAccessException">Thrown when a non-admin sets Bypass.</exception>
        public async Task<AskThread> SetThreadPolicyAsync(AuthContext caller, string threadId, CliPermissionPolicyEnum? policy, CancellationToken token = default)
        {
            if (caller == null) throw new ArgumentNullException(nameof(caller));
            if (CliPermissionAccess.IsCaptainSession(caller) || String.IsNullOrEmpty(caller.TenantId) || String.IsNullOrEmpty(caller.UserId) || String.IsNullOrWhiteSpace(threadId))
                throw new KeyNotFoundException("Thread not found.");
            AskThread? thread = await _Database.AskThreads.ReadAsync(caller.TenantId!, caller.UserId!, threadId, token).ConfigureAwait(false);
            if (thread == null) throw new KeyNotFoundException("Thread not found.");
            if (policy == CliPermissionPolicyEnum.Bypass && !CliPermissionAccess.IsAdminFor(caller, thread.TenantId))
                throw new UnauthorizedAccessException("Only an admin can set Bypass for CLI tools.");
            await _Database.AskThreads.UpdateCliPermissionPolicyAsync(thread.TenantId!, thread.Id, policy, token).ConfigureAwait(false);
            _Logging.Info(_Header + "thread " + thread.Id + " CLI permission policy set to " + (policy?.ToString() ?? "inherit") + " by " + caller.UserId);
            if (AskThreads != null)
            {
                AskThread? decorated = await AskThreads.GetThreadAsync(caller, thread.Id, token).ConfigureAwait(false);
                if (decorated != null)
                {
                    AskThreads.Emit(decorated, "ask.thread", new { threadId = decorated.Id, thread = decorated });
                    return decorated;
                }
            }

            return await _Database.AskThreads.ReadByIdAsync(thread.Id, token).ConfigureAwait(false) ?? thread;
        }

        #endregion

        #region Private-Methods

        private async Task<CliPermissionRequest> ReadVisibleAsync(AuthContext caller, string requestId, CancellationToken token)
        {
            if (caller == null) throw new ArgumentNullException(nameof(caller));
            if (String.IsNullOrWhiteSpace(requestId)) throw new KeyNotFoundException("CLI permission request not found.");
            CliPermissionRequest? request = await _Database.CliPermissionRequests.ReadAsync(requestId, token).ConfigureAwait(false);
            if (request == null || !CliPermissionAccess.CanView(caller, request)) throw new KeyNotFoundException("CLI permission request not found.");
            return request;
        }

        private static void RequireRuleAdmin(AuthContext caller, CliPermissionRule rule)
        {
            if (CliPermissionAccess.IsCaptainSession(caller)) throw new UnauthorizedAccessException("A captain session cannot change CLI permission rules.");
            if (caller.IsAdmin) return;
            if (caller.IsTenantAdmin && rule.TenantId != null && String.Equals(rule.TenantId, caller.TenantId, StringComparison.Ordinal)) return;
            throw new UnauthorizedAccessException("Only an admin of the rule's tenant can change it.");
        }

        private async Task<bool> CancelOneAsync(CliPermissionRequest request, string message)
        {
            return await CancelOneAsync(request, message, CliPermissionRequestStatusEnum.Cancelled).ConfigureAwait(false);
        }

        private async Task<bool> CancelOneAsync(CliPermissionRequest request, string message, CliPermissionRequestStatusEnum status)
        {
            CliPermissionDecisionSourceEnum source = status == CliPermissionRequestStatusEnum.Expired ? CliPermissionDecisionSourceEnum.Timeout : CliPermissionDecisionSourceEnum.Cancelled;
            bool done = await _Database.CliPermissionRequests.TryDecideAsync(request.Id, status, source, null, null, message, CancellationToken.None).ConfigureAwait(false);
            if (!done) return false;
            if (_Waiters.TryGetValue(request.Id, out TaskCompletionSource<bool>? waiter)) waiter.TrySetResult(false);
            CliPermissionRequest updated = await _Database.CliPermissionRequests.ReadAsync(request.Id, CancellationToken.None).ConfigureAwait(false) ?? request;
            Announce(ResolvedEvent, updated);
            await RefreshCardAsync(updated).ConfigureAwait(false);
            return true;
        }

        private async Task PostCardAsync(CliPermissionRequest request, CliPermissionPromptProgress progress)
        {
            if (AskThreads == null || String.IsNullOrEmpty(request.ThreadId)) return;
            try
            {
                AskThread? thread = await AskThreads.ReadThreadInternalAsync(request.ThreadId!).ConfigureAwait(false);
                if (thread == null)
                {
                    lock (progress) progress.CardError = "thread " + request.ThreadId + " not found";
                    _Logging.Warn(_Header + "could not post the permission card for " + request.Id + ": thread " + request.ThreadId + " not found");
                    return;
                }

                AskMessage card = new AskMessage();
                card.Role = AskMessageRoleEnum.System;
                card.Kind = AskMessageKindEnum.CliPermission;
                card.CaptainId = request.CaptainId;
                card.ContentText = request.ToolName + ": " + request.SummaryText;
                // The card links to the request through the request's message id; set it before the message is announced.
                card = await AskThreads.AppendCliPermissionCardAsync(thread, card, request).ConfigureAwait(false);
                request.MessageId = card.Id;
                lock (progress) progress.MessageId = card.Id;
            }
            catch (Exception ex)
            {
                lock (progress) progress.CardError = ex.GetType().Name + ": " + ex.Message;
                _Logging.Warn(_Header + "could not post the permission card for " + request.Id + ": " + ex.Message);
            }
        }

        private static void Advance(CliPermissionPromptProgress progress, CliPermissionPromptStageEnum stage, List<string> timings)
        {
            DateTime now = DateTime.UtcNow;
            lock (progress)
            {
                timings.Add(progress.Stage + " " + (int)(now - progress.StageStartedUtc).TotalMilliseconds + " ms");
                progress.Stage = stage;
                progress.StageStartedUtc = now;
            }
        }

        private async Task RefreshCardAsync(CliPermissionRequest request)
        {
            if (AskThreads == null || String.IsNullOrEmpty(request.ThreadId) || String.IsNullOrEmpty(request.MessageId)) return;
            try
            {
                await AskThreads.RefreshMessageAsync(request.ThreadId!, request.MessageId!).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _Logging.Debug(_Header + "could not refresh the permission card for " + request.Id + ": " + ex.Message);
            }
        }

        private void Announce(string eventType, CliPermissionRequest request)
        {
            try { OnRequestEvent?.Invoke(eventType, request); }
            catch (Exception ex) { _Logging.Warn(_Header + "error announcing " + eventType + " for " + request.Id + ": " + ex.Message); }
        }

        private static string DenialMessage(CliPermissionRequest request)
        {
            if (!String.IsNullOrWhiteSpace(request.DecisionMessage) && request.DecisionSource != CliPermissionDecisionSourceEnum.Approver)
                return request.DecisionMessage!;
            string text = "An approver in Armada denied this " + request.ToolName + " call (" + request.Id + ").";
            if (!String.IsNullOrWhiteSpace(request.DecisionMessage)) text += " Reason: " + request.DecisionMessage;
            return text + " Do not retry the same call; continue without it or explain what you need.";
        }

        private static string? SafeHome()
        {
            try
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                return String.IsNullOrWhiteSpace(home) ? null : home;
            }
            catch
            {
                return null;
            }
        }

        private static string Clip(string text, int max)
        {
            if (String.IsNullOrEmpty(text)) return String.Empty;
            return text.Length <= max ? text : text.Substring(0, max) + "...";
        }

        #endregion
    }
}
