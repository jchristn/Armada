# Personas and Prompt Templates -- Implementation Plan

This plan covers two interrelated features:

1. **Personas** -- Named agent roles (Architect, Worker, Judge, TestEngineer, etc.) that define what a captain does during a mission, with user-extensible persona definitions and per-captain capability constraints.
2. **Prompt Templates** -- Extracting all hardcoded prompts from C# code into user-editable, database-stored templates with embedded resource defaults as fallbacks.

Design choices:
- **Option B**: Persona is a property of the task/mission, not the captain. Any captain can fill any role.
- **Option 3**: Ship embedded resource defaults, allow database-stored overrides.
- **Level 2**: Pipeline configured at fleet/vessel level, overridable per dispatch.

---

## Table of Contents

- [Phase 1: Prompt Template Infrastructure](#phase-1-prompt-template-infrastructure)
- [Phase 2: Persona Model and Storage](#phase-2-persona-model-and-storage)
- [Phase 3: Pipeline Configuration](#phase-3-pipeline-configuration)
- [Phase 4: Admiral Dispatch Integration](#phase-4-admiral-dispatch-integration)
- [Phase 5: Built-in Persona Prompt Templates](#phase-5-built-in-persona-prompt-templates)
- [Phase 6: Dashboard UI](#phase-6-dashboard-ui)
- [Phase 7: MCP and REST API](#phase-7-mcp-and-rest-api)
- [Phase 8: Tests](#phase-8-tests)
- [Phase 9: Documentation](#phase-9-documentation)
- [Appendix A: Migration Summary](#appendix-a-migration-summary)
- [Appendix B: Hardcoded Prompts Inventory](#appendix-b-hardcoded-prompts-inventory)
- [Appendix C: Template Placeholder Reference](#appendix-c-template-placeholder-reference)

---

## Phase 1: Prompt Template Infrastructure

Extract all hardcoded prompts into a template system with embedded defaults and database overrides.

### 1.1 Prompt Template Model

- [x] Create `src/Armada.Core/Models/PromptTemplate.cs`
  - `Id` (string, `ptpl_` prefix)
  - `TenantId` (string?)
  - `Name` (string, unique key, e.g. `"mission.rules"`, `"mission.context_conservation"`, `"persona.worker"`)
  - `Description` (string?, human-readable purpose)
  - `Category` (string, e.g. `"mission"`, `"persona"`, `"commit"`, `"landing"`)
  - `Content` (string, the template body with `{Placeholder}` parameters)
  - `IsBuiltIn` (bool, true for system defaults -- cannot be deleted, only overridden)
  - `Active` (bool)
  - `CreatedUtc`, `LastUpdateUtc`

### 1.2 Database Schema: `prompt_templates` Table

**DDL (SQLite reference -- adapt types per driver):**

```sql
CREATE TABLE IF NOT EXISTS prompt_templates (
    id              TEXT PRIMARY KEY,
    tenant_id       TEXT,
    name            TEXT NOT NULL,
    description     TEXT,
    category        TEXT NOT NULL DEFAULT 'mission',
    content         TEXT NOT NULL,            -- SQLite: TEXT, MySQL: LONGTEXT, PG: TEXT, MSSQL: NVARCHAR(MAX)
    is_built_in     INTEGER NOT NULL DEFAULT 0,
    active          INTEGER NOT NULL DEFAULT 1,
    created_utc     TEXT NOT NULL,
    last_update_utc TEXT NOT NULL,
    FOREIGN KEY (tenant_id) REFERENCES tenants(id)
);
```

**Indexes:**

```sql
CREATE UNIQUE INDEX idx_prompt_templates_tenant_name ON prompt_templates(tenant_id, name);
CREATE INDEX idx_prompt_templates_category ON prompt_templates(category);
CREATE INDEX idx_prompt_templates_active ON prompt_templates(active);
```

**Implementation checklist:**

- [x] Create `IPromptTemplateMethods` interface in `src/Armada.Core/Database/Interfaces/`
  - `CreateAsync`, `ReadAsync`, `ReadByNameAsync`, `UpdateAsync`, `DeleteAsync`, `ExistsAsync`, `AllAsync`
- [x] Implement for SQLite in `src/Armada.Core/Database/Sqlite/Implementations/PromptTemplateMethods.cs`
- [x] Implement for MySQL in `src/Armada.Core/Database/Mysql/Implementations/PromptTemplateMethods.cs`
  - Use `LONGTEXT` for `content` column
- [x] Implement for PostgreSQL in `src/Armada.Core/Database/Postgresql/Implementations/PromptTemplateMethods.cs`
  - Use `TEXT` for `content` column
- [x] Implement for SQL Server in `src/Armada.Core/Database/SqlServer/Implementations/PromptTemplateMethods.cs`
  - Use `NVARCHAR(MAX)` for `content` column
- [x] Add in-code `SchemaMigration` entry (next sequence number after current max) in each driver's `TableQueries.cs`:
  - SQLite: `src/Armada.Core/Database/Sqlite/Queries/TableQueries.cs` (currently at migration 18)
  - MySQL: `src/Armada.Core/Database/Mysql/Queries/TableQueries.cs`
  - PostgreSQL: `src/Armada.Core/Database/Postgresql/Queries/TableQueries.cs`
  - SQL Server: `src/Armada.Core/Database/SqlServer/Queries/TableQueries.cs`
  - Each migration must include CREATE TABLE + all CREATE INDEX statements
- [x] Add the table to the initial schema DDL in each driver's `TableQueries.cs` (for fresh installs)
- [x] Create migration scripts in `migrations/`:
  - `migrations/migrate_add_prompt_templates.sh`
  - `migrations/migrate_add_prompt_templates.bat`
  - Follow existing pattern (sqlite3 check, settings file, backup, idempotent column check, SQL execution)
  - Scripts must include table creation + all indexes
- [x] Wire into `DatabaseDriver` as `PromptTemplates` property

### 1.3 Embedded Resource Defaults

Embedded defaults are stored inline in `PromptTemplateService.cs` constructor rather than as separate resource files.
This avoids the .csproj embedded resource complexity and keeps templates co-located with the service that uses them.

- [x] Define all built-in template content in `PromptTemplateService._EmbeddedDefaults` dictionary
  - `mission.rules` -- worktree rules, ASCII-only, exit codes
  - `mission.context_conservation` -- critical context window rules
  - `mission.merge_conflict_avoidance` -- multi-captain conflict rules
  - `mission.progress_signals` -- ARMADA:PROGRESS/STATUS/MESSAGE format
  - `mission.model_context_updates` -- instructions for updating vessel model context
  - `commit.instructions_preamble` -- commit trailer injection instructions
  - `agent.launch_prompt` -- short CLI prompt wrapper
  - `persona.worker` -- default worker persona (current captain behavior)
  - `persona.architect` -- architect persona
  - `persona.judge` -- judge/reviewer persona
  - `persona.test_engineer` -- test writing persona

### 1.4 Template Resolution Service

- [x] Create `src/Armada.Core/Services/PromptTemplateService.cs` implementing `IPromptTemplateService`
  - `ResolveAsync(string name)` -- check database first, fall back to embedded resource
  - `RenderAsync(string name, Dictionary<string, string> parameters)` -- resolve + substitute placeholders
  - `SeedDefaultsAsync()` -- on startup, ensure all built-in templates exist in database (insert if missing, do not overwrite user edits)
  - `ListAsync(string? category)` -- list all templates, merged view of DB + embedded
  - `ResetToDefaultAsync(string name)` -- restore a template to its embedded resource content
- [x] Create `IPromptTemplateService` interface in `src/Armada.Core/Services/Interfaces/`

### 1.5 Refactor MissionService.GenerateClaudeMdAsync

- [x] Replace inline string concatenation with calls to `IPromptTemplateService.RenderAsync()`
- [x] Each section becomes a named template resolved through the service (`ResolveSectionAsync`)
- [x] The method assembles sections: resolve each template, concatenate in order
- [x] Preserve all existing behavior via `GetHardcodedFallback` when template service is null
- [x] Persona-aware: `ResolvePersonaPromptAsync` resolves `persona.{name}` template for the mission preamble
- [x] Template params dictionary built from mission/vessel/captain context

### 1.6 Refactor AgentLifecycleHandler Launch Prompt

- [x] Replace hardcoded prompt at `AgentLifecycleHandler.cs:100` with template resolution
- [x] Template name: `agent.launch_prompt`
- [x] Hardcoded fallback preserved for backward compatibility
- [x] Wired `IPromptTemplateService` into AgentLifecycleHandler constructor

### 1.7 Refactor MessageTemplateService

- [x] `commit.instructions_preamble` template seeded via PromptTemplateService.SeedDefaultsAsync
- [x] Inject IPromptTemplateService into MessageTemplateService to resolve preamble at runtime

### 1.8 Full Template Coverage -- Zero Hardcoded Prompt Strings

Goal: every string that forms part of a prompt to an agent must be resolvable from a template, so it is editable in the dashboard. The following items are still hardcoded in `MissionService.GenerateClaudeMdAsync` and `MissionLandingHandler`:

- [x] Create template `mission.captain_instructions_wrapper` (category: "structure")
  - Default: `## Captain Instructions\n{CaptainInstructions}\n`
  - Rendered only when `{CaptainInstructions}` is non-empty
- [x] Create template `mission.project_context_wrapper` (category: "structure")
  - Default: `## Project Context\n{ProjectContext}\n`
  - Rendered only when `{ProjectContext}` is non-empty
- [x] Create template `mission.code_style_wrapper` (category: "structure")
  - Default: `## Code Style\n{StyleGuide}\n`
  - Rendered only when `{StyleGuide}` is non-empty
- [x] Create template `mission.model_context_wrapper` (category: "structure")
  - Default: `## Model Context\nThe following context was accumulated by AI agents during previous missions on this repository. Use this information to work more effectively.\n\n{ModelContext}\n`
  - Rendered only when model context is enabled and `{ModelContext}` is non-empty
- [x] Create template `mission.metadata` (category: "structure")
  - Default: `# Mission Instructions\n\n{PersonaPrompt}\n\n## Mission\n- **Title:** {MissionTitle}\n- **ID:** {MissionId}\n- **Voyage:** {VoyageId}\n\n## Description\n{MissionDescription}\n\n## Repository\n- **Name:** {VesselName}\n- **Branch:** {BranchName}\n- **Default Branch:** {DefaultBranch}\n`
  - This controls the entire mission metadata layout -- users can rearrange, add, or remove fields
- [x] Create template `mission.existing_instructions_wrapper` (category: "structure")
  - Default: `\n## Existing Project Instructions\n\n{ExistingClaudeMd}`
  - Rendered only when the repo already has a CLAUDE.md
- [x] Create template `landing.pr_body` (category: "landing")
  - Default: `## Mission\n**{MissionTitle}**\n\n{MissionDescription}`
  - Currently hardcoded in `MissionLandingHandler.cs:215-221`
- [x] Refactor `MissionService.GenerateClaudeMdAsync` to resolve all wrapper/metadata sections through `ResolveSectionAsync` instead of inline strings
- [x] Refactor `MissionLandingHandler` PR body generation to resolve through template service
- [x] Refactor `MessageTemplateService.RenderCommitInstructions` to resolve preamble from `commit.instructions_preamble` template at runtime
- [x] Seed all new templates in `PromptTemplateService._EmbeddedDefaults` (7 new: 6 structure + 1 landing)
- [x] Update dashboard Prompt Template editor with category tab bar for quick filtering by structure/mission/persona/commit/landing/agent

---

## Phase 2: Persona Model and Storage

### 2.1 Persona Model

- [x] Create `src/Armada.Core/Models/Persona.cs`
  - `Id` (string, `prs_` prefix)
  - `TenantId` (string?)
  - `Name` (string, unique, e.g. `"Worker"`, `"Architect"`, `"Judge"`, `"TestEngineer"`)
  - `Description` (string?, what this persona does)
  - `PromptTemplateName` (string, references a PromptTemplate by name, e.g. `"persona.worker"`)
  - `IsBuiltIn` (bool, true for system-shipped personas)
  - `Active` (bool)
  - `CreatedUtc`, `LastUpdateUtc`

### 2.2 Database Schema: `personas` Table

**DDL (SQLite reference -- adapt types per driver):**

```sql
CREATE TABLE IF NOT EXISTS personas (
    id                   TEXT PRIMARY KEY,
    tenant_id            TEXT,
    name                 TEXT NOT NULL,
    description          TEXT,
    prompt_template_name TEXT NOT NULL,       -- references prompt_templates.name
    is_built_in          INTEGER NOT NULL DEFAULT 0,
    active               INTEGER NOT NULL DEFAULT 1,
    created_utc          TEXT NOT NULL,
    last_update_utc      TEXT NOT NULL,
    FOREIGN KEY (tenant_id) REFERENCES tenants(id)
);
```

**Indexes:**

```sql
CREATE UNIQUE INDEX idx_personas_tenant_name ON personas(tenant_id, name);
CREATE INDEX idx_personas_active ON personas(active);
CREATE INDEX idx_personas_prompt_template ON personas(prompt_template_name);
```

**Implementation checklist:**

- [x] Create `IPersonaMethods` interface in `src/Armada.Core/Database/Interfaces/`
  - `CreateAsync`, `ReadAsync`, `ReadByNameAsync`, `UpdateAsync`, `DeleteAsync`, `ExistsAsync`, `AllAsync`
- [x] Implement for SQLite in `src/Armada.Core/Database/Sqlite/Implementations/PersonaMethods.cs`
- [x] Implement for MySQL in `src/Armada.Core/Database/Mysql/Implementations/PersonaMethods.cs`
- [x] Implement for PostgreSQL in `src/Armada.Core/Database/Postgresql/Implementations/PersonaMethods.cs`
- [x] Implement for SQL Server in `src/Armada.Core/Database/SqlServer/Implementations/PersonaMethods.cs`
- [x] Add in-code `SchemaMigration` entry in each driver's `TableQueries.cs` (CREATE TABLE + indexes)
- [x] Add the table to the initial schema DDL in each driver's `TableQueries.cs`
- [x] Create migration scripts:
  - `migrations/migrate_add_personas.sh`
  - `migrations/migrate_add_personas.bat`
- [x] Wire into `DatabaseDriver` as `Personas` property

### 2.3 Built-in Persona Seeding

- [x] On startup, seed default personas if they don't exist:
  - `Worker` -- standard mission executor (current behavior)
  - `Architect` -- plans voyages and decomposes work into missions
  - `Judge` -- reviews completed mission diffs for correctness and completeness
  - `TestEngineer` -- writes/updates tests for mission changes
- [x] Built-in personas reference built-in prompt templates (`persona.worker`, etc.)

### 2.4 Captain Persona Capabilities

**New columns on `captains` table:**

```sql
ALTER TABLE captains ADD COLUMN allowed_personas TEXT;       -- nullable JSON array, e.g. '["Worker","Judge"]'
ALTER TABLE captains ADD COLUMN preferred_persona TEXT;      -- nullable, e.g. 'Architect'
```

**Indexes:**

```sql
CREATE INDEX idx_captains_preferred_persona ON captains(preferred_persona);
```

**Implementation checklist:**

- [x] Add `AllowedPersonas` (string?, nullable JSON array) to `Captain` model
  - `null` means "can take on any persona" (default)
  - When set, contains a list of persona names the captain is allowed to fill, e.g. `["Worker", "Judge"]`
  - This is a soft preference for dispatch routing -- the Admiral prefers matching captains but can fall back
- [x] Add `PreferredPersona` (string?, nullable) to `Captain` model
  - Optional hint for dispatch priority
- [x] Add in-code `SchemaMigration` entry in each driver's `TableQueries.cs` (ALTER TABLE + index)
- [x] Create migration scripts:
  - `migrations/migrate_add_captain_personas.sh`
  - `migrations/migrate_add_captain_personas.bat`
- [x] Update `McpCaptainTools` to expose both fields on create/update
- [x] Update `CaptainCreateArgs` and `CaptainUpdateArgs`

### 2.5 Mission Persona Assignment

**New columns on `missions` table:**

```sql
ALTER TABLE missions ADD COLUMN persona TEXT;                -- nullable, e.g. 'Worker', 'Judge'
```

**Indexes:**

```sql
CREATE INDEX idx_missions_persona ON missions(persona);
```

**Implementation checklist:**

- [x] Add `Persona` (string?, nullable) to `Mission` model
  - When set, indicates which persona this mission requires
  - `null` defaults to `"Worker"` for backward compatibility
- [x] Add in-code `SchemaMigration` entry in each driver's `TableQueries.cs` (ALTER TABLE + index)
- [x] Create migration scripts:
  - `migrations/migrate_add_mission_persona.sh`
  - `migrations/migrate_add_mission_persona.bat`
- [x] Update `McpMissionTools` to expose `persona` on create/update

---

## Phase 3: Pipeline Configuration

### 3.1 Pipeline Model

A pipeline is an ordered list of persona stages that a dispatch goes through.

- [x] Create `src/Armada.Core/Models/Pipeline.cs`
  - `Id` (string, `ppl_` prefix)
  - `TenantId` (string?)
  - `Name` (string, e.g. `"Default"`, `"FullReview"`, `"WorkerOnly"`)
  - `Description` (string?)
  - `Stages` (List<PipelineStage>) -- ordered list
  - `IsBuiltIn` (bool)
  - `Active` (bool)
  - `CreatedUtc`, `LastUpdateUtc`

- [x] Create `src/Armada.Core/Models/PipelineStage.cs`
  - `Order` (int, 1-based)
  - `PersonaName` (string, references a Persona by name)
  - `IsOptional` (bool, if true the Admiral may skip this stage)
  - `Description` (string?, e.g. "Plan the voyage", "Execute the mission", "Review the diff")

### 3.2 Database Schema: `pipelines` and `pipeline_stages` Tables

**DDL (SQLite reference -- adapt types per driver):**

```sql
CREATE TABLE IF NOT EXISTS pipelines (
    id              TEXT PRIMARY KEY,
    tenant_id       TEXT,
    name            TEXT NOT NULL,
    description     TEXT,
    is_built_in     INTEGER NOT NULL DEFAULT 0,
    active          INTEGER NOT NULL DEFAULT 1,
    created_utc     TEXT NOT NULL,
    last_update_utc TEXT NOT NULL,
    FOREIGN KEY (tenant_id) REFERENCES tenants(id)
);

CREATE TABLE IF NOT EXISTS pipeline_stages (
    id              TEXT PRIMARY KEY,
    pipeline_id     TEXT NOT NULL,
    stage_order     INTEGER NOT NULL,
    persona_name    TEXT NOT NULL,
    is_optional     INTEGER NOT NULL DEFAULT 0,
    description     TEXT,
    FOREIGN KEY (pipeline_id) REFERENCES pipelines(id) ON DELETE CASCADE
);
```

**Indexes:**

```sql
-- pipelines
CREATE UNIQUE INDEX idx_pipelines_tenant_name ON pipelines(tenant_id, name);
CREATE INDEX idx_pipelines_active ON pipelines(active);

-- pipeline_stages
CREATE INDEX idx_pipeline_stages_pipeline ON pipeline_stages(pipeline_id);
CREATE UNIQUE INDEX idx_pipeline_stages_order ON pipeline_stages(pipeline_id, stage_order);
CREATE INDEX idx_pipeline_stages_persona ON pipeline_stages(persona_name);
```

**Implementation checklist:**

- [x] Create `IPipelineMethods` interface in `src/Armada.Core/Database/Interfaces/`
  - `CreateAsync`, `ReadAsync`, `ReadByNameAsync`, `UpdateAsync`, `DeleteAsync`, `ExistsAsync`, `AllAsync`
  - Must handle loading/saving stages as part of pipeline CRUD (join or separate queries)
- [x] Implement for SQLite in `src/Armada.Core/Database/Sqlite/Implementations/PipelineMethods.cs`
- [x] Implement for MySQL in `src/Armada.Core/Database/Mysql/Implementations/PipelineMethods.cs`
- [x] Implement for PostgreSQL in `src/Armada.Core/Database/Postgresql/Implementations/PipelineMethods.cs`
- [x] Implement for SQL Server in `src/Armada.Core/Database/SqlServer/Implementations/PipelineMethods.cs`
- [x] Add in-code `SchemaMigration` entry in each driver's `TableQueries.cs` (both CREATE TABLEs + all indexes)
- [x] Add both tables to the initial schema DDL in each driver's `TableQueries.cs`
- [x] Create migration scripts:
  - `migrations/migrate_add_pipelines.sh`
  - `migrations/migrate_add_pipelines.bat`
  - Must create both tables and all indexes
- [x] Wire into `DatabaseDriver` as `Pipelines` property

### 3.3 Built-in Pipeline Seeding

- [x] Seed default pipelines on startup:
  - `WorkerOnly` -- `[Worker]` (backward compatible, current behavior)
  - `Reviewed` -- `[Worker, Judge]`
  - `FullPipeline` -- `[Architect, Worker, TestEngineer, Judge]`
  - `Tested` -- `[Worker, TestEngineer, Judge]`

### 3.4 Fleet/Vessel Pipeline Configuration

**New columns on `fleets` and `vessels` tables:**

```sql
ALTER TABLE fleets ADD COLUMN default_pipeline_id TEXT;
ALTER TABLE vessels ADD COLUMN default_pipeline_id TEXT;
```

**Indexes:**

```sql
CREATE INDEX idx_fleets_default_pipeline ON fleets(default_pipeline_id);
CREATE INDEX idx_vessels_default_pipeline ON vessels(default_pipeline_id);
```

**Implementation checklist:**

- [x] Add `DefaultPipelineId` (string?, nullable, `ppl_` prefix) to `Fleet` model
- [x] Add `DefaultPipelineId` (string?, nullable, `ppl_` prefix) to `Vessel` model
  - Vessel setting overrides fleet setting
  - `null` means use `WorkerOnly` for backward compatibility
- [x] Add in-code `SchemaMigration` entry in each driver's `TableQueries.cs` (ALTER TABLEs + indexes)
  - Note: combined into migration 23 (pipelines) rather than separate migration 24
- [x] Create migration scripts:
  - Note: combined into `migrations/migrate_add_pipelines.sh/.bat` rather than separate scripts
- [x] Update `McpFleetTools` and `McpVesselTools` to expose `defaultPipelineId`

### 3.5 Dispatch Pipeline Override

- [x] Add `pipelineId` (string?, optional) parameter to `armada_dispatch` MCP tool
  - When provided, overrides the fleet/vessel default for this dispatch only
- [x] Add `pipeline` (string?, optional) parameter as a convenience alias (resolves by name)

---

## Phase 4: Admiral Dispatch Integration

### 4.1 Pipeline-Aware Dispatch

- [x] When `armada_dispatch` is called:
  1. Resolve pipeline: explicit param > vessel default > fleet default > `WorkerOnly`
  2. For a single-stage pipeline (e.g. `WorkerOnly`), behave exactly as today
  3. For multi-stage pipelines, create a voyage containing one mission per stage
  4. First stage mission is created immediately; subsequent stages are created with status `Pending` and a `DependsOnMissionId` field

### 4.2 Mission Dependency Chain

**New columns on `missions` table:**

```sql
ALTER TABLE missions ADD COLUMN depends_on_mission_id TEXT;
```

**Indexes:**

```sql
CREATE INDEX idx_missions_depends_on ON missions(depends_on_mission_id);
```

**Implementation checklist:**

- [x] Add `DependsOnMissionId` (string?, nullable) to `Mission` model
  - When set, this mission cannot be assigned until the dependency mission reaches a terminal success state
- [x] Add in-code `SchemaMigration` entry in each driver's `TableQueries.cs` (ALTER TABLE + index)
  - Note: combined into migration 22 (mission persona) rather than separate migration 25
- [x] Create migration scripts:
  - Note: combined into `migrations/migrate_add_mission_persona.sh/.bat` rather than separate scripts
- [x] Admiral health check / assignment loop: skip missions whose dependency is not yet satisfied
  - Dependency check in `TryAssignAsync` -- skips if dependency not Complete/WorkProduced

### 4.3 Stage Handoff

- [x] When a mission completes successfully and another mission depends on it:
  1. `TryHandoffToNextStageAsync` finds dependent missions in the same voyage
  2. Injects prior stage context (persona, title, branch, diff snapshot) into next mission's description
  3. Sets the same branch name so the next stage works on the same branch
  4. Automatically attempts to assign the next stage mission
- [x] Architect stage special handling: parses [ARMADA:MISSION] markers from output, creates Worker missions, clones post-worker stages (Judge, TestEngineer) for each additional worker

### 4.4 Captain Dispatch Routing

- [x] Modify `FindAvailableCaptainAsync` to consider persona requirements:
  1. Mission has a `Persona` field (e.g. `"Architect"`)
  2. Filter available captains by `AllowedPersonas` (null = all allowed)
  3. Sort by `PreferredPersona` match (prefer captains whose preference matches)
  4. Fall back to any available captain if no preferred match

---

## Phase 5: Built-in Persona Prompt Templates

### 5.1 Worker Persona Template (`persona.worker`)

- [x] Worker persona template embedded in PromptTemplateService and seeded on startup
- [x] This is the default -- missions without an explicit persona use this template
- [x] All placeholders available via template params dictionary in GenerateClaudeMdAsync

### 5.2 Architect Persona Template (`persona.architect`)

- [x] Architect persona template embedded in PromptTemplateService with [ARMADA:MISSION] output format
- [x] ParseArchitectOutput parses markers into structured mission definitions
- [x] TryHandoffToNextStageAsync creates Worker missions from architect output

### 5.3 Judge Persona Template (`persona.judge`)

- [x] Judge persona template embedded in PromptTemplateService with PASS/FAIL/NEEDS_REVISION verdicts

### 5.4 Test Engineer Persona Template (`persona.test_engineer`)

- [x] TestEngineer persona template embedded in PromptTemplateService

---

## Phase 6: Dashboard UI

### 6.1 Persona Management Page

- [x] Create `src/Armada.Dashboard/src/pages/Personas.tsx` -- list view with table, filters, sort, pagination, CRUD modals
- [x] Create `src/Armada.Dashboard/src/pages/PersonaDetail.tsx` -- detail view with edit, JSON viewer, delete (built-in guard)
- [x] Add navigation entry in sidebar under System section

### 6.2 Prompt Template Management Page

- [x] Create `src/Armada.Dashboard/src/pages/PromptTemplates.tsx` -- list view with category filter dropdown, content length, reset to default
- [x] Create `src/Armada.Dashboard/src/pages/PromptTemplateDetail.tsx` -- two-column editor:
  - Left: full-height monospace textarea editor with save/reset buttons, unsaved indicator
  - Right: parameter reference panel grouped by context (Mission, Vessel, Captain, Pipeline, System) with click-to-insert
  - Built-in badge, ActionMenu with View JSON and Reset to Default
  - Detail grid with ID, Active, Created, Last Updated
- [x] Add navigation entry in sidebar under System section

### 6.3 Pipeline Management Page

- [x] Create `src/Armada.Dashboard/src/pages/Pipelines.tsx` -- list view with stages display (arrow-joined), CRUD, dynamic stage editor
- [x] Create `src/Armada.Dashboard/src/pages/PipelineDetail.tsx` -- detail view with stages table, edit modal with dynamic stage list
- [x] Add navigation entry in sidebar under System section

### 6.4 Vessel/Fleet Detail Updates

- [x] Add `DefaultPipelineId` field to `VesselDetail.tsx` (display + edit form)
- [x] Add `DefaultPipelineId` field to `FleetDetail.tsx` (display + edit form)

### 6.5 Captain Detail Updates

- [x] Add `AllowedPersonas` and `PreferredPersona` to `CaptainDetail.tsx` (display + edit form)

### 6.6 Mission Detail Updates

- [x] Show `Persona` field on MissionDetail.tsx (defaults to "Worker" when null)
- [x] Show `DependsOnMissionId` link on MissionDetail.tsx (conditional, links to dependency)

### 6.7 Dashboard Build

- [x] Rebuild dashboard dist assets after all UI changes

---

## Phase 7: MCP and REST API

### 7.1 Prompt Template MCP Tools

- [x] `armada_get_prompt_template` -- get a template by name
- [x] `armada_update_prompt_template` -- update template content
- [x] `armada_reset_prompt_template` -- reset to embedded default
- [x] Register in `McpToolRegistrar.cs` or create `McpPromptTemplateTools.cs`

### 7.2 Persona MCP Tools

- [x] `armada_create_persona` -- create a custom persona
- [x] `armada_get_persona` -- get persona by ID or name
- [x] `armada_update_persona` -- update persona properties
- [x] `armada_delete_persona` -- delete a custom persona (block deletion of built-in)
- [x] Register in `McpToolRegistrar.cs` or create `McpPersonaTools.cs`

### 7.3 Pipeline MCP Tools

- [x] `armada_create_pipeline` -- create a custom pipeline
- [x] `armada_get_pipeline` -- get pipeline by ID or name
- [x] `armada_update_pipeline` -- update pipeline stages
- [x] `armada_delete_pipeline` -- delete a custom pipeline (block deletion of built-in)
- [x] Register in `McpToolRegistrar.cs` or create `McpPipelineTools.cs`

### 7.4 Enumerate Support

- [x] Add `persona`, `prompt_template`, and `pipeline` as entity types in `armada_enumerate`

### 7.5 REST API Routes

- [x] Create `src/Armada.Server/Routes/PromptTemplateRoutes.cs` -- 5 endpoints (list, enumerate, get by name, update, reset)
- [x] Create `src/Armada.Server/Routes/PersonaRoutes.cs` -- 6 endpoints (list, enumerate, get, create, update, delete)
- [x] Create `src/Armada.Server/Routes/PipelineRoutes.cs` -- 6 endpoints (list, enumerate, get, create, update, delete)

### 7.6 WebSocket Commands

- [x] Add `get_persona`, `update_persona`, `create_persona`, `delete_persona`
- [x] Add `get_prompt_template`, `update_prompt_template`
- [x] Add `get_pipeline`, `update_pipeline`, `create_pipeline`, `delete_pipeline`

### 7.7 Updated Existing Tools

- [x] `armada_dispatch` -- add `pipelineId` parameter (pipeline name alias TBD)
- [x] `armada_create_captain` / `armada_update_captain` -- add `allowedPersonas`, `preferredPersona`
- [x] `armada_update_vessel` -- add `defaultPipelineId`
- [x] `armada_update_fleet` -- add `defaultPipelineId`

---

## Phase 8: Tests

### 8.1 Unit Tests

- [x] `PromptTemplateServiceTests.cs` -- 7 tests: seed, resolve DB/embedded, render, reset, list, list by category
- [x] `PersonaPipelineDbTests.cs` -- 9 tests: persona CRUD, pipeline CRUD with stages, cascade delete
- [x] `PipelineDispatchTests.cs` -- 5 tests: single/multi-stage dispatch, dependency blocking, persona routing, AllowedPersonas filtering
- [x] Update `MissionPromptTests.cs` -- 4 new tests: template-resolved rules, persona prompt, model context, placeholder substitution

### 8.2 Automated Tests

- [ ] Template round-trip: create, read, update, delete via MCP tools (future -- automated/integration tests)
- [ ] Persona round-trip: create, read, update, delete via MCP tools (future -- automated/integration tests)
- [ ] Pipeline round-trip: create, read, update, delete via MCP tools (future -- automated/integration tests)
- [ ] Dispatch with pipeline: verify missions created with correct personas and dependencies (future -- automated/integration tests)

---

## Phase 9: Documentation

### 9.1 MCP_API.md

- [x] Add sections for all new MCP tools (prompt templates, personas, pipelines)
- [x] Update `armada_dispatch` documentation with pipeline parameters
- [x] Update `armada_create_captain` / `armada_update_captain` with new fields

### 9.2 REST_API.md

- [x] Add sections for prompt template, persona, and pipeline REST endpoints

### 9.3 README.md

- [x] Add Personas section explaining the concept and built-in personas
- [x] Add Pipeline section explaining how to configure dispatch workflows
- [x] Update architecture section to mention prompt template system

### 9.4 New Documentation

- [x] Create `docs/PERSONAS_GUIDE.md` (user-facing guide, plan remains as PERSONAS.md)
  - What personas are and how they work
  - Built-in personas and their behavior
  - Creating custom personas
  - Pipeline configuration
  - Prompt template customization

---

## Appendix A: Migration Summary

All schema changes required by this plan, consolidated for reference. Each migration must be implemented as:
1. An in-code `SchemaMigration` entry in each of the four driver `TableQueries.cs` files (SQLite, MySQL, PostgreSQL, SQL Server)
2. Added to the initial schema DDL in each driver (for fresh installs)
3. A pair of standalone migration scripts (`migrations/*.sh` + `migrations/*.bat`) following the existing pattern

Current highest SQLite migration number: **18**. New migrations should start at **19**.

| Migration # | Name | Tables/Columns Affected | Indexes Created | Scripts |
|------------|------|------------------------|----------------|---------|
| 19 | Add prompt_templates table | New table: `prompt_templates` (id, tenant_id, name, description, category, content, is_built_in, active, created_utc, last_update_utc) | `idx_prompt_templates_tenant_name` (UNIQUE), `idx_prompt_templates_category`, `idx_prompt_templates_active` | `migrate_add_prompt_templates.sh/.bat` |
| 20 | Add personas table | New table: `personas` (id, tenant_id, name, description, prompt_template_name, is_built_in, active, created_utc, last_update_utc) | `idx_personas_tenant_name` (UNIQUE), `idx_personas_active`, `idx_personas_prompt_template` | `migrate_add_personas.sh/.bat` |
| 21 | Add captain persona fields | `captains`: +`allowed_personas` (TEXT), +`preferred_persona` (TEXT) | `idx_captains_preferred_persona` | `migrate_add_captain_personas.sh/.bat` |
| 22 | Add mission persona field | `missions`: +`persona` (TEXT) | `idx_missions_persona` | `migrate_add_mission_persona.sh/.bat` |
| 23 | Add pipelines and pipeline_stages tables | New table: `pipelines` (id, tenant_id, name, description, is_built_in, active, created_utc, last_update_utc). New table: `pipeline_stages` (id, pipeline_id, stage_order, persona_name, is_optional, description) | `idx_pipelines_tenant_name` (UNIQUE), `idx_pipelines_active`, `idx_pipeline_stages_pipeline`, `idx_pipeline_stages_order` (UNIQUE), `idx_pipeline_stages_persona` | `migrate_add_pipelines.sh/.bat` |
| 24 | Add default_pipeline_id to fleets and vessels | `fleets`: +`default_pipeline_id` (TEXT). `vessels`: +`default_pipeline_id` (TEXT) | `idx_fleets_default_pipeline`, `idx_vessels_default_pipeline` | `migrate_add_default_pipeline.sh/.bat` |
| 25 | Add mission dependency chain | `missions`: +`depends_on_mission_id` (TEXT) | `idx_missions_depends_on` | `migrate_add_mission_dependency.sh/.bat` |

**Total: 7 migrations, 4 new tables, 7 new columns on existing tables, 16 new indexes**

**Driver-specific type mappings for `content`/large text columns:**

| Driver | Type |
|--------|------|
| SQLite | `TEXT` |
| MySQL | `LONGTEXT` |
| PostgreSQL | `TEXT` |
| SQL Server | `NVARCHAR(MAX)` |

---

## Appendix B: Hardcoded Prompts Inventory

All prompts that are or were hardcoded in C#. Status column indicates current state.

| # | Template Name | Category | Description | Status |
|---|--------------|----------|-------------|--------|
| 1 | `mission.captain_instructions_wrapper` | structure | Wrapper: `## Captain Instructions\n{CaptainInstructions}` | **DONE** -- template-resolved |
| 2 | `mission.project_context_wrapper` | structure | Wrapper: `## Project Context\n{ProjectContext}` | **DONE** -- template-resolved |
| 3 | `mission.code_style_wrapper` | structure | Wrapper: `## Code Style\n{StyleGuide}` | **DONE** -- template-resolved |
| 4 | `mission.model_context_wrapper` | structure | Wrapper: `## Model Context\n` + preamble + `{ModelContext}` | **DONE** -- template-resolved |
| 5 | `mission.metadata` | structure | Mission title, ID, voyage, description, repo info layout | **DONE** -- template-resolved |
| 6 | `mission.existing_instructions_wrapper` | structure | Wrapper: `## Existing Project Instructions\n{ExistingClaudeMd}` | **DONE** -- template-resolved |
| 7 | `mission.rules` | mission | Worktree rules, commit rules, ASCII-only | **DONE** -- template-resolved |
| 8 | `mission.context_conservation` | mission | Context window management rules | **DONE** -- template-resolved |
| 9 | `mission.merge_conflict_avoidance` | mission | Multi-captain conflict prevention | **DONE** -- template-resolved |
| 10 | `mission.progress_signals` | mission | ARMADA signal format documentation | **DONE** -- template-resolved |
| 11 | `mission.model_context_updates` | mission | Instructions for updating vessel model context | **DONE** -- template-resolved |
| 12 | `agent.launch_prompt` | agent | Short CLI prompt: `Mission: {MissionTitle}\n\n{MissionDescription}` | **DONE** -- template-resolved |
| 13 | `commit.instructions_preamble` | commit | "IMPORTANT: For every git commit..." | **DONE** -- resolved at runtime via GetEmbeddedDefault |
| 14 | `landing.pr_body` | landing | PR body: `## Mission\n**{MissionTitle}**\n\n{MissionDescription}` | **DONE** -- template-resolved |
| 15 | `commit.message_template` | commit | Commit trailer template | Already configurable via MessageTemplateSettings |
| 16 | `commit.pr_description_template` | commit | PR description metadata | Already configurable via MessageTemplateSettings |
| 17 | `commit.merge_message_template` | commit | Merge commit message | Already configurable via MessageTemplateSettings |
| 18 | `persona.worker` | persona | Default worker persona preamble | **DONE** -- template-resolved |
| 19 | `persona.architect` | persona | Architect planning instructions | **DONE** -- template-resolved |
| 20 | `persona.judge` | persona | Judge review instructions | **DONE** -- template-resolved |
| 21 | `persona.test_engineer` | persona | Test engineer instructions | **DONE** -- template-resolved |

---

## Appendix C: Template Placeholder Reference

All placeholders available for template rendering:

### Mission Context
| Placeholder | Source | Description |
|-------------|--------|-------------|
| `{MissionId}` | `mission.Id` | Mission identifier |
| `{MissionTitle}` | `mission.Title` | Mission title |
| `{MissionDescription}` | `mission.Description` | Full mission description |
| `{MissionPersona}` | `mission.Persona` | Persona assigned to this mission |
| `{VoyageId}` | `mission.VoyageId` | Parent voyage identifier |
| `{VoyageTitle}` | `voyage.Title` | Parent voyage title |
| `{BranchName}` | `mission.BranchName` | Git branch for this mission |

### Vessel Context
| Placeholder | Source | Description |
|-------------|--------|-------------|
| `{VesselId}` | `vessel.Id` | Vessel identifier |
| `{VesselName}` | `vessel.Name` | Vessel display name |
| `{DefaultBranch}` | `vessel.DefaultBranch` | Default branch (e.g. main) |
| `{ProjectContext}` | `vessel.ProjectContext` | User-supplied project description |
| `{StyleGuide}` | `vessel.StyleGuide` | User-supplied style guide |
| `{ModelContext}` | `vessel.ModelContext` | Agent-accumulated context |
| `{FleetId}` | `vessel.FleetId` | Parent fleet identifier |

### Captain Context
| Placeholder | Source | Description |
|-------------|--------|-------------|
| `{CaptainId}` | `captain.Id` | Captain identifier |
| `{CaptainName}` | `captain.Name` | Captain display name |
| `{CaptainInstructions}` | `captain.SystemInstructions` | User-supplied captain instructions |

### Dock/Runtime Context
| Placeholder | Source | Description |
|-------------|--------|-------------|
| `{DockId}` | `dock.Id` | Dock (worktree) identifier |
| `{WorktreePath}` | `dock.WorktreePath` | Filesystem path to worktree |

### Pipeline Context (new)
| Placeholder | Source | Description |
|-------------|--------|-------------|
| `{PipelineName}` | `pipeline.Name` | Pipeline name |
| `{StageNumber}` | stage order | Current stage number |
| `{TotalStages}` | pipeline stage count | Total stages in pipeline |
| `{PreviousStageDiff}` | git diff from prior stage | Diff output from the previous stage (for Judge/TestEngineer) |
| `{PreviousStageOutput}` | prior mission output | Structured output from the previous stage (for Architect output) |

### System
| Placeholder | Source | Description |
|-------------|--------|-------------|
| `{Timestamp}` | `DateTime.UtcNow` | Current UTC timestamp |
| `{ExistingClaudeMd}` | file read | Contents of repo's existing CLAUDE.md |
