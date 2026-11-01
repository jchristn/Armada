# Phase 4 worker verification

Branch: armada/codex-1/msn_mvxf3n23_V1oCE5oqulg
Mission: msn_mvxf3n23_V1oCE5oqulg
State: BLOCKED; do not land as a completed AC4 implementation.

## Blocking acceptance case

AC4 requires missing required MCP arguments to return JSON-RPC invalid params
(-32602). E2E.UsageTenantIsolation.mcp_missing_argument_is_protocol_error
preserves that assertion and fails. The actual response is a JSON-RPC result
with content describing missing captainId and isError=true, without error.code.

The pinned Voltaic 2.4.0 package source at commit
f9b5c0daeae97f6f624fa9e1a71fcbe8a26a6b14, McpEndpoint.CallToolCoreAsync,
validates the required schema before invoking Armada's handler and converts
validation exceptions into tool errors. McpHttpServer exposes no per-tool
validation policy or public original-dispatcher wrapper. Replacing tools/call
would require owning the whole dispatcher; removing required from the schema
would misrepresent the public contract. Neither workaround was applied.
Existing E2E.McpTool tests explicitly expect the pinned behavior for other tools.
A supported upstream override, or a reviewed change to the explicit AC4 contract,
is required. This worker does not change that acceptance criterion.

Independent Test Engineer reproduced this failure; independent Judge returned
NEEDS_REVISION. No pipeline was bypassed, no merge to main was attempted.

## Implemented scope

- Account-owned snapshot/latest views, bounded limit/token series and mission
  completion drill-down with shared REST/MCP caller scope.
- Tenant-derived filters, foreign-ID resolution before secondary validation,
  explicit global-admin tenant selection, sanitized public views.
- Nullable persisted token attribution, stable mission pagination, safe deleted
  or foreign metadata joins, retry totals without duplicate token IDs.
- Capture receipts, passive observation client union, read-only Ask usage status,
  post-commit tenant event notification and typed .NET client methods.
- Local REST/MCP/WebSocket docs, collection assertions and regenerated surface.

New suites implement IArmadaTestSuite and are discovered by the existing
ArmadaTestSuites reflection registry. No manual registration list exists.
The only Phase 2 implementation extension is SnapshotCommitted notification
following snapshot/member/latest persistence; cancellation, scheduling and
concurrency logic are unchanged. New public models are in Core, already
referenced by Armada.Client, so no duplicate Client model definitions are needed.

## Executed checks

Installed SDKs: 8.0.425 and 10.0.401. Selected net10.0 for targeted tests.
Commands used exec_command, preserving the process exit status; run_process was
not available in the tool inventory.

- dotnet restore src/Armada.sln: exit 0, fresh worktree dependencies restored.
- dotnet build src/Armada.sln --no-restore -v quiet: exit 0, both project target
  frameworks built, final observed result 0 warnings / 0 errors.
- bash scripts/common/generate-api-surface.sh -f net10.0: exit 0; 518 REST routes,
  264 MCP tools, 77 WebSocket events. Generated settings include previously
  implemented usage capture settings absent from the old frozen artifact.
- dotnet run --project src/Test.Automated --framework net10.0 -- --suites
  Services.Usage,Services.UsageDefaults,E2E.ApiContract,E2E.Authorization,Client.Coverage,Client.RouteSurface:
  initial result 119 total / 118 pass / 1 fail. The failure exposed 503 schema
  declarations using ApiErrorResponse, which cannot name ServiceUnavailable.
  Fixed to existing ApiStatusErrorResponse; 429 uses existing SlowDown code.
  All Phase 2/3 preservation cases in this run passed, including local HTTP
  provider-header capture attached before requests, stream EOF, secrets,
  quarantine, host cancellation, settings invalidation and slot conservation.
- dotnet run --project src/Test.Automated --framework net10.0 -- --suites
  E2E.Usage,E2E.UsageTenantIsolation,Client.Contract.Usage,E2E.ApiContract,E2E.Authorization,Client.Coverage,Client.RouteSurface:
  after the 503 fix and regenerated surface: exit 1; 39 total / 38 pass / 1 fail.
  Only failure: required-missing MCP protocol assertion above. ApiContract,
  AuthorizationCoverage, Client.Coverage and Client.RouteSurface all passed.
- dotnet run --project src/Test.Automated --framework net10.0 -- --suites
  Client.Contract.Usage: final exit 0, 2/2 pass. Added passive_capture_preserves_original
  proves the SDK union keeps the passive observation's original timestamp and
  rejects a foreign Harbor instead of dropping passive result fields.
- E2E.UsageEvents.commit_fanout_reconnect: passed on the actual temporary
  Admiral websocket. Subscribers attach and observe a status response before
  invoking the production commit path with a sanitized fixture. Own-tenant
  regular/admin receive the event; another tenant and an unscoped global admin
  do not. Diagnostics/account keys are absent; reconnect refetch finds one row.
  This is persistence/notification evidence, not external CLI probe evidence.
- E2E.UsageAggregation: 3/3 passed: full hourly rollups cannot be placed across
  offset bucket boundaries; failed samples break named-window lines with null
  points; account keys without membership remain unattributed.
- Collection JSON parse and structural preservation check: exit 0. All existing
  collection folders/requests/examples/scripts/auth and variable prefix compare
  equal to HEAD; nine new Usage requests carry response assertions.
- git diff --check for code/docs/changelog: exit 0. Collection keeps its existing
  CRLF encoding, which default git whitespace checking flags on added lines.

## Acceptance and linked decisions

AC4 is NOT met because the required protocol-error case fails.

D1: account_owned_samples_half_open proves two captains linked to one canonical
sample contribute one observation; rest_roles_and_explicit_tenant and
mcp_tenant_scope prove identical account identities remain isolated across
A/B and admin scope requires an explicit tenant. These cases passed.

D5: missions_use_persisted_attribution and mission_metadata_and_stable_pages
prove [from,to), old account attribution after captain membership changes,
nullable legacy exclusion coverage, retry totals, chat separation, cached-token
nonduplication, safe deleted/foreign metadata and bounded stable pagination.
These cases passed. No current account is inferred for old null attribution.

Latest raw-retention/range tests passed; empty/default/error tests passed;
all five SDK methods have live calls and errors. Runtime string MCP filtering
and A/B MCP scope passed. Post-commit event routing/reconnect passed as above.

## Remaining limits and unverified paths

- Required MCP protocol error is the explicit landing blocker.
- Public HTTP 202 single-flight joins, 429 cooldown headers and disconnected
  host 503 are implemented but not covered by new live endpoint success/error
  cases. Existing service-level single-flight/cooldown/offline-host tests passed;
  that is not equivalent to endpoint coverage. Captain-session REST/MCP/WS
  visibility and full role-by-operation matrix remain unverified here.
- Postman assertions were saved and structurally checked, not executed against
  a live or mocked server. No Postman workspace was initialized or published.
- PostgreSQL/MySQL/SQL Server query behavior was not executed in this mission;
  new live suites used SQLite. No provider schema/storage changes were made.
- TokenUsageCapture currently does not populate persisted account attribution;
  these queries honor explicitly stored fields and show absent attribution as
  unknown. Start-to-completion production attribution wiring was not expanded.
- Retained hourly rollups lack Harbor identity; Harbor-filtered historical
  rollups are omitted with an explicit warning. Offset bucket boundaries omit
  hours that cannot be allocated exactly. A window on token drilldown identifies
  quota context, not token allocation; coverage explains the distinction.
- Full test suite, dashboard/TUI/mobile builds and phases 5-7 were not run or
  advanced. Armada's post-stage pipeline remains authoritative for landing.

## Origin reachability

Fresh git fetch origin main returned
30358ef7824534abd6e7448cb2fc9d3c116355e1 on 2026-10-31.
Both commands exited 0:

git merge-base --is-ancestor 1eeb9e5853710e49dfe0c9ef9a88f16aaa23c176 origin/main
git merge-base --is-ancestor f4e88920fde7a664bbb95ea001d5b92c4148b720 origin/main

The worker commit is pushed to its mission branch for review. It is not landed
on main because AC4 has the failure and verification gaps recorded above.
