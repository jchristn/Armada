# Legacy ownership cleanup and final schema validation

This segment completes M1 and the remaining M2 cleanup from [the concurrency plan](../../CONCURRENCY_IMPROVEMENTS.md). It removes the singular captain mission/dock pointers and vessel concurrency boolean from production models, storage and first-party clients. Missions own their dock, process, heartbeat and recovery budget; planning/refinement own an exclusive session. Captain responses expose active mission IDs and counts.

## Upgrade and compatibility

Migrations 119–121 persist assignment/hierarchy metadata and wait intervals, move rescue provenance to `RescueOfMissionId`, preserve active mission/session ownership, normalize live legacy Testing assignments, and drop the three legacy columns. Historical schema replay does not recreate them. MySQL can resume each of the three final migrations after an interrupted, partly applied DDL sequence.

Populated v1.2/schema-116 fixtures were upgraded using real SQLite, PostgreSQL 16, MySQL 8.4 and SQL Server 2022 databases. Provider suites cover atomic captain slots, exclusive sessions, model round trips, rescue backfill/deletion, runtime compare-and-set updates, wait interval close/retention/cascade and final-schema replay. PostgreSQL and SQL Server each pass 18 provider cases and MySQL passes 19; the final migration/runtime reruns pass 3, 3 and 4 cases respectively. MySQL's extra case independently interrupts migrations 119, 120 and 121 before their last statement and successfully resumes each fixture. Owned temporary database containers were removed after validation.

This is the explicitly approved v1.3 compatibility break. Removed vessel request fields and Helm options return actionable numeric-limit guidance. Back up before upgrading; downgrade requires restoring that backup. See [UPGRADING](../UPGRADING.md#v120-to-v130-development), [REST](../REST_API.md), [MCP](../MCP_API.md) and [compatibility policy](../COMPATIBILITY.md).

## Lifecycle and regression coverage

Regression cases cover sibling-safe completion/cancellation/recall, all-assignment restart/recovery, stale recovery after ownership changes, mission-specific Harbor routing, unavailable hosts, per-mission retry clocks/budgets, and exclusive session protection. Testing progress hints remain InProgress while the agent process runs; they cannot silently free execution capacity. HaltVoyage stops each affected running sibling through the normal cancellation path before changing ownership, while unrelated work continues.

A real pi process crash test verifies that the replacement belongs to the same mission/worktree, stale MCP configuration is removed and repository instructions are regenerated without nesting previous generated prompts. The standalone installed-pi smoke test passes all three cases. Its disposable fixtures explicitly disable discovery of the developer’s MCP servers, extensions, skills and context files while retaining the real model/tools and explicit Armada permission extension; earlier broad runs encountered live-model timeouts. The permission failure exposed a string-only fake MCP input parser: pi can send a numeric bash timeout. The fixture now preserves JSON value types, and the deterministic extension test includes that exact numeric input. Rescue black-box links use rescue provenance rather than hierarchy; historical immutable records remain readable.

REST/MCP/WebSocket contract tests reject removed fields and prevent clients from supplying server-owned assignment metadata. Regenerating the API surface reports 521 REST routes, 264 MCP tools, 65 WebSocket commands, 77 events, 97 CLI commands and 335 settings keys without changing the baseline. The black-box JSON schema includes the new rescue provenance field.

The complete .NET 8 suite passes 6,792 executed cases, with zero failures and 14 conditional skips. Skips are the separate baseline-host upgrade drill, unavailable Java/PHP/Rust toolchains, two documented GCS-emulator listing limitations and one server-provider-only case in the SQLite run. Populated schema-116 upgrades and server-provider storage/slot checks were executed separately as described above.

The complete .NET 10 run covers the same 6,806 descriptors: 6,791 executed passes, 14 skips and the numeric-input pi fixture failure described above. After correcting that fixture, all 13 deterministic/live-pi checks pass. The final focused ownership/recovery/forensics run passes 171 cases; the final .NET 8 rerun after the last source changes passes 86 cases. No failing case remains unresolved. The solution builds for .NET 8 and .NET 10 with zero warnings and errors.

All 1,860 Dashboard tests and 1,384 Mobile tests pass. Dashboard production build, Mobile strict lint/type checks, route/parity checks, seven parity-gate self-tests, shell syntax, local validation-document links and CRLF-aware whitespace checks pass.

## UX, layout and usability

A disposable local Admiral upgraded an owned database copy to schema 121 and served the actual Dashboard. Its fixture includes one captain with three active missions, separate process/dock records, a serial captain and an explicit rescue relationship. No user's running Admiral, Harbor, deployment or repository data was changed.

Eight surfaces were checked in light and dark themes at 1440, 768 and 390 pixels: captains table, captain detail, vessel detail, captain editor, vessel editor and each of the three mission details. All 48 checks require one shared breadcrumb, no document horizontal overflow, all three distinct mission links, mission-owned process IDs, numeric vessel limits and rescue-origin navigation where applicable. The owned UX server was stopped after validation. The visual review also removed mission detail's fixed four-column inline override so it follows the existing global responsive detail grid on phones. Tables retain contained scrolling and editors retain usable internal scrolling.

See [layout measurements](legacy-cleanup-layout-results.json), [desktop captain](legacy-cleanup-captain-desktop.png), [phone captains](legacy-cleanup-captains-mobile.png), [phone capacity editor](legacy-cleanup-capacity-mobile.png) and [phone rescue origin](legacy-cleanup-rescue-mobile.png). Screenshots were visually inspected for legibility, wrapping, theme contrast, control consistency and usable navigation.

Mobile tests, strict lint/type checks, route generation and parity checks pass. The route scanner now recognizes the existing global legacy redirect component, keeping redirects and documented browser-only pages out of the native route inventory. The parity manifest has 706 entries: 593 implemented, 101 documented not applicable and 12 extensions, with none planned. Seven parity-gate self-tests pass.

## What to test manually

1. Back up a populated v1.2 database, upgrade a copy and confirm schema 121 starts with existing missions and exclusive sessions preserved.
2. Set an endpoint-backed captain to three slots. Allow at least three missions per voyage on the vessel, then dispatch one voyage containing three independent missions (or allow three voyage slots for three standalone missions). Open each from the captain table/detail and verify its own process, dock, instructions, log and diff.
3. Cancel one mission. The other two must continue; the vacant slot must accept new work. A live Testing progress hint must not release its slot.
4. Restart the Admiral or interrupt one agent process. Each assignment must recover separately; a stale callback must not overwrite another assignment or exclusive session.
5. Open a rescue mission and its black-box evidence. Rescue origin must link to the original mission independently of hierarchy. Check these views and capacity editors on a phone in both themes.

## Remaining plan

This segment closes the original remaining item for legacy ownership cleanup and migrations 119–121. It does not complete the remaining scheduler stress/provider/invariant matrix, hierarchy and branch execution, wait telemetry/Grafana, public operations/client controls, or final release-wide integration. Those remain explicitly unchecked in the plan.

Release command: `./build-all.sh v1.3.0` (Admiral, Dashboard and proxy for amd64/arm64, plus VSIX). The execution log for this segment is `/tmp/armada-v130-legacy-cleanup-build-all.log`.
