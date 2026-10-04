# Fleet Actions

A fleet action is one piece of work you want done to many repositories at once. Sometimes that work is a shell command: pull the default branch everywhere, prune the branches that already merged, run every build. Sometimes it is a job for a captain: update the stale packages in each repository and land the result. Armada handles both through the same model, so you pick the vessels, pick the action, and get back a run that records exactly what happened to each vessel.

The feature replaces the "custom action" button in CodeHub, which typed a prompt into a fresh terminal window per repository and then forgot about it. Armada keeps the output, enforces timeouts and concurrency, lets you cancel, and remembers every run until the retention window expires.

## Actions, runs, and targets

An **action** is a saved, named definition. It has a kind, a body, and a few execution defaults. You can run an action as often as you like, and editing it later never changes the record of runs that already happened, because every run stores a snapshot of the definition it started from.

A **run** is one invocation of an action over a set of vessels. It carries its own status, its own concurrency, and counts of how many targets succeeded, failed, were skipped, or were cancelled. You can also start an ad hoc run with an inline definition when a one-off command does not deserve a saved action; such a run simply has no action ID.

A **target** is one vessel inside a run. Each target has its own status, a stable reason code when it was skipped or failed, timestamps, a duration, and either captured process output (Command actions) or the ID of the voyage it dispatched (Mission actions).

### Command actions

A Command action runs its command text in each vessel's working directory. That is deliberate: the point of "fast-forward everything" is to update the checkouts you actually work in, so Armada does not create a throwaway worktree for these runs. The safety net is the clean-tree check. When the action requires a clean working tree (the default for Command actions), Armada runs `git status --porcelain` first and skips the vessel with reason `DirtyTree` if anything is modified or untracked. Your uncommitted work is never touched by a fleet action you forgot was running.

Armada hands the command to the platform shell on standard input rather than as a quoted argument string. On Linux and macOS that is `/bin/sh -s`. On Windows it is `powershell -NoProfile -NonInteractive -ExecutionPolicy Bypass -Command -`. Feeding the shell through standard input sidesteps every quoting problem and leaves nothing on disk. PowerShell has two quirks worth knowing in this mode: it reads the script line by line, so a block statement that spans lines needs a blank line after it, and the exit code only reflects whether the last statement succeeded. If you want a native tool's exit code to decide success, end the command with `exit $LASTEXITCODE`.

When a vessel has a preferred Harbor, its checkout lives on that Harbor's machine, so the command runs there. The Harbor link does not carry standard input, so a Harbor-routed command is passed as a single argument instead (`/bin/sh -c` on Linux and macOS, `powershell ... -Command` on a Windows Harbor). If the preferred Harbor is not connected, the target is skipped with `HarborUnavailable` rather than run against the wrong machine. One limitation applies to Harbor targets: cancelling a run stops waiting for a Harbor command, but the Harbor protocol has no way to kill a process it already started, so a long command on a Harbor finishes on its own.

Each target records its exit code, standard output, and standard error. Output is capped at `FleetActions.MaxOutputBytes` per stream, keeping the **end** of the stream, because that is where build and test failures land. When anything was dropped, `OutputTruncated` is set. A command that runs past the action's timeout is killed, together with any child processes, and the target becomes `TimedOut`.

### Mission actions

A Mission action dispatches one voyage per vessel, with the rendered prompt as the single mission's description, through the same path `POST /api/v1/voyages` uses. The target stays `Running` while the voyage is active, becomes `Succeeded` when the voyage completes, and becomes `Failed` with reason `VoyageFailed` when a mission fails or its landing fails. Dispatch validation runs first. When it rejects a vessel (for example because the pipeline cannot be found), the target is skipped with `DispatchRejected` and the validator's message lands in `ErrorText`.

Concurrency means something different here. A captain voyage can take an hour, so a Mission run's concurrency is a pacing limit: at most that many voyages from the run are active at once. A sixty-vessel dependency sweep at concurrency 4 trickles work out four voyages at a time instead of flooding every captain and starving everything else. The Admiral's health-check loop advances Mission runs on every cycle, updating finished voyages and dispatching the next pending vessels, so progress moves at the heartbeat interval.

The `Persona` field is stored on actions and run snapshots, but this release does not yet apply it at dispatch time. Use a pipeline when you need a particular persona sequence.

## Templates

Command text and prompt templates are rendered per vessel from a small, fixed set of variables. There is no expression language and no code execution, just substitution.

| Variable | Value |
|---|---|
| `{{vessel.name}}` | The vessel's name |
| `{{vessel.id}}` | The vessel ID (`vsl_` prefix) |
| `{{vessel.defaultBranch}}` | The vessel's default branch |
| `{{vessel.workingDirectory}}` | The vessel's working directory, or empty |
| `{{vessel.buildCommand}}` | The vessel's definition-of-done build command, or empty |
| `{{health.summary}}` | Plain-text list of the vessel's failing, warning, and unknown health findings, followed by its outdated or vulnerable packages |

Names are case-insensitive and may have spaces inside the braces. Anything else between double braces is rejected when you save the action (HTTP 400, with the unknown name in the message), which also means a GitHub Actions style `${{ ... }}` cannot appear in a command. Substitution is a single pass: if a vessel's name happened to contain `{{vessel.id}}`, it would be inserted literally and never expanded again.

Values are inserted verbatim, without shell escaping. Vessel names and branches are set by tenant administrators, which is the same group allowed to run commands, but keep it in mind if you build commands around those values.

A target whose template references `{{vessel.buildCommand}}` is skipped with `NoBuildCommand` when the vessel has none. `{{health.summary}}` reads whatever the Vessel Health feature last stored; when nothing has been collected yet, it renders "No health data has been collected for this vessel yet." so the prompt still reads sensibly.

## Built-in actions

Each tenant starts with five actions so the feature is useful on day one. They are ordinary rows: edit them, delete them, or copy them as a starting point. Deleting a built-in hides it permanently, because Armada remembers the built-in key and never seeds it again.

| Action | Kind | What it does |
|---|---|---|
| Fast-forward default branch | Command | `git pull --ff-only`, skipping dirty trees |
| Prune merged branches | Command | `git fetch --prune`, then `git branch -d` for each local branch merged into the default branch; never the current branch, never the default branch |
| Build | Command | Runs `{{vessel.buildCommand}}`; vessels without one are skipped |
| Update outdated dependencies | Mission | A prompt that cites `{{health.summary}}` and asks a captain to update packages, build, and test |
| Add a test project | Mission | A prompt that asks a captain to add a conventional test project and make it pass |

The two shell bodies are chosen for the Admiral's platform when the tenant is seeded: a POSIX script on Linux and macOS, a PowerShell one-liner on Windows. Prune uses `git branch -d`, which refuses to delete anything git does not consider merged, and it falls back to the local default branch when the repository has no `origin`.

The intended loop runs through the Health page: select the red rows, run "Update outdated dependencies", and each vessel gets a voyage whose prompt lists that vessel's own outdated packages. When the voyages land, the next health evaluation turns the rows green.

## Status, cancellation, and restarts

A run's status rolls up from its targets. It is `Pending` until the first target starts and `Running` while any target is pending or running. Once every target is resolved it becomes `Completed`, or `CompletedWithFailures` when at least one target failed or timed out. Skipped targets do not count as failures, since a skip is Armada declining to act, not something going wrong. A run is `Failed` only when the runner itself hit an unexpected error.

Cancelling a run marks every pending target `Cancelled`, kills running commands (their targets also become `Cancelled`), and for Mission runs cancels each voyage that has not finished yet using the normal voyage cancel, which cancels pending and assigned missions and lets a mission already in progress finish. Cancelling a finished run returns HTTP 409.

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

Command actions execute arbitrary code on the Admiral host or a Harbor, so creating, editing, deleting, running, or cancelling one requires tenant admin, the same bar as workspace exec. Mission actions follow voyage dispatch, which is also tenant admin today, so in practice every fleet action write requires tenant admin and every authenticated user in the tenant can read actions and runs. Before a run is created, every target vessel is re-read within the caller's tenant; a single vessel from another tenant, or one that does not exist, rejects the whole request instead of being quietly skipped.

Captured output can contain secrets that request-history redaction will not catch, because that redaction only covers headers. For that reason output is returned only by the single-target endpoint (`GET /api/v1/fleet-action-runs/{id}/targets/{targetId}`). Run creation responses, run details, target lists, and the MCP status tool return length hints instead.

## Using it

From the API, `POST /api/v1/fleet-actions/{id}/run` with `{"VesselIds": [...], "Concurrency": 4}` starts a run and returns 202 with the run ID; `GET /api/v1/fleet-action-runs/{id}` shows progress. [REST_API.md](REST_API.md#fleet-actions) has every route and shape.

From an MCP client, use `run_fleet_action`, `fleet_action_run_status`, and `cancel_fleet_action_run`, and `enumerate` with `fleet_action`, `fleet_action_run`, or `fleet_action_run_target` to browse. Output is excluded from enumeration unless you pass `includeOutput: true`. [MCP_API.md](MCP_API.md#fleet-actions) has the details.

From the command line:

```
armada action list
armada action list --runs
armada action run "Fast-forward default branch" --fleet flt_abc123
armada action run build --vessel api --vessel web --concurrency 2
armada action status far_...
armada action cancel far_...
```

`armada action run` accepts an action ID, name, or built-in key, and vessels by ID or name.

## Telemetry

Two instruments are emitted on the `Armada` meter: `armada_fleet_action_targets_total{kind,outcome}` counts targets as they finish (outcome is the terminal target status), and `armada_fleet_action_target_duration_seconds{kind}` records how long executed targets took. Labels never include vessel IDs, paths, or command text.
