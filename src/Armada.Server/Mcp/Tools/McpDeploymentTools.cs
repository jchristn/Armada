namespace Armada.Server.Mcp.Tools
{
    using System;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using Armada.Core.Models;
    using Armada.Core.Services;

    /// <summary>
    /// Registers MCP tools for deployment inspection and bounded actions.
    /// </summary>
    public static class McpDeploymentTools
    {
        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() }
        };

        /// <summary>
        /// Registers deployment MCP tools.
        /// </summary>
        public static void Register(RegisterToolDelegate register, DeploymentService deploymentService)
        {
            register(
                "get_deployment",
                "Inspect one deployment including approval, verification, rollback, linked checks, and request-history evidence.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        deploymentId = new { type = "string", description = "Deployment ID (dpl_ prefix)" }
                    },
                    required = new[] { "deploymentId" }
                },
                async (args) =>
                {
                    DeploymentIdArgs request = JsonSerializer.Deserialize<DeploymentIdArgs>(args!.Value, _JsonOptions)
                        ?? throw new InvalidOperationException("Could not deserialize DeploymentIdArgs.");
                    AuthContext auth = McpToolHelpers.ResolveCallerContext();
                    Deployment? deployment = await deploymentService.ReadAsync(auth, request.DeploymentId).ConfigureAwait(false);
                    if (deployment == null) return (object)new { Error = "Deployment not found" };
                    return (object)deployment;
                });

            register(
                "create_deployment",
                "Create a bounded deployment record and execute it immediately when approval is not required.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        vesselId = new { type = "string", description = "Optional vessel ID (vsl_ prefix)" },
                        workflowProfileId = new { type = "string", description = "Optional workflow profile override (wfp_ prefix)" },
                        environmentId = new { type = "string", description = "Optional environment ID (env_ prefix)" },
                        environmentName = new { type = "string", description = "Optional environment name" },
                        releaseId = new { type = "string", description = "Optional linked release ID (rel_ prefix)" },
                        missionId = new { type = "string", description = "Optional linked mission ID (msn_ prefix)" },
                        voyageId = new { type = "string", description = "Optional linked voyage ID (vyg_ prefix)" },
                        title = new { type = "string", description = "Optional deployment title" },
                        sourceRef = new { type = "string", description = "Optional source ref such as a branch, tag, or commit" },
                        summary = new { type = "string", description = "Optional short summary" },
                        notes = new { type = "string", description = "Optional operator notes" },
                        autoExecute = new { type = "boolean", description = "Whether to execute immediately when approval is not required" }
                    }
                },
                async (args) =>
                {
                    DeploymentUpsertRequest request = JsonSerializer.Deserialize<DeploymentUpsertRequest>(args!.Value, _JsonOptions)
                        ?? throw new InvalidOperationException("Could not deserialize DeploymentUpsertRequest.");
                    AuthContext auth = McpToolHelpers.ResolveCallerContext();
                    return (object)await deploymentService.CreateAsync(auth, request).ConfigureAwait(false);
                });

            register(
                "approve_deployment",
                "Approve a pending deployment and begin execution.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        deploymentId = new { type = "string", description = "Deployment ID (dpl_ prefix)" },
                        comment = new { type = "string", description = "Optional approval comment" }
                    },
                    required = new[] { "deploymentId" }
                },
                async (args) =>
                {
                    JsonElement value = args!.Value;
                    string deploymentId = value.GetProperty("deploymentId").GetString() ?? String.Empty;
                    string? comment = value.TryGetProperty("comment", out JsonElement commentElement) ? commentElement.GetString() : null;
                    AuthContext auth = McpToolHelpers.ResolveCallerContext();
                    return (object)await deploymentService.ApproveAsync(auth, deploymentId, comment).ConfigureAwait(false);
                });

            register(
                "verify_deployment",
                "Run post-deploy verification for an existing deployment.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        deploymentId = new { type = "string", description = "Deployment ID (dpl_ prefix)" }
                    },
                    required = new[] { "deploymentId" }
                },
                async (args) =>
                {
                    DeploymentIdArgs request = JsonSerializer.Deserialize<DeploymentIdArgs>(args!.Value, _JsonOptions)
                        ?? throw new InvalidOperationException("Could not deserialize DeploymentIdArgs.");
                    AuthContext auth = McpToolHelpers.ResolveCallerContext();
                    return (object)await deploymentService.VerifyAsync(auth, request.DeploymentId).ConfigureAwait(false);
                });

            register(
                "rollback_deployment",
                "Run the configured rollback flow for an existing deployment.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        deploymentId = new { type = "string", description = "Deployment ID (dpl_ prefix)" }
                    },
                    required = new[] { "deploymentId" }
                },
                async (args) =>
                {
                    DeploymentIdArgs request = JsonSerializer.Deserialize<DeploymentIdArgs>(args!.Value, _JsonOptions)
                        ?? throw new InvalidOperationException("Could not deserialize DeploymentIdArgs.");
                    AuthContext auth = McpToolHelpers.ResolveCallerContext();
                    return (object)await deploymentService.RollbackAsync(auth, request.DeploymentId).ConfigureAwait(false);
                });
        }
    }
}
