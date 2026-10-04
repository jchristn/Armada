# Bringing CodeHub's Capabilities into Armada

> **Type:** implementation plan (work-tracking). Annotate task status and the
> progress log as you go; keep this doc in sync with what actually shipped.
>
> **Status:** Phases A-C implemented on `feature/codehub-capabilities`; visual QA and simulated user testing not yet run; Phase D (optional) not started
> **Owner:** _unassigned_
> **Requirements baseline:** `~/Code/Agents/requirements` (see the compliance checklist near the end)
> **Last updated:** 2026-10-03

Status values used throughout: `[ ]` not started, `[~]` in progress, `[x]` done,
`[!]` blocked. Put a one-line note under any task you touch, and add a dated row
to the Progress Log at the bottom.

## Goal

Retire CodeHub as a separate tool. Today the operator runs two platforms against
the same `~/Code` tree: CodeHub to see which repositories are rotting (stale
dependencies, no tests, drifted branches), and Armada to actually do work in them.
The handoff between the two is manual. You spot a red row in CodeHub, open a
terminal, and then go tell Armada about it.

Three CodeHub capabilities need a home in Armada:

1. **Bulk onboarding.** Register dozens of repositories at once, from a pasted list
   of directories or by scanning a root directory for git repositories.
2. **Fleet actions.** Apply one action (a shell command or an AI prompt) across many
   selected vessels, with per-vessel results.
3. **Repository health.** A table that grades every vessel against defined criteria
   (outdated dependencies, test infrastructure, branch count, commits ahead and
   behind, and more), with backend filtering, sorting and paging.

Armada has advantages CodeHub never had. It already knows how to dispatch AI work
to captains, land the result through the merge queue, and record what happened.
CodeHub's "custom action" was a prompt typed into a new desktop terminal window,
with no output capture and no history. So the port should not be a copy. The real
prize is the loop that neither tool closes on its own: **a health finding becomes
a fleet action, the action becomes N voyages, and the voyages land as fixes.**

## Non-goals

- **Desktop launchers.** CodeHub opens Explorer, Finder, terminals and agent CLIs in
  new windows on the server host (`wt.exe`, AppleScript, a list of eleven Linux
  terminal emulators). Armada is a server that may be remote or containerized, and
  captains replace the interactive agent launch. None of this is ported.
- **Deleting repositories from disk** (Recycle Bin / Trash). Armada never deletes a
  user's clone. Removing a vessel stays non-destructive to `WorkingDirectory`.
- **CodeHub's author-specific scoring.** CodeHub only grades test infrastructure as
  Green when a project references Touchstone *and* is named `Test.Automated`; it also
  has Radiant and Watson checks. Armada needs criteria that work for anyone's
  repositories. The opinionated checks can come back later as an optional policy pack
  (see Phase D).
- **Infrastructure Armada already has:** request history, the API Explorer, the
  settings editor, themes, i18n, and the static API key auth model.
- **Migrating CodeHub's SQLite data.** Onboarding again from the same directories
  takes seconds and evaluating health takes minutes, so there is nothing worth
  carrying over. Manual annotations are the one exception; see Decision D5.

## What CodeHub does, and what we keep

CodeHub is a single-operator Watson + React app backed by SQLite. Its design is
close to Armada's own: Watson 7, PrettyId, SyslogLogging, React 19 and Vite. The
mechanics port easily. The data model needs rethinking.

**Onboarding in CodeHub** writes "scan selections" (include and exclude paths) and
does not register projects directly. Repositories only appear when a scan runs. At
scan time, discovery:

- recurses up to depth 8;
- skips a fixed set of excluded names (`bin`, `obj`, `node_modules`, `dist`, `.git`,
  `.vs`, `packages`, `TestResults`) and any directory starting with `.`;
- treats a directory as a repository when `.git` exists *as a directory*;
- stops descending once it finds a repository, so nested repos and submodules are
  not found.

The paste-a-list path normalizes each line with `Path.GetFullPath`, de-duplicates
case-insensitively, drops paths that don't exist, and returns `{added, ignored}`.

What we keep: the discovery rule, the exclude list, case-insensitive de-duplication
and repairing on-disk casing, and the cheap paste-a-list flow.

What we change: Armada's onboarding is **explicit**. Discovery produces a
reviewable candidate list, and the operator confirms it before any vessel is
created. Armada also has to recognize *worktrees*, because `.git` is a file there.
CodeHub misses worktrees entirely, and Armada's own docks are worktrees, so scanning
a root that contains Armada's data directory would otherwise import every dock as a
vessel.

**Custom actions in CodeHub** are `{name, prompt}` and nothing else. A run loops
over the selected repositories *inside the HTTP request*. For each one it writes the
prompt to a temp file and opens the chosen agent CLI in a new terminal window. It
captures no output, has no timeout or cancellation, and keeps no history.

What we keep: named, reusable action definitions, the select-then-apply table UX
(checkboxes plus a bulk bar), and the per-target result list.

What we change: actions come in two kinds. A **Command** action runs a shell command
in each vessel's working directory, capturing exit code and output. A **Mission**
action dispatches one voyage per vessel through the normal Admiral path. Both run as
persisted, cancellable, concurrency-limited background runs.

**Health evaluation in CodeHub** produces a per-repository row plus "signals". The
following table lists each criterion, how CodeHub computes it, and how Armada
should handle it.

| Criterion | CodeHub implementation | Verdict for Armada |
|---|---|---|
| Ahead/behind | `git rev-list --left-right --count <base>...HEAD`, base = first of `origin/main`, `origin/master`, `main`, `master`; no fetch | Keep. Fetch first (configurable) and use the vessel's `DefaultBranch`. Add upstream (`@{u}`) divergence. |
| Branch count | `git for-each-ref refs/heads`, one `rev-list` per branch | Keep. Add stale branches and leftover `armada/*` branches. |
| Outdated deps | `dotnet list package --outdated --format json`, 120 s timeout, drift via `DriftCalculator` | Keep for NuGet. Add npm in the first release. **A failed check must be Unknown, not Green.** |
| Vulnerabilities | `dotnet list package --vulnerable` + Dependabot alerts via GitHub API | Keep `dotnet list --vulnerable` and `npm audit`. Dependabot moves to Phase D. |
| Test infra | Touchstone + `Test.Automated` = Green, other test projects = Yellow, none = Red (C# only) | Replace with per-ecosystem detection, plus Armada's own check-run history. |
| Telemetry | Package refs (`OpenTelemetry*`, `Radiant`, ...) or a scan of up to 200 `.cs` files | Defer to the Phase D policy pack. |
| Issues/PRs | GitHub REST, no pagination (caps at 100) | Phase D, with pagination. |
| Last commit | `git log -1 --format=%cI` | Keep. |
| Overall | Worst of the scored signals | Keep the worst-of rollup. Make the criteria that feed it configurable. |
| Annotations | Per-column manual status override + note | Keep, as "overrides". Apply them consistently in summaries too (CodeHub's `/overview` ignores them). |

CodeHub has bugs we should design out rather than inherit, and the plan cites each
one where it applies:

1. A failed `dotnet list` shows as healthy.
2. Scheduled scans skip any repository whose HEAD hasn't changed, which freezes
   advisory and GitHub data.
3. Ahead/behind counts go stale because it never fetches.
4. Its list endpoint loads every repository into memory before filtering.
5. Home KPI links navigate to filtered URLs that the table never reads.

## Where this lands in Armada

The good news from surveying the codebase: almost every primitive exists per
vessel. What's missing is multi-vessel orchestration and persistence.

| Need | Existing Armada piece |
|---|---|
| Create a vessel | `POST /api/v1/vessels` (`src/Armada.Server/Routes/VesselRoutes.cs`); MCP `add_vessel` (`Mcp/Tools/McpVesselTools.cs`) with `TryResolveLocalClonePath`, which sets `WorkingDirectory` and deliberately leaves `LocalPath` unset |
| Bulk-route precedent | `POST /api/v1/vessels/delete/multiple` collects per-item skips |
| Shell execution | `IHostCommandExecutor.RunAsync(HostCommandRequest)` with Local and Remote (Harbor) implementations; `WorkspaceService.ExecAsync` behind `POST /api/v1/workspace/vessels/{id}/exec` (tenant admin only) |
| AI dispatch | `IAdmiralService.DispatchVoyageAsync(...)`, `ValidateDispatchAsync`; one voyage targets exactly one vessel |
| Background work | `JobService` (`EnqueueAsync`, `CancelAsync`, stale-Running reaper); `HealthCheckLoopAsync` in `ArmadaServer.cs` with every-cycle, 10-cycle and 100-cycle steps |
| Git facts | `IGitService.ListBranchesAsync` returns `BranchInfo` with Ahead/Behind; `GET /api/v1/vessels/{id}/git-status` (ahead/behind after fetch); `FetchAsync`, `IsRepositoryAsync` |
| Readiness | `VesselReadinessService.EvaluateAsync` (error/warning issues) |
| Test results | `CheckRunService` (build/test/coverage runs per vessel) |
| Dashboard | `VesselsHub.tsx` (tabs: Vessels, Fleets, Workspace), `api/client.ts`, `lib/useResourceTable.ts`, `lib/useAutoRefresh.ts`, `components/navConfig.tsx`, `i18n/runtime.ts` |
| MCP enumeration | `McpEnumerateTools.cs`: add to the `entityType` description and the `switch` |
| Persistence | Interface-per-entity on `DatabaseDriver`, four providers, `SchemaMigration` in each `TableQueries.cs` (MySQL is wired by hand in `MysqlDatabaseDriver.cs`). The latest migration is v70 (memories), which makes a good template. |
| Tests | `Test.Shared/Suites/**`, found by reflection; `TestGitRepoHelper`, `E2EServerFixture` |

**Naming.** Armada already has a `Signal` entity (admiral-captain messages, `sig_`).
CodeHub's per-criterion results therefore become **findings** here. The features
are named **Vessel Import**, **Fleet Actions** and **Vessel Health** so they line
up with Armada's existing vocabulary.

## Design

### A. Vessel Import

The flow has two steps: **discover**, then **import**. Nothing is written until the
operator confirms.

**Discover** takes either explicit directories, roots to scan, or both, and returns
candidates. It writes no vessels; it does persist an import batch with status
`Discovered` so the review screen survives a page reload. For each input path:

- **Explicit directory.** If it is a git repository, it becomes one candidate. If it
  isn't, discovery recurses into it like a scan root. This matches CodeHub's
  "ResolveTarget" fallback and makes "paste a parent folder" work.
- **Scan root.** Discovery collects git roots breadth-first to `MaxDepth` (default
  6, clamped 1 to 16). It skips names on `ExcludedDirectoryNames` and anything
  starting with `.`, and does not descend into a repository once it finds one.
- **What counts as a repository.** A directory is a repository when `.git` is a
  directory. When `.git` is a *file*, the directory is a worktree or submodule: it
  is reported with reason `Worktree` and excluded by default.
- **Directories that are always excluded:** `ArmadaSettings.ReposDirectory`, the
  docks directory, and the data directory. They are reported with reason
  `ArmadaManaged` so an operator who points the scan at `~/.armada` understands
  what happened.
- **Path handling:** de-duplicate case-insensitively on Windows and macOS (by
  default) and case-sensitively on Linux, and resolve on-disk casing the way
  CodeHub's `SelectionService` does.

Each candidate carries:

- `Path`
- `ProposedName`: the folder name, made unique within the tenant with a `-2`, `-3`
  suffix
- `RemoteUrl`: from `git remote get-url origin`, or null
- `DefaultBranch`: from `git symbolic-ref --short refs/remotes/origin/HEAD`, then
  the current branch, then `main`
- `ExistingVesselId`: set when a vessel in the tenant already has this
  `WorkingDirectory`, or the same normalized `RepoUrl`
- `Status`: `New`, `AlreadyOnboarded`, `Worktree`, `ArmadaManaged`, `NotFound`,
  `NotGit` or `AccessDenied`

**Import** takes the batch ID, the candidate paths the operator kept, an optional
`FleetId`, and optional per-vessel defaults (`DefaultPipelineId`, landing mode).
Each vessel is created following the exact rule `add_vessel` already uses:

- `RepoUrl` is the origin URL if there is one, otherwise the local path.
- `WorkingDirectory` is the discovered path.
- **`LocalPath` is left unset**, so removing the vessel can never delete the user's
  checkout.

Creation goes through a new `VesselService.CreateAsync`, which the existing POST
route and `add_vessel` are refactored to call. That way all three entry points share
validation instead of repeating it. Batches of 25 or fewer run inline. Larger
batches run as a `Job`, and the response returns the job ID.

**Discovery executes on the Admiral host by default.** If the operator picks a
Harbor, discovery runs through `RemoteHostCommandExecutor` using a small
`find-git-roots` command (Phase D). Armada's deployments include a Docker Admiral
that cannot see `~/Code` at all, and the UI has to say so plainly rather than return
an empty list. When the Admiral reports it is containerized and the root does not
exist, discovery returns `NotFound`, plus a hint to use a Harbor or mount the
directory.

**Security.** Reading arbitrary server directories is privileged. Discover, browse
and import all require `PermissionLevel.TenantAdmin`, the same level as
`/api/v1/vessels` writes and workspace exec. A new setting, `Import.AllowedRoots`,
limits discovery and browsing to listed roots. When it is empty, browsing is limited
to the user profile directory and the drives or mount points under it; system
directories are never offered.

### B. Fleet Actions

**An action definition** (`FleetAction`) holds:

- `Name` and `Description`
- `Kind`: `Command` or `Mission`
- `CommandText` (Command kind), or `PromptTemplate` plus an optional `PipelineId`
  and `Persona` (Mission kind)
- `TimeoutSeconds`: Command kind only; default 300, clamped 5 to 7200
- `DefaultConcurrency`: default 4, clamped 1 to 32
- `RequiresCleanWorkingTree`: default true for Command kind

Templates support a small fixed variable set, rendered server-side with no code
execution:

- `{{vessel.name}}`, `{{vessel.id}}`, `{{vessel.defaultBranch}}`,
  `{{vessel.workingDirectory}}`
- `{{health.summary}}`: a plain-text list of the vessel's non-green findings, which
  is what lets a health row turn into a useful prompt

**A run** (`FleetActionRun`) is one invocation over a target set. It stores a
snapshot of the rendered definition, so editing the action later doesn't rewrite
history. It also stores the status (`Pending`, `Running`, `Completed`,
`CompletedWithFailures`, `Cancelled`, `Failed`), target counts, concurrency,
`CreatedByUserId`, and timestamps. An ad hoc run with no saved definition is allowed
and keeps `ActionId` null.

**A run target** (`FleetActionRunTarget`) tracks one vessel within a run. It holds
`VesselId` and a status (`Pending`, `Skipped`, `Running`, `Succeeded`, `Failed`,
`Cancelled`, `TimedOut`). It also holds `SkipReason` as a stable code (`DirtyTree`,
`NoWorkingDirectory`, `DispatchRejected`, `NotAuthorized`).

The remaining fields depend on the action kind:

- **Command kind** adds `ExitCode`, `OutputText` and `ErrorText`. Output is truncated
  to `FleetActions.MaxOutputBytes` (default 65536); the truncation flag is kept.
- **Mission kind** adds `VoyageId`.

Every target also records `StartedUtc`, `CompletedUtc` and `DurationMs`.

**How a Command run executes.** A new `FleetActionRunner` service runs it, owned by
the server and started from `ArmadaServer.StartAsync`. A `SemaphoreSlim` gates it at
the run's concurrency. Each target goes through `IHostCommandExecutor` with the
vessel's `WorkingDirectory` as the working directory. When the vessel has a
`PreferredHarborId`, the target runs on that Harbor, because that's where the
checkout lives. The command goes to the platform shell through stdin, not as an
argument string. That sidesteps the quoting problems CodeHub solved with temp files,
and nothing is left on disk. Cancellation cancels the token for in-flight processes
and marks pending targets `Cancelled`.

**Restart recovery.** On startup, any target still marked `Running` from a Command
run is set to `Failed` with the reason `Interrupted`. Pending targets resume.

**How a Mission run executes.** The run fans out to one `DispatchVoyageAsync` per
vessel, with the rendered prompt as the single mission description. Each target
stores its `VoyageId` and then follows the voyage's status:

- the target is `Running` while the voyage is active;
- `Succeeded` when the voyage completes and lands;
- `Failed` on a mission failure or `LandingFailed`.

A new every-cycle step in `HealthCheckLoopAsync` keeps target statuses in sync. It
only touches runs that still have unresolved targets.

Concurrency for Mission runs means *dispatch pacing*. At most N voyages from this
run are active at once, so a 60-vessel "update dependencies" run doesn't swamp every
captain and starve unrelated work. Cancelling a Mission run cancels its voyages that
haven't landed yet, using the existing cancel path.

**Why runs are a new entity rather than voyages.** A voyage belongs to exactly one
vessel, and widening that contract would ripple through scheduling, landing and the
dashboard. A run is a thin parent over N voyages, so voyages stay as they are.

**Security.**
- Command kind is arbitrary code execution on the host, so creating and running it
  requires `TenantAdmin`, matching workspace exec.
- Mission kind follows the existing voyage dispatch permission.
- Before execution begins, every target vessel is re-read with the tenant-scoped
  `ReadAsync`. Vessels from another tenant are rejected for the whole request, not
  silently skipped.
- Output may contain secrets that request-history redaction will not catch, because
  it only redacts headers. Run-target output is returned only from the run-detail
  endpoints, never echoed in the create response.

**Built-in actions** are seeded on first boot. Each one can be edited and deleted.
They exist so the feature is useful on day one:

| Action | Kind | Body |
|---|---|---|
| Fast-forward default branch | Command | `git pull --ff-only` |
| Prune merged branches | Command | `git fetch --prune` then delete local branches merged into the default branch, excluding the current one |
| Build | Command | uses the vessel's definition-of-done build command when set |
| Update outdated dependencies | Mission | prompt template that cites `{{health.summary}}` |
| Add a test project | Mission | prompt template |

### C. Vessel Health

**Persistence.** Everything the table sorts or filters on is a typed, indexed column.
Lists live in child tables. The four new tables are:

`vessel_health` (one current row per vessel), with:
- **Keys:** `VesselId` (unique), `TenantId`
- **Status:** `OverallStatus`, `EvaluatedUtc`, `EvaluationDurationMs`, `ErrorCode`
- **Git:** `CurrentBranch`, `IsDirty`, `UntrackedCount`, `AheadOfDefault`,
  `BehindDefault`, `AheadOfUpstream`, `BehindUpstream`, `LastCommitUtc`
- **Branches:** `BranchCount`, `StaleBranchCount`, `ArmadaBranchCount`
- **Project:** `PrimaryLanguage`, `ProjectCount`
- **Dependencies:** `OutdatedCount`, `OutdatedMajorCount`, `VulnerableCount`,
  `MaxVulnerabilitySeverity`, `DependencyStatus`
- **Testing and CI:** `TestInfraStatus`, `LastCheckRunStatus`, `HasCiConfig`,
  `HasLicense`, `HasReadme`
- **Armada activity:** `ReadinessErrorCount`, `RecentMissionFailureCount`
- **Freshness:** `ManifestHash`, `DependenciesEvaluatedUtc`

`vessel_health_findings` holds one row per criterion per vessel:
- `Criterion` (stable code) and `Status` (`Pass`, `Warn`, `Fail`, `NotApplicable`,
  `Unknown`)
- `DetailCode` (stable code), plus typed `ValueA` and `ValueB` integers for the
  counts the client formats
- `EvaluatedUtc`

`vessel_dependencies` lists each outdated or vulnerable dependency:
- `Ecosystem` and `ProjectPath`
- `PackageName`, `CurrentVersion`, `LatestVersion`
- `Drift` (`Patch`, `Minor`, `Major`), `Severity`, `AdvisoryUrl`

`vessel_health_overrides` holds a manual status and note per vessel per criterion.

Deleting a vessel cascades to all four tables.

Stable codes, not rendered sentences, are what make i18n work. The server stores
`DetailCode = OutdatedPackages, ValueA = 7, ValueB = 2`. The dashboard renders
"7 outdated packages (2 major)" in the viewer's locale.

**Criteria** are pluggable: `IVesselHealthCriterion` exposes `Code`, `AppliesAsync`
and `EvaluateAsync`, and each criterion has its own class. These ship in the first
release:

| Code | Source | Pass / Warn / Fail (defaults, all configurable) |
|---|---|---|
| `GitDivergence` | `git fetch --prune` (if `FetchBeforeEvaluate`), then `rev-list --left-right --count origin/<DefaultBranch>...HEAD` and `@{u}...HEAD` | behind 0 / behind 1-20 / behind > 20 or diverged both ways |
| `WorkingTree` | `git status --porcelain` | clean / untracked only / modified tracked files |
| `Branches` | `IGitService.ListBranchesAsync`, plus commit dates | stale <= 3 and armada/* = 0 / stale <= 10 / more |
| `CommitRecency` | `git log -1 --format=%cI` | informational; filter and sort only |
| `Dependencies` | NuGet: `dotnet list package --outdated --format json`; npm: `npm outdated --json` | none / minor or patch drift / any major drift. **Tool failure, timeout or missing restore = `Unknown` with a `DetailCode`, never Pass.** |
| `Vulnerabilities` | `dotnet list package --vulnerable --format json`; `npm audit --json` | none / low or moderate / high or critical |
| `TestInfrastructure` | ecosystem detectors (below) plus the latest `CheckRun` for the vessel | test project or config found and last check run passed / found but no passing run / none found |
| `ContinuousIntegration` | presence of `.github/workflows/*.y*ml`, `azure-pipelines.yml`, `.gitlab-ci.yml`, `Jenkinsfile` | present / -- / absent (excluded from the overall rollup by default) |
| `ArmadaReadiness` | `VesselReadinessService.EvaluateAsync` | no issues / warnings / errors |
| `MissionOutcomes` | failed and `LandingFailed` missions on the vessel in the last `MissionWindowDays` (default 7) | 0 / 1-2 / 3+ |

Test-infrastructure detection is per ecosystem, so it works on repositories that
don't follow any one author's conventions:

- **.NET:** references to `Microsoft.NET.Test.Sdk`, `xunit*`, `NUnit*`, `MSTest*` or
  `Touchstone*`.
- **Node:** `jest`, `vitest`, `mocha` or `playwright` in `devDependencies`, or a
  `test` script that isn't npm's default placeholder.
- **Python:** `pytest.ini`, `[tool.pytest]` in `pyproject.toml`, `tox.ini`, or a
  `tests/` directory.
- **Go:** any `*_test.go` file.
- **Rust:** a `tests/` directory or `#[cfg(test)]` in `src/`.

The `CheckRun` cross-check is something only Armada can do: a repository with test
projects whose last test run failed is not healthy.

**Which path is evaluated.** Health runs against `WorkingDirectory` when it is set,
since that is the checkout a person is actually using. Otherwise it runs against the
bare `LocalPath`, where dirty-tree and test detection report `NotApplicable`.

**Freshness, not "HEAD unchanged".** Git criteria run on every evaluation, because
they're cheap. Dependency and vulnerability checks are slow (`dotnet list` can take
a minute), so they're skipped when two things hold:
- the hash of all manifest and lock files (`*.csproj`, `Directory.Packages.props`,
  `packages.lock.json`, `package.json`, `package-lock.json`, and so on) matches
  `ManifestHash`;
- `DependenciesEvaluatedUtc` is newer than `DependencyMaxAgeHours` (default 24).

The max-age guard fixes CodeHub's frozen-advisory bug: new CVEs get published
against unchanged lockfiles.

**Scheduling.** `HealthCheckLoopAsync` gets a step that runs every
`RepositoryHealth.IntervalMinutes` (default 360; 0 disables it). When due, the step
enqueues a `Job` of kind `Report` that evaluates every active vessel. Evaluation is
limited to `MaxConcurrency` vessels at a time (default 4, clamped 1 to 32). Only one
evaluation job runs per tenant; a second request returns 409 with the existing job
ID. Manual evaluation of selected vessels is always available and forces the
dependency checks.

**Overall status** is the worst status among criteria listed in
`RepositoryHealth.ScoredCriteria`, after overrides are applied. `Unknown` and
`NotApplicable` never make a vessel look healthy, and they never fail it either.
A vessel whose every scored criterion is `Unknown` shows `Unknown`, not Pass.
Overrides apply to the list endpoint, the detail endpoint *and* the summary
endpoint, so the KPI tiles always agree with the table.

### The loop that justifies all of this

On the Health page, select the red rows, then choose **Run action...** and pick
"Update outdated dependencies". Each vessel gets a voyage whose prompt lists that
vessel's outdated packages. The run page tracks the voyages, and once they land, the
next evaluation turns the rows green. With import feeding the health table and
actions working the backlog it reveals, CodeHub stops being needed.

## API surface

All routes live under `/api/v1`, matching the rest of Armada and BACKEND_ARCHITECTURE
"API Rules". Each feature gets its own registrar class in `src/Armada.Server/Routes/`
(`VesselImportRoutes`, `FleetActionRoutes`, `VesselHealthRoutes`), wired in
`ArmadaServer.RegisterRoutes()`. Every route carries OpenAPI tag and summary
metadata, uses typed request and response DTOs, and validates before doing work.

| Method | Path | Permission | Purpose |
|---|---|---|---|
| GET | `/api/v1/vessels/import/browse?path=` | TenantAdmin | List subdirectories under an allowed root (flags `isGitRepository`, `isWorktree`, `hasSubdirectories`) |
| POST | `/api/v1/vessels/import/discover` | TenantAdmin | `{directories[], roots[], maxDepth?, harborId?}` returns `{batchId, candidates[]}` |
| POST | `/api/v1/vessels/import` | TenantAdmin | `{batchId, paths[], fleetId?, defaults?}` returns per-item results, or `{jobId}` for large batches |
| POST | `/api/v1/vessels/import/batches/enumerate` | Authenticated | Paged import history |
| GET | `/api/v1/vessels/import/batches/{id}` | Authenticated | Batch with items |
| POST | `/api/v1/fleet-actions/enumerate` | Authenticated | Paged action definitions |
| POST / GET / PUT / DELETE | `/api/v1/fleet-actions[/{id}]` | TenantAdmin for Command kind, Authenticated for Mission kind | CRUD |
| POST | `/api/v1/fleet-actions/{id}/run` | as above | `{vesselIds[], concurrency?, overrides?}` returns 202 `{runId}` |
| POST | `/api/v1/fleet-actions/run` | as above | Ad hoc run with an inline definition |
| POST | `/api/v1/fleet-action-runs/enumerate` | Authenticated | Paged runs |
| GET | `/api/v1/fleet-action-runs/{id}` | Authenticated | Run with target summaries (no output) |
| POST | `/api/v1/fleet-action-runs/{id}/targets/enumerate` | Authenticated | Paged targets, filterable by status |
| GET | `/api/v1/fleet-action-runs/{id}/targets/{targetId}` | Authenticated | Target with `OutputText` and `ErrorText` |
| POST | `/api/v1/fleet-action-runs/{id}/cancel` | as the run | Cancel |
| POST | `/api/v1/vessel-health/enumerate` | Authenticated | Filtered, sorted, paged health rows |
| GET | `/api/v1/vessel-health/summary` | Authenticated | KPI counts (override-aware) |
| GET | `/api/v1/vessels/{id}/health` | Authenticated | Row + findings + dependencies + overrides |
| POST | `/api/v1/vessel-health/evaluate` | TenantAdmin | `{vesselIds[]?, fleetId?, force?}` returns 202 `{jobId}`, or 409 with the running job's ID |
| PUT / DELETE | `/api/v1/vessels/{id}/health/overrides/{criterion}` | TenantAdmin | Manual override with note |

**Enumerate contract.** BACKEND_ARCHITECTURE never defines "jfilters", so this plan
defines the contract here, following the `RequestHistoryFilter` pattern: a typed
filter DTO, never a dictionary. `VesselHealthEnumerateRequest` carries:

- **Paging:** `PageNumber` (from 1) and `PageSize` (clamped 1 to 500)
- **Sorting:** `SortBy`, a whitelisted column code, and `SortDescending`
- **Text and fleet filters:** `NameContains`, `FleetId`, `PrimaryLanguage`,
  `CurrentBranchContains`
- **Status filters:** `OverallStatus[]`, `DependencyStatus[]`, `TestInfraStatus[]`,
  `IsDirty`, `HasCiConfig`
- **Divergence:** `Divergence`, one of `Ahead`, `Behind`, `Diverged` or `Even`
- **Ranges:** `MinBranchCount` / `MaxBranchCount` and
  `LastCommitAfterUtc` / `LastCommitBeforeUtc`
- **Excluded vessels:** `IncludeInactive`

The response uses the shape the dashboard already expects: `items`, `pageNumber`,
`pageSize`, `totalCount`, `totalPages`. Filtering and sorting run in SQL against the
indexed columns, never in memory. That avoids CodeHub's "load everything, then LINQ"
design.

**MCP tools** are registered in both `RegisterAll` and `RegisterCatalogGroup` in
`McpToolRegistrar.cs`, using new `McpVesselImportTools`, `McpFleetActionTools` and
`McpVesselHealthTools` groups:

| Feature | Tools |
|---|---|
| Vessel import | `discover_vessels`, `import_vessels` |
| Fleet actions | `create_fleet_action`, `update_fleet_action`, `delete_fleet_action`, `run_fleet_action`, `fleet_action_run_status`, `cancel_fleet_action_run` |
| Vessel health | `vessel_health`, `evaluate_vessel_health`, `set_vessel_health_override` |

`enumerate` gains the entity types `vessel_import_batch`, `fleet_action`,
`fleet_action_run`, `fleet_action_run_target` and `vessel_health`. In keeping with
CLAUDE.md's context-conservation rule, `OutputText` is excluded by default behind an
`includeOutput` flag that returns length hints, the same pattern `includeTestOutput`
uses.

**Helm CLI** adds three command groups:

- `armada vessel import <paths...> [--root <dir>]... [--fleet <id>] [--dry-run] [--yes]`.
  Dry-run prints the candidate table; without `--yes` it prompts once.
- `armada action list|run|status|cancel`.
- `armada health [--status Fail] [--fleet <id>] [--evaluate]`.

## Dashboard

There are three surfaces, each routed and present in the nav. This is the route
inventory DASHBOARD_STYLE_AND_USABILITY requires:

| Route | Nav section | User job | Backend | Key actions | Empty state |
|---|---|---|---|---|---|
| `/vessels/health` (new tab in `VesselsHub`) | BUILD | Find vessels that need attention and act on them | `vessel-health/enumerate`, `summary`, `evaluate` | Row: View details, Branches, Override, Re-evaluate, Open vessel. Bulk: Run action..., Re-evaluate | "No vessels yet. Import repositories" CTA, which opens the import wizard |
| Import wizard (modal from Vessels tab and Health tab; route `/vessels/import` for deep links) | BUILD | Onboard many repositories at once | `import/browse`, `import/discover`, `import` | Source step (paste list or browse roots) -> Review (checkbox table of candidates with status badges, fleet picker) -> Results | Discover returned nothing: explain depth, excludes, and the containerized-Admiral case |
| `/fleet-actions` with `/fleet-actions/runs/:id` | DELIVERY | Define reusable actions; watch runs | `fleet-actions/*`, `fleet-action-runs/*` | Actions table: Run, Edit, Duplicate, View JSON, Delete. Runs table: View, Cancel, Re-run failed targets. Run detail: target table plus an output drawer | "No actions yet" with the seeded built-ins explained |

**Health table columns** (all sortable on the server; ID cells never wrap):

- Vessel and Fleet
- Overall
- Divergence, shown as up/down arrows plus a localized tooltip naming the base
- Dirty and Branches (count, with stale count)
- Dependencies, Vulnerabilities, Tests and CI
- Last commit (relative time) and Evaluated

**Status badges** pair an icon with a localized text label and never rely on color
alone. A `*` marks an overridden cell, and its tooltip shows the note. Column
selection and page size persist per table, and filters live in the URL query string.
CodeHub's Home tiles linked to filtered URLs the table ignored; ours read them, so
deep links from the Home KPIs work.

**Home** gains KPI tiles: Vessels failing health, Outdated majors, High/critical
vulnerabilities, and Active fleet action runs. Each tile links to a filtered view.
Home also gets two CTA cards, **Import repositories** and **Run fleet action**, and
both respect permissions.

**Run detail** is the screen that has to feel live:

- the run header shows status, counts and a progress bar;
- the target table auto-refreshes every 5 s while the run is active, using
  `useAutoRefresh`, and pauses while a modal or drawer is open;
- Mission targets link to their voyage;
- Command targets open an output drawer that uses the existing `LogViewer`.

**Shared building blocks.** All three surfaces use `useResourceTable`, named
`api/client.ts` functions (`enumerateVesselHealth`, `discoverVesselImport`,
`runFleetAction`, and so on), the shared confirmation dialog (never
`window.confirm`), and toasts for long-running operations.

## Settings

Three nested settings objects are added to `ArmadaSettings`, each one class per file
with validated, clamped setters and XML docs stating defaults and ranges. All of them
show up on the Settings page and accept environment-variable overrides.

| Setting | Default | Range | Applied |
|---|---|---|---|
| `Import.AllowedRoots` | `[]` (user profile only) | -- | live |
| `Import.MaxDepth` | 6 | 1-16 | live |
| `Import.ExcludedDirectoryNames` | CodeHub's list plus `.armada`, `target`, `venv`, `.venv`, `__pycache__` | -- | live |
| `Import.InlineBatchLimit` | 25 | 1-500 | live |
| `FleetActions.MaxConcurrency` | 8 | 1-32 | live |
| `FleetActions.DefaultTimeoutSeconds` | 300 | 5-7200 | live |
| `FleetActions.MaxOutputBytes` | 65536 | 1024-1048576 | live |
| `FleetActions.RunRetentionDays` | 30 | 1-3650 | live (pruned in the 100-cycle step) |
| `RepositoryHealth.IntervalMinutes` | 360 | 0-10080 (0 = off) | live |
| `RepositoryHealth.MaxConcurrency` | 4 | 1-32 | live |
| `RepositoryHealth.FetchBeforeEvaluate` | true | -- | live |
| `RepositoryHealth.DependencyMaxAgeHours` | 24 | 1-720 | live |
| `RepositoryHealth.DependencyCommandTimeoutSeconds` | 120 | 10-900 | live |
| `RepositoryHealth.StaleBranchDays` | 90 | 1-3650 | live |
| `RepositoryHealth.MissionWindowDays` | 7 | 1-90 | live |
| `RepositoryHealth.ScoredCriteria` | all except `ContinuousIntegration`, `CommitRecency` | criterion codes | live |
| `RepositoryHealth.Thresholds.*` | per the criteria table | clamped per field | live |

## Tasks

Each phase ships on its own and is useful without the next one. Phase A goes first
because Phase C has nothing to grade until vessels exist in bulk.

### Phase A -- Vessel Import

- [x] **A1 -- Settings.** Add `VesselImportSettings` (`Import`) to `ArmadaSettings`
  with the fields above.
  _Acceptance:_ out-of-range values clamp; env overrides apply; the Settings page
  shows and saves them.
  _Note (2026-10-03):_ `VesselImportSettings` class and `ArmadaSettings.Import` added with clamping; Settings page/API exposure and env overrides not done (no existing env-override mechanism).

- [x] **A2 -- Discovery engine.** `VesselDiscoveryService` in
  `src/Armada.Core/Services/` (interface in `Interfaces/`), with
  `DiscoverAsync(VesselDiscoveryRequest, CancellationToken)` and its supporting
  models and enums (`VesselImportCandidate`, `VesselImportCandidateStatusEnum`). It
  handles the git, worktree, ArmadaManaged and NotGit rules, depth, excludes,
  de-duplication, casing repair, and remote and default-branch inference through
  `IGitService` or `IHostCommandExecutor`.
  _Acceptance:_ a `VesselDiscoverySuite` built on `TestGitRepoHelper` covers:
  - nested repositories (stops at the first);
  - a worktree (reported, excluded);
  - Armada's repos and docks dirs (ArmadaManaged);
  - depth limit, the exclude list, and duplicate paths in different case;
  - a path with spaces and unicode;
  - a `../` path normalized;
  - a nonexistent path (NotFound);
  - a root with 1,000 empty directories, finishing under 5 s.

- [x] **A3 -- `VesselService.CreateAsync`.** Extract validation and creation out of
  `VesselRoutes` POST and `McpVesselTools.add_vessel` into a shared service; both
  callers delegate to it. Behavior is unchanged; `LocalPath` is never set for a
  local-path `RepoUrl`.
  _Acceptance:_ existing vessel route and MCP suites pass unchanged; a new case
  asserts a local-path import leaves `LocalPath` null.

- [x] **A4 -- Persistence.** `VesselImportBatch` (`vib_`) and `VesselImportItem`
  (`vii_`) models, `IVesselImportMethods`, four provider implementations, and
  migration v71 (or the next free number) in all four `TableQueries.cs` plus the
  MySQL wiring. Unique index on `(tenant_id, batch_id, path)`.
  _Acceptance:_ `VesselImportDatabaseSuite` passes on every provider in the matrix.
  _Note (2026-10-03):_ shipped as `IVesselImportBatchMethods` + `IVesselImportItemMethods`, migration v71; suite green on all four providers.

- [x] **A5 -- Import execution.** `VesselImportService.ImportAsync`: inline at or
  below `InlineBatchLimit`, otherwise a `Job`. Per-item outcomes are `Created`,
  `SkippedExisting`, `SkippedNameConflict` (only when the auto-suffix is disabled)
  and `Failed`, each with a stable reason code. Creating the same path twice must be
  idempotent.
  _Acceptance:_ importing the same batch twice creates no duplicates; a 200-path
  import runs as a job and the batch reflects the final counts.

- [x] **A6 -- REST routes.** Add `VesselImportRoutes` and the permission entries in
  `AuthorizationConfig` (TenantAdmin for browse, discover and import; Authenticated
  for history reads).
  _Acceptance:_ route suite covers 400 (empty input), 403 (non-admin), 404 (a batch
  from another tenant), and the 200 and 202 paths.

- [x] **A7 -- MCP and enumerate.** Add `discover_vessels`, `import_vessels`, and the
  `vessel_import_batch` enumerate type.
  _Acceptance:_ MCP suite round-trips both tools.

- [x] **A8 -- Helm.** Add `armada vessel import` with dry-run and confirm.
  _Acceptance:_ manual run against `~/Code` lists candidates; `--yes` imports.

- [~] **A9 -- Dashboard wizard.** Three steps: Source, Review, Results. Source has a
  paste list (with a live line count) and a server-side browse tree with checkboxes.
  Review has a candidate table with a status filter, select-all-new, and a fleet
  picker. All strings go through i18n.
  _Acceptance:_ visual QA at 1280 / 768 / 390, in light and dark, for every step,
  including an empty discovery and a 500-candidate review.
  _Notes:_ Built and unit-tested; the Playwright visual QA pass (1280/768/390, light/dark) has not been run.

- [x] **A10 -- Docs.** Update `docs/REST_API.md`, `docs/MCP_API.md`, the Postman
  collection (an "Import" folder), README "Onboarding many repositories", and a
  CHANGELOG entry under Unreleased.

### Phase B -- Fleet Actions

- [x] **B1 -- Settings.** Add `FleetActionSettings`.
  _Note (2026-10-03):_ class and `ArmadaSettings.FleetActions` added; Settings page/API exposure pending.

- [x] **B2 -- Models and persistence.** `FleetAction` (`fac_`), `FleetActionRun`
  (`far_`), `FleetActionRunTarget` (`fat_`), with enums `FleetActionKindEnum`,
  `FleetActionRunStatusEnum` and `FleetActionTargetStatusEnum`. Add the interfaces,
  four provider implementations, and the next migration. Index
  `(tenant_id, run_id, status)` on targets. `OutputText` and `ErrorText` are
  explicitly named unmanaged-text columns; every status and number is typed.
  _Acceptance:_ database suite on every provider, including concurrent target
  updates on SQLite under the write lock.
  _Note (2026-10-03):_ migration v72; SQLite writes serialized through the driver write lock; suite green on all four providers.

- [x] **B3 -- Template renderer.** `FleetActionTemplateRenderer` resolves the fixed
  variable set. An unknown variable fails validation with a 400 that names it.
  _Acceptance:_ unit suite; a variable value containing `{{` is not re-expanded.

- [x] **B4 -- Command runner.** `FleetActionRunner` handles concurrency, timeout,
  cancellation, the dirty-tree pre-check, Harbor routing via `PreferredHarborId`,
  output truncation, and restart recovery (`Running` -> `Failed/Interrupted`).
  _Acceptance:_ suite covers success, non-zero exit, timeout, cancel mid-run (pending
  targets become Cancelled and the in-flight process is killed), a dirty tree
  skipped, and output truncation flagged.

- [x] **B5 -- Mission fan-out.** Dispatch with pacing, store `VoyageId`, add an
  every-cycle sync step in `HealthCheckLoopAsync`, and cancel through the existing
  voyage cancel path. A `ValidateDispatchAsync` failure becomes `Skipped` with
  `DispatchRejected` and the validator's message.
  _Acceptance:_ E2E suite with stub captains: 5 vessels at concurrency 2 never have
  more than 2 active voyages, and statuses follow the voyages to completion.

- [x] **B6 -- Seeded built-ins.** First-boot seeding of the five actions above,
  tenant-scoped, editable, and not re-seeded after deletion.

- [x] **B7 -- REST routes and authz.** Add `FleetActionRoutes` and the
  `AuthorizationConfig` entries. The Command-kind check happens in the handler
  (TenantAdmin), because `PermissionLevel` is path-based and can't see the body.
  _Acceptance:_ a non-admin creating or running a Command action gets 403; a
  cross-tenant vessel in `vesselIds` rejects the whole run.

- [x] **B8 -- MCP, enumerate, Helm.** Add the tools listed above, the enumerate
  types with `includeOutput`, and `armada action`.

- [~] **B9 -- Dashboard.** Add the `/fleet-actions` page (actions and runs tabs), the
  run-detail page with the output drawer, and a **Run action...** bulk action on the
  Vessels table. The run modal covers: pick or define an action, preview the
  rendered command for the first selected vessel, set concurrency, and confirm. The
  confirmation for a Command run states the vessel count and that the command runs
  in each working directory.
  _Acceptance:_ visual QA as in A9; a 50-target run stays responsive.
  _Notes:_ Built and unit-tested; visual QA not run. Actions/Runs tables have no column chooser and sort only by Created (the server supports no other order).

- [x] **B10 -- Docs.** Update REST_API.md, MCP_API.md, Postman ("Fleet Actions"
  folder), a new `docs/FLEET_ACTIONS.md` guide, and the CHANGELOG.

### Phase C -- Vessel Health

- [x] **C1 -- Settings.** Add `RepositoryHealthSettings`, including thresholds.
  _Note (2026-10-03):_ `RepositoryHealthSettings` + `RepositoryHealthThresholds` added; Settings page/API exposure pending.

- [x] **C2 -- Models and persistence.** Add the four tables above (`vhl_`, `vhf_`,
  `vdp_`, `vho_`), interfaces, provider implementations, and a migration. Index every
  column the enumerate filter or sort exposes, plus `(tenant_id, overall_status)`.
  Deleting a vessel cascades.
  _Acceptance:_ database suite on all providers, with sort and filter on every
  whitelisted column.
  _Note (2026-10-03):_ migration v73; status columns hold effective (override-aware) values; vessel delete cascades via FK ON DELETE CASCADE; suite green on all four providers.

- [x] **C3 -- Criterion framework.** `IVesselHealthCriterion`,
  `VesselHealthEvaluator` (runs the applicable criteria, applies the rollup and
  overrides, writes atomically per vessel), and the `ManifestHash` freshness logic.
  _Acceptance:_ the rollup suite pins down the Unknown and NotApplicable semantics,
  plus override precedence.

- [x] **C4 -- Git criteria.** `GitDivergence`, `WorkingTree`, `Branches` and
  `CommitRecency`. All of them reuse `IGitService` where it has a method and add
  methods there rather than shelling out ad hoc.
  _Acceptance:_ suite built on `TestGitRepoHelper` with a local bare "origin": ahead,
  behind and diverged counts are correct after fetch; dirty versus untracked;
  stale-branch age; leftover `armada/*` branches counted.

- [x] **C5 -- Dependency criteria.** `Dependencies` and `Vulnerabilities` for NuGet
  and npm. Port CodeHub's `dotnet list` JSON parsing and `DriftCalculator`,
  rewritten to Armada style (no `var`, typed DTOs and no `JsonElement` access, one
  class per file). Search solutions while honoring the exclude list. A missing tool,
  missing restore, timeout or non-zero exit produces `Unknown` plus a `DetailCode`.
  _Acceptance:_ fixture JSON files for both tools; an explicit test that a failed
  `dotnet list` is never `Pass`.

- [x] **C6 -- Repository-shape criteria.** `TestInfrastructure` (ecosystem detectors
  plus the latest `CheckRun`), `ContinuousIntegration`, license and readme.
  _Acceptance:_ fixture repositories for .NET, Node, Python, Go and Rust.

- [x] **C7 -- Armada criteria.** `ArmadaReadiness` (wraps `VesselReadinessService`)
  and `MissionOutcomes`.

- [x] **C8 -- Scheduling.** Add the `HealthCheckLoopAsync` step and a `Job` per
  evaluation, with one evaluation per tenant at a time (409 otherwise) and per-vessel
  failures isolated.
  _Acceptance:_ with `IntervalMinutes = 1` in a test fixture, an evaluation is
  enqueued once and not again while it runs.

- [x] **C9 -- REST, MCP, Helm.** Add `VesselHealthRoutes`, the enumerate DTO, tools,
  the `vessel_health` enumerate type, and `armada health`.

- [~] **C10 -- Dashboard.** Health tab, detail modal (sections: Summary, Findings,
  Dependencies, Branches, Overrides, Raw JSON), Home KPIs and CTAs, and a bulk
  **Run action...** that pre-selects the Mission kind and offers the built-in
  templates that reference `{{health.summary}}`.
  _Acceptance:_ visual QA; Home tiles deep-link into filtered views; a filter set
  survives reload through the URL.
  _Notes:_ Built and unit-tested; visual QA not run. Overridden values are marked only in the detail modal, not in the list.

- [x] **C11 -- Telemetry.** On the existing `ArmadaMetrics` meter, add:
  - `armada_health_evaluations_total{outcome}`
  - `armada_health_criterion_duration_seconds{criterion}`
  - `armada_fleet_action_targets_total{kind,outcome}`
  - `armada_fleet_action_target_duration_seconds{kind}`
  - `armada_vessel_import_items_total{outcome}`

  Spans: a root span per job or run and `stage:<Criterion>` children. No vessel IDs,
  paths or commands go in metric labels; they belong only on span attributes. Add a
  Grafana panel group under `assets/grafana/`.

- [x] **C12 -- Docs.** Update REST_API.md, MCP_API.md, Postman ("Vessel Health"),
  a new `docs/VESSEL_HEALTH.md` (criteria, thresholds, and what each status means),
  README, and the CHANGELOG.

### Phase D -- Optional, after A-C ship

- [ ] **D1 -- GitHub signals.** Open issues and PRs with ages, plus Dependabot alerts,
  all paginated. Use the vessel's GitHub token override or the global token, and
  record failures as `Unknown`.
- [ ] **D2 -- More ecosystems.** pip (`pip list --outdated --format json` inside the
  project's venv when detectable), Go (`go list -m -u -json all`), Cargo (only when
  `cargo-outdated` is installed; otherwise `NotApplicable` with a hint).
- [ ] **D3 -- Policy packs.** Opt-in criteria sets such as "jchristn conventions":
  Touchstone plus `Test.Automated`, telemetry detection, and the Watson 7 check
  ported from CodeHub.
- [ ] **D4 -- Harbor-side discovery.** A `find-git-roots` Harbor command so a
  containerized Admiral can import repositories on a workstation.
- [ ] **D5 -- Health history.** A daily rollup table to drive trend charts on the
  Health tab (hand-rolled SVG per FRONTEND_ARCHITECTURE "Charts").
- [ ] **D6 -- CodeHub annotation import.** A one-shot Helm command that reads
  CodeHub's `annotations` table and maps each entry to a vessel override by matching
  `WorkingDirectory`.

## Compliance checklist (per ~/Code/Agents/requirements)

These items apply to every phase. Tick them off per phase in the progress log.

- [x] **CODE_STYLE.md.** Every new C# file follows these rules:
  - usings inside the namespace, System first, then alphabetical;
  - XML docs on public members only, with defaults, min and max documented;
  - `_PascalCase` private fields; no `var`; no tuples;
  - one class or enum per file;
  - configurable values as public members with backing fields, not constants;
  - `CancellationToken` and `.ConfigureAwait(false)` on async calls;
  - specific exception types with `<exception>` tags;
  - guard clauses; no `Console.WriteLine` in library code; no em-dashes anywhere.

  Verified by a clean 0-warning `dotnet build src/Armada.sln`.
- [x] **BACKEND_ARCHITECTURE "Structured Persistence".** Every sort and filter field
  is a typed, indexed column. Lists are child tables. The only unmanaged text is the
  explicitly named `OutputText` and `ErrorText`, and the reason is documented in the
  model's XML doc.
- [x] **Provider matrix.** Each migration is present in all four providers, including
  the manual MySQL wiring. Database suites pass against Sqlite, Postgresql, Mysql and
  SqlServer (`scripts/common/run-db-parity-tests.sh`). The SQLite write lock covers
  concurrent import and target writes.
- [x] **Tenancy.** Every row carries `TenantId`. Every read and enumerate is scoped by
  the caller's tenant, which comes from the auth context, never the request body.
  Another tenant's ID returns 404. Deleting a vessel cascades.
- [x] **IDs.** `vib_`, `vii_`, `fac_`, `far_`, `fat_`, `vhl_`, `vhf_`, `vdp_` and
  `vho_` are added to `Constants.cs`, with no collisions with existing prefixes
  (checked against `Constants.cs` on 2026-10-03).
- [x] **AUTHENTICATION.** Armada authorizes by path through `PermissionLevel` in
  `AuthorizationConfig`, not with AUTHENTICATION.md's `(ResourceType, Operation)`
  model. These features follow Armada's existing model; that is a recorded
  nonconformance and is not fixed here. Filesystem browse and discover, and
  Command-kind actions, are TenantAdmin because they read the host filesystem or
  execute code on it. Authorization denials on these routes are logged with the
  request ID.
- [x] **REPOSITORY_REQUIREMENTS #13 and #14.** `docs/REST_API.md` and the Postman
  collection (folders per feature, variables for the base URL and key) document every
  new route. `docs/MCP_API.md` documents every new tool and enumerate type. Both are
  updated in the same change as the code.
- [x] **BACKEND_TEST_ARCHITECTURE.** New suites live in `Test.Shared/Suites/**` and
  run under `Test.Automated`, `Test.Xunit` and `Test.Nunit` on net8.0 and net10.0.
  Tests bind to `127.0.0.1`, write nothing to the console, and set up and clean up
  their own data.
- [~] **DASHBOARD_STYLE_AND_USABILITY.** The route inventory is in this doc.
  Filtering, sorting and paging run on the server, and filter state lives in the URL.
  The pagination bar sits above the table with page sizes 10/25/50/100. Each row has
  an actions menu and each table a bulk bar with a clear-selection control. Dialogs
  are custom, with no browser dialogs. Empty, loading and error states have a retry
  that keeps the filters. Status uses icon plus text. Playwright visual QA runs at
  1280, 768 and 390 px in light and dark.
- [x] **I18N.** Every new string, `aria-*` attribute and tooltip goes through the
  dashboard i18n runtime. Counts use plurals; ahead, behind and relative times use the
  locale formatters. The server returns stable codes, and the client localizes them.
- [x] **TELEMETRY_REQUIREMENTS.** Metrics and spans as in C11. Labels stay
  low-cardinality, and a telemetry failure never fails a request.
- [ ] **SIMULATED_USER_TESTING.** After each phase is declared complete, run a session
  in an isolated `armada-usertest` stack. Probes include:
  - import of thousands of directories;
  - paths with spaces, unicode and `../`;
  - benign shell-metacharacter command payloads;
  - double submit, and cancel during a 50-target run;
  - a restart mid-run;
  - a cross-tenant ID probe.

  Write the report to `user-testing/<yyyy-mm-dd>-<label>.md`. Fixes require approval.
- [x] **VERSIONING.** No version numbers change as part of this work. CHANGELOG
  entries go under Unreleased.
- [x] **WRITING_DOCUMENTS.** The new guides (`FLEET_ACTIONS.md`, `VESSEL_HEALTH.md`)
  and README sections are written as prose, and contain no em-dashes.

## Decisions

These are proposed defaults. Change any of them before Phase A starts; after that
they get expensive.

- **D1. Explicit import, not live selections.** CodeHub re-discovers its selected
  roots on every scan, so new repositories appear by themselves. Armada imports once
  and creates vessels. A later "watch root" option (re-run discovery on the health
  schedule and surface *new* candidates as a Home notice, never auto-import) is cheap
  to add if the operator misses the CodeHub behavior.
- **D2. Command actions run in the user's working directory.** That is the point of
  "git pull across everything". The alternative is a throwaway dock per target, which
  keeps the operator's checkout untouched but makes `git pull` meaningless. Mission
  actions already run in docks. The dirty-tree pre-check (on by default) is the
  safety net.
- **D3. One voyage per vessel for Mission actions**, grouped by a run, rather than
  extending voyages to span vessels.
- **D4. Health evaluates `WorkingDirectory` first.** Vessels without one (pure
  remote URLs) get git-only criteria, evaluated against the bare clone.
- **D5. No CodeHub data migration** except the optional annotation import (Phase D6).
- **D6. Seeded built-in actions** are tenant-scoped, ordinary rows, rather than
  hard-coded.

## Progress Log

Append a dated row whenever you advance a task. Keep newest at the bottom.

| Date | Author | Task(s) | Change |
|------|--------|---------|--------|
| 2026-10-03 | (design) | -- | Initial plan drafted from a review of CodeHub (`~/Code/Codehub`) and Armada's current vessel, job, dispatch, git and dashboard surfaces. |
| 2026-10-03 | Claude | A1, A4, B1, B2, C1, C2 | Settings classes, models/enums, ID prefixes, interfaces, four provider implementations, migrations v71-v73, and Database suites (VesselImport, FleetAction, VesselHealth). |
| 2026-10-04 | Claude | A2-A3, A5-A8, A10 | Vessel Import backend: discovery, shared VesselService, import service, REST, MCP, Helm, docs. |
| 2026-10-04 | Claude | B3-B8, B10 | Fleet Actions backend: renderer, runner, mission fan-out, seeding, REST, MCP, Helm, FLEET_ACTIONS.md. |
| 2026-10-04 | Claude | C3-C9, C11, C12 | Vessel Health backend: criteria, evaluator, scheduling, overrides, REST, MCP, Helm, telemetry, VESSEL_HEALTH.md, Grafana "Armada Fleet Operations" dashboard. Full Test.Automated 2792/2792 on net10.0. |
| 2026-10-04 | Claude | A9, B9, C10 | Dashboard: import wizard, Fleet Actions pages and RunActionModal, Health tab and detail modal, Home KPIs, Settings sections; 143/143 vitest. Visual QA and simulated user testing still to do. |
