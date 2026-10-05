namespace Armada.Server.Mcp.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;

    /// <summary>
    /// Registers the CLI tool permission MCP tools: the permission prompt tool CLI captains call
    /// (<c>cli_permission_prompt</c>, the target of Claude Code's --permission-prompt-tool), and the tools people use to
    /// list and decide requests, manage rules, and set a captain's policy.
    /// </summary>
    public static class McpCliPermissionTools
    {
        #region Private-Members

        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register the tools. When <paramref name="service"/> is null every tool is still registered and answers a typed
        /// Unavailable error (the standalone stdio server), so the tool list matches the HTTP server's.
        /// </summary>
        /// <param name="register">Registration delegate.</param>
        /// <param name="database">Database driver.</param>
        /// <param name="service">CLI permission service, or null.</param>
        public static void Register(RegisterToolDelegate register, DatabaseDriver database, CliPermissionService? service)
        {
            if (register == null) throw new ArgumentNullException(nameof(register));

            register(
                CliPermissionPolicyResolver.PromptToolName,
                "Permission prompt tool for CLI captains (Claude Code --permission-prompt-tool). Only callable by a captain's own mission- or Ask-thread-scoped session; people decide requests with decide_cli_permission_request. Waits until an approver allows or denies the call, a CLI permission rule decides it, or it expires, then returns {\"behavior\":\"allow\",\"updatedInput\":{...}} or {\"behavior\":\"deny\",\"message\":\"...\"}.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        tool_name = new { type = "string", description = "Tool that needs permission (for example Bash)" },
                        input = new { type = "object", description = "The tool's input" },
                        tool_use_id = new { type = "string", description = "Tool use identifier" }
                    },
                    required = new[] { "tool_name", "input" }
                },
                async (args) =>
                {
                    if (service == null) return (object)McpToolError.Unavailable("CLI permission prompts are not available on this server.");
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    if (!McpToolHelpers.HasPresentedCredential() || !CliPermissionAccess.IsCaptainSession(caller))
                        return (object)McpToolError.Forbidden("cli_permission_prompt can only be called by a captain's mission- or thread-scoped session.");

                    CliPermissionPromptArgs request = args.HasValue
                        ? JsonSerializer.Deserialize<CliPermissionPromptArgs>(args.Value, _JsonOptions) ?? new CliPermissionPromptArgs()
                        : new CliPermissionPromptArgs();
                    if (String.IsNullOrWhiteSpace(request.ToolName)) return (object)McpToolError.InvalidArgument("tool_name is required.");

                    CliPermissionPromptContext? context = await BuildContextAsync(database, caller).ConfigureAwait(false);
                    if (context == null) return (object)McpToolError.NotFound("The mission or conversation of this session no longer exists.");

                    string input = String.IsNullOrWhiteSpace(request.InputJson) ? "{}" : request.InputJson!;
                    // The MCP call's token: when the captain cancels the call or its connection drops, the pending
                    // request is resolved at once instead of waiting for the prompt timeout.
                    CliPermissionPromptOutcome outcome = await service.PromptAsync(context, request.ToolName, input, McpToolHelpers.CallToken).ConfigureAwait(false);
                    if (outcome.Allowed) return (object)new CliPermissionPromptResult { Behavior = CliPermissionPromptResult.Allow, UpdatedInput = input };
                    return (object)new CliPermissionPromptResult { Behavior = CliPermissionPromptResult.Deny, Message = outcome.Message };
                });

            register(
                "list_cli_permission_requests",
                "List CLI permission requests (a CLI captain asking to run one of its own tools, for example a shell command) visible to the caller, newest first. Admins see their tenant (global admins every tenant); other users see requests from their own missions and Ask conversations. Each request has the tool name, a redacted input summary, captain, vessel, mission or thread, status, expiry, and canDecide.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        status = new { type = "string", description = "Pending, Allowed, Denied, Expired, or Cancelled" },
                        missionId = new { type = "string", description = "Mission filter (msn_ prefix)" },
                        threadId = new { type = "string", description = "Ask thread filter (ath_ prefix)" },
                        captainId = new { type = "string", description = "Captain filter (cpt_ prefix)" },
                        vesselId = new { type = "string", description = "Vessel filter (vsl_ prefix)" },
                        limit = new { type = "integer", description = "Maximum rows (1-1000, default 100)" }
                    }
                },
                async (args) =>
                {
                    if (service == null) return (object)McpToolError.Unavailable("CLI permissions are not available on this server.");
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    CliPermissionRequestListArgs request = args.HasValue
                        ? JsonSerializer.Deserialize<CliPermissionRequestListArgs>(args.Value, _JsonOptions) ?? new CliPermissionRequestListArgs()
                        : new CliPermissionRequestListArgs();
                    CliPermissionRequestQuery query = new CliPermissionRequestQuery
                    {
                        MissionId = Blank(request.MissionId),
                        ThreadId = Blank(request.ThreadId),
                        CaptainId = Blank(request.CaptainId),
                        VesselId = Blank(request.VesselId)
                    };
                    if (request.Limit.HasValue) query.Limit = request.Limit.Value;
                    if (!String.IsNullOrWhiteSpace(request.Status))
                    {
                        if (!EnumNames.TryParse(request.Status, true, out CliPermissionRequestStatusEnum status))
                            return (object)McpToolError.InvalidArgument("status must be one of " + McpToolHelpers.EnumNames<CliPermissionRequestStatusEnum>() + ".");
                        query.Status = status;
                    }

                    List<CliPermissionRequest> rows = await service.ListAsync(caller, query).ConfigureAwait(false);
                    return (object)new { count = rows.Count, requests = rows };
                });

            register(
                "get_cli_permission_request",
                "Get one CLI permission request visible to the caller.",
                new
                {
                    type = "object",
                    properties = new { requestId = new { type = "string", description = "Request ID (cpr_ prefix)" } },
                    required = new[] { "requestId" }
                },
                async (args) =>
                {
                    if (service == null) return (object)McpToolError.Unavailable("CLI permissions are not available on this server.");
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    CliPermissionRequestIdArgs request = JsonSerializer.Deserialize<CliPermissionRequestIdArgs>(args!.Value, _JsonOptions)!;
                    return (object)await service.GetAsync(caller, request.RequestId).ConfigureAwait(false);
                });

            register(
                "decide_cli_permission_request",
                "Allow once, allow and remember (store an allow rule; admins only), or deny a pending CLI permission request. Deciding requires a global admin or a tenant admin of the request's tenant, or the mission or thread owner when Permissions.AllowOwnerApproval is on. Requires a credential (not the unauthenticated loopback identity) and is refused for captain sessions, so a captain can never approve its own prompt.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        requestId = new { type = "string", description = "Request ID (cpr_ prefix)" },
                        decision = new { type = "string", description = "AllowOnce, AllowAndRemember, or Deny" },
                        message = new { type = "string", description = "Optional message (returned to the captain with a denial)" },
                        rulePattern = new { type = "string", description = "Rule for AllowAndRemember, for example Bash(git status:*); defaults to the request's suggested rule" },
                        ruleScope = new { type = "string", description = "Global, Vessel, or Captain (default Captain)" }
                    },
                    required = new[] { "requestId", "decision" }
                },
                async (args) =>
                {
                    if (service == null) return (object)McpToolError.Unavailable("CLI permissions are not available on this server.");
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    if (!McpToolHelpers.HasPresentedCredential() || CliPermissionAccess.IsCaptainSession(caller))
                        return (object)McpToolError.Forbidden("Deciding a CLI permission request over MCP requires a person's credential; captain sessions and the unauthenticated loopback identity cannot decide.");
                    CliPermissionDecideArgs request = JsonSerializer.Deserialize<CliPermissionDecideArgs>(args!.Value, _JsonOptions)!;
                    if (!EnumNames.TryParse(request.Decision, true, out CliPermissionDecisionEnum decision))
                        return (object)McpToolError.InvalidArgument("decision must be one of " + McpToolHelpers.EnumNames<CliPermissionDecisionEnum>() + ".");
                    CliPermissionDecisionRequest body = new CliPermissionDecisionRequest { Decision = decision, Message = request.Message, RulePattern = request.RulePattern };
                    if (!String.IsNullOrWhiteSpace(request.RuleScope))
                    {
                        if (!EnumNames.TryParse(request.RuleScope, true, out CliPermissionRuleScopeEnum scope))
                            return (object)McpToolError.InvalidArgument("ruleScope must be one of " + McpToolHelpers.EnumNames<CliPermissionRuleScopeEnum>() + ".");
                        body.RuleScope = scope;
                    }

                    return (object)await service.DecideAsync(caller, request.RequestId, body).ConfigureAwait(false);
                });

            register(
                "list_cli_permission_rules",
                "List CLI permission rules visible to the caller (the caller's tenant plus rules for every tenant). Rules use Claude Code permission rule syntax, for example Bash(git status:*), WebFetch(domain:example.com), or Edit(//repo/src/**); deny rules win over allow rules.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        scope = new { type = "string", description = "Global, Vessel, or Captain" },
                        vesselId = new { type = "string", description = "Vessel filter (vsl_ prefix)" },
                        captainId = new { type = "string", description = "Captain filter (cpt_ prefix)" }
                    }
                },
                async (args) =>
                {
                    if (service == null) return (object)McpToolError.Unavailable("CLI permissions are not available on this server.");
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    CliPermissionRuleListArgs request = args.HasValue
                        ? JsonSerializer.Deserialize<CliPermissionRuleListArgs>(args.Value, _JsonOptions) ?? new CliPermissionRuleListArgs()
                        : new CliPermissionRuleListArgs();
                    CliPermissionRuleQuery query = new CliPermissionRuleQuery { VesselId = Blank(request.VesselId), CaptainId = Blank(request.CaptainId) };
                    if (!String.IsNullOrWhiteSpace(request.Scope))
                    {
                        if (!EnumNames.TryParse(request.Scope, true, out CliPermissionRuleScopeEnum scope))
                            return (object)McpToolError.InvalidArgument("scope must be one of " + McpToolHelpers.EnumNames<CliPermissionRuleScopeEnum>() + ".");
                        query.Scope = scope;
                    }

                    List<CliPermissionRule> rules = await service.ListRulesAsync(caller, query).ConfigureAwait(false);
                    return (object)new { count = rules.Count, rules };
                });

            register(
                "create_cli_permission_rule",
                "Create a CLI permission rule (admins). A matching deny rule denies a captain's permission prompt without asking; a matching allow rule allows it. Scope Global applies to every captain of the tenant (a global admin may omit tenantId to apply it to every tenant), Vessel to missions on one vessel, Captain to one captain.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        pattern = new { type = "string", description = "Rule, for example Bash(npm run test:*)" },
                        action = new { type = "string", description = "Allow or Deny" },
                        scope = new { type = "string", description = "Global, Vessel, or Captain (default Global)" },
                        vesselId = new { type = "string", description = "Vessel for a Vessel rule" },
                        captainId = new { type = "string", description = "Captain for a Captain rule" },
                        tenantId = new { type = "string", description = "Tenant for a Global rule (global admins); omit for every tenant" },
                        description = new { type = "string", description = "Optional note" }
                    },
                    required = new[] { "pattern", "action" }
                },
                async (args) =>
                {
                    if (service == null) return (object)McpToolError.Unavailable("CLI permissions are not available on this server.");
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    CliPermissionRuleArgs request = JsonSerializer.Deserialize<CliPermissionRuleArgs>(args!.Value, _JsonOptions)!;
                    // A rule target outside the caller's scope answers not-found, like every by-id tool.
                    if (Blank(request.VesselId) != null && await McpCallerScope.ReadVesselAsync(database, caller, request.VesselId).ConfigureAwait(false) == null)
                        return (object)McpToolError.NotFound("Vessel not found");
                    if (Blank(request.CaptainId) != null && await McpCallerScope.ReadCaptainAsync(database, caller, request.CaptainId).ConfigureAwait(false) == null)
                        return (object)McpToolError.NotFound("Captain not found");
                    CliPermissionRule rule = new CliPermissionRule { Pattern = request.Pattern, Description = request.Description, VesselId = Blank(request.VesselId), CaptainId = Blank(request.CaptainId), TenantId = Blank(request.TenantId) };
                    if (!EnumNames.TryParse(request.Action, true, out CliPermissionRuleActionEnum action))
                        return (object)McpToolError.InvalidArgument("action must be Allow or Deny.");
                    rule.Action = action;
                    if (!String.IsNullOrWhiteSpace(request.Scope))
                    {
                        if (!EnumNames.TryParse(request.Scope, true, out CliPermissionRuleScopeEnum scope))
                            return (object)McpToolError.InvalidArgument("scope must be one of " + McpToolHelpers.EnumNames<CliPermissionRuleScopeEnum>() + ".");
                        rule.Scope = scope;
                    }

                    return (object)await service.CreateRuleAsync(caller, rule).ConfigureAwait(false);
                });

            register(
                "update_cli_permission_rule",
                "Update a CLI permission rule's pattern, action, and description (admins of the rule's tenant).",
                new
                {
                    type = "object",
                    properties = new
                    {
                        ruleId = new { type = "string", description = "Rule ID (cpl_ prefix)" },
                        pattern = new { type = "string", description = "Rule pattern" },
                        action = new { type = "string", description = "Allow or Deny" },
                        description = new { type = "string", description = "Optional note" }
                    },
                    required = new[] { "ruleId", "pattern", "action" }
                },
                async (args) =>
                {
                    if (service == null) return (object)McpToolError.Unavailable("CLI permissions are not available on this server.");
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    CliPermissionRuleArgs request = JsonSerializer.Deserialize<CliPermissionRuleArgs>(args!.Value, _JsonOptions)!;
                    if (!EnumNames.TryParse(request.Action, true, out CliPermissionRuleActionEnum action))
                        return (object)McpToolError.InvalidArgument("action must be Allow or Deny.");
                    CliPermissionRule update = new CliPermissionRule { Pattern = request.Pattern, Action = action, Description = request.Description };
                    return (object)await service.UpdateRuleAsync(caller, request.RuleId ?? String.Empty, update).ConfigureAwait(false);
                });

            register(
                "delete_cli_permission_rule",
                "Delete a CLI permission rule (admins of the rule's tenant).",
                new
                {
                    type = "object",
                    properties = new { ruleId = new { type = "string", description = "Rule ID (cpl_ prefix)" } },
                    required = new[] { "ruleId" }
                },
                async (args) =>
                {
                    if (service == null) return (object)McpToolError.Unavailable("CLI permissions are not available on this server.");
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    CliPermissionRuleIdArgs request = JsonSerializer.Deserialize<CliPermissionRuleIdArgs>(args!.Value, _JsonOptions)!;
                    await service.DeleteRuleAsync(caller, request.RuleId).ConfigureAwait(false);
                    return (object)new { deleted = true, ruleId = request.RuleId };
                });

            register(
                "set_captain_cli_permission_policy",
                "Set or clear a captain's CLI tool permission policy (Refuse, ApproveInArmada, or Bypass; omit policy to inherit). Requires a global admin or a tenant admin of the captain's tenant. Bypass runs the CLI with its permission-bypass flag (for example --dangerously-skip-permissions): the captain can run any command as the Admiral's user.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        captainId = new { type = "string", description = "Captain ID (cpt_ prefix)" },
                        policy = new { type = "string", description = "Refuse, ApproveInArmada, or Bypass; omit to clear" }
                    },
                    required = new[] { "captainId" }
                },
                async (args) =>
                {
                    if (service == null) return (object)McpToolError.Unavailable("CLI permissions are not available on this server.");
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    if (CliPermissionAccess.IsCaptainSession(caller))
                        return (object)McpToolError.Forbidden("A captain session cannot change CLI tool permission policies.");
                    CaptainCliPermissionPolicyArgs request = JsonSerializer.Deserialize<CaptainCliPermissionPolicyArgs>(args!.Value, _JsonOptions)!;
                    CliPermissionPolicyEnum? policy = null;
                    if (!String.IsNullOrWhiteSpace(request.Policy))
                    {
                        if (!EnumNames.TryParse(request.Policy, true, out CliPermissionPolicyEnum parsed))
                            return (object)McpToolError.InvalidArgument("policy must be one of " + McpToolHelpers.EnumNames<CliPermissionPolicyEnum>() + ".");
                        policy = parsed;
                    }

                    return (object)await service.SetCaptainPolicyAsync(caller, request.CaptainId, policy).ConfigureAwait(false);
                });
        }

        /// <summary>
        /// Resolve the session of a captain's scoped token: the Ask thread (owner checked against the token) or the
        /// mission (its captain, vessel, voyage, and dock).
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="caller">Caller with AskThreadId or MissionId.</param>
        /// <returns>The context, or null when the thread or mission no longer exists.</returns>
        public static async Task<CliPermissionPromptContext?> BuildContextAsync(DatabaseDriver database, AuthContext caller)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));
            if (caller == null) throw new ArgumentNullException(nameof(caller));
            CliPermissionPromptContext context = new CliPermissionPromptContext { TenantId = caller.TenantId, UserId = caller.UserId };

            if (!String.IsNullOrEmpty(caller.AskThreadId))
            {
                AskThread? thread = await database.AskThreads.ReadByIdAsync(caller.AskThreadId!).ConfigureAwait(false);
                if (thread == null
                    || !String.Equals(thread.TenantId, caller.TenantId, StringComparison.Ordinal)
                    || !String.Equals(thread.UserId, caller.UserId, StringComparison.Ordinal))
                    return null;
                context.ThreadId = thread.Id;
                context.CaptainId = thread.CaptainId;
            }
            else if (!String.IsNullOrEmpty(caller.MissionId) && !String.IsNullOrEmpty(caller.TenantId))
            {
                Mission? mission = await database.Missions.ReadAsync(caller.TenantId!, caller.MissionId!).ConfigureAwait(false);
                if (mission == null) return null;
                context.MissionId = mission.Id;
                context.VoyageId = mission.VoyageId;
                context.VesselId = mission.VesselId;
                context.CaptainId = mission.CaptainId;
                context.UserId = mission.UserId;
                if (!String.IsNullOrEmpty(mission.DockId))
                {
                    Dock? dock = await database.Docks.ReadAsync(mission.DockId!).ConfigureAwait(false);
                    if (dock != null && !String.IsNullOrWhiteSpace(dock.WorktreePath)) context.WorkingDirectory = dock.WorktreePath;
                }
            }
            else
            {
                return null;
            }

            if (!String.IsNullOrEmpty(context.CaptainId))
            {
                Captain? captain = await database.Captains.ReadAsync(context.CaptainId!).ConfigureAwait(false);
                if (captain != null) context.Runtime = captain.Runtime;
            }

            return context;
        }

        #endregion

        #region Private-Methods

        private static string? Blank(string? value)
        {
            return String.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        #endregion
    }
}
