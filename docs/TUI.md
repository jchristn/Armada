# Armada TUI

The Armada TUI is the web dashboard in a terminal. It talks to the same Admiral over the same REST API and WebSocket, enforces nothing the server does not, and is meant for people who live in a shell, work over SSH, or are on a machine without a browser. It ships inside the `armada` CLI, so one install gives you both.

This page covers what exists today: installing and starting it, server profiles, signing in, getting around, and the keys. The shell, login, navigation, notifications, and the shared widgets are in place. The Activity and System screens are built (see below); the other screens arrive in later milestones, and until then their routes open a placeholder that names the screen and the workstream that will build it. That is deliberate: every dashboard link, deep link, and palette entry already resolves, so nothing has to be rewired when the screens land. Progress is tracked in `TUI_APP_PLAN.md`.

## Install and start

Install Helm the way you normally do (`dotnet tool install -g Armada.Helm`, or the installers), then run:

```
armada tui
armada tui --server http://127.0.0.1:7890
armada tui --profile work
armada tui --server https://armada.example.com --profile prod --route "/missions?tab=voyages"
```

With no options the TUI connects to the last profile you used, or to the local Admiral from your Armada settings. `--server` connects to a URL and remembers it as a profile named after the host. `--profile NAME` picks a saved profile (and, combined with `--server`, creates or updates it). `--route` chooses the first screen after sign-in; otherwise you land on the screen you left, and on a first run you land in Ask Armada.

For scripted starts, `ARMADA_URL` supplies the server and `ARMADA_TOKEN` a session token or API key, which skips the login screen when the server accepts it.

The terminal needs to be at least 80 columns by 24 rows. Below that the TUI shows a "terminal too small" screen and comes back when you resize. It works in Windows Terminal, iTerm2, kitty, WezTerm, Ghostty, Alacritty, GNOME Terminal, and inside tmux and SSH sessions. Terminal.app and legacy conhost run with fewer mouse and key features.

## Profiles, preferences, and credentials

Preferences live in `~/.armada/tui.json`: server profiles (name, URL, last user, last tenant, last login method), theme, language, sidebar and dock state, the last route, per-table column and page-size choices, auto-refresh intervals, notification settings, and key rebindings. The file never holds a secret. Set `ARMADA_TUI_PREFERENCES` to use another file, which is how the tests keep away from your home directory.

Session tokens and API keys are stored per profile in the operating system's credential store: the macOS Keychain (through `/usr/bin/security`), Windows Credential Manager, or libsecret on Linux (through `secret-tool`). When none of those is available, or a keychain write fails (a locked keychain over SSH, for example), the token goes to `~/.armada/tui-credentials.json`, created with mode 0600. `ARMADA_TUI_CREDENTIAL_STORE` forces a store (`file`, `keychain`, `wincred`, or `secret-tool`) and `ARMADA_TUI_CREDENTIALS` moves the file.

You can switch servers from the login screen's Server picker (which also has "Add server...") or from File, Switch server profile.

## Signing in

The login screen mirrors the dashboard. Email Login asks for your email, looks up your tenants, shows a tenant picker when more than one matches, and then asks for the password. API Key Login takes a session token, a credential's bearer token, or the Admiral API key; the TUI tries it as `X-Token`, then as a bearer token, then as `X-Api-Key`, and validates it with `whoami`. `F2` switches between the two modes, and `Ctrl+R` shows or hides a masked password or key. The language and theme pickers work before you sign in, and the screen shows the default credentials (`admin@armada` / `password`) for a fresh install.

A stored token is reused on the next start. If the server later answers 401, the TUI returns to the login screen with "Your session expired. Sign in again." File, Sign out forgets the stored token.

## Getting around

The screen is a header, a menu bar, the sidebar, the main area, an optional Ask dock, and a status bar. The header shows the tenant, your email, your role (Global Admin or Tenant Admin), server health, whether the WebSocket is live, running background jobs, pending approvals, and the notification bell with its unread count. Every state is written out in text, so nothing depends on color. When you are connected through Armada.Proxy, a second header line shows the proxy instance, and File has Switch Deployment and Proxy Logout.

The sidebar has the same sections, labels, and order as the dashboard, with the Needs You count beside Needs You (marked `!` when something is critical). Below 110 columns it collapses to one-letter icons, and below 90 it hides; `Ctrl+B` brings it back. Sections fold with Left and Right.

There are three ways to reach anything, and they work everywhere:

- the menu bar (`F10`), where the Actions menu always lists the current screen's actions;
- the command palette (`Ctrl+K`), which fuzzy-matches every destination, every hub tab, and every command, and opens an entity directly when you type its id (`msn_...`, `vsl_...`, `vyg_...`, `cpt_...`, and so on);
- the go-to keys: `g` followed by a letter.

The palette runs commands, not just navigation, so "theme light" or "toggle sidebar" work from it. Hubs (Missions, Delivery, Vessels, Captains, Configuration, Activity, Settings, Dispatch, Fleet Actions) show their tabs across the top, and the active tab is part of the route, which is why deep links such as `/vessels/health?overall=Fail` and Back and Forward behave.

Toasts appear at the top right for mission, voyage, captain, deployment, backlog item, and incident changes, with the dashboard's wording and severity. `Ctrl+O` runs the newest toast's action (usually Open). `Ctrl+N` opens the notification center: the latest 100 notifications, kept between runs, with `m` to mark all read and `x` (twice) to clear. The terminal bell rings for failures and approvals, and setting `OsNotifications` in `tui.json` (`Osc9`, `Osc777`, or `Native`) raises an OS notification while the terminal is not focused.

Lists use a shared grid: sized columns, server-side sorting with `^` and `v` indicators, multi-select for bulk actions, a column chooser, and a paging bar that reads "Showing 1-25 of 248. Page 1 of 10." Background refreshes never move your cursor.

## Activity and System screens

The Activity hub (`g y`), Jobs (`g j`), the API Explorer (`g x`), and the Settings hub (`g s`) are built. Every screen uses the dashboard's wording, so the language picker translates them like the rest of the shell. Lists share the grid keys from the key map; screen actions also appear under Actions in the menu bar (`F10`) and in the command palette, so `Ctrl+K` then "Export CSV" or "Backup Now" runs the action on the current screen. Anything that writes a file (exports, backups, saved responses) asks for a path first, suggests a file name in the current directory, expands `~`, and asks before overwriting an existing file. Restores ask for a file and then for confirmation.

### Viewing activity

The All Activity tab is the unified timeline: backlog refinement, planning, dispatch, releases, deployments, incidents, API requests, and events in one list, newest first. The cards at the top count the visible entries, errors, warnings, and distinct source types, and a line under the filters breaks the entries down by source. Filter by free text, backlog item, actor, vessel, source type, or postmortem context, then press `Enter` in a text field or `a` (Apply) to reload; Reset clears every filter. `Enter` on an entry opens the record it points to, and the row menu (`.`) also offers View JSON for the entry's metadata, Open Workspace for its vessel, and Delete for entries that come from the API request log. Deep links such as `/activity?source=history&actor=alice&postmortemOnly=true` open with those filters applied.

Saved views remember a set of filters under a name. Press `w` to save the current filters and `v` to open the list, where you can apply or delete a view. Views are kept in `tui-activity-views.json` next to `tui.json`, so they follow your preferences file rather than a browser. Export JSON, Export CSV, and Export Markdown write up to 5,000 entries that match the current filters, with the same content as the dashboard's downloads, to a file named `armada-history-<timestamp>` by default.

The Token Usage tab charts model tokens over the last hour, day, week, or month (`h`, `d`, `w`, `m`). The cards show total, input, output, and cached tokens, and a note says how many records were estimated rather than reported. "Usage over time" stacks each model's tokens per time bucket, or with `t` splits them into input, output, and cached tokens. `b` switches between stacked bars and lines, and every series has its own glyph as well as its own color. "Usage by model" ranks models by total tokens. Where the dashboard copies a chart as an image, the TUI copies it as a tab-separated table (`y`), and `e` exports the time buckets with a column per model as CSV.

### API request history

The API Requests tab lists the API traffic the server has captured. Four cards show the total requests, success rate, failures, and average duration for the current window, and the Activity chart stacks successful and failed requests per time bucket; move to its range tabs with `Tab` and use `Left` and `Right` to choose Last Hour, Last Day, Last Week, or Last Month. Press `f` to show the filters: method, status code, route, principal, credential, result, from, and to. A global admin also gets a tenant filter, and global and tenant admins get a user filter. Text filters apply on `Enter`, pickers as soon as you choose, and Reset returns to the last 24 hours. Failed requests show a `!` before the status code.

`Enter` on a row opens the request detail drawer: entry ID, principal, auth method, status, duration, capture time, path and query parameters, request and response headers, and both bodies, noting when a stored body was truncated. In the drawer, `r` replays the request in the API Explorer, `Del` deletes the entry, `y` copies one block or everything, and `Esc` closes it. A link to `/requests/<id>` opens the drawer directly. To delete in bulk, mark rows with `Space` and press `Del`, or use Delete Visible Range, which reads Delete Filtered once other filters are set. Every delete asks for confirmation.

### Events, signals, and jobs

The Events tab is the system event log. It pages on the server, and the filter row narrows the current page by event type, entity type, and message as you type; admins also get a User picker. The row menu offers View Detail, View JSON, Delete, and links to the entity, captain, mission, vessel, and voyage. `Enter` opens the event's detail page, which pretty-prints a JSON payload; `Enter` on a linked field opens that record and `y` copies the field.

The Signals tab lists messages between the Admiral and captains. Filter by type, captain, unread only, and (for admins) user; Clear Filters resets them, and a second row searches type, sender, recipient, and payload on the current page. `Enter` opens a signal, `r` marks the selected one read, and `.` opens the row menu. To send a signal press `n`, choose the type, write the payload (`Ctrl+E` opens your editor), choose a captain or leave "Admiral (broadcast)", and press `Ctrl+S`. The detail page links the sender, recipient, and related mission and can mark the signal read or delete it.

Jobs lists background jobs with their kind, status, progress, and the error reason of failed jobs. `x` cancels the selected job while it is still queued or running. The list refreshes on the screen's auto-refresh interval and with `F5`.

### Using the API Explorer

The API Explorer reads the server's live OpenAPI document. Pick a category, type part of a path or summary into the filter, and choose an operation. The builder fills in path, query, and header parameters with their example or default values and, for operations that take a body, an example JSON body built from the schema; `Ctrl+E` edits the body in `$EDITOR`. The Request Preview line shows the exact URL that will be called. `F9` sends the request with your session's credentials and `F8` aborts it. The response pane shows the status, content type, duration, and size, with Preview, Body, Headers, and Code views; the Code view shows the request as curl, JavaScript fetch, or C#, and works before you send anything. `y` copies the current view, Save Response writes the body to a file, and OpenAPI JSON and Swagger open in your browser. Replay in API Explorer, from the request history, opens the matching operation with the captured path values, query values, headers, and body filled in, minus your stored session token.

### Changing settings

The Server tab of Settings shows the Admiral's health, uptime, connection (Live over the WebSocket or HTTP), and remote tunnel, then the server details and every settings section the dashboard has: Server Configuration, Rebuild Armada, Agent Settings, Planning Session Settings, Repository Health, Vessel Import, Fleet Actions, Data Retention, Remote Control, MCP Configuration, and System Paths. Move through the page with `Tab` and `Shift+Tab`; it scrolls to follow you. Each section saves on its own: press `Ctrl+S` anywhere in a section or use its Save button. Values are checked with the dashboard's messages and a section with an invalid value will not save. Vessel Import, Fleet Actions, and Data Retention save only after a change and offer Discard changes. The header says "Unsaved changes" while any section has edits, and a refresh never overwrites them. Lists such as allowed roots take one entry per line, and `Ctrl+E` opens them in your editor. Masked secrets in Remote Control are revealed with `Ctrl+R`, and turning on the tunnel asks for confirmation. Repository Health is editable only by a global admin; others see it read-only. MCP snippets and system paths copy with `Enter` or `y`. The Server page has no login rate limit or Ask settings, so the TUI has none either; they stay in `settings.json`.

Diagnostics runs the server's health checks when you open it and again with `r`. The header shows the verdict (Healthy, Warnings, or Unhealthy), the cards count passed, warning, and failed checks, and `Enter` on a check shows its full message.

### Backing up and restoring

Global admins see Database Backup on the Server tab. Backup Now downloads a backup ZIP and asks where to save it, suggesting the server's file name. Restore from Backup asks for a ZIP on this machine, asks you to confirm, uploads it, and reloads the page; restart the server afterward. Through Armada.Proxy, backup still works but restore is blocked.

### Restarting, stopping, and rebuilding

Server Actions, also for global admins, has Setup Wizard, Health Check, Restart Server, Stop Server, Factory Reset, Rebuild Armada, and Roll Back. Each server-changing action asks for confirmation with the dashboard's wording, and nothing is sent until you confirm. Factory reset deletes all data, including the database, logs, docks, and repositories, but keeps settings. Through Armada.Proxy these local-only actions are disabled and a banner says why.

To rebuild the Admiral from source, choose the vessel that holds Armada's source under Rebuild Armada and save. Rebuild Armada then offers the vessel's branches and a field for any branch, tag, or commit, with the default branch filled in. After you confirm, the build log opens and updates every second and a half until the build succeeds, fails, or the server begins cutting over to the new build. Close the log with `Esc`, reopen it with Build Log, and copy it with `y`. After a successful rebuild, Roll Back returns to the previous build.

### Managing tenants, users, and credentials

The Settings hub has Tenants, Users, and Credentials tabs. Global admins see every tenant and can create one with `n`, edit one with `Enter`, and delete with `Del`; other users see their own tenant read-only. Users and Credentials are for tenant admins and global admins, with search fields and a tenant filter above the grid (Credentials also filters by user).

To add a user, press `n` and fill in the email, the optional first and last name, and the password twice. The tenant is chosen for you unless you are a global admin. Global admins can also grant Global Admin, and tenant admins can grant Tenant Admin. `Ctrl+S` saves. When you edit a user, leave the password fields blank to keep the current password. The form refuses to save without an email, without a password on a new user, or when the two passwords differ.

A credential is an API bearer token for a user. Press `n`, pick the user (global admins also pick the tenant), give it an optional name, and save. The new token is shown once: press `y` to copy it before closing, because the list shows it masked afterward. Edit a credential to rename it or turn it off.

Every list supports `Space` to select rows, `Ctrl+A` to select the page, and `Del` to delete the selection. Deleting tenants, users, or credentials asks you to type `delete` to confirm, as the dashboard does. Through Armada.Proxy, create, edit, and delete are hidden and a banner explains why.

### Running the setup wizard

The setup wizard gets a new Armada ready to dispatch one safe first mission. It opens on its own after you sign in when the server has no fleets, vessels, or captains, or when one of them is still missing and you have not finished or skipped setup before. A start route given with `--route` wins for that sign-in. You can open it at any time from Server Actions on the Settings Server tab or with `--route /setup`.

The wizard has six steps, shown across the top with the current one in brackets and finished ones marked `[x]`. Objective explains what will happen; `Tab` to the buttons and choose Start Setup. Fleet, Vessel, and Captain each let you reuse an existing record (Use Existing) or create a new one (Create New); only idle captains are offered. Fill in the form with `Tab` and `Shift+Tab`, open pickers with `Enter`, and press `Ctrl+S` to save the step and move on. Long text fields open in your editor with `Ctrl+E`. For a Mux captain the wizard lists your saved Mux endpoints.

Dispatch sends a read-only onboarding mission to the vessel you chose; the defaults ask the captain to inspect the repository and report a summary without changing files. Handoff follows the mission until it settles, shows the vessel's readiness and the next recommended step, and offers links into Vessel Onboarding, Backlog, Planning, Workspace, Workflow Profiles, Environments, Checks, and Playbooks. Skip Setup and Finish Setup both mark setup as done in your TUI preferences (`SetupCompleted` in `tui.json`) and take you to Missions. Back returns to an earlier step and keeps what you typed.

## Key map

| Scope | Keys |
|---|---|
| Global | `Ctrl+K` palette; `F10` menu bar; `?` or `F1` help; `Ctrl+A` approvals; `Ctrl+N` notifications; `Ctrl+O` run the latest toast's action; `Ctrl+J` Ask dock; `Ctrl+B` sidebar; `Tab` / `Shift+Tab` / `F6` next pane; `Alt+Left` / `Alt+Right` back and forward (`Backspace` also goes back outside text fields); `F5` refresh; `F12` hand the mouse back to the terminal for text selection; `Ctrl+Q` quit |
| Go to | `g h` Dashboard, `g a` Ask Armada, `g i` Needs You, `g f` Fleet Actions, `g m` Missions, `g d` Delivery, `g v` Vessels, `g c` Captains, `g o` Configuration, `g y` Activity, `g j` Jobs, `g x` API Explorer, `g s` Settings |
| Hubs | `[` / `]` previous and next tab, `Alt+1` to `Alt+9` jump to a tab |
| Lists | arrows, `PgUp`/`PgDn`, `Home`/`End`; `Enter` open; `.` or `Shift+F10` row actions; `Space` select, `Shift+Up/Down` extend, `Ctrl+A` select the page, `Esc` clear; `Left`/`Right` focus a column, `s` sort it, `S` reverse; `c` columns; `<` / `>` pages; `z` page size |
| Forms | `Tab` / `Shift+Tab` fields; `Enter` or `Space` opens a select; `Ctrl+S` save; `Esc` cancel |
| Text fields | `Home`/`End` (`Ctrl+A`/`Ctrl+E`), `Ctrl+U` clear, `Ctrl+W` delete a word, `Ctrl+R` reveal a masked value |
| Logs and viewers | `f` follow; `/` or `Ctrl+F` search, `n`/`N` next and previous; `y` copy; `]`/`[` next and previous file in a diff |

The help overlay (`?`) lists the bindings for the screen you are on. Any command can be rebound in the `KeyBindings` section of `~/.armada/tui.json`, keyed by command id (for example `"help.palette": "ctrl+p"`).

Copying uses OSC 52, so it works over SSH. Terminals that do not support OSC 52 get a dialog with the text to select by hand.

## Themes and languages

View offers Dark, Light, High contrast, and Auto. Auto reads the terminal background from `COLORFGBG` and picks Dark when it cannot tell. High contrast also switches borders to ASCII. The language picker uses the dashboard's catalog (served at `/dashboard/i18n/armada.json`) and the same nine languages, including Simplified and Traditional Chinese, Cantonese, and Japanese; widths are measured in terminal cells, so CJK text lines up.
