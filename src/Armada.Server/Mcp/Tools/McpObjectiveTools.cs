namespace Armada.Server.Mcp.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Server;

    /// <summary>
    /// Registers MCP tools for objective inspection and creation.
    /// </summary>
    public static class McpObjectiveTools
    {
        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() }
        };

        /// <summary>
        /// Registers objective MCP tools.
        /// </summary>
        public static void Register(
            RegisterToolDelegate register,
            DatabaseDriver database,
            ObjectiveService objectiveService,
            PlanningSessionCoordinator? planningSessionCoordinator = null,
            ObjectiveRefinementCoordinator? objectiveRefinementCoordinator = null)
        {
            register(
                "list_objectives",
                "Enumerate backlog objectives with optional vessel, fleet, status, backlog-state, priority, and free-text filters.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        owner = new { type = "string", description = "Optional owner filter" },
                        category = new { type = "string", description = "Optional category filter" },
                        parentObjectiveId = new { type = "string", description = "Optional parent objective filter" },
                        vesselId = new { type = "string", description = "Optional vessel filter" },
                        fleetId = new { type = "string", description = "Optional fleet filter" },
                        status = new { type = "string", description = "Optional lifecycle status filter" },
                        backlogState = new { type = "string", description = "Optional backlog-state filter" },
                        kind = new { type = "string", description = "Optional kind filter" },
                        priority = new { type = "string", description = "Optional priority filter" },
                        effort = new { type = "string", description = "Optional effort filter" },
                        targetVersion = new { type = "string", description = "Optional target-version filter" },
                        search = new { type = "string", description = "Optional free-text search" },
                        pageNumber = new { type = "integer", description = "Optional page number" },
                        pageSize = new { type = "integer", description = "Optional page size" }
                    }
                },
                async (args) =>
                {
                    ObjectiveQuery query = JsonSerializer.Deserialize<ObjectiveQuery>(args!.Value, _JsonOptions) ?? new ObjectiveQuery();
                    AuthContext auth = McpToolHelpers.ResolveCallerContext();
                    return (object)await objectiveService.EnumerateAsync(auth, query).ConfigureAwait(false);
                });

            register(
                "list_backlog",
                "Enumerate backlog items with optional vessel, fleet, status, backlog-state, priority, and free-text filters.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        owner = new { type = "string", description = "Optional owner filter" },
                        category = new { type = "string", description = "Optional category filter" },
                        parentObjectiveId = new { type = "string", description = "Optional parent backlog item filter" },
                        vesselId = new { type = "string", description = "Optional vessel filter" },
                        fleetId = new { type = "string", description = "Optional fleet filter" },
                        status = new { type = "string", description = "Optional lifecycle status filter" },
                        backlogState = new { type = "string", description = "Optional backlog-state filter" },
                        kind = new { type = "string", description = "Optional kind filter" },
                        priority = new { type = "string", description = "Optional priority filter" },
                        effort = new { type = "string", description = "Optional effort filter" },
                        targetVersion = new { type = "string", description = "Optional target-version filter" },
                        search = new { type = "string", description = "Optional free-text search" },
                        pageNumber = new { type = "integer", description = "Optional page number" },
                        pageSize = new { type = "integer", description = "Optional page size" }
                    }
                },
                async (args) =>
                {
                    ObjectiveQuery query = JsonSerializer.Deserialize<ObjectiveQuery>(args!.Value, _JsonOptions) ?? new ObjectiveQuery();
                    AuthContext auth = McpToolHelpers.ResolveCallerContext();
                    return (object)await objectiveService.EnumerateAsync(auth, query).ConfigureAwait(false);
                });

            register(
                "get_objective",
                "Inspect one objective including linked repositories, planning sessions, voyages, releases, deployments, incidents, and acceptance criteria.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        objectiveId = new { type = "string", description = "Objective ID (obj_ prefix)" }
                    },
                    required = new[] { "objectiveId" }
                },
                async (args) =>
                {
                    ObjectiveIdArgs request = JsonSerializer.Deserialize<ObjectiveIdArgs>(args!.Value, _JsonOptions)
                        ?? throw new InvalidOperationException("Could not deserialize ObjectiveIdArgs.");
                    AuthContext auth = McpToolHelpers.ResolveCallerContext();
                    Objective? objective = await objectiveService.ReadAsync(auth, request.ObjectiveId).ConfigureAwait(false);
                    if (objective == null) return (object)new { Error = "Objective not found" };
                    return (object)objective;
                });

            register(
                "get_backlog_item",
                "Inspect one backlog item including linked repositories, planning sessions, voyages, releases, deployments, incidents, and acceptance criteria.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        objectiveId = new { type = "string", description = "Backlog item ID (obj_ prefix)" }
                    },
                    required = new[] { "objectiveId" }
                },
                async (args) =>
                {
                    ObjectiveIdArgs request = JsonSerializer.Deserialize<ObjectiveIdArgs>(args!.Value, _JsonOptions)
                        ?? throw new InvalidOperationException("Could not deserialize ObjectiveIdArgs.");
                    AuthContext auth = McpToolHelpers.ResolveCallerContext();
                    Objective? objective = await objectiveService.ReadAsync(auth, request.ObjectiveId).ConfigureAwait(false);
                    if (objective == null) return (object)new { Error = "Backlog item not found" };
                    return (object)objective;
                });

            register(
                "create_objective",
                "Create an internal-first objective or intake-style record that can link vessels, planning sessions, voyages, checks, releases, deployments, and incidents.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        title = new { type = "string", description = "Objective title" },
                        description = new { type = "string", description = "Optional long-form description" },
                        status = new { type = "string", description = "Optional objective status such as Draft, Scoped, Planned, or InProgress" },
                        kind = new { type = "string", description = "Optional backlog kind such as Feature, Bug, Refactor, or Research" },
                        category = new { type = "string", description = "Optional category such as Frontend, Backend, DevEx, or Ops" },
                        priority = new { type = "string", description = "Optional priority such as P0, P1, P2, or P3" },
                        rank = new { type = "integer", description = "Optional deterministic backlog rank" },
                        backlogState = new { type = "string", description = "Optional backlog state such as Inbox or ReadyForDispatch" },
                        effort = new { type = "string", description = "Optional effort such as XS, S, M, L, or XL" },
                        owner = new { type = "string", description = "Optional owner display label" },
                        targetVersion = new { type = "string", description = "Optional target release version" },
                        dueUtc = new { type = "string", description = "Optional due timestamp in UTC" },
                        parentObjectiveId = new { type = "string", description = "Optional parent objective identifier" },
                        blockedByObjectiveIds = new { type = "array", items = new { type = "string" }, description = "Blocking objective identifiers" },
                        refinementSummary = new { type = "string", description = "Optional captain-generated refinement summary" },
                        suggestedPipelineId = new { type = "string", description = "Optional suggested pipeline identifier" },
                        refinementSessionIds = new { type = "array", items = new { type = "string" }, description = "Linked refinement-session IDs" },
                        tags = new { type = "array", items = new { type = "string" }, description = "Optional tags" },
                        acceptanceCriteria = new { type = "array", items = new { type = "string" }, description = "Acceptance criteria" },
                        nonGoals = new { type = "array", items = new { type = "string" }, description = "Explicit non-goals" },
                        rolloutConstraints = new { type = "array", items = new { type = "string" }, description = "Rollout constraints" },
                        evidenceLinks = new { type = "array", items = new { type = "string" }, description = "Evidence or source links" },
                        fleetIds = new { type = "array", items = new { type = "string" }, description = "Linked fleet IDs" },
                        vesselIds = new { type = "array", items = new { type = "string" }, description = "Linked vessel IDs" },
                        planningSessionIds = new { type = "array", items = new { type = "string" }, description = "Linked planning-session IDs" },
                        voyageIds = new { type = "array", items = new { type = "string" }, description = "Linked voyage IDs" },
                        missionIds = new { type = "array", items = new { type = "string" }, description = "Linked mission IDs" },
                        checkRunIds = new { type = "array", items = new { type = "string" }, description = "Linked check-run IDs" },
                        releaseIds = new { type = "array", items = new { type = "string" }, description = "Linked release IDs" },
                        deploymentIds = new { type = "array", items = new { type = "string" }, description = "Linked deployment IDs" },
                        incidentIds = new { type = "array", items = new { type = "string" }, description = "Linked incident IDs" }
                    },
                    required = new[] { "title" }
                },
                async (args) =>
                {
                    ObjectiveUpsertRequest request = JsonSerializer.Deserialize<ObjectiveUpsertRequest>(args!.Value, _JsonOptions)
                        ?? throw new InvalidOperationException("Could not deserialize ObjectiveUpsertRequest.");
                    AuthContext auth = McpToolHelpers.ResolveCallerContext();
                    return (object)await objectiveService.CreateAsync(auth, request).ConfigureAwait(false);
                });

            register(
                "create_backlog_item",
                "Create a backlog item that can link vessels, planning sessions, voyages, checks, releases, deployments, and incidents.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        title = new { type = "string", description = "Backlog item title" },
                        description = new { type = "string", description = "Optional long-form description" },
                        status = new { type = "string", description = "Optional backlog lifecycle status such as Draft, Scoped, Planned, or InProgress" },
                        kind = new { type = "string", description = "Optional backlog kind such as Feature, Bug, Refactor, or Research" },
                        category = new { type = "string", description = "Optional category such as Frontend, Backend, DevEx, or Ops" },
                        priority = new { type = "string", description = "Optional priority such as P0, P1, P2, or P3" },
                        rank = new { type = "integer", description = "Optional deterministic backlog rank" },
                        backlogState = new { type = "string", description = "Optional backlog state such as Inbox or ReadyForDispatch" },
                        effort = new { type = "string", description = "Optional effort such as XS, S, M, L, or XL" },
                        owner = new { type = "string", description = "Optional owner display label" },
                        targetVersion = new { type = "string", description = "Optional target release version" },
                        dueUtc = new { type = "string", description = "Optional due timestamp in UTC" },
                        parentObjectiveId = new { type = "string", description = "Optional parent backlog item identifier" },
                        blockedByObjectiveIds = new { type = "array", items = new { type = "string" }, description = "Blocking backlog item identifiers" },
                        refinementSummary = new { type = "string", description = "Optional captain-generated refinement summary" },
                        suggestedPipelineId = new { type = "string", description = "Optional suggested pipeline identifier" },
                        refinementSessionIds = new { type = "array", items = new { type = "string" }, description = "Linked refinement-session IDs" },
                        tags = new { type = "array", items = new { type = "string" }, description = "Optional tags" },
                        acceptanceCriteria = new { type = "array", items = new { type = "string" }, description = "Acceptance criteria" },
                        nonGoals = new { type = "array", items = new { type = "string" }, description = "Explicit non-goals" },
                        rolloutConstraints = new { type = "array", items = new { type = "string" }, description = "Rollout constraints" },
                        evidenceLinks = new { type = "array", items = new { type = "string" }, description = "Evidence or source links" },
                        fleetIds = new { type = "array", items = new { type = "string" }, description = "Linked fleet IDs" },
                        vesselIds = new { type = "array", items = new { type = "string" }, description = "Linked vessel IDs" },
                        planningSessionIds = new { type = "array", items = new { type = "string" }, description = "Linked planning-session IDs" },
                        voyageIds = new { type = "array", items = new { type = "string" }, description = "Linked voyage IDs" },
                        missionIds = new { type = "array", items = new { type = "string" }, description = "Linked mission IDs" },
                        checkRunIds = new { type = "array", items = new { type = "string" }, description = "Linked check-run IDs" },
                        releaseIds = new { type = "array", items = new { type = "string" }, description = "Linked release IDs" },
                        deploymentIds = new { type = "array", items = new { type = "string" }, description = "Linked deployment IDs" },
                        incidentIds = new { type = "array", items = new { type = "string" }, description = "Linked incident IDs" }
                    },
                    required = new[] { "title" }
                },
                async (args) =>
                {
                    ObjectiveUpsertRequest request = JsonSerializer.Deserialize<ObjectiveUpsertRequest>(args!.Value, _JsonOptions)
                        ?? throw new InvalidOperationException("Could not deserialize ObjectiveUpsertRequest.");
                    AuthContext auth = McpToolHelpers.ResolveCallerContext();
                    return (object)await objectiveService.CreateAsync(auth, request).ConfigureAwait(false);
                });

            register(
                "update_objective",
                "Update one objective/backlog entry, including prioritization, category, linked entities, and refinement metadata.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        objectiveId = new { type = "string", description = "Objective ID (obj_ prefix)" },
                        title = new { type = "string", description = "Optional title override" },
                        description = new { type = "string", description = "Optional long-form description" },
                        status = new { type = "string", description = "Optional lifecycle status" },
                        kind = new { type = "string", description = "Optional backlog kind" },
                        category = new { type = "string", description = "Optional category" },
                        priority = new { type = "string", description = "Optional priority" },
                        rank = new { type = "integer", description = "Optional deterministic backlog rank" },
                        backlogState = new { type = "string", description = "Optional backlog state" },
                        effort = new { type = "string", description = "Optional effort" },
                        owner = new { type = "string", description = "Optional owner display label" },
                        targetVersion = new { type = "string", description = "Optional target release version" },
                        dueUtc = new { type = "string", description = "Optional due timestamp in UTC" },
                        parentObjectiveId = new { type = "string", description = "Optional parent objective identifier" },
                        blockedByObjectiveIds = new { type = "array", items = new { type = "string" }, description = "Blocking objective identifiers" },
                        refinementSummary = new { type = "string", description = "Optional captain-generated refinement summary" },
                        suggestedPipelineId = new { type = "string", description = "Optional suggested pipeline identifier" },
                        refinementSessionIds = new { type = "array", items = new { type = "string" }, description = "Linked refinement-session IDs" },
                        tags = new { type = "array", items = new { type = "string" }, description = "Optional tags" },
                        acceptanceCriteria = new { type = "array", items = new { type = "string" }, description = "Acceptance criteria" },
                        nonGoals = new { type = "array", items = new { type = "string" }, description = "Explicit non-goals" },
                        rolloutConstraints = new { type = "array", items = new { type = "string" }, description = "Rollout constraints" },
                        evidenceLinks = new { type = "array", items = new { type = "string" }, description = "Evidence or source links" },
                        fleetIds = new { type = "array", items = new { type = "string" }, description = "Linked fleet IDs" },
                        vesselIds = new { type = "array", items = new { type = "string" }, description = "Linked vessel IDs" },
                        planningSessionIds = new { type = "array", items = new { type = "string" }, description = "Linked planning-session IDs" },
                        voyageIds = new { type = "array", items = new { type = "string" }, description = "Linked voyage IDs" },
                        missionIds = new { type = "array", items = new { type = "string" }, description = "Linked mission IDs" },
                        checkRunIds = new { type = "array", items = new { type = "string" }, description = "Linked check-run IDs" },
                        releaseIds = new { type = "array", items = new { type = "string" }, description = "Linked release IDs" },
                        deploymentIds = new { type = "array", items = new { type = "string" }, description = "Linked deployment IDs" },
                        incidentIds = new { type = "array", items = new { type = "string" }, description = "Linked incident IDs" }
                    },
                    required = new[] { "objectiveId" }
                },
                async (args) =>
                {
                    UpdateObjectiveArgs request = JsonSerializer.Deserialize<UpdateObjectiveArgs>(args!.Value, _JsonOptions)
                        ?? throw new InvalidOperationException("Could not deserialize UpdateObjectiveArgs.");
                    AuthContext auth = McpToolHelpers.ResolveCallerContext();
                    return (object)await objectiveService.UpdateAsync(auth, request.ObjectiveId, request.ToUpsertRequest()).ConfigureAwait(false);
                });

            register(
                "update_backlog_item",
                "Update one backlog item, including prioritization, category, linked entities, and refinement metadata.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        objectiveId = new { type = "string", description = "Backlog item ID (obj_ prefix)" },
                        title = new { type = "string", description = "Optional title override" },
                        description = new { type = "string", description = "Optional long-form description" },
                        status = new { type = "string", description = "Optional lifecycle status" },
                        kind = new { type = "string", description = "Optional backlog kind" },
                        category = new { type = "string", description = "Optional category" },
                        priority = new { type = "string", description = "Optional priority" },
                        rank = new { type = "integer", description = "Optional deterministic backlog rank" },
                        backlogState = new { type = "string", description = "Optional backlog state" },
                        effort = new { type = "string", description = "Optional effort" },
                        owner = new { type = "string", description = "Optional owner display label" },
                        targetVersion = new { type = "string", description = "Optional target release version" },
                        dueUtc = new { type = "string", description = "Optional due timestamp in UTC" },
                        parentObjectiveId = new { type = "string", description = "Optional parent backlog item identifier" },
                        blockedByObjectiveIds = new { type = "array", items = new { type = "string" }, description = "Blocking backlog item identifiers" },
                        refinementSummary = new { type = "string", description = "Optional captain-generated refinement summary" },
                        suggestedPipelineId = new { type = "string", description = "Optional suggested pipeline identifier" },
                        refinementSessionIds = new { type = "array", items = new { type = "string" }, description = "Linked refinement-session IDs" },
                        tags = new { type = "array", items = new { type = "string" }, description = "Optional tags" },
                        acceptanceCriteria = new { type = "array", items = new { type = "string" }, description = "Acceptance criteria" },
                        nonGoals = new { type = "array", items = new { type = "string" }, description = "Explicit non-goals" },
                        rolloutConstraints = new { type = "array", items = new { type = "string" }, description = "Rollout constraints" },
                        evidenceLinks = new { type = "array", items = new { type = "string" }, description = "Evidence or source links" },
                        fleetIds = new { type = "array", items = new { type = "string" }, description = "Linked fleet IDs" },
                        vesselIds = new { type = "array", items = new { type = "string" }, description = "Linked vessel IDs" },
                        planningSessionIds = new { type = "array", items = new { type = "string" }, description = "Linked planning-session IDs" },
                        voyageIds = new { type = "array", items = new { type = "string" }, description = "Linked voyage IDs" },
                        missionIds = new { type = "array", items = new { type = "string" }, description = "Linked mission IDs" },
                        checkRunIds = new { type = "array", items = new { type = "string" }, description = "Linked check-run IDs" },
                        releaseIds = new { type = "array", items = new { type = "string" }, description = "Linked release IDs" },
                        deploymentIds = new { type = "array", items = new { type = "string" }, description = "Linked deployment IDs" },
                        incidentIds = new { type = "array", items = new { type = "string" }, description = "Linked incident IDs" }
                    },
                    required = new[] { "objectiveId" }
                },
                async (args) =>
                {
                    UpdateObjectiveArgs request = JsonSerializer.Deserialize<UpdateObjectiveArgs>(args!.Value, _JsonOptions)
                        ?? throw new InvalidOperationException("Could not deserialize UpdateObjectiveArgs.");
                    AuthContext auth = McpToolHelpers.ResolveCallerContext();
                    return (object)await objectiveService.UpdateAsync(auth, request.ObjectiveId, request.ToUpsertRequest()).ConfigureAwait(false);
                });

            register(
                "reorder_objectives",
                "Apply one or more explicit backlog rank updates using objective-compatible tool naming.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        items = new
                        {
                            type = "array",
                            description = "Ordered list of explicit objective rank updates",
                            items = new
                            {
                                type = "object",
                                properties = new
                                {
                                    objectiveId = new { type = "string", description = "Objective ID (obj_ prefix)" },
                                    rank = new { type = "integer", description = "New deterministic rank" }
                                },
                                required = new[] { "objectiveId", "rank" }
                            }
                        }
                    },
                    required = new[] { "items" }
                },
                async (args) =>
                {
                    ObjectiveReorderRequest request = JsonSerializer.Deserialize<ObjectiveReorderRequest>(args!.Value, _JsonOptions)
                        ?? throw new InvalidOperationException("Could not deserialize ObjectiveReorderRequest.");
                    AuthContext auth = McpToolHelpers.ResolveCallerContext();
                    return (object)await objectiveService.ReorderAsync(auth, request).ConfigureAwait(false);
                });

            register(
                "reorder_backlog_items",
                "Apply one or more explicit backlog rank updates using backlog terminology.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        items = new
                        {
                            type = "array",
                            description = "Ordered list of explicit backlog item rank updates",
                            items = new
                            {
                                type = "object",
                                properties = new
                                {
                                    objectiveId = new { type = "string", description = "Backlog item ID (obj_ prefix)" },
                                    rank = new { type = "integer", description = "New deterministic rank" }
                                },
                                required = new[] { "objectiveId", "rank" }
                            }
                        }
                    },
                    required = new[] { "items" }
                },
                async (args) =>
                {
                    ObjectiveReorderRequest request = JsonSerializer.Deserialize<ObjectiveReorderRequest>(args!.Value, _JsonOptions)
                        ?? throw new InvalidOperationException("Could not deserialize ObjectiveReorderRequest.");
                    AuthContext auth = McpToolHelpers.ResolveCallerContext();
                    return (object)await objectiveService.ReorderAsync(auth, request).ConfigureAwait(false);
                });

            if (objectiveRefinementCoordinator != null)
            {
                register(
                    "list_backlog_refinement_sessions",
                    "List captain-backed refinement sessions for one backlog item (lightweight, paginated). Returns session metadata only, most-recently-updated first; fetch a session's transcript with get_backlog_refinement_session.",
                    new
                    {
                        type = "object",
                        properties = new
                        {
                            objectiveId = new { type = "string", description = "Backlog item ID (obj_ prefix)" },
                            pageNumber = new { type = "integer", description = "1-based page number (default 1)" },
                            pageSize = new { type = "integer", description = "Results per page (default 25, max 100)" }
                        },
                        required = new[] { "objectiveId" }
                    },
                    async (args) =>
                    {
                        ObjectiveIdArgs request = JsonSerializer.Deserialize<ObjectiveIdArgs>(args!.Value, _JsonOptions)
                            ?? throw new InvalidOperationException("Could not deserialize ObjectiveIdArgs.");
                        AuthContext auth = McpToolHelpers.ResolveCallerContext();
                        Objective? objective = await objectiveService.ReadAsync(auth, request.ObjectiveId).ConfigureAwait(false);
                        if (objective == null) return (object)new { Error = "Backlog item not found" };

                        int pageNumber = request.PageNumber.HasValue ? request.PageNumber.Value : 1;
                        if (pageNumber < 1) pageNumber = 1;
                        int pageSize = request.PageSize.HasValue ? request.PageSize.Value : 25;
                        if (pageSize < 1) pageSize = 1;
                        if (pageSize > 100) pageSize = 100;

                        List<ObjectiveRefinementSession> sessions = await EnumerateObjectiveRefinementSessionsAsync(database, auth, objective.Id).ConfigureAwait(false);
                        List<ObjectiveRefinementSession> ordered = sessions
                            .OrderByDescending(session => session.LastUpdateUtc)
                            .ToList();

                        long totalRecords = ordered.Count;
                        int totalPages = pageSize > 0 ? (int)Math.Ceiling((double)totalRecords / pageSize) : 0;
                        List<ObjectiveRefinementSession> page = ordered
                            .Skip((pageNumber - 1) * pageSize)
                            .Take(pageSize)
                            .ToList();

                        return (object)new
                        {
                            success = true,
                            pageNumber,
                            pageSize,
                            totalPages,
                            totalRecords,
                            objects = page
                        };
                    });

                register(
                    "create_backlog_refinement_session",
                    "Start a captain-backed refinement session for a backlog item. The caller must specify the captain explicitly.",
                    new
                    {
                        type = "object",
                        properties = new
                        {
                            objectiveId = new { type = "string", description = "Backlog item ID (obj_ prefix)" },
                            captainId = new { type = "string", description = "Captain ID (cpt_ prefix)" },
                            fleetId = new { type = "string", description = "Optional fleet ID override" },
                            vesselId = new { type = "string", description = "Optional vessel ID override" },
                            title = new { type = "string", description = "Optional refinement session title" },
                            initialMessage = new { type = "string", description = "Optional initial prompt or refinement request" }
                        },
                        required = new[] { "objectiveId", "captainId" }
                    },
                    async (args) =>
                    {
                        CreateBacklogRefinementSessionArgs request = JsonSerializer.Deserialize<CreateBacklogRefinementSessionArgs>(args!.Value, _JsonOptions)
                            ?? throw new InvalidOperationException("Could not deserialize CreateBacklogRefinementSessionArgs.");
                        AuthContext auth = McpToolHelpers.ResolveCallerContext();
                        Objective objective = await objectiveService.ReadAsync(auth, request.ObjectiveId).ConfigureAwait(false)
                            ?? throw new InvalidOperationException("Backlog item not found.");
                        Captain captain = await ReadCaptainForContextAsync(database, auth, request.CaptainId).ConfigureAwait(false)
                            ?? throw new InvalidOperationException("Captain not found.");
                        string? vesselId = !String.IsNullOrWhiteSpace(request.VesselId) ? request.VesselId : objective.VesselIds.FirstOrDefault();
                        Vessel? vessel = !String.IsNullOrWhiteSpace(vesselId)
                            ? await ReadVesselForContextAsync(database, auth, vesselId!).ConfigureAwait(false)
                            : null;
                        if (!String.IsNullOrWhiteSpace(vesselId) && vessel == null)
                            throw new InvalidOperationException("Vessel not found.");

                        ObjectiveRefinementSession session = await objectiveRefinementCoordinator
                            .CreateAsync(auth.TenantId, auth.UserId, objective, captain, vessel, request.ToCreateRequest())
                            .ConfigureAwait(false);
                        await objectiveService.LinkRefinementSessionAsync(auth, objective.Id, session.Id).ConfigureAwait(false);
                        return (object)await BuildObjectiveRefinementSessionDetailAsync(database, objectiveService, auth, session).ConfigureAwait(false);
                    });

                register(
                    "get_backlog_refinement_session",
                    "Inspect one backlog refinement session, including its transcript, captain, vessel, and linked backlog item.",
                    new
                    {
                        type = "object",
                        properties = new
                        {
                            sessionId = new { type = "string", description = "Objective refinement session ID (ors_ prefix)" }
                        },
                        required = new[] { "sessionId" }
                    },
                    async (args) =>
                    {
                        ObjectiveRefinementSessionIdArgs request = JsonSerializer.Deserialize<ObjectiveRefinementSessionIdArgs>(args!.Value, _JsonOptions)
                            ?? throw new InvalidOperationException("Could not deserialize ObjectiveRefinementSessionIdArgs.");
                        AuthContext auth = McpToolHelpers.ResolveCallerContext();
                        ObjectiveRefinementSession session = await ReadObjectiveRefinementSessionForContextAsync(database, auth, request.SessionId).ConfigureAwait(false)
                            ?? throw new InvalidOperationException("Objective refinement session not found.");
                        return (object)await BuildObjectiveRefinementSessionDetailAsync(database, objectiveService, auth, session).ConfigureAwait(false);
                    });

                register(
                    "send_backlog_refinement_message",
                    "Append one user message to a backlog refinement session and launch the next captain turn.",
                    new
                    {
                        type = "object",
                        properties = new
                        {
                            sessionId = new { type = "string", description = "Objective refinement session ID (ors_ prefix)" },
                            content = new { type = "string", description = "Message content" }
                        },
                        required = new[] { "sessionId", "content" }
                    },
                    async (args) =>
                    {
                        SendBacklogRefinementMessageArgs request = JsonSerializer.Deserialize<SendBacklogRefinementMessageArgs>(args!.Value, _JsonOptions)
                            ?? throw new InvalidOperationException("Could not deserialize SendBacklogRefinementMessageArgs.");
                        AuthContext auth = McpToolHelpers.ResolveCallerContext();
                        ObjectiveRefinementSession session = await ReadObjectiveRefinementSessionForContextAsync(database, auth, request.SessionId).ConfigureAwait(false)
                            ?? throw new InvalidOperationException("Objective refinement session not found.");
                        await objectiveRefinementCoordinator.SendMessageAsync(session, request.Content).ConfigureAwait(false);
                        return (object)await BuildObjectiveRefinementSessionDetailAsync(database, objectiveService, auth, session).ConfigureAwait(false);
                    });

                register(
                    "summarize_backlog_refinement_session",
                    "Create or select a structured backlog-refinement summary from one refinement session.",
                    new
                    {
                        type = "object",
                        properties = new
                        {
                            sessionId = new { type = "string", description = "Objective refinement session ID (ors_ prefix)" },
                            messageId = new { type = "string", description = "Optional transcript message ID to summarize instead of the latest assistant turn" }
                        },
                        required = new[] { "sessionId" }
                    },
                    async (args) =>
                    {
                        SummarizeBacklogRefinementArgs request = JsonSerializer.Deserialize<SummarizeBacklogRefinementArgs>(args!.Value, _JsonOptions)
                            ?? throw new InvalidOperationException("Could not deserialize SummarizeBacklogRefinementArgs.");
                        AuthContext auth = McpToolHelpers.ResolveCallerContext();
                        ObjectiveRefinementSession session = await ReadObjectiveRefinementSessionForContextAsync(database, auth, request.SessionId).ConfigureAwait(false)
                            ?? throw new InvalidOperationException("Objective refinement session not found.");
                        return (object)await objectiveRefinementCoordinator.SummarizeAsync(session, request.ToSummaryRequest()).ConfigureAwait(false);
                    });

                register(
                    "apply_backlog_refinement_summary",
                    "Apply a refinement summary back to the linked backlog item and optionally promote its backlog state.",
                    new
                    {
                        type = "object",
                        properties = new
                        {
                            sessionId = new { type = "string", description = "Objective refinement session ID (ors_ prefix)" },
                            messageId = new { type = "string", description = "Optional transcript message ID to summarize and apply" },
                            markMessageSelected = new { type = "boolean", description = "Whether to mark the summarized message as selected (default true)" },
                            promoteBacklogState = new { type = "boolean", description = "Whether to promote the backlog state based on refinement output (default true)" }
                        },
                        required = new[] { "sessionId" }
                    },
                    async (args) =>
                    {
                        ApplyBacklogRefinementArgs request = JsonSerializer.Deserialize<ApplyBacklogRefinementArgs>(args!.Value, _JsonOptions)
                            ?? throw new InvalidOperationException("Could not deserialize ApplyBacklogRefinementArgs.");
                        AuthContext auth = McpToolHelpers.ResolveCallerContext();
                        ObjectiveRefinementSession session = await ReadObjectiveRefinementSessionForContextAsync(database, auth, request.SessionId).ConfigureAwait(false)
                            ?? throw new InvalidOperationException("Objective refinement session not found.");
                        Objective objective = await objectiveService.ReadAsync(auth, session.ObjectiveId).ConfigureAwait(false)
                            ?? throw new InvalidOperationException("Backlog item not found.");
                        (ObjectiveRefinementSummaryResponse summary, Objective updated) = await objectiveRefinementCoordinator
                            .ApplyAsync(auth, objective, session, request.ToApplyRequest(), objectiveService)
                            .ConfigureAwait(false);
                        return (object)new ObjectiveRefinementApplyResponse
                        {
                            Summary = summary,
                            Objective = updated
                        };
                    });

                register(
                    "stop_backlog_refinement_session",
                    "Request stop for one active backlog refinement session and release the selected captain.",
                    new
                    {
                        type = "object",
                        properties = new
                        {
                            sessionId = new { type = "string", description = "Objective refinement session ID (ors_ prefix)" }
                        },
                        required = new[] { "sessionId" }
                    },
                    async (args) =>
                    {
                        ObjectiveRefinementSessionIdArgs request = JsonSerializer.Deserialize<ObjectiveRefinementSessionIdArgs>(args!.Value, _JsonOptions)
                            ?? throw new InvalidOperationException("Could not deserialize ObjectiveRefinementSessionIdArgs.");
                        AuthContext auth = McpToolHelpers.ResolveCallerContext();
                        ObjectiveRefinementSession session = await ReadObjectiveRefinementSessionForContextAsync(database, auth, request.SessionId).ConfigureAwait(false)
                            ?? throw new InvalidOperationException("Objective refinement session not found.");
                        ObjectiveRefinementSession stopping = await objectiveRefinementCoordinator.RequestStopAsync(session).ConfigureAwait(false);
                        return (object)await BuildObjectiveRefinementSessionDetailAsync(database, objectiveService, auth, stopping).ConfigureAwait(false);
                    });
            }

            if (planningSessionCoordinator != null)
            {
                register(
                    "create_backlog_planning_session",
                    "Create a repository-aware planning session from a backlog item while preserving the objective linkage.",
                    new
                    {
                        type = "object",
                        properties = new
                        {
                            objectiveId = new { type = "string", description = "Backlog item ID (obj_ prefix)" },
                            captainId = new { type = "string", description = "Captain ID (cpt_ prefix)" },
                            vesselId = new { type = "string", description = "Vessel ID (vsl_ prefix)" },
                            fleetId = new { type = "string", description = "Optional fleet ID override" },
                            pipelineId = new { type = "string", description = "Optional pipeline ID override" },
                            title = new { type = "string", description = "Optional planning session title" },
                            selectedPlaybooks = new { type = "array", items = new { type = "object" }, description = "Optional ordered playbook selections" }
                        },
                        required = new[] { "objectiveId", "captainId", "vesselId" }
                    },
                    async (args) =>
                    {
                        CreateBacklogPlanningSessionArgs request = JsonSerializer.Deserialize<CreateBacklogPlanningSessionArgs>(args!.Value, _JsonOptions)
                            ?? throw new InvalidOperationException("Could not deserialize CreateBacklogPlanningSessionArgs.");
                        AuthContext auth = McpToolHelpers.ResolveCallerContext();
                        string objectiveId = String.IsNullOrWhiteSpace(request.ObjectiveId)
                            ? throw new InvalidOperationException("Objective ID is required.")
                            : request.ObjectiveId;
                        Objective objective = await objectiveService.ReadAsync(auth, objectiveId).ConfigureAwait(false)
                            ?? throw new InvalidOperationException("Backlog item not found.");
                        Captain captain = await ReadCaptainForContextAsync(database, auth, request.CaptainId).ConfigureAwait(false)
                            ?? throw new InvalidOperationException("Captain not found.");
                        Vessel vessel = await ReadVesselForContextAsync(database, auth, request.VesselId).ConfigureAwait(false)
                            ?? throw new InvalidOperationException("Vessel not found.");

                        PlanningSession session = await planningSessionCoordinator
                            .CreateAsync(auth.TenantId, auth.UserId, captain, vessel, request.ToPlanningSessionCreateRequest())
                            .ConfigureAwait(false);
                        await objectiveService.LinkPlanningSessionAsync(auth, objective.Id, session.Id).ConfigureAwait(false);
                        return (object)await BuildPlanningSessionDetailAsync(database, objectiveService, auth, session).ConfigureAwait(false);
                    });

                register(
                    "get_backlog_planning_session",
                    "Inspect one planning session created from backlog/objective work, including its transcript and linked backlog items.",
                    new
                    {
                        type = "object",
                        properties = new
                        {
                            sessionId = new { type = "string", description = "Planning session ID (psn_ prefix)" }
                        },
                        required = new[] { "sessionId" }
                    },
                    async (args) =>
                    {
                        PlanningSessionIdArgs request = JsonSerializer.Deserialize<PlanningSessionIdArgs>(args!.Value, _JsonOptions)
                            ?? throw new InvalidOperationException("Could not deserialize PlanningSessionIdArgs.");
                        AuthContext auth = McpToolHelpers.ResolveCallerContext();
                        PlanningSession session = await ReadPlanningSessionForContextAsync(database, auth, request.SessionId).ConfigureAwait(false)
                            ?? throw new InvalidOperationException("Planning session not found.");
                        return (object)await BuildPlanningSessionDetailAsync(database, objectiveService, auth, session).ConfigureAwait(false);
                    });

                register(
                    "dispatch_backlog_planning_session",
                    "Dispatch a voyage from a planning session and keep the linked backlog item associated with the resulting voyage.",
                    new
                    {
                        type = "object",
                        properties = new
                        {
                            sessionId = new { type = "string", description = "Planning session ID (psn_ prefix)" },
                            messageId = new { type = "string", description = "Optional transcript message ID to dispatch from" },
                            title = new { type = "string", description = "Optional voyage title override" },
                            description = new { type = "string", description = "Optional mission description override" }
                        },
                        required = new[] { "sessionId" }
                    },
                    async (args) =>
                    {
                        DispatchBacklogPlanningSessionArgs request = JsonSerializer.Deserialize<DispatchBacklogPlanningSessionArgs>(args!.Value, _JsonOptions)
                            ?? throw new InvalidOperationException("Could not deserialize DispatchBacklogPlanningSessionArgs.");
                        AuthContext auth = McpToolHelpers.ResolveCallerContext();
                        PlanningSession session = await ReadPlanningSessionForContextAsync(database, auth, request.SessionId).ConfigureAwait(false)
                            ?? throw new InvalidOperationException("Planning session not found.");
                        Voyage voyage = await planningSessionCoordinator.DispatchAsync(session, request.ToDispatchRequest()).ConfigureAwait(false);
                        List<Objective> linkedObjectives = await objectiveService.EnumerateByPlanningSessionAsync(auth, session.Id).ConfigureAwait(false);
                        List<Objective> updatedObjectives = new List<Objective>();
                        foreach (Objective linkedObjective in linkedObjectives)
                        {
                            updatedObjectives.Add(await objectiveService.LinkVoyageAsync(auth, linkedObjective.Id, voyage.Id).ConfigureAwait(false));
                        }

                        return (object)new
                        {
                            Voyage = voyage,
                            Objectives = updatedObjectives,
                            ObjectiveIds = updatedObjectives.Select(item => item.Id).ToList()
                        };
                    });
            }

            register(
                "delete_objective",
                "Delete one objective/backlog entry and its snapshot history.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        objectiveId = new { type = "string", description = "Objective ID (obj_ prefix)" }
                    },
                    required = new[] { "objectiveId" }
                },
                async (args) =>
                {
                    ObjectiveIdArgs request = JsonSerializer.Deserialize<ObjectiveIdArgs>(args!.Value, _JsonOptions)
                        ?? throw new InvalidOperationException("Could not deserialize ObjectiveIdArgs.");
                    AuthContext auth = McpToolHelpers.ResolveCallerContext();
                    await objectiveService.DeleteAsync(auth, request.ObjectiveId).ConfigureAwait(false);
                    return (object)new { Success = true, ObjectiveId = request.ObjectiveId };
                });

            register(
                "delete_backlog_item",
                "Delete one backlog item and its snapshot history.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        objectiveId = new { type = "string", description = "Backlog item ID (obj_ prefix)" }
                    },
                    required = new[] { "objectiveId" }
                },
                async (args) =>
                {
                    ObjectiveIdArgs request = JsonSerializer.Deserialize<ObjectiveIdArgs>(args!.Value, _JsonOptions)
                        ?? throw new InvalidOperationException("Could not deserialize ObjectiveIdArgs.");
                    AuthContext auth = McpToolHelpers.ResolveCallerContext();
                    await objectiveService.DeleteAsync(auth, request.ObjectiveId).ConfigureAwait(false);
                    return (object)new { Success = true, ObjectiveId = request.ObjectiveId };
                });
        }

        private static async Task<List<ObjectiveRefinementSession>> EnumerateObjectiveRefinementSessionsAsync(
            DatabaseDriver database,
            AuthContext auth,
            string objectiveId)
        {
            List<ObjectiveRefinementSession> sessions = auth.IsAdmin
                ? await database.ObjectiveRefinementSessions.EnumerateAsync().ConfigureAwait(false)
                : auth.IsTenantAdmin
                    ? await database.ObjectiveRefinementSessions.EnumerateAsync(auth.TenantId!).ConfigureAwait(false)
                    : await database.ObjectiveRefinementSessions.EnumerateAsync(auth.TenantId!, auth.UserId!).ConfigureAwait(false);
            return sessions
                .Where(session => String.Equals(session.ObjectiveId, objectiveId, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        private static async Task<PlanningSession?> ReadPlanningSessionForContextAsync(
            DatabaseDriver database,
            AuthContext auth,
            string id)
        {
            if (auth.IsAdmin)
                return await database.PlanningSessions.ReadAsync(id).ConfigureAwait(false);
            if (auth.IsTenantAdmin)
                return await database.PlanningSessions.ReadAsync(auth.TenantId!, id).ConfigureAwait(false);
            return await database.PlanningSessions.ReadAsync(auth.TenantId!, auth.UserId!, id).ConfigureAwait(false);
        }

        private static async Task<ObjectiveRefinementSession?> ReadObjectiveRefinementSessionForContextAsync(
            DatabaseDriver database,
            AuthContext auth,
            string id)
        {
            if (auth.IsAdmin)
                return await database.ObjectiveRefinementSessions.ReadAsync(id).ConfigureAwait(false);
            if (auth.IsTenantAdmin)
                return await database.ObjectiveRefinementSessions.ReadAsync(auth.TenantId!, id).ConfigureAwait(false);
            return await database.ObjectiveRefinementSessions.ReadAsync(auth.TenantId!, auth.UserId!, id).ConfigureAwait(false);
        }

        private static async Task<Captain?> ReadCaptainForContextAsync(
            DatabaseDriver database,
            AuthContext auth,
            string id)
        {
            if (auth.IsAdmin)
                return await database.Captains.ReadAsync(id).ConfigureAwait(false);
            if (auth.IsTenantAdmin)
                return await database.Captains.ReadAsync(auth.TenantId!, id).ConfigureAwait(false);
            return await database.Captains.ReadAsync(auth.TenantId!, auth.UserId!, id).ConfigureAwait(false);
        }

        private static async Task<Vessel?> ReadVesselForContextAsync(
            DatabaseDriver database,
            AuthContext auth,
            string id)
        {
            if (auth.IsAdmin)
                return await database.Vessels.ReadAsync(id).ConfigureAwait(false);
            if (auth.IsTenantAdmin)
                return await database.Vessels.ReadAsync(auth.TenantId!, id).ConfigureAwait(false);
            return await database.Vessels.ReadAsync(auth.TenantId!, auth.UserId!, id).ConfigureAwait(false);
        }

        private static async Task<object> BuildPlanningSessionDetailAsync(
            DatabaseDriver database,
            ObjectiveService objectiveService,
            AuthContext auth,
            PlanningSession session)
        {
            PlanningSession refreshed = await ReadPlanningSessionForContextAsync(database, auth, session.Id).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Planning session not found: " + session.Id);
            List<PlanningSessionMessage> messages = await database.PlanningSessionMessages
                .EnumerateBySessionAsync(refreshed.Id)
                .ConfigureAwait(false);
            Captain? captain = await ReadCaptainForContextAsync(database, auth, refreshed.CaptainId).ConfigureAwait(false);
            Vessel? vessel = await ReadVesselForContextAsync(database, auth, refreshed.VesselId).ConfigureAwait(false);
            List<Objective> linkedObjectives = await objectiveService.EnumerateByPlanningSessionAsync(auth, refreshed.Id).ConfigureAwait(false);

            return new
            {
                Session = refreshed,
                Messages = messages.OrderBy(message => message.Sequence).ToList(),
                Captain = captain,
                Vessel = vessel,
                Objectives = linkedObjectives,
                ObjectiveIds = linkedObjectives.Select(objective => objective.Id).ToList()
            };
        }

        private static async Task<ObjectiveRefinementSessionDetail> BuildObjectiveRefinementSessionDetailAsync(
            DatabaseDriver database,
            ObjectiveService objectiveService,
            AuthContext auth,
            ObjectiveRefinementSession session)
        {
            ObjectiveRefinementSession refreshed = await ReadObjectiveRefinementSessionForContextAsync(database, auth, session.Id).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Objective refinement session not found: " + session.Id);
            List<ObjectiveRefinementMessage> messages = await database.ObjectiveRefinementMessages
                .EnumerateBySessionAsync(refreshed.Id)
                .ConfigureAwait(false);
            Captain? captain = await ReadCaptainForContextAsync(database, auth, refreshed.CaptainId).ConfigureAwait(false);
            Vessel? vessel = !String.IsNullOrWhiteSpace(refreshed.VesselId)
                ? await ReadVesselForContextAsync(database, auth, refreshed.VesselId!).ConfigureAwait(false)
                : null;
            Objective? objective = await objectiveService.ReadAsync(auth, refreshed.ObjectiveId).ConfigureAwait(false);

            return new ObjectiveRefinementSessionDetail
            {
                Session = refreshed,
                Messages = messages.OrderBy(message => message.Sequence).ToList(),
                Captain = captain,
                Vessel = vessel,
                Objective = objective
            };
        }

        private sealed class PlanningSessionIdArgs
        {
            public string SessionId { get; set; } = String.Empty;
        }

        private sealed class ObjectiveRefinementSessionIdArgs
        {
            public string SessionId { get; set; } = String.Empty;
        }

        private sealed class CreateBacklogPlanningSessionArgs : PlanningSessionCreateRequest
        {
            public PlanningSessionCreateRequest ToPlanningSessionCreateRequest()
            {
                return new PlanningSessionCreateRequest
                {
                    Title = Title,
                    CaptainId = CaptainId,
                    VesselId = VesselId,
                    FleetId = FleetId,
                    PipelineId = PipelineId,
                    SelectedPlaybooks = SelectedPlaybooks ?? new List<SelectedPlaybook>(),
                    ObjectiveId = ObjectiveId
                };
            }
        }

        private sealed class DispatchBacklogPlanningSessionArgs : PlanningSessionDispatchRequest
        {
            public string SessionId { get; set; } = String.Empty;

            public PlanningSessionDispatchRequest ToDispatchRequest()
            {
                return new PlanningSessionDispatchRequest
                {
                    MessageId = MessageId,
                    Title = Title,
                    Description = Description
                };
            }
        }

        private sealed class CreateBacklogRefinementSessionArgs : ObjectiveRefinementSessionCreateRequest
        {
            public string ObjectiveId { get; set; } = String.Empty;

            public ObjectiveRefinementSessionCreateRequest ToCreateRequest()
            {
                return new ObjectiveRefinementSessionCreateRequest
                {
                    CaptainId = CaptainId,
                    FleetId = FleetId,
                    VesselId = VesselId,
                    Title = Title,
                    InitialMessage = InitialMessage
                };
            }
        }

        private sealed class SendBacklogRefinementMessageArgs : ObjectiveRefinementMessageRequest
        {
            public string SessionId { get; set; } = String.Empty;
        }

        private sealed class SummarizeBacklogRefinementArgs : ObjectiveRefinementSummaryRequest
        {
            public string SessionId { get; set; } = String.Empty;

            public ObjectiveRefinementSummaryRequest ToSummaryRequest()
            {
                return new ObjectiveRefinementSummaryRequest
                {
                    MessageId = MessageId
                };
            }
        }

        private sealed class ApplyBacklogRefinementArgs : ObjectiveRefinementApplyRequest
        {
            public string SessionId { get; set; } = String.Empty;

            public ObjectiveRefinementApplyRequest ToApplyRequest()
            {
                return new ObjectiveRefinementApplyRequest
                {
                    MessageId = MessageId,
                    MarkMessageSelected = MarkMessageSelected,
                    PromoteBacklogState = PromoteBacklogState
                };
            }
        }

        private sealed class UpdateObjectiveArgs : ObjectiveUpsertRequest
        {
            public string ObjectiveId { get; set; } = String.Empty;

            public ObjectiveUpsertRequest ToUpsertRequest()
            {
                return new ObjectiveUpsertRequest
                {
                    Title = Title,
                    Description = Description,
                    Status = Status,
                    Kind = Kind,
                    Category = Category,
                    Priority = Priority,
                    Rank = Rank,
                    BacklogState = BacklogState,
                    Effort = Effort,
                    Owner = Owner,
                    TargetVersion = TargetVersion,
                    DueUtc = DueUtc,
                    ParentObjectiveId = ParentObjectiveId,
                    BlockedByObjectiveIds = BlockedByObjectiveIds,
                    RefinementSummary = RefinementSummary,
                    SuggestedPipelineId = SuggestedPipelineId,
                    SuggestedPlaybooks = SuggestedPlaybooks,
                    Tags = Tags,
                    AcceptanceCriteria = AcceptanceCriteria,
                    NonGoals = NonGoals,
                    RolloutConstraints = RolloutConstraints,
                    EvidenceLinks = EvidenceLinks,
                    FleetIds = FleetIds,
                    VesselIds = VesselIds,
                    PlanningSessionIds = PlanningSessionIds,
                    RefinementSessionIds = RefinementSessionIds,
                    VoyageIds = VoyageIds,
                    MissionIds = MissionIds,
                    CheckRunIds = CheckRunIds,
                    ReleaseIds = ReleaseIds,
                    DeploymentIds = DeploymentIds,
                    IncidentIds = IncidentIds
                };
            }
        }
    }
}
