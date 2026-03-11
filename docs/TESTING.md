# Testing

## Run All Tests

All commands run from the repository root. Each test project is a standalone console application.

```bash
dotnet run --project test/Armada.Test.Automated --framework net10.0
dotnet run --project test/Armada.Test.Unit --framework net10.0
dotnet run --project test/Armada.Test.Runtimes --framework net10.0
```

## Test Projects

| Project | Tests | What It Covers |
|---------|-------|----------------|
| `Armada.Test.Automated` | ~781 | REST API, MCP tools, WebSocket, authentication, end-to-end workflows |
| `Armada.Test.Unit` | ~377 | Database operations, model serialization, service logic |
| `Armada.Test.Runtimes` | ~32 | Agent runtime adapters (Claude Code, Codex) |
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
# Run with default settings
dotnet run --project test/Armada.Test.Automated --framework net10.0

# Keep test database after run (for debugging)
dotnet run --project test/Armada.Test.Automated --framework net10.0 -- --no-cleanup
```

## Test Data Isolation

Each test suite creates its own data, asserts only on that data, and cleans up after itself. Suites track created entity IDs and delete them at the end. This means:
- Suites never assume the database is empty
- Suites never assert exact total counts across entity types
- Suites can run in any order without affecting each other

## Adding Tests

1. Find or create the appropriate suite in `Suites/`
2. Add a call to `RunTest("Test Name", async () => { ... })` inside the suite's `RunTestsAsync()` method
3. Use assertion helpers: `Assert()`, `AssertEqual()`, `AssertNotNull()`, `AssertTrue()`, `AssertStatusCode()`
4. Track any created entity IDs and delete them in the suite's cleanup section
5. Register new suites in `Program.cs` via `runner.AddSuite(new YourTests(...))`
