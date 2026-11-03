# Usage enforcement and mission inspection (v1.3.0)

This wave adds opt-in Enforce admission and operator inspection on `feature/v1.3.0`. Observe/FirstAvailable remains the default. The complete [implementation plan](../../USAGE_BASED_CAPTAIN_SELECTION_PLAN.md) still tracks recovery actions, additional native surfaces, telemetry and release validation; those are not implied complete by these additions.

## Configure and test

1. As global administrator, open Dashboard > Server > Captain selection. Choose Observe or Enforce and First available, Most remaining allowance, or Most used allowance. Save applies live with a revision check; concurrent editors must reload. Environment-supplied policy remains read-only.
2. Configure two qualified captains and collect fresh account usage on their actual execution hosts. For a controlled test, use separate accounts with different remaining allowance. Keep existing persona/routing requirements identical. With Enforce, switch strategies and dispatch a small mission to a disposable repository. Most remaining prefers more headroom; Most used consolidates on the most utilized eligible account without crossing the reserve. First available preserves existing qualified routing order, with known usage before permitted unknown usage.
3. Open the mission. Captain selection shows the recorded captain, configured model and separately runtime-reported model. Compare captains shows identity/model, compact state and flexible details. Inspect History, filter Waiting/Admitted, page, refresh, open a historical decision and inspect JSON. A missing runtime report stays Unknown. A deleted captain's historical name remains readable without a broken link.
4. For a Pending mission, Preview current selection reevaluates current readiness/evidence without probing, writing history, claiming capacity or starting work. It is not a promise of later admission: placement and usage can change. Preview does not provision or contact a Harbor to create a dock. Real admission applies placement and transaction-time checks again.
5. With Unknown usage set to Wait, missing/stale/ambiguous account evidence leaves work Pending with a semantic reason. Refresh capture through the existing Captain Usage controls. Recheck times are hints, not guaranteed starts. A reported provider reset never invents fresh balance. Unknown Allow deprioritized explicitly permits uncertain usage; it does not turn it into a measured zero.
6. Two captains sharing one account must share its reservation budget. With only one attempt's estimated headroom left above the reserve, concurrent admissions allow one attempt. Running work stays running after policy edits. A credential-host mismatch or unavailable credential Harbor waits instead of moving a known-account admission to another account.
7. Inspect the same authorized data with `armada mission selection <mission>`, `--history --outcome Waiting --page 1 --page-size 25`, `--preview`, `--decision <decision>`, or `--json`. JSON follows the existing Helm PascalCase contract. `--history`, `--preview` and `--decision` are mutually exclusive.

## Admission and accounting

Enforce commits the mission assignment, captain slot, immutable decision and reservations before launch. The transaction locks the mission, captain and shared account state in a stable order, then rechecks mission/captain versions, raw configured model, owner, quarantine, membership, source freshness, execution host, captured window identity and active/unsettled budgets. Server providers use read-committed isolation so a contender that waited for the account lock sees the winner's reservation. SQLite retains its existing serialized write path. A stale proposal or failed evidence insert cannot leave a slot or partial claim behind. An already-committed receipt cannot launch again.

Percentage settings/reservations round conservatively upward to six decimal places; available headroom rounds downward. This matches storage precision and avoids fractional under-reservation. Algorithm `usage-bands-v2` records the known-before-unknown FirstAvailable refinement; existing v1 history is unchanged.

PostgreSQL/MySQL mission readers now retain `UserId` when a mission is read and updated; the repair race exposed this pre-existing ownership omission. The provider tests assert ownership survives before admission.

Migration 126 adds an internal execution key and `usage_untracked_executions`. Work started with selection Disabled, before the upgrade, or without a complete measurable reservation creates operational uncertainty, not fabricated historical selection evidence. Startup adopts active legacy work before dispatch. Unknown account attribution conservatively affects the same tenant/runtime until there is proof; it cannot silently disappear when membership changes. With explicit AllowDeprioritized, unknown work can still run.

Active execution stays active. Ended/superseded work stays unsettled until a verified active capture began after execution end plus the configured lag on the same account, host and configuration. Passive observations and missing legacy probe-start times cannot prove settlement. Aging changes unresolved work to Uncertain, never to invented zero debt. Reconciliation is bounded, fair and guarded against stale revisions or rewritten execution-end times.

## API and privacy

Existing authorization resolves the mission/retained decision before filters and counts. Owner, tenant-admin, global-admin and mission-token restrictions are shared across REST/MCP. Candidate pages omit internal account keys and hidden candidates; visible ranks do not expose hidden rank gaps. Historical names are stored separately from current captain availability. Actual-model evidence is suppressed if the selected candidate is hidden.

- `GET /api/v1/missions/{id}/captain-selection`
- `GET /api/v1/missions/{id}/captain-selection/history`, with `POST .../history/enumerate` twin
- `POST /api/v1/missions/{id}/captain-selection/preview`
- `GET /api/v1/captain-selection-decisions/{id}`
- `GET /api/v1/captain-selection-decisions/{id}/candidates`, with `POST .../candidates/enumerate` twin

History supports trigger/outcome and UTC `[from,to)` filters. Pages default to 25 and are bounded to 1000. The .NET SDK and Core client provide typed reads. MCP dedicated selection tools and `enumerate` types `captain_selection_decision` (missionId required) and `captain_selection_candidate` (decisionId required) use the same service. The repository Postman collection includes reusable success, waiting and authorization-check requests, with inherited credential variables; it was not published externally.

## UX review and evidence

The mission card lives on the existing Mission detail route and uses its global breadcrumb. It reuses DataTable, pagination, refresh, copy and JSON controls. Identity and state columns are narrow; details get remaining width. Long names/models truncate with full hover text. Phone tables scroll inside the card, while translated controls wrap. Polling preserves filters/pages, pauses in hidden tabs and avoids overlapping requests. Superseded requests are aborted. Authorization loss clears cached evidence.

Browser review renders the real React card/CSS against isolated fixture responses, not the deployed server. Test widths are 1280, 768 and 390; themes light/dark; locales English/Spanish/Japanese; additional enlarged-text/RTL and keyboard focus checks. Waiting, legacy, unavailable and redacted states are included. Representative screenshots and the geometry/focus report are stored beside this document. This review found and corrected a Spanish phone-pagination overflow. It does not replace the final live operator/simulated-user session required by U5.

## Remaining work

Classified provider cooldowns, audited authorized fallback/reserve exceptions, complete proven-unstarted failure/recovery handling, richer settlement proof/export, native Mobile/TUI operational controls, Events/Needs You and WebSocket invalidation, telemetry/stress/upgrade evidence and final release packaging remain tracked in the plan. The current conservative behavior may keep failed/unknown work's allowance uncertain until a trustworthy capture; no force-release or bypass action is being advertised.

## Verification record

Local evidence for this wave includes 43/43 browser checks with inspected screenshots, 1,891/1,891 Dashboard tests and a production build, native type checking and 1,388/1,388 native assertions, six fixture-backed Helm command checks, 55 targeted repair/admission/response-shape checks, and the refreshed public API inventory (528 REST routes, 270 MCP tools, 98 CLI commands). PostgreSQL, MySQL and SQL Server each pass 55/55 shared checks, including admission/repair races, mission CRUD and migration hygiene. Final framework/provider/full-suite/build outcomes are recorded below when complete. Provider and runtime fixtures are isolated; these checks do not deploy the production server.
