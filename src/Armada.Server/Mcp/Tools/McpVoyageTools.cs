namespace Armada.Server.Mcp.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Armada.Core;
    using ArmadaConstants = Armada.Core.Constants;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// Registers MCP tools for voyage operations (dispatch, status, cancel, purge).
    /// </summary>
    public static class McpVoyageTools
    {
        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        /// <summary>
        /// Registers voyage MCP tools with the server.
        /// </summary>
        /// <param name="register">Delegate to register each tool.</param>
        /// <param name="database">Database driver for voyage data access.</param>
        /// <param name="admiral">Admiral service for voyage orchestration.</param>
        public static void Register(RegisterToolDelegate register, DatabaseDriver database, IAdmiralService admiral)
        {
            register(
                "armada_dispatch",
                "Dispatch a new voyage with missions to a vessel",
                new
                {
                    type = "object",
                    properties = new
                    {
                        title = new { type = "string", description = "Voyage title" },
                        description = new { type = "string", description = "Voyage description" },
                        vesselId = new { type = "string", description = "Target vessel ID" },
                        missions = new
                        {
                            type = "array",
                            items = new
                            {
                                type = "object",
                                properties = new
                                {
                                    title = new { type = "string" },
                                    description = new { type = "string" }
                                }
                            }
                        }
                    },
                    required = new[] { "title", "vesselId", "missions" }
                },
                async (args) =>
                {
                    VoyageDispatchArgs request = JsonSerializer.Deserialize<VoyageDispatchArgs>(args!.Value, _JsonOptions)!;
                    string title = request.Title;
                    string description = request.Description ?? "";
                    string vesselId = request.VesselId;
                    List<MissionDescription> missions = request.Missions;

                    // TODO: DispatchVoyageAsync creates voyage/missions internally; tenant scoping
                    // will require a tenantId parameter or overload in a future phase.
                    Voyage voyage = await admiral.DispatchVoyageAsync(title, description, vesselId, missions).ConfigureAwait(false);
                    return (object)voyage;
                });

            register(
                "armada_voyage_status",
                "Get status of a specific voyage. Returns summary with mission counts by default; opt-in to full mission details.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        voyageId = new { type = "string", description = "Voyage ID (vyg_ prefix)" },
                        summary = new { type = "boolean", description = "Return summary only with mission counts by status (default true). Set false to include mission objects." },
                        includeMissions = new { type = "boolean", description = "Include full mission objects (default false). Only used when summary=false." },
                        includeDescription = new { type = "boolean", description = "Include Description on embedded missions (default false)" },
                        includeDiffs = new { type = "boolean", description = "Include saved diff for each mission (default false)" },
                        includeLogs = new { type = "boolean", description = "Include log excerpt for each mission (default false). Currently reserved for future use." }
                    },
                    required = new[] { "voyageId" }
                },
                async (args) =>
                {
                    VoyageStatusArgs request = JsonSerializer.Deserialize<VoyageStatusArgs>(args!.Value, _JsonOptions)!;
                    string voyageId = request.VoyageId;
                    Voyage? voyage = await database.Voyages.ReadAsync(voyageId).ConfigureAwait(false);
                    if (voyage == null) return (object)new { Error = "Voyage not found" };

                    List<Mission> missions = await database.Missions.EnumerateByVoyageAsync(voyageId).ConfigureAwait(false);

                    // Default: summary mode (returns voyage metadata + mission counts by status, no mission objects)
                    bool isSummary = request.Summary != false;
                    if (isSummary)
                    {
                        Dictionary<string, int> counts = missions.GroupBy(m => m.Status.ToString())
                            .ToDictionary(g => g.Key, g => g.Count());
                        return (object)new
                        {
                            Voyage = new { voyage.Id, voyage.Title, voyage.Description, voyage.Status, voyage.CreatedUtc, voyage.LastUpdateUtc },
                            TotalMissions = missions.Count,
                            MissionCountsByStatus = counts
                        };
                    }

                    // Non-summary: optionally include mission objects
                    if (request.IncludeMissions != true)
                    {
                        return (object)new { Voyage = voyage, TotalMissions = missions.Count };
                    }

                    // Full mission objects with optional field inclusion
                    foreach (Mission m in missions)
                    {
                        m.DiffSnapshot = request.IncludeDiffs == true ? m.DiffSnapshot : null;
                        if (request.IncludeDescription != true) m.Description = null;
                        // includeLogs is reserved for future use -- logs are stored in external files
                        // and are not available on the mission object. This flag currently has no effect.
                    }
                    return (object)new { Voyage = voyage, Missions = missions };
                });

            register(
                "armada_cancel_voyage",
                "Cancel an entire voyage and all its pending missions",
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
                    VoyageIdArgs request = JsonSerializer.Deserialize<VoyageIdArgs>(args!.Value, _JsonOptions)!;
                    string voyageId = request.VoyageId;
                    Voyage? voyage = await database.Voyages.ReadAsync(voyageId).ConfigureAwait(false);
                    if (voyage == null) return (object)new { Error = "Voyage not found" };

                    // Cancel only pending/assigned missions (in-progress work is left running)
                    List<Mission> missions = await database.Missions.EnumerateByVoyageAsync(voyageId).ConfigureAwait(false);
                    int cancelledCount = 0;
                    foreach (Mission m in missions)
                    {
                        if (m.Status == MissionStatusEnum.Pending || m.Status == MissionStatusEnum.Assigned)
                        {
                            // Release the captain if this mission was assigned to one
                            if (!String.IsNullOrEmpty(m.CaptainId))
                            {
                                Captain? captain = await database.Captains.ReadAsync(m.CaptainId).ConfigureAwait(false);
                                if (captain != null && captain.CurrentMissionId == m.Id)
                                {
                                    List<Mission> otherMissions = (await database.Missions.EnumerateByCaptainAsync(captain.Id).ConfigureAwait(false))
                                        .Where(om => om.Id != m.Id && (om.Status == MissionStatusEnum.InProgress || om.Status == MissionStatusEnum.Assigned)).ToList();
                                    if (otherMissions.Count == 0)
                                    {
                                        captain.State = CaptainStateEnum.Idle;
                                        captain.CurrentMissionId = null;
                                        captain.CurrentDockId = null;
                                        captain.ProcessId = null;
                                        captain.RecoveryAttempts = 0;
                                        captain.LastUpdateUtc = DateTime.UtcNow;
                                        await database.Captains.UpdateAsync(captain).ConfigureAwait(false);
                                    }
                                }
                            }

                            m.Status = MissionStatusEnum.Cancelled;
                            m.CompletedUtc = DateTime.UtcNow;
                            m.LastUpdateUtc = DateTime.UtcNow;
                            await database.Missions.UpdateAsync(m).ConfigureAwait(false);
                            cancelledCount++;
                        }
                    }

                    voyage.Status = VoyageStatusEnum.Cancelled;
                    voyage.CompletedUtc = DateTime.UtcNow;
                    voyage.LastUpdateUtc = DateTime.UtcNow;
                    voyage = await database.Voyages.UpdateAsync(voyage).ConfigureAwait(false);
                    return (object)new { Voyage = voyage, CancelledMissions = cancelledCount };
                });

            register(
                "armada_purge_voyage",
                "Permanently delete a voyage and all its missions from the database. This cannot be undone.",
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
                    VoyageIdArgs request = JsonSerializer.Deserialize<VoyageIdArgs>(args!.Value, _JsonOptions)!;
                    string voyageId = request.VoyageId;
                    Voyage? voyage = await database.Voyages.ReadAsync(voyageId).ConfigureAwait(false);
                    if (voyage == null) return (object)new { Error = "Voyage not found" };

                    // Block deletion of active voyages
                    if (voyage.Status == VoyageStatusEnum.Open || voyage.Status == VoyageStatusEnum.InProgress)
                        return (object)new { Error = "Cannot delete voyage while status is " + voyage.Status + ". Cancel the voyage first." };

                    List<Mission> missions = await database.Missions.EnumerateByVoyageAsync(voyageId).ConfigureAwait(false);

                    // Block deletion if any missions are actively assigned or in progress
                    int activeMissionCount = missions.Count(m => m.Status == MissionStatusEnum.Assigned || m.Status == MissionStatusEnum.InProgress);
                    if (activeMissionCount > 0)
                        return (object)new { Error = "Cannot delete voyage with " + activeMissionCount + " active mission(s) in Assigned or InProgress status. Cancel or complete them first." };

                    foreach (Mission m in missions)
                    {
                        await database.Missions.DeleteAsync(m.Id).ConfigureAwait(false);
                    }

                    await database.Voyages.DeleteAsync(voyageId).ConfigureAwait(false);
                    return (object)new { Status = "deleted", VoyageId = voyageId, MissionsDeleted = missions.Count };
                });

            register(
                "armada_delete_voyages",
                "Permanently delete multiple voyages and their associated missions from the database by ID. Voyages that are Open/InProgress or have active missions are skipped. Returns a summary of deleted and skipped entries. This cannot be undone.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        ids = new { type = "array", items = new { type = "string" }, description = "List of voyage IDs to delete (vyg_ prefix)" }
                    },
                    required = new[] { "ids" }
                },
                async (args) =>
                {
                    DeleteMultipleArgs request = JsonSerializer.Deserialize<DeleteMultipleArgs>(args!.Value, _JsonOptions)!;
                    if (request.Ids == null || request.Ids.Count == 0)
                        return (object)new { Error = "ids is required and must not be empty" };

                    DeleteMultipleResult result = new DeleteMultipleResult();
                    foreach (string id in request.Ids)
                    {
                        if (String.IsNullOrEmpty(id))
                        {
                            result.Skipped.Add(new DeleteMultipleSkipped(id ?? "", "Empty ID"));
                            continue;
                        }
                        Voyage? voyage = await database.Voyages.ReadAsync(id).ConfigureAwait(false);
                        if (voyage == null)
                        {
                            result.Skipped.Add(new DeleteMultipleSkipped(id, "Not found"));
                            continue;
                        }
                        if (voyage.Status == VoyageStatusEnum.Open || voyage.Status == VoyageStatusEnum.InProgress)
                        {
                            result.Skipped.Add(new DeleteMultipleSkipped(id, "Cannot delete voyage while status is " + voyage.Status + ". Cancel the voyage first."));
                            continue;
                        }
                        List<Mission> missions = await database.Missions.EnumerateByVoyageAsync(id).ConfigureAwait(false);
                        int activeMissionCount = missions.Count(m => m.Status == MissionStatusEnum.Assigned || m.Status == MissionStatusEnum.InProgress);
                        if (activeMissionCount > 0)
                        {
                            result.Skipped.Add(new DeleteMultipleSkipped(id, "Cannot delete voyage with " + activeMissionCount + " active mission(s). Cancel or complete them first."));
                            continue;
                        }
                        foreach (Mission m in missions)
                        {
                            await database.Missions.DeleteAsync(m.Id).ConfigureAwait(false);
                        }
                        await database.Voyages.DeleteAsync(id).ConfigureAwait(false);
                        result.Deleted++;
                    }
                    result.ResolveStatus();
                    return (object)result;
                });
        }
    }
}
