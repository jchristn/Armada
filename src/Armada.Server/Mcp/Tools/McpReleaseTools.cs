namespace Armada.Server.Mcp.Tools
{
    using System;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using Armada.Core.Models;
    using Armada.Core.Services;

    /// <summary>
    /// Registers MCP tools for release drafting and inspection.
    /// </summary>
    public static class McpReleaseTools
    {
        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() }
        };

        /// <summary>
        /// Registers release MCP tools.
        /// </summary>
        public static void Register(RegisterToolDelegate register, ReleaseService releaseService)
        {
            register(
                "get_release",
                "Inspect one release record including linked voyages, missions, checks, versions, tags, notes, and derived artifacts.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        releaseId = new { type = "string", description = "Release ID (rel_ prefix)" }
                    },
                    required = new[] { "releaseId" }
                },
                async (args) =>
                {
                    ReleaseIdArgs request = JsonSerializer.Deserialize<ReleaseIdArgs>(args!.Value, _JsonOptions)
                        ?? throw new InvalidOperationException("Could not deserialize ReleaseIdArgs.");
                    AuthContext auth = McpToolHelpers.ResolveCallerContext();
                    Release? release = await releaseService.ReadAsync(auth, request.ReleaseId).ConfigureAwait(false);
                    if (release == null) return (object)new { Error = "Release not found" };
                    return (object)release;
                });

            register(
                "create_release",
                "Create a first-class release record from linked voyages, missions, and structured checks, with derived version, notes, and artifacts when available.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        vesselId = new { type = "string", description = "Optional vessel ID (vsl_ prefix)" },
                        workflowProfileId = new { type = "string", description = "Optional workflow profile override (wfp_ prefix)" },
                        title = new { type = "string", description = "Optional release title override" },
                        version = new { type = "string", description = "Optional version label" },
                        tagName = new { type = "string", description = "Optional git tag or image tag" },
                        summary = new { type = "string", description = "Optional short release summary" },
                        notes = new { type = "string", description = "Optional long-form release notes" },
                        status = new { type = "string", description = "Optional release status such as Draft, Candidate, or Shipped" },
                        voyageIds = new { type = "array", items = new { type = "string" }, description = "Linked voyage IDs (vyg_ prefix)" },
                        missionIds = new { type = "array", items = new { type = "string" }, description = "Linked mission IDs (msn_ prefix)" },
                        checkRunIds = new { type = "array", items = new { type = "string" }, description = "Linked check-run IDs (chk_ prefix)" }
                    }
                },
                async (args) =>
                {
                    ReleaseUpsertRequest request = JsonSerializer.Deserialize<ReleaseUpsertRequest>(args!.Value, _JsonOptions)
                        ?? throw new InvalidOperationException("Could not deserialize ReleaseUpsertRequest.");
                    AuthContext auth = McpToolHelpers.ResolveCallerContext();
                    return (object)await releaseService.CreateAsync(auth, request).ConfigureAwait(false);
                });
        }
    }
}
