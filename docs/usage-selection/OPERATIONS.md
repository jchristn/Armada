# Usage-based captain selection: operator guide (v1.3.0)

This guide is for operators running an Admiral with usage-based captain selection. It covers rollout from Observe to Enforce, every `UsageSelection` setting, how to read waits, recovery permissions, reset generations, retention, telemetry and known limitations. For the admission model and acceptance steps, see [Usage enforcement and mission inspection](ENFORCEMENT_AND_MISSION_INSPECTION.md). The design and delivery record is the [implementation plan](../../USAGE_BASED_CAPTAIN_SELECTION_PLAN.md).

## Modes and strategies

| Mode | Behavior |
|---|---|
| `Disabled` | No new selection history. Existing quota holds still apply. Work started while Disabled is tracked as operational uncertainty, not as a fabricated decision. |
| `Observe` (default) | Actual routing is unchanged. Each admission records the actual choice and what the configured strategy would have chosen. A required ledger write that fails can still defer a launch, which is the one operational difference from Disabled. |
| `Enforce` (opt-in) | The configured strategy and usage gates decide admission. The mission assignment, captain slot, immutable decision and reservations commit in one transaction before launch, on SQLite, PostgreSQL, MySQL and SQL Server. |

| Strategy | Choice among qualified captains |
|---|---|
| `FirstAvailable` (default) | Existing routing order. Captains with known usage come before explicitly permitted unknown usage. |
| `MostRemainingAllowance` | Prefers comfortable accounts and more headroom within comparable groups. |
| `MostUsedAllowance` | Consolidates work on the most utilized eligible account without crossing the reserve. |

The strategy never changes persona, capability tier, strict assignments, review exclusions, worker envelopes or capacity limits. It chooses only among captains that already qualify.

## Where to configure and inspect

Only a global administrator can change the `UsageSelection` group. Changes apply live to future admissions with a revision check; running work is never preempted.

- **Dashboard:** Server settings, Captain selection section. Each mission detail page has a Captain selection card (summary, compare captains, history, preview, recovery permissions).
- **TUI:** Server settings, Captain selection group. Each mission has a Captain Selection tab (`c` compare, `h` history, `v` preview, `j` JSON, `g` refresh, `a` recovery permissions).
- **Mobile:** Server settings selection section. Each mission overview has a Captain selection section and, for administrators, a Recovery permissions sheet.
- **Helm:** `armada mission selection <mission>` with `--history`, `--outcome`, `--preview`, `--decision <id>` or `--json`. Recovery is `armada mission selection-recovery <mission> [--allow-fallback] [--reserve-exception] --reason "..." [--expected-revision N] [--yes]`.
- **REST:** `GET`/`PUT /api/v1/settings` (`UsageSelection` group, `ExpectedUsageSelectionRevision` for optimistic writes). Mission routes are under `/api/v1/missions/{id}/captain-selection` (summary, `history`, `history/enumerate`, `preview`, `recovery`) and decisions under `/api/v1/captain-selection-decisions/{id}` and `/candidates`. See [REST_API.md](../REST_API.md#captain-selection).
- **MCP:** `get_usage_selection_settings`, `update_usage_selection_settings`, `get_captain_selection`, `get_captain_selection_history`, `get_captain_selection_decision`, `preview_captain_selection`, `authorize_captain_selection_recovery`, and `enumerate` types `captain_selection_decision` and `captain_selection_candidate`. See [MCP_API.md](../MCP_API.md#captain-selection).
- **Settings file:** the `usageSelection` group in `settings.json`. The `ARMADA_USAGE_SELECTION` environment variable supplies a complete JSON group, which then shows as environment-controlled and read-only in every client.

Helm uses strict parsing in v1.3.0: an unknown option or stray argument exits non-zero before any request is sent. Check scripts that call `armada mission selection` or other commands with options they do not define.

## Turn on Observe review

New and upgraded installations already run Observe with `FirstAvailable`. To get a useful comparison:

1. Make sure usage capture is running for every captain's account on the host that actually runs it (Activity > Captain Usage). Selection uses captured evidence only; it never probes during admission.
2. Choose the strategy you intend to enforce later (`MostRemainingAllowance` or `MostUsedAllowance`) while staying in Observe. Observe then records what that strategy would have chosen, while routing stays unchanged.
3. Let normal work run. Open a few missions' Captain selection views and compare the actual and hypothetical choices and their reasons.
4. Watch the Grafana dashboard **Armada Captain Selection** (uid `armada-captain-selection`, `http://127.0.0.1:3001/d/armada-captain-selection` in the bundled compose stack). The **Observe divergence** panel counts admissions where the configured strategy would have chosen a different captain.

## Move to Enforce safely

Enforce can make work wait where Observe would have launched it. Before switching:

1. **Review Observe divergence.** On the Grafana dashboard, check **Observe divergence** and **Waits by reason**. A high divergence rate means Enforce will change routing often; confirm that the hypothetical choices in mission history are the ones you want.
2. **Verify captures are fresh.** In Activity > Captain Usage, every account that should take work needs a recent successful capture from its execution host. Evidence older than the effective freshness horizon (see `MaximumFreshnessSeconds`) counts as stale. Stale, missing, failed or derived evidence is unknown usage.
3. **Choose the unknown-usage policy.** Keep `UnknownUsageBehavior` at `AllowDeprioritized` for the first Enforce period so accounts without evidence still run after known accounts. Switch to `Wait` only after captures are reliable.
4. **Check reserves.** Confirm `ReservePercent` and `PerAttemptReservationPercent` against how much allowance you need for work outside Armada.
5. **Switch the mode** to `Enforce` in Server settings (or through REST, MCP or the settings file). Enabling Enforce reconciles active attempts, including work launched while Disabled, before admitting new work. Running work keeps running.
6. **Watch the first hour.** On the dashboard, check **Waits by reason**, **Admission transaction outcomes** and **Ledger persistence failures**. Open waiting missions and read their reasons (below).

To back out, set the mode to `Observe` (or `Disabled`). The change applies to future admissions; existing decisions and reservations remain.

## Settings reference

All settings are in the `UsageSelection` group (`src/Armada.Core/Settings/UsageSelectionSettings.cs`). Numeric values outside the bounds are clamped; non-finite values are rejected. Percentage values round upward to six decimal places.

| Setting | Default | Bounds | What it does |
|---|---|---|---|
| `Mode` | `Observe` | `Disabled`, `Observe`, `Enforce` | Rollout mode, described above. |
| `Strategy` | `FirstAvailable` | `FirstAvailable`, `MostRemainingAllowance`, `MostUsedAllowance` | Preference among qualified captains. Separate from hard usage and capacity gates. |
| `UnknownUsageBehavior` | `AllowDeprioritized` | `AllowDeprioritized`, `Wait` | For absent, stale, failed, derived or unsupported evidence: run after known candidates, or wait for evidence. Never overrides a quota hold or provider cooldown. |
| `ReservePercent` | 10 | 0 to 95 | Percentage points kept unused in each applicable percentage window. An operator reserve, not a mission estimate. |
| `ComfortableHeadroomPercent` | 30 | 1 to 100, must exceed `ReservePercent` | Remaining percentage at or above which an account is Comfortable rather than Constrained. A value not above the reserve is rejected. |
| `PerAttemptReservationPercent` | 2 | 0.1 to 25 | Percentage points reserved in each applicable window per admitted attempt. Admission needs `ReservePercent` plus this amount. |
| `MaximumFreshnessSeconds` | 900 | 120 to 86400 | Upper bound on evidence age. The effective horizon is the smaller of this and the existing quota freshness rule, so it never loosens quota protection. |
| `UnknownRetrySeconds` | 60 | 5 to 3600 | Retry delay, with jitter, for missions waiting on unknown evidence. |
| `ProviderCooldownSeconds` | 60 | 5 to 3600 | Cooldown applied after a classified quota or rate-limit exit that has no valid provider retry time. A longer valid provider deadline is respected. |
| `SettlementLagSeconds` | 60 | 0 to 3600 | Time after an execution ends before a newer capture may absorb its estimated consumption. |
| `MaximumUnsettledSeconds` | 3600 | 60 to 86400 | Age after which unsettled estimated consumption becomes Uncertain and follows `UnknownUsageBehavior`. It never turns into recovered allowance. |
| `DecisionRetentionDays` | 90 | 7 to 1825 | Age after which decision history is pruned. Decisions with live reservations or a pending or running mission are kept. |
| `MaximumCandidates` | 1000 | 1 to 10000 | Largest candidate set evaluated for one mission. Over this, the mission waits with `CandidateLimitExceeded`; selection never picks from a truncated set. |

## Read waits and reasons

A waiting mission shows a stable reason code, translated for display, in its Captain selection view. Timestamps are labeled as a reported reset, a retry, or an earliest possible recovery. None is a guaranteed start time.

| Reason | Meaning | What to do |
|---|---|---|
| `BelowReserve` | Headroom is below `ReservePercent` plus `PerAttemptReservationPercent`. | Wait for a reset or settlement, or grant a soft-reserve exception if the work justifies it. |
| `AccountQuotaExhausted` | An authoritative quota hold or exhausted window. | Wait. No permission bypasses it. |
| `AccountCooldown` | A classified provider limit put the account in cooldown. | Wait for the deadline; a fresh capture is then evaluated. |
| `UsageMissing`, `UsageStale`, `UsageNotAuthoritative` | No usable evidence, under `Wait`. | Fix or refresh capture in Activity > Captain Usage. |
| `UsageIdentityUnknown`, `UsageConfigurationChanged` | Account identity is unverified or the capture configuration changed. | Recheck the captain's capture setup on its host and capture again. |
| `UsageModelUnknown`, `UsageScopeUnknown`, `UsageEvidenceInvalid` | A window cannot be applied to this captain, or its data is malformed. | Check the captain's model configuration and capture output. |
| `UsageUncertainDebt` | Earlier estimated consumption aged past `MaximumUnsettledSeconds` without a settling capture. | Capture again on the account's host. |
| `AwaitingFreshUsage` | A window reset passed or its identity is unconfirmed. | Capture again; a reset timer alone never adds allowance. |
| `FallbackPermissionRequired` | The preferred captain cannot run and fallback needs administrator permission. | Grant eligible fallback (below) or make the preferred captain available. |
| `CandidateLimitExceeded` | More candidates than `MaximumCandidates`. | Narrow routing or raise the bound. |
| `SelectionPersistenceUnavailable` | The decision ledger could not be written; no process starts. | Check database health; the mission retries with backoff. |
| `PlacementWaiting` | The chosen captain is waiting on dock or host placement. | Check the captain's Harbor and dock placement. |
| `UsageHostMismatch`, `UsageHostUnresolved` | The execution host does not match, or cannot be resolved to, the host that supplied the usage evidence. | Bring the credential Harbor back or fix the captain's host. Known-account work is not moved to another account. |

Use **Preview** to re-evaluate a Pending mission against current evidence. Preview writes nothing, claims nothing and does not contact a Harbor. Real admission rechecks everything.

**Needs You and events.** In Enforce, a Pending mission whose latest decision has waited at least 15 minutes for an operator-actionable reason (identity, capture configuration or failure, candidate limit, or a needed fallback permission) appears in Needs You as `selection_blocked`. Reserve, quota, cooldown and capacity waits are excluded because they clear on their own. Every materially new waiting decision also records a `mission.selection_waiting` event. Clients refresh on the `captain-selection.changed` WebSocket event, which carries identifiers only.

## Recovery permissions

A tenant or system administrator can grant two permissions on a Pending mission, each with a required audit reason (1 to 512 characters). Both start off.

- **Allow eligible fallback:** lets an explicitly preferred assignment move to another captain that still passes persona, tier, ownership, worker-envelope and capacity rules. Strict voyage assignments stay strict.
- **Allow soft-reserve exception:** lets admission use the protected reserve. It does not bypass an exhausted account, a quota hold, a provider cooldown or unknown evidence.

A mission pinned to one captain that cannot run says which permission would help: its waiting advice suggests fallback when other captains were excluded only for lacking that permission, and a soft-reserve exception when the pinned captain is below the reserve. Compare captains shows those captains' real headroom.

Saves are revision-checked. A stale save returns a conflict, keeps your draft reason and asks you to refresh. Permission changes cannot alter running work. Each admitted decision keeps the exact permission revision, author, reason and time it used. Use the Dashboard, TUI (`a`), Mobile sheet, `armada mission selection-recovery`, `POST /api/v1/missions/{id}/captain-selection/recovery` or MCP `authorize_captain_selection_recovery`.

## Reset generations

Each reservation belongs to a window generation. Only a window that its capture adapter declares `Fixed` and that reports an absolute reset time gets a fixed generation, named by its reset minute (for example `fixed:20261104T1500Z`). Claude five-hour and seven-day windows, Codex rate-limit windows and Gemini daily quotas qualify when the provider reports a reset time. Everything else (rate-limit headers, which are `Rolling`; interactive readings; older Harbor hosts; windows without a reset time) uses continuous accounting under the single `continuous` generation.

An automatic retry (`Trigger` `Redispatch`) after an attempt that produced no work takes over that attempt's reservation on the same account window, keeping the larger amount, so the retry is not charged twice and cannot wait on its own reservation. After an Admiral restart the retry reserves again instead, which only over-reserves. A deleted captain's reservations stay bound to the account they reserved and settle from a fresh capture of that account that started after the execution ended.

An ended reservation from an old fixed generation closes (Settled, reason `WindowReset`) only when the reset has passed, the execution ended before the reset, and a fresh capture of the same account, host and configuration that started after the reset reports the next generation. Running work keeps its reservation across a reset. Continuous reservations never close on a reset; they settle only through a qualifying post-execution capture.

## Decision size

A decision stores the captains that explain it: the one that ran, the one Observe proposed and the one the mission asked for, plus, for a wait, the 25 candidates nearest to eligibility. Every other evaluated captain is counted by owner and reason. This keeps admission cost flat on large fleets (a 1000-captain run admitted with p95 under 1 s in Enforce and Observe); preview still evaluates and lists every current captain.

## Retention

History retention runs with the other data-expiry work, about every 100 health ticks, on every database provider. It applies `DecisionRetentionDays` in bounded batches (at most 20 batches of 500 decisions per pass) and keeps decisions that hold an active, unsettled or uncertain reservation or whose mission is pending or running. Retained decisions keep their copied window evidence after raw usage snapshots expire. There is no public delete endpoint for decisions. Other retention settings are listed in [UPGRADING.md](../UPGRADING.md#data-retention).

## Telemetry

The bundled compose stack provisions the **Armada Captain Selection** Grafana dashboard (uid `armada-captain-selection`), linked from the Dashboard home page's External services card. Panels cover decisions by mode and outcome, waits by reason, Observe divergence, stage latency, admission transaction outcomes, reservations by state, reconciliation, provider cooldowns, ledger persistence failures and history retention. Instrument names, labels and span details are in [TELEMETRY.md](../TELEMETRY.md#captain-selection).

Counters reset when the Admiral restarts; the decision ledger is the audit record. A failing exporter never blocks admission, while a failed ledger write keeps the mission waiting.

## Backup, upgrade and rollback

Migrations 123 through 129 add the selection ledger; 129 stores per-reason counts of candidates a decision did not list. Back up the database and the protected settings and usage identity before upgrading. Rolling back to v1.2.0 requires restoring that pre-upgrade backup; do not point an older binary at an upgraded database. Follow [UPGRADING.md](../UPGRADING.md#v120-to-v130-development) for the post-upgrade checklist and the per-provider backup and restore steps.

## Known limitations

Name these in any release handoff:

- **Outside usage is invisible until captured.** Claude Code, Codex and other harnesses run outside Armada consume the same account allowance, and selection only sees it after the next capture.
- **Account identity is host-local.** Account keys are derived from host-local key material, so the same subscription on two Harbors appears as two accounts. Armada does not merge them or treat them as independent allowance.
- **Provider window semantics are only partly declared.** Windows not declared `Fixed` with a reset time stay on continuous accounting, which is conservative and can hold estimated debt until a post-execution capture settles it.
- **Reservations are conservative estimates.** `PerAttemptReservationPercent` is the same for every task size and is not a measurement of mission cost. Concurrent consumption is deliberately overstated.
- **No guaranteed completion budget.** Admission checks headroom at launch. It does not guarantee that a mission finishes before an account runs out or resets.
