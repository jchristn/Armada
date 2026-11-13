# Waiting diagnostics validation

This segment shares running safety decisions and runtime/endpoint capacity filters with dispatch, adds bounded next steps to REST/MCP mission reads, and makes them usable through existing editors.

## Automated checks

- .NET 10: 153 focused scheduling, pipeline, REST/MCP parity, authorization, real-process concurrency and TUI checks passed.
- .NET 8: 92 scheduling, authorization, real-process concurrency and TUI checks passed.
- Dashboard: 1,851 tests passed. Mobile: type check and 20 mission-screen tests passed, including advice navigation to an existing native review screen.
- The full solution builds on .NET 8 and .NET 10 without warnings or errors. The maintained Postman collection parses successfully.

The regressions distinguish a free captain from available runtime/endpoint capacity, endpoint saturation from unavailability, safety locks from numeric gates, lowered limits from current occupancy, and CLI capacity advice from endpoint-backed capacity advice. Real REST and MCP reads verify that tenant readers receive advice text without configuration actions, while global admins receive the actions. The existing 200-mission mixed-limit and cancellation/recovery scenarios remain covered.

## UX, styling and aesthetics

A disposable local Admiral served the actual built Dashboard. Synthetic assignments represent three running missions after a limit was lowered to two; no provider or agent processes were launched for the layout fixture. Captains, captain detail and the pending mission were checked at 1440, 768 and 390 pixels in light and dark themes (18 page combinations).

Reviewed the waiting card's visual hierarchy, readable setting labels, wrapping of long mission IDs, global breadcrumbs, spacing, contrast and navigation targets. The new advice links initially inherited a taller line height; this was corrected, and their measured height now matches the existing action buttons at 22px at all checked widths. The captain detail editor also now uses the same shared modal sizing as the list editor.

Clicked the vessel remedy and verified the actual vessel edit modal opens. Captain edit, captain create and endpoint edit deep links were additionally checked at desktop and phone widths. All six forms open correctly, have no document-level horizontal overflow, and their Save/Cancel footers remain reachable and visible after scrolling. There is no page-level horizontal overflow in any checked page. Native clients use existing review routes rather than promising unsupported edit deep links.

See [measurements](waiting-diagnostics-layout-results.json). Screenshots were reviewed locally and are not committed.

## Operator validation

Follow [waiting diagnostics testing](../CONCURRENCY_TESTING.md#validate-waiting-diagnostics) to saturate a vessel, runtime or endpoint, open a waiting mission, follow its remedy, and compare REST with MCP `mission_status`.

After commit/push on `feature/v1.3.0`, the release checkpoint is `./build-all.sh v1.3.0`: build/push/pull Admiral, Dashboard and Proxy for both architectures and build/test/package the VSIX. Its output is recorded in `/tmp/armada-v130-waiting-diagnostics-build-all.log` and reported with the checkpoint result.

Verification-slot release, the remaining dependency/eligibility evaluator consolidation, invariant monitoring, hierarchy and wait telemetry remain open in the implementation plan.
