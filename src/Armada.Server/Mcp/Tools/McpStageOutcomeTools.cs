namespace Armada.Server.Mcp.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Server.Routes;

    /// <summary>
    /// Registers the stage outcome and failure-cause rule MCP tools: list, read and classify stage outcomes, re-apply the
    /// rules, draft a rule from an outcome, and manage the rules. Every tool enforces the same rules as REST through
    /// <see cref="StageOutcomeAccessService"/>; its typed exceptions become typed tool errors.
    /// </summary>
    public static class McpStageOutcomeTools
    {
        #region Private-Members

        private const string UnavailableMessage = "Stage outcomes are not available on this server.";

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
        /// <param name="service">Stage outcome access service, or null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="register"/> is null.</exception>
        public static void Register(RegisterToolDelegate register, StageOutcomeAccessService? service)
        {
            if (register == null) throw new ArgumentNullException(nameof(register));

            object filterProperties = new
            {
                tenantId = new { type = "string", description = "Tenant filter (global admins only; ignored otherwise)" },
                missionId = new { type = "string", description = "Mission filter (msn_ prefix)" },
                voyageId = new { type = "string", description = "Voyage filter (vyg_ prefix)" },
                vesselId = new { type = "string", description = "Vessel filter (vsl_ prefix)" },
                captainId = new { type = "string", description = "Captain filter (cpt_ prefix)" },
                persona = new { type = "string", description = "Persona name (exact, case-insensitive)" },
                model = new { type = "string", description = "Model (exact, case-insensitive)" },
                suspectedCause = new { type = "string", description = "Protocol, InstructionFollowing, ToolUse, Context, Capability, NotTheModel, Unknown, Mixed, or None" },
                causeSource = new { type = "string", description = "None, Rule, Judge, or Manual" },
                disposition = new { type = "string", description = "Completed, Relaunched, OutputRepair, Escalated, Failed, Interrupted, Cancelled, or Pending" },
                isEvaluation = new { type = "boolean", description = "true for evaluation attempts only, false for production only" },
                fromUtc = new { type = "string", description = "Ended at or after (ISO 8601 UTC)" },
                toUtc = new { type = "string", description = "Ended before (ISO 8601 UTC)" },
                pageNumber = new { type = "integer", description = "Page number (default 1)" },
                pageSize = new { type = "integer", description = "Page size (1-1000, default 25)" }
            };

            register(
                "list_stage_outcomes",
                "List stage outcomes (one per stage attempt: who ran it, what was observed, what Armada did next, and the suspected cause of a shortfall), newest first, one page at a time. A global admin sees every tenant; everyone else sees their own tenant. Returns an enumeration result (objects, totalRecords, totalPages).",
                new { type = "object", properties = filterProperties },
                async (args) =>
                {
                    if (service == null) return (object)McpToolError.Unavailable(UnavailableMessage);
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    StageOutcomeListArgs request = Read<StageOutcomeListArgs>(args);
                    StageOutcomeQuery query = ToQuery(request);
                    if (!request.PageSize.HasValue) query.PageSize = 25;
                    return (object)await service.ListOutcomesAsync(caller, query).ConfigureAwait(false);
                });

            register(
                "get_stage_outcome",
                "Get one stage outcome visible to the caller.",
                new
                {
                    type = "object",
                    properties = new { outcomeId = new { type = "string", description = "Outcome ID (sto_ prefix)" } },
                    required = new[] { "outcomeId" }
                },
                async (args) =>
                {
                    if (service == null) return (object)McpToolError.Unavailable(UnavailableMessage);
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    StageOutcomeIdArgs request = Read<StageOutcomeIdArgs>(args);
                    return (object)await service.GetOutcomeAsync(caller, request.OutcomeId).ConfigureAwait(false);
                });

            register(
                "classify_stage_outcome",
                "Classify a stage outcome's suspected cause by hand (causeSource Manual, recorded with the caller as classifier); the rules never overwrite a manual cause. Omit cause to clear the manual cause and hand the outcome back to the rules.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        outcomeId = new { type = "string", description = "Outcome ID (sto_ prefix)" },
                        cause = new { type = "string", description = "Protocol, InstructionFollowing, ToolUse, Context, Capability, NotTheModel, Unknown, or Mixed; omit to hand back to the rules" },
                        note = new { type = "string", description = "Optional note (evidence)" }
                    },
                    required = new[] { "outcomeId" }
                },
                async (args) =>
                {
                    if (service == null) return (object)McpToolError.Unavailable(UnavailableMessage);
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    StageOutcomeClassifyArgs request = Read<StageOutcomeClassifyArgs>(args);
                    StageOutcomeClassifyRequest body = new StageOutcomeClassifyRequest { Note = request.Note };
                    if (!String.IsNullOrWhiteSpace(request.Cause))
                    {
                        if (!EnumNames.TryParse(request.Cause, true, out FailureCauseEnum cause))
                            return (object)McpToolError.InvalidArgument("cause must be one of " + McpToolHelpers.EnumNames<FailureCauseEnum>() + ".");
                        body.Cause = cause;
                    }

                    return (object)await service.ClassifyAsync(caller, request.OutcomeId, body).ConfigureAwait(false);
                });

            register(
                "reclassify_stage_outcomes",
                "Re-apply the enabled failure-cause rules to every stage outcome matching the filter (paging is ignored), so a new or edited rule takes effect on past attempts; outcomes classified by hand are left alone. Global admins and tenant admins (their own tenant) only. Returns examined, changed and skippedManual.",
                new { type = "object", properties = filterProperties },
                async (args) =>
                {
                    if (service == null) return (object)McpToolError.Unavailable(UnavailableMessage);
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    StageOutcomeListArgs request = Read<StageOutcomeListArgs>(args);
                    return (object)await service.ReclassifyAsync(caller, ToQuery(request)).ConfigureAwait(false);
                });

            register(
                "draft_failure_cause_rule",
                "Draft (without storing) a failure-cause rule from a classified stage outcome: its cause and tenant, priority 5, and a match on its failure kind, runtime failure kind, stage-check status and reason, disposition and persona. Edit the draft and pass it to create_failure_cause_rule.",
                new
                {
                    type = "object",
                    properties = new { outcomeId = new { type = "string", description = "Outcome ID (sto_ prefix)" } },
                    required = new[] { "outcomeId" }
                },
                async (args) =>
                {
                    if (service == null) return (object)McpToolError.Unavailable(UnavailableMessage);
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    StageOutcomeIdArgs request = Read<StageOutcomeIdArgs>(args);
                    return (object)await service.DraftRuleAsync(caller, request.OutcomeId).ConfigureAwait(false);
                });

            register(
                "list_failure_cause_rules",
                "List the failure-cause rules visible to the caller in evaluation order (priority ascending, then name), one page at a time: the caller's tenant plus the rules for every tenant, built-in rules included. The first enabled matching rule assigns a stage outcome's suspected cause.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        pageNumber = new { type = "integer", description = "Page number (default 1)" },
                        pageSize = new { type = "integer", description = "Page size (1-1000, default 100)" }
                    }
                },
                async (args) =>
                {
                    if (service == null) return (object)McpToolError.Unavailable(UnavailableMessage);
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    FailureCauseRuleListArgs request = Read<FailureCauseRuleListArgs>(args);
                    EnumerationQuery page = new EnumerationQuery { PageNumber = request.PageNumber ?? 1, PageSize = request.PageSize ?? 100 };
                    return (object)await service.ListRulesAsync(caller, page).ConfigureAwait(false);
                });

            register(
                "get_failure_cause_rule",
                "Get one failure-cause rule visible to the caller.",
                new
                {
                    type = "object",
                    properties = new { ruleId = new { type = "string", description = "Rule ID (fcr_ prefix)" } },
                    required = new[] { "ruleId" }
                },
                async (args) =>
                {
                    if (service == null) return (object)McpToolError.Unavailable(UnavailableMessage);
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    FailureCauseRuleIdArgs request = Read<FailureCauseRuleIdArgs>(args);
                    return (object)await service.GetRuleAsync(caller, request.RuleId).ConfigureAwait(false);
                });

            register(
                "create_failure_cause_rule",
                "Create a failure-cause rule. A global admin may set tenantId (omit for every tenant); anyone else creates a rule for their own tenant. Every match criterion given must match (AND); within a list any value matches (OR). Invalid when the name is missing or over 200 characters, the match has no criterion, the cause is None, or textPattern is not a valid regular expression.",
                new
                {
                    type = "object",
                    properties = RuleProperties(false),
                    required = new[] { "name", "cause", "match" }
                },
                async (args) =>
                {
                    if (service == null) return (object)McpToolError.Unavailable(UnavailableMessage);
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    FailureCauseRuleArgs request = Read<FailureCauseRuleArgs>(args);
                    FailureCauseRule rule = new FailureCauseRule { TenantId = request.TenantId };
                    McpToolError? invalid = Apply(request, rule);
                    if (invalid != null) return (object)invalid;
                    return (object)await service.CreateRuleAsync(caller, rule).ConfigureAwait(false);
                });

            register(
                "update_failure_cause_rule",
                "Update a failure-cause rule; omitted fields keep their stored values and match, when given, replaces the whole match. Rules for every tenant and built-in rules need a global admin. An edited built-in rule is never overwritten when the Admiral seeds the built-ins.",
                new
                {
                    type = "object",
                    properties = RuleProperties(true),
                    required = new[] { "ruleId" }
                },
                async (args) =>
                {
                    if (service == null) return (object)McpToolError.Unavailable(UnavailableMessage);
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    FailureCauseRuleArgs request = Read<FailureCauseRuleArgs>(args);
                    FailureCauseRule rule = await service.GetRuleAsync(caller, request.RuleId).ConfigureAwait(false);
                    McpToolError? invalid = Apply(request, rule);
                    if (invalid != null) return (object)invalid;
                    return (object)await service.UpdateRuleAsync(caller, rule.Id, rule).ConfigureAwait(false);
                });

            register(
                "delete_failure_cause_rule",
                "Delete a failure-cause rule. Rules for every tenant and built-in rules need a global admin. A deleted built-in rule is stored again the next time the Admiral starts; disable it (enabled false) to keep it out of classification.",
                new
                {
                    type = "object",
                    properties = new { ruleId = new { type = "string", description = "Rule ID (fcr_ prefix)" } },
                    required = new[] { "ruleId" }
                },
                async (args) =>
                {
                    if (service == null) return (object)McpToolError.Unavailable(UnavailableMessage);
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    FailureCauseRuleIdArgs request = Read<FailureCauseRuleIdArgs>(args);
                    FailureCauseRule deleted = await service.DeleteRuleAsync(caller, request.RuleId).ConfigureAwait(false);
                    return (object)new { deleted = true, ruleId = deleted.Id, isBuiltIn = deleted.IsBuiltIn };
                });
        }

        #endregion

        #region Private-Methods

        private static T Read<T>(JsonElement? args) where T : class, new()
        {
            if (!args.HasValue || args.Value.ValueKind != JsonValueKind.Object) return new T();
            try
            {
                return JsonSerializer.Deserialize<T>(args.Value, _JsonOptions) ?? new T();
            }
            catch (JsonException ex)
            {
                throw new ArgumentException("Invalid arguments: " + ex.Message, ex);
            }
        }

        private static StageOutcomeQuery ToQuery(StageOutcomeListArgs request)
        {
            Dictionary<string, string?> values = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["tenantId"] = request.TenantId,
                ["missionId"] = request.MissionId,
                ["voyageId"] = request.VoyageId,
                ["vesselId"] = request.VesselId,
                ["captainId"] = request.CaptainId,
                ["persona"] = request.Persona,
                ["model"] = request.Model,
                ["suspectedCause"] = request.SuspectedCause,
                ["causeSource"] = request.CauseSource,
                ["disposition"] = request.Disposition,
                ["isEvaluation"] = request.IsEvaluation.HasValue ? (request.IsEvaluation.Value ? "true" : "false") : null,
                ["fromUtc"] = request.FromUtc,
                ["toUtc"] = request.ToUtc,
                ["pageNumber"] = request.PageNumber.HasValue ? request.PageNumber.Value.ToString(CultureInfo.InvariantCulture) : null,
                ["pageSize"] = request.PageSize.HasValue ? request.PageSize.Value.ToString(CultureInfo.InvariantCulture) : null
            };
            return StageOutcomeRoutes.ParseQuery(key => values.TryGetValue(key, out string? value) ? value : null);
        }

        private static McpToolError? Apply(FailureCauseRuleArgs request, FailureCauseRule rule)
        {
            if (request.Name != null) rule.Name = request.Name;
            if (request.Description != null) rule.Description = request.Description;
            if (request.Priority.HasValue) rule.Priority = request.Priority.Value;
            if (request.Enabled.HasValue) rule.Enabled = request.Enabled.Value;
            if (request.Match != null) rule.Match = request.Match;
            if (!String.IsNullOrWhiteSpace(request.Cause))
            {
                if (!EnumNames.TryParse(request.Cause, true, out FailureCauseEnum cause))
                    return McpToolError.InvalidArgument("cause must be one of " + McpToolHelpers.EnumNames<FailureCauseEnum>() + ".");
                rule.Cause = cause;
            }
            else if (String.IsNullOrEmpty(request.RuleId))
            {
                return McpToolError.InvalidArgument("cause is required.");
            }

            return null;
        }

        private static object RuleProperties(bool update)
        {
            object match = new
            {
                type = "object",
                description = "Criteria: every criterion given must match (AND); within a list any value matches (OR)",
                properties = new
                {
                    personas = new { type = "array", items = new { type = "string" }, description = "Persona names (case-insensitive)" },
                    runtimes = new { type = "array", items = new { type = "string" }, description = "Runtimes (ClaudeCode, Codex, Gemini, Cursor, Mux, OpenCode, Pi, ApiEndpoint, Custom)" },
                    failureKinds = new { type = "array", items = new { type = "string" }, description = "Mission failure kinds (MissionFailureKindEnum names, for example InvalidOutput, Timeout, Infra)" },
                    runtimeFailureKinds = new { type = "array", items = new { type = "string" }, description = "Runtime exit classifications (Clean, UsageLimit, AuthFailure, Crash, ModelUnavailable, TaskFailure, Transient, Unclassified)" },
                    stageCheckStatuses = new { type = "array", items = new { type = "string" }, description = "Stage check statuses (Passed, Failed, Skipped, CouldNotRun)" },
                    stageCheckReasons = new { type = "array", items = new { type = "string" }, description = "Stage check reasons (StageCheckReasonEnum names)" },
                    dispositions = new { type = "array", items = new { type = "string" }, description = "Dispositions (Completed, Relaunched, OutputRepair, Escalated, Failed, Interrupted, Cancelled, Pending)" },
                    textPattern = new { type = "string", description = "Regular expression (case-insensitive) matched against the failure reason" }
                }
            };

            if (update)
            {
                return new
                {
                    ruleId = new { type = "string", description = "Rule ID (fcr_ prefix)" },
                    name = new { type = "string", description = "Short name, unique per tenant, at most 200 characters" },
                    description = new { type = "string", description = "What the rule recognizes and why" },
                    priority = new { type = "integer", description = "Evaluation order, lowest first" },
                    enabled = new { type = "boolean", description = "Whether the rule is evaluated" },
                    cause = new { type = "string", description = "Protocol, InstructionFollowing, ToolUse, Context, Capability, NotTheModel, Unknown, or Mixed" },
                    match
                };
            }

            return new
            {
                tenantId = new { type = "string", description = "Tenant (global admins); omit for every tenant" },
                name = new { type = "string", description = "Short name, unique per tenant, at most 200 characters" },
                description = new { type = "string", description = "What the rule recognizes and why" },
                priority = new { type = "integer", description = "Evaluation order, lowest first (default 100)" },
                enabled = new { type = "boolean", description = "Whether the rule is evaluated (default true)" },
                cause = new { type = "string", description = "Protocol, InstructionFollowing, ToolUse, Context, Capability, NotTheModel, Unknown, or Mixed" },
                match
            };
        }

        #endregion
    }
}
