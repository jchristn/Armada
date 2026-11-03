# U1 storage and configurable-policy foundation

Status: storage/transaction foundation implemented; live settings, dispatch integration and operator UI remain in progress. Selection strategy is configurable in the typed/settings-file contract, with FirstAvailable as the compatibility default. It is not yet connected to dispatch. MostRemainingAllowance and MostUsedAllowance are distinct directions; both preserve routing buckets and enforcement gates.

## Changes and transaction evidence

Migration 123 creates structured policy, decision, candidate, reason, window, reservation, execution-event and cooldown tables on SQLite, PostgreSQL, MySQL and SQL Server. No managed candidate/policy/window collections are persisted as JSON. Tenant/decision relationships, indexes, per-attempt uniqueness and immutable history writes are explicit. Every row has typed ownership/lifecycle fields. Historical snapshot references are evidence strings rather than deletion-cascading references, so raw capture retention cannot erase selection evidence.

Migration 124 records a nullable Admiral-side probe-start timestamp. Active capture records the timestamp before the host round trip; legacy/passive observations remain null. A null start time cannot prove that a capture began after execution ended. The historical v115 upgrade fixture now seeds historical SQL directly instead of asking the current writer to write a future column into an old schema. Fresh/upgrade/replay coverage passed on all four providers.

`TryAdmitObservedAsync` uses the existing CaptainSlotStore connection and transaction, including SQLite's write lease. A successful result commits slot, mission assignment and the complete decision aggregate together. A forced child-key collision proves that a history-write failure leaves the mission Pending and its captain unclaimed. Replays return AlreadyCommitted without granting another launch. Enforce mode is explicitly rejected by this foundation method; account-budget revalidation belongs to U3.

Effective policy revisions include the configured strategy and a canonical non-secret settings fingerprint. Equal policies reuse a tenant-scoped revision. Candidate totals and pages are calculated after decision-owner and candidate-owner filtering. Windows/reasons and actual-model execution events require the same scope. Reservation transitions require matching tenant, state and revision; terminal states cannot return to Active. Expiration preserves live/unsettled debt and tenant deletion removes children before parent/policy rows.

## Checks run

- SQLite, net10.0: Database plus Services.UsageSelection, Services.UsageQuarantine and Services.UsageScheduler, 747 passed, zero failed/skipped.
- SQLite, net8.0: the earlier broad slice passed 744 cases before the final two atomic-admission and settings-file cases were added. The final focused checkpoint (ledger, slot concurrency, selection, quota and scheduler) passed 75/75 on net8.0 and net10.0; do not treat the earlier broad count as validation of later code.
- PostgreSQL, MySQL and SQL Server: 33/33 selected ledger/migration/usage cases on each provider. After adding the atomic observed-admission path and execution/reason reads, the focused ledger/captain-slot subset passed 12/12 on each provider.
- New pure policy tests distinguish FirstAvailable, MostRemainingAllowance and MostUsedAllowance; exercise settings-file defaults/round trips; and preserve deterministic ordering across incomparable provider windows.
- Core compilation succeeded without warnings/errors. The prior U0 build-all completed and published/pulled all three image families and built the VSIX.

Docker's internal disk was full on the first server-provider attempt. The retry used temporary memory-backed data directories for owned parity-test containers, with SQL Server running as root only inside its isolated test container. No existing containers, images or volumes were pruned or changed. Every owned test container was removed by the harness. The host's regular data and the user's remote deployment were not test targets.

## UX/usability/aesthetic review

No rendered page changed in this segment. The review checks the future backend-driven states against the U0 design: actual versus hypothetical choice, intended versus reported model, observed versus estimated allowance, source evidence that may have expired, and replay versus new launch. Those distinctions have typed storage fields rather than ambiguous formatted strings. There is no new page, route, button or breadcrumb to become orphaned. Runtime responsive/theme/accessibility screenshots remain required when U2/U4 expose the controls; none are claimed here.

The next segment must expose live settings with capability metadata and connect Observe mode before advertising usage-based scheduling as available. U1 is not fully complete until settings/API validation, full transaction preconditions and the remaining milestone checks are recorded in the root plan.
