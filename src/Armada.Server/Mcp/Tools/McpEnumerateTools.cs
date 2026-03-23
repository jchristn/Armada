namespace Armada.Server.Mcp.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Core.Database;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// Registers the MCP enumerate tool for paginated, filtered entity browsing.
    /// </summary>
    public static class McpEnumerateTools
    {
        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        // NOTE: Enumerate operations use unscoped (admin-level) methods since MCP has no auth context.
        // When MCP auth is added, these should switch to tenant-scoped overloads.
        /// <summary>
        /// Registers the enumerate MCP tool with the server.
        /// </summary>
        /// <param name="register">Delegate to register each tool.</param>
        /// <param name="database">Database driver for entity enumeration.</param>
        /// <param name="mergeQueue">Optional merge queue service for merge queue enumeration.</param>
        public static void Register(RegisterToolDelegate register, DatabaseDriver database, IMergeQueueService? mergeQueue = null)
        {
            register(
                "armada_enumerate",
                "Find and browse entities with paginated, filtered, sorted access to: fleets, vessels, captains, missions, voyages, docks, signals, events, merge_queue. Returns paginated results with total counts. Filter by vesselId, fleetId, captainId, voyageId, status, date range, and more.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        entityType = new { type = "string", description = "Entity type to enumerate: fleets, vessels, captains, missions, voyages, docks, signals, events, merge_queue" },
                        pageNumber = new { type = "integer", description = "Page number (1-based, default 1)" },
                        pageSize = new { type = "integer", description = "Results per page (default 10, max 1000)" },
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
                        unreadOnly = new { type = "boolean", description = "Return only unread signals (signals only)" },
                        includeDescription = new { type = "boolean", description = "Include full Description on missions/voyages (default false; returns descriptionLength hint when false)" },
                        includeContext = new { type = "boolean", description = "Include ProjectContext and StyleGuide on vessels (default false; returns length hints when false)" },
                        includeTestOutput = new { type = "boolean", description = "Include TestOutput on merge queue entries (default false; returns testOutputLength hint when false)" },
                        includePayload = new { type = "boolean", description = "Include full Payload on events (default false; returns payloadLength hint when false)" },
                        includeMessage = new { type = "boolean", description = "Include full Message on signals (default false; returns messageLength hint when false)" }
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
                            if (request.IncludeContext != true)
                            {
                                object projectedVessels = new
                                {
                                    vessels.Success,
                                    vessels.PageNumber,
                                    vessels.PageSize,
                                    vessels.TotalPages,
                                    vessels.TotalRecords,
                                    Objects = vessels.Objects.Select(v => new
                                    {
                                        v.Id, v.FleetId, v.Name, v.RepoUrl, v.LocalPath, v.WorkingDirectory,
                                        v.DefaultBranch, v.LandingMode, v.BranchCleanupPolicy,
                                        v.AllowConcurrentMissions, v.Active, v.CreatedUtc, v.LastUpdateUtc,
                                        ProjectContextLength = v.ProjectContext?.Length ?? 0,
                                        StyleGuideLength = v.StyleGuide?.Length ?? 0,
                                        v.EnableModelContext,
                                        ModelContextLength = v.ModelContext?.Length ?? 0
                                    }).ToList(),
                                    vessels.TotalMs
                                };
                                return (object)projectedVessels;
                            }
                            return (object)vessels;
                        case "captains":
                        case "captain":
                            EnumerationResult<Captain> captains = await database.Captains.EnumerateAsync(query).ConfigureAwait(false);
                            object projectedCaptains = new
                            {
                                captains.Success,
                                captains.PageNumber,
                                captains.PageSize,
                                captains.TotalPages,
                                captains.TotalRecords,
                                Objects = captains.Objects.Select(c => new
                                {
                                    c.Id, c.TenantId, c.UserId, c.Name, c.Runtime, c.State,
                                    c.CurrentMissionId, c.CurrentDockId, c.ProcessId,
                                    c.RecoveryAttempts, c.LastHeartbeatUtc, c.CreatedUtc, c.LastUpdateUtc,
                                    SystemInstructionsLength = c.SystemInstructions?.Length ?? 0
                                }).ToList(),
                                captains.TotalMs
                            };
                            return (object)projectedCaptains;
                        case "missions":
                        case "mission":
                            EnumerationResult<Mission> missions = await database.Missions.EnumerateAsync(query).ConfigureAwait(false);
                            foreach (Mission m in missions.Objects) m.DiffSnapshot = null;
                            if (request.IncludeDescription != true)
                            {
                                object projectedMissions = new
                                {
                                    missions.Success,
                                    missions.PageNumber,
                                    missions.PageSize,
                                    missions.TotalPages,
                                    missions.TotalRecords,
                                    Objects = missions.Objects.Select(m => new
                                    {
                                        m.Id, m.Title, m.Status, m.VesselId, m.VoyageId, m.CaptainId,
                                        m.BranchName, m.DockId, m.ProcessId, m.PrUrl, m.CommitHash,
                                        m.Priority, m.ParentMissionId,
                                        m.CreatedUtc, m.LastUpdateUtc, m.StartedUtc, m.CompletedUtc,
                                        DescriptionLength = m.Description?.Length ?? 0
                                    }).ToList(),
                                    missions.TotalMs
                                };
                                return (object)projectedMissions;
                            }
                            return (object)missions;
                        case "voyages":
                        case "voyage":
                            EnumerationResult<Voyage> voyages = await database.Voyages.EnumerateAsync(query).ConfigureAwait(false);
                            if (request.IncludeDescription != true)
                            {
                                object projectedVoyages = new
                                {
                                    voyages.Success,
                                    voyages.PageNumber,
                                    voyages.PageSize,
                                    voyages.TotalPages,
                                    voyages.TotalRecords,
                                    Objects = voyages.Objects.Select(v => new
                                    {
                                        v.Id, v.Title, v.Status, v.CreatedUtc, v.CompletedUtc, v.LastUpdateUtc,
                                        v.AutoPush, v.AutoCreatePullRequests, v.AutoMergePullRequests, v.LandingMode,
                                        DescriptionLength = v.Description?.Length ?? 0
                                    }).ToList(),
                                    voyages.TotalMs
                                };
                                return (object)projectedVoyages;
                            }
                            return (object)voyages;
                        case "docks":
                        case "dock":
                            EnumerationResult<Dock> docks = await database.Docks.EnumerateAsync(query).ConfigureAwait(false);
                            return (object)docks;
                        case "signals":
                        case "signal":
                            EnumerationResult<Signal> signals = await database.Signals.EnumerateAsync(query).ConfigureAwait(false);
                            if (request.IncludeMessage != true)
                            {
                                object projectedSignals = new
                                {
                                    signals.Success,
                                    signals.PageNumber,
                                    signals.PageSize,
                                    signals.TotalPages,
                                    signals.TotalRecords,
                                    Objects = signals.Objects.Select(s => new
                                    {
                                        s.Id, s.FromCaptainId, s.ToCaptainId, s.Type, s.Read, s.CreatedUtc,
                                        PayloadLength = s.Payload?.Length ?? 0
                                    }).ToList(),
                                    signals.TotalMs
                                };
                                return (object)projectedSignals;
                            }
                            return (object)signals;
                        case "events":
                        case "event":
                            EnumerationResult<ArmadaEvent> events = await database.Events.EnumerateAsync(query).ConfigureAwait(false);
                            if (request.IncludePayload != true)
                            {
                                object projectedEvents = new
                                {
                                    events.Success,
                                    events.PageNumber,
                                    events.PageSize,
                                    events.TotalPages,
                                    events.TotalRecords,
                                    Objects = events.Objects.Select(e => new
                                    {
                                        e.Id, e.EventType, e.EntityType, e.EntityId,
                                        e.CaptainId, e.MissionId, e.VesselId, e.VoyageId,
                                        e.Message, e.CreatedUtc,
                                        PayloadLength = e.Payload?.Length ?? 0
                                    }).ToList(),
                                    events.TotalMs
                                };
                                return (object)projectedEvents;
                            }
                            return (object)events;
                        case "merge_queue":
                        case "merge-queue":
                        case "mergequeue":
                        case "merge_entries":
                            EnumerationResult<MergeEntry> mqResult = await database.MergeEntries.EnumerateAsync(query).ConfigureAwait(false);
                            if (request.IncludeTestOutput != true)
                            {
                                object projectedMerge = new
                                {
                                    mqResult.Success,
                                    mqResult.PageNumber,
                                    mqResult.PageSize,
                                    mqResult.TotalPages,
                                    mqResult.TotalRecords,
                                    Objects = mqResult.Objects.Select(e => new
                                    {
                                        e.Id, e.MissionId, e.VesselId, e.BranchName, e.TargetBranch,
                                        e.Status, e.Priority, e.BatchId, e.TestCommand, e.TestExitCode,
                                        e.CreatedUtc, e.LastUpdateUtc, e.TestStartedUtc, e.CompletedUtc,
                                        TestOutputLength = e.TestOutput?.Length ?? 0
                                    }).ToList(),
                                    mqResult.TotalMs
                                };
                                return (object)projectedMerge;
                            }
                            return (object)mqResult;
                        default:
                            return (object)new { Error = "Unknown entity type: " + entityType + ". Valid types: fleets, vessels, captains, missions, voyages, docks, signals, events, merge_queue" };
                    }
                });
        }
    }
}
