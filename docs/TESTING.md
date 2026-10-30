# Testing

## Run All Tests

All commands run from the repository root. Every test case is a runner-agnostic descriptor in
`src/Test.Shared`, executed either by the console runner or the xUnit/NUnit adapters. Both `net8.0`
and `net10.0` are supported; pass `--framework net8.0` or `--framework net10.0`.

```bash
# Console runner -- all suites
dotnet run --project src/Test.Automated --framework net10.0

# Same suites via the xUnit / NUnit adapters (VSTest)
dotnet test src/Test.Xunit --framework net10.0
dotnet test src/Test.Nunit --framework net10.0

# Run a targeted subset by suite-id prefix
ARMADA_TEST_SUITES="Database,Services.MergeQueue" dotnet run --project src/Test.Automated --framework net10.0

# React dashboard build and smoke tests (node_modules is not committed)
cd src/Armada.Dashboard
npm ci
npm run build
npm run test:run
cd ../..
```

For reference, the 1.0.0 full run on `net10.0` is 3,896 tests (3,887 passed, 9 skipped) and the dashboard Vitest
suite is 348 tests.

Database-backed tests run against SQLite in-process by default. The PostgreSQL, MySQL, and SQL Server
drivers are exercised by the same descriptors through `--db-*` (see [Multi-Database Testing](#multi-database-testing)).

## Persona priorities (1.1.0)

The complete console suites pass on net8.0 and net10.0: 4,494 passed, zero failures, and nine provider/baseline-dependent skips out of 4,503 descriptors. Dashboard Vitest passes 751 tests; mobile Jest passes 1,201. The priority/routing/persistence subset also runs unchanged against SQLite, PostgreSQL, MySQL, and SQL Server, with 52 passing descriptors per provider, including the tenant-fence regression. The xUnit/NUnit adapters execute the same routing descriptors.

`Services.PersonaCaptainPriority` covers ordered matching, model/endpoint/tier constraints, validation, availability/exhaustion policies, and relational round-trips. `Services.CaptainRouting` covers assignment, legacy and explicit override precedence, persona/tier fences, quarantine, ownership, and tenant isolation. `Services.PipelineDispatch` covers Architect-generated Workers; `E2E.PersonaPriorityApi` and `E2E.McpTool` cover configuration round-trips, rejected updates, and clearing. Client tests cover structured editing, ordering, duplication, localization, and default-picker behavior.

```bash
# Match the deterministic Unicode rendering expected by the terminal tests when your shell sets NO_COLOR.
env -u NO_COLOR dotnet run --project src/Test.Automated --framework net10.0

# Focused routing and configuration checks
env -u NO_COLOR dotnet run --project src/Test.Automated --framework net10.0 -- \
  --suites Services.PersonaCaptainPriority,Services.CaptainRouting,Services.PipelineDispatch,E2E.PersonaPriorityApi

# Upgrade the current committed baseline to this checkout on each database provider.
env -u NO_COLOR bash scripts/common/run-upgrade-test.sh --from-ref HEAD --providers all --framework net10.0
```

The upgrade command is meaningful while HEAD is the older baseline and the feature changes remain in the working tree. After committing the feature, pass the prior release commit or tag explicitly. Migration 83 preserves legacy persona defaults and creates empty priorities with FallThrough/Wait defaults; the upgrade descriptors also save and reread a new hierarchy after migration.

## Test Projects

| Project | What It Covers |
|---------|----------------|
| `src/Test.Shared` | The shared descriptor library: every test case (3,839 on `net10.0`: models, database drivers, services, runtimes, the client contract, the terminal UI, the upgrade path, and end-to-end REST/MCP/WebSocket lifecycle), plus the test infrastructure (fixtures, stubs, `TestDatabaseHelper`, `TestTemp`). Depends on `Touchstone.Core`. |
| `src/Test.Automated` | Console runner (`Touchstone.Cli`) that executes every discovered suite and prints per-test results; the primary way to run the full suite. |
| `src/Test.Xunit` | Runs the shared descriptors through the xUnit adapter (VSTest / IDE integration). |
| `src/Test.Nunit` | Runs the shared descriptors through the NUnit adapter (VSTest / IDE integration). |
| `Armada.Dashboard` Vitest suite | React component and page smoke tests (`npm run test:run`). |

## How It Works

Tests are written once as runner-agnostic [Touchstone](https://www.nuget.org/packages/Touchstone.Core) descriptors
and run unchanged by the console runner and the xUnit and NUnit adapters. The React dashboard is the exception: it
uses `vitest` plus Testing Library for browser-surface smoke and interaction tests.

- **Suites are discovered by reflection.** `ArmadaTestSuites` finds every non-abstract class in `Test.Shared` that
  implements `IArmadaTestSuite`, constructs it through its public parameterless constructor, and calls `Build()`.
  There is no manual registry, so a new suite cannot be left out of a run.
- **`Build()` returns a `TestSuiteDescriptor`** (a suite id, a display name, the cases, and optional before/after
  hooks). Each case is a `TestCaseDescriptor` with the suite id, a case id, a display name, and an async body; a case
  fails by throwing (use the helpers in `Asserts`).
- **Suites run in suite-id order.**

### Suite ids

A suite id is a dotted name whose first segment is the area and whose folder under `src/Test.Shared/Suites/` matches:
`Client.*`, `Database.*`, `E2E.*`, `Models.*`, `Runtimes.*`, `Services.*`, `Tui.*`, and `Upgrade.*` (for example
`Database.VesselDatabase`, `Services.MergeQueue`, `E2E.ApiContract`, `Models.DashboardCodeList`). Keep ids unique and
name new suites after the class they cover, so a prefix filter selects a meaningful group.

### Selecting suites

`ARMADA_TEST_SUITES` takes a comma-separated list of suite-id prefixes (case-insensitive) and limits the run to the
suites whose id starts with one of them. It is read when the suites are built, so it works the same for the console
runner and the xUnit and NUnit adapters. The console runner's `--suites <prefixes>` sets the same variable.

```bash
ARMADA_TEST_SUITES=E2E dotnet run --project src/Test.Automated --framework net10.0
dotnet run --project src/Test.Automated --framework net10.0 -- --suites Database,Services.MergeQueue
```

## Command-Line Options

`Test.Automated` accepts these arguments after `--`:

| Argument | Description |
|----------|-------------|
| `--suites <prefixes>` | Comma-separated suite-id prefixes; sets `ARMADA_TEST_SUITES` |
| `--results <path>` | Write the run results to a file |
| `--db-type <type>` | Database provider: `sqlite` (default), `postgresql`, `mysql`, or `sqlserver`; sets `ARMADA_TEST_DB_TYPE` |
| `--db-host <host>` | Server hostname (default `127.0.0.1`); sets `ARMADA_TEST_DB_HOST` |
| `--db-port <port>` | Server port (default: the provider's); sets `ARMADA_TEST_DB_PORT` |
| `--db-user <user>` | Server username; sets `ARMADA_TEST_DB_USER` |
| `--db-pass <password>` | Server password; sets `ARMADA_TEST_DB_PASS` |
| `--db-name <name>` | Base database name (default `armada_test`); each test derives a uniquely suffixed database from it; sets `ARMADA_TEST_DB_NAME` |
| `--generate-api-surface <dir>` | Run no tests: boot a throwaway Admiral and rewrite `api-surface-1.0.json` and `API_SURFACE_1.0.md` in `<dir>` (used by `scripts/common/generate-api-surface.sh`) |

The xUnit and NUnit adapters take no arguments; set the `ARMADA_TEST_*` environment variables instead.

```bash
# Default: SQLite, no connection arguments needed
dotnet run --project src/Test.Automated --framework net10.0

# The Database suites against PostgreSQL
dotnet run --project src/Test.Automated --framework net10.0 -- --suites Database \
  --db-type postgresql --db-host localhost --db-port 5432 --db-user postgres --db-pass secret --db-name armada_test
```

## Multi-Database Testing

Armada supports four database backends: SQLite, PostgreSQL, SQL Server, and MySQL. Every DB-backed test reads its
provider from `TestDatabaseConfig` (the `ARMADA_TEST_DB_*` variables), so the same descriptors run against any of
them. `scripts/common/run-db-parity-tests.sh` runs the `Database` suites against all four: SQLite in process and
each server provider in a throwaway Docker container on a random host port that is removed afterward.

```bash
scripts/common/run-db-parity-tests.sh                       # all four providers
scripts/common/run-db-parity-tests.sh --providers sqlite,postgresql
scripts/common/run-db-parity-tests.sh --framework net8.0 --no-build
```

`--providers` takes any comma-separated subset of `sqlite`, `postgresql` (or `postgres`, `pg`), `mysql` (or
`mariadb`), and `sqlserver` (or `mssql`). `--framework` defaults to `net10.0`; `--no-build` skips the initial build.
The container images can be overridden with `ARMADA_POSTGRES_IMAGE` (default `postgres:17-alpine`),
`ARMADA_MYSQL_IMAGE` (default `mysql:8.4`), and `ARMADA_SQLSERVER_IMAGE` (default
`mcr.microsoft.com/mssql/server:2022-latest`); `ARMADA_SQLSERVER_SA_PASSWORD` sets the SQL Server SA password. The
script requires Docker and exits non-zero if any provider has a failing test. A provider whose container cannot start
(`docker run` fails, for example a missing image or a stopped Docker daemon) or exits before it is ready is reported
as failed at once, with Docker's error or the container's last log lines, instead of after the readiness wait (about
180 seconds). On Windows,
`scripts\windows\run-db-parity-tests.bat` forwards to the same script (it needs `bash` from Git for Windows or WSL on `PATH`).

Run the full suite at least against SQLite, and run the parity script for any change that touches the schema or a
database driver.

## Test Data Isolation

Each suite creates its own data, asserts only on that data, and cleans up after itself:
- Suites never assume the database is empty
- Suites never assert exact total counts across entity types
- Suites can run in any order without affecting each other

### TestTemp sandbox

`TestTemp` owns every temp file and directory the tests create. All of them live under the system temp directory with
the `armada_` prefix. When the test assembly loads (a module initializer, so it applies to all three runners) it:

- sweeps `armada_*` entries older than two hours left by earlier runs that were killed before cleanup (fresher
  entries are kept, so a concurrent run is not disturbed);
- arms a process-exit hook that deletes everything created through `TestTemp.NewDirectory`, `TestTemp.NewFile`, or
  registered with `TestTemp.Track`;
- sandboxes the user profile: `ARMADA_DATA_DIR`, `ARMADA_TUI_PREFERENCES`, and `ARMADA_TUI_CREDENTIALS` point into a
  per-run directory and `ARMADA_TUI_CREDENTIAL_STORE` is `file`, so tests never read or write the real `~/.armada`
  or the OS keychain (the run fails fast if the profile default was already resolved);
- pins the git line-ending settings for every git process the run starts (see below).

Create temp paths only through `TestTemp`, never with `Path.GetTempPath()` directly.

## Host Independence

Results must not depend on the machine running the tests:
- No real agent CLIs. The in-process test servers (`E2EServerFixture`, `SecurityTestServer`, `InProcessArmadaServer`) replace every CLI agent runtime (Claude Code, Codex, Gemini, Cursor, Mux, OpenCode, pi) with `StubAgentProcesses`, which launches a long-running `sleep` (`ping` on Windows) the test owns. Dispatch behaves the same whether or not an agent CLI is installed, no model is called, and tests never start a real agent. Other stubs (`StubCaptainRuntime`, `StubAskTurnRunner`, `StubGitService`, `StubHostCommandExecutor`, `StubHttpHandler`) stand in for model calls, git, host commands, and outbound HTTP where a suite needs them.
- No user configuration and no desktop side effects. The profile sandbox above keeps tests away from `~/.armada`, the keychain, and the real settings; tests do not open browsers, notifications, or other desktop surfaces.
- Every git process the run starts (test helpers and product code) gets `core.autocrlf=false` and `core.eol=lf` through `GIT_CONFIG_COUNT` (`TestGitEnvironment`), so a host or runner setting such as `core.autocrlf=true` cannot change checked-out file contents.
- Wait for conditions, not durations: poll for the state the test needs (a job finished, a request recorded) with a deadline (`MonotonicDeadline`, `JobWait`), or wait on a signal from the stub (for example `FakeHealthCriterion.FirstEvaluationStarted`). No fixed sleeps and no absolute performance thresholds.
- Ports come from `TestPorts` (random loopback ports in 20000-31999, never reused within a process, with 21000-21099 and 25000-25099 left for manually started servers), never hard-coded, so parallel runs and a running Admiral do not collide.

## API Contract Test

`E2E.ApiContract` compares the live public surface (REST, MCP, WebSocket, CLI, settings) to the frozen baseline in
`docs/api-surface-1.0.json` and fails on removals and incompatible changes, printing a diff. After an intended addition,
regenerate the baseline and its Markdown view and commit them with the change:

```bash
dotnet run --project src/Test.Automated --framework net10.0 -- --suites E2E.ApiContract
scripts/common/generate-api-surface.sh
```

See [COMPATIBILITY.md](COMPATIBILITY.md).

## Adding Tests

1. Find the suite for the area in `src/Test.Shared/Suites/<Area>/`, or add a class that implements `IArmadaTestSuite`
   with a public parameterless constructor and a suite id that starts with the area (for example `Services.MyFeature`).
   Reflection discovery picks it up; there is nothing to register.
2. Add a `TestCaseDescriptor` to the list `Build()` returns. Fail by throwing; use the helpers in `Asserts`.
3. Track any created entities and temp paths (`TestTemp`) and clean them up in the case or the suite's after hook.
4. Run just your suite with `ARMADA_TEST_SUITES=<suite id> dotnet run --project src/Test.Automated --framework net10.0`,
   then the full suite.

## Persona prompt and handoff evaluations

`python3 -m unittest discover -s scripts/common/tests` validates additive deployment safety and the paired evaluation harness without contacting a deployment or model. Backend prompt suites cover default seeding/upgrades, customization preservation, rendering/reset, and Architect fan-out payload retention.

The optional live runner is `scripts/common/evaluate-prompt-handoffs.py`. Use `--base <backend-url> --captain-id <id> --output <external-report.json>` with interactive credentials, or `--local-codex --local-model <model> --output <external-report.json>` for read-only ephemeral local runs with shell/web disabled. Captain chat sends synthetic offline tasks and never dispatches missions. Baseline/improved pairs use empty history, identical task and captain, and unchanged runtime decoding settings; a changed reported model invalidates the pair. The Architect case also evaluates a fixed Worker prompt receiving only the parsed upstream description. The `blackbox` case (`ask.system`) pairs a short baseline with the Ask black box section (`AskBlackBoxGuidance.Section`; `Services.AskBlackBox` checks the case carries the current text) and adds a grounding score: the answer headings, a labeled opinion, user and Armada changes separated, and any file, commit or mission the reply names that the supplied entry does not hold.

Review unsupported claims, acceptance correctness, residual risks, and tool-call metrics manually. Lexical evidence coverage is a screening metric and cannot certify correctness or smaller-model suitability. Runtime defaults and synthetic tool restrictions are documented limitations. Keep report/secret files outside the repository unless their contents have been reviewed and sanitized.

Token heatmap checks: `Services.TokenUsageSynthetic`, `E2E.TokenUsageSyntheticApi`, `Database.TokenUsage`, and `Tui.Activity.Tokens` cover import/replay/auth/scoping, daily queries, large counts, and short-terminal navigation. Run database parity across all four providers. Dashboard `tokenHeatmap.test.ts` and `TokenActivityHeatmap.test.tsx` cover UTC aggregation and calendar interaction; mobile `w4Activity.test.tsx` checks daily data and selected days. Generator tests live in `scripts/common/tests/test_seed_token_usage.py`. See [execution checklist](../archive/TOKEN_USAGE_HEATMAP.md).

`Services.SyntheticActivity` verifies provider-neutral historical generation, all chart sources, recent bucket coverage, ownership, replay and profile bounds. `E2E.SyntheticActivity` verifies global-admin access, validation, replay and encoded mission/API history windows. Run both .NET targets and four-provider parity before publishing the synthetic activity generator.
