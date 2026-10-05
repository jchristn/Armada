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
                "Start a fleet action run over vessels. Pass actionId to run a saved action, or the inline definition fields (name, kind, commandText or promptTemplate) for an ad hoc run. Every vessel must be in the caller's tenant or the whole request is rejected. Returns the run id; poll fleet_action_run_status.",
                new
                {
                    type = "object",
                    properties = RunProperties(),
                    required = new[] { "vesselIds" }
                },
                async (args) =>
                {
                    if (service == null) return Unavailable();
                    FleetActionRunArgs request = Parse<FleetActionRunArgs>(args) ?? new FleetActionRunArgs();
                    return await InvokeAsync(async () =>
                    {
                        FleetActionRunRequest body = new FleetActionRunRequest
                        {
                            VesselIds = request.VesselIds ?? new List<string>(),
                            Concurrency = request.Concurrency
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

                        FleetActionRun run = await service.StartRunAsync(McpToolHelpers.ResolveCallerContext(), request.ActionId, body).ConfigureAwait(false);
                        return FleetActionRunStartResult.FromRun(run);
                    }).ConfigureAwait(false);
                });

            register(
                "fleet_action_run_status",
                "Get a fleet action run with per-target summaries (status, skip/failure reason codes, exit code, voyage id, output length hints). Output text is not included.",
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
            return McpToolError.Unavailable("Fleet actions are not available on this server.");
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

        private static FleetActionUpsertRequest ToUpsertRequest(FleetActionUpsertArgs args)
        {
            FleetActionKindEnum? kind = null;
            if (!String.IsNullOrWhiteSpace(args.Kind))
            {
                if (!Enum.TryParse(args.Kind, true, out FleetActionKindEnum parsed))
                    throw new ArgumentException("Unknown kind: " + args.Kind + ". Use Command or Mission.");
                kind = parsed;
            }

            return new FleetActionUpsertRequest
            {
                Name = args.Name,
                Description = args.Description,
                Kind = kind,
                CommandText = args.CommandText,
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
                actionId = new { type = "string", description = "Saved fleet action ID (fac_ prefix). Omit for an ad hoc run." },
                vesselIds = new { type = "array", items = new { type = "string" }, description = "Target vessel IDs (vsl_ prefix), 1-500" },
                concurrency = new { type = "integer", description = "Concurrency for this run (1-32); defaults to the action's default" },
                name = new { type = "string", description = "Ad hoc: display name" },
                kind = new { type = "string", description = "Ad hoc: Command or Mission" },
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
