## Project Context
Armada is a multi-agent orchestration system that scales human developers with AI. It coordinates AI coding agents ("captains") to work on tasks ("missions") across git repositories ("vessels"). Written in C# (.NET), it exposes MCP tools for fleet, vessel, captain, mission, voyage, dock, signal, and merge queue management.

IMPORTANT — Context Conservation: When interacting with Armada MCP tools, ALWAYS prefer armada_enumerate over armada_list_* tools. The armada_enumerate tool supports pagination (pageNumber, pageSize), filtering (by vesselId, fleetId, captainId, voyageId, status, date ranges), and sorting. The armada_list_* tools return ALL records at once and can exhaust context windows with large result sets. Use armada_enumerate with a small pageSize (10-25) to conserve context.

## Code Style
For C#: no var, no tuples, using statements instead of declarations, using statements inside the namespace blocks, XML documentation, public things named LikeThis, private things named _LikeThis, one entity per file, null check on set where appropriate and value-clamping to reasonable ranges where appropriate

## Model Context
The following context was accumulated by AI agents during previous missions on this repository. Use this information to work more effectively.

## Architecture Notes

### Database Layer
- Four parallel DB implementations: SQLite, PostgreSQL, MySQL, SQL Server
- Each has independent TableQueries.cs with migration definitions and SQL strings
- MySQL uses static string properties AND GetMigrations() in MysqlDatabaseDriver.cs (migration statements defined in TableQueries, registered in driver)
- PostgreSQL, SQL Server, and SQLite use GetMigrations() returning List<SchemaMigration> in TableQueries.cs
- Migrations are versioned integers, run automatically on startup by the database driver's InitializeAsync()
- SchemaMigration class: (version int, description string, params string[] statements)
- CaptainMethods.cs and MissionMethods.cs handle CRUD with manual reader-to-object mapping
- Reader methods (CaptainFromReader, MissionFromReader) may be in the driver file OR in the implementation files depending on DB type
- MySQL has CaptainFromReader in BOTH MysqlDatabaseDriver.cs (internal static) AND CaptainMethods.cs (private) -- both must be updated
- SQL Server has readers in SqlServerDatabaseDriver.cs
- SQLite has readers in SqliteDatabaseDriver.cs
- PostgreSQL has readers in the implementation files (CaptainMethods.cs, MissionMethods.cs)
- New columns added via migration use try/catch pattern in readers for backward compatibility

### Migration Version Numbers (as of v0.5.0)
- SQLite: versions 1-27 (last: total_runtime_seconds on missions)
- PostgreSQL: versions 1-15
- SQL Server: versions 1-15
- MySQL: versions 1-17

### Agent Runtime
- IAgentRuntime interface defines StartAsync(workingDirectory, prompt, environment, logFilePath, token) plus Model property
- BaseAgentRuntime has Model property (string? Model) that runtimes can use in BuildArguments()
- ClaudeCodeRuntime.BuildArguments() constructs CLI args: --print, --verbose, optional --dangerously-skip-permissions, optional --model, then prompt
- CodexRuntime also supports --model flag
- AgentLifecycleHandler.HandleLaunchAgentAsync() sets runtime.Model = captain.Model before starting
- AgentRuntimeFactory.Create(AgentRuntimeEnum) returns the appropriate IAgentRuntime implementation

### Captain Model Selection (v0.5.0)
- Captain.Model property (string?, nullable) specifies the AI model
- When null, the runtime selects its default model automatically
- Model validation on create/update: ValidateModelAsync() briefly starts the agent with the model to verify it works
- Validation only implemented for ClaudeCode runtime (runs "claude --model X --print Say OK")
- Error messages surfaced through REST (400 status), MCP (Error field), and dashboard (ErrorModal)

### Mission Total Runtime (v0.5.0)
- Mission.TotalRuntimeSeconds (double?, nullable) tracks execution time
- Calculated at mission completion: CompletedUtc - StartedUtc
- Set in MissionLandingHandler (3 locations), AdmiralService.HandleProcessExitAsync, and MissionService

### REST API
- Routes in src/Armada.Server/Routes/ -- one file per entity (CaptainRoutes.cs, MissionRoutes.cs, etc.)
- SwiftStack framework handles routing and serialization
- Captain create: POST /api/v1/captains, update: PUT /api/v1/captains/{id}
- PUT routes deserialize full entity from body, then preserve operational fields from existing record

### MCP Tools
- Tools in src/Armada.Server/Mcp/Tools/ -- one file per entity category (McpCaptainTools.cs, McpMissionTools.cs, etc.)
- Registration in McpToolRegistrar.cs
- Tools use delegate-based handlers with JSON schema parameter definitions
- Args DTOs in src/Armada.Server/Mcp/ (CaptainCreateArgs.cs, CaptainUpdateArgs.cs, etc.)
- Pattern: extract params from request args, call service/DB methods, return JSON response

### Dashboard
- React + TypeScript in src/Armada.Dashboard/
- Pages in src/pages/: CaptainDetail.tsx, MissionDetail.tsx, Dispatch.tsx, etc.
- Shared components: ErrorModal.tsx (title + message + dismiss), ConfirmDialog.tsx (danger mode + delete confirmation)
- State management: useState/useCallback hooks, context providers (Auth, Notification, Theme, WebSocket)
- Detail pages use 3-column grid layout with `detail-field` CSS class
- Type definitions in src/types/models.ts -- must be updated when adding new entity properties
- Form state in detail pages initialized via useState and populated in openEdit() function

### Version References
- Primary: src/Armada.Core/Constants.cs (ProductVersion)
- Build: src/Directory.Build.props, src/Armada.Helm/Armada.Helm.csproj
- Docker: docker/compose.yaml (image tags)
- Dashboard: src/Armada.Dashboard/package.json
- Docs: README.md, CHANGELOG.md, docs/REST_API.md, docs/MCP_API.md

### Migration Scripts
- Located in migrations/ directory
- Both .sh (Unix) and .bat (Windows) variants
- Pattern: check sqlite3 exists, find DB from settings.json, backup, check idempotency, ALTER TABLE, print success
- Named: migrate_v{from}_to_v{to}.sh or migrate_add_{feature}.sh

# Mission Instructions

You are an Armada architect agent. Your role is to analyze a codebase and decompose a high-level objective into well-defined, right-sized missions that worker captains can execute independently and in parallel.

## Your Objective
For v0.5.0 of Armada, the user should be able to specify a model for each captain.  If no model is specified, the model should be selected automatically by the captain when invoked.  This will require the captain data structure to accept a "model" property which will need to be persisted in and retrieved from the underlying database.  Any changes should be made across all four supported database types (sqlite, sql server, postgres, mysql).  New columns should be added through migration code paths that run on server startup, and, a new migration script should be created in migrations/.

This field will need to be editable in the dashboard (make sure it is styled correctly!) and exposed through and manipulated through REST and MCP.  Postman, REST_API, and MCP_API will need to be updated with this new field.  

When the model is specified, it should be passed into the captain where possible through invocation of the agent.  

Adjust all versions to 0.5.0 throughout the code and markdown files.  Update the README and CHANGELOG with information about the change.  

Upon a model change, it is desired that Armada attempt to start the captain briefly to validate the existence/validity of the model, and present an error message to the user (dashboard, REST, MCP) if the specified model is invalid or unavailable.  This error message, when presented through the dashboard, should use a modal - use one of the existing modals/components for this.

The mission detail should include a total runtime field, which should be exposed in the mission detail view.  Like "model", this property will require a migration script in code, migration script change for v0.5.0, updates to the dashboard, REST_API.md, and MCP_API.md updates.

The dispatch page shows "1 task detected" followed by another text box that does nothing but repeat exactly what is in the user description text box.  Both the "1 task detected" and the subsequent textbox should be removed from the dispatch page.

Change the compose.yaml to reference image tags v0.5.0.

## Repository
- Vessel: Armada
- Branch: armada/claude-code-1/msn_mnevgatx_MtfVFNFoU9c
- Default branch: main

## Project Context
Armada is a multi-agent orchestration system that scales human developers with AI. It coordinates AI coding agents ("captains") to work on tasks ("missions") across git repositories ("vessels"). Written in C# (.NET), it exposes MCP tools for fleet, vessel, captain, mission, voyage, dock, signal, and merge queue management.

IMPORTANT — Context Conservation: When interacting with Armada MCP tools, ALWAYS prefer armada_enumerate over armada_list_* tools. The armada_enumerate tool supports pagination (pageNumber, pageSize), filtering (by vesselId, fleetId, captainId, voyageId, status, date ranges), and sorting. The armada_list_* tools return ALL records at once and can exhaust context windows with large result sets. Use armada_enumerate with a small pageSize (10-25) to conserve context.

## Style Guide
For C#: no var, no tuples, using statements instead of declarations, using statements inside the namespace blocks, XML documentation, public things named LikeThis, private things named _LikeThis, one entity per file, null check on set where appropriate and value-clamping to reasonable ranges where appropriate

## Model Context
## Architecture Notes

### Database Layer
- Four parallel DB implementations: SQLite, PostgreSQL, MySQL, SQL Server
- Each has independent TableQueries.cs with migration definitions and SQL strings
- MySQL uses static string properties AND GetMigrations() in MysqlDatabaseDriver.cs (migration statements defined in TableQueries, registered in driver)
- PostgreSQL, SQL Server, and SQLite use GetMigrations() returning List<SchemaMigration> in TableQueries.cs
- Migrations are versioned integers, run automatically on startup by the database driver's InitializeAsync()
- SchemaMigration class: (version int, description string, params string[] statements)
- CaptainMethods.cs and MissionMethods.cs handle CRUD with manual reader-to-object mapping
- Reader methods (CaptainFromReader, MissionFromReader) may be in the driver file OR in the implementation files depending on DB type
- MySQL has CaptainFromReader in BOTH MysqlDatabaseDriver.cs (internal static) AND CaptainMethods.cs (private) -- both must be updated
- SQL Server has readers in SqlServerDatabaseDriver.cs
- SQLite has readers in SqliteDatabaseDriver.cs
- PostgreSQL has readers in the implementation files (CaptainMethods.cs, MissionMethods.cs)
- New columns added via migration use try/catch pattern in readers for backward compatibility

### Migration Version Numbers (as of v0.5.0)
- SQLite: versions 1-27 (last: total_runtime_seconds on missions)
- PostgreSQL: versions 1-15
- SQL Server: versions 1-15
- MySQL: versions 1-17

### Agent Runtime
- IAgentRuntime interface defines StartAsync(workingDirectory, prompt, environment, logFilePath, token) plus Model property
- BaseAgentRuntime has Model property (string? Model) that runtimes can use in BuildArguments()
- ClaudeCodeRuntime.BuildArguments() constructs CLI args: --print, --verbose, optional --dangerously-skip-permissions, optional --model, then prompt
- CodexRuntime also supports --model flag
- AgentLifecycleHandler.HandleLaunchAgentAsync() sets runtime.Model = captain.Model before starting
- AgentRuntimeFactory.Create(AgentRuntimeEnum) returns the appropriate IAgentRuntime implementation

### Captain Model Selection (v0.5.0)
- Captain.Model property (string?, nullable) specifies the AI model
- When null, the runtime selects its default model automatically
- Model validation on create/update: ValidateModelAsync() briefly starts the agent with the model to verify it works
- Validation only implemented for ClaudeCode runtime (runs "claude --model X --print Say OK")
- Error messages surfaced through REST (400 status), MCP (Error field), and dashboard (ErrorModal)

### Mission Total Runtime (v0.5.0)
- Mission.TotalRuntimeSeconds (double?, nullable) tracks execution time
- Calculated at mission completion: CompletedUtc - StartedUtc
- Set in MissionLandingHandler (3 locations), AdmiralService.HandleProcessExitAsync, and MissionService

### REST API
- Routes in src/Armada.Server/Routes/ -- one file per entity (CaptainRoutes.cs, MissionRoutes.cs, etc.)
- SwiftStack framework handles routing and serialization
- Captain create: POST /api/v1/captains, update: PUT /api/v1/captains/{id}
- PUT routes deserialize full entity from body, then preserve operational fields from existing record

### MCP Tools
- Tools in src/Armada.Server/Mcp/Tools/ -- one file per entity category (McpCaptainTools.cs, McpMissionTools.cs, etc.)
- Registration in McpToolRegistrar.cs
- Tools use delegate-based handlers with JSON schema parameter definitions
- Args DTOs in src/Armada.Server/Mcp/ (CaptainCreateArgs.cs, CaptainUpdateArgs.cs, etc.)
- Pattern: extract params from request args, call service/DB methods, return JSON response

### Dashboard
- React + TypeScript in src/Armada.Dashboard/
- Pages in src/pages/: CaptainDetail.tsx, MissionDetail.tsx, Dispatch.tsx, etc.
- Shared components: ErrorModal.tsx (title + message + dismiss), ConfirmDialog.tsx (danger mode + delete confirmation)
- State management: useState/useCallback hooks, context providers (Auth, Notification, Theme, WebSocket)
- Detail pages use 3-column grid layout with `detail-field` CSS class
- Type definitions in src/types/models.ts -- must be updated when adding new entity properties
- Form state in detail pages initialized via useState and populated in openEdit() function

### Version References
- Primary: src/Armada.Core/Constants.cs (ProductVersion)
- Build: src/Directory.Build.props, src/Armada.Helm/Armada.Helm.csproj
- Docker: docker/compose.yaml (image tags)
- Dashboard: src/Armada.Dashboard/package.json
- Docs: README.md, CHANGELOG.md, docs/REST_API.md, docs/MCP_API.md

### Migration Scripts
- Located in migrations/ directory
- Both .sh (Unix) and .bat (Windows) variants
- Pattern: check sqlite3 exists, find DB from settings.json, backup, check idempotency, ALTER TABLE, print success
- Named: migrate_v{from}_to_v{to}.sh or migrate_add_{feature}.sh

## Instructions

1. **Analyze the codebase structure.** Understand the directory layout, module boundaries, key abstractions, and existing patterns. Read only the files necessary to form a plan -- do not read the entire codebase.

2. **Identify the files involved.** For each logical change, determine exactly which files need to be created or modified. Be precise -- captains will be scoped to specific files.

3. **Decompose into missions.** Each mission should:
   - Have a clear, concise title (under 80 characters)
   - Have a detailed description explaining what to do, which files to touch, and why
   - Be completable by a single captain in one session
   - List every file it will modify (for merge conflict detection)
   - Be independently testable where possible

4. **Avoid file overlaps.** Two missions MUST NOT modify the same file unless absolutely unavoidable. If overlap is necessary, document it clearly and mark those missions as sequential (not parallel). File overlap causes merge conflicts during landing.

5. **Consider execution order.** Mark missions that can run in parallel vs. those that must be sequential. Prefer parallel execution to minimize total wall-clock time.

6. **Right-size the missions.** Each mission should touch 1-5 files. If a mission would touch more than 8 files, split it. If it touches only a single line in one file, consider merging it with a related mission.

7. **Output structured mission definitions.** For each mission, provide: title, description (with explicit file list and instructions), estimated complexity (low/medium/high), and dependencies on other missions if any.

IMPORTANT: Output your mission definitions using this exact format so the Admiral can parse them:

[ARMADA:MISSION] Title of mission
Description of the mission, including which files to modify and what changes to make.

[ARMADA:MISSION] Another mission title
Another mission description.

Each [ARMADA:MISSION] marker starts a new mission definition. The first line after the marker is the title, and everything until the next marker (or end of output) is the description.


## Mission
- **Title:** [Architect] For v0.5.0 of Armada, the user should be able to specify a m...
- **ID:** msn_mnevgatx_MtfVFNFoU9c
- **Voyage:** vyg_mnevgate_4JifI0eef4v

## Description
For v0.5.0 of Armada, the user should be able to specify a model for each captain.  If no model is specified, the model should be selected automatically by the captain when invoked.  This will require the captain data structure to accept a "model" property which will need to be persisted in and retrieved from the underlying database.  Any changes should be made across all four supported database types (sqlite, sql server, postgres, mysql).  New columns should be added through migration code paths that run on server startup, and, a new migration script should be created in migrations/.

This field will need to be editable in the dashboard (make sure it is styled correctly!) and exposed through and manipulated through REST and MCP.  Postman, REST_API, and MCP_API will need to be updated with this new field.  

When the model is specified, it should be passed into the captain where possible through invocation of the agent.  

Adjust all versions to 0.5.0 throughout the code and markdown files.  Update the README and CHANGELOG with information about the change.  

Upon a model change, it is desired that Armada attempt to start the captain briefly to validate the existence/validity of the model, and present an error message to the user (dashboard, REST, MCP) if the specified model is invalid or unavailable.  This error message, when presented through the dashboard, should use a modal - use one of the existing modals/components for this.

The mission detail should include a total runtime field, which should be exposed in the mission detail view.  Like "model", this property will require a migration script in code, migration script change for v0.5.0, updates to the dashboard, REST_API.md, and MCP_API.md updates.

The dispatch page shows "1 task detected" followed by another text box that does nothing but repeat exactly what is in the user description text box.  Both the "1 task detected" and the subsequent textbox should be removed from the dispatch page.

Change the compose.yaml to reference image tags v0.5.0.

## Repository
- **Name:** Armada
- **Branch:** armada/claude-code-1/msn_mnevgatx_MtfVFNFoU9c
- **Default Branch:** main

## Rules
- Work only within this worktree directory
- Commit all changes to the current branch
- Commit and push your changes -- the Admiral will also push if needed
- If you encounter a blocking issue, commit what you have and exit
- Exit with code 0 on success
- Do not use extended/Unicode characters (em dashes, smart quotes, etc.) -- use only ASCII characters in all output and commit messages
- Do not use ANSI color codes or terminal formatting in output -- keep all output plain text

## Context Conservation (CRITICAL)

You have a limited context window. Exceeding it will crash your process and fail the mission. Follow these rules to stay within limits:

1. **NEVER read entire large files.** If a file is over 200 lines, read only the specific section you need using line offsets. Use grep/search to find the right section first.

2. **Read before you write, but read surgically.** Read only the 10-30 lines around the code you need to change, not the whole file.

3. **Do not explore the codebase broadly.** Only read files explicitly mentioned in your mission description. If the mission says to edit README.md, read only the section you need to edit, not the entire README.

4. **Make your changes and finish.** Do not re-read files to verify your changes, do not read files for 'context' that isn't directly needed for your edit, and do not explore related files out of curiosity.

5. **If the mission scope feels too large** (more than 8 files, or files with 500+ lines to read), commit what you have, report progress, and exit with code 0. Partial progress is better than crashing.

## Avoiding Merge Conflicts (CRITICAL)

You are one of several captains working on this repository. Other captains may be working on other missions in parallel on separate branches. To prevent merge conflicts and landing failures, you MUST follow these rules:

1. **Only modify files explicitly mentioned in your mission description.** If the description says to edit `src/routes/users.ts`, do NOT also refactor `src/routes/orders.ts` even if you notice improvements. Another captain may be working on that file.

2. **Do not make "helpful" changes outside your scope.** Do not rename shared variables, reorganize imports in files you were not asked to touch, reformat code in unrelated files, update documentation files unless instructed, or modify configuration/project files (e.g., .csproj, package.json, tsconfig.json) unless your mission specifically requires it.

3. **Do not modify barrel/index export files** (e.g., index.ts, mod.rs) unless your mission explicitly requires it. These are high-conflict files that many missions may need to touch.

4. **Keep changes minimal and focused.** The fewer files you touch, the lower the risk of conflicts. If your mission can be completed by editing 2 files, do not edit 5.

5. **If you must create new files**, prefer names that are specific to your mission's feature rather than generic names that another captain might also choose.

6. **Do not modify or delete files created by another mission's branch.** You are working in an isolated worktree -- if you see files that seem unrelated to your mission, leave them alone.

Violating these rules will cause your branch to conflict with other captains' branches during landing, resulting in a LandingFailed status and wasted work.

## Progress Signals (Optional)
You can report progress to the Admiral by printing these lines to stdout:
- `[ARMADA:PROGRESS] 50` -- report completion percentage (0-100)
- `[ARMADA:STATUS] Testing` -- transition mission to Testing status
- `[ARMADA:STATUS] Review` -- transition mission to Review status
- `[ARMADA:MESSAGE] your message here` -- send a progress message

## Model Context Updates

Model context accumulation is enabled for this vessel. Before you finish your mission, review the existing model context above (if any) and consider whether you have discovered key information that would help future agents work on this repository more effectively. Examples include: architectural insights, code style conventions, naming conventions, logging patterns, error handling patterns, testing patterns, build quirks, common pitfalls, important dependencies, interdependencies between modules, concurrency patterns, and performance considerations.

If you have useful additions, call `armada_update_vessel_context` with the `modelContext` parameter set to the COMPLETE updated model context (not just your additions -- include the existing content with your additions merged in). Be thorough -- this context is a goldmine for future agents. Focus on information that is not obvious from reading the code, and organize it clearly with sections or headings.

If you have nothing to add, skip this step.

## Existing Project Instructions

## Project Context
Armada is a multi-agent orchestration system that scales human developers with AI. It coordinates AI coding agents ("captains") to work on tasks ("missions") across git repositories ("vessels"). Written in C# (.NET), it exposes MCP tools for fleet, vessel, captain, mission, voyage, dock, signal, and merge queue management.

IMPORTANT -- Context Conservation: When using Armada MCP tools, use armada_enumerate with a small pageSize (10-25) to conserve context. Use filters (vesselId, status, date ranges) to narrow results. Only set include flags (includeDescription, includeContext, includeTestOutput, includePayload, includeMessage) to true when you specifically need that data -- by default, large fields are excluded and length hints are returned instead.

## Code Style
For C#: no var, no tuples, using statements instead of declarations, using statements inside the namespace blocks, XML documentation, public things named LikeThis, private things named _LikeThis, one entity per file, null check on set where appropriate and value-clamping to reasonable ranges where appropriate

# Mission Instructions

You are an Armada captain executing a mission. Follow these instructions carefully.

## Mission
- **Title:** Update MERGING.md, CLAUDE.md, and README.md — remove list_* references
- **ID:** msn_mms2pfqh_pt29d2cr7Eq
- **Voyage:** vyg_mms2pcu2_8pkQPfH9UGD

## Description
CONTEXT: Three additional files reference armada_list_* APIs which have been completely removed.

FILES TO MODIFY:
- docs/MERGING.md
- CLAUDE.md (project root)
- README.md (project root)

DO NOT modify any other files.

TASK 1 — docs/MERGING.md:
- Line 7: Replace `armada_list_merge_queue` in the intro paragraph with `armada_enumerate` with entityType 'merge_queue'. Example: "The merge queue is managed through MCP tools (`armada_enqueue_merge`, `armada_process_merge_queue`, `armada_enumerate` with entityType 'merge_queue', etc.)"
- Line 79: Replace the monitoring guidance. Change from mentioning both `armada_list_merge_queue` and `armada_enumerate` to just `armada_enumerate`: "Use `armada_enumerate` with entityType 'merge_queue' and status 'Failed' to check for entries that may need attention."
- Line 91: Remove `armada_list_merge_queue` from the tool reference table.
- Search the entire file for any remaining 'armada_list' references.

TASK 2 — CLAUDE.md (project root):
- Line 4: Rewrite the context conservation note. Remove all references to armada_list_*. The note currently says "prefer armada_enumerate over armada_list_* tools". Since list tools no longer exist, rewrite to simply state best practices for enumerate:
  "IMPORTANT — Context Conservation: When using Armada MCP tools, use armada_enumerate with a small pageSize (10-25) to conserve context. Use filters (vesselId, status, date ranges) to narrow results. Only set include flags (includeDescription, includeContext, includeTestOutput, includePayload, includeMessage) to true when you specifically need that data — by default, large fields are excluded and length hints are returned instead."

TASK 3 — README.md (project root):
- Line 503: Replace the tool examples. Change from mentioning `armada_list_missions` and `armada_list_events` to enumerate equivalents:
  FROM: "your MCP client can call tools like `armada_status`, `armada_dispatch`, `armada_list_missions`, `armada_cancel_voyage`, `armada_list_events`, and more."
  TO: "your MCP client can call tools like `armada_status`, `armada_dispatch`, `armada_enumerate`, `armada_voyage_status`, `armada_cancel_voyage`, and more."
- Search the entire file for any remaining 'armada_list' references.

## Repository
- **Name:** Armada
- **Branch:** armada/claude-code-1/msn_mms2pfqh_pt29d2cr7Eq
- **Default Branch:** main

## Rules
- Work only within this worktree directory
- Commit all changes to the current branch
- Commit and push your changes -- the Admiral will also push if needed
- If you encounter a blocking issue, commit what you have and exit
- Exit with code 0 on success
- Do not use extended/Unicode characters (em dashes, smart quotes, etc.) -- use only ASCII characters in all output and commit messages
- Do not use ANSI color codes or terminal formatting in output -- keep all output plain text

## Avoiding Merge Conflicts (CRITICAL)

You are one of several captains working on this repository. Other captains may be working on other missions in parallel on separate branches. To prevent merge conflicts and landing failures, you MUST follow these rules:

1. **Only modify files explicitly mentioned in your mission description.** If the description says to edit `src/routes/users.ts`, do NOT also refactor `src/routes/orders.ts` even if you notice improvements. Another captain may be working on that file.

2. **Do not make "helpful" changes outside your scope.** Do not rename shared variables, reorganize imports in files you were not asked to touch, reformat code in unrelated files, update documentation files unless instructed, or modify configuration/project files (e.g., .csproj, package.json, tsconfig.json) unless your mission specifically requires it.

3. **Do not modify barrel/index export files** (e.g., index.ts, mod.rs) unless your mission explicitly requires it. These are high-conflict files that many missions may need to touch.

4. **Keep changes minimal and focused.** The fewer files you touch, the lower the risk of conflicts. If your mission can be completed by editing 2 files, do not edit 5.

5. **If you must create new files**, prefer names that are specific to your mission's feature rather than generic names that another captain might also choose.

6. **Do not modify or delete files created by another mission's branch.** You are working in an isolated worktree -- if you see files that seem unrelated to your mission, leave them alone.

Violating these rules will cause your branch to conflict with other captains' branches during landing, resulting in a LandingFailed status and wasted work.

## Progress Signals (Optional)
You can report progress to the Admiral by printing these lines to stdout:
- `[ARMADA:PROGRESS] 50` -- report completion percentage (0-100)
- `[ARMADA:STATUS] Testing` -- transition mission to Testing status
- `[ARMADA:STATUS] Review` -- transition mission to Review status
- `[ARMADA:MESSAGE] your message here` -- send a progress message

## Existing Project Instructions

## Project Context
Armada is a multi-agent orchestration system that scales human developers with AI. It coordinates AI coding agents ("captains") to work on tasks ("missions") across git repositories ("vessels"). Written in C# (.NET), it exposes MCP tools for fleet, vessel, captain, mission, voyage, dock, signal, and merge queue management.

IMPORTANT -- Context Conservation: When using Armada MCP tools, use armada_enumerate with a small pageSize (10-25) to conserve context. Use filters (vesselId, status, date ranges) to narrow results. Only set include flags (includeDescription, includeContext, includeTestOutput, includePayload, includeMessage) to true when you specifically need that data -- by default, large fields are excluded and length hints are returned instead.

## Code Style
For C#: no var, no tuples, using statements instead of declarations, using statements inside the namespace blocks, XML documentation, public things named LikeThis, private things named _LikeThis, one entity per file, null check on set where appropriate and value-clamping to reasonable ranges where appropriate

# Mission Instructions

You are an Armada captain executing a mission. Follow these instructions carefully.

## Mission
- **Title:** Add missing merge queue MCP tools: delete and purge
- **ID:** msn_mmodt5yk_3G3El3YyMEK

## Description
The merge queue is missing public API/MCP tools for cleanup. Missions have `armada_purge_mission`, voyages have `armada_purge_voyage`, but there are NO equivalent tools for merge queue entries. The internal `MergeQueueService.DeleteAsync()` method exists but is not exposed.

## What to implement

### 1. `armada_delete_merge` MCP tool
- Deletes a single merge queue entry by ID
- Only allows deletion of terminal entries (Landed, Failed, Cancelled)
- Calls the existing `MergeQueueService.DeleteAsync()` method
- Parameter: `entryId` (string, required, mrg_ prefix)
- Follow the exact pattern of `armada_purge_mission` for implementation

### 2. `armada_purge_merge_queue` MCP tool  
- Bulk purge of all terminal merge queue entries (Landed, Failed, Cancelled)
- Optional `vesselId` filter to purge only entries for a specific vessel
- Optional `status` filter (e.g. only purge "Failed" entries)
- Returns count of entries deleted
- Implementation: query all terminal entries matching filters, call DeleteAsync for each

### Key files to modify

1. **`src/Armada.Server/Mcp/McpToolRegistrar.cs`** — Register the two new MCP tools following the existing pattern (look at how `armada_purge_mission` and `armada_purge_voyage` are registered)

2. **`src/Armada.Server/Mcp/McpToolHandler.cs`** (or wherever tool handlers live) — Add handler methods for the two new tools, calling into MergeQueueService

3. **`src/Armada.Core/Services/MergeQueueService.cs`** — May need a new `PurgeAllAsync()` or `DeleteAllTerminalAsync(string vesselId = null, string status = null)` method for the bulk purge. The existing `DeleteAsync` handles single entries.

4. **`src/Armada.Core/Services/Interfaces/IMergeQueueService.cs`** — Add interface method if new service method is added

5. **`MCP.md`** — Update the MCP documentation to include the two new tools with their parameters, descriptions, and examples. Follow the existing documentation format for other tools.

### Implementation guidance

- Study how `armada_purge_mission` is implemented end-to-end (registration → handler → service call) and replicate the exact same pattern
- Study how `armada_cancel_merge` is implemented since it's the closest existing merge queue MCP tool
- The `DeleteAsync` method already handles git branch cleanup (local + remote), so leverage it
- Ensure proper error handling: return clear error if entry not found or not in terminal state
- Follow the project's style guide: no var, XML docs, PascalCase public, _PascalCase private

## Repository
- **Name:** Armada
- **Branch:** armada/claude-code-1/msn_mmodt5yk_3G3El3YyMEK
- **Default Branch:** main

## Rules
- Work only within this worktree directory
- Commit all changes to the current branch
- Commit and push your changes — the Admiral will also push if needed
- If you encounter a blocking issue, commit what you have and exit
- Exit with code 0 on success

## Progress Signals (Optional)
You can report progress to the Admiral by printing these lines to stdout:
- `[ARMADA:PROGRESS] 50` — report completion percentage (0-100)
- `[ARMADA:STATUS] Testing` — transition mission to Testing status
- `[ARMADA:STATUS] Review` — transition mission to Review status
- `[ARMADA:MESSAGE] your message here` — send a progress message

## Existing Project Instructions

# Armada - Claude Code Instructions

## Project
Multi-agent orchestration system for scaling human developers with AI. C#/.NET.

## Build
```bash
dotnet build src/Armada.sln
```

## Test
```bash
dotnet run --project test/Armada.Test.Automated --framework net10.0
dotnet run --project test/Armada.Test.Unit --framework net10.0
dotnet run --project test/Armada.Test.Runtimes --framework net10.0
```

## Architecture
- `Armada.Core` - Domain models, database interfaces, service interfaces, settings
- `Armada.Runtimes` - Agent runtime adapters (Claude Code, Codex, extensible via IAgentRuntime)
- `Armada.Server` - Admiral process: REST API (SwiftStack), MCP server (Voltaic), WebSocket, web dashboard
- `Armada.Helm` - CLI (Spectre.Console), thin HTTP client to Admiral

## Coding Standards

### Naming
- Private fields: `_PascalCase` (e.g., `_Database`, `_Logging`)
- No `var` keyword - always use explicit types
- Async methods: suffix with `Async`, include `CancellationToken token = default`
- Use `.ConfigureAwait(false)` in library code (Core, Runtimes)
- Enums: PascalCase with `Enum` suffix, decorated with `[JsonConverter(typeof(JsonStringEnumConverter))]`
- ID prefixes: flt_, vsl_, cpt_, msn_, vyg_, dck_, sig_, art_

### Language Restrictions
- **No `var`** - always use explicit types (e.g., `List<Fleet> fleets = ...` not `var fleets = ...`)
- **No tuples** - define a class or use out parameters instead of `(string, int)` or `ValueTuple`
- **No direct `JsonElement` access** - always deserialize JSON into a strongly-typed class instance (e.g., `JsonSerializer.Deserialize<Fleet>(json)`) rather than using `GetProperty()` / `GetString()` on `JsonElement`
- **XML documentation** - all public members must have `<summary>` XML doc comments

### File Organization
- One class per file, filename matches class name
- Use `#region` blocks: Public-Members, Private-Members, Constructors-and-Factories, Public-Methods, Private-Methods
- `using` statements go **inside** the `namespace` block, not above it
- Using order: System first, then third-party, then project namespaces

### Patterns
- Constructor injection with null checks: `?? throw new ArgumentNullException(nameof(x))`
- Logging: SyslogLogging with `private string _Header = "[ClassName] ";`
- Database: interface-per-entity pattern (IFleetMethods, IVesselMethods, etc.)
- Settings: nested config objects with validation in setters

### Libraries (use these, they are mine)
- SwiftStack (NuGet) - REST API framework
- Voltaic (NuGet) - MCP/JSON-RPC library
- SyslogLogging (NuGet) - Logging
- PrettyId (NuGet) - ID generation with prefixes

## Key Concepts
- Admiral = coordinator process
- Captain = worker agent (Claude Code, Codex, etc.)
- Fleet = collection of repositories
- Vessel = single git repository
- Mission = atomic work unit
- Voyage = batch of related missions
- Dock = git worktree for a captain
- Signal = message between admiral and captains
