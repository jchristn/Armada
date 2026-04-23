# Playbooks Implementation Plan

This document is the implementation playbook for adding first-class, selectable markdown playbooks to Armada.

Primary use case:
- An administrator opens the dashboard, goes to Playbooks, creates a markdown document named `CSHARP_BACKEND_ARCHITECTURE.md`, and stores detailed backend requirements for C# work.
- When creating a mission or dispatching a voyage, the administrator can select one or more playbooks.
- Armada passes the selected playbooks to the model as part of the mission instructions for every relevant stage of execution.
- The full lifecycle of playbooks lives in the UI: create, read, update, delete, search, preview, and selection during dispatch.

## Tracking

Use these markers directly in this file:
- `[ ]` Not started
- `[-]` In progress
- `[x]` Complete
- Add owner/date/PR notes inline under any item when useful.

Suggested annotation format:

```md
- [ ] Add playbook CRUD routes
  Owner: alice
  PR: #123
  Notes: waiting on database migration review
```

## Current Status

Implementation snapshot as of 2026-04-23:

- [x] Core playbook feature is implemented across backend persistence, mission snapshotting, prompt injection, and the three delivery modes.
- [x] REST, MCP, remote-control/proxy, C# SDK, Helm CLI, dashboard UX, and Postman surfaces are implemented.
- [x] Mission and voyage detail views expose playbook selections and mission snapshots.
- [x] Schema changes and driver support are implemented for SQLite, MySQL, PostgreSQL, and SQL Server.
- [x] Build validation completed for `Armada.Server`, `Armada.Helm`, `Armada.Dashboard`, and the unit test project compile.
- [-] Remaining follow-up work is concentrated in deeper automated coverage, optional docs/examples polish such as `GETTING_STARTED.md`, and any additional manual QA the team wants before release.

Recommended annotation practice from this point:
- mark checklist items below as `[x]` only when you have directly verified that specific requirement
- use `[-]` for release polish or follow-up hardening rather than reopening shipped core behavior

## Product Decisions

These decisions should be implemented first and treated as the baseline unless product explicitly changes direction.

- [x] Model playbooks as a new tenant-scoped entity, not as prompt templates.
  Reason: prompt templates define Armada’s system prompt structure; playbooks are user-authored, selectable instruction documents.

- [x] Store playbooks in Armada’s database with a user-facing filename and markdown content, not as unmanaged server-local files.
  Reason: Armada is multi-tenant, already stores operational configuration in the database, and needs API/UI lifecycle management.

- [x] Treat the filename as a first-class field and require `.md` validation.
  Example: `CSHARP_BACKEND_ARCHITECTURE.md`.

- [x] Allow any number of playbooks to be selected for a voyage or mission, with explicit ordering.
  Reason: order matters when multiple documents are injected into the model context.

- [x] Support three delivery modes for every selected playbook:
  - `InlineFullContent`
  - `InstructionWithReference`
  - `AttachIntoWorktree`
  Reason: different missions need different tradeoffs between fidelity, context cost, and local file ergonomics.

- [x] Do not model delivery mode on the playbook record itself.
  Implemented design:
  - the actual `DeliveryMode` used for a dispatch lives on the voyage selection and mission snapshot
  Reason: the same playbook must remain reusable with different delivery behavior across different voyages and missions.

- [x] Snapshot selected playbook content onto missions when work is created.
  Reason: a mission must remain reproducible even if the source playbook is edited or deleted later.

- [x] For path-based modes, Armada must materialize a readable file and record the resolved path used by the mission.
  Reason: the model does not consume an abstract file handle; the runtime needs a concrete path it can read.

- [x] If a playbook is attached into the worktree, place it in a deterministic generated directory and keep it out of git history.
  Recommended path: `.armada/playbooks/`
  Recommended protection: worktree-local ignore or equivalent generated-file strategy that does not modify the repository's tracked files.

- [x] Apply selected playbooks to every pipeline stage created from a voyage unless a stage-specific override is intentionally added later.

- [x] Expose full CRUD and selection flows in the React dashboard as the canonical UI.

- [ ] Decide legacy embedded dashboard behavior explicitly.
  Recommended default: show playbook management only in the React dashboard and display a clear unsupported message in any legacy fallback UI, rather than shipping a half-implemented experience.

- [x] Add context-budget safeguards for large or numerous playbooks.
  Minimum requirement: aggregate size calculation, warnings in UI, and server-side validation/error messaging.

## Scope Summary

The implementation touches all of these layers:
- Backend domain model and storage
- Prompt assembly and mission launch flow
- Voyage and mission dispatch/request models
- REST API and authorization
- MCP tools
- Remote management and proxy surfaces
- Dashboard pages, navigation, forms, and detail views
- C# client SDK and CLI
- Tests across unit, database, automation, and manual UI QA
- Documentation and Postman collection

## Phase 1: Domain Model And Persistence

### 1.1 Add playbook domain entities

- [ ] Add a `Playbook` entity in the core model layer.
  Minimum fields:
  - `Id`
  - `TenantId`
  - `FileName`
  - `Description`
  - `Content`
  - `CreatedAtUtc`
  - `UpdatedAtUtc`

- [ ] Decide whether `Title` is stored separately or derived from filename.
  Recommended: store both `FileName` and optional `Description`; derive title from filename unless a separate title is clearly needed.

- [ ] Add a voyage-level selection entity.
  Recommended shape: `VoyagePlaybookSelection` with:
  - `VoyageId`
  - `PlaybookId`
  - `SortOrder`
  - `DeliveryMode`

- [ ] Add a mission-level immutable snapshot entity.
  Recommended shape: `MissionPlaybookSnapshot` with:
  - `MissionId`
  - `PlaybookId` nullable only if source is later deleted
  - `FileName`
  - `Description`
  - `Content`
  - `SortOrder`
  - `DeliveryMode`
  - `ResolvedPath` nullable
  - `WorktreeRelativePath` nullable
  - `SourceUpdatedAtUtc` or equivalent provenance field

- [ ] Decide whether standalone mission creation also persists a reusable mission-level selection reference in addition to snapshots.
  Recommended: snapshots are required; separate mission selection reference is optional.

### 1.2 Extend database abstractions

- [ ] Add database method interfaces for playbook CRUD and relationship access in `DatabaseDriver` abstractions.

- [ ] Add `PlaybookMethods` and selection/snapshot access methods to all database drivers:
  - SQLite
  - MySQL
  - PostgreSQL
  - SQL Server

- [ ] Follow the same pattern currently used for prompt templates, personas, and pipelines so feature parity exists across every supported database.

### 1.3 Add migrations and indexes

- [ ] Add schema migrations for:
  - `playbooks`
  - `voyage_playbooks`
  - `mission_playbook_snapshots`

- [ ] Add uniqueness and validation constraints.
  Minimum expectations:
  - `(TenantId, FileName)` unique
  - non-empty markdown content
  - non-empty filename ending in `.md`

- [ ] Add indexes for common queries.
  Minimum expectations:
  - `playbooks(TenantId, UpdatedAtUtc)`
  - `voyage_playbooks(VoyageId, SortOrder)`
  - `mission_playbook_snapshots(MissionId, SortOrder)`

- [ ] Define delete behavior explicitly.
  Recommended:
  - deleting a playbook removes future selection ability
  - existing mission snapshots remain intact
  - voyage selections referencing deleted playbooks are removed or blocked by FK rules depending on product choice

### 1.4 Service layer

- [ ] Add `PlaybookService` in the core services layer.
  Responsibilities:
  - validate filenames and content
  - enforce tenant scoping
  - list/search playbooks
  - resolve selected IDs in requested order
  - resolve default vs overridden delivery mode
  - create mission snapshots
  - materialize files for reference-based modes
  - manage deterministic worktree attachment paths
  - build prompt-ready combined markdown

- [ ] Add size/count validation rules in `PlaybookService`.
  Minimum expectations:
  - total selected characters/bytes
  - max playbook count
  - per-playbook max size
  - valid delivery mode values
  - actionable validation messages

- [ ] Add usage queries for dashboard display.
  Recommended metrics:
  - last used at
  - mission count
  - voyage count

## Phase 2: Prompt Assembly And Runtime Injection

### 2.1 Introduce playbooks into prompt composition

- [ ] Extend mission prompt assembly so selected playbooks are injected into model instructions before the agent starts work.

- [ ] Update `MissionService` instruction-file generation to include selected playbooks in the generated runtime instruction document.
  Target flow currently includes project context, style guide, persona prompts, and mission rules; playbooks must become a first-class section in that same flow.

- [ ] Update `MissionPromptBuilder` template parameter assembly to expose selected playbooks where appropriate.

- [ ] Add a dedicated prompt template wrapper for playbooks.
  Recommended template name: `mission.playbooks_wrapper`.

- [ ] Implement the three delivery modes explicitly in prompt rendering.
  Required behavior:
  - `InlineFullContent`: include the full markdown body in the rendered instructions
  - `InstructionWithReference`: include a concise instruction telling the model to consult the playbook at a resolved readable path outside or alongside the worktree
  - `AttachIntoWorktree`: materialize the playbook into the dock/worktree and instruct the model to consult that worktree path

- [ ] Decide exact placement order in the final instruction bundle.
  Recommended order:
  1. captain/system instructions
  2. vessel project context
  3. vessel style guide
  4. accumulated model context
  5. selected playbooks
  6. persona guidance
  7. mission-specific task instructions

- [ ] Ensure playbooks are clearly delimited in the rendered prompt.
  Minimum format:
  - section header
  - filename per playbook
  - delivery mode
  - preserved markdown body for inline mode
  - resolved path for reference/worktree modes
  - stable order matching UI selection

### 2.2 Snapshot and retry behavior

- [ ] Ensure mission creation snapshots the selected playbooks before prompt generation.

- [ ] Ensure pipeline-generated child missions inherit the same playbook snapshot content in the same order.

- [ ] Ensure mission restart and retry behavior is deterministic.
  Recommended rule:
  - default restart/retry uses the original mission snapshots
  - optional explicit override can replace playbooks only if requested by API/UI/CLI later

- [ ] Ensure mission detail and audit tooling can show exactly which playbooks were used for a completed mission.

- [ ] Ensure file materialization happens before the agent starts and is available for the entire mission lifecycle.

- [ ] Decide cleanup behavior for materialized files.
  Recommended default:
  - keep mission-scoped reference files available while the mission is active
  - allow deterministic regeneration from mission snapshots when historical replay or inspection is needed
  - do not require tracked repository changes for worktree-attached files

### 2.3 Context safety

- [ ] Define truncation policy.
  Recommended default: reject requests exceeding configured limits instead of silently truncating user-authored architecture requirements.

- [ ] Surface aggregate size metadata in responses used by the UI so selection screens can warn before dispatch.

- [ ] Add structured errors for oversize playbook sets so CLI, SDK, MCP, and UI can present the same failure reason.

- [ ] Document that inline mode consumes prompt context directly, while reference and worktree modes trade prompt tokens for file materialization complexity.

## Phase 3: Voyage, Mission, And Dispatch Flows

### 3.1 Request and DTO changes

- [ ] Extend voyage creation requests to include ordered playbook selections.
  Recommended field: `SelectedPlaybooks`.
  Recommended item shape:
  - `PlaybookId`
  - `DeliveryMode`

- [ ] Extend mission creation requests to include ordered playbook selections.

- [ ] Extend mission restart/update models where appropriate to preserve or override playbooks.

- [ ] Extend read models so voyage and mission detail endpoints return:
  - selected playbook references
  - mission snapshot metadata
  - resolved delivery mode
  - resolved path metadata where applicable
  - aggregate size/count information where useful

### 3.2 Inheritance rules

- [ ] Define voyage-to-mission inheritance explicitly.
  Recommended rule:
  - voyage-level `SelectedPlaybooks` are applied to every mission in that voyage unless mission-level explicit playbooks are added in a future enhancement

- [ ] Define mission-level explicit creation behavior.
  Recommended rule:
  - standalone mission creation accepts `SelectedPlaybooks` directly

- [ ] Define precedence rules if both voyage-level and mission-level playbooks ever exist simultaneously.
  Recommended default:
  - mission-level explicit selection replaces inherited selection unless product asks for merge semantics

- [ ] Ensure pipeline dispatch preserves playbooks across every generated stage and dependency chain.

### 3.3 Operational surfaces

- [ ] Update logging and event payloads where needed so operators can understand which playbooks were applied during dispatch.

- [ ] Decide whether mission/voyage search and filtering should support playbook-based queries.
  Recommended: not required for v1, but usage display on detail pages is required.

## Phase 4: REST API, Authorization, And Contracts

### 4.1 New playbook API

- [ ] Add `/api/v1/playbooks` routes for:
  - list
  - get by id
  - create
  - update
  - delete

- [ ] Support list filters.
  Recommended filters:
  - search by filename/description
  - sort by updated date
  - pagination

- [ ] Return usage metadata on list/detail endpoints if feasible without excessive cost.

### 4.2 Existing API changes

- [ ] Update voyage create and mission create endpoints to accept `SelectedPlaybooks`.

- [ ] Update voyage detail and mission detail responses to include selected playbooks and mission snapshots.

- [ ] Update restart/retry endpoints and contracts if they need to preserve or override playbooks.

- [ ] Update remote dispatch contract payloads that mirror voyage and mission creation.

### 4.3 Authorization

- [ ] Add explicit authorization rules for playbook routes in `AuthorizationConfig`.

- [ ] Decide permission model.
  Recommended:
  - authenticated users can list and view playbooks
  - tenant admins can create, update, and delete playbooks
  - dispatch permissions remain aligned with existing mission/voyage permissions

- [ ] Ensure tenant scoping is enforced in every CRUD and selection path.

### 4.4 Contract documentation

- [ ] Update API schemas/examples for every changed route.

- [ ] Add example payloads showing multi-playbook selection during voyage dispatch.

- [ ] Add example responses showing mission snapshot metadata.

## Phase 5: MCP, Remote Management, Proxy, And Automation Interfaces

### 5.1 MCP tools

- [ ] Add MCP tools for playbook lifecycle management.
  Minimum tool set:
  - create playbook
  - get playbook
  - list/enumerate playbooks
  - update playbook
  - delete playbook

- [ ] Extend existing dispatch-related MCP tools to accept structured playbook selections with delivery modes.
  Minimum surfaces:
  - voyage dispatch
  - mission create
  - mission update if applicable
  - mission restart if override support exists

- [ ] Extend enumerate support to include a `playbooks` entity type.

### 5.2 Remote management and proxy

- [ ] Update remote management request/response handling to accept and return playbook data where voyage and mission dispatch already flow through remote surfaces.

- [ ] Update proxy-facing contracts and docs so remote clients can create work with playbooks attached.

- [ ] Verify playbook metadata survives serialization across server, proxy, and remote control layers.

### 5.3 Backward compatibility

- [ ] Ensure older clients that omit playbooks continue to work unchanged.

- [ ] Ensure servers reject unknown or invalid playbook IDs with clear validation messages instead of partial execution.

## Phase 6: Dashboard UX And Visual Design

This phase must be treated as product work, not just field wiring. The UI has to be understandable, styled, and operationally useful.

### 6.1 Navigation and route structure

- [ ] Add a Playbooks entry to the dashboard navigation in a location consistent with other configuration entities such as Personas, Pipelines, and Templates.

- [ ] Add pages for:
  - Playbooks list
  - Playbook detail/editor
  - any create route if separated from detail

- [ ] Add route protection and empty states consistent with the rest of the dashboard.

### 6.2 Playbooks list page

- [ ] Build a fully styled Playbooks index page.
  Minimum content:
  - page title and summary
  - search box
  - create button
  - table or card list with filename, description, updated time, and usage indicators
  - empty state explaining what playbooks do

- [ ] Make the list page understandable at a glance.
  Recommended layout:
  - top summary strip describing how playbooks affect dispatched work
  - primary list area
  - secondary guidance panel with selection behavior and best practices

- [ ] Add delete affordances with confirmation flow that explains snapshot behavior.
  Minimum message:
  - deleting the playbook stops future selection
  - past missions keep their snapshots

### 6.3 Playbook detail/editor page

- [ ] Build a fully styled playbook editor/detail page.
  Minimum content:
  - filename input
  - description input
  - default delivery mode selector with clear explanations of all three modes
  - markdown editor
  - preview tab or split view
  - save/delete actions
  - metadata panel with created/updated timestamps and usage info

- [ ] Validate `.md` filename input in the UI before submit.

- [ ] Add unsaved-change handling.

- [ ] Preserve readable typography and spacing for long-form markdown authoring.

- [ ] Design the page to work on desktop and mobile.
  Recommended desktop layout:
  - left main editor area
  - right metadata/help panel
  Recommended mobile layout:
  - stacked sections with sticky action bar

### 6.4 Dispatch and voyage creation selection UI

- [ ] Add playbook selection to `Dispatch` and `VoyageCreate`.

- [ ] Build a reusable `PlaybookPicker` component instead of duplicating form code.

- [ ] Support ordered multi-select.
  Minimum interactions:
  - add from available list
  - remove
  - reorder
  - choose or override delivery mode per selected playbook
  - preview selected content

- [ ] Add search/filter in the selector for large playbook libraries.

- [ ] Show aggregate size/count while selecting.

- [ ] Add warnings for oversize or risky selections before submit.

- [ ] Show clear mode guidance while selecting.
  Minimum guidance:
  - inline mode: highest fidelity, highest prompt cost
  - reference mode: lower prompt cost, depends on resolved external/reference path
  - worktree mode: lower prompt cost, file appears in the mission worktree

- [ ] Make the selector understandable without training.
  Recommended layout:
  - available playbooks library on one side
  - selected playbooks stack on the other
  - inline preview drawer or modal
  - clear explanation that selection order is preserved in prompt injection

- [ ] Preserve responsive behavior on smaller screens.
  Minimum requirement:
  - selector collapses into stacked panels without becoming unusable

### 6.5 Detail pages and visibility

- [ ] Show selected playbooks on voyage detail pages.

- [ ] Show snapshot playbooks on mission detail pages.

- [ ] Make it obvious whether the user is seeing live playbook data or the immutable mission snapshot.

- [ ] Show the resolved delivery mode and any materialized path used by the mission.

- [ ] Add quick links from mission/voyage details back to the source playbook where applicable.

### 6.6 Localization and theme support

- [ ] Add all new dashboard strings to the localization system.

- [ ] Verify styling in both supported theme modes.

- [ ] Reuse dashboard design tokens and CSS variables rather than shipping isolated one-off styles.

### 6.7 Access control in UI

- [ ] Hide or disable create/edit/delete actions for non-admin users if backend permissions are admin-only.

- [ ] Keep read-only browsing usable for users who can dispatch but cannot modify playbooks.

## Phase 7: CLI, SDKs, And Developer Surfaces

### 7.1 TypeScript/dashboard client

- [ ] Update dashboard API client methods and model types for playbook CRUD and selection-aware dispatch requests.

- [ ] Add typed response models for voyage playbook selections, delivery modes, resolved paths, and mission snapshot data.

### 7.2 C# client SDK

- [ ] Extend `ArmadaApiClient` with playbook CRUD support.

- [ ] Extend voyage and mission request models with `SelectedPlaybooks` and delivery mode support.

- [ ] Add examples for constructing requests with multiple playbooks.

### 7.3 Helm CLI

- [ ] Add CLI commands for playbook management if Armada CLI is expected to be feature-complete.
  Recommended commands:
  - `armada playbooks list`
  - `armada playbooks get`
  - `armada playbooks create`
  - `armada playbooks update`
  - `armada playbooks delete`

- [ ] Extend existing voyage/mission creation commands to accept playbook selections and delivery modes.

- [ ] Decide CLI syntax for delivery mode selection.
  Recommended v1 syntax:
  - repeated `--playbook <id>:inline`
  - repeated `--playbook <id>:reference`
  - repeated `--playbook <id>:worktree`

- [ ] Decide whether CLI should accept filenames and resolve them server-side or require IDs.
  Recommended for v1: accept IDs; optional future enhancement for filename lookup.

- [ ] Update command help text and examples.

## Phase 8: Tests And Verification

### 8.1 Unit tests

- [ ] Add unit tests for playbook validation.
  Minimum cases:
  - valid `.md` filename
  - invalid extension
  - duplicate filename in tenant
  - empty content

- [ ] Add unit tests for selection ordering and combined markdown rendering.

- [ ] Add unit tests for delivery mode resolution.
  Minimum cases:
  - playbook default mode used when no override is supplied
  - voyage/mission override mode replaces default
  - invalid delivery mode is rejected

- [ ] Add unit tests for mission snapshot creation.

- [ ] Add unit tests for prompt generation to verify all three delivery modes render correctly.

- [ ] Add unit tests for worktree materialization paths and git-safe ignore behavior.

- [ ] Add unit tests for restart/retry semantics and inheritance behavior.

### 8.2 Database tests

- [ ] Add database tests for CRUD across supported drivers.

- [ ] Add tests for tenant scoping.

- [ ] Add tests for snapshot persistence after source playbook updates/deletes.

- [ ] Add tests for cascade and FK behavior around voyage selections and mission snapshots.

### 8.3 API and automation tests

- [ ] Add automated API coverage for playbook CRUD endpoints.

- [ ] Add automated coverage for voyage dispatch with one playbook.

- [ ] Add automated coverage for voyage dispatch with multiple ordered playbooks.

- [ ] Add automated coverage for standalone mission creation with playbooks.

- [ ] Add automated coverage for invalid playbook IDs, invalid delivery modes, and oversize payloads.

- [ ] Add automated coverage for all three delivery modes end to end.

- [ ] Add automated coverage for mission detail and voyage detail responses containing playbook metadata.

### 8.4 MCP, proxy, and remote tests

- [ ] Add tests for MCP playbook tools.

- [ ] Add tests for dispatch through MCP with structured playbook selections and delivery modes.

- [ ] Add tests for remote/proxy serialization and round-trip behavior.

### 8.5 Dashboard verification

- [ ] Add frontend test coverage if the dashboard test stack exists or is being introduced.
  Minimum candidate coverage:
  - playbook list renders
  - create/edit validation
  - selector ordering behavior
  - oversize warning behavior

- [ ] If frontend automated tests are not currently present, add a manual QA checklist and complete it before merge.
  Minimum manual QA:
  - create playbook
  - edit playbook
  - delete playbook
  - select one playbook in voyage dispatch
  - select multiple playbooks and reorder them
  - dispatch once with inline mode
  - dispatch once with reference mode
  - dispatch once with worktree mode
  - verify mission detail shows snapshots
  - verify non-admin permissions
  - verify mobile layout

## Phase 9: Documentation, Postman, And Release Readiness

### 9.1 Product and operator documentation

- [ ] Update `README.md` with a high-level explanation of playbooks.

- [ ] Update `GETTING_STARTED.md` with basic playbook setup and dispatch usage.

- [ ] Update relevant instruction and architecture docs to explain how playbooks differ from prompt templates, personas, pipelines, and vessel context.

- [ ] Document snapshot semantics clearly so operators understand why old missions do not change when a playbook is edited.

- [ ] Document the three delivery modes and when to use each one.

### 9.2 API documentation

- [ ] Update REST API docs with playbook CRUD and selection-aware dispatch examples.

- [ ] Update MCP API docs with new tools and fields.

- [ ] Update proxy and remote management docs where voyage and mission contracts are mirrored.

### 9.3 Postman collection

- [ ] Add a `Playbooks` section to `Armada.postman_collection.json`.

- [ ] Add request examples for:
  - create playbook
  - list playbooks
  - update playbook
  - delete playbook
  - dispatch voyage with `SelectedPlaybooks`
  - create mission with `SelectedPlaybooks`

- [ ] Add sample response examples showing playbook metadata on mission/voyage details.

### 9.4 Release notes and migration guidance

- [ ] Add release notes describing the feature, migration requirements, and any client updates needed.

- [ ] Call out any minimum dashboard version requirements if the React dashboard is required for full playbook management.

## Acceptance Criteria

The feature is not complete until every statement below is true.

- [ ] An admin can create, edit, preview, and delete a markdown playbook entirely from the dashboard.

- [ ] The admin can create a playbook named `CSHARP_BACKEND_ARCHITECTURE.md` and store detailed C# backend guidance in markdown form.

- [ ] A user dispatching a voyage can select one or more playbooks in a clear, styled, understandable UI.

- [ ] Selection order is preserved and visible in the UI.

- [ ] For every selected playbook, the user can choose or inherit one of the three delivery modes: inline, reference, or worktree.

- [ ] Selected playbooks are delivered to the model exactly according to the resolved delivery mode used for mission execution.

- [ ] Every pipeline stage created from a voyage receives the intended playbooks.

- [ ] Mission detail shows the immutable snapshot of the playbooks actually used, including resolved delivery mode and materialized path metadata when applicable.

- [ ] Editing or deleting a playbook does not silently change previously created missions.

- [ ] CRUD is available through the REST API, dashboard, SDKs, and automation surfaces that are expected to support configuration entities.

- [ ] Validation failures are understandable in UI, CLI, SDK, MCP, and API responses.

- [ ] The dashboard implementation is responsive, styled coherently with the existing product, and readable on both desktop and mobile.

- [ ] Documentation and Postman examples are updated.

- [ ] Existing clients that do not use playbooks continue to work.

## Recommended Implementation Order

- [ ] Finalize product decisions and schema shape
- [ ] Implement core entities, migrations, and service layer
- [ ] Inject playbooks into prompt generation and snapshot flow
- [ ] Extend voyage/mission contracts and dispatch paths
- [ ] Add REST routes and authorization
- [ ] Add dashboard list/editor/selector/detail views
- [ ] Extend SDK, CLI, MCP, proxy, and remote control layers
- [ ] Complete tests
- [ ] Update docs and Postman
- [ ] Run end-to-end verification before merge

## Risks And Watchouts

- [ ] Do not reuse prompt templates as a substitute for playbooks.
  This will blur product boundaries and make per-dispatch document selection awkward.

- [ ] Do not inject live playbook content into old missions without snapshots.
  That would break auditability and reproducibility.

- [ ] Do not ship selection UI without size feedback.
  Users can easily exceed practical context limits with multiple architecture documents.

- [ ] Do not attach files into the worktree in a way that pollutes git status or risks accidental commits.

- [ ] Do not assume path-reference modes are free.
  They reduce prompt size, but they require reliable file materialization, path stability, and clear operator visibility.

- [ ] Do not hide playbook application details from mission/voyage detail screens.
  Operators need to know what instructions actually shaped a run.

- [ ] Do not ship a weak dashboard UX.
  This feature is configuration-heavy; list, editor, preview, selection, and detail pages all need clear information hierarchy and styling.
