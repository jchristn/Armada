namespace Armada.Server.Mcp.Tools
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Armada.Core;
    using ArmadaConstants = Armada.Core.Constants;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;

    /// <summary>
    /// Registers MCP tools for mission operations (status, create, update, cancel, purge, restart, transition, diff, log).
    /// </summary>
    public static class McpMissionTools
    {
        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        /// <summary>
        /// Registers mission MCP tools with the server.
        /// </summary>
        /// <param name="register">Delegate to register each tool.</param>
        /// <param name="database">Database driver for mission data access.</param>
        /// <param name="admiral">Admiral service for mission orchestration.</param>
        /// <param name="settings">Armada settings, or null if unavailable.</param>
        /// <param name="git">Git service for diff operations, or null if unavailable.</param>
        /// <param name="landingService">Optional landing service for mission landing operations.</param>
        public static void Register(RegisterToolDelegate register, DatabaseDriver database, IAdmiralService admiral, ArmadaSettings? settings, IGitService? git, ILandingService? landingService = null)
        {
            register(
                "armada_mission_status",
                "Get status of a specific mission",
                new
                {
                    type = "object",
                    properties = new
                    {
                        missionId = new { type = "string", description = "Mission ID (msn_ prefix)" }
                    },
                    required = new[] { "missionId" }
                },
                async (args) =>
                {
                    MissionIdArgs request = JsonSerializer.Deserialize<MissionIdArgs>(args!.Value, _JsonOptions)!;
                    string missionId = request.MissionId;
                    Mission? mission = await database.Missions.ReadAsync(missionId).ConfigureAwait(false);
                    if (mission == null) return (object)new { Error = "Mission not found" };
                    mission.DiffSnapshot = null;
                    return (object)mission;
                });

            register(
                "armada_create_mission",
                "Create and dispatch a standalone mission to a vessel",
                new
                {
                    type = "object",
                    properties = new
                    {
                        title = new { type = "string", description = "Mission title" },
                        description = new { type = "string", description = "Mission description/instructions" },
                        vesselId = new { type = "string", description = "Target vessel ID (vsl_ prefix)" },
                        voyageId = new { type = "string", description = "Optional voyage ID to associate with (vyg_ prefix)" }
                    },
                    required = new[] { "title", "description", "vesselId" }
                },
                async (args) =>
                {
                    MissionCreateArgs request = JsonSerializer.Deserialize<MissionCreateArgs>(args!.Value, _JsonOptions)!;
                    Mission mission = new Mission();
                    mission.TenantId = ArmadaConstants.DefaultTenantId;
                    mission.Title = request.Title;
                    mission.Description = request.Description;
                    mission.VesselId = request.VesselId;
                    if (request.VoyageId != null)
                        mission.VoyageId = request.VoyageId;
                    mission = await admiral.DispatchMissionAsync(mission).ConfigureAwait(false);
                    if (mission.Status == Armada.Core.Enums.MissionStatusEnum.Pending)
                    {
                        return (object)new
                        {
                            Mission = mission,
                            Warning = "Mission created but could not be assigned to any captain. It will be retried on the next health check cycle."
                        };
                    }
                    return (object)mission;
                });

            register(
                "armada_update_mission",
                "Update an existing mission's title, description, priority, vessel, voyage, branch, or PR URL. Operational fields (status, timestamps, captain) are managed by the system.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        missionId = new { type = "string", description = "Mission ID (msn_ prefix)" },
                        title = new { type = "string", description = "New mission title" },
                        description = new { type = "string", description = "New mission description/instructions" },
                        vesselId = new { type = "string", description = "New target vessel ID (vsl_ prefix)" },
                        voyageId = new { type = "string", description = "New voyage association (vyg_ prefix)" },
                        priority = new { type = "integer", description = "New priority (lower is higher priority)" },
                        branchName = new { type = "string", description = "Git branch name for this mission" },
                        prUrl = new { type = "string", description = "Pull request URL" },
                        parentMissionId = new { type = "string", description = "Parent mission ID for sub-tasks (msn_ prefix)" }
                    },
                    required = new[] { "missionId" }
                },
                async (args) =>
                {
                    MissionUpdateArgs request = JsonSerializer.Deserialize<MissionUpdateArgs>(args!.Value, _JsonOptions)!;
                    string missionId = request.MissionId;
                    Mission? mission = await database.Missions.ReadAsync(missionId).ConfigureAwait(false);
                    if (mission == null) return (object)new { Error = "Mission not found" };
                    if (request.Title != null)
                        mission.Title = request.Title;
                    if (request.Description != null)
                        mission.Description = request.Description;
                    if (request.VesselId != null)
                        mission.VesselId = request.VesselId;
                    if (request.VoyageId != null)
                        mission.VoyageId = request.VoyageId;
                    if (request.Priority.HasValue)
                        mission.Priority = request.Priority.Value;
                    if (request.BranchName != null)
                        mission.BranchName = request.BranchName;
                    if (request.PrUrl != null)
                        mission.PrUrl = request.PrUrl;
                    if (request.ParentMissionId != null)
                        mission.ParentMissionId = request.ParentMissionId;
                    mission.LastUpdateUtc = DateTime.UtcNow;
                    mission = await database.Missions.UpdateAsync(mission).ConfigureAwait(false);
                    return (object)mission;
                });

            register(
                "armada_cancel_mission",
                "Cancel a specific mission",
                new
                {
                    type = "object",
                    properties = new
                    {
                        missionId = new { type = "string", description = "Mission ID (msn_ prefix)" }
                    },
                    required = new[] { "missionId" }
                },
                async (args) =>
                {
                    MissionIdArgs request = JsonSerializer.Deserialize<MissionIdArgs>(args!.Value, _JsonOptions)!;
                    string missionId = request.MissionId;
                    Mission? mission = await database.Missions.ReadAsync(missionId).ConfigureAwait(false);
                    if (mission == null) return (object)new { Error = "Mission not found" };

                    // Release the captain if this mission was assigned to one
                    if (!String.IsNullOrEmpty(mission.CaptainId))
                    {
                        Captain? captain = await database.Captains.ReadAsync(mission.CaptainId).ConfigureAwait(false);
                        if (captain != null && captain.CurrentMissionId == mission.Id)
                        {
                            List<Mission> otherMissions = (await database.Missions.EnumerateByCaptainAsync(captain.Id).ConfigureAwait(false))
                                .Where(m => m.Id != mission.Id && (m.Status == MissionStatusEnum.InProgress || m.Status == MissionStatusEnum.Assigned)).ToList();
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

                    mission.Status = MissionStatusEnum.Cancelled;
                    mission.CompletedUtc = DateTime.UtcNow;
                    mission.LastUpdateUtc = DateTime.UtcNow;
                    mission = await database.Missions.UpdateAsync(mission).ConfigureAwait(false);
                    return (object)mission;
                });

            register(
                "armada_purge_mission",
                "Permanently delete a mission from the database. This cannot be undone.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        missionId = new { type = "string", description = "Mission ID (msn_ prefix)" }
                    },
                    required = new[] { "missionId" }
                },
                async (args) =>
                {
                    MissionIdArgs request = JsonSerializer.Deserialize<MissionIdArgs>(args!.Value, _JsonOptions)!;
                    string missionId = request.MissionId;
                    Mission? mission = await database.Missions.ReadAsync(missionId).ConfigureAwait(false);
                    if (mission == null) return (object)new { Error = "Mission not found" };

                    await database.Missions.DeleteAsync(missionId).ConfigureAwait(false);
                    return (object)new { Status = "deleted", MissionId = missionId };
                });

            register(
                "armada_delete_missions",
                "Permanently delete multiple missions from the database by ID. Returns a summary of deleted and skipped entries. This cannot be undone.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        ids = new { type = "array", items = new { type = "string" }, description = "List of mission IDs to delete (msn_ prefix)" }
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
                        Mission? mission = await database.Missions.ReadAsync(id).ConfigureAwait(false);
                        if (mission == null)
                        {
                            result.Skipped.Add(new DeleteMultipleSkipped(id, "Not found"));
                            continue;
                        }
                        await database.Missions.DeleteAsync(id).ConfigureAwait(false);
                        result.Deleted++;
                    }
                    result.ResolveStatus();
                    return (object)result;
                });

            register(
                "armada_restart_mission",
                "Restart a failed or cancelled mission, resetting it to Pending for re-dispatch. Optionally update title and description (instructions) before restarting.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        missionId = new { type = "string", description = "Mission ID (msn_ prefix)" },
                        title = new { type = "string", description = "Optional new title. Omit to keep original." },
                        description = new { type = "string", description = "Optional new description/instructions. Omit to keep original." }
                    },
                    required = new[] { "missionId" }
                },
                async (args) =>
                {
                    MissionRestartArgs request = JsonSerializer.Deserialize<MissionRestartArgs>(args!.Value, _JsonOptions)!;
                    string missionId = request.MissionId;
                    Mission? mission = await database.Missions.ReadAsync(missionId).ConfigureAwait(false);
                    if (mission == null) return (object)new { Error = "Mission not found" };

                    if (mission.Status != MissionStatusEnum.Failed && mission.Status != MissionStatusEnum.Cancelled)
                        return (object)new { Error = "Only Failed or Cancelled missions can be restarted (current: " + mission.Status + ")" };

                    if (!String.IsNullOrEmpty(request.Title)) mission.Title = request.Title;
                    if (!String.IsNullOrEmpty(request.Description)) mission.Description = request.Description;

                    mission.Status = MissionStatusEnum.Pending;
                    mission.CaptainId = null;
                    mission.BranchName = null;
                    mission.PrUrl = null;
                    mission.CommitHash = null;
                    mission.StartedUtc = null;
                    mission.CompletedUtc = null;
                    mission.LastUpdateUtc = DateTime.UtcNow;
                    mission = await database.Missions.UpdateAsync(mission).ConfigureAwait(false);

                    Signal signal = new Signal(SignalTypeEnum.Progress, "Mission " + missionId + " restarted");
                    signal.TenantId = ArmadaConstants.DefaultTenantId;
                    await database.Signals.CreateAsync(signal).ConfigureAwait(false);

                    return (object)mission;
                });

            register(
                "armada_retry_landing",
                "Retry landing for a mission in LandingFailed status. Rebases the mission branch onto the current target and re-attempts landing.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        missionId = new { type = "string", description = "Mission ID (msn_ prefix) to retry landing for" }
                    },
                    required = new[] { "missionId" }
                },
                async (args) =>
                {
                    if (landingService == null) return (object)new { Error = "Landing service not configured" };
                    MissionRetryLandingArgs request = JsonSerializer.Deserialize<MissionRetryLandingArgs>(args!.Value, _JsonOptions)!;
                    bool success = await landingService.RetryLandingAsync(request.MissionId).ConfigureAwait(false);
                    Mission? mission = await database.Missions.ReadAsync(request.MissionId).ConfigureAwait(false);
                    return (object)new { Success = success, Mission = mission };
                });

            register(
                "armada_transition_mission_status",
                "Transition a mission to a new status with validation. Valid transitions: Pending->Assigned, Assigned->InProgress, InProgress->Testing/Review/Complete/Failed, Testing->Review/InProgress/Complete/Failed, Review->Complete/InProgress/Failed. Most states allow ->Cancelled.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        missionId = new { type = "string", description = "Mission ID (msn_ prefix)" },
                        status = new { type = "string", description = "Target status: Pending, Assigned, InProgress, WorkProduced, Testing, Review, Complete, Failed, LandingFailed, Cancelled" }
                    },
                    required = new[] { "missionId", "status" }
                },
                async (args) =>
                {
                    MissionTransitionArgs request = JsonSerializer.Deserialize<MissionTransitionArgs>(args!.Value, _JsonOptions)!;
                    string missionId = request.MissionId;
                    string statusStr = request.Status;

                    Mission? mission = await database.Missions.ReadAsync(missionId).ConfigureAwait(false);
                    if (mission == null) return (object)new { Error = "Mission not found" };

                    if (!Enum.TryParse<MissionStatusEnum>(statusStr, true, out MissionStatusEnum newStatus))
                        return (object)new { Error = "Invalid status: " + statusStr };

                    if (!McpToolHelpers.IsValidTransition(mission.Status, newStatus))
                        return (object)new { Error = "Invalid transition from " + mission.Status + " to " + newStatus };

                    mission.Status = newStatus;
                    mission.LastUpdateUtc = DateTime.UtcNow;

                    if (newStatus == MissionStatusEnum.Complete || newStatus == MissionStatusEnum.Failed || newStatus == MissionStatusEnum.Cancelled)
                        mission.CompletedUtc = DateTime.UtcNow;

                    mission = await database.Missions.UpdateAsync(mission).ConfigureAwait(false);

                    Signal signal = new Signal(SignalTypeEnum.Progress, "Mission " + missionId + " transitioned to " + newStatus);
                    signal.TenantId = ArmadaConstants.DefaultTenantId;
                    if (!String.IsNullOrEmpty(mission.CaptainId)) signal.FromCaptainId = mission.CaptainId;
                    await database.Signals.CreateAsync(signal).ConfigureAwait(false);

                    return (object)mission;
                });

            // Diff and log tools require settings and git service
            if (settings != null)
            {
                register(
                    "armada_get_mission_diff",
                    "Get the git diff of changes made by a captain for a mission. Returns saved diff if available, otherwise live worktree diff.",
                    new
                    {
                        type = "object",
                        properties = new
                        {
                            missionId = new { type = "string", description = "Mission ID (msn_ prefix)" }
                        },
                        required = new[] { "missionId" }
                    },
                    async (args) =>
                    {
                        MissionIdArgs request = JsonSerializer.Deserialize<MissionIdArgs>(args!.Value, _JsonOptions)!;
                        string missionId = request.MissionId;
                        Mission? mission = await database.Missions.ReadAsync(missionId).ConfigureAwait(false);
                        if (mission == null) return (object)new { Error = "Mission not found" };

                        // Check for a saved diff file first
                        string savedDiffPath = Path.Combine(settings.LogDirectory, "diffs", missionId + ".diff");
                        if (File.Exists(savedDiffPath))
                        {
                            string savedDiff = await McpToolHelpers.ReadTextFileSafeAsync(savedDiffPath).ConfigureAwait(false);
                            return (object)new { MissionId = missionId, Branch = mission.BranchName ?? "", Diff = savedDiff };
                        }

                        // Check for database-persisted diff snapshot
                        if (!String.IsNullOrEmpty(mission.DiffSnapshot))
                        {
                            return (object)new { MissionId = missionId, Branch = mission.BranchName ?? "", Diff = mission.DiffSnapshot };
                        }

                        // Fall back to live worktree diff
                        if (git == null)
                            return (object)new { Error = "No saved diff available and git service not configured" };

                        Dock? dock = null;
                        if (!String.IsNullOrEmpty(mission.DockId))
                        {
                            dock = await database.Docks.ReadAsync(mission.DockId).ConfigureAwait(false);
                        }

                        if (dock == null && !String.IsNullOrEmpty(mission.CaptainId))
                        {
                            Captain? captain = await database.Captains.ReadAsync(mission.CaptainId).ConfigureAwait(false);
                            if (captain != null && !String.IsNullOrEmpty(captain.CurrentDockId))
                                dock = await database.Docks.ReadAsync(captain.CurrentDockId).ConfigureAwait(false);
                        }

                        if (dock == null && !String.IsNullOrEmpty(mission.BranchName) && !String.IsNullOrEmpty(mission.VesselId))
                        {
                            List<Dock> docks = await database.Docks.EnumerateByVesselAsync(mission.VesselId).ConfigureAwait(false);
                            dock = docks.FirstOrDefault(d => d.BranchName == mission.BranchName && d.Active);
                        }

                        if (dock == null || String.IsNullOrEmpty(dock.WorktreePath) || !Directory.Exists(dock.WorktreePath))
                            return (object)new { Error = "No diff available — worktree was already reclaimed and no saved diff exists" };

                        string baseBranch = "main";
                        if (!String.IsNullOrEmpty(mission.VesselId))
                        {
                            Vessel? vessel = await database.Vessels.ReadAsync(mission.VesselId).ConfigureAwait(false);
                            if (vessel != null) baseBranch = vessel.DefaultBranch;
                        }

                        string diff = await git.DiffAsync(dock.WorktreePath, baseBranch).ConfigureAwait(false);
                        return (object)new { MissionId = missionId, Branch = dock.BranchName ?? "", Diff = diff };
                    });

                register(
                    "armada_get_mission_log",
                    "Get the session log for a mission. Supports pagination.",
                    new
                    {
                        type = "object",
                        properties = new
                        {
                            missionId = new { type = "string", description = "Mission ID (msn_ prefix)" },
                            lines = new { type = "integer", description = "Number of lines to return (default 100)" },
                            offset = new { type = "integer", description = "Line offset to start from (default 0)" }
                        },
                        required = new[] { "missionId" }
                    },
                    async (args) =>
                    {
                        MissionLogArgs request = JsonSerializer.Deserialize<MissionLogArgs>(args!.Value, _JsonOptions)!;
                        string missionId = request.MissionId;
                        Mission? mission = await database.Missions.ReadAsync(missionId).ConfigureAwait(false);
                        if (mission == null) return (object)new { Error = "Mission not found" };

                        string logPath = Path.Combine(settings.LogDirectory, "missions", missionId + ".log");
                        if (!File.Exists(logPath))
                            return (object)new { MissionId = missionId, Log = "", Lines = 0, TotalLines = 0 };

                        string[] allLines = await McpToolHelpers.ReadLogFileSafeAsync(logPath).ConfigureAwait(false);
                        int totalLines = allLines.Length;

                        int offset = Math.Max(0, request.Offset ?? 0);
                        int lineCount = Math.Max(1, request.Lines ?? 100);

                        string[] slice = allLines.Skip(offset).Take(lineCount).ToArray();
                        string log = String.Join("\n", slice);
                        return (object)new { MissionId = missionId, Log = log, Lines = slice.Length, TotalLines = totalLines };
                    });
            }
        }
    }
}
