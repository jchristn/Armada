# Armada MCP API — Large Response Risk Analysis & Remediation Plan

> **Date:** 2026-03-15
> **Purpose:** Identify every API where the response payload could be excessive, consuming context window and increasing cost. Provide an actionable remediation plan.
>
> **Implementation Voyage:** `vyg_mms2pcu2_8pkQPfH9UGD` (dispatched 2026-03-15T18:13Z)
> **Status:** ✅ All 8 missions complete — ready to land

---

## Risk Rating Legend

| Rating | Description |
|--------|-------------|
| **CRITICAL** | Known to produce responses that exceed MCP token limits (>60k chars) or regularly produce >10k token responses |
| **HIGH** | Likely to produce multi-thousand-token responses under normal usage at scale |
| **MEDIUM** | Can produce large responses under specific conditions |
| **LOW** | Responses are inherently bounded and small |
| **NEGLIGIBLE** | Fixed/tiny responses (single object, confirmation, scalar) |
| **ACCEPTED** | Large by design; caller explicitly requests heavy data |

---

## Current State — Risk Summary

| # | API | Risk | Trigger Condition | Observed Max |
|---|-----|------|-------------------|-------------|
| 1 | `armada_enumerate` | **CRITICAL** | Any entity type with many records; pageSize up to 1000 | **995,738 chars** |
| 2 | `armada_voyage_status` | **CRITICAL** | Voyage with many missions, each carrying full description + log excerpts | **70,741 chars** |
| 3 | `armada_list_missions` | **CRITICAL** | No status filter, or popular status with many missions; returns ALL missions with full descriptions | Unbounded |
| 4 | `armada_list_merge_queue` | **HIGH** | Accumulated queue entries; no pagination, no filters; each entry includes TestOutput | **~10.8k tokens** |
| 5 | `armada_get_mission_diff` | **HIGH** | Mission that touched many files or generated large diffs | Unbounded (size of git diff) |
| 6 | `armada_get_mission_log` | **HIGH** | Default 100 lines; lines can be arbitrarily long (full agent transcript) | Unbounded (line length) |
| 7 | `armada_get_captain_log` | **HIGH** | Same as mission log — default 100 lines of agent transcript | Unbounded (line length) |
| 8 | `armada_list_events` | **HIGH** | Default limit 50; event payloads can include full mission/voyage metadata | Potentially large |
| 9 | `armada_list_signals` | **HIGH** | No pagination; signals carry arbitrary-length message bodies | Unbounded |
| 10 | `armada_list_voyages` | **HIGH** | No pagination; each voyage includes description + mission summary data | Grows with usage |
| 11 | `armada_status` | **MEDIUM** | Aggregates all active work; grows with number of active missions/captains/voyages | Grows with scale |
| 12 | `armada_mission_status` | **MEDIUM** | Single mission, but description field can be very long; may include inline context | Variable |
| 13 | `armada_list_vessels` | **MEDIUM** | No pagination; vessels carry `projectContext` and `styleGuide` (potentially multi-KB each) | Grows with vessels |
| 14 | `armada_list_captains` | **MEDIUM** | No pagination; captain objects include state metadata | Moderate |
| 15 | `armada_list_docks` | **MEDIUM** | No pagination; many concurrent worktrees across vessels | Moderate |
| 16 | `armada_get_vessel` | **MEDIUM** | Single vessel, but `projectContext` and `styleGuide` can be very large | Variable |
| 17 | `armada_get_fleet` | **MEDIUM** | Returns fleet with embedded vessel list (each vessel has context + style guide) | Grows with fleet size |
| 18 | `armada_list_fleets` | **LOW** | Typically few fleets; minimal per-fleet data | Small |
| 19 | `armada_get_captain` | **LOW** | Single captain object | Small |
| 20 | `armada_get_merge_entry` | **LOW** | Single entry, but `TestOutput` field could be large | Variable |
| 21 | `armada_process_merge_queue` | **MEDIUM** | Processes ALL queued entries; returns aggregate results with TestOutput for each | Grows with queue |
| 22 | `armada_process_merge_entry` | **LOW** | Returns merge result; `TestOutput` could be large | Variable |

All remaining APIs (create, update, cancel, purge, delete, transition, restart, send_signal, stop, backup, restore, echo, ping, getTime, getSessions) are **NEGLIGIBLE** — fixed-size confirmations or single-object returns.

---

## List API → Enumerate Replacement Matrix

Every `list_*` API has an `armada_enumerate` equivalent. `armada_enumerate` provides **pagination** (`pageSize`, `pageNumber`), **sorting** (`order`), and **time-range filtering** (`createdAfter`, `createdBefore`) universally. None of the `list_*` APIs offer any of these.

| List API | Enumerate `entityType` | List Filters | Enumerate Filters | Parity | Replacement? |
|----------|----------------------|--------------|-------------------|--------|-------------|
| `armada_list_fleets` | `fleets` | _(none)_ | _(none entity-specific)_ | ✅ Full | ✅ **YES** |
| `armada_list_vessels` | `vessels` | _(none)_ | `fleetId` | ✅ Superset | ✅ **YES** |
| `armada_list_captains` | `captains` | _(none)_ | `status` | ✅ Superset | ✅ **YES** |
| `armada_list_missions` | `missions` | `status` | `status`, `vesselId`, `voyageId`, `captainId` | ✅ Superset | ✅ **YES** |
| `armada_list_voyages` | `voyages` | `status` | `status` | ✅ Full | ✅ **YES** |
| `armada_list_docks` | `docks` | `vesselId` | `vesselId` | ✅ Full | ✅ **YES** |
| `armada_list_events` | `events` | `missionId`, `voyageId`, `captainId`, `limit` | `missionId`, `voyageId`, `captainId`, `eventType` | ✅ Superset | ✅ **YES** |
| `armada_list_signals` | `signals` | `captainId` | `captainId`, `toCaptainId`, `signalType`, `unreadOnly` | ✅ Superset | ✅ **YES** |
| `armada_list_merge_queue` | `merge_queue` | _(none)_ | `status`, `vesselId` | ✅ Superset | ✅ **YES** |

**All 9 list APIs have a viable enumerate replacement with zero filter gaps.**

---

## Remediation Plan

### Overview

Three changes eliminate all CRITICAL and HIGH risks:

1. **Remove all `list_*` APIs** — replace with `armada_enumerate` (which already has pagination + filters)
2. **Harden `armada_enumerate`** — reduce default page size, add boolean flags to control inclusion of large fields
3. **Harden `armada_voyage_status`** — add summary mode and boolean flags for subordinate data

### Target State After Remediation

| # | API | Before | After | Change |
|---|-----|--------|-------|--------|
| 1 | `armada_enumerate` | **CRITICAL** | **LOW** | Default pageSize 25; large fields excluded by default |
| 2 | `armada_voyage_status` | **CRITICAL** | **LOW** | Summary mode default; opt-in to subordinate data |
| 3 | `armada_list_missions` | **CRITICAL** | — | **Removed** |
| 4 | `armada_list_merge_queue` | **HIGH** | — | **Removed** |
| 5 | `armada_get_mission_diff` | **HIGH** | **ACCEPTED** | Large by design — no change needed |
| 6 | `armada_get_mission_log` | **HIGH** | **ACCEPTED** | Large by design — no change needed |
| 7 | `armada_get_captain_log` | **HIGH** | **ACCEPTED** | Large by design — no change needed |
| 8 | `armada_list_events` | **HIGH** | — | **Removed** |
| 9 | `armada_list_signals` | **HIGH** | — | **Removed** |
| 10 | `armada_list_voyages` | **HIGH** | — | **Removed** |
| 11 | `armada_status` | **MEDIUM** | **MEDIUM** | Unchanged (future: add scope filters) |
| 12 | `armada_mission_status` | **MEDIUM** | **MEDIUM** | Unchanged (single entity, acceptable) |
| 13 | `armada_list_vessels` | **MEDIUM** | — | **Removed** |
| 14 | `armada_list_captains` | **MEDIUM** | — | **Removed** |
| 15 | `armada_list_docks` | **MEDIUM** | — | **Removed** |
| 16 | `armada_get_vessel` | **MEDIUM** | **MEDIUM** | Unchanged (single entity, acceptable) |
| 17 | `armada_get_fleet` | **MEDIUM** | **MEDIUM** | Unchanged (single entity, acceptable) |
| 18 | `armada_list_fleets` | **LOW** | — | **Removed** |
| 19 | `armada_get_merge_entry` | **LOW** | **LOW** | Unchanged |
| 20 | `armada_process_merge_queue` | **MEDIUM** | **MEDIUM** | Unchanged (future: summary-only response) |
| 21 | `armada_process_merge_entry` | **LOW** | **LOW** | Unchanged |

**Risk reduction summary:**

| Risk Level | Before | After | Delta |
|------------|--------|-------|-------|
| **CRITICAL** | 3 | **0** | −3 |
| **HIGH** | 7 | **0** | −7 |
| **MEDIUM** | 7 | 5 | −2 |
| **LOW** | 3 | 3 | 0 |
| **ACCEPTED** | 0 | 3 | +3 |
| **Removed** | 0 | 9 | +9 |

---

## Work Items

### WI-1: Remove all `list_*` APIs from server code

Remove the 9 list endpoints entirely. This is **complete removal**, not deprecation — no `list_*` tool should exist after this work.

#### WI-1a: MCP tool registrations (`src/Armada.Server/Mcp/McpToolRegistrar.cs`)

| # | Task | Status | Notes |
|---|------|--------|-------|
| WI-1a.1 | Remove `armada_list_fleets` tool registration (~line 197) | ⬜ Todo | |
| WI-1a.2 | Remove `armada_list_vessels` tool registration (~line 344) | ⬜ Todo | |
| WI-1a.3 | Remove `armada_list_captains` tool registration (~line 1247) | ⬜ Todo | |
| WI-1a.4 | Remove `armada_list_missions` tool registration (~line 788) | ⬜ Todo | |
| WI-1a.5 | Remove `armada_list_voyages` tool registration (~line 584) | ⬜ Todo | |
| WI-1a.6 | Remove `armada_list_docks` tool registration (~line 1690) | ⬜ Todo | |
| WI-1a.7 | Remove `armada_list_events` tool registration (~line 1588) | ⬜ Todo | |
| WI-1a.8 | Remove `armada_list_signals` tool registration (~line 1499) | ⬜ Todo | |
| WI-1a.9 | Remove `armada_list_merge_queue` tool registration (~line 1823) | ⬜ Todo | |
| WI-1a.10 | Update `armada_enumerate` description (~line 116) to remove "Use this instead of armada_list_*" phrasing — enumerate is now the only option, no comparison needed | ⬜ Todo | |

#### WI-1b: Server-side handler methods

| # | Task | Status | Notes |
|---|------|--------|-------|
| WI-1b.1 | Remove MCP handler methods for all 9 list tools | ⬜ Todo | Keep internal service methods if used by enumerate or other handlers |
| WI-1b.2 | Remove any routing/dispatch logic for list tool names | ⬜ Todo | |

#### WI-1c: Embedded system prompt (`src/Armada.Helm/Commands/McpInstallCommand.cs`)

| # | Task | Status | Notes |
|---|------|--------|-------|
| WI-1c.1 | Replace `armada_list_fleets`, `armada_list_vessels` on lines 269-270 with `armada_enumerate` equivalents | ⬜ Todo | e.g., `armada_enumerate({ entityType: "fleets" })` |
| WI-1c.2 | Replace `armada_list_captains` on line 270 with `armada_enumerate({ entityType: "captains" })` | ⬜ Todo | |
| WI-1c.3 | Remove any remaining "prefer enumerate over list" language — enumerate is the only option now | ⬜ Todo | |

#### WI-1d: Tests (`test/Armada.Test.Automated/Suites/McpToolTests.cs`)

| # | Task | Status | Notes |
|---|------|--------|-------|
| WI-1d.1 | Remove `armada_list_captains` test calls (~lines 243, 253) | ⬜ Todo | |
| WI-1d.2 | Remove `armada_list_vessels` test calls (~lines 262, 273) | ⬜ Todo | |
| WI-1d.3 | Remove `armada_list_fleets` test calls (~lines 282, 292, 302) | ⬜ Todo | |
| WI-1d.4 | Remove `armada_list_missions` test calls (~lines 311, 321, 330, 341, 351) | ⬜ Todo | |
| WI-1d.5 | Remove `armada_list_voyages` test calls (~lines 361, 373, 384, 393) | ⬜ Todo | |
| WI-1d.6 | Remove `armada_list_events` test calls (~lines 403, 412, 426) | ⬜ Todo | |
| WI-1d.7 | Remove `armada_list_signals` test calls (~lines 436, 456, 493) | ⬜ Todo | |
| WI-1d.8 | Remove `armada_list_docks` test calls (~lines 1589, 1598) | ⬜ Todo | |
| WI-1d.9 | Remove `armada_list_merge_queue` test calls (~lines 1611, 1657) | ⬜ Todo | |
| WI-1d.10 | Remove list tool names from tool enumeration/registration verification (~lines 93-128) | ⬜ Todo | |
| WI-1d.11 | Update integration tests that use list APIs as setup/verification steps (~lines 1017, 1185, 1745, 1765, 1810, 1850, 1876-1960) — replace with `armada_enumerate` calls | ⬜ Todo | |
| WI-1d.12 | Run full test suite and verify no remaining references to removed tools | ⬜ Todo | |

---

### WI-2: Harden `armada_enumerate`

Reduce default page size and add boolean flags so large text fields are excluded by default.

| # | Task | Status | Notes |
|---|------|--------|-------|
| WI-2.1 | Change default `pageSize` from 100 to **10** | ⬜ Todo | Max stays at 1000 — caller's responsibility to choose wisely |
| WI-2.3 | Add `includeDescription` boolean parameter (default `false`) | ⬜ Todo | Applies to: missions, voyages |
| WI-2.4 | Add `includeContext` boolean parameter (default `false`) | ⬜ Todo | Applies to: vessels (`projectContext`, `styleGuide`) |
| WI-2.5 | Add `includeTestOutput` boolean parameter (default `false`) | ⬜ Todo | Applies to: merge_queue |
| WI-2.6 | Add `includePayload` boolean parameter (default `false`) | ⬜ Todo | Applies to: events (embedded entity snapshots) |
| WI-2.7 | Add `includeMessage` boolean parameter (default `false`) | ⬜ Todo | Applies to: signals (full message body) |
| WI-2.8 | When boolean flags are `false`, return a `length` hint instead (e.g., `descriptionLength: 4523`) | ⬜ Todo | Lets callers decide whether to fetch the full field |
| WI-2.9 | Update MCP tool schema in `McpToolRegistrar.cs` with new parameters and descriptions | ⬜ Todo | |
| WI-2.10 | Add/update tests for new parameters and defaults | ⬜ Todo | |

---

### WI-3: Harden `armada_voyage_status`

Add summary mode and boolean flags to control inclusion of subordinate data.

| # | Task | Status | Notes |
|---|------|--------|-------|
| WI-3.1 | Add `summary` boolean parameter (default `true`) | ⬜ Todo | When `true`: return voyage metadata + mission counts by status only (no mission objects) |
| WI-3.2 | Add `includeMissions` boolean parameter (default `false`) | ⬜ Todo | When `true` (and `summary: false`): embed mission objects |
| WI-3.3 | Add `includeDescription` boolean parameter (default `false`) | ⬜ Todo | When `true`: include `Description` on embedded missions |
| WI-3.4 | Add `includeDiffs` boolean parameter (default `false`) | ⬜ Todo | When `true`: include saved diff for each mission |
| WI-3.5 | Add `includeLogs` boolean parameter (default `false`) | ⬜ Todo | When `true`: include log excerpt for each mission |
| WI-3.6 | Define summary response schema | ⬜ Todo | Example: `{ voyage: { id, title, status, createdUtc }, missionCounts: { pending: 2, inProgress: 3, complete: 10, failed: 1 }, totalMissions: 16 }` |
| WI-3.7 | Update MCP tool schema in `McpToolRegistrar.cs` with new parameters and descriptions | ⬜ Todo | |
| WI-3.8 | Add/update tests for summary mode and boolean flags | ⬜ Todo | |

---

### WI-4: Update all documentation

Every document that references `armada_list_*` must be updated to reflect complete removal. All code examples, tool reference tables, and guidance text must use `armada_enumerate` exclusively. Additionally, new `armada_enumerate` boolean flags and `armada_voyage_status` summary mode must be documented.

#### WI-4a: `docs/MCP_API.md` — Master API reference

| # | Task | Status | Notes |
|---|------|--------|-------|
| WI-4a.1 | Remove Table of Contents entries for all 9 list APIs (lines 26, 33, 42, 48, 60, 70, 74, 78, 84) | ⬜ Todo | |
| WI-4a.2 | Remove `armada_list_fleets` API section (~line 465) | ⬜ Todo | |
| WI-4a.3 | Remove `armada_list_vessels` API section (~line 549) | ⬜ Todo | |
| WI-4a.4 | Remove `armada_list_voyages` API section (~line 636) | ⬜ Todo | |
| WI-4a.5 | Remove `armada_list_missions` API section (~line 662) | ⬜ Todo | |
| WI-4a.6 | Remove `armada_list_captains` API section (~line 690) | ⬜ Todo | |
| WI-4a.7 | Remove `armada_list_signals` API section (~line 727) | ⬜ Todo | |
| WI-4a.8 | Remove `armada_list_events` API section (~line 753) | ⬜ Todo | |
| WI-4a.9 | Remove `armada_list_docks` API section (~line 1813) | ⬜ Todo | |
| WI-4a.10 | Remove `armada_list_merge_queue` API section (~line 1966) | ⬜ Todo | |
| WI-4a.11 | Update `armada_enumerate` section with new boolean flag parameters (`includeDescription`, `includeContext`, `includeTestOutput`, `includePayload`, `includeMessage`) and updated defaults (`pageSize: 25`, max `100`) | ⬜ Todo | |
| WI-4a.12 | Update `armada_voyage_status` section with new parameters (`summary`, `includeMissions`, `includeDescription`, `includeDiffs`, `includeLogs`) | ⬜ Todo | |

#### WI-4b: `docs/INSTRUCTIONS_FOR_CLAUDE_CODE.md`

| # | Task | Status | Notes |
|---|------|--------|-------|
| WI-4b.1 | Replace `armada_list_fleets()` and `armada_list_vessels()` in Research section (lines 31, 33) with `armada_enumerate` examples | ⬜ Todo | |
| WI-4b.2 | Replace `armada_list_events({ missionId })` in Adapt section (line 146) with `armada_enumerate({ entityType: "events", missionId })` | ⬜ Todo | |
| WI-4b.3 | Remove `armada_list_fleets` from Fleets tool reference table (line 181) | ⬜ Todo | |
| WI-4b.4 | Remove `armada_list_vessels` from Vessels tool reference table (line 191) | ⬜ Todo | |
| WI-4b.5 | Remove `armada_list_voyages` from Voyages tool reference table (line 202) | ⬜ Todo | |
| WI-4b.6 | Remove `armada_list_missions` from Missions tool reference table (line 211) | ⬜ Todo | |
| WI-4b.7 | Remove `armada_list_captains` from Captains tool reference table (line 234) | ⬜ Todo | |
| WI-4b.8 | Remove `armada_list_signals` from Signals tool reference table (line 248) | ⬜ Todo | |
| WI-4b.9 | Remove `armada_list_events` from Events tool reference table (line 254) | ⬜ Todo | |
| WI-4b.10 | Remove `armada_list_docks` from Docks tool reference table (line 260) | ⬜ Todo | |
| WI-4b.11 | Remove `armada_list_merge_queue` from Merge Queue tool reference table (line 269) | ⬜ Todo | |
| WI-4b.12 | Replace `armada_list_captains` in "stalled captain" guidance (line 300) with `armada_enumerate({ entityType: "captains", status: "Stalled" })` | ⬜ Todo | |
| WI-4b.13 | Rewrite "Decision-Making Guidance" section (line 283) — remove all "prefer enumerate over list" language; enumerate is now the only option | ⬜ Todo | |
| WI-4b.14 | Update `armada_enumerate` entry in Enumeration tool reference with new boolean flags and updated defaults | ⬜ Todo | |
| WI-4b.15 | Update `armada_voyage_status` entry with new summary/include parameters | ⬜ Todo | |

#### WI-4c: `docs/INSTRUCTIONS_FOR_CODEX.md`

| # | Task | Status | Notes |
|---|------|--------|-------|
| WI-4c.1 | Apply identical changes as WI-4b (same structure as Claude Code instructions) | ⬜ Todo | Lines 31, 33, 146, 181, 191, 202, 211, 234, 248, 254, 260, 269, 283, 300 |
| WI-4c.2 | Verify no Codex-specific references to list APIs remain | ⬜ Todo | |

#### WI-4d: `docs/INSTRUCTIONS_FOR_CURSOR.md`

| # | Task | Status | Notes |
|---|------|--------|-------|
| WI-4d.1 | Replace `armada_list_events({ missionId })` in Adapt section (line 152) with enumerate | ⬜ Todo | |
| WI-4d.2 | Remove enumerate-vs-list guidance (lines 166, 173) — enumerate is now the only option | ⬜ Todo | |
| WI-4d.3 | Remove `armada_list_fleets` from Fleets table (line 194) | ⬜ Todo | |
| WI-4d.4 | Remove `armada_list_vessels` from Vessels table (line 204) | ⬜ Todo | |
| WI-4d.5 | Remove `armada_list_voyages` from Voyages table (line 215) | ⬜ Todo | |
| WI-4d.6 | Remove `armada_list_missions` from Missions table (line 224) | ⬜ Todo | |
| WI-4d.7 | Remove `armada_list_captains` from Captains table (line 247) | ⬜ Todo | |
| WI-4d.8 | Remove `armada_list_signals` from Signals table (line 261) | ⬜ Todo | |
| WI-4d.9 | Remove `armada_list_events` from Events table (line 267) | ⬜ Todo | |
| WI-4d.10 | Remove `armada_list_docks` from Docks table (line 273) | ⬜ Todo | |
| WI-4d.11 | Remove `armada_list_merge_queue` from Merge Queue table (line 282) | ⬜ Todo | |
| WI-4d.12 | Replace `armada_list_captains` in "stalled captain" guidance (line 313) | ⬜ Todo | |
| WI-4d.13 | Rewrite "Decision-Making Guidance" and tool reference preamble — remove all list/enumerate comparison language | ⬜ Todo | |
| WI-4d.14 | Update `armada_enumerate` and `armada_voyage_status` entries with new parameters | ⬜ Todo | |

#### WI-4e: `docs/INSTRUCTIONS_FOR_GEMINI.md`

| # | Task | Status | Notes |
|---|------|--------|-------|
| WI-4e.1 | Apply identical changes as WI-4b (same structure as Claude Code instructions) | ⬜ Todo | Lines 31, 33, 146, 181, 191, 202, 211, 234, 248, 254, 260, 269, 283, 300 |
| WI-4e.2 | Verify no Gemini-specific references to list APIs remain | ⬜ Todo | |

#### WI-4f: `docs/MERGING.md`

| # | Task | Status | Notes |
|---|------|--------|-------|
| WI-4f.1 | Replace `armada_list_merge_queue` in intro paragraph (line 7) with `armada_enumerate({ entityType: "merge_queue" })` | ⬜ Todo | |
| WI-4f.2 | Replace `armada_list_merge_queue` in monitoring guidance (line 79) — remove "or armada_enumerate" alternative phrasing; enumerate is the only option | ⬜ Todo | |
| WI-4f.3 | Remove `armada_list_merge_queue` from tool reference table (line 91) | ⬜ Todo | |

#### WI-4g: `CLAUDE.md` (project root)

| # | Task | Status | Notes |
|---|------|--------|-------|
| WI-4g.1 | Rewrite context conservation note (line 4) — remove "prefer armada_enumerate over armada_list_*" phrasing; list APIs no longer exist | ⬜ Todo | Keep the enumerate best-practice guidance (pageSize 10-25, filters, etc.) |

#### WI-4h: `README.md` (project root)

| # | Task | Status | Notes |
|---|------|--------|-------|
| WI-4h.1 | Replace `armada_list_missions` and `armada_list_events` in tool examples (line 503) with `armada_enumerate` examples | ⬜ Todo | |

#### WI-4i: Orchestrator-specific docs

| # | Task | Status | Notes |
|---|------|--------|-------|
| WI-4i.1 | Grep `docs/CLAUDE_CODE_AS_ORCHESTRATOR.md` for any `list_` references and remove | ⬜ Todo | No matches found in initial scan — verify after code changes |
| WI-4i.2 | Grep `docs/CODEX_AS_ORCHESTRATOR.md` for any `list_` references and remove | ⬜ Todo | No matches found in initial scan — verify after code changes |
| WI-4i.3 | Grep `docs/CURSOR_AS_ORCHESTRATOR.md` for any `list_` references and remove | ⬜ Todo | No matches found in initial scan — verify after code changes |
| WI-4i.4 | Grep `docs/GEMINI_AS_ORCHESTRATOR.md` for any `list_` references and remove | ⬜ Todo | No matches found in initial scan — verify after code changes |

#### WI-4j: Archive docs (informational — low priority)

| # | Task | Status | Notes |
|---|------|--------|-------|
| WI-4j.1 | `archive/ARMADA.md` — list API checklist items (~lines 544-546) | ⬜ Todo | Archive files are historical; update or add a note that list APIs were removed |
| WI-4j.2 | `archive/CODEX_RESPONSE.md` — guidance about list APIs (~line 127) | ⬜ Todo | Same — historical note |

---

### WI-5: Final verification

After all changes are made, perform a full sweep to ensure no references remain.

| # | Task | Status | Notes |
|---|------|--------|-------|
| WI-5.1 | Run `grep -r "armada_list" --include="*.cs" --include="*.md" --include="*.json"` across entire repo | ⬜ Todo | Should return zero matches (excluding LARGE_RESPONSES.md itself and archive/) |
| WI-5.2 | Run `grep -r "list_fleets\|list_vessels\|list_captains\|list_missions\|list_voyages\|list_docks\|list_events\|list_signals\|list_merge_queue"` across entire repo | ⬜ Todo | Catches any references without the `armada_` prefix |
| WI-5.3 | Build the solution — verify no compile errors from removed handlers | ⬜ Todo | |
| WI-5.4 | Run full test suite — verify all tests pass with enumerate replacements | ⬜ Todo | |
| WI-5.5 | Start Admiral server and verify MCP tool list no longer includes any `list_*` tools | ⬜ Todo | |
| WI-5.6 | Test orchestrator prompt end-to-end: confirm LLM uses `armada_enumerate` for all collection queries | ⬜ Todo | |

---

## Future Improvements (Not Blocking)

These are lower-priority enhancements that would further reduce remaining MEDIUM risks. Not required for the initial remediation.

| # | API | Improvement | Status | Notes |
|---|-----|-------------|--------|-------|
| FI-1 | `armada_status` | Add `fleetId`/`vesselId` scope parameter | ⬜ Todo | Reduces response when only one project is relevant |
| FI-2 | `armada_status` | Add `compact` mode (counts only, no entity details) | ⬜ Todo | |
| FI-3 | `armada_get_fleet` | Return vessel summaries (Id, Name, RepoUrl) instead of full vessel objects | ⬜ Todo | Avoids embedding projectContext/styleGuide |
| FI-4 | `armada_get_vessel` | Add `includeContext` flag (default true) to optionally omit projectContext/styleGuide | ⬜ Todo | |
| FI-5 | `armada_process_merge_queue` | Return summary only (entry IDs + statuses), omit TestOutput | ⬜ Todo | |
| FI-6 | `armada_get_merge_entry` | Add `maxTestOutputLength` parameter to truncate TestOutput | ⬜ Todo | |
| FI-7 | `armada_update_vessel` | Return confirmation only instead of echoing full vessel object | ⬜ Todo | Avoids echoing back large projectContext/styleGuide |
| FI-8 | `armada_update_vessel_context` | Same as FI-7 | ⬜ Todo | |

---

## Reference: APIs with NEGLIGIBLE Risk (No Action Required)

| API | Response Type |
|-----|--------------|
| `armada_dispatch` | Created voyage + mission IDs |
| `armada_create_mission` | Created mission object |
| `armada_create_captain` | Created captain object |
| `armada_create_fleet` | Created fleet object |
| `armada_add_vessel` | Created vessel object |
| `armada_update_captain` | Updated captain object |
| `armada_update_fleet` | Updated fleet object |
| `armada_update_mission` | Updated mission object |
| `armada_update_vessel` | Updated vessel object (see FI-7) |
| `armada_update_vessel_context` | Updated vessel object (see FI-8) |
| `armada_cancel_mission` | Confirmation |
| `armada_cancel_voyage` | Confirmation |
| `armada_cancel_merge` | Confirmation |
| `armada_purge_mission` | Confirmation |
| `armada_purge_voyage` | Confirmation |
| `armada_restart_mission` | Updated mission object |
| `armada_transition_mission_status` | Updated mission object |
| `armada_enqueue_merge` | Created merge entry |
| `armada_send_signal` | Confirmation |
| `armada_stop_all` | Confirmation |
| `armada_stop_captain` | Confirmation |
| `armada_stop_server` | Confirmation |
| `armada_backup` | File path string |
| `armada_restore` | Confirmation |
| `armada_delete_captain` | Confirmation |
| `armada_delete_fleet` | Confirmation |
| `armada_delete_vessel` | Confirmation |
| `echo` | Echoed input |
| `ping` | "pong" |
| `getTime` | ISO timestamp |
| `getSessions` | Session ID list |
