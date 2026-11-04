# Usage-based captain selection and decision history

## Status and release scope

Planned for **v1.3.0**, entirely on **`feature/v1.3.0`**. Baseline inspected: `7c1794486` (2026-11-03). Implementation progress is tracked below. Recheck the branch, migrations and API inventory before starting each milestone because other v1.3.0 work is in progress.

Armada should select a qualified captain with sufficient account headroom and retain evidence explaining that choice. An operator must be able to answer both “Why this captain and model?” and “Why is this mission waiting?” without reconstructing scheduler logs. Usage-aware selection extends existing routing, quota holds and admission; it does not introduce a second scheduler.

The required v1.3.0 deliverable comprises U0 through U6 below: observation mode, deterministic enforcement, durable decisions, reservations, diagnostics, all supported client surfaces, and validation. Adaptive consumption prediction is a later extension, not a prerequisite or a feature to advertise as shipped. This plan adds to [CONCURRENCY_IMPROVEMENTS.md](CONCURRENCY_IMPROVEMENTS.md); its capacity, lifecycle and ownership rules remain authoritative.

## Implementation progress

- [x] U0 baseline/contract review and executable normalized evidence fixtures: [review](docs/usage-selection/BASELINE_AND_CONTRACT.md).
- [x] Pure policy foundation: typed candidate/window/results, bounded settings defaults and deterministic headroom-band ranking. Connected to Observe dispatch and read-only comparisons; actual routing remains unchanged in Observe.
- [x] Configurable strategies integrated with Observe/Enforce admission, live server settings, Dashboard, REST/MCP and typed .NET clients; Helm inspection is available. Remaining native operator/settings workflows are tracked in U4.
- [x] U1 storage foundation: migrations 123/124 on all providers, typed immutable history/evidence, policy/strategy revisions, scoped paging, reservation CAS/retention, capture-start timing and atomic Observe-mode claim/history primitives. See [validation](docs/usage-selection/U1_STORAGE_VALIDATION.md).
- [x] U1 live policy coordinator and Dashboard settings controls, REST/MCP updates, environment metadata, optimistic revisions and recoverable persistence. See [segment notes](docs/usage-selection/LIVE_CONFIGURATION_AND_OBSERVE.md).
- [x] U1 complete: provider-neutral storage/migrations and scoped ledger guards, live Dashboard/REST/MCP settings with persistence/revision/override validation, and four-provider lifecycle tests. Remaining native settings/recovery workflows belong to U4.
- [x] U2 complete (`048629f8a`, build-all v1.3.0 successful): Observe dispatch/repair, bounded evidence loading, scoped history/preview/client inspection, restart/raw-capture pruning and responsive comparison pass. Preview mirrors retry/placement-policy readiness without provisioning a dock; a proposed choice is never labeled an admitted execution.
- [x] U3 complete (`a15a707f0`, snapshot ownership follow-up `c1526497f`): atomic admission/repair, account-wide cooldowns, owned-receipt cleanup, settlement/restart guards and audited recovery permissions. All four providers, Dashboard recovery UX, full regression and build-all v1.3.0 pass. See [U3 validation](docs/usage-selection/U3_RECOVERY_VALIDATION.md). Correction from the takeover review: every reservation uses the single `continuous` window generation. Provider adapters do not yet declare fixed versus rolling reset semantics, so old-window debt is held until a post-execution capture settles it, never closed by a reset. This is conservative but incomplete; see the U5 open item.
- [x] Takeover review remediation (2026-11-03), detailed below: real captain assignment time (migration 128), broader persistence-failure handling, deterministic blocker eviction, stable outcome/event/reason constants, readable policy and enforcement validator, committed review screenshots removed. All four providers pass.
- [ ] U4 complete remaining native operator/settings workflows, richer reservation/wait views, Events/Needs You and WebSocket invalidation.
- [ ] U5 observability, provider/stress and simulated-user validation.
- [ ] U6 final release integration and handoff.

U0's design/UX review and implementation map are recorded in the linked review. The policy suite covers 28 cases; existing quota/default regression suites are run alongside it. No history, API, UI or admission milestone is complete based on this foundation.

### Current wave: enforcement and mission inspection (2026-11-03)

- [x] Atomic Enforce dispatch/repair and same-account races, transaction-time source/membership/version/budget checks, intended model binding, replay and rollback.
- [x] PostgreSQL/MySQL mission readers retain ownership through read/update/repair; provider matrix includes mission CRUD and migration replay/hygiene.
- [x] Credential-host placement pinning with existing branch continuity and Harbor permission/policy gates; record deduplicated placement waits.
- [x] Migration 126 operational debt for Disabled/legacy/unknown executions, startup adoption, guarded reconciliation and active-capture proof. Do not invent historical selection decisions or balances.
- [x] Shared Dashboard mission card, history/filter/paging/preview, safe historical names/links, Core SDK, Helm inspection and generic MCP enumeration with scope before paging.
- [x] Shared semantic English source and eight translated locale catalogs; native bundler access to shared resources.
- [x] Enforcement wave committed/pushed (`e3bd1abc4`, container follow-ups `d2274d9d6`/`cc7c33a92`); `build-all v1.3.0` completed for server/dashboard/proxy and VSIX. Exact evidence and remaining limits: [implementation guide](docs/usage-selection/ENFORCEMENT_AND_MISSION_INSPECTION.md).

### Follow-up: native inspection and regression completion

- [x] Mobile mission overview selection section uses existing native controls for recorded model provenance, comparisons, paged/filterable history, retained detail/JSON and blocked read-only previews. Deleted captains have no link; revoked access clears evidence. Polling pauses off-screen/background.
- [x] Shared TUI/Mobile API parity inventories include all five reads. Native recovery/settings controls and the TUI mission inspection workflow remain U4 work.
- [x] Native tests/type checking/lint and shared-module lint pass; terminal display/focus/parity/localization checks pass with an explicit headless default palette, independent of shell `NO_COLOR`.
- [x] Native follow-up committed/pushed as `1f3b59b78`; `build-all v1.3.0` completed for all images and VSIX.
- [x] U5 omnibus regression follow-up: source `9214ed53f` passes 6,885 tests with zero failures and 14 skips on .NET 10. The contract crawl is paced below the unchanged production MCP limit; owned test sessions are closed. Broader U5 stress, platform and upgrade evidence remains open.

## 1. Requirements and implementation rules

The normative requirements live in `~/Code/Agents/requirements`. The table maps those standards to deliverables so developers can review compliance. Read the named files before implementing the affected milestone; do not rely on this table as a replacement for them.

| Requirement source | Application to this work |
|---|---|
| `BACKEND_ARCHITECTURE.md` | Typed contracts and settings; service-owned policy; domain database interfaces; structured tables and columns; all four database providers; Watson hosting, authorization and cancellation. No JSON document store for policy, candidates or reservations. |
| `CODE_STYLE.md` | One class/enum per file, explicit types, namespace-first usings, documented public contracts, `_PascalCase` private fields, validated settings with documented bounds, cancellation and appropriate `ConfigureAwait(false)`, disposal, no library console output or tuples. |
| `AUTHENTICATION.md` | Authenticate and authorize every transport server-side; tenant/user/resource scope in storage queries; explicit deny wins; audit privileged changes. IDs must not bypass ownership. |
| `BACKEND_TEST_ARCHITECTURE.md` | Shared Touchstone descriptors, console/xUnit/NUnit adapters, self-contained fixtures, all providers, full CI on every push/PR, supported OS/framework matrix; document and preserve the project's actual AOT support. |
| `FRONTEND_ARCHITECTURE.md`, `DASHBOARD_STYLE_AND_USABILITY.md` | Shared API client, shell, breadcrumbs, tables, filters, refresh and modals; responsive accessible workflows; role-aware controls; no orphan routes or raw-JSON-only settings. |
| `I18N.md` | Stable semantic reason codes and translation keys; shared catalogs, explicit-locale formatting, all supported locales, expansion/CJK/RTL checks and accessibility strings. Do not store translated explanations. |
| `TELEMETRY_REQUIREMENTS.md` | Application meters/spans, bounded labels, best-effort exporters, Prometheus/Tempo and provisioned Grafana; preserve Watson telemetry and correlation. |
| `BENCHMARKING.md` | Isolated public-surface benchmark, reproducible data/commands, cold and warm results, no production credentials or direct database measurement as the primary benchmark. |
| `SIMULATED_USER_TESTING.md` | Clean documented setup; public-surface inventory and journeys; evidence/coverage report; observe and report findings before fixing them. Respect the user's existing authorization when deciding which findings may be fixed. |
| `REPOSITORY_REQUIREMENTS.md` | Source under `src/`; synchronized REST/MCP/OpenAPI/Postman/SDK docs; CI and Docker conventions; no unrelated repository restructuring. |
| `VERSIONING.md`, `INSTALLERS.md` | Keep the user-authorized 1.3.0 versions; preserve Publisher/CI packaging and platform channels. No independent version bump or new installer architecture. |
| `EXAMPLE_APPLICATIONS.md`, `WRITING_DOCUMENTS.md` | Reuse Armada/provider reference patterns, concrete operator examples, human review of docs; no em-dashes. |

### Compatibility preflight

The inspected Armada route registrars use `ApiRequest` and typed `app.Get/Post` convenience registration. The current backend requirements describe direct Watson 7 route tables and explicitly prohibit those conveniences. U0 must establish the installed Watson version and existing adapter boundary, document the discrepancy, and choose a requirements-compliant feature registrar through the existing host. Do not silently copy an incompatible pattern or make a whole-server HTTP migration part of this feature. If the host cannot support compliant registration without that migration, record the concrete dependency and resolve it before the API milestone.

The existing translation system also contains English-sentence keys. New selection strings should use stable semantic keys and the shared runtime; do not undertake unrelated catalog conversion. Retain compatibility with existing clients and serialized enums. Append enum members without renumbering old values. No changes to authentication algorithms, identity derivation or shipping packaging are authorized by this plan.

## 2. Verified starting points

Existing usage capture already handles active harness probes and passive provider observations. Usage routing must reuse its sanitization, freshness and membership boundaries. The policy below is proposed behavior; the files in this table are the implementation starting points, not evidence that the proposal exists.

| Existing area | Starting point and required reuse |
|---|---|
| Shared routing/admission | `src/Armada.Core/Services/MissionService.CaptainAdmission.cs`, `MissionService.CaptainRouting.cs`, `CaptainEligibility.cs`, `MissionService.Admission.cs`, `MissionAdmissionEvaluator.cs`. Dispatch, diagnostics, monitoring and repairs share eligibility. |
| Quota policy | `src/Armada.Core/Services/UsageQuotaPolicy.cs`: authoritative fresh non-derived observations; model applicability; exhausted-window holds; freshness `max(2 × effective capture interval, 120 seconds)` and future-skew protection. |
| Hold ownership | `src/Armada.Core/Services/UsageQuotaHold.cs`: only quota-owned holds may be cleared by quota evidence; older observations cannot replace newer holds. Preserve manual, crash, health and configuration holds. |
| Identity and capture | `UsageAccountResolver.cs`, `UsageCaptureScheduler.cs`, `UsageCaptureService.cs`, `UsageRateLimitHandler.cs`, `src/Armada.Core/Settings/UsageCaptureSettings.cs`. No synchronous probes inside selection locks. |
| Storage | `src/Armada.Core/Models/UsageSnapshot.cs`, `UsageWindow.cs`, `UsageAccountMembership.cs`, plus existing snapshot/member database interfaces and four provider implementations. |
| Existing explanations | `MissionAssignmentBlockerReasonEnum`, existing waiting diagnostics/actions and `MissionService.WorkerEnvelope.cs` / `WorkerEnvelopeDecision`. Preserve envelope decisions and link them instead of replacing their history. |
| Operator surfaces | Mission detail/waiting cards, captain detail, Dashboard Activity > Captain Usage, Settings, Events, Needs You; matching Mobile/TUI/Helm and .NET client operations. |

Migration 122 belongs to the built-in fleet action work at this baseline. Allocate subsequent migration numbers from the branch's actual head when implementing. Never rewrite migrations 1–122 or promise that a specific next number is still available.

## 3. Scope and safety invariants

Usage affects which eligible resource starts the next attempt. It must not reduce the mission's required capabilities, change its persona, waive independent review, bypass an exclusive session, or exceed any vessel/captain/global/runtime/endpoint gate. Existing strict overrides remain strict. Existing explicit non-strict preferences retain their documented fallback behavior; the audit record must distinguish a preference from a pinned assignment.

The following invariants are release blockers:

1. Account-wide windows apply to all verified members; model-scoped windows apply only to proven matching models. Unrecognized scopes are unknown, never assumed account-wide or unlimited.
2. Shared allowance is reserved once per account/window/attempt, not independently per captain. Missing identity never collapses unrelated captains into an “unknown account.”
3. Every admitted attempt in Observe or Enforce mode has a durable admission decision and execution correlation before launch. Diagnostics and previews never create assignments, reservations or audit rows.
4. Reservations and captain claims succeed together or neither succeeds. Retries cannot duplicate either. No process launches before the admission transaction commits.
5. Running work is not preempted when limits, observations, policies or modes change. Usage reservations cannot release sibling captain slots, docks or sessions.
6. Observations are evidence, not an exact promise of remaining tokens or mission cost. Direct Claude Code/Codex activity outside Armada can consume the same account allowance.
7. Telemetry failure never blocks admission. Required decision/reservation persistence failure does block a new admission with a retryable explanation; logging is not a substitute for the ledger.
8. Read APIs and Events cannot reveal another user's candidate/account details merely because they share a tenant or account. Authorization is enforced before pagination and totals.

Out of scope: multi-Admiral distributed scheduling, buying allowance, transferring provider quota, automated credential changes, token-to-percentage conversion without provider evidence, changing a model inside a running process, and guaranteed completion before reset. Non-mission planning/refinement sessions continue their current exclusive-session and quota behavior; they do not gain new mission-style budget reservations in this release. Label that coverage limit rather than implying the ledger measures all consumption.

## 4. Account and window identity

Use the verified effective membership: tenant, credential host, runtime, opaque account key and configuration revision. The capture group key is a probe configuration discriminator, not proof that two captures share an account. Provider names, emails, endpoint URLs and captain names are not allowance identities. Resolve model aliases only through explicit provider/runtime mappings. Mux and automatic provider routing must expose a proven target before applying model-specific allowance; otherwise record unknown model applicability, preserve account-wide gates and avoid claiming model-specific headroom.

Account keys are HMAC-derived using host-local key material, including tenant/runtime/profile context. The same real subscription on two Harbors can therefore appear as distinct accounts. v1.3.0 must disclose this limitation and must not merge them or claim independent allowance from them. Cross-host/provider-account linking requires an explicit verified mapping design and is deferred. If reliable identity is unavailable, rank the candidate as unknown and apply the configured unknown policy.

Define a typed internal `UsageBudgetKey` with the membership identity plus a stable normalized window discriminator, model/scope applicability and reset generation. Preserve source window names as evidence; do not infer a duration from a label. A changed reset timestamp alone is not proof of a new generation for a rolling window. Provider adapters must declare fixed-reset versus rolling semantics; uncertain rollover requests a fresh observation and retains conservative accounting until resolved. Add a nullable observation/probe-start timestamp to capture evidence where it is not recorded today, with all-provider migration/transport mapping. Legacy evidence without trustworthy start timing may rank candidates but cannot settle post-execution debt.

Membership changes close the old identity. Old reservations remain attached to the attempt's original account; new work evaluates the new membership. Never backfill unknown legacy memberships. Cross-user shared-account accounting may aggregate authorized internal reservation amounts, but user-facing output redacts peers' identities and mission IDs.

## 5. Policy settings and rollout defaults

Add strongly typed `UsageSelectionSettings` under the existing settings infrastructure, exposed as the `UsageSelection` settings group. Apply changes live to future admissions with a monotonic revision. A decision captures both algorithm version and effective settings, including relevant inherited routing preferences. Reject non-finite numeric settings and invalid cross-field combinations consistently across file, API and client edits. Environment overrides remain visible/read-only. Do not add an ad hoc configuration file or a second settings API.

| Setting | Proposed default | Bounds and meaning |
|---|---|---|
| `Strategy` | `FirstAvailable` | `FirstAvailable`, `MostRemainingAllowance`, `MostUsedAllowance`. Choice preference is separate from hard usage/capacity gates; persist it with each policy revision. |
| `Mode` | `Observe` | `Disabled`, `Observe`, `Enforce`. New and upgraded installations start Observe until explicitly enabled. Disabled preserves current quota holds and does not write new selection history. |
| `ReservePercent` | 10 | 0–95 percentage points kept unused per applicable percentage window. It is an operator reserve, not a measured mission estimate. |
| `ComfortableHeadroomPercent` | 30 | 1–100, strictly greater than ReservePercent. Used for preference bands. Reject invalid cross-field settings. |
| `PerAttemptReservationPercent` | 2 | 0.1–25 percentage points in each applicable percentage window. Admission needs reserve plus this amount. Same conservative value across task sizes in v1.3.0. |
| `UnknownUsageBehavior` | `AllowDeprioritized` | Or `Wait`. Applies to absent/stale/failed/derived/unsupported evidence; never overrides an authoritative quota hold or provider cooldown. |
| `MaximumFreshnessSeconds` | 900 | 120–86400. Effective freshness is the minimum of this bound and the existing freshness horizon. Do not loosen the quota policy's current protection. |
| `UnknownRetrySeconds` | 60 | 5–3600, with stable bounded jitter to avoid retry storms. |
| `ProviderCooldownSeconds` | 60 | 5–3600 fallback when a classified quota/rate-limit response has no valid retry time. Respect a longer valid provider retry deadline, bounded by existing retry policy. |
| `SettlementLagSeconds` | 60 | 0–3600 after attempt execution ends before a newer observation may absorb unsettled estimated consumption. |
| `MaximumUnsettledSeconds` | 3600 | 60–86400. Aging debt becomes an uncertain-account gate/ranking downgrade, not invented recovered quota. |
| `DecisionRetentionDays` | 90 | 7–1825 for decision history. Retain referenced live attempts/reservations regardless of age. |
| `MaximumCandidates` | 1000 | 1–10000 evaluated candidates per mission; exceeding this blocks with a bounded diagnostic, never silently selects from a truncated set. |

Only system administrators edit deployment-wide settings. No per-user knobs may bypass hard quota holds. A mission/voyage can carry an explicitly authorized fallback permission and a soft-reserve exception; defaults are false. Reuse existing strict/preferred captain semantics rather than adding contradictory switches. A soft-reserve exception requires a tenant administrator or system administrator, a reason, and accounting audit. It cannot bypass exhausted quota, cooldowns, capability, concurrency, security or worker-envelope restrictions.

Observe mode computes the hypothetical enforced outcome but runs the unchanged current selector. The required durable admission write can still defer a launch on persistence failure; document that operational difference from Disabled. Its actual choice and hypothetical choice/outcome are recorded separately. Shadow reservations track actual running attempts and are used only in hypothetical accounting; they must never constrain legacy routing. On enabling Enforce, reconcile all active attempts before admitting new work, including work launched while Disabled. Unknown memberships become uncertain, not fabricated reservations.

## 6. Deterministic selection algorithm

Implement a pure evaluator that takes a timestamped typed snapshot and returns reasons, applicable windows, bands and a stable ordering. The orchestration layer loads evidence and persists outcomes; the evaluator performs no I/O. Use one evaluation path for dispatch, repairs, diagnostics, monitoring and read-only preview. Inject the clock and ordinal tie-break behavior for repeatable tests.

The ordering is deliberate: usage chooses among qualified alternatives, not between qualified and unqualified models. Captain selection is explicitly configurable. FirstAvailable preserves the existing deterministic order within allowed routing buckets after enforcement gates. MostRemainingAllowance prefers comfortable bands and greater effective headroom within comparable groups. MostUsedAllowance consolidates work on the most utilized eligible account while respecting launch reserves, preferring Constrained then Comfortable and keeping Unknown last. Effective utilization includes separately recorded active/unsettled estimates. Never label both directions “most usage.”

1. Resolve dependency/handoff/integration readiness. Stop before selecting a captain when inputs are not ready.
2. Build the current shared eligibility snapshot. Preserve strict assignments, worker envelopes, persona/tier/priority policy, review-author exclusions and runtime/endpoint requirements. Record the relevant routing revision/evaluation evidence when it is already part of eligibility; do not start running model evaluations during dispatch.
3. Apply current hard capacity and non-usage availability gates. Classify authoritative quota holds/provider cooldowns even when they excluded a captain earlier, so diagnostics show the real reason.
4. Load current verified membership, latest applicable authoritative observations, active reservations and unsettled debt in bounded batched reads. A newer failed observation must not make an old successful one fresh. Reject future-skewed, non-finite, malformed, negative or incompatible measurements for routing; preserve a sanitized diagnostic.
5. For each applicable percentage window calculate `available = max(0, 100 - usedPercent - activeReservationPercent - unsettledPercent)`. The tightest applicable window is the bottleneck. A candidate is soft-gated when available is less than `ReservePercent + PerAttemptReservationPercent`; equality is allowed. At or above 100% observed usage is a hard quota gate regardless of reserve exception.
6. Percentages from different providers/windows are not interchangeable money or tokens. Compare bands, not raw cross-provider percentages. Count/limit windows may derive a percentage only when unit, limit and applicability are explicit and valid. Unconvertible windows remain unknown; a proven exhausted count window still hard-gates.
7. For MostRemainingAllowance, within each existing allowed routing preference bucket, rank Comfortable, Constrained, then Unknown. For MostUsedAllowance, rank Constrained, Comfortable, then Unknown. FirstAvailable preserves the supplied existing order among known selectable candidates, followed by explicitly permitted Unknown candidates. Comfortable means sufficient launch headroom and at least ComfortableHeadroomPercent; Constrained means sufficient launch headroom below that band. Unknown participates only under AllowDeprioritized. Blocked candidates do not participate. Existing preference/fallback permissions determine whether moving to another bucket is allowed.
8. For usage strategies within the same routing bucket and usage band, prefer greater bottleneck headroom (MostRemainingAllowance) or less headroom (MostUsedAllowance) when the entire tied group shares comparable provider/window semantics, then lower occupied-slot ratio, then least-recent assignment, then captain ID ordinal. Skip headroom comparison for the whole tied group when its signatures differ; pairwise conditional comparison can produce non-transitive ordering. FirstAvailable instead uses the existing deterministic routing rank supplied by shared routing. Unknown timestamps/reset times never win as zero. Cross-provider ties skip incomparable headroom and use the remaining tie-breakers.
9. Revalidate and atomically claim/reserve/persist as described below. If evidence or capacity changed, recompute rather than launching from an outdated preview.

Reset time affects the next reconsideration and the explanation, not permission to spend an exhausted balance. For several blocking windows, report the latest applicable known fixed reset as an earliest possible recovery estimate. Missing/rolling resets mean no reliable ready time. Always recheck a fresh capture at reset; a timer alone never manufactures available quota. Retry earlier for capture completion, reservation release, settings changes or account changes, with deduplicated scheduler wakeups.

Explicit assignments wait when usage blocks their selected captain. No fallback occurs without the existing non-strict routing semantics or explicit authorization. A fallback is a new execution attempt with its own decision; it cannot move a partially executed mission to another provider without the current safe recovery/resume rules. Prevent starvation through least-recent assignment within equivalent bands and age-visible waiting, not an implicit relaxation of model fit or reserves.

### Example with competing accounts

An Architect mission allows Claude and Codex at the same routing preference. Claude captains A1 and A2 share account A: 89% used, 2 points reserved, leaving 9 points. Codex captain B1 has account B: 40% used and 2 reserved, leaving 58. With a 10-point reserve and 2-point new reservation, A is soft-gated and B is Comfortable. Select B1 and store both account-A candidates as BelowReserve with shared-account evidence. If the mission pins A1, it waits instead. If the Claude allowance resets in ten minutes, show that as a reported reset, not a guaranteed start time.

## 7. Reservation accounting and transactional admission

A reservation is a conservative allocation of launch headroom, not measured tokens or a provider guarantee. Reserve the configured percentage in every applicable window for the selected account. Running reservations remain fully counted even after a new observation arrives; that deliberately overstates concurrent consumption rather than double-spending headroom. Store observed usage and estimated reservation separately in APIs and charts.

Use states `Active`, `Unsettled`, `Settled`, `Released` and `Uncertain`, with reason/timestamp and compare-and-set transitions. Pre-launch failures release immediately. Once a process might have consumed allowance, execution completion, cancellation, crash or verification transition moves Active to Unsettled. Verification frees the execution slot but does not erase estimated consumption. A repair creates a new attempt/reservation while the previous attempt's unsettled amount remains visible.

An authoritative applicable capture can settle an ended attempt only when its capture time is after execution end plus SettlementLagSeconds and its membership/configuration/window generation is valid. A probe started before that boundary cannot settle the debt merely because it finished later. The estimate is then absorbed by observed utilization; do not add it forever. No inference is made that the observation's delta belongs entirely to that mission. Failed/stale captures cannot settle debt. Expired unsettled debt becomes Uncertain and follows UnknownUsageBehavior; a known hard hold still wins. Proven fixed-window rollover can close old-window debt after fresh confirmation; rolling semantics require the adapter's rule.

### Admission transaction and lock order

Armada has a single scheduling Admiral. Extend its existing bounded per-vessel then shared capacity lock order; do not introduce an account-first lock acquired elsewhere. Capture writes, cooldown changes and settings/membership revisions must participate in a consistent revision/revalidation protocol. Do not hold scheduler locks while calling a harness, provider, Harbor, filesystem or exporter.

Within the shared admission boundary:

1. Recheck readiness, mission state, eligible captain capacity, membership, policy revision, latest usage revision and applicable reservation totals.
2. Execute one provider transaction that conditionally claims the mission/captain slot, writes the attempt correlation, creates decision/candidate/window evidence and active reservations. Use the existing slot mutation semantics through a new domain-specific admission operation. Do not nest independent connections that each commit half the operation.
3. Enforce unique tenant/mission/execution-attempt idempotency and unique attempt/window reservations. A replay returns the existing result. Use conditional versions to reject stale capture/settings/account state.
4. Commit before launch. If commit acknowledgement is uncertain, query by idempotency key before any retry or process launch. A rollback leaves no slot, reservation or admitted decision.
5. Launch through existing process/dock ownership. Record immutable launch success/failure execution events correlated to the decision; release only the failed attempt's claims when launch never occurred.

U0 must prove that the current provider interfaces can expose this transaction boundary. If not, add a domain transaction method to all four providers before enforcement. SQLite uses its existing serialized write/transaction policy with bounded busy retries; MySQL/PostgreSQL/SQL Server use appropriate transactional row/version guards. No background path may mutate reservations outside these guards. Restart recovery reconciles persisted attempts against mission-owned process/slot evidence before new admissions; it must not expire an active process reservation just because a lease timer elapsed.

## 8. Durable decision schema

Use structured relational storage and typed models. Add dedicated database method interfaces for decision history and usage admission/reservations; do not persist a serialized candidate collection, policy object or window list. Every managed row has a PrettyId-generated ID of at most 64 characters, TenantId, CreatedUtc, LastUpdateUtc and Active. Add UserId/resource ownership where applicable. Child writes and reads enforce the same tenant as their parent.

| Table/model | Required typed fields beyond common identity/ownership |
|---|---|
| `captain_selection_decisions` / `CaptainSelectionDecision` | MissionId, VoyageId, ExecutionAttemptId, Trigger, Mode, Outcome, SelectedCaptainId, SelectedProvider, SelectedModel, HypotheticalCaptainId/Outcome, AlgorithmVersion, PolicyRevisionId, RoutingRevision/reference, EvidenceFingerprint, PreviousDecisionId, EvaluatedUtc, NextReconsiderUtc, PrimaryReasonCode, OverrideActorId/Reason, CandidateCount. Selected model means configured/intended, not proven actual. |
| `captain_selection_policy_revisions` | Immutable effective settings columns, algorithm version, effective timestamp and actor. Store tenant-scoped effective revisions (including inherited deployment settings) instead of repeating a JSON policy on each row. Capture inherited routing inputs in typed decision fields/child rows. |
| `captain_selection_candidates` | DecisionId, CaptainId, Provider, ConfiguredModel, RoutingBucket, EligibilityOutcome, Rank, UsageBand, PrimaryReasonCode, OccupiedSlots/Capacity, LastAssignedUtc, MembershipId, account identity reference, evidence age/confidence. Candidates outside the authorized response scope are redacted in projections, not misreported as eligible. |
| `captain_selection_candidate_reasons` | CandidateId, stable reason code, ordering and typed reason parameters such as required/available headroom or referenced blocker. No English explanation blob. |
| `captain_selection_windows` | CandidateId, SnapshotId nullable reference, immutable snapshot ID string, CapturedUtc, Source/Status, ConfigurationRevision, WindowKey/Name, Model/Scope, generation, reported percent/count/limit/unit/reset, applicability/confidence, observed headroom, active/unsettled estimate and required reserve at decision time. |
| `usage_budget_reservations` | DecisionId, MissionId, ExecutionAttemptId, CaptainId, Membership/account key dimensions, WindowKey/Generation, ReservedPercent, State, Revision, ExecutionStartedUtc/EndedUtc, SettlementEligibleUtc, SettledBySnapshotId, ReleasedUtc and TransitionReason. |
| `captain_selection_execution_events` | DecisionId, ExecutionAttemptId, EventKind, ActualProvider/Model nullable, reporting source, timestamp, sanitized failure classification. Append-only actual-model/fallback/outcome evidence; do not rewrite the original decision. |

Add persisted account/window provider cooldown state if existing quota storage cannot represent it safely. It needs tenant/account/window scope, observed revision, retry deadline, classification and owner; a generic provider error is not automatically a quota hold.

Admission decisions, candidates, reasons, policy revisions and captured window evidence are immutable after commit. Mutable reservation state and runtime recovery are separate. Common Active/LastUpdate columns must not become an edit API for audit rows. Foreign keys, tenant-aware uniqueness, cascade behavior and deletion tests must be explicit. Deleting a captain or raw usage snapshot does not delete a retained decision; keep sanitized historical model/name evidence and nullable live references. Tenant purge deletes its ledger and all children. Mission deletion follows documented retained-history authorization and retention rather than orphaning rows.

Index `(tenant_id, mission_id, evaluated_utc, id)`, `(tenant_id, outcome, evaluated_utc, id)`, decision/candidate child ordering, `(tenant_id, captain_id, evaluated_utc, id)` where materialized/query-supported, and active reservation lookups by budget key/state. Use SQL pagination/filtering and deterministic time/ID ordering, not load-all-and-filter. Full candidate pages are separate from mission-status summaries. If MaximumCandidates is exceeded, persist a bounded non-admission summary with `CandidateLimitExceeded` and explicit counts, not a fictitious complete comparison.

Raw snapshots currently default to short retention. Window evidence copied into the ledger survives raw retention and remains readable without its source row. Never store raw harness output, prompts, credentials, account emails, credential paths or bearer tokens. Retention deletes bounded batches of expired inactive decisions/children, then unreferenced policy revisions; it must preserve live reservations and unexpired attempt history. Retention is not an arbitrary public delete endpoint.

### What creates a decision

Persist actual admission/re-admission, safe fallback, operator exception and materially changed waiting outcomes. Observe mode also stores its hypothetical result. Deduplicate waiting decisions by a canonical fingerprint of policy/routing revision, candidate eligibility, membership, usage bands, blocking windows/reset generations, reservation amounts and next-action class. A newer snapshot ID with unchanged decision-relevant values does not create a new row. Link the first decision for each material state and keep bounded current “last evaluated” operational status separately. Every actual attempt gets its own decision even when the selection is unchanged.

A preview never appends a decision. A refused admission whose database is unavailable cannot truthfully claim a persisted decision ID; surface a transient `SelectionPersistenceUnavailable` blocker through the current operational error path and retry with backoff.

## 9. Public contracts and permissions

Proposed additive contracts below are targets, not existing endpoints. U0 must check naming/collisions and finalize typed DTOs in the local API contract before implementing. Keep the existing versioned `/api/v1` namespace and shared authorization/error conventions. Do not publish private account keys in request parameters.

| Surface | Proposed operation and behavior |
|---|---|
| REST | `GET /api/v1/missions/{id}/captain-selection`: compact current/last actual decision, hypothetical result when applicable, blocking reasons, evidence age and retry hint. |
| REST | `GET /api/v1/missions/{id}/captain-selection/decisions`: paginated history filtered by trigger/outcome/time. |
| REST | `GET /api/v1/captain-selection-decisions/{id}` and `/candidates`: decision detail and separately paginated candidate/window evidence. |
| REST | `POST /api/v1/missions/{id}/captain-selection/preview`: read-only evaluation of the existing mission and currently authorized routing inputs; no probe, claim or ledger writes. |
| REST | Existing settings group read/update exposes UsageSelection with live/restart/override metadata and per-field errors. Existing mission/voyage mutation contracts gain optional authorized fallback/soft-reserve fields with concurrency validation. |
| MCP | `get_captain_selection`, `preview_captain_selection`, and `enumerate` types `captain_selection_decision` / `captain_selection_candidate`; reuse existing settings tools and mission mutation authorization. |
| WebSocket | Mission-scoped `captain-selection.changed` invalidation with decision ID/outcome/revision, scoped identically to mission subscriptions. Clients re-fetch authorized detail; never broadcast candidate arrays or account identity. |
| .NET client / Helm | Typed read/history/preview and mutation support; proposed `armada mission selection <id>` with history/preview flags, stable JSON output and human summary. Finalize names against the existing command tree. |
| Dashboard / Mobile / TUI | Mission selection summary, history/comparison, waiting reasons, permitted settings and exception/fallback workflows through shared contracts. |

GET operations return 200 with an explicit no-history state for a visible legacy mission, not a fake historic decision. Preview returns 200 with a blocked outcome when scheduling cannot proceed; malformed fields return 400. Missing/invisible resources follow the repository's non-enumerating 404 convention. Missing authentication returns 401; authenticated disallowed operations return 403. Mutation conflict on an already-running/non-editable attempt returns 409; unavailable preview dependencies return 503 with retry metadata. MCP maps these to current structured tool errors. Lists use the existing EnumerationQuery/result shape, default page size 25 and bounded maximum 1000, with allowlisted sorting/filtering. Return UTC timestamps and stable reason codes; clients localize display text.

System admins may inspect across tenants only through explicitly scoped admin operations. Tenant admins may inspect their tenant. Operators/viewers see only authorized missions and redacted candidate projections; account aggregates can be shown only if the existing usage permissions allow them. Hidden candidates must not leak IDs, names, exact allowance, counts or ownership through totals, errors, links or exports. Apply authorization before SQL pagination/counts. End-user APIs cannot submit a selected captain disguised as a stored decision, or manufacture an authoritative usage snapshot. Settings writes, exceptions and denied privileged actions enter the existing accounting audit stream.

Update `docs/REST_API.md`, `docs/MCP_API.md`, OpenAPI/API Explorer, `Armada.postman_collection.json`, API surface baselines, SDK documentation, settings catalog, client parity inventories and permission maps together. Postman examples use variables/secret inputs and include success, blocked, stale, redacted, forbidden and conflict assertions. Keep request capture sanitized under the existing retention policy; never capture secret exception payloads.

## 10. Operator experience

Add a compact **Captain selection** section to the existing mission detail page, near assignment/waiting information. Show chosen captain, intended model, actual reported model when known, mode, selection time and one sentence explaining the primary reason. Expand to the comparison and historical decisions. A legacy mission says “Selection history was not recorded for this attempt.” Unknown actual models remain unknown.

Use a three-column comparison: Captain/model, outcome, details. Keep status columns narrow and details flexible, consistent with the repaired evaluation tables. Use shared table column selection, pagination above the table, filters, copy controls, refresh and detail drawers. IDs do not wrap. Authorized optional columns can expose evidence age/reset/estimated reservations. Omit hidden-account details entirely rather than placing them in tooltips or client-side JSON.

Pending cards use the same semantic reasons as admission, such as `BelowReserve`, `AccountQuotaExhausted`, `UsageStale`, `UsageIdentityUnknown`, `AccountCooldown`, `AwaitingFreshUsage`, `UsageEvidenceInvalid`, `SelectionPersistenceUnavailable` and `CandidateLimitExceeded`. Reuse existing reason codes where they already mean exactly the same thing. Explain whether a timestamp is a reported reset, retry, or earliest possible recovery. Offer authorized links to Captain Usage, existing captain settings and selection policy. Do not provide a one-click bypass of a hard hold.

Activity > Captain Usage can show “Observed usage” and “Estimated Armada reservations” separately, with its existing account-scope disclosure. Do not add estimates to the provider's observed utilization series or restore verbose instructional prose. Show shared-account relationships only within the viewer's authorization. Routine selection belongs in history/Events, not a new Jobs page or a Needs You item per waiting tick. Create deduplicated Needs You items only for actionable persistent configuration/capture failures under the existing failure policy.

All navigation uses the global breadcrumb component and registered routes. Reuse the existing modal/footer/refresh styles and button heights. Preserve filters and focus across manual refresh and event invalidation; cancel stale fetches. Settings are labeled form controls with validation, mode explanation and an Observe-versus-Enforce comparison, not a JSON editor. Modals trap/restore focus, close with Escape/backdrop when safe, and give explicit save/discard feedback.

Validate 1280+, 768 and 390 pixel layouts, light/dark themes, keyboard and screen-reader behavior, long model names, empty/loading/error/stale/redacted states and permission changes. Translate every visible/accessibility string in supported catalogs using semantic keys and explicit-locale dates/percentages/durations. Include English, a Romance language, CJK and RTL/pseudo-RTL plus expansion checks. Native Mobile/TUI reuse reasons and formatting while using their established navigation and controls.

## 11. Telemetry and operational behavior

Extend existing usage/admission telemetry rather than adding a competing collector. Measure selection outcome counts, stage durations (eligibility/evidence/evaluation/transaction), waits by reason, provider cooldowns, reservation reconciliation and retention outcomes. Use bounded mode/outcome/reason/runtime/stage labels. Never label metrics with tenant/user/captain/mission/account IDs, snapshot IDs, model strings or provider error text.

Create correlated selection/admission/reconciliation spans under existing background or request roots. Allow only approved sanitized identifiers in traces; no account keys, secrets or raw provider output. Set span status for caught failures. Durable decision history is the audit source; metrics reset on restart and are not decision storage. Exporter failures are best effort, while admission-ledger failures remain safety failures as specified above.

Provision a usage-selection Grafana dashboard in the existing product folder: wait reasons over time, Observe/enforced divergence, selection/admission latency, active/unsettled/uncertain reservations, cooldown outcomes and reconciliation errors. Verify Prometheus/Tempo links and the existing external-services card. Preserve pinned compose images, private telemetry endpoints, healthchecks and startup dependencies; no separate metrics database or new public unauthenticated diagnostic surface.

## 12. Delivery milestones

Every unchecked task is future work. Each milestone ends with recorded automated evidence, a UX/usability/aesthetic/layout pass for affected or backend-driven states, updated plan/docs/contracts, a commit/push to `feature/v1.3.0`, and `./build-all.sh v1.3.0`. Builds publish both v1.3.0/latest plus VSIX; they do not deploy the user's live server. Do not mark a milestone complete merely because a subset of its tests passed. Planning-only edits do not require building product images.

### U0. Contract, baseline and fixtures

- [x] Inventory current routing preference/override semantics, quota hold application, cooldown behavior, provider transaction boundaries, settings APIs, permissions and client routes. Resolve the Watson discrepancy and record compatibility decisions.
- [x] Finalize DTOs/reason codes, policy defaults, identity/window-generation adapters and migration allocation. Define a source-to-normalized evidence fixture for ClaudeCode, Codex, API headers, missing/derived/invalid data and model-scoped windows.
- [x] Freeze expected rankings for explicit/preferred/automatic routes. Add public journey/contract inventory and mock examples without changing runtime dispatch.
- [x] Exit: reviewed implementation map, requirements traceability, reproducible fixtures and preview contract; UX review of proposed placement and state sketches recorded.

### U1. Schema, settings and immutable history

- [x] Add typed entities/enums/DTOs, PrettyId prefixes, provider-neutral interfaces and migrations/mappers for all four providers. Add indexes, ownership, immutable decision writes, reservation transitions, retention and restart reconciliation primitives.
- [x] Add live UsageSelection settings through REST/MCP/settings catalogs, validation, revision recording and environment override metadata. Observe preserves current scheduling behavior.
- [x] Test fresh/upgrade/replay, rollback-on-failure, all typed round trips, authorization/counts, retention and snapshot/captain deletion behavior on every provider.
- [x] Exit: settings usable through existing surfaces and empty/history states reviewed; no enforcement yet and no unscoped ledger API.

### U2. Observe mode vertical slice

- [x] Implement the pure evaluator, batched evidence loader, deterministic preference bands and read-only preview using shared eligibility. Implement actual versus hypothetical records and material waiting-state deduplication. Full placement/backoff preview parity is tracked below.
- [x] Persist actual attempt decisions and shadow reservations without altering current routing, holds, slot counts or reset behavior. Capture intended/actual model and execution correlations.
- [x] Add mission summary/history APIs, MCP/SDK/Helm reads and minimal shared Dashboard mission section. Complete Observe-mode parity and candidate redaction tests.
- [x] Exit: dispatch/repair compatibility and current versus hypothetical choices are verified; authenticated evidence survives restart/raw snapshot pruning and the comparison passes responsive/translated preview review. Wave `048629f8a` passed build-all v1.3.0.

### U3. Atomic enforcement and recovery

- [x] Implement the cross-provider atomic claim/decision/reservation boundary, account-wide window accounting, idempotency, revision revalidation and rollback behavior. Enable Enforce only after the admission invariants pass.
- [x] Add settlement, conservative continuous-generation reset handling, uncertain debt, restart reconciliation, classified provider cooldowns and wakeups. Fixed-window rollover adapters moved to U5. Connect repairs, cancellations, launch failures and verification slot release without harming siblings.
- [x] Add authorized fallback and soft-reserve exception mutations/audit; retain strict assignment and all existing safety gates.
- [x] Exit: concurrent dispatch cannot spend the same known headroom twice; users can see and recover from usage waits; no running work is preempted.

### U4. Complete operator/client surfaces

- [ ] Finish remaining Dashboard reservation/wait actions and Events/Needs You integration. History/comparison, settings and audited recovery permissions are delivered in U2/U3. Reuse global breadcrumbs and shared controls.
- [ ] Complete remaining native Mobile/TUI inspection/settings/recovery workflows and dedicated Helm operator mutations; connect WebSocket invalidations. Maintain .NET/MCP/route/permission/feature inventories as these workflows land. Keep exports paginated/redacted and actual-model provenance visible.
- [ ] For the remaining operator workflows, update translations, docs, API Explorer/OpenAPI, Postman and API surface baselines. Verify manual/event refresh and filter/focus preservation.
- [ ] Exit: each supported client can inspect a selection/wait, understand stale/shared evidence and operate the permitted recovery/settings workflow. No orphan UI or JSON-only configuration.

### U5. Telemetry, provider/stress and simulated-user validation

- [ ] Add metrics/spans/Grafana and tests for bounded cardinality, disabled exporters and errors. Add retention/reconciliation load checks; the reservation reconciler currently performs 4-6 reads per row.
- [ ] Provider adapters declare fixed versus rolling reset semantics per window. For proven fixed windows, record a reset generation on reservations and close old-generation debt after a fresh post-reset capture. Until then all accounting stays on the continuous generation; the release handoff must name this limitation.
- [ ] Run the full shared provider matrix, supported frameworks/OS runners, frontend/native tests, strict lint/type/route/i18n/parity checks and CI coverage. Verify actual AOT applicability and packaging metadata.
- [ ] Run isolated public-surface benchmarks and a clean simulated-user session with coverage/evidence/findings. Apply authorized fixes in separate iterations and revalidate; do not edit during observation sessions.
- [ ] Exit: reproducible test/benchmark reports, responsive/accessibility artifacts and user-testing findings disposition; no unexplained admission/accounting invariant failure.

### U6. v1.3.0 release integration and operator handoff

- [ ] Integrate with the remaining concurrency milestones and fleet-action fan-out, including admission load, repair recovery and model/persona requirements.
- [ ] Update README, CHANGELOG, Docker Hub README, usage/operations/settings docs, upgrade/backup/rollback instructions and REST/MCP/SDK examples. Label Observe default and Enforce opt-in accurately.
- [ ] Verify migration from a populated v1.2.0 fixture through the actual v1.3.0 head on all providers, plus upgrade of an earlier v1.3.0 development database. Older binaries require backup restore, not schema downgrade guesses.
- [ ] Run the final complete test suite, packaging/CI checks and `build-all v1.3.0`; record exact commit, commands, artifacts and remaining limitations. Provide the operator test script below with supported client entry points.

## 13. Required verification matrix

Tests live in shared Touchstone suites under `src/Test.Shared` and existing frontend/native harnesses. Add proposed `Services.UsageSelection`, `Database.CaptainSelection`, `E2E.CaptainSelection` and lifecycle/invariant cases to normal discovery; no hidden release-only test category. Provider fixtures and external services are isolated, cleaned up by ownership and never use the user's view.homedns.org deployment or credentials.

| Area | Cases that must pass |
|---|---|
| Eligibility | Persona/capability/envelope/tier/priority exclusions, strict/preferred overrides, review independence, model evaluations already used by routing, no-eligible versus full-eligible distinction. |
| Evidence | Fresh/stale/missing/derived/failed/future samples; NaN/infinity/out-of-range values; percent/count windows, model/scope matching, unknown reset, multiple blocking windows, old results after account/configuration changes. |
| Shared accounts | Two captains sharing a verified account reserve the same budget; different tenants/hosts/runtimes/profiles are not falsely merged; missing identity stays unknown; cross-user aggregates are redacted. |
| Arithmetic | Equality boundary, zero/near-full headroom, multiple windows, reserve exception limits, incomparable provider units, deterministic ordering and tie fairness. |
| Transactions | At least 50 simultaneous admissions to one shared account across vessels with headroom for exactly one configured reservation: exactly one admission; zero leaked claims/reservations. Repeat under all four providers. |
| Races | Capture/settings/membership changes during claim, duplicate dispatch/idempotency, commit acknowledgement loss, late launch failure, cancellation during launch, concurrent repair and completion, deadlock/timeouts with existing lock order. |
| Lifecycle | Crash before/after commit, restart with running process, duplicate recovery, verification/repair, Harbor disconnect/reconnect, sibling cancellation, mode transitions, expired unsettled debt, fixed/rolling rollover and capture start/end settlement timing. |
| Quota/error behavior | Existing quota hold ownership, stale capture cannot release a hard hold, manual/health holds preserved, valid Retry-After, non-quota 429/5xx/auth errors classified correctly, bounded fallback attempts. |
| Audit/retention | One admitted decision per attempt, material waiting deduplication, immutable records, raw capture pruning, captain deletion, tenant purge, candidate pagination and no silent truncation. |
| Security/API | Every role and transport, cross-tenant/cross-user reads and counts, hidden candidates/window evidence, unknown IDs, unauthorized exception/settings write, secrets absent from logs/history/export/metrics. |
| UX | Observe/enforced/disabled, wait/reset/unknown, legacy history, empty/loading/error/refresh, table sizing, deep links/breadcrumbs, screen readers/keyboard, themes/breakpoints/locales, Mobile/TUI parity. |
| Integration | Fleet actions fan out across many vessels while sharing account budget; all existing numeric gates remain respected; pending diagnostics, actual admission and monitoring agree. |

For benchmarks, extend the existing load infrastructure where possible with a black-box `benchmarks/` workflow compliant with BENCHMARKING.md; do not use product assemblies or database reads as the primary measurements. Exercise 10/100/1000 candidate scenarios and at least 200 mixed admissions, comparing Disabled/Observe/Enforce with identical seeded public inputs. Record hardware, provider, commit, policy, latency p50/p95/p99, throughput, waits, errors, selected-account distribution and query/transaction diagnostics. Separate synthetic fixture results from real-provider observations; no paid live calls are needed for CI. A provisional acceptance budget is less than 20% p95 end-to-end admission regression versus Disabled on the same warm fixture, with zero accounting violations. Record absolute timings and investigate rather than hide cold-start or persistence cost.

CI runs the full suite on every push to every branch and PR, with server database jobs on Linux and SQLite suites on Windows/macOS, all supported target frameworks, and native AOT coverage only where supported. Upload reports even on failure and retain required status checks. Targeted local commands accelerate development but do not substitute for the complete CI/release matrix. Use `127.0.0.1` for loopback fixtures.

### Developer commands and evidence

Run from the repository root. The selection suites below are implemented; the linked U3 record provides the completed wave’s exact commands and results. Repeat the focused backend command for every supported target framework; use the existing provider harness for MySQL, PostgreSQL and SQL Server with isolated credentials supplied out of band.

```sh
ARMADA_TEST_SUITES=Services.UsageSelection,Database.CaptainSelection,Database.UsageEnforcement,E2E.CaptainSelectionApi dotnet run --project src/Test.Automated -f net10.0 -- --results /tmp/armada-usage-selection-results.json
dotnet build src/Armada.sln --no-restore
npm --prefix src/Armada.Dashboard run test:run
npm --prefix src/Armada.Dashboard run build
./build-all.sh v1.3.0
```

Use `scripts/common/run-db-parity-tests.sh` and `scripts/common/run-upgrade-test.sh` according to their current help/configuration instead of copying credentials into commands or reports. Record full-suite runner results, native-client and SDK checks separately. The build-all success message proves image/package production, not provider correctness, visual validation or deployment. Each milestone's evidence record names its commit, environment, exact commands, pass/fail/skip counts, UX artifacts and remaining defects.

## 14. Operator acceptance script

A developer should hand the operator a clean test deployment, example policies and two or more fixture captains/accounts. Deterministic synthetic captures must be available only in an isolated test harness, not as a production API that can forge allowance. Real-account smoke tests are optional and must identify their evidence as real without exposing credentials.

1. Open Settings and confirm Observe is the default. Dispatch an automatically routed mission and inspect actual/hypothetical selections, relevant windows and the captured policy revision.
2. Open the same mission through Dashboard, REST/MCP and one native client. Verify the same outcome/reasons and correct global breadcrumbs/deep links.
3. Enable Enforce. Give two eligible captains one verified constrained account and another eligible captain a separate healthy account. Dispatch and confirm selection uses the healthy account with a clear explanation.
4. Pin the constrained captain. Confirm the mission waits, names the correct gate, offers authorized next steps and does not silently change model/provider.
5. Submit concurrent missions against one known account with room for one reservation. Confirm one starts, others wait, and no other concurrency gate is exceeded.
6. Finish or cancel an attempt and observe estimated unsettled usage separately from provider usage. Deliver a sufficiently recent authoritative capture and verify settlement and pending-work reconsideration.
7. Simulate a classified provider quota response and reset/cooldown, then stale/failed capture. Verify no rapid fallback loop or invented recovery time; fresh valid evidence restores eligibility.
8. Restart while missions are running and later while verification is active. Confirm reservations/attempt history recover, siblings stay intact and repairs get new admission decisions.
9. Switch users/tenants and check every detail, comparison, count and export for proper redaction. Test a viewer's inability to edit policy or request a reserve exception.
10. Prune raw captures in the isolated deployment and inspect retained decisions. Confirm evidence remains meaningful, actual model is labeled only when reported, and history explains Observe versus Enforce.

Record coverage, transcripts, expected versus actual outcomes and findings under `docs/usage-selection/`. Review screenshots locally; do not commit them (the screenshots directory is ignored). A release handoff must name unresolved limitations: external harness usage, host-local identity, incomplete provider window semantics, conservative reservation estimates and no guaranteed completion budget.

## 15. Deferred adaptive ranking

After the deterministic implementation is validated, use decision/execution correlations to study consumption by persona/task/model, completion reliability, evaluation evidence and cost. Observed account deltas include external work and concurrent missions; they are not clean per-mission training labels. Use provenance-aware estimates with confidence bounds and an explain-only comparison before changing routing.

No adaptive model, learned budget estimate, automatic quality downgrade or cross-host account inference is part of the required v1.3.0 milestones. The decision ledger deliberately preserves the inputs needed to evaluate those proposals later without pretending today's capture is an exact billing meter.

### U2 verification follow-up (complete)

The MCP HTTP test helper validates initialization and closes its owned single-use session. A bounded-session fixture verifies repeated calls and initialization rejection. The exhaustive read-only contract crawl respects the existing production rate limit. That U2 checkpoint passes 6,885 tests in the .NET 10 omnibus, with zero failures and 14 skips; .NET 8 focused checks pass 83/83. Skipped toolchain/provider/upgrade fixtures and the remaining U5 validation work are not counted as completed.

Mobile polling lifecycle follow-up: timers are owned by screen focus and cleared on blur; a focused regression verifies polling pauses and resumes. The complete native suite passes 1,392/1,392 across 84 suites with clean exit, alongside type checking and native/shared lint. Native device visual review remains open.

### U3 completion record

- [x] Additive migration 127, immutable actual-host/account execution attribution and structured per-execution provider cooldown receipts. Classified limits share a bounded deadline across the proven account; repeated exits do not extend it and expiration never invents fresh balance.
- [x] Durable launch markers, proven-unstarted release, conservative legacy adoption and execution-key-bound cleanup. All four mission readers bind the key to the same row snapshot; superseded operations cannot mark or release a newer receipt. In-place continuation retains active debt; verification repairs and re-dispatch receive new admission decisions.
- [x] Immutable administrator fallback/reserve permissions with optimistic revision checks, exact historical audit correlation and hard-gate preservation. Dashboard recovery uses the shared accessible dialog and refresh controls; conflicts preserve the draft reason, and audit text is expandable. REST/MCP and both .NET clients are covered by authenticated tests.
- [x] Current .NET 10 focused batch 110/110; .NET 8 selection/API/TUI batch 121/121; PostgreSQL/MySQL/SQL Server 63/63 each, including 50-way shared-account contention, stale snapshot ownership, lifecycle and authorization races. SQLite is covered by the focused/full runs.
- [x] Full .NET 10 regression: 6,895 passed, zero failed, 14 skipped (6,909 total). Skipped toolchain/provider/baseline-upgrade fixtures remain part of U5/U6 validation; they are not counted as passed.
- [x] Dashboard 1,897/1,897, production build, native 1,392/1,392 with clean exit, type checking and native/shared lint. Recovery UX review 24/24 across responsive themes/locales, RTL/enlarged text, keyboard focus, long audit text and conflict refresh. Native device review remains U4/U5.
- [x] Commit/push to feature/v1.3.0; build-all v1.3.0 succeeded for all three images and VSIX. Linux amd64/arm64 manifests and local image assembly/recovery-bundle contents verified; no production deployment.

See [operator steps and verification](docs/usage-selection/U3_RECOVERY_VALIDATION.md), [layout report](docs/usage-selection/recovery-layout-results.json) and [container evidence](docs/usage-selection/container-validation.json). U4-U6 remain open.

### Takeover review remediation (2026-11-03)

An independent review of U0-U3 confirmed the admission transaction, lock order, replay, scoped reads and reconciler are sound. It found these defects, now fixed:

- [x] Fairness tie-breaker read `Captain.LastUpdateUtc`, which changes on every captain update (including slot release), and stored it in the ledger as `last_assigned_utc`. Migration 128 adds `captains.last_assigned_utc` on all four providers, backfilled from `MAX(missions.assigned_utc)`. Slot claims and verification resumes set it, and selection reads it through the batched, tenant-scoped `ICaptainSelectionMethods.ReadLastAssignedAsync`. Never-assigned captains keep the existing "unknown never wins" ordering.
- [x] Waiting-decision persistence caught only `DbException`. It now also handles `TransientDatabaseError` (timeouts and wrapped provider errors), so these surface `SelectionPersistenceUnavailable` and retry.
- [x] The transient-blocker cache evicted an arbitrary entry when full. It now evicts the entry whose retry is due soonest.
- [x] Persisted outcomes, execution event kinds and reason codes were repeated string literals. Added `CaptainSelectionOutcomes`, `CaptainSelectionEventKinds` and `UsageSelectionReasonCodes`; stored values are unchanged.
- [x] `UsageSelectionPolicy`, the transaction-time enforcement validator and the `CaptainSlotStore` scope checks were split into named steps with no behavior change. `GenerationConfirmed` was renamed to `WindowIdentityConfirmed`, because it proves label integrity, not a reset generation.
- [x] Review screenshots (20 PNGs) were committed against repository policy. They are removed and `docs/usage-selection/screenshots/` is ignored.
- [x] U3 status corrected to describe continuous-generation accounting (above), with fixed-window adapters tracked in U5.

Evidence: `dotnet build src/Armada.sln` clean (0 warnings). On .NET 10/SQLite, the focused batch `Services.UsageSelection, Database.CaptainSelection, Database.UsageEnforcement, E2E.CaptainSelectionApi, Database.MigrationHygiene, Services.CaptainAdmissionParity, Services.UsageSelectionConfiguration` passes 102/102, including the new `slot_claims_record_assignment_time_independent_of_other_captain_updates`. Full .NET 10 regression: 6,896 passed, zero failed, 14 skipped (6,910 total; the same skipped fixtures as U3). After freeing Docker disk space on the development host, `scripts/common/run-db-parity-tests.sh --providers postgresql,mysql,sqlserver --suites Database.CaptainSelection,Database.CaptainConcurrency,Database.UsageEnforcement,Database.MigrationHygiene,Database.MissionDatabase` passes 64/64 on each of PostgreSQL 17, MySQL 8.4 and SQL Server 2022, covering migration 128, the new assignment-time case, 50-way shared-account contention and stale-snapshot ownership.

