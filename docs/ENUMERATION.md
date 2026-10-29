# Bounded Enumeration Contract

Armada has no "get all" API. No REST route or MCP tool returns, embeds, or echoes a collection that is not bounded by
a server-enforced limit. This file is the contract every route and tool follows and the checklist that records how
each audited surface meets it. The shared code is `src/Armada.Core/Services/CollectionLimits.cs`,
`EnumerationQuery`, and `EnumerationResult<T>`.

## Paging

- A stored collection (rows in a database table, or any set that grows with operational data) is read one page at a
  time with `EnumerationQuery` and returned as `EnumerationResult<T>`: `PageNumber`, `PageSize`, `TotalPages`,
  `TotalRecords`, `Objects`.
- `PageSize` defaults to 10 (`CollectionLimits.DefaultPageSize`), minimum 1, maximum 1000. A value outside the range is
  normalized to the nearest bound (existing convention); the result's `PageSize` shows the size used. `PageNumber`
  below 1 is normalized to 1. A page past the end returns an empty `Objects` with accurate totals.
- A route may set a lower maximum; it documents it and normalizes the same way.
- The page is cut in the data source: SQL `LIMIT/OFFSET` (or the provider equivalent) with a `COUNT` over the same
  filter, in every database provider (SQLite, PostgreSQL, MySQL, SQL Server). Filtering and authorization scope apply
  before counting, so `TotalRecords` is exact for the caller. Loading every row and slicing in memory is not allowed
  for stored rows.
- Ordering is deterministic: the requested `Order` (CreatedDescending by default) with the id as the tie-break, so
  pages never overlap or skip rows when timestamps are equal.
- REST: `POST .../enumerate` takes the query in the body (querystring values override it); a paged `GET` list takes it
  in the querystring and has a `POST .../enumerate` twin. A malformed body is 400. MCP: the tool takes `pageNumber`
  and `pageSize` and returns the same page shape.

## Collections that are not stored rows

| Kind | Rule | Limit |
|---|---|---|
| Children that grow with operational data (missions of a voyage, vessels of a fleet, transcript messages, tracked work, dependencies, resources, results) | Not embedded in the parent read. The parent carries a count where useful; the children have their own paged enumeration. | Page bounds above |
| Ids or keys in one request (bulk delete, purge, reorder, cleanup keys) | Rejected when over the limit. A result list derived from the request (for example `Skipped`) is bounded by it. | `MaxRequestItems` = 1000 |
| Configuration lists and dictionaries stored on one entity (patterns, tags, headers, stages, priorities, inputs, parameters) | Every write path (REST, MCP, WebSocket, service) rejects a list over the limit. | `MaxConfigItems` = 256 |
| Link id lists stored on one entity (objective, release, deployment, incident links) | A write or link that would exceed the limit is rejected. | `MaxLinkedIds` = 1000 |
| Summary buckets (token usage, request history, mission history) | A window and bucket size that would produce more buckets is rejected. | `MaxSummaryBuckets` = 1000 |
| Mutation results | Scalars, ids, and counts. Per-item detail is bounded by the request limit or read through an enumeration. | As above |
| External sources (GitHub, git, the file system, Harbor) | Requested from the source in bounded pages and exposed through `EnumerationQuery` paging; never cut short without saying so. | Page bounds above |

Exceeding a limit is never a silent truncation. The request fails with `CollectionLimitExceededException`: REST 400
with `Data.Code` `CollectionLimitExceeded` (`CollectionLimitErrorDetail`: `Field`, `Limit`, `Actual`), MCP
`ErrorCode` `InvalidArgument` with `Code` `CollectionLimitExceeded`.

Every collection property of a model a route or tool returns declares its limit with `[CollectionBound(limit)]`
(`src/Armada.Core/Services/CollectionBoundAttribute.cs`, using the `CollectionLimits` constants), and every write path
calls `CollectionBounds.Validate(model)`, which enforces the declared limits on the model and every Armada model it
contains. The `E2E.ResponseShape` guard fails for a collection property reachable from a response that declares no
bound, and for any array in a live response that is neither a page's `Objects` nor a declared bounded property.

Collections fixed in code (for example the Ask quick-action catalog, the doctor checks, or the six model probe results)
are bounded by construction and listed in the checklist with the code that bounds them.

## Checklist

The checklist below maps every surface from the inventory audit (voyage `vyg_mvsudvxf_OPT5n2zeNlp`, mission
`msn_mvsudvxs_1SwRANimKxU`, commit `634622de4`: 272 of 610 REST routes and MCP tools unbounded) to the migration that
closes it and the evidence. It is maintained with the code: a change that adds or reshapes a collection updates it.

Status legend: **Paged** (an `EnumerationResult<T>` page cut in the data source), **Extracted** (removed from the
parent; a count stays and the children have their own paged enumeration), **Limited** (input or stored list rejected
over its `CollectionLimits` limit on every write path), **Aggregated** (a server-produced breakdown keeps the largest
entries and folds the rest into an explicit total), **Fixed** (bounded by construction, declared with
`[CollectionBound]`). Test ids are Touchstone `Suite/case` ids; `Database.*` suites run against all four providers.

Every row below is closed. Cross-cutting evidence: `E2E.ResponseShape` (declared bounds on every response model,
live scan of every GET route and read-only MCP tool over a seeded world, validator limits, typed REST and MCP limit
errors) and `E2E.CollectionSurface` pass.

### Inventory coverage

- Baseline: 610 registered items at commit `634622de4` (410 REST handlers plus `GET /openapi.json` and `GET /swagger`,
  200 MCP tools); 272 unbounded (44 operationally growing, 228 caller or configuration derived), 95 bounded, 243 with
  no collection. Current main at the start of this work was the audited commit, so no surface was added in between.
- After this change: 436 REST routes and 210 MCP tools (`docs/api-surface-1.0.json`). The 34 new items are the paged
  enumerations listed below. None of the 272 findings remains open. The audit's line-by-line approach is replaced by
  `E2E.ResponseShape`, which checks every route and tool on every run.

### Operationally growing collections (audit 5.1, 44 items)

| Surface | Change | Evidence |
|---|---|---|
| REST `GET /api/v1/token-usage/summary`, MCP `token_usage_summary`: `Buckets`, `Buckets[].Models`, `ByModel` | Buckets limited to 1000 (window and width checked before reading; width at most 527040 minutes). Models aggregated to the top `modelLimit` (default 10, 1 to 100) plus `OtherModels`, `OtherModelCount`, `ModelCount` | `Services.SummaryBounds`, `E2E.DeliveryBounds/token_usage_summary_window_over_limit_is_typed_error`, `/token_usage_summary_breaks_out_model_limit` |
| REST `GET /api/v1/request-history/summary`: `Buckets` | Limited to 1000 buckets; width at most 527040 minutes | `Services.SummaryBounds/request_history_window_bounded`, `E2E.DeliveryBounds` |
| REST `GET /api/v1/missions/history`: `Buckets`, unpaged point scan, in-memory fleet filter | Limited to 1000 buckets (`MissionHistoryQuery.Validate`); counts grouped by bucket and status in SQL, fleet filter in SQL | `Database.WorkBounds/history_*`, `E2E.WorkBounds/mission_history_bucket_limit` |
| REST `GET /api/v1/status`, MCP `status`, WebSocket `status`: `Voyages`, `Voyages[].VesselIds` | Extracted: counts (`ActiveVoyages`, `InProgressVoyages`, `OpenVoyages`); `POST /api/v1/voyages/progress/enumerate`, MCP and WebSocket `list_voyage_progress`. `VesselIds` replaced by `VesselId` and `VesselCount` | `Database.WorkBounds/voyage_progress_pages_active_voyages`, `E2E.WorkBounds/voyage_progress_enumeration_pages`, `E2E.Status/get_status_embeds_no_voyage_list`, `E2E.McpTool/armada_list_voyage_progress_pages_active_voyages` |
| REST `GET /api/v1/fleets/{id}`, MCP and WebSocket `get_fleet`: `Vessels` | Extracted: `FleetDetailResponse { Fleet, VesselCount }`; vessels through `POST /api/v1/vessels/enumerate` with `fleetId` | `E2E.VesselFleetBounds/fleet_read_counts_vessels_and_pages_them`, `/fleet_vessel_count_is_tenant_scoped` |
| REST `GET /api/v1/voyages/{id}`, WebSocket `get_voyage`: `Missions` and their nested lists | Extracted: `VoyageProgress` (counts); missions through `POST /api/v1/missions/enumerate` with `voyageId` | `E2E.WorkBounds/voyage_detail_and_cancel_carry_counts`, `E2E.WebSocket` get_voyage |
| MCP `voyage_status`: `Missions` (opt-in) | `includeMissions: true` rejected with InvalidArgument naming `enumerate`; always returns counts | `E2E.McpTool/armada_voyage_status_include_missions_is_rejected` |
| REST `DELETE /api/v1/voyages/{id}`, MCP and WebSocket `cancel_voyage`: `NotCancelled` | Counts: `VoyageCancelResult` (`CancelledMissions`, `NotCancelledMissions`, `FirstNotCancelledReason`) | `E2E.WorkBounds/voyage_detail_and_cancel_carry_counts` |
| REST `GET /api/v1/missions/{id}`: `PlaybookSnapshots`, `AssignmentBlocker.BlockingMissionIds`, `.Captains` | Snapshots limited (one per selected playbook, at most 256); blocker lists the first 25 with `BlockingMissionCount` and `CaptainCount` | `MissionAssignmentBlocker.SetBlockingMissions`, `E2E.WorkBounds/dispatch_limits` |
| REST `GET /api/v1/missions/{id}/github/pull-request`: reviewers, labels, reviews, comments, checks | Counts on the detail (`ReviewCount`, `CommentCount`, `CheckCount`); reviewers and labels at most 100 with counts; `POST .../github/pull-request/{reviews,comments,checks}/enumerate` page GitHub with `per_page`/`page` | `E2E.GitHubIntegration/github_pull_request_lists_page_from_github` |
| REST `POST /api/v1/releases/{id}/github/pull-requests/enumerate`: page plus nested PR lists | Paged from the release's links (at most 1000), only the page is read from GitHub; nested lists as above | `GitHubIntegrationService.EnumerateReleasePullRequestsAsync` |
| REST `GET /api/v1/ask/threads/{id}`, MCP `get_ask_thread`: tracked work, proposals, tool calls, snapshot rows | Extracted: `TrackedWorkCount`, `PendingProposalCount`, `PendingCliPermissionCount`, `ToolCallCount`; `POST .../work/enumerate`, `.../proposals/enumerate`, `.../turn/tool-calls/enumerate`; MCP `list_ask_work`, `list_ask_proposals`, `list_ask_tool_calls` | `Database.BoundedCollections/ask_tracked_work_pages`, `/ask_proposal_pages`, `E2E.BoundedAskPlanning/ask_collections_paged_and_owner_scoped` |
| REST `GET /api/v1/ask/threads/{id}/work/{workId}`, MCP `get_ask_work`: `Missions`, `Targets` | Extracted: `MissionCount`, `TargetCount`; `POST .../work/{workId}/missions/enumerate`, `.../targets/enumerate`; MCP `list_ask_work_missions`, `list_ask_work_targets` | `E2E.BoundedAskPlanning/ask_collections_paged_and_owner_scoped` |
| REST `POST /api/v1/ask/threads/{id}/messages/enumerate`, MCP `list_ask_messages`, MCP `send_ask_message`: nested tool calls and snapshots | Extracted: `ToolCallCount`; `POST .../messages/{messageId}/tool-calls/enumerate`; snapshots carry counts only | `Database.BoundedCollections/ask_tool_call_pages_and_counts` |
| Planning-session transcripts (9 items: REST create, read, messages, stop, stop-turn; MCP create and get) | Extracted: `MessageCount`; `POST /api/v1/planning-sessions/{id}/messages/enumerate`, MCP `list_backlog_planning_session_messages` | `E2E.BoundedAskPlanning/planning_and_refinement_transcripts_paged` |
| Refinement-session transcripts (10 items: REST create (two prefixes), read, messages, stop; MCP create, get, send, stop) | Extracted: `MessageCount`; `POST /api/v1/objective-refinement-sessions/{id}/messages/enumerate`, MCP `list_backlog_refinement_session_messages` | `Database.BoundedCollections/refinement_sessions_and_transcript_pages`, `E2E.BoundedAskPlanning` |
| REST `GET` and `POST /api/v1/vessels/{id}/cleanup`: `Resources`, `Results`, `Keys` | Extracted: `ResourceCount`; `GET /api/v1/vessels/{id}/cleanup/resources` and `POST .../enumerate`; `Keys` limited to 1000; docks read in bounded pages | `Services.VesselCleanup/resources_page_with_exact_totals`, `/cleanup_rejects_too_many_keys` |
| REST `GET /api/v1/vessels/{id}/health`, `PUT`/`DELETE .../health/overrides/{criterion}`, MCP `vessel_health`: `Dependencies` | Extracted: `DependencyCount`; `GET /api/v1/vessels/{id}/health/dependencies`, `POST .../enumerate`, MCP `list_vessel_dependencies` (SQL paging) | `Database.VesselHealth/dependencies_enumerate_pages_in_sql`, `E2E.VesselHealthApi/dependencies_are_paged_not_embedded` |
| REST `GET /api/v1/vessels/{id}/history/commits`: `Commits[].ParentShas` | Fixed at 64 with `ParentCount`; `limit` above 200 rejected | `GitMachineOutputParser.ParseCommitHeader` |
| REST `GET /api/v1/workspace/vessels/{vesselId}/tree`: `Entries` | Paged, including split mode (the Harbor cuts the page; `HarborFileRequest.PageNumber`, `PageSize`) | `Services.VesselHost/workspace_tree_and_changes_on_harbor_are_paged` |
| REST `GET /api/v1/workspace/vessels/{vesselId}/changes`: `Changes` | Paged (git status parsed whole, page cut with an exact total) | `Services.VesselHost/workspace_tree_and_changes_on_harbor_are_paged` |
| REST `GET /api/v1/workspace/vessels/{vesselId}/status`: `ActiveMissions`, `ScopedFiles` | Extracted: `ActiveMissionCount`; `GET .../active-missions` and `POST .../enumerate` (SQL paging); `ScopedFiles` fixed at 256 with `ScopedFileCount` | `E2E.VesselFleetBounds/workspace_active_missions_page_from_the_database` |
| MCP `delete_vessel`: `Warnings` | Fixed at 50 with `WarningCount` | `VesselDeleteResult` |
| REST `GET /api/v1/storage`, `POST /api/v1/storage/refresh`: `Hosts`, `History[].Hosts`, `Database.Tables`, `Hosts[].Volumes` | Hosts extracted (`HostCount`, `GET /api/v1/storage/hosts` and `POST .../enumerate`); history breakdowns aggregated to 24 keys plus `(other)`; history capacity 2 to 1000; tables at most 1000 with a warning; volumes 256 | `Services.Storage/history_breakdowns_fold`, `E2E.Storage`, `Tui.Storage` |
| REST `GET /api/v1/harbors/{id}/metrics`, MCP `get_harbor_metrics`: segments, series, launch speed | Aggregated: launch speed 15 plus `IsOther`, token series 19 plus `IsOther`, segments coalesced above 1000 (`SegmentsCoalesced`, `RawSegmentCount`); totals kept | `Services.ConfigCollectionBounds/harbor_metrics_*` |
| REST `GET /api/v1/battery/runs/{id}`, MCP `get_battery_run`: `Packs`, `Results`, `Results[].Details.*` | Results extracted: `GET /api/v1/battery/runs/{id}/results`, `POST .../enumerate`, MCP `list_battery_results` (SQL paging); packs limited to 256; details bounded by manifest limits, false positives fixed at 256 with `FalsePositiveCount` | `Database.BatteryPersistence/results_enumerate_pages_in_database`, `E2E.Battery`, `Services.Battery/seeded_false_positives_bounded_and_counted` |
| REST `POST /api/v1/tenants/lookup`: `Tenants` | Paged in SQL (users joined to tenants, ordered by name then id) | `Database.DeliveryEnumeration/tenant_lookup_pages_active_tenants`, `E2E.DeliveryBounds/tenant_lookup_pages_with_totals` |

### Bulk-delete and request-echo lists (audit 5.2, 31 items)

| Surface | Change | Evidence |
|---|---|---|
| Bulk deletes of missions, voyages, signals, events (REST `delete/multiple` and MCP `delete_*`) | Limited: more than 1000 ids rejected before anything is deleted; `Skipped` fixed by the request | `E2E.WorkBounds/bulk_delete_limit_*`, `E2E.ResponseShape/oversized_requests_are_typed_errors` |
| Bulk deletes of vessels, fleets, docks | Limited (1000) | `E2E.VesselFleetBounds/bulk_deletes_reject_over_limit` |
| Bulk delete of captains | Limited (1000) | `E2E.ConfigBoundsApi/captains_bulk_delete_rejects_over_limit` |
| Bulk delete of workflow profiles | Limited (1000) | `E2E.ConfigCatalogBounds/workflow_profile_bulk_delete_limited` |
| Bulk delete of request history | Limited (1000) | `E2E.DeliveryBounds` |
| Ask threads bulk delete (`DeletedIds`, `Skipped`; MCP `delete_ask_threads`) | Limited (1000) | `E2E.BoundedAskPlanning/ask_bulk_delete_limit` |
| Merge queue purge (`Skipped`; MCP `purge_merge_entries`) | Limited (1000) in `MergeQueueService.DeleteMultipleAsync` | `E2E.MergeQueue/purge_over_request_limit_rejected` |
| Objective and backlog reorder (REST and MCP, 4 items) | Limited (1000 items); response is `ObjectiveReorderResult` (counts and applied ranks), not the objectives | `E2E.BoundedAskPlanning/objective_and_memory_list_limits` |
| Refinement summarize and apply (REST and MCP, 4 items): `AcceptanceCriteria`, `NonGoals`, `RolloutConstraints` | Limited to 256, validated before the summary is broadcast and on apply | `ObjectiveRefinementCoordinator`, `ObjectiveService` |

### Paged lists whose objects carried unbounded lists (audit 5.3, 61 items)

Every page was already bounded; the nested lists are now limited at their models (next section), and the routes that
paged in memory now page in the data source.

| Surface | Change | Evidence |
|---|---|---|
| Vessels list and enumerate | M:Vessel limited | `E2E.VesselFleetBounds/vessel_config_lists_limited_on_every_write_path` |
| Harbors enumerate (REST, MCP `enumerate harbors`) | Paged in SQL; M:Harbor limited | `Database.ScopedPaging/harbors_page_in_the_database` |
| Model endpoints enumerate (REST, MCP) | Paged in SQL with the viewer scope before counting; M:ModelEndpoint limited | `Database.ScopedPaging/model_endpoints_page_with_viewer_scope` |
| Personas list and enumerate (REST, MCP) | Visibility applied in SQL before counting (pages were short); M:Persona limited | `Database.ScopedPaging/personas_page_with_viewer_scope` |
| Environments, incidents, runbooks, runbook executions, deployments, releases (list and enumerate) | Incidents and runbook executions moved to tables and paged in SQL (was in memory, max 500); runbooks paged in the playbooks table; models limited | `Database.DeliveryEnumeration`, `E2E.DeliveryBounds/incidents_and_runbook_executions_page_with_totals` |
| Fleet actions and fleet action runs enumerate (REST, MCP) | Models limited | `E2E.FleetActionApi/collection_limits_rest`, `/collection_limits_mcp` |
| Check runs list and enumerate | M:CheckRun limited | `E2E.WorkflowProfileCheckRun/check_runs_import_rejects_too_many_artifacts` |
| Battery packs list and enumerate, MCP `list_battery_packs` | Paged from disk (only the page's manifests are read); manifests limited | `Services.Battery/catalog_pages_from_disk`, `/manifest_over_limits_is_malformed` |
| Handoff artifacts list and enumerate, MCP `list_handoff_artifacts` | M:Handoff fixed | `Services.HandoffArtifacts/builder_bounds_agent_lists_with_marker` |
| Workflow profiles, pipelines, project profiles, playbooks, prompt templates, skills (list and enumerate) | Visibility applied in SQL before counting (pages were short and totals inexact); models limited | `Database.ConfigVisibilityPaging`, `E2E.ConfigCatalogBounds/regular_user_pages_are_full_with_exact_totals` |
| Objectives and backlog list and enumerate, MCP `list_objectives`, `list_backlog` | Paged and filtered in SQL (was in memory, max 500); M:Objective limited | `Database.BoundedCollections/objective_pages_filters_and_scope` |
| Planning sessions enumerate | Paged in SQL | `Database.BoundedCollections/planning_session_pages` |
| Memories list and enumerate, MCP `search_memory` | M:Memory limited | `E2E.BoundedAskPlanning/objective_and_memory_list_limits` |
| REST `POST /api/v1/check-runs/sync/github-actions` | Pages GitHub (`per_page` 1 to 100, `PageNumber`); returns `CheckRunIds` and source totals instead of embedded runs | `E2E.GitHubIntegration` |
| MCP `enumerate` (33 entity types) | Every type returns the standard page shape; harbors, model endpoints, personas, skills, cli permission rules, prompt templates, pipelines, playbooks and the profile types page in SQL; `missions` and `voyages` with `includeDescription` return scalar rows | `E2E.ConfigCatalogBounds/enumerate_pages_have_standard_shape`, `E2E.ResponseShape/live_mcp_reads_carry_only_bounded_arrays` |

### Single-entity reads and writes carrying model lists (audit 5.4, 136 items)

Each model's lists are declared with `[CollectionBound]` and enforced on every write path (REST, MCP, WebSocket,
internal services); reads return only what writes allowed.

| Model (items) | Limits | Enforced in | Evidence |
|---|---|---|---|
| Vessel (10, including readiness) | Pattern and glob lists 256; readiness issues at most 200 with `IssueCount` | Vessel create and update in all four providers | `E2E.VesselFleetBounds/vessel_config_lists_limited_on_every_write_path`, `E2E.WebSocket/create_vessel_over_config_limit_returns_invalid_argument` |
| Mission and voyage (4) | `SelectedPlaybooks` 256, missions per dispatch 1000 | `PlaybookService.ResolveSelectionsAsync`, `AdmiralService` dispatch | `E2E.WorkBounds/dispatch_limits` |
| Planning session and dispatch (2) | `SelectedPlaybooks` 256; dispatch returns `PlanningSessionDispatchResult` | `PlanningSessionCoordinator`, `PlanningDispatchLinker` | `E2E.BoundedAskPlanning` |
| Objective (15) | 6 text lists 256, 11 link id lists 1000 | `ObjectiveService` (create, update, link, import) | `E2E.BoundedAskPlanning/objective_and_memory_list_limits` |
| Memory (6) | `Tags` 256 | `MemoryService` | same |
| Harbor (9) | `Capabilities` 256, also on the link handshake | `HarborService`, `HarborConnectionManager` | `Services.ConfigCollectionBounds/harbor_*`, `E2E.ConfigBoundsApi/harbor_create_rejects_capabilities_and_pages` |
| Model endpoint (8) | `Headers`, `AllowedTools` 256; `HealthHistory` server-owned, 500 | `ModelEndpointService` | `Services.ConfigCollectionBounds/model_endpoint_lists_bounded_and_history_server_owned` |
| Mux endpoint (2) | Paged list (`MuxEndpointPage`, `POST .../mux/endpoints/enumerate`); headers 256 | `MuxCliService` | `mux_endpoints_return_a_page` |
| Captain tools (2) | `Tools` one page (`POST /api/v1/captains/{id}/tools/enumerate`); `Servers` 256 | `CaptainToolService` | `captain_tools_return_a_page` |
| Persona (6) | `CaptainPriorities` 256 | `PersonaWriteService` | `persona_priorities_rejected_over_limit` |
| Settings (2) | Import roots and exclusions, offline Harbor ids 256; enum lists one entry per value | Settings `PUT` | `E2E.DeliveryBounds/settings_update_rejects_oversized_lists`, `/enum_lists_keep_one_entry_per_value` |
| Deployment environment (3) | `VerificationDefinitions`, headers 256 | `DeploymentEnvironmentService` | `E2E.DeliveryBounds` |
| Incident (3) | `RescueMissionIds` 1000 | `IncidentService`, `MissionRecoveryCoordinator` | `Database.DeliveryEnumeration` |
| Runbook (4) and runbook execution (5) | Steps and parameters 256 (excess counted in `OmittedStepCount`, `OmittedParameterCount`); execution values 256 | `RunbookService` | `Database.DeliveryEnumeration/runbooks_page_in_the_playbooks_table`, `/runbook_executions_page_and_filter_in_the_database` |
| Deployment (11) | `CheckRunIds` 1000 (verification checks capacity first), `ObjectiveIds` 1000 | `DeploymentService` | `E2E.DeliveryBounds` |
| Release (6) | `VoyageIds`, `MissionIds`, `CheckRunIds`, `Artifacts` 1000; a voyage link that would expand past 1000 missions is rejected | `ReleaseService` | `E2E.DeliveryBounds` |
| Fleet action and run (10) | Tools, inputs, allowed values, input values 256; run vessel ids 1000 (per-run maximum now 1000, was 5000); run read no longer embeds `Targets` | `FleetActionService`, `FleetActionInputValidator` | `E2E.FleetActionApi/collection_limits_rest`, `/run_accepted_and_finishes` |
| Check run (8) | `Artifacts` 256 | `CheckRunService` | `E2E.WorkflowProfileCheckRun/check_runs_import_rejects_too_many_artifacts` |
| Battery run packs (2) | 256 | `BatteryRunService.StartAsync` | `E2E.Battery` |
| Handoff artifact (2) | `Findings`, `AcceptanceCriteria`, `CompletionLines` 255 plus a marker naming the omitted count; other lists fixed (8, 1, 20) | `HandoffArtifactBuilder.Build` | `Services.HandoffArtifacts/builder_bounds_agent_lists_with_marker` |
| Workflow profile and command previews (12) | Lists 256; previews at most 15 + 6 x 256 | `WorkflowProfileAccessService` | `E2E.ConfigCatalogBounds/workflow_profile_lists_limited` |
| Pipeline (6) | `Stages` 256 (REST, MCP, WebSocket) | Pipeline routes, MCP tools, WebSocket handler | `E2E.ConfigCatalogBounds/pipeline_stages_limited` |
| Project profile (4) | `PersonaOverrides`, `Skills` 256 | `ProjectProfileService.ValidateAsync` | `E2E.ConfigCatalogBounds/project_profile_lists_limited` |

### Paged in memory, now paged at the source (audit 6)

| Surface | Change | Evidence |
|---|---|---|
| REST `POST /api/v1/inbox/enumerate`, MCP `enumerate inbox` | Missions, merge entries and deployments paged and counted in SQL in a fixed kind order; transient kinds (stalled captains, Ask proposals, CLI requests, capped at 1000) merged in | `Database.WorkBounds/inbox_pages_at_source` |
| REST `POST /api/v1/cli-permissions/rules/enumerate`, MCP `enumerate cli_permission_rules` | Paged in SQL | `Database.ScopedPaging/cli_rules_page_in_the_database` |
| REST `POST /api/v1/push/devices/enumerate` | Paged in SQL | `Database.DeliveryEnumeration/push_devices_page_oldest_first` |
| REST `GET`/`POST /api/v1/harbors/{id}/jobs(/enumerate)`, MCP `list_harbor_jobs` | Paged in SQL | `Database.ScopedPaging/harbor_jobs_page_in_the_database` |
| REST `GET`/`POST /api/v1/failure-cause-rules(/enumerate)`, MCP `list_failure_cause_rules` | Paged in SQL | `Database.FailureCauseRules/enumerate_page_in_database` |
| Refinement session lists (both prefixes), MCP `list_backlog_refinement_sessions` | Paged in SQL, max 1000 | `Database.BoundedCollections/refinement_sessions_and_transcript_pages` |
| MCP `list_prompt_templates` | Paged in SQL, standard 1 to 1000 (was 100 in memory) | `list_prompt_templates_pages_in_data_source` |
| REST `POST /api/v1/vessels/{id}/branches/enumerate` | Kept: `git for-each-ref` cannot offset; per-branch work runs only for the page. Malformed body is 400 | `VesselRoutes` |
| WebSocket `list_merge_queue`, `enumerate merge_queue` (new finding) | Paged in SQL | `E2E.WebSocket/list_merge_queue_pages_in_database` |

### New findings closed during implementation

- Equal timestamps: voyages, missions, signals, events, docks, vessels, fleets and the configuration catalogs now
  break ordering ties by id in every provider (`Database.WorkBounds/equal_timestamps_page_by_id`,
  `Database.DockDatabase/dock_enumerate_pages_ties_by_id`).
- Visibility after paging (personas, skills, pipelines, playbooks, prompt templates, workflow and project profiles):
  pages came back short and totals counted rows the caller could not see. Fixed in SQL in every provider.
- Vessel import: discovery candidates capped at 1000 (was configurable up to 100000) with `Truncated`; browse is paged;
  import and recommendation requests reject more than 1000 paths, fleets or vessels.
- Captain chat `History` limited to 1000 messages; Harbor probe `Arguments` 256; failure-cause rule `Match` lists 256;
  `HarborReasons` on checkout errors at most 50 with `HarborReasonCount`.

### Black box surfaces (v1.2.0)

| Surface | Bound | Evidence |
|---|---|---|
| REST `GET /api/v1/blackbox` and `POST .../enumerate`, `POST .../search`, `GET`/`POST .../missions/{id}/blackbox/entries[/enumerate]`, `.../{id}/objects[/enumerate]`, `.../{id}/notes[/enumerate]`, `POST .../hotspots/enumerate`, `POST .../fingerprints/enumerate`; MCP `list_blackbox_entries`, `list_blackbox_objects`, `list_blackbox_notes`, `list_blackbox_hotspots`, `list_blackbox_fingerprints`, `search_blackbox`, `enumerate` entityType `blackbox`; WebSocket `blackbox.list` | **Paged** in SQL with the caller's scope before counting (default 10, 1 to 1000 normalized); search query 1 to 200 characters | `E2E.BlackBox/rest_lists_and_analytics`, `/mcp_read_tools`, `/websocket_commands`, `Database.BlackBoxEntries`, `Database.BlackBoxFiles`, `Database.BlackBoxObjects`, `Database.BlackBoxLinksNotes` |
| Object reads (`GET .../objects/{name}`, MCP `read_blackbox_object`, resource `armada://blackbox/{entryId}/objects/{name}`) | **Limited**: REST 1 MB and MCP 64 KB per read (length outside the range is 400 / `InvalidArgument`), with `X-BlackBox-Next-Offset` / `nextOffset` and the total; the resource returns the first 64 KB and names the offset to continue with `read_blackbox_object` | `E2E.BlackBox/rest_sections_objects_download` (3 MB in 1 MB reads), `/mcp_read_tools` (64 KB chunks), `/mcp_resources_and_prompts` |
| `GET .../blackbox/{id}/download` (.zip) | **Fixed**: the entry's objects (at most 128 per entry, each at most `maxEvidenceObjectBytes`, the entry at most `maxEntryBytes`) plus `manifest.json`, whose `Objects` and `MissingObjects` lists are bounded at 128 | `E2E.BlackBox/rest_sections_objects_download`, `BlackBoxDownloadManifest` |
| `POST /api/v1/blackbox/delete`, MCP `delete_blackbox_entries` | **Limited** to 1000 ids (CollectionLimitExceeded before anything is deleted) | `E2E.BlackBox/rest_limits_and_authorization`, `/mcp_write_tools_and_errors` |
| Entry, record, section and brief reads (REST, MCP `get_blackbox`, `read_blackbox_section`, resources `brief.md` and `record.json`, WebSocket `blackbox.get` and `blackbox.brief`) | **Fixed**: every record list carries `[CollectionBound]` (`BlackBoxLimits`), `record.json` at most 256 KB, briefs at most 40 KB with the ten newest notes | `Models.BlackBoxSchema`, `E2E.ResponseShape`, `E2E.BlackBox/rest_briefs_by_audience` |
| `POST /api/v1/blackbox/bundle`, MCP `get_blackbox_bundle` | **Limited**: `max` clamped to 1 to 50 entries; Markdown cut at 1 MB with `Truncated`; at most 10 Armada versions per group | `Services.BlackBoxMaintainer/bundle_grouping_and_cap` |
| `GET /api/v1/blackbox/fingerprints/{fingerprint}/trend`, MCP `get_blackbox_fingerprint_trend` | **Fixed**: counts the newest 1000 entries of the fingerprint (read in pages of 100) and lists at most 100 versions | `Services.BlackBoxMaintainer/fingerprint_trend` |
| `POST /api/v1/blackbox/purge`, MCP `purge_blackbox` | **Limited**: `maxEntries` clamped to 1 to 100000 (default 10000), deleted in pages of 200; the result carries counts and at most 100 `SampleEntryIds` | `E2E.BlackBox/rest_global_admin_routes`, `BlackBoxPurgeResult` |
| `POST /api/v1/blackbox/gc`, MCP `run_blackbox_gc`, event `blackbox.gc_completed` | **Fixed**: counts, at most 100 `CandidateEntryIds` and 20 `Errors`; each pass works in batches of `gcBatchSize` (10 to 1000), deletes store objects 1000 keys at a time, and reads at most 100000 keys per orphan sweep | `Services.BlackBoxGarbageCollector/expiry_pinned_current_dry_run`, `Services.BlackBoxRetention`, `BlackBoxGcReport` |
| `POST /api/v1/blackbox/backfill` | **Limited**: `maxSubjects` clamped to 1 to 100000 per subject kind, read in pages of `blackBox.sweeper.pageSize` (1 to 1000); the result is counts | `E2E.BlackBox/rest_global_admin_routes`, `BlackBoxSweeper` |
| `POST /api/v1/blackbox/storage/migrate`, `GET .../storage/migrations/{id}` | **Limited**: `batchSize` clamped to 1 to 1000 (default 50); the job is scalars and a cursor | `Services.BlackBoxStorageTargets/migration_failures_and_restart`, `BlackBoxStorageMigrationJob` |
| `GET /api/v1/blackbox/retention`, `GET /api/v1/blackbox/storage`, MCP `get_blackbox_retention`, `get_blackbox_storage` | **Fixed**: counts; `Usage` at most one row per storage target (7) | `E2E.BlackBox/rest_global_admin_routes`, `BlackBoxStorageStatus` |
| Backup manifest `blackBox` section (`GET /api/v1/backup`, MCP `backup`) and the restore result's `BlackBox` section | **Fixed**: `ExternalEntryRefs` at most 10000 with `ExternalEntryRefsTruncated`, `ExternalTargets` at most 7; the restore result lists at most 100 external entry ids | `Services.BlackBoxStorageTargets/backup_inventory`, `BlackBoxBackupInventory` |

### Deliberately left, with reasons

- `GET /api/v1/history` and `POST /api/v1/history/enumerate` return a bounded page, but the server still walks its 13
  sources in 500-row batches. Paging at the source needs a materialized timeline table; tracked as follow-up work.
- Token-usage and request-history summaries read every record in the window before aggregating; the response is
  bounded (1000 buckets, top models), the server load is not. SQL aggregation is follow-up work.
- Rows stored before these limits existed (for example an objective with more than 256 tags, or an import batch of up
  to 5000 items) are not rewritten. Writes reject them; reads still return them until they are edited or expire.
- Mux endpoints, workspace changes, vessel cleanup inventory and branch lists are cut from a source (a CLI, git, the
  file system) that cannot page; the page and its exact total are computed from the source's full answer on the host.
