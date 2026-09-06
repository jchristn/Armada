namespace Armada.Server.Mcp.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
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
                "enumerate",
                "Find and browse entities with paginated, filtered, sorted access to: objectives, fleets, vessels, captains, missions, voyages, docks, signals, events, merge_queue, personas, prompt_templates, pipelines, playbooks, workflow_profiles, project_profiles, skills, check_runs, releases, deployments, incidents, runbooks, and runbook_executions. Returns paginated results with total counts. Filter by vesselId, fleetId, captainId, voyageId, status, date range, and more.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        entityType = new { type = "string", description = "Entity type to enumerate: objectives, jobs, model_endpoints, harbors, fleets, vessels, captains, missions, voyages, docks, signals, events, merge_queue, personas, prompt_templates, pipelines, playbooks, workflow_profiles, project_profiles, skills, check_runs, releases, deployments, incidents, runbooks, runbook_executions" },
                        pageNumber = new { type = "integer", description = "Page number (1-based, default 1)" },
                        pageSize = new { type = "integer", description = "Results per page (default 10, max 1000)" },
                        order = new { type = "string", description = "Sort order: CreatedAscending, CreatedDescending (default)" },
                        createdAfter = new { type = "string", description = "ISO 8601 timestamp — only return entities created after this time" },
                        createdBefore = new { type = "string", description = "ISO 8601 timestamp — only return entities created before this time" },
                        status = new { type = "string", description = "Filter by status (entity-specific: Pending/InProgress/Complete/Failed/Cancelled for missions, Active/Complete/Cancelled for voyages, Idle/Working/Stalled for captains, Queued/Testing/Passed/Failed/Landed/Cancelled for merge queue)" },
                        search = new { type = "string", description = "Optional free-text search where supported (currently releases)" },
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
                        case "objectives":
                        case "objective":
                        case "backlog":
                        case "backlog_item":
                        case "backlog-item":
                        case "backlog_items":
                        case "backlog-items":
                            ObjectiveService objectives = new ObjectiveService(database);
                            EnumerationResult<Objective> objectiveResult = await objectives.EnumerateAsync(
                                McpToolHelpers.CreateDefaultTenantAdminContext(),
                                new ObjectiveQuery
                                {
                                    PageNumber = query.PageNumber,
                                    PageSize = query.PageSize,
                                    VesselId = query.VesselId,
                                    VoyageId = query.VoyageId,
                                    MissionId = query.MissionId,
                                    Search = request.Search,
                                    Status = !String.IsNullOrWhiteSpace(query.Status) && Enum.TryParse(query.Status, true, out ObjectiveStatusEnum objectiveStatus)
                                        ? objectiveStatus
                                        : null
                                }).ConfigureAwait(false);
                            return (object)objectiveResult;
                        case "jobs":
                        case "job":
                            System.Collections.Generic.List<Job> allJobs = await database.Jobs.EnumerateAsync().ConfigureAwait(false);
                            int jobPageSize = query.PageSize > 0 ? query.PageSize : 25;
                            int jobPageNumber = query.PageNumber > 0 ? query.PageNumber : 1;
                            System.Collections.Generic.List<Job> jobPage = allJobs
                                .Skip((jobPageNumber - 1) * jobPageSize)
                                .Take(jobPageSize)
                                .ToList();
                            return (object)new { Success = true, PageNumber = jobPageNumber, PageSize = jobPageSize, TotalRecords = allJobs.Count, Objects = jobPage };
                        case "model_endpoints":
                        case "model-endpoints":
                        case "model_endpoint":
                        case "endpoints":
                            System.Collections.Generic.List<ModelEndpoint> allEndpoints = await database.ModelEndpoints.EnumerateAsync().ConfigureAwait(false);
                            int mepPageSize = query.PageSize > 0 ? query.PageSize : 25;
                            int mepPageNumber = query.PageNumber > 0 ? query.PageNumber : 1;
                            System.Collections.Generic.List<ModelEndpoint> mepPage = allEndpoints
                                .Skip((mepPageNumber - 1) * mepPageSize)
                                .Take(mepPageSize)
                                .ToList();
                            return (object)new { Success = true, PageNumber = mepPageNumber, PageSize = mepPageSize, TotalRecords = allEndpoints.Count, Objects = mepPage };
                        case "harbors":
                        case "harbor":
                            System.Collections.Generic.List<Harbor> allHarbors = await database.Harbors.EnumerateAsync().ConfigureAwait(false);
                            int hbrPageSize = query.PageSize > 0 ? query.PageSize : 25;
                            int hbrPageNumber = query.PageNumber > 0 ? query.PageNumber : 1;
                            System.Collections.Generic.List<Harbor> hbrPage = allHarbors
                                .Skip((hbrPageNumber - 1) * hbrPageSize)
                                .Take(hbrPageSize)
                                .ToList();
                            return (object)new { Success = true, PageNumber = hbrPageNumber, PageSize = hbrPageSize, TotalRecords = allHarbors.Count, Objects = hbrPage };
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
                                        v.HasGitHubTokenOverride,
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
                            if (request.IncludeDescription != true)
                            {
                                EnumerationResult<MissionSummary> missionSummaries = await database.Missions.EnumerateSummariesAsync(query).ConfigureAwait(false);
                                object projectedMissions = new
                                {
                                    missionSummaries.Success,
                                    missionSummaries.PageNumber,
                                    missionSummaries.PageSize,
                                    missionSummaries.TotalPages,
                                    missionSummaries.TotalRecords,
                                    Objects = missionSummaries.Objects.Select(m => new
                                    {
                                        m.Id, m.Title, m.Status, m.VesselId, m.VoyageId, m.CaptainId,
                                        m.BranchName, m.DockId, m.ProcessId, m.PrUrl, m.CommitHash,
                                        m.Priority, m.ParentMissionId, m.Persona, m.DependsOnMissionId,
                                        m.CreatedUtc, m.LastUpdateUtc, m.StartedUtc, m.CompletedUtc,
                                        m.DescriptionLength, m.DiffSnapshotLength, m.AgentOutputLength
                                    }).ToList(),
                                    missionSummaries.TotalMs
                                };
                                return (object)projectedMissions;
                            }
                            EnumerationResult<Mission> missions = await database.Missions.EnumerateAsync(query).ConfigureAwait(false);
                            foreach (Mission m in missions.Objects) m.DiffSnapshot = null;
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
                        case "releases":
                        case "release":
                            ReleaseQuery releaseQuery = new ReleaseQuery
                            {
                                PageNumber = query.PageNumber,
                                PageSize = query.PageSize,
                                FromUtc = query.CreatedAfter,
                                ToUtc = query.CreatedBefore,
                                Search = request.Search
                            };
                            if (!String.IsNullOrWhiteSpace(query.VesselId))
                                releaseQuery.VesselId = query.VesselId;
                            if (!String.IsNullOrWhiteSpace(query.Status) && Enum.TryParse(query.Status, true, out ReleaseStatusEnum releaseStatus))
                                releaseQuery.Status = releaseStatus;
                            EnumerationResult<Release> releases = await database.Releases.EnumerateAsync(releaseQuery).ConfigureAwait(false);
                            return (object)releases;
                        case "deployments":
                        case "deployment":
                            DeploymentQuery deploymentQuery = new DeploymentQuery
                            {
                                PageNumber = query.PageNumber,
                                PageSize = query.PageSize,
                                VesselId = query.VesselId,
                                MissionId = query.MissionId,
                                VoyageId = query.VoyageId,
                                FromUtc = query.CreatedAfter,
                                ToUtc = query.CreatedBefore,
                                Search = request.Search
                            };
                            if (!String.IsNullOrWhiteSpace(query.Status) && Enum.TryParse(query.Status, true, out DeploymentStatusEnum deploymentStatus))
                                deploymentQuery.Status = deploymentStatus;
                            EnumerationResult<Deployment> deployments = await database.Deployments.EnumerateAsync(deploymentQuery).ConfigureAwait(false);
                            return (object)deployments;
                        case "incidents":
                        case "incident":
                            IncidentService incidents = new IncidentService(database);
                            EnumerationResult<Incident> incidentResult = await incidents.EnumerateAsync(
                                McpToolHelpers.CreateDefaultTenantAdminContext(),
                                new IncidentQuery
                                {
                                    PageNumber = query.PageNumber,
                                    PageSize = query.PageSize,
                                    VesselId = query.VesselId,
                                    MissionId = query.MissionId,
                                    VoyageId = query.VoyageId,
                                    Search = request.Search
                                }).ConfigureAwait(false);
                            return (object)incidentResult;
                        case "runbooks":
                        case "runbook":
                            RunbookService runbooks = new RunbookService(database, new SyslogLogging.LoggingModule());
                            EnumerationResult<Runbook> runbookResult = await runbooks.EnumerateAsync(
                                McpToolHelpers.CreateDefaultTenantAdminContext(),
                                new RunbookQuery
                                {
                                    PageNumber = query.PageNumber,
                                    PageSize = query.PageSize,
                                    Search = request.Search
                                }).ConfigureAwait(false);
                            return (object)runbookResult;
                        case "runbook_executions":
                        case "runbook-executions":
                        case "runbookexecution":
                            RunbookService executionService = new RunbookService(database, new SyslogLogging.LoggingModule());
                            EnumerationResult<RunbookExecution> runbookExecutions = await executionService.EnumerateExecutionsAsync(
                                McpToolHelpers.CreateDefaultTenantAdminContext(),
                                new RunbookExecutionQuery
                                {
                                    PageNumber = query.PageNumber,
                                    PageSize = query.PageSize,
                                    Search = request.Search
                                }).ConfigureAwait(false);
                            return (object)runbookExecutions;
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
                        case "personas":
                        case "persona":
                            EnumerationResult<Persona> personas = await database.Personas.EnumerateAsync(query).ConfigureAwait(false);
                            return (object)personas;
                        case "prompt_templates":
                        case "prompt_template":
                        case "templates":
                        case "template":
                            EnumerationResult<PromptTemplate> templates = await database.PromptTemplates.EnumerateAsync(query).ConfigureAwait(false);
                            if (request.IncludeDescription != true)
                            {
                                object projectedTemplates = new
                                {
                                    templates.Success,
                                    templates.PageNumber,
                                    templates.PageSize,
                                    templates.TotalPages,
                                    templates.TotalRecords,
                                    Objects = templates.Objects.Select(t => new
                                    {
                                        t.Id, t.Name, t.Description, t.Category, t.IsBuiltIn, t.Active,
                                        t.CreatedUtc, t.LastUpdateUtc,
                                        ContentLength = t.Content?.Length ?? 0
                                    }).ToList(),
                                    templates.TotalMs
                                };
                                return (object)projectedTemplates;
                            }
                            return (object)templates;
                        case "pipelines":
                        case "pipeline":
                            EnumerationResult<Pipeline> pipelines = await database.Pipelines.EnumerateAsync(query).ConfigureAwait(false);
                            return (object)pipelines;
                        case "playbooks":
                        case "playbook":
                            EnumerationResult<Playbook> playbooks = await database.Playbooks.EnumerateAsync(query).ConfigureAwait(false);
                            if (request.IncludeDescription != true)
                            {
                                object projectedPlaybooks = new
                                {
                                    playbooks.Success,
                                    playbooks.PageNumber,
                                    playbooks.PageSize,
                                    playbooks.TotalPages,
                                    playbooks.TotalRecords,
                                    Objects = playbooks.Objects.Select(p => new
                                    {
                                        p.Id, p.TenantId, p.UserId, p.FileName, p.Description, p.Active,
                                        p.CreatedUtc, p.LastUpdateUtc,
                                        ContentLength = p.Content?.Length ?? 0
                                    }).ToList(),
                                    playbooks.TotalMs
                                };
                                return (object)projectedPlaybooks;
                            }
                            return (object)playbooks;
                        case "workflow_profiles":
                        case "workflow_profile":
                        case "workflowprofiles":
                            EnumerationResult<WorkflowProfile> workflowProfiles = await database.WorkflowProfiles.EnumerateAsync(new WorkflowProfileQuery
                            {
                                PageNumber = query.PageNumber,
                                PageSize = query.PageSize
                            }).ConfigureAwait(false);
                            return (object)workflowProfiles;
                        case "project_profiles":
                        case "project_profile":
                        case "projectprofiles":
                            EnumerationResult<ProjectProfile> projectProfiles = await database.ProjectProfiles.EnumerateAsync(new ProjectProfileQuery
                            {
                                PageNumber = query.PageNumber,
                                PageSize = query.PageSize
                            }).ConfigureAwait(false);
                            return (object)projectProfiles;
                        case "skills":
                        case "skill":
                            EnumerationResult<Skill> skills = await database.Skills.EnumerateAsync(new SkillQuery
                            {
                                PageNumber = query.PageNumber,
                                PageSize = query.PageSize
                            }).ConfigureAwait(false);
                            return (object)skills;
                        case "check_runs":
                        case "check_run":
                        case "checkruns":
                            EnumerationResult<CheckRun> checkRuns = await database.CheckRuns.EnumerateAsync(new CheckRunQuery
                            {
                                PageNumber = query.PageNumber,
                                PageSize = query.PageSize
                            }).ConfigureAwait(false);
                            return (object)checkRuns;
                        default:
                            return (object)new { Error = "Unknown entity type: " + entityType + ". Valid types: fleets, vessels, captains, missions, voyages, docks, signals, events, merge_queue, personas, prompt_templates, pipelines, playbooks, workflow_profiles, project_profiles, skills, check_runs, releases, jobs, model_endpoints" };
                    }
                });
        }
    }
}
