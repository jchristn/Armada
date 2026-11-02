# Activity refresh validation

Activity > Usage Overview and Token Usage now use the existing shared RefreshButton, including its rotating arrow, success checkmark, tooltip and keyboard behavior. Token activity's local refresh also uses that shared control. No REST/MCP contract or endpoint changed.

## Activity tab audit

| Tab | Refresh control |
| --- | --- |
| All Activity | Existing DataTable refresh |
| API Requests | Existing DataTable refresh (list and summary) |
| Events | Existing DataTable refresh |
| Signals | Existing DataTable refresh |
| Token Usage | Added shared page refresh; standardized heatmap refresh |
| Usage Overview | Added shared refresh beside Apply/Clear filters |
| Account Limits | Existing DataTable refresh |
| Jobs | Existing DataTable refresh |

Overview refresh reloads both visible series and filter choices and updates the selected bucket's mission drill-down. It preserves applied URL filters, unapplied draft filters and the bucket selection. Token Usage refresh reloads totals, both charts, the heatmap and captain choices while preserving range, filters, display mode and heatmap selection. Version guards prevent obsolete responses replacing newer results. The shared button explicitly uses type=button so clicking inside the overview filter form cannot submit draft filters.

## Verification

Focused tests exercise pending requests, success only after the requests finish, error/retry, request scopes and preservation of filters/selection. The full Dashboard suite passes (1,854 tests); Mobile type checking and the .NET 8/.NET 10 solution build pass. New labels are translated in all eight supported non-English locales and covered by the usage catalog checks.

A disposable local Admiral served the actual production Dashboard, using its real REST endpoints. Overview, Token Usage and Account Limits were checked in light and dark themes at 1440, 768 and 390 pixels (18 combinations). Refresh reached its success state and preserved the URL in every case. Network observations confirm overview reloads /usage/series and /usage/latest, token usage reloads its summary twice (charts plus heatmap) and captain choices, and Account Limits reloads /usage/latest. The fixture intentionally has no captured usage: the controls remain available in the empty state. Populated, loading and error behavior is covered by component tests.

The first phone layout left the Token Usage page button on a row by itself; it was grouped with the display controls. Final screenshots show consistent shared control sizing, aligned filter actions, readable wrapping, stable breadcrumbs and no document-level horizontal overflow.

See [measurements and request paths](activity-refresh-layout-results.json), [desktop overview](activity-refresh-overview-desktop.png), [phone overview](activity-refresh-overview-mobile.png), and [phone token usage](activity-refresh-tokens-mobile.png).

## Manual validation

Open Activity > Usage Overview, set a range/provider/account/host and click the arrow beside Apply/Clear filters. Verify new data appears without losing filters or a selected chart bucket. An unapplied draft filter must remain a draft. On Token Usage, choose a captain/model and range, then use the arrow beside the chart display controls; totals, charts and heatmap should reload together. Other Activity tabs retain their existing table refresh buttons.

Commit/push is on feature/v1.3.0, followed by ./build-all.sh v1.3.0. The build/push/pull and VSIX output is recorded in /tmp/armada-v130-activity-refresh-build-all.log.
