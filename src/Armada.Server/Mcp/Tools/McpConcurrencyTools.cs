namespace Armada.Server.Mcp.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using Armada.Core.Database;
    using Armada.Core.Models;
    using Armada.Core.Services;

    /// <summary>Registers MCP tools for the mission tree and vessel concurrency.</summary>
    public static class McpConcurrencyTools
    {
        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        /// <summary>Registers the tools.</summary>
        /// <param name="register">Delegate to register each tool.</param>
        /// <param name="database">Database driver.</param>
        /// <exception cref="ArgumentNullException">When an argument is null.</exception>
        public static void Register(RegisterToolDelegate register, DatabaseDriver database)
        {
            if (register == null) throw new ArgumentNullException(nameof(register));
            if (database == null) throw new ArgumentNullException(nameof(database));
            ConcurrencyReportService reports = new ConcurrencyReportService(database);

            register(
                "voyage_tree",
                "Get a voyage's missions arranged as logical missions. Each node holds Mission (the root's summary), Stages (its later pipeline stages, in order) and Children (child logical missions by ParentMissionId). Roots lists the top-level logical missions; TotalMissions and MaxDepth describe the whole tree.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        voyageId = new { type = "string", description = "Voyage ID (vyg_ prefix)" }
                    },
                    required = new[] { "voyageId" }
                },
                async (args) =>
                {
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    VoyageTreeArgs request = JsonSerializer.Deserialize<VoyageTreeArgs>(args!.Value, _JsonOptions)!;
                    Voyage? voyage = await McpCallerScope.ReadVoyageAsync(database, caller, request.VoyageId).ConfigureAwait(false);
                    if (voyage == null) return (object)McpToolError.NotFound("Voyage not found");
                    List<MissionSummary> missions = await database.Missions.EnumerateSummariesByVoyageAsync(voyage.Id).ConfigureAwait(false);
                    return (object)MissionTreeBuilder.BuildVoyageTree(voyage, missions);
                });

            register(
                "vessel_concurrency",
                "Get a vessel's concurrency: voyage slots in use against MaxParallelVoyages, the voyages holding them (running and Pending counts), Pending missions waiting on a recorded gate (longest first, at most 100; WaitingCount is the total) and slot wait p50/p95/max over 24 hours and 7 days. Callers who are not tenant administrators see only their own voyages and missions listed.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        vesselId = new { type = "string", description = "Vessel ID (vsl_ prefix)" }
                    },
                    required = new[] { "vesselId" }
                },
                async (args) =>
                {
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    VesselConcurrencyArgs request = JsonSerializer.Deserialize<VesselConcurrencyArgs>(args!.Value, _JsonOptions)!;
                    Vessel? vessel = await McpCallerScope.ReadVesselAsync(database, caller, request.VesselId).ConfigureAwait(false);
                    if (vessel == null) return (object)McpToolError.NotFound("Vessel not found");
                    return (object)await reports.GetVesselConcurrencyAsync(vessel,
                        caller.IsAdmin ? null : caller.TenantId, caller.IsAdmin || caller.IsTenantAdmin ? null : caller.UserId).ConfigureAwait(false);
                });
        }
    }
}
