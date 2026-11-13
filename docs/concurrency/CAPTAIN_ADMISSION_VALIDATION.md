# Captain admission selection validation

Dispatch, waiting diagnostics, stuck-admission preflight and launch-failure retry checks now share a captain selection snapshot. It resolves voyage preferences on a temporary mission view, applies the existing scope, activation, author exclusion, quarantine, persona/default/priority, tier and Worker-envelope rules, and filters runtime/endpoint capacity before ranking candidates. Dispatch alone records Worker-envelope decision events. Verification repair admission uses the same resolved eligibility predicate while retaining its original captain and atomic version-guarded claim.

Full eligible captains produce CaptainConcurrencyLimit even when unrelated captains have spare capacity. Working captains with spare slots now report EligibleForMission and explain exclusions. Retry backoff can be bypassed by an untried eligible Working captain with spare slots, but not by an unrelated untried captain.

## Regression validation

Fifteen new Services.CaptainAdmissionParity cases compare diagnostics with actual dispatch and monitoring for unresolved strict/non-strict voyage overrides, preferred/default behavior, persona priority availability, runtime limits, inactive requests, effective server ceilings, full-versus-ineligible pools and launch backoff. They verify read-only mission versions/preferences, dock creation and real envelope decision event behavior. Existing routing, tier, priority and envelope suites preserve established selection behavior.

The .NET 10 broad regression passes 547 cases, including HTTP/git/process end-to-end concurrency, API contracts, lifecycle, the 200-mission matrix and verification recovery. After a final shared-context cleanup and stronger envelope-event test, all 170 affected cases pass again. The .NET 8 service/database/routing regression passes all 480 cases. The Dashboard passes 1,858 tests; production assets and the .NET 8/.NET 10 solution build succeed with zero solution warnings/errors. The concurrency test script includes the parity, tier, persona-priority and envelope suites; shell syntax and whitespace checks pass. Executed database tests use SQLite; live SQL Server/MySQL/PostgreSQL validation remains open.

## UX/layout/aesthetic validation

An owned disposable local Admiral served actual authenticated mission detail responses, with two seeded capacity/routing cases. Startup recovery reset the initial synthetic assignments; fixtures were restored after startup for stable layout inspection. Both waiting cards were checked in light/dark themes at 1440, 768 and 390 pixels (12 cases). No API interception or model execution was used. Shared breadcrumbs, compact action links, spacing, wrapping and the captain links remain consistent with the existing Dashboard. No document horizontal overflow was measured. Desktop and phone screenshots were visually inspected.

See [layout measurements](captain-admission-layout-results.json) and [authenticated read-only evidence](captain-admission-read-only-results.json). Screenshots were reviewed locally and are not committed. REST checks confirm repeated reads leave mission routing preferences, versions, captain slots and event count unchanged. The owned server was stopped after validation; no user Admiral or Harbor was modified.

## Manual validation

Fill a voyage's strictly pinned captain while keeping an unrelated captain free. Its Pending mission should explain captain slot saturation; diagnostics must not persist the unresolved pin. Free the pinned slot and dispatch: that captain should receive the mission. A non-strict busy preference may fall back according to normal persona/tier rules. Working captains with spare capacity restricted to another persona should explain that mismatch. Failed verification repairs should wait for capacity and preserve their original ownership.

Next work remains legacy-field cleanup/migrations, broader provider/load validation, hierarchy, wait telemetry, remaining public operations/clients and release integration. Release command: `./build-all.sh v1.3.0`; build log: `/tmp/armada-v130-captain-admission-build-all.log`.
