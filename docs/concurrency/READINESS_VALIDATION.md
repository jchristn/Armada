# Dependency and integration readiness validation

Dispatch and waiting diagnostics now call the same readiness evaluation before safety and capacity checks. Dependency status, pipeline handoff and integration-barrier decisions are evaluated once in shared code. Diagnostics do not mutate missions or reserve capacity. Dispatch alone blocks or cancels an integration after a failed sibling, according to BlockDependentsOfFailedStages; the displayed summary now names that configured transition correctly.

## Regression coverage

Added 23 cases to Services.MissionAssignmentBlocker. They cover all unsettled dependency states (including verification), missing dependencies, completed dependencies, branch handoff readiness, Architect marker readiness, and the existing no-upstream-branch fallback. The integration failure matrix covers Failed/LandingFailed with blocking enabled and disabled and proves a diagnostic read preserves Pending before dispatch applies the transition.

The single-slot scenario tries a higher-priority integration first, verifies it claims no captain capacity, starts its lower-priority Worker, then releases the completed Worker's slot and starts integration. Service tests use SQLite, directory-creating git and a synthetic process launch callback. They do not call a model.

The .NET 10 regression run passes 235 tests across database slot transactions, concurrent access, readiness, handoff, pipeline/sequential dispatch, architect parsing, stage-check recovery and E2E concurrency. This includes the 200-mission mixed-limit stress case and existing HTTP/git/real-process lifecycle tests. The full .NET 8/.NET 10 solution builds with zero warnings/errors. The .NET 8 regression run passes 248 tests, including the same concurrency coverage plus E2E.ApiContract; the checked-in API surface baseline remains compatible. The full Dashboard suite passes all 1,856 tests (236 files).

## UX/layout/aesthetic validation

An isolated local Admiral served the production Dashboard and real REST-computed blockers for database-seeded fixture missions. No intercepted responses were used. Dependency and integration cards were checked in light and dark themes at 1440, 768 and 390 pixels (12 combinations). Their linked upstream/holding mission routes are present in every case, summaries wrap within their cards, shared breadcrumbs remain consistent, and no document-level horizontal overflow was measured. The integration card correctly says the next dispatch will block it. The disposable server was stopped after validation; no user server or Harbor was modified.

See [measurements](readiness-layout-results.json), [integration desktop](readiness-integration-desktop.png), and [dependency phone](readiness-dependency-mobile.png).

## Manual validation

Create a pipeline whose upstream stage is still running; inspect its Pending downstream mission and follow the dependency link. Finish upstream and confirm the downstream stage starts only after its handoff is ready. In a voyage limited to one active mission, put integration before an unfinished Worker; the Worker must run first and integration must start after sibling work settles. If a sibling fails, the waiting summary must predict Blocked or Cancelled according to the server setting.

Public response fields and reason values remain unchanged. REST_API and MCP_API describe the shared readiness rules and read-only diagnostics. Remaining captain eligibility unification, invariant monitoring, verification slot release, hierarchy and migrations are still open in the plan.

Release command: ./build-all.sh v1.3.0; output: /tmp/armada-v130-readiness-build-all.log.

Postman CLI v1.75.0 does not implement the skill's `postman status` command (unknown command). Verification used the repository's existing contract tests and real local REST reads; no Postman workspace artifacts were changed or published.
