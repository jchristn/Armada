# Changelog

## 1.5.0 (unreleased)

## 1.4.0

- **Loading indicators everywhere**: dashboard pages show a spinner for every value, table, cell and chart that is still loading, never a default 0 or an empty state; the status bar shows a spinner while missions load and opens the Missions page.
- **Manage is a table of contents**: each hub lists its dashboard tabs (Activity: All Activity, API Requests, Events,
  Signals, Token Usage, Captain Usage, Concurrency; Configuration: Workflow Profiles through Memory; and so on), every
  entry has an icon and opens its page, and nothing expands into lists of raw records (no more rows named by
  `req_...` ids). The tree loads nothing from the Admiral, so it opens instantly; Needs You opens its page and shows its
  count.
- **Lighter on the Admiral**: the background refresh reads the newest 50 missions instead of 250, and record lists are
  no longer loaded for the tree.
- **The dashboard's own pages in editor tabs**: clicking a hub, a Missions tab, a record kind or a record in
  **Manage** opens the dashboard's page for it in an editor tab, with the same charts, forms, pickers and actions as the
  browser. That includes the Overview's mission history, API request chart and token usage heatmap, Activity's All
  Activity, API Requests, Token Usage heatmap, Captain Usage and Concurrency charts, Dispatch and Create Voyage (vessel,
  pipeline and captain pickers, each with a Manage link), Planning (with **Run on** to pick the Harbor), and the backlog
  form with its vessel picker. Every request goes through the extension's connection and live events come from its
  event stream, so no browser sign-in is needed. Each tab has back, forward, reload, breadcrumbs, live status, **Open
  in another tab** and **Open in browser**. Cmd/Ctrl+click a link to open it in its own tab. Open tabs come back after a
  reload. **Armada: Open Page...** lists every page.
- **Needs You opens the item's page**: a review, failure, stall, CLI permission request or model alert opens the page
  where you act on it, instead of a pop-up in the command bar.
- **Create and edit open the dashboard's forms** (backlog item with its vessel, planning session with its Harbor,
  release, prompt template, ...); other kinds open their list page, which has the create button.
- **New settings**: `armada.pages.openRecords` (`dashboardPage` by default, or `details` for the compact detail views
  and expand-only hubs) and `armada.pages.tabReuse` (`area`: one tab per area, `single`, or `never`).
- **All Activity loads again**: the native list asks for what the dashboard's History tab asks for (newest 100, without
  read requests) instead of a query a large timeline could not answer in time.
- **A simpler sidebar, laid out like the dashboard**: three views instead of fourteen, as in Mux. **Ask Armada** (the
  chat), **Conversations** and **Manage**: one tree with Armada Overview and the dashboard's hubs in its order (Needs
  You, Dispatch, Fleet Actions, Missions, Activity, Vessels, Captains, Delivery, Configuration, API Explorer,
  Settings), each record kind under the hub whose dashboard page shows it. Hubs carry their create and run buttons and
  open as editor tabs; the view's menu holds backup, Harbor, MCP, help and account actions.
- **Conversations**: Ask Armada's conversation list, pinned first, with unread counts, age, the summary on hover and
  the open conversation marked. Open, rename, pin, summarize, mark read or unread, archive and delete (several at
  once), search, and show archived conversations.
- **Ask Armada chat layout and telemetry**: your messages on the right, Armada's on the left; each reply shows its
  telemetry (TTFT, first text, streaming and total time, prompt, completion and cached tokens, tokens per second,
  cost, tool calls and their time) with detail on hover, the conversation's totals, and a running timer while a reply
  streams.
- **Where Ask Armada runs**: **Run on** picks a Harbor (this computer's Harbor by default) or the Armada server, and
  **Folder** picks the working directory there (the open workspace folder by default on this computer; `~` expands to
  your home directory), so questions about files are answered from your machine. Needs an Admiral newer than 1.3.0;
  an older one is detected and explained.
- **Editor tabs**: Ask Armada and every sidebar view open as editor tabs (a button on each view, and **Open a View in
  an Editor Tab...**); view tabs are full-width live pages of the view's groups and records with its main actions.
- **Ask Armada conversation settings**: pick the captain (or none), turn auto-approve on or off (with the dashboard's
  warning), and set the CLI tool policy (inherit, ask in Armada, refuse, bypass for tenant and global admins) with the policy in
  force shown; a new conversation starts with the chosen settings.
- **Full dashboard parity, natively**: every entry of the parity manifest (`parity.json`, 759 entries) is implemented
  or not applicable. Added since the first native views: planning and backlog refinement chats that dispatch a plan or
  apply a refinement; the vessel workspace as a VS Code file system (`armada-vessel:`) with search, run a command,
  changes and a host terminal; vessel import with a folder browser for remote Admirals and fleet categorization; the
  Armada Overview; Activity Charts; the API explorer; sign in with email and password; notifications for new Needs You
  items; model alert actions; backup and restore; change password; server administration (restart, rebuild, roll
  back, stop, reset); local Harbor start, stop, restart and show; Install Armada MCP for Agents; About; multi-select
  delete; **More...** on missions, voyages, vessels and black box entries; labels in VS Code's language from the
  Admiral's catalog (`armada.language`).
- **Ask Armada** chat view at the top of the Armada sidebar: the dashboard's own conversation state, streaming
  replies, tool calls, approve and reject cards for proposed actions and CLI tool requests, stop, new conversation
  and history. Ask Armada About This Failure asks here instead of opening the browser.
- **Backlog, Fleet Actions, Delivery, Configuration, Activity and System** views: 26 kinds of record (releases,
  deployments, environments, incidents, runbooks, checks, personas, pipelines, playbooks, prompt templates, workflow
  and project profiles, skills, model endpoints, events, signals, Harbors, docks, CLI permission rules, tenants, users,
  credentials, battery packs, backlog items, fleet actions and runs) with record pages, create, edit and delete
  through native forms, and each kind's own actions. Vessels, fleets and captains are created and edited from the
  Fleet and Captains views.
- **Native Armada views** replace the Navigation link list: Needs You, Missions, Voyages, Captains, Fleet and Merge
  Queue are VS Code trees with live updates from the Admiral's event stream, status icons, rich tooltips, inline
  actions and context menus (view log and changes, approve and deny reviews, cancel, restart, stop and restart
  captains, process the merge queue, answer CLI permission requests). Missions, voyages, captains and merge entries
  open as pages drawn with the VS Code theme, with their actions and the live end of the log; logs and changes open
  as read-only documents. **Dispatch Mission** and **Dispatch Voyage** are native forms. A status bar item shows active
  missions and what needs you. Dashboard links in black box entries open the native pages.
- The extension is now `LatticeAI.armada-ai` (publisher `LatticeAI`, name `armada-ai`, display name Armada AI), on the
  VS Code Marketplace. It was never published as `jchristn.armada-vscode`, so nothing migrates.
- **Armada view container** in the activity bar with a **Navigation** view (the dashboard's navigation model:
  Dashboard, Ask Armada, OPERATIONS, BUILD, CONFIGURATION, SYSTEM; pages open in the dashboard for now) and the
  **Failures** view, which moved here from the Explorer. `armada.failures.showInExplorer` (off by default) also shows
  it in the Explorer. Both views explain what to do while disconnected.
- **Connection manager**: takes the Admiral and its credential from the Armada settings already on the machine, with
  nothing in VS Code settings: a running Harbor's Admiral (`~/.armada-harbor/settings.json`, its access key as a
  bearer token), the local Admiral (`~/.armada/settings.json`) and the TUI's profiles (`~/.armada/tui.json`, tokens
  from the TUI's credential store). Startup connects with no prompt. **Armada: Connect** is the one connect command: a
  picker of those Admirals (where each came from, whether it responds, whether a credential was found) plus **Enter
  an Admiral URL...**; a key is asked for only when none works and is kept in secret storage for that Admiral. Each
  connection checks health, the contract version ("server too old" or "extension too old"; an Admiral from before
  contract versions counts as contract 1) and whoami, and sets `armada.role.globalAdmin` and `armada.role.tenantAdmin`.
  A status bar item shows the state, target and credential and opens the connection actions; the **Armada** output
  channel logs every attempt. **Armada: Open Settings** opens the Armada, Harbor or TUI settings file, or the
  extension's preferences. There is no `armada.url` or `armada.authHeader` setting.
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
