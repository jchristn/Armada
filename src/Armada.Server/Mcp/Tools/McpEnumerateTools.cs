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

        // Owned (Category A) entities are enumerated through EnumerationScope.EnumerateScopedAsync using the
        // caller identity resolved from the authenticated MCP transport (McpToolHelpers.ResolveCallerContext),
        // so a regular caller sees only their own records. Configuration (Category B) entities remain
        // tenant-visible here; per-object ownership editing is enforced by their services/routes.
        /// <summary>
        /// Registers the enumerate MCP tool with the server.
        /// </summary>
        /// <param name="register">Delegate to register each tool.</param>
        /// <param name="database">Database driver for entity enumeration.</param>
        /// <param name="mergeQueue">Optional merge queue service for merge queue enumeration.</param>
        /// <param name="inbox">Optional inbox service, for entityType inbox.</param>
        /// <param name="cliPermissions">Optional CLI permission service, for entityTypes cli_permission_rules and cli_permission_requests.</param>
        /// <param name="blackBox">Optional black box service, for entityType blackbox.</param>
        public static void Register(RegisterToolDelegate register, DatabaseDriver database, IMergeQueueService? mergeQueue = null, InboxService? inbox = null, CliPermissionService? cliPermissions = null, Armada.Core.BlackBox.IBlackBoxService? blackBox = null)
        {
            register(
                "enumerate",
                "Find and browse entities with paginated, filtered, sorted access to: inbox (the needs-you items, most urgent first), cli_permission_rules, cli_permission_requests, objectives, fleets, vessels, captains, missions, voyages, docks, signals, events, merge_queue, personas, prompt_templates, pipelines, playbooks, workflow_profiles, project_profiles, skills, check_runs, releases, deployments, incidents, runbooks, runbook_executions, vessel_import_batch, fleet_action, fleet_action_run, fleet_action_run_target, vessel_health, and blackbox (black box entry summaries, newest first). Returns paginated results with total counts. Filter by vesselId, fleetId, captainId, voyageId, status, date range, and more.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        entityType = new { type = "string", description = "Entity type to enumerate: inbox (needs-you items, most urgent first), cli_permission_rules (vesselId and captainId filter), cli_permission_requests (newest first; status, missionId, threadId, captainId, vesselId filter), objectives, jobs, model_endpoints, harbors, fleets, vessels, captains, missions, voyages, docks, signals, events, merge_queue, personas, prompt_templates, pipelines, playbooks, workflow_profiles, project_profiles, skills, check_runs, releases, deployments, incidents, runbooks, runbook_executions, vessel_import_batch, fleet_action, fleet_action_run, fleet_action_run_target (requires runId), vessel_health (status filters overall status, fleetId and search narrow by fleet and vessel name), blackbox (black box entry summaries; status filters resolution, search matches reason, summary and notes; vesselId, voyageId, missionId, captainId, fleetId and the date range filter)" },
                        pageNumber = new { type = "integer", description = "Page number (1-based, default 1)" },
                        pageSize = new { type = "integer", description = "Results per page (default 10, normalized into 1..1000; a few entity types apply their REST route's lower maximum, and the result's PageSize reports the size used)" },
                        order = new { type = "string", description = "Sort order: CreatedAscending, CreatedDescending (default)" },
                        createdAfter = new { type = "string", description = "ISO 8601 timestamp — only return entities created after this time" },
                        createdBefore = new { type = "string", description = "ISO 8601 timestamp — only return entities created before this time" },
                        status = new { type = "string", description = "Filter by status (entity-specific: " + McpToolHelpers.EnumNames<MissionStatusEnum>().Replace(", ", "/") + " for missions, " + McpToolHelpers.EnumNames<VoyageStatusEnum>().Replace(", ", "/") + " for voyages, " + McpToolHelpers.EnumNames<CaptainStateEnum>().Replace(", ", "/") + " for captains, " + McpToolHelpers.EnumNames<MergeStatusEnum>().Replace(", ", "/") + " for merge queue, " + McpToolHelpers.EnumNames<VesselImportBatchStatusEnum>().Replace(", ", "/") + " for vessel_import_batch)" },
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
                        includeDescription = new { type = "boolean", description = "Add the full Description to mission/voyage rows (default false; returns descriptionLength hint when false). Rows stay scalar: use get_mission / voyage_status for the full model" },
                        includeContext = new { type = "boolean", description = "Include ProjectContext and StyleGuide on vessels (default false; returns length hints when false)" },
                        includeTestOutput = new { type = "boolean", description = "Include TestOutput on merge queue entries (default false; returns testOutputLength hint when false)" },
                        includePayload = new { type = "boolean", description = "Include full Payload on events (default false; returns payloadLength hint when false)" },
                        includeMessage = new { type = "boolean", description = "Include full Message on signals (default false; returns messageLength hint when false)" },
                        runId = new { type = "string", description = "Fleet action run ID (far_ prefix); required for fleet_action_run_target" },
                        threadId = new { type = "string", description = "Filter by Ask thread ID (ath_ prefix; cli_permission_requests)" },
                        includeOutput = new { type = "boolean", description = "Include RenderedText, OutputText and ErrorText on fleet_action_run_target (default false; returns outputLength, errorLength and renderedLength hints when false)" },
                        includeInactive = new { type = "boolean", description = "Include soft-deleted built-in actions for fleet_action (default false)" }
                    },
                    required = new[] { "entityType" }
                },
                async (args) =>
                {
                    EnumerateArgs request = JsonSerializer.Deserialize<EnumerateArgs>(args!.Value, _JsonOptions)!;
                    string entityType = (request.EntityType ?? "").ToLowerInvariant();
                    EnumerationQuery query = request.ToEnumerationQuery();
                    // Scope owned (Category A) entities to the authenticated MCP caller. When the caller is a
                    // regular user this returns only their records; a tenant/global admin sees the tenant/system,
                    // and an unauthenticated local caller falls back to the default tenant-admin identity.
                    AuthContext callerCtx = McpToolHelpers.ResolveCallerContext();

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
                                McpToolHelpers.ResolveCallerContext(),
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
                            // Same scope as GET /api/v1/jobs: a regular user sees their own jobs, a tenant admin the
                            // tenant's, and only a global admin sees every tenant.
                            JobQuery jobQuery = new JobQuery();
                            jobQuery.PageNumber = query.PageNumber > 0 ? query.PageNumber : 1;
                            jobQuery.PageSize = query.PageSize > 0 ? query.PageSize : 25;
                            if (!callerCtx.IsAdmin) jobQuery.TenantId = callerCtx.TenantId ?? Constants.DefaultTenantId;
                            if (!callerCtx.IsAdmin && !callerCtx.IsTenantAdmin) jobQuery.UserId = callerCtx.UserId;
                            return (object)await database.Jobs.EnumeratePageAsync(jobQuery).ConfigureAwait(false);
                        case "model_endpoints":
                        case "model-endpoints":
                        case "model_endpoint":
                        case "endpoints":
                            return (object)await new ModelEndpointService(database, new SyslogLogging.LoggingModule()).EnumeratePageAsync(callerCtx, query).ConfigureAwait(false);
                        case "inbox":
                            if (inbox == null) return (object)McpToolError.Unavailable("The inbox is not available on this server.");
                            return (object)await inbox.EnumerateAsync(callerCtx, query).ConfigureAwait(false);
                        case "cli_permission_rules":
                        case "cli-permission-rules":
                            if (cliPermissions == null) return (object)McpToolError.Unavailable("CLI permissions are not available on this server.");
                            CliPermissionRuleQuery ruleQuery = new CliPermissionRuleQuery
                            {
                                VesselId = String.IsNullOrWhiteSpace(query.VesselId) ? null : query.VesselId,
                                CaptainId = String.IsNullOrWhiteSpace(query.CaptainId) ? null : query.CaptainId
                            };
                            return (object)await cliPermissions.EnumerateRulesAsync(callerCtx, ruleQuery, query.PageNumber, query.PageSize).ConfigureAwait(false);
                        case "cli_permission_requests":
                        case "cli-permission-requests":
                            if (cliPermissions == null) return (object)McpToolError.Unavailable("CLI permissions are not available on this server.");
                            CliPermissionRequestEnumerateRequest requestQuery = new CliPermissionRequestEnumerateRequest
                            {
                                Status = query.Status,
                                MissionId = query.MissionId,
                                CaptainId = query.CaptainId,
                                VesselId = query.VesselId,
                                ThreadId = request.ThreadId
                            };
                            CliPermissionRequestQuery requestFilter;
                            try
                            {
                                requestFilter = requestQuery.ToFilter();
                            }
                            catch (ArgumentException)
                            {
                                return (object)McpToolError.InvalidArgument("status must be one of " + McpToolHelpers.EnumNames<CliPermissionRequestStatusEnum>() + ".");
                            }

                            return (object)await cliPermissions.EnumerateAsync(callerCtx, requestFilter, query.PageNumber, query.PageSize).ConfigureAwait(false);
                        case "harbors":
                        case "harbor":
                            return (object)await new HarborService(database, new SyslogLogging.LoggingModule()).EnumeratePageAsync(callerCtx, query).ConfigureAwait(false);
                        case "vessel_health":
                        case "vessel-health":
                        case "health":
                            VesselHealthEnumerateRequest healthRequest = new VesselHealthEnumerateRequest();
                            healthRequest.PageNumber = query.PageNumber;
                            healthRequest.PageSize = query.PageSize;
                            healthRequest.FleetId = query.FleetId;
                            healthRequest.NameContains = String.IsNullOrWhiteSpace(request.Search) ? null : request.Search;
                            if (!String.IsNullOrWhiteSpace(query.Status)
                                && !Char.IsDigit(query.Status.Trim()[0])
                                && Enum.TryParse(query.Status.Trim(), true, out VesselHealthStatusEnum healthStatus)
                                && Enum.IsDefined(typeof(VesselHealthStatusEnum), healthStatus))
                                healthRequest.OverallStatus = new List<VesselHealthStatusEnum> { healthStatus };
                            return (object)await database.VesselHealth.EnumerateAsync(callerCtx.TenantId ?? Constants.DefaultTenantId, healthRequest).ConfigureAwait(false);
                        case "fleets":
                        case "fleet":
                            EnumerationResult<Fleet> fleets = await Armada.Core.Models.EnumerationScope.EnumerateScopedAsync(callerCtx, query, q => database.Fleets.EnumerateAsync(q), (t, q) => database.Fleets.EnumerateAsync(t, q), (t, u, q) => database.Fleets.EnumerateAsync(t, u, q)).ConfigureAwait(false);
                            return (object)fleets;
                        case "vessels":
                        case "vessel":
                            EnumerationResult<Vessel> vessels = await Armada.Core.Models.EnumerationScope.EnumerateScopedAsync(callerCtx, query, q => database.Vessels.EnumerateAsync(q), (t, q) => database.Vessels.EnumerateAsync(t, q), (t, u, q) => database.Vessels.EnumerateAsync(t, u, q)).ConfigureAwait(false);
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
                                        v.AllowConcurrentMissions, v.AutoApprove, v.Active, v.CreatedUtc, v.LastUpdateUtc,
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
                            EnumerationResult<Captain> captains = await Armada.Core.Models.EnumerationScope.EnumerateScopedAsync(callerCtx, query, q => database.Captains.EnumerateAsync(q), (t, q) => database.Captains.EnumerateAsync(t, q), (t, u, q) => database.Captains.EnumerateAsync(t, u, q)).ConfigureAwait(false);
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
                                EnumerationResult<MissionSummary> missionSummaries = await Armada.Core.Models.EnumerationScope.EnumerateScopedAsync(callerCtx, query, q => database.Missions.EnumerateSummariesAsync(q), (t, q) => database.Missions.EnumerateSummariesAsync(t, q), (t, u, q) => database.Missions.EnumerateSummariesAsync(t, u, q)).ConfigureAwait(false);
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
                            // includeDescription adds the Description to the summary fields. The full Mission model (and its
                            // playbook and assignment-blocker lists) is read with get_mission / mission_status, never embedded
                            // in a page here.
                            EnumerationResult<Mission> missions = await Armada.Core.Models.EnumerationScope.EnumerateScopedAsync(callerCtx, query, q => database.Missions.EnumerateAsync(q), (t, q) => database.Missions.EnumerateAsync(t, q), (t, u, q) => database.Missions.EnumerateAsync(t, u, q)).ConfigureAwait(false);
                            object describedMissions = new
                            {
                                missions.Success,
                                missions.PageNumber,
                                missions.PageSize,
                                missions.TotalPages,
                                missions.TotalRecords,
                                Objects = missions.Objects.Select(m => new
                                {
                                    m.Id, m.Title, m.Description, m.Status, m.VesselId, m.VoyageId, m.CaptainId,
                                    m.BranchName, m.DockId, m.ProcessId, m.PrUrl, m.CommitHash,
                                    m.Priority, m.ParentMissionId, m.Persona, m.DependsOnMissionId,
                                    m.CreatedUtc, m.LastUpdateUtc, m.StartedUtc, m.CompletedUtc,
                                    DescriptionLength = m.Description?.Length ?? 0,
                                    DiffSnapshotLength = m.DiffSnapshot?.Length ?? 0,
                                    AgentOutputLength = m.AgentOutput?.Length ?? 0
                                }).ToList(),
                                missions.TotalMs
                            };
                            return (object)describedMissions;
                        case "voyages":
                        case "voyage":
                            EnumerationResult<Voyage> voyages = await Armada.Core.Models.EnumerationScope.EnumerateScopedAsync(callerCtx, query, q => database.Voyages.EnumerateAsync(q), (t, q) => database.Voyages.EnumerateAsync(t, q), (t, u, q) => database.Voyages.EnumerateAsync(t, u, q)).ConfigureAwait(false);
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
                            // includeDescription adds the Description; SelectedPlaybooks and the voyage's missions are read
                            // with voyage_status, never embedded in a page here.
                            object describedVoyages = new
                            {
                                voyages.Success,
                                voyages.PageNumber,
                                voyages.PageSize,
                                voyages.TotalPages,
                                voyages.TotalRecords,
                                Objects = voyages.Objects.Select(v => new
                                {
                                    v.Id, v.Title, v.Description, v.Status, v.CreatedUtc, v.CompletedUtc, v.LastUpdateUtc,
                                    v.AutoPush, v.AutoCreatePullRequests, v.AutoMergePullRequests, v.LandingMode,
                                    DescriptionLength = v.Description?.Length ?? 0
                                }).ToList(),
                                voyages.TotalMs
                            };
                            return (object)describedVoyages;
                        case "docks":
                        case "dock":
                            EnumerationResult<Dock> docks = await Armada.Core.Models.EnumerationScope.EnumerateScopedAsync(callerCtx, query, q => database.Docks.EnumerateAsync(q), (t, q) => database.Docks.EnumerateAsync(t, q), (t, u, q) => database.Docks.EnumerateAsync(t, u, q)).ConfigureAwait(false);
                            return (object)docks;
                        case "signals":
                        case "signal":
                            EnumerationResult<Signal> signals = await Armada.Core.Models.EnumerationScope.EnumerateScopedAsync(callerCtx, query, q => database.Signals.EnumerateAsync(q), (t, q) => database.Signals.EnumerateAsync(t, q), (t, u, q) => database.Signals.EnumerateAsync(t, u, q)).ConfigureAwait(false);
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
                            EnumerationResult<ArmadaEvent> events = await Armada.Core.Models.EnumerationScope.EnumerateScopedAsync(callerCtx, query, q => database.Events.EnumerateAsync(q), (t, q) => database.Events.EnumerateAsync(t, q), (t, u, q) => database.Events.EnumerateAsync(t, u, q)).ConfigureAwait(false);
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
                            ApplyOwnerScope(callerCtx, out string? releaseTenant, out string? releaseUser);
                            releaseQuery.TenantId = releaseTenant;
                            releaseQuery.UserId = releaseUser;
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
                            ApplyOwnerScope(callerCtx, out string? deploymentTenant, out string? deploymentUser);
                            deploymentQuery.TenantId = deploymentTenant;
                            deploymentQuery.UserId = deploymentUser;
                            if (!String.IsNullOrWhiteSpace(query.Status) && Enum.TryParse(query.Status, true, out DeploymentStatusEnum deploymentStatus))
                                deploymentQuery.Status = deploymentStatus;
                            EnumerationResult<Deployment> deployments = await database.Deployments.EnumerateAsync(deploymentQuery).ConfigureAwait(false);
                            return (object)deployments;
                        case "incidents":
                        case "incident":
                            IncidentService incidents = new IncidentService(database);
                            EnumerationResult<Incident> incidentResult = await incidents.EnumerateAsync(
                                McpToolHelpers.ResolveCallerContext(),
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
                                McpToolHelpers.ResolveCallerContext(),
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
                                McpToolHelpers.ResolveCallerContext(),
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
                            EnumerationResult<MergeEntry> mqResult = await Armada.Core.Models.EnumerationScope.EnumerateScopedAsync(callerCtx, query, q => database.MergeEntries.EnumerateAsync(q), (t, q) => database.MergeEntries.EnumerateAsync(t, q), (t, u, q) => database.MergeEntries.EnumerateAsync(t, u, q)).ConfigureAwait(false);
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
                            return (object)await database.Personas.EnumeratePageAsync(query, ScopedEnumerationFilter.ForScopedEntity(callerCtx)).ConfigureAwait(false);
                        case "memories":
                        case "memory":
                            MemoryEnumerateRequest memoryQuery = MemoryEnumerateRequest.FromQuery(query);
                            if (!String.IsNullOrWhiteSpace(request.Search)) memoryQuery.Search = request.Search.Trim();
                            memoryQuery.ApplyDefaultSort(!String.IsNullOrEmpty(request.Order));
                            EnumerationResult<Memory> memoryResult = await new MemoryService(database, new SyslogLogging.LoggingModule())
                                .EnumerateAsync(callerCtx, memoryQuery).ConfigureAwait(false);
                            return (object)memoryResult;
                        case "prompt_templates":
                        case "prompt_template":
                        case "templates":
                        case "template":
                            // Visibility is applied in the data source before counting, so TotalRecords is exact.
                            PromptTemplateQuery templateQuery = PromptTemplateQuery.From(query);
                            templateQuery.TenantId = callerCtx.IsAdmin ? null : (callerCtx.TenantId ?? Constants.DefaultTenantId);
                            templateQuery.ViewerUserId = ViewerUserIdOf(callerCtx);
                            EnumerationResult<PromptTemplate> templates = await database.PromptTemplates.EnumerateVisibleAsync(templateQuery).ConfigureAwait(false);
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
                            EnumerationResult<Pipeline> pipelines = callerCtx.IsAdmin
                                ? await database.Pipelines.EnumerateAsync(query).ConfigureAwait(false)
                                : await database.Pipelines.EnumerateVisibleAsync(callerCtx.TenantId ?? Constants.DefaultTenantId, ViewerUserIdOf(callerCtx), query).ConfigureAwait(false);
                            return (object)pipelines;
                        case "playbooks":
                        case "playbook":
                            EnumerationResult<Playbook> playbooks = callerCtx.IsAdmin
                                ? await database.Playbooks.EnumerateAsync(query).ConfigureAwait(false)
                                : await database.Playbooks.EnumerateVisibleAsync(callerCtx.TenantId ?? Constants.DefaultTenantId, ViewerUserIdOf(callerCtx), query).ConfigureAwait(false);
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
                                PageSize = query.PageSize,
                                TenantId = callerCtx.IsAdmin ? null : (callerCtx.TenantId ?? Constants.DefaultTenantId),
                                ViewerUserId = ViewerUserIdOf(callerCtx)
                            }).ConfigureAwait(false);
                            return (object)workflowProfiles;
                        case "project_profiles":
                        case "project_profile":
                        case "projectprofiles":
                            EnumerationResult<ProjectProfile> projectProfiles = await database.ProjectProfiles.EnumerateAsync(new ProjectProfileQuery
                            {
                                PageNumber = query.PageNumber,
                                PageSize = query.PageSize,
                                TenantId = callerCtx.IsAdmin ? null : (callerCtx.TenantId ?? Constants.DefaultTenantId),
                                ViewerUserId = ViewerUserIdOf(callerCtx)
                            }).ConfigureAwait(false);
                            return (object)projectProfiles;
                        case "skills":
                        case "skill":
                            EnumerationResult<Skill> skills = await database.Skills.EnumerateAsync(new SkillQuery
                            {
                                PageNumber = query.PageNumber,
                                PageSize = query.PageSize,
                                TenantId = callerCtx.IsAdmin ? null : (callerCtx.TenantId ?? Constants.DefaultTenantId),
                                RestrictToViewer = !callerCtx.IsAdmin && !callerCtx.IsTenantAdmin,
                                ViewerUserId = callerCtx.IsAdmin || callerCtx.IsTenantAdmin ? null : callerCtx.UserId
                            }).ConfigureAwait(false);
                            return (object)skills;
                        case "check_runs":
                        case "check_run":
                        case "checkruns":
                            ApplyOwnerScope(callerCtx, out string? checkTenant, out string? checkUser);
                            EnumerationResult<CheckRun> checkRuns = await database.CheckRuns.EnumerateAsync(new CheckRunQuery
                            {
                                PageNumber = query.PageNumber,
                                PageSize = query.PageSize,
                                TenantId = checkTenant,
                                UserId = checkUser
                            }).ConfigureAwait(false);
                            return (object)checkRuns;
                        case "vessel_import_batch":
                        case "vessel_import_batches":
                        case "vessel-import-batch":
                        case "import_batches":
                            EnumerationResult<VesselImportBatch> importBatches = await database.VesselImportBatches.EnumerateAsync(
                                String.IsNullOrEmpty(callerCtx.TenantId) ? Armada.Core.Constants.DefaultTenantId : callerCtx.TenantId,
                                query).ConfigureAwait(false);
                            return (object)importBatches;
                        case "fleet_action":
                        case "fleet_actions":
                        case "fleet-action":
                        case "fleet-actions":
                            EnumerationResult<FleetAction> fleetActions = await database.FleetActions.EnumerateAsync(
                                callerCtx.TenantId ?? Constants.DefaultTenantId, query, request.IncludeInactive == true).ConfigureAwait(false);
                            return (object)fleetActions;
                        case "fleet_action_run":
                        case "fleet_action_runs":
                        case "fleet-action-run":
                        case "fleet-action-runs":
                            if (!String.IsNullOrWhiteSpace(query.Status))
                            {
                                if (!Enum.TryParse(query.Status, true, out FleetActionRunStatusEnum runStatus))
                                    return (object)McpToolError.InvalidArgument("Unknown fleet action run status: " + query.Status);
                                query.Status = runStatus.ToString();
                            }
                            EnumerationResult<FleetActionRun> fleetActionRuns = await database.FleetActionRuns.EnumerateAsync(
                                callerCtx.TenantId ?? Constants.DefaultTenantId, query).ConfigureAwait(false);
                            return (object)fleetActionRuns;
                        case "fleet_action_run_target":
                        case "fleet_action_run_targets":
                        case "fleet-action-run-target":
                        case "fleet-action-run-targets":
                            if (String.IsNullOrWhiteSpace(request.RunId))
                                return (object)McpToolError.InvalidArgument("runId is required for entityType fleet_action_run_target");
                            string targetTenantId = callerCtx.TenantId ?? Constants.DefaultTenantId;
                            FleetActionRun? targetRun = await database.FleetActionRuns.ReadAsync(targetTenantId, request.RunId!).ConfigureAwait(false);
                            if (targetRun == null) return (object)McpToolError.NotFound("Fleet action run not found: " + request.RunId);
                            FleetActionTargetStatusEnum? targetStatus = null;
                            if (!String.IsNullOrWhiteSpace(query.Status))
                            {
                                if (!Enum.TryParse(query.Status, true, out FleetActionTargetStatusEnum parsedTargetStatus))
                                    return (object)McpToolError.InvalidArgument("Unknown fleet action target status: " + query.Status);
                                targetStatus = parsedTargetStatus;
                            }
                            EnumerationResult<FleetActionRunTarget> targets = await database.FleetActionRunTargets.EnumerateByRunAsync(
                                targetTenantId, targetRun.Id, targetStatus, query.PageNumber, query.PageSize).ConfigureAwait(false);
                            if (request.IncludeOutput == true) return (object)targets;
                            return (object)new EnumerationResult<FleetActionRunTargetSummary>
                            {
                                Success = targets.Success,
                                PageNumber = targets.PageNumber,
                                PageSize = targets.PageSize,
                                TotalPages = targets.TotalPages,
                                TotalRecords = targets.TotalRecords,
                                Objects = targets.Objects.Select(FleetActionRunTargetSummary.FromTarget).ToList(),
                                TotalMs = targets.TotalMs
                            };
                        case "blackbox":
                        case "black_box":
                        case "blackbox_entries":
                        case "blackbox-entries":
                            if (blackBox == null) return (object)McpToolError.Unavailable("The black box is not available on this server.");
                            BlackBoxEnumerateRequest blackBoxQuery = new BlackBoxEnumerateRequest
                            {
                                PageNumber = query.PageNumber,
                                PageSize = query.PageSize,
                                Order = query.Order,
                                CreatedAfter = query.CreatedAfter,
                                CreatedBefore = query.CreatedBefore,
                                FleetId = query.FleetId,
                                VesselId = query.VesselId,
                                VoyageId = query.VoyageId,
                                MissionId = query.MissionId,
                                CaptainId = query.CaptainId,
                                SearchText = String.IsNullOrWhiteSpace(request.Search) ? null : request.Search.Trim()
                            };
                            if (!String.IsNullOrWhiteSpace(query.Status))
                            {
                                if (!Armada.Core.EnumNames.TryParse(query.Status.Trim(), true, out BlackBoxResolutionEnum blackBoxResolution) || blackBoxResolution == BlackBoxResolutionEnum.Unknown)
                                    return (object)McpToolError.InvalidArgument("Unknown black box resolution: " + query.Status + " (Open, Resolved, Duplicate, WontFix or ArmadaFixed)");
                                blackBoxQuery.Resolution = blackBoxResolution;
                            }

                            return (object)await blackBox.EnumerateAsync(callerCtx, blackBoxQuery).ConfigureAwait(false);
                        default:
                            return (object)McpToolError.InvalidArgument("Unknown entity type: " + entityType + ". Valid types: inbox, cli_permission_rules, cli_permission_requests, fleets, vessels, captains, missions, voyages, docks, signals, events, merge_queue, personas, prompt_templates, pipelines, playbooks, workflow_profiles, project_profiles, skills, check_runs, releases, jobs, model_endpoints, vessel_import_batch, fleet_action, fleet_action_run, fleet_action_run_target, vessel_health, blackbox");
                    }
                });
        }

        private static string? ViewerUserIdOf(AuthContext ctx)
        {
            // Configuration entities a regular user sees: the tenant-wide ones plus their own (ScopedVisibility.CanView),
            // applied in the data source before counting.
            return ctx.IsAdmin || ctx.IsTenantAdmin ? null : ctx.UserId;
        }

        /// <summary>
        /// Owner scope for owned entities whose query carries TenantId/UserId (the same rule REST applies): a global
        /// admin is unscoped, a tenant admin is scoped to the tenant, and a regular user to their own records.
        /// </summary>
        private static void ApplyOwnerScope(AuthContext ctx, out string? tenantId, out string? userId)
        {
            tenantId = null;
            userId = null;
            if (ctx.IsAdmin) return;
            tenantId = ctx.TenantId ?? Constants.DefaultTenantId;
            if (!ctx.IsTenantAdmin) userId = ctx.UserId;
        }
    }
}
