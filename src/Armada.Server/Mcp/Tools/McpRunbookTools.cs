namespace Armada.Server.Mcp.Tools
{
    using System;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using Armada.Core.Models;
    using Armada.Core.Services;

    /// <summary>
    /// Registers MCP tools for runbook inspection and execution.
    /// </summary>
    public static class McpRunbookTools
    {
        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() }
        };

        /// <summary>
        /// Registers runbook MCP tools.
        /// </summary>
        public static void Register(RegisterToolDelegate register, RunbookService runbookService)
        {
            register(
                "get_runbook",
                "Inspect one runbook including parameters, bound workflow profile, environment, and step structure.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        runbookId = new { type = "string", description = "Runbook ID (same as playbook ID)" }
                    },
                    required = new[] { "runbookId" }
                },
                async (args) =>
                {
                    RunbookIdArgs request = JsonSerializer.Deserialize<RunbookIdArgs>(args!.Value, _JsonOptions)
                        ?? throw new InvalidOperationException("Could not deserialize RunbookIdArgs.");
                    AuthContext auth = McpToolHelpers.ResolveCallerContext();
                    Runbook? runbook = await runbookService.ReadAsync(auth, request.RunbookId).ConfigureAwait(false);
                    if (runbook == null) return (object)new { Error = "Runbook not found" };
                    return (object)runbook;
                });

            register(
                "get_runbook_execution",
                "Inspect one runbook execution including completed steps, notes, and deployment or incident linkage.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        runbookExecutionId = new { type = "string", description = "Runbook execution ID (rbx_ prefix)" }
                    },
                    required = new[] { "runbookExecutionId" }
                },
                async (args) =>
                {
                    RunbookExecutionIdArgs request = JsonSerializer.Deserialize<RunbookExecutionIdArgs>(args!.Value, _JsonOptions)
                        ?? throw new InvalidOperationException("Could not deserialize RunbookExecutionIdArgs.");
                    AuthContext auth = McpToolHelpers.ResolveCallerContext();
                    RunbookExecution? execution = await runbookService.ReadExecutionAsync(auth, request.RunbookExecutionId).ConfigureAwait(false);
                    if (execution == null) return (object)new { Error = "Runbook execution not found" };
                    return (object)execution;
                });

            register(
                "start_runbook_execution",
                "Start a guided runbook execution with optional parameter overrides and deployment or incident context.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        runbookId = new { type = "string", description = "Runbook ID (same as playbook ID)" },
                        title = new { type = "string", description = "Optional execution title override" },
                        workflowProfileId = new { type = "string", description = "Optional workflow profile override (wfp_ prefix)" },
                        environmentId = new { type = "string", description = "Optional environment ID (env_ prefix)" },
                        environmentName = new { type = "string", description = "Optional environment name override" },
                        checkType = new { type = "string", description = "Optional check type override" },
                        deploymentId = new { type = "string", description = "Optional related deployment ID (dpl_ prefix)" },
                        incidentId = new { type = "string", description = "Optional related incident ID (inc_ prefix)" },
                        notes = new { type = "string", description = "Optional execution notes" },
                        parameterValues = new
                        {
                            type = "object",
                            additionalProperties = new { type = "string" },
                            description = "Optional parameter-value map"
                        }
                    },
                    required = new[] { "runbookId" }
                },
                async (args) =>
                {
                    JsonElement value = args!.Value;
                    string runbookId = value.GetProperty("runbookId").GetString() ?? String.Empty;
                    RunbookExecutionStartRequest request = JsonSerializer.Deserialize<RunbookExecutionStartRequest>(value, _JsonOptions)
                        ?? new RunbookExecutionStartRequest();
                    AuthContext auth = McpToolHelpers.ResolveCallerContext();
                    return (object)await runbookService.StartExecutionAsync(auth, runbookId, request).ConfigureAwait(false);
                });
        }
    }
}
