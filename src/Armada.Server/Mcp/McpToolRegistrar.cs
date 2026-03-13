namespace Armada.Server.Mcp
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.IO.Compression;
    using System.Linq;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Microsoft.Data.Sqlite;
    using Armada.Core;
    using ArmadaConstants = Armada.Core.Constants;
    using Armada.Core.Database;
    using Armada.Core.Database.Sqlite;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;

    /// <summary>
    /// Delegate matching the RegisterTool signature shared by McpHttpServer and McpServer.
    /// </summary>
    public delegate void RegisterToolDelegate(
        string name,
        string description,
        object inputSchema,
        Func<JsonElement?, Task<object>> handler);

    /// <summary>
    /// Registers all Armada MCP tools on any MCP server transport.
    /// Shared between the HTTP MCP server and the stdio MCP server.
    /// </summary>
    public static class McpToolRegistrar
    {
        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        /// <summary>
        /// Register all Armada tools using the provided registration delegate.
        /// </summary>
        /// <param name="register">Tool registration delegate (works with both McpHttpServer and McpServer).</param>
        /// <param name="database">Database driver for direct queries.</param>
        /// <param name="admiral">Admiral service for orchestration operations.</param>
        /// <param name="settings">Application settings for log/diff paths.</param>
        /// <param name="git">Git service for diff operations.</param>
        /// <param name="mergeQueue">Merge queue service.</param>
        /// <param name="onStop">Callback to stop the server.</param>
        /// <param name="onStopCaptain">Callback to kill a captain's agent process by captain ID. Called before RecallCaptainAsync.</param>
        public static void RegisterAll(
            RegisterToolDelegate register,
            DatabaseDriver database,
            IAdmiralService admiral,
            ArmadaSettings? settings = null,
            IGitService? git = null,
            IMergeQueueService? mergeQueue = null,
            Action? onStop = null,
            Func<string, Task>? onStopCaptain = null)
        {
            RegisterStatusTools(register, admiral, onStop);
            RegisterEnumerateTools(register, database, mergeQueue);
            RegisterFleetTools(register, database);
            RegisterVesselTools(register, database);
            RegisterVoyageTools(register, database, admiral);
            RegisterMissionTools(register, database, admiral, settings, git);
            RegisterCaptainTools(register, database, admiral, settings, onStopCaptain);
            RegisterSignalTools(register, database);
            RegisterEventTools(register, database);
            RegisterDockTools(register, database);
            if (mergeQueue != null) RegisterMergeQueueTools(register, mergeQueue);
            if (settings != null) RegisterBackupTools(register, database, settings);
        }

        #region Private-Methods

        private static void RegisterStatusTools(RegisterToolDelegate register, IAdmiralService admiral, Action? onStop)
        {
            register(
                "armada_status",
                "Get aggregate status of all active work in Armada",
                new { type = "object", properties = new { } },
                async (args) =>
                {
                    ArmadaStatus status = await admiral.GetStatusAsync().ConfigureAwait(false);
                    return (object)status;
                });

            if (onStop != null)
            {
                register(
                    "armada_stop_server",
                    "Initiate a graceful shutdown of the Admiral server",
                    new { type = "object", properties = new { } },
                    async (args) =>
                    {
                        _ = Task.Run(async () =>
                        {
                            await Task.Delay(500).ConfigureAwait(false);
                            onStop();
                        });
                        return (object)new { Status = "shutting_down" };
                    });
            }
        }

        private static void RegisterEnumerateTools(RegisterToolDelegate register, DatabaseDriver database, IMergeQueueService? mergeQueue = null)
        {
            register(
                "armada_enumerate",
                "PREFERRED tool for finding and browsing entities. Use this instead of armada_list_* tools to avoid context bloat. Supports paginated, filtered, and sorted access to: fleets, vessels, captains, missions, voyages, docks, signals, events, merge_queue. Returns paginated results with total counts. Filter by vesselId, fleetId, captainId, voyageId, status, date range, and more.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        entityType = new { type = "string", description = "Entity type to enumerate: fleets, vessels, captains, missions, voyages, docks, signals, events, merge_queue" },
                        pageNumber = new { type = "integer", description = "Page number (1-based, default 1)" },
                        pageSize = new { type = "integer", description = "Results per page (default 100, max 1000)" },
                        order = new { type = "string", description = "Sort order: CreatedAscending, CreatedDescending (default)" },
                        createdAfter = new { type = "string", description = "ISO 8601 timestamp — only return entities created after this time" },
                        createdBefore = new { type = "string", description = "ISO 8601 timestamp — only return entities created before this time" },
                        status = new { type = "string", description = "Filter by status (entity-specific: Pending/InProgress/Complete/Failed/Cancelled for missions, Active/Complete/Cancelled for voyages, Idle/Working/Stalled for captains, Queued/Testing/Passed/Failed/Landed/Cancelled for merge queue)" },
                        fleetId = new { type = "string", description = "Filter by fleet ID (vessels)" },
                        vesselId = new { type = "string", description = "Filter by vessel ID (missions, docks)" },
                        captainId = new { type = "string", description = "Filter by captain ID (missions, events, signals)" },
                        voyageId = new { type = "string", description = "Filter by voyage ID (missions, events)" },
                        missionId = new { type = "string", description = "Filter by mission ID (events)" },
                        eventType = new { type = "string", description = "Filter by event type string (events only)" },
                        signalType = new { type = "string", description = "Filter by signal type (signals only)" },
                        toCaptainId = new { type = "string", description = "Filter by recipient captain ID (signals only)" },
                        unreadOnly = new { type = "boolean", description = "Return only unread signals (signals only)" }
                    },
                    required = new[] { "entityType" }
                },
                async (args) =>
                {
                    EnumerateArgs request = JsonSerializer.Deserialize<EnumerateArgs>(args!.Value, _JsonOptions)!;
                    string entityType = (request.EntityType ?? "").ToLowerInvariant();
                    EnumerationQuery query = request.ToEnumerationQuery();

                    switch (entityType)
                    {
                        case "fleets":
                        case "fleet":
                            EnumerationResult<Fleet> fleets = await database.Fleets.EnumerateAsync(query).ConfigureAwait(false);
                            return (object)fleets;
                        case "vessels":
                        case "vessel":
                            EnumerationResult<Vessel> vessels = await database.Vessels.EnumerateAsync(query).ConfigureAwait(false);
                            return (object)vessels;
                        case "captains":
                        case "captain":
                            EnumerationResult<Captain> captains = await database.Captains.EnumerateAsync(query).ConfigureAwait(false);
                            return (object)captains;
                        case "missions":
                        case "mission":
                            EnumerationResult<Mission> missions = await database.Missions.EnumerateAsync(query).ConfigureAwait(false);
                            foreach (Mission m in missions.Objects) m.DiffSnapshot = null;
                            return (object)missions;
                        case "voyages":
                        case "voyage":
                            EnumerationResult<Voyage> voyages = await database.Voyages.EnumerateAsync(query).ConfigureAwait(false);
                            return (object)voyages;
                        case "docks":
                        case "dock":
                            EnumerationResult<Dock> docks = await database.Docks.EnumerateAsync(query).ConfigureAwait(false);
                            return (object)docks;
                        case "signals":
                        case "signal":
                            EnumerationResult<Signal> signals = await database.Signals.EnumerateAsync(query).ConfigureAwait(false);
                            return (object)signals;
                        case "events":
                        case "event":
                            EnumerationResult<ArmadaEvent> events = await database.Events.EnumerateAsync(query).ConfigureAwait(false);
                            return (object)events;
                        case "merge_queue":
                        case "merge-queue":
                        case "mergequeue":
                        case "merge_entries":
                            EnumerationResult<MergeEntry> mqResult = await database.MergeEntries.EnumerateAsync(query).ConfigureAwait(false);
                            return (object)mqResult;
                        default:
                            return (object)new { Error = "Unknown entity type: " + entityType + ". Valid types: fleets, vessels, captains, missions, voyages, docks, signals, events, merge_queue" };
                    }
                });
        }

        private static void RegisterFleetTools(RegisterToolDelegate register, DatabaseDriver database)
        {
            register(
                "armada_list_fleets",
                "List all registered fleets. NOTE: prefer armada_enumerate with entityType='fleets' for paginated results that conserve context.",
                new { type = "object", properties = new { } },
                async (args) =>
                {
                    List<Fleet> fleets = await database.Fleets.EnumerateAsync().ConfigureAwait(false);
                    return (object)fleets;
                });

            register(
                "armada_get_fleet",
                "Get details of a specific fleet including its vessels",
                new
                {
                    type = "object",
                    properties = new
                    {
                        fleetId = new { type = "string", description = "Fleet ID (flt_ prefix)" }
                    },
                    required = new[] { "fleetId" }
                },
                async (args) =>
                {
                    FleetIdArgs request = JsonSerializer.Deserialize<FleetIdArgs>(args!.Value, _JsonOptions)!;
                    string fleetId = request.FleetId;
                    Fleet? fleet = await database.Fleets.ReadAsync(fleetId).ConfigureAwait(false);
                    if (fleet == null) return (object)new { Error = "Fleet not found" };
                    List<Vessel> vessels = await database.Vessels.EnumerateByFleetAsync(fleetId).ConfigureAwait(false);
                    return (object)new { Fleet = fleet, Vessels = vessels };
                });

            register(
                "armada_create_fleet",
                "Create a new fleet (collection of repositories)",
                new
                {
                    type = "object",
                    properties = new
                    {
                        name = new { type = "string", description = "Fleet name" },
                        description = new { type = "string", description = "Fleet description" }
                    },
                    required = new[] { "name" }
                },
                async (args) =>
                {
                    FleetCreateArgs request = JsonSerializer.Deserialize<FleetCreateArgs>(args!.Value, _JsonOptions)!;
                    Fleet fleet = new Fleet();
                    fleet.Name = request.Name;
                    fleet.Description = request.Description ?? "";
                    fleet = await database.Fleets.CreateAsync(fleet).ConfigureAwait(false);
                    return (object)fleet;
                });

            register(
                "armada_update_fleet",
                "Update an existing fleet",
                new
                {
                    type = "object",
                    properties = new
                    {
                        fleetId = new { type = "string", description = "Fleet ID (flt_ prefix)" },
                        name = new { type = "string", description = "New fleet name" },
                        description = new { type = "string", description = "New fleet description" }
                    },
                    required = new[] { "fleetId" }
                },
                async (args) =>
                {
                    FleetUpdateArgs request = JsonSerializer.Deserialize<FleetUpdateArgs>(args!.Value, _JsonOptions)!;
                    string fleetId = request.FleetId;
                    Fleet? fleet = await database.Fleets.ReadAsync(fleetId).ConfigureAwait(false);
                    if (fleet == null) return (object)new { Error = "Fleet not found" };
                    if (request.Name != null)
                        fleet.Name = request.Name;
                    if (request.Description != null)
                        fleet.Description = request.Description;
                    fleet = await database.Fleets.UpdateAsync(fleet).ConfigureAwait(false);
                    return (object)fleet;
                });

            register(
                "armada_delete_fleet",
                "Delete a fleet by ID",
                new
                {
                    type = "object",
                    properties = new
                    {
                        fleetId = new { type = "string", description = "Fleet ID (flt_ prefix)" }
                    },
                    required = new[] { "fleetId" }
                },
                async (args) =>
                {
                    FleetIdArgs request = JsonSerializer.Deserialize<FleetIdArgs>(args!.Value, _JsonOptions)!;
                    string fleetId = request.FleetId;
                    bool exists = await database.Fleets.ExistsAsync(fleetId).ConfigureAwait(false);
                    if (!exists) return (object)new { Error = "Fleet not found" };
                    await database.Fleets.DeleteAsync(fleetId).ConfigureAwait(false);
                    return (object)new { Status = "deleted", FleetId = fleetId };
                });
        }

        private static void RegisterVesselTools(RegisterToolDelegate register, DatabaseDriver database)
        {
            register(
                "armada_list_vessels",
                "List all registered vessels (repositories). WARNING: returns ALL vessels unfiltered. Prefer armada_enumerate with entityType='vessels' for paginated/filtered results that conserve context.",
                new { type = "object", properties = new { } },
                async (args) =>
                {
                    List<Vessel> vessels = await database.Vessels.EnumerateAsync().ConfigureAwait(false);
                    return (object)vessels;
                });

            register(
                "armada_get_vessel",
                "Get details of a specific vessel (repository)",
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
                    VesselIdArgs request = JsonSerializer.Deserialize<VesselIdArgs>(args!.Value, _JsonOptions)!;
                    string vesselId = request.VesselId;
                    Vessel? vessel = await database.Vessels.ReadAsync(vesselId).ConfigureAwait(false);
                    if (vessel == null) return (object)new { Error = "Vessel not found" };
                    return (object)vessel;
                });

            register(
                "armada_add_vessel",
                "Register a new vessel (git repository) in a fleet",
                new
                {
                    type = "object",
                    properties = new
                    {
                        name = new { type = "string", description = "Display name for the vessel" },
                        repoUrl = new { type = "string", description = "Git repository URL (HTTPS or SSH)" },
                        fleetId = new { type = "string", description = "Fleet ID to add the vessel to" },
                        defaultBranch = new { type = "string", description = "Default branch name (defaults to main)" },
                        projectContext = new { type = "string", description = "Project context describing architecture, key files, and dependencies" },
                        styleGuide = new { type = "string", description = "Style guide describing naming conventions, patterns, and library preferences" },
                        workingDirectory = new { type = "string", description = "Optional local directory where completed mission changes will be pulled after merge" }
                    },
                    required = new[] { "name", "repoUrl", "fleetId" }
                },
                async (args) =>
                {
                    VesselAddArgs request = JsonSerializer.Deserialize<VesselAddArgs>(args!.Value, _JsonOptions)!;
                    Vessel vessel = new Vessel();
                    vessel.Name = request.Name;
                    vessel.RepoUrl = request.RepoUrl;
                    vessel.FleetId = request.FleetId;
                    vessel.DefaultBranch = request.DefaultBranch ?? "main";
                    vessel.ProjectContext = request.ProjectContext;
                    vessel.StyleGuide = request.StyleGuide;
                    vessel.WorkingDirectory = request.WorkingDirectory;
                    vessel = await database.Vessels.CreateAsync(vessel).ConfigureAwait(false);
                    return (object)vessel;
                });

            register(
                "armada_update_vessel",
                "Update an existing vessel's properties",
                new
                {
                    type = "object",
                    properties = new
                    {
                        vesselId = new { type = "string", description = "Vessel ID (vsl_ prefix)" },
                        name = new { type = "string", description = "New display name" },
                        repoUrl = new { type = "string", description = "New repository URL" },
                        defaultBranch = new { type = "string", description = "New default branch" },
                        projectContext = new { type = "string", description = "New project context" },
                        styleGuide = new { type = "string", description = "New style guide" },
                        workingDirectory = new { type = "string", description = "New local directory where completed mission changes will be pulled after merge" }
                    },
                    required = new[] { "vesselId" }
                },
                async (args) =>
                {
                    VesselUpdateArgs request = JsonSerializer.Deserialize<VesselUpdateArgs>(args!.Value, _JsonOptions)!;
                    string vesselId = request.VesselId;
                    Vessel? vessel = await database.Vessels.ReadAsync(vesselId).ConfigureAwait(false);
                    if (vessel == null) return (object)new { Error = "Vessel not found" };
                    if (request.Name != null)
                        vessel.Name = request.Name;
                    if (request.RepoUrl != null)
                        vessel.RepoUrl = request.RepoUrl;
                    if (request.DefaultBranch != null)
                        vessel.DefaultBranch = request.DefaultBranch;
                    if (request.ProjectContext != null)
                        vessel.ProjectContext = request.ProjectContext;
                    if (request.StyleGuide != null)
                        vessel.StyleGuide = request.StyleGuide;
                    if (request.WorkingDirectory != null)
                        vessel.WorkingDirectory = request.WorkingDirectory;
                    vessel = await database.Vessels.UpdateAsync(vessel).ConfigureAwait(false);
                    return (object)vessel;
                });

            register(
                "armada_delete_vessel",
                "Delete a vessel by ID",
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
                    VesselIdArgs request = JsonSerializer.Deserialize<VesselIdArgs>(args!.Value, _JsonOptions)!;
                    string vesselId = request.VesselId;
                    bool exists = await database.Vessels.ExistsAsync(vesselId).ConfigureAwait(false);
                    if (!exists) return (object)new { Error = "Vessel not found" };
                    await database.Vessels.DeleteAsync(vesselId).ConfigureAwait(false);
                    return (object)new { Status = "deleted", VesselId = vesselId };
                });

            register(
                "armada_update_vessel_context",
                "Update a vessel's project context and style guide without modifying other properties",
                new
                {
                    type = "object",
                    properties = new
                    {
                        vesselId = new { type = "string", description = "Vessel ID (vsl_ prefix)" },
                        projectContext = new { type = "string", description = "Project context describing architecture, key files, and dependencies" },
                        styleGuide = new { type = "string", description = "Style guide describing naming conventions, patterns, and library preferences" }
                    },
                    required = new[] { "vesselId" }
                },
                async (args) =>
                {
                    VesselContextArgs request = JsonSerializer.Deserialize<VesselContextArgs>(args!.Value, _JsonOptions)!;
                    string vesselId = request.VesselId;
                    Vessel? vessel = await database.Vessels.ReadAsync(vesselId).ConfigureAwait(false);
                    if (vessel == null) return (object)new { Error = "Vessel not found" };
                    if (request.ProjectContext != null)
                        vessel.ProjectContext = request.ProjectContext;
                    if (request.StyleGuide != null)
                        vessel.StyleGuide = request.StyleGuide;
                    vessel = await database.Vessels.UpdateAsync(vessel).ConfigureAwait(false);
                    return (object)vessel;
                });
        }

        private static void RegisterVoyageTools(RegisterToolDelegate register, DatabaseDriver database, IAdmiralService admiral)
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

                    Voyage voyage = await admiral.DispatchVoyageAsync(title, description, vesselId, missions).ConfigureAwait(false);
                    return (object)voyage;
                });

            register(
                "armada_list_voyages",
                "List all voyages, optionally filtered by status. WARNING: returns ALL matching voyages and can produce very large responses. Prefer armada_enumerate with entityType='voyages' for paginated/filtered results that conserve context.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        status = new { type = "string", description = "Filter by status: Active, Complete, Cancelled" }
                    }
                },
                async (args) =>
                {
                    StatusFilterArgs request = JsonSerializer.Deserialize<StatusFilterArgs>(args!.Value, _JsonOptions)!;
                    if (!String.IsNullOrEmpty(request.Status) && Enum.TryParse<VoyageStatusEnum>(request.Status, true, out VoyageStatusEnum status))
                    {
                        List<Voyage> filtered = await database.Voyages.EnumerateByStatusAsync(status).ConfigureAwait(false);
                        return (object)filtered;
                    }
                    List<Voyage> voyages = await database.Voyages.EnumerateAsync().ConfigureAwait(false);
                    return (object)voyages;
                });

            register(
                "armada_voyage_status",
                "Get status of a specific voyage with all its missions",
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
                    List<Mission> missions = await database.Missions.EnumerateByVoyageAsync(voyageId).ConfigureAwait(false);
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
        }

        private static void RegisterMissionTools(RegisterToolDelegate register, DatabaseDriver database, IAdmiralService admiral, ArmadaSettings? settings, IGitService? git)
        {
            register(
                "armada_list_missions",
                "List all missions, optionally filtered by status. WARNING: returns ALL matching missions and can produce very large responses. Prefer armada_enumerate with entityType='missions' for paginated/filtered results that conserve context.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        status = new { type = "string", description = "Filter by status: Pending, Assigned, InProgress, Testing, Review, Complete, Failed, Cancelled" }
                    }
                },
                async (args) =>
                {
                    StatusFilterArgs request = JsonSerializer.Deserialize<StatusFilterArgs>(args!.Value, _JsonOptions)!;
                    if (!String.IsNullOrEmpty(request.Status) && Enum.TryParse<MissionStatusEnum>(request.Status, true, out MissionStatusEnum status))
                    {
                        List<Mission> filtered = await database.Missions.EnumerateByStatusAsync(status).ConfigureAwait(false);
                        foreach (Mission m in filtered) m.DiffSnapshot = null;
                        return (object)filtered;
                    }
                    List<Mission> missions = await database.Missions.EnumerateAsync().ConfigureAwait(false);
                    foreach (Mission m in missions) m.DiffSnapshot = null;
                    return (object)missions;
                });

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
                    await database.Signals.CreateAsync(signal).ConfigureAwait(false);

                    return (object)mission;
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
                        status = new { type = "string", description = "Target status: Pending, Assigned, InProgress, Testing, Review, Complete, Failed, Cancelled" }
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

                    if (!IsValidTransition(mission.Status, newStatus))
                        return (object)new { Error = "Invalid transition from " + mission.Status + " to " + newStatus };

                    mission.Status = newStatus;
                    mission.LastUpdateUtc = DateTime.UtcNow;

                    if (newStatus == MissionStatusEnum.Complete || newStatus == MissionStatusEnum.Failed || newStatus == MissionStatusEnum.Cancelled)
                        mission.CompletedUtc = DateTime.UtcNow;

                    mission = await database.Missions.UpdateAsync(mission).ConfigureAwait(false);

                    Signal signal = new Signal(SignalTypeEnum.Progress, "Mission " + missionId + " transitioned to " + newStatus);
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
                            string savedDiff = await ReadTextFileSafeAsync(savedDiffPath).ConfigureAwait(false);
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

                        string[] allLines = await ReadLogFileSafeAsync(logPath).ConfigureAwait(false);
                        int totalLines = allLines.Length;

                        int offset = Math.Max(0, request.Offset ?? 0);
                        int lineCount = Math.Max(1, request.Lines ?? 100);

                        string[] slice = allLines.Skip(offset).Take(lineCount).ToArray();
                        string log = String.Join("\n", slice);
                        return (object)new { MissionId = missionId, Log = log, Lines = slice.Length, TotalLines = totalLines };
                    });
            }
        }

        private static void RegisterCaptainTools(RegisterToolDelegate register, DatabaseDriver database, IAdmiralService admiral, ArmadaSettings? settings, Func<string, Task>? onStopCaptain = null)
        {
            register(
                "armada_list_captains",
                "List all captains with their current state. NOTE: prefer armada_enumerate with entityType='captains' for paginated results that conserve context.",
                new { type = "object", properties = new { } },
                async (args) =>
                {
                    List<Captain> captains = await database.Captains.EnumerateAsync().ConfigureAwait(false);
                    return (object)captains;
                });

            register(
                "armada_get_captain",
                "Get details of a specific captain (AI agent)",
                new
                {
                    type = "object",
                    properties = new
                    {
                        captainId = new { type = "string", description = "Captain ID (cpt_ prefix)" }
                    },
                    required = new[] { "captainId" }
                },
                async (args) =>
                {
                    CaptainIdArgs request = JsonSerializer.Deserialize<CaptainIdArgs>(args!.Value, _JsonOptions)!;
                    string captainId = request.CaptainId;
                    Captain? captain = await database.Captains.ReadAsync(captainId).ConfigureAwait(false);
                    if (captain == null) return (object)new { Error = "Captain not found" };
                    return (object)captain;
                });

            register(
                "armada_create_captain",
                "Register a new captain (AI agent)",
                new
                {
                    type = "object",
                    properties = new
                    {
                        name = new { type = "string", description = "Captain display name" },
                        runtime = new { type = "string", description = "Agent runtime: ClaudeCode, Codex" }
                    },
                    required = new[] { "name" }
                },
                async (args) =>
                {
                    CaptainCreateArgs request = JsonSerializer.Deserialize<CaptainCreateArgs>(args!.Value, _JsonOptions)!;
                    Captain captain = new Captain();
                    captain.Name = request.Name;
                    if (!String.IsNullOrEmpty(request.Runtime) && Enum.TryParse<AgentRuntimeEnum>(request.Runtime, true, out AgentRuntimeEnum rt))
                        captain.Runtime = rt;
                    captain = await database.Captains.CreateAsync(captain).ConfigureAwait(false);
                    return (object)captain;
                });

            register(
                "armada_update_captain",
                "Update a captain's name or runtime. Operational fields (state, process, mission) are preserved.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        captainId = new { type = "string", description = "Captain ID (cpt_ prefix)" },
                        name = new { type = "string", description = "New display name" },
                        runtime = new { type = "string", description = "New agent runtime: ClaudeCode, Codex" }
                    },
                    required = new[] { "captainId" }
                },
                async (args) =>
                {
                    CaptainUpdateArgs request = JsonSerializer.Deserialize<CaptainUpdateArgs>(args!.Value, _JsonOptions)!;
                    string captainId = request.CaptainId;
                    Captain? captain = await database.Captains.ReadAsync(captainId).ConfigureAwait(false);
                    if (captain == null) return (object)new { Error = "Captain not found" };
                    if (request.Name != null)
                        captain.Name = request.Name;
                    if (!String.IsNullOrEmpty(request.Runtime) && Enum.TryParse<AgentRuntimeEnum>(request.Runtime, true, out AgentRuntimeEnum rt))
                        captain.Runtime = rt;
                    captain.LastUpdateUtc = DateTime.UtcNow;
                    captain = await database.Captains.UpdateAsync(captain).ConfigureAwait(false);
                    return (object)captain;
                });

            register(
                "armada_stop_captain",
                "Stop a specific captain agent, killing its process and recalling it to idle state",
                new
                {
                    type = "object",
                    properties = new
                    {
                        captainId = new { type = "string", description = "Captain ID (cpt_ prefix)" }
                    },
                    required = new[] { "captainId" }
                },
                async (args) =>
                {
                    CaptainIdArgs request = JsonSerializer.Deserialize<CaptainIdArgs>(args!.Value, _JsonOptions)!;
                    string captainId = request.CaptainId;
                    if (onStopCaptain != null)
                        await onStopCaptain(captainId).ConfigureAwait(false);
                    await admiral.RecallCaptainAsync(captainId).ConfigureAwait(false);
                    return (object)new { Status = "stopped", CaptainId = captainId };
                });

            register(
                "armada_stop_all",
                "Emergency stop all running captains",
                new { type = "object", properties = new { } },
                async (args) =>
                {
                    await admiral.RecallAllAsync().ConfigureAwait(false);
                    return (object)new { Status = "all_stopped" };
                });

            register(
                "armada_delete_captain",
                "Delete a captain. If working, the captain is recalled first.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        captainId = new { type = "string", description = "Captain ID (cpt_ prefix)" }
                    },
                    required = new[] { "captainId" }
                },
                async (args) =>
                {
                    CaptainIdArgs request = JsonSerializer.Deserialize<CaptainIdArgs>(args!.Value, _JsonOptions)!;
                    string captainId = request.CaptainId;
                    Captain? captain = await database.Captains.ReadAsync(captainId).ConfigureAwait(false);
                    if (captain == null) return (object)new { Error = "Captain not found" };

                    // Block deletion of working captains
                    if (captain.State == CaptainStateEnum.Working)
                        return (object)new { Error = "Cannot delete captain while state is Working. Stop the captain first." };

                    // Block deletion if captain has active missions
                    List<Mission> captainMissions = await database.Missions.EnumerateByCaptainAsync(captainId).ConfigureAwait(false);
                    int activeMissionCount = captainMissions.Count(m => m.Status == MissionStatusEnum.Assigned || m.Status == MissionStatusEnum.InProgress);
                    if (activeMissionCount > 0)
                        return (object)new { Error = "Cannot delete captain with " + activeMissionCount + " active mission(s) in Assigned or InProgress status. Cancel or complete them first." };

                    await database.Captains.DeleteAsync(captainId).ConfigureAwait(false);
                    return (object)new { Status = "deleted", CaptainId = captainId };
                });

            // Captain log requires settings
            if (settings != null)
            {
                register(
                    "armada_get_captain_log",
                    "Get the current session log for a captain. Supports pagination.",
                    new
                    {
                        type = "object",
                        properties = new
                        {
                            captainId = new { type = "string", description = "Captain ID (cpt_ prefix)" },
                            lines = new { type = "integer", description = "Number of lines to return (default 100)" },
                            offset = new { type = "integer", description = "Line offset to start from (default 0)" }
                        },
                        required = new[] { "captainId" }
                    },
                    async (args) =>
                    {
                        CaptainLogArgs request = JsonSerializer.Deserialize<CaptainLogArgs>(args!.Value, _JsonOptions)!;
                        string captainId = request.CaptainId;
                        Captain? captain = await database.Captains.ReadAsync(captainId).ConfigureAwait(false);
                        if (captain == null) return (object)new { Error = "Captain not found" };

                        string pointerPath = Path.Combine(settings.LogDirectory, "captains", captainId + ".current");
                        string? logPath = null;

                        if (File.Exists(pointerPath))
                        {
                            string target = (await ReadTextFileSafeAsync(pointerPath).ConfigureAwait(false)).Trim();
                            if (File.Exists(target))
                                logPath = target;
                        }

                        if (logPath == null)
                            return (object)new { CaptainId = captainId, Log = "", Lines = 0, TotalLines = 0 };

                        string[] allLines = await ReadLogFileSafeAsync(logPath).ConfigureAwait(false);
                        int totalLines = allLines.Length;

                        int offset = Math.Max(0, request.Offset ?? 0);
                        int lineCount = Math.Max(1, request.Lines ?? 100);

                        string[] slice = allLines.Skip(offset).Take(lineCount).ToArray();
                        string log = String.Join("\n", slice);
                        return (object)new { CaptainId = captainId, Log = log, Lines = slice.Length, TotalLines = totalLines };
                    });
            }
        }

        private static void RegisterSignalTools(RegisterToolDelegate register, DatabaseDriver database)
        {
            register(
                "armada_list_signals",
                "List signals (messages between admiral and captains). WARNING: can return large result sets. Prefer armada_enumerate with entityType='signals' for paginated/filtered results that conserve context.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        captainId = new { type = "string", description = "Filter signals by captain ID" }
                    }
                },
                async (args) =>
                {
                    CaptainIdArgs request = JsonSerializer.Deserialize<CaptainIdArgs>(args!.Value, _JsonOptions)!;
                    if (!String.IsNullOrEmpty(request.CaptainId))
                    {
                        List<Signal> filtered = await database.Signals.EnumerateByRecipientAsync(request.CaptainId, false).ConfigureAwait(false);
                        return (object)filtered;
                    }
                    List<Signal> signals = await database.Signals.EnumerateRecentAsync().ConfigureAwait(false);
                    return (object)signals;
                });

            register(
                "armada_send_signal",
                "Send a signal/message to a captain",
                new
                {
                    type = "object",
                    properties = new
                    {
                        captainId = new { type = "string", description = "Target captain ID" },
                        message = new { type = "string", description = "Signal message" }
                    },
                    required = new[] { "captainId", "message" }
                },
                async (args) =>
                {
                    SignalSendArgs request = JsonSerializer.Deserialize<SignalSendArgs>(args!.Value, _JsonOptions)!;
                    string captainId = request.CaptainId;
                    string message = request.Message;
                    Signal signal = new Signal(SignalTypeEnum.Mail, message);
                    signal.ToCaptainId = captainId;
                    signal = await database.Signals.CreateAsync(signal).ConfigureAwait(false);
                    return (object)signal;
                });
        }

        private static void RegisterEventTools(RegisterToolDelegate register, DatabaseDriver database)
        {
            register(
                "armada_list_events",
                "Query the event audit trail with optional filters. WARNING: can return large result sets. Prefer armada_enumerate with entityType='events' for paginated/filtered results that conserve context.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        missionId = new { type = "string", description = "Filter events by mission ID" },
                        captainId = new { type = "string", description = "Filter events by captain ID" },
                        voyageId = new { type = "string", description = "Filter events by voyage ID" },
                        limit = new { type = "integer", description = "Maximum number of events to return (default 50)" }
                    }
                },
                async (args) =>
                {
                    EventListArgs request = JsonSerializer.Deserialize<EventListArgs>(args!.Value, _JsonOptions)!;
                    int limit = request.Limit ?? 50;

                    List<ArmadaEvent> events = await database.Events.EnumerateRecentAsync(limit).ConfigureAwait(false);

                    // Apply optional filters
                    if (!String.IsNullOrEmpty(request.MissionId))
                    {
                        events = await database.Events.EnumerateByMissionAsync(request.MissionId, limit).ConfigureAwait(false);
                    }
                    else if (!String.IsNullOrEmpty(request.CaptainId))
                    {
                        events = await database.Events.EnumerateByCaptainAsync(request.CaptainId, limit).ConfigureAwait(false);
                    }
                    else if (!String.IsNullOrEmpty(request.VoyageId))
                    {
                        events = await database.Events.EnumerateByVoyageAsync(request.VoyageId, limit).ConfigureAwait(false);
                    }

                    return (object)events;
                });
        }

        private static void RegisterDockTools(RegisterToolDelegate register, DatabaseDriver database)
        {
            register(
                "armada_list_docks",
                "List all docks (git worktrees) with their status, optionally filtered by vessel. NOTE: prefer armada_enumerate with entityType='docks' for paginated/filtered results that conserve context.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        vesselId = new { type = "string", description = "Filter docks by vessel ID (vsl_ prefix)" }
                    }
                },
                async (args) =>
                {
                    VesselIdArgs request = JsonSerializer.Deserialize<VesselIdArgs>(args!.Value, _JsonOptions)!;
                    if (!String.IsNullOrEmpty(request.VesselId))
                    {
                        List<Dock> filtered = await database.Docks.EnumerateByVesselAsync(request.VesselId).ConfigureAwait(false);
                        return (object)filtered;
                    }
                    List<Dock> docks = await database.Docks.EnumerateAsync().ConfigureAwait(false);
                    return (object)docks;
                });
        }

        private static void RegisterMergeQueueTools(RegisterToolDelegate register, IMergeQueueService mergeQueue)
        {
            register(
                "armada_list_merge_queue",
                "List all entries in the merge queue. NOTE: prefer armada_enumerate with entityType='merge_queue' for paginated/filtered results that conserve context.",
                new { type = "object", properties = new { } },
                async (args) =>
                {
                    List<MergeEntry> entries = await mergeQueue.ListAsync().ConfigureAwait(false);
                    return (object)entries;
                });

            register(
                "armada_get_merge_entry",
                "Get details of a specific merge queue entry",
                new
                {
                    type = "object",
                    properties = new
                    {
                        entryId = new { type = "string", description = "Merge entry ID (mrg_ prefix)" }
                    },
                    required = new[] { "entryId" }
                },
                async (args) =>
                {
                    MergeEntryIdArgs request = JsonSerializer.Deserialize<MergeEntryIdArgs>(args!.Value, _JsonOptions)!;
                    string entryId = request.EntryId;
                    MergeEntry? entry = await mergeQueue.GetAsync(entryId).ConfigureAwait(false);
                    if (entry == null) return (object)new { Error = "Merge entry not found" };
                    return (object)entry;
                });

            register(
                "armada_enqueue_merge",
                "Add a branch to the merge queue for testing and merging",
                new
                {
                    type = "object",
                    properties = new
                    {
                        missionId = new { type = "string", description = "Associated mission ID (msn_ prefix)" },
                        vesselId = new { type = "string", description = "Target vessel ID (vsl_ prefix)" },
                        branchName = new { type = "string", description = "Branch name to merge" },
                        targetBranch = new { type = "string", description = "Target branch (defaults to main)" },
                        priority = new { type = "integer", description = "Queue priority (lower = higher, default 0)" },
                        testCommand = new { type = "string", description = "Custom test command to run" }
                    },
                    required = new[] { "vesselId", "branchName" }
                },
                async (args) =>
                {
                    MergeEnqueueArgs request = JsonSerializer.Deserialize<MergeEnqueueArgs>(args!.Value, _JsonOptions)!;
                    MergeEntry entry = new MergeEntry();
                    entry.VesselId = request.VesselId;
                    entry.BranchName = request.BranchName;
                    if (request.MissionId != null)
                        entry.MissionId = request.MissionId;
                    entry.TargetBranch = request.TargetBranch ?? "main";
                    if (request.Priority.HasValue)
                        entry.Priority = request.Priority.Value;
                    if (request.TestCommand != null)
                        entry.TestCommand = request.TestCommand;
                    entry = await mergeQueue.EnqueueAsync(entry).ConfigureAwait(false);
                    return (object)entry;
                });

            register(
                "armada_cancel_merge",
                "Cancel a queued merge entry",
                new
                {
                    type = "object",
                    properties = new
                    {
                        entryId = new { type = "string", description = "Merge entry ID (mrg_ prefix)" }
                    },
                    required = new[] { "entryId" }
                },
                async (args) =>
                {
                    MergeEntryIdArgs request = JsonSerializer.Deserialize<MergeEntryIdArgs>(args!.Value, _JsonOptions)!;
                    string entryId = request.EntryId;
                    await mergeQueue.CancelAsync(entryId).ConfigureAwait(false);
                    return (object)new { Status = "cancelled", EntryId = entryId };
                });

            register(
                "armada_process_merge_entry",
                "Process a single merge queue entry by ID: creates integration branch, runs tests, and lands if passing",
                new
                {
                    type = "object",
                    properties = new
                    {
                        entryId = new { type = "string", description = "Merge entry ID (mrg_ prefix)" }
                    },
                    required = new[] { "entryId" }
                },
                async (args) =>
                {
                    MergeEntryIdArgs request = JsonSerializer.Deserialize<MergeEntryIdArgs>(args!.Value, _JsonOptions)!;
                    string entryId = request.EntryId;
                    MergeEntry? entry = await mergeQueue.ProcessSingleAsync(entryId).ConfigureAwait(false);
                    if (entry == null) return (object)new { Error = "Merge entry not found or not in Queued status" };
                    return (object)entry;
                });

            register(
                "armada_process_merge_queue",
                "Process the merge queue: creates integration branches, runs tests, and lands passing batches",
                new { type = "object", properties = new { } },
                async (args) =>
                {
                    await mergeQueue.ProcessQueueAsync().ConfigureAwait(false);
                    return (object)new { Status = "processed" };
                });
        }

        private static bool IsValidTransition(MissionStatusEnum current, MissionStatusEnum target)
        {
            return (current, target) switch
            {
                (MissionStatusEnum.Pending, MissionStatusEnum.Assigned) => true,
                (MissionStatusEnum.Pending, MissionStatusEnum.Cancelled) => true,
                (MissionStatusEnum.Assigned, MissionStatusEnum.InProgress) => true,
                (MissionStatusEnum.Assigned, MissionStatusEnum.Cancelled) => true,
                (MissionStatusEnum.InProgress, MissionStatusEnum.Testing) => true,
                (MissionStatusEnum.InProgress, MissionStatusEnum.Review) => true,
                (MissionStatusEnum.InProgress, MissionStatusEnum.Complete) => true,
                (MissionStatusEnum.InProgress, MissionStatusEnum.Failed) => true,
                (MissionStatusEnum.InProgress, MissionStatusEnum.Cancelled) => true,
                (MissionStatusEnum.Testing, MissionStatusEnum.Review) => true,
                (MissionStatusEnum.Testing, MissionStatusEnum.InProgress) => true,
                (MissionStatusEnum.Testing, MissionStatusEnum.Complete) => true,
                (MissionStatusEnum.Testing, MissionStatusEnum.Failed) => true,
                (MissionStatusEnum.Review, MissionStatusEnum.Complete) => true,
                (MissionStatusEnum.Review, MissionStatusEnum.InProgress) => true,
                (MissionStatusEnum.Review, MissionStatusEnum.Failed) => true,
                _ => false
            };
        }

        private static void RegisterBackupTools(RegisterToolDelegate register, DatabaseDriver database, ArmadaSettings settings)
        {
            register(
                "armada_backup",
                "Create a ZIP backup of the Armada database and settings for disaster recovery. " +
                "Uses SQLite online backup API for a consistent snapshot.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        outputPath = new { type = "string", description = "Output path for the ZIP file. Default: ~/.armada/backups/armada-backup-{timestamp}.zip" }
                    }
                },
                async (args) =>
                {
                    BackupArgs backupArgs = args != null
                        ? JsonSerializer.Deserialize<BackupArgs>(args.Value, _JsonOptions) ?? new BackupArgs()
                        : new BackupArgs();

                    object result = await PerformBackupAsync(database, settings, backupArgs.OutputPath).ConfigureAwait(false);
                    return result;
                });

            register(
                "armada_restore",
                "Restore the Armada database and settings from a ZIP backup file. " +
                "Creates a safety backup of the current state before overwriting. Server restart recommended after restore.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        filePath = new { type = "string", description = "Path to the ZIP backup file to restore from" }
                    },
                    required = new[] { "filePath" }
                },
                async (args) =>
                {
                    RestoreArgs restoreArgs = args != null
                        ? JsonSerializer.Deserialize<RestoreArgs>(args.Value, _JsonOptions) ?? new RestoreArgs()
                        : new RestoreArgs();

                    if (String.IsNullOrEmpty(restoreArgs.FilePath))
                        throw new ArgumentException("filePath is required");

                    object result = await PerformRestoreAsync(database, settings, restoreArgs.FilePath).ConfigureAwait(false);
                    return result;
                });
        }

        /// <summary>
        /// Perform a backup of the database and settings into a ZIP file.
        /// </summary>
        internal static async Task<object> PerformBackupAsync(DatabaseDriver database, ArmadaSettings settings, string? outputPath)
        {
            string backupsDir = Path.Combine(ArmadaConstants.DefaultDataDirectory, "backups");
            Directory.CreateDirectory(backupsDir);

            string timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd-HHmmss");
            string zipPath = outputPath ?? Path.Combine(backupsDir, "armada-backup-" + timestamp + ".zip");

            // Ensure parent directory exists
            string? zipDir = Path.GetDirectoryName(zipPath);
            if (!String.IsNullOrEmpty(zipDir)) Directory.CreateDirectory(zipDir);

            string tempDbPath = Path.Combine(Path.GetTempPath(), "armada-backup-" + Guid.NewGuid().ToString("N") + ".db");

            try
            {
                // Use SQLite online backup API for a consistent snapshot
                // Pooling=False ensures Windows releases the file handle when the connection is disposed,
                // so that ZipFile.Open can read the temp file without "used by another process" errors.
                string sourceConnStr = "Data Source=" + settings.DatabasePath;
                string destConnStr = "Data Source=" + tempDbPath + ";Pooling=False";

                using (SqliteConnection sourceConn = new SqliteConnection(sourceConnStr))
                using (SqliteConnection destConn = new SqliteConnection(destConnStr))
                {
                    await sourceConn.OpenAsync().ConfigureAwait(false);
                    await destConn.OpenAsync().ConfigureAwait(false);
                    sourceConn.BackupDatabase(destConn);
                }

                // Get schema version
                int schemaVersion = 0;
                if (database is SqliteDatabaseDriver sqliteDriver)
                {
                    schemaVersion = await sqliteDriver.GetSchemaVersionAsync().ConfigureAwait(false);
                }

                // Get record counts
                Dictionary<string, long> recordCounts = await GetRecordCountsAsync(settings.DatabasePath).ConfigureAwait(false);

                // Build manifest
                object manifest = new
                {
                    backupTimestampUtc = DateTime.UtcNow.ToString("o"),
                    schemaVersion = schemaVersion,
                    armadaVersion = ArmadaConstants.ProductVersion,
                    recordCounts = recordCounts
                };

                string manifestJson = JsonSerializer.Serialize(manifest, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true
                });

                // Create ZIP
                if (File.Exists(zipPath)) File.Delete(zipPath);

                using (ZipArchive zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
                {
                    zip.CreateEntryFromFile(tempDbPath, "armada.db");

                    string settingsPath = ArmadaSettings.DefaultSettingsPath;
                    if (File.Exists(settingsPath))
                    {
                        zip.CreateEntryFromFile(settingsPath, "settings.json");
                    }

                    ZipArchiveEntry manifestEntry = zip.CreateEntry("manifest.json");
                    using (StreamWriter writer = new StreamWriter(manifestEntry.Open()))
                    {
                        await writer.WriteAsync(manifestJson).ConfigureAwait(false);
                    }
                }

                long sizeBytes = new FileInfo(zipPath).Length;

                return new
                {
                    Path = zipPath,
                    TimestampUtc = DateTime.UtcNow.ToString("o"),
                    SchemaVersion = schemaVersion,
                    SizeBytes = sizeBytes,
                    RecordCounts = recordCounts
                };
            }
            finally
            {
                if (File.Exists(tempDbPath)) File.Delete(tempDbPath);
            }
        }

        /// <summary>
        /// Restore the database and settings from a ZIP backup file.
        /// </summary>
        internal static async Task<object> PerformRestoreAsync(DatabaseDriver database, ArmadaSettings settings, string filePath, string? originalFilename = null)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("Backup file not found: " + filePath);

            // Validate ZIP contents
            using (ZipArchive zip = ZipFile.OpenRead(filePath))
            {
                ZipArchiveEntry? dbEntry = zip.GetEntry("armada.db");
                if (dbEntry == null)
                    throw new InvalidOperationException("ZIP does not contain armada.db entry");
            }

            string tempDir = Path.Combine(Path.GetTempPath(), "armada-restore-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                // Extract ZIP
                ZipFile.ExtractToDirectory(filePath, tempDir);

                string extractedDbPath = Path.Combine(tempDir, "armada.db");
                string extractedSettingsPath = Path.Combine(tempDir, "settings.json");

                // Validate extracted database
                string validateConnStr = "Data Source=" + extractedDbPath;
                using (SqliteConnection validateConn = new SqliteConnection(validateConnStr))
                {
                    await validateConn.OpenAsync().ConfigureAwait(false);
                    using (SqliteCommand cmd = validateConn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='schema_migrations';";
                        object? result = await cmd.ExecuteScalarAsync().ConfigureAwait(false);
                        if (result == null || result == DBNull.Value)
                            throw new InvalidOperationException("Extracted database does not contain schema_migrations table — not a valid Armada backup");
                    }
                }

                // Checkpoint the current live database
                string liveConnStr = "Data Source=" + settings.DatabasePath;
                using (SqliteConnection liveConn = new SqliteConnection(liveConnStr))
                {
                    await liveConn.OpenAsync().ConfigureAwait(false);
                    using (SqliteCommand cmd = liveConn.CreateCommand())
                    {
                        cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                    }
                }

                // Create safety backup of current state
                string timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd-HHmmss");
                string backupsDir = Path.Combine(ArmadaConstants.DefaultDataDirectory, "backups");
                Directory.CreateDirectory(backupsDir);
                string safetyBackupPath = Path.Combine(backupsDir, "pre-restore-" + timestamp + ".zip");

                await PerformBackupAsync(database, settings, safetyBackupPath).ConfigureAwait(false);

                // Replace database file
                File.Copy(extractedDbPath, settings.DatabasePath, overwrite: true);

                // Replace settings.json if present in backup
                bool settingsRestored = false;
                if (File.Exists(extractedSettingsPath))
                {
                    File.Copy(extractedSettingsPath, ArmadaSettings.DefaultSettingsPath, overwrite: true);
                    settingsRestored = true;
                }

                // Get schema version from restored database
                int schemaVersion = 0;
                if (database is SqliteDatabaseDriver sqliteDriver)
                {
                    schemaVersion = await sqliteDriver.GetSchemaVersionAsync().ConfigureAwait(false);
                }

                string displayName = !String.IsNullOrEmpty(originalFilename) ? originalFilename : Path.GetFileName(filePath);
                string message = "Database restored from " + displayName + ". ";
                if (!settingsRestored)
                    message += "Warning: settings.json was not found in the backup ZIP. ";
                message += "Restart the server to reload the restored data.";

                return new
                {
                    Status = "restored",
                    BackupPath = safetyBackupPath,
                    SchemaVersion = schemaVersion,
                    Message = message
                };
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, recursive: true); }
                    catch { /* best effort cleanup */ }
                }
            }
        }

        /// <summary>
        /// Get record counts for all Armada tables.
        /// </summary>
        private static async Task<Dictionary<string, long>> GetRecordCountsAsync(string databasePath)
        {
            Dictionary<string, long> counts = new Dictionary<string, long>();
            string[] tables = new[] { "fleets", "vessels", "captains", "missions", "voyages", "docks", "signals", "events", "merge_entries" };

            string connStr = "Data Source=" + databasePath;
            using (SqliteConnection conn = new SqliteConnection(connStr))
            {
                await conn.OpenAsync().ConfigureAwait(false);

                foreach (string table in tables)
                {
                    using (SqliteCommand cmd = conn.CreateCommand())
                    {
                        // Verify table exists before counting
                        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name=@table;";
                        cmd.Parameters.AddWithValue("@table", table);
                        object? exists = await cmd.ExecuteScalarAsync().ConfigureAwait(false);
                        if (exists == null || exists == DBNull.Value)
                        {
                            counts[table] = 0;
                            continue;
                        }
                    }

                    using (SqliteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT COUNT(*) FROM " + table + ";";
                        object? result = await cmd.ExecuteScalarAsync().ConfigureAwait(false);
                        counts[table] = (result != null && result != DBNull.Value) ? Convert.ToInt64(result) : 0;
                    }
                }
            }

            return counts;
        }

        /// <summary>
        /// Read a text file safely, allowing concurrent writes from other processes.
        /// </summary>
        private static async Task<string> ReadTextFileSafeAsync(string path)
        {
            using FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using StreamReader reader = new StreamReader(fs);
            return await reader.ReadToEndAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Read a log file safely as lines, allowing concurrent writes from other processes.
        /// </summary>
        private static async Task<string[]> ReadLogFileSafeAsync(string path)
        {
            string content = await ReadTextFileSafeAsync(path).ConfigureAwait(false);
            return content.Split('\n');
        }

        #endregion
    }
}
