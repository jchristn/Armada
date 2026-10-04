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

# React dashboard build and smoke tests
cd src/Armada.Dashboard
npm run build
npm run test:run
cd ../..
```

Database-driver coverage runs against SQLite in-process by default. The PostgreSQL, MySQL, and SQL
Server drivers are exercised by the same descriptors when those backends are available.

## Test Projects

| Project | What It Covers |
|---------|----------------|
| `src/Test.Shared` | The shared descriptor library: all ~2,200 test cases (models, database drivers, services, runtimes, and end-to-end REST/MCP/WebSocket lifecycle), plus the test infrastructure (fixtures, stubs, `TestDatabaseHelper`). Depends only on `Touchstone.Core`. |
| `src/Test.Automated` | Console/CLI runner (Touchstone.Cli) that executes every suite and prints per-test results; the primary way to run the full suite. |
| `src/Test.Xunit` | Runs the shared descriptors through the xUnit adapter (VSTest / IDE integration). |
| `src/Test.Nunit` | Runs the shared descriptors through the NUnit adapter (VSTest / IDE integration). |
| `Armada.Dashboard` Vitest suite | React component and page smoke tests. |

## How It Works

No test framework (xUnit, NUnit, MSTest) is used. Each test project is a console app that runs tests sequentially and reports results.

The React dashboard is the exception: it uses `vitest` plus Testing Library for browser-surface smoke tests and interaction tests.

- `TestSuite` — abstract base class in `Test.Shared`. Each suite groups related tests, provides assertion helpers, and cleans up its own test data.
- `TestRunner` — orchestrates suites, prints colored results, generates summary with failed test details.
- `RunTest(name, action)` — wraps each test with a Stopwatch. Prints PASS/FAIL with elapsed milliseconds. Catches exceptions and records failure details.

## Output

```
================================================================================
ARMADA AUTOMATED TEST SUITE
================================================================================

--- Fleet API Tests ---
  PASS  Create Fleet (12ms)
  PASS  Read Fleet (8ms)
  PASS  Update Fleet (15ms)
  PASS  Delete Fleet (6ms)
  ...

--- Captain API Tests ---
  PASS  Create Captain (14ms)
  ...

================================================================================
TEST SUMMARY
================================================================================
Total: 781  Passed: 781  Failed: 0  Runtime: 42150ms

================================================================================
RESULT: PASS
================================================================================
```

## Command-Line Options

```bash
# Run with default settings (temporary SQLite database, cleaned up after execution)
dotnet run --project src/Test.Automated --framework net10.0

# Keep test database after run (for debugging)
dotnet run --project src/Test.Automated --framework net10.0 -- --no-cleanup

# Run only one automated suite by name fragment
dotnet run --project src/Test.Automated --framework net10.0 -- --suite "Request History"

# Focus on delivery and real-time surfaces
dotnet run --project src/Test.Automated --framework net10.0 -- --suite "MCP"
dotnet run --project src/Test.Automated --framework net10.0 -- --suite "WebSocket"
dotnet run --project src/Test.Automated --framework net10.0 -- --suite "Release"
dotnet run --project src/Test.Automated --framework net10.0 -- --suite "Objectives"
dotnet run --project src/Test.Automated --framework net10.0 -- --suite "GitHub Integration"
dotnet run --project src/Test.Automated --framework net10.0 -- --suite "Environment"
dotnet run --project src/Test.Automated --framework net10.0 -- --suite "Deployment"
dotnet run --project src/Test.Automated --framework net10.0 -- --suite "Checks"

# Test against PostgreSQL instead of default temp SQLite
dotnet run --project src/Test.Automated --framework net10.0 -- --type postgresql -h localhost -u postgres -w secret -d armada_test

# Test against SQL Server
dotnet run --project src/Test.Automated --framework net10.0 -- --type sqlserver -h localhost --port 1433 -u sa -w secret -d armada_test

# Test against MySQL
dotnet run --project src/Test.Automated --framework net10.0 -- --type mysql -h localhost --port 3306 -u root -w secret -d armada_test
```

### Database Arguments

| Argument | Short | Description | Default |
|----------|-------|-------------|---------|
| `--type` | | Database backend: `sqlite`, `postgresql`, `sqlserver`, `mysql` | Temporary SQLite |
| `--filename` | | SQLite database file path | Temp file (auto-cleaned) |
| `--hostname` | `-h` | Database server hostname | `localhost` |
| `--port` | | Database server port | Backend default |
| `--username` | `-u` | Database username | — |
| `--password` | `-w` | Database password | — |
| `--database` | `-d` | Database name | — |
| `--schema` | | Database schema | Backend default |

If no `--type` is provided, both Test.Automated and Test.Database default to a temporary SQLite database that is automatically cleaned up after execution.

`Test.Automated` also supports `--suite <name>` to run only suites whose display name or type name contains the supplied text.

## Multi-Database Testing

Armada supports four database backends: SQLite, PostgreSQL, SQL Server, and MySQL. The testing strategy covers databases at two layers:

- **Test.Database** exhaustively tests the database driver layer directly, running CRUD operations for core orchestration entities plus newer delivery entities such as workflow profiles, check runs, objectives, environments, deployments, and releases against each backend.
- **Test.Automated** tests the full stack (REST API, MCP tools, WebSocket) and can now target any database backend via the `--type` argument.
- **Dashboard Vitest** now includes first-class delivery/tooling smoke coverage for Workspace, Planning, History, Request History, API Explorer, Checks, Objectives, and Releases.
- **Incident coverage** now exercises rollback-linked incident context through both unit and automated REST suites.

### CI Recommendations

- Run **Test.Database** against all 4 backends to ensure driver correctness across SQLite, PostgreSQL, SQL Server, and MySQL.
- Run **Test.Automated** at minimum against SQLite (fast, no external dependencies) plus one server-based backend (e.g., PostgreSQL) to verify full-stack behavior with a real database server.

### Connection Pooling

Test runs create and dispose many database connections rapidly. When testing against server-based backends, be aware that connection pooling settings affect test behavior. The default pool sizes are generally sufficient for test runs, but if you see connection timeouts or failures under heavy parallel test execution, consider increasing the pool size or running test suites sequentially.

## Test Data Isolation

Each test suite creates its own data, asserts only on that data, and cleans up after itself. Suites track created entity IDs and delete them at the end. This pattern is followed by all test projects, including Test.Database. This means:
- Suites never assume the database is empty
- Suites never assert exact total counts across entity types
- Suites can run in any order without affecting each other
- Use `--no-cleanup` to preserve test data after a run for debugging

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

1. Find or create the appropriate suite in `Suites/`
2. Add a call to `RunTest("Test Name", async () => { ... })` inside the suite's `RunTestsAsync()` method
3. Use assertion helpers: `Assert()`, `AssertEqual()`, `AssertNotNull()`, `AssertTrue()`, `AssertStatusCode()`
4. Track any created entity IDs and delete them in the suite's cleanup section
5. Register new suites in `Program.cs` via `runner.AddSuite(new YourTests(...))`
