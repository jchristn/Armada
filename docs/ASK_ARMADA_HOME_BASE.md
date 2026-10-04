# Ask Armada as the Home Base

> **Type:** implementation plan (work-tracking). Annotate task status and the progress log as you go.
>
> **Status:** Implemented and verified end to end (Phases 0-5); merged to `main`
> **Last updated:** 2026-10-04

Status values: `[ ]` not started, `[~]` in progress, `[x]` done, `[!]` blocked.

## Goal

Ask Armada becomes the place you run Armada from. You keep separate conversations for separate pieces of
work, start work from them, and watch that work move end to end without leaving the chat: a voyage you
start from a thread shows up in that thread as a live card that follows every mission through captain
assignment, pipeline stages, checks, the merge queue, and landing, and the thread gets a short update
message whenever something meaningful happens (started, a mission failed, landed, done).

## Decisions (confirmed by the user, 2026-10-04)

- **Confirmation.** Anything that changes state is shown as an inline confirm card with the exact
  parameters and runs only when the user approves it. Each thread has an **auto-approve** toggle that skips
  the card. Read-only calls never prompt.
- **Kickoff.** Both: natural language through the thread's captain (which calls Armada's MCP tools), and
  built-in **quick actions** (`/dispatch`, `/fleet-action`, `/status`, `/health`, `/import`) with inline
  forms that work even when no captain is connected to MCP.
- **Monitoring depth.** Full lifecycle: live card per voyage / fleet action run / job with every mission's
  status, captain, pipeline stage, check runs, merge queue and landing outcome, failure reasons, plus a
  milestone message in the thread. Milestone messages are written by the thread's captain when it is idle,
  otherwise a deterministic sentence is posted.
- **Visibility.** Threads are private to the user who created them. The work they start stays visible
  tenant-wide on the normal pages.

## What exists today (2026-10-04)

- `src/Armada.Dashboard/src/pages/AskArmada.tsx` is a single, unsaved chat with a chosen captain. History
  lives only in the browser and is re-sent each turn.
- `src/Armada.Server/CaptainChatService.cs` runs the captain's runtime per turn in a throwaway directory,
  streams `ask.chunk`, `ask.thinking`, and `ask.tool` events over the WebSocket hub, and for ApiEndpoint
  captains mints a session token so the runtime reaches Armada's MCP server as the caller.
  `BaseAgentRuntime` can already give a launch its own scoped Armada MCP config (`CaptainLaunchIsolationPlanner`).
- `src/Armada.Core/Services/AskArmadaService.cs` (`POST /api/v1/ask`) is a separate keyword-matching,
  read-only responder. It stays as the fallback for `/status` when no captain is selected.
- `src/Armada.Server/WebSocket/ArmadaWebSocketHub.cs` broadcasts every event (`mission.changed`,
  `voyage.changed`, `captain.changed`, `ask.*`, and others) to **every connected client**, with no tenant or
  user scoping, and `/ws` is registered with no authentication while `WebSocketCommandHandler` accepts
  mutating commands (`stop_server`, `delete_vessel`, `create_voyage`, ...). These are security defects that
  this plan fixes first (Phase 0) because the thread experience is built on the socket.

## Design

### Data model (migration v75, all four providers)

All rows carry `tenant_id`, `user_id` (owner), `created_utc`, `last_update_utc`. IDs use new prefixes in
`Constants.cs`. Columns used for sorting or filtering are typed and indexed.

| Table | Prefix | Columns |
|---|---|---|
| `ask_threads` | `ath_` | `title`, `captain_id` (nullable), `auto_approve` (bool, default false), `summary_text` (nullable), `summary_utc`, `pinned` (bool), `archived` (bool), `last_message_utc`, `message_count`, `unread_count` (updates posted while the user was not viewing) |
| `ask_messages` | `amg_` | `thread_id`, `sequence` (int, per thread, monotonic), `role` (`User`, `Assistant`, `System`), `kind` (`Text`, `ActionProposal`, `ActionResult`, `WorkUpdate`, `Summary`, `Error`), `content_text`, `thinking_text` (nullable), `proposal_id` (nullable), `tracked_work_id` (nullable), `captain_id` (nullable), `duration_ms` (nullable) |
| `ask_message_tool_calls` | `atc_` | `message_id`, `thread_id`, `call_id`, `tool_name`, `arguments_text`, `result_text`, `ok` (nullable bool), `elapsed_ms` |
| `ask_action_proposals` | `aap_` | `thread_id`, `message_id`, `tool_name`, `arguments_text`, `summary_text` (one-line human description), `source` (`Captain`, `QuickAction`), `status` (`Pending`, `Approved`, `Rejected`, `Expired`, `Executed`, `Failed`), `result_text`, `error_text`, `decided_by_user_id`, `decided_utc`, `executed_utc` |
| `ask_tracked_work` | `atw_` | `thread_id`, `entity_type` (`Voyage`, `Mission`, `FleetActionRun`, `Job`, `VesselImportBatch`), `entity_id`, `title`, `status` (latest known status string), `state` (`Active`, `Succeeded`, `Failed`, `Cancelled`), `snapshot_hash` (to detect changes), `last_change_utc`, `completed_utc`; unique `(thread_id, entity_type, entity_id)` |

`arguments_text`, `result_text`, `content_text`, and `thinking_text` are deliberately unmanaged text
(tool payloads and model output); every status, kind, and identifier is a typed column.

Deleting a thread deletes its messages, tool calls, proposals, and tracked-work rows (the work itself is
untouched).

### Turn flow (captain)

1. `POST /api/v1/ask/threads/{id}/messages` with `{ Content, ShowThinking }` persists the user message,
   returns `202 { MessageId, TurnId }`, and runs the turn in the background.
2. `CaptainChatService` gains a thread-aware entry point: the prompt history is built server-side from the
   thread's persisted messages (last N turns, plus the thread summary when older history is trimmed), and
   the captain is launched with a **thread-scoped Armada MCP connection**: a session token minted for the
   caller and bound to the thread (`AskThreadId` on the token or an equivalent server-side mapping), passed
   to ApiEndpoint captains through the existing environment variables and to CLI captains through the
   scoped MCP config the runtime layer already supports.
3. Streaming events go **only to the thread owner's sockets**: `ask.chunk`, `ask.thinking`, `ask.tool`
   (unchanged payloads plus `threadId`). The final assistant message and its tool calls are persisted.
4. The MCP server recognises calls made on a thread-scoped token. A tool on the read-only allowlist runs
   normally. Any other tool, when the thread's `auto_approve` is false, is **not executed**: a proposal row
   and an `ActionProposal` message are created, an `ask.proposal` event is sent, and the tool returns to the
   captain: "Proposed as aap_... and waiting for the user's approval in this conversation. Do not retry;
   tell the user what you proposed." When `auto_approve` is true the tool runs immediately and is recorded
   as an `Executed` proposal so the thread still shows what ran.
5. The read-only allowlist is explicit and lives in one place (e.g. `AskToolPolicy`): `enumerate`,
   `status`, `voyage_status`, `mission_status`, `fleet_action_run_status`, `vessel_health`, `get_*`,
   `list_*`-style readers, and similar. Everything not on the list is treated as state-changing.

### Proposals and execution

- `POST /api/v1/ask/threads/{id}/proposals/{proposalId}/approve` executes the tool **in-process through
  the same MCP tool handler** (same validation, same tenant scoping as if the captain had called it),
  records `result_text`, marks the proposal `Executed` or `Failed`, posts an `ActionResult` message, and
  starts tracking any work the result created. `.../reject` marks it `Rejected`. Pending proposals expire
  after `Ask.ProposalExpiryMinutes` (default 60).
- After an approval, the server runs a short follow-up captain turn ("The user approved aap_...; result:
  ...") so the conversation continues naturally, unless the thread has no captain.
- **Quick actions** call `POST /api/v1/ask/threads/{id}/actions` with `{ ToolName, Arguments }` (the same
  tool names and argument shapes as MCP). They go through the identical proposal path: with auto-approve
  off the action is created already approved by the user who submitted the form (the form is the
  confirmation), so it executes immediately and is recorded with `source = QuickAction`.
- **Work linking** is by tool, in one mapping table: `dispatch` / `create_voyage` -> Voyage id,
  `create_mission` / `retry_mission` / `restart_mission` -> Mission id, `run_fleet_action` -> FleetActionRun
  id, `evaluate_vessel_health` -> Job id, `import_vessels` / `discover_vessels` -> VesselImportBatch (and
  Job) id, `cancel_*` -> refreshes the existing tracked item.

### Live monitoring

- `AskWorkTracker` (Server-owned, started with the server) watches every `Active` tracked item. It reacts
  to the hub's existing change notifications (mission, voyage, captain, check run, merge queue) and also
  sweeps every `Ask.TrackerIntervalSeconds` (default 5) so nothing is missed.
- For each item it builds a **work snapshot** (the card's data) and compares its hash to the stored one.
  On change it pushes `ask.work` with the snapshot to the owner's sockets and updates the row.
- **Milestones** post a `WorkUpdate` message to the thread: work started (first mission assigned), a
  mission failed (with reason), a mission landed or its PR opened, landing failed, the whole item
  succeeded / failed / was cancelled. The message is written by the thread's captain when it is idle
  (`Ask.NarrateMilestones`, default true, with a short timeout) and otherwise is a deterministic sentence.
  When the user is not looking at the thread its `unread_count` increases and the thread list shows it.
- Voyage snapshot: voyage id, title, status, counts by mission status, and per mission: id, title, status,
  persona/pipeline stage, captain id and name, branch, latest check run status, merge-queue entry status,
  PR URL, landing outcome, failure reason, started/completed times. Fleet action run snapshot: run status,
  counts, and per-target status/reason (linking Mission targets' voyages). Job and import batch snapshots:
  status, progress counts, error.

### WebSocket security (Phase 0)

- `/ws` requires authentication at upgrade (the same bearer/session token as REST; browsers pass it as a
  query parameter or the `Sec-WebSocket-Protocol` header, since they cannot set `Authorization`).
  Unauthenticated upgrades are rejected.
- Each session records `TenantId`, `UserId`, `IsAdmin`, `IsTenantAdmin`. `WebSocketCommandHandler`
  commands are authorized with the same rules as their REST equivalents (`stop_server` admin only, writes
  TenantAdmin, reads tenant-scoped).
- Broadcasting becomes scoped: `BroadcastToTenant(tenantId, ...)` for entity changes (entities carry
  `TenantId`), `SendToUser(tenantId, userId, ...)` for `ask.*` events; admins may opt into all-tenant
  events. The dashboard's socket client sends its token.

### REST API (`/api/v1/ask/...`, all Authenticated, all owner-scoped; another user's thread returns 404)

| Method | Path | Body | Response |
|---|---|---|---|
| POST | `/ask/threads/enumerate` | `{ PageNumber, PageSize, Search, IncludeArchived }` | `EnumerationResult<AskThread>` ordered pinned first, then `LastMessageUtc` desc |
| POST | `/ask/threads` | `{ Title?, CaptainId?, AutoApprove? }` | 201 `AskThread` |
| GET | `/ask/threads/{id}` | | `AskThreadDetail { Thread, TrackedWork[], PendingProposals[] }` |
| PUT | `/ask/threads/{id}` | `{ Title?, CaptainId?, AutoApprove?, Pinned?, Archived? }` | `AskThread` |
| DELETE | `/ask/threads/{id}` | | 204 |
| POST | `/ask/threads/{id}/messages/enumerate` | `{ BeforeSequence?, PageSize }` | `{ Messages: AskMessage[] (with ToolCalls[], Proposal?, TrackedWork?), HasMore }` |
| POST | `/ask/threads/{id}/messages` | `{ Content, ShowThinking? }` | 202 `{ MessageId, TurnId }` |
| POST | `/ask/threads/{id}/cancel` | | stops the running captain turn |
| POST | `/ask/threads/{id}/summarize` | | 202; posts a `Summary` message and sets `SummaryText` |
| POST | `/ask/threads/{id}/read` | | marks the thread read (`UnreadCount = 0`) |
| POST | `/ask/threads/{id}/actions` | `{ ToolName, Arguments }` | `AskActionProposal` (executed) |
| POST | `/ask/threads/{id}/proposals/{pid}/approve` | | `AskActionProposal` |
| POST | `/ask/threads/{id}/proposals/{pid}/reject` | | `AskActionProposal` |
| GET | `/ask/threads/{id}/work/{workId}` | | `AskWorkSnapshot` |
| GET | `/ask/quick-actions` | | catalog of quick actions with their argument schemas |

New auto-generated titles: the first user message is truncated to a short title; after the first
assistant reply the captain may propose a better title (cheap, same turn) when the title is still the
default.

### WebSocket events (owner-scoped; all include `threadId`)

`ask.chunk { turnId, delta }`, `ask.thinking { turnId, delta }`, `ask.tool { turnId, phase, ... }`,
`ask.turn { turnId, state: started|completed|failed|cancelled, messageId }`,
`ask.message { message }` (any persisted message: milestone, result, summary),
`ask.proposal { proposal }`, `ask.work { trackedWorkId, snapshot }`,
`ask.thread { thread }` (title, unread count, last message changed).

### Dashboard

- `/ask` becomes a two-pane layout: a **thread list** (search, New conversation, pinned first, unread badge,
  live "working" dot when the thread has active tracked work, row menu: Rename, Pin, Summarize, Archive,
  Delete with confirm) and the **conversation**. `/ask/:threadId` deep-links a thread. On narrow screens
  the list becomes a drawer.
- Conversation header: editable title, captain picker, **Auto-approve** toggle (with a clear warning),
  Summarize, overflow menu. A collapsible **Work in this conversation** strip lists tracked items with live
  status chips; clicking one scrolls to its card.
- Messages render by kind: text (markdown), tool-call chips (existing), **confirm cards** (tool, one-line
  summary, expandable exact arguments, Approve / Reject, then the outcome), **work cards** (live: header
  with status and progress bar, per-mission rows with status, captain, stage, checks, merge/landing, PR
  link, failure reason, and links to the normal detail pages), **milestone messages**, summaries, errors.
- Composer: `/` opens the quick-action menu; each quick action opens an inline form (vessel picker and
  mission list for `/dispatch`, action + vessel picker for `/fleet-action`, etc.). Typing plain text sends
  to the captain. Stop button cancels a running turn.
- All updates arrive over the scoped WebSocket; the page also reconciles by refetching on reconnect.
  All strings go through the i18n runtime with catalog entries for every locale.

## Tasks

### Phase 0 -- WebSocket security

- [x] **P0.1** Reproduce in a test: an unauthenticated client can open `/ws` and run a mutating command;
  another tenant's client receives `mission.changed` and `ask.*` events. Then fix: authenticated upgrade,
  identity on the session, command authorization, tenant/user-scoped delivery, dashboard socket sends its
  token. _Acceptance:_ the reproduction tests now fail closed; existing WebSocket and dashboard behavior
  for a logged-in user is unchanged.

### Phase 1 -- Threads (backend)

- [x] **P1.1** Models, enums, ID prefixes, migration v75 (all four providers, MySQL wiring), DB interfaces
  and implementations, database suites.
- [x] **P1.2** `AskThreadService`: CRUD, owner scoping, message persistence and paging, unread tracking,
  auto titles, summarize.
- [x] **P1.3** Thread-aware `CaptainChatService` turn: server-side history, background turn with cancel,
  persisted assistant message and tool calls, owner-scoped streaming.
- [x] **P1.4** REST routes above; `/api/v1/ask` and `/api/v1/captains/{id}/chat` keep working.

### Phase 2 -- Actions and approvals

- [x] **P2.1** Thread-scoped MCP token; `AskToolPolicy` read-only allowlist; MCP interception creating
  proposals; auto-approve path.
- [x] **P2.2** Approve / reject / expire; in-process execution through the MCP tool handler; follow-up turn.
- [x] **P2.3** Quick actions catalog and `/actions` endpoint; work-linking table by tool.

### Phase 3 -- Live monitoring

- [x] **P3.1** `AskWorkTracker` with change-driven updates plus sweep; snapshot builders for voyage,
  mission, fleet action run, job, import batch; hash-based change detection; `ask.work` events.
- [x] **P3.2** Milestone detection and `WorkUpdate` messages; captain narration with deterministic fallback;
  unread counts.

### Phase 4 -- Dashboard

- [x] **P4.1** Thread list, routing, thread management (rename, pin, summarize, archive, delete), unread
  and working indicators.
- [x] **P4.2** Conversation rendering by message kind; confirm cards; live work cards; work strip.
- [x] **P4.3** Composer with quick actions and inline forms; stop; auto-approve toggle.
- [x] **P4.4** Scoped WebSocket client with token, reconnect, and refetch.

### Phase 5 -- Docs and verification

- [~] **P5.1** REST_API.md, MCP_API.md (thread-scoped behavior and the approval result text),
  WEBSOCKET_API.md, Postman ("Ask Threads" folder), CHANGELOG, README Ask Armada section.
- [x] **P5.2** Real end-to-end on macOS with a throwaway data directory and a real Claude Code captain:
  start a thread, ask a question, ask it to dispatch a small voyage against a temp repo, approve the confirm
  card, watch the work card and milestone messages update through landing, open a second thread for a
  separate activity, rename, summarize, delete. Screenshots at 1512 px light and dark.

## Settings (`ArmadaSettings.Ask`)

| Setting | Default | Range |
|---|---|---|
| `HistoryTurns` | 20 | 2-200 |
| `ProposalExpiryMinutes` | 60 | 1-1440 |
| `TrackerIntervalSeconds` | 5 | 2-300 |
| `NarrateMilestones` | true | -- |
| `NarrationTimeoutSeconds` | 60 | 10-600 |
| `TurnTimeoutMinutes` | 15 | 1-120 |

## Progress Log

| Date | Author | Task(s) | Change |
|------|--------|---------|--------|
| 2026-10-04 | (design) | -- | Plan drafted from the user's request and confirmed decisions. |
| 2026-10-04 | dashboard agent | P4.1-P4.4 | Dashboard built against typed mocks on `feature/ask-ui` (backend not merged yet): two-pane `/ask` + `/ask/:threadId`, thread management, message kinds, confirm and live work cards, work strip, composer with quick actions, token-authenticated socket with backoff and refetch on reconnect. Field-shape choices recorded under "UI assumptions" below. |
| 2026-10-04 | backend agent | P0.1 | Reproduction suite `E2E.WebSocketSecurity` written first: 7 of 11 cases failed against the open `/ws` (unauthenticated upgrade, invalid token, unauthenticated `create_fleet`, tenant admin command, cross-tenant `voyage.changed`, admin without opt-in, non-admin opt-in). Fixed: authenticated upgrade in PreRouting, identity per socket, tenant/user-scoped delivery, admin-only commands, proxy relay forwards the token. All 11 pass. |
| 2026-10-04 | backend agent | P1.1-P1.4 | Models, enums, prefixes, `Ask` settings, five DB interfaces with SQLite/PostgreSQL/SQL Server/MySQL implementations, migration v75 (parity verified on all four providers). `AskThreadService`, thread-aware `CaptainChatService.RunTurnAsync`, `AskTurnCoordinator`, REST routes. |
| 2026-10-04 | backend agent | P2.1-P2.3 | Thread-scoped session token (`askThreadId` claim on the MCP request), `AskToolPolicy`, `AskActionService` gate wrapping every MCP tool, approve/reject/expire with compare-and-set, in-process execution through the registered handler, follow-up turn, quick actions, work-linking table. |
| 2026-10-04 | backend agent | P3.1-P3.2 | `AskWorkSnapshotBuilder`, `AskWorkTracker` (hub change events + sweep), `AskMilestoneDetector`, narration with idle check and deterministic fallback, unread counts. |
| 2026-10-04 | backend agent | P5.2 (backend part) | Real run on macOS against a throwaway server (ports 47890/47891, `ARMADA_DATA_DIR` in a scratch directory) with a real Claude Code captain: unauthenticated `/ws` got `401`; the captain called `mcp__armada__dispatch` over its thread-scoped token and got "Proposed as aap_..." (no voyage existed before approval); approve executed the real handler (voyage created in the user's tenant), a second approve got `409`; `ask.work` snapshots and `WorkUpdate` messages followed the voyage (started, work produced, finished, the last one narrated by the captain); a second tenant's socket received only its `status.snapshot` and its REST read of the thread was `404`. Fixed during the run: percent-encoded session tokens on `?token=`, and voyages Armada marks Complete while a mission is still landing are now followed until every mission settles. |
| 2026-10-04 | backend agent | P5.1 | REST_API.md, MCP_API.md, WEBSOCKET_API.md, Postman "Ask Threads" folder, CHANGELOG. README Ask section left for the dashboard merge. |
| 2026-10-04 | orchestrator | P5.2 | Integration run through the real dashboard (Playwright, Chromium, 1512 px) against a throwaway server (ports 57890/57891) with a real Claude Code captain and a temp repo with a bare origin, vessel `LocalMerge` with auto-land on. Three conversations: (1) captain proposed `dispatch`, confirm card in about 6 s, approved, mission ran, landing failed because the first temp repo had no `origin` (test setup; the thread reported it correctly, including a captain-written explanation); (2) same flow in a new conversation through "Mission landed" and "voyage complete", commit verified on origin; (3) after fixes, order verified and rename, summarize, and delete exercised. Fixed during integration: the "not connected to Armada over MCP" banner was shown for Claude Code captains even though the server connects them per turn; the captain's reply was persisted when the turn ended, so a confirm card approved while the captain was still writing (and the resulting updates) sorted above the reply; the reply's position is now reserved when the turn starts. Full suite 2889/2889, dashboard 218/218. |

## UI assumptions (dashboard, 2026-10-04)

Where the contract above leaves a shape open, the dashboard (`src/Armada.Dashboard`) assumes the following. Every
assumed field is optional in the UI types (`types/models.ts`) and every endpoint call is one named function in
`api/client.ts`, so a different backend choice is a one-line change. Request bodies are PascalCase; responses are
camelized by the client; unknown statuses and kinds render as plain text.

- **AskThread** may carry `ActiveWorkCount` (int, tracked items still `Active`; drives the list's "working" dot until
  live `ask.work` events arrive) and `ActiveTurnId` (string or null; when present it is authoritative for whether a
  captain turn is running after a load or reconnect). An empty `Title` renders as "New conversation".
- **AskThreadDetail** is `{ Thread, TrackedWork[], PendingProposals[] }`; an `AskTrackedWork` may embed its latest
  `Snapshot`. Items without one are fetched with `GET .../work/{workId}` (up to 20, active first).
- **AskMessage** embeds `ToolCalls[]` (`CallId`, `ToolName`, `ArgumentsText`, `ResultText`, `Ok`, `ElapsedMs`),
  `Proposal`, `TrackedWork`, and has `CreatedUtc`. A message whose `Proposal` was already shown on its
  `ActionProposal` message does not repeat the card on the `ActionResult`.
- **Messages enumerate** returns `{ Messages, HasMore }`; without `BeforeSequence` it is the newest page. Order within
  a page does not matter (the UI sorts by `Sequence`). Older pages use `BeforeSequence = <oldest loaded Sequence>`.
- **AskActionProposal** may carry `ExpiresUtc` (shown on pending cards). A decided status (`Executed`, `Failed`,
  `Rejected`, `Expired`) is never replaced by a stale `Pending`/`Approved` copy.
- **AskWorkSnapshot** (flat, all optional except the first four): `TrackedWorkId`, `EntityType`, `EntityId`,
  `Status`, `State`, `Title`, `Counts` (status -> count), `TotalCount`, `CompletedCount`, `FailedCount`,
  `Missions[]` (`Id`, `Title`, `Status`, `VesselId`, `Persona`, `PipelineStage`, `CaptainId`, `CaptainName`,
  `BranchName`, `CheckRunId`, `CheckRunStatus`, `MergeEntryId`, `MergeQueueStatus`, `PrUrl`, `LandingOutcome`,
  `FailureReason`, `StartedUtc`, `CompletedUtc`), `Targets[]` (`Id`, `VesselId`, `VesselName`, `Status`, `Reason`,
  `MissionId`, `VoyageId`), `ErrorText`, `StartedUtc`, `CompletedUtc`, `CapturedUtc`. Progress comes from the rows,
  else `Counts`, else `CompletedCount`/`TotalCount`.
- **Quick actions** (`GET /ask/quick-actions`) are an array (a `{ QuickActions | Actions | Objects }` wrapper is also
  accepted) of `{ Name, Command, Title, Description, ToolName, ArgumentsSchema }`. Names the UI gives forms:
  `dispatch` (tool `dispatch`, args `{ title, vesselId, missions: [{ title, description }], pipelineId? }`),
  `fleet-action` (tool `run_fleet_action`, args `{ actionId, vesselIds }`), `import` (`ToolName` null; opens the
  existing import wizard). Any other action runs its `ToolName` with `{}` (`status` -> `status`, `health` ->
  `evaluate_vessel_health`). When the endpoint fails, the UI uses these five built-ins. `Arguments` are the MCP
  tool's own camelCase argument names.
- **New conversations** are created lazily on the first message or quick action from `/ask`, with `{ CaptainId }`
  only (the server picks the title). `PUT` sends only the changed fields; `CaptainId: null` clears the captain.
- **WebSocket**: the dashboard connects to `/ws?token=<session token>` and still sends `{ Route: "subscribe" }` on
  open. Ask event payloads are camelCase (PascalCase is tolerated). `ask.turn.state` is matched case-insensitively and
  may carry `error`; `ask.work` carries `trackedWorkId`, `snapshot`, and optionally `trackedWork`; `ask.thread`
  carries the full thread. If `threadId` is missing, the nested entity's `threadId` (or `thread.id`) is used.
- **Reconciliation**: after each terminal `ask.turn`, approve/reject, and quick action, the UI refetches the newest
  message page and the thread detail, so a separate `ask.message` for the final reply or result is welcome but not
  required. After a socket reconnect it refetches the thread list and the open thread.
- **Read state**: the open thread is marked read (`POST .../read`) when it loads, when the tab becomes visible, and
  when an `ask.thread` for it reports `UnreadCount > 0`.
- `GET /ask/threads/{id}` returning 404 shows a "conversation not found" state.

## Backend implementation notes (2026-10-04)

These refine the contract above; the dashboard's "UI assumptions" (on `feature/ask-ui`) are satisfied as noted.

- **WebSocket authentication.** `/ws` accepts the REST headers, `?token=<token>` (session token, bearer credential
  token, or API key), or a `Sec-WebSocket-Protocol` entry `armada-token.<base64url(token)>`. Watson 7.2 does not echo a
  subprotocol in its `101` response and browsers reject that, so **browsers must use `?token=`** (the dashboard does).
- **WebSocket commands are global-admin only** (deviation from "writes TenantAdmin, reads tenant-scoped"): the command
  handler reads and writes by id across tenants, so tenant-scoping it would mean re-implementing every command; tenant
  users use the tenant-scoped REST API. The dashboard does not use commands.
- **Delivery.** Entity events go to the entity's tenant (tenant resolved from the entity; unresolved events go to global
  admins only); admins opt in to all tenants with `AllTenants: true` on subscribe. `ask.*` events go only to the owner;
  the all-tenants opt-in does not apply to them.
- **Additive fields** (not in the original table, requested by the UI): `AskThread.ActiveWorkCount` and
  `AskThread.ActiveTurnId` (computed), `AskActionProposal.ExpiresUtc` (computed while pending),
  `AskTrackedWork.Snapshot` (embedded in thread detail and in messages), `AskQuickAction.Name` and `ArgumentsSchema`.
  `ask.work` is `{ threadId, trackedWorkId, snapshot, trackedWork }`; `ask.turn` adds `error` on failure.
- **AskWorkSnapshot** is flat: `TrackedWorkId, ThreadId, EntityType, EntityId, Title, Status, State, Found, TotalCount,
  CompletedCount, FailedCount, ActiveCount, Progress, Counts, Missions[], Targets[], ErrorText, StartedUtc, CompletedUtc,
  CapturedUtc`. Mission rows: `Id, Title, Status, VoyageId, VesselId, Persona, PipelineStage, CaptainId, CaptainName,
  BranchName, CheckRunId, CheckRunStatus, MergeEntryId, MergeQueueStatus` (alias `MergeStatus`)`, PrUrl, LandingOutcome,
  FailureReason, StartedUtc, CompletedUtc`. Target rows: `Id, VesselId, VesselName, Status, Reason, VoyageId, MissionId,
  ExitCode`.
- **PUT** distinguishes an absent `CaptainId` (keep) from `"CaptainId": null` (clear). Creating with only
  `{ CaptainId }` is supported; the first message names the thread. The optional "captain proposes a better title" step
  is not implemented (the truncated first message is the title).
- **Quick actions** use the real MCP argument names: `/dispatch` -> `dispatch { title, vesselId, missions: [{ title,
  description }], description?, pipelineId? }`; `/fleet-action` -> `run_fleet_action { actionId, vesselIds,
  concurrency? }`; `/status` -> `status {}`; `/health` -> `evaluate_vessel_health {}` (optional `vesselIds`, `fleetId`,
  `force`); `/import` -> `discover_vessels { roots?, directories?, maxDepth?, runInBackground? }`.
- **Thread-scoped MCP** works for ApiEndpoint captains (environment) and Claude Code (per-launch strict MCP config with an
  `X-Token` header, deleted when the process exits). Codex, Gemini, Cursor, Mux, and OpenCode captains keep their host
  MCP configuration in thread turns (isolating them would hide their own logins), so their tool calls are not gated.
- **Work linking** keeps `create_voyage` and `retry_mission` in the table although no MCP tools have those names today.
- **Narration** runs only when the thread's captain is `Idle`, no turn runs in the thread, and no other Ask turn uses the
  captain; it never changes the captain's state, uses a gated thread token, and falls back to the deterministic sentence
  on timeout or failure. Milestones are detected against an in-memory previous snapshot; after a restart only terminal
  changes are reported, so nothing is repeated.
- **Settling.** A mission row counts as done when Complete or WorkProduced with nothing landing (no merge-queue entry
  queued, testing, or passed); a voyage stays Active (even if Armada already marked it Complete) until every mission has
  settled, so landing outcomes still reach the thread. Milestones add "Mission X produced its work on branch Y".
- **Unread**: every captain or Armada message (reply, proposal card, action result, work update, summary, error)
  increments `UnreadCount`; user messages do not. `POST .../read` resets it.
- **Retention**: none beyond delete.
