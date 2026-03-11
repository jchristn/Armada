# EASE OF USE - Reducing Time-to-Value for Armada Users

> **Goal**: Minimize the steps, concepts, and cognitive load required for a user
> to go from `git clone` to dispatching their first mission.
>
> **Current state**: 1 command to first mission (`armada go "Fix the bug"`).
> **Previous state**: 4 commands (config init, fleet add, vessel add, go).

---

## Status Legend

- `[ ]` Not started
- `[~]` In progress
- `[x]` Complete
- `[!]` Blocked (note reason inline)

Annotate with initials and dates, e.g. `[x] JC 2026-03-10`

---

## Table of Contents

1. [Zero-Config First Run](#1-zero-config-first-run)
2. [Reduce Mandatory Concepts](#2-reduce-mandatory-concepts)
3. [Smart Defaults and Inference](#3-smart-defaults-and-inference)
4. [Streamlined CLI UX](#4-streamlined-cli-ux)
5. [Visibility Without Effort](#5-visibility-without-effort)
6. [Error Recovery UX](#6-error-recovery-ux)
7. [Onboarding and Guidance](#7-onboarding-and-guidance)

---

## 1. Zero-Config First Run

**Problem**: User must run `config init` before anything works. They must answer
questions about ports, directories, and API keys before they've even seen what
Armada does. This front-loads decisions onto someone who has zero context.

### 1.1 Auto-initialize on first use

- [x] If `~/.armada/settings.json` doesn't exist when any command runs, create it
      silently with all defaults. No interactive prompts. No `config init` required.
      Implemented in `BaseCommand.AutoInitializeIfNeeded()`.
- [x] Print a one-line notice: `Initialized Armada config at ~/.armada/settings.json`
- [x] `config init` remains available for users who want to customize, but it
      becomes optional, not a prerequisite.

### 1.2 Defer configuration until it matters

- [x] Don't ask about API keys until the user runs a command that needs one
      (e.g., remote API access). Local CLI-to-embedded-server needs no key.
      API key is null by default and only checked on remote access.
- [x] Don't ask about ports until the user explicitly runs `armada server start`
      for standalone mode. Embedded server picks defaults silently.
- [ ] Don't ask about PR creation until a mission completes. Prompt once:
      "Mission complete. Create a PR? (y/n, remember my choice)"

### 1.3 Auto-detect agent runtimes

- [x] On first run, scan PATH for `claude`, `codex`, and other known agent CLIs.
      Implemented in `RuntimeDetectionService.DetectDefaultRuntime()`.
- [x] Auto-configure the first one found as the default runtime.
      Used during auto-captain creation in `BaseCommand.EnsureCaptainsAsync()`.
- [x] If none found, print a clear message with install instructions.
- [x] `DefaultRuntime` setting stores the choice; auto-detection is fallback.

---

## 2. Reduce Mandatory Concepts

**Problem**: User must understand Fleet, Vessel, Captain, Mission, Voyage, Dock,
Signal, and Runtime before dispatching work. That's 8 domain concepts. The user
cares about one thing: "do this task on this repo."

### 2.1 Implicit default fleet

- [x] Create a `default` fleet automatically on first run.
      Implemented in `BaseCommand.EnsureDefaultFleetAsync()`.
- [x] All vessels are added to the default fleet unless `--fleet` is specified.
- [x] `armada fleet add` becomes a power-user command for organizing many repos.
- [x] Remove fleet as a required step in getting started docs.

### 2.2 Implicit captain pool

- [x] When a mission is dispatched and no captains exist, auto-create one using
      the detected default runtime (from 1.3).
      Implemented in `BaseCommand.EnsureCaptainsAsync()`.
- [x] Name auto-created captains sequentially: `captain-1`, `captain-2`, etc.
- [x] Respect `MaxCaptains` setting as the upper bound for auto-creation.
- [x] `armada captain add` becomes a power-user command for explicit control.

### 2.3 Inline vessel registration in `go` command

- [x] `armada go "Fix the bug" --repo https://github.com/you/project` should:
  1. Check if a vessel exists for that repo URL. If yes, use it.
  2. If not, auto-register it (name inferred from URL via `GitInference.InferVesselName()`).
  3. Clone the bare repo, provision worktree, dispatch mission.
- [x] Support `--repo .` or `--repo /path/to/local/repo` for local repositories.
  Detect the remote URL from `git remote get-url origin` and register from that.
- [x] If only one vessel is registered, `--repo` / `--vessel` becomes optional.
  Default to the only known vessel.

### 2.4 Reduce concept vocabulary in casual usage

- [x] In CLI output and help text, use plain language alongside domain terms:
  - "tasks" alongside "mission" in help descriptions
  - "repositories" alongside "vessel"
  - "agents" alongside "captain"
  - "batches of missions" alongside "voyage"
      Updated in `Program.cs` command descriptions and `GETTING_STARTED.md` concept table.
- [x] Keep domain terms in API, code, and advanced docs. Use plain language in
      CLI-facing text where the user is thinking in their own terms.

---

## 3. Smart Defaults and Inference

**Problem**: The user has to make decisions that the system could make for them.
Every decision is friction. Reduce decisions to only those the system genuinely
cannot infer.

### 3.1 Infer vessel from current directory

- [x] If `armada go "Fix the bug"` is run with no `--vessel` or `--repo`:
  1. Check if CWD is inside a git repository via `GitInference.IsGitRepository()`.
  2. Get the remote URL from `GitInference.GetRemoteUrl()`.
  3. Match against registered vessels via `EntityResolver.ResolveVesselByRemoteUrl()`, or auto-register.
- [x] This makes the most common case (`cd my-project && armada go "do X"`)
      require zero flags.

### 3.2 Infer branch from vessel

- [x] Auto-detect default branch (`main`, `master`, `develop`) from the remote
      via `GitInference.GetDefaultBranch()`.
- [x] Fall back to `main` if detection fails.

### 3.3 Auto-scale captain pool

- [x] When a voyage is dispatched with N missions and fewer than N idle captains:
  auto-create captains up to `MaxCaptains` to fill demand.
      Implemented in `GoCommand.AutoScaleCaptainsAsync()`.
- [x] `IdleCaptainTimeoutSeconds` setting added for auto-removal (default: 0 = disabled).
      Server-side enforcement is a future task.
- [x] Default `MinIdleCaptains=0`, `MaxCaptains=5` — sensible out of the box.

### 3.4 Smart mission decomposition

- [ ] Add an optional `--decompose` flag to `armada go` that sends the prompt
      to the configured LLM to break it into sub-missions before dispatching.
- [ ] Example: `armada go "Add authentication to the API" --decompose` creates
      a voyage with missions for middleware, login endpoint, token validation, etc.
- [ ] Later: make `--decompose` the default for prompts that look complex
      (multiple verbs, "and" conjunctions, long descriptions).

---

## 4. Streamlined CLI UX

**Problem**: The CLI requires the user to remember command hierarchies and IDs.
The user is context-switching between their code and Armada commands. Minimize
the mental overhead of interacting with the CLI.

### 4.1 The one-command happy path

- [x] Make this work end-to-end with zero prior setup:
      ```bash
      armada go "Add input validation to the signup form"
      ```
      Behind the scenes: auto-init config, detect runtime, auto-register vessel
      from CWD, auto-create captain, dispatch mission. All in `GoCommand`.
- [x] Print a concise confirmation with voyage ID, mission count, and next step.

### 4.2 Name-based lookups everywhere

- [x] All commands that accept IDs also accept names via `EntityResolver`:
  - `armada mission show fix-login-bug` (match by title substring)
  - `armada captain stop claude-1` (match by name)
  - `armada vessel remove my-project` (match by name)
  - `armada voyage show "API Hardening"` (match by title)
  - `armada fleet remove staging` (match by name)
- [x] If a name matches multiple entities, returns null (ambiguous). Commands
      list available entities to help user clarify.

### 4.3 Contextual `armada status`

- [x] When run inside a git repo that matches a registered vessel, `armada status`
      shows vessel name and focuses on that context.
- [x] Show global status with `armada status --all`.
- [x] Default view answers: "what's happening in the project I'm looking at?"

### 4.4 Combined output view

- [x] `armada watch` supports `--captain claude-1` for single-agent focus
      (filters signals by captain name).
- [ ] Full tmux-like split panes with scrolling log feed from all active captains.
      Deferred: requires significant Spectre.Console Layout work.

### 4.5 Completion and suggestions

- [x] After commands, suggest the logical next step:
  - After `vessel add`: "Dispatch work with `armada go ...`"
  - After `go`: "Run `armada watch` to monitor progress."
  - After `captain add`: "Dispatch work with `armada go ...`"
  - After `mission create`: "Run `armada watch` to monitor progress."
  - After `voyage create`: "Run `armada watch` to monitor progress."
  - After failed mission show: "Retry with `armada mission retry <id>`"
  - After voyage show with failures: "Retry with `armada voyage retry <id>`"
  - On empty status: "Get started: `armada go ...`"
- [x] Keep suggestions to one line. Don't lecture.

---

## 5. Visibility Without Effort

**Problem**: Once work is dispatched, the user has to actively poll for status.
The system should surface progress without the user asking.

### 5.1 Desktop notifications

- [x] When a mission completes or fails during `armada watch`, send an OS notification:
  - macOS: `osascript -e 'display notification'`
  - Linux: `notify-send`
  - Windows: PowerShell toast notification
      Implemented in `NotificationService.Send()`.
- [x] Configurable: `armada config set Notifications true`
- [x] Default: on.

### 5.2 Terminal bell on completion

- [x] When `armada watch` detects a mission completion or failure, ring the
      terminal bell (`\a`) via `NotificationService.Bell()`.
- [x] Configurable: `armada config set TerminalBell true` (default: true).

### 5.3 Summary on reconnect

- [ ] When the user runs `armada status` after being away, show a "since you
      last checked" summary:
  - Missions completed: N
  - Missions failed: M (with one-line reasons)
  - PRs created: K
  - Captains recovered: J
- [ ] Track "last seen" timestamp per CLI session.

---

## 6. Error Recovery UX

**Problem**: When things go wrong, the user has to diagnose system internals.
Errors should be self-explanatory and suggest fixes.

### 6.1 Actionable error messages

- [x] Every error message includes what to do next. Examples:
  - "No vessels registered. Register one with `armada vessel add ...` or use `--repo`."
  - "Vessel not found: <name>. Available vessels: ..."
  - "Captain not found: <name>. Available captains: ..."
  - "Mission not found: <id>. List missions with `armada mission list`."
  - "No agent runtimes found. Install Claude Code: `npm install -g ...`"
  - "Not a git repository: <path>"
  - "Unable to retrieve status. Is the Admiral running? Try `armada server start`."
      Implemented across all command files.

### 6.2 `armada doctor`

- [x] New command that checks system health and reports issues with fixes:
  - Is settings.json valid?
  - Are agent runtimes on PATH?
  - Is the database accessible?
  - Is the Admiral server running?
  - Are any captains stalled?
  - Is git available?
  - Are there failed missions that could be retried?
      Implemented in `DoctorCommand`.
- [x] Output a checklist with PASS/FAIL/WARN and remediation commands.

### 6.3 One-command retry

- [x] `armada mission retry <id-or-name>` to re-dispatch a failed mission.
      Creates a new mission with the same title/description/vessel/voyage.
      Implemented in `MissionRetryCommand`.
- [x] `armada voyage retry <id-or-name>` to retry all failed missions in a voyage.
      Implemented in `VoyageRetryCommand`.

---

## 7. Onboarding and Guidance

**Problem**: GETTING_STARTED.md was 466 lines. The user needs a 30-second path
to value, not a manual.

### 7.1 Rewrite GETTING_STARTED.md

- [x] Restructured into three sections:
  1. **30-Second Start** (5 lines): `cd your-project && armada go "..."`, done.
  2. **5-Minute Guide** (~40 lines): multi-task, voyages, monitoring, multiple repos.
  3. **Full Reference** (rest): how it works, concepts, config, API, CLI reference.
- [x] Calculator example condensed to 5 lines inline (no separate examples/ directory needed).

### 7.2 In-CLI onboarding

- [x] On very first run (no settings file exists), print welcome message with
      the one command that works: `armada go "your task description"`.
      Implemented in `Program.Main()`.
- [x] No interactive wizard. No prompts. Just tell them the one command.

### 7.3 `armada help` improvements

- [x] Commands grouped by frequency of use in `Program.cs`:
  - **Common** (top-level): go, status, watch, log, doctor
  - **Entity management**: mission, voyage, vessel, captain, fleet
  - **Infrastructure**: server, config, mcp
- [x] Each command's `--help` includes realistic examples via `.WithExample()`.

---

## Implementation Priority

Ordered by impact-to-effort ratio. Status after implementation:

| Priority | Item | Section | Status |
|----------|------|---------|--------|
| P0 | Auto-init config on first use | 1.1 | [x] Complete |
| P0 | Implicit default fleet | 2.1 | [x] Complete |
| P0 | Implicit captain pool | 2.2 | [x] Complete |
| P0 | Infer vessel from CWD | 3.1 | [x] Complete |
| P0 | One-command happy path | 4.1 | [x] Complete |
| P1 | Inline vessel registration | 2.3 | [x] Complete |
| P1 | Auto-detect runtimes | 1.3 | [x] Complete |
| P1 | Name-based lookups | 4.2 | [x] Complete |
| P1 | Actionable error messages | 6.1 | [x] Complete |
| P1 | Rewrite GETTING_STARTED.md | 7.1 | [x] Complete |
| P2 | Contextual status | 4.3 | [x] Complete |
| P2 | `armada doctor` | 6.2 | [x] Complete |
| P2 | Combined output view | 4.4 | [~] Captain filter added; full split-pane deferred |
| P2 | Mission retry | 6.3 | [x] Complete |
| P2 | Desktop notifications | 5.1 | [x] Complete |
| P3 | Smart decomposition | 3.4 | [ ] Not started (requires LLM integration) |
| P3 | Defer configuration | 1.2 | [x] Complete (API key and ports already deferred) |
| P3 | Auto-scale captains | 3.3 | [x] Complete |
| P3 | Plain language vocabulary | 2.4 | [x] Complete |
| P3 | Summary on reconnect | 5.3 | [ ] Not started (needs last-seen tracking) |
| P3 | In-CLI onboarding | 7.2 | [x] Complete |
| P3 | Help improvements | 7.3 | [x] Complete |
| P3 | Terminal bell | 5.2 | [x] Complete |
| P3 | Completion suggestions | 4.5 | [x] Complete |

**21 of 24 items complete. 1 partially complete. 2 deferred (require deeper infrastructure).**

---

## Success Metrics

The new user journey:

```bash
cd my-project
armada go "Add input validation to the signup form"
```

**One command. Zero setup. Zero concepts to learn upfront.**

The system auto-initializes config, detects the runtime, infers the repo from
CWD, creates a default fleet, auto-provisions a captain, and dispatches the
mission. The user learns Fleet, Voyage, Captain, and Dock when they need them --
not before.
