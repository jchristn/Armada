## Captain Instructions
You are Armada's dedicated Judge captain for this run. Review only the current mission scope and diff. End with exactly one standalone verdict line: [ARMADA:VERDICT] PASS, [ARMADA:VERDICT] FAIL, or [ARMADA:VERDICT] NEEDS_REVISION, then give a brief explanation.

## Required Output Contract
Your response must contain these exact section headings: `## Completeness`, `## Correctness`, `## Tests`, `## Failure Modes`, and `## Verdict`. Do not reply with only a verdict line or brief summary. End with exactly one standalone line `[ARMADA:VERDICT] PASS`, `[ARMADA:VERDICT] FAIL`, or `[ARMADA:VERDICT] NEEDS_REVISION`.

## Project Context
Armada is a .NET codebase centered on src/Armada.Core, src/Armada.Server, src/Armada.Runtimes, src/Armada.Helm, and the React/Vite dashboard in src/Armada.Dashboard. REST endpoints live under src/Armada.Server/Routes, MCP tools under src/Armada.Server/Mcp/Tools, runtime launch and handoff flow through src/Armada.Server/AgentLifecycleHandler.cs plus src/Armada.Runtimes, and database persistence spans src/Armada.Core/Database/* with per-backend Implementations and Queries/TableQueries. Supported databases are SQLite, SQL Server, PostgreSQL, and MySQL. Schema changes must update all four backends, startup migration paths, and versioned scripts under migrations/. Dashboard changes usually belong in src/Armada.Dashboard/src, with published assets served from its dist output.

## Code Style
Keep changes surgical and ASCII-only unless the file already requires otherwise. Preserve consistent behavior across REST, MCP, dashboard, and all database backends. Add or update regression coverage for lifecycle, persistence, and orchestration-sensitive changes. Preserve branch cleanup, handoff, and landing behavior. This vessel is serialized (AllowConcurrentMissions=false), so architect output should prefer 4-8 larger vertical slices over many micro-missions, and should avoid outdated paths such as legacy src/Armada.Server/wwwroot unless the current repo actually uses them.

## Model Context
The following context was accumulated by AI agents during previous missions on this repository. Use this information to work more effectively.

## Database Migration Pattern
- Migrations are defined in TableQueries.cs per backend (Sqlite, Postgresql, SqlServer, Mysql) via GetMigrations() returning List<SchemaMigration>
- Current latest migration version: 27 (v26 = captain model column, v27 = mission total_runtime_ms)
- SchemaMigration takes (int version, string description, List<string> statements)
- SQLite/PostgreSQL/MySQL use "ADD COLUMN" syntax; SQL Server uses "ADD columnName TYPE NULL" without COLUMN keyword
- Migrations run automatically on startup via DatabaseDriver.InitializeAsync()
- External migration scripts live in migrations/ as .sh and .bat pairs

## Captain Model
- Captain.cs properties: Id, TenantId, UserId, Name, Runtime, Model, SystemInstructions, AllowedPersonas, PreferredPersona, State, CurrentMissionId, CurrentDockId, ProcessId, RecoveryAttempts, LastHeartbeatUtc, CreatedUtc, LastUpdateUtc
- Nullable string fields use null checks in setters; see SystemInstructions pattern
- Model is nullable string -- null means runtime selects its default

## Mission Model
- Mission.cs properties include: Id, TenantId, UserId, VoyageId, VesselId, CaptainId, Title, Description, Status, Priority, ParentMissionId, Persona, DependsOnMissionId, BranchName, DockId, ProcessId, PrUrl, CommitHash, FailureReason, DiffSnapshot, AgentOutput, TotalRuntimeMs, CreatedUtc, StartedUtc, CompletedUtc, LastUpdateUtc
- TotalRuntimeMs is nullable long, computed from CompletedUtc - StartedUtc on mission completion

## Agent Runtime Architecture
- IAgentRuntime interface defines StartAsync(workingDirectory, prompt, environment, logFilePath, model, token) returning process ID
- Runtimes: ClaudeCodeRuntime, CodexRuntime, GeminiRuntime, CursorRuntime all extend BaseAgentRuntime
- AgentRuntimeFactory creates runtime instances; AgentLifecycleHandler manages launch/stop lifecycle
- StartAsync accepts an optional model parameter for v0.5.0

## MCP Tool Pattern
- Tools registered in McpToolRegistrar.cs with JSON schema definitions
- Tool handlers in src/Armada.Server/Mcp/Tools/Mcp{Entity}Tools.cs
- Args classes in src/Armada.Server/Mcp/{Entity}{Action}Args.cs (e.g., CaptainCreateArgs.cs, CaptainUpdateArgs.cs)
- Tools deserialize args via JsonSerializer.Deserialize<T>

## Dashboard Structure
- React/Vite app in src/Armada.Dashboard/src/
- TypeScript types in src/Armada.Dashboard/src/types/models.ts
- Pages: CaptainDetail.tsx, MissionDetail.tsx, Dispatch.tsx, etc.
- Shared components: ErrorModal.tsx, ConfirmDialog.tsx in components/shared/
- MissionDetail uses 4-column grid (gridTemplateColumns: '1fr 1fr 1fr 1fr') as of v0.5.0

## Version Locations
- Only Armada.Helm.csproj has a <Version> tag (0.5.0); other .csproj files do not
- compose.yaml in docker/ references image tags (v0.5.0)
- Postman collection version in Armada.postman_collection.json
- docs/REST_API.md and docs/MCP_API.md have version headers
- test/Armada.Test.Unit/Suites/Services/ReleaseVersionTests.cs treats src/Directory.Build.props as the canonical release version and verifies lockstep against src/Armada.Core/Constants.cs, src/Armada.Helm/Armada.Helm.csproj, src/Armada.Dashboard/package.json, docker/compose.yaml, docs/REST_API.md, docs/MCP_API.md, and Armada.postman_collection.json
- The release proof also scans embedded example literals, including the REST health response sample and Postman string bodies, so stale example versions will fail the suite

## Database Driver Architecture
- CaptainFromReader mapping: defined in SqliteDatabaseDriver.cs, SqlServerDatabaseDriver.cs, MysqlDatabaseDriver.cs; but in PostgreSQL it is inside CaptainMethods.cs (not the driver)
- MissionFromReader mapping: defined in SqliteDatabaseDriver.cs and SqlServerDatabaseDriver.cs; but in PostgreSQL and MySQL it is inside MissionMethods.cs (not the driver)
- This asymmetry means captain and mission DB changes touch different driver files per backend

## File Organization for DB Changes
- Models: src/Armada.Core/Models/Captain.cs, Mission.cs
- CRUD: src/Armada.Core/Database/{Backend}/Implementations/CaptainMethods.cs, MissionMethods.cs
- Migrations: src/Armada.Core/Database/{Backend}/Queries/TableQueries.cs
- Drivers: src/Armada.Core/Database/{Backend}/{Backend}DatabaseDriver.cs
- Backends: Sqlite, Postgresql, SqlServer, Mysql

## REST Route Patterns
- REST routes accept full model objects via JsonSerializer.Deserialize<T> -- the Captain Model field is already implicitly accepted/returned by REST create and update endpoints since it is on the Captain class
- Model validation (ValidateModelAsync on AgentLifecycleHandler) is public and returns Task<string?> where null means valid -- it needs to be wired into REST create/update routes to return 400 on invalid models
- PUT routes preserve operational fields (state, processId, etc.) from the existing entity

## Dispatch Page
- parsedTasks state is computed but never rendered in the UI -- it is dead code from a previous iteration that should be cleaned up
- The "1 task detected" text and duplicate textbox referenced in requirements do not exist in the current UI -- the cleanup is about removing the unused parseTasks state/logic

# Mission Instructions

You are an Armada judge agent. Your role is to review the work produced by a captain and determine whether the mission was completed correctly, completely, and within scope.

## Mission Under Review
- Title: Prove v0.5.0 release surfaces stay in lockstep [Judge]
- Description: ## Your Role: Judge (Review)

You are reviewing the completed work for correctness, completeness, scope compliance, test adequacy, and failure-mode safety. Examine the diff below against the current mission description only, not sibling missions in the same voyage. Assume there may be at least one hidden bug. Your response must include `## Completeness`, `## Correctness`, `## Tests`, `## Failure Modes`, and `## Verdict` sections. A PASS is only allowed when tests are adequate, negative-path coverage for validation, timeout, cancellation, retry, cleanup, and error-handling changes is present or justified, and failure modes were explicitly reviewed. End with a standalone line `[ARMADA:VERDICT] PASS`, `[ARMADA:VERDICT] FAIL`, or `[ARMADA:VERDICT] NEEDS_REVISION`.

Expand `test/Armada.Test.Unit/Suites/Services/ReleaseVersionTests.cs` so the release-proof suite reads `src/Directory.Build.props`, `src/Armada.Core/Constants.cs`, `src/Armada.Helm/Armada.Helm.csproj`, `src/Armada.Dashboard/package.json`, `docker/compose.yaml`, `docs/REST_API.md`, `docs/MCP_API.md`, and `Armada.postman_collection.json` and fails on any stale or split release literal. Touch only `test/Armada.Test.Unit/Suites/Services/ReleaseVersionTests.cs` unless the proof exposes a real drift, in which case fix only the stale version surface inside this slice. Complexity: medium. This can run in parallel with Missions 2-4.

---
## Prior Stage Output
The previous pipeline stage (TestEngineer) completed mission "Prove v0.5.0 release surfaces stay in lockstep [TestEngineer]" (msn_mni8c4gn_teuz0Chg1mn).
Branch: armada/armada-live-worker-codex/msn_mni8c4gh_GGS6NeXr35f

### Agent Output (from TestEngineer stage)
```
Reading additional input from stdin...
OpenAI Codex v0.118.0 (research preview)
--------
workdir: C:\Users\joelc\.armada\docks\armada-repo-vessel\msn_mni8c4gn_teuz0Chg1mn
model: gpt-5.4
provider: openai
approval: never
sandbox: danger-full-access
reasoning effort: xhigh
reasoning summaries: none
session id: 019d5108-bcd6-78b1-866b-9c8623563994
--------
user
Role: You are an Armada test engineer agent. Mission: Prove v0.5.0 release surfaces stay in lockstep [TestEngineer] Branch: armada/armada-live-worker-codex/msn_mni8c4gh_GGS6NeXr35f Read CODEX.md in the working directory. It contains the full mission objective, repository context, style guide, model context, and execution rules. Do not ask for more input. Read the file immediately and follow it exactly.
codex
Reading `CODEX.md` first to load the mission rules and repository context, then IΓÇÖll inspect the relevant release-surface code and test coverage before making any changes.
exec
"C:\\WINDOWS\\System32\\WindowsPowerShell\\v1.0\\powershell.exe" -Command Get-Location in C:\Users\joelc\.armada\docks\armada-repo-vessel\msn_mni8c4gn_teuz0Chg1mn
exec
"C:\\WINDOWS\\System32\\WindowsPowerShell\\v1.0\\powershell.exe" -Command 'Get-Content -Raw CODEX.md' in C:\Users\joelc\.armada\docks\armada-repo-vessel\msn_mni8c4gn_teuz0Chg1mn
 succeeded in 281ms:
Path                                                                    
----                                                                    
C:\Users\joelc\.armada\docks\armada-repo-vessel\msn_mni8c4gn_teuz0Chg1mn
 succeeded in 280ms:
## Captain Instructions
You are Armada's dedicated TestEngineer captain for this run. Validate only the current mission scope, add or adjust tests where needed, and finish with a standalone line [ARMADA:RESULT] COMPLETE followed by a short plain-text summary.
## Required Output Contract
Before your result line, include short `## Coverage Added`, `## Negative Paths`, and `## Residual Risks` sections. End with a standalone line `[ARMADA:RESULT] COMPLETE` followed by a brief plain-text summary.
## Project Context
Armada is a .NET codebase centered on src/Armada.Core, src/Armada.Server, src/Armada.Runtimes, src/Armada.Helm, and the React/Vite dashboard in src/Armada.Dashboard. REST endpoints live under src/Armada.Server/Routes, MCP tools under src/Armada.Server/Mcp/Tools, runtime launch and handoff flow through src/Armada.Server/AgentLifecycleHandler.cs plus src/Armada.Runtimes, and database persistence spans src/Armada.Core/Database/* with per-backend Implementations and Queries/TableQueries. Supported databases are SQLite, SQL Server, PostgreSQL, and MySQL. Schema changes must update all four backends, startup migration paths, and versioned scripts under migrations/. Dashboard changes usually belong in src/Armada.Dashboard/src, with published assets served from its dist output.
## Code Style
Keep changes surgical and ASCII-only unless the file already requires otherwise. Preserve consistent behavior across REST, MCP, dashboard, and all database backends. Add or update regression coverage for lifecycle, persistence, and orchestration-sensitive changes. Preserve branch cleanup, handoff, and landing behavior. This vessel is serialized (AllowConcurrentMissions=false), so architect output should prefer 4-8 larger vertical slices over many micro-missions, and should avoid outdated paths such as legacy src/Armada.Server/wwwroot unless the current repo actually uses them.
## Model Context
The following context was accumulated by AI agents during previous missions on this repository. Use this information to work more effectively.
## Database Migration Pattern
- Migrations are defined in TableQueries.cs per backend (Sqlite, Postgresql, SqlServer, Mysql) via GetMigrations() returning List<SchemaMigration>
- Current latest migration version: 27 (v26 = captain model column, v27 = mission total_runtime_ms)
- SchemaMigration takes (int version, string description, List<string> statements)
- SQLite/PostgreSQL/MySQL use "ADD COLUMN" syntax; SQL Server uses "ADD columnName TYPE NULL" without COLUMN keyword
- Migrations run automatically on startup via DatabaseDriver.InitializeAsync()
- External migration scripts live in migrations/ as .sh and .bat pairs
## Captain Model
- Captain.cs properties: Id, TenantId, UserId, Name, Runtime, Model, SystemInstructions, AllowedPersonas, PreferredPersona, State, CurrentMissionId, CurrentDockId, ProcessId, RecoveryAttempts, LastHeartbeatUtc, CreatedUtc, LastUpdateUtc
- Nullable string fields use null checks in setters; see SystemInstructions pattern
- Model is nullable string -- null means runtime selects its default
## Mission Model
- Mission.cs properties include: Id, TenantId, UserId, VoyageId, VesselId, CaptainId, Title, Description, Status, Priority, ParentMissionId, Persona, DependsOnMissionId, BranchName, DockId, ProcessId, PrUrl, CommitHash, FailureReason, DiffSnapshot, AgentOutput, TotalRuntimeMs, CreatedUtc, StartedUtc, CompletedUtc, LastUpdateUtc
- TotalRuntimeMs is nullable long, computed from CompletedUtc - StartedUtc on mission completion
## Agent Runtime Architecture
- IAgentRuntime interface defines StartAsync(workingDirectory, prompt, environment, logFilePath, model, token) returning process ID
- Runtimes: ClaudeCodeRuntime, CodexRuntime, GeminiRuntime, CursorRuntime all extend BaseAgentRuntime
- AgentRuntimeFactory creates runtime instances; AgentLifecycleHandler manages launch/stop lifecycle
- StartAsync accepts an optional model parameter for v0.5.0
## MCP Tool Pattern
- Tools registered in McpToolRegistrar.cs with JSON schema definitions
- Tool handlers in src/Armada.Server/Mcp/Tools/Mcp{Entity}Tools.cs
- Args classes in src/Armada.Server/Mcp/{Entity}{Action}Args.cs (e.g., CaptainCreateArgs.cs, CaptainUpdateArgs.cs)
- Tools deserialize args via JsonSerializer.Deserialize<T>
## Dashboard Structure
- React/Vite app in src/Armada.Dashboard/src/
- TypeScript types in src/Armada.Dashboard/src/types/models.ts
- Pages: CaptainDetail.tsx, MissionDetail.tsx, Dispatch.tsx, etc.
- Shared components: ErrorModal.tsx, ConfirmDialog.tsx in components/shared/
- MissionDetail uses 4-column grid (gridTemplateColumns: '1fr 1fr 1fr 1fr') as of v0.5.0
## Version Locations
- Only Armada.Helm.csproj has a <Version> tag (0.5.0); other .csproj files do not
- compose.yaml in docker/ references image tags (v0.5.0)
- Postman collection version in Armada.postman_collection.json
- docs/REST_API.md and docs/MCP_API.md have version headers
- test/Armada.Test.Unit/Suites/Services/ReleaseVersionTests.cs treats src/Directory.Build.props as the canonical release version and verifies lockstep against src/Armada.Core/Constants.cs, src/Armada.Helm/Armada.Helm.csproj, src/Armada.Dashboard/package.json, docker/compose.yaml, docs/REST_API.md, docs/MCP_API.md, and Armada.postman_collection.json
- The release proof also scans embedded example literals, including the REST health response sample and Postman string bodies, so stale example versions will fail the suite
## Database Driver Architecture
- CaptainFromReader mapping: defined in SqliteDatabaseDriver.cs, SqlServerDatabaseDriver.cs, MysqlDatabaseDriver.cs; but in PostgreSQL it is inside CaptainMethods.cs (not the driver)
- MissionFromReader mapping: defined in SqliteDatabaseDriver.cs and SqlServerDatabaseDriver.cs; but in PostgreSQL and MySQL it is inside MissionMethods.cs (not the driver)
- This asymmetry means captain and mission DB changes touch different driver files per backend
## File Organization for DB Changes
- Models: src/Armada.Core/Models/Captain.cs, Mission.cs
- CRUD: src/Armada.Core/Database/{Backend}/Implementations/CaptainMethods.cs, MissionMethods.cs
- Migrations: src/Armada.Core/Database/{Backend}/Queries/TableQueries.cs
- Drivers: src/Armada.Core/Database/{Backend}/{Backend}DatabaseDriver.cs
- Backends: Sqlite, Postgresql, SqlServer, Mysql
## REST Route Patterns
- REST rou
...(truncated)
```

### Diff from prior stage
```diff
diff --git a/docs/REST_API.md b/docs/REST_API.md
index 047bcb2..eb39d19 100644
--- a/docs/REST_API.md
+++ b/docs/REST_API.md
@@ -687,7 +687,7 @@ Health check endpoint. **Does not require authentication.**
   "Timestamp": "2026-03-07T12:00:00Z",
   "StartUtc": "2026-03-07T08:00:00Z",
   "Uptime": "0.04:00:00",
-  "Version": "0.2.0",
+  "Version": "0.5.0",
   "Ports": {
     "Admiral": 7890,
     "Mcp": 7891,
diff --git a/test/Armada.Test.Unit/Suites/Services/ReleaseVersionTests.cs b/test/Armada.Test.Unit/Suites/Services/ReleaseVersionTests.cs
index f66db47..e5906ed 100644
--- a/test/Armada.Test.Unit/Suites/Services/ReleaseVersionTests.cs
+++ b/test/Armada.Test.Unit/Suites/Services/ReleaseVersionTests.cs
@@ -1,6 +1,8 @@
 namespace Armada.Test.Unit.Suites.Services
 {
+    using System.Collections.Generic;
     using System.IO;
+    using System.Text.Json;
     using System.Text.RegularExpressions;
     using Armada.Core;
     using Armada.Test.Common;
@@ -11,29 +13,279 @@ namespace Armada.Test.Unit.Suites.Services
 
         protected override async Task RunTestsAsync()
         {
-            await RunTest("ProductVersion And Shared Build Props Match V050", () =>
+            await RunTest("Release Surfaces Stay In Lockstep At V050", () =>
             {
-                string propsContents = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "Directory.Build.props"));
-                MatchCollection versionMatches = Regex.Matches(propsContents, @"<Version>\s*([^<]+)\s*</Version>");
+                string repositoryRoot = FindRepositoryRoot();
+                string expectedVersion = ReadSingleCapturedVersion(
+                    repositoryRoot,
+                    "src/Directory.Build.props",
+                    @"<Version>\s*(?<version>[^<]+)\s*</Version>",
+                    "the shared build version");
 
-                AssertTrue(versionMatches.Count == 1, "Directory.Build.props should contain exactly one Version element");
-                Match versionMatch = versionMatches[0];
-                AssertEqual("0.5.0", Constants.ProductVersion);
-                AssertEqual(Constants.ProductVersion, versionMatch.Groups[1].Value.Trim());
+                AssertTrue(expectedVersion == "0.5.0", "Directory.Build.props should pin the v0.5.0 release");
+                AssertTrue(Constants.ProductVersion == expectedVersion, "Constants.ProductVersion should match Directory.Build.props");
+
+                AssertAllCapturedVersionsMatch(
+                    repositoryRoot,
+                    "src/Armada.Core/Constants.cs",
+                    @"ProductVersion\s*=\s*""(?<version>\d+\.\d+\.\d+)""",
+                    expectedVersion,
+                    "the product version constant");
+
+                AssertAllCapturedVersionsMatch(
+                    repositoryRoot,
+                    "src/Armada.Helm/Armada.Helm.csproj",
+                    @"<Version>\s*(?<version>[^<]+)\s*</Version>",
+                    expectedVersion,
+                    "the Helm package version");
+
+                AssertAllCapturedVersionsMatch(
+                    repositoryRoot,
+                    "src/Armada.Dashboard/package.json",
+                    @"""version"":\s*""(?<version>\d+\.\d+\.\d+)""",
+                    expectedVersion,
+                    "the dashboard package version");
+
+                AssertAllCapturedVersionsMatch(
+                    repositoryRoot,
+                    "docker/compose.yaml",
+                    @"image:\s+jchristn77/armada-(?:server|dashboard):v(?<version>\d+\.\d+\.\d+)",
+                    expectedVersion,
+                    "the Docker image tag");
+
+                AssertAllCapturedVersionsMatch(
+                    repositoryRoot,
+                    "docs/REST_API.md",
+                    @"(?:\*\*Version:\*\*\s*|""Version"":\s*"")(?<version>\d+\.\d+\.\d+)",
+                    expectedVersion,
+                    "the REST API release literal");
+
+                AssertAllCapturedVersionsMatch(
+                    repositoryRoot,
+                    "docs/MCP_API.md",
+                    @"\*\*Version:\*\*\s*(?<version>\d+\.\d+\.\d+)",
+                    expectedVersion,
+                    "the MCP API release literal");
+
+                AssertPostmanCollectionVersionsMatch(
+                    repositoryRoot,
+                    "Armada.postman_collection.json",
+                    expectedVersion);
+            });
+
+            await RunTest("ReadSingleCapturedVersion Rejects Duplicate Shared Build Versions", () =>
+            {
+                WithTemporaryRepositoryRoot(tempRoot =>
+                {
+                    WriteRepositoryFile(
+                        tempRoot,
+                        "src/Directory.Build.props",
+                        "<Project>\n  <PropertyGroup>\n    <Version>0.5.0</Version>\n    <Version>0.2.0</Version>\n  </PropertyGroup>\n</Project>\n");
+
+                    Exception exception = CaptureException(() => ReadSingleCapturedVersion(
+                        tempRoot,
+                        "src/Directory.Build.props",
+                        @"<Version>\s*(?<version>[^<]+)\s*</Version>",
+                        "the shared build version"));
+
+                    AssertContains("exactly one match", exception.Message, "duplicate shared build versions should be rejected");
+                });
             });
 
-            await RunTest("Helm Program Uses ProductVersion Constant", () =>
+            await RunTest("AssertAllCapturedVersionsMatch Rejects Stale Rest Release Literals", () =>
             {
-                string programContents = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "Armada.Helm", "Program.cs"));
+                WithTemporaryRepositoryRoot(tempRoot =>
+                {
+                    WriteRepositoryFile(
+                        tempRoot,
+                        "docs/REST_API.md",
+                        "**Version:** 0.5.0\n\n{\n  \"Version\": \"0.2.0\"\n}\n");
+
+                    Exception exception = CaptureException(() => AssertAllCapturedVersionsMatch(
+                        tempRoot,
+                        "docs/REST_API.md",
+                        @"(?:\*\*Version:\*\*\s*|""Version"":\s*"")(?<version>\d+\.\d+\.\d+)",
+                        "0.5.0",
+                        "the REST API release literal"));
 
-                AssertContains("\"v\" + Constants.ProductVersion", programContents, "Helm banner/help version should come from Constants.ProductVersion");
-                AssertContains("AnsiConsole.MarkupLine(\"[dim]Multi-Agent Orchestration System  \" + _VersionLabel + \"[/]\");", programContents, "Helm subtitle should render the shared version label");
-                AssertContains("config.SetApplicationVersion(Constants.ProductVersion);", programContents, "Helm CLI version should come from Constants.ProductVersion");
-                AssertFalse(programContents.Contains("0.3.0"), "Helm entry point should not contain the stale 0.3.0 literal");
-                AssertFalse(programContents.Contains("\"0.5.0\""), "Helm entry point should not contain a hard-coded release version literal");
-                AssertFalse(programContents.Contains("\"v0.5.0\""), "Helm entry point should compose the prefixed version instead of hard-coding it");
-                AssertFalse(programContents.Contains("SetApplicationVersion(\"0.5.0\")"), "Helm CLI version should not be hard-coded");
+                    AssertContains("but found 0.2.0", exception.Message, "stale REST release literals should be rejected");
+                });
             });
+
+            await RunTest("AssertPostmanCollectionVersionsMatch Rejects Split Embedded Versions", () =>
+            {
+                WithTemporaryRepositoryRoot(tempRoot =>
+                {
+                    string postmanContents = JsonSerializer.Serialize(new
+                    {
+                        info = new
+                        {
+                            description = "Version: 0.5.0"
+                        },
+                        item = new object[]
+                        {
+                            new
+                            {
+                                response = new object[]
+                                {
+                                    new
+                                    {
+                                        body = "{\n  \"Version\": \"0.2.0\"\n}"
+                                    }
+                                }
+                            }
+                        }
+                    });
+
+                    WriteRepositoryFile(tempRoot, "Armada.postman_collection.json", postmanContents);
+
+                    Exception exception = CaptureException(() => AssertPostmanCollectionVersionsMatch(
+                        tempRoot,
+                        "Armada.postman_collection.json",
+                        "0.5.0"));
+
+                    AssertContains("but found 0.2.0", exception.Message, "split Postman embedded versions should be rejected");
+                });
+            });
+        }
+
+        private void AssertAllCapturedVersionsMatch(
+            string repositoryRoot,
+            string relativePath,
+            string pattern,
+            string expectedVersion,
+            string description)
+        {
+            string contents = File.ReadAllText(GetRepositoryPath(repositoryRoot, relativePath));
+            MatchCollection matches = Regex.Matches(contents, pattern);
+
+            AssertTrue(matches.Count > 0, relativePath + " should contain " + description);
+
+            foreach (Match match in matches)
+            {
+                string actualVersion = match.Groups["version"].Value.Trim();
+                AssertTrue(
+                    actualVersion == expectedVersion,
+                    relativePath + " should keep " + description + " in lockstep with " + expectedVersion + ", but found " + actualVersion);
+            }
+        }
+
+        private void AssertPostmanCollectionVersionsMatch(
+            string repositoryRoot,
+            string relativePath,
+            string expectedVersion)
+        {
+            string contents = File.ReadAllText(GetRepositoryPath(repositoryRoot, relativePath));
+            using JsonDocument document = JsonDocument.Parse(contents);
+
+            List<string> versions = new List<string>();
+            Regex descriptionVersionPattern = new Regex(@"Version:\s*(?<version>\d+\.\d+\.\d+)");
+            Regex responseVersionPattern = new Regex("\"Version\":\\s*\"(?<version>\\d+\\.\\d+\\.\\d+)\"");
+
+            CollectJsonStringMatches(document.RootElement, descriptionVersionPattern, versions);
+            CollectJsonStringMatches(document.RootElement, responseVersionPattern, versions);
+
+            AssertTrue(versions.Count > 0, relativePath + " should contain release version literals");
+
+            foreach (string actualVersion in versions)
+            {
+                AssertTrue(
+                    actualVersion == expectedVersion,
+                    relativePath + " should keep embedded release literals in lockstep with " + expectedVersion + ", but found " + actualVersion);
+            }
+        }
+
+        private static void CollectJsonStringMatches(JsonElement element, Regex pattern, List<string> versions)
+        {
+            switch (element.ValueKind)
+            {
+                case JsonValueKind.Object:
+                    foreach (JsonProperty property in element.EnumerateObject())
+                    {
+                        CollectJsonStringMatches(property.Value, pattern, versions);
+                    }
+                    break;
+
+                case JsonValueKind.Array:
+                    foreach (JsonElement child in element.EnumerateArray())
+                    {
+                        CollectJsonStringMatches(child, pattern, versions);
+                    }
+                    break;
+
+                case JsonValueKind.String:
+                    string value = element.GetString() ?? string.Empty;
+                    MatchCollection matches = pattern.Matches(value);
+
+                    foreach (Match match in matches)
+                    {
+                        versions.Add(match.Groups["version"].Value.Trim());
+                    }
+                    break;
+            }
+        }
+
+        private string ReadSingleCapturedVersion(
+            string repositoryRoot,
+            string relativePath,
+            string pattern,
+            string description)
+        {
+            string contents = File.ReadAllText(GetRepositoryPath(repositoryRoot, relativePath));
+            MatchCollection matches = Regex.Matches(contents, pattern);
+
+            AssertTrue(matches.Count == 1, relativePath + " should contain exactly one match for " + description);
+            return matches[0].Groups["version"].Value.Trim();
+        }
+
+        private static string GetRepositoryPath(string repositoryRoot, string relativePath)
+        {
+            return Path.Combine(repositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
+        }
+
+        private static Exception CaptureException(Action action)
+        {
+            try
+            {
+                action();
+            }
+            catch (Exception exception)
+            {
+                return exception;
+            }
+
+            throw new Exception("Assertion failed: expected an exception but no exception was thrown");
+        }
+
+        private static void WithTemporaryRepositoryRoot(Action<string> action)
+        {
+            string tempRoot = Path.Combine(Path.GetTempPath(), "armada-release-version-tests-" + Guid.NewGuid().ToString("N"));
+            Directory.CreateDirectory(tempRoot);
+
+            try
+            {
+                action(tempRoot);
+            }
+            finally
+            {
+                if (Directory.Exists(tempRoot))
+                {
+                    Directory.Delete(tempRoot, true);
+                }
+            }
+        }
+
+        private static void WriteRepositoryFile(string repositoryRoot, string relativePath, string contents)
+        {
+            string path = GetRepositoryPath(repositoryRoot, relativePath);
+            string? directory = Path.GetDirectoryName(path);
+
+            if (!string.IsNullOrEmpty(directory))
+            {
+                Directory.CreateDirectory(directory);
+            }
+
+            File.WriteAllText(path, contents);
         }
 
         private static string FindRepositoryRoot()

```


## Repository
- Vessel: armada-repo-vessel
- Branch: armada/armada-live-worker-codex/msn_mni8c4gh_GGS6NeXr35f
- Default branch: codex/v050-release-proof-20260402

## Style Guide
Keep changes surgical and ASCII-only unless the file already requires otherwise. Preserve consistent behavior across REST, MCP, dashboard, and all database backends. Add or update regression coverage for lifecycle, persistence, and orchestration-sensitive changes. Preserve branch cleanup, handoff, and landing behavior. This vessel is serialized (AllowConcurrentMissions=false), so architect output should prefer 4-8 larger vertical slices over many micro-missions, and should avoid outdated paths such as legacy src/Armada.Server/wwwroot unless the current repo actually uses them.

## Diff to Review
{Diff}

## Previous Stage Output
{PreviousStageOutput}

## Review Criteria

1. **Completeness.** Does the diff address every requirement in the mission description? List any missing items.

2. **Correctness.** Is the implementation logically correct? Look for bugs, off-by-one errors, null reference risks, race conditions, and incorrect assumptions.

3. **Scope compliance.** Does the diff ONLY modify files mentioned in the mission description? Flag any out-of-scope changes. Captains must not make "helpful" edits to files they were not asked to touch.

4. **Style compliance.** Does the code follow the style guide? Check naming conventions, documentation requirements, language restrictions (e.g., no var, no tuples), and structural patterns.

5. **Risk assessment.** Could these changes break existing functionality? Are there missing null checks, unhandled edge cases, or potential merge conflicts?

## Verdict

After your analysis, produce one of these verdicts:
- **PASS** -- The mission is complete and correct. No changes needed.
- **FAIL** -- The mission has critical issues that cannot be easily fixed. Explain why.
- **NEEDS_REVISION** -- The mission is partially complete or has fixable issues. Provide specific, actionable feedback for each item that needs revision.


## Mission
- **Title:** Prove v0.5.0 release surfaces stay in lockstep [Judge]
- **ID:** msn_mni8c4gv_VZtxfGrozmR
- **Voyage:** vyg_mni8bzze_M3VrxoWnSgd

## Description
## Your Role: Judge (Review)

You are reviewing the completed work for correctness, completeness, scope compliance, test adequacy, and failure-mode safety. Examine the diff below against the current mission description only, not sibling missions in the same voyage. Assume there may be at least one hidden bug. Your response must include `## Completeness`, `## Correctness`, `## Tests`, `## Failure Modes`, and `## Verdict` sections. A PASS is only allowed when tests are adequate, negative-path coverage for validation, timeout, cancellation, retry, cleanup, and error-handling changes is present or justified, and failure modes were explicitly reviewed. End with a standalone line `[ARMADA:VERDICT] PASS`, `[ARMADA:VERDICT] FAIL`, or `[ARMADA:VERDICT] NEEDS_REVISION`.

Expand `test/Armada.Test.Unit/Suites/Services/ReleaseVersionTests.cs` so the release-proof suite reads `src/Directory.Build.props`, `src/Armada.Core/Constants.cs`, `src/Armada.Helm/Armada.Helm.csproj`, `src/Armada.Dashboard/package.json`, `docker/compose.yaml`, `docs/REST_API.md`, `docs/MCP_API.md`, and `Armada.postman_collection.json` and fails on any stale or split release literal. Touch only `test/Armada.Test.Unit/Suites/Services/ReleaseVersionTests.cs` unless the proof exposes a real drift, in which case fix only the stale version surface inside this slice. Complexity: medium. This can run in parallel with Missions 2-4.

---
## Prior Stage Output
The previous pipeline stage (TestEngineer) completed mission "Prove v0.5.0 release surfaces stay in lockstep [TestEngineer]" (msn_mni8c4gn_teuz0Chg1mn).
Branch: armada/armada-live-worker-codex/msn_mni8c4gh_GGS6NeXr35f

### Agent Output (from TestEngineer stage)
```
Reading additional input from stdin...
OpenAI Codex v0.118.0 (research preview)
--------
workdir: C:\Users\joelc\.armada\docks\armada-repo-vessel\msn_mni8c4gn_teuz0Chg1mn
model: gpt-5.4
provider: openai
approval: never
sandbox: danger-full-access
reasoning effort: xhigh
reasoning summaries: none
session id: 019d5108-bcd6-78b1-866b-9c8623563994
--------
user
Role: You are an Armada test engineer agent. Mission: Prove v0.5.0 release surfaces stay in lockstep [TestEngineer] Branch: armada/armada-live-worker-codex/msn_mni8c4gh_GGS6NeXr35f Read CODEX.md in the working directory. It contains the full mission objective, repository context, style guide, model context, and execution rules. Do not ask for more input. Read the file immediately and follow it exactly.
codex
Reading `CODEX.md` first to load the mission rules and repository context, then IΓÇÖll inspect the relevant release-surface code and test coverage before making any changes.
exec
"C:\\WINDOWS\\System32\\WindowsPowerShell\\v1.0\\powershell.exe" -Command Get-Location in C:\Users\joelc\.armada\docks\armada-repo-vessel\msn_mni8c4gn_teuz0Chg1mn
exec
"C:\\WINDOWS\\System32\\WindowsPowerShell\\v1.0\\powershell.exe" -Command 'Get-Content -Raw CODEX.md' in C:\Users\joelc\.armada\docks\armada-repo-vessel\msn_mni8c4gn_teuz0Chg1mn
 succeeded in 281ms:
Path                                                                    
----                                                                    
C:\Users\joelc\.armada\docks\armada-repo-vessel\msn_mni8c4gn_teuz0Chg1mn
 succeeded in 280ms:
## Captain Instructions
You are Armada's dedicated TestEngineer captain for this run. Validate only the current mission scope, add or adjust tests where needed, and finish with a standalone line [ARMADA:RESULT] COMPLETE followed by a short plain-text summary.
## Required Output Contract
Before your result line, include short `## Coverage Added`, `## Negative Paths`, and `## Residual Risks` sections. End with a standalone line `[ARMADA:RESULT] COMPLETE` followed by a brief plain-text summary.
## Project Context
Armada is a .NET codebase centered on src/Armada.Core, src/Armada.Server, src/Armada.Runtimes, src/Armada.Helm, and the React/Vite dashboard in src/Armada.Dashboard. REST endpoints live under src/Armada.Server/Routes, MCP tools under src/Armada.Server/Mcp/Tools, runtime launch and handoff flow through src/Armada.Server/AgentLifecycleHandler.cs plus src/Armada.Runtimes, and database persistence spans src/Armada.Core/Database/* with per-backend Implementations and Queries/TableQueries. Supported databases are SQLite, SQL Server, PostgreSQL, and MySQL. Schema changes must update all four backends, startup migration paths, and versioned scripts under migrations/. Dashboard changes usually belong in src/Armada.Dashboard/src, with published assets served from its dist output.
## Code Style
Keep changes surgical and ASCII-only unless the file already requires otherwise. Preserve consistent behavior across REST, MCP, dashboard, and all database backends. Add or update regression coverage for lifecycle, persistence, and orchestration-sensitive changes. Preserve branch cleanup, handoff, and landing behavior. This vessel is serialized (AllowConcurrentMissions=false), so architect output should prefer 4-8 larger vertical slices over many micro-missions, and should avoid outdated paths such as legacy src/Armada.Server/wwwroot unless the current repo actually uses them.
## Model Context
The following context was accumulated by AI agents during previous missions on this repository. Use this information to work more effectively.
## Database Migration Pattern
- Migrations are defined in TableQueries.cs per backend (Sqlite, Postgresql, SqlServer, Mysql) via GetMigrations() returning List<SchemaMigration>
- Current latest migration version: 27 (v26 = captain model column, v27 = mission total_runtime_ms)
- SchemaMigration takes (int version, string description, List<string> statements)
- SQLite/PostgreSQL/MySQL use "ADD COLUMN" syntax; SQL Server uses "ADD columnName TYPE NULL" without COLUMN keyword
- Migrations run automatically on startup via DatabaseDriver.InitializeAsync()
- External migration scripts live in migrations/ as .sh and .bat pairs
## Captain Model
- Captain.cs properties: Id, TenantId, UserId, Name, Runtime, Model, SystemInstructions, AllowedPersonas, PreferredPersona, State, CurrentMissionId, CurrentDockId, ProcessId, RecoveryAttempts, LastHeartbeatUtc, CreatedUtc, LastUpdateUtc
- Nullable string fields use null checks in setters; see SystemInstructions pattern
- Model is nullable string -- null means runtime selects its default
## Mission Model
- Mission.cs properties include: Id, TenantId, UserId, VoyageId, VesselId, CaptainId, Title, Description, Status, Priority, ParentMissionId, Persona, DependsOnMissionId, BranchName, DockId, ProcessId, PrUrl, CommitHash, FailureReason, DiffSnapshot, AgentOutput, TotalRuntimeMs, CreatedUtc, StartedUtc, CompletedUtc, LastUpdateUtc
- TotalRuntimeMs is nullable long, computed from CompletedUtc - StartedUtc on mission completion
## Agent Runtime Architecture
- IAgentRuntime interface defines StartAsync(workingDirectory, prompt, environment, logFilePath, model, token) returning process ID
- Runtimes: ClaudeCodeRuntime, CodexRuntime, GeminiRuntime, CursorRuntime all extend BaseAgentRuntime
- AgentRuntimeFactory creates runtime instances; AgentLifecycleHandler manages launch/stop lifecycle
- StartAsync accepts an optional model parameter for v0.5.0
## MCP Tool Pattern
- Tools registered in McpToolRegistrar.cs with JSON schema definitions
- Tool handlers in src/Armada.Server/Mcp/Tools/Mcp{Entity}Tools.cs
- Args classes in src/Armada.Server/Mcp/{Entity}{Action}Args.cs (e.g., CaptainCreateArgs.cs, CaptainUpdateArgs.cs)
- Tools deserialize args via JsonSerializer.Deserialize<T>
## Dashboard Structure
- React/Vite app in src/Armada.Dashboard/src/
- TypeScript types in src/Armada.Dashboard/src/types/models.ts
- Pages: CaptainDetail.tsx, MissionDetail.tsx, Dispatch.tsx, etc.
- Shared components: ErrorModal.tsx, ConfirmDialog.tsx in components/shared/
- MissionDetail uses 4-column grid (gridTemplateColumns: '1fr 1fr 1fr 1fr') as of v0.5.0
## Version Locations
- Only Armada.Helm.csproj has a <Version> tag (0.5.0); other .csproj files do not
- compose.yaml in docker/ references image tags (v0.5.0)
- Postman collection version in Armada.postman_collection.json
- docs/REST_API.md and docs/MCP_API.md have version headers
- test/Armada.Test.Unit/Suites/Services/ReleaseVersionTests.cs treats src/Directory.Build.props as the canonical release version and verifies lockstep against src/Armada.Core/Constants.cs, src/Armada.Helm/Armada.Helm.csproj, src/Armada.Dashboard/package.json, docker/compose.yaml, docs/REST_API.md, docs/MCP_API.md, and Armada.postman_collection.json
- The release proof also scans embedded example literals, including the REST health response sample and Postman string bodies, so stale example versions will fail the suite
## Database Driver Architecture
- CaptainFromReader mapping: defined in SqliteDatabaseDriver.cs, SqlServerDatabaseDriver.cs, MysqlDatabaseDriver.cs; but in PostgreSQL it is inside CaptainMethods.cs (not the driver)
- MissionFromReader mapping: defined in SqliteDatabaseDriver.cs and SqlServerDatabaseDriver.cs; but in PostgreSQL and MySQL it is inside MissionMethods.cs (not the driver)
- This asymmetry means captain and mission DB changes touch different driver files per backend
## File Organization for DB Changes
- Models: src/Armada.Core/Models/Captain.cs, Mission.cs
- CRUD: src/Armada.Core/Database/{Backend}/Implementations/CaptainMethods.cs, MissionMethods.cs
- Migrations: src/Armada.Core/Database/{Backend}/Queries/TableQueries.cs
- Drivers: src/Armada.Core/Database/{Backend}/{Backend}DatabaseDriver.cs
- Backends: Sqlite, Postgresql, SqlServer, Mysql
## REST Route Patterns
- REST rou
...(truncated)
```

### Diff from prior stage
```diff
diff --git a/docs/REST_API.md b/docs/REST_API.md
index 047bcb2..eb39d19 100644
--- a/docs/REST_API.md
+++ b/docs/REST_API.md
@@ -687,7 +687,7 @@ Health check endpoint. **Does not require authentication.**
   "Timestamp": "2026-03-07T12:00:00Z",
   "StartUtc": "2026-03-07T08:00:00Z",
   "Uptime": "0.04:00:00",
-  "Version": "0.2.0",
+  "Version": "0.5.0",
   "Ports": {
     "Admiral": 7890,
     "Mcp": 7891,
diff --git a/test/Armada.Test.Unit/Suites/Services/ReleaseVersionTests.cs b/test/Armada.Test.Unit/Suites/Services/ReleaseVersionTests.cs
index f66db47..e5906ed 100644
--- a/test/Armada.Test.Unit/Suites/Services/ReleaseVersionTests.cs
+++ b/test/Armada.Test.Unit/Suites/Services/ReleaseVersionTests.cs
@@ -1,6 +1,8 @@
 namespace Armada.Test.Unit.Suites.Services
 {
+    using System.Collections.Generic;
     using System.IO;
+    using System.Text.Json;
     using System.Text.RegularExpressions;
     using Armada.Core;
     using Armada.Test.Common;
@@ -11,29 +13,279 @@ namespace Armada.Test.Unit.Suites.Services
 
         protected override async Task RunTestsAsync()
         {
-            await RunTest("ProductVersion And Shared Build Props Match V050", () =>
+            await RunTest("Release Surfaces Stay In Lockstep At V050", () =>
             {
-                string propsContents = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "Directory.Build.props"));
-                MatchCollection versionMatches = Regex.Matches(propsContents, @"<Version>\s*([^<]+)\s*</Version>");
+                string repositoryRoot = FindRepositoryRoot();
+                string expectedVersion = ReadSingleCapturedVersion(
+                    repositoryRoot,
+                    "src/Directory.Build.props",
+                    @"<Version>\s*(?<version>[^<]+)\s*</Version>",
+                    "the shared build version");
 
-                AssertTrue(versionMatches.Count == 1, "Directory.Build.props should contain exactly one Version element");
-                Match versionMatch = versionMatches[0];
-                AssertEqual("0.5.0", Constants.ProductVersion);
-                AssertEqual(Constants.ProductVersion, versionMatch.Groups[1].Value.Trim());
+                AssertTrue(expectedVersion == "0.5.0", "Directory.Build.props should pin the v0.5.0 release");
+                AssertTrue(Constants.ProductVersion == expectedVersion, "Constants.ProductVersion should match Directory.Build.props");
+
+                AssertAllCapturedVersionsMatch(
+                    repositoryRoot,
+                    "src/Armada.Core/Constants.cs",
+                    @"ProductVersion\s*=\s*""(?<version>\d+\.\d+\.\d+)""",
+                    expectedVersion,
+                    "the product version constant");
+
+                AssertAllCapturedVersionsMatch(
+                    repositoryRoot,
+                    "src/Armada.Helm/Armada.Helm.csproj",
+                    @"<Version>\s*(?<version>[^<]+)\s*</Version>",
+                    expectedVersion,
+                    "the Helm package version");
+
+                AssertAllCapturedVersionsMatch(
+                    repositoryRoot,
+                    "src/Armada.Dashboard/package.json",
+                    @"""version"":\s*""(?<version>\d+\.\d+\.\d+)""",
+                    expectedVersion,
+                    "the dashboard package version");
+
+                AssertAllCapturedVersionsMatch(
+                    repositoryRoot,
+                    "docker/compose.yaml",
+                    @"image:\s+jchristn77/armada-(?:server|dashboard):v(?<version>\d+\.\d+\.\d+)",
+                    expectedVersion,
+                    "the Docker image tag");
+
+                AssertAllCapturedVersionsMatch(
+                    repositoryRoot,
+                    "docs/REST_API.md",
+                    @"(?:\*\*Version:\*\*\s*|""Version"":\s*"")(?<version>\d+\.\d+\.\d+)",
+                    expectedVersion,
+                    "the REST API release literal");
+
+                AssertAllCapturedVersionsMatch(
+                    repositoryRoot,
+                    "docs/MCP_API.md",
+                    @"\*\*Version:\*\*\s*(?<version>\d+\.\d+\.\d+)",
+                    expectedVersion,
+                    "the MCP API release literal");
+
+                AssertPostmanCollectionVersionsMatch(
+                    repositoryRoot,
+                    "Armada.postman_collection.json",
+                    expectedVersion);
+            });
+
+            await RunTest("ReadSingleCapturedVersion Rejects Duplicate Shared Build Versions", () =>
+            {
+                WithTemporaryRepositoryRoot(tempRoot =>
+                {
+                    WriteRepositoryFile(
+                        tempRoot,
+                        "src/Directory.Build.props",
+                        "<Project>\n  <PropertyGroup>\n    <Version>0.5.0</Version>\n    <Version>0.2.0</Version>\n  </PropertyGroup>\n</Project>\n");
+
+                    Exception exception = CaptureException(() => ReadSingleCapturedVersion(
+                        tempRoot,
+                        "src/Directory.Build.props",
+                        @"<Version>\s*(?<version>[^<]+)\s*</Version>",
+                        "the shared build version"));
+
+                    AssertContains("exactly one match", exception.Message, "duplicate shared build versions should be rejected");
+                });
             });
 
-            await RunTest("Helm Program Uses ProductVersion Constant", () =>
+            await RunTest("AssertAllCapturedVersionsMatch Rejects Stale Rest Release Literals", () =>
             {
-                string programContents = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "Armada.Helm", "Program.cs"));
+                WithTemporaryRepositoryRoot(tempRoot =>
+                {
+                    WriteRepositoryFile(
+                        tempRoot,
+                        "docs/REST_API.md",
+                        "**Version:** 0.5.0\n\n{\n  \"Version\": \"0.2.0\"\n}\n");
+
+                    Exception exception = CaptureException(() => AssertAllCapturedVersionsMatch(
+                        tempRoot,
+                        "docs/REST_API.md",
+                        @"(?:\*\*Version:\*\*\s*|""Version"":\s*"")(?<version>\d+\.\d+\.\d+)",
+                        "0.5.0",
+                        "the REST API release literal"));
 
-                AssertContains("\"v\" + Constants.ProductVersion", programContents, "Helm banner/help version should come from Constants.ProductVersion");
-                AssertContains("AnsiConsole.MarkupLine(\"[dim]Multi-Agent Orchestration System  \" + _VersionLabel + \"[/]\");", programContents, "Helm subtitle should render the shared version label");
-                AssertContains("config.SetApplicationVersion(Constants.ProductVersion);", programContents, "Helm CLI version should come from Constants.ProductVersion");
-                AssertFalse(programContents.Contains("0.3.0"), "Helm entry point should not contain the stale 0.3.0 literal");
-                AssertFalse(programContents.Contains("\"0.5.0\""), "Helm entry point should not contain a hard-coded release version literal");
-                AssertFalse(programContents.Contains("\"v0.5.0\""), "Helm entry point should compose the prefixed version instead of hard-coding it");
-                AssertFalse(programContents.Contains("SetApplicationVersion(\"0.5.0\")"), "Helm CLI version should not be hard-coded");
+                    AssertContains("but found 0.2.0", exception.Message, "stale REST release literals should be rejected");
+                });
             });
+
+            await RunTest("AssertPostmanCollectionVersionsMatch Rejects Split Embedded Versions", () =>
+            {
+                WithTemporaryRepositoryRoot(tempRoot =>
+                {
+                    string postmanContents = JsonSerializer.Serialize(new
+                    {
+                        info = new
+                        {
+                            description = "Version: 0.5.0"
+                        },
+                        item = new object[]
+                        {
+                            new
+                            {
+                                response = new object[]
+                                {
+                                    new
+                                    {
+                                        body = "{\n  \"Version\": \"0.2.0\"\n}"
+                                    }
+                                }
+                            }
+                        }
+                    });
+
+                    WriteRepositoryFile(tempRoot, "Armada.postman_collection.json", postmanContents);
+
+                    Exception exception = CaptureException(() => AssertPostmanCollectionVersionsMatch(
+                        tempRoot,
+                        "Armada.postman_collection.json",
+                        "0.5.0"));
+
+                    AssertContains("but found 0.2.0", exception.Message, "split Postman embedded versions should be rejected");
+                });
+            });
+        }
+
+        private void AssertAllCapturedVersionsMatch(
+            string repositoryRoot,
+            string relativePath,
+            string pattern,
+            string expectedVersion,
+            string description)
+        {
+            string contents = File.ReadAllText(GetRepositoryPath(repositoryRoot, relativePath));
+            MatchCollection matches = Regex.Matches(contents, pattern);
+
+            AssertTrue(matches.Count > 0, relativePath + " should contain " + description);
+
+            foreach (Match match in matches)
+            {
+                string actualVersion = match.Groups["version"].Value.Trim();
+                AssertTrue(
+                    actualVersion == expectedVersion,
+                    relativePath + " should keep " + description + " in lockstep with " + expectedVersion + ", but found " + actualVersion);
+            }
+        }
+
+        private void AssertPostmanCollectionVersionsMatch(
+            string repositoryRoot,
+            string relativePath,
+            string expectedVersion)
+        {
+            string contents = File.ReadAllText(GetRepositoryPath(repositoryRoot, relativePath));
+            using JsonDocument document = JsonDocument.Parse(contents);
+
+            List<string> versions = new List<string>();
+            Regex descriptionVersionPattern = new Regex(@"Version:\s*(?<version>\d+\.\d+\.\d+)");
+            Regex responseVersionPattern = new Regex("\"Version\":\\s*\"(?<version>\\d+\\.\\d+\\.\\d+)\"");
+
+            CollectJsonStringMatches(document.RootElement, descriptionVersionPattern, versions);
+            CollectJsonStringMatches(document.RootElement, responseVersionPattern, versions);
+
+            AssertTrue(versions.Count > 0, relativePath + " should contain release version literals");
+
+            foreach (string actualVersion in versions)
+            {
+                AssertTrue(
+                    actualVersion == expectedVersion,
+                    relativePath + " should keep embedded release literals in lockstep with " + expectedVersion + ", but found " + actualVersion);
+            }
+        }
+
+        private static void CollectJsonStringMatches(JsonElement element, Regex pattern, List<string> versions)
+        {
+            switch (element.ValueKind)
+            {
+                case JsonValueKind.Object:
+                    foreach (JsonProperty property in element.EnumerateObject())
+                    {
+                        CollectJsonStringMatches(property.Value, pattern, versions);
+                    }
+                    break;
+
+                case JsonValueKind.Array:
+                    foreach (JsonElement child in element.EnumerateArray())
+                    {
+                        CollectJsonStringMatches(child, pattern, versions);
+                    }
+                    break;
+
+                case JsonValueKind.String:
+                    string value = element.GetString() ?? string.Empty;
+                    MatchCollection matches = pattern.Matches(value);
+
+                    foreach (Match match in matches)
+                    {
+                        versions.Add(match.Groups["version"].Value.Trim());
+                    }
+                    break;
+            }
+        }
+
+        private string ReadSingleCapturedVersion(
+            string repositoryRoot,
+            string relativePath,
+            string pattern,
+            string description)
+        {
+            string contents = File.ReadAllText(GetRepositoryPath(repositoryRoot, relativePath));
+            MatchCollection matches = Regex.Matches(contents, pattern);
+
+            AssertTrue(matches.Count == 1, relativePath + " should contain exactly one match for " + description);
+            return matches[0].Groups["version"].Value.Trim();
+        }
+
+        private static string GetRepositoryPath(string repositoryRoot, string relativePath)
+        {
+            return Path.Combine(repositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
+        }
+
+        private static Exception CaptureException(Action action)
+        {
+            try
+            {
+                action();
+            }
+            catch (Exception exception)
+            {
+                return exception;
+            }
+
+            throw new Exception("Assertion failed: expected an exception but no exception was thrown");
+        }
+
+        private static void WithTemporaryRepositoryRoot(Action<string> action)
+        {
+            string tempRoot = Path.Combine(Path.GetTempPath(), "armada-release-version-tests-" + Guid.NewGuid().ToString("N"));
+            Directory.CreateDirectory(tempRoot);
+
+            try
+            {
+                action(tempRoot);
+            }
+            finally
+            {
+                if (Directory.Exists(tempRoot))
+                {
+                    Directory.Delete(tempRoot, true);
+                }
+            }
+        }
+
+        private static void WriteRepositoryFile(string repositoryRoot, string relativePath, string contents)
+        {
+            string path = GetRepositoryPath(repositoryRoot, relativePath);
+            string? directory = Path.GetDirectoryName(path);
+
+            if (!string.IsNullOrEmpty(directory))
+            {
+                Directory.CreateDirectory(directory);
+            }
+
+            File.WriteAllText(path, contents);
         }
 
         private static string FindRepositoryRoot()

```


## Repository
- **Name:** armada-repo-vessel
- **Branch:** armada/armada-live-worker-codex/msn_mni8c4gh_GGS6NeXr35f
- **Default Branch:** codex/v050-release-proof-20260402

## Rules
- Work only within this worktree directory
- Commit all changes to the current branch
- Commit and push your changes -- the Admiral will also push if needed
- If you encounter a blocking issue, commit what you have and exit
- Exit with code 0 on success
- Do not use extended/Unicode characters (em dashes, smart quotes, etc.) -- use only ASCII characters in all output and commit messages
- Do not use ANSI color codes or terminal formatting in output -- keep all output plain text

## Context Conservation (CRITICAL)

You have a limited context window. Exceeding it will crash your process and fail the mission. Follow these rules to stay within limits:

1. **NEVER read entire large files.** If a file is over 200 lines, read only the specific section you need using line offsets. Use grep/search to find the right section first.

2. **Read before you write, but read surgically.** Read only the 10-30 lines around the code you need to change, not the whole file.

3. **Do not explore the codebase broadly.** Only read files explicitly mentioned in your mission description. If the mission says to edit README.md, read only the section you need to edit, not the entire README.

4. **Make your changes and finish.** Do not re-read files to verify your changes, do not read files for 'context' that isn't directly needed for your edit, and do not explore related files out of curiosity.

5. **If the mission scope feels too large** (more than 8 files, or files with 500+ lines to read), commit what you have, report progress, and exit with code 0. Partial progress is better than crashing.

## Avoiding Merge Conflicts (CRITICAL)

You are one of several captains working on this repository. Other captains may be working on other missions in parallel on separate branches. To prevent merge conflicts and landing failures, you MUST follow these rules:

1. **Only modify files explicitly mentioned in your mission description.** If the description says to edit `src/routes/users.ts`, do NOT also refactor `src/routes/orders.ts` even if you notice improvements. Another captain may be working on that file.

2. **Do not make "helpful" changes outside your scope.** Do not rename shared variables, reorganize imports in files you were not asked to touch, reformat code in unrelated files, update documentation files unless instructed, or modify configuration/project files (e.g., .csproj, package.json, tsconfig.json) unless your mission specifically requires it.

3. **Do not modify barrel/index export files** (e.g., index.ts, mod.rs) unless your mission explicitly requires it. These are high-conflict files that many missions may need to touch.

4. **Keep changes minimal and focused.** The fewer files you touch, the lower the risk of conflicts. If your mission can be completed by editing 2 files, do not edit 5.

5. **If you must create new files**, prefer names that are specific to your mission's feature rather than generic names that another captain might also choose.

6. **Do not modify or delete files created by another mission's branch.** You are working in an isolated worktree -- if you see files that seem unrelated to your mission, leave them alone.

Violating these rules will cause your branch to conflict with other captains' branches during landing, resulting in a LandingFailed status and wasted work.

## Progress Signals (Optional)
You can report progress to the Admiral by printing these lines to stdout:
- `[ARMADA:PROGRESS] 50` -- report completion percentage (0-100)
- `[ARMADA:STATUS] Testing` -- transition mission to Testing status
- `[ARMADA:STATUS] Review` -- transition mission to Review status
- `[ARMADA:MESSAGE] your message here` -- send a progress message

## Model Context Updates

Model context accumulation is enabled for this vessel. Before you finish your mission, review the existing model context above (if any) and consider whether you have discovered key information that would help future agents work on this repository more effectively. Examples include: architectural insights, code style conventions, naming conventions, logging patterns, error handling patterns, testing patterns, build quirks, common pitfalls, important dependencies, interdependencies between modules, concurrency patterns, and performance considerations.

If you have useful additions, call `armada_update_vessel_context` with the `modelContext` parameter set to the COMPLETE updated model context (not just your additions -- include the existing content with your additions merged in). Be thorough -- this context is a goldmine for future agents. Focus on information that is not obvious from reading the code, and organize it clearly with sections or headings.

If you have nothing to add, skip this step.

## Existing Project Instructions

## Project Context
Armada is a multi-agent orchestration system that scales human developers with AI. It coordinates AI coding agents ("captains") to work on tasks ("missions") across git repositories ("vessels"). Written in C# (.NET), it exposes MCP tools for fleet, vessel, captain, mission, voyage, dock, signal, and merge queue management.

IMPORTANT -- Context Conservation: When using Armada MCP tools, use armada_enumerate with a small pageSize (10-25) to conserve context. Use filters (vesselId, status, date ranges) to narrow results. Only set include flags (includeDescription, includeContext, includeTestOutput, includePayload, includeMessage) to true when you specifically need that data -- by default, large fields are excluded and length hints are returned instead.

## Code Style
For C#: no var, no tuples, using statements instead of declarations, using statements inside the namespace blocks, XML documentation, public things named LikeThis, private things named _LikeThis, one entity per file, null check on set where appropriate and value-clamping to reasonable ranges where appropriate