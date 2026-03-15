# Learned Context Feature — Implementation Plan

Accumulated, LLM-managed context on vessels. After each mission completes, the captain
summarizes what it learned about the project and compacts it into a persistent
`LearnedContext` field on the vessel. Future missions receive this context automatically.

---

## Phase 1: Data Model & Database

### 1.1 Add fields to Vessel model
- [ ] **File:** `src/Armada.Core/Models/Vessel.cs`
- [ ] Add `LearnedContext` property (string?, default null)
  ```csharp
  /// <summary>
  /// LLM-accumulated context about the project — architecture discoveries, build quirks,
  /// patterns, and conventions learned from completed missions. Managed by captains
  /// when EnableLlmContext is true; editable by users in the dashboard.
  /// </summary>
  public string? LearnedContext { get; set; } = null;
  ```
- [ ] Add `EnableLlmContext` property (bool, default true)
  ```csharp
  /// <summary>
  /// When true, captains will summarize project learnings after each mission and
  /// update LearnedContext automatically. Default: true.
  /// </summary>
  public bool EnableLlmContext { get; set; } = true;
  ```

### 1.2 Database migrations — all four drivers

Each driver needs a new migration adding both columns to the `vessels` table.

- [ ] **SQLite** — `src/Armada.Core/Database/Sqlite/Queries/TableQueries.cs`
  - Add a new `SchemaMigration` entry (next available number) with:
    ```sql
    ALTER TABLE vessels ADD COLUMN learned_context TEXT;
    ALTER TABLE vessels ADD COLUMN enable_llm_context INTEGER NOT NULL DEFAULT 1;
    ```

- [ ] **MySQL** — `src/Armada.Core/Database/Mysql/Queries/TableQueries.cs`
  - Add migration:
    ```sql
    ALTER TABLE vessels ADD COLUMN learned_context LONGTEXT;
    ALTER TABLE vessels ADD COLUMN enable_llm_context TINYINT(1) NOT NULL DEFAULT 1;
    ```
  - Also add the columns to the initial `Vessels` CREATE TABLE string for fresh installs.

- [ ] **PostgreSQL** — `src/Armada.Core/Database/Postgresql/Queries/TableQueries.cs`
  - Add migration:
    ```sql
    ALTER TABLE vessels ADD COLUMN learned_context TEXT;
    ALTER TABLE vessels ADD COLUMN enable_llm_context BOOLEAN NOT NULL DEFAULT TRUE;
    ```
  - Also update the initial CREATE TABLE if PostgreSQL uses one.

- [ ] **SQL Server** — `src/Armada.Core/Database/SqlServer/Queries/TableQueries.cs`
  - Add migration:
    ```sql
    ALTER TABLE vessels ADD learned_context NVARCHAR(MAX);
    ALTER TABLE vessels ADD enable_llm_context BIT NOT NULL DEFAULT 1;
    ```
  - Also update the initial CREATE TABLE if SQL Server uses one.

### 1.3 Update VesselMethods in all four drivers

For each driver (Sqlite, Mysql, Postgresql, SqlServer), update the `VesselMethods` implementation:

- [ ] **CreateAsync** — add `learned_context` and `enable_llm_context` to INSERT columns and parameter bindings
- [ ] **ReadAsync** — read `learned_context` and `enable_llm_context` from the data reader and populate the Vessel object
- [ ] **UpdateAsync** — add `learned_context` and `enable_llm_context` to the UPDATE SET clause and parameter bindings
- [ ] **EnumerateAsync / EnumerateByFleetAsync** — ensure the reader mapping includes the new columns

Files to update:
- [ ] `src/Armada.Core/Database/Sqlite/Implementations/VesselMethods.cs`
- [ ] `src/Armada.Core/Database/Mysql/Implementations/VesselMethods.cs`
- [ ] `src/Armada.Core/Database/Postgresql/Implementations/VesselMethods.cs`
- [ ] `src/Armada.Core/Database/SqlServer/Implementations/VesselMethods.cs`

---

## Phase 2: CLAUDE.md Generation — Inject Learned Context

### 2.1 Add LearnedContext section to generated CLAUDE.md
- [ ] **File:** `src/Armada.Core/Services/MissionService.cs` — `GenerateClaudeMdAsync` method (line ~419)
- [ ] Insert a `## Learned Context` section **after** the StyleGuide section and **before** Mission Instructions. This positions accumulated knowledge alongside the other context fields but keeps it distinct from human-authored ProjectContext.
  ```csharp
  if (!String.IsNullOrEmpty(vessel.LearnedContext))
  {
      content +=
          "## Learned Context\n" +
          "The following was accumulated from previous missions on this repository. " +
          "It may contain architecture notes, build instructions, testing quirks, " +
          "or other observations. Treat it as helpful but potentially outdated.\n\n" +
          vessel.LearnedContext + "\n" +
          "\n";
  }
  ```

### 2.2 Add context-update instructions to CLAUDE.md (when enabled)
- [ ] **File:** `src/Armada.Core/Services/MissionService.cs` — `GenerateClaudeMdAsync` method
- [ ] When `vessel.EnableLlmContext` is true, append an additional section to the Rules or add a new section after Progress Signals explaining the context-update step:
  ```
  ## Context Accumulation
  After completing your mission work, you will be asked to summarize what you
  learned about this project. This helps future missions work more effectively.
  Focus on non-obvious discoveries: build steps, architectural patterns, testing
  requirements, dependency quirks, or anything that would save a future captain time.
  ```
  This is informational only — the actual summarization is driven by a second prompt (Phase 3).

---

## Phase 3: Post-Mission Context Summarization

This is the core feature. After a mission completes successfully (exit code 0), a second
short-lived agent invocation asks the captain to produce an updated LearnedContext.

### 3.1 Add summarization prompt constant
- [ ] **File:** `src/Armada.Core/Services/MissionService.cs` (or a new `ContextSummarization.cs` in the same directory — developer's choice)
- [ ] Define the summarization prompt as a static string. The prompt must:
  1. Include the current `LearnedContext` (if any) so updates are incremental
  2. Include the mission title and description for context on what was just done
  3. Include the diff summary (use `DiffSnapshot` — already captured before this step)
  4. Instruct the LLM to output ONLY the updated context, nothing else
  5. Enforce the ~2000 token limit and compaction

  ```
  Suggested prompt (adapt as needed):

  You just completed a mission on this repository. Your task now is to update
  the project's learned context — a persistent knowledge base that helps future
  missions work more effectively.

  CURRENT LEARNED CONTEXT:
  ---
  {existingLearnedContext ?? "(none — this is the first entry)"}
  ---

  MISSION JUST COMPLETED:
  Title: {mission.Title}
  Description: {mission.Description}

  CHANGES MADE (diff summary):
  {diffSnapshot, truncated to first ~3000 chars if very large}

  INSTRUCTIONS:
  1. Review what you learned about this project during your mission.
  2. Merge any new, non-obvious insights into the existing learned context.
     Focus on: architecture patterns, build/test commands, dependency quirks,
     file organization conventions, gotchas, and anything that would save
     a future developer time.
  3. Do NOT include: mission-specific details, task descriptions, commit
     messages, or anything that only applies to the work you just did.
  4. COMPACT the result: remove redundancies, merge related points, drop
     anything that is now outdated based on the changes you just made.
  5. Keep the total output under 2000 tokens (~1500 words). If the context
     is growing too large, prioritize the most broadly useful information
     and drop niche details.
  6. Output ONLY the updated learned context text. No preamble, no
     explanation, no markdown headers, no code fences around the whole
     output. Just the context itself, using concise bullet points or
     short paragraphs.
  ```

### 3.2 Implement the summarization invocation
- [ ] **File:** `src/Armada.Server/ArmadaServer.cs`
- [ ] Add a new private method `HandleContextSummarizationAsync(Mission mission, Dock dock, Vessel vessel)`
- [ ] This method:
  1. Checks `vessel.EnableLlmContext` — if false, return immediately
  2. Reads the current `vessel.LearnedContext` from the database
  3. Builds the summarization prompt (from 3.1) using mission details and `mission.DiffSnapshot`
  4. Truncates the diff to a reasonable size (~3000 chars) to keep the prompt manageable
  5. Launches a new agent process via the same runtime (ClaudeCodeRuntime) in the same dock/worktree:
     - Working directory: `dock.WorktreePath`
     - Prompt: the summarization prompt
     - The process output is captured to a separate log file (e.g., `{missionId}-context.log`)
  6. Waits for the process to exit (with a timeout — suggest 120 seconds)
  7. Reads the output from the log file
  8. Strips any preamble/postamble the LLM may have added (basic cleanup)
  9. Updates `vessel.LearnedContext` with the output
  10. Saves the vessel via `_Database.Vessels.UpdateAsync(vessel)`
  11. Logs the update

  **Important implementation notes:**
  - This must run AFTER `HandleCaptureDiffAsync` (so `mission.DiffSnapshot` is available)
  - This must run BEFORE `ReclaimDockAsync` (so the worktree still exists)
  - If the summarization fails or times out, log a warning and continue — do NOT
    block mission completion or leave the captain stuck
  - The captain's process tracking maps (`_ProcessToCaptain`, `_ProcessToMission`) should
    NOT be updated for this secondary invocation — it's a fire-and-forget summarization,
    not a tracked mission

### 3.3 Wire summarization into the completion flow
- [ ] **File:** `src/Armada.Core/Services/MissionService.cs` — `HandleCompletionAsync` method (line ~280)
  OR
  **File:** `src/Armada.Server/ArmadaServer.cs` — `HandleMissionCompleteAsync` method (line ~2807)
- [ ] Insert the summarization call at the right point in the completion sequence:
  ```
  Current flow:
    1. Mark mission WorkProduced
    2. HandleCaptureDiffAsync (capture diff + commit hash)
    3. HandleMissionCompleteAsync (push, PR, merge, etc.)
    4. ReclaimDockAsync (clean up worktree)
    5. Release captain to Idle

  New flow:
    1. Mark mission WorkProduced
    2. HandleCaptureDiffAsync (capture diff + commit hash)
    3. >>> HandleContextSummarizationAsync <<<  (NEW — uses worktree + diff)
    4. HandleMissionCompleteAsync (push, PR, merge, etc.)
    5. ReclaimDockAsync (clean up worktree)
    6. Release captain to Idle
  ```
  The summarization runs BEFORE landing (push/PR) because:
  - The worktree is still available for the agent to reference files if needed
  - It doesn't affect the git state (no commits, just reading + summarizing)
  - If it fails, landing still proceeds normally

### 3.4 Handle the summarization agent output
- [ ] The summarization agent runs as `--print` mode (same as missions), so its output goes to stdout
- [ ] Capture output via a dedicated log file, then read it back after the process exits
- [ ] Alternative approach: if the runtime supports capturing stdout directly into a string (instead of file), use that. Otherwise, read from `{missionId}-context.log` after exit.
- [ ] Apply basic sanitization to the output:
  - Trim leading/trailing whitespace
  - Remove any leading "Here is the updated context:" type preamble
  - Remove trailing "Let me know if..." type postamble
  - If the output is empty or clearly malformed, skip the update and log a warning

---

## Phase 4: MCP Tool & API Updates

### 4.1 Update the MCP vessel context tool
- [ ] **File:** `src/Armada.Server/Mcp/VesselContextArgs.cs`
- [ ] Add `LearnedContext` property:
  ```csharp
  /// <summary>
  /// LLM-accumulated learned context about the project.
  /// </summary>
  public string? LearnedContext { get; set; }
  ```

- [ ] **File:** `src/Armada.Server/Mcp/McpToolRegistrar.cs` — `armada_update_vessel_context` registration (line ~511)
- [ ] Add `learnedContext` to the schema properties and to the handler logic:
  ```csharp
  learnedContext = new { type = "string", description = "LLM-accumulated learned context" }
  ```
  In the handler:
  ```csharp
  if (request.LearnedContext != null)
      vessel.LearnedContext = request.LearnedContext;
  ```

### 4.2 Update REST API context endpoint
- [ ] **File:** `src/Armada.Server/ArmadaServer.cs` — PATCH `/api/v1/vessels/{id}/context` (line ~857)
- [ ] Add handling for `LearnedContext` in the patch logic:
  ```csharp
  if (patch.LearnedContext != null)
      existing.LearnedContext = patch.LearnedContext;
  ```
- [ ] The existing PUT `/api/v1/vessels/{id}` already does a full replace, so it will
  naturally pick up the new fields as long as the model serialization is correct.

### 4.3 Update WebSocket handler
- [ ] **File:** `src/Armada.Server/WebSocket/ArmadaWebSocketHub.cs` — `update_vessel_context` case (line ~382)
- [ ] Add handling for `LearnedContext`:
  ```csharp
  if (ctxPatch.LearnedContext != null)
      ctxVessel.LearnedContext = ctxPatch.LearnedContext;
  ```

---

## Phase 5: Dashboard UI

### 5.1 Update vessel create modal
- [ ] **File:** `src/Armada.Server/wwwroot/index.html` — create-vessel modal (line ~1684)
- [ ] Add the `EnableLlmContext` toggle after the Style Guide textarea:
  ```html
  <div class="form-group">
      <label class="toggle-label">
          <input type="checkbox" x-model="modalData.enableLlmContext">
          Enable LLM Context Learning
      </label>
      <small class="form-help">When enabled, captains will automatically summarize what they learn about this project after each mission.</small>
  </div>
  ```
- [ ] No LearnedContext textarea on the create modal (it starts empty).

### 5.2 Update vessel edit modal
- [ ] **File:** `src/Armada.Server/wwwroot/index.html` — edit-vessel modal (line ~1719)
- [ ] Add the `EnableLlmContext` toggle (same as create modal):
  ```html
  <div class="form-group">
      <label class="toggle-label">
          <input type="checkbox" x-model="modalData.enableLlmContext">
          Enable LLM Context Learning
      </label>
      <small class="form-help">When enabled, captains will automatically summarize what they learn about this project after each mission.</small>
  </div>
  ```
- [ ] Add the `LearnedContext` textarea AFTER the toggle:
  ```html
  <div class="form-group">
      <label>Learned Context</label>
      <small class="form-help">Accumulated by captains from completed missions. You can edit or clear this at any time — your edits are authoritative until the next mission updates it.</small>
      <textarea x-model="modalData.learnedContext" rows="6" placeholder="(populated automatically by captains after missions complete)"></textarea>
      <span class="char-count" x-text="(modalData.learnedContext || '').length + ' chars'"></span>
  </div>
  ```

### 5.3 Update JavaScript data binding
- [ ] **File:** `src/Armada.Server/wwwroot/js/dashboard.js`
- [ ] Update `openCreateVessel` (line ~2139) — add defaults:
  ```javascript
  enableLlmContext: true, learnedContext: ''
  ```
- [ ] Update `openEditVessel` (line ~2140) — map from vessel data:
  ```javascript
  enableLlmContext: v.enableLlmContext !== false, learnedContext: v.learnedContext || ''
  ```
- [ ] Update `createVessel` API call (line ~1539) — include new fields in POST body:
  ```javascript
  enableLlmContext: this.modalData.enableLlmContext,
  learnedContext: this.modalData.learnedContext || null
  ```
- [ ] Update `saveVesselEdit` API call (line ~1560) — include new fields in PUT body:
  ```javascript
  enableLlmContext: this.modalData.enableLlmContext,
  learnedContext: this.modalData.learnedContext || null
  ```

### 5.4 Update vessel detail view
- [ ] **File:** `src/Armada.Server/wwwroot/index.html` — vessel detail section (line ~474)
- [ ] Add a LearnedContext display block alongside ProjectContext and StyleGuide:
  ```html
  <div class="detail-context-section" x-show="detail.learnedContext">
      <h4>Learned Context</h4>
      <pre class="detail-context-block" x-text="detail.learnedContext"></pre>
  </div>
  ```

### 5.5 Update vessel list badges
- [ ] **File:** `src/Armada.Server/wwwroot/index.html` — vessel table badges (line ~385)
- [ ] Add a badge for LearnedContext:
  ```html
  <span class="vessel-badge" x-show="v.learnedContext" title="Has Learned Context">LRN</span>
  ```

### 5.6 Add toggle styling (if not already present)
- [ ] **File:** `src/Armada.Server/wwwroot/css/dashboard.css`
- [ ] Add toggle label style if needed:
  ```css
  .toggle-label {
      display: flex;
      align-items: center;
      gap: 0.5rem;
      cursor: pointer;
      font-size: 0.9rem;
      color: var(--text);
  }
  .toggle-label input[type="checkbox"] {
      width: 1rem;
      height: 1rem;
      accent-color: var(--accent);
  }
  ```

---

## Phase 6: JSON Serialization

### 6.1 Verify JSON property naming
- [ ] Check how Vessel properties are serialized to JSON. Armada likely uses camelCase
  serialization (based on dashboard JS using `v.projectContext`, `v.styleGuide`).
- [ ] Ensure `LearnedContext` serializes as `learnedContext` and `EnableLlmContext` as
  `enableLlmContext`. If the project uses `JsonPropertyName` attributes or a global
  naming policy, follow the same pattern.
- [ ] If the project uses `System.Text.Json` with a `JsonNamingPolicy.CamelCase` policy,
  no changes needed. If it uses explicit attributes, add:
  ```csharp
  [JsonPropertyName("learnedContext")]
  public string? LearnedContext { get; set; }

  [JsonPropertyName("enableLlmContext")]
  public bool EnableLlmContext { get; set; } = true;
  ```

---

## Implementation Order & Dependencies

```
Phase 1 (Data Model + DB)  ─── no dependencies, do first
    │
    ├── Phase 2 (CLAUDE.md injection)  ─── depends on Phase 1
    │
    ├── Phase 4 (MCP/API/WebSocket)    ─── depends on Phase 1
    │
    ├── Phase 5 (Dashboard UI)         ─── depends on Phase 1 + 4
    │
    └── Phase 6 (JSON serialization)   ─── depends on Phase 1
         │
         └── Phase 3 (Summarization)   ─── depends on Phase 1 + 2 + 6
```

Phases 2, 4, 5, and 6 can be done in parallel after Phase 1.
Phase 3 should be done last as it depends on everything else being wired up.

---

## Testing Checklist

- [ ] Create a new vessel with `enableLlmContext: true` — verify DB has the column, API returns it
- [ ] Create a new vessel with `enableLlmContext: false` — verify it persists
- [ ] Edit a vessel's LearnedContext in the modal — verify it saves and displays in detail view
- [ ] Clear LearnedContext (blank the field) — verify it saves as null/empty
- [ ] Run a mission on a vessel with `enableLlmContext: true`:
  - Verify the summarization prompt runs after diff capture
  - Verify LearnedContext is populated on the vessel after mission completes
  - Verify the next mission's CLAUDE.md includes the "## Learned Context" section
- [ ] Run a mission on a vessel with `enableLlmContext: false`:
  - Verify no summarization prompt runs
  - Verify CLAUDE.md does not include a Learned Context section
- [ ] Run multiple sequential missions — verify context is compacted/merged, not just appended
- [ ] Verify summarization timeout — if the LLM hangs, mission completion should still proceed after 120s
- [ ] Verify summarization failure — if the process crashes, mission completion should proceed normally
- [ ] Verify LRN badge appears in vessel list when LearnedContext is populated
- [ ] Verify all four database drivers work (if multi-driver testing is feasible)
- [ ] Verify the MCP tool `armada_update_vessel_context` can set/clear LearnedContext
- [ ] Edit LearnedContext manually, then run a mission — verify the captain's summarization
  merges new info into your edited version (not overwriting from scratch)

---

## Risk Notes

| Risk | Mitigation |
|------|------------|
| Summarization adds latency to mission completion | Keep timeout at 120s; on failure, skip silently |
| Context grows stale as codebase evolves | The compaction prompt instructs the LLM to drop outdated info based on the diff it just made |
| Context bloats despite compaction prompt | The 2000-token limit in the prompt acts as a soft cap; monitor in practice |
| Summarization LLM produces garbage output | Basic sanitization + if output is empty/malformed, skip the update |
| Second agent process inherits mission CLAUDE.md | This is acceptable — the context + rules won't interfere with a summarization-only task |
| User edits get overwritten | By design: user edits are authoritative until the next mission updates it. The summarization prompt receives the current value (including user edits) and merges into it. |

---

## Future Considerations (Out of Scope)

These are NOT part of this implementation but worth noting for later:

- **Voyage-level context**: Accumulate context across all missions in a voyage, then merge into vessel context at voyage completion
- **Context versioning**: Store a history of LearnedContext changes with timestamps and mission IDs
- **Selective context**: Tag context entries by area (build, test, architecture) and only inject relevant ones per mission
- **Context quality scoring**: Track whether missions that received learned context performed better (fewer retries, faster completion)
- **Fleet-level context**: Share common learnings across all vessels in a fleet
- **Automatic staleness detection**: Invalidate context entries when the files they reference are significantly changed
