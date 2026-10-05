# Fragility Remediation Plan

> **Status:** In progress
> **Started:** 2026-10-04
> **Scope:** every place a decision (production behavior, security gate, or test pass/fail) rests on matching text
> where a structured form exists or can be created.

## Why

A codebase scan (three read-only passes: Core/Server, Runtimes/Clients/TUI/Helm/Proxy/Dashboard, and tests) found
about 430 such decisions: roughly 61 in Core/Server (7 High), 60 in the other production projects (2 High), and 260
in tests (about 25 High: false passes or dead checks). Examples: quarantining a captain because a log line contains
"403"; choosing 404 vs 400 by searching an exception message for "not found"; a secret scan that skips added lines
starting with "++ "; a test that converts exceptions to the string "EXCEPTION " and then skips them; assertions that
always pass because `DescriptionLength` contains "Description".

## Rules (apply to all new code and to every fix)

1. Never branch on exception message text. Throw and catch typed exceptions (`KeyNotFoundException` for a missing
   or invisible entity, `ArgumentException` for bad input, a dedicated exception type for anything else that a
   caller must distinguish).
2. Never classify by substring or regex over text that has, or can be given, a structured form. Return typed results
   (enums, error codes, result classes). Persist kinds as enum columns, not as prefixes inside free-text fields.
3. No sentinel strings for types or errors (`"EXCEPTION "`, `"none"`, `"! "` prefixes, `"local-"` id prefixes, display
   labels as keys). Use enums, flags, or nullable typed fields.
4. External processes: use machine formats (`git ... -z`, `--porcelain=v2`, `--numstat`, `--name-status`, `--json`,
   `show-ref --verify`, exit codes) and launch with `LC_ALL=C` (and `DOTNET_CLI_UI_LANGUAGE=en`) where text must be
   read at all.
5. Deserialize JSON into typed classes (project rule); no `JsonElement` / `JsonNode` property walking except to
   preserve unknown keys when editing third-party config files.
6. Paths: containment via `Path.GetRelativePath` (reject `..` and rooted results), never `StartsWith` on strings.
   Route policies match canonicalized paths or route templates.
7. Tests assert on deserialized models, status codes, and typed error codes; select stub requests by route, not by
   position or body text; never treat "the call failed" as "the call was denied".

## Workstreams

- [x] **R0 MCP tool errors.** `McpToolErrorCodeEnum` + `McpToolError` on all 210 MCP error returns;
  `McpToolClient.CallToolResultAsync` with `isError`; `Mcp.ToolCallsPerSecond`; McpTenantIsolation suite rewritten
  on typed results (commit 73541c11). Follow-up in R2: remove `_UntypedNotFoundTools`.
- [x] **R1a Git, diffs, boundary, auto-land.** DockBoundaryScanner and MissionService diff counting from
  `--name-status -z` / `--numstat -z` (High: secret scan bypass, protected-path bypass, auto-land limits);
  GitService exception-message filters replaced by exit codes and pre-checks; branch existence via `show-ref
  --verify`; default branch via `symbolic-ref`; WorkspaceService `status --porcelain=v2 -z --branch`; `gh pr create`
  URL via `gh pr view --json url`; `LC_ALL=C` on every git/gh launch; worktree list `-z`; iso-strict dates.
- [x] **R1b Failure and verdict classification.** Runtime failure kind from structured runtime events and exit info
  instead of `RuntimeFailureClassifier` substrings and log scraping (High: wrong quarantines); persisted
  `MissionFailureKindEnum` (migration on all four providers) instead of `FailureReason` prefixes re-parsed by
  `MissionFailureClassifier` (High: wrong auto-rescue); judge verdict from the structured verdict signal only
  (High); provider reset from structured data; mission status changes not driven by stdout text.
- [x] **R1c Edge and security gates.** Proxy route policy on canonicalized paths (High); proxy static file and
  CheckRun/inventory path containment; relay allowlist after normalization; dashboard prefix segment boundary;
  typed `UnsafeListenerConfigurationException`; SQLite/MySQL migrations without message matching; Helm runtime
  parsing to `AgentRuntimeEnum` with errors for unknown values (High: `--runtime opencode` created Claude Code);
  Helm 409 via `HttpRequestException.StatusCode`; service/startup registrar exact checks.
- [x] **R2 Typed not-found.** Services throw `KeyNotFoundException` for missing or invisible entities (about 111
  `InvalidOperationException("... not found")` sites); REST routes map exception types to 404/400/409 (13 routes
  that search `ex.Message`); MCP handler exceptions mapped through `McpToolError.FromException`; typed MCP argument
  classes where `GetProperty` is used; remove `_UntypedNotFoundTools` from the isolation suite.
- [x] **R3 Typed protocol parsing.** `McpToolClient` fully typed (JSON-RPC envelope, SSE frame by id, `isError`
  passed to `ApiAgentRuntime` tool events); shared typed event classes for Claude stream-json, Codex, Mux, OpenCode
  replacing the four hand-walked parsers (CaptainChatService, PlanningSessionCoordinator, RuntimeLogFormatter,
  MissionRoutes); tool events on a typed channel instead of stdout markers; CaptainRuntimeToolCatalog JSON-RPC;
  Proxy settings and request bodies; runtime tool argument classes; ObjectiveRefinement summary; npm error codes;
  Ask action results typed.
  Ask action results typed. Done on work/fx-protocol; Proxy settings and request bodies were left to R1c (proxy
  is outside the R3 scope); istanbul coverage typing landed in R1a.
- [x] **R4 Clients, TUI, dashboard.** Status and runtime fields typed as enums in client models and TUI logic
  (approvals, rebuild status, notifications severity, status badges); `ArmadaApiException` codes instead of message
  compares; dashboard `ApiError` status/code instead of message compares; event routing by `EntityType`; action menu
  keys; diff file lists from the API. Starts after the running TUI agents merge. Done on work/fx-clients; diff
  files come from `UnifiedDiffParser` line kinds (TUI, Helm) and a TypeScript port of it (dashboard) rather than a
  new API field.
- [ ] **R5 Tests.** About 260 sites: typed body and response assertions (helpers that deserialize stub bodies),
  `StubHttpHandler` single request queue with route selection, McpToolSuite on typed results and error codes, fix
  always-pass checks (`Description`/`DescriptionLength`, `"log line 1"`, status-or-text OR chains), host-locale
  independence, exit codes instead of tool wording, dashboard tests on parsed messages and matched calls. Starts
  after the running TUI test agent merges.

## Progress log

| Date | Who | Items | Notes |
|---|---|---|---|
| 2026-10-04 | Claude | Scan, R0 | Scan reports from three read-only passes; R0 merged to main. |
| 2026-10-04 | Claude | R1a | `UnifiedDiffParser` (hunk ranges, C-quoted paths, deletes/renames) drives the boundary scan and auto-land; dock branch facts from `--name-status -z` / `--numstat -z`; `GitCommandException` + exit-code pre-checks; `show-ref --verify`; `symbolic-ref`; porcelain v2 -z; `gh pr view --json`; worktree list -z; iso-strict dates; `LC_ALL=C` on git/gh launches; check-run artifacts preferred and typed JSON. Branch work/fx-git. |
| 2026-10-04 | Claude | R2 | About 114 service throw sites now `KeyNotFoundException`; `RouteErrorMapper` replaces the 13 `ex.Message.Contains("not found")` routes and the IOE catches around converted calls; `McpToolRegistrar.MapToolExceptions` maps handler exceptions by type; typed args for `approve_deployment` and `start_runbook_execution`; `_UntypedNotFoundTools` removed; McpToolSuite asserts ErrorCode NotFound; new E2E.TypedNotFound suite. Deferred: three `ArgumentException("... not found")` sites (see CHANGELOG). |
| 2026-10-04 | Claude | R3 | Typed MCP client (envelopes, SSE frame by id, typed exception codes); ApiEndpoint typed tool/diagnostic channels with isError; `Armada.Core.Protocol` stream events replace four parsers; injectable captain tool discovery; shared string-aware JSON extractor; typed Ask outcomes; npm/dotnet restore from codes and files; typed runtime tool arguments; run_process argv. Branch work/fx-protocol. |
| 2026-10-04 | Claude | R1c | Canonical proxy route policy (bypass reproduced first), PathContainment everywhere, typed proxy bodies/settings/state, UnsafeListenerConfigurationException, SQLite/MySQL migrations, Helm runtime parsing, StatusCode, --task, registrar exit codes. Branch work/fx-edge. |
| 2026-10-04 | Claude | R1b | Typed `RuntimeExitInfo` from `IAgentRuntime.OnProviderError` (API status, Claude protocol error lines); persisted `Mission.FailureKind` (migration 77); `JudgeVerdictParser` (protocol line only) and labeled Affected Case fields; agent status stdout-only InProgress/Testing; `armada-plan` JSON architect plan and `WaitForVoyageWorkers`; timeline severity, check-run type, token report anchoring. Deferred: Codex exposes no structured error in text mode (classifies as crash until R3 runs it with `--json`); `ArchitectHandoffMarker` sentinel in descriptions. |
| 2026-10-04 | Claude | R4 | Per-enum `StatusBadge` map and typed notification severity (deployment status plus verification); `EntityChangedEvent` typed accessors via strict `EnumNames`; `InboxItem.EntityName` and `InboxItemKinds`; rebuild status `ServerRebuildStatusEnum?` (legacy "none" read as null); `VesselReadinessIssue.InputProvider`; `Objective.SourceNumber`; `CaptainToolSourceKindEnum`; `DoctorCheckStatusEnum`; `ArmadaApiException.IsTimeout`; event routes by `EntityType`, timeline delete by `SourceType`; `AskMessage.IsLocal`; ask turn/tool phase enums; `ActionMenuItem.Key`; JSON-token unions for quick actions and OpenAPI values; `AgentRuntimeEnum` in captain forms, setup wizard, planning, Ask; Helm MCP client kind; diff line kinds from `UnifiedDiffParser` (TUI, Helm) and a TS port (dashboard); dashboard `ApiError`/`TimeoutError`, typed severity, entity-type routing, `isLocal`, i18n stops rewriting user data; strict Harbor runtime names; publisher signs by SHA-1. New suites Tui.TypedFields and Services.InboxEntityName, 10 dashboard test files. Deferred: Helm id-or-name arguments still use the documented id prefixes; `HarborLaunchRequest.Runtime` stays a string on the wire (typed `RuntimeType` accessor); no tests for Armada.Publisher (net10.0-only project, not referenced by the multi-targeted test assembly); TUI diff file jump still uses text search; whole-string catalog lookup in `T(text)` and the dashboard DOM translator (documented in code). Branch work/fx-clients. |
