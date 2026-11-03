# Live selection configuration and Observe history

This v1.3.0 segment adds configurable policy controls and connects Observe decisions to normal dispatch and verification repair. It does not complete the full usage-selection plan. Enforce remains unavailable; the server refuses live requests to enable it rather than silently applying an incomplete budget gate.

## Operator controls

Open Dashboard > Server > Settings > Captain selection as a global administrator. Choose Disabled or Observe and one of:

- **First available**, the compatibility default using the existing routing order.
- **Most remaining allowance**, favoring eligible accounts with more effective headroom within comparable routing/band groups.
- **Most used allowance**, favoring more utilized eligible accounts while preserving the protected reserve.

In Observe, the strategy controls the hypothetical comparison. Actual launches keep existing captain routing, capacity, quota and placement rules. The mode and strategy are separate controls; changing strategy does not authorize fallback, override a strict assignment or bypass safety rules. Unknown evidence can be allowed after known eligible accounts or deferred. Expand Evidence and accounting limits for the other policy values.

GET/PUT `/api/v1/settings` use the `UsageSelection` group. GET also returns `UsageSelectionRevision` and `UsageSelectionCapabilities`. PUT can supply `ExpectedUsageSelectionRevision`; a stale editor receives 409 and must refresh. Omitted groups stay unchanged. Revisions are persisted monotonic integers that JavaScript can represent exactly under normal operation. MCP uses the same coordinator through `get_usage_selection_settings` and `update_usage_selection_settings`. Only a global administrator can change this server-wide group.

`ARMADA_USAGE_SELECTION` is a complete JSON group override. Its effective values are reported as environment-controlled and cannot be overwritten by REST/MCP. The file-backed values remain intact when another settings group is saved.

Settings changes are audited before persistence and published live only after the file is committed. A protected adjacent rollback journal recovers interrupted writes without renaming the settings file itself, preserving Docker single-file bind mounts. Startup restores an interrupted write before parsing settings and records recovery. The journal may contain server secrets from the previous settings file: it uses restrictive permissions and is never exposed or logged. Other pre-existing settings writers are not converted into a general settings transaction by this work.

## Selection inspection

The following authenticated REST routes share tenant, mission-owner and mission-token checks:

| Method | Route | Result |
|---|---|---|
| GET | `/api/v1/missions/{id}/captain-selection` | Current admitted attempt separately from latest retained decision |
| GET | `/api/v1/missions/{id}/captain-selection/history` | Filtered, paged decision history |
| POST | `/api/v1/missions/{id}/captain-selection/preview` | Current-state comparison without captures, writes, claims or launch |
| GET | `/api/v1/captain-selection-decisions/{id}` | Immutable policy, scoped candidate page and execution evidence |
| GET | `/api/v1/captain-selection-decisions/{id}/candidates` | Further scoped comparison pages |

Pages use `pageNumber`/`pageSize`, default 1/25, with size 1..1000. History accepts `trigger`, `outcome`, `from` and `to`; the time range is half-open. Standalone decision reads default to the caller's tenant; only a global administrator can supply a different `tenantId`. Candidate-owner filters apply before totals. Internal account keys never appear in these response models. Retained records can outlive raw captures or a deleted mission.

MCP exposes `get_captain_selection`, `get_captain_selection_history`, `get_captain_selection_decision` and `preview_captain_selection`. The .NET SDK has corresponding typed methods and candidate paging. Missing runtime services return Unavailable, including standalone configurations without an orchestration service. Generic MCP enumerate integration and Helm/native client controls remain in the plan.

## Execution and estimates

Observe admission commits the actual captain claim, mission attempt pointer, policy, candidate/window evidence and shadow estimates in one transaction. Replay never grants a second launch. A later legacy/Disabled claim clears the pointer, preventing an older decision from being labeled as the current attempt. Configured intended models and runtime-reported models stay distinct; missing or redacted reports remain unknown.

Health maintenance reconciles an oldest-checked bounded reservation page. A running matching attempt retains its active estimate. Ended or superseded attempts become unsettled. Settlement requires a fresh authoritative matching-account/configuration/window capture whose trusted active probe began after execution ended plus the recorded policy lag. Passive/legacy observations without probe-start timing cannot prove settlement. An elapsed reset timer cannot manufacture allowance. Missing evidence eventually becomes uncertain debt, rather than silently disappearing. A SQL index supports the bounded reconciliation scan.

This is conservative shadow accounting. Full atomic enforcement, classified provider cooldowns, all lifecycle exceptions, settlement proof exports and operator recovery remain U3 work. The server-side preview still needs full placement/backoff parity review before the whole U2 milestone is complete. Dashboard mission history/comparison and the remaining supported clients remain incomplete.

## Validation and layout review

- Final broad SQLite/API slice: 777/777 passed on net8.0 and net10.0, zero failures/skips. This includes all Database suites, Services.UsageSelection/UsageSelectionConfiguration/UsageQuarantine/UsageScheduler, E2E.CaptainSelectionApi/ApiContract and Client.RouteSurface. The final MCP schema/API inventory check passed 19/19 on net10.0.
- Routing, priority, envelope and admission parity slice: 74/74 on both net8.0 and net10.0; includes a real tenant-scoped Observe dispatch with two distinct captured accounts. Preview proposes the higher-headroom captain while actual dispatch preserves the legacy choice; the launch callback proves history already committed, and execution start prevents an unstarted-release transition.
- The focused ledger and captain-slot matrix passed 16/16 on PostgreSQL, MySQL and SQL Server, including source loading, attempt correlation, uncertain debt and post-execution capture proof. The final index/lifecycle changes passed the same 16/16 checks on each provider.
- HTTP checks exercise live strategy application, file reload, stale revisions, unsupported Enforce, tenant administrator rejection, owner/peer/foreign tenant history isolation, redacted totals, SDK reads and preview without writes. MCP checks exercise the same policy coordinator.
- Dashboard: 237 files and 1884 tests passed; TypeScript/Vite production build passed. UI tests cover advertised strategy options, full-group preservation, revision conflicts retaining edits, explicit refresh, invalid/blank inputs and environment/non-admin read-only behavior.
- Actual React settings controls were rendered with fixture API responses in headless Chrome, using production component/CSS source, at 1280/768/390 pixels in light/dark themes, collapsed and expanded: 12/12 geometry/label checks passed. Desktop dark and mobile light screenshots were inspected. These are component layout checks, not a claim of a deployed browser end-to-end run. Existing settings controls, button classes and page navigation are reused; no page or breadcrumb was added. Keyboard focus checks also pass for every enabled visible control after adding scoped focus outlines. See [layout results](settings-layout-results.json), [mobile light](screenshots/settings-light-390.png) and [desktop dark](screenshots/settings-dark-1280.png). Full temporary artifacts are in `/tmp/armada-selection-visual` during this session.
- Translation coverage includes every new stable key in all eight non-English locale packs; translations retain the existing beta status.

Docker's internal disk remains full. Server-provider fixtures use owned temporary containers with memory-backed data directories. Existing user containers, volumes, images and the remote deployment were not changed for testing.
