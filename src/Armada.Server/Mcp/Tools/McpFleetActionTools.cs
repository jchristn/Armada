namespace Armada.Server.Mcp.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;

    /// <summary>
    /// Registers MCP tools for fleet actions: create, update, delete, run, run status, and cancel. All tools act in
    /// the authenticated caller's tenant. Command-kind actions require tenant admin. Errors are returned as an object
    /// with an Error field rather than thrown, matching the other Armada tool groups. Run output is never returned by
    /// these tools; use enumerate with entityType fleet_action_run_target and includeOutput true when it is needed.
    /// </summary>
    public static class McpFleetActionTools
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
        /// Registers fleet action MCP tools.
        /// </summary>
        /// <param name="register">Delegate to register each tool.</param>
        /// <param name="service">Fleet action service; when null (catalog description only) the handlers return an error.</param>
        public static void Register(RegisterToolDelegate register, FleetActionService? service)
        {
            register(
                "create_fleet_action",
                "Create a reusable fleet action. Kind Command runs commandText in each vessel's working directory (tenant admin only); kind Mission dispatches one voyage per vessel from promptTemplate. Templates may use {{vessel.name}}, {{vessel.id}}, {{vessel.defaultBranch}}, {{vessel.workingDirectory}}, {{vessel.buildCommand}}, {{health.summary}}.",
                new
                {
                    type = "object",
                    properties = DefinitionProperties(false),
                    required = new[] { "name" }
                },
                async (args) =>
                {
                    if (service == null) return Unavailable();
                    FleetActionUpsertArgs request = Parse<FleetActionUpsertArgs>(args) ?? new FleetActionUpsertArgs();
                    return await InvokeAsync(async () =>
                    {
                        FleetActionUpsertRequest body = ToUpsertRequest(request);
                        return await service.CreateActionAsync(McpToolHelpers.ResolveCallerContext(), body).ConfigureAwait(false);
                    }).ConfigureAwait(false);
                });

            register(
                "update_fleet_action",
                "Update a fleet action. Only the supplied fields change. Built-in actions can be edited.",
                new
                {
                    type = "object",
                    properties = DefinitionProperties(true),
                    required = new[] { "actionId" }
                },
                async (args) =>
                {
                    if (service == null) return Unavailable();
                    FleetActionUpsertArgs request = Parse<FleetActionUpsertArgs>(args) ?? new FleetActionUpsertArgs();
                    return await InvokeAsync(async () =>
                    {
                        if (String.IsNullOrWhiteSpace(request.ActionId)) throw new ArgumentException("actionId is required.");
                        FleetActionUpsertRequest body = ToUpsertRequest(request);
                        return await service.UpdateActionAsync(McpToolHelpers.ResolveCallerContext(), request.ActionId!, body).ConfigureAwait(false);
                    }).ConfigureAwait(false);
                });

            register(
                "delete_fleet_action",
                "Delete a fleet action. Built-in actions are soft-deleted and never re-seeded. Past runs are kept.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        actionId = new { type = "string", description = "Fleet action ID (fac_ prefix)" }
                    },
                    required = new[] { "actionId" }
                },
                async (args) =>
                {
                    if (service == null) return Unavailable();
                    FleetActionIdArgs request = Parse<FleetActionIdArgs>(args) ?? new FleetActionIdArgs();
                    return await InvokeAsync(async () =>
                    {
                        await service.DeleteActionAsync(McpToolHelpers.ResolveCallerContext(), request.ActionId).ConfigureAwait(false);
                        return new { Deleted = true, ActionId = request.ActionId };
                    }).ConfigureAwait(false);
                });

            register(
                "run_fleet_action",
                "Start a fleet action run over vessels. Pass actionId to run a saved action, or the inline definition fields (name, kind, commandText or promptTemplate) for an ad hoc run. Pass vesselIds, or fromRunId to follow up on an earlier run (for example run a fix action only on the vessels where a scan found something). Every vessel must be in the caller's tenant or the whole request is rejected. Returns the run id; poll fleet_action_run_status.",
                new
                {
                    type = "object",
                    properties = RunProperties(),
                },
                async (args) =>
                {
                    if (service == null) return Unavailable();
                    FleetActionRunArgs request = Parse<FleetActionRunArgs>(args) ?? new FleetActionRunArgs();
                    return await InvokeAsync(async () =>
                    {
                        if (request.ApprovalOptions != null && (request.ApprovalOptions.ApproveAll || request.ApprovalOptions.TimeoutDecision == ApprovalTimeoutDecisionEnum.Approve)
                            && !McpToolHelpers.HasPresentedCredential())
                            return (object)McpToolError.Forbidden("Automatic approval requires an administrator's credential.");
                        FleetActionRunRequest body = new FleetActionRunRequest
                        {
                            VesselIds = request.VesselIds ?? new List<string>(),
                            Concurrency = request.Concurrency,
                            InputValues = request.InputValues ?? new(),
                            FromRunId = request.FromRunId,
                            FromTargetStatuses = ParseTargetStatuses(request.FromTargetStatuses)
                        };

                        if (String.IsNullOrWhiteSpace(request.ActionId))
                        {
                            body.Definition = ToUpsertRequest(request);
                        }
                        else
                        {
                            body.Overrides = new FleetActionRunOverrides
                            {
                                TimeoutSeconds = request.TimeoutSeconds,
                                RequiresCleanWorkingTree = request.RequiresCleanWorkingTree,
                                PipelineId = request.PipelineId
                            };
                        }

                        body.Overrides ??= new FleetActionRunOverrides();
                        body.Overrides.ApprovalOptions = request.ApprovalOptions;
                        FleetActionRun run = await service.StartRunAsync(McpToolHelpers.ResolveCallerContext(), request.ActionId, body).ConfigureAwait(false);
                        return FleetActionRunStartResult.FromRun(run);
                    }).ConfigureAwait(false);
                });

            register(
                "fleet_action_run_status",
                "Get a fleet action run with its target counts (succeeded, failed, skipped, cancelled). Targets are not embedded: page them with enumerate (entityType fleet_action_run_target, runId, pageNumber, pageSize, optional status) for per-target status, reason codes, exit code, voyage id and output length hints.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        runId = new { type = "string", description = "Run ID (far_ prefix)" }
                    },
                    required = new[] { "runId" }
                },
                async (args) =>
                {
                    if (service == null) return Unavailable();
                    FleetActionRunIdArgs request = Parse<FleetActionRunIdArgs>(args) ?? new FleetActionRunIdArgs();
                    return await InvokeAsync(async () =>
                    {
                        return await service.ReadRunAsync(McpToolHelpers.ResolveCallerContext(), request.RunId).ConfigureAwait(false);
                    }).ConfigureAwait(false);
                });

            register(
                "cancel_fleet_action_run",
                "Cancel a fleet action run: pending targets are cancelled, running commands are killed, and unlanded voyages of a Mission run are cancelled.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        runId = new { type = "string", description = "Run ID (far_ prefix)" }
                    },
                    required = new[] { "runId" }
                },
                async (args) =>
                {
                    if (service == null) return Unavailable();
                    FleetActionRunIdArgs request = Parse<FleetActionRunIdArgs>(args) ?? new FleetActionRunIdArgs();
                    return await InvokeAsync(async () =>
                    {
                        return await service.CancelRunAsync(McpToolHelpers.ResolveCallerContext(), request.RunId).ConfigureAwait(false);
                    }).ConfigureAwait(false);
                });
        }

        #endregion

        #region Private-Methods

        private static T? Parse<T>(JsonElement? args) where T : class
        {
            if (!args.HasValue) return null;
            return JsonSerializer.Deserialize<T>(args.Value, _JsonOptions);
        }

        private static object Unavailable()
        {
            return McpToolError.Unavailable("Fleet actions are not available on this server: runs are executed and tracked by the Admiral process. Use the Admiral's HTTP MCP endpoint (armada mcp stdio runs standalone).");
        }

        private static async Task<object> InvokeAsync(Func<Task<object>> action)
        {
            try
            {
                return await action().ConfigureAwait(false);
            }
            catch (UnauthorizedAccessException e)
            {
                return new McpToolError(McpToolErrorCodeEnum.Forbidden, e.Message) { StatusCode = 403 };
            }
            catch (KeyNotFoundException e)
            {
                return new McpToolError(McpToolErrorCodeEnum.NotFound, e.Message) { StatusCode = 404 };
            }
            catch (CollectionLimitExceededException e)
            {
                return McpToolError.FromException(e);
            }
            catch (FleetActionTemplateException e)
            {
                return new McpToolError(McpToolErrorCodeEnum.InvalidArgument, e.Message, FleetActionTemplateErrorDetail.UnknownTemplateVariableCode) { StatusCode = 400 };
            }
            catch (ArgumentException e)
            {
                return new McpToolError(McpToolErrorCodeEnum.InvalidArgument, e.Message) { StatusCode = 400 };
            }
            catch (InvalidOperationException e)
            {
                return new McpToolError(McpToolErrorCodeEnum.Conflict, e.Message) { StatusCode = 409 };
            }
        }

        private static List<FleetActionTargetStatusEnum>? ParseTargetStatuses(List<string>? values)
        {
            if (values == null) return null;
            List<FleetActionTargetStatusEnum> statuses = new List<FleetActionTargetStatusEnum>();
            foreach (string value in values)
            {
                if (!Enum.TryParse(value, true, out FleetActionTargetStatusEnum status))
                    throw new ArgumentException("Unknown target status: " + value + ".");
                statuses.Add(status);
            }

            return statuses;
        }

        private static FleetActionUpsertRequest ToUpsertRequest(FleetActionUpsertArgs args)
        {
            FleetActionKindEnum? kind = null;
            if (!String.IsNullOrWhiteSpace(args.Kind))
            {
                if (!Enum.TryParse(args.Kind, true, out FleetActionKindEnum parsed))
                    throw new ArgumentException("Unknown kind: " + args.Kind + ". Use Command or Mission.");
                kind = parsed;
            }

            MissionModeEnum? missionMode = null;
            if (!String.IsNullOrWhiteSpace(args.MissionMode))
            {
                if (!Enum.TryParse(args.MissionMode, true, out MissionModeEnum parsedMode))
                    throw new ArgumentException("Unknown missionMode: " + args.MissionMode + ". Use Implementation, Audit, or Research.");
                missionMode = parsedMode;
            }

            return new FleetActionUpsertRequest
            {
                Name = args.Name,
                Description = args.Description,
                Kind = kind,
                CommandText = args.CommandText,
                CommandWindows = args.CommandWindows,
                OutputParser = args.OutputParser,
                ScanSourceActionKey = args.ScanSourceActionKey,
                ApplicabilityCommand = args.ApplicabilityCommand,
                ApplicabilityCommandWindows = args.ApplicabilityCommandWindows,
                MissionMode = missionMode,
                RequiredTools = args.RequiredTools,
                RequiresNetwork = args.RequiresNetwork,
                Inputs = args.Inputs,
                PromptTemplate = args.PromptTemplate,
                PipelineId = args.PipelineId,
                Persona = args.Persona,
                TimeoutSeconds = args.TimeoutSeconds,
                DefaultConcurrency = args.DefaultConcurrency,
                RequiresCleanWorkingTree = args.RequiresCleanWorkingTree
            };
        }

        private static object DefinitionProperties(bool includeActionId)
        {
            if (includeActionId)
            {
                return new
                {
                    actionId = new { type = "string", description = "Fleet action ID (fac_ prefix)" },
                    name = new { type = "string", description = "Display name" },
                    description = new { type = "string", description = "Optional description" },
                    kind = new { type = "string", description = "Command or Mission" },
                    commandWindows = new { type = "string", description = "Optional Windows shell command" },
                outputParser = new { type = "string", description = "Registered output parser ID" },
                scanSourceActionKey = new { type = "string", description = "Source scan action key or ID for scan.summary" },
                applicabilityCommand = new { type = "string", description = "Optional shell check run in each working directory first; a non-zero exit skips the vessel (NotApplicable). Requires tenant admin." },
                applicabilityCommandWindows = new { type = "string", description = "Optional Windows (PowerShell) applicability check" },
                missionMode = new { type = "string", @enum = new[] { "Implementation", "Audit", "Research" }, description = "Mission kind: Audit and Research are report-only (read-only, Worker-only, nothing lands; the report is the target output)" },
                requiredTools = new { type = "array", items = new { type = "string" } },
                requiresNetwork = new { type = "boolean" },
                inputs = new { type = "array", items = new { type = "object", properties = new {
                    name = new { type = "string" }, type = new { type = "string", @enum = new[] { "string", "semver", "enum", "package" } },
                    required = new { type = "boolean" }, @default = new { type = "string" },
                    allowedValues = new { type = "array", items = new { type = "string" } }, description = new { type = "string" }
                }, required = new[] { "name", "type" } } },
                commandText = new { type = "string", description = "Shell command template (Command kind)" },
                    promptTemplate = new { type = "string", description = "Prompt template (Mission kind)" },
                    pipelineId = new { type = "string", description = "Optional pipeline ID (Mission kind); empty string clears it" },
                    persona = new { type = "string", description = "Optional persona name (Mission kind); empty string clears it" },
                    timeoutSeconds = new { type = "integer", description = "Per-target timeout in seconds (5-7200)" },
                    defaultConcurrency = new { type = "integer", description = "Default concurrency (1-32)" },
                    requiresCleanWorkingTree = new { type = "boolean", description = "Skip vessels with uncommitted changes (Command kind)" }
                };
            }

            return new
            {
                name = new { type = "string", description = "Display name" },
                description = new { type = "string", description = "Optional description" },
                kind = new { type = "string", description = "Command (default) or Mission" },
                commandWindows = new { type = "string", description = "Optional Windows shell command" },
                outputParser = new { type = "string", description = "Registered output parser ID" },
                scanSourceActionKey = new { type = "string", description = "Source scan action key or ID for scan.summary" },
                applicabilityCommand = new { type = "string", description = "Optional shell check run in each working directory first; a non-zero exit skips the vessel (NotApplicable). Requires tenant admin." },
                applicabilityCommandWindows = new { type = "string", description = "Optional Windows (PowerShell) applicability check" },
                missionMode = new { type = "string", @enum = new[] { "Implementation", "Audit", "Research" }, description = "Mission kind: Audit and Research are report-only (read-only, Worker-only, nothing lands; the report is the target output)" },
                requiredTools = new { type = "array", items = new { type = "string" } },
                requiresNetwork = new { type = "boolean" },
                inputs = new { type = "array", items = new { type = "object", properties = new {
                    name = new { type = "string" }, type = new { type = "string", @enum = new[] { "string", "semver", "enum", "package" } },
                    required = new { type = "boolean" }, @default = new { type = "string" },
                    allowedValues = new { type = "array", items = new { type = "string" } }, description = new { type = "string" }
                }, required = new[] { "name", "type" } } },
                commandText = new { type = "string", description = "Shell command template (required for Command kind)" },
                promptTemplate = new { type = "string", description = "Prompt template (required for Mission kind)" },
                pipelineId = new { type = "string", description = "Optional pipeline ID (Mission kind)" },
                persona = new { type = "string", description = "Optional persona name (Mission kind)" },
                timeoutSeconds = new { type = "integer", description = "Per-target timeout in seconds (5-7200, default from settings)" },
                defaultConcurrency = new { type = "integer", description = "Default concurrency (1-32, default 4)" },
                requiresCleanWorkingTree = new { type = "boolean", description = "Skip vessels with uncommitted changes (default true for Command)" }
            };
        }

        private static object RunProperties()
        {
            return new
            {
                approvalOptions = new { type = "object", description = "Per-run tool/review approvals; automatic approval requires an administrator credential.", properties = new {
                    approveAll = new { type = "boolean" }, timeoutSeconds = new { type = "integer", minimum = 10, maximum = 3600 }, timeoutDecision = new { type = "string", @enum = new[] { "Deny", "Approve" } }
                } },
                actionId = new { type = "string", description = "Saved fleet action ID (fac_ prefix). Omit for an ad hoc run." },
                vesselIds = new { type = "array", items = new { type = "string" }, description = "Target vessel IDs (vsl_ prefix), 1-500. Omit when fromRunId is set." },
                fromRunId = new { type = "string", description = "Follow-up run: target the vessels of this earlier run (far_ prefix) whose status is in fromTargetStatuses, for example a fix action after a scan" },
                fromTargetStatuses = new { type = "array", items = new { type = "string", @enum = new[] { "Findings", "Failed", "Succeeded", "Skipped", "TimedOut", "Cancelled" } }, description = "Statuses selected from fromRunId (default Findings and Failed)" },
                inputValues = new { type = "object", additionalProperties = new { type = "string" }, description = "Runtime values keyed by declared input name" },
                concurrency = new { type = "integer", description = "Concurrency for this run (1-32); defaults to the action's default" },
                name = new { type = "string", description = "Ad hoc: display name" },
                kind = new { type = "string", description = "Ad hoc: Command or Mission" },
                commandWindows = new { type = "string", description = "Optional Windows shell command" },
                outputParser = new { type = "string", description = "Registered output parser ID" },
                scanSourceActionKey = new { type = "string", description = "Source scan action key or ID for scan.summary" },
                applicabilityCommand = new { type = "string", description = "Optional shell check run in each working directory first; a non-zero exit skips the vessel (NotApplicable). Requires tenant admin." },
                applicabilityCommandWindows = new { type = "string", description = "Optional Windows (PowerShell) applicability check" },
                missionMode = new { type = "string", @enum = new[] { "Implementation", "Audit", "Research" }, description = "Mission kind: Audit and Research are report-only (read-only, Worker-only, nothing lands; the report is the target output)" },
                requiredTools = new { type = "array", items = new { type = "string" } },
                requiresNetwork = new { type = "boolean" },
                inputs = new { type = "array", items = new { type = "object", properties = new {
                    name = new { type = "string" }, type = new { type = "string", @enum = new[] { "string", "semver", "enum", "package" } },
                    required = new { type = "boolean" }, @default = new { type = "string" },
                    allowedValues = new { type = "array", items = new { type = "string" } }, description = new { type = "string" }
                }, required = new[] { "name", "type" } } },
                commandText = new { type = "string", description = "Ad hoc: shell command template" },
                promptTemplate = new { type = "string", description = "Ad hoc: prompt template" },
                pipelineId = new { type = "string", description = "Pipeline ID for Mission runs (overrides the saved action's)" },
                timeoutSeconds = new { type = "integer", description = "Per-target timeout in seconds (overrides the saved action's)" },
                requiresCleanWorkingTree = new { type = "boolean", description = "Skip dirty working trees (overrides the saved action's)" }
            };
        }

        #endregion
    }
}
