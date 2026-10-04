# Armada TUI

The Armada TUI is the web dashboard in a terminal. It talks to the same Admiral over the same REST API and WebSocket, enforces nothing the server does not, and is meant for people who live in a shell, work over SSH, or are on a machine without a browser. It ships inside the `armada` CLI, so one install gives you both.

This page covers what exists today: installing and starting it, server profiles, signing in, getting around, Ask Armada, the Approvals center, notifications, and the keys. Ask Armada is the first full screen and the one the TUI opens into. The remaining screens arrive in later milestones, and until then their dashboard routes open a placeholder that names the screen and the workstream that will build it. That is deliberate: every dashboard link, deep link, and palette entry already resolves, so nothing has to be rewired when the screens land. Progress is tracked in `TUI_APP_PLAN.md`.

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

A fresh server will not let the seeded `admin@armada` account do anything with its default password except change it, and the TUI follows the dashboard here. After the password step it shows "Change the default password" with the current password, the new one, and a confirmation. The new password needs at least 8 characters, has to match its confirmation, and cannot be `password` or the current one; the server checks the current password and also retires the default bearer token. Once the change succeeds the TUI confirms the identity with `whoami` and carries on to Ask Armada (or your `--route`). Sign out on that step abandons the session. The same step appears if a stored token or `ARMADA_TOKEN` belongs to an account that still has to change its password.

While the server reports default credentials in use (an `admin@armada` account somewhere still has the default password, or the default bearer token is active), administrators see a warning line under the header on every screen. It goes away when the last default credential is changed.

## Getting around

The screen is a header, a menu bar, the sidebar, the main area, an optional Ask dock, and a status bar. The header shows the tenant, your email, your role (Global Admin or Tenant Admin), server health, whether the WebSocket is live, running background jobs, pending approvals, and the notification bell with its unread count. Every state is written out in text, so nothing depends on color. When you are connected through Armada.Proxy, a second header line shows the proxy instance, and File has Switch Deployment and Proxy Logout.

The sidebar has the same sections, labels, and order as the dashboard, with the Needs You count beside Needs You (marked `!` when something is critical). Below 110 columns it collapses to one-letter icons, and below 90 it hides; `Ctrl+B` brings it back. Sections fold with Left and Right.

There are three ways to reach anything, and they work everywhere:

- the menu bar (`F10`), where the Actions menu always lists the current screen's actions;
- the command palette (`Ctrl+K`), which fuzzy-matches every destination, every hub tab, and every command, and opens an entity directly when you type its id (`msn_...`, `vsl_...`, `vyg_...`, `cpt_...`, and so on);
- the go-to keys: `g` followed by a letter.

The palette runs commands, not just navigation, so "theme light" or "toggle sidebar" work from it. Hubs (Missions, Delivery, Vessels, Captains, Configuration, Activity, Settings, Dispatch, Fleet Actions) show their tabs across the top, and the active tab is part of the route, which is why deep links such as `/vessels/health?overall=Fail` and Back and Forward behave.

Toasts appear at the top right for mission, voyage, captain, deployment, backlog item, and incident changes, with the dashboard's wording and severity (for example `Mission "Fix tables" - Landed` as a success, `Captain "claude-1" - Stalled` as a warning). Each one is also kept in the notification history with a link to the item. `Ctrl+O` runs the newest toast's action (usually Open). `Ctrl+N` opens the notification center: the latest 100 notifications, kept between runs, with `m` to mark all read and `x` (twice) to clear. The terminal bell rings for failures and approvals, and setting `OsNotifications` in `tui.json` (`Osc9`, `Osc777`, or `Native`) raises an OS notification while the terminal is not focused.

Lists use a shared grid: sized columns, server-side sorting with `^` and `v` indicators, multi-select for bulk actions, a column chooser, and a paging bar that reads "Showing 1-25 of 248. Page 1 of 10." Background refreshes never move your cursor.

## Ask Armada

Ask Armada is where the TUI starts and where most of the work happens. It is the dashboard's Ask page laid out for a terminal: the conversation list on the left, the open conversation on the right, and the composer at the bottom. Everything lives in one session object, so you can jump to another screen and come back to the same conversation, the same draft, and a reply that kept streaming while you were away.

The conversation list searches on the server as you type (after a short pause), keeps pinned conversations on top and the rest by their latest message, and marks each row with its unread count, `* Working` while work it started is still running, `Replying...` while the captain is answering, and an `[Archived]` tag when archived ones are shown (`A` toggles them). `Enter` opens a conversation, `n` starts a new one, `/` searches, `e` renames in place, `p` pins, `s` asks for a summary, `Del` deletes after the dashboard's confirmation, and `.` opens the row menu with all of those. The list pages 50 at a time and loads the next page when the cursor reaches "Load more". Below 100 columns the list folds away and `Ctrl+T` brings it back as an overlay. Opening a conversation marks it read, and so does returning focus to the terminal while it is open; nothing is marked read while the terminal is in the background.

The header shows the title (`e` to rename), the captain (`c` picks one, including "No captain (quick actions only)"), auto-approve (`Ctrl+Y`), Summarize (`s`), and More (`.`). Turning auto-approve on asks first, with the dashboard's warning, and a banner stays across the top of the conversation while it is on. When the captain cannot reach Armada over MCP (its runtime is neither Claude Code nor an API endpoint and it reports no Armada tools) a note says so and links the setup instructions for that runtime. A strip under the header lists the work the conversation is tracking with "N active of M"; `w` jumps to the next item's live card.

The transcript renders every message kind the server writes: your messages, captain replies in Markdown with tool chips (`[ok]`, `[..]` while running, `[x!]` on failure), a collapsed thinking section, the turn's duration, and a metrics line the TUI measures itself (time to first token, approximate tokens and tokens per second, total time). Confirm cards show the tool, who proposed it, the one-line summary, the exact arguments, the expiry, and the outcome once decided. Action results carry the live work card for what they started, progress updates link back to that card, and summaries, errors, and system notes have their own styles. While a reply streams, the Markdown is re-rendered on every chunk, so lists, headings, and code blocks read correctly before the turn ends instead of collapsing onto one line.

`Up` and `Down` move between messages and into the rows of a work card. On a confirm card, `a` approves, `r` rejects, `x` shows the arguments (and the result once it ran), and `y` copies them. On a work card row, `Enter` opens the mission, `o` opens its pull request, `l` shows its log, and `d` its diff. `Enter` on a captain reply expands its tool calls and `t` its thinking; `y` copies a message as Markdown and `Y` the whole conversation. `PgUp` and `PgDn` scroll, `Home` goes to the top and loads earlier messages (30 at a time), `End` returns to the live tail, and `Ctrl+F` or `/` searches with `n` and `N`. When you scroll away from the bottom the transcript stops following and counts the new lines below instead of moving under you.

The composer sends with `Enter` and adds a line with `Shift+Enter` or `Ctrl+J`. `Ctrl+E` opens the draft in `$EDITOR`, `Up` on an empty composer recalls what you sent earlier, and `Esc` moves to the transcript and focuses the newest card waiting for a decision. Your message appears at once and is confirmed or removed when the server answers. Typing `/` opens the quick actions: Dispatch and Fleet action open inline forms with the dashboard's fields and checks (submitting the form is the confirmation), Status and Health run immediately, and Import opens the import screen. While a turn runs, `Ctrl+C` or `Esc` twice stops it; the TUI shows "Stopping..." and settles the turn itself if the server has not confirmed within 8 seconds.

Two shortcuts reach Ask from anywhere. `Ctrl+J` toggles the Ask dock at the bottom of any screen, which shows the tail of the open conversation (streaming included), the pending approval count, and a one-line composer that sends to that conversation. `Alt+A` (Ask about this) opens a new conversation with the current screen's subject already typed, for example `On vessel DemoRepo (vsl_...): `.

## Approvals

`Ctrl+A` opens the Approvals center, a single queue of everything waiting on you, most urgent first. The header shows the count on every screen, a new item rings the terminal bell (and raises an OS notification while the terminal is in the background, when `OsNotifications` is set), and a proposal from a conversation you are not looking at also raises a toast whose `Ctrl+O` action opens that conversation.

| Item | Where it comes from | Keys |
|---|---|---|
| Ask proposal | `ask.proposal` events and open conversations | `a` approve, `r` reject, `x` arguments |
| Mission review | the Needs You inbox and `mission.changed` | `a` approve, `c` conditionally approve, `m` more work required, `d` deny |
| Deployment approval | the inbox and `deployment.changed` | `a` approve, `d` deny |
| Failed landing | the inbox and `mission.changed` | `l` retry landing |
| Stalled captain | the inbox and `captain.changed` | `s` stop, `R` recall, `t` restart |

Every decision makes the same call with the same confirmation as the dashboard screen the item belongs to. Review decisions go through the Resolve Review dialog, where feedback is required for Conditionally Approve and More Work Required. Deployments and captain actions ask first. `Enter` opens the item's own screen and `F5` re-reads the inbox. The queue follows the inbox poll and also reacts to entity events immediately, so a review appears the moment its mission enters Review and disappears as soon as it leaves.

## Key map

| Scope | Keys |
|---|---|
| Global | `Ctrl+K` palette; `F10` menu bar; `?` or `F1` help; `Ctrl+A` approvals; `Ctrl+N` notifications; `Ctrl+O` run the latest toast's action; `Ctrl+J` Ask dock; `Alt+A` ask about this; `Ctrl+B` sidebar; `Tab` / `Shift+Tab` / `F6` next pane; `Alt+Left` / `Alt+Right` back and forward (`Backspace` also goes back outside text fields); `F5` refresh; `F12` hand the mouse back to the terminal for text selection; `Ctrl+Q` quit |
| Go to | `g h` Dashboard, `g a` Ask Armada, `g i` Needs You, `g f` Fleet Actions, `g m` Missions, `g d` Delivery, `g v` Vessels, `g c` Captains, `g o` Configuration, `g y` Activity, `g j` Jobs, `g x` API Explorer, `g s` Settings |
| Hubs | `[` / `]` previous and next tab, `Alt+1` to `Alt+9` jump to a tab |
| Lists | arrows, `PgUp`/`PgDn`, `Home`/`End`; `Enter` open; `.` or `Shift+F10` row actions; `Space` select, `Shift+Up/Down` extend, `Ctrl+A` select the page, `Esc` clear; `Left`/`Right` focus a column, `s` sort it, `S` reverse; `c` columns; `<` / `>` pages; `z` page size |
| Forms | `Tab` / `Shift+Tab` fields; `Enter` or `Space` opens a select; `Ctrl+S` save; `Esc` cancel |
| Text fields | `Home`/`End` (`Ctrl+A`/`Ctrl+E`), `Ctrl+U` clear, `Ctrl+W` delete a word, `Ctrl+R` reveal a masked value |
| Ask | `Enter` send; `Shift+Enter` or `Ctrl+J` newline; `Ctrl+E` `$EDITOR`; `/` quick actions; `Esc` to the messages (twice to stop a turn); `Ctrl+C` stop; `Up`/`Down` messages and card rows; `a`/`r` approve or reject; `x` arguments; `t` thinking; `y`/`Y` copy; `o`/`l`/`d` pull request, log, diff; `n` new conversation; `e` rename; `c` captain; `s` summarize; `.` more; `w` next tracked work; `Ctrl+Y` auto-approve; `Ctrl+T` conversation list; `Alt+T` show thinking |
| Conversation list | `Enter` open; `n` new; `/` search; `A` show archived; `e` rename; `p` pin; `s` summarize; `Del` delete; `.` row menu; `R` retry |
| Approvals | `a` approve; `r` reject; `x` arguments; `c` conditionally approve; `m` more work; `d` deny; `l` retry landing; `s` stop; `R` recall; `t` restart; `Enter` open; `F5` refresh |
| Logs and viewers | `f` follow; `/` or `Ctrl+F` search, `n`/`N` next and previous; `y` copy; `]`/`[` next and previous file in a diff |

The help overlay (`?`) lists the bindings for the screen you are on. Any command can be rebound in the `KeyBindings` section of `~/.armada/tui.json`, keyed by command id (for example `"help.palette": "ctrl+p"`).

Copying uses OSC 52, so it works over SSH. Terminals that do not support OSC 52 get a dialog with the text to select by hand.

## Themes and languages

View offers Dark, Light, High contrast, and Auto. Auto reads the terminal background from `COLORFGBG` and picks Dark when it cannot tell. High contrast also switches borders to ASCII. The language picker uses the dashboard's catalog (served at `/dashboard/i18n/armada.json`) and the same nine languages, including Simplified and Traditional Chinese, Cantonese, and Japanese; widths are measured in terminal cells, so CJK text lines up.
