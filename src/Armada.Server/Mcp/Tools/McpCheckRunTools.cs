namespace Armada.Server.Mcp.Tools
{
    using System;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using Armada.Core.Database;
    using Armada.Core.Models;
    using Armada.Core.Services;

    /// <summary>
    /// Registers MCP tools for structured check-run execution and inspection.
    /// </summary>
    public static class McpCheckRunTools
    {
        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() }
        };

        /// <summary>
        /// Registers structured check-run MCP tools.
        /// </summary>
        public static void Register(RegisterToolDelegate register, DatabaseDriver database, CheckRunService checkRunService)
        {
            register(
                "get_check_run",
                "Inspect one structured check run including status, output, artifacts, parsed test summary, and coverage summary.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        checkRunId = new { type = "string", description = "Check run ID (chk_ prefix)" }
                    },
                    required = new[] { "checkRunId" }
                },
                async (args) =>
                {
                    CheckRunIdArgs request = JsonSerializer.Deserialize<CheckRunIdArgs>(args!.Value, _JsonOptions)
                        ?? throw new InvalidOperationException("Could not deserialize CheckRunIdArgs.");
                    CheckRun? run = await database.CheckRuns.ReadAsync(request.CheckRunId).ConfigureAwait(false);
                    if (run == null) return (object)new { Error = "Check run not found" };
                    return (object)run;
                });

            register(
                "run_check",
                "Start a structured check run for a vessel using the resolved workflow profile or an explicit command override.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        vesselId = new { type = "string", description = "Target vessel ID (vsl_ prefix)" },
                        workflowProfileId = new { type = "string", description = "Optional workflow profile override (wfp_ prefix)" },
                        missionId = new { type = "string", description = "Optional linked mission ID (msn_ prefix)" },
                        voyageId = new { type = "string", description = "Optional linked voyage ID (vyg_ prefix)" },
                        type = new { type = "string", description = "Check type such as Build, UnitTest, IntegrationTest, Deploy, SmokeTest, HealthCheck, or ReleaseVersioning" },
                        environmentName = new { type = "string", description = "Optional workflow-profile environment name for deploy/rollback/verification checks" },
                        label = new { type = "string", description = "Optional display label override" },
                        branchName = new { type = "string", description = "Optional branch association" },
                        commitHash = new { type = "string", description = "Optional commit-hash association" },
                        commandOverride = new { type = "string", description = "Optional raw shell command to execute instead of the workflow-profile command" }
                    },
                    required = new[] { "vesselId", "type" }
                },
                async (args) =>
                {
                    CheckRunRequest request = JsonSerializer.Deserialize<CheckRunRequest>(args!.Value, _JsonOptions)
                        ?? throw new InvalidOperationException("Could not deserialize CheckRunRequest.");
                    AuthContext auth = McpToolHelpers.ResolveCallerContext();
                    return (object)await checkRunService.RunAsync(auth, request).ConfigureAwait(false);
                });

            register(
                "retry_check_run",
                "Retry a previously completed structured check run using the same resolved scope and command context.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        checkRunId = new { type = "string", description = "Check run ID (chk_ prefix)" }
                    },
                    required = new[] { "checkRunId" }
                },
                async (args) =>
                {
                    CheckRunIdArgs request = JsonSerializer.Deserialize<CheckRunIdArgs>(args!.Value, _JsonOptions)
                        ?? throw new InvalidOperationException("Could not deserialize CheckRunIdArgs.");
                    AuthContext auth = McpToolHelpers.ResolveCallerContext();
                    return (object)await checkRunService.RetryAsync(auth, request.CheckRunId).ConfigureAwait(false);
                });
        }
    }
}
