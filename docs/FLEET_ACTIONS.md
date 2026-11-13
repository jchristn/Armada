# Fleet Actions

A fleet action is one piece of work you want done to many repositories at once. Sometimes that work is a shell command: pull the default branch everywhere, prune the branches that already merged, run every build. Sometimes it is a job for a captain: update the stale packages in each repository and land the result. Armada handles both through the same model, so you pick the vessels, pick the action, and get back a run that records exactly what happened to each vessel.

The feature replaces the "custom action" button in CodeHub, which typed a prompt into a fresh terminal window per repository and then forgot about it. Armada keeps the output, enforces timeouts and concurrency, lets you cancel, and remembers every run until the retention window expires.

## Actions, runs, and targets

An **action** is a saved, named definition. It has a kind, a body, and a few execution defaults. You can run an action as often as you like, and editing it later never changes the record of runs that already happened, because every run stores a snapshot of the definition it started from.

A **run** is one invocation of an action over a set of vessels. It carries its own status, its own concurrency, and counts of how many targets succeeded, failed, were skipped, or were cancelled. You can also start an ad hoc run with an inline definition when a one-off command does not deserve a saved action; such a run simply has no action ID.

A **target** is one vessel inside a run. Each target has its own status, a stable reason code when it was skipped or failed, timestamps, a duration, and either captured process output (Command actions) or the ID of the voyage it dispatched (Mission actions).

### Command actions

A Command action runs its command text in each vessel's working directory. That is deliberate: the point of "fast-forward everything" is to update the checkouts you actually work in, so Armada does not create a throwaway worktree for these runs. A vessel with no working directory configured (or whose working directory does not exist on the Admiral host) is skipped with reason `NoWorkingDirectory`. The safety net is the clean-tree check. When the action requires a clean working tree (the default for Command actions), Armada runs `git status --porcelain` first and skips the vessel with reason `DirtyTree` if anything is modified or untracked. Your uncommitted work is never touched by a fleet action you forgot was running.

Armada hands the command to the platform shell on standard input rather than as a quoted argument string. On Linux and macOS that is `/bin/sh -s`. On Windows it is `powershell -NoProfile -NonInteractive -ExecutionPolicy Bypass -Command -`. Feeding the shell through standard input sidesteps every quoting problem and leaves nothing on disk. PowerShell has two quirks worth knowing in this mode: it reads the script line by line, so a block statement that spans lines needs a blank line after it, and the exit code only reflects whether the last statement succeeded. If you want a native tool's exit code to decide success, end the command with `exit $LASTEXITCODE`.

When a vessel has a preferred Harbor, its checkout lives on that Harbor's machine, so the command runs there. The Harbor link does not carry standard input, so a Harbor-routed command is passed as a single argument instead (`/bin/sh -c` on Linux and macOS, `powershell ... -Command` on a Windows Harbor). If the preferred Harbor is not connected, the target is skipped with `HarborUnavailable` rather than run against the wrong machine. One limitation applies to Harbor targets: cancelling a run stops waiting for a Harbor command, but the Harbor protocol has no way to kill a process it already started, so a long command on a Harbor finishes on its own.

Each target records its exit code, standard output, and standard error. Output is capped at `FleetActions.MaxOutputBytes` per stream, keeping the **end** of the stream, because that is where build and test failures land. When anything was dropped, `OutputTruncated` is set. A command that runs past the action's timeout is killed, together with any child processes, and the target becomes `TimedOut`.

### Mission actions

A Mission action dispatches one voyage per vessel, with the rendered prompt as the single mission's description, through the same path `POST /api/v1/voyages` uses. The target stays `Running` while the voyage is active, becomes `Succeeded` when the voyage completes, and becomes `Failed` with reason `VoyageFailed` when a mission fails or its landing fails, and becomes `Cancelled` when the voyage is cancelled. Dispatch validation runs first. When it rejects a vessel (for example because the pipeline cannot be found), the target is skipped with `DispatchRejected` and the validator's message lands in `ErrorText`.

Concurrency means something different here. A captain voyage can take an hour, so a Mission run's concurrency is a pacing limit: at most that many voyages from the run are active at once. A sixty-vessel dependency sweep at concurrency 4 trickles work out four voyages at a time instead of flooding every captain and starving everything else. The Admiral's health-check loop advances Mission runs on every cycle, updating finished voyages and dispatching the next pending vessels, so progress moves at the heartbeat interval.

The `Persona` field (Mission actions) is applied at dispatch. Without a pipeline, each vessel's mission runs as a single stage with that persona; the vessel and fleet default pipelines are skipped, because naming a persona is an explicit choice. With a single-stage pipeline, the persona replaces the stage's persona and the stage's review settings still apply. A multi-stage pipeline decides its own personas, and `Persona` is ignored. Use a pipeline when you need a particular persona sequence.

## Templates

Command text, prompt templates, and applicability checks are rendered per vessel from a small, fixed set of variables. There is no expression language and no code execution, just substitution.

| Variable | Value |
|---|---|
| `{{vessel.name}}` | The vessel's name |
| `{{vessel.id}}` | The vessel ID (`vsl_` prefix) |
| `{{vessel.defaultBranch}}` | The vessel's default branch |
| `{{vessel.workingDirectory}}` | The vessel's working directory, or empty |
| `{{vessel.ecosystems}}` | The ecosystems detected in the working directory (for example `DotNet, Node`) |
| `{{vessel.buildCommand}}` | The vessel's definition-of-done build command, else the repository's build script, else the default for its detected ecosystem |
| `{{vessel.testCommand}}` | The vessel's test command override, else the repository's test script, else the ecosystem default |
| `{{vessel.lintCommand}}` | The lint command, resolved the same way |
| `{{vessel.auditCommand}}` | The dependency audit command, resolved the same way, with osv-scanner as the universal fallback |
| `{{vessel.outdatedCommand}}` | The package manager's outdated report (for example `dotnet list package --outdated`, `npm outdated`) |
| `{{vessel.licensesCommand}}` | The ecosystem's license report, where one exists (Maven, Composer, containers) |
| `{{health.summary}}` | Plain-text list of the vessel's failing, warning, and unknown health findings, followed by its outdated or vulnerable packages |
| `{{scan.summary}}` | Mission actions with a `ScanSourceActionKey`: the vessel's latest result from that scan action (see [Scans and fixes](#scans-and-fixes)) |
| `{{input.<name>}}` | The run's value for a declared input (see [Inputs](#inputs)) |

Names are case-insensitive and may have spaces inside the braces. Anything else between double braces is rejected when you save the action (HTTP 400, with the unknown name in the message), which also means a GitHub Actions style `${{ ... }}` cannot appear in a command. Substitution is a single pass: if a vessel's name happened to contain `{{vessel.id}}`, it would be inserted literally and never expanded again.

Values are inserted verbatim, without shell escaping. Vessel names and branches are set by tenant administrators, which is the same group allowed to run commands, but keep it in mind if you build commands around those values. Use the `package`, `semver`, or `enum` input types for values that end up in a shell command: they cannot contain shell metacharacters.

The ecosystem commands are resolved from the vessel's checkout on the Admiral host. A target whose template references `{{vessel.buildCommand}}` is skipped with `NoBuildCommand` when nothing resolves. A Command target whose template references one of the other ecosystem commands that resolves to nothing is skipped with `NoMatchingEcosystem`, with the detected ecosystems in `ErrorText`. `{{health.summary}}` reads whatever the Vessel Health feature last stored; when nothing has been collected yet, it renders "No health data has been collected for this vessel yet." so the prompt still reads sensibly.

## Inputs

An action can declare typed inputs that the person running it fills in, for example the package and version for "Update a specific dependency". Each input has a name (letters, digits, and underscores), a type, whether it is required, an optional default, and help text. Templates reference it as `{{input.<name>}}`.

| Type | Accepts |
|---|---|
| `string` | Any text. Fine in prompts; do not put it in a shell command |
| `semver` | A semantic version such as `1.2.3` |
| `enum` | One of the declared `AllowedValues` |
| `package` | A package identifier: letters, digits, and `@ / . _ + : -`, up to 200 characters, starting with a letter, digit, or `@` (for example `Newtonsoft.Json`, `@scope/name`, `com.example:artifact`, `github.com/org/repo`). Safe to use in shell commands |

Values are validated before the run is written; a missing required input or a value of the wrong type rejects the request with HTTP 400. The run stores the resolved values, so its history shows what it ran with.

## Applicability checks

An action can carry an applicability check: a shell command (`ApplicabilityCommand`, with an optional PowerShell `ApplicabilityCommandWindows`) rendered with the same variables and run in each vessel's working directory before the target executes or dispatches. When the check exits non-zero, the target is skipped with reason `NotApplicable` and no command runs and no captain is dispatched. "Update a specific dependency" uses one to look for the package in the vessel's tracked manifest files, so a run over sixty vessels sends captains only to the ten that actually use the package.

For Mission actions the check is best effort: a vessel without a working directory, or whose checkout is on a Harbor that is not connected, is dispatched anyway, and the prompt still tells the captain what to do when the change does not apply. Because the check runs a shell command, creating, editing, or running an action that has one requires tenant admin, the same as a Command action.

## Scans and fixes

Several built-ins come in pairs: a Command action that scans (Run tests, Lint, Vulnerability audit, Outdated dependencies report) and a Mission action that fixes what the scan found. The fix action names its scan in `ScanSourceActionKey` (a built-in key or an action ID), and its prompt cites `{{scan.summary}}`: the vessel's most recent result from a finished run of that scan.

A scan with an output parser (for example `npm-audit-json`, `gitleaks-json`, `dotnet-test-trx`) turns the output into a findings summary, and a target with at least one parsed finding becomes `Findings` instead of `Succeeded` or `Failed`. The `auto` parser picks the parser that matches the vessel's resolved audit command, which is what "Vulnerability audit" uses. A scan without a parser records its exit code and the tail of its output as the summary, so a failing test run gives "Fix failing tests" the failures to start from.

### Follow-up runs

To act only where a scan found something, start the fix as a follow-up run: pass `FromRunId` (the scan run's ID) instead of `VesselIds`. The new run targets the vessels of that run whose target status is in `FromTargetStatuses`, which defaults to `Findings` and `Failed`. Setting both `FromRunId` and `VesselIds`, or naming statuses that match no target, rejects the request with HTTP 400. The dashboard run page and the TUI run screen offer this as **Follow up**, listing the paired fix action first.

## Report-only actions

A Mission action's `MissionMode` is `Implementation` by default. Set it to `Audit` or `Research` for an action that should produce a report rather than a change. Each mission then runs read-only (the captain is told not to modify the repository, and an empty diff counts as success), on the Worker-only pipeline unless the action names a pipeline, with landing mode `None`. When the voyage finishes, the captain's final message is stored on the target's `OutputText`, kept from the start and cut at `FleetActions.MaxOutputBytes`. The dashboard and TUI show a run's reports together as one report you can read or download.

Every Mission target records the captain's final message the same way, so an implementation run also shows what each captain said it did.

## Built-in actions

Each tenant starts with these actions so the feature is useful on day one. They are ordinary rows: edit them, delete them, or copy them as a starting point. Deleting a built-in hides it permanently, because Armada remembers the built-in key and never seeds it again. New built-ins are added to existing tenants when the Admiral starts. When a built-in's shipped definition changes in a release, a tenant's copy is upgraded only if nobody edited it; an edited copy keeps its edits.

| Action | Key | Kind | What it does |
|---|---|---|---|
| Fast-forward default branch | `ff-default-branch` | Command | `git pull --ff-only`, skipping dirty trees |
| Prune merged branches | `prune-merged-branches` | Command | `git fetch --prune`, then `git branch -d` for each local branch merged into the default branch; never the current branch, never the default branch |
| Repository status report | `repo-status` | Command | Read-only snapshot: branch, last commit, ahead/behind as of the last fetch, uncommitted changes, local and merged branches, stashes |
| Update a specific dependency | `update-package` | Mission | Inputs `package`, `version`, `notes`. Applicability check skips vessels whose manifests do not mention the package; the captain updates every reference, regenerates lock files, and adapts the code |
| Upgrade runtime or framework | `upgrade-runtime` | Mission | Input `target` (for example `net10.0`, Node 22); updates project files, CI, and images and fixes the breakage |
| Standardize repository files | `standardize-files` | Mission | Inputs `files` and `standard`; adds or aligns shared files without touching code |
| Sync docs with code | `sync-docs` | Mission | Brings the README, build instructions, and API docs in line with the code; docs only |
| Build | `build` | Command | Runs `{{vessel.buildCommand}}`; vessels where nothing resolves are skipped |
| Run tests | `run-tests` | Command | Runs `{{vessel.testCommand}}`; scan for Fix failing tests |
| Fix failing tests | `fix-failing-tests` | Mission | Fixes the failures in the latest Run tests result, code or test, whichever is wrong |
| Add a test project | `add-test-project` | Mission | Adds a conventional test project to repositories that have none; makes no change where tests exist |
| Improve test coverage | `improve-coverage` | Mission | Inputs `area` and `focus`; adds meaningful tests without changing production code |
| Lint | `lint` | Command | Runs `{{vessel.lintCommand}}`; scan for Fix lint findings |
| Fix lint findings | `fix-lint` | Mission | Mechanical fixes for the latest Lint result |
| Code cleanliness review | `cleanliness-review` | Mission, report-only | Dead code, duplication, complexity hotspots, inconsistent naming and error handling |
| Remove dead code and unused dependencies | `remove-dead-code` | Mission | Removes what is provably unused; build and tests must pass |
| Performance review | `performance-review` | Mission, report-only | Input `focus`; likely performance problems ranked by impact with evidence |
| Apply performance fixes | `apply-performance-fixes` | Mission | Input `findings` (the approved items from a review); behavior stays identical |
| Vulnerability audit | `vulnerability-audit` | Command | Runs `{{vessel.auditCommand}}` with the `auto` parser; advisories become findings |
| Fix vulnerable dependencies | `fix-vulnerabilities` | Mission | Upgrades each package in the latest audit to the smallest version that fixes it |
| Outdated dependencies report | `outdated-report` | Command | Runs `{{vessel.outdatedCommand}}`; scan for Update outdated dependencies |
| Update outdated dependencies | `update-dependencies` | Mission | Input `policy` (patch, minor, major; default minor); cites the latest outdated report and health findings |
| Secrets scan | `secrets-scan` | Command | gitleaks with redacted output; leaks become findings. No automatic fix: history rewriting and rotation are human decisions |
| Security review | `security-review` | Mission, report-only | Injection, authorization gaps, SSRF, unsafe deserialization, weak crypto, secrets, CORS, missing validation; ranked by severity with exploit path |
| License compliance | `license-compliance` | Command | Input `policy` (strong-copyleft, copyleft, report-only); fails the vessel when a disallowed license appears in the license report |

The shell bodies are chosen for the Admiral's platform when the tenant is seeded: a POSIX script on Linux and macOS, a PowerShell one-liner on Windows. Prune uses `git branch -d`, which refuses to delete anything git does not consider merged, and it falls back to the local default branch when the repository has no `origin`. Secrets scan and License compliance need `gitleaks` and the ecosystem's license tool on the host; a missing tool skips the vessel with `ToolMissing:<tool>`.

Two loops cover most use. For a dependency that just shipped a release, run "Update a specific dependency" over the whole fleet with the package and version; only the vessels that use it get a captain. For hygiene, run a scan (Run tests, Vulnerability audit, Lint) across the fleet, then use **Follow up** on that run to start the paired fix on just the vessels with findings or failures. The report-only reviews are for deciding what to do next: read the combined report, then paste the items you approve into "Apply performance fixes" or a dispatch of your own.

## Status, cancellation, and restarts

A run's status rolls up from its targets. It is `Pending` until the first target starts and `Running` while any target is pending or running. Once every target is resolved it becomes `Completed`, or `CompletedWithFailures` when at least one target failed or timed out. Skipped targets do not count as failures, since a skip is Armada declining to act, not something going wrong. A run is `Failed` only when the runner itself hit an unexpected error.

Cancelling a run sets the run to `Cancelled`, marks every pending target `Cancelled`, kills running commands (their targets also become `Cancelled`), and for Mission runs cancels each voyage that has not finished yet using the normal voyage cancel, which cancels pending and assigned missions and lets a mission already in progress finish. Cancelling a finished run returns HTTP 409.

The runner survives an Admiral restart. On startup, any Command target still marked `Running` is failed with reason `Interrupted`, since its process died with the old Admiral, and pending targets resume. Mission runs simply keep syncing, because their state lives in the voyages.

## Concurrency and settings

Two limits apply to Command runs. The run's own concurrency caps how many of its targets execute at once, and `FleetActions.MaxConcurrency` caps how many Command targets execute at once across every run on the Admiral. The tighter limit wins. Mission runs are paced by their own concurrency only.

| Setting | Default | Range | Effect |
|---|---|---|---|
| `FleetActions.MaxConcurrency` | 8 | 1-32 | Global cap on Command targets executing at once |
| `FleetActions.DefaultTimeoutSeconds` | 300 | 5-7200 | Timeout for new actions that do not specify one |
| `FleetActions.MaxOutputBytes` | 65536 | 1024-1048576 | Bytes kept per output stream per target |
| `FleetActions.RunRetentionDays` | 30 | 1-3650 | Finished runs older than this are pruned (checked roughly every 100 health cycles) |

All four are applied live and can be changed through `PUT /api/v1/settings` with a `FleetActions` object, which replaces the whole group.

## Permissions

Command actions execute arbitrary code on the Admiral host or a Harbor, so creating, editing, deleting, running, or cancelling one requires tenant admin, the same bar as workspace exec. The same applies to any action with an applicability check. Mission actions follow voyage dispatch, which is also tenant admin today, so in practice every fleet action write requires tenant admin and every authenticated user in the tenant can read actions and runs. Before a run is created, every target vessel is re-read within the caller's tenant; a single vessel from another tenant, or one that does not exist, rejects the whole request instead of being quietly skipped.

Captured output can contain secrets that request-history redaction will not catch, because that redaction only covers headers. For that reason output is returned only by the single-target endpoint (`GET /api/v1/fleet-action-runs/{id}/targets/{targetId}`). Run creation responses, run details, target lists, and the MCP status tool return length hints instead.

## Using it

In the dashboard, open **Fleet Actions** in the sidebar (`/fleet-actions`) to browse, create, edit, and run actions and to watch runs (each run has a detail page at `/fleet-actions/runs/:id`). The Vessels list and the Vessel Health page both offer a bulk **Run action...** button for the selected vessels. In the TUI, the same Fleet Actions hub has **Actions** and **Runs** tabs, and Ask Armada's `/fleet-action` quick action runs a saved action across selected vessels.

From the API, `POST /api/v1/fleet-actions/{id}/run` with `{"VesselIds": [...], "Concurrency": 4, "InputValues": {...}}` (or `{"FromRunId": "far_..."}` for a follow-up) starts a run and returns 202 with the run ID; `GET /api/v1/fleet-action-runs/{id}` shows progress. [REST_API.md](REST_API.md#fleet-actions) has every route and shape.

From an MCP client, use `run_fleet_action`, `fleet_action_run_status`, and `cancel_fleet_action_run`, and `enumerate` with `fleet_action`, `fleet_action_run`, or `fleet_action_run_target` to browse. Output is excluded from enumeration unless you pass `includeOutput: true`. [MCP_API.md](MCP_API.md#fleet-actions) has the details.

From the command line:

```
armada action list
armada action list --runs
armada action run "Fast-forward default branch" --fleet flt_abc123
armada action run build --vessel api --vessel web --concurrency 2
armada action run update-package --fleet flt_abc123 --input package=Newtonsoft.Json --input version=13.0.4
armada action run fix-failing-tests --from-run far_...
armada action status far_...
armada action cancel far_...
```

`armada action run` accepts an action ID, name, or built-in key, and vessels by ID or name. `--input name=value` (repeatable) sets inputs. `--from-run <run-id>` starts a follow-up run on that run's vessels, selected by `--from-status` (repeatable; default Findings and Failed).

## Telemetry

Two instruments are emitted on the `Armada` meter: `armada_fleet_action_targets_total{kind,outcome}` counts targets as they finish (outcome is the terminal target status), and `armada_fleet_action_target_duration_seconds{kind}` records how long executed targets took. Labels never include vessel IDs, paths, or command text.
