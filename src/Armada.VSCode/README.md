# Armada for VS Code

The Armada extension brings the Armada **mission and voyage black box** into the editor. When a mission or voyage
fails, the Admiral records a black box entry: where it failed, how, why, which files and missions conflicted, and the
ranked recovery paths. This extension lists those entries for the repository you have open, hands the **FIX THIS**
brief to your coding agent, and runs recovery paths with a dry run and a confirmation.

## Features

- **Armada Failures** view in the Explorer: recent black box entries grouped by vessel, with kind, step and age. By
  default it shows only the vessel whose repository is open (matched by git remote URL against the vessel's
  `RepoUrl`). The view badge counts open entries.
- **FIX THIS**: copy the brief, open it as a Markdown document with a preview, or send it to the editor chat.
- **Recovery paths**: a dry run shows what will happen, a modal dialog confirms, and only then does the path run.
  Paths marked `dataLossRisk` are refused unless you tick an explicit acknowledgement.
- **Resolve** entries and **add notes**.
- **Ask Armada about this failure**: opens the dashboard Ask page with the canonical prompt.
- **Entry view**: Summary, Where, How, Status, Why, Conflict, What would have prevented it, What Armada could change,
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
- **Live updates** over the Armada WebSocket, with a polling fallback when the socket is unavailable.

> Screenshot placeholder: the Armada Failures view with an open entry selected.
>
> Screenshot placeholder: the entry view showing the Recovery section.
>
> Screenshot placeholder: a conflicted file decoration in the Explorer.

## Requirements

- VS Code 1.101 or later (or a compatible editor such as Cursor).
- An Armada Admiral 1.2.0 or later with the black box routes, and a credential: a bearer token from
  `POST /api/v1/credentials` (recommended) or the settings API key.

## Install from a VSIX

Build the package (see [Development](#development)) or take `armada-vscode-<version>.vsix` from a release, then:

- In VS Code: open the Extensions view, choose **...** (Views and More Actions), **Install from VSIX...**, and pick the
  file.
- From a terminal: `code --install-extension armada-vscode-1.2.0.vsix`

## Configure

1. Run **Armada: Connect** from the Command Palette.
2. Enter the Admiral URL (for example `http://localhost:7890`; a trailing `/dashboard` is ignored).
3. Enter your API key or bearer token. It is checked against the server and then stored in VS Code secret storage,
   never in settings files.

**Armada: Disconnect** removes the stored key.

| Setting | Default | Meaning |
|---|---|---|
| `armada.url` | `http://localhost:7890` | Admiral URL |
| `armada.tenant` | empty | Optional tenant id; admin credentials use it to narrow lists to one tenant |
| `armada.authHeader` | `Bearer` | Send the key as `Authorization: Bearer` or as the deprecated `X-Api-Key` |
| `armada.dashboardUrl` | empty | Dashboard URL for Ask Armada; empty means `armada.url` plus `/dashboard` |
| `armada.briefAudience` | `agent` | FIX THIS audience: `agent`, `user` or `maintainer` |
| `armada.fixThis.openPreview` | `true` | Open a Markdown preview beside the FIX THIS editor |
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
| Armada: Connect | Prompt for the URL and key, verify, store the key in secret storage |
| Armada: Disconnect | Remove the stored key |
| Armada: Refresh Failures | Reload vessels and entries |
| Armada: Toggle Workspace Vessel Filter | Switch between the open repository's vessel and every vessel |
| Armada: Open Black Box for Mission... | Quick pick of recent failures, or paste a mission, voyage or entry id |
| Armada: Copy FIX THIS | Copy the brief to the clipboard |
| Armada: Open FIX THIS in Editor | Open the brief as an untitled Markdown document (save it as `.md` if you want to keep it) |
| Armada: Send FIX THIS to Chat | Open the editor chat with the brief attached; copies it when the editor has no chat |
| Armada: Run Recovery Path... | Dry run, show the plan, confirm, then run with the confirm token |
| Armada: Resolve Black Box Entry | Set Resolved, Duplicate, WontFix or ArmadaFixed, with an optional note and link |
| Armada: Add Note | Add a note to the entry |
| Armada: Ask Armada about this failure | Open the dashboard Ask page with the canonical prompt pre-filled |
| Armada: Show Black Box Timeline | Open the entry's combined timeline as Markdown (100 events per page, with Next Page and Previous Page) |
| Armada: Compare Black Box Attempts | Pick another attempt of the mission (or enter any entry id) and open the side-by-side diff |
| Armada: Preview Mission Overlap | For the open repository's vessel (or one you pick), enter a title and brief and see the overlap and brief lint warnings; nothing is dispatched |

Every entry command works from the tree view's context menu, from the entry view, or from the Command Palette (where
it asks which entry).

The Ask Armada prompt is: "Show me the pertinent details from the black box for mission {id} and give me your opinion
on why it failed and what could have been done differently". The extension opens `/dashboard/ask?prompt=<prompt>`, which
starts a new conversation with the prompt in the message box; if the browser cannot be opened, the prompt is copied to
the clipboard instead.

## Errors

The extension never crashes on server errors. It explains each one: no connection, a rejected key (401), a forbidden
action (403), a missing entry (404), a changed mission or expired confirm token (409), a missing black box object
(410), and an unavailable black box store (503). The tree view shows the current error with a click-to-retry row.

## Server surface

The extension was written against `MISSION_BLACKBOX_PLAN.md` section 8.1. Every route and DTO lives in `src/api/`
(`blackbox.ts` and `types.ts`), so it can be reconciled with the server in one place.

REST routes:

- `GET /api/v1/vessels?pageNumber=&pageSize=[&tenantId=]`
- `POST /api/v1/blackbox/enumerate`
- `GET /api/v1/blackbox/{id}`
- `GET /api/v1/blackbox/{id}/brief?audience=`
- `GET /api/v1/blackbox/{id}/sections/Conflict`
- `GET /api/v1/blackbox/{id}/notes`, `POST /api/v1/blackbox/{id}/notes`
- `POST /api/v1/blackbox/{id}/resolve`
- `POST /api/v1/blackbox/{id}/recovery/{pathId}`
- `POST /api/v1/blackbox/hotspots/enumerate`
- `GET /api/v1/missions/{id}/blackbox`, `GET /api/v1/voyages/{id}/blackbox`
- `POST /api/v1/vessels/{id}/overlap-preview`, `POST /api/v1/missions/lint-brief`
- `POST /api/v1/vessels/{id}/reservations/enumerate`, `POST /api/v1/vessels/{id}/blackbox-doctor/enumerate`
- `GET /api/v1/blackbox/{id}/diff/{otherId}`, `POST /api/v1/blackbox/{id}/timeline/enumerate`,
  `POST /api/v1/blackbox/{id}/nearby-successes/enumerate`
- `POST /api/v1/blackbox/recovery-stats/enumerate`, `GET /api/v1/voyages/{id}/blackbox/cascade`

WebSocket (`/ws?token=`): `blackbox.captured`, `blackbox.analyzed`, `blackbox.resolved`, `blackbox.deleted`.

## Development

```bash
cd src/Armada.VSCode
npm ci
npm run build              # type check and bundle to dist/extension.js
npm test                   # unit tests (Node test runner, fake vscode module, no VS Code needed)
npm run test:integration   # @vscode/test-electron; skipped when offline or without a display
npm run package            # armada-vscode-<version>.vsix
```

From the repository root, `build-vscode.sh` (or `build-vscode.bat`) runs the same steps.

## License

MIT. See [LICENSE.md](LICENSE.md).
