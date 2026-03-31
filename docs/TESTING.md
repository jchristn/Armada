# Testing

## Run All Tests

All commands run from the repository root. Each test project is a standalone console application.

```bash
dotnet run --project test/Armada.Test.Automated --framework net10.0
dotnet run --project test/Armada.Test.Unit --framework net10.0
dotnet run --project test/Armada.Test.Runtimes --framework net10.0

# Database driver tests (SQLite default)
dotnet run --project test/Armada.Test.Database --framework net10.0 -- --type sqlite --filename test.db

# PostgreSQL
dotnet run --project test/Armada.Test.Database --framework net10.0 -- --type postgresql --hostname localhost --port 5432 --username postgres --password secret --database armada_test

# SQL Server
dotnet run --project test/Armada.Test.Database --framework net10.0 -- --type sqlserver --hostname localhost --port 1433 --username sa --password secret --database armada_test

# MySQL
dotnet run --project test/Armada.Test.Database --framework net10.0 -- --type mysql --hostname localhost --port 3306 --username root --password secret --database armada_test
```

## Test Projects

| Project | Tests | What It Covers |
|---------|-------|----------------|
| `Armada.Test.Automated` | ~781 | REST API, MCP tools, WebSocket, authentication, end-to-end workflows |
| `Armada.Test.Unit` | ~377 | Database operations, model serialization, service logic |
| `Armada.Test.Runtimes` | ~32 | Agent runtime adapters (Claude Code, Codex) |
| `Armada.Test.Database` | ~100+ | Database driver CRUD operations across all 4 backends (SQLite, PostgreSQL, SQL Server, MySQL) |
| `Armada.Test.Common` | — | Shared test infrastructure (TestRunner, TestSuite, TestResult) |

## How It Works

No test framework (xUnit, NUnit, MSTest) is used. Each test project is a console app that runs tests sequentially and reports results.

- `TestSuite` — abstract base class in `Armada.Test.Common`. Each suite groups related tests, provides assertion helpers, and cleans up its own test data.
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
dotnet run --project test/Armada.Test.Automated --framework net10.0

# Keep test database after run (for debugging)
dotnet run --project test/Armada.Test.Automated --framework net10.0 -- --no-cleanup

# Test against PostgreSQL instead of default temp SQLite
dotnet run --project test/Armada.Test.Automated --framework net10.0 -- --type postgresql -h localhost -u postgres -w secret -d armada_test

# Test against SQL Server
dotnet run --project test/Armada.Test.Automated --framework net10.0 -- --type sqlserver -h localhost --port 1433 -u sa -w secret -d armada_test

# Test against MySQL
dotnet run --project test/Armada.Test.Automated --framework net10.0 -- --type mysql -h localhost --port 3306 -u root -w secret -d armada_test
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

## Multi-Database Testing

Armada supports four database backends: SQLite, PostgreSQL, SQL Server, and MySQL. The testing strategy covers databases at two layers:

- **Test.Database** exhaustively tests the database driver layer directly, running CRUD operations for all 9 entity types (fleets, vessels, captains, missions, voyages, docks, signals, artifacts, merge queue entries) against each backend.
- **Test.Automated** tests the full stack (REST API, MCP tools, WebSocket) and can now target any database backend via the `--type` argument.

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

## Adding Tests

1. Find or create the appropriate suite in `Suites/`
2. Add a call to `RunTest("Test Name", async () => { ... })` inside the suite's `RunTestsAsync()` method
3. Use assertion helpers: `Assert()`, `AssertEqual()`, `AssertNotNull()`, `AssertTrue()`, `AssertStatusCode()`
4. Track any created entity IDs and delete them in the suite's cleanup section
5. Register new suites in `Program.cs` via `runner.AddSuite(new YourTests(...))`
