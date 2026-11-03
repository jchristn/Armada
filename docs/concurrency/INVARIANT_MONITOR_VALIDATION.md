# Concurrency invariant monitor validation

The health tick reconciles durable captain slots, then runs the new ConcurrencyInvariantMonitor as an isolated phase. It observes captain reserved-slot counts against effective limits, sticky vessel voyage occupancy and the busiest running voyage/standalone-pipeline group against the per-voyage mission limit. VoyageSlotPolicy is shared with admission, preserving standalone stage grouping and review/landing occupancy. The monitor neither claims nor cancels work.

Events and warning logs identify the affected captain/vessel. Each event retains its entity's tenant/user scope and carries Gate, Count, Limit and Cause; private mission/voyage IDs are omitted. Metric dimensions are only the fixed gate and cause values. Unchanged observations are suppressed within the process, overlapping checks are serialized, deleted entities lose their baseline, and recovery below the limit resets the episode. Startup and ambiguous edit/claim ordering have unknown cause; only an observed reduction with non-increasing occupancy is classified as draining. Growth unexplained by observed limit changes is unexpected, not proof of a particular cause.

## Tests

Added 19 provider-independent monitor tests covering lowering limits without preemption, initial/restart uncertainty, increasing occupancy during draining, ambiguous edit ordering, unexplained overruns, server ceilings, unlimited limits, Pending work, review occupancy, standalone pipeline grouping, settled/blocked/failed work, terminal voyages, bounded metric labels, deduplication, cancellation, health-tick integration and deletion/recreation of entity baselines. The existing 200-mission mixed-limit test now invokes the monitor at each wave and asserts zero violations.

The .NET 10 run passes 333 regression cases, including Admiral lifecycle, dependency/handoff readiness, pipeline/sequential dispatch, database slot concurrency, real HTTP/git/process concurrency and API contract coverage. All 1,856 Dashboard tests pass. The same 333 cases pass on .NET 8. The complete .NET 8/.NET 10 solution builds with zero warnings/errors. Final focused monitor runs verify the explanatory wording for ambiguous edit ordering. These runs use SQLite; no live PostgreSQL/MySQL/SQL Server instance was exercised in this wave. The monitor uses existing provider-independent read/event operations and changes no schema.

## UX/layout/aesthetic pass

A disposable local Admiral started with two fixture voyages awaiting Review on a vessel limited to one. Its actual startup health tick emitted an unknown-cause concurrency.limit_exceeded event. The production Dashboard fetched real REST data; no responses were intercepted. Activity > Events and Event Details were checked in light/dark at 1440, 768 and 390 pixels (12 cases). Shared breadcrumb ancestry, table overflow containment, wrapped message/payload and entity links were verified. No document horizontal overflow was measured. The fixture server was stopped after validation; no user Admiral or Harbor was changed.

See [measurements](invariants-layout-results.json), [event list](invariants-events-desktop.png), and [phone detail](invariants-detail-mobile.png).

## Manual validation

Run several missions, lower captain or vessel limits below current usage, and wait for a health tick. Existing work must continue and admission must wait. In Activity > Events, inspect concurrency.limit_exceeded and follow its captain/vessel link. With a prior observed baseline and non-increasing occupancy, Cause should be limit_lowered. A first observation after restart is unknown. Repeated unchanged health ticks must not produce duplicate observations. With telemetry enabled, inspect armada_concurrency_limit_violations_total by gate and cause; this is a changed-observation counter, not a live-overrun gauge.

## Remaining scope

Runtime/endpoint/global observers, stuck-admission detection, wait-time metrics and full invariant integration remain open. Captain monitoring reflects durable reservations, which still include verification ownership. Releasing those reservations requires a separate durable exited-process claim and coordinated restart/resume handling; this wave preserves that recovery behavior.

Release command: ./build-all.sh v1.3.0; log: /tmp/armada-v130-invariants-build-all.log.
