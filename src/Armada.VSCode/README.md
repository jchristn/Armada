# Armada for VS Code

The Armada extension brings Armada into the editor. It connects to your Armada Admiral (the local one with no setup),
shows your missions, voyages, captains, fleet, merge queue and everything that needs you as live VS Code views, and
works the **mission and voyage black box**: when a mission or
voyage fails, the Admiral records where it failed, how, why, which files and missions conflicted, and the ranked
recovery paths. The extension lists those entries for the repository you have open, hands the **FIX THIS** brief to
your coding agent, and runs recovery paths with a dry run and a confirmation.

Everything is native VS Code: trees with live updates, context menus and inline actions, detail pages drawn with the
editor's own theme, logs and changes as read-only documents, and forms for dispatch. The extension is being built out
to full parity with the dashboard, the TUI, Harbor and the CLI ([VSCODE_EXTENSION_PLAN.md](../../VSCODE_EXTENSION_PLAN.md));
areas without a native view yet (configuration pages such as personas, pipelines and prompt templates) are reachable
only through **Armada: Open Dashboard in Browser**.

## The Armada view container

The Armada icon in the activity bar opens:

- **Needs You**: reviews waiting for approval, failed work, stalled captains, CLI permission requests and alerts. A
  review has Approve and Deny inline; a permission request is answered from a quick pick; the rest open their page.
- **Missions**: Needs Review, Active, Queued and Recent, with status icons, the vessel and the running time. Click a
  mission for its page; right-click to view the log or changes, approve or deny a review, cancel, restart, open the
  black box or copy the id. **+** dispatches a mission.
- **Voyages**: active voyages with their progress and missions, and recent ones. A rocket button dispatches a voyage.
- **Captains**: each captain's state, runtime and model, with the missions it is working; view the log, stop or
  restart it.
- **Fleet**: fleets and their vessels; dispatch to a vessel, or open a vessel's local folder.
- **Merge Queue**: queued and recent landings; process or remove an entry, or process the whole queue.
- **Failures**: the black box tree (below).

All of them update live from the Admiral's event stream. A status bar item shows the active mission count and what
needs you.

**Pages and documents.** A mission, voyage, captain or merge entry opens as a page in an editor tab, drawn with the VS
Code theme: its status, fields, brief or failure, the live end of the log while it runs, and buttons for its actions.
Logs (mission and captain) and a mission's changes open as read-only documents (`.log`, `.diff`) with search, folding
and diff highlighting; an open log follows a running mission. **Dispatch Mission** and **Dispatch Voyage** are forms
with the open repository's vessel preselected.

While the extension is not connected, both views show what to do: connect, or open the output.

## Connecting

The extension connects by itself after VS Code starts; nothing runs while the extension is activating. It takes the
Admiral and the credential from the Armada settings already on the machine, so nothing about the connection lives in
VS Code settings. It looks, in this order, at:

1. **Harbor**: `~/.armada-harbor/settings.json` (or `$ARMADA_HARBOR_HOME`). `ServerLinkUrl` names the Admiral Harbor
   links to, whose REST API is the same host and port over http or https, and `AccessKey` is sent as a bearer token.
   A running Harbor puts its Admiral first.
2. **The local Admiral**: `~/.armada/settings.json` (or `$ARMADA_DATA_DIR`): `admiralPort` and the local `apiKey`,
   sent as `X-Api-Key` to `http://127.0.0.1:<port>`. The Admiral writes the key the first time it starts.
3. **The TUI's server profiles**: `~/.armada/tui.json`, with each profile's session token from the TUI's credential
   store (the macOS Keychain or libsecret, else `tui-credentials.json`).

Entries for the same Admiral are merged, and every credential found for it is tried in turn. At startup the extension
connects to the Admiral last picked with **Armada: Connect**, else to the first one that answers and accepts a
credential.

**Armada: Connect** is the one connect command. It lists the Admirals found on the machine (with where each came from,
whether it responds, and whether a credential was found) and **Enter an Admiral URL...** for any other. A key is asked
for only when no stored credential works; it is tried as a bearer token and then as `X-Api-Key`, and the one that
works is kept in VS Code secret storage for that Admiral.

Each connection probes `GET /api/v1/status/health`, checks the Admiral's contract version (the major part must match
the extension's; otherwise it says whether the Admiral or the extension is too old; an Admiral from before contract
versions, 1.3.0 and earlier, reports none and is treated as contract 1), then proves the credential with
`GET /api/v1/whoami`. The context keys `armada.connected`, `armada.connectionState`, `armada.role.globalAdmin` and
`armada.role.tenantAdmin` follow the result, for menus and `when` clauses.

The status bar shows the state and the target (`Armada: local`, `Armada: armada.example.com`, `Armada: server too
old`, ...), and its tooltip says where the Admiral was found and which credential is in use. Click it for connection
actions: open the dashboard, connect to another Admiral, reconnect or retry, disconnect, show the output, open the
settings. **Armada: Show Output** opens the **Armada** log, which
records every connection attempt and live-update change (never a key).

**Armada: Disconnect** drops the connection and removes a stored remote key; the extension then stays disconnected,
also after a restart, until you run **Armada: Connect**.

The extension does not start an Admiral yet; start Armada yourself (for example with `armada server start`).

## Live updates

The extension keeps one WebSocket connection (`/ws`) per Admiral, subscribes once, and passes every event to the
surfaces that listen (today the black box). When the socket drops it reconnects after 5 seconds, doubling up to 5
minutes, and meanwhile polls every `armada.liveUpdates.pollIntervalSeconds`. With `armada.liveUpdates.useWebSocket`
off it only polls.

## Black box

- **Failures** view in the Armada container (and, with `armada.failures.showInExplorer`, as **Armada Failures** in the
  Explorer): recent black box entries grouped by vessel, with kind, step and age. By default it shows only the vessel
  whose repository is open (matched by git remote URL against the vessel's `RepoUrl`). The view badge counts open
  entries.
- **FIX THIS**: copy the brief, open it as a Markdown document with a preview, or send it to the editor chat.
- **Recovery paths**: a dry run shows what will happen, a modal dialog confirms, and only then does the path run.
  Paths marked `dataLossRisk` are refused unless you tick an explicit acknowledgement.
- **Resolve** entries and **add notes**.
- **Ask Armada About This Failure**: opens the dashboard Ask page with the canonical prompt.
- **Armada resilience**: right after the Summary the entry view shows whether Armada's own handoff and landing could
  have healed the failure (and what it could have done), the detection gap and invariant, severity with recurrence,
  the recoveries Armada already attempted, code pointers at the Armada build, the proposed Armada changes with
  acceptance tests and the regression test; outside handoff and landing, one line saying why no Armada change is
  suggested. An entry triaged ArmadaDefect carries a "Suspected Armada defect" badge. **Draft Armada Fix Mission**
  copies the top proposed change as a mission brief and opens it in the editor chat; **Show Resilience Gaps** lists the
  Armada resilience gaps (failure modes triaged ArmadaDefect in handoff and landing scope) page by page.
- **Entry view**: Summary, Armada resilience, Where, How, Status, Why, Conflict, What would have prevented it, What Armada could change,
  Recovery (with each path's tracked success rate and, when a runbook is linked, the runbook name linking to the
  dashboard), Attempts, Recurring on this vessel (the vessel's configuration doctor: recommendations that keep
  recurring, with a link to an example entry) and Notes, with buttons for every action, including Timeline and Compare
  Attempts.
- **Black box timeline**: one ordered timeline across the entry and its notes, the mission, the dock, the merge queue
  entry, the captain and the Harbor link, as a Markdown table, page by page.
- **Attempt diff**: what changed between two attempts of a mission (or any two entries) side by side: captain, model,
  base commit, configuration, step, and the files only each attempt changed.
- **Mission overlap preview**: before dispatching, enter a brief for the open repository's vessel and see the overlap
  warnings (active missions, reserved and hotspot directories) and brief lint warnings dispatch would return.
- **File decorations**: files that conflicted in an open entry's landing get a `BX` badge and the hover text
  "conflicted in msn_x landing, 2026-10-28"; the vessel's hotspot directories get an `H` badge.
- **Live updates** from the extension's single Armada WebSocket connection (see [Live updates](#live-updates)).


## Requirements

- VS Code 1.101 or later (or a compatible editor such as Cursor).
- An Armada Admiral with contract version 1.x (or one from before contract versions), and a credential for it: one
  already on the machine (Harbor's access key, the local API key, a TUI session), or a bearer token from
  `POST /api/v1/credentials` or an API key entered in **Armada: Connect**.

## Install

From the VS Code Marketplace: search the Extensions view for **Armada AI**, or run
`code --install-extension LatticeAI.armada-ai`.

From source: `./build-vscode.sh --install` (or `build-vscode.bat --install`) at the repository root builds
`vsix/armada-vscode-<version>.vsix` and installs it. For a `.vsix` from a release, use
`code --install-extension <file>`.

## Settings

| Setting | Default | Meaning |
|---|---|---|
| `armada.tenant` | empty | Optional tenant id; admin credentials use it to narrow lists to one tenant |
| `armada.dashboardUrl` | empty | Dashboard URL for links and Ask Armada; empty means Harbor's `DashboardUrl` for Harbor's Admiral, else the connected Admiral plus `/dashboard` |
| `armada.fixThis.openPreview` | `true` | Open a Markdown preview beside the FIX THIS editor |
| `armada.failures.showInExplorer` | `false` | Also show the black box tree in the Explorer, as **Armada Failures** |
| `armada.failures.filterToWorkspaceVessel` | `true` | Show only the open repository's vessel (falls back to all vessels when none matches) |
| `armada.failures.includeResolved` | `false` | Also list resolved entries |
| `armada.failures.maxEntries` | `50` | Entries loaded per vessel |
| `armada.liveUpdates.useWebSocket` | `true` | Listen on the Armada WebSocket |
| `armada.liveUpdates.pollIntervalSeconds` | `60` | Polling interval when the socket is off or down (10 to 3600) |
| `armada.decorations.enabled` | `true` | Conflict and hotspot file decorations |
| `armada.decorations.hotspotLimit` | `10` | Hotspot directories to decorate (0 turns them off) |
| `armada.notifications.onCapture` | `true` | Notify when an entry is captured for the open repository's vessel |

## Commands

| Command | What it does |
|---|---|
| Armada: Connect | Pick an Admiral found on this machine or enter a URL, and connect (asks for a key only when none works) |
| Armada: Reconnect | Connect again to the current Admiral |
| Armada: Disconnect | Drop the connection and remove a key entered for that Admiral |
| Armada: Connection Actions... | The status bar's quick pick of connection actions |
| Armada: Show Output | Open the Armada log |
| Armada: Open Settings | Open `~/.armada/settings.json`, Harbor's settings, the TUI profiles, or the extension's preferences |
| Armada: Open Dashboard in Browser | Open the connected Admiral's dashboard in the browser |
| Armada: Dispatch Mission... | The dispatch form (vessel, title, brief, priority) |
| Armada: Dispatch Voyage... | The voyage form (one mission per line) |
| Armada: Process Merge Queue | Land everything queued |
| Armada: Refresh | Reload the Armada views |
| Armada: Refresh Failures | Reload vessels and entries |
| Armada: Toggle Workspace Vessel Filter | Switch between the open repository's vessel and every vessel |
| Armada: Open Black Box for Mission... | Quick pick of recent failures, or paste a mission, voyage or entry id |
| Armada: Copy FIX THIS | Copy the brief to the clipboard |
| Armada: Open FIX THIS in Editor | Open the brief as an untitled Markdown document (save it as `.md` if you want to keep it) |
| Armada: Send FIX THIS to Chat | Open the editor chat with the brief attached; copies it when the editor has no chat |
| Armada: Run Recovery Path... | Dry run, show the plan, confirm, then run with the confirm token |
| Armada: Resolve Black Box Entry | Set Resolved, Duplicate, WontFix or ArmadaFixed, with an optional note and link |
| Armada: Add Note | Add a note to the entry |
| Armada: Ask Armada About This Failure | Open the dashboard Ask page with the canonical prompt pre-filled |
| Armada: Show Black Box Timeline | Open the entry's combined timeline as Markdown (100 events per page, with Next Page and Previous Page) |
| Armada: Compare Black Box Attempts | Pick another attempt of the mission (or enter any entry id) and open the side-by-side diff |
| Armada: Draft Armada Fix Mission | Copy the entry's top proposed Armada change as a mission brief and open it in the editor chat (copied only when the editor has no chat) |
| Armada: Show Resilience Gaps | Open the Armada resilience gaps (handoff and landing failure modes triaged ArmadaDefect, most severe first) as Markdown, 25 per page with Next Page and Previous Page; filtered to the open repository's vessel when exactly one matches |
| Armada: Preview Mission Overlap | For the open repository's vessel (or one you pick), enter a title and brief and see the overlap and brief lint warnings; nothing is dispatched |

Every entry command works from the tree view's context menu, from the entry view, or from the Command Palette (where
it asks which entry).

The Ask Armada prompt is: "Show me the pertinent details from the black box for mission {id} and give me your opinion
on why it failed and what could have been done differently". The extension opens `/dashboard/ask?prompt=<prompt>`, which
starts a new conversation with the prompt in the message box; if the browser cannot be opened, the prompt is copied to
the clipboard instead.

## Errors

The extension never crashes on server errors. It explains each one: no connection, an unreachable Admiral, an Admiral
or extension that is too old, a rejected key (401), a forbidden action (403), a missing entry (404), a changed mission
or expired confirm token (409), a missing black box object (410), and an unavailable black box store or action host
(503). The Failures view shows the current error with a click-to-retry row.

## How it is built

- **Thin client.** The extension imports the dashboard's own API client, types and helpers (`@dashboard/api/client`,
  `@dashboard/types/*`, `@dashboard/lib/*`) through the `@dashboard/*` alias the mobile app uses (`tsconfig.json`
  paths and the esbuild alias in `esbuild.mjs`), so there is no second copy of any route or DTO.
  `npm run lint:shared` lints those shared modules against the same sharing contract as the mobile app
  (`src/Armada.Mobile/sharing.config.js` and `eslint.shared.js`) plus the Node host's rules, so a dashboard change
  that breaks a non-browser host fails here and in the mobile app.
- **Shared client gaps.** `src/transport/hostClient.ts` holds the few things the shared client does not: pointing it at
  an Admiral with the extension's credential, a typed reading of the health response (the shared `getHealth` returns a
  plain record), the mission/voyage/entry id dispatch for black box lookups, and dashboard URL builders.
  `src/transport/errors.ts` turns the shared client's errors into messages.
- **Layout.** `src/connection/` (discovery, contract check, connection manager, status bar), `src/transport/`
  (shared client host side, the `/ws` event bus), `src/surfaces/nav/` and `src/surfaces/blackbox/`.
- **Parity.** `parity.json` tracks every dashboard route, tab, client call, event and settings field, the TUI-only
  surfaces, Harbor's menu and windows, and the CLI commands in scope, with the status of each; it is generated by
  `scripts/vscode/generate-parity-manifest.py` and checked by the `VsCode.Parity` test suite.

## Development

```bash
cd src/Armada.VSCode
npm ci
npm run build              # type check (src and tests) and bundle to dist/extension.js
npm run lint               # ESLint, then the shared-module contract (lint:shared)
npm test                   # unit tests (Node test runner, fake vscode module, no VS Code needed)
npm run test:integration   # @vscode/test-electron; skipped when offline or without a display
npm run package            # ../../vsix/armada-vscode-<version>.vsix
```

The integration tests never read your `~/.armada`: they point `ARMADA_DATA_DIR` at an empty temporary directory. To
run them against a throwaway Admiral, start one with its own data directory and set `ARMADA_VSCODE_TEST_DATA_DIR` to
it and `ARMADA_VSCODE_EXPECT_ADMIRAL=1`; the suite then checks the connection, whoami, the role keys, the navigation
tree, the black box store and the WebSocket subscription.

From the repository root, `build-vscode.sh` (or `build-vscode.bat`) runs install, build, lint, tests and packaging into
`vsix/`, and the `vscode-extension` GitHub workflow does the same in CI, plus the integration tests and the parity
check.

## License

MIT. See [LICENSE.md](LICENSE.md).
