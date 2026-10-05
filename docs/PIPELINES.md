# Pipelines -- Implementation Reference

This document covers the complete pipeline implementation in Armada v1.0.0: data model, dispatch flow, execution lifecycle, review gates, stage handoff, architect special handling, captain routing, and extensibility.

---

## Table of Contents

1. [Overview](#1-overview)
2. [Data Model](#2-data-model)
3. [Built-in Pipelines and Personas](#3-built-in-pipelines-and-personas)
4. [Dispatch Flow](#4-dispatch-flow)
5. [Pipeline Resolution](#5-pipeline-resolution)
6. [Mission Creation and Dependency Chain](#6-mission-creation-and-dependency-chain)
7. [Assignment and Persona-Aware Captain Routing](#7-assignment-and-persona-aware-captain-routing)
8. [Stage Handoff](#8-stage-handoff)
9. [Architect Special Handling](#9-architect-special-handling)
10. [Prompt Template Resolution](#10-prompt-template-resolution)
11. [Database Schema](#11-database-schema)
12. [API Surface](#12-api-surface)
13. [Dashboard Integration](#13-dashboard-integration)
14. [Configuration Patterns](#14-configuration-patterns)
15. [Extensibility](#15-extensibility)

---

## 1. Overview

A **pipeline** is an ordered sequence of **persona stages** that a dispatch goes through. Without pipelines, every dispatch creates Worker missions that run independently. With pipelines, a single dispatch can flow through planning (Architect), implementation (Worker), testing (Test Engineer), and review (Judge) stages automatically.

Key design decisions:
- **Option B**: Persona is a property of the mission, not the captain. Any captain can fill any role.
- **Option 3**: Built-in defaults ship with the code; database-stored overrides allow customization.
- **Level 2**: Pipeline configured at vessel/fleet level, overridable per dispatch.

---

## 2. Data Model

### Pipeline

| Field | Type | Description |
|-------|------|-------------|
| `Id` | string (`ppl_` prefix) | Unique identifier |
| `TenantId` | string? | Tenant scope |
| `UserId` | string? | Owning user |
| `Scope` | `ScopeEnum` | `TenantWide` (default) or `UserSpecific` |
| `Name` | string | Unique pipeline name |
| `Description` | string? | Human-readable description |
| `Stages` | List\<PipelineStage\> | Ordered list of stages |
| `IsBuiltIn` | bool | True for system-shipped pipelines (cannot be deleted) |
| `Active` | bool | Whether the pipeline is active |

**Source:** `src/Armada.Core/Models/Pipeline.cs`

### PipelineStage

| Field | Type | Description |
|-------|------|-------------|
| `Id` | string (`pps_` prefix) | Unique identifier |
| `PipelineId` | string | Parent pipeline ID |
| `Order` | int | Execution order (1-based) |
| `PersonaName` | string | Persona name for this stage (e.g. "Worker", "Judge") |
| `IsOptional` | bool | Stored and shown in the UI; dispatch currently runs every stage regardless |
| `Description` | string? | What this stage does |
| `RequiresReview` | bool | When true, the stage's mission stops in `Review` and waits for an explicit approve/deny before the pipeline continues (default false) |
| `ReviewDenyAction` | `ReviewDenyActionEnum` | What a denied review does: `RetryStage` (default; send the same stage back for rework) or `FailPipeline` (fail the stage and cancel downstream stages) |

**Source:** `src/Armada.Core/Models/PipelineStage.cs`

### Persona

| Field | Type | Description |
|-------|------|-------------|
| `Id` | string (`prs_` prefix) | Unique identifier |
| `Name` | string | Unique persona name |
| `Description` | string? | What this persona does |
| `PromptTemplateName` | string | References a prompt template by name (see [Prompt Template Resolution](#10-prompt-template-resolution) for how mission prompts pick a template) |
| `IsBuiltIn` | bool | True for system-shipped personas |
| `Active` | bool | Whether the persona is active |
| `Scope` | `ScopeEnum` | `TenantWide` (default) or `UserSpecific` |
| `DefaultCaptainId` | string? | Optional preferred captain for missions of this persona (see [CAPTAIN_ROUTING.md](CAPTAIN_ROUTING.md)) |

**Source:** `src/Armada.Core/Models/Persona.cs`

### Fields on Existing Models

**Mission** (new fields):
- `Persona` (string?) -- Which persona this mission requires. Null defaults to Worker.
- `DependsOnMissionId` (string?) -- Mission ID this mission depends on. Cannot be assigned until dependency completes.
- `RequiresReview` / `ReviewDenyAction` -- Copied from the owning pipeline stage when the mission is created.
- `WaitForVoyageWorkers` (bool) -- Set by an Architect plan to defer a worker until the voyage's other workers settle.

**Captain** (new fields):
- `AllowedPersonas` (string?) -- JSON array of persona names this captain can fill. Null means any.
- `PreferredPersona` (string?) -- Soft routing preference for dispatch.

**Fleet / Vessel** (new field):
- `DefaultPipelineId` (string?) -- Default pipeline for dispatches to this fleet/vessel.

---

## 3. Built-in Pipelines and Personas

### Personas (seeded on startup by `PersonaSeedService`)

| Name | Template | Description |
|------|----------|-------------|
| Worker | `persona.worker` | Standard mission executor -- writes code, makes changes, commits |
| Architect | `persona.architect` | Plans work, decomposes goals into missions using `[ARMADA:MISSION]` markers |
| Product Manager | `persona.product_manager` | Shapes the product picture, clarifies user outcomes, turns work into durable requirements |
| Usability Engineer | `persona.usability_engineer` | Improves usability, edge-case experience, and consistency with the surrounding product |
| Test Engineer | `persona.test_engineer` | Writes tests for changes, follows existing test patterns |
| Linter | `persona.linter` | Evaluates changed code and documentation for style and correctness; fixes clear in-scope violations and flags the rest |
| Judge | `persona.judge` | Reviews diffs for correctness, completeness, scope, and style |
| Recorder | `persona.recorder` | Distills durable memories (episodic/semantic/procedural) from the voyage into the vessel context and memory store |

### Pipelines (seeded on startup by `PersonaSeedService`)

| Name | Stages | Description |
|------|--------|-------------|
| WorkerOnly | Worker | Backward-compatible default |
| Reviewed | Worker [review] -> Judge [review] | Implementation + review |
| Tested | Worker [review] -> Test Engineer [review] -> Judge [review] | Implementation + testing + review |
| FullPipeline | Product Manager [review] -> Architect [review] -> Worker [review] -> Usability Engineer [review] -> Test Engineer [review] -> Linter [review] -> Judge [review] -> Recorder | Full lifecycle: frame, plan, implement, refine, test, lint, review, and record durable memory |
| Recorded | Worker [review] -> Recorder | Do the work, then distill durable memories from it |

`[review]` marks a stage seeded with `RequiresReview = true` (a human review gate; see [Review Gates](#review-gates)). Judge stages are seeded with `ReviewDenyAction = FailPipeline`; every other gated stage uses the default `RetryStage`. WorkerOnly and the Recorder stages have no gate.

**Source:** `src/Armada.Core/Services/PersonaSeedService.cs`

---

## 4. Dispatch Flow

The complete lifecycle of a pipeline dispatch:

```
User calls dispatch(title, vesselId, missions, pipeline: "Reviewed")
    |
    v
AdmiralService.DispatchVoyageAsync(title, desc, vesselId, missions, pipelineId)
    |
    v
ResolvePipelineAsync(pipelineId, vessel)
    |  Checks: explicit param -> vessel default -> fleet default -> null
    |
    v
Is pipeline null or single-stage Worker?
    |-- YES --> Standard dispatch (one Worker mission per description)
    |-- NO  --> Multi-stage pipeline dispatch
                    |
                    v
                Create Voyage
                    |
                    v
                For each mission description:
                    For each pipeline stage (ordered):
                        Create Mission with:
                            - Title: "[{PersonaName}] {title}" (title truncated to 60 chars)
                            - Persona: stage.PersonaName
                            - DependsOnMissionId: previous stage's mission ID (null for first)
                            - RequiresReview / ReviewDenyAction: copied from the stage
                        If first stage: TryAssignAsync (may assign immediately)
                    |
                    v
                Update Voyage status (InProgress if any mission assigned)
                    |
                    v
                Return Voyage to caller
```

**Source:** `src/Armada.Core/Services/AdmiralService.cs`, method `DispatchVoyageAsync` (the pipelineId overload)

---

## 5. Pipeline Resolution

The Admiral resolves which pipeline to use in `ResolvePipelineAsync`. Resolution follows a strict precedence order -- **highest priority wins**:

| Priority | Source | How to Set |
|----------|--------|------------|
| 1 (highest) | **Explicit dispatch parameter** | `pipelineId` or `pipeline` on `dispatch` / voyage create |
| 2 | **Vessel default** | `DefaultPipelineId` on the target vessel |
| 3 | **Fleet default** | `DefaultPipelineId` on the vessel's parent fleet |
| 4 (lowest) | **System fallback** | WorkerOnly (no pipeline, standard single-mission behavior) |

```
1. If pipelineId is provided (explicit dispatch override):
   a. Try ReadAsync(pipelineId)      -- lookup by ID
   b. Try ReadByNameAsync(pipelineId) -- lookup by name (convenience)
   c. If found, use it (highest priority)

2. If vessel.DefaultPipelineId is set:
   a. Try ReadAsync(vessel.DefaultPipelineId)
   b. If found, use it
   c. If pipeline was deleted: clear the stale reference (set to null, update vessel), log warning

3. If vessel.FleetId is set and fleet.DefaultPipelineId is set:
   a. Try ReadAsync(fleet.DefaultPipelineId)
   b. If found, use it
   c. If pipeline was deleted: clear the stale reference (set to null, update fleet), log warning

4. Return null (falls back to WorkerOnly behavior)
```

**Stale reference handling:** If a vessel or fleet references a pipeline that has been deleted, the Admiral automatically clears the `DefaultPipelineId` to null, persists the update, and logs a warning. This prevents stale IDs from accumulating.

**Source:** `src/Armada.Core/Services/AdmiralService.cs`, method `ResolvePipelineAsync`

---

## 6. Mission Creation and Dependency Chain

For a 3-stage pipeline (Architect, Worker, Judge) dispatching one mission description "Add caching":

```
Mission 1: "[Architect] Add caching"
  persona: "Architect"
  dependsOnMissionId: null        <-- assigned immediately

Mission 2: "[Worker] Add caching"
  persona: "Worker"
  dependsOnMissionId: Mission 1   <-- waits for Architect

Mission 3: "[Judge] Add caching"
  persona: "Judge"
  dependsOnMissionId: Mission 2   <-- waits for Worker
```

If the dispatch includes multiple mission descriptions, each gets its own full pipeline chain:

```
Description A -> [Architect A] -> [Worker A] -> [Judge A]
Description B -> [Architect B] -> [Worker B] -> [Judge B]
```

All missions belong to the same voyage.

---

## 7. Assignment and Persona-Aware Captain Routing

### Dependency Gating

Before assigning a mission, `TryAssignAsync` checks:

```csharp
if (!String.IsNullOrEmpty(mission.DependsOnMissionId))
{
    Mission? dependency = await _Database.Missions.ReadAsync(mission.DependsOnMissionId);
    if (dependency == null) return false;
    if (dependency.Status != Complete && dependency.Status != WorkProduced)
        return false;  // Dependency not satisfied -- skip assignment
    if (dependency.Status == WorkProduced && !IsPipelineHandoffPrepared(mission, dependency))
        return false;  // Handoff (branch + context) not written yet
}
```

A dependency waiting in `Review` (a review gate) is neither `Complete` nor `WorkProduced`, so the next stage waits until the review is approved. This runs on every assignment attempt (including health check retries), so dependent missions are automatically picked up once their dependency completes.

**Source:** `src/Armada.Core/Services/MissionService.cs`, method `TryAssignAsync`

### Captain Selection

`FindAvailableCaptainAsync(persona, requiredTier, requestedCaptainId)` selects a captain for a mission:

```
1. Get all idle captains (none -> mission stays Pending)
2. If the mission has a preferred captain (RequestedCaptainId) and it is idle:
   return it, bypassing AllowedPersonas and tier routing
3. Filter by AllowedPersonas:
   - no persona on the mission = every idle captain is eligible
   - null AllowedPersonas = captain can fill any role
   - Non-null = check if persona is in the JSON array
4. If no eligible captains: return null (hard constraint -- the mission
   stays Pending; see "Why This Mission Is Waiting" on the mission page)
5. If the mission requires a tier: keep captains at or above it,
   preferring the lowest qualifying tier (none -> stays Pending)
6. Among eligible, prefer a PreferredPersona match
7. Otherwise return the first eligible captain
```

The preferred captain comes from the dispatch payload, the voyage's per-persona override, or the persona's `DefaultCaptainId`. See [CAPTAIN_ROUTING.md](CAPTAIN_ROUTING.md) and [SCHEDULING.md](SCHEDULING.md#why-a-mission-is-waiting).

**Source:** `src/Armada.Core/Services/MissionService.cs`, method `FindAvailableCaptainAsync`

---

## 8. Stage Handoff

### Review Gates

When a stage's mission finishes and its `RequiresReview` is true, the mission moves to `Review` instead of handing off. It waits there for a person to resolve it:

- **Dashboard / TUI:** the mission page's **Resolve Review** action (also reachable from **Needs You**). The dialog offers Approve, Conditionally Approve (continue, but the next stage must consider your feedback), More Work Required, and Deny; feedback is required for Conditionally Approve and More Work Required.
- **REST:** `POST /api/v1/missions/{id}/review/approve` and `POST /api/v1/missions/{id}/review/deny`.

Approving a non-terminal stage continues to the next pipeline stage; approving the last stage continues to landing. Denying applies the stage's `ReviewDenyAction` (or an action supplied with the deny): `RetryStage` sends the same stage back for rework, `FailPipeline` fails the stage and cancels its downstream stages. If nobody resolves a review within `ReviewTimeoutMinutes` (default 1440; 0 disables), the watchdog escalates and releases the held captain while preserving the mission and its dock for the reviewer.

### Handoff

When a mission reaches `WorkProduced` status (and is not waiting at a review gate), `TryHandoffToNextStageAsync` runs:

```
1. Find all missions in the same voyage that depend on this mission
   and are still in Pending status

2. For each dependent mission:
   a. Inject handoff context into the mission's description:
      - A persona-specific preamble for the next stage
      - Prior stage persona, title, ID
      - Branch name
      - The prior stage's agent output (first 8000 characters)
      - Diff snapshot (if available, rendered as a code block)
   b. Set the same branch name (next stage works on the same branch)
   c. Attempt to assign the mission (dependency check will now pass)
```

### Stage-lag branch hardening

Before handing off, the handoff force-advances the shared branch to the prior
stage's produced commit. A stage's dock can end on a **detached HEAD** -- its
commit lives at the dock's live `HEAD` but the shared branch ref still points at
the older commit. Because the next stage reuses that same branch, it would
otherwise check out stale code and miss the prior stage's work.

`HardenStageBranchAsync` resolves the completed stage's dock's live `HEAD`
(`IGitService.GetHeadCommitHashAsync`) and lifts it onto the branch ref via
`IGitService.ForceAdvanceBranchAsync` (a `git update-ref`, which -- unlike
`git branch -f` -- tolerates a branch that is checked out or detached in a
worktree sharing the repository). The step is best-effort: a git failure is
logged and the handoff continues. Architect fan-out does not use it, because the
worker missions it produces start on fresh branches.

The handoff context is appended to the mission description as a markdown section:

```markdown
---
## Prior Stage Output
The previous pipeline stage (Worker) completed mission "[Worker] Add caching" (msn_abc123).
Branch: armada/captain-1/msn_abc123

### Agent Output (from Worker stage)
...

### Diff from prior stage
```diff
+public class CacheService { ... }
```
```

**Source:** `src/Armada.Core/Services/MissionService.cs`, method `TryHandoffToNextStageAsync`

---

## 9. Architect Special Handling

The Architect persona is special: instead of passing context to the next stage, its output can define multiple Worker missions.

### Output Format

The Architect agent is instructed (via the `persona.architect` template) to output mission definitions. The preferred format is a fenced code block whose info string is `armada-plan`, containing JSON:

````
```armada-plan
{"missions":[
  {"title":"Add CacheService with TTL support","description":"Implement CacheService ...","dependsOn":null,"waitForOtherMissions":false},
  {"title":"Add caching middleware to GET endpoints","description":"Add CacheMiddleware ...","dependsOn":1,"waitForOtherMissions":false}
]}
```
````

`dependsOn` is the 1-based number of an earlier mission in the plan whose whole Worker chain must finish before this one starts (null when it can start immediately), and `waitForOtherMissions` defers the mission until the voyage's other workers settle. When the output has several plan blocks, the last well-formed one wins; missions without a title, with a placeholder title such as `<title>`, or with a repeated title are dropped.

When there is no valid plan block, Armada falls back to the legacy `[ARMADA:MISSION]` markers (optionally closed with `[/ARMADA:MISSION]`, with a `Depends on:` line for sequencing), and then to numbered summary lines:

```
[ARMADA:MISSION] Add CacheService with TTL support
Implement CacheService in src/Services/CacheService.cs with Get, Set, Remove methods.
Files: src/Services/CacheService.cs, src/Services/Interfaces/ICacheService.cs

[ARMADA:MISSION] Add caching middleware to GET endpoints
Add CacheMiddleware that checks the cache before executing the handler.
Files: src/Middleware/CacheMiddleware.cs, src/Startup.cs
```

### Parsing

`ParseArchitectOutput` first looks for an `armada-plan` block in the architect mission's `AgentOutput`. Without one, it searches `AgentOutput`, then `DiffSnapshot`, then `Description` for `[ARMADA:MISSION]` markers (then numbered summary lines). Marker blocks split on the marker; the first line is the title and the remaining lines are the description.

**Source:** `src/Armada.Core/Services/MissionService.cs`, method `ParseArchitectOutput`

### Mission Fan-Out

When an Architect stage completes with parseable mission definitions:

```
1. Parse the plan into N mission definitions
2. Update the existing Worker mission (next stage) with the first definition
   (title "{definition title} [Worker]") and retitle its downstream chain
3. Create N-1 additional Worker missions for the remaining definitions
4. For each additional Worker:
   - Clone the whole post-Worker chain (in FullPipeline: Usability Engineer, Test Engineer,
     Linter, Judge, Recorder) as new missions, copying each stage's review gate
   - Chain dependencies: additional Worker depends on Architect,
     cloned stages depend on their Worker
5. Apply the plan's dependsOn / waitForOtherMissions sequencing
6. Skip normal handoff (return early); the Workers are picked up by normal assignment
```

Result for an Architect that produces 2 mission definitions in a FullPipeline:

```
[Architect] (completed)
  |
  +-- [Worker 1] "Add CacheService" --> [Usability Engineer 1] --> [Test Engineer 1] --> [Linter 1] --> [Judge 1] --> [Recorder 1]
  |
  +-- [Worker 2] "Add middleware"    --> [Usability Engineer 2] --> [Test Engineer 2] --> [Linter 2] --> [Judge 2] --> [Recorder 2]
```

If the Architect produces no valid mission definitions, the Architect mission is marked `Failed` (failure kind `InvalidOutput`, reason "Architect produced no [ARMADA:MISSION] markers in output" or "...no valid [ARMADA:MISSION] definitions...") rather than handing its raw output to the Worker.

**Source:** `src/Armada.Core/Services/MissionService.cs`, within `TryHandoffToNextStageAsync`

---

## 10. Prompt Template Resolution

Each persona references a prompt template by name. When `GenerateClaudeMdAsync` builds the mission instructions file (`CLAUDE.md`, `CODEX.md`, `CURSOR.md`, `GEMINI.md`, or `MUX.md`, depending on the captain's runtime):

```
1. Build template parameter dictionary from mission/vessel/captain context
2. Resolve persona prompt: the persona's PromptTemplateName (looked up by the
   mission's persona name, tenant first). When the persona is not found, or its
   template does not exist (logged as a warning), use the conventional name
   "persona.{persona name in snake_case}" (Worker -> persona.worker,
   Test Engineer -> persona.test_engineer, SecurityAuditor -> persona.security_auditor).
   A project profile's persona override can swap in a different template and
   append instructions; it wins over both.
   - IPromptTemplateService.RenderAsync checks DB first, then embedded defaults
3. Resolve each section (rules, context conservation, etc.) via ResolveSectionAsync
4. Fallback: GetHardcodedFallback returns the original inline strings
   (backward compatibility when template service is unavailable)
```

Template resolution order:
```
Database (user customization) -> Embedded Default (shipped with code) -> Hardcoded Fallback
```

Mission prompts use the template named by the persona's `PromptTemplateName`, so a custom persona can point at any template. The conventional `persona.{snake_case}` name is the fallback when that template is missing.

All built-in templates (27 in 1.0.0) are seeded into the database on startup via `PromptTemplateService.SeedDefaultsAsync()`. Users can edit them via dashboard, MCP, or REST without touching code.

**Source:** `src/Armada.Core/Services/MissionService.cs`, `src/Armada.Core/Services/PromptTemplateService.cs`

---

## 11. Database Schema

### New Tables (Migrations 19-23)

```sql
-- Migration 19: Prompt templates
CREATE TABLE prompt_templates (
    id TEXT PRIMARY KEY, tenant_id TEXT, name TEXT NOT NULL,
    description TEXT, category TEXT NOT NULL DEFAULT 'mission',
    content TEXT NOT NULL, is_built_in INTEGER NOT NULL DEFAULT 0,
    active INTEGER NOT NULL DEFAULT 1, created_utc TEXT NOT NULL,
    last_update_utc TEXT NOT NULL
);
CREATE UNIQUE INDEX idx_prompt_templates_tenant_name ON prompt_templates(tenant_id, name);

-- Migration 20: Personas
CREATE TABLE personas (
    id TEXT PRIMARY KEY, tenant_id TEXT, name TEXT NOT NULL,
    description TEXT, prompt_template_name TEXT NOT NULL,
    is_built_in INTEGER NOT NULL DEFAULT 0, active INTEGER NOT NULL DEFAULT 1,
    created_utc TEXT NOT NULL, last_update_utc TEXT NOT NULL
);
CREATE UNIQUE INDEX idx_personas_tenant_name ON personas(tenant_id, name);

-- Migration 21: Captain persona fields
ALTER TABLE captains ADD COLUMN allowed_personas TEXT;
ALTER TABLE captains ADD COLUMN preferred_persona TEXT;

-- Migration 22: Mission persona and dependency fields
ALTER TABLE missions ADD COLUMN persona TEXT;
ALTER TABLE missions ADD COLUMN depends_on_mission_id TEXT;

-- Migration 23: Pipelines, stages, and fleet/vessel defaults
CREATE TABLE pipelines (
    id TEXT PRIMARY KEY, tenant_id TEXT, name TEXT NOT NULL,
    description TEXT, is_built_in INTEGER NOT NULL DEFAULT 0,
    active INTEGER NOT NULL DEFAULT 1, created_utc TEXT NOT NULL,
    last_update_utc TEXT NOT NULL
);
CREATE TABLE pipeline_stages (
    id TEXT PRIMARY KEY, pipeline_id TEXT NOT NULL,
    stage_order INTEGER NOT NULL, persona_name TEXT NOT NULL,
    is_optional INTEGER NOT NULL DEFAULT 0, description TEXT,
    FOREIGN KEY (pipeline_id) REFERENCES pipelines(id) ON DELETE CASCADE
);
ALTER TABLE fleets ADD COLUMN default_pipeline_id TEXT;
ALTER TABLE vessels ADD COLUMN default_pipeline_id TEXT;
```

Later migrations added review gates (SQLite migration 32: `requires_review` and `review_deny_action` on `pipeline_stages` and `missions`, plus the mission review columns), `personas.default_captain_id`, and a `scope` column on personas, pipelines, and prompt templates.

All migrations are implemented for SQLite, MySQL, PostgreSQL, and SQL Server. Standalone migration scripts are in `migrations/`.

### Indexes

| Index | Table | Columns | Type |
|-------|-------|---------|------|
| `idx_prompt_templates_tenant_name` | prompt_templates | tenant_id, name | UNIQUE |
| `idx_prompt_templates_category` | prompt_templates | category | |
| `idx_personas_tenant_name` | personas | tenant_id, name | UNIQUE |
| `idx_personas_prompt_template` | personas | prompt_template_name | |
| `idx_captains_preferred_persona` | captains | preferred_persona | |
| `idx_missions_persona` | missions | persona | |
| `idx_missions_depends_on` | missions | depends_on_mission_id | |
| `idx_pipelines_tenant_name` | pipelines | tenant_id, name | UNIQUE |
| `idx_pipeline_stages_pipeline` | pipeline_stages | pipeline_id | |
| `idx_pipeline_stages_order` | pipeline_stages | pipeline_id, stage_order | UNIQUE |
| `idx_pipeline_stages_persona` | pipeline_stages | persona_name | |
| `idx_fleets_default_pipeline` | fleets | default_pipeline_id | |
| `idx_vessels_default_pipeline` | vessels | default_pipeline_id | |

---

## 12. API Surface

### MCP Tools

| Tool | Description |
|------|-------------|
| `create_persona` | Create a custom persona |
| `get_persona` | Get persona by name |
| `update_persona` | Update persona properties |
| `delete_persona` | Delete (blocked for built-in) |
| `create_pipeline` | Create a pipeline with stages |
| `get_pipeline` | Get pipeline by name (includes stages) |
| `update_pipeline` | Update pipeline and stages |
| `delete_pipeline` | Delete (blocked for built-in) |
| `list_prompt_templates` | List templates, optionally by category |
| `create_prompt_template` | Create a template |
| `get_prompt_template` | Get template by name |
| `update_prompt_template` | Update template content (creates the template if it does not exist) |
| `reset_prompt_template` | Reset to built-in default |
| `dispatch` | Now accepts `pipelineId` and `pipeline` params |
| `enumerate` | Now supports `personas`, `prompt_templates`, `pipelines` entity types |

The MCP `create_pipeline` and `update_pipeline` stage objects accept `personaName`, `isOptional`, and `description` only; set `requiresReview` and `reviewDenyAction` through the REST API, the dashboard, or the TUI.

### REST Endpoints

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/v1/personas` | List personas |
| POST | `/api/v1/personas/enumerate` | Paginated persona list |
| POST | `/api/v1/personas` | Create persona |
| GET | `/api/v1/personas/{name}` | Get by name |
| PUT | `/api/v1/personas/{name}` | Update |
| DELETE | `/api/v1/personas/{name}` | Delete |
| GET | `/api/v1/pipelines` | List pipelines |
| POST | `/api/v1/pipelines/enumerate` | Paginated pipeline list |
| POST | `/api/v1/pipelines` | Create pipeline |
| GET | `/api/v1/pipelines/{name}` | Get by name |
| PUT | `/api/v1/pipelines/{name}` | Update |
| DELETE | `/api/v1/pipelines/{name}` | Delete |
| GET | `/api/v1/prompt-templates` | List templates |
| POST | `/api/v1/prompt-templates/enumerate` | Paginated template list |
| POST | `/api/v1/prompt-templates` | Create template |
| GET | `/api/v1/prompt-templates/{name}` | Get by name |
| PUT | `/api/v1/prompt-templates/{name}` | Update content |
| POST | `/api/v1/prompt-templates/{name}/reset` | Reset to default |
| POST | `/api/v1/voyages` | Now accepts `pipelineId` and `pipeline` |
| POST | `/api/v1/missions/{id}/review/approve` | Approve a mission waiting at a review gate |
| POST | `/api/v1/missions/{id}/review/deny` | Deny a mission waiting at a review gate |

### WebSocket Commands

`get_persona`, `create_persona`, `update_persona`, `delete_persona`,
`get_prompt_template`, `update_prompt_template`,
`get_pipeline`, `create_pipeline`, `update_pipeline`, `delete_pipeline`

---

## 13. Dashboard Integration

### New Pages

- **Personas** (**Configuration > Personas**, `/configuration?tab=personas`; detail at `/personas/{name}`) -- List, create, edit, delete. Prompt template name is a dropdown.
- **Pipelines** (**Configuration > Pipelines**, `/configuration?tab=pipelines`; detail at `/pipelines/{name}`) -- List, create, edit with up/down stage reordering. Persona name is a dropdown, and each stage has review-gate and deny-action settings; the stage chain shows `[review]` on gated stages.
- **Prompt Templates** (**Configuration > Prompts**, `/configuration?tab=prompts`) -- List with a category filter. Click to open the two-column editor.
- **Prompt Template Editor** (`/prompt-templates/{name}`) -- Monospace editor + parameter reference panel with click-to-insert.

### Updated Pages

- **Vessel Detail** -- `Default Pipeline` dropdown (not a text field)
- **Fleet Detail** -- `Default Pipeline` dropdown
- **Captain Detail** -- `Allowed Personas` and `Preferred Persona` fields
- **Mission Detail** -- `Persona` field, `Depends On` link to dependency mission, **Resolve Review** for a mission waiting at a review gate
- **Dispatch** -- Pipeline dropdown showing stage names
- **Voyage Create** -- Pipeline dropdown

### Navigation

Personas, Pipelines, and Prompts are tabs of the **Configuration** hub (sidebar section **CONFIGURATION**). The old `/personas`, `/pipelines`, and `/prompt-templates` URLs redirect to those tabs. The TUI uses the same routes.

---

## 14. Configuration Patterns

### Per-Vessel Default (Most Common)

Set a default pipeline on a vessel so all dispatches to it use a specific workflow:

```jsonc
// update_vessel
{ "vesselId": "vsl_abc", "defaultPipelineId": "ppl_reviewed" }
```

### Per-Fleet Default

Set at the fleet level -- applies to all vessels in the fleet unless overridden:

```jsonc
// update_fleet
{ "fleetId": "flt_abc", "defaultPipelineId": "ppl_tested" }
```

### Per-Dispatch Override

Override for a single voyage regardless of vessel/fleet defaults:

```jsonc
// dispatch
{ "title": "Quick fix", "vesselId": "vsl_abc", "pipeline": "WorkerOnly", "missions": [...] }
```

### Dedicated Captains

Assign captains to specific roles:

```jsonc
// Opus captain for planning and review
// update_captain
{ "captainId": "cpt_opus", "preferredPersona": "Architect", "allowedPersonas": "[\"Architect\",\"Judge\"]" }

// Sonnet captains for implementation and testing
// update_captain
{ "captainId": "cpt_sonnet", "allowedPersonas": "[\"Worker\",\"Test Engineer\"]" }
```

---

## 15. Extensibility

### Creating a Custom Persona

1. **Create a prompt template** with instructions for the new role:
   ```jsonc
   // update_prompt_template
   { "name": "persona.security_auditor", "content": "You are a security auditor...", "description": "Security review" }
   ```

2. **Create the persona** referencing the template:
   ```jsonc
   // create_persona
   { "name": "SecurityAuditor", "promptTemplateName": "persona.security_auditor" }
   ```

3. **Create a pipeline** using the persona:
   ```jsonc
   // create_pipeline
   {
     "name": "SecureReview",
     "stages": [
       { "personaName": "Worker" },
       { "personaName": "SecurityAuditor" },
       { "personaName": "Judge" }
     ]
   }
   ```

4. **Dispatch** with the pipeline:
   ```jsonc
   // dispatch
   { "title": "Add login", "vesselId": "vsl_abc", "pipeline": "SecureReview", "missions": [...] }
   ```

### Key Files

| File | Purpose |
|------|---------|
| `src/Armada.Core/Models/Pipeline.cs` | Pipeline model |
| `src/Armada.Core/Models/PipelineStage.cs` | Stage model |
| `src/Armada.Core/Models/Persona.cs` | Persona model |
| `src/Armada.Core/Models/PromptTemplate.cs` | Template model |
| `src/Armada.Core/Services/AdmiralService.cs` | Dispatch + pipeline resolution |
| `src/Armada.Core/Services/MissionService.cs` | Assignment, handoff, architect parsing, captain routing |
| `src/Armada.Core/Services/PromptTemplateService.cs` | Template resolution + 27 embedded defaults |
| `src/Armada.Core/Services/MissionPromptBuilder.cs` | Persona template naming and prompt assembly |
| `src/Armada.Core/Services/PersonaSeedService.cs` | Startup seeding of personas + pipelines |
| `src/Armada.Server/Mcp/Tools/McpPersonaTools.cs` | Persona MCP tools |
| `src/Armada.Server/Mcp/Tools/McpPipelineTools.cs` | Pipeline MCP tools |
| `src/Armada.Server/Mcp/Tools/McpPromptTemplateTools.cs` | Template MCP tools |
| `src/Armada.Server/Routes/PersonaRoutes.cs` | Persona REST endpoints |
| `src/Armada.Server/Routes/PipelineRoutes.cs` | Pipeline REST endpoints |
| `src/Armada.Server/Routes/PromptTemplateRoutes.cs` | Template REST endpoints |
| `src/Armada.Dashboard/src/pages/Pipelines.tsx` | Pipeline dashboard page |
| `src/Armada.Dashboard/src/pages/Personas.tsx` | Persona dashboard page |
| `src/Armada.Dashboard/src/pages/PromptTemplates.tsx` | Template list page |
| `src/Armada.Dashboard/src/pages/PromptTemplateDetail.tsx` | Template editor page |
| `docs/PERSONAS.md` | Original implementation record |
| `docs/PERSONAS_GUIDE.md` | User-facing guide |
| `docs/TESTING_PIPELINES.md` | End-to-end test examples |
