# Vessel Health

Vessel health answers one question for every repository Armada manages: does it need attention? Armada grades each vessel against ten criteria, keeps one current row per vessel, and rolls the grades up into a single overall status. The point is to find the rotting repositories (stale dependencies, no tests, branches nobody cleaned up, a checkout that drifted far behind its default branch) and then fix them with the same captains and voyages Armada already runs.

The grades are deliberately conservative about what they claim. A check that could not run is reported as Unknown, never as healthy. A failed `dotnet list`, a missing `npm`, a timeout, or output Armada could not parse all show up as Unknown with a reason code, because a green cell you cannot trust is worse than no cell at all.

For the routes, request and response shapes, and the full list of detail codes, see the Vessel Health section of [REST_API.md](REST_API.md#vessel-health). MCP tools are described in [MCP_API.md](MCP_API.md#vessel_health).

## Statuses

Every criterion, and the vessel overall, carries one of five statuses.

| Status | Meaning |
|---|---|
| `Pass` | Healthy by the configured thresholds. |
| `Warn` | Worth a look soon. |
| `Fail` | Needs attention. |
| `NotApplicable` | The criterion does not apply, for example a working-tree check on a vessel that only has a bare clone. |
| `Unknown` | The criterion could not be evaluated, or the vessel has never been evaluated. |

Each finding also has a stable detail code and two numbers, so the dashboard can say "23 outdated packages (5 major)" in the viewer's language instead of showing a sentence the server wrote in English.

## Which checkout is graded

Armada evaluates the vessel's working directory when it is set, exists on disk, and is a git checkout, because that is the copy a person is actually working in. When there is no usable working directory, Armada falls back to the bare clone at `LocalPath`. A bare clone has no working tree, so the working-tree, test-infrastructure, dependency, and vulnerability criteria report NotApplicable there (detail code `BareRepository`). Git criteria, CI, license, and readme detection still work against a bare clone because they read the tracked files at HEAD. When the Admiral has neither (split mode, with the Admiral in Docker and the code on a developer's machine), Armada evaluates the vessel's checkout on a connected Harbor that can serve it (see [HARBOR.md](HARBOR.md#checkouts-outside-missions)): git and the dependency tools run on that Harbor, and the file inventory comes from git's tracked files there.

When neither path is usable, the row records `ErrorCode = RepositoryUnavailable` and every criterion that needs a repository is Unknown. The two criteria that only read Armada's own database, readiness and mission outcomes, still run.

Before grading divergence Armada runs `git fetch --all --prune` (controlled by `FetchBeforeEvaluate`). In a user's working directory the fetch never rewrites remote configuration. If the fetch fails, divergence is Unknown with `FetchFailed` rather than a confident number computed from stale remote refs.

## Criteria

### GitDivergence

Counts commits between HEAD and `origin/<DefaultBranch>` (falling back to the local default branch when there is no remote copy), and between HEAD and its upstream (`@{u}`) when one is configured. Being ahead is normal for a feature branch and still passes. Being behind by `BehindWarn` commits or more warns, and by `BehindFail` or more fails. Being both ahead and behind, against the default branch or against the upstream, fails outright because someone has to reconcile two lines of history.

### WorkingTree

Reads `git status --porcelain`. A clean tree passes. Untracked files alone warn, since they are often build output or scratch files. Modified tracked files fail, because uncommitted edits are the thing most likely to be lost or to block an automated `git pull`.

### Branches

Lists local branches and splits them into two groups. A stale branch is one whose tip commit is older than `StaleBranchDays`, excluding the default branch and the branch currently checked out. Leftover `armada/*` branches, created by captains and never cleaned up, are counted separately. The criterion passes while stale branches stay below `StaleBranchWarn` and there are no leftover captain branches, warns when either appears, and fails once stale branches reach `StaleBranchFail`.

### CommitRecency

Records when the last commit happened. It is informational, always Pass when a commit exists, and excluded from the overall rollup by default. Use it to sort and filter the table, not to grade.

### Dependencies

Runs `dotnet list <target> package --outdated --format json` for .NET and `npm outdated --json` for npm. For .NET, the targets are the solution files when the repository has between one and five of them; otherwise every non-test project file. npm runs only where a `package.json` sits next to a `package-lock.json` or `npm-shrinkwrap.json`. Directories in the import exclude list (`bin`, `obj`, `node_modules`, and so on) are never searched.

Drift compares major, minor, and patch numbers after dropping any prerelease or build suffix, and the first component that moved decides. Nothing outdated passes, minor or patch drift warns, and any major drift fails. If any tool run fails, the criterion is Unknown with one of `ToolMissing`, `RestoreRequired`, `Timeout`, `ParseError`, or `ToolFailed`, even when other targets succeeded. Packages found by the targets that did succeed are still listed.

### Vulnerabilities

Runs `dotnet list <target> package --vulnerable --format json` and `npm audit --json` against the same targets. No vulnerable packages passes, a highest severity of Low or Moderate warns, and High or Critical fails. Failures follow the same Unknown rules as Dependencies.

### TestInfrastructure

Looks for tests per ecosystem, so it works for repositories that follow nobody's particular conventions. A .NET project counts as a test project when it references `Microsoft.NET.Test.Sdk`, `xunit*`, `NUnit*`, `MSTest*`, or `Touchstone*`, or sets `IsTestProject`. A Node package counts when `jest`, `vitest`, `mocha`, or `playwright` appears in its dependencies, or when its `test` script is anything other than npm's "no test specified" placeholder. Python counts `pytest.ini`, `[tool.pytest]` in `pyproject.toml`, `[tool:pytest]` in `setup.cfg`, `tox.ini`, `conftest.py`, or a `tests` directory. Go counts any `*_test.go` file, and Rust counts a `tests` directory next to `Cargo.toml` or `#[cfg(test)]` in a source file.

Finding tests is only half of it. Armada also looks at the vessel's latest completed test check run (UnitTest, IntegrationTest, E2ETest, or SmokeTest). Tests found and the last run passed is Pass. Tests found but no passing latest run, whether because nothing ran or the run failed, is Warn. A recognized project with no tests at all is Fail, and a repository with no recognizable project is NotApplicable. The check-run cross-check is something only Armada can do: a repository whose test suite exists but last failed is not healthy, however tidy its project layout looks.

### ContinuousIntegration

Passes when the repository has a GitHub Actions workflow, `azure-pipelines.yml`, `.gitlab-ci.yml`, or a `Jenkinsfile`, and fails otherwise. It is excluded from the overall rollup by default because plenty of healthy personal repositories have no CI. The same pass also records whether a root license file and a root readme exist (`HasLicense`, `HasReadme`).

### ArmadaReadiness

Wraps Armada's vessel readiness check, the same one behind the vessel readiness view. No issues passes, warnings warn, and any error fails.

### MissionOutcomes

Counts Failed and LandingFailed missions on the vessel that finished within the last `MissionWindowDays`. Zero passes, `MissionFailureWarn` or more warns, and `MissionFailureFail` or more fails. A repository where captains keep failing is usually telling you something about its build, its tests, or its instructions.

## The overall status

The overall status is the worst grade among the criteria listed in `ScoredCriteria`, after overrides are applied: Fail outranks Warn, which outranks Pass. Unknown and NotApplicable are neutral. They never count as a pass and they never fail a vessel, so a vessel with one Unknown dependency check and everything else green is Pass overall, while its dependency column plainly shows Unknown. A vessel whose scored criteria are all Unknown is Unknown, not Pass. If every scored criterion is NotApplicable, the overall status is NotApplicable.

## Overrides

Sometimes a grade is correct and still not actionable: a dependency pinned on purpose, a repository that is archived, a test suite that is known to be flaky. An override sets a manual status and an optional note for one criterion, or for Overall. The status columns on the health row are recomputed immediately from the stored findings, without re-running anything, and the raw findings keep the evaluated result. Overrides apply everywhere the status is read, including the summary counts, so the KPI tiles always agree with the table. An Overall override replaces the computed overall status outright.

## Freshness and scheduling

Git checks are cheap and run on every evaluation. Dependency and vulnerability checks are slow (a `dotnet list` can take a minute), so Armada hashes every manifest and lock file and skips those two checks while the hash is unchanged and the previous results are younger than `DependencyMaxAgeHours`. The age limit matters: new advisories are published against lockfiles that never change, and a hash-only rule would freeze them out forever. A failed dependency check does not count as fresh, so the next evaluation retries it. Manual evaluations force the dependency checks unless the request says otherwise.

Every `IntervalMinutes`, the Admiral starts an evaluation of every active vessel in each tenant. Evaluations run as ordinary background jobs (kind `Report`), visible and cancellable through the jobs API, with at most `MaxConcurrency` vessels evaluated at once and each vessel's failure isolated from the rest. Only one evaluation job runs per tenant; asking for another while one is running returns the running job's ID with HTTP 409. The scheduler measures the interval from the last evaluation job recorded in the database, so restarting the Admiral does not trigger a fresh sweep.

## Settings

All of these live under `RepositoryHealth` in `settings.json`, are returned and accepted by `GET` and `PUT /api/v1/settings`, and apply live. Out-of-range values are clamped.

| Setting | Default | Range | Effect |
|---|---|---|---|
| `IntervalMinutes` | 360 | 0 to 10080 | Minutes between scheduled evaluations. 0 turns the schedule off; manual evaluation still works. |
| `MaxConcurrency` | 4 | 1 to 32 | Vessels evaluated at once within a job. |
| `FetchBeforeEvaluate` | true | | Fetch before grading divergence. |
| `DependencyMaxAgeHours` | 24 | 1 to 720 | Maximum age of dependency results before they are refreshed with unchanged manifests. |
| `DependencyCommandTimeoutSeconds` | 120 | 10 to 900 | Timeout for each `dotnet` or `npm` invocation. A timeout grades Unknown. |
| `StaleBranchDays` | 90 | 1 to 3650 | Age at which a branch counts as stale. |
| `MissionWindowDays` | 7 | 1 to 90 | Window for counting failed missions. |
| `ScoredCriteria` | every criterion except ContinuousIntegration and CommitRecency | criterion names | Criteria that feed the overall status. |
| `Thresholds.BehindWarn` | 1 | 1 to 100000 | Commits behind that warn. |
| `Thresholds.BehindFail` | 21 | 1 to 100000 | Commits behind that fail. |
| `Thresholds.StaleBranchWarn` | 4 | 1 to 10000 | Stale branches that warn. |
| `Thresholds.StaleBranchFail` | 11 | 1 to 10000 | Stale branches that fail. |
| `Thresholds.MissionFailureWarn` | 1 | 1 to 1000 | Recent failed missions that warn. |
| `Thresholds.MissionFailureFail` | 3 | 1 to 1000 | Recent failed missions that fail. |

Directory names skipped while scanning a repository come from `Import.ExcludedDirectoryNames`, the same list vessel import uses.

## Using it

In the dashboard, the Health tab of the Vessels page (`/vessels/health`) shows the table with status count tiles, filters, per-row Re-evaluate and Override actions, and an Evaluate all button (tenant admins); the TUI has the same screen at the same route. From the command line, `armada health` prints the table sorted worst first. Add `--status Fail` to see only failing vessels, `--fleet <id>` to narrow to one fleet, and `--evaluate` to start an evaluation first. From an MCP client, `enumerate` with `entityType` set to `vessel_health` lists rows, `vessel_health` shows one vessel's findings, `evaluate_vessel_health` starts a job, and `set_vessel_health_override` records a manual status.

Evaluation emits two instruments on the Armada meter: the counter `armada.health.evaluations` (exported to Prometheus as `armada_health_evaluations_total`), labeled by `outcome` (Succeeded, Failed, Cancelled), and the histogram `armada.health.criterion_duration_seconds` (`armada_health_criterion_duration_seconds`), labeled by `criterion`. Each job produces a `vessel_health.job` span with a `vessel_health.evaluate_vessel` span per vessel and `stage:<Criterion>` spans beneath it. Vessel identifiers appear only as span attributes, never as metric labels.
