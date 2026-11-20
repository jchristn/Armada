# VS Code extension plan: full Armada parity inside the editor

Status: approved plan; milestone V0 in progress (see the status note in section 7). Written 2026-11-19 against `main` at `294174381` (v1.3.0). The owner's decisions
(2026-11-19) are recorded in section 12 and folded into the sections below.

## 1. Goal

Make the VS Code extension a full Armada surface, on par with the web dashboard, the TUI, Harbor (the desktop/tray app)
and the CLI, so a developer can run Armada without leaving the editor. Parity means every capability those surfaces
offer is reachable inside VS Code with the same actions and the same results, tracked by a generated manifest and
enforced by a test suite and a release gate, the way the TUI and Mobile apps already are.

The approach follows Mux's VS Code extension (`~/Code/Mux/src/Mux.VSCode`, published as `usemux.mux-ai`) and its
publishing runbook (`~/Code/Mux/PUBLISH_VSCODE.md`), keeping what worked there and fixing what Mux's history shows
broke (section 10).

## 2. Where things stand

**The extension today** (`src/Armada.VSCode`, v1.3.0, about 3,700 TS lines plus 2,400 test lines) covers only the
mission and voyage black box: an "Armada Failures" tree in the Explorer, one entry webview, 18 commands, conflict and
hotspot file decorations, live updates over `/ws` with a polling fallback, and an API key in `SecretStorage`. It has its
own hand-written client (`src/api/blackbox.ts`, about 30 routes) and DTOs (`src/api/types.ts`), and imports nothing from
the dashboard. It builds with esbuild, tests with Node's test runner plus an activation-only `@vscode/test-electron` run,
and packages with `build-vscode.sh`. No workflow, Publisher channel or release step builds, tests or publishes it; the
`.vsix` on the v1.3.0 release was uploaded by hand, and it is not on the Marketplace or Open VSX.
`ReleaseVersionSuite.cs:47` already locks its version to `Constants.ProductVersion`.

**What parity has to cover:**

| Surface | Size | Source of truth |
| --- | --- | --- |
| Web dashboard | 87 page modules: 52 routes, 9 tabbed hubs with 47 tabs, about 495 client exports (462 call the server), 74 settings fields, 29 WebSocket event families | `src/Armada.Dashboard/src/App.tsx`, `lib/dashboardNavigation.ts`, `api/client.ts` |
| TUI | 709 parity entries: 699 implemented, 5 not-applicable, 5 TUI-only extensions (`/approvals`, `/setup`, catalog/OpenAPI/raw calls) | `src/Armada.Tui/parity.json` |
| Mobile | 711 parity entries, 104 routes, shares the dashboard's `api/`, `types/`, `lib/` via the `@dashboard/*` alias | `src/Armada.Mobile/parity.json`, `sharing.config.js` |
| Harbor | Tray menu, main window (running jobs, activity), Status window (overview, activity charts, logs), Settings window (General, Repositories, Admiral settings), headless host runtime | `src/Armada.Harbor`, `Core/Hosting/HarborMenuCommandEnum.cs`, `HarborLinkHost.cs` |
| Helm CLI | `go`, `status`, `watch`, `log`, `diff`, `doctor`, `inbox`, `reset`, plus `server`, `config`, `profile`, `mcp`, `mission`, `voyage`, `blackbox`, `action`, `vessel`, `captain`, `fleet`, `backlog`, `playbook` groups | `src/Armada.Helm/Program.cs:235-576` |

The server already exposes everything needed: 442 REST routes (`docs/REST_API.md`, live `/openapi.json`), `/ws` with 81
events and 65 admin commands (`docs/WEBSOCKET_API.md`), and 273 MCP tools on port 7891.

## 3. Design principles (from Mux, adjusted for Armada)

1. **Thin client over the Admiral.** No orchestration logic in TypeScript. VS Code is another surface onto the same
   Admiral, like the dashboard, TUI and Mobile app. When the editor needs something the server lacks, the fix goes into
   the server or the shared dashboard modules (section 8), never a TS reimplementation. This is the stance in
   `~/Code/Mux/archive/VSCODE_EXTENSION.md:5-7`.
2. **Native where VS Code has a native shape; webviews only where it does not.** Mux used trees, quick picks, input
   boxes, the status bar and notifications for lists, navigation and actions, and webviews only for chat, charts and
   adaptive forms. Armada does the same, and goes further on editor integration (diffs, logs, terminals, file systems,
   decorations) because Armada's work product is code in the developer's repository.
3. **Reuse the dashboard's code; do not fork it.** Mux rewrote its webview UI and chart logic instead of reusing the web
   app, and now maintains three copies. Armada already shares the dashboard's client, types and helpers with Mobile
   through `@dashboard/*` under the `lint:shared` contract; the extension host and its webviews use the same modules,
   and the webviews render the dashboard's own React components where a page is not native.
4. **One parity manifest, generated, gated.** `src/Armada.VSCode/parity.json` is built from the same dashboard parser
   as the TUI and Mobile manifests, plus Harbor and CLI entries, and `VsCodeParitySuite` fails on gaps.
5. **Use the shared local identity.** Like Mux reading the shared `~/.mux/settings.json` key, the extension reads
   `~/.armada/settings.json` for the local Admiral's port and key, so a local developer connects without prompts and
   shares one Admiral with the CLI, TUI and Harbor.

## 4. Architecture

### 4.1 Extension host

```
src/Armada.VSCode/
  src/
    extension.ts            activation (onStartupFinished, no network work during activate)
    connection/             discovery, lifecycle, auth, contract check, profiles
    transport/              shared dashboard client configured for the host; one /ws event bus
    surfaces/
      nav/                  Armada view container: navigation tree (OPERATIONS, BUILD, CONFIGURATION, SYSTEM)
      needsYou/             Needs You tree, badge, notifications with actions
      missions/ voyages/ vessels/ captains/ ...   one folder per domain: trees, commands, editor integration
      harbor/               Harbor host management (section 4.5)
      blackbox/             today's black box features, moved onto the shared client
    editor/                 diff documents, log channels, file system providers, terminals, decorations, CodeLens
    webview/                panel host: one React bundle, router, theme bridge, message protocol
    chat/                   optional @armada chat participant (section 4.6)
    mcp/                    MCP server definition provider (section 4.6)
  webview-ui/               the webview React app (built by esbuild/Vite, imports @dashboard/*)
  parity.json
```

- **Shared client in the host.** The host imports `@dashboard/api/client`, `@dashboard/types/*` and
  `@dashboard/lib/armadaSocket` through the same alias Mobile uses, configured with `configureClient({ baseUrl,
  headers, platform })` and `buildSocketUrlForServer`. The hand-written `api/types.ts` and `api/blackbox.ts` go away.
  The extension adds itself to the `lint:shared` contract so a dashboard change that breaks a non-browser host fails CI
  for both Mobile and VS Code.
- **One event bus.** A single `/ws` connection per Admiral (Node 22's global `WebSocket`, as today) subscribes once and
  fans events out to trees, open panels and the status bar. It keeps the existing backoff (5 s to 300 s) and 60 s
  polling while down. Webviews never open their own sockets.
- **All network traffic goes through the host.** Webviews talk to the host with `postMessage`; the host makes every
  REST call and owns the socket. This is what makes Remote-SSH, WSL and Codespaces work (a webview runs on the local UI
  side, so a webview `fetch` to `localhost` would reach the wrong machine), keeps tokens out of webview storage, and
  keeps the webview CSP at `connect-src 'none'`.

### 4.2 Native surfaces

| VS Code feature | Armada use |
| --- | --- |
| Activity bar view container `armada` | Navigation tree mirroring the dashboard nav model (`lib/navModel.ts`, shared with Mobile); Needs You; Missions and Voyages; Vessels; Captains and Docks; Harbor. The black box tree ("Armada Failures") moves here from the Explorer; a setting (`armada.failures.showInExplorer`, default off) also shows it in the Explorer for people used to it there (decision 4). |
| Tree views with inline actions and context menus | Every list page: filter, sort and paging from the shared enumeration helpers; inline approve/deny, cancel, restart, retry, land, purge; `TreeDragAndDropController` for backlog and persona-priority reordering |
| View badges | Needs You count (from the same inbox kinds as `lib/inboxKinds.ts`), black box open entries |
| Status bar | Admiral connection and target (local or profile), running and waiting missions, captain slots in use, Harbor link state; click opens a quick pick of actions |
| Notifications with buttons | Approval-needed, landing failed, CLI permission requests (modal with a countdown for permission prompts), model alerts, starving missions, deployment approvals; each deep-links to its panel |
| Quick picks and input boxes | Dispatch from the palette or editor selection (vessel, captain, persona and pipeline pickers), profile switch, command palette for every action (gated by `armada.connected` and role context keys) |
| Virtual documents and `vscode.diff` | Mission and attempt diffs: per-file side-by-side diffs through an `armada-diff:` content provider (needs the server change in 8.3); unified diff as a `.diff` document until then. Instructions, briefs, playbooks and reports as Markdown documents with preview |
| `LogOutputChannel` per followed mission or captain | Live mission logs, captain logs, Harbor and Admiral logs, replacing the dashboard's 1 s log polling with event-driven appends |
| `FileSystemProvider` `armada-dock:` | The dashboard Workspace page (file tree, edit, save, search) becomes real editor tabs on a dock or vessel checkout the Admiral serves; for docks on this machine, open the folder directly instead |
| Terminals (`Pseudoterminal`) | Workspace exec and `openWorkspaceTerminal`; `armada watch` style live voyage progress |
| File decorations, CodeLens, Comments API | Existing conflict and hotspot decorations; owned-path and sibling-overlap markers from mission ownership; PR evidence (review comments) on changed files |
| SCM integration (read-only) | Show a mission's changed files grouped like a source-control view, with open-diff and land actions |
| Walkthrough (`contributes.walkthroughs`) | First-run: install Armada, connect, add a vessel, create captains, dispatch a mission; replaces nothing, points at the setup wizard panel |

### 4.3 Webview panels

Panels render what has no native shape: rich detail pages, chat, charts and large forms. One React bundle
(`webview-ui/`) built from the dashboard's own pages and components, with the host injecting everything that is
browser-specific in the dashboard today:

| Dashboard assumption | Webview replacement |
| --- | --- |
| `BrowserRouter basename="/dashboard"` (`App.tsx:67`) | `MemoryRouter`; the host opens a panel at a route and receives navigation requests so links can open native views or new panels |
| `localStorage['armada_session_token']`, theme, page sizes, notifications, composer drafts (23 files) | Host-backed storage adapter (`globalState`/`workspaceState`; tokens only in `SecretStorage`, never in the webview) |
| `buildSocketUrl(window.location)` (`WebSocketContext.tsx:57`) and other `window.location` uses (ApiExplorer, Server MCP snippets, UsageObservabilityCard, Layout) | Live-event context fed by the host's event bus; server origin passed in config |
| Downloads by anchor click (`platform/browserPlatform.ts`, History export), `<input type=file>` restore, clipboard, `window.confirm`, `target="_blank"` | `ClientPlatformAdapter` that posts to the host: `showSaveDialog`, `showOpenDialog`, `env.clipboard`, modal `showWarningMessage`, `env.openExternal` |
| `/dashboard/i18n/armada.json` (7.4 MB) | Fetched by the host from the Admiral at runtime and passed in, so the VSIX does not carry it; follows `vscode.env.language` |
| CSS variables on `[data-theme]` (`index.css`, 61 variables) | Theme bridge stylesheet mapping them to `--vscode-*` tokens, switched by the `vscode-light`/`vscode-dark`/`vscode-high-contrast` body classes |
| Cmd/Ctrl-K command palette | Disabled in webviews; VS Code's palette carries the commands |
| 973 inline `style={{}}` uses | CSP `style-src ${cspSource} 'unsafe-inline'`; `script-src 'nonce-...'`; bundle every script into the nonce'd bundle (Mux's separately loaded script sometimes failed to load, lesson 7 in 10.2) |

Panel reuse rules: one panel per entity (reveal if open), `retainContextWhenHidden` only for chat and charts, and
serialization (`WebviewPanelSerializer`) so open panels survive a reload.

### 4.4 Connection and lifecycle

Modeled on Mux's `src/server/lifecycle.ts`:

1. Read `~/.armada/settings.json` (`AdmiralPort`, key) for the local Admiral; settings `armada.url` and profiles
   (shared with `~/.armada/tui.json` where possible) for others.
2. Probe `GET /api/v1/status/health`, check the contract version (8.3), then prove the credential with
   `GET /api/v1/whoami`. Role context keys (`armada.role.globalAdmin`, `armada.role.tenantAdmin`) gate admin commands
   and menus, matching the server's tiers.
3. If no Admiral is reachable and `armada.server.autoStart` is on, start one with the **bundled** CLI
   (`armada server start`, from the extension's own `bin/`, section 4.7). The extension always runs its bundled,
   version-matched binaries and never an installed Armada on `PATH` (decision 2), but it connects to an Admiral that is
   already running (for example the tray app's or one started from a terminal) instead of starting a second one, and a
   contract-version mismatch with that Admiral is reported, not papered over. If the bundled binary cannot start (for
   example a quarantine or permission problem), show the exact failure and the fix, not a timeout (Mux lesson 10).
   Kill only an Admiral the extension started.
4. Concurrent callers share one in-flight connect.
5. Auth modes: local key (zero prompt), credential token (Bearer), username and password through
   `POST /api/v1/authenticate` (`X-Token`, 24 h, refreshed), and remote access through Armada.Proxy (challenge login with
   `setCookie:false`, then `X-Armada-Proxy-Session`). Through the proxy, MCP and the Harbor link are unavailable, and the
   blocked operations (server stop/reset, restore, settings and admin writes) are hidden with an explanation.

### 4.5 Harbor in VS Code (desktop parity)

Harbor's UI (tray, main, status and settings windows) maps to an Armada "Harbor" view; its host runtime stays the C#
binary.

- **Status and jobs:** link state in the status bar; running jobs with their latest activity, output in a log channel;
  24 h and 7 d activity charts from `/api/v1/harbors/{id}/metrics`; enable, disable and stop-job actions.
- **Run a Harbor from VS Code:** start and stop the bundled `armada-harbor --headless` (`HarborHeadlessRunner.cs`) as
  a child process, or detect the tray app already running and leave it in charge (the single-instance lock in 8.4
  decides). Rewriting the runtime in TS (about 2,600 lines plus the per-runtime argument building) is out of scope.
- **Settings:** a panel for Harbor's General and Repositories settings (written through the Harbor changes in 8.4) and
  the Admiral settings tab (already REST).
- **Menu parity:** Open Dashboard, Copy MCP URL, Copy Diagnostics, Connect, Disconnect, Restore Previous Version.

In Local mode (the default) no Harbor is needed and the view says so.

### 4.6 AI integration native to VS Code

- **MCP server definition provider** (`contributes.mcpServerDefinitionProviders`,
  `vscode.lm.registerMcpServerDefinitionProvider`): registers the connected Admiral's MCP endpoint
  (`http://<host>:7891/mcp`, with its bearer header) so Copilot agent mode and other MCP-aware chat in VS Code can use
  Armada's 273 tools with no config file. This also covers the VS Code target missing from `armada mcp install`.
- **Ask Armada** keeps full parity in a webview (streamed chunks, thinking, tool calls, work cards, proposals, CLI
  permission cards, quick actions, thread management).
- **`@armada` chat participant** (V2, decision 3): maps a VS Code chat turn onto an Ask thread for quick questions,
  streams the reply, and hands rich results (proposals, work cards, CLI permission prompts) to the Ask panel with a
  button. Slash commands for the common intents (`/dispatch`, `/status`, `/why` for a failed mission). Mux did not do
  this. Needs a parity entry of kind `extension`.
- **Hand-offs that exist today stay:** FIX THIS briefs to the editor chat, draft-a-fix-mission.

### 4.7 Bundled binaries (platform-specific VSIX)

Each platform VSIX (decision 2) carries the self-contained Armada CLI (with the embedded Admiral and `armada tui`) and
headless Harbor under `bin/`, built by the same Publisher self-contained publish the installers use.

- Targets: `win32-x64`, `win32-arm64`, `darwin-x64`, `darwin-arm64`, `linux-x64`, `linux-arm64` (`vsce package
  --target`), one per RID in the INSTALLERS.md matrix. VS Code installs the VSIX that matches the extension host, so a
  Remote-SSH or WSL session gets the Linux build on the remote side automatically. `alpine-*` and `web` are not built;
  the listing says so.
- Size: about 100 MB per target with both binaries. Check both registries' size limits before V1 and trim (for example
  `PublishTrimmed` where the runtimes allow it) if needed.
- macOS: ad-hoc `codesign --sign -` every bundled executable (INSTALLERS.md "ad-hoc sign or the app is damaged"), and
  verify a launch from a VSIX installed through the Marketplace on a clean Mac.
- Executable bits: confirm the zip keeps them; if an install loses them, `chmod +x` the bundled binaries on first
  activation.
- Data directories stay shared (`~/.armada`, `~/.armada-harbor`), so the extension, the installers, the TUI and the tray
  app see the same Admiral data, key and Harbor identity.
- The extension version, the bundled binaries and the Admiral it starts are always the same product version (9.3).

## 5. Capability mapping by domain

"Native" means trees, commands, documents and editor features; "Panel" means a webview built from the dashboard's
components. Every row carries all actions of the dashboard page and the TUI screen.

| Domain (dashboard routes) | Native | Panel | Notes |
| --- | --- | --- | --- |
| Home (`/`) | Status bar summary, nav tree root counts | Home panel (KPIs, mission history chart, voyage progress) | |
| Needs You (`/inbox`, TUI `/approvals`) | Tree with all 12 inbox kinds, badge, notifications with approve/deny/apply/ignore | Item detail opens the owning panel | TUI-only `/approvals` maps here |
| Ask Armada (`/ask`) | Thread tree, unread badge, `Armada: Ask` command with selection context | Chat panel (all `ask.*` streaming events, cards, proposals, permission countdown) | Optional chat participant |
| Dispatch, Planning, Backlog, Objectives, Refinement (`/dispatch`, `/backlog/:id`, `/objectives/:id`) | Quick dispatch from palette or selection (brief lint and overlap preview before send); backlog tree with drag reorder; GitHub import command | Dispatch form, Planning and Refinement chat, Objective detail | |
| Missions, Voyages (`/missions`, `/missions/:id`, `/voyages/:id`, `/voyages/create`) | Trees filtered by status, gate (`waitingGate`), parent (`rootsOnly`); voyage mission tree as a tree view; per-file diffs; log channel; instructions document; restart, retry, cancel, transition, landing | Mission detail (waits, hierarchy, captain selection and recovery, PR evidence, landing preview), Voyage detail and create | Hierarchy fields from v1.3.0 (`Key`, `ParentKey`, `BranchStrategy`, owned paths) in the create flow |
| Merge queue (tab, `/merge-queue/:id`) | Tree; process one or all, cancel, enqueue | Entry detail | |
| Black box (tab and sub-tabs, `/blackbox/:id`) | Today's tree, decorations, FIX THIS, recovery with dry run; resilience gaps, fingerprints, hotspots as trees | Entry detail (migrated onto shared components), retention, storage and GC admin | Existing features keep working throughout |
| Fleet actions (`/fleet-actions`, runs) | Actions and runs trees; run with vessel and input pickers; follow-up from a run | Action editor, run monitor and reports | |
| Vessels, Fleets, Workspace (`/vessels`, `/fleets/:id`, `/workspace/...`) | Vessel tree matched to the open workspace's git remotes (today's `repoUrl` logic); add the current repo as a vessel; branches, push, merge; workspace through `armada-dock:` and terminals | Vessel detail, import wizard, health, history heatmap and timeline, onboarding, cleanup | |
| Captains, Docks, Evaluations, Battery (`/captains`, `/docks/:id`, `/evaluations`) | Trees; stop, recall, restart, unquarantine; captain log channel; dock open-folder | Captain detail (tools, usage, CLI policy), Evaluate dialog, probe runs, battery pack editor | Battery packs edit files on the Admiral's file system; VS Code can do that through a file system provider, which the TUI could not |
| Configuration (`/configuration` hub and detail routes) | Trees for personas, pipelines, prompts, playbooks, workflow and project profiles, skills, endpoints, harbors, memory; prompts and playbooks edit as Markdown documents through a file system provider with save-to-server | Persona (priority editor), pipeline, workflow profile (validate, preview, resolve), endpoint health and evaluate | Editing prompt templates in the real editor is a parity-plus |
| Delivery (`/delivery` hub and detail routes) | Trees; approve, deny, verify, rollback; check-run retry | Deployment, environment, release, incident, check run (compare), runbook execution | |
| Activity (`/activity?source=...`, `/requests/:id`, `/signals/:id`, `/events/:id`) | Events and signals trees; send signal | History (with export through the save dialog), request history and replay, token usage, captain usage and account limits, concurrency charts | All charts are hand-rolled SVG in the dashboard; they render unchanged in panels |
| Server and admin (`/server` hub) | Server lifecycle commands (start, stop, restart), doctor results as a tree with problems surfaced in the Problems panel | Server settings (74 fields), storage, diagnostics, CLI permissions, tenants, users, credentials, backup and restore | Proxy-blocked actions hidden remotely |
| API Explorer (`/api-explorer`) | | Panel | TUI keeps it too |
| Setup wizard (modal, TUI `/setup`) | Walkthrough | Wizard panel; completion stored server-side (Mux lesson 11) | |
| Harbor (desktop app) | Section 4.5 | Harbor settings and metrics | New manifest kind `harbor` |
| CLI-only (`server`, `config init`, `doctor`, `mcp install`, `profile`, `reset`, `go`, `watch`) | Commands and terminals; `go` maps to quick dispatch, `watch` to a live voyage terminal | | New manifest kind `cli`; only the entries a VS Code user needs |

Server capabilities with no dashboard UI today (model scorecards, alert thresholds, stage outcomes, failure-cause rules)
are out of scope for parity, the same as for the TUI and Mobile.

## 6. Parity tracking

- `scripts/vscode/generate-parity-manifest.py` reuses `scripts/tui/dashboard_surfaces.py` and `client_exports.py`
  (routes, `HUB_PAGES` tabs, events, client exports, settings fields), adds the TUI's extension entries, Harbor menu
  commands and windows (`kind: harbor`), and the CLI commands in scope (`kind: cli`).
- Statuses: `implemented`, `planned`, `not-applicable` (with notes), `extension` (VS Code only, with notes).
  `implemented` means reachable natively or through a shared-component panel with the same actions. Reaching a page only
  through the interim dashboard fallback (below) does not count.
- Each implemented entry names its surface (`view`, `command`, `panel route`, `document provider`), so
  `VsCodeParitySuite` (Test.Shared, modeled on `TuiParitySuite.cs` and `Infrastructure/ParityManifest.cs`) can check
  that it resolves to a real contribution in `package.json` or a registered panel route in `webview-ui`.
- `--check` runs in CI. During the milestones `planned` entries are allowed; the release gate in `docs/RELEASING.md`
  requires zero `planned` once the extension is declared at parity (milestone V8).
- **Interim fallback:** until a domain is ported, an "Open in dashboard" panel can load the Admiral's `/dashboard` route
  in an iframe (the server sets no `X-Frame-Options`). It keeps the extension useful during the build-out and is
  removed at V8.

## 7. Milestones

Each milestone ends with the repository's usual completion requirements: tests, a UX pass in light, dark and high
contrast at narrow and wide editor widths (screenshots kept in the scratchpad, not committed), docs and CHANGELOG, and
the parity manifest updated. Sizes are relative (S, M, L, XL).

| # | Milestone | Contents | Size |
| --- | --- | --- | --- |
| V0 | Foundations | View container and nav tree; connection manager (4.4) with local discovery, autostart, contract check, all auth modes, profiles; shared client through `@dashboard/*` and `lint:shared`; event bus; webview host (router, theme bridge, platform and storage adapters, host transport, CSP, serializer); black box moved onto the shared client with its tests passing; parity generator and suite with everything `planned`; CI workflow build job (9.2); interim dashboard fallback | XL |
| V1 | Operations core | Home, Needs You (native plus notifications), Missions, Voyages (trees, panels, diffs, logs, mission tree), quick dispatch and voyage create with hierarchy, merge queue, captains and docks, status bar; platform-specific packaging with bundled binaries (4.7); publishing setup (9.4) and the first pre-release | XL |
| V2 | Conversations and planning | Ask Armada panel with full streaming parity, Planning, Backlog (drag reorder), Objectives, Refinement; MCP server definition provider; `@armada` chat participant | L |
| V3 | Vessels and workspace | Vessel and fleet trees with workspace-remote matching, add current repo, import wizard, health, history, onboarding, branches, cleanup; `armada-dock:` file system provider and terminals | L |
| V4 | Fleet actions, black box, delivery | Fleet actions and runs; remaining black box tabs and admin; deployments, environments, releases, incidents, check runs, runbooks | L |
| V5 | Configuration and activity | Personas, pipelines, prompt templates and playbooks as editable documents, workflow and project profiles, skills, endpoints, memory; history, request history, token usage, captain usage, concurrency, events, signals; API explorer | L |
| V6 | System and administration | Server settings, storage, diagnostics and doctor (Problems panel), CLI permissions, tenants, users, credentials, backup and restore, server lifecycle, setup wizard and walkthrough | M |
| V7 | Harbor | Harbor view, status bar link state, jobs and logs, metrics, headless start and stop, settings panel, menu parity; depends on 8.4 | M |
| V8 | Hardening and parity gate | Remote-SSH, WSL and Codespaces passes; proxy mode; localization; accessibility (keyboard paths and screen reader labels for every tree and panel); performance with large fleets; remove the dashboard fallback; flip the gate to zero `planned`; docs; first stable Marketplace and Open VSX release | L |

**Status (2026-11-19): V0 slice 1 is done** on `feature/vscode-extension`: identity `latticetech.armada`; the Armada
view container with the navigation tree (pages open in the browser) and the black box tree (also in the Explorer behind
`armada.failures.showInExplorer`); the connection manager with local discovery, the remote URL and key flow, health,
contract check, whoami and role context keys, a shared in-flight attempt, the status bar and the output channel; the
shared client through `@dashboard/*` with the extension in the `lint:shared` contract; the `/ws` event bus; the
`ContractVersion` field on the health route; the parity generator and `VsCode.Parity` with everything else `planned`;
and the CI `build` job. Remaining in V0: the webview host (router, theme bridge, platform and storage adapters, host
transport, CSP, serializer) and the interim dashboard fallback panel; the dashboard shared-module changes in 8.1
(injected transport and socket factory, event-source interfaces); Admiral autostart (needs the bundled binaries from
4.7); the other auth modes (username and password, proxy) and profiles.

V1 through V6 can overlap once V0 lands, since each domain is a separate folder over the same infrastructure. The
first Marketplace and Open VSX publish is a pre-release at the end of V1 (decision 6); each later milestone ships as a
pre-release, and V8 publishes the first stable release once the parity gate passes. So V1 also includes the one-time
publishing setup (9.4) and the platform-specific packaging (4.7).

## 8. Changes outside the extension

### 8.1 Dashboard shared modules (`src/Armada.Dashboard`)

- `configureClient` accepts an injected transport (a `fetch` implementation) and the socket helper accepts a socket
  factory, so the host can route calls through its own connection and tests can fake it.
- Live-update hooks bound to `WebSocketContext` (`useLiveRefresh`, `useInboxCount`, `useAskUnreadCount`) take an
  event-source interface the dashboard, Mobile and the webview each implement.
- Replace the remaining `window.location` uses with configuration; route downloads, file pickers, clipboard, confirm
  and external links through `ClientPlatformAdapter`; make the i18n catalog path and the theme provider configurable;
  let `App` run under `MemoryRouter`.
- Keep pages importable from the webview: page-level components take their data hooks from context rather than
  importing browser-bound modules directly. Done domain by domain as V1 to V6 reach each page.
- All of this keeps the dashboard's behavior unchanged and is covered by its existing Vitest suites.

### 8.2 Mobile

No functional change. It gains the same injected-transport and event-source interfaces, and its `lint:shared` contract
now protects the extension too.

### 8.3 Server

- `contractVersion` on the health response (and in `api-surface-1.0.json`), with major-version mismatch messages on
  both sides, as in Mux's `src/server/contract.ts`.
- Per-file base and head content for a mission diff (or a file-at-commit read in a dock), so native side-by-side diffs
  work for local and Harbor docks.
- Setup completion stored server-side if it is not already, so every surface agrees on first run.
- No CORS or framing changes are needed (CORS is `*`; host-side transport avoids it anyway).

### 8.4 Harbor

- A single-instance lock, so a headless Harbor started from VS Code and the tray app cannot both run against one
  settings folder.
- Settings overrides through command-line arguments or environment variables, so the extension does not have to edit
  `~/.armada-harbor/settings.json` directly.
- Move `AccessKey` and `Secret` out of plaintext into the OS keychain, as the TUI already does
  (`Armada.Tui/Services/Credentials`).

### 8.5 Helm CLI

- Machine-readable output for what the extension parses: `armada server status --json`, `armada doctor --json`.
- Optionally `armada mcp install --target vscode` writing `.vscode/mcp.json`, for users who do not install the
  extension. The extension itself uses the definition provider (4.6).

## 9. Build, test and publishing

### 9.1 Build and test

- Keep esbuild for the host (`esbuild.mjs`); add a second esbuild entry for `webview-ui` (React, `@dashboard/*`
  aliases). Mux shipped unbundled `node_modules`; Armada stays bundled.
- Pin Node 22 in `src/Armada.VSCode/.nvmrc`; keep `engines.vscode ^1.101.0` matched with `@types/vscode` (raise it only
  if the MCP definition provider or chat participant APIs need it).
- Lint with ESLint plus `tsc --noEmit` (Mux only type-checked), and the shared-module lint.
- Unit tests stay on Node's test runner with the `vscode` fake (`test/setup.ts`); webview components test with Vitest
  next to the dashboard's own tests.
- Integration tests with `@vscode/test-cli`/`@vscode/test-electron` against a real Admiral started on a throwaway data
  directory (seeded with `Armada.PerfSeed`), covering connect, a dispatch through to a diff, an approval from Needs You,
  live updates and a webview round trip. Mux's streaming integration test is still skipped; Armada's must run in CI
  under `xvfb-run`.
- `VsCodeParitySuite` runs in the .NET suites with the other parity suites.
- The extension ID lives in `package.json` only; tests read it from there (Mux hard-coded it in a test and broke a
  publish, lesson 2).

### 9.2 CI and release

Following Mux's `.github/workflows/vscode-extension.yml`, adjusted to `requirements/INSTALLERS.md`:

- **`vscode-extension.yml`, job `build`:** on pushes and PRs touching `src/Armada.VSCode/**` or the dashboard's shared
  modules, and on `workflow_dispatch`. Steps: `npm ci`, lint, i18n check, unit tests, `xvfb-run -a` integration tests,
  parity `--check`, and a `vsce package --target linux-x64` smoke package. PR builds skip the other five targets.
- **Platform packaging:** a matrix over the six targets. Each job publishes the CLI and headless Harbor for its RID
  with the Publisher, ad-hoc signs them on macOS (so the darwin targets build on `macos-latest`), copies them into
  `bin/`, runs `vsce package --target <target>`, smoke-tests the bundled `armada --version` on its own OS, and uploads
  `armada-vscode-<version>-<target>.vsix`.
- **`release.yml`:** add the six `.vsix` files to the fan-in, so they land on the GitHub release with every installer
  and are covered by `SHA256SUMS` (today one `.vsix` is uploaded by hand and is not checksummed).
- **Marketplace and Open VSX publish:** a job behind a switch that runs after the GitHub release, as INSTALLERS.md
  requires for package-manager channels: `vsce publish --packagePath <all six>` and `ovsx publish` for each target,
  with `--pre-release` until V8, gated on `ENABLE_PACKAGE_MANAGERS` (or Mux's `workflow_dispatch` input
  `publish=true`), each step skipping with a visible note when its secret is missing. Mux removed tag-triggered
  publishing; keep publishing a deliberate step.
- Local `build-vscode.sh`/`.bat` stay for packaging; add `scripts/<os>/publish-vscode.*` that publish to **both** the
  Marketplace and Open VSX (Mux's local scripts skip Open VSX).

### 9.3 Versioning

Lockstep with the product version, as `ReleaseVersionSuite.cs:47` already enforces. Every product release publishes the
extension; an extension-only fix ships in the next product patch release. This is the single documented policy Mux
lacked (its version drifted three times). From V1 to V7 the extension publishes with `--pre-release`. The Marketplace
treats every published version as final, so a pre-release version must be a plain `major.minor.patch` that is never
reused for the stable channel; the release runbook picks those numbers with the owner (VERSIONING.md: an agent never
chooses a version).

### 9.4 One-time publishing setup ([HUMAN])

From `~/Code/Mux/PUBLISH_VSCODE.md`:

1. Create an Azure DevOps organization at `https://aex.dev.azure.com/me` in a private window (`dev.azure.com` bounces to
   the Azure portal, which has no PAT option).
2. Create a PAT with Organization **All accessible organizations** and scope **Marketplace > Manage**; store it as
   `VSCE_PAT` with `gh secret set VSCE_PAT --repo jchristn/Armada`.
3. Create the Marketplace publisher at `https://marketplace.visualstudio.com/manage` with the **same Microsoft account**
   that issued the PAT.
4. Open VSX: sign in with GitHub, sign the Eclipse publisher agreement, create a token, run
   `npx ovsx create-namespace <publisher> -p <token>` once, and store the token as `OVSX_PAT`.
5. Confirm the extension `name` and `displayName` are free on both registries before the first publish. Mux had both
   rejected (`mux`, then the displayName). The decided identity is the reverse-DNS identifier `ai.latticetech.armada`, which
   the registries spell as publisher `latticetech` and name `armada` (extension ID `latticetech.armada`; publisher IDs
   cannot contain dots), with display name "Armada", falling back to "Armada for VSCode" if "Armada" is taken
   (decision 1). Change `publisher` and `name` in `package.json`, the only place the ID lives.

### 9.5 Listing

Icon (128x128 from `assets/`), `README.md` written as the Marketplace page with screenshots of the trees, a mission
diff, Needs You and Ask (Mux has none), `CHANGELOG.md`, `LICENSE.md` plus `"license": "MIT"`, `repository.directory`,
`homepage`, `bugs`, categories (`AI`, `Chat`, `Other`) and keywords. A `docs/VSCODE.md` user guide (Mux's
`docs/VSCODE.md` is the model: install, connect, each area, settings, troubleshooting by failure class), linked from the
root README.

## 10. Learnings from Mux

### 10.1 Keep

- Thin client over the server; fix the server, not the extension (`archive/VSCODE_EXTENSION.md:5-7, 279`).
- Native trees and pickers for lists and actions; webviews only for chat, charts and forms.
- Reuse the shared local key and port; start the server only when needed; kill only what you started; one in-flight
  connect (`src/server/lifecycle.ts:118-206, 383-398`).
- Contract-version check with clear "server too old" and "extension too old" messages (`src/server/contract.ts`).
- `onStartupFinished` activation with no network work during `activate`.
- No telemetry of its own; defer to VS Code's consent.
- Manifest and runtime localization (`package.nls.*.json`, `l10n/bundle.l10n.*.json`) with a check script that fails on
  missing, orphaned or drifted keys (`scripts/i18n-check.mjs`); for Armada, generate them from the shared catalog's
  locales (es, zh-Hans, zh-Hant, yue-Hant, ja, de, fr, it).
- `vscode:prepublish` builds before every publish.
- Status bar connection help: retry, start the server in a terminal, open settings.

### 10.2 Avoid (from Mux's history)

1. Name collisions: check `name` and `displayName` on both registries first.
2. Publisher drift and a test that hard-coded the extension ID: one source for the ID.
3. Node version mismatch between local and CI: pin with `.nvmrc`.
4. Versioning drift: one policy (9.3), stated in CONTRIBUTING and RELEASING.
5. A missing CLI showing as a 15 s timeout: with bundled binaries the CLI cannot be missing, but a binary that fails
   to start (quarantine, lost executable bit, wrong architecture) must surface its real error immediately.
6. A first-run wizard that never fired because state lived in `globalState`: store completion server-side.
7. A webview script loaded separately that sometimes failed: bundle it and load it with a nonce.
8. A dropped first streaming token: test streaming end to end in CI, which Mux still skips.
9. No remote story: declare `extensionKind` and `capabilities` (untrusted and virtual workspaces), route webview traffic
   through the host, and test Remote-SSH, WSL and Codespaces (V8).
10. Lint that only type-checks, and duplicated webview code: ESLint plus shared dashboard code.
11. Local publish scripts that skip Open VSX.

## 11. Workspace trust, remote and security

- `extensionKind: ["workspace"]`, so the extension runs next to the repository and reaches the Admiral on that machine;
  the webview transport (4.1) keeps panels working when the UI is local.
- `capabilities.untrustedWorkspaces: { supported: "limited" }`: read-only browsing works in an untrusted workspace;
  dispatch, recovery paths, workspace exec, terminals and server lifecycle require trust.
  `virtualWorkspaces: false` for actions that need a local git checkout.
- Tokens only in `SecretStorage`; never in settings, webview state or logs (the existing redaction rules apply to
  anything the extension logs).
- Destructive actions (purge, cleanup, reset, restore, recovery paths with `dataLossRisk`) keep the dashboard's
  confirmations as modal dialogs, with the dry runs they have today.

## 12. Owner decisions (2026-11-19)

1. **Publisher and name.** Identifier `ai.latticetech.armada`. On the Marketplace and Open VSX that is publisher
   `latticetech` and extension name `armada` (extension ID `latticetech.armada`), since registry publisher IDs cannot
   contain dots; anywhere a dotted identifier is allowed, use `ai.latticetech.armada`. Display name "Armada", falling
   back to "Armada for VSCode" if the registries reject "Armada". The current `jchristn.armada-vscode` was never
   published, so nothing migrates.
2. **Binaries.** Bundle the self-contained CLI and headless Harbor in six platform-specific VSIX builds, and always run
   the bundled copies, while connecting to an Admiral that is already running rather than starting a second (4.4,
   4.7).
3. **Chat participant.** Build the `@armada` participant in V2 (4.6).
4. **Black box placement.** Move "Armada Failures" into the Armada container, with an opt-in setting that also shows it
   in the Explorer (4.2).
5. **Dashboard refactors.** Approved: refactor the dashboard's shared code domain by domain so its pages render in the
   webviews, with no fork (8.1).
6. **Pre-release publishing.** Pre-releases on the Marketplace and Open VSX from the end of V1; the first stable release
   at V8 (7, 9.3).
