# Limit chart clarity validation

Removed the unlabeled 75% and 90% dashed threshold guides. The ordinary solid percentage grid remains. Legend labels use provider and window, with numbered sources when a provider has multiple account/host combinations. Numbering is consistent across windows and input ordering within the visible source set. Detailed account/host context remains in hover titles, point tooltips and the accessible data table. No REST or MCP contracts changed.

## Verification

All 1,856 Dashboard tests pass. Focused tests cover compact labels, disambiguation across hosts, order-independent numbering, guide removal and retained grid lines. Existing selection, keyboard, gaps and reset coverage passes. Dashboard production build, .NET 8/.NET 10 solution build and Mobile type checking pass.

The production Dashboard was inspected in light and dark themes at 1440, 768 and 390 pixels. A disposable local server and intercepted synthetic chart response provided five populated series across three account/host sources; this validates rendering, not provider ingestion. All six cases have zero threshold guides, five grid lines, compact distinct labels and no document overflow. Desktop labels fit on one row; phone labels wrap readably. The chart retains its existing horizontal scrolling on small screens.

See [measurements](limit-chart-layout-results.json). Screenshots were reviewed locally and are not committed.

## Manual validation

Open Activity > Usage Overview and select Limits only. Confirm there are no dashed red horizontal guides, series names are short, multiple sources can be distinguished, and hovering a legend entry reveals account/host details. Select a chart point or use the keyboard to inspect its bucket, and expand the data table for detailed context.

Release command: `./build-all.sh v1.3.0`. Build output is recorded in `/tmp/armada-v130-limit-chart-build-all.log`.
