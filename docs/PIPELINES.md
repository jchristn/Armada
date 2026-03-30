# Pipelines -- Implementation Reference

This document covers the complete pipeline implementation in Armada v0.4.0: data model, dispatch flow, execution lifecycle, stage handoff, architect special handling, captain routing, and extensibility.

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

A **pipeline** is an ordered sequence of **persona stages** that a dispatch goes through. Without pipelines, every dispatch creates Worker missions that run independently. With pipelines, a single dispatch can flow through planning (Architect), implementation (Worker), testing (TestEngineer), and review (Judge) stages automatically.

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
| `IsOptional` | bool | If true, the Admiral may skip this stage |
| `Description` | string? | What this stage does |

**Source:** `src/Armada.Core/Models/PipelineStage.cs`

### Persona

| Field | Type | Description |
|-------|------|-------------|
| `Id` | string (`prs_` prefix) | Unique identifier |
| `Name` | string | Unique persona name |
| `Description` | string? | What this persona does |
| `PromptTemplateName` | string | References a prompt template by name |
| `IsBuiltIn` | bool | True for system-shipped personas |

**Source:** `src/Armada.Core/Models/Persona.cs`

### Fields on Existing Models

**Mission** (new fields):
- `Persona` (string?) -- Which persona this mission requires. Null defaults to Worker.
- `DependsOnMissionId` (string?) -- Mission ID this mission depends on. Cannot be assigned until dependency completes.

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
| Judge | `persona.judge` | Reviews diffs for correctness, completeness, scope, and style |
| TestEngineer | `persona.test_engineer` | Writes tests for changes, follows existing test patterns |

### Pipelines (seeded on startup by `PersonaSeedService`)

| Name | Stages | Description |
|------|--------|-------------|
| WorkerOnly | Worker | Backward-compatible default |
| Reviewed | Worker -> Judge | Implementation + review |
| Tested | Worker -> TestEngineer -> Judge | Implementation + testing + review |
| FullPipeline | Architect -> Worker -> TestEngineer -> Judge | Planning + implementation + testing + review |

**Source:** `src/Armada.Core/Services/PersonaSeedService.cs`

---

## 4. Dispatch Flow

The complete lifecycle of a pipeline dispatch:

```
User calls armada_dispatch(title, vesselId, missions, pipeline: "Reviewed")
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
    |-- YES --> Standard dispatch (existing behavior, no persona fields)
    |-- NO  --> Multi-stage pipeline dispatch
                    |
                    v
                Create Voyage
                    |
                    v
                For each mission description:
                    For each pipeline stage (ordered):
                        Create Mission with:
                            - Title: "{original title} [{PersonaName}]"
                            - Persona: stage.PersonaName
                            - DependsOnMissionId: previous stage's mission ID (null for first)
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
| 1 (highest) | **Explicit dispatch parameter** | `pipelineId` or `pipeline` on `armada_dispatch` / voyage create |
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
Mission 1: "Add caching [Architect]"
  persona: "Architect"
  dependsOnMissionId: null        <-- assigned immediately

Mission 2: "Add caching [Worker]"
  persona: "Worker"
  dependsOnMissionId: Mission 1   <-- waits for Architect

Mission 3: "Add caching [Judge]"
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
    if (dependency.Status != Complete && dependency.Status != WorkProduced)
        return false;  // Dependency not satisfied -- skip assignment
}
```

This runs on every assignment attempt (including health check retries), so dependent missions are automatically picked up once their dependency completes.

**Source:** `src/Armada.Core/Services/MissionService.cs`, method `TryAssignAsync`

### Captain Selection

`FindAvailableCaptainAsync(persona)` selects a captain for a mission:

```
1. Get all idle captains
2. If no persona requirement: return first idle captain
3. Filter by AllowedPersonas:
   - null AllowedPersonas = captain can fill any role
   - Non-null = check if persona is in the JSON array
4. If no eligible captains: fall back to any idle captain
   (soft constraint -- work still gets done)
5. Among eligible, prefer PreferredPersona match
6. Return best match
```

**Source:** `src/Armada.Core/Services/MissionService.cs`, method `FindAvailableCaptainAsync`

---

## 8. Stage Handoff

When a mission reaches `WorkProduced` status, `TryHandoffToNextStageAsync` runs:

```
1. Find all missions in the same voyage that depend on this mission
   and are still in Pending status

2. For each dependent mission:
   a. Inject handoff context into the mission's description:
      - Prior stage persona, title, ID
      - Branch name
      - Diff snapshot (if available, rendered as a code block)
   b. Set the same branch name (next stage works on the same branch)
   c. Attempt to assign the mission (dependency check will now pass)
```

The handoff context is appended to the mission description as a markdown section:

```markdown
---
## Prior Stage Output
The previous pipeline stage (Worker) completed mission "Add caching" (msn_abc123).
Branch: armada/captain-1/msn_abc123

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

The Architect agent is instructed (via the `persona.architect` template) to output mission definitions using markers:

```
[ARMADA:MISSION] Add CacheService with TTL support
Implement CacheService in src/Services/CacheService.cs with Get, Set, Remove methods.
Files: src/Services/CacheService.cs, src/Services/Interfaces/ICacheService.cs

[ARMADA:MISSION] Add caching middleware to GET endpoints
Add CacheMiddleware that checks the cache before executing the handler.
Files: src/Middleware/CacheMiddleware.cs, src/Startup.cs
```

### Parsing

`ParseArchitectOutput` searches the architect mission's `DiffSnapshot` first, then `Description`, for `[ARMADA:MISSION]` markers. It splits on the marker, extracts the title (first line) and description (remaining lines).

**Source:** `src/Armada.Core/Services/MissionService.cs`, method `ParseArchitectOutput`

### Mission Fan-Out

When an Architect stage completes with parseable markers:

```
1. Parse [ARMADA:MISSION] markers into N mission definitions
2. Update the existing Worker mission (next stage) with the first definition
3. Create N-1 additional Worker missions for the remaining definitions
4. For each additional Worker:
   - Clone the post-Worker stages (TestEngineer, Judge) as new missions
   - Chain dependencies: additional Worker depends on Architect,
     cloned stages depend on their Worker
5. Assign the first Worker mission
6. Skip normal handoff (return early)
```

Result for an Architect that produces 2 mission definitions in a FullPipeline:

```
[Architect] (completed)
  |
  +-- [Worker 1] "Add CacheService" --> [TestEngineer 1] --> [Judge 1]
  |
  +-- [Worker 2] "Add middleware"    --> [TestEngineer 2] --> [Judge 2]
```

If no `[ARMADA:MISSION]` markers are found, the Architect's output falls through to normal stage handoff (context injection).

**Source:** `src/Armada.Core/Services/MissionService.cs`, within `TryHandoffToNextStageAsync`

---

## 10. Prompt Template Resolution

Each persona references a prompt template by name. When `GenerateClaudeMdAsync` builds the CLAUDE.md for a mission:

```
1. Build template parameter dictionary from mission/vessel/captain context
2. Resolve persona prompt: "persona.{mission.Persona.ToLower()}"
   - IPromptTemplateService.RenderAsync checks DB first, then embedded defaults
3. Resolve each section (rules, context conservation, etc.) via ResolveSectionAsync
4. Fallback: GetHardcodedFallback returns the original inline strings
   (backward compatibility when template service is unavailable)
```

Template resolution order:
```
Database (user customization) -> Embedded Default (shipped with code) -> Hardcoded Fallback
```

All 18 built-in templates are seeded into the database on startup via `PromptTemplateService.SeedDefaultsAsync()`. Users can edit them via dashboard, MCP, or REST without touching code.

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
| `armada_create_persona` | Create a custom persona |
| `armada_get_persona` | Get persona by name |
| `armada_update_persona` | Update persona properties |
| `armada_delete_persona` | Delete (blocked for built-in) |
| `armada_create_pipeline` | Create a pipeline with stages |
| `armada_get_pipeline` | Get pipeline by name (includes stages) |
| `armada_update_pipeline` | Update pipeline and stages |
| `armada_delete_pipeline` | Delete (blocked for built-in) |
| `armada_get_prompt_template` | Get template by name |
| `armada_update_prompt_template` | Update template content |
| `armada_reset_prompt_template` | Reset to built-in default |
| `armada_dispatch` | Now accepts `pipelineId` and `pipeline` params |
| `armada_enumerate` | Now supports `personas`, `prompt_templates`, `pipelines` entity types |

### REST Endpoints

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/v1/personas` | List personas |
| POST | `/api/v1/personas` | Create persona |
| GET | `/api/v1/personas/{name}` | Get by name |
| PUT | `/api/v1/personas/{name}` | Update |
| DELETE | `/api/v1/personas/{name}` | Delete |
| GET | `/api/v1/pipelines` | List pipelines |
| POST | `/api/v1/pipelines` | Create pipeline |
| GET | `/api/v1/pipelines/{name}` | Get by name |
| PUT | `/api/v1/pipelines/{name}` | Update |
| DELETE | `/api/v1/pipelines/{name}` | Delete |
| GET | `/api/v1/prompt-templates` | List templates |
| GET | `/api/v1/prompt-templates/{name}` | Get by name |
| PUT | `/api/v1/prompt-templates/{name}` | Update content |
| POST | `/api/v1/prompt-templates/{name}/reset` | Reset to default |
| POST | `/api/v1/voyages` | Now accepts `pipelineId` and `pipeline` |

### WebSocket Commands

`get_persona`, `create_persona`, `update_persona`, `delete_persona`,
`get_prompt_template`, `update_prompt_template`,
`get_pipeline`, `create_pipeline`, `update_pipeline`, `delete_pipeline`

---

## 13. Dashboard Integration

### New Pages

- **Personas** (`/personas`) -- List, create, edit, delete. Prompt template name is a dropdown.
- **Pipelines** (`/pipelines`) -- List, create, edit with drag-to-reorder stages. Persona name is a dropdown.
- **Prompt Templates** (`/prompt-templates`) -- List with category tab bar. Click to open two-column editor.
- **Prompt Template Editor** (`/prompt-templates/{name}`) -- Monospace editor + parameter reference panel with click-to-insert.

### Updated Pages

- **Vessel Detail** -- `Default Pipeline` dropdown (not a text field)
- **Fleet Detail** -- `Default Pipeline` dropdown
- **Captain Detail** -- `Allowed Personas` and `Preferred Persona` fields
- **Mission Detail** -- `Persona` field, `Depends On` link to dependency mission
- **Dispatch** -- Pipeline dropdown showing stage names
- **Voyage Create** -- Pipeline dropdown

### Navigation

Personas, Pipelines, and Templates are in the **System** section of the sidebar.

---

## 14. Configuration Patterns

### Per-Vessel Default (Most Common)

Set a default pipeline on a vessel so all dispatches to it use a specific workflow:

```json
// armada_update_vessel
{ "vesselId": "vsl_abc", "defaultPipelineId": "ppl_reviewed" }
```

### Per-Fleet Default

Set at the fleet level -- applies to all vessels in the fleet unless overridden:

```json
// armada_update_fleet
{ "fleetId": "flt_abc", "defaultPipelineId": "ppl_tested" }
```

### Per-Dispatch Override

Override for a single voyage regardless of vessel/fleet defaults:

```json
// armada_dispatch
{ "title": "Quick fix", "vesselId": "vsl_abc", "pipeline": "WorkerOnly", "missions": [...] }
```

### Dedicated Captains

Assign captains to specific roles:

```json
// Opus captain for planning and review
// armada_update_captain
{ "captainId": "cpt_opus", "preferredPersona": "Architect", "allowedPersonas": "[\"Architect\",\"Judge\"]" }

// Sonnet captains for implementation and testing
// armada_update_captain
{ "captainId": "cpt_sonnet", "allowedPersonas": "[\"Worker\",\"TestEngineer\"]" }
```

---

## 15. Extensibility

### Creating a Custom Persona

1. **Create a prompt template** with instructions for the new role:
   ```json
   // armada_update_prompt_template
   { "name": "persona.security_auditor", "content": "You are a security auditor...", "description": "Security review" }
   ```

2. **Create the persona** referencing the template:
   ```json
   // armada_create_persona
   { "name": "SecurityAuditor", "promptTemplateName": "persona.security_auditor" }
   ```

3. **Create a pipeline** using the persona:
   ```json
   // armada_create_pipeline
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
   ```json
   // armada_dispatch
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
| `src/Armada.Core/Services/PromptTemplateService.cs` | Template resolution + 18 embedded defaults |
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
| `docs/PERSONAS.md` | Implementation plan with progress tracking |
| `docs/PERSONAS_GUIDE.md` | User-facing guide |
| `docs/TESTING_PIPELINES.md` | End-to-end test examples |
