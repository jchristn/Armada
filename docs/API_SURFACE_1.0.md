# Armada API Surface 1.0

> **Type:** reference (generated). Do not edit by hand: run `scripts/common/generate-api-surface.sh`, which boots a throwaway
> Admiral and rewrites this file and `docs/api-surface-1.0.json` from the running code. The `E2E.ApiContract`
> test compares the live surface to the JSON file and fails on removals and incompatible changes. What the
> promise covers, and how items are deprecated, marked experimental, and graduated, is in
> [COMPATIBILITY.md](COMPATIBILITY.md).

Items marked **experimental** are excluded from the 1.0 compatibility promise. Authorization levels are the
declared requirements from `RouteAuthorizationRegistry` and `McpToolAuthorizationRegistry`: `NoAuthRequired`,
`Authenticated`, `TenantAdmin` (tenant administrator or global administrator), `AdminOnly` (global
administrator).

## Contents

- [Counts](#counts)
- [REST API](#rest-api)
- [MCP tools](#mcp-tools)
- [WebSocket](#websocket)
- [CLI](#cli)
- [Settings](#settings)

## Counts

| Surface | Total | Experimental |
|---|---|---|
| REST routes | 350 | 11 |
| MCP tools | 146 | 5 |
| WebSocket endpoints | 2 | 1 |
| WebSocket commands | 59 | 0 |
| WebSocket event types | 65 | 0 |
| CLI commands | 58 | 0 |
| Settings keys | 175 | 12 |

## REST API

Base path `/api/v1`. Request and response bodies are PascalCase JSON; errors are `ApiErrorResponse` (see
[REST_API.md](REST_API.md)). Type names are the OpenAPI schema names (`GET /openapi.json`).

| Method | Route | Auth | Request | Responses | Status |
|---|---|---|---|---|---|
| PUT | `/api/v1/account/password` | Authenticated | `PasswordChangeRequest` | 200 `WhoAmIResult` |  |
| GET | `/api/v1/ask/quick-actions` | Authenticated |  | 200 `List<AskQuickAction>` |  |
| POST | `/api/v1/ask/threads` | Authenticated | `AskThreadCreateRequest` (optional) | 201 `AskThread` |  |
| POST | `/api/v1/ask/threads/enumerate` | Authenticated | `AskThreadEnumerateRequest` (optional) | 200 `EnumerationResult<AskThread>` |  |
| DELETE | `/api/v1/ask/threads/{id}` | Authenticated |  | 204 |  |
| GET | `/api/v1/ask/threads/{id}` | Authenticated |  | 200 `AskThreadDetail` |  |
| PUT | `/api/v1/ask/threads/{id}` | Authenticated | `AskThreadUpdateRequest` | 200 `AskThread` |  |
| POST | `/api/v1/ask/threads/{id}/actions` | Authenticated | `AskActionRequest` | 200 `AskActionProposal` |  |
| POST | `/api/v1/ask/threads/{id}/cancel` | Authenticated |  |  |  |
| POST | `/api/v1/ask/threads/{id}/messages` | Authenticated | `AskMessageSendRequest` | 202 `AskMessageSendResponse` |  |
| POST | `/api/v1/ask/threads/{id}/messages/enumerate` | Authenticated | `AskMessageEnumerateRequest` (optional) | 200 `AskMessagePage` |  |
| POST | `/api/v1/ask/threads/{id}/proposals/{pid}/approve` | Authenticated |  | 200 `AskActionProposal` |  |
| POST | `/api/v1/ask/threads/{id}/proposals/{pid}/reject` | Authenticated |  | 200 `AskActionProposal` |  |
| POST | `/api/v1/ask/threads/{id}/read` | Authenticated |  | 200 `AskThread` |  |
| POST | `/api/v1/ask/threads/{id}/summarize` | Authenticated |  |  |  |
| GET | `/api/v1/ask/threads/{id}/work/{workId}` | Authenticated |  | 200 `AskWorkSnapshot` |  |
| POST | `/api/v1/authenticate` | NoAuthRequired |  |  |  |
| GET | `/api/v1/backlog` | Authenticated |  | 200 `EnumerationResult<Objective>` |  |
| POST | `/api/v1/backlog` | TenantAdmin | `ObjectiveUpsertRequest` | 201 `Objective` |  |
| POST | `/api/v1/backlog/enumerate` | Authenticated | `ObjectiveQuery` (optional) | 200 `EnumerationResult<Objective>` |  |
| POST | `/api/v1/backlog/reorder` | TenantAdmin | `ObjectiveReorderRequest` | 200 `List<Objective>` |  |
| DELETE | `/api/v1/backlog/{id}` | TenantAdmin |  | 204, 404 |  |
| GET | `/api/v1/backlog/{id}` | Authenticated |  | 200 `Objective`, 404 |  |
| PUT | `/api/v1/backlog/{id}` | TenantAdmin | `ObjectiveUpsertRequest` | 200 `Objective`, 404 |  |
| GET | `/api/v1/backlog/{id}/refinement-sessions` | Authenticated |  | 200 `List<ObjectiveRefinementSession>` |  |
| POST | `/api/v1/backlog/{id}/refinement-sessions` | TenantAdmin | `ObjectiveRefinementSessionCreateRequest` | 201 `ObjectiveRefinementSessionDetail` |  |
| GET | `/api/v1/backup` | AdminOnly |  |  |  |
| GET | `/api/v1/captains` | Authenticated |  | 200 `EnumerationResult<Captain>` |  |
| POST | `/api/v1/captains` | TenantAdmin | `Captain` | 201 `Captain` |  |
| POST | `/api/v1/captains/delete/multiple` | TenantAdmin | `DeleteMultipleRequest` | 200 `DeleteMultipleResult` |  |
| POST | `/api/v1/captains/enumerate` | Authenticated | `EnumerationQuery` (optional) |  |  |
| POST | `/api/v1/captains/stop-all` | TenantAdmin |  |  |  |
| DELETE | `/api/v1/captains/{id}` | TenantAdmin |  | 204, 404, 409 `Object` |  |
| GET | `/api/v1/captains/{id}` | Authenticated |  | 200 `Captain`, 404 |  |
| PUT | `/api/v1/captains/{id}` | TenantAdmin | `Captain` | 200 `Captain`, 404 |  |
| POST | `/api/v1/captains/{id}/chat` | TenantAdmin | `CaptainChatRequest` | 200 `CaptainChatResponse` |  |
| GET | `/api/v1/captains/{id}/log` | Authenticated |  | 404 |  |
| POST | `/api/v1/captains/{id}/stop` | TenantAdmin |  | 404 |  |
| GET | `/api/v1/captains/{id}/tools` | Authenticated |  | 200 `CaptainToolAccessResult`, 404 |  |
| POST | `/api/v1/captains/{id}/unquarantine` | TenantAdmin |  | 404 |  |
| GET | `/api/v1/check-runs` | Authenticated |  | 200 `EnumerationResult<CheckRun>` |  |
| POST | `/api/v1/check-runs` | TenantAdmin | `CheckRunRequest` | 201 `CheckRun` |  |
| POST | `/api/v1/check-runs/enumerate` | Authenticated | `CheckRunQuery` (optional) | 200 `EnumerationResult<CheckRun>` |  |
| POST | `/api/v1/check-runs/import` | TenantAdmin | `CheckRunImportRequest` | 201 `CheckRun` |  |
| POST | `/api/v1/check-runs/sync/github-actions` | TenantAdmin | `GitHubActionsSyncRequest` | 200 `GitHubActionsSyncResult` |  |
| DELETE | `/api/v1/check-runs/{id}` | TenantAdmin |  | 204, 404 |  |
| GET | `/api/v1/check-runs/{id}` | Authenticated |  | 200 `CheckRun`, 404 |  |
| POST | `/api/v1/check-runs/{id}/retry` | TenantAdmin |  | 201 `CheckRun`, 404 |  |
| GET | `/api/v1/credentials` | Authenticated |  |  |  |
| POST | `/api/v1/credentials` | Authenticated |  |  |  |
| DELETE | `/api/v1/credentials/{id}` | Authenticated |  |  |  |
| GET | `/api/v1/credentials/{id}` | Authenticated |  |  |  |
| PUT | `/api/v1/credentials/{id}` | Authenticated |  |  |  |
| GET | `/api/v1/deployments` | Authenticated |  | 200 `EnumerationResult<Deployment>` |  |
| POST | `/api/v1/deployments` | TenantAdmin | `DeploymentUpsertRequest` | 201 `Deployment` |  |
| POST | `/api/v1/deployments/enumerate` | Authenticated | `DeploymentQuery` (optional) | 200 `EnumerationResult<Deployment>` |  |
| DELETE | `/api/v1/deployments/{id}` | TenantAdmin |  | 204, 404 |  |
| GET | `/api/v1/deployments/{id}` | Authenticated |  | 200 `Deployment`, 404 |  |
| PUT | `/api/v1/deployments/{id}` | TenantAdmin | `DeploymentUpsertRequest` | 200 `Deployment`, 404 |  |
| POST | `/api/v1/deployments/{id}/approve` | TenantAdmin | `DeploymentApprovalBody` (optional) | 200 `Deployment`, 404 |  |
| POST | `/api/v1/deployments/{id}/deny` | TenantAdmin | `DeploymentApprovalBody` (optional) | 200 `Deployment`, 404 |  |
| POST | `/api/v1/deployments/{id}/rollback` | TenantAdmin |  | 200 `Deployment`, 404 |  |
| POST | `/api/v1/deployments/{id}/verify` | TenantAdmin |  | 200 `Deployment`, 404 |  |
| GET | `/api/v1/docks` | Authenticated |  | 200 `EnumerationResult<Dock>` |  |
| POST | `/api/v1/docks/delete/multiple` | TenantAdmin | `DeleteMultipleRequest` | 200 `DeleteMultipleResult` |  |
| POST | `/api/v1/docks/enumerate` | Authenticated | `EnumerationQuery` (optional) |  |  |
| DELETE | `/api/v1/docks/{id}` | TenantAdmin |  | 204, 404, 409 `Object` |  |
| GET | `/api/v1/docks/{id}` | Authenticated |  | 200 `Dock`, 404 |  |
| DELETE | `/api/v1/docks/{id}/purge` | TenantAdmin |  | 200 `Object`, 404 |  |
| POST | `/api/v1/docks/{id}/repair` | TenantAdmin |  | 200 `Object`, 404 |  |
| POST | `/api/v1/docks/{id}/unstick` | TenantAdmin |  | 200 `Object`, 404 |  |
| GET | `/api/v1/doctor` | Authenticated |  |  |  |
| GET | `/api/v1/environments` | Authenticated |  | 200 `EnumerationResult<DeploymentEnvironment>` |  |
| POST | `/api/v1/environments` | TenantAdmin | `DeploymentEnvironmentUpsertRequest` | 201 `DeploymentEnvironment` |  |
| POST | `/api/v1/environments/enumerate` | Authenticated | `DeploymentEnvironmentQuery` (optional) | 200 `EnumerationResult<DeploymentEnvironment>` |  |
| DELETE | `/api/v1/environments/{id}` | TenantAdmin |  | 204, 404 |  |
| GET | `/api/v1/environments/{id}` | Authenticated |  | 200 `DeploymentEnvironment`, 404 |  |
| PUT | `/api/v1/environments/{id}` | TenantAdmin | `DeploymentEnvironmentUpsertRequest` | 200 `DeploymentEnvironment`, 404 |  |
| GET | `/api/v1/events` | Authenticated |  | 200 `EnumerationResult<ArmadaEvent>` |  |
| POST | `/api/v1/events/delete/multiple` | TenantAdmin | `DeleteMultipleRequest` | 200 `DeleteMultipleResult` |  |
| POST | `/api/v1/events/enumerate` | Authenticated | `EnumerationQuery` (optional) |  |  |
| DELETE | `/api/v1/events/{id}` | TenantAdmin |  | 204, 404 |  |
| POST | `/api/v1/fleet-action-runs/enumerate` | Authenticated | `EnumerationQuery` (optional) | 200 `EnumerationResult<FleetActionRun>` |  |
| GET | `/api/v1/fleet-action-runs/{id}` | Authenticated |  | 200 `FleetActionRunDetail`, 404 |  |
| POST | `/api/v1/fleet-action-runs/{id}/cancel` | TenantAdmin |  | 200 `FleetActionRun`, 404 |  |
| POST | `/api/v1/fleet-action-runs/{id}/targets/enumerate` | Authenticated | `FleetActionTargetEnumerateRequest` (optional) | 200 `EnumerationResult<FleetActionRunTargetSummary>`, 404 |  |
| GET | `/api/v1/fleet-action-runs/{id}/targets/{targetId}` | Authenticated |  | 200 `FleetActionRunTarget`, 404 |  |
| POST | `/api/v1/fleet-actions` | TenantAdmin | `FleetActionUpsertRequest` | 201 `FleetAction` |  |
| POST | `/api/v1/fleet-actions/enumerate` | Authenticated | `FleetActionEnumerateRequest` (optional) | 200 `EnumerationResult<FleetAction>` |  |
| POST | `/api/v1/fleet-actions/run` | TenantAdmin | `FleetActionRunRequest` | 202 `FleetActionRunStartResult` |  |
| DELETE | `/api/v1/fleet-actions/{id}` | TenantAdmin |  | 404 |  |
| GET | `/api/v1/fleet-actions/{id}` | Authenticated |  | 200 `FleetAction`, 404 |  |
| PUT | `/api/v1/fleet-actions/{id}` | TenantAdmin | `FleetActionUpsertRequest` | 200 `FleetAction`, 404 |  |
| POST | `/api/v1/fleet-actions/{id}/run` | TenantAdmin | `FleetActionRunRequest` | 202 `FleetActionRunStartResult`, 404 |  |
| GET | `/api/v1/fleets` | Authenticated |  | 200 `EnumerationResult<Fleet>` |  |
| POST | `/api/v1/fleets` | TenantAdmin | `Fleet` | 201 `Fleet` |  |
| POST | `/api/v1/fleets/delete/multiple` | TenantAdmin | `DeleteMultipleRequest` | 200 `DeleteMultipleResult` |  |
| POST | `/api/v1/fleets/enumerate` | Authenticated | `EnumerationQuery` (optional) |  |  |
| DELETE | `/api/v1/fleets/{id}` | TenantAdmin |  | 204 |  |
| GET | `/api/v1/fleets/{id}` | Authenticated |  | 200 `Fleet`, 404 |  |
| PUT | `/api/v1/fleets/{id}` | TenantAdmin | `Fleet` | 200 `Fleet`, 404 |  |
| GET | `/api/v1/harbors` | Authenticated |  | 200 `List<Harbor>` | experimental |
| POST | `/api/v1/harbors` | Authenticated | `Harbor` | 201 `Harbor`, 400 | experimental |
| DELETE | `/api/v1/harbors/{id}` | Authenticated |  | 204, 404 | experimental |
| GET | `/api/v1/harbors/{id}` | Authenticated |  | 200 `Harbor`, 404 | experimental |
| PUT | `/api/v1/harbors/{id}` | Authenticated | `Harbor` | 200 `Harbor`, 404 | experimental |
| POST | `/api/v1/harbors/{id}/disable` | Authenticated |  | 200 `Harbor`, 404 | experimental |
| POST | `/api/v1/harbors/{id}/enable` | Authenticated |  | 200 `Harbor`, 404 | experimental |
| POST | `/api/v1/harbors/{id}/probe` | TenantAdmin | `HarborProbeRequest` (optional) | 200 `HostCommandResult`, 404 | experimental |
| GET | `/api/v1/history` | Authenticated |  | 200 `EnumerationResult<HistoricalTimelineEntry>` |  |
| POST | `/api/v1/history/enumerate` | Authenticated | `HistoricalTimelineQuery` (optional) | 200 `EnumerationResult<HistoricalTimelineEntry>` |  |
| GET | `/api/v1/inbox` | Authenticated |  | 200 `List<InboxItem>` |  |
| GET | `/api/v1/incidents` | Authenticated |  | 200 `EnumerationResult<Incident>` |  |
| POST | `/api/v1/incidents` | TenantAdmin | `IncidentUpsertRequest` | 201 `Incident` |  |
| POST | `/api/v1/incidents/enumerate` | Authenticated | `IncidentQuery` (optional) | 200 `EnumerationResult<Incident>` |  |
| DELETE | `/api/v1/incidents/{id}` | TenantAdmin |  | 204, 404 |  |
| GET | `/api/v1/incidents/{id}` | Authenticated |  | 200 `Incident`, 404 |  |
| PUT | `/api/v1/incidents/{id}` | TenantAdmin | `IncidentUpsertRequest` | 200 `Incident`, 404 |  |
| GET | `/api/v1/jobs` | Authenticated |  | 400 |  |
| GET | `/api/v1/jobs/{id}` | Authenticated |  | 404 |  |
| POST | `/api/v1/jobs/{id}/cancel` | Authenticated |  | 404 |  |
| GET | `/api/v1/memories` | Authenticated |  | 200 `EnumerationResult<Memory>` |  |
| POST | `/api/v1/memories` | Authenticated | `Memory` | 201 `Memory`, 400 |  |
| DELETE | `/api/v1/memories/{id}` | Authenticated |  | 204, 404 |  |
| GET | `/api/v1/memories/{id}` | Authenticated |  | 200 `Memory`, 404 |  |
| PUT | `/api/v1/memories/{id}` | Authenticated | `Memory` | 200 `Memory`, 404 |  |
| GET | `/api/v1/merge-queue` | Authenticated |  | 200 `EnumerationResult<MergeEntry>` |  |
| POST | `/api/v1/merge-queue` | TenantAdmin | `MergeEntry` | 201 `MergeEntry` |  |
| POST | `/api/v1/merge-queue/enumerate` | Authenticated | `EnumerationQuery` (optional) |  |  |
| POST | `/api/v1/merge-queue/process` | TenantAdmin |  |  |  |
| POST | `/api/v1/merge-queue/purge` | TenantAdmin | `PurgeMergeEntriesRequest` | 200 `MergeQueuePurgeResult` |  |
| DELETE | `/api/v1/merge-queue/{id}` | TenantAdmin |  | 204 |  |
| GET | `/api/v1/merge-queue/{id}` | Authenticated |  | 200 `MergeEntry`, 404 |  |
| POST | `/api/v1/merge-queue/{id}/process` | TenantAdmin |  | 200 `MergeEntry`, 404 |  |
| DELETE | `/api/v1/merge-queue/{id}/purge` | TenantAdmin |  | 200 `Object`, 404, 409 `Object` |  |
| GET | `/api/v1/missions` | Authenticated |  | 200 `EnumerationResult<Mission>` |  |
| POST | `/api/v1/missions` | TenantAdmin | `Mission` | 201 `Mission` |  |
| POST | `/api/v1/missions/delete/multiple` | TenantAdmin | `DeleteMultipleRequest` | 200 `DeleteMultipleResult` |  |
| POST | `/api/v1/missions/enumerate` | Authenticated | `EnumerationQuery` (optional) |  |  |
| GET | `/api/v1/missions/history` | Authenticated |  | 200 `MissionHistorySummaryResult` |  |
| GET | `/api/v1/missions/summaries` | Authenticated |  | 200 `EnumerationResult<MissionSummary>` |  |
| POST | `/api/v1/missions/summaries/enumerate` | Authenticated | `EnumerationQuery` (optional) | 200 `EnumerationResult<MissionSummary>` |  |
| DELETE | `/api/v1/missions/{id}` | TenantAdmin |  | 200 `Mission`, 404 |  |
| GET | `/api/v1/missions/{id}` | Authenticated |  | 200 `Mission`, 404 |  |
| PUT | `/api/v1/missions/{id}` | TenantAdmin | `Mission` | 200 `Mission`, 404 |  |
| GET | `/api/v1/missions/{id}/diff` | Authenticated |  | 404 |  |
| GET | `/api/v1/missions/{id}/evaluate-autoland` | Authenticated |  | 200 `AutoLandDecision`, 400, 404 |  |
| GET | `/api/v1/missions/{id}/github/pull-request` | Authenticated |  | 200 `GitHubPullRequestDetail`, 404 |  |
| GET | `/api/v1/missions/{id}/instructions` | Authenticated |  | 404 |  |
| GET | `/api/v1/missions/{id}/landing-preview` | Authenticated |  | 200 `LandingPreviewResult`, 400, 404 |  |
| GET | `/api/v1/missions/{id}/log` | Authenticated |  | 404 |  |
| DELETE | `/api/v1/missions/{id}/purge` | TenantAdmin |  | 200 `Object`, 404 |  |
| POST | `/api/v1/missions/{id}/restart` | TenantAdmin | `MissionRestartRequest` (optional) | 200 `Mission`, 400, 404 |  |
| POST | `/api/v1/missions/{id}/retry-landing` | TenantAdmin |  | 200 `Object`, 400, 404 |  |
| POST | `/api/v1/missions/{id}/review/approve` | TenantAdmin | `MissionReviewDecisionRequest` (optional) | 200 `Mission`, 400, 404 |  |
| POST | `/api/v1/missions/{id}/review/deny` | TenantAdmin | `MissionReviewDecisionRequest` (optional) | 200 `Mission`, 400, 404 |  |
| PUT | `/api/v1/missions/{id}/status` | TenantAdmin | `StatusTransitionRequest` | 200 `Mission`, 400, 404 |  |
| GET | `/api/v1/model-endpoints` | Authenticated |  | 200 `List<ModelEndpoint>` |  |
| POST | `/api/v1/model-endpoints` | Authenticated | `ModelEndpoint` | 201 `ModelEndpoint`, 400 |  |
| POST | `/api/v1/model-endpoints/health-check` | Authenticated |  | 200 `ModelEndpointHealthSweepResponse` |  |
| DELETE | `/api/v1/model-endpoints/{id}` | Authenticated |  | 204, 404 |  |
| GET | `/api/v1/model-endpoints/{id}` | Authenticated |  | 200 `ModelEndpoint`, 404 |  |
| PUT | `/api/v1/model-endpoints/{id}` | Authenticated | `ModelEndpoint` | 200 `ModelEndpoint`, 404 |  |
| POST | `/api/v1/model-endpoints/{id}/validate` | Authenticated |  | 200 `ModelEndpointProbeResult`, 404 |  |
| DELETE | `/api/v1/objective-refinement-sessions/{id}` | TenantAdmin |  | 204 |  |
| GET | `/api/v1/objective-refinement-sessions/{id}` | Authenticated |  | 200 `ObjectiveRefinementSessionDetail` |  |
| POST | `/api/v1/objective-refinement-sessions/{id}/apply` | TenantAdmin | `ObjectiveRefinementApplyRequest` (optional) | 200 `ObjectiveRefinementApplyResponse` |  |
| POST | `/api/v1/objective-refinement-sessions/{id}/messages` | TenantAdmin | `ObjectiveRefinementMessageRequest` | 200 `ObjectiveRefinementSessionDetail` |  |
| POST | `/api/v1/objective-refinement-sessions/{id}/stop` | TenantAdmin |  | 200 `ObjectiveRefinementSessionDetail` |  |
| POST | `/api/v1/objective-refinement-sessions/{id}/summarize` | TenantAdmin | `ObjectiveRefinementSummaryRequest` (optional) | 200 `ObjectiveRefinementSummaryResponse` |  |
| GET | `/api/v1/objectives` | Authenticated |  | 200 `EnumerationResult<Objective>` |  |
| POST | `/api/v1/objectives` | TenantAdmin | `ObjectiveUpsertRequest` | 201 `Objective` |  |
| POST | `/api/v1/objectives/enumerate` | Authenticated | `ObjectiveQuery` (optional) | 200 `EnumerationResult<Objective>` |  |
| POST | `/api/v1/objectives/import/github` | TenantAdmin | `GitHubObjectiveImportRequest` | 200 `Objective`, 201 `Objective` |  |
| POST | `/api/v1/objectives/reorder` | TenantAdmin | `ObjectiveReorderRequest` | 200 `List<Objective>` |  |
| DELETE | `/api/v1/objectives/{id}` | TenantAdmin |  | 204, 404 |  |
| GET | `/api/v1/objectives/{id}` | Authenticated |  | 200 `Objective`, 404 |  |
| PUT | `/api/v1/objectives/{id}` | TenantAdmin | `ObjectiveUpsertRequest` | 200 `Objective`, 404 |  |
| GET | `/api/v1/objectives/{id}/refinement-sessions` | Authenticated |  | 200 `List<ObjectiveRefinementSession>` |  |
| POST | `/api/v1/objectives/{id}/refinement-sessions` | TenantAdmin | `ObjectiveRefinementSessionCreateRequest` | 201 `ObjectiveRefinementSessionDetail` |  |
| POST | `/api/v1/onboarding` | NoAuthRequired |  |  |  |
| GET | `/api/v1/personas` | Authenticated |  | 200 `EnumerationResult<Persona>` |  |
| POST | `/api/v1/personas` | Authenticated | `Persona` | 201 `Persona` |  |
| POST | `/api/v1/personas/enumerate` | Authenticated | `EnumerationQuery` (optional) |  |  |
| DELETE | `/api/v1/personas/{name}` | Authenticated |  | 204, 400, 404 |  |
| GET | `/api/v1/personas/{name}` | Authenticated |  | 200 `Persona`, 404 |  |
| PUT | `/api/v1/personas/{name}` | Authenticated | `Persona` | 200 `Persona`, 404 |  |
| GET | `/api/v1/pipelines` | Authenticated |  | 200 `EnumerationResult<Pipeline>` |  |
| POST | `/api/v1/pipelines` | Authenticated | `Pipeline` | 201 `Pipeline` |  |
| POST | `/api/v1/pipelines/enumerate` | Authenticated | `EnumerationQuery` (optional) |  |  |
| DELETE | `/api/v1/pipelines/{name}` | Authenticated |  | 204, 400, 404 |  |
| GET | `/api/v1/pipelines/{name}` | Authenticated |  | 200 `Pipeline`, 404 |  |
| PUT | `/api/v1/pipelines/{name}` | Authenticated | `Pipeline` | 200 `Pipeline`, 404 |  |
| GET | `/api/v1/planning-sessions` | Authenticated |  | 200 `List<PlanningSession>` |  |
| POST | `/api/v1/planning-sessions` | TenantAdmin | `PlanningSessionCreateRequest` |  |  |
| DELETE | `/api/v1/planning-sessions/{id}` | TenantAdmin |  |  |  |
| GET | `/api/v1/planning-sessions/{id}` | Authenticated |  |  |  |
| POST | `/api/v1/planning-sessions/{id}/dispatch` | TenantAdmin | `PlanningSessionDispatchRequest` (optional) |  |  |
| POST | `/api/v1/planning-sessions/{id}/messages` | TenantAdmin | `PlanningSessionMessageRequest` |  |  |
| POST | `/api/v1/planning-sessions/{id}/stop` | TenantAdmin |  |  |  |
| POST | `/api/v1/planning-sessions/{id}/stop-turn` | TenantAdmin |  |  |  |
| POST | `/api/v1/planning-sessions/{id}/summarize` | TenantAdmin | `PlanningSessionSummaryRequest` (optional) |  |  |
| GET | `/api/v1/playbooks` | Authenticated |  | 200 `EnumerationResult<Playbook>` |  |
| POST | `/api/v1/playbooks` | TenantAdmin | `Playbook` | 201 `Playbook` |  |
| POST | `/api/v1/playbooks/enumerate` | Authenticated | `EnumerationQuery` (optional) |  |  |
| DELETE | `/api/v1/playbooks/{id}` | TenantAdmin |  | 200 `Object`, 404 |  |
| GET | `/api/v1/playbooks/{id}` | Authenticated |  | 200 `Playbook`, 404 |  |
| PUT | `/api/v1/playbooks/{id}` | TenantAdmin | `Playbook` | 200 `Playbook`, 404 |  |
| GET | `/api/v1/project-profiles` | Authenticated |  | 200 `EnumerationResult<ProjectProfile>` |  |
| POST | `/api/v1/project-profiles` | Authenticated | `ProjectProfile` | 201 `ProjectProfile` |  |
| POST | `/api/v1/project-profiles/enumerate` | Authenticated | `ProjectProfileQuery` (optional) | 200 `EnumerationResult<ProjectProfile>` |  |
| GET | `/api/v1/project-profiles/resolve/vessels/{vesselId}` | Authenticated |  | 200 `ProjectProfileResolutionResult`, 404 |  |
| POST | `/api/v1/project-profiles/validate` | Authenticated | `ProjectProfile` | 200 `ProjectProfileValidationResult` |  |
| DELETE | `/api/v1/project-profiles/{id}` | Authenticated |  | 204, 404 |  |
| GET | `/api/v1/project-profiles/{id}` | Authenticated |  | 200 `ProjectProfile`, 404 |  |
| PUT | `/api/v1/project-profiles/{id}` | Authenticated | `ProjectProfile` | 200 `ProjectProfile`, 404 |  |
| GET | `/api/v1/project-profiles/{id}/persona-preview/{persona}` | Authenticated |  | 200 `PersonaPromptPreview`, 404 |  |
| GET | `/api/v1/prompt-templates` | Authenticated |  | 200 `EnumerationResult<PromptTemplate>` |  |
| POST | `/api/v1/prompt-templates` | Authenticated | `PromptTemplate` | 201 `PromptTemplate` |  |
| POST | `/api/v1/prompt-templates/enumerate` | Authenticated | `EnumerationQuery` (optional) |  |  |
| GET | `/api/v1/prompt-templates/{name}` | Authenticated |  | 200 `PromptTemplate`, 404 |  |
| PUT | `/api/v1/prompt-templates/{name}` | Authenticated | `PromptTemplate` | 200 `PromptTemplate`, 404 |  |
| POST | `/api/v1/prompt-templates/{name}/reset` | Authenticated |  | 200 `PromptTemplate`, 404 |  |
| GET | `/api/v1/releases` | Authenticated |  | 200 `EnumerationResult<Release>` |  |
| POST | `/api/v1/releases` | TenantAdmin | `ReleaseUpsertRequest` | 201 `Release` |  |
| POST | `/api/v1/releases/enumerate` | Authenticated | `ReleaseQuery` (optional) | 200 `EnumerationResult<Release>` |  |
| DELETE | `/api/v1/releases/{id}` | TenantAdmin |  | 204, 404 |  |
| GET | `/api/v1/releases/{id}` | Authenticated |  | 200 `Release`, 404 |  |
| PUT | `/api/v1/releases/{id}` | TenantAdmin | `ReleaseUpsertRequest` | 200 `Release`, 404 |  |
| GET | `/api/v1/releases/{id}/github/pull-requests` | Authenticated |  | 200 `List<GitHubPullRequestDetail>`, 404 |  |
| POST | `/api/v1/releases/{id}/refresh` | TenantAdmin |  | 200 `Release`, 404 |  |
| GET | `/api/v1/request-history` | Authenticated |  | 200 `EnumerationResult<RequestHistoryEntry>` |  |
| POST | `/api/v1/request-history/delete/by-filter` | TenantAdmin | `RequestHistoryQuery` (optional) | 200 `DeleteMultipleResult` |  |
| POST | `/api/v1/request-history/delete/multiple` | TenantAdmin | `DeleteMultipleRequest` | 200 `DeleteMultipleResult` |  |
| GET | `/api/v1/request-history/summary` | Authenticated |  | 200 `RequestHistorySummaryResult` |  |
| DELETE | `/api/v1/request-history/{id}` | TenantAdmin |  | 204, 404 |  |
| GET | `/api/v1/request-history/{id}` | Authenticated |  | 200 `RequestHistoryRecord`, 404 |  |
| POST | `/api/v1/restore` | AdminOnly |  |  |  |
| GET | `/api/v1/runbook-executions` | Authenticated |  | 200 `EnumerationResult<RunbookExecution>` |  |
| POST | `/api/v1/runbook-executions/enumerate` | Authenticated | `RunbookExecutionQuery` (optional) | 200 `EnumerationResult<RunbookExecution>` |  |
| DELETE | `/api/v1/runbook-executions/{id}` | TenantAdmin |  | 204, 404 |  |
| GET | `/api/v1/runbook-executions/{id}` | Authenticated |  | 200 `RunbookExecution`, 404 |  |
| PUT | `/api/v1/runbook-executions/{id}` | TenantAdmin | `RunbookExecutionUpdateRequest` | 200 `RunbookExecution`, 404 |  |
| GET | `/api/v1/runbooks` | Authenticated |  | 200 `EnumerationResult<Runbook>` |  |
| POST | `/api/v1/runbooks` | TenantAdmin | `RunbookUpsertRequest` | 201 `Runbook` |  |
| POST | `/api/v1/runbooks/enumerate` | Authenticated | `RunbookQuery` (optional) | 200 `EnumerationResult<Runbook>` |  |
| DELETE | `/api/v1/runbooks/{id}` | TenantAdmin |  | 204, 404 |  |
| GET | `/api/v1/runbooks/{id}` | Authenticated |  | 200 `Runbook`, 404 |  |
| PUT | `/api/v1/runbooks/{id}` | TenantAdmin | `RunbookUpsertRequest` | 200 `Runbook`, 404 |  |
| POST | `/api/v1/runbooks/{id}/executions` | TenantAdmin | `RunbookExecutionStartRequest` (optional) | 201 `RunbookExecution`, 404 |  |
| GET | `/api/v1/runtimes/mux/endpoints` | Authenticated |  | 200 `MuxEndpointListResult` |  |
| GET | `/api/v1/runtimes/mux/endpoints/{name}` | Authenticated |  | 200 `MuxEndpointShowResult`, 404 |  |
| POST | `/api/v1/server/rebuild` | AdminOnly |  |  | experimental |
| GET | `/api/v1/server/rebuild/status` | AdminOnly |  |  | experimental |
| POST | `/api/v1/server/reset` | AdminOnly |  |  |  |
| POST | `/api/v1/server/restart` | AdminOnly |  |  |  |
| POST | `/api/v1/server/rollback` | AdminOnly |  |  | experimental |
| POST | `/api/v1/server/stop` | AdminOnly |  |  |  |
| GET | `/api/v1/settings` | AdminOnly |  |  |  |
| PUT | `/api/v1/settings` | AdminOnly |  |  |  |
| GET | `/api/v1/signals` | Authenticated |  | 200 `EnumerationResult<Signal>` |  |
| POST | `/api/v1/signals` | TenantAdmin | `Signal` | 201 `Signal` |  |
| POST | `/api/v1/signals/delete/multiple` | TenantAdmin | `DeleteMultipleRequest` | 200 `DeleteMultipleResult` |  |
| POST | `/api/v1/signals/enumerate` | Authenticated | `EnumerationQuery` (optional) |  |  |
| GET | `/api/v1/signals/recent` | Authenticated |  | 200 `List<Signal>` |  |
| GET | `/api/v1/signals/recipient/{captainId}` | Authenticated |  | 200 `List<Signal>` |  |
| DELETE | `/api/v1/signals/{id}` | TenantAdmin |  | 204, 404 |  |
| GET | `/api/v1/signals/{id}` | Authenticated |  | 200 `Signal`, 404 |  |
| PUT | `/api/v1/signals/{id}/read` | TenantAdmin |  | 200 `Signal`, 404 |  |
| GET | `/api/v1/skills` | Authenticated |  | 200 `EnumerationResult<Skill>` |  |
| POST | `/api/v1/skills` | Authenticated | `Skill` | 201 `Skill` |  |
| POST | `/api/v1/skills/enumerate` | Authenticated | `SkillQuery` (optional) | 200 `EnumerationResult<Skill>` |  |
| DELETE | `/api/v1/skills/{id}` | Authenticated |  | 204, 404 |  |
| GET | `/api/v1/skills/{id}` | Authenticated |  | 200 `Skill`, 404 |  |
| PUT | `/api/v1/skills/{id}` | Authenticated | `Skill` | 200 `Skill`, 404 |  |
| GET | `/api/v1/status` | Authenticated |  | 200 `ArmadaStatus` |  |
| GET | `/api/v1/status/health` | NoAuthRequired |  |  |  |
| GET | `/api/v1/tenants` | AdminOnly |  |  |  |
| POST | `/api/v1/tenants` | AdminOnly |  |  |  |
| POST | `/api/v1/tenants/lookup` | NoAuthRequired |  |  |  |
| DELETE | `/api/v1/tenants/{id}` | AdminOnly |  |  |  |
| GET | `/api/v1/tenants/{id}` | Authenticated |  |  |  |
| PUT | `/api/v1/tenants/{id}` | AdminOnly |  |  |  |
| GET | `/api/v1/token-usage` | Authenticated |  | 200 `EnumerationResult<TokenUsageRecord>` |  |
| POST | `/api/v1/token-usage/delete/by-filter` | Authenticated | `TokenUsageQuery` (optional) | 200 `DeleteMultipleResult` |  |
| GET | `/api/v1/token-usage/summary` | Authenticated |  | 200 `TokenUsageSummaryResult` |  |
| GET | `/api/v1/users` | Authenticated |  |  |  |
| POST | `/api/v1/users` | TenantAdmin |  |  |  |
| DELETE | `/api/v1/users/{id}` | Authenticated |  |  |  |
| GET | `/api/v1/users/{id}` | Authenticated |  |  |  |
| PUT | `/api/v1/users/{id}` | Authenticated |  |  |  |
| POST | `/api/v1/vessel-health/enumerate` | Authenticated | `VesselHealthEnumerateRequest` (optional) | 200 `EnumerationResult<VesselHealth>` |  |
| POST | `/api/v1/vessel-health/evaluate` | TenantAdmin | `VesselHealthEvaluateRequest` (optional) | 202 `VesselHealthEvaluationStart`, 404, 409 `VesselHealthEvaluationStart` |  |
| GET | `/api/v1/vessel-health/summary` | Authenticated |  | 200 `VesselHealthSummary` |  |
| GET | `/api/v1/vessels` | Authenticated |  | 200 `EnumerationResult<Vessel>` |  |
| POST | `/api/v1/vessels` | TenantAdmin | `Vessel` | 201 `Vessel` |  |
| POST | `/api/v1/vessels/delete/multiple` | TenantAdmin | `DeleteMultipleRequest` | 200 `DeleteMultipleResult` |  |
| POST | `/api/v1/vessels/enumerate` | Authenticated | `EnumerationQuery` (optional) |  |  |
| POST | `/api/v1/vessels/import` | TenantAdmin | `VesselImportRequest` | 200 `VesselImportResponse`, 202 `VesselImportResponse`, 400, 403 `ApiErrorResponse`, 404, 409 `ApiErrorResponse` |  |
| POST | `/api/v1/vessels/import/batches/enumerate` | Authenticated | `EnumerationQuery` (optional) | 200 `EnumerationResult<VesselImportBatch>` |  |
| GET | `/api/v1/vessels/import/batches/{id}` | Authenticated |  | 200 `VesselImportBatchDetail`, 404 |  |
| POST | `/api/v1/vessels/import/batches/{id}/categorize` | TenantAdmin | `VesselImportCategorizationRequest` (optional) | 202 `VesselImportBatch`, 400, 403 `ApiErrorResponse`, 404, 409 `ApiErrorResponse` |  |
| POST | `/api/v1/vessels/import/batches/{id}/fleet-recommendations/apply` | TenantAdmin | `FleetRecommendationApplyRequest` | 200 `FleetRecommendationApplyResult`, 400, 403 `ApiErrorResponse`, 404, 409 `ApiErrorResponse` |  |
| GET | `/api/v1/vessels/import/browse` | TenantAdmin |  | 200 `VesselBrowseResult`, 400, 403 `ApiErrorResponse`, 404 |  |
| GET | `/api/v1/vessels/import/categorization/default-prompt` | TenantAdmin |  | 200 `FleetCategorizationDefaultPrompt`, 403 `ApiErrorResponse` |  |
| POST | `/api/v1/vessels/import/discover` | TenantAdmin | `VesselDiscoveryRequest` | 200 `VesselImportDiscoverResponse`, 202 `VesselImportDiscoverResponse`, 400, 403 `ApiErrorResponse` |  |
| DELETE | `/api/v1/vessels/{id}` | TenantAdmin |  | 204 |  |
| GET | `/api/v1/vessels/{id}` | Authenticated |  | 200 `Vessel`, 404 |  |
| PUT | `/api/v1/vessels/{id}` | TenantAdmin | `Vessel` | 200 `Vessel`, 404 |  |
| GET | `/api/v1/vessels/{id}/branches` | Authenticated |  |  |  |
| POST | `/api/v1/vessels/{id}/branches/merge` | TenantAdmin |  |  |  |
| POST | `/api/v1/vessels/{id}/branches/push` | TenantAdmin |  |  |  |
| POST | `/api/v1/vessels/{id}/build-context` | TenantAdmin | `VesselBuildContextRequest` | 200 `Vessel`, 404 |  |
| PATCH | `/api/v1/vessels/{id}/context` | TenantAdmin | `Vessel` | 200 `Vessel`, 404 |  |
| GET | `/api/v1/vessels/{id}/git-status` | Authenticated |  |  |  |
| GET | `/api/v1/vessels/{id}/health` | Authenticated |  | 200 `VesselHealthDetail`, 404 |  |
| DELETE | `/api/v1/vessels/{id}/health/overrides/{criterion}` | TenantAdmin |  | 200 `VesselHealthDetail`, 404 |  |
| PUT | `/api/v1/vessels/{id}/health/overrides/{criterion}` | TenantAdmin | `VesselHealthOverrideRequest` | 200 `VesselHealthDetail`, 404 |  |
| GET | `/api/v1/vessels/{id}/landing-preview` | Authenticated |  | 200 `LandingPreviewResult`, 404 |  |
| GET | `/api/v1/vessels/{id}/readiness` | Authenticated |  | 200 `VesselReadinessResult`, 404 |  |
| GET | `/api/v1/voyages` | Authenticated |  | 200 `EnumerationResult<Voyage>` |  |
| POST | `/api/v1/voyages` | TenantAdmin | `VoyageRequest` | 201 `Voyage` |  |
| POST | `/api/v1/voyages/delete/multiple` | TenantAdmin | `DeleteMultipleRequest` | 200 `DeleteMultipleResult` |  |
| POST | `/api/v1/voyages/enumerate` | Authenticated | `EnumerationQuery` (optional) |  |  |
| DELETE | `/api/v1/voyages/{id}` | TenantAdmin |  | 404 |  |
| GET | `/api/v1/voyages/{id}` | Authenticated |  | 404 |  |
| DELETE | `/api/v1/voyages/{id}/purge` | TenantAdmin |  | 200 `Object`, 404, 409 `Object` |  |
| GET | `/api/v1/whoami` | Authenticated |  |  |  |
| GET | `/api/v1/workflow-profiles` | Authenticated |  | 200 `EnumerationResult<WorkflowProfile>` |  |
| POST | `/api/v1/workflow-profiles` | TenantAdmin | `WorkflowProfile` | 201 `WorkflowProfile` |  |
| POST | `/api/v1/workflow-profiles/enumerate` | Authenticated | `WorkflowProfileQuery` (optional) | 200 `EnumerationResult<WorkflowProfile>` |  |
| GET | `/api/v1/workflow-profiles/preview/vessels/{vesselId}` | Authenticated |  | 200 `WorkflowProfileResolutionPreviewResult`, 404 |  |
| GET | `/api/v1/workflow-profiles/resolve/vessels/{vesselId}` | Authenticated |  | 200 `WorkflowProfile`, 404 |  |
| POST | `/api/v1/workflow-profiles/validate` | TenantAdmin | `WorkflowProfile` | 200 `WorkflowProfileValidationResult` |  |
| DELETE | `/api/v1/workflow-profiles/{id}` | TenantAdmin |  | 204, 404 |  |
| GET | `/api/v1/workflow-profiles/{id}` | Authenticated |  | 200 `WorkflowProfile`, 404 |  |
| PUT | `/api/v1/workflow-profiles/{id}` | TenantAdmin | `WorkflowProfile` | 200 `WorkflowProfile`, 404 |  |
| GET | `/api/v1/workspace/vessels/{vesselId}/changes` | Authenticated |  | 200 `WorkspaceChangesResult` |  |
| GET | `/api/v1/workspace/vessels/{vesselId}/diff` | Authenticated |  | 200 `WorkspaceDiffResult` |  |
| POST | `/api/v1/workspace/vessels/{vesselId}/directory` | Authenticated | `WorkspaceCreateDirectoryRequest` | 201 `WorkspaceOperationResult` |  |
| DELETE | `/api/v1/workspace/vessels/{vesselId}/entry` | Authenticated |  | 200 `WorkspaceOperationResult` |  |
| POST | `/api/v1/workspace/vessels/{vesselId}/exec` | TenantAdmin | `WorkspaceExecRequest` | 200 `WorkspaceExecResult` |  |
| GET | `/api/v1/workspace/vessels/{vesselId}/file` | Authenticated |  | 200 `WorkspaceFileResponse` |  |
| PUT | `/api/v1/workspace/vessels/{vesselId}/file` | Authenticated | `WorkspaceSaveRequest` | 200 `WorkspaceSaveResult` |  |
| POST | `/api/v1/workspace/vessels/{vesselId}/rename` | Authenticated | `WorkspaceRenameRequest` | 200 `WorkspaceOperationResult` |  |
| GET | `/api/v1/workspace/vessels/{vesselId}/search` | Authenticated |  | 200 `WorkspaceSearchResult` |  |
| GET | `/api/v1/workspace/vessels/{vesselId}/status` | Authenticated |  | 200 `WorkspaceStatusResult` |  |
| GET | `/api/v1/workspace/vessels/{vesselId}/tree` | Authenticated |  | 200 `WorkspaceTreeResult` |  |
| GET | `/openapi.json` | NoAuthRequired |  |  |  |
| GET | `/swagger` | NoAuthRequired |  |  |  |

## MCP tools

Arguments are camelCase; `*` marks a required argument. See [MCP_API.md](MCP_API.md).

| Tool | Auth | Arguments | Status |
|---|---|---|---|
| `add_vessel` | TenantAdmin | `allowConcurrentMissions: boolean`, `autoApprove: boolean`, `defaultBranch: string`, `defaultPipelineId: string`, `enableModelContext: boolean`, `fleetId*: string`, `gitHubTokenOverride: string`, `name*: string`, `projectContext: string`, `repoUrl*: string`, `styleGuide: string`, `workingDirectory: string` |  |
| `apply_backlog_refinement_summary` | TenantAdmin | `markMessageSelected: boolean`, `messageId: string`, `promoteBacklogState: boolean`, `sessionId*: string` |  |
| `apply_fleet_recommendations` | TenantAdmin | `batchId*: string`, `fleets: array<object>` |  |
| `approve_deployment` | TenantAdmin | `comment: string`, `deploymentId*: string` |  |
| `backup` | AdminOnly | `outputPath: string` |  |
| `cancel_fleet_action_run` | TenantAdmin | `runId*: string` |  |
| `cancel_merge` | TenantAdmin | `entryId*: string` |  |
| `cancel_mission` | TenantAdmin | `missionId*: string` |  |
| `cancel_voyage` | TenantAdmin | `voyageId*: string` |  |
| `categorize_vessel_import` | TenantAdmin | `applyFleetsAutomatically: boolean`, `batchId*: string`, `captainId: string`, `prompt: string` |  |
| `create_backlog_item` | TenantAdmin | `acceptanceCriteria: array<string>`, `backlogState: string`, `blockedByObjectiveIds: array<string>`, `category: string`, `checkRunIds: array<string>`, `deploymentIds: array<string>`, `description: string`, `dueUtc: string`, `effort: string`, `evidenceLinks: array<string>`, `fleetIds: array<string>`, `incidentIds: array<string>`, `kind: string`, `missionIds: array<string>`, `nonGoals: array<string>`, `owner: string`, `parentObjectiveId: string`, `planningSessionIds: array<string>`, `priority: string`, `rank: integer`, `refinementSessionIds: array<string>`, `refinementSummary: string`, `releaseIds: array<string>`, `rolloutConstraints: array<string>`, `status: string`, `suggestedPipelineId: string`, `tags: array<string>`, `targetVersion: string`, `title*: string`, `vesselIds: array<string>`, `voyageIds: array<string>` |  |
| `create_backlog_planning_session` | TenantAdmin | `captainId*: string`, `fleetId: string`, `objectiveId*: string`, `pipelineId: string`, `selectedPlaybooks: array<object>`, `title: string`, `vesselId*: string` |  |
| `create_backlog_refinement_session` | TenantAdmin | `captainId*: string`, `fleetId: string`, `initialMessage: string`, `objectiveId*: string`, `title: string`, `vesselId: string` |  |
| `create_captain` | TenantAdmin | `allowedPersonas: string`, `autoApprove: boolean`, `model: string`, `muxAdapterType: string`, `muxApprovalPolicy: string`, `muxBaseUrl: string`, `muxConfigDirectory: string`, `muxEndpoint: string`, `muxMaxTokens: integer`, `muxSystemPromptPath: string`, `muxTemperature: number`, `name*: string`, `preferredPersona: string`, `reasoningEffort: string`, `runtime: string`, `systemInstructions: string`, `tier: string` |  |
| `create_deployment` | TenantAdmin | `autoExecute: boolean`, `environmentId: string`, `environmentName: string`, `missionId: string`, `notes: string`, `releaseId: string`, `sourceRef: string`, `summary: string`, `title: string`, `vesselId: string`, `voyageId: string`, `workflowProfileId: string` |  |
| `create_fleet` | TenantAdmin | `description: string`, `name*: string` |  |
| `create_fleet_action` | TenantAdmin | `commandText: string`, `defaultConcurrency: integer`, `description: string`, `kind: string`, `name*: string`, `persona: string`, `pipelineId: string`, `promptTemplate: string`, `requiresCleanWorkingTree: boolean`, `timeoutSeconds: integer` |  |
| `create_harbor` | Authenticated | `enabled: boolean`, `maxConcurrentJobs: integer`, `name*: string` | experimental |
| `create_memory` | Authenticated | `content*: string`, `key: string`, `salience: number`, `scope: string`, `sourceDetail: string`, `sourceKind: string`, `sourceMissionId: string`, `sourceVesselId: string`, `sourceVoyageId: string`, `summary: string`, `tags: array<string>`, `topic: string`, `type: string`, `vesselId: string` |  |
| `create_mission` | TenantAdmin | `description*: string`, `mode: string`, `persona: string`, `selectedPlaybooks: array<object>`, `title*: string`, `vesselId*: string`, `voyageId: string` |  |
| `create_model_endpoint` | Authenticated | `apiKey: string`, `baseUrl*: string`, `dimensionality: integer`, `enabled: boolean`, `kind: string`, `model: string`, `name*: string`, `provider: string`, `timeoutMs: integer` |  |
| `create_objective` | TenantAdmin | `acceptanceCriteria: array<string>`, `backlogState: string`, `blockedByObjectiveIds: array<string>`, `category: string`, `checkRunIds: array<string>`, `deploymentIds: array<string>`, `description: string`, `dueUtc: string`, `effort: string`, `evidenceLinks: array<string>`, `fleetIds: array<string>`, `incidentIds: array<string>`, `kind: string`, `missionIds: array<string>`, `nonGoals: array<string>`, `owner: string`, `parentObjectiveId: string`, `planningSessionIds: array<string>`, `priority: string`, `rank: integer`, `refinementSessionIds: array<string>`, `refinementSummary: string`, `releaseIds: array<string>`, `rolloutConstraints: array<string>`, `status: string`, `suggestedPipelineId: string`, `tags: array<string>`, `targetVersion: string`, `title*: string`, `vesselIds: array<string>`, `voyageIds: array<string>` |  |
| `create_persona` | TenantAdmin | `defaultCaptainId: string`, `description: string`, `name*: string`, `promptTemplateName*: string` |  |
| `create_pipeline` | TenantAdmin | `description: string`, `name*: string`, `stages*: array<object>` |  |
| `create_playbook` | TenantAdmin | `active: boolean`, `content*: string`, `description: string`, `fileName*: string` |  |
| `create_prompt_template` | TenantAdmin | `active: boolean`, `category*: string`, `content*: string`, `description: string`, `name*: string` |  |
| `create_release` | TenantAdmin | `checkRunIds: array<string>`, `missionIds: array<string>`, `notes: string`, `status: string`, `summary: string`, `tagName: string`, `title: string`, `version: string`, `vesselId: string`, `voyageIds: array<string>`, `workflowProfileId: string` |  |
| `delete_backlog_item` | TenantAdmin | `objectiveId*: string` |  |
| `delete_captain` | TenantAdmin | `captainId*: string` |  |
| `delete_captains` | TenantAdmin | `ids*: array<string>` |  |
| `delete_dock` | TenantAdmin | `dockId*: string` |  |
| `delete_docks` | TenantAdmin | `ids*: array<string>` |  |
| `delete_event` | TenantAdmin | `eventId*: string` |  |
| `delete_events` | TenantAdmin | `ids*: array<string>` |  |
| `delete_fleet` | TenantAdmin | `fleetId*: string` |  |
| `delete_fleet_action` | TenantAdmin | `actionId*: string` |  |
| `delete_fleets` | TenantAdmin | `ids*: array<string>` |  |
| `delete_harbor` | Authenticated | `harborId*: string` | experimental |
| `delete_memory` | Authenticated | `memoryId*: string` |  |
| `delete_merge` | TenantAdmin | `entryId*: string` |  |
| `delete_missions` | TenantAdmin | `ids*: array<string>` |  |
| `delete_model_endpoint` | Authenticated | `endpointId*: string` |  |
| `delete_objective` | TenantAdmin | `objectiveId*: string` |  |
| `delete_persona` | TenantAdmin | `name*: string` |  |
| `delete_pipeline` | TenantAdmin | `name*: string` |  |
| `delete_playbook` | TenantAdmin | `id*: string` |  |
| `delete_signals` | TenantAdmin | `ids*: array<string>` |  |
| `delete_vessel` | TenantAdmin | `vesselId*: string` |  |
| `delete_vessels` | TenantAdmin | `ids*: array<string>` |  |
| `delete_voyages` | TenantAdmin | `ids*: array<string>` |  |
| `discover_vessels` | TenantAdmin | `directories: array<string>`, `maxDepth: integer`, `roots: array<string>`, `runInBackground: boolean` |  |
| `dispatch` | TenantAdmin | `captainAssignments: array<object>`, `description: string`, `missions: array<object>`, `objectiveId: string`, `pipeline: string`, `pipelineId: string`, `selectedPlaybooks: array<object>`, `title*: string`, `vesselId: string` |  |
| `dispatch_backlog_planning_session` | TenantAdmin | `description: string`, `messageId: string`, `sessionId*: string`, `title: string` |  |
| `enqueue_merge` | TenantAdmin | `branchName*: string`, `missionId: string`, `priority: integer`, `targetBranch: string`, `testCommand: string`, `vesselId*: string` |  |
| `enumerate` | Authenticated | `captainId: string`, `createdAfter: string`, `createdBefore: string`, `entityType*: string`, `eventType: string`, `fleetId: string`, `includeContext: boolean`, `includeDescription: boolean`, `includeInactive: boolean`, `includeMessage: boolean`, `includeOutput: boolean`, `includePayload: boolean`, `includeTestOutput: boolean`, `missionId: string`, `order: string`, `pageNumber: integer`, `pageSize: integer`, `runId: string`, `search: string`, `signalType: string`, `status: string`, `toCaptainId: string`, `unreadOnly: boolean`, `vesselId: string`, `voyageId: string` |  |
| `evaluate_autoland` | Authenticated | `missionId*: string` |  |
| `evaluate_vessel_health` | TenantAdmin | `fleetId: string`, `force: boolean`, `vesselIds: array<string>` |  |
| `fleet_action_run_status` | Authenticated | `runId*: string` |  |
| `get_backlog_item` | Authenticated | `objectiveId*: string` |  |
| `get_backlog_planning_session` | Authenticated | `sessionId*: string` |  |
| `get_backlog_refinement_session` | Authenticated | `sessionId*: string` |  |
| `get_captain` | Authenticated | `captainId*: string` |  |
| `get_captain_log` | Authenticated | `captainId*: string`, `lines: integer`, `offset: integer` |  |
| `get_captain_tools` | Authenticated | `captainId*: string` |  |
| `get_check_run` | Authenticated | `checkRunId*: string` |  |
| `get_deployment` | Authenticated | `deploymentId*: string` |  |
| `get_dock` | Authenticated | `dockId*: string` |  |
| `get_fleet` | Authenticated | `fleetId*: string` |  |
| `get_harbor` | Authenticated | `harborId*: string` | experimental |
| `get_memory` | Authenticated | `memoryId*: string` |  |
| `get_merge_entry` | Authenticated | `entryId*: string` |  |
| `get_mission_diff` | Authenticated | `missionId*: string` |  |
| `get_mission_log` | Authenticated | `formatted: boolean`, `lines: integer`, `missionId*: string`, `offset: integer` |  |
| `get_model_endpoint` | Authenticated | `endpointId*: string` |  |
| `get_objective` | Authenticated | `objectiveId*: string` |  |
| `get_persona` | Authenticated | `name*: string` |  |
| `get_pipeline` | Authenticated | `name*: string` |  |
| `get_playbook` | Authenticated | `id*: string` |  |
| `get_prompt_template` | Authenticated | `name*: string` |  |
| `get_release` | Authenticated | `releaseId*: string` |  |
| `get_runbook` | Authenticated | `runbookId*: string` |  |
| `get_runbook_execution` | Authenticated | `runbookExecutionId*: string` |  |
| `get_vessel` | Authenticated | `vesselId*: string` |  |
| `health_check_model_endpoints` | Authenticated |  |  |
| `import_vessels` | TenantAdmin | `allNew: boolean`, `applyFleetsAutomatically: boolean`, `batchId*: string`, `captainId: string`, `categorize: boolean`, `defaultPipelineId: string`, `fleetId: string`, `landingMode: string`, `paths: array<string>`, `prompt: string` |  |
| `inbox` | Authenticated |  |  |
| `list_backlog` | Authenticated | `backlogState: string`, `category: string`, `effort: string`, `fleetId: string`, `kind: string`, `owner: string`, `pageNumber: integer`, `pageSize: integer`, `parentObjectiveId: string`, `priority: string`, `search: string`, `status: string`, `targetVersion: string`, `vesselId: string` |  |
| `list_backlog_refinement_sessions` | Authenticated | `objectiveId*: string`, `pageNumber: integer`, `pageSize: integer` |  |
| `list_objectives` | Authenticated | `backlogState: string`, `category: string`, `effort: string`, `fleetId: string`, `kind: string`, `owner: string`, `pageNumber: integer`, `pageSize: integer`, `parentObjectiveId: string`, `priority: string`, `search: string`, `status: string`, `targetVersion: string`, `vesselId: string` |  |
| `list_prompt_templates` | Authenticated | `category: string`, `pageNumber: integer`, `pageSize: integer` |  |
| `mission_status` | Authenticated | `missionId*: string` |  |
| `papercut_summary` | Authenticated | `category: string`, `limit: integer`, `minSeverity: string`, `scanLimit: integer`, `sinceHours: integer`, `ungrouped: boolean`, `vesselId: string` |  |
| `process_merge_entry` | TenantAdmin | `entryId*: string` |  |
| `process_merge_queue` | TenantAdmin |  |  |
| `purge_dock` | TenantAdmin | `dockId*: string` |  |
| `purge_merge_entries` | TenantAdmin | `entryIds*: array<string>` |  |
| `purge_merge_entry` | TenantAdmin | `entryId*: string` |  |
| `purge_merge_queue` | TenantAdmin | `status: string`, `vesselId: string` |  |
| `purge_mission` | TenantAdmin | `missionId*: string` |  |
| `purge_voyage` | TenantAdmin | `voyageId*: string` |  |
| `release_captain` | TenantAdmin | `captainId*: string` |  |
| `reorder_backlog_items` | TenantAdmin | `items*: array<object>` |  |
| `reorder_objectives` | TenantAdmin | `items*: array<object>` |  |
| `repair_dock` | TenantAdmin | `dockId*: string` |  |
| `reset_prompt_template` | TenantAdmin | `name*: string` |  |
| `restart_mission` | TenantAdmin | `description: string`, `missionId*: string`, `title: string` |  |
| `restore` | AdminOnly | `filePath*: string` |  |
| `retry_check_run` | TenantAdmin | `checkRunId*: string` |  |
| `retry_landing` | TenantAdmin | `missionId*: string` |  |
| `rollback_deployment` | TenantAdmin | `deploymentId*: string` |  |
| `run_check` | TenantAdmin | `branchName: string`, `commandOverride: string`, `commitHash: string`, `environmentName: string`, `label: string`, `missionId: string`, `type*: string`, `vesselId*: string`, `voyageId: string`, `workflowProfileId: string` |  |
| `run_fleet_action` | TenantAdmin | `actionId: string`, `commandText: string`, `concurrency: integer`, `kind: string`, `name: string`, `pipelineId: string`, `promptTemplate: string`, `requiresCleanWorkingTree: boolean`, `timeoutSeconds: integer`, `vesselIds*: array<string>` |  |
| `search_memory` | Authenticated | `pageNumber: integer`, `pageSize: integer`, `search: string`, `topic: string`, `type: string`, `vesselId: string` |  |
| `send_backlog_refinement_message` | TenantAdmin | `content*: string`, `sessionId*: string` |  |
| `send_signal` | TenantAdmin | `captainId*: string`, `message*: string` |  |
| `set_harbor_enabled` | Authenticated | `enabled*: boolean`, `harborId*: string` | experimental |
| `set_vessel_health_override` | TenantAdmin | `criterion*: string`, `note: string`, `remove: boolean`, `status: string`, `vesselId*: string` |  |
| `start_runbook_execution` | TenantAdmin | `checkType: string`, `deploymentId: string`, `environmentId: string`, `environmentName: string`, `incidentId: string`, `notes: string`, `parameterValues: object`, `runbookId*: string`, `title: string`, `workflowProfileId: string` |  |
| `status` | Authenticated |  |  |
| `stop_all` | TenantAdmin |  |  |
| `stop_backlog_refinement_session` | TenantAdmin | `sessionId*: string` |  |
| `stop_captain` | TenantAdmin | `captainId*: string` |  |
| `stop_server` | AdminOnly |  |  |
| `summarize_backlog_refinement_session` | TenantAdmin | `messageId: string`, `sessionId*: string` |  |
| `token_usage_summary` | Authenticated | `bucketMinutes: number`, `captainId: string`, `fromUtc: string`, `model: string`, `runtime: string`, `sinceHours: integer`, `source: string`, `toUtc: string`, `vesselId: string` |  |
| `transition_mission_status` | TenantAdmin | `missionId*: string`, `status*: string` |  |
| `unstick_dock` | TenantAdmin | `dockId*: string` |  |
| `update_backlog_item` | TenantAdmin | `acceptanceCriteria: array<string>`, `backlogState: string`, `blockedByObjectiveIds: array<string>`, `category: string`, `checkRunIds: array<string>`, `deploymentIds: array<string>`, `description: string`, `dueUtc: string`, `effort: string`, `evidenceLinks: array<string>`, `fleetIds: array<string>`, `incidentIds: array<string>`, `kind: string`, `missionIds: array<string>`, `nonGoals: array<string>`, `objectiveId*: string`, `owner: string`, `parentObjectiveId: string`, `planningSessionIds: array<string>`, `priority: string`, `rank: integer`, `refinementSessionIds: array<string>`, `refinementSummary: string`, `releaseIds: array<string>`, `rolloutConstraints: array<string>`, `status: string`, `suggestedPipelineId: string`, `tags: array<string>`, `targetVersion: string`, `title: string`, `vesselIds: array<string>`, `voyageIds: array<string>` |  |
| `update_captain` | TenantAdmin | `allowedPersonas: string`, `autoApprove: boolean`, `captainId*: string`, `model: string`, `muxAdapterType: string`, `muxApprovalPolicy: string`, `muxBaseUrl: string`, `muxConfigDirectory: string`, `muxEndpoint: string`, `muxMaxTokens: integer`, `muxSystemPromptPath: string`, `muxTemperature: number`, `name: string`, `preferredPersona: string`, `reasoningEffort: string`, `runtime: string`, `systemInstructions: string`, `tier: string` |  |
| `update_fleet` | TenantAdmin | `defaultPipelineId: string`, `description: string`, `fleetId*: string`, `name: string` |  |
| `update_fleet_action` | TenantAdmin | `actionId*: string`, `commandText: string`, `defaultConcurrency: integer`, `description: string`, `kind: string`, `name: string`, `persona: string`, `pipelineId: string`, `promptTemplate: string`, `requiresCleanWorkingTree: boolean`, `timeoutSeconds: integer` |  |
| `update_harbor` | Authenticated | `enabled: boolean`, `harborId*: string`, `maxConcurrentJobs: integer`, `name: string` | experimental |
| `update_memory` | Authenticated | `content: string`, `key: string`, `memoryId*: string`, `salience: number`, `scope: string`, `sourceDetail: string`, `summary: string`, `tags: array<string>`, `topic: string`, `type: string`, `vesselId: string` |  |
| `update_mission` | TenantAdmin | `branchName: string`, `description: string`, `missionId*: string`, `mode: string`, `parentMissionId: string`, `persona: string`, `prUrl: string`, `priority: integer`, `title: string`, `vesselId: string`, `voyageId: string` |  |
| `update_model_endpoint` | Authenticated | `apiKey: string`, `baseUrl: string`, `dimensionality: integer`, `enabled: boolean`, `endpointId*: string`, `kind: string`, `model: string`, `name: string`, `provider: string`, `timeoutMs: integer` |  |
| `update_objective` | TenantAdmin | `acceptanceCriteria: array<string>`, `backlogState: string`, `blockedByObjectiveIds: array<string>`, `category: string`, `checkRunIds: array<string>`, `deploymentIds: array<string>`, `description: string`, `dueUtc: string`, `effort: string`, `evidenceLinks: array<string>`, `fleetIds: array<string>`, `incidentIds: array<string>`, `kind: string`, `missionIds: array<string>`, `nonGoals: array<string>`, `objectiveId*: string`, `owner: string`, `parentObjectiveId: string`, `planningSessionIds: array<string>`, `priority: string`, `rank: integer`, `refinementSessionIds: array<string>`, `refinementSummary: string`, `releaseIds: array<string>`, `rolloutConstraints: array<string>`, `status: string`, `suggestedPipelineId: string`, `tags: array<string>`, `targetVersion: string`, `title: string`, `vesselIds: array<string>`, `voyageIds: array<string>` |  |
| `update_persona` | TenantAdmin | `defaultCaptainId: string`, `description: string`, `name*: string`, `promptTemplateName: string` |  |
| `update_pipeline` | TenantAdmin | `description: string`, `name*: string`, `stages: array<object>` |  |
| `update_playbook` | TenantAdmin | `active: boolean`, `content: string`, `description: string`, `fileName: string`, `id*: string` |  |
| `update_prompt_template` | TenantAdmin | `content*: string`, `description: string`, `name*: string` |  |
| `update_vessel` | TenantAdmin | `allowConcurrentMissions: boolean`, `autoApprove: boolean`, `clearAutoApprove: boolean`, `defaultBranch: string`, `defaultPipelineId: string`, `enableModelContext: boolean`, `gitHubTokenOverride: string`, `modelContext: string`, `name: string`, `projectContext: string`, `repoUrl: string`, `styleGuide: string`, `vesselId*: string`, `workingDirectory: string` |  |
| `update_vessel_context` | TenantAdmin | `modelContext: string`, `projectContext: string`, `styleGuide: string`, `vesselId*: string` |  |
| `validate_model_endpoint` | Authenticated | `endpointId*: string` |  |
| `verify_deployment` | TenantAdmin | `deploymentId*: string` |  |
| `vessel_health` | Authenticated | `includeDependencies: boolean`, `vesselId*: string` |  |
| `voyage_status` | Authenticated | `includeDescription: boolean`, `includeDiffs: boolean`, `includeLogs: boolean`, `includeMissions: boolean`, `summary: boolean`, `voyageId*: string` |  |

## WebSocket

Server-to-client messages are camelCase JSON; client field names are matched case-insensitively. See
[WEBSOCKET_API.md](WEBSOCKET_API.md).

### Endpoints

| Name | Path | Status |
|---|---|---|
| dashboard | `/ws` |  |
| harbor-link | `Harbor.LinkPath (default /v1.0/harbor/connect)` | experimental |

### Messages

- Client routes: `command`, `subscribe`
- Client message fields: `action`, `allTenants`, `captainId`, `data`, `entityType`, `filePath`, `id`, `lines`, `offset`, `outputPath`, `query`, `route`, `status`
- Event envelope fields: `type`, `message`, `data`, `timestamp`
- Command reply fields (`command.result`, `command.error`): `type`, `action`, `data`, `error`

### Commands

`backup`, `cancel_merge`, `cancel_mission`, `cancel_voyage`, `create_captain`, `create_fleet`, `create_mission`, `create_persona`, `create_pipeline`, `create_vessel`, `create_voyage`, `delete_captain`, `delete_fleet`, `delete_persona`, `delete_pipeline`, `delete_vessel`, `enqueue_merge`, `enumerate`, `get_captain`, `get_captain_log`, `get_fleet`, `get_merge_entry`, `get_mission`, `get_mission_diff`, `get_mission_log`, `get_persona`, `get_pipeline`, `get_prompt_template`, `get_vessel`, `get_voyage`, `list_captains`, `list_docks`, `list_events`, `list_fleets`, `list_merge_queue`, `list_missions`, `list_missions_summary`, `list_signals`, `list_vessels`, `list_voyages`, `process_merge_queue`, `purge_mission`, `purge_voyage`, `restart_mission`, `restore`, `send_signal`, `status`, `stop_all`, `stop_captain`, `stop_server`, `transition_mission_status`, `update_captain`, `update_fleet`, `update_mission`, `update_persona`, `update_pipeline`, `update_prompt_template`, `update_vessel`, `update_vessel_context`

### Events

Generic events carry a `message` and the payload `{ entityType, entityId, captainId, missionId, vesselId, voyageId }`.

| Type | Scope | Payload |
|---|---|---|
| `approval-needed` | tenant | `entityType`, `entityId`, `missionId`, `title`, `status`, `vesselId`, `voyageId`, `reviewRequestedUtc` |
| `ask.chunk` | user | `threadId`, `turnId`, `delta` |
| `ask.message` | user | `threadId`, `message` |
| `ask.proposal` | user | `threadId`, `proposal` |
| `ask.thinking` | user | `threadId`, `turnId`, `delta` |
| `ask.thread` | user | `threadId`, `thread` |
| `ask.tool` | user | `threadId`, `turnId`, `phase`, `id`, `name`, `arguments`, `ok`, `elapsedMs`, `result` |
| `ask.turn` | user | `threadId`, `turnId`, `state`, `messageId`, `error` |
| `ask.work` | user | `threadId`, `trackedWorkId`, `snapshot`, `trackedWork` |
| `captain.batch_deleted` | tenant | generic |
| `captain.changed` | tenant | `id`, `name`, `state` |
| `captain.launched` | tenant | generic |
| `check-run.changed` | tenant | `CheckRun` |
| `deployment.changed` | tenant | `Deployment` |
| `deployment.progress` | tenant | `id`, `title`, `status`, `verificationStatus`, `environmentId`, `environmentName`, `startedUtc`, `completedUtc`, `lastUpdateUtc` |
| `dock.batch_deleted` | tenant | generic |
| `dock.deleted` | tenant | generic |
| `dock.purged` | tenant | generic |
| `dock.repaired` | tenant | generic |
| `dock.unstuck` | tenant | generic |
| `environment.health` | tenant | `environmentId`, `environmentName`, `id`, `title`, `status`, `verificationStatus`, `lastMonitoredUtc`, `lastRegressionAlertUtc`, `latestMonitoringSummary`, `monitoringFailureCount` |
| `event.batch_deleted` | tenant | generic |
| `event.deleted` | tenant | generic |
| `fleet.batch_deleted` | tenant | generic |
| `incident.changed` | tenant | `Incident` |
| `merge.batch_purged` | tenant | generic |
| `merge.purged` | tenant | generic |
| `mission.batch_deleted` | tenant | generic |
| `mission.changed` | tenant | `id`, `title`, `status`, `voyageId` |
| `mission.completed` | tenant | generic |
| `mission.deleted` | tenant | generic |
| `mission.landing_failed` | tenant | generic |
| `mission.manual_complete_no_dock` | tenant | generic |
| `mission.pull_request_open` | tenant | generic |
| `mission.restarted` | tenant | generic |
| `mission.review_approved` | tenant | generic |
| `mission.review_denied` | tenant | generic |
| `mission.status_changed` | tenant | generic |
| `mission.work_produced` | tenant | generic |
| `objective-refinement-session.applied` | tenant | `sessionId`, `objectiveId`, `summary` |
| `objective-refinement-session.changed` | tenant | `session` |
| `objective-refinement-session.created` | tenant | generic |
| `objective-refinement-session.deleted` | tenant | `sessionId`, `objectiveId` |
| `objective-refinement-session.message.created` | tenant | `sessionId`, `objectiveId`, `message` |
| `objective-refinement-session.message.updated` | tenant | `sessionId`, `objectiveId`, `message` |
| `objective-refinement-session.stopped` | tenant | generic |
| `objective-refinement-session.summary.created` | tenant | `sessionId`, `messageId`, `summary` |
| `objective.changed` | tenant | `Objective` |
| `planning-session.changed` | tenant | `session` |
| `planning-session.created` | tenant | generic |
| `planning-session.deleted` | tenant | `sessionId` |
| `planning-session.dispatch.created` | tenant | `sessionId`, `voyageId`, `messageId` |
| `planning-session.message.created` | tenant | `sessionId`, `message` |
| `planning-session.message.updated` | tenant | `sessionId`, `message` |
| `planning-session.stopped` | tenant | generic |
| `planning-session.summary.created` | tenant | `sessionId`, `messageId`, `draft` |
| `planning-session.thinking` | tenant | `sessionId`, `messageId`, `delta` |
| `planning-session.tool` | tenant | `sessionId`, `messageId`, `phase`, `id`, `name`, `arguments`, `ok`, `elapsedMs`, `result` |
| `runbook-execution.changed` | tenant | `RunbookExecution` |
| `signal.batch_deleted` | tenant | generic |
| `status.snapshot` | connection | `ArmadaStatus` + `allTenants` |
| `vessel.batch_deleted` | tenant | generic |
| `voyage.batch_deleted` | tenant | generic |
| `voyage.changed` | tenant | `id`, `title`, `status` |
| `voyage.deleted` | tenant | generic |

## CLI

Commands of the `armada` CLI (Helm). `*` marks a required argument or option. Global options (`--help`,
`--version`) are omitted.

| Command | Arguments | Options |
|---|---|---|
| `armada action cancel` | `<run>*` | `--json`, `--page`, `--page-size`, `--verbose` |
| `armada action list` |  | `--include-inactive`, `--json`, `--page`, `--page-size`, `--runs`, `--verbose` |
| `armada action run` | `<action>*` | `--concurrency\|-c`, `--fleet\|-f`, `--json`, `--page`, `--page-size`, `--verbose`, `--vessel\|-v` |
| `armada action status` | `<run>*` | `--json`, `--page`, `--page-size`, `--verbose` |
| `armada backlog create` |  | `--acceptance`, `--backlog-state`, `--blocked-by`, `--category`, `--constraint`, `--description\|-d`, `--due-utc`, `--effort`, `--evidence`, `--fleet`, `--json`, `--kind`, `--non-goal`, `--owner`, `--page`, `--page-size`, `--parent`, `--pipeline`, `--priority`, `--rank`, `--status`, `--summary`, `--tag`, `--target-version`, `--title\|-t`, `--verbose`, `--vessel` |
| `armada backlog delete` | `<backlog>*` | `--json`, `--page`, `--page-size`, `--verbose` |
| `armada backlog list` |  | `--backlog-state`, `--effort`, `--fleet`, `--json`, `--kind`, `--owner`, `--page`, `--page-size`, `--priority`, `--search\|-s`, `--status`, `--target-version`, `--verbose`, `--vessel` |
| `armada backlog reorder` | `<backlog>*` | `--json`, `--page`, `--page-size`, `--rank\|-r`, `--verbose` |
| `armada backlog show` | `<backlog>*` | `--json`, `--page`, `--page-size`, `--verbose` |
| `armada backlog update` | `<backlog>*` | `--acceptance`, `--backlog-state`, `--blocked-by`, `--category`, `--constraint`, `--description\|-d`, `--due-utc`, `--effort`, `--evidence`, `--fleet`, `--json`, `--kind`, `--non-goal`, `--owner`, `--page`, `--page-size`, `--parent`, `--pipeline`, `--priority`, `--rank`, `--status`, `--summary`, `--tag`, `--target-version`, `--title\|-t`, `--verbose`, `--vessel` |
| `armada captain add` | `<name>*` | `--json`, `--model\|-m`, `--mux-adapter-type`, `--mux-approval-policy`, `--mux-base-url`, `--mux-config-dir`, `--mux-endpoint`, `--mux-max-tokens`, `--mux-system-prompt-path`, `--mux-temperature`, `--page`, `--page-size`, `--runtime\|-r`, `--verbose` |
| `armada captain list` |  | `--json`, `--page`, `--page-size`, `--verbose` |
| `armada captain remove` | `<captain>*` | `--json`, `--page`, `--page-size`, `--verbose` |
| `armada captain stop` | `<captain>*` | `--json`, `--page`, `--page-size`, `--verbose` |
| `armada captain stop-all` |  | `--json`, `--page`, `--page-size`, `--verbose` |
| `armada captain update` | `<captain>*` | `--json`, `--model\|-m`, `--mux-adapter-type`, `--mux-approval-policy`, `--mux-base-url`, `--mux-config-dir`, `--mux-endpoint`, `--mux-max-tokens`, `--mux-system-prompt-path`, `--mux-temperature`, `--name\|-n`, `--page`, `--page-size`, `--runtime\|-r`, `--verbose` |
| `armada config init` |  | `--json`, `--page`, `--page-size`, `--verbose` |
| `armada config set` | `<key>*` `<value>*` | `--json`, `--page`, `--page-size`, `--verbose` |
| `armada config show` |  | `--json`, `--page`, `--page-size`, `--verbose` |
| `armada diff` | `<mission>` | `--json`, `--page`, `--page-size`, `--verbose` |
| `armada doctor` |  | `--json`, `--page`, `--page-size`, `--verbose` |
| `armada fleet add` | `<name>*` | `--description\|-d`, `--json`, `--page`, `--page-size`, `--verbose` |
| `armada fleet list` |  | `--json`, `--page`, `--page-size`, `--verbose` |
| `armada fleet remove` | `<fleet>*` | `--json`, `--page`, `--page-size`, `--verbose` |
| `armada go` | `<prompt>*` | `--json`, `--log\|-l`, `--merge`, `--no-merge`, `--no-pr`, `--no-push`, `--page`, `--page-size`, `--pr`, `--push`, `--repo\|-r`, `--task\|-t`, `--verbose`, `--vessel\|-v` |
| `armada health` |  | `--evaluate\|-e`, `--fleet\|-f`, `--json`, `--page`, `--page-size`, `--status\|-s`, `--verbose` |
| `armada inbox` |  | `--critical`, `--json`, `--page`, `--page-size`, `--verbose` |
| `armada log` | `<identifier>*` | `--follow\|-f`, `--json`, `--lines\|-n`, `--page`, `--page-size`, `--verbose` |
| `armada mcp install` |  | `--dry-run`, `--json`, `--page`, `--page-size`, `--verbose`, `--yes` |
| `armada mcp remove` |  | `--dry-run`, `--json`, `--page`, `--page-size`, `--verbose`, `--yes` |
| `armada mcp stdio` |  |  |
| `armada mission cancel` | `<mission>*` | `--json`, `--page`, `--page-size`, `--verbose` |
| `armada mission create` | `<title>*` | `--description\|-d`, `--json`, `--page`, `--page-size`, `--priority\|-p`, `--verbose`, `--vessel\|-v`, `--voyage` |
| `armada mission list` |  | `--captain\|-c`, `--json`, `--page`, `--page-size`, `--status\|-s`, `--verbose`, `--vessel\|-v`, `--voyage` |
| `armada mission restart` | `<mission>*` | `--description\|-d`, `--json`, `--page`, `--page-size`, `--title\|-t`, `--verbose` |
| `armada mission retry` | `<mission>*` | `--json`, `--page`, `--page-size`, `--verbose` |
| `armada mission show` | `<mission>*` | `--json`, `--page`, `--page-size`, `--verbose` |
| `armada playbook add` | `<file-name>*` | `--content\|-c`, `--description\|-d`, `--from-file\|-f`, `--inactive`, `--json`, `--page`, `--page-size`, `--verbose` |
| `armada playbook list` |  | `--json`, `--page`, `--page-size`, `--verbose` |
| `armada playbook remove` | `<playbook>*` | `--json`, `--page`, `--page-size`, `--verbose` |
| `armada playbook show` | `<playbook>*` | `--json`, `--page`, `--page-size`, `--verbose` |
| `armada reset` |  | `--force\|-f`, `--json`, `--page`, `--page-size`, `--verbose` |
| `armada server restart` |  | `--json`, `--page`, `--page-size`, `--verbose` |
| `armada server start` |  | `--json`, `--page`, `--page-size`, `--verbose` |
| `armada server status` |  | `--json`, `--page`, `--page-size`, `--verbose` |
| `armada server stop` |  | `--json`, `--page`, `--page-size`, `--verbose` |
| `armada status` |  | `--all\|-a`, `--json`, `--page`, `--page-size`, `--verbose` |
| `armada tui` |  | `--profile`, `--route`, `--server` |
| `armada vessel add` | `<name>*` `<repoUrl>*` | `--branch\|-b`, `--fleet\|-f`, `--json`, `--page`, `--page-size`, `--verbose` |
| `armada vessel import` | `<paths>` | `--apply`, `--captain`, `--categorize`, `--depth`, `--dry-run`, `--fleet\|-f`, `--json`, `--page`, `--page-size`, `--prompt-file`, `--root\|-r`, `--verbose`, `--yes\|-y` |
| `armada vessel list` |  | `--fleet\|-f`, `--json`, `--page`, `--page-size`, `--verbose` |
| `armada vessel remove` | `<vessel>*` | `--json`, `--page`, `--page-size`, `--verbose` |
| `armada voyage cancel` | `<voyage>*` | `--json`, `--page`, `--page-size`, `--verbose` |
| `armada voyage create` | `<title>*` | `--json`, `--mission\|-m`, `--page`, `--page-size`, `--playbook\|-p`, `--verbose`, `--vessel\|-v` |
| `armada voyage list` |  | `--json`, `--page`, `--page-size`, `--status\|-s`, `--verbose` |
| `armada voyage retry` | `<voyage>*` | `--json`, `--page`, `--page-size`, `--verbose` |
| `armada voyage show` | `<voyage>*` | `--json`, `--page`, `--page-size`, `--verbose` |
| `armada watch` |  | `--captain\|-c`, `--interval\|-i`, `--json`, `--page`, `--page-size`, `--verbose` |

## Settings

Keys of `settings.json` (camelCase; `[]` marks the fields of list elements). Defaults are shown for a fresh
install with the home directory written as `~`; defaults are not frozen (see COMPATIBILITY.md).

| Key | Type | Default | Status |
|---|---|---|---|
| `admiralPort` | int | `7890` |  |
| `agents` | array<object> | `[{"runtime":"ClaudeCode","command":"claude","args":"--dangerously-skip-permis...` |  |
| `agents[].args` | string | `""` |  |
| `agents[].command` | string | `"claude"` |  |
| `agents[].environment` | map<string,string> | `{}` |  |
| `agents[].maxConcurrent` | int | `0` |  |
| `agents[].runtime` | enum AgentRuntimeEnum (ClaudeCode\|Codex\|Gemini\|Cursor\|Mux\|OpenCode\|ApiEndpoint\|Custom) | `"ClaudeCode"` |  |
| `agents[].supportsResume` | bool | `false` |  |
| `allowDefaultCredentialsOnNetwork` | bool | `false` |  |
| `allowSelfRegistration` | bool | `false` |  |
| `apiKey` | string | `null` |  |
| `ask` | object |  |  |
| `ask.captainAutoApprove` | bool | `false` |  |
| `ask.historyTurns` | int | `20` |  |
| `ask.narrateMilestones` | bool | `true` |  |
| `ask.narrationTimeoutSeconds` | int | `60` |  |
| `ask.proposalExpiryMinutes` | int | `60` |  |
| `ask.trackerIntervalSeconds` | int | `5` |  |
| `ask.turnTimeoutMinutes` | int | `15` |  |
| `autoCreatePullRequests` | bool | `false` |  |
| `autoMergePullRequests` | bool | `false` |  |
| `autoPush` | bool | `true` |  |
| `branchCleanupPolicy` | enum BranchCleanupPolicyEnum (LocalOnly\|LocalAndRemote\|None) | `"LocalOnly"` |  |
| `captainCrashLoopThreshold` | int | `3` |  |
| `captainCrashLoopWindowMinutes` | int | `15` |  |
| `captainQuarantineMinutes` | int | `15` |  |
| `dashboardPath` | string | `null` |  |
| `dataDirectory` | string | `"~/.armada"` |  |
| `dataRetentionDays` | int | `30` |  |
| `database` | object |  |  |
| `database.confirmedBackupForSchemaVersion` | int | `0` |  |
| `database.connectionIdleTimeoutSeconds` | int | `60` |  |
| `database.connectionLifetimeSeconds` | int | `300` |  |
| `database.databaseName` | string | `"armada"` |  |
| `database.filename` | string | `"armada.db"` |  |
| `database.hostname` | string | `"localhost"` |  |
| `database.logQueries` | bool | `false` |  |
| `database.maxPoolSize` | int | `25` |  |
| `database.migrationBackupRetentionCount` | int | `5` |  |
| `database.minPoolSize` | int | `1` |  |
| `database.password` | string | `""` |  |
| `database.port` | int | `0` |  |
| `database.requireBackupConfirmationForMigrations` | bool | `false` |  |
| `database.requireEncryption` | bool | `false` |  |
| `database.schema` | string | `"public"` |  |
| `database.type` | enum DatabaseTypeEnum (Sqlite\|Postgresql\|SqlServer\|Mysql) | `"Sqlite"` |  |
| `database.username` | string | `""` |  |
| `databasePath` | string | `"~/.armada/armada.db"` |  |
| `defaultRuntime` | string | `null` |  |
| `deploymentMode` | enum DeploymentModeEnum (Local\|Split) | `"Local"` | experimental |
| `docksDirectory` | string | `"~/.armada/docks"` |  |
| `escalationRules` | array<object> | `[]` |  |
| `escalationRules[].action` | enum EscalationActionEnum (Log\|Webhook) | `"Log"` |  |
| `escalationRules[].cooldownMinutes` | int | `30` |  |
| `escalationRules[].enabled` | bool | `true` |  |
| `escalationRules[].thresholdMinutes` | int | `15` |  |
| `escalationRules[].trigger` | enum EscalationTriggerEnum (CaptainStalled\|MissionOverdue\|MissionFailed\|RecoveryExhausted\|PoolExhausted) | `"CaptainStalled"` |  |
| `escalationRules[].webhookUrl` | string | `null` |  |
| `fleetActions` | object |  |  |
| `fleetActions.defaultTimeoutSeconds` | int | `300` |  |
| `fleetActions.maxConcurrency` | int | `8` |  |
| `fleetActions.maxOutputBytes` | int | `65536` |  |
| `fleetActions.runRetentionDays` | int | `30` |  |
| `gitHubToken` | string | `null` |  |
| `harbor` | object |  | experimental |
| `harbor.advertisedMcpBaseUrl` | string | `null` | experimental |
| `harbor.defaultMaxJobsPerHarbor` | int | `4` | experimental |
| `harbor.heartbeatIntervalSeconds` | int | `15` | experimental |
| `harbor.heartbeatTimeoutSeconds` | int | `45` | experimental |
| `harbor.linkPath` | string | `"/v1.0/harbor/connect"` | experimental |
| `harbor.requireAuth` | bool | `false` | experimental |
| `heartbeatIntervalSeconds` | int | `10` |  |
| `idleCaptainTimeoutSeconds` | int | `0` |  |
| `import` | object |  |  |
| `import.allowedRoots` | array<string> | `[]` |  |
| `import.categorizationTimeoutMinutes` | int | `20` |  |
| `import.excludedDirectoryNames` | array<string> | `["bin","obj","node_modules","dist",".git",".vs","packages","TestResults",".ar...` |  |
| `import.inlineBatchLimit` | int | `25` |  |
| `import.maxDepth` | int | `6` |  |
| `isolateCaptainLaunch` | bool | `false` |  |
| `landingMode` | enum LandingModeEnum (LocalMerge\|PullRequest\|MergeQueue\|None)? | `null` |  |
| `logDirectory` | string | `"~/.armada/logs"` |  |
| `loginRateLimit` | object |  |  |
| `loginRateLimit.enabled` | bool | `true` |  |
| `loginRateLimit.lockoutMinutes` | int | `15` |  |
| `loginRateLimit.maxFailuresPerAccount` | int | `10` |  |
| `loginRateLimit.maxFailuresPerAddress` | int | `50` |  |
| `loginRateLimit.maxLockoutMinutes` | int | `1440` |  |
| `loginRateLimit.windowMinutes` | int | `15` |  |
| `maxCaptains` | int | `0` |  |
| `maxConcurrentMissions` | int | `0` |  |
| `maxLandingRetries` | int | `3` |  |
| `maxLogFileCount` | int | `5` |  |
| `maxLogFileSizeBytes` | long | `10485760` |  |
| `maxMissionRecoveryAttempts` | int | `2` |  |
| `maxMissionRuntimeMinutes` | int | `240` |  |
| `maxNoOpRedispatchAttempts` | int | `1` |  |
| `maxRecoveryAttempts` | int | `3` |  |
| `mcp` | object |  |  |
| `mcp.allowUnauthenticatedLoopback` | bool | `true` |  |
| `mcp.toolCallsPerSecond` | int | `100` |  |
| `mcpPort` | int | `7891` |  |
| `mergeQueueTestCommand` | string | `null` |  |
| `messageTemplates` | object |  |  |
| `messageTemplates.commitMessageTemplate` | string | `"\nArmada-Mission-Id: {MissionId}\nArmada-Voyage-Id: {VoyageId}\nArmada-Capta...` |  |
| `messageTemplates.enableCommitMetadata` | bool | `true` |  |
| `messageTemplates.enablePrMetadata` | bool | `true` |  |
| `messageTemplates.mergeCommitTemplate` | string | `"Merge armada mission: {BranchName}\n\nArmada-Mission-Id: {MissionId}\nArmada...` |  |
| `messageTemplates.prDescriptionTemplate` | string | `"\n\n---\nCommitted by [Armada](https://github.com/jchristn/armada)\n- Missio...` |  |
| `minAvailableMemoryBytesForLaunch` | long | `0` |  |
| `minIdleCaptains` | int | `0` |  |
| `notifications` | bool | `true` |  |
| `planningSessionAbandonmentTimeoutMinutes` | int | `240` |  |
| `planningSessionInactivityTimeoutMinutes` | int | `60` |  |
| `planningSessionRetentionDays` | int | `0` |  |
| `rebuildSlotRetentionCount` | int | `3` | experimental |
| `rebuildSupervisorHarborId` | string | `null` | experimental |
| `remoteControl` | object |  |  |
| `remoteControl.allowInvalidCertificates` | bool | `false` |  |
| `remoteControl.connectTimeoutSeconds` | int | `15` |  |
| `remoteControl.enabled` | bool | `false` |  |
| `remoteControl.enrollmentToken` | string | `null` |  |
| `remoteControl.heartbeatIntervalSeconds` | int | `30` |  |
| `remoteControl.instanceId` | string | `null` |  |
| `remoteControl.password` | string | `"armadaadmin"` |  |
| `remoteControl.reconnectBaseDelaySeconds` | int | `5` |  |
| `remoteControl.reconnectMaxDelaySeconds` | int | `60` |  |
| `remoteControl.tunnelUrl` | string | `"http://proxy.armadago.ai:7893/tunnel"` |  |
| `reposDirectory` | string | `"~/.armada/repos"` |  |
| `repositoryHealth` | object |  |  |
| `repositoryHealth.dependencyCommandTimeoutSeconds` | int | `120` |  |
| `repositoryHealth.dependencyMaxAgeHours` | int | `24` |  |
| `repositoryHealth.fetchBeforeEvaluate` | bool | `true` |  |
| `repositoryHealth.intervalMinutes` | int | `360` |  |
| `repositoryHealth.maxConcurrency` | int | `4` |  |
| `repositoryHealth.missionWindowDays` | int | `7` |  |
| `repositoryHealth.scoredCriteria` | array<enum VesselHealthCriterionEnum (GitDivergence\|WorkingTree\|Branches\|CommitRecency\|Dependencies\|Vulnerabilities\|TestInfrastructure\|ContinuousIntegration\|ArmadaReadiness\|MissionOutcomes\|Overall)> | `["GitDivergence","WorkingTree","Branches","Dependencies","Vulnerabilities","T...` |  |
| `repositoryHealth.staleBranchDays` | int | `90` |  |
| `repositoryHealth.thresholds` | object |  |  |
| `repositoryHealth.thresholds.behindFail` | int | `21` |  |
| `repositoryHealth.thresholds.behindWarn` | int | `1` |  |
| `repositoryHealth.thresholds.missionFailureFail` | int | `3` |  |
| `repositoryHealth.thresholds.missionFailureWarn` | int | `1` |  |
| `repositoryHealth.thresholds.staleBranchFail` | int | `11` |  |
| `repositoryHealth.thresholds.staleBranchWarn` | int | `4` |  |
| `requestHistoryCaptureRequestHeaders` | bool | `true` |  |
| `requestHistoryCaptureResponseHeaders` | bool | `true` |  |
| `requestHistoryEnabled` | bool | `true` |  |
| `requestHistoryExcludeRoutes` | array<string> | `["/api/v1/status/health","/api/v1/request-history"]` |  |
| `requestHistoryMaxBodyBytes` | int | `32768` |  |
| `requestHistoryRetentionDays` | int | `30` |  |
| `requireAuthForShutdown` | bool | `false` |  |
| `requireHarborForLaunch` | bool | `false` | experimental |
| `rest` | object |  |  |
| `rest.hostname` | string | `"localhost"` |  |
| `rest.ssl` | bool | `false` |  |
| `retention` | object |  |  |
| `retention.askThreadArchiveAfterDays` | int | `90` |  |
| `retention.askThreadDeleteAfterDays` | int | `0` |  |
| `retention.importBatchRetentionDays` | int | `90` |  |
| `retention.jobRetentionDays` | int | `30` |  |
| `reviewTimeoutMinutes` | int | `1440` |  |
| `selfVesselId` | string | `null` | experimental |
| `sessionTokenEncryptionKey` | string | `null` |  |
| `stallThresholdMinutes` | int | `10` |  |
| `syslogServers` | array<SyslogServer> | `[{"hostname":"127.0.0.1","port":514,"ipPort":"127.0.0.1:514"}]` |  |
| `telemetry` | object |  |  |
| `telemetry.enabled` | bool | `false` |  |
| `telemetry.lokiEndpoint` | string | `null` |  |
| `telemetry.otlpEndpoint` | string | `null` |  |
| `telemetry.prometheusEnabled` | bool | `true` |  |
| `telemetry.prometheusPort` | int | `9464` |  |
| `telemetry.serviceName` | string | `"armada"` |  |
| `terminalBell` | bool | `true` |  |
| `webSocketEnabled` | bool | `true` |  |
