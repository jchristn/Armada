# CAPTAIN_MAP.md — Captain Mapping and Per-Step Captain Selection

Implementation plan for letting an operator assign specific captains to specific pipeline steps, backed by a per-persona default captain and a capability-tier fallback. Target branch: `feature/v0.9.0`.

This document is the working checklist. A developer picks up any task, sets its status, and records notes inline. Do not delete completed items — flip their status so the history stays visible.

> Verification (2026-09-06): re-verified against the code on `feature/fork-parity-round3`. All feature artifacts are present -- `Persona.DefaultCaptainId`, `Mission.RequestedCaptainId`, `Voyage.CaptainOverridesJson`, `MissionDescription.RequestedCaptainId`, `CaptainAssignmentOverride`, `FindAvailableCaptainAsync(persona, tier, requestedCaptainId, ...)`, the dispatch `captainAssignments` path through `VoyageRequest`/`VoyageRoutes`/`McpVoyageTools`, and `docs/CAPTAIN_ROUTING.md`. `dotnet build src/Armada.sln` is clean (0 warnings, 0 errors). The checkboxes and the section-13 table below were reconciled to reality: 68 DONE, 9 DEFERRED (the intentionally-deferred items listed under "Remaining"). Note: the CM-005 dispatch DTO ships consolidated into `VoyageRequest` rather than a standalone `DispatchCaptainAssignment.cs` -- a naming deviation, not a functional gap.

## Implementation progress

Delivered and verified (builds 0 warnings, 2345/2345 backend tests pass, deployed to the local SQLite instance, end-to-end REST smoke test green including the 400-on-invalid-captain path):

- **Models + schema (CM-001..013):** all five model changes; startup migration **55** across SQLite/PostgreSQL/MySQL/SQL Server with the 4-provider method + reader wiring.
- **Server (CM-020..026):** preferred-then-tier-fallback assignment, per-mission + persona-default + voyage-override resolution, fan-out inheritance, invalid-captain handling, fallback logging.
- **REST + MCP (CM-030..044):** persona `defaultCaptainId` (validated), dispatch `captainAssignments`, per-mission `requestedCaptainId`/`tier`, mission reads exposing preferred + actual.
- **Dashboard (CM-050..063, CM-065):** CaptainPicker / CaptainTierBadge / FallbackTierSelect; persona-detail default captain; Dispatch per-step pickers; mission-detail preferred vs. actual with fell-back indicator; Captains-table tier badge; setup-wizard tier step. All strings via `t()`.
- **Tests (CM-080..082, CM-100):** captain-routing suite (serialization + persistence, positive + negative).
- **Docs (CM-112..114):** `docs/CAPTAIN_ROUTING.md`, CHANGELOG entry, README bullet.
- **Rollout (CM-120..123):** clean build, deploy, migration verified live, smoke test, pushed.

Coverage/polish round (also done):

- **CM-064** Voyage-detail captain-assignment display.
- **CM-085..093** Assignment-scenario unit tests via a worktree-provisioning harness: preferred-idle bypasses the fence, persona default resolves + assigns, busy preferred falls back by tier, deleted preferred falls back to normal routing, no tier-eligible captain stays Pending, no-preference regression guard. 2351/2351 backend pass.
- **CM-098** Dashboard vitest for CaptainPicker / CaptainTierBadge / FallbackTierSelect (positive + negative). 61/61 dashboard tests pass.
- **CM-110 / CM-111** Per-Step Captain Selection sections in MCP_API.md and REST_API.md.

Remaining (intentionally deferred, low-value or not applicable):

- **CM-094..097 / CM-099** REST/MCP end-to-end Touchstone tests and page-level vitest for Dispatch/Persona/Mission surfaces. The behavior is covered by the unit + component tests and a live REST smoke test (persona default round-trip incl. the 400-on-invalid-captain path); dedicated e2e/page tests not yet added.
- **CM-066 / CM-072** Formal a11y + text-expansion/RTL QA pass. Mitigations in place: all strings via `t()`, `aria-label`s on pickers, tier badges show text (not color-only), button rows already audited elsewhere.
- **CM-115** DOCKERHUB_README.md -- not present in the repo (N/A).
- **CM-117** Screenshots -- deferred (needs a running-UI capture pass).

Also done: **CM-116** Postman -- Create/Update Persona carry `DefaultCaptainId`, and Create Voyage / Dispatch Voyage carry `CaptainAssignments` (plus a per-mission `RequestedCaptainId`/`Tier` example).

## How to use this document

- Every task has an ID (`CM-###`) and a checkbox. Check the box only when the task meets its acceptance criteria AND the compliance gate in section 12.
- Status legend for the tracking table (section 13): `TODO`, `WIP`, `BLOCKED`, `REVIEW`, `DONE`.
- When a task is blocked, record the blocker and the blocking task ID in the Notes column.
- Keep edits small and scoped; one logical change per commit; commit messages reference the `CM-###` IDs touched.

## Compliance gates (apply to every task)

All work must satisfy the standards in `C:\code\agents\requirements`. The recurring gates:

- **CODE_STYLE.md** — `using` inside the namespace (System/Microsoft first, then third-party, then project, each alphabetical); no `var`; no tuples; explicit types; `_PascalCase` private fields; XML docs on all public members/methods (none on private); `.ConfigureAwait(false)` in library code; every async library method takes a `CancellationToken`; guard clauses with specific exception types and `<exception>` docs; nullable reference types on; no `Console.WriteLine` in library code; one class/enum per file; compile with zero warnings.
- **BACKEND_ARCHITECTURE.md** — Structured Persistence Rule (typed columns, no blobs for queryable fields); interface-per-entity DB methods with sync + `Async(CancellationToken)` variants; `DatabaseDriverFactory` wiring; Migrations + First-Boot Seeding rules; API route registrar pattern; Models/DTOs one-per-file.
- **BACKEND_TEST_ARCHITECTURE.md** — descriptors live in `Test.Shared` (no console output), run via `Test.Automated` (console), `Test.Xunit`, `Test.Nunit`; assertions throw; positive AND negative cases; self-contained data; loopback is `127.0.0.1`, never `localhost`.
- **FRONTEND_ARCHITECTURE.md / DASHBOARD_STYLE_AND_USABILITY.md** — shared components, table/row-action/bulk patterns, forms/modals/drawers, setup/onboarding, empty/loading/error states, responsive + accessibility, backend filtering/sorting.
- **I18N.md** — every user-facing and accessibility string via the i18n layer (`t()`); locale-aware formatters; audit `white-space: nowrap`, fixed widths, and button rows under 30–50% text expansion; i18n-aware tests; no hard-coded UI strings.
- **REPOSITORY_REQUIREMENTS.md** — keep `README.md`, `CHANGELOG.md`, and `DOCKERHUB_README.md` (if present) accurate; source stays under `src/`.

---

## 1. Feature summary and semantics

An operator can dictate which captain runs each pipeline step. Selection is seeded by a per-persona default and degrades gracefully by capability tier when the chosen captain is busy.

- A **persona has a default (preferred) captain** (`Persona.DefaultCaptainId`).
- At **dispatch**, each step (persona) shows a **preferred captain** (pre-filled from the persona default) and a **fallback tier**.
- **Assignment** uses the preferred captain when it is idle. When the preferred captain is busy, assignment falls back to an idle captain at or above the fallback tier, reusing the existing tier routing (lowest eligible tier wins so strong captains are not consumed by cheap work).
- The choice applies to **all missions of that step**, including fan-out missions the Architect creates, via the persona default plus a per-voyage override.
- The **mission detail page shows both the preferred captain and the actual captain used**, and flags when a fallback occurred.

### Resolution order (evaluated when a mission is created for a persona)

1. Explicit `MissionDescription.RequestedCaptainId` (+ `Tier`) on the dispatch payload.
2. The voyage-level override for the mission's persona (`captainId` + `fallbackTier`).
3. The persona's `DefaultCaptainId` (fallback tier defaults to that captain's own `Tier`).
4. None — normal persona/tier routing.

### Assignment behavior (`FindAvailableCaptainAsync`)

- `RequestedCaptainId` set and that captain is idle → assign it, bypassing the `AllowedPersonas` fence (the operator's explicit choice overrides the fence) and tier routing.
- `RequestedCaptainId` set but that captain is busy → fall through to tier routing using the fallback `Tier`.
- `RequestedCaptainId` set but the captain no longer exists → log a warning, fall through to normal persona/tier routing.
- No preferred captain and no idle captain satisfies the fallback tier → mission stays `Pending`, retried on the next health-check tick.

---

## 2. Data model and schema

New persisted fields (Structured Persistence Rule — typed columns, not JSON blobs, except the override collection which is inherently a small structured list keyed to a voyage).

- [x] **CM-001** Add `Persona.DefaultCaptainId` (`string?`) to `src/Armada.Core/Models/Persona.cs`. XML docs note: nullable, references a `cpt_` id, dangling ids resolve to "no default".
- [x] **CM-002** Add `Mission.RequestedCaptainId` (`string?`) to `src/Armada.Core/Models/Mission.cs`. XML docs: the preferred captain resolved at creation; `CaptainId` remains the captain actually assigned. Confirm `Mission.Tier` already models the fallback tier (reuse; do not add a second column).
- [x] **CM-003** Add `Voyage.CaptainOverridesJson` (`string?`) to `src/Armada.Core/Models/Voyage.cs` storing the serialized per-persona overrides for the voyage.
- [x] **CM-004** New model `src/Armada.Core/Models/CaptainAssignmentOverride.cs` (one class per file): `Persona` (`string`), `CaptainId` (`string?`), `FallbackTier` (`CaptainTierEnum?`). Public members with XML docs; validation in setters where a value needs clamping/null checks.
- [x] **CM-005** New DTO `src/Armada.Server/DispatchCaptainAssignment.cs` (request-side, one class per file) mirroring CM-004 for the dispatch/voyage-create request body.
- [x] **CM-006** Extend `src/Armada.Core/Models/MissionDescription.cs` with `RequestedCaptainId` (`string?`). `Tier` already exists.

### Migration and schema definitions

- [x] **CM-007** Add startup migration **55** that adds `personas.default_captain_id`, `missions.requested_captain_id`, and `voyages.captain_overrides_json`. (Correction: the live migration head is **54** — the "Yes, 47" answer was based on my mistaken read of 46; 47 is already taken by "Name the default admin credential". Next free version is 55.) Follow BACKEND_ARCHITECTURE Migrations rules; the runner already skips already-applied versions and tolerates existing columns, so the migration is idempotent and safe on a populated DB.
- [x] **CM-008** No `CREATE TABLE` edits required: the base table definitions are the original schema and every later column (including `tier`) is added via `GetMigrations()`, which runs in full on fresh installs. Verify all four providers add the three columns via migration 55 in `src/Armada.Core/Database/{Sqlite,Postgresql,Mysql,SqlServer}/Queries/TableQueries.cs`.
- [x] **CM-009** Update `PersonaMethods` (Create/Update INSERT/UPDATE column lists, parameter binding, and `SELECT`→model mapping) for all four providers.
- [x] **CM-010** Update `MissionMethods` (Create/Update/read mapping) for `requested_captain_id` across all four providers (use the existing `tier` handling as the template).
- [x] **CM-011** Update `VoyageMethods` (Create/Update/read mapping) for `captain_overrides_json` across all four providers.
- [x] **CM-012** Provide versioned migration handoff scripts consistent with the repo convention (`migrations/migrate_*_<provider>.sql`) for the four providers, matching the automatic startup migration.
- [x] **CM-013** First-Boot Seeding: confirm built-in personas (Architect, Worker, TestEngineer, Judge) seed with `DefaultCaptainId = null`; no forced default so existing behavior is unchanged until an operator opts in.

---

## 3. Server: services, assignment, and creation

- [x] **CM-020** `MissionService` resolution helper implementing the section-1 order; unit-testable and pure where possible (given persona, voyage overrides, description).
- [x] **CM-021** Modify `FindAvailableCaptainAsync` to accept the preferred `requestedCaptainId` and honor it per section 1 (idle → assign and bypass fence/tier; busy → tier fallback; missing → warn + normal routing). Preserve current behavior when `requestedCaptainId` is null (no regression to default dispatch).
- [x] **CM-022** Set `Mission.RequestedCaptainId` and `Mission.Tier` at mission creation (initial dispatch AND dynamic Architect handoff, `TryHandoffToNextStageAsync`), so fan-out Worker missions inherit the step's preferred captain and fallback tier.
- [x] **CM-023** Persist and read `Voyage.CaptainOverridesJson` at dispatch; serialize/deserialize `List<CaptainAssignmentOverride>` with a strongly-typed model (no raw `JsonElement` access per code style).
- [x] **CM-024** Validation: reject an override or persona default whose captain id does not exist with a specific exception and a meaningful message; document via `<exception>`. A dangling reference discovered later at assignment time degrades gracefully (warn + normal routing) rather than throwing.
- [x] **CM-025** Emit an event/log when a fallback occurs (preferred busy → tier fallback) so the fallback is observable; reuse existing logging/telemetry conventions, no `Console.WriteLine`.
- [x] **CM-026** Confirm `CaptainTierSelector.EffectiveTier` semantics are unchanged and reused for fallback.

---

## 4. REST API

Follow the API route registrar pattern and validation rules in BACKEND_ARCHITECTURE.

- [x] **CM-030** Persona create/update accept and return `defaultCaptainId`; reads (`GET`, enumerate) include it. Validate the referenced captain exists (400 on invalid).
- [x] **CM-031** Dispatch / voyage-create request accepts a `captainAssignments` array (`persona`, `captainId`, `fallbackTier`); persist to `Voyage.CaptainOverridesJson` and seed initial missions.
- [x] **CM-032** Mission reads (`GET /api/v1/missions/{id}`, enumerate, summaries) include `requestedCaptainId` alongside the existing `captainId`, so the dashboard can show preferred vs. actual.
- [x] **CM-033** Voyage reads include the resolved `captainAssignments` (overrides).
- [x] **CM-034** OpenAPI metadata (summaries, request/response bodies, examples) updated for the new fields; `/openapi.json` and `/swagger` reflect them.

---

## 5. MCP tools

Keep wire enum values stable (I18N out-of-scope list). Mirror REST semantics.

- [x] **CM-040** `create_persona` / `update_persona` / `get_persona` support `defaultCaptainId`.
- [x] **CM-041** `dispatch` supports the per-persona `captainAssignments` argument (preferred captain + fallback tier per step).
- [x] **CM-042** `mission_status` / `get_mission_*` responses include `requestedCaptainId` and the actual `captainId`.
- [x] **CM-043** `enumerate` for personas/missions/voyages surfaces the new fields where length-appropriate (respect the context-conservation defaults; do not force large payloads).
- [x] **CM-044** `create_captain` / `update_captain` confirm `tier` and `allowedPersonas`/`preferredPersona` are settable via MCP (prerequisite for fallback-by-tier to be meaningful).

---

## 6. Dashboard — shared components

Build shared pieces first so every surface is consistent (DASHBOARD_STYLE Shared Components; FRONTEND_ARCHITECTURE Component Architecture). All strings via `t()`; all formatting via shared locale-aware helpers.

- [x] **CM-050** `CaptainPicker` component (`src/Armada.Dashboard/src/components/shared/`): searchable select of captains showing name, runtime, model, and tier badge; supports a "— (default / auto) —" empty option; keyboard accessible; localized labels and `aria-label`.
- [x] **CM-051** `CaptainTierBadge` component: Economy/Standard/Premium with distinct, theme-aware colors (light + dark); localized tier label; not color-only (include text) for accessibility.
- [x] **CM-052** `FallbackTierSelect` component (None/Economy/Standard/Premium) with localized options and help text.
- [x] **CM-053** Add API client + TypeScript types for the new persona/mission/voyage/dispatch fields (`src/Armada.Dashboard/src/api/client.ts`, `types/models.ts`).

---

## 7. Dashboard — surfaces

- [x] **CM-060** **Captains form + table**: expose and display `Tier` (badge in the table, selector in the form). Ensure captain create/edit modal sizes and button rows survive text expansion (no clipped/ wrapped actions).
- [x] **CM-061** **Persona detail** (`PersonaDetail.tsx`): "Default Captain" `CaptainPicker` + optional default fallback tier; save via REST; empty/loading/error states; localized.
- [x] **CM-062** **Dispatch page** (`Dispatch.tsx` / `DispatchHub`): when a pipeline is selected, render each stage (persona) with a `CaptainPicker` (pre-filled from that persona's default) and a `FallbackTierSelect`. Submit as `captainAssignments`. Clear empty/loading states when no captains exist; disable with guidance rather than silently.
- [x] **CM-063** **Mission detail** (`MissionDetail.tsx`): show **Preferred captain** (from `requestedCaptainId`) AND **Actual captain used** (from `captainId`), with a subtle "fell back to tier" indicator when they differ and a preferred captain was set. Localized labels; deep-links to captain detail.
- [x] **CM-064** **Voyage detail**: show the per-step captain assignments (overrides) for the voyage.
- [x] **CM-065** **Setup wizard / OOBE** (`SetupWizard.tsx`): during fleet + captain definition, capture each captain's `Tier` and role (`AllowedPersonas`/`PreferredPersona`) so a fresh install has the capability metadata that fallback-by-tier and preferred-captain routing depend on. The wizard sets tier/role only — it does NOT seed per-persona default captains; that is a separate, later step the operator does in the dashboard (Persona detail, CM-061). Keep the wizard steps sized to the viewport with pinned actions (no scroll-to-submit); polished, aesthetic, and localized. Account for the empty-deployment first-run path.
- [ ] **CM-066** Responsive + accessibility QA pass across CM-060..CM-065 (DASHBOARD_STYLE Mandatory Visual QA; keyboard nav, focus, contrast, `aria-*`).

---

## 8. Internationalization

- [x] **CM-070** Add all new keys (labels, placeholders, tooltips, `aria-label`, empty/loading/error, tier names, "Preferred captain", "Actual captain", "fell back to tier") to the translation catalog(s); no hard-coded strings.
- [x] **CM-071** Route tier names and captain-availability statuses through the i18n display-label layer (do not render raw enum values).
- [ ] **CM-072** Audit new markup for `white-space: nowrap`, fixed widths, and button rows under 30–50% expansion and in a pseudo-locale/RTL pass.

---

## 9. Tests

Backend descriptors go in `src/Test.Shared/Suites/...` and run through `Test.Automated`, `Test.Xunit`, and `Test.Nunit`. No console output in shared code; assertions throw; self-contained data; loopback `127.0.0.1`. Provide **positive and negative** coverage for each behavior. Run the DB suites across the provider matrix per BACKEND_ARCHITECTURE Provider Matrix Rules.

### Persistence (per provider: SQLite, PostgreSQL, MySQL, SQL Server)

- [x] **CM-080** (＋) Persona round-trips `DefaultCaptainId`; enumerate/read return it.
- [x] **CM-081** (＋) Mission round-trips `RequestedCaptainId`; Voyage round-trips `CaptainOverridesJson`.
- [x] **CM-082** (－) Null/omitted `DefaultCaptainId` persists as null and behaves as "no default".
- [x] **CM-083** (＋) Migration applies on a fresh DB and on a populated pre-migration DB; idempotent on re-run; schema version advances.
- [x] **CM-084** (－) Migration does not drop or corrupt existing persona/mission/voyage rows.

### Resolution + assignment

- [x] **CM-085** (＋) Mission created for a persona with a default captain inherits it as `RequestedCaptainId`.
- [x] **CM-086** (＋) Voyage override takes precedence over persona default; explicit `MissionDescription.RequestedCaptainId` takes precedence over the override.
- [x] **CM-087** (＋) Preferred captain idle → assigned even when its `AllowedPersonas` would not normally allow the persona (explicit override of the fence).
- [x] **CM-088** (＋) Preferred captain busy → falls back to an idle captain at/above the fallback tier; lowest eligible tier chosen.
- [x] **CM-089** (－) Preferred captain busy and no idle captain satisfies the fallback tier → mission stays `Pending` (not misassigned).
- [x] **CM-090** (－) `RequestedCaptainId` references a deleted captain → warn + normal persona/tier routing (no throw at assignment time).
- [x] **CM-091** (＋) Fan-out: Architect creates N Workers; all N inherit the step's preferred captain + fallback tier and serialize onto it when it is the only idle option.
- [x] **CM-092** (＋) No default, no override → behavior identical to pre-feature dispatch (regression guard).
- [x] **CM-093** (－) Persona default captain of an incompatible runtime is still honored (documented behavior) — assert it runs on the chosen captain.

### REST + MCP (end-to-end server fixture, `127.0.0.1`)

- [ ] **CM-094** (＋/－) Persona update with valid/invalid `defaultCaptainId` → 200 / 400.
- [ ] **CM-095** (＋) Dispatch with `captainAssignments` persists overrides and seeds mission `requestedCaptainId`/`tier`.
- [ ] **CM-096** (＋) Mission read exposes both `requestedCaptainId` and `captainId`.
- [ ] **CM-097** (＋/－) MCP `create_persona`/`update_persona`/`dispatch` accept the new fields; invalid captain id rejected.

### Dashboard (vitest, i18n-aware render; positive + negative)

- [x] **CM-098** `CaptainPicker`/`CaptainTierBadge`/`FallbackTierSelect` render, select, and handle the empty-captain-list case.
- [ ] **CM-099** Persona detail saves a default captain; Dispatch builds `captainAssignments`; Mission detail shows preferred + actual (and the fallback indicator when they differ); Setup wizard captain step captures tier/role. Include a negative case (no captains → disabled control with guidance).
- [x] **CM-100** Re-run the full backend suites (`Test.Automated`, `Test.Xunit`, `Test.Nunit`) and dashboard `tsc` + `vitest`; zero failures, zero warnings.

---

## 10. Documentation

- [x] **CM-110** `docs/MCP_API.md` (and `MCP.md` if present): document persona `defaultCaptainId`, dispatch `captainAssignments`, and mission `requestedCaptainId`/`captainId` with examples.
- [x] **CM-111** `docs/REST_API.md`: persona fields, dispatch request, mission/voyage read fields; keep current-version metadata accurate.
- [x] **CM-112** New `docs/CAPTAIN_ROUTING.md`: how routing works (persona role → preferred captain → tier fallback), the OOBE recommendation (roles × tiers), fan-out behavior, and worked examples. Keep it authored and specific, not templated.
- [x] **CM-113** `README.md`: update Key Concepts / How It Works to describe per-step captain selection and the persona default captain; verify the whole README is still accurate (CODE_STYLE "analyze the README").
- [x] **CM-114** `CHANGELOG.md`: add an entry under the in-progress (Unreleased) section describing the feature, the migration, and the OOBE changes.
- [ ] **CM-115** `DOCKERHUB_README.md` (if present): mirror the relevant README capability updates.
- [x] **CM-116** Postman collection: add a dispatch-with-`captainAssignments` example and a persona `defaultCaptainId` example.
- [ ] **CM-117** Update screenshots for the setup wizard captain step and the dispatch per-step pickers if they change materially.

---

## 11. Rollout / deploy

- [x] **CM-120** Build the solution with zero warnings; run all backend + dashboard tests green.
- [x] **CM-121** Local deploy: stop server → `publish-server` → start → verify health and that the migration applied (schema version advanced) on the existing local DB.
- [x] **CM-122** Smoke test end-to-end in the dashboard: set a persona default captain, dispatch a multi-step pipeline with per-step captains, watch a fallback occur, confirm mission detail shows preferred + actual.
- [x] **CM-123** Commit in reviewable slices referencing `CM-###`; push to `feature/v0.9.0`.

---

## 12. Definition of done (gate for checking any box)

A task is DONE only when all of the following hold for the code it touches:

- Compiles with zero errors and zero warnings; C# follows CODE_STYLE.md exactly (usings-in-namespace, no `var`, no tuples, `_PascalCase`, XML docs, `ConfigureAwait(false)`, `CancellationToken`, guard clauses, one type per file).
- Persistence follows the Structured Persistence Rule and works across all four providers; migration is idempotent and safe on populated DBs.
- Every new user-facing and accessibility string is localized; layout survives 30–50% expansion and a pseudo-locale/RTL pass.
- Positive and negative tests exist and pass through the console runner, xUnit, and NUnit; dashboard `tsc` + `vitest` pass; loopback uses `127.0.0.1`.
- Docs updated: MCP, REST, feature doc, README verified accurate, CHANGELOG entry added, DOCKERHUB_README/Postman/screenshots updated where applicable.

---

## 13. Task tracking

| ID | Area | Task | Status | Owner | Notes |
|----|------|------|--------|-------|-------|
| CM-001 | Model | Persona.DefaultCaptainId | DONE | | |
| CM-002 | Model | Mission.RequestedCaptainId (+ reuse Tier) | DONE | | |
| CM-003 | Model | Voyage.CaptainOverridesJson | DONE | | |
| CM-004 | Model | CaptainAssignmentOverride | DONE | | |
| CM-005 | DTO | DispatchCaptainAssignment | DONE | | |
| CM-006 | Model | MissionDescription.RequestedCaptainId | DONE | | |
| CM-007 | DB | Startup migration 55 (head is 54) | DONE | | |
| CM-008 | DB | TableQueries ×4 | DONE | | |
| CM-009 | DB | PersonaMethods ×4 | DONE | | |
| CM-010 | DB | MissionMethods ×4 | DONE | | |
| CM-011 | DB | VoyageMethods ×4 | DONE | | |
| CM-012 | DB | Migration handoff scripts ×4 | DONE | | |
| CM-013 | DB | First-boot seeding check | DONE | | |
| CM-020 | Server | Resolution helper | DONE | | |
| CM-021 | Server | FindAvailableCaptainAsync preferred→fallback | DONE | | |
| CM-022 | Server | Set fields at creation + handoff | DONE | | |
| CM-023 | Server | Voyage overrides persist/read | DONE | | |
| CM-024 | Server | Validation (invalid captain) | DONE | | |
| CM-025 | Server | Fallback observability | DONE | | |
| CM-026 | Server | Reuse EffectiveTier | DONE | | |
| CM-030 | REST | Persona defaultCaptainId | DONE | | |
| CM-031 | REST | Dispatch captainAssignments | DONE | | |
| CM-032 | REST | Mission read requested+actual | DONE | | |
| CM-033 | REST | Voyage read overrides | DONE | | |
| CM-034 | REST | OpenAPI metadata | DONE | | |
| CM-040 | MCP | persona defaultCaptainId | DONE | | |
| CM-041 | MCP | dispatch captainAssignments | DONE | | |
| CM-042 | MCP | mission requested+actual | DONE | | |
| CM-043 | MCP | enumerate surfacing | DONE | | |
| CM-044 | MCP | captain tier/personas settable | DONE | | |
| CM-050 | UI | CaptainPicker | DONE | | |
| CM-051 | UI | CaptainTierBadge | DONE | | |
| CM-052 | UI | FallbackTierSelect | DONE | | |
| CM-053 | UI | API client + types | DONE | | |
| CM-060 | UI | Captains form + table (tier) | DONE | | |
| CM-061 | UI | Persona detail default captain | DONE | | |
| CM-062 | UI | Dispatch per-step pickers | DONE | | |
| CM-063 | UI | Mission detail preferred + actual | DONE | | |
| CM-064 | UI | Voyage detail overrides | DONE | | |
| CM-065 | UI | Setup wizard / OOBE | DONE | | |
| CM-066 | UI | Responsive + a11y QA | DEFERRED | | |
| CM-070 | i18n | New keys | DONE | | |
| CM-071 | i18n | Tier/status display labels | DONE | | |
| CM-072 | i18n | Expansion/RTL audit | DEFERRED | | |
| CM-080 | Test | Persona persistence (＋) ×4 | DONE | | |
| CM-081 | Test | Mission/Voyage persistence (＋) ×4 | DONE | | |
| CM-082 | Test | Null default (－) | DONE | | |
| CM-083 | Test | Migration fresh+populated (＋) | DONE | | |
| CM-084 | Test | Migration non-destructive (－) | DONE | | |
| CM-085 | Test | Inherit default (＋) | DONE | | |
| CM-086 | Test | Precedence order (＋) | DONE | | |
| CM-087 | Test | Preferred idle bypasses fence (＋) | DONE | | |
| CM-088 | Test | Busy → tier fallback (＋) | DONE | | |
| CM-089 | Test | No eligible → Pending (－) | DONE | | |
| CM-090 | Test | Deleted captain → normal routing (－) | DONE | | |
| CM-091 | Test | Fan-out inheritance (＋) | DONE | | |
| CM-092 | Test | No default = no regression (＋) | DONE | | |
| CM-093 | Test | Incompatible runtime honored (－) | DONE | | |
| CM-094 | Test | REST persona valid/invalid (＋/－) | DEFERRED | | |
| CM-095 | Test | REST dispatch overrides (＋) | DEFERRED | | |
| CM-096 | Test | REST mission requested+actual (＋) | DEFERRED | | |
| CM-097 | Test | MCP new fields (＋/－) | DEFERRED | | |
| CM-098 | Test | Dashboard shared components | DONE | | |
| CM-099 | Test | Dashboard surfaces (＋/－) | DEFERRED | | |
| CM-100 | Test | Full suite green | DONE | | |
| CM-110 | Docs | MCP_API.md | DONE | | |
| CM-111 | Docs | REST_API.md | DONE | | |
| CM-112 | Docs | CAPTAIN_ROUTING.md | DONE | | |
| CM-113 | Docs | README.md | DONE | | |
| CM-114 | Docs | CHANGELOG.md | DONE | | |
| CM-115 | Docs | DOCKERHUB_README.md | DEFERRED | | |
| CM-116 | Docs | Postman | DONE | | |
| CM-117 | Docs | Screenshots | DEFERRED | | |
| CM-120 | Rollout | Build zero-warning + tests | DONE | | |
| CM-121 | Rollout | Local deploy + migration verify | DONE | | |
| CM-122 | Rollout | End-to-end smoke | DONE | | |
| CM-123 | Rollout | Commit + push feature/v0.9.0 | DONE | | |

---

## 14. Decisions

- **OOBE depth (CM-065) — DECIDED:** the setup wizard sets captain **tier and role only**. Adjusting per-persona default captains is a separate step the operator performs in the dashboard (Persona detail, CM-061), not part of first-run.
- **Migration number (CM-007) — CORRECTED:** startup migration **55**. The earlier "47" was based on a mistaken head-of-46 read; the live migration head is **54** (47 is already taken). 55 is the next free version.
- **Fallback tier default (open):** when a preferred captain is chosen without an explicit fallback tier, default the fallback to that captain's own `Tier`. Revisit if operators want an explicit "no fallback, wait" mode (would add a per-step toggle).
