# Armada TUI

The Armada TUI is the web dashboard in a terminal. It talks to the same Admiral over the same REST API and WebSocket, enforces nothing the server does not, and is meant for people who live in a shell, work over SSH, or are on a machine without a browser. It ships inside the `armada` CLI, so one install gives you both.

This page covers what exists today: installing and starting it, server profiles, signing in, getting around, and the keys. The shell, login, navigation, notifications, and the shared widgets are in place. The individual screens arrive in later milestones (Ask Armada first), and until then every dashboard route opens a placeholder that names the screen and the workstream that will build it. That is deliberate: every dashboard link, deep link, and palette entry already resolves, so nothing has to be rewired when the screens land. Progress is tracked in `TUI_APP_PLAN.md`.

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
