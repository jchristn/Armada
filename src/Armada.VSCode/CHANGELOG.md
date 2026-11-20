# Changelog

## Unreleased

- The extension is now `latticetech.armada` (publisher `latticetech`, name `armada`, display name Armada). It was
  never published as `jchristn.armada-vscode`, so nothing migrates.
- **Armada view container** in the activity bar with a **Navigation** view (the dashboard's navigation model:
  Dashboard, Ask Armada, OPERATIONS, BUILD, CONFIGURATION, SYSTEM; pages open in the dashboard for now) and the
  **Failures** view, which moved here from the Explorer. `armada.failures.showInExplorer` (off by default) also shows
  it in the Explorer. Both views explain what to do while disconnected.
- **Connection manager**: connects to the local Admiral from `~/.armada/settings.json` (or `$ARMADA_DATA_DIR`) with no
  prompt; **Connect to a Remote Admiral...** keeps the URL and key flow, with the key in secret storage. Each
  connection checks health, the Admiral's contract version ("server too old" or "extension too old") and whoami, and
  sets `armada.role.globalAdmin` and `armada.role.tenantAdmin`. A status bar item shows the state and target and opens
  the connection actions; the **Armada** output channel logs every attempt. `armada.url` now defaults to empty (the
  local Admiral).
- **Shared client**: the extension uses the dashboard's API client, types and helpers through the `@dashboard/*`
  alias instead of its own copies, and its lint checks the dashboard's shared modules against the sharing contract it
  now shares with the mobile app.
- **Event bus**: one `/ws` connection per Admiral that subscribes once and feeds every surface, with the same
  reconnect backoff and polling fallback.
- Parity manifest (`parity.json`), ESLint, Node 22 pinned in `.nvmrc`, and a `vscode-extension` CI workflow that
  builds, lints, tests, checks parity and packages the VSIX.
- Packages build into `vsix/armada-vscode-<version>.vsix` at the repository root; `build-vscode.sh --install` (or
  `build-vscode.bat --install`) builds and installs it in one step.

## 1.3.0

- Align extension packaging with the v1.3.0 release.

## 1.2.0

- First release: the Armada Failures view (black box entries grouped by vessel, filtered to the open repository,
  with an open-entry badge), FIX THIS copy, editor and chat hand-off, recovery paths with dry run and confirmation,
  resolve, notes, Ask Armada hand-off, entry view, conflict and hotspot file decorations, and live updates over the
  Armada WebSocket with a polling fallback.
- Black box insights: **Armada: Show Black Box Timeline** (paged combined timeline), **Armada: Compare Black Box
  Attempts** (side-by-side attempt diff), **Armada: Preview Mission Overlap** (overlap and brief lint warnings for a
  brief on the open repository's vessel), and in the entry view each recovery path's success rate, its linked runbook
  and the vessel's recurring configuration recommendations.
- Armada resilience: the entry view's Armada resilience section (self-healable verdict, detection gap, code pointers
  at the build commit, proposed Armada changes, regression test), the "Suspected Armada defect" badge, **Armada: Draft
  Armada Fix Mission** and **Armada: Show Resilience Gaps**.
