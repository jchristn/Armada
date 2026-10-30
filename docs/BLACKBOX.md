# Mission and voyage black box

When a mission or voyage fails, Armada writes a **black box entry** at the moment of failure: an index row plus a
small bundle of objects (the versioned `record.json`, the FIX THIS briefs and bounded evidence). The entry answers,
with evidence: which mission failed, where (phase, step, host, code site), how (exception, exit code, failing command,
git error), the status before and after, why (classified cause with rule ids and confidence), which missions, files
and directories it collided with, the configuration in effect and what would have prevented it, what Armada itself
could change, and the ranked recovery paths with exact commands.

This document has two parts:

- **Part 1, user guide** (sections 1 to 12): concepts, what an entry contains, the FIX THIS brief, where to find
  entries, recovery, MCP and Ask Armada usage, storage targets, retention and garbage collection, security,
  troubleshooting and the prevention, forensics and operations extensions.
- **Part 2, developer contract** (section 13): the services, storage, database, models, events and rules the
  surfaces build on.

The consumer schema (every object, section and field with types and bounds) is in
[BLACKBOX_SCHEMA.md](BLACKBOX_SCHEMA.md) and the generated JSON Schema in
[blackbox-record.schema.json](blackbox-record.schema.json). The REST routes, MCP tools, resources and prompts, and
WebSocket commands and events are in [REST_API.md](REST_API.md), [MCP_API.md](MCP_API.md) and
[WEBSOCKET_API.md](WEBSOCKET_API.md). The design and phases are in `MISSION_BLACKBOX_PLAN.md`; where the plan and
the code differ, this document describes the code.

Work that is designed but not done yet is listed in section 14, Open items.

## Contents

Part 1, user guide

1. [Concepts](#1-concepts)
2. [What an entry contains](#2-what-an-entry-contains)
3. [The FIX THIS brief and its audiences](#3-the-fix-this-brief-and-its-audiences)
4. [Where to find entries](#4-where-to-find-entries)
5. [Recovery paths and recommendations](#5-recovery-paths-and-recommendations)
6. [Using the black box over MCP](#6-using-the-black-box-over-mcp)
7. [Ask Armada](#7-ask-armada)
8. [Storage targets](#8-storage-targets)
9. [Retention and garbage collection](#9-retention-and-garbage-collection)
10. [Security and redaction](#10-security-and-redaction)
11. [Troubleshooting](#11-troubleshooting)
12. [Prevention, forensics and operations](#12-prevention-forensics-and-operations)

Part 2, developer contract

13. [Developer contract](#13-developer-contract)
14. [Open items](#14-open-items)

# Part 1: user guide

## 1. Concepts

**Entry.** One failure of one mission or voyage. Every terminal failure (`Failed`, `LandingFailed`, `Blocked`, a
`Cancelled` that was not a plain operator cancellation) and every failed or cancelled voyage gets exactly one
**current** entry. When the mission is retried and fails again, the new entry becomes current (attempt 2, 3, ...) and
the earlier one is kept as a superseded attempt. A few non-terminal events also get an entry so the evidence is not
lost: a provisioning failure that returned the mission to Pending, a dependent held as Blocked, a stop the owning host
never confirmed, an orphaned exit held as WorkProduced, a landing that was not attempted, an expired landing deferral
and a used-up rescue budget. Their `StatusAfter` is the status the mission kept. Entry ids start with `bbx_`.

**Objects.** The files of an entry: `record.json`, three rendered briefs and the evidence (agent output, the diff, the
mission log tail, check output, git status, target commits, events). See section 2.

**Subject.** The mission or voyage the entry is about. `GET /api/v1/missions/{id}`, MCP `mission_status`, the voyage
reads and `voyage_status` carry `LastBlackBoxEntryId`, so any client can find the current entry without knowing the
black box routes. Clearing a mission's `FailureReason` (restart, rescue, repair) never erases its entry.

**Phase and step.** Where it failed. Each failure write records one step; the phase follows from it:

| Phase | Steps |
|---|---|
| Dispatch | VoyageParentNotRunnable, AssignmentFailed, NoEligibleCaptain, HarborUnavailable, MemoryPressure |
| Provision | DockProvision, WorktreeCreate, BranchCreate, StartCommitRecord |
| Execution | ProcessCrash, UsageLimit, AuthFailure, ModelUnavailable, Stall, MaxRuntime, RecoveryExhausted, Interrupted, OperatorStop |
| Completion | NoOp, Unfinished, CaptainBlocked, InvalidOutput, ScopeViolation, BoundaryViolation, DefinitionOfDone, StageCheck |
| Review | JudgeRejected, ReviewDenied, ReviewTimeout, ReviewApprovedDockMissing |
| Handoff | PlanParse, StageBranchHarden, DependencyCancelled, IntegrationBarrier |
| Landing | DockMissing, TargetIntegration, Verification, DirtyCheckout, UnpublishedAncestry, Push, PullRequest, FastForward, LocalMerge, RetryLanding, DeferralExpired |
| MergeQueue | QueueResolveRepo, QueueMergeConflict, QueueTests, QueueProcessing, QueuePush |
| Recovery | RescueFailed, RedispatchExhausted, OrphanedExit, UnconfirmedStop, MissingMergeHandoff |
| Voyage | SettlementFailed, Halted, Cancelled, StageFailed |
| External | VesselDeleted, OperatorTransition |

`NoEligibleCaptain` and `MemoryPressure` are waits, not failure writes, so they are not captured inline; a
memory-pressure deferral records one `mission.dispatch_deferred` event per Pending mission and reason instead.

**Capture mode.** `Inline` entries are written by the failure site itself and hold its volatile facts (the target tip,
the porcelain status, the conflicted files, whether the worktree existed, the Harbor link state, the exception). A
**sweeper** runs every `blackBox.sweeper.intervalSeconds` (60) and captures any failed mission or voyage that has no
current entry (`Sweeper`), and the **backfill** does the same for failures from before the upgrade or any window you
choose (`Backfill`). Sweeper and backfill entries are flagged as lower fidelity in the brief: the failure site's
volatile facts are missing.

**State.** An entry is written `Captured`, then enriched in the background (`Enriched`: git forensics run on the host
that owns the dock, conflict attribution, attempts, evidence) and analyzed (`Analyzed`: the rule catalog). Other
states: `EnrichmentFailed` (retried up to `sweeper.maxEnrichmentAttempts`, 5), `ObjectMissing` (a stored object is gone
from its store), `Deleting` (being deleted; hidden from readers).

**Triage.** The analysis votes on one verdict: `UserConfig` (your Armada configuration), `MissionBrief` (the brief or
the dependency ordering), `Transient` (provider, network, usage limit; retrying is likely to work), `ArmadaDefect` (a
bug in Armada), `External` (an operator action or something outside Armada), or `Unknown` (no rule voted, or a tie,
which names the tied verdicts). Confidence is Low, Medium or High.

**Resolution.** `Open` until someone resolves it: `Resolved`, `Duplicate`, `WontFix` or `ArmadaFixed`, with an
optional note and link. Resolving shortens the entry's life (section 9).

**Fingerprint.** A hash of phase, step, kind, failure site and the normalized reason, so the same failure mode groups
across missions and Armada versions (lists, hotspots, trends, the maintainer bundle).

**Notes.** Annotations by operators, agents, Ask Armada and Armada itself. Every recovery dry run and run is noted on
the entry automatically. Briefs include the ten newest notes.

## 2. What an entry contains

The record answers the questions in plan section 1, each from a named section of `record.json`:

| Question | Record section |
|---|---|
| Which mission (id, title) and voyage failed | `subject` (mission, voyage, vessel, captain, persona, dock, Harbor, attempt, rescue chain) |
| Where it failed: phase, step, host, code site | `site` (phase, step, host kind and name, source file, method, line, Armada version and commit) |
| How it failed | `mechanism` (exception type, message and frames, exit code, runtime failure kind, failing command, exit code and output, git operation and stderr) |
| Status before and after, and the timeline | `status` (before, after, timeline from the mission's events) |
| Why it failed | `classification` (kind, raw reason, suspected cause) and `analysis.why` (findings with rule ids, confidence and evidence references) |
| What configuration could have prevented it | `configuration` (the effective values with their source: mission, voyage, vessel, workflow profile, captain, persona, global or default) and `analysis.recommendations` |
| Whether a conflicting mission caused it, and which | `conflict.conflictingMissions` (missions that landed on the target since this one started and touched the same files, by `Armada-Mission-Id` trailer) and `conflict.concurrentMissions` |
| Which files caused the conflict | `conflict.conflictedFiles` (with `conflictedFileTotal`) and `scope` for scope and boundary failures |
| Which directories caused the conflict | `conflict.conflictedDirectories` (depth 1 to 3), `conflictedParentDirectories`, `deepestConflictedDirectory` |
| What Armada could change | `analysis.improvements` (hypotheses with source pointers and severity) |
| The recovery paths | `analysis.recoveryPaths` (ranked, each step with its MCP call, REST request, CLI command and machine-readable action) |

Plus `workspace` (dock, worktree and branch existence, branch tip, ahead and behind, dirty paths, target movement, the
host that was inspected), `attempts` (earlier attempts, oldest first), `evidence` (the object index, diff stat,
relevant events), `related` (fingerprint, similar entries, cascade root, downstream missions), `voyage` (voyage entries:
roll-up by status, failed missions and their entries), `redaction` (which rules fired, never values) and `truncation`
(what was cut and its original size). A section that could not be captured is null and the brief says so.

Objects:

| Object | Content |
|---|---|
| `record.json` | The versioned record (at most 256 KB). |
| `brief.md` | The FIX THIS brief for an agent. |
| `brief.user.md` | The plain account for the operator. |
| `brief.maintainer.md` | The Armada maintainer view. |
| `evidence/agent-output.txt`, `evidence/mission.log` | The captain's output and the mission log tail (last 256 KB). |
| `evidence/diff.patch`, `evidence/diffstat.txt` | The mission's diff and its per-file stats. |
| `evidence/check-<n>.txt`, `evidence/verification-<n>.txt` | Check runs and landing verification receipts. |
| `evidence/merge-entry.txt` | The merge queue entry and its test output. |
| `evidence/git-status.txt` | `git status --porcelain` of the worktree and the target checkout, read on the owning host. |
| `evidence/target-commits.txt` | Commits on the target since the mission's start commit (up to 500). |
| `evidence/events.json` | The newest 50 events of the subject. |

Every object is redacted and bounded: evidence is capped at `blackBox.storage.maxEvidenceObjectBytes` (1 MB) keeping
the head and tail around a marker line, and an entry at `maxEntryBytes` (8 MB). Downloads (`.zip`) add a
`manifest.json`. Field-level detail is in [BLACKBOX_SCHEMA.md](BLACKBOX_SCHEMA.md).

## 3. The FIX THIS brief and its audiences

Every entry renders a Markdown brief you can paste into any coding agent with the words "FIX THIS". Briefs are
rendered live from the record (so renderer improvements apply to old entries), target 12 KB and never exceed 40 KB.
Three audiences:

| Audience | Object | Starts with | For |
|---|---|---|---|
| `agent` (default) | `brief.md` | "# FIX THIS: ..." and a triage instruction: decide UserConfig, MissionBrief, Transient, ArmadaDefect or External, quote evidence, use only the listed recovery paths, confirm data-loss steps with the user | A coding agent fixing the work (Claude Code, Codex, a captain) |
| `user` | `brief.user.md` | "# What went wrong: ..." and "What you can do" first; no rule ids, source pointers or stack frames | The operator |
| `maintainer` | `brief.maintainer.md` | "# Armada maintainer brief: ..." with the code site, triage votes, rule ids, fingerprint and occurrences, and the Armada improvement hypotheses | Someone fixing Armada itself |

The agent brief's sections are Summary, Identity, Where it failed, How it failed, Status timeline, Why, Conflict,
Workspace, Scope, Configuration in effect, What would have prevented it, What Armada could change, Recovery,
Attempts, Evidence, Related failures, Notes on this record and Your answer. Each recovery step shows its MCP call,
CLI command and REST request. `redactHosts=true` strips absolute paths, host names and user names, for a brief you
share outside the installation.

Three ways to get a brief:

1. The dashboard black box panel on the mission page: **Copy FIX THIS** (also on Inbox failure rows).
2. A terminal: `curl -s -H "Authorization: Bearer $TOKEN" http://localhost:7890/api/v1/missions/msn_x/blackbox/brief | pbcopy`
   (or `armada blackbox show msn_x --brief | pbcopy`, section 4).
3. MCP: `get_blackbox {"missionId": "msn_x", "format": "brief"}`.

## 4. Where to find entries

| Surface | Where | Status |
|---|---|---|
| Dashboard | Mission and voyage detail black box panel; Black Box tab on the Missions page; entry page `/blackbox/{id}`; settings | Available |
| TUI | Mission and voyage Failure tab; Black Box tab on the Missions screen (`g b`); entry screen; Settings and Storage sections | Available |
| Helm CLI | `armada blackbox ...`, `armada mission failure <id>`, `armada voyage failure <id>` | Available |
| VS Code | The Armada extension (`src/Armada.VSCode`) | Available |
| Mobile | Mission and voyage detail panel, Black Box tab on the Missions hub, entry screen | Available |
| REST | `/api/v1/missions/{id}/blackbox`, `/api/v1/voyages/{id}/blackbox`, `/api/v1/blackbox/...` | Available |
| MCP | 23 tools, 4 resources, 2 prompts | Available |
| WebSocket | Commands `blackbox.get`, `blackbox.brief`, `blackbox.list`; eight `blackbox.*` events | Available |
| Ask Armada | Black box read tools without approval; write tools as proposals; `/blackbox`; black box cards | Available |

### Dashboard

**Mission and voyage pages.** A **Black box** panel replaces the bare failure text (Retry Landing stays). Its
sections follow the brief: Summary (triage verdict, capture state, a notice for sweeper or backfill captures and for
missing objects), Where, How, Why, Conflict (files, directories and conflicting missions), What would have prevented
it (with **Dry run...** then confirm to apply a recommendation), What Armada could change, Recovery (each path dry runs
first and runs only after you confirm; data-loss paths are flagged), Configuration (each value with its source),
Attempts (switch between attempts), Evidence, Similar failures and Notes. Actions: **Copy FIX THIS** (for the
selected brief audience: User, Agent or Maintainer), **Download .md**, **Download .zip**, **View JSON**, **Ask Armada
about this failure** (opens Ask with the prompt filled in; nothing is sent until you press Send), Resolve (with a
note and link), Pin or Unpin, Reanalyze, Add note and **Open full page**. The panel updates live from the
`blackbox.*` events, including while analysis is still running.

**Black Box tab on the Missions page** (`/missions?tab=blackbox`; the old `/blackbox` redirects there, keeping its
filters, and the command palette lists **Black Box**): sections **Entries** (every filter, saved filters),
**Fingerprints**, **Hotspots**, **Recovery stats** and **Retention** (policy, usage, last and next garbage collection
run, **Preview garbage collection**, and for administrators **Purge by filter** with a dry run first). The section is
in `?bbtab=` (for example `/missions?tab=blackbox&bbtab=fingerprints`), since the Missions page owns `?tab=`.
`/blackbox/{id}` shows one entry on its own page.

**Settings.** The server settings page has a Black Box section: storage target and its credentials (masked), compress,
key prefix, fallback to the database, a store health check, **Migrate stored entries** with progress, and the retention
policy. **Inbox** failure rows have Copy FIX THIS and Ask Armada actions, and the Storage page counts black box usage.

### TUI

**Failure tab** (mission and voyage screens; `]` moves to it). It shows the entry's status, where it failed, triage,
resolution, the ranked recovery paths and the user audience brief. While the tab has focus its keys run before the
screen's and the status bar lists them: `c` copies the FIX THIS (agent) brief to the clipboard (OSC 52), `x` exports
it to a file (default `blackbox-{entryId}.md` in the current directory), `r` lists the recovery paths (choosing one
dry runs it and shows the plan; confirming runs it with the confirm token, and a data-loss path asks you to type
`LOSE DATA` first; notes are written as Operator), `a` opens Ask Armada with the prompt filled in, `j` shows the entry
as JSON, `s` resolves, `p` pins or unpins, `n` adds a note and `o` opens the entry. Every action is also in the `.`
menu and the palette. Without an entry the tab says so and the screen's own keys (for example `r` Restart) apply.

**Black Box tab on the Missions screen** (`/missions?tab=blackbox`, `g b`; `/blackbox` redirects there): server-paged entries filtered by vessel, triage, resolution, phase and
fingerprint; `F` opens the fingerprints view (choosing one filters the list, `X` clears it), `R` the recovery stats;
Enter opens an entry. **Entry screen** (`/blackbox/{id}`): the Brief tab (the same keys), the Objects tab (Enter opens a
paged viewer that reads 64 KB at a time; `]` and `[` move between chunks), Notes, Timeline, Diff and Nearby Successes
(section 12); `w` downloads the .zip, `f` lists similar failures. The voyage screen's Cascade tab and the vessel
screen's Reservations and Black Box Doctor tabs are described in section 12.

**Settings and Storage.** Settings has a **Black Box Retention** section (retention days, resolved retention, max
entries per vessel, max total size, GC interval, keep latest, delete with mission or vessel). The Storage tab lists the
storage target, health, usage, retention and garbage collection runs, and `G` runs a garbage collection dry run.
Storage target credentials are edited in the dashboard or the settings file.

### Helm CLI

```
armada blackbox show <missionId|voyageId|entryId> [--brief] [--audience agent|user|maintainer] [--redact-hosts] [--json]
armada blackbox list [--vessel] [--voyage] [--mission] [--captain] [--kind] [--triage] [--phase] [--step]
                     [--resolution] [--fingerprint] [--pinned] [--since 30d] [--before 2026-09-01] [--page] [--page-size]
armada blackbox objects <entryId>
armada blackbox cat <entryId> <name> [--offset N] [--length N]
armada blackbox download <entryId> [-o file.zip]
armada blackbox recover <entryId> <pathId> [--execute [--yes] [--acknowledge-data-loss]]
armada blackbox resolve <entryId> --resolution Resolved [--note] [--link]
armada blackbox note <entryId> "<text>"
armada blackbox pin|unpin|delete <entryId...>
armada blackbox purge [filters] [--include-pinned] [--max N] [--execute [--yes]]
armada blackbox gc [--dry-run]
armada blackbox retention
armada blackbox storage status | migrate --to <target> [--from] [--delete-source] | migration <id>
armada blackbox bundle [filters] [--max N] [--format markdown|json] [-o bundle.md]
armada blackbox backfill [--since 30d] [--max N]
armada blackbox export-fixture <entryId> [--name] [-o dir]
armada mission failure <id>        (alias of blackbox show for a mission)
armada voyage failure <id>         (alias of blackbox show for a voyage)
```

The prevention and forensics commands (`timeline`, `diff`, `nearby`, `recovery-stats`, `cascade`, `vessel
overlap-preview|reservations|doctor|update`, `mission lint-brief`) are listed in section 12.

`armada blackbox show msn_x --brief | pbcopy` is the terminal path to "paste into an agent and say FIX THIS": with
`--brief` (and `--json`, `cat` and `bundle` without `-o`) stdout holds only the payload and every status line goes to
stderr. Exit codes: 0 success, 1 failure or bad option, 2 credential (401 or 403) or target error, 3 not found (404),
4 refused. `recover` and `purge` are dry runs by default; `--execute` asks to confirm (`--yes` answers for you and is
required when the console is not interactive), and `recover` refuses a data-loss path without
`--acknowledge-data-loss`. `export-fixture` writes `{name}.json` into `src/Test.Shared/Fixtures/BlackBox` when run
inside the Armada repository, else into `-o`.

### VS Code

The extension in `src/Armada.VSCode` (see its [README](../src/Armada.VSCode/README.md)) adds an **Armada Failures**
view in the Explorer (entries grouped by vessel, filtered to the open repository's vessel by remote URL, with a badge
for open entries), **Copy FIX THIS**, **Open FIX THIS in Editor**, **Send FIX THIS to Chat**, **Run Recovery Path...**
(dry run, modal confirmation, explicit acknowledgement for data-loss paths), **Resolve Black Box Entry**, **Add
Note**, **Ask Armada about this failure**, an entry view, `BX` decorations on files that conflicted in an open entry and
`H` decorations on hotspot directories, and live updates over the WebSocket with a polling fallback. Connect with
**Armada: Connect** (the key is kept in VS Code secret storage).

### Mobile

The mobile app shows the black box on mission and voyage detail (the user audience summary, recovery paths with a
dry run and a confirmation sheet, Resolve, notes, Copy and Share of the brief through the platform share sheet, and
Ask Armada with the prompt prefilled), and a **Black Box** tab on the Missions hub (filtered by vessel and failure
kind on the server, paged as you scroll, refreshed on `blackbox.*` events) with a full entry screen at
`/blackbox/{id}`. See [MOBILE.md](MOBILE.md).

### REST, MCP and WebSocket

- REST (tag `BlackBox`, 39 routes): read a mission's current entry `GET /api/v1/missions/{id}/blackbox`, its brief
  `.../blackbox/brief?audience=user`, all attempts `.../blackbox/entries`; voyages the same under
  `/api/v1/voyages/{id}/blackbox`; any entry under `/api/v1/blackbox/{id}` (sections, objects with ranged reads,
  `.zip` download, notes, resolve, pin, recovery, recommendation apply, reanalyze, export-fixture, delete); lists and
  analytics (`GET /api/v1/blackbox`, `POST .../enumerate`, `POST .../search`, `POST .../bundle`,
  `POST .../hotspots/enumerate`, `POST .../fingerprints/enumerate`, `GET .../fingerprints/{fingerprint}/trend`); and
  administration (retention, storage, migrate, gc, purge, backfill, bulk delete). See [REST_API.md](REST_API.md).
- MCP: see section 6 and [MCP_API.md](MCP_API.md).
- WebSocket: `blackbox.get`, `blackbox.brief` and `blackbox.list` commands (global administrators, like every
  WebSocket command) and the events `blackbox.captured`, `blackbox.capture_failed`, `blackbox.analyzed`,
  `blackbox.resolved`, `blackbox.deleted`, `blackbox.storage_fallback`, `blackbox.gc_completed` and
  `blackbox.recovery_run`, delivered to the entry's tenant. See [WEBSOCKET_API.md](WEBSOCKET_API.md).
- .NET: `Armada.Client` has a method per route (`GetMissionBlackBoxAsync`, `GetBlackBoxBriefAsync`,
  `RunBlackBoxRecoveryAsync`, ...).

Who sees what: a global administrator sees every tenant's entries, a tenant administrator their tenant's, and a
user their own. An entry you cannot see reads exactly like one that does not exist (404 `BlackBoxNotFound`, never 403).
Notes, resolve, pin and recovery need an authenticated caller who can see the entry (each recovery step is also
authorized as its own tool); delete, bulk delete, reanalyze and the retention status need a tenant administrator;
purge, garbage collection, storage, migration, backfill and fixture export need a global administrator.

## 5. Recovery paths and recommendations

The analysis ranks up to 10 recovery paths (for example `bbx-land-001.stash-retry` or `bbx-exec-006.restart`) and
lists configuration recommendations (for example `bbx-land-001.landing-mode`). Running one is always the same two
steps:

1. **Dry run** (the default): `POST /api/v1/blackbox/{id}/recovery/{pathId}` with `{}` or `{"DryRun": true}` (MCP
   `run_blackbox_recovery {"entryId", "pathId"}`). Nothing runs. The reply lists each step's tool and arguments and
   carries a `ConfirmToken` (`bbc_...`) valid for 10 minutes.
2. **Confirm**: the same call with `{"DryRun": false, "ConfirmToken": "bbc_..."}`. For a path marked `DataLossRisk`
   (a step such as `cancel_mission` or `unstick_dock`) also pass `"AcknowledgeDataLoss": true`, after the user agreed.

Rules the server enforces:

- Only the actions recorded in the entry run, never a free-form command, and only against the entry's own mission,
  vessel and dock. The tools a path may name are `retry_landing`, `restart_mission`, `cancel_mission`,
  `unstick_dock`, `repair_dock`, `update_vessel` and `update_mission`.
- Every step is authorized as if the caller had called that tool directly.
- A token is single use and bound to the entry, the path, the caller, the plan and the mission's current version. It
  is refused (409 `BlackBoxConfirmationRequired`) when missing, reused, expired, for another entry, path or caller, or
  when the mission or the plan changed since the dry run: run a new dry run.
- Steps run in order; a manual step is reported, not run; the first failed step stops the run.
- Every dry run and run is noted on the entry and emits `blackbox.recovery_run`.

A configuration recommendation with an `applyAction` (for example an `update_vessel` patch such as
`{"landingMode": "PullRequest"}`) is applied the same way through
`POST /api/v1/blackbox/{id}/recommendations/{recommendationId}/apply` or MCP `apply_blackbox_recommendation`.
Advice-only recommendations have no action and are refused with 400.

When the failure is dealt with, resolve the entry (`POST /api/v1/blackbox/{id}/resolve` with `Resolution`, `Note`
and `Link`, or MCP `resolve_blackbox`), and pin entries you want to keep past their expiry.

## 6. Using the black box over MCP

Any MCP-connected agent (Claude Code, Codex, an Armada captain, Ask Armada) can take "Review the black box for mission
msn_abc and fix it" end to end:

```
User:  Review the black box for mission msn_abc and fix it.
Agent: get_blackbox {missionId: "msn_abc"}                                   summary: where, how, why, triage, top paths
Agent: read_blackbox_section {entryId, section: "Workspace"}                 dirty paths, checkout
Agent: read_blackbox_object {entryId, name: "evidence/git-status.txt"}       64 KB chunks, follow nextOffset
Agent: run_blackbox_recovery {entryId, pathId: "bbx-land-001.stash-retry"}   dry run: the plan and a confirmToken
Agent: (tells the user what will happen, or proceeds if told to act)
Agent: run_blackbox_recovery {entryId, pathId, dryRun: false, confirmToken}  per-step results; the entry is annotated
Agent: resolve_blackbox {entryId, resolution: "Resolved", note: "...", link: "..."}
```

| Goal | Tools |
|---|---|
| Explain it | `get_blackbox` (`format` summary, brief or json), `read_blackbox_section`, `list_blackbox_objects`, `read_blackbox_object` |
| Fix it, recover it | `run_blackbox_recovery` with a ranked path: dry run, then confirm |
| Prevent it | `apply_blackbox_recommendation`: dry run, then confirm |
| Fix Armada | `get_blackbox {audience: "maintainer", format: "brief"}` or `get_blackbox_bundle`, then a mission on the Armada vessel with that brief |
| Compare | `list_blackbox_entries {fingerprint}`, `search_blackbox`, `list_blackbox_fingerprints`, `get_blackbox_fingerprint_trend`, `list_blackbox_hotspots` |
| Clean up | `resolve_blackbox`, `annotate_blackbox`, `pin_blackbox`, `delete_blackbox_entries` |
| Administer | `get_blackbox_retention`, `get_blackbox_storage`, `run_blackbox_gc`, `purge_blackbox`, `reanalyze_blackbox`, `export_blackbox_fixture` |

Every tool takes `entryId`, or `missionId` / `voyageId` for that subject's current entry. `get_blackbox` with the
default `format: "summary"` returns a digest of about 2 KB: start there, then read only the sections the question
needs, and page evidence with `read_blackbox_object` (`offset`, `length` up to 64 KB, `nextOffset` until null)
instead of loading it whole. `blackbox` is also an entity type of the `enumerate` tool.

Safety, carried in every action tool's description and in the `review_blackbox` prompt:

> Read the black box summary first. Quote evidence, not guesses. State the triage verdict and whether you agree. If
> you act, use only recovery paths or recommendations from the entry, do a dry run, tell the user what will happen,
> and confirm before running anything marked dataLossRisk. Annotate the entry with what you did.

Resources and prompts (on the HTTP MCP server, `/mcp`):

| Resource | Content |
|---|---|
| `armada://blackbox/{entryId}/brief.md` | The agent brief (`text/markdown`) |
| `armada://blackbox/{entryId}/record.json` | The record (camelCase, as stored) |
| `armada://blackbox/{entryId}/objects/{name}` | The first 64 KB of an object (URL-encode a folder: `evidence%2Fgit-status.txt`); page the rest with `read_blackbox_object` |
| `armada://missions/{missionId}/blackbox` | The digest of the mission's current entry |

A missing or invisible entry or object is the MCP resource-not-found error (`-32002`) with the typed `NotFound` data.

| Prompt | Arguments | Expands to |
|---|---|---|
| `review_blackbox` | `missionId` (required), `goal` (explain, fix, prevent, fix Armada, compare or clean up; default explain) | The flow above with the goal-to-tool map and the safety rules |
| `explain_blackbox` | `missionId` | An explanation that separates evidence from opinion, without running anything |

Errors are typed: `NotFound` with `Code` `BlackBoxNotFound` (also for an entry you cannot see) or
`BlackBoxObjectMissing`, `Conflict` with `BlackBoxDeleting`, `BlackBoxObjectCorrupt` or `BlackBoxConfirmationRequired`,
`Unavailable` with `BlackBoxStoreUnavailable`, and `InvalidArgument` (with `Code` `CollectionLimitExceeded` above 1000
ids). A resource read of a missing entry or object currently returns the generic JSON-RPC error -32603 instead of a
typed error; use the tools when you need the code.

## 7. Ask Armada

The black box read tools (`get_blackbox`, `read_blackbox_section`, `list_blackbox_objects`, `read_blackbox_object`,
`list_blackbox_entries`, `search_blackbox`, `list_blackbox_notes`, `get_blackbox_bundle`, `list_blackbox_hotspots`,
`list_blackbox_fingerprints`, `get_blackbox_fingerprint_trend`, `get_blackbox_retention`, `get_blackbox_storage`) are on
the Ask Armada read-only allowlist, so the thread captain calls them without approval. The write tools
(`run_blackbox_recovery`, `apply_blackbox_recommendation`, `resolve_blackbox`, `annotate_blackbox` and the rest) stay
behind the Ask gate: Ask turns them into proposals you approve or reject.

Ask it:

> Show me the pertinent details from the black box for mission msn_x and give me your opinion on why it failed and
> what could have been done differently.

The captain reads the summary, then only the sections it needs, and answers with what happened (from the entry),
why (the findings with rule ids and confidence), its opinion (labeled as opinion), what could have been done
differently (what you can change and what Armada should change), and next steps as proposals.

Admin-level reads (`get_blackbox_retention` for tenant admins, `get_blackbox_storage` and `export_blackbox_fixture`
for admins) run as you: from Ask they are refused when you lack the role, never proposed.

- **`/blackbox <missionId> [question]`** shows the entry's card in the conversation (`get_blackbox`) and then sends
  your question, if any, to the captain. On the dashboard `/blackbox` alone opens a form listing recent failures.
- **The card** shows status chips (triage and confidence, kind, step, capture mode, resolution), where and how it
  failed, the findings, the conflicting missions and files, the ranked recovery paths and the configuration
  recommendations, Copy FIX THIS, and the follow-ups: **Fix it** and **Apply the configuration change** create a
  proposal (approving it runs a dry run, then confirms it); **Draft an Armada fix mission** and **Compare with similar
  failures** ask the captain. A mission id mentioned in the conversation links its entry's card automatically.
- **Prompt guidance**: the built-in `ask.system` template has a "Black box" section with the answer shape above, the
  rule never to invent files, commits or missions that are not in the entry, saying so when a fact is missing (for
  example an entry captured by the sweeper), offering the mission's FailureReason when there is no entry, and offering
  to save the opinion as a note (a proposal; the note's `authorKind` is AskArmada). A customized template gets the
  section appended to the turn prompt.
- **Entry points**: "Ask Armada about this failure" on the dashboard panel and Inbox, the TUI, VS Code and mobile open
  a new conversation with the prompt above in the message box (`/ask?prompt=<text>`, removed from the URL once read).

## 8. Storage targets

The index rows always live in the database. The objects go to the configured target:

| Target | `target` value | Backing | Notes |
|---|---|---|---|
| Database (default) | `Database` | `blackbox_objects.data` (SQLite BLOB, PostgreSQL bytea, SQL Server varbinary(max), MySQL LONGBLOB) | No configuration. Included in Armada backups. |
| Disk | `Disk` | A directory on the Admiral host, default `{DataDirectory}/blackbox` | Refuses paths outside the root and symbolic links below it. |
| S3 and S3-compatible | `S3` | AWS S3, Less3, MinIO, View and other S3 APIs | Custom endpoints use path-style requests. |
| Azure Blob | `AzureBlob` | Azure Storage (or Azurite) | Account name and key, or a connection string. |
| Google Cloud Storage | `GoogleCloud` | GCS (or an emulator) | Service account JSON file. |
| CIFS | `Cifs` | An SMB share | Host, share, user and password. |
| NFS | `Nfs` | An NFS export (v3 by default) | Host and absolute export path. |

Settings live under `blackBox.storage` in `settings.json` (or `PUT /api/v1/settings`). The external targets use the
Blobject 6.1.2 clients. Every external object is written under
`{keyPrefix}/{tenantId}/{vesselId or _}/{yyyy}/{MM}/{entryId}/{objectName}`. The storage target is recorded **per
entry**: changing the setting affects new entries only, and existing entries stay readable from where they were
written until you migrate them.

The full `blackBox.storage` block with its defaults (the Admiral writes every property back, nulls included):

```json
"storage": {
  "target": "Database",
  "keyPrefix": "armada/blackbox",
  "compress": true,
  "maxEvidenceObjectBytes": 1048576,
  "maxEntryBytes": 8388608,
  "fallbackToDatabase": true,
  "disk": { "directory": null },
  "s3": { "endpoint": null, "region": "us-east-1", "bucket": null, "accessKey": null, "secretKey": null, "usePathStyle": true, "useSsl": true },
  "azureBlob": { "accountName": null, "container": null, "accessKey": null, "connectionString": null, "endpoint": null },
  "googleCloud": { "projectId": null, "bucket": null, "credentialsPath": null, "endpoint": null },
  "cifs": { "hostname": null, "share": null, "domain": null, "username": null, "password": null, "port": 445, "preferEncryption": true },
  "nfs": { "hostname": null, "exportPath": null, "version": 3, "port": null, "mountPort": null, "userId": 0, "groupId": 0 }
}
```

| Setting | Default | Range | Meaning |
|---|---|---|---|
| `target` | `Database` | Database, Disk, S3, AzureBlob, GoogleCloud, Cifs, Nfs | Where new entries' objects go |
| `keyPrefix` | `armada/blackbox` | | Prefix of every external key; the orphan sweep only looks under it |
| `compress` | true | | Store objects gzip-compressed (reads always return decoded bytes) |
| `maxEvidenceObjectBytes` | 1048576 (1 MB) | 64 KB to 16 MB | Cap per evidence object (head and tail kept) |
| `maxEntryBytes` | 8388608 (8 MB) | 1 MB to 64 MB | Cap per entry |
| `fallbackToDatabase` | true | | Write to the database when the external target fails |

Out-of-range numbers are clamped. `PUT /api/v1/settings` validates `blackBox.storage` before applying anything and
answers 400 "Invalid black box storage settings: ..." with every error, each naming the setting and never a secret.

### Database (default)

Nothing to configure. Entries are in the database, so they are in every backup and restore with it.

### Disk

```json
"storage": { "target": "Disk", "disk": { "directory": "/var/lib/armada/blackbox" } }
```

A relative `directory` resolves against the data directory; null means `{DataDirectory}/blackbox`.

### S3 and S3-compatible (AWS S3, Less3, MinIO, View)

AWS S3 (no `endpoint`; without keys the AWS SDK's default credential chain is used):

```json
"storage": {
  "target": "S3",
  "s3": { "endpoint": null, "region": "us-west-2", "bucket": "acme-armada-blackbox", "accessKey": "AKIA...", "secretKey": "...", "usePathStyle": true, "useSsl": true }
}
```

Less3 or MinIO (a custom endpoint needs both keys; its scheme must agree with `useSsl`; requests are path-style):

```json
"storage": {
  "target": "S3",
  "s3": { "endpoint": "http://less3.internal:8000", "region": "us-west-1", "bucket": "armada", "accessKey": "default", "secretKey": "default", "usePathStyle": true, "useSsl": false }
}
```

For MinIO use its API endpoint, for example `"endpoint": "https://minio.internal:9000"` with `"useSsl": true`.
Give both keys or neither; a bucket and a region are required.

### Azure Blob

Account name and key:

```json
"storage": {
  "target": "AzureBlob",
  "azureBlob": { "accountName": "acmearmada", "container": "blackbox", "accessKey": "base64key==", "connectionString": null, "endpoint": null }
}
```

Or a connection string (AccountName and AccountKey, optional BlobEndpoint, for example Azurite):

```json
"storage": {
  "target": "AzureBlob",
  "azureBlob": { "container": "blackbox", "connectionString": "DefaultEndpointsProtocol=https;AccountName=acmearmada;AccountKey=base64key==;EndpointSuffix=core.windows.net" }
}
```

`endpoint` defaults to `https://{account}.blob.core.windows.net/`.

### Google Cloud Storage

```json
"storage": {
  "target": "GoogleCloud",
  "googleCloud": { "projectId": "acme-armada", "bucket": "acme-armada-blackbox", "credentialsPath": "/etc/armada/gcs-service-account.json", "endpoint": null }
}
```

The service account JSON file must exist when the settings are saved. `endpoint` points at an emulator when set.

### CIFS (SMB)

```json
"storage": {
  "target": "Cifs",
  "cifs": { "hostname": "nas.internal", "share": "armada", "domain": "ACME", "username": "svc-armada", "password": "...", "port": 445, "preferEncryption": true }
}
```

Host (a valid name or address, resolved when the client is built), share, user and password are required; `domain`
is optional.

### NFS

```json
"storage": {
  "target": "Nfs",
  "nfs": { "hostname": "nfs.internal", "exportPath": "/exports/armada", "version": 3, "port": null, "mountPort": null, "userId": 1000, "groupId": 1000 }
}
```

`exportPath` must be absolute. `version` is 2 to 4 (default 3); null ports use the portmapper.

### Health, fallback and secrets

- `GET /api/v1/blackbox/storage` (global administrator) reports the target, its health check, whether fallback is on,
  any unfinished migration and bytes per target. `GET /api/v1/doctor` has a **Black Box Storage** check: Pass when the
  target validates and answers, Fail with the reason (and what fallback will do), Warn when the black box is disabled.
- **Fallback.** When an external write fails and `fallbackToDatabase` is true, the entry is written to the database
  instead and `blackbox.storage_fallback` is emitted. With fallback off, the index row and `record.json` still go to the
  database (capture is never lost) and the other objects are marked missing (`ObjectMissing`).
- **Secrets** (`s3.accessKey`, `s3.secretKey`, `azureBlob.accessKey`, `azureBlob.connectionString`,
  `googleCloud.credentialsPath`, `cifs.password`) are masked as `********` in `GET /api/v1/settings`, kept when written
  back masked, never logged, and registered with the redactor so they never reach an entry.
- **Harbor split mode.** Evidence that lives on a Harbor is read over the Harbor link during enrichment, but objects
  are always written by the Admiral to the Admiral's target. See [HARBOR.md](HARBOR.md).
- **Storage inventory.** `GET /api/v1/storage` reports black box bytes per target as a `BlackBox` location.

### Migrating between targets

```
POST /api/v1/blackbox/storage/migrate   {"To": "S3", "DeleteSource": true, "BatchSize": 50}
GET  /api/v1/blackbox/storage/migrations/bbm_...
```

A migration job (global administrator) copies each entry's objects to the target, verifies every SHA-256 after
writing and reading back, switches the entry, then deletes the source copies (`DeleteSource`, default true). It pages
in id order (`BatchSize` 1 to 1000, default 50), saves its cursor after every entry, resumes after a restart, and is
idempotent: running it again skips entries already moved. A corrupt or unreadable object fails only its entry, which
stays where it was (the job ends `Failed` with `FailedEntries` and `LastError`). Only one migration runs at a time (409
while another is unfinished); a target that fails its health check is refused with 503. `From` limits the job to
entries on one source target. Set `blackBox.storage.target` to the new target as well, so new entries go there.

Within one Admiral process the job and the entry writer (capture, enrichment and re-analysis, which rewrite
`record.json` and the briefs) share per-entry locks (`BlackBoxEntryLocks`): a write in flight holds back the move of its
entry, a move holds back writes to its entry, and a write re-reads the entry's location under the lock, so it lands on
the new target and never saves the old location back over the switch. Run one Admiral against a data directory while a
migration runs; the locks do not span processes.

**Backups.** Database-target entries are inside the database backup. External-target entries are not copied: the
backup manifest's `blackBox` section lists them (up to 10000 references), and the restore result says which restored
entries point at objects outside the database.

## 9. Retention and garbage collection

`blackBox.retention`:

| Setting | Default | Range | Meaning |
|---|---|---|---|
| `retentionDays` | 180 | 0 to 3650 (0 keeps forever) | Age after which an unpinned entry expires. Longer than event retention (30 days) on purpose: the entry outlives the events it summarizes. |
| `resolvedRetentionDays` | 30 | 0 to 3650 | Days after resolution after which a resolved entry expires, when sooner than `retentionDays`. |
| `keepLatestPerMission` | true | | The current entry of a mission or voyage that still exists is kept past its expiry. |
| `maxEntriesPerVessel` | 1000 | 10 to 100000 | Above this, the vessel's oldest unpinned, non-current entries are evicted. |
| `maxTotalBytes` | 1073741824 (1 GB) | 16 MB to 1 TB | Global cap: oldest unpinned resolved entries go first, then the oldest unpinned. |
| `deleteWithMission` | false | | Delete a mission's entries when the mission is purged (false keeps them with the denormalized title). |
| `deleteWithVessel` | true | | Delete a vessel's entries when the vessel is deleted. |
| `gcIntervalMinutes` | 60 | 5 to 1440 | How often garbage collection runs. |
| `gcBatchSize` | 200 | 10 to 1000 | Entries per batch (also the page of the missing-object check). |
| `orphanGraceHours` | 24 | 1 to 720 | Minimum age before an unreferenced external object is deleted. |

Per vessel: `Vessel.BlackBoxRetentionDays` (0 to 3650, null uses `retentionDays`), set with `PUT /api/v1/vessels/{id}`
or MCP `update_vessel`. Pinned entries never expire. `ExpiresUtc` is computed at capture and recomputed on resolve,
pin, unpin, a vessel retention change and a retention policy change (a background recompute).

Other `blackBox` settings:

| Setting | Default | Range | Meaning |
|---|---|---|---|
| `enabled` | true | | Capture, sweep and collect garbage. False stops new entries; existing entries stay readable. Read on every tick, no restart needed. |
| `captureTimeoutMs` | 2000 | 100 to 30000 | Budget for everything a capture does beyond the index insert; on timeout the rest is left to enrichment. |
| `sweeper.intervalSeconds` | 60 | 10 to 86400 | How often the sweeper captures failures without an entry. |
| `sweeper.pageSize` | 100 | 1 to 1000 | Missions and voyages per sweeper page. |
| `sweeper.maxEnrichmentAttempts` | 5 | 1 to 20 | Enrichment retries before an entry stays `EnrichmentFailed`. |
| `summaryEndpointId` | null | a model endpoint id | When set, that model endpoint writes the entry summary (3 to 6 sentences, labeled model-written); any failure keeps the rules summary. |

Each garbage collection run (every `gcIntervalMinutes`, or `POST /api/v1/blackbox/gc`):

1. Finishes deletes left in the Deleting state.
2. Expires entries whose `ExpiresUtc` passed (unpinned, and not the current entry of a live subject when
   `keepLatestPerMission`).
3. Enforces quotas: per vessel, then global bytes, in the order above.
4. Cascades: entries whose vessel was deleted (`deleteWithVessel`) or whose mission was purged (`deleteWithMission`);
   pinned entries are kept. Deleting a tenant deletes all of its entries, pinned ones too.
5. Sweeps orphans on external targets: keys under `{keyPrefix}/` with no object row, older than `orphanGraceHours`
   (keys of unknown age and keys outside the prefix are never touched).
6. Checks a rotating page of entries for objects missing from their store and marks them `ObjectMissing`.
7. Reports `blackbox.gc_completed` with the counts (expired, evicted, cascaded, retried deletes, orphans removed,
   objects missing, bytes freed, failures).

Deletes are two-phase: the entry is marked Deleting (hidden), its objects are deleted from the store, then its rows. If
the store delete fails, the entry stays Deleting and the next run retries it. `RetentionService` and
`DataExpiryService` do not touch black box tables.

Manual controls:

| Operation | REST | MCP | Who |
|---|---|---|---|
| Retention status (policy, counts, bytes, next and last run) | `GET /api/v1/blackbox/retention` | `get_blackbox_retention` | Tenant admin (own tenant), global admin (all) |
| Run garbage collection now (`DryRun` lists candidates) | `POST /api/v1/blackbox/gc` | `run_blackbox_gc` | Global admin |
| Purge by filter (`DryRun` defaults to true; `IncludePinned`; `MaxEntries` 1 to 100000) | `POST /api/v1/blackbox/purge` | `purge_blackbox` | Global admin |
| Delete one | `DELETE /api/v1/blackbox/{id}` | `delete_blackbox_entries` | Tenant admin |
| Delete many (at most 1000 ids) | `POST /api/v1/blackbox/delete` | `delete_blackbox_entries` | Tenant admin |
| Pin, unpin | `POST /api/v1/blackbox/{id}/pin`, `/unpin` | `pin_blackbox` | Any caller who sees the entry |
| Backfill (`Since`, `MaxSubjects` default 1000) | `POST /api/v1/blackbox/backfill` | | Global admin |

Example retention status:

```json
{
  "Policy": { "RetentionDays": 180, "ResolvedRetentionDays": 30, "KeepLatestPerMission": true, "MaxEntriesPerVessel": 1000, "MaxTotalBytes": 1073741824, "DeleteWithMission": false, "DeleteWithVessel": true, "GcIntervalMinutes": 60, "GcBatchSize": 200, "OrphanGraceHours": 24 },
  "TotalEntries": 3,
  "TotalBytes": 28653,
  "PinnedEntries": 0,
  "DeletingEntries": 0,
  "ExpiredEntries": 0,
  "NextRunUtc": "2026-10-29T22:10:34.416490Z"
}
```

Metrics on the Admiral's Prometheus endpoint: `armada_blackbox_entries_total{phase,step,kind,triage}`,
`armada_blackbox_bytes{target}`, `armada_blackbox_gc_deleted_total{reason}` and
`armada_blackbox_capture_failures_total`.

## 10. Security and redaction

- **Redaction** runs on the record, every text evidence object and every rendered brief, before storage and again
  before rendering. Rules, in order: known secret values (model endpoint keys, storage credentials, any setting marked
  secret), private key blocks, `Authorization` headers, URLs with embedded credentials, GitHub, Anthropic, OpenAI, AWS,
  Azure, Google and Slack tokens, JSON web tokens, and `password=` / `secret=` / `token=` / api key / access key pairs.
  Matches become `[REDACTED]`; the record's `redaction` section lists rule names and counts, never values.
- **Tenancy.** Entries are tenant-scoped. An entry you cannot see is 404, never 403, so nothing leaks whether another
  tenant's entry exists. WebSocket commands, like every WebSocket command, need a global administrator.
- **Host details.** The `user` and `agent` briefs include absolute paths and host names (they are your machines).
  Anything that leaves the installation (a brief, the maintainer bundle) can be rendered with `redactHosts=true`, which
  strips absolute paths, host names and user names. `export-fixture` also removes tenant and user ids and the
  repository URL.
- **Integrity.** Every object carries the SHA-256 of its stored bytes; a mismatch is 409 `BlackBoxObjectCorrupt`. The
  Disk target refuses keys outside its root and paths that cross a symbolic link.
- **Actions.** Recovery and recommendation runs execute only recorded actions, authorized as the caller (section 5).
- **Credentials.** External store credentials are secret settings: masked on read, written through the settings API,
  never in entries, logs or briefs.

## 11. Troubleshooting

| Symptom | Cause and fix |
|---|---|
| 404 `BlackBoxNotFound` for a failed mission | No entry yet (the sweeper captures within `sweeper.intervalSeconds`; run `POST /api/v1/blackbox/backfill` for older failures), the mission belongs to another tenant or user, or `blackBox.enabled` is false. A mission that never failed has no entry. |
| 503 "The black box is not available on this server." | The black box runtime is not wired (for example the standalone stdio MCP server). Use the Admiral's HTTP MCP server or REST. |
| 503 `BlackBoxStoreUnavailable` | The external target cannot be reached or is misconfigured. Check `GET /api/v1/blackbox/storage` and the doctor's Black Box Storage check. New entries fall back to the database while `fallbackToDatabase` is true. |
| 410 `BlackBoxObjectMissing` | The object is gone from its store (deleted outside Armada, or a fallback-off write failed). The entry state is `ObjectMissing`; `record.json` and the briefs still work because the record is always in the database. |
| 409 `BlackBoxObjectCorrupt` | The stored bytes do not match their SHA-256. Restore the object from a backup of the store, or delete the entry. |
| 409 `BlackBoxDeleting` | The entry is being deleted; a store failure leaves it Deleting until the next garbage collection run retries it. |
| 409 `BlackBoxConfirmationRequired` | Run the recovery dry run again: the token was missing, used, expired (10 minutes), for another caller, or the mission changed. |
| The brief says "Captured by the backfill after the fact" or "by the sweeper" | The failure site did not record it inline, so the volatile git facts and the exception are missing. Use the evidence objects and the mission log. |
| "Target movement omitted: the mission's start commit was not recorded" | Missions provisioned before the upgrade have no start commit; conflict attribution needs it. New missions record it at every provision. |
| Facts recorded as unknown on a Harbor-hosted mission | The Harbor was not connected during enrichment. Reconnect it and `POST /api/v1/blackbox/{id}/reanalyze` (tenant admin). |
| An entry stays `EnrichmentFailed` | Enrichment failed `maxEnrichmentAttempts` times; the record from capture is intact. Reanalyze after fixing the cause (`lastError` on the entry says what failed). |
| Old entries do not reflect new rules | Entries analyzed with an older rules version are re-analyzed in the background after each sweep; `reanalyze` forces one. |
| The database grows | Check `GET /api/v1/blackbox/retention`, lower `retentionDays` or `maxTotalBytes`, run `POST /api/v1/blackbox/gc {"DryRun": true}` to preview, or move objects to an external target. |

## 12. Prevention, forensics and operations

Built on the black box (plan section 13); the developer contract is in section 13.21. Every read is scoped like the
entry reads (a user sees their own entries, vessels and voyages) and every MCP tool below is read-only, so Ask Armada
calls them without a proposal.

- **Overlap warnings at dispatch.** Before a mission is created, Armada estimates the files it will touch (its
  `Touch only ...` directive and the repository paths its brief names that exist in the vessel) and compares them with
  the vessel's active missions, the directories the black box marks as conflict hotspots and the directories active
  missions reserve. The warnings come back as `OverlapWarnings` on `POST /api/v1/missions`, `POST /api/v1/voyages`,
  MCP `create_mission` and `dispatch`. Preview them first with `POST /api/v1/vessels/{id}/overlap-preview` or
  `preview_mission_overlap`.
- **Chaining overlapping missions.** Set `serializeOverlappingMissions` on the vessel (REST, `add_vessel`,
  `update_vessel`) and a mission that overlaps an active one is dispatched with `DependsOnMissionId` set to it, so it
  starts after that mission lands. A dependency you set yourself is never replaced.
- **Soft directory reservations.** An active mission whose scoped files sit in a hotspot directory (at least
  `blackBox.hotspotReservationThreshold` entries, default 3, 0 turns it off) reserves it; a second mission there is
  warned or chained. List them with `GET /api/v1/vessels/{id}/reservations` or `list_vessel_reservations`.
- **Brief linter.** `POST /api/v1/missions/lint-brief` or `lint_mission_brief` flags dependency language without a
  dependency ("after mission 2 lands"), verification-only briefs that tend to fail as NoOp (dispatch those as Audit or
  Research), host paths, and paths the vessel does not have. Dispatch runs the same lint and returns
  `BriefLintWarnings`; it never blocks.
- **Configuration doctor.** `GET /api/v1/vessels/{id}/blackbox-doctor` or `get_vessel_blackbox_doctor` lists the
  recommendations that keep recurring on a vessel ("BBX-LAND-001 recommended LandingMode PullRequest 6 times this
  month"), with an example entry to apply it from.
- **Attempt diff.** `GET /api/v1/blackbox/{id}/diff/{otherId}` or `diff_blackbox_entries`: what changed between two
  attempts (captain, model, base commit, configuration, changed files, step).
- **Nearby successes.** Successful landings are kept for `blackBox.successRetentionDays` (default 7, 0 off); a failure
  lists the landings on the same vessel around it that touched the same files (`Related.NearbySuccesses`,
  `GET /api/v1/blackbox/{id}/nearby-successes`, `list_blackbox_nearby_successes`).
- **Combined timeline.** `GET /api/v1/blackbox/{id}/timeline` or `get_blackbox_timeline`: one ordered timeline across
  the entry and its notes, the mission, the dock, the merge queue entry, the captain and the Harbor link.
- **Voyage cascade graph.** `GET /api/v1/voyages/{id}/blackbox/cascade` or `get_voyage_blackbox_cascade`: which failure
  took the others down (DependsOn, CancelledBecause, BlockedBy and Sibling edges, each mission with its entry).
- **Runbook linkage.** Add a line `blackbox: bbx-land-001.pull-request` (a recovery path, a rule id, a failure step or
  a failure kind) to a runbook's description and the matching recovery path starts that runbook when you run it.
- **Recovery outcomes.** Every confirmed recovery run is followed until the mission's next terminal status and noted on
  the entry; `GET /api/v1/blackbox/recovery-stats` or `get_blackbox_recovery_stats` shows each path's success rate,
  which the advisor uses to rank equally strong paths.

The maintainer bundle (`POST /api/v1/blackbox/bundle`, `get_blackbox_bundle`), fingerprint trends per Armada version,
regression fixtures (`export-fixture`) and the Prometheus metrics come from the same plan section.

### Where the extensions appear in the UIs

| Extension | Dashboard | VS Code | Mobile |
|---|---|---|---|
| Overlap and brief lint preview | **Dispatch preview** under the brief on Dispatch, Create Mission, Create Voyage (per mission) and the pipeline run form, refreshed as you type (debounced; overlap preview with a vessel, brief lint without) | **Armada: Preview Mission Overlap** (Command Palette and the Armada Failures title bar): the open repository's vessel by remote URL, then a title and brief; opens the warnings, brief lint, suggested dependency and likely files | Live check on Dispatch, Create Voyage (per mission) and Create Mission |
| Dispatch warnings (create responses) | **Dispatch warnings** banner on the missions list after Create Mission, and on the voyage page after Dispatch, Create Voyage or a pipeline run | Shown by the preview command | A sheet of the returned warnings before the new mission or voyage opens; a toast after a pipeline run, planning dispatch or Retry Failed |
| Serialize overlapping missions | Vessel **Edit**: **Serialize Overlapping Missions** (with a tooltip); the Reservations card says whether overlaps are chained or warned | | |
| Reservations | Vessel page **Reservations** card (paged, linked to the reserving missions) | | |
| Configuration doctor | Vessel page **Black Box doctor** card (window 7, 30, 90 or 365 days, paged, links to the example entry and mission, **Apply...** with a dry run and confirmation where the recommendation has an action) | Entry view: **Recurring on this vessel** (the first five findings, linked to their example entries) | |
| Combined timeline | Entry panel and page: **Timeline** section (paged, oldest or newest first, entity links; open by default on `/blackbox/{id}`) | **Armada: Show Black Box Timeline** (Command Palette, Failures context menu, entry view button; 100 per page) | Entry screen **Timeline** (ten per page, Load more, linked) |
| Attempt diff | Entry panel and page: **Attempt diff** section (pick another attempt of the mission or type any entry id; side by side values and changed files) | **Armada: Compare Black Box Attempts** (Command Palette, Failures context menu, entry view button) | |
| Nearby successes | Entry panel and page: **Nearby successes** section (paged, linked to the missions that landed) | | Mission entry screen **Nearby successes** (ten per page, Load more, linked) |
| Voyage cascade graph | Voyage page **Cascade graph** card for a voyage with failures: an SVG graph (nodes open the entry or mission, the root outlined, a legend for the four edge kinds) and the same missions and edges as a list | | Failed voyage detail **Cascade** list (missions with status, failure kind, root marker and edges, linked) |
| Runbook linkage | Recovery paths show **Runs runbook: {title}** linked to the runbook | Entry view: the linked runbook on each path | Entry screen: **Runbook: {name}** on each path |
| Recovery statistics | Missions page Black Box tab, **Recovery stats** section (paged by rule and path, filter by vessel and rule, success rate bars); each recovery path shows its success rate | Entry view: each path's success rate | Entry screen: each path's success rate |

The TUI and the Helm CLI are described below.

### In the TUI

- **Dispatch preview and warnings.** Dispatch, Create Voyage and the Create Mission dialog take `Alt+P` (listed in the
  status bar or the dialog footer): with a vessel chosen it calls the overlap preview (which carries the brief lint),
  without one the brief lint alone, and shows the result; nothing is dispatched. After a create, the `OverlapWarnings`
  and `BriefLintWarnings` of the response open in a **Dispatch Warnings** viewer with a warning toast.
- **Vessel screen.** `L` toggles **Serialize Overlapping Missions** (also a checkbox in the edit form, and shown on the
  Overview). The **Reservations** tab pages the soft directory reservations (Enter opens the mission). The **Black Box
  Doctor** tab pages the recurring recommendations; on an actionable one `A` applies it through the same dry run and
  confirm flow as recovery (the example entry's recommendation, written as Operator), `i` shows the details and Enter
  opens the example entry. Advice-only findings show no `A`.
- **Entry screen.** **Timeline** pages the combined timeline oldest first (Enter opens the row's mission, voyage, dock,
  merge entry or captain; `<` `>` page). **Diff** compares the entry with the mission's previous attempt; `D` picks
  another attempt or any entry id. **Nearby Successes** pages the landings around the failure that touched the same
  files (Enter opens the mission).
- **Voyage screen.** The **Cascade** tab shows the cascade graph as a tree from the root failure (`*`), each mission
  with its status, failure kind, the edges that link it and its entry; Enter opens the entry, `m` the mission.
- **Black Box tab (Missions).** `R` shows the recovery stats (runs, outcomes and success rate per path, for the vessel filter).
- **Runbooks.** A recovery path linked to a runbook shows `Runbook: <title> (<id>)` and its tracked success rate on the
  Failure tab, `[runbook: <title>]` in the `r` menu, and the dry run plan says the runbook runs instead of the steps.

Each tab's keys come first in the status bar while it is current, and every action is in the `.` menu and the palette.

### In Helm

```
armada blackbox timeline <entryId> [--newest-first] [--page] [--page-size]
armada blackbox diff <entryId> <otherId>
armada blackbox nearby <entryId> [--page] [--page-size]
armada blackbox recovery-stats [--rule BBX-LAND-001] [--vessel vsl_...]
armada voyage cascade <voyageId>              (also: armada blackbox cascade <voyageId>)
armada vessel overlap-preview <vessel> --brief "<text>" | --brief-file <path|-> [--title] [--depends-on] [--exclude-mission]
armada vessel reservations <vessel>
armada vessel doctor <vessel> [--window-days 30]
armada vessel update <vessel> --serialize-overlapping-missions true|false
armada mission lint-brief --brief "<text>" | --brief-file <path|-> [--title] [--vessel] [--depends-on] [--voyage]
```

`<vessel>` is a name or an id. Every command takes `--json` (the payload alone on stdout) and uses the black box exit
codes: 0 success (overlap warnings and lint findings are advice and never change it), 1 failure or bad option (no
brief, both `--brief` and `--brief-file`, an unreadable file, `--window-days` outside 1 to 365, `vessel update` with
nothing to change, the same entry twice for `diff`), 2 credential or target error, 3 not found (an unknown entry,
voyage or vessel). `vessel update` reads the vessel and writes it back whole, so only the named setting changes.

# Part 2: developer contract

## 13. Developer contract

Everything below exists on `feature/blackbox` and is covered by tests. Surfaces (REST, MCP, WebSocket commands,
clients, UIs) call only `IBlackBoxService`; failure sites call only `IBlackBoxCapture` (through
`BlackBoxCaptureExtensions`); enrichment and analysis plug in through `IBlackBoxEnricher` and `IBlackBoxAdvisor`; storage
targets plug in through `IBlackBoxStore` and `BlackBoxStoreFactory`.

### 13.1 Composition and wiring

- `BlackBoxRuntime.Create(DatabaseDriver database, ArmadaSettings settings, LoggingModule logging, IBlackBoxVersionProvider? version = null, IBlackBoxEventSink? events = null)`
  builds every component once and exposes them: `Redactor`, `Stores`, `Objects`, `Renderer`, `Writer`, `Events`,
  `Capture`, `Pipeline`, `Advisor`, `Reanalyzer`, `Queue`, `Sweeper`, `GarbageCollector`, `Migrator`, `Hooks`, `Service`,
  `Background`. It sets `Pipeline.Advisor` to a `BlackBoxAdvisor` over `BlackBoxRuleCatalog.Default`,
  `Pipeline.SummaryWriter` to a `ModelEndpointBlackBoxSummaryWriter`, registers `BlackBoxConfigurationEnricher` as the
  first enricher, and gives `Background` the `Reanalyzer` and the `Migrator`. `Events` wraps the given sink in a
  `MeteringBlackBoxEventSink` (section 13.19). `ApplySettingsChanged(previousRetention)` drops cached stores,
  registers new secrets with the redactor and, when `retentionDays` or `resolvedRetentionDays` changed, starts the
  background expiry recompute; `PUT /api/v1/settings` copies `blackBox` into the live settings object
  (`BlackBoxSettings.CopyFrom`, so every component sees the change) and then calls it.
- The Admiral (`src/Armada.Server/ArmadaServer.cs`) builds one runtime right after `MissionService`, with
  `BuildIdentityBlackBoxVersionProvider` (BuildIdentity version and commit) and a `DatabaseBlackBoxEventSink` whose
  `OnEmitted` broadcasts each event to the tenant's WebSocket clients. It sets `MissionService.BlackBox`, starts
  `Background` (enrichment worker, sweeper every `blackBox.sweeper.intervalSeconds`, garbage collection every
  `blackBox.retention.gcIntervalMinutes`; both check `blackBox.enabled` on every tick) and stops it before the database
  is disposed; `Background.Start()` also resumes unfinished storage migrations. `ArmadaServer.BlackBox` exposes the
  runtime to routes and tests. After registering the MCP tools the Admiral sets `Service.ActionExecutor` to a
  `BlackBoxMcpActionExecutor` (recovery runs and recommendation applies run through the registered MCP tool handlers,
  as the caller). The Admiral passes `runtime.Hooks` (`IBlackBoxLifecycleHooks`) to `MissionRoutes`, `VesselRoutes`,
  `TenantRoutes` (`BlackBoxHooks` property) and `McpToolRegistrar.RegisterAll(..., blackBoxHooks)`, and the runtime to
  `StatusRoutes.BlackBox` (doctor check and settings changes).
- Namespaces: services in `Armada.Core.BlackBox`, storage in `Armada.Core.BlackBox.Storage`, models in
  `Armada.Core.Models`, enums in `Armada.Core.Enums`, settings in `Armada.Core.Settings`, database interfaces in
  `Armada.Core.Database.Interfaces`, shared SQL in `Armada.Core.Database.BlackBox`.

### 13.2 Wiring a failure site (one line)

Update the failing row first, then record it. The capture never throws, records the caller's member, file and line as
the failure site, and is a no-op when the black box is not wired (`null`):

```csharp
MissionStatusEnum statusBefore = mission.Status;
mission.Status = MissionStatusEnum.Failed;
mission.FailureKind = MissionFailureKindEnum.ScopeViolation;
mission.FailureReason = "...";
await _Database.Missions.UpdateAsync(mission, token).ConfigureAwait(false);
await BlackBox.RecordMissionFailureAsync(mission, statusBefore, BlackBoxStepEnum.ScopeViolation, context =>
{
    context.Dock = dock;                                  // rows you already hold save reads
    context.OutOfScopeFiles.AddRange(outOfScopeFiles);    // facts only the failure site knows
    context.GitFacts = new BlackBoxGitFacts { ConflictedFiles = conflicted, TargetTipAfter = tip };
    context.Exception = ex;
}, token).ConfigureAwait(false);
```

The reference site is `MissionService.TryFailMissionForScopeViolationAsync` (`src/Armada.Core/Services/MissionService.cs`).
Services that fail missions expose `public IBlackBoxCapture? BlackBox { get; set; }` and the Admiral assigns
`runtime.Capture`. Voyages use `RecordVoyageFailureAsync(voyage, statusBefore, step, reason, configure, token)`.
`BlackBoxCaptureContext` fields: Mission (required), StatusBefore, Step, CaptureMode, Exception, ExitCode,
RuntimeFailureKind, FailingCommand, CommandExitCode, CommandOutput, GitFacts, ScopedFiles, OutOfScopeFiles,
BoundaryFindings, CheckResults, MergeEntryId, IncidentId, StageOutcomeId, SuspectedCause, CauseRuleId, Reason (used when
the site writes no Mission.FailureReason, such as a non-terminal entry), Note, Vessel, Captain, CaptainId (when the site
cleared Mission.CaptainId before its write), Voyage, Dock, CallerMember, CallerFile, CallerLine, Component. `BlackBoxGitFacts`: ConflictedFiles, DirtyPaths,
TargetTipBefore, TargetTipAfter, WorktreeExisted, CheckoutPath, BranchTip, GitOperation, GitStderr.
`BlackBoxVoyageCaptureContext`: Voyage (required), StatusBefore, Step, CaptureMode, Reason, Exception, RootMissionId,
SettlementRule, HaltReason, Note, CallerMember, CallerFile, CallerLine, Component.

What a capture does: numbers the attempt and supersedes the previous current entry (`CreateCurrentAsync`), builds
Subject, Status, Site, Mechanism, Classification and Configuration (from the mission, vessel, voyage, captain, dock,
Harbor and global settings), Workspace, Conflict (with a depth 1 to 3 directory roll-up) and Scope from the context,
adds the previous attempt, computes the fingerprint and expiry, redacts and bounds the record, writes `record.json`,
the three briefs and in-memory evidence (`evidence/agent-output.txt`, `evidence/diff.patch`), writes `blackbox_files`
rows (Conflicted, DirtyInCheckout, OutOfScope), sets `missions.last_blackbox_entry_id` with a narrow update (no
`last_update_utc` change), emits `blackbox.captured` and queues enrichment. Everything beyond the index insert runs
under `blackBox.captureTimeoutMs` (default 2000); on timeout record.json is rewritten with `truncation.timedOut` and
the rest is left to enrichment.

Site conventions (every failure write in plan 5.1 is wired; `Services.BlackBoxCaptureSites` checks each step):

- Read `StatusBefore` before the mutation; capture only after the row update succeeded (for a compare-and-set write, only
  when it wrote). Pass the rows you hold (`Dock`, `Captain`, `Vessel`, `Voyage`) so the entry keeps ids the site cleared.
- `BlackBoxSiteFacts.ApplyException(context, ex)` records the exception and the git facts it carries
  (`GitMergeConflictException.ConflictedFiles`, `GitDirtyCheckoutException.ModifiedPaths` and checkout, `GitCommandException`
  command and stderr, `LandingVerificationException` command and exit code). `LandingStepOf`, `ExecutionStepOf` and
  `MergeQueueStepOf` map an exception, a `RuntimeFailureKindEnum` or a `MergeQueueFailure` to a step.
- Non-terminal entries: a provisioning failure that returns the mission to Pending (once per mission and cause), a
  dependent held as Blocked, an unconfirmed stop (once per episode), an orphaned exit held as WorkProduced, a landing not
  attempted, an expired landing deferral, a used-up rescue budget. `StatusAfter` is the status the mission kept.
- Triage: operator stops and cancellations (`OperatorStop`, the External phase, kind `OperatorAction`, a voyage
  `Cancelled`) are `External`.
- Kind: an `OperatorStop` or External-phase entry (`OperatorTransition`, `VesselDeleted`) whose mission row has no
  `FailureKind` records `FailureKind` `OperatorAction` on the entry and in `classification.kind`, so every surface shows
  a kind.
- Every mission capture whose status changed emits `mission.status_changed` with `MissionStatusChangedPayload`
  (`PreviousStatus`), through `BlackBoxCapture.MissionStatusChanged` (the Admiral's `EmitMissionStatusChangedAsync`) or as
  an event row, whether or not the black box is enabled.
- Volatile facts: `DockService` writes the start commit into `Dock.GitAnchorsJson` on every provision (`DockStartCommit`)
  and `Mission.StartCommit` is set when the dock is recorded; checks read the `.start` file, then the anchors, then the
  mission. `BlackBoxCapture.Harbors` records `HarborConnectionManager.GetLinkState(harborId)` for Harbor-hosted docks.
  `MissionLandingHandler` passes `WorktreeExisted=false` when it had to recreate, or could not recreate, the worktree,
  and the dock head and target tip read by `LandingTargetSynchronization`. A memory-pressure deferral records one typed
  `mission.dispatch_deferred` event (`MissionDispatchDeferredPayload`) per Pending mission and reason.
- Not captured inline: `NoEligibleCaptain` and `MemoryPressure` (waits, not failure writes). `VesselDeleted` is named by
  the vessel delete on REST and MCP, which cancel each active mission through `CancelMissionAsync` with
  `MissionCancelOptions` (`BlackBoxOperatorCapture.VesselDeletedCancel`), so the cancel records exactly one
  `VesselDeleted` entry per mission; the WebSocket command records it directly. Other cancels keep `OperatorTransition`
  or `OperatorStop`.

### 13.3 Services
### IBlackBoxCapture

`src/Armada.Core/BlackBox/IBlackBoxCapture.cs`

```csharp
Task<string?> CaptureMissionFailureAsync(BlackBoxCaptureContext context, CancellationToken token = default);
Task<string?> CaptureVoyageFailureAsync(BlackBoxVoyageCaptureContext context, CancellationToken token = default);
```

### IBlackBoxService (the facade every surface calls)

`src/Armada.Core/BlackBox/IBlackBoxService.cs`

```csharp
Task<BlackBoxEntry> GetMissionEntryAsync(AuthContext caller, string missionId, bool includeRecord = true, CancellationToken token = default);
Task<BlackBoxEntry> GetVoyageEntryAsync(AuthContext caller, string voyageId, bool includeRecord = true, CancellationToken token = default);
Task<BlackBoxEntry> GetEntryAsync(AuthContext caller, string entryId, bool includeRecord = true, CancellationToken token = default);
Task<string> ResolveEntryIdAsync(AuthContext caller, string? missionId, string? voyageId, string? entryId, CancellationToken token = default);
Task<BlackBoxDigest> GetDigestAsync(AuthContext caller, string entryId, CancellationToken token = default);
Task<BlackBoxRecord> GetRecordAsync(AuthContext caller, string entryId, CancellationToken token = default);
Task<BlackBoxSectionResult> GetSectionAsync(AuthContext caller, string entryId, BlackBoxSectionEnum section, CancellationToken token = default);
Task<string> GetBriefAsync(AuthContext caller, string entryId, BlackBoxAudienceEnum audience = BlackBoxAudienceEnum.Agent, bool redactHosts = false, CancellationToken token = default);
Task<EnumerationResult<BlackBoxEntrySummary>> EnumerateMissionEntriesAsync(AuthContext caller, string missionId, EnumerationQuery? query, CancellationToken token = default);
Task<EnumerationResult<BlackBoxEntrySummary>> EnumerateAsync(AuthContext caller, BlackBoxEnumerateRequest? query, CancellationToken token = default);
Task<EnumerationResult<BlackBoxEntrySummary>> SearchAsync(AuthContext caller, BlackBoxSearchRequest request, CancellationToken token = default);
Task<EnumerationResult<BlackBoxObjectSummary>> EnumerateObjectsAsync(AuthContext caller, string entryId, EnumerationQuery? query, CancellationToken token = default);
Task<Armada.Core.BlackBox.Storage.BlackBoxObjectReadResult> ReadObjectAsync(AuthContext caller, string entryId, string name, long offset = 0, long? length = null, CancellationToken token = default);
Task<BlackBoxObjectChunk> ReadObjectChunkAsync(AuthContext caller, string entryId, string name, long offset = 0, int? length = null, CancellationToken token = default);
Task<byte[]> DownloadAsync(AuthContext caller, string entryId, CancellationToken token = default);
Task<BlackBoxNote> AddNoteAsync(AuthContext caller, string entryId, BlackBoxNoteAddRequest request, BlackBoxNoteAuthorKindEnum defaultAuthor = BlackBoxNoteAuthorKindEnum.Operator, CancellationToken token = default);
Task<EnumerationResult<BlackBoxNote>> EnumerateNotesAsync(AuthContext caller, string entryId, EnumerationQuery? query, CancellationToken token = default);
Task<BlackBoxEntry> ResolveAsync(AuthContext caller, string entryId, BlackBoxResolveRequest request, CancellationToken token = default);
Task<BlackBoxEntry> PinAsync(AuthContext caller, string entryId, bool pinned, CancellationToken token = default);
Task<BlackBoxDeleteResult> DeleteAsync(AuthContext caller, string entryId, CancellationToken token = default);
Task<BlackBoxDeleteResult> DeleteManyAsync(AuthContext caller, BlackBoxDeleteManyRequest request, CancellationToken token = default);
Task<BlackBoxPurgeResult> PurgeAsync(AuthContext caller, BlackBoxPurgeRequest request, CancellationToken token = default);
Task<BlackBoxGcReport> RunGcAsync(AuthContext caller, BlackBoxGcRunRequest? request, CancellationToken token = default);
Task<BlackBoxRetentionStatus> GetRetentionStatusAsync(AuthContext caller, CancellationToken token = default);
Task<BlackBoxStorageStatus> GetStorageStatusAsync(AuthContext caller, CancellationToken token = default);
Task<BlackBoxStorageMigrationJob> StartStorageMigrationAsync(AuthContext caller, BlackBoxStorageMigrationRequest request, CancellationToken token = default);
Task<BlackBoxStorageMigrationJob> GetStorageMigrationAsync(AuthContext caller, string migrationId, CancellationToken token = default);
Task<BlackBoxBackfillResult> BackfillAsync(AuthContext caller, BlackBoxBackfillRequest? request, CancellationToken token = default);
Task<BlackBoxEntry> ReanalyzeAsync(AuthContext caller, string entryId, CancellationToken token = default);
Task<BlackBoxRecoveryRunResult> RunRecoveryAsync(AuthContext caller, string entryId, string pathId, BlackBoxRecoveryRunRequest request, CancellationToken token = default);
Task<BlackBoxRecommendationApplyResult> ApplyRecommendationAsync(AuthContext caller, string entryId, string recommendationId, BlackBoxRecommendationApplyRequest request, CancellationToken token = default);
Task<BlackBoxBundleResult> GetBundleAsync(AuthContext caller, BlackBoxBundleRequest? request, CancellationToken token = default);
Task<EnumerationResult<BlackBoxHotspot>> EnumerateHotspotsAsync(AuthContext caller, BlackBoxHotspotEnumerateRequest? request, CancellationToken token = default);
Task<EnumerationResult<BlackBoxFingerprintSummary>> EnumerateFingerprintsAsync(AuthContext caller, BlackBoxFingerprintEnumerateRequest? request, CancellationToken token = default);
Task<BlackBoxFingerprintTrend> GetFingerprintTrendAsync(AuthContext caller, string fingerprint, CancellationToken token = default);
Task<BlackBoxFixture> ExportFixtureAsync(AuthContext caller, string entryId, string? name = null, CancellationToken token = default);
```

### IBlackBoxEnricher

`src/Armada.Core/BlackBox/IBlackBoxEnricher.cs`

```csharp
string Name { get; }
Task EnrichAsync(BlackBoxEnrichmentContext context, CancellationToken token = default);
```

### IBlackBoxAdvisor

`src/Armada.Core/BlackBox/IBlackBoxAdvisor.cs`

```csharp
int RulesVersion { get; }
BlackBoxAnalysis? Analyze(BlackBoxRecord record);
```

Implemented by `BlackBoxAdvisor` (section 13.13).

### IBlackBoxActionExecutor

`src/Armada.Core/BlackBox/IBlackBoxActionExecutor.cs`

```csharp
Task<BlackBoxRecoveryStepResult> ExecuteAsync(AuthContext caller, BlackBoxAction action, CancellationToken token = default);
```

### IBlackBoxSummaryWriter

`src/Armada.Core/BlackBox/IBlackBoxSummaryWriter.cs`

```csharp
Task<string?> WriteSummaryAsync(string endpointId, BlackBoxRecord record, BlackBoxAnalysis analysis, CancellationToken token = default);
```

### IBlackBoxEnrichmentQueue

`src/Armada.Core/BlackBox/IBlackBoxEnrichmentQueue.cs`

```csharp
int Pending { get; }
bool Enqueue(string entryId);
```

### IBlackBoxEventSink

`src/Armada.Core/BlackBox/IBlackBoxEventSink.cs`

```csharp
Task EmitAsync(string eventType, string message, string? tenantId, string? entryId, string? missionId, string? voyageId, string? vesselId, object payload, CancellationToken token = default);
```

### IBlackBoxVersionProvider

`src/Armada.Core/BlackBox/IBlackBoxVersionProvider.cs`

```csharp
string Version { get; }
string? Commit { get; }
```

Service rules: a caller that is not authenticated, or not a global admin and without a tenant, gets
`UnauthorizedAccessException`. Visibility: global admin all tenants, tenant admin their tenant, user their own entries
(`BlackBoxEntry.UserId`). Invisible entries read as `BlackBoxNotFoundException` (no existence leak); an entry in the
Deleting state reads as `BlackBoxDeletingException`. Request validation errors are `ArgumentException` or
`ArgumentOutOfRangeException` (bounds: notes 1 to 8000 characters, search query 1 to 200, REST reads at most 1 MB,
MCP chunks at most 64 KB, delete at most 1000 ids as `CollectionLimitExceededException`). Briefs are rendered live from
the record (so renderer improvements apply to old entries) and include the ten newest notes. Storage migration
(`StartStorageMigrationAsync`, `GetStorageMigrationAsync`) needs a global admin (`UnauthorizedAccessException`
otherwise); Start throws `ArgumentException` for an Unknown, equal-to-From or misconfigured target (REST 400),
`InvalidOperationException` while a different migration is unfinished (409) and `BlackBoxStoreUnavailableException` when
the target fails its health check (503); Get throws `BlackBoxNotFoundException` for an unknown job (404). Starting the
move that is already unfinished returns that job. `GetStorageStatusAsync` fills `ActiveMigrationId` and reports a
misconfigured target as unhealthy with the reason.

Other public classes:

| Class | Role |
|---|---|
| `BlackBoxCapture` | `IBlackBoxCapture`; `Enrichment` property; `static TrimSourcePath(string?)` |
| `BlackBoxCaptureExtensions` | `RecordMissionFailureAsync`, `RecordVoyageFailureAsync` (one-line helpers) |
| `BlackBoxService` | `IBlackBoxService`; `static Summarize(BlackBoxEntry)`, `static SummarizeObject(BlackBoxObject)` |
| `BlackBoxPipeline` | `AddEnricher(IBlackBoxEnricher)`, `Advisor` (default `NoOpBlackBoxAdvisor`; the runtime sets `BlackBoxAdvisor`), `SummaryWriter`, `SummaryTimeoutMs` (30000), `ProcessAsync(entryId)`, `ReanalyzeAsync(entryId)` (advisor only), `LoadRecordAsync(entry)` |
| `BlackBoxAdvisor` | `IBlackBoxAdvisor`; `Catalog`; `static Analyze(record, catalog, diagnostics)`, `static Tally(votes)` |
| `BlackBoxRuleCatalog` | `CurrentRulesVersion`, `Default`, `Rules`, `Find(id)`, `static BuildRules()`; rule building blocks `BlackBoxRule`, `BlackBoxRuleRecommendation`, `BlackBoxRuleImprovement`, `BlackBoxRulePath`, `BlackBoxRuleStep` |
| `BlackBoxRuleTemplate`, `BlackBoxRulePlaceholders`, `BlackBoxRuleFacts` | Placeholder rendering, placeholder values, derived facts shared by rule predicates |
| `BlackBoxActionCommands` | `SupportedTools`, `DataLossTools`, `IsSupported`, `Rest(action)`, `Cli(action)` |
| `BlackBoxReanalyzer` | `RunOnceAsync()`: re-analyzes entries with an older rules version (`PageSize` 100, `MaxPagesPerRun` 5, `MaxPerRun` 100) |
| `BlackBoxConfigurationResolver` | `Resolve(inputs)`, `LoadAsync(...)` (persona, workflow profile), `Merge(target, extra)`; `BlackBoxConfigurationInputs`; `BlackBoxConfigurationEnricher` |
| `BlackBoxConfirmationStore` | `Issue`, `Consume`, `Lifetime` (10 minutes), `MaxPending` (10000), `Clock`; `BlackBoxPendingConfirmation` |
| `BlackBoxFixtureExporter` | `Sanitize(record)`, `Build(record, advisor, ...)`, `Serialize`, `Parse`, `WriteAsync(fixture, directory)` |
| `ModelEndpointBlackBoxSummaryWriter` | `IBlackBoxSummaryWriter` over a model endpoint (`MaxTokens` 400, `MaxPromptChars` 12000), `BuildPrompt` |
| `BlackBoxMcpActionExecutor` (Armada.Server) | `IBlackBoxActionExecutor` through `AskActionService.ExecuteAsync` |
| `BlackBoxEnrichmentQueue` | `IBlackBoxEnrichmentQueue`; `Start()`, `StopAsync()`, `DrainAsync()`; capacity 10000 |
| `BlackBoxEnrichmentContext` | Entry, Record, Mission, Voyage, Vessel, and collectors Files, Links, Objects (`BlackBoxPendingObject`: Name, ContentType, Data, IsText) |
| `BlackBoxEntryWriter` | Writes record.json, briefs and evidence with redaction, bounds, fallback and the time budget; saves totals |
| `BlackBoxSweeper` | `SweepAsync()`, `BackfillAsync(since, maxSubjects)`, `static InferStep(Mission)`, `StaleCapturedAfter` (10 minutes) |
| `BlackBoxGarbageCollector` | `RunAsync(dryRun)`, `DeleteEntryAsync(entry, reason)` (two-phase), `LastReport`, `NextRunUtc` |
| `BlackBoxBackgroundService` | `Start()`, `StopAsync()`: queue worker, sweeper loop, GC loop |
| `BlackBoxBriefRenderer` | `Render(entry, record, audience, redactHosts)`, `static ObjectName(audience)`, `TargetBytes` (12 KB), `MaxBytes` (40 KB), `CutMarker` |
| `BlackBoxRedactor` | `Redact(text, report)`, `RedactUtf8(bytes, report)`, `RedactRecord(record)`, `RegisterSecrets(values)`, `Rules`, `Marker` |
| `BlackBoxFingerprint` | `Compute(phase, step, kind, method, reason)`, `NormalizeReason(reason)` |
| `BlackBoxTaxonomy` | `PhaseOf(step)`, `StepsOf(phase)` |
| `BlackBoxRecordBounder` | `Bound(record)`, `AddNote`, `AddField`, `StringCap(propertyName)` |
| `BlackBoxRetentionPolicy` | `ComputeExpiry(entry, policy, vesselRetentionDays)` |
| `BlackBoxLimits` | Every limit (collection sizes, string caps, record 256 KB, brief 12 KB target and 40 KB cap, chunk sizes) |
| `BlackBoxJson` | `Options` (camelCase, tolerant enums, unknown fields skipped), `SerializeRecord`, `DeserializeRecord` |
| `BlackBoxSchemaGenerator` | `Generate()`, `GenerateJson()`; source of `docs/blackbox-record.schema.json` |
| `BlackBoxSettingsSecrets` | `Mask(settings, marker)`, `KeepMasked(incoming, current, marker)` (used by `GET`/`PUT /api/v1/settings`) |
| `BlackBoxObjectNames`, `BlackBoxEventTypes` | Stable object names and event types |
| `DatabaseBlackBoxEventSink`, `AssemblyBlackBoxVersionProvider`, `NoOpBlackBoxAdvisor`, `NoOpBlackBoxEnricher` | Defaults |

Exceptions (`Armada.Core.BlackBox`, base `BlackBoxException` with `Code`): `BlackBoxNotFoundException`
(BlackBoxNotFound, REST 404), `BlackBoxDeletingException` (BlackBoxDeleting, 409), `BlackBoxObjectMissingException`
(BlackBoxObjectMissing, 410), `BlackBoxObjectCorruptException` (BlackBoxObjectCorrupt, 409),
`BlackBoxStoreUnavailableException` (BlackBoxStoreUnavailable, 503), `BlackBoxConfirmationException`
(BlackBoxConfirmationRequired, 409, for recovery runs).

### 13.4 Storage
### IBlackBoxStore

`src/Armada.Core/BlackBox/Storage/IBlackBoxStore.cs`

```csharp
BlackBoxStorageTargetEnum Target { get; }
Task WriteAsync(BlackBoxObjectWrite write, CancellationToken token = default);
Task<Stream> OpenReadAsync(BlackBoxObjectRef reference, long offset, long? length, CancellationToken token = default);
Task<bool> ExistsAsync(BlackBoxObjectRef reference, CancellationToken token = default);
Task DeleteAsync(BlackBoxObjectRef reference, CancellationToken token = default);
Task<BlackBoxStoreDeleteResult> DeleteManyAsync(BlackBoxObjectRefSet references, CancellationToken token = default);
IAsyncEnumerable<BlackBoxStoredKey> EnumerateAsync(string prefix, CancellationToken token = default);
Task<BlackBoxStoreHealth> CheckHealthAsync(CancellationToken token = default);
```

- `DatabaseBlackBoxStore` (default): bytes in `blackbox_objects.data` (SQLite BLOB, PostgreSQL bytea, SQL Server
  varbinary(max), MySQL LONGBLOB); ranged reads are cut in SQL; the metadata row must exist before its bytes are written.
- `BlobjectBlackBoxStore(target, BlobClientBase client, diskRoot)` (`IDisposable`): every external target over its
  Blobject 6.1.2 client (`Blobject.Disk`, `Blobject.AmazonS3` for S3 and S3-compatible stores such as MinIO and Less3,
  `Blobject.AzureBlob`, `Blobject.GoogleCloud`, `Blobject.CIFS`, `Blobject.NFS`). Objects live at their `StorageKey`.
  Reads fetch the whole object (`GetAsync`) and slice it (objects are bounded by `maxEvidenceObjectBytes`); the Disk
  target reads ranges straight from the file. Not-found errors of every SDK (FileNotFound, KeyNotFound, S3 404, Azure
  404, Google 404) become `BlackBoxObjectMissingException`; other failures become `BlackBoxStoreUnavailableException`
  (no secrets in messages). Deleting a missing key succeeds; `DeleteManyAsync` maps Blobject's per-key results back to
  object names. `EnumerateAsync(prefix)` skips folder entries and reports `LastUpdateUtc` in UTC (the Disk target walks
  the file system itself and skips reparse points). `CheckHealthAsync` is Blobject `ValidateConnectivity`. The Disk
  target (`ResolveDiskPath(key)`) refuses keys with `.`/`..` segments, rooted keys, keys with `:` and keys that resolve
  outside its root, and refuses any path below the root that crosses a symbolic link or other reparse point (write,
  read, exists, delete); the root itself may sit under a link (for example macOS `/var`).
- `BlackBoxStoreFactory(DatabaseDriver, BlackBoxSettings, string? dataDirectory = null)`: `Get(target)`,
  `GetConfigured()`, `Register(target, builder)` (test doubles and other stores), `Reset()` (disposes cached built-in
  stores), `DiskDirectory`. `CreateBuiltIn` has one `case` per target (Database, Disk, S3, AzureBlob, GoogleCloud, Cifs,
  Nfs); a target whose settings are invalid or whose client cannot be built throws `BlackBoxStoreUnavailableException`
  ("The black box S3 storage target is misconfigured: blackBox.storage.s3.bucket is required ..."), which the writer
  treats like a store failure (fallback to the database); `Unknown` throws `NotSupportedException`.
- `BlackBoxBlobClientFactory`: `Create(target, storageSettings, dataDirectory)` builds the Blobject client from validated
  settings; `ResolveDiskDirectory(settings, dataDirectory)` is the configured directory (relative paths resolve against
  the data directory) or `{DataDirectory}/blackbox`. S3 with a custom endpoint uses path-style requests (Blobject forces
  them; `usePathStyle` only shapes generated URLs); without an endpoint it is AWS S3 (no keys means the SDK's default
  credential chain). Azure takes account name and key, or a connection string (AccountName, AccountKey, optional
  BlobEndpoint), and `endpoint` (default `https://{account}.blob.core.windows.net/`). Google Cloud reads the service
  account JSON from `credentialsPath` and takes an optional `endpoint` (an emulator). CIFS resolves the host name when
  the client is built, so an unknown host is reported as misconfigured.
- `BlackBoxStorageSettingsValidator`: `Validate(storageSettings, target = null)` returns errors that name the setting
  (`blackBox.storage.s3.bucket is required for the S3 target.`) and never contain a secret: S3 bucket, region, both or
  neither keys, http/https endpoint with a host whose scheme agrees with `useSsl` (keys required with an endpoint);
  Azure container and account name plus key or a connection string with AccountName and AccountKey, http/https
  endpoint; Google project, bucket and an existing credentials file, http/https endpoint; CIFS host (a valid host name
  or address), share, user and password; NFS host and an absolute export path; Disk a valid path; Unknown is refused.
  `Require` throws `ArgumentException` with every error. `PUT /api/v1/settings` validates `blackBox.storage` before
  applying anything (400 "Invalid black box storage settings: ...").
- `BlackBoxStorageDoctor.CheckAsync(settings, stores)` returns `BlackBoxStorageDoctorResult` (Status, Message): Pass when
  the configured target validates and its health check passes, Fail with the validation errors or the health message
  (plus what fallback will do), Warn when the black box is disabled or the runtime is not running. It is the twelfth
  `GET /api/v1/doctor` check, named "Black Box Storage".
- `BlackBoxStorageMigrator`: see section 13.20.
- Storage inventory: `StorageInventoryService` adds one `StorageLocation` per target that holds entries to the
  Admiral's host report (Category `BlackBox`, EntityId the target, Path `database:blackbox_objects`, the Disk directory,
  `s3://bucket/prefix`, `azure://account/container/prefix`, `gs://bucket/prefix`, `smb://host/share/prefix` or
  `nfs://host/export/prefix`, Bytes from the index, FileCount the entry count; the Database figure is also inside the
  database files).
- `BlackBoxBackupInventoryBuilder.BuildAsync(database, maxListed)` returns `BlackBoxBackupInventory` (DatabaseEntries,
  DatabaseBytes, ExternalEntries, ExternalTargets, ExternalEntryRefs (EntryId, Target, StoragePrefix, ObjectCount,
  TotalBytes; at most 10000), ExternalEntryRefsTruncated, Message). Backups (SQLite) contain Database-target entries
  inside `armada.db`; `manifest.json` gains a `blackBox` section with this inventory, the backup result a `BlackBox`
  summary, and the restore result a `BlackBox` section (counts, targets, up to 100 external entry ids) plus a sentence
  in its message when restored entries point at objects outside the database.
- `BlackBoxObjectRepository`: `WriteAsync(entry, name, contentType, raw, compress, forceDatabase)` and
  `ReadAsync(entry, name, offset, length)`; `static RefOf(entry, obj)`. An object whose `StorageKey` is null lives in the
  database whatever the entry's target (record.json is forced there when an external store fails).
- `BlackBoxObjectCodec`: `Encode(raw, compress)` (gzip or identity), `Decode(stored, encoding)`, `Sha256Hex(bytes)`. The
  SHA-256 is over the stored (encoded) bytes and is checked on every whole read and every read of a gzip object
  (always read whole); a mismatch throws `BlackBoxObjectCorruptException`.
- `BlackBoxKeyLayout`: `BuildEntryPrefix(keyPrefix, tenantId, vesselId, createdUtc, entryId)` gives
  `{keyPrefix}/{tenantId}/{vesselId or _}/{yyyy}/{MM}/{entryId}`; `BuildKey(prefix, objectName)`; `Segment(value)`
  sanitizes so a key never escapes its prefix.
- Store DTOs: `BlackBoxObjectRef` (Target, EntryId, ObjectId, Name, StorageKey), `BlackBoxObjectWrite` (Reference,
  ContentType, ContentEncoding, Data, Sha256), `BlackBoxObjectRefSet` (References, at most 1000),
  `BlackBoxStoreDeleteResult` (Requested, Deleted, Failed, FailedNames, FirstError), `BlackBoxStoredKey` (Key,
  SizeBytes, LastModifiedUtc), `BlackBoxEncodedObject`, `BlackBoxObjectReadResult` (Object, Data, Offset, TotalBytes,
  Verified).
- Fallback: when an external write of record.json fails and `blackBox.storage.fallbackToDatabase` is true, the entry
  moves to the Database target and `blackbox.storage_fallback` is emitted; with fallback off, record.json is written to
  the database, the other objects are marked missing and the entry state is ObjectMissing.

### 13.5 Database
### IBlackBoxEntryMethods (DatabaseDriver.BlackBoxEntries)

`src/Armada.Core/Database/Interfaces/IBlackBoxEntryMethods.cs`

```csharp
Task<BlackBoxEntry> CreateAsync(BlackBoxEntry entry, CancellationToken token = default);
Task<BlackBoxEntry> CreateCurrentAsync(BlackBoxEntry entry, CancellationToken token = default);
Task<BlackBoxEntry?> ReadAsync(string id, string? tenantId = null, bool includeDeleting = false, CancellationToken token = default);
Task<BlackBoxEntry?> ReadCurrentByMissionAsync(string missionId, string? tenantId = null, CancellationToken token = default);
Task<BlackBoxEntry?> ReadCurrentByVoyageAsync(string voyageId, string? tenantId = null, CancellationToken token = default);
Task<EnumerationResult<BlackBoxEntry>> EnumerateAsync(BlackBoxEnumerateRequest query, CancellationToken token = default);
Task<EnumerationResult<BlackBoxEntry>> EnumerateByMissionAsync(string missionId, string? tenantId, EnumerationQuery query, CancellationToken token = default);
Task<EnumerationResult<BlackBoxEntry>> EnumerateByFingerprintAsync(string fingerprint, string? tenantId, EnumerationQuery query, CancellationToken token = default);
Task<long> CountAsync(BlackBoxEnumerateRequest query, CancellationToken token = default);
Task<BlackBoxEntry> UpdateAsync(BlackBoxEntry entry, CancellationToken token = default);
Task<bool> MarkSupersededAsync(string id, CancellationToken token = default);
Task<bool> UpdateStateAsync(string id, BlackBoxStateEnum state, string? lastError = null, int? enrichmentAttempts = null, DateTime? nextAttemptUtc = null, CancellationToken token = default);
Task<bool> UpdateResolutionAsync(string id, BlackBoxResolutionEnum resolution, string? note, string? link, DateTime? resolvedUtc, DateTime? expiresUtc, CancellationToken token = default);
Task<bool> UpdatePinnedAsync(string id, bool pinned, DateTime? expiresUtc, CancellationToken token = default);
Task<bool> UpdateExpiresAsync(string id, DateTime? expiresUtc, CancellationToken token = default);
Task<bool> DeleteCascadeAsync(string id, CancellationToken token = default);
Task<EnumerationResult<BlackBoxEntry>> EnumerateExpiredAsync(DateTime nowUtc, bool keepLatestPerMission, EnumerationQuery query, CancellationToken token = default);
Task<EnumerationResult<BlackBoxEntry>> EnumerateDeletingAsync(EnumerationQuery query, CancellationToken token = default);
Task<EnumerationResult<BlackBoxEntry>> EnumerateRetryableAsync(DateTime nowUtc, int maxAttempts, EnumerationQuery query, CancellationToken token = default);
Task<EnumerationResult<BlackBoxFingerprintSummary>> EnumerateFingerprintsAsync(BlackBoxFingerprintEnumerateRequest query, CancellationToken token = default);
Task<long> SumBytesAsync(string? tenantId = null, string? vesselId = null, CancellationToken token = default);
Task<List<BlackBoxStorageTargetUsage>> SumBytesByTargetAsync(string? tenantId = null, CancellationToken token = default);
Task<long> CountByVesselAsync(string vesselId, string? tenantId = null, CancellationToken token = default);
Task<EnumerationResult<string>> EnumerateUncapturedMissionIdsAsync(DateTime? updatedSince, EnumerationQuery query, CancellationToken token = default);
Task<EnumerationResult<string>> EnumerateUncapturedVoyageIdsAsync(DateTime? updatedSince, EnumerationQuery query, CancellationToken token = default);
Task<bool> SetMissionLastEntryAsync(string missionId, string? entryId, CancellationToken token = default);
Task<bool> SetVoyageLastEntryAsync(string voyageId, string? entryId, CancellationToken token = default);
Task<EnumerationResult<BlackBoxEntry>> EnumerateEvictionCandidatesAsync(BlackBoxEvictionQuery query, CancellationToken token = default);
Task<EnumerationResult<string>> EnumerateVesselsOverQuotaAsync(int maxEntries, EnumerationQuery query, CancellationToken token = default);
Task<EnumerationResult<BlackBoxEntry>> EnumerateCascadeCandidatesAsync(BlackBoxCascadeReasonEnum reason, string? subjectId, EnumerationQuery query, CancellationToken token = default);
Task<EnumerationResult<BlackBoxEntry>> ScanAsync(BlackBoxScanQuery query, CancellationToken token = default);
Task<bool> UpdateStorageAsync(string id, BlackBoxStorageTargetEnum target, string? storagePrefix, CancellationToken token = default);
```

Eviction candidates are unpinned and not Deleting, oldest first, filtered by `BlackBoxEvictionQuery` (VesselId,
ResolvedOnly, ExcludeCurrent, KeepLatestPerMission, PageNumber, PageSize). Vessels over quota count non-Deleting
entries. Cascade candidates: MissionDeleted (Mission entries whose mission row is gone), VesselDeleted (entries whose
vessel row is gone), both without pinned entries, and TenantDeleted (every entry of the tenant, pinned included).
`ScanAsync` is a keyset scan in id order (`BlackBoxScanQuery`: AfterId, StorageTarget, ExcludeStorageTarget, VesselId,
NeedsMigrationTo, PageSize; TotalRecords counts what remains after the cursor); `NeedsMigrationTo` selects entries
whose target differs or, for an external target, that still hold a database-resident object that is not missing.
`UpdateStorageAsync` changes only `storage_target` and `storage_prefix`.

### IBlackBoxObjectMethods (DatabaseDriver.BlackBoxObjects)

`src/Armada.Core/Database/Interfaces/IBlackBoxObjectMethods.cs`

```csharp
Task<BlackBoxObject> UpsertAsync(BlackBoxObject obj, CancellationToken token = default);
Task<BlackBoxObject?> ReadAsync(string entryId, string name, bool includeData = false, CancellationToken token = default);
Task<BlackBoxObject?> ReadByIdAsync(string id, bool includeData = false, CancellationToken token = default);
Task<byte[]?> ReadDataRangeAsync(string id, long offset, int length, CancellationToken token = default);
Task<bool> WriteDataAsync(string id, byte[]? data, CancellationToken token = default);
Task<bool> SetMissingAsync(string id, bool missing, CancellationToken token = default);
Task<EnumerationResult<BlackBoxObject>> EnumerateAsync(string entryId, EnumerationQuery query, CancellationToken token = default);
Task<int> DeleteByEntryAsync(string entryId, CancellationToken token = default);
Task<List<string>> FilterReferencedStorageKeysAsync(List<string> storageKeys, CancellationToken token = default);
Task<bool> SetLocationAsync(string id, string? storageKey, byte[]? data, CancellationToken token = default);
```

`FilterReferencedStorageKeysAsync` (at most 1000 keys, queried 100 at a time) returns the keys an object row
references; `SetLocationAsync` sets `storage_key` and `data` together and clears `missing` (storage migration).

### IBlackBoxFileMethods (DatabaseDriver.BlackBoxFiles)

`src/Armada.Core/Database/Interfaces/IBlackBoxFileMethods.cs`

```csharp
Task<int> CreateManyAsync(List<BlackBoxFile> files, CancellationToken token = default);
Task<EnumerationResult<BlackBoxFile>> EnumerateAsync(string entryId, BlackBoxFileRoleEnum? role, EnumerationQuery query, CancellationToken token = default);
Task<EnumerationResult<BlackBoxHotspot>> EnumerateHotspotsAsync(BlackBoxHotspotEnumerateRequest query, CancellationToken token = default);
Task<int> DeleteByEntryAsync(string entryId, CancellationToken token = default);
```

### IBlackBoxLinkMethods (DatabaseDriver.BlackBoxLinks)

`src/Armada.Core/Database/Interfaces/IBlackBoxLinkMethods.cs`

```csharp
Task<int> CreateManyAsync(List<BlackBoxLink> links, CancellationToken token = default);
Task<EnumerationResult<BlackBoxLink>> EnumerateAsync(string entryId, EnumerationQuery query, CancellationToken token = default);
Task<EnumerationResult<BlackBoxLink>> EnumerateByMissionAsync(string missionId, string? tenantId, EnumerationQuery query, CancellationToken token = default);
Task<int> DeleteByEntryAsync(string entryId, CancellationToken token = default);
```

### IBlackBoxNoteMethods (DatabaseDriver.BlackBoxNotes)

`src/Armada.Core/Database/Interfaces/IBlackBoxNoteMethods.cs`

```csharp
Task<BlackBoxNote> CreateAsync(BlackBoxNote note, CancellationToken token = default);
Task<BlackBoxNote?> ReadAsync(string id, string? tenantId = null, CancellationToken token = default);
Task<EnumerationResult<BlackBoxNote>> EnumerateAsync(string entryId, EnumerationQuery query, CancellationToken token = default);
Task<long> CountByEntryAsync(string entryId, CancellationToken token = default);
Task<bool> DeleteAsync(string id, CancellationToken token = default);
Task<int> DeleteByEntryAsync(string entryId, CancellationToken token = default);
```

### IBlackBoxMigrationMethods (DatabaseDriver.BlackBoxMigrations)

`src/Armada.Core/Database/Interfaces/IBlackBoxMigrationMethods.cs`

```csharp
Task<BlackBoxStorageMigrationJob> CreateAsync(BlackBoxStorageMigrationJob job, CancellationToken token = default);
Task<BlackBoxStorageMigrationJob?> ReadAsync(string id, CancellationToken token = default);
Task<bool> UpdateAsync(BlackBoxStorageMigrationJob job, CancellationToken token = default);
Task<EnumerationResult<BlackBoxStorageMigrationJob>> EnumerateActiveAsync(EnumerationQuery query, CancellationToken token = default);
Task<EnumerationResult<BlackBoxStorageMigrationJob>> EnumerateAsync(EnumerationQuery query, CancellationToken token = default);
```

Implementation: the SQL is shared in `Armada.Core.Database.BlackBox` (`BlackBoxEntryMethodsBase`,
`BlackBoxObjectMethodsBase`, `BlackBoxFileMethodsBase`, `BlackBoxLinkMethodsBase`, `BlackBoxNoteMethodsBase`,
`BlackBoxSqlPaging`) over a per-provider `BlackBoxSqlDialect` (`SqliteBlackBoxSqlDialect`, `PostgresqlBlackBoxSqlDialect`,
`SqlServerBlackBoxSqlDialect`, `MysqlBlackBoxSqlDialect`: connection, transactional write under the provider's write
gate, timestamps, BLOB binding, boolean literals, paging, BLOB slicing). Each provider has thin `BlackBox*Methods`
classes in its `Implementations` folder. Every list pages in SQL with a COUNT over the same filter, ordered with the
id as the tie-break. A null `tenantId` means any tenant. Entries in the Deleting state are hidden unless
`includeDeleting` or `State = Deleting` is asked for. A full-row mission or voyage update never clears
`last_blackbox_entry_id` (COALESCE); `DeleteCascadeAsync` clears it.

Migration 104 (all four providers) adds `blackbox_migrations` (id `bbm_...`, state, from_target, to_target,
delete_source, batch_size, total_entries, migrated_entries, failed_entries, bytes_copied, last_entry_id, last_error,
created_utc, started_utc, finished_utc, last_update_utc; index on state and created_utc), shared SQL in
`BlackBoxMigrationMethodsBase`. Active jobs are Pending or Running, oldest first.

Schema (migration 103, all four providers): `blackbox_entries`, `blackbox_objects` (unique `entry_id, name`; `data`
BLOB), `blackbox_files`, `blackbox_links`, `blackbox_notes`; indexes from the plan (tenant and mission and current,
voyage, vessel and created, fingerprint and created, expires, resolution and triage, files by tenant, vessel and
directory); new columns `missions.last_blackbox_entry_id`, `missions.start_commit`, `voyages.last_blackbox_entry_id`,
`vessels.blackbox_retention_days`. Model properties: `Mission.LastBlackBoxEntryId`, `Mission.StartCommit`,
`Voyage.LastBlackBoxEntryId`, `Vessel.BlackBoxRetentionDays` (clamped 0 to 3650, null uses the global policy).

### 13.6 Models

All in `Armada.Core.Models`, one class per file, every collection with `[CollectionBound]`:

`BlackBoxAction`, `BlackBoxAnalysis`, `BlackBoxAnalyzedPayload`, `BlackBoxAttempt`, `BlackBoxBackfillRequest`, `BlackBoxBackfillResult`, `BlackBoxBundleRequest`, `BlackBoxBundleResult`, `BlackBoxCaptureFailedPayload`, `BlackBoxCapturedPayload`, `BlackBoxClassification`, `BlackBoxCommitRef`, `BlackBoxConfigRecommendation`, `BlackBoxConfigValue`, `BlackBoxConfiguration`, `BlackBoxConflict`, `BlackBoxConflictingMission`, `BlackBoxDeleteManyRequest`, `BlackBoxDeleteResult`, `BlackBoxDeletedPayload`, `BlackBoxDiffStat`, `BlackBoxDigest`, `BlackBoxDirectoryCount`, `BlackBoxDownloadManifest`, `BlackBoxEntry`, `BlackBoxEntrySummary`, `BlackBoxEnumerateRequest`, `BlackBoxEventRef`, `BlackBoxEvidenceIndex`, `BlackBoxEvidenceItem`, `BlackBoxFile`, `BlackBoxFinding`, `BlackBoxFingerprintEnumerateRequest`, `BlackBoxFingerprintSummary`, `BlackBoxGcReport`, `BlackBoxGcRunRequest`, `BlackBoxHotspot`, `BlackBoxHotspotEnumerateRequest`, `BlackBoxImprovement`, `BlackBoxLink`, `BlackBoxMechanism`, `BlackBoxNote`, `BlackBoxNoteAddRequest`, `BlackBoxObject`, `BlackBoxObjectChunk`, `BlackBoxObjectSummary`, `BlackBoxPurgeRequest`, `BlackBoxPurgeResult`, `BlackBoxRecommendationApplyRequest`, `BlackBoxRecommendationApplyResult`, `BlackBoxRecord`, `BlackBoxRecoveryPath`, `BlackBoxRecoveryRunPayload`, `BlackBoxRecoveryRunRequest`, `BlackBoxRecoveryRunResult`, `BlackBoxRecoveryStep`, `BlackBoxRecoveryStepResult`, `BlackBoxRedaction`, `BlackBoxRedactionRuleCount`, `BlackBoxRelated`, `BlackBoxResolveRequest`, `BlackBoxResolvedPayload`, `BlackBoxRetentionStatus`, `BlackBoxScope`, `BlackBoxSearchRequest`, `BlackBoxSectionResult`, `BlackBoxSimilarEntry`, `BlackBoxSite`, `BlackBoxStatusChange`, `BlackBoxStatusCount`, `BlackBoxStatusTimelineEntry`, `BlackBoxStorageFallbackPayload`, `BlackBoxStorageMigrationJob`, `BlackBoxStorageMigrationRequest`, `BlackBoxStorageStatus`, `BlackBoxStorageTargetUsage`, `BlackBoxStoreHealth`, `BlackBoxSubject`, `BlackBoxTriageVerdict`, `BlackBoxTriageVote`, `BlackBoxTruncatedField`, `BlackBoxTruncation`, `BlackBoxVoyageRollup`, `BlackBoxWorkspace`.

Record sections (`BlackBoxRecord`): Subject, Status, Site, Mechanism, Classification, Workspace, Conflict, Scope,
Configuration, Attempts, Evidence, Analysis, Related, Voyage, Redaction, Truncation. A section that was not captured is
null (Attempts is an empty list; Redaction and Truncation are always present). Field-level detail is in
[BLACKBOX_SCHEMA.md](BLACKBOX_SCHEMA.md).

### 13.7 Enums

All with `[JsonConverter(typeof(JsonStringEnumConverter))]`; every enum has an `Unknown` member (tolerant readers map
values from a newer Armada to it):

- `BlackBoxAudienceEnum`: Agent, User, Maintainer, Unknown
- `BlackBoxBundleFormatEnum`: Markdown, Json, Unknown
- `BlackBoxCaptureModeEnum`: Inline, Sweeper, Backfill, Unknown
- `BlackBoxConfidenceEnum`: Low, Medium, High, Unknown
- `BlackBoxConfigSourceEnum`: Mission, Voyage, Vessel, WorkflowProfile, Captain, Persona, Global, Default, Unknown
- `BlackBoxFileRoleEnum`: Conflicted, DirtyInCheckout, OutOfScope, ChangedByMission, ChangedByConflictingMission, BoundaryViolation, Unknown
- `BlackBoxHostKindEnum`: Admiral, Harbor, Unknown
- `BlackBoxJobStateEnum`: Pending, Running, Completed, Failed, Cancelled, Unknown
- `BlackBoxLinkRelationEnum`: LandedOnTargetSinceStart, OverlappingFiles, SharedDock, SiblingInVoyage, DependencyOf, ParentOfRescue, RescueOf, CascadeRoot, Unknown
- `BlackBoxNoteAuthorKindEnum`: Operator, Agent, AskArmada, System, Unknown
- `BlackBoxPhaseEnum`: Unknown, Dispatch, Provision, Execution, Completion, Review, Handoff, Landing, MergeQueue, Recovery, Voyage, External
- `BlackBoxResolutionEnum`: Open, Resolved, Duplicate, WontFix, ArmadaFixed, Unknown
- `BlackBoxSectionEnum`: Subject, Status, Site, Mechanism, Classification, Workspace, Conflict, Scope, Configuration, Attempts, Evidence, Analysis, Related, Voyage, Redaction, Truncation, Unknown
- `BlackBoxSeverityEnum`: Low, Medium, High, Critical, Unknown
- `BlackBoxStateEnum`: Captured, Enriched, Analyzed, EnrichmentFailed, Deleting, ObjectMissing, Unknown
- `BlackBoxStepEnum`: Unknown, VoyageParentNotRunnable, AssignmentFailed, NoEligibleCaptain, HarborUnavailable, MemoryPressure, DockProvision, WorktreeCreate, BranchCreate, StartCommitRecord, ProcessCrash, UsageLimit, AuthFailure, ModelUnavailable, Stall, MaxRuntime, RecoveryExhausted, Interrupted, OperatorStop, NoOp, Unfinished, CaptainBlocked, InvalidOutput, ScopeViolation, BoundaryViolation, DefinitionOfDone, StageCheck, JudgeRejected, ReviewDenied, ReviewTimeout, ReviewApprovedDockMissing, PlanParse, StageBranchHarden, DependencyCancelled, IntegrationBarrier, DockMissing, TargetIntegration, Verification, DirtyCheckout, UnpublishedAncestry, Push, PullRequest, FastForward, LocalMerge, RetryLanding, DeferralExpired, QueueResolveRepo, QueueMergeConflict, QueueTests, QueueProcessing, QueuePush, RescueFailed, RedispatchExhausted, OrphanedExit, UnconfirmedStop, MissingMergeHandoff, SettlementFailed, Halted, Cancelled, StageFailed, VesselDeleted, OperatorTransition
- `BlackBoxStorageTargetEnum`: Database, Disk, S3, AzureBlob, GoogleCloud, Cifs, Nfs, Unknown
- `BlackBoxSubjectTypeEnum`: Mission, Voyage, Unknown
- `BlackBoxTriageEnum`: Unknown, UserConfig, MissionBrief, Transient, ArmadaDefect, External

- `BlackBoxCascadeReasonEnum`: MissionDeleted, VesselDeleted, TenantDeleted, Unknown

Each `BlackBoxStepEnum` value belongs to one phase (`BlackBoxTaxonomy.PhaseOf`); the table is in the plan, section 5.1.

### 13.8 Settings

`ArmadaSettings.BlackBox` (`BlackBoxSettings`, JSON `blackBox`): `Enabled` (true), `CaptureTimeoutMs` (2000, 100 to
30000), `Storage` (`BlackBoxStorageSettings`: `Target` (Database), `KeyPrefix` (armada/blackbox), `Compress` (true),
`MaxEvidenceObjectBytes` (1 MB, 64 KB to 16 MB), `MaxEntryBytes` (8 MB, 1 MB to 64 MB), `FallbackToDatabase` (true),
`Disk` (Directory, null for `{DataDirectory}/blackbox`), `S3` (Endpoint, Region us-east-1, Bucket, AccessKey,
SecretKey, UsePathStyle true, UseSsl true), `AzureBlob` (AccountName, Container, AccessKey, ConnectionString, Endpoint),
`GoogleCloud` (ProjectId, Bucket, CredentialsPath, Endpoint), `Cifs` (Hostname, Port 445, Share, Domain, Username,
Password, PreferEncryption true), `Nfs` (Hostname, ExportPath, Version 3 (2 to 4), Port and MountPort (null: the
portmapper), UserId 0, GroupId 0)), `Retention` (`BlackBoxRetentionSettings`: `RetentionDays` 180
(0 to 3650, 0 keeps forever), `ResolvedRetentionDays` 30, `KeepLatestPerMission` true, `MaxEntriesPerVessel` 1000,
`MaxTotalBytes` 1 GB, `DeleteWithMission` false, `DeleteWithVessel` true, `GcIntervalMinutes` 60, `GcBatchSize` 200,
`OrphanGraceHours` 24), `Sweeper` (`BlackBoxSweeperSettings`: `IntervalSeconds` 60, `PageSize` 100,
`MaxEnrichmentAttempts` 5), `SummaryEndpointId` (null), `HotspotReservationThreshold` (3, 0 to 1000, 0 turns
reservations off), `SuccessRetentionDays` (7, 0 to 90, 0 turns the success recorder off), `NearbySuccessWindowHours` (24,
1 to 168) and `InsightIntervalSeconds` (60, 5 to 3600) (section 13.21). Out-of-range values are clamped in the setters. Secrets
(S3 AccessKey and SecretKey, Azure AccessKey and ConnectionString, Google CredentialsPath, CIFS Password) are masked as
`********` in `GET /api/v1/settings` and kept when written back masked through `PUT /api/v1/settings` (`blackBox`);
`BlackBoxRuntime` registers them with the redactor so they never reach an entry.

### 13.9 Objects

| Name | Content type | Written by |
|---|---|---|
| `record.json` | application/json | capture, rewritten by the pipeline |
| `brief.md` | text/markdown | agent audience, capture and pipeline |
| `brief.user.md` | text/markdown | user audience |
| `brief.maintainer.md` | text/markdown | maintainer audience |
| `evidence/agent-output.txt` | text/plain | capture (Mission.AgentOutput) |
| `evidence/diff.patch` | text/x-diff | capture (Mission.DiffSnapshot) |
| `evidence/mission.log`, `evidence/diffstat.txt`, `evidence/check-<n>.txt`, `evidence/verification-<n>.txt`, `evidence/merge-entry.txt`, `evidence/git-status.txt`, `evidence/target-commits.txt` | text/plain | enrichers (13.17) |
| `evidence/events.json` | application/json | enrichers (13.17) |
| `manifest.json` | application/json | only inside a `.zip` download (`BlackBoxDownloadManifest`) |

Names are constants in `BlackBoxObjectNames`. Text evidence is redacted and capped at `maxEvidenceObjectBytes` with the
head and tail kept around a marker line; the original size is recorded in `truncation.fields`.

### 13.10 Events

Recorded as `ArmadaEvent` rows (EntityType `blackbox`, EntityId the entry id, Payload the camelCase JSON of the typed
payload) and broadcast to the tenant's WebSocket clients (declared in `WebSocketSurface`):

| Event | Payload |
|---|---|
| `blackbox.captured` | `BlackBoxCapturedPayload` (entryId, missionId, voyageId, vesselId, phase, step, kind, attempt, captureMode) |
| `blackbox.capture_failed` | `BlackBoxCaptureFailedPayload` (missionId, voyageId, step, error) |
| `blackbox.analyzed` | `BlackBoxAnalyzedPayload` (entryId, missionId, voyageId, state, triage, rulesVersion) |
| `blackbox.resolved` | `BlackBoxResolvedPayload` (entryId, missionId, voyageId, resolution) |
| `blackbox.deleted` | `BlackBoxDeletedPayload` (entryId, missionId, voyageId, reason) |
| `blackbox.storage_fallback` | `BlackBoxStorageFallbackPayload` (entryId, from, to, error) |
| `blackbox.gc_completed` | `BlackBoxGcReport` |
| `blackbox.recovery_run` | `BlackBoxRecoveryRunPayload` (entryId, pathId, recommendationId, dryRun, succeeded, stepsRun, stepsFailed, userId); every recovery dry run and run, and every recommendation dry run and apply |

### 13.11 Redaction

`BlackBoxRedactor` rules, in order: KnownSecretValue (registered values), PrivateKeyBlock, AuthorizationHeader,
UrlCredentials, GitHubToken, AnthropicKey, OpenAiKey, AwsAccessKeyId, AwsSecretAccessKey, AzureKey, GoogleApiKey,
SlackToken, JsonWebToken, SecretPair (password, secret, token, api key, access key pairs). Matches become
`[REDACTED]` (key names and URL schemes are kept); the record's `redaction` section lists rule names and counts, never
values. Redaction runs on the record, every text evidence object and every rendered brief.

### 13.12 Tests

`Database.BlackBoxEntries`, `Database.BlackBoxObjects`, `Database.BlackBoxFiles`, `Database.BlackBoxLinksNotes` (all
four providers through `scripts/common/run-db-parity-tests.sh`), `Services.BlackBoxCapture`, `Services.BlackBoxSweeper`,
`Services.BlackBoxGarbageCollector`, `Services.BlackBoxStore`, `Services.BlackBoxService`, `Services.BlackBoxRedactor`,
`Services.BlackBoxFingerprint`, `Services.BlackBoxBriefRenderer`, `Services.BlackBoxAdvisor` (a golden case per rule,
coverage, ties, placeholders, empty record, ranking, re-analysis, audiences), `Services.BlackBoxRecovery`,
`Services.BlackBoxFixtures`, `Services.BlackBoxMaintainer` (bundle, trends), `Services.BlackBoxSummary`,
`Services.BlackBoxConfiguration`, `Models.BlackBoxSchema` (schema drift and validation). Test helpers: `BlackBoxTestKit`,
`ScriptedBlackBoxStore`, `RecordingBlackBoxEventSink`, `BlackBoxSchemaValidator`, `BlackBoxGoldenCase`,
`BlackBoxRecoveryFixture`, `RecordingBlackBoxActionExecutor`, `ScriptedBlackBoxSummaryWriter`, `FakeChatEndpoint`. After changing a model, run `scripts/common/generate-blackbox-schema.sh`.

### 13.13 Advisor and rule catalog

`BlackBoxAdvisor.Analyze(record, catalog)` is pure and deterministic. It evaluates every rule's match predicate (a
throwing predicate is no match), renders the matched rules, lets generic rules step aside for specific ones that
rendered (`YieldsTo`), and falls back to `BBX-UNKNOWN-001` when nothing rendered. Findings, recommendations and paths are
ordered by vote weight, then catalog order; recovery paths are deduplicated by their actions, ranked 1 to n and capped at
10. Triage sums the weights per verdict; the highest total wins and a tie reports `Unknown` with `tiedWith` naming the
candidates (confidence: High when the winner has at least 4 and leads by 2, Medium from 2, else Low). The summary (at
most 6 sentences) is assembled from the subject, the top two findings, the triage, the top recovery path and the first
recommendation.

Rule text is a template: `{name}` is filled from the record (`BlackBoxRulePlaceholders.Names`, plus `{config:Setting}`
for an effective configuration value) and `{name|fallback}` uses the fallback when the value is missing. A why statement
with a missing placeholder skips the rule; a recommendation, improvement or recovery path with a missing placeholder is
left out (both logged at debug). Stable ids: a recovery path is `rule id lower case + "." + key` (for example
`bbx-land-001.stash-retry`), a recommendation the same (for example `bbx-land-001.landing-mode`).

`BlackBoxRuleCatalog.CurrentRulesVersion` (1) is stored on every analyzed entry and record. Bump it when a rule's match,
text, vote or recovery changes: `BlackBoxReanalyzer` (run after each sweep) re-analyzes Analyzed entries with an older
version through `BlackBoxPipeline.ReanalyzeAsync` (advisor and summary only, no enrichers).

| Rule | Phase | Matches | Vote |
|---|---|---|---|
| BBX-LAND-001 | Landing | step DirtyCheckout with dirty paths | UserConfig 3 |
| BBX-LAND-002 | Landing | worktree missing, step DockMissing, or "could not start process ... working directory" | ArmadaDefect 3 |
| BBX-LAND-003 | Landing | conflicted files and a conflicting mission that landed with overlapping files | UserConfig 2 |
| BBX-LAND-004 | Landing | every conflicted file is generated or append-only (CHANGELOG, i18n, API surface, Postman, lock files, schemas) | ArmadaDefect 3 |
| BBX-LAND-005 | Landing | step Verification | MissionBrief 2 |
| BBX-LAND-006 | Landing | step Push, PullRequest or UnpublishedAncestry | External 2 |
| BBX-LAND-007 | Landing | any other landing failure | none |
| BBX-MQ-001 | MergeQueue | kind LandingConflict with no conflicted files | ArmadaDefect 2 |
| BBX-MQ-002 | MergeQueue | step QueueTests | MissionBrief 1 |
| BBX-MQ-003 | MergeQueue | any other merge-queue failure | none |
| BBX-SCOPE-001 | Completion | scope violation whose out-of-scope entries are blank or scoped | ArmadaDefect 4 |
| BBX-SCOPE-002 | Completion | start commit missing on a scope failure | none |
| BBX-SCOPE-003 | Completion | a real scope violation | MissionBrief 2 |
| BBX-NOOP-001 | Completion | kind or step NoOp | MissionBrief 2 |
| BBX-DEP-001 | Completion | captain BLOCKED naming unlanded prerequisites dispatched in parallel | MissionBrief 4 |
| BBX-BLOCK-001 | Completion | any other BLOCKED report | MissionBrief 2 |
| BBX-OUT-001 | Completion | invalid output | UserConfig 1 |
| BBX-BOUND-001 | Completion | boundary violation | MissionBrief 2 |
| BBX-CHECK-001 | Completion | definition of done, stage check, compile or test failure | MissionBrief 1 |
| BBX-UNFIN-001 | Completion | step Unfinished | MissionBrief 1 |
| BBX-EXEC-001 | Execution | usage limit | Transient 3 |
| BBX-EXEC-002 | Execution | stall recovery exhausted | Transient 2 |
| BBX-EXEC-003 | Execution | max runtime exceeded | MissionBrief 2 |
| BBX-EXEC-004 | Execution | authentication failure | UserConfig 3 |
| BBX-EXEC-005 | Execution | model unavailable | Transient 2 |
| BBX-EXEC-006 | Execution | process crash | Transient 1 |
| BBX-EXEC-007 | Execution | operator stop or interruption | External 3 |
| BBX-EXEC-008 | Execution | recovery exhausted | Transient 1 |
| BBX-INFRA-001 | Dispatch | Harbor disconnected or unavailable | External 3 |
| BBX-DISP-001 | Dispatch | no eligible captain, assignment failed | UserConfig 2 |
| BBX-DISP-002 | Dispatch | any other dispatch failure | Transient 1 |
| BBX-PROV-001 | Provision | any provisioning failure | Transient 1 |
| BBX-REV-001 | Review | review denied | External 2 |
| BBX-REV-002 | Review | judge rejected | MissionBrief 2 |
| BBX-REV-003 | Review | review approved but the dock is gone | ArmadaDefect 3 |
| BBX-REV-004 | Review | review timed out | External 2 |
| BBX-DEP-002 | Handoff | dependency failed or cancelled | External 1 |
| BBX-HAND-001 | Handoff | any other handoff failure | ArmadaDefect 1 |
| BBX-RECOV-001 | Recovery | Armada's recovery failed | ArmadaDefect 2 |
| BBX-VOY-001 | Voyage | a voyage entry | none |
| BBX-EXT-001 | External | vessel deleted, operator transition | External 3 |
| BBX-UNKNOWN-001 | (fallback) | nothing else rendered | none |

Recorded actions name only `BlackBoxActionCommands.SupportedTools`: `retry_landing`, `restart_mission`, `cancel_mission`,
`unstick_dock`, `repair_dock`, `update_vessel` (which now takes `landingMode`, or `Default` to clear it),
`update_mission`. `cancel_mission` and `unstick_dock` steps are data-loss steps. Each step carries the MCP call, the
REST request and, where Helm has one, the CLI command.

The optional model-written summary: when `blackBox.summaryEndpointId` is set and `Pipeline.SummaryWriter` is wired,
the pipeline asks that model endpoint (through the same PolyPrompt clients as the ApiEndpoint runtime) for 3 to 6
sentences from the record facts and the rule findings, within `SummaryTimeoutMs`, and sets `summaryIsModelWritten`.
Any failure, timeout or empty reply keeps the rules summary.

### 13.14 Recovery contract

`RunRecoveryAsync(caller, entryId, pathId, request)` and `ApplyRecommendationAsync(caller, entryId, recommendationId,
request)` run only what the entry recorded:

1. The entry must be visible to the caller; the path (or recommendation, which needs an `applyAction`) must be in the
   entry's analysis (else `BlackBoxNotFoundException`; an advice-only recommendation is `ArgumentException`).
2. Every recorded action must name a supported tool and target the entry's own mission, vessel and dock
   (`ArgumentException`).
3. Every action is authorized as if the caller called the tool directly, against its `McpToolAuthorizationRegistry`
   requirement (`UnauthorizedAccessException`, before anything runs). The executor then runs the tool through its MCP
   handler as the caller, which repeats the tool's own authorization and tenant scoping.
4. `dryRun` (the default) returns the plan (or the action), runs nothing, and issues a confirm token (`bbc_...`) bound to
   the entry, the path or recommendation, the kind, the caller, the plan's SHA-256 and the subject's version (mission or
   voyage status and last update; plus the vessel's last update for recommendations). Tokens expire after 10 minutes,
   are single use, and live in memory.
5. A confirmed call (`dryRun: false`) needs the token (`BlackBoxConfirmationException` when missing). A path with
   `dataLossRisk` also needs `acknowledgeDataLoss: true` (checked before the token is used). The token is then consumed
   whatever happens next and refused when unknown, used, expired, for another entry, path, kind or caller, when the
   subject changed since the dry run, or when the plan changed (re-analysis); each refusal asks for a new dry run.
6. Steps run in order; a manual step is reported, not run; the first failed step stops the run and the rest report
   "Not run". Tools that return a result with `success: false` (such as `retry_landing`) count as failed.
7. The entry is annotated (`blackbox_notes`, `authorKind` from the request, default `Agent`; human UIs pass `Operator`)
   and `blackbox.recovery_run` is emitted for dry runs and runs. The result carries per-step results.
8. Where no `IBlackBoxActionExecutor` is wired, confirmed calls throw `BlackBoxActionsUnavailableException` (REST `503`
   `BlackBoxActionsUnavailable`, MCP `Unavailable`). A step whose precondition is not met is refused (`Refused`,
   `UnmetPreconditions`) on the dry run and on the confirm instead of being run.

### 13.15 Maintainer loop: bundle, trends, fixtures

- `GetBundleAsync`: up to 50 entries (filters of `BlackBoxEnumerateRequest`, `max` clamped to 1..50), grouped by
  fingerprint, most frequent first. Markdown: the preamble asking the agent to rank Armada improvements, check them
  against the source and write each as an Armada mission brief (with a fixture test, and new rules in catalog format),
  then per group the fingerprint, first and last seen, Armada versions, triage and rule ids, the newest entry's full
  maintainer brief and one-liners for the rest (cut at 1 MB). JSON: a `BlackBoxBundleDocument` with
  `BlackBoxBundleGroup`s. `redactHosts` strips host paths; `truncated` is set when more entries matched than were
  bundled.
- `GetFingerprintTrendAsync`: counts of one fingerprint per Armada version (`BlackBoxFingerprintTrend`, versions ordered
  by first sighting, each with open and ArmadaFixed counts; at most 1000 entries and 100 versions), scoped to the caller.
- `ExportFixtureAsync`: a `BlackBoxFixture` (sanitized record plus the rule ids and triage the advisor produces now);
  `BlackBoxFixtureExporter.WriteAsync` writes `{name}.json`. Fixtures live in `src/Test.Shared/Fixtures/BlackBox/` and
  `Services.BlackBoxFixtures` checks each one still produces its expected rule ids (the first one leading) and triage,
  is sanitized, and validates against the record schema. Current fixtures: `land-001-dirty-checkout`
  (msn_mvskboi0_vJGaqsNlGqU), `land-002-dock-deleted` (msn_mvsjeweh_tmCfX52gt3z), `scope-001-false-violation` (Garrison
  msn_mvsqzal6_b1BfR4tnZ29), `dep-001-parallel-dependency-mission-2` and `-mission-4` (the bounded-enumeration voyage).

### 13.16 Effective configuration

`BlackBoxConfigurationResolver.Resolve` records, with sources: LandingMode (voyage, vessel, global, MergeAndPush),
AutoPush, AutoCreatePullRequests, AutoMergePullRequests (voyage, global), BranchCleanupPolicy (vessel, global), the
mission's RequiresReview, WaitForVoyageWorkers, DependsOnMissionId, Persona, MissionMode and Tier, the vessel's
AllowConcurrentMissions, AutoApprove, RequirePassingChecksToLand, DefinitionOfDone settings, AutoLandEnabled,
ProtectedPathPatterns and DefaultPipelineId, the workflow profile's name, BuildCommand and UnitTestCommand, TestCommand
and LintCommand (vessel override, else profile), MergeQueueTestCommand, the captain's runtime, model, endpoint,
reasoning effort and permission policy, the persona's CaptainPriorities, PriorityAvailability, PriorityExhaustion and
DefaultCaptainId, the global thresholds and attempt limits, and BlackBoxRetentionDays (vessel, global); at most 64
values. Capture resolves from the rows it holds; `BlackBoxConfigurationEnricher` adds the persona and workflow profile
values without replacing what capture recorded.

### 13.17 Enrichment (built-in enrichers)

`BlackBoxRuntime.Create` registers the built-in enrichers (`Armada.Core.BlackBox.Enrichment`,
`BlackBoxEnricherCatalog.CreateDefaults`) on the pipeline, in this order. Each derives from `BlackBoxEnricherBase`,
which catches everything but cancellation and records `"<Name> enrichment did not complete (...)"` in
`truncation.notes`, so a failing host or git command never marks the entry EnrichmentFailed.

| Enricher | Records |
|---|---|
| `Workspace` | Worktree existence, `branchExists`, branch tip, ahead and behind the target (`GetDivergenceAsync`), commits ahead of start, porcelain of the worktree and of the target checkout (`evidence/git-status.txt`, dirty paths when capture had none), conflicted files of an unfinished merge, Harbor link state and last seen, `inspectedHost`, `inspectionNote`; ChangedByMission file rows (from the diff snapshot, the worktree, or start..tip) |
| `TargetMovement` | Commits on the target between the start commit and the target tip at the failure (or the current tip, noted), up to 500 read, newest 50 in `conflict.targetCommitsSinceStart` with `targetCommitTotal` and a truncation entry; `evidence/target-commits.txt`; `workspace.targetMovementNote` says why it was omitted (no start commit, unreachable host, start commit not in the repository) |
| `ConflictAttribution` | Per target commit: `Armada-Mission-Id` trailer, changed paths intersected with the conflicted files and the mission's changed paths; `conflict.conflictingMissions` (missions first, then commits outside Armada with their author), LandedOnTargetSinceStart links, ChangedByConflictingMission rows |
| `ConcurrentOverlap` | Other missions on the vessel active during the run window that touched the same files (diff snapshot, or their branch on the owning host, at most 10 branch reads), `conflict.concurrentMissions`, OverlappingFiles links |
| `DirectoryRollup` | `conflictedDirectories` (depth 1 to 3), `conflictedParentDirectories`, `deepestConflictedDirectory` (at least two files) |
| `Attempts` | Earlier entries, stage outcomes and handoff artifacts as `attempts` (bounded 20, oldest first); subject counters (redispatch, output repair, no-op); `subject.rescueChain`; RescueOf and ParentOfRescue (`Incident.RescueMissionIds`) links |
| `Evidence` | `evidence/agent-output.txt` (tail, when capture did not store it), `evidence/mission.log` (tail), `evidence/check-<n>.txt`, `evidence/verification-<n>.txt`, `evidence/merge-entry.txt`, `evidence/diff.patch` (worktree diff when no snapshot), `evidence/diffstat.txt` and `evidence.diffStat`, `evidence/events.json` and `evidence.relevantEvents` |
| `Related` | DependencyOf and CascadeRoot links up `DependsOnMissionId`, `related.cascadeRootEntryId`, `related.downstreamMissionIds`, SiblingInVoyage and SharedDock links (fingerprint matches and similar entries are added by the pipeline) |
| `Voyage` | Voyage entries: roll-up by status, failed missions and their entries, cascade root, downstream missions, CascadeRoot and SiblingInVoyage links |
| `FileRows` | Last: re-emits the captured roles (Conflicted, DirtyInCheckout, OutOfScope, BoundaryViolation for findings that name a path) with the rows above and removes duplicates; the pipeline replaces the entry's file and link rows with these sets, so reanalyze never duplicates rows |

Host routing: enrichers inspect docks through `VesselGitInspector` (`src/Armada.Core/Services/VesselGitInspector.cs`,
shared with `VesselCleanupService`): a Harbor-hosted dock is read on its Harbor through the Harbor git request path
(`GitService` over `RemoteHostCommandExecutor`); a Harbor that is not connected resolves to no host and its facts are
recorded as unknown. `BlackBoxRuntime.Inspector` starts with the Admiral host only; the Admiral sets
`Inspector.Hosts` to its Harbor-aware `DockHostResolver`. Tests use `Inspector.HarborHostOverride` with a stand-in
host (`RecordingHostCommandExecutor`).

Git reads added for forensics (`IGitService`, default implementations answer null): `GetMergeBaseAsync(repo, a, b)`
and `GetCommitRangeAsync(repo, from, to, limit)` (`GitCommitRange`: commits newest first with author, dates, subject,
`GitTrailer` list, `MissionId` from the `Armada-Mission-Id` trailer, changed paths capped at 200 per commit,
`TotalCount`, `Truncated`; limit 1 to 500). Trailers are parsed by `GitTrailerParser`.

Bounds: tails keep the last 256 KB (`BlackBoxForensics.TailBytes`) starting at a line; per-role file rows at most 200;
every cut is a `truncation.fields` entry; the writer then redacts every text object and caps it at
`maxEvidenceObjectBytes`. Suites: `Services.BlackBoxEnrichment`, `Services.GitForensics`.

Storage and retention suites: `Database.BlackBoxStorageQueries` and `Database.BlackBoxStoreContract` (the shared
contract on the Database target, all four providers, migrating to Disk), `Services.BlackBoxStoreContract.Disk`,
`.S3` (a throwaway MinIO container), `.AzureBlob` (Azurite), `.GoogleCloud` (fake-gcs-server), `.Cifs` (in-process
OpenCIFS.Server 0.1.1) and `.Nfs` (in-process OpenNFS.Server 0.2.1, NFSv3), all built from
`BlackBoxStoreContractSuiteBase`; `Services.BlackBoxStorageTargets` (validation, masking, Disk guards, unreachable and
misconfigured stores with and without fallback, oversized evidence, doctor, migration validation, failure and restart,
backup inventory, metrics, settings changes) and `Services.BlackBoxRetention` (plan section 16.5).
`BlackBoxStorageEmulators` (`Test.Shared/Infrastructure/BlackBoxStorage`) starts each target once per process
(containers labeled `armada.test=blackbox-storage`, removed at exit) and reports a skip reason when Docker or an
image is missing, so those suites skip instead of failing; `ARMADA_TEST_NO_DOCKER=1` skips them on purpose and
`ARMADA_TEST_MINIO_IMAGE`, `ARMADA_TEST_AZURITE_IMAGE` and `ARMADA_TEST_FAKEGCS_IMAGE` pick images (defaults:
`minio/minio:latest` or `pgsty/minio`, `mcr.microsoft.com/azure-storage/azurite:3.35.0`, `fsouza/fake-gcs-server:1.56.1`).
Against fake-gcs-server the enumerate and orphan-sweep cases are skipped: the emulator writes timestamps with
trailing-zero microseconds trimmed, which Google.Apis cannot always parse, so listing fails intermittently there.

### 13.18 Retention and garbage collection

`BlackBoxRetentionPolicy.ComputeExpiry(entry, policy, vesselRetentionDays)` sets `expires_utc` at capture, on resolve
and on pin and unpin (service), on a retention policy change (`ApplySettingsChanged` starts
`GarbageCollector.StartRecompute()`), and for one vessel's entries when the vessel changes
(`IBlackBoxLifecycleHooks.OnVesselUpdatedAsync`; `PUT /api/v1/vessels/{id}` calls it when `blackBoxRetentionDays`
changed, MCP `update_vessel` on every update). `RecomputeExpiryAsync(vesselId)` scans in id order and writes only
changed expiries (idempotent).

`BlackBoxGarbageCollector.RunAsync(dryRun)` (every `gcIntervalMinutes` and on demand) runs, in order:

1. Finish deletes left Deleting (`RetriedDeletes`).
2. Expire: `expires_utc` before `Clock()`, unpinned, not the current entry of a mission or voyage that still exists when
   `keepLatestPerMission` (`Expired`).
3. Quotas (`Evicted`): for each vessel above `maxEntriesPerVessel`, the oldest unpinned non-current entries down to the
   limit; then, while the sum of `total_bytes` is above `maxTotalBytes`, the oldest unpinned resolved entries, then the
   oldest unpinned (current entries of live subjects stay with `keepLatestPerMission`).
4. Cascades (`Cascaded`): entries whose vessel row is gone (`deleteWithVessel`, default true) and Mission entries whose
   mission row is gone (`deleteWithMission`, default false). Pinned entries are kept.
5. Orphan sweep (`OrphansRemoved`): for the configured external target and every external target that holds entries,
   keys under `{keyPrefix}/` with no object row and a modification time older than `orphanGraceHours` are deleted
   (keys of unknown age and keys outside the prefix are never touched; at most `MaxOrphanKeysPerRun`, 100000).
6. Missing check (`ObjectsMissing`): a rotating page of `gcBatchSize` entries; every object whose bytes are gone from
   its store is marked missing and the entry becomes ObjectMissing.
7. Report: `LastReport`, the bytes metric, and `blackbox.gc_completed` with the counts, `BytesFreed`, `Failures`,
   `Errors` and up to 100 `CandidateEntryIds`.

Deletes are two-phase (`DeleteEntryAsync`): mark Deleting (hidden), delete external bytes with `DeleteManyAsync` in
batches of 1000, then the rows; a store failure leaves the entry Deleting for the next run. A dry run counts and samples
candidates and deletes nothing (no event). `CascadeAsync(reason, subjectId)` and the hooks run a cascade at once:
`OnMissionDeletedAsync` (REST purge and delete-multiple, MCP `purge_mission` and `delete_missions`; follows
`deleteWithMission`), `OnVesselDeletedAsync` (REST and MCP vessel deletes; follows `deleteWithVessel`, then the mission
cascade for the vessel's deleted missions), `OnTenantDeletedAsync` (tenant deletion: every entry and object, pinned
ones too). Hooks never throw; the next run catches anything they miss (WebSocket deletes, voyage purges, data expiry).
`RetentionService` and `DataExpiryService` do not touch black box tables. `MissionArchive` snapshots record the
mission's current entry as `BlackBoxEntryId`.

### 13.19 Metrics

`BlackBoxMetrics` instruments on Armada's meter, exported by the Admiral's telemetry host (Prometheus names):
`armada_blackbox_entries_total{phase,step,kind,triage}` (each capture; triage as captured),
`armada_blackbox_bytes{target}` (stored bytes per target, refreshed after each garbage collection run and each admin
storage status read), `armada_blackbox_gc_deleted_total{reason}` (expired, evicted, cascaded, retried, orphan) and
`armada_blackbox_capture_failures_total`. `MeteringBlackBoxEventSink` records them from the events it forwards.

### 13.20 Storage migration

`BlackBoxStorageMigrator` (`runtime.Migrator`): `StartAsync(request, runInBackground)`, `RunAsync(jobId)`,
`ResumePendingAsync()`, `GetAsync(id)`, `StopAsync()`, `ActiveJobId`, `OnEntryProcessedAsync` (test hook). A job
(`BlackBoxStorageMigrationJob`, `bbm_` id, persisted in `blackbox_migrations`) pages through
`ScanAsync(NeedsMigrationTo = To, StorageTarget = From)` in id order, `BatchSize` (1 to 1000) at a time, and saves its
progress and cursor (`LastEntryId`) after every entry, so it resumes after a restart (`Background.Start()`) and running
it again skips entries already moved; a job with nothing to move is created Completed. Per entry: every external
object is read, checked against its SHA-256, written to the same key on the target and read back and checked again;
database-resident objects are read, checked, copied to `{prefix}/{name}` and checked; only then is the entry switched
(`UpdateStorageAsync`), the database rows pointed at their keys (`SetLocationAsync`; their bytes cleared, or kept when
`DeleteSource` is false) and the source copies deleted. Moving to Database writes each verified object into its row
before switching the entry. A corrupt or unreadable object fails only its entry (`FailedEntries`, `LastError`), which
stays where it was; the job ends Completed, or Failed when any entry failed. Every step leaves every object readable.

### 13.21 Prevention, forensics and operations (plan 13)

All in `Armada.Core.BlackBox`, built by `BlackBoxInsights` (`runtime.Insights`: `Analyzer`, `DispatchAdvisor`,
`Service`, `Successes`, `PathStats`, `Outcomes`, `Runbooks`, `NearbySuccessEnricher`, `Background`). The constructor
wires itself in: it adds the nearby-success enricher to the pipeline, sets `Pipeline.AnalysisHook` to the runbook linker,
`Advisor.PathStats` to the path statistics and `Service.RecoveryObserver` to the outcome tracker. The Admiral sets
`AdmiralService.DispatchAdvisor = runtime.Insights.DispatchAdvisor`, registers `BlackBoxInsightRoutes` (before
`BlackBoxRoutes`, so `/api/v1/blackbox/recovery-stats` wins over `/api/v1/blackbox/{id}`) and
`McpBlackBoxInsightTools`, and starts and stops `Insights.Background` with the other black box workers. Every read
goes through `BlackBoxInsightService`, which scopes like `BlackBoxService` (global admin all tenants, tenant admin the
tenant, user their own entries, vessels and voyages; anything else is `BlackBoxNotFoundException` for an entry and
`KeyNotFoundException` for a vessel or voyage, REST 404 and MCP NotFound). Limits are constants in
`BlackBoxInsightLimits`.

```csharp
Task<MissionOverlapPreview> PreviewOverlapAsync(AuthContext caller, string vesselId, MissionOverlapPreviewRequest? request, CancellationToken token = default);
Task<VesselReservationPage> EnumerateReservationsAsync(AuthContext caller, string vesselId, EnumerationQuery? query, CancellationToken token = default);
Task<BriefLintResult> LintBriefAsync(AuthContext caller, BriefLintRequest request, CancellationToken token = default);
Task<BlackBoxDoctorPage> GetDoctorAsync(AuthContext caller, string vesselId, int? windowDays, EnumerationQuery? query, CancellationToken token = default);
Task<BlackBoxAttemptDiff> DiffAsync(AuthContext caller, string entryId, string otherEntryId, CancellationToken token = default);
Task<EnumerationResult<BlackBoxNearbySuccess>> EnumerateNearbySuccessesAsync(AuthContext caller, string entryId, EnumerationQuery? query, CancellationToken token = default);
Task<BlackBoxTimelinePage> GetTimelineAsync(AuthContext caller, string entryId, EnumerationQuery? query, CancellationToken token = default);
Task<BlackBoxCascadeGraph> GetVoyageCascadeAsync(AuthContext caller, string voyageId, CancellationToken token = default);
Task<EnumerationResult<BlackBoxRecoveryStat>> EnumerateRecoveryStatsAsync(AuthContext caller, string? ruleId, string? vesselId, EnumerationQuery? query, CancellationToken token = default);
```

Surfaces (REST tag `BlackBox`, every MCP tool read-only and in `AskToolPolicy`'s read-only allowlist, authorization
`Authenticated`, resource `BlackBox`; client methods in `ArmadaClient.BlackBoxInsights.cs`):

| Feature | REST | MCP | Armada.Client |
|---|---|---|---|
| Overlap preview | `POST /api/v1/vessels/{id}/overlap-preview` | `preview_mission_overlap` | `PreviewMissionOverlapAsync` |
| Reservations | `GET /api/v1/vessels/{id}/reservations`, `POST .../reservations/enumerate` | `list_vessel_reservations` | `ListVesselReservationsAsync` |
| Brief lint | `POST /api/v1/missions/lint-brief` | `lint_mission_brief` | `LintMissionBriefAsync` |
| Configuration doctor | `GET /api/v1/vessels/{id}/blackbox-doctor`, `POST .../blackbox-doctor/enumerate` | `get_vessel_blackbox_doctor` | `GetVesselBlackBoxDoctorAsync` |
| Attempt diff | `GET /api/v1/blackbox/{id}/diff/{otherId}` | `diff_blackbox_entries` | `DiffBlackBoxEntriesAsync` |
| Nearby successes | `GET /api/v1/blackbox/{id}/nearby-successes`, `POST .../nearby-successes/enumerate` | `list_blackbox_nearby_successes` | `ListBlackBoxNearbySuccessesAsync` |
| Timeline | `GET /api/v1/blackbox/{id}/timeline`, `POST .../timeline/enumerate` | `get_blackbox_timeline` | `GetBlackBoxTimelineAsync` |
| Cascade graph | `GET /api/v1/voyages/{id}/blackbox/cascade` | `get_voyage_blackbox_cascade` | `GetVoyageBlackBoxCascadeAsync` |
| Recovery statistics | `GET /api/v1/blackbox/recovery-stats`, `POST .../recovery-stats/enumerate` | `get_blackbox_recovery_stats` | `GetBlackBoxRecoveryStatsAsync` |

#### Overlap warnings and chaining (13.1.1)

`MissionOverlapAnalyzer.LoadAsync(vessel, excludeMissionId)` builds a `VesselOverlapContext`: the vessel's tracked files
(`ListTrackedFilesAsync` on the Admiral's repository through `VesselGitInspector`, cached five minutes; null for a
vessel without a local repository), its active missions (`VesselGitInspector.ActiveMissionStatuses`, newest 200,
`ActiveMissionsTruncated`), each with a `MissionFileSet` of likely files (its scoped-files directive through
`MissionService.ParseMissionScopedFiles`, brief paths that exist in the tracked files, its dock's
`GitAnchorsSnapshot.RecentPathCommits`, and its `DiffSnapshot` or, for at most ten missions without one, its branch
from its start commit or merge base), the vessel's hotspot directories (`EnumerateHotspotsAsync`, Conflicted rows, top
1000) and the reservations they produce. `LikelyFiles(title, description, tracked)` gives a new mission's set (scoped
files plus verified brief paths; directories when a brief names one). `Compare(context, likely, dependsOn, missionId)`
is pure: an `ActiveMission` warning per active mission sharing files (paths compared case-insensitively, a bare scoped
file name matches anywhere, a directory overlaps the files under it), a `Reservation` warning per reserved directory
the mission touches, a `Hotspot` warning per hot directory it touches (entries at least the reservation threshold, or 3
when reservations are off); ordered ActiveMission, Reservation, Hotspot, larger overlap first; at most 50
(`WarningCount` has the total). `SuggestedDependsOnMissionId` is the overlapping active mission (ActiveMission or
Reservation, never a bare hotspot) with the most overlapping files, newest on a tie; none when a dependency is set.

`BlackBoxDispatchAdvisor` (`IMissionDispatchAdvisor`, `Armada.Core.Services.Interfaces`) runs just before each
dispatched mission is created: `AdmiralService.DispatchMissionAsync`, every mission of a standard voyage and the first
stage of each pipeline mission. It returns `MissionDispatchAdvice` (OverlapWarnings, BriefLintWarnings,
SerializedAfterMissionId). When `Vessel.SerializeOverlappingMissions` is true and the mission has no
`DependsOnMissionId`, it sets it to the suggestion and marks that warning `Serialized`; the mission then waits for the
overlapping one with the semantics of an explicit dependency (in the same voyage that includes the pipeline handoff
rules). An explicit dependency is never replaced. The Admiral copies the advice onto the created mission
(`Mission.OverlapWarnings`, `Mission.BriefLintWarnings`, each warning's `MissionId` set) and collects a voyage's onto
`Voyage.OverlapWarnings` and `Voyage.BriefLintWarnings` (at most 50 each). Both are transient (`JsonIgnore` when null,
never stored) and appear on REST `POST /api/v1/missions` and `POST /api/v1/voyages`, MCP `create_mission` and
`dispatch`, and any other caller of the Admiral's dispatch methods. An advisor that throws is logged and the dispatch continues without
advice. `SerializeOverlappingMissions` (migration 105, `vessels.serialize_overlapping_missions`, default false) is set
with `PUT /api/v1/vessels/{id}`, `add_vessel` and `update_vessel` (`serializeOverlappingMissions`).

#### Soft directory reservations (13.1.2)

An active mission reserves each directory of its scoped files whose hotspot row has at least
`blackBox.hotspotReservationThreshold` entries (default 3, 0 turns reservations off). `VesselReservationPage`
(`EnumerationResult<VesselReservation>` plus Threshold, HotDirectoryCount, ActiveMissionsScanned, ScanTruncated) lists
them by directory then mission id; a page is cut from the computed set with an exact total. A mission touching a
reserved directory gets a `Reservation` warning, and is chained after the reserving mission when the vessel serializes.

#### Brief linter (13.1.3)

`MissionBriefLinter.Lint(title, description, dependsOnMissionId, tracked, mode)` is pure. Rules: `BRIEF-DEP-001`
dependency language ("after mission 2 lands", "once X is merged", "depends on", "mission N", a `msn_` id) without
`DependsOnMissionId` (at most five findings); `BRIEF-NOOP-001` a verify, check or report brief with no change verb, or
a "do not change" brief (skipped for Audit and Research missions); `BRIEF-PATH-001` a multi-segment path whose first
segment the vessel does not have (only with tracked files); `BRIEF-PATH-002` a host path (`/Users/...`, `C:\...`,
`~/...`) or a parent-relative `../` path. Every finding has a severity (Warning), message, excerpt (200 characters) and
suggestion; at most 50. Dispatch runs the same lint; it never blocks.

#### Configuration doctor (13.1.4)

`GetDoctorAsync` reads the records of the caller's newest 200 entries on the vessel in the window (`windowDays` 1 to
365, default 30; outside is `ArgumentOutOfRangeException`, REST 400) and groups their configuration recommendations by
rule id and setting: `BlackBoxDoctorFinding` (Key, RuleId, RecommendationId and the current and suggested value and
rationale of the newest entry, Count, FirstSeenUtc, LastSeenUtc, ExampleEntryId, ExampleMissionId, Applicable).
Most frequent first, then newest. `BlackBoxDoctorPage` adds WindowDays, EntriesInWindow, EntriesScanned and
ScanTruncated.

#### Attempt diff (13.3.5)

`DiffAsync` compares two visible entries (the same id twice is `ArgumentException`): subject (captain id, name,
runtime, model, endpoint, persona, Harbor, branch), status before and after, site (phase, step, host, method, Armada
version and commit), classification (kind, suspected cause, fingerprint, triage), workspace (start commit, target tips,
branch tip, mission commit, commits behind), conflicted file total, and every effective configuration value. At most
100 `BlackBoxDiffField`s. Changed files come from each entry's ChangedByMission rows: only-in-each (200 each, with
counts) and in-both count; `ChangedFilesKnown` is false when either side has none.

#### Success flight recorder and nearby successes (13.3.6)

`BlackBoxSuccessRecorder.RunOnceAsync` reads the newest 200 `mission.completed` events (every landing path records
one) and `RecordAsync(mission)` writes a `BlackBoxSuccess` (`bbs_` id, migration 106 `blackbox_successes`) for a
Complete mission not yet recorded for that landing: vessel, mission, voyage, captain, title, base commit
(`StartCommit`), landed commit (`CommitHash`), target branch, changed files (diff snapshot, else start..landed commit on
the Admiral; 200 kept with the total), created, started and landed times and the queue, run and total milliseconds.
Records expire `blackBox.successRetentionDays` after landing (default 7, 0 to 90; 0 records nothing and the next
cleanup deletes every record). `CleanupAsync` deletes expired records. `BlackBoxNearbySuccessEnricher` (after the
built-in enrichers) compares a mission entry's files (its file rows except ChangedByConflictingMission, conflicted
files, out-of-scope files and dirty paths) with the successes on the vessel within
`blackBox.nearbySuccessWindowHours` (default 24, 1 to 168) of the capture (200 compared) and writes
`Related.NearbySuccesses` (ten, most overlap first, then closest in time) and `Related.NearbySuccessTotal`.
`EnumerateNearbySuccessesAsync` pages the same set (users see their own missions' successes).

#### Combined timeline (13.3.7)

`GetTimelineAsync` merges `BlackBoxTimelineItem`s (TimestampUtc, Source, Kind, Message, EntityId) from the entry
(captured, enriched, analyzed, resolved), its notes, the mission row (created, started, finished) and its events (or the
voyage and, for a voyage entry, its events), the dock row and its `dock` events, the merge entry row (queued, tests
started, finished) and its `merge_entry` events, the captain's events from the mission's creation to ten minutes after
the capture (only events without a mission or for this mission), and the Harbor link events in that window plus the
last one before it (`harbor_link_events`). An event appears once. Each source is capped at 200 and the timeline at
1000 (`Sources` counts with Truncated per source, `Truncated`). Ordered by time, source, entity id and kind in the
query's order; REST defaults to oldest first when no `order` is given, MCP takes `order`.

#### Voyage cascade graph (13.3.8)

`GetVoyageCascadeAsync` reads the voyage's first 200 missions (oldest first) and `BuildCascade` (pure) returns
`BlackBoxCascadeGraph`: nodes (`BlackBoxCascadeNode`: status, failure kind, current entry, dependency, IsRoot), the root
(the earliest Failed or LandingFailed mission with no failed ancestor) and its entry, the voyage's entry, and edges
(`BlackBoxCascadeEdge`): CancelledBecause (dependency and dependent both stopped), BlockedBy (dependency stopped,
dependent Pending or Blocked), DependsOn (otherwise), and Sibling from the root to each other stopped mission without
a dependency that is not downstream of the root. At most 1000 edges; `Truncated` when either cap applied.

#### Runbook linkage (13.4.9)

`BlackBoxRunbookLinker` (`IBlackBoxAnalysisHook`, run by the pipeline after the advisor) links each recovery path to
the best active runbook the entry's owner can see (200 read): a tag line `blackbox: <tags>` in the runbook's
description or overview naming the path id (strongest), the rule id, the failure step or the failure kind; then a
title containing the rule id, then a title containing the step name. The path gets `RunbookId` and `RunbookTitle`.
`BlackBoxService.RunRecoveryAsync` then plans `BlackBoxRunbookLinker.RunbookPlan`: one `start_runbook_execution` step
(runbookId, title, notes naming the entry and path, incidentId when the entry has one; REST
`POST /api/v1/runbooks/{id}/executions`) instead of the path's tool steps. `start_runbook_execution` is in
`BlackBoxActionCommands.SupportedTools` and is authorized like a direct call (tenant admin).

#### Recovery outcome tracking (13.4.10)

`BlackBoxRecoveryOutcomeTracker` (`IBlackBoxRecoveryObserver`, called after every confirmed run) writes a
`BlackBoxRecoveryOutcome` (`bro_` id, migration 106 `blackbox_recovery_outcomes`): Pending when every step succeeded,
RunFailed at once otherwise. `SettleAsync` (200 Pending per pass, oldest first) settles one when the mission has a newer
black box entry (Failed), is Complete since the run (Succeeded), or is Failed, LandingFailed or Cancelled since the run
(Failed); a voyage entry uses the voyage's status; a deleted subject or seven days without a terminal status is
Expired. Each settled outcome adds a System note to the entry. `BlackBoxPathStatsCache` holds rates for the 1000
most-run paths (succeeded over settled, at least three settled runs); the advisor (`BlackBoxAdvisor.PathStats`, and the
`pathStats` argument of the static `Analyze`) orders paths of equally weighted rules by rate (unknown counts as 0.5) and
records `SuccessRate` and `TrackedRuns` on each path; without rates the catalog order stands. Statistics
(`BlackBoxRecoveryStat`: Runs, Succeeded, Failed, Pending, Expired, SuccessRate, LastRunUtc per rule and path) are
grouped and paged in SQL, scoped to the caller's runs (a user's own, a tenant admin's tenant).

`BlackBoxInsightBackgroundService` runs every `blackBox.insightIntervalSeconds` (default 60, 5 to 3600) while
`blackBox.enabled`: success recording, success cleanup, outcome settling, a path statistics refresh, and once a day the
deletion of outcomes older than a year.

Database: `IBlackBoxSuccessMethods` (`DatabaseDriver.BlackBoxSuccesses`: CreateAsync, ReadAsync, ReadLatestByMissionAsync,
EnumerateByVesselAsync (window, tenant and owner scope, newest first), CountAsync, DeleteExpiredAsync) and
`IBlackBoxRecoveryOutcomeMethods` (`DatabaseDriver.BlackBoxRecoveryOutcomes`: CreateAsync, ReadAsync,
EnumerateByEntryAsync, EnumeratePendingAsync, SettleAsync (Pending only), EnumerateStatsAsync, DeleteByEntryAsync,
DeleteCreatedBeforeAsync), shared SQL in `BlackBoxSuccessMethodsBase` and `BlackBoxRecoveryOutcomeMethodsBase`.

Tests: `Services.BlackBoxPrevention`, `Services.BlackBoxForensicsExtensions`, `Services.BlackBoxOperations`,
`Database.BlackBoxInsights` (all four providers), `E2E.BlackBoxInsightsApi`, `Client.Contract.BlackBoxInsights`, and the
new tools in `E2E.McpTenantIsolation`.

### 13.22 Ask Armada

Plan section 10. The pieces, in `Armada.Core.Services.Ask` unless noted:

- **Policy** (`AskToolPolicy`): every BlackBox tool the MCP authorization registry declares as a Read (the 13 of
  `McpBlackBoxTools.ReadTools` plus `export_blackbox_fixture`) runs without approval; every other black box tool
  (recovery, recommendation, resolve, annotate, pin, delete, purge, gc, reanalyze) becomes a proposal. The
  admin-level reads stay allowlisted because the gate wraps the registry check: a thread call runs as the thread
  owner, so `get_blackbox_retention` (TenantAdmin) and `get_blackbox_storage` / `export_blackbox_fixture` (AdminOnly)
  are refused with Forbidden for an owner without the role (`E2E.AskThreads` admin_reads_keep_caller_authorization).
- **Prompt** (`AskBlackBoxGuidance.Section`): the "## Black box" section of the built-in `ask.system` template (get
  the summary first, read only the needed sections, page evidence, answer with What happened / Why / My opinion /
  What could have been done differently / Next steps, never invent files, commits or missions, say when facts are
  missing, offer FailureReason when there is no entry, offer to save the opinion as a note). An untouched earlier
  built-in is upgraded at startup; `AskTurnCoordinator.BuildPrompt` appends the section to a customized template that
  lacks the heading, so operator edits keep working.
- **Quick action** `/blackbox` (`AskQuickActionCatalog`): `missionId` (type `mission`, required) and `question` (text,
  optional) mapping to `get_blackbox`; `question` is removed before the tool runs (the client sends it to the captain).
- **Card**: tracked entity `BlackBox` (`AskTrackedEntityTypeEnum`). `AskWorkSnapshotBuilder.BlackBox` (the
  `IBlackBoxService`) reads the entry as the thread's owner into `AskWorkSnapshot.BlackBox` (`AskBlackBoxCard`: the
  digest, conflicting missions, the first conflicted files, recommendations, note count and the follow-ups from
  `AskBlackBoxGuidance.FollowUps`: Fix it and Apply as proposals, Draft an Armada fix mission and Compare with
  similar failures as messages). The card's state is Succeeded for Resolved and ArmadaFixed, otherwise Failed.
- **Linking** (`AskWorkLinker`, `Armada.Server.Ask.AskActionService`): `get_blackbox` links the card (as a quick action,
  an approved proposal, or a thread read); the write tools refresh it; `LinkMentionedBlackBoxesAsync` links the cards
  of mentioned missions (`AskTurnCoordinator.OnConversationText`).
- **Approval**: an approved `run_blackbox_recovery` or `apply_blackbox_recommendation` proposal runs the dry run, then
  confirms it with its token (`DryRunThenConfirmAsync`); `McpBlackBoxTools.CallerAuthorKind` attributes Ask notes to
  AskArmada.
- **Tests**: `Services.AskBlackBox` (plan 16.8), `Services.PromptTemplateService`, `Tui.Ask`, dashboard
  `AskBlackBoxCard.test.tsx`, `askBlackBox.test.ts` and `AskArmada.test.tsx`; the prompt evaluation script has a
  `blackbox` case whose grounding check flags files, commits and missions that are not in the entry.

## 14. Open items

None. The TUI and Helm views and commands for the section 12 extensions are in section 12.
