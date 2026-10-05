# Armada TUI: Terminal Client Plan

> **Type:** implementation plan (work-tracking). Annotate task status and the progress log as you go; keep this
> document in sync with what actually shipped.
>
> **Status:** In progress (W0, W1, W2 Ask Armada, W3 Operations, W4 Build, W5 Delivery, W6 Configuration, and W7 Activity and System done; Milestones A, B, and C complete; every dashboard route and tab is implemented; W8.1-W8.3, W8.5, W8.6, and W9 done; left: hands-on terminal checks in W8.4 and the simulated user session W8.7)
> **Built on:** TUIKit 1.2.1 (`TUIKit` on NuGet; source at `~/Code/Tuikit`)
> **Parity baseline:** the web dashboard at `src/Armada.Dashboard` as of 2026-10-04 (52 page routes, 37 hub tabs,
> about 45 modals and drawers, 317 server-calling API client functions, 29 WebSocket event types)
> **Last updated:** 2026-10-05

Status values: `[ ]` not started, `[~]` in progress, `[x]` done, `[!]` blocked.

## Goal

A terminal application that can do everything the web dashboard can do, so an operator who lives in a terminal (or
works over SSH, or on a machine with no browser) never needs the browser. Ask Armada is the centerpiece: the TUI opens
into it, and from it the operator can talk to a captain, approve the work it proposes, and watch that work run to
completion in place, while the rest of Armada is one keystroke away through menus, a command palette, and a sidebar.

"Parity" is concrete here: every dashboard screen, tab, table, filter, row action, bulk action, form field, modal,
live update, and setting has a mapped terminal equivalent, and a test enforces that mapping so the TUI cannot silently
fall behind when the dashboard grows (see "Parity enforcement").

Where the terminal is a better fit than a browser, the TUI goes further: an approvals center with single-key decisions,
a persistent notification inbox, terminal bell and OS notifications for things that need you, `$EDITOR` integration,
and command-palette actions (not just navigation).

## Design principles

1. **Ask-first.** Launching `armada tui` lands in Ask Armada with the last conversation open. Everything else is reachable
   without losing the conversation: other screens open in the main region while a compact Ask dock keeps streaming.
2. **Keyboard-complete, mouse-friendly.** Every action has a key path (menu, palette, or binding). Mouse works where
   TUIKit supports it (click to focus, click rows, wheel scroll, click menus), but nothing requires it.
3. **One way to find things.** The same three entry points work everywhere: the menu bar (`F10`/`Alt`), the command
   palette (`Ctrl+K`), and the row action menu (`.` or `Shift+F10` on the selected row). The help overlay (`?`) lists
   the bindings for the current screen.
4. **Live by default.** Screens subscribe to the WebSocket and update in place; auto-refresh polling is the fallback,
   with the same intervals the dashboard offers (off, 15, 30, 60, 120, 180, 300 seconds).
5. **Never lose the user's place.** Background updates never move the selection, scroll position, or focus; new items
   are announced (status bar count, toast) rather than jumped to.
6. **Safe actions.** Destructive actions use the same confirmations as the dashboard (including typed `delete` where the
   dashboard requires it). Approvals always show the exact arguments.
7. **Same server, same rules.** The TUI is a thin client over the existing REST API and WebSocket. It adds no server
   endpoints of its own except where called out under "Server changes", and it enforces nothing the server does not;
   it hides or disables what the server would refuse (from `whoami`: global admin, tenant admin, user).

## What TUIKit gives us, and what it does not

TUIKit 1.2.1 is a capable base: a docked region layout, a modal stack, `CommandRegistry` (palette, menu bar, slash
aliases), `MenuBar`, `DataTable<T>`, `ListView`/`FuzzyList`/`Tree`/`CheckList`, `TextField`/`TextEditor` (undo, find,
kill ring), `Form` with validators, `MessageModal`/`PromptModal`/`SelectModal`/`MultiSelectModal`/`FileSelectModal`,
`NotificationCenter` toasts, `Pane` (scrollback, search, smart scroll lock, in-place line updates),
`StreamingTranscript` (token streaming then Markdown), `MarkdownRenderer`, `SyntaxHighlighter`, `DiffView`, charts
(`Sparkline`, `LineChart`, `BarChart`, `Histogram`, `HeatMap`), `ProgressBar`/`Gauge`/`Spinner`/`ActivityIndicator`,
OSC 52 clipboard, OSC 8 links, `SuspendAsync` for shelling out to `$EDITOR`, themes (Dark, Light, HighContrast),
headless testing (`HeadlessBackend`, `WidgetTester`, `Snapshot`), and telemetry (`TUIKit` meter and activity source).

The survey found gaps that a dashboard-parity client hits immediately. Each one is assigned to a workstream below.

| Gap | Impact on Armada TUI | Plan |
|---|---|---|
| No hierarchical focus; `TabView`, `SplitView`, `ScrollView` do not forward keys (TabView not mouse either) | Tabbed hubs and split detail views cannot receive input in nested widgets | Armada focus router (W1.4) now; upstream key forwarding in TUIKit (U1) |
| Few widget change events (none on `ListView`, `DataTable`, `TextField`, `TextEditor`) | Selection-driven detail panes and form dirty tracking need polling or wrappers | Thin binding layer (W1.5); upstream events (U2) |
| `DataTable<T>`: equal-width columns, ordinal string sort, no sort indicator, no multi-select, no paging, no filter, no per-cell style, no CJK-safe widths | Every Armada list needs sized columns, server sort with indicators, multi-select for bulk actions, colored status badges | `ArmadaGrid` widget (W1.6) built on TUIKit surfaces; upstream DataTable improvements (U3) |
| No dropdown/combo, button, context menu, tooltip, date picker, badge, toolbar | Forms and row actions need them | Armada widgets (W1.7) composed from `SelectModal`/`FuzzyList`/`AutocompleteOverlay`; upstream versions (U4) |
| Theme does not reach widgets; Markdown and toast colors hard-coded | Light/dark/high-contrast would only half apply | Theme applicator that pushes styles into every widget instance (W1.8); upstream theme-aware widgets (U5) |
| Toasts: single line, 40 columns, no history, no actions, no dismiss | Notifications need an inbox and actionable toasts | Armada notification center with history and actions (W1.10); upstream (U6) |
| No palette modal, no help overlay | Both are central to the UX | Armada palette modal and help overlay (W1.9) |
| Streaming Markdown collapses newlines until finalized | Ask replies look flat while streaming | Incremental block renderer for the live turn (W2.3); upstream (U7) |
| No resize event (size polled per frame) | Responsive layout needs a size check | Size watcher in `RenderOverlay` that swaps layouts at breakpoints (W1.3) |
| Full recompose every frame; `Pane` rewraps all scrollback per frame | Long transcripts and logs cost CPU | Scrollback caps, lower `TargetFps` when idle, upstream wrap cache (U8) |
| No `Unbind`/region removal | Dynamic screens | Stable region set with swappable root widgets (W1.2) |
| Most widgets not thread-safe; no SynchronizationContext | WebSocket events arrive on background threads | Single event pump that marshals through `app.Post` (W1.11) |
| No RTL, no screen-reader support | Accessibility parity with the dashboard is not achievable in a terminal | Documented limitation; high-contrast theme, ASCII icons, no color-only states (W8.4) |
| Unicode width math in several widgets uses `string.Length` | CJK locales (zh-Hans, zh-Hant, yue-Hant, ja) misalign | Use `TextWidth` in Armada widgets; upstream fix (U9) |

Because the maintainer owns TUIKit, the plan does both: build what Armada needs now inside `Armada.Tui` behind small
interfaces, and upstream the general pieces to TUIKit (workstream U), then delete the Armada copies once a TUIKit
release ships them.

## Architecture

### Projects

| Project | Purpose |
|---|---|
| `src/Armada.Client` (new) | Typed .NET client for the Armada REST API and WebSocket: one method per server call the dashboard makes (317 functions in `api/client.ts`), typed request/response models (reusing `Armada.Core.Models` where they already exist), auth handling, error mapping (`ApiErrorResponse` with code and request id), paging helpers, and an `ArmadaSocket` with token auth, subscribe, reconnect with backoff and jitter, and typed event dispatch. Usable by the TUI, Helm, and tests. |
| `src/Armada.Tui` (new) | The terminal application: shell, screens, widgets, services. References `TUIKit` and `Armada.Client`. Targets net8.0 and net10.0. |
| `src/Armada.Helm` | Gains `armada tui [--server URL] [--profile NAME]`, which hosts `Armada.Tui` in-process so one install provides both (see Decision D1). |
| `src/Test.Shared` | New `Tui.*` suites (headless rendering, keyboard flows, parity manifest) run by Test.Automated, Test.Xunit, Test.Nunit. |

### Shell layout

```
+------------------------------------------------------------------------------------------+
| Armada  [Default Tenant] admin@armada  * Healthy  * Live   (3 running)   [!2]  [bell 5]  | header
| File  Go  View  Actions  Ask  Help                                                         | menu bar
+---------------+--------------------------------------------------------------------------+
| ASK ARMADA    |                                                                          |
|  > Threads    |                       main region (current screen)                       |
| OPERATIONS    |                                                                          |
|  Needs You (2)|                                                                          |
|  Planning     |                                                                          |
|  Dispatch     |                                                                          |
|  Fleet Actions|                                                                          |
|  Missions     |                                                                          |
| DELIVERY      |                                                                          |
| BUILD         |                                                                          |
| CONFIGURATION |                                                                          |
| ACTIVITY      +--------------------------------------------------------------------------+
| SYSTEM        | Ask dock (collapsible): last thread, streaming reply, pending approvals    |
+---------------+--------------------------------------------------------------------------+
| ? Help  Ctrl+K Palette  . Actions  / Filter  F5 Refresh  Tab Next pane   [approvals: 1]  | status bar
+------------------------------------------------------------------------------------------+
```

- **Header:** product name; tenant and user; role badge (Global Admin / Tenant Admin); server health dot (polls
  `getHealth` every 30 s, links to Diagnostics); WebSocket Live/Offline dot; background activity count (jobs running,
  same polling as the dashboard: 5 s while running, 30 s idle); pending approvals count; notification bell with unread
  count; proxy-mode strip (instance, state, version, Switch Deployment, Proxy Logout) when connected through
  Armada.Proxy.
- **Menu bar:** File (Switch server profile, Sign out, Quit), Go (every sidebar destination), View (theme, sidebar,
  Ask dock, density, language, auto-refresh), Actions (context actions for the current screen), Ask (new conversation,
  approvals, quick actions), Help (keys, about, docs links).
- **Sidebar:** the dashboard nav sections and items with the same labels, collapsible sections, Needs You badge
  (polls `getInbox` every 20 s and on any WebSocket message, at most every 4 s, colored by Critical/Warning, capped at
  99+). Collapses to icons below 110 columns and hides below 90 (toggle with `Ctrl+B`).
- **Main region:** the current screen. Hubs show their tabs as a tab strip (`[` / `]` or `Alt+1..9` to switch).
- **Ask dock:** optional bottom panel (toggle `Ctrl+J`) that shows the active Ask thread's live tail and pending
  approvals while another screen is open.
- **Status bar:** context key hints, filter state, refresh state ("auto 15s" / "paused"), pending approvals count.
- **Responsive breakpoints:** at least 80x24 to run (TUIKit's "terminal too small" screen below that); sidebar collapse
  rules above; detail views stack vertically below 120 columns.

### Core services (W1)

| Service | Responsibility |
|---|---|
| `Router` | Named routes mirroring the dashboard paths (`/missions/:id`, `/vessels/health?overall=Fail`, ...), back/forward stack (`Alt+Left`/`Alt+Right`, `Backspace` on non-text focus), deep links from notifications and palette. |
| `SessionService` | Login flows, token storage, `whoami`, role flags, 401 handling (back to login), server profiles. |
| `EventPump` | Owns `ArmadaSocket`; decodes events; fans out to subscribers; marshals every UI mutation through `app.Post`; coalesces bursts (one refresh per screen per 250 ms). |
| `RefreshService` | Per-screen auto-refresh interval (persisted per screen, default 15 s), pause while a modal or drawer is open, manual `F5`. |
| `NotificationService` | Toasts (info, success, warning, error; 5 s auto-dismiss like the dashboard), persistent store of the latest 100 notifications (unread state, mark read, mark all read, clear), deep links, terminal bell and OS notification hooks (OSC 9 / OSC 777 / `notify-send`/`osascript` behind a setting). |
| `ApprovalService` | One queue of everything waiting on the user: Ask proposals (`ask.proposal` Pending), mission reviews (Review status), deployments pending approval, stalled captains and failed landings from the inbox. Drives the header count, the Approvals center, and `a`/`r` single-key decisions. |
| `ModalService` | Wrappers over TUIKit modals that return results on the loop (`ShowAsync` continuations are off-loop; the service posts back). |
| `CommandService` | Registers every command once (id, title, category, chord, enablement predicate, slash alias) with `CommandRegistry`; builds the menu bar, palette, and help overlay from the same registry. |
| `ClipboardService` | OSC 52 copy (IDs, tokens, JSON, curl snippets, raw diffs) with a fallback "copied text" modal when OSC 52 is not supported. |
| `ExternalService` | Open URLs (`open`/`xdg-open`/`start`), shell out to `$EDITOR` via `SuspendAsync`, save and load files (exports, backups). |
| `LocalizationService` | Fetches `/dashboard/i18n/armada.json`, applies the same `t()` keys and ICU plurals as the dashboard, formats dates and numbers per locale; persists the chosen locale. |
| `ThemeService` | Dark, Light, HighContrast, Auto (from `COLORFGBG` / terminal background query); pushes styles into every widget (W1.8). |
| `PreferencesService` | `~/.armada/tui.json`: server profiles, last route, last Ask thread, theme, locale, sidebar state, per-table columns, page sizes, sort, refresh intervals, Ask "show thinking", Ask draft captain. Credentials go to the OS keychain when available, else a `0600` file. |

### Shared widgets (W1)

| Widget | Built from | Used for |
|---|---|---|
| `ArmadaGrid<T>` | custom `IWidget` using TUIKit surfaces and `TextWidth` | every table: sized and proportional columns, server-side sort with indicators (`^`/`v`), multi-select with checkbox column and range select (`Space`, `Shift+Up/Down`, `Ctrl+A`), per-cell styled content (status badges, progress bars, copy affordance), column chooser with pinned columns, page controls ("Showing 1-25 of 248. Page 1 of 10."), page sizes 10/25/50/100/250, empty/loading/error states, row virtualization |
| `FilterBar` | `TextField`, `SelectField`, `MultiSelectField`, `TriStateField`, `DateField` | the dashboard filter rows; `/` focuses it; filters persist per screen |
| `SelectField` / `MultiSelectField` | `AutocompleteOverlay` + `FuzzyList` + `SelectModal` | dropdowns and pickers (vessel, captain, fleet, pipeline, persona, environment, workflow profile, release, deployment, check type, status enums) |
| `ActionMenu` | `SelectModal`-style popup anchored at the row | row menus (`.` or `Shift+F10`), bulk action menus |
| `Button` / `ButtonRow` | `Label` + focus + key handling | form and modal actions |
| `FormView` | TUIKit `Form` plus sections, inline validation, dirty tracking, Save/Discard | every create/edit form |
| `DetailView` | `DefinitionList` + sections + copyable fields | detail pages |
| `JsonViewer` | `Pane` + `SyntaxHighlighter` (json) | View JSON everywhere (copy, save to file) |
| `DiffViewer` | `DiffView` + file list | mission diffs, workspace diff |
| `LogViewer` | `Pane` with follow toggle, search, copy, "readable" formatting | mission logs, captain logs, rebuild log, check output |
| `MarkdownView` | `MarkdownRenderer` | descriptions, summaries, playbooks, assistant replies |
| `ConfirmDialog` | `MessageModal` | all confirmations, including the typed-`delete` variant |
| `ErrorDialog` | `MessageModal` | errors with code, message, request id, retry |
| `ProgressCell` / `StatusBadge` | styled text | statuses with icon + text (never color alone) |
| `ChartView` | `LineChart`, `BarChart`, `Sparkline`, `BrailleCanvas` | mission history, request activity, token usage, endpoint health history |
| `Drawer` | right-docked region swap | target detail, request detail, quick views |
| `Wizard` | step header + content + Back/Next/Skip | setup wizard, import wizard |

## Ask Armada (W2)

Ask Armada is the most important screen and is built first after the foundation. Everything in the dashboard's Ask
Armada is reproduced, then extended with terminal-native approvals and a dock.

### Layout

```
+---------------------------+----------------------------------------------------------------+
| CONVERSATIONS        [N]ew| TUIKit fixes          Captain: claude-1 (ClaudeCode)  [auto: off] |
| / search                  | Work: Voyage "Fix table renderer" InProgress 1/3  | Fleet run 4/9  |
| [ ] show archived         +----------------------------------------------------------------+
| * TUIKit fixes      2  (o)| You: dispatch a voyage to TUIKit to fix column widths           |
|   Greeting rollout        | claude-1: I've proposed the dispatch below.                     |
|   Billing cleanup  [arch] |  [tool] mcp__armada__dispatch  proposed as aap_...              |
|   ...                     | +- Approval needed -----------------------------------------+   |
|                           | | dispatch  vessel=TUIKit  title="Fix table renderer" ...   |   |
|                           | | [a] Approve   [r] Reject   [x] Arguments   expires 18:41  |   |
|                           | +-----------------------------------------------------------+   |
|                           | +- Voyage "Fix table renderer" ------------- InProgress ---+   |
|                           | | [====-------] 1 of 3 finished, 0 failed                   |   |
|                           | |  Complete    Fix column widths   claude-2  Landed         |   |
|                           | |  InProgress  Fix sort indicator  claude-3  stage: Worker  |   |
|                           | |  Pending     Add tests                                    |   |
|                           | +-----------------------------------------------------------+   |
|                           | Progress update: Mission "Fix column widths" landed.            |
|                           +----------------------------------------------------------------+
|                           | > message the captain, or / for quick actions         [Enter] |
+---------------------------+----------------------------------------------------------------+
```

### Thread list (`AskThreadListView`)

- Server-side search with 300 ms debounce (`enumerateAskThreads` with `Search`), "Show archived" toggle, New
  conversation (`n`), paging 50 per page with "Load more" (or automatic on scroll to the end), retry on error.
- Sort: pinned first, then most recent message.
- Row content: title, pinned marker, unread count badge, working indicator (active tracked work), "Replying..." while a
  turn runs, archived marker.
- Row menu (`.`): Rename (inline edit; `Esc` cancels), Pin/Unpin, Summarize, Archive/Unarchive, Delete (confirm text
  matches the dashboard: messages and action history are removed; work it started keeps running).
- Keys: `Up`/`Down`/`Home`/`End`, `Enter` opens, `n` new, `/` search, `p` pin, `e` rename, `Del` delete.
- Unread: the open thread is marked read (`markAskThreadRead`) when opened, when the terminal regains focus
  (`TerminalFocusChanged`), and on an `ask.thread` with unread > 0 for the open thread; never while the terminal is
  unfocused.
- Narrow terminals: the list becomes a toggled overlay (`Ctrl+T`).

### Conversation header

- Editable title (`e`), captain picker including "No captain (quick actions only)" (`c`; the pre-thread draft captain is
  remembered like the dashboard's `armada_ask_captain`), Auto-approve toggle (`Ctrl+Y`) with a warning confirm and a
  persistent banner while on, Summarize (`s`), More menu (Rename, Pin, Archive, Delete).
- No-MCP note when the captain has no Armada MCP tools and its runtime is neither ApiEndpoint nor ClaudeCode, with the
  per-runtime instructions link.
- Work strip: tracked work with "active of total"; `w` cycles focus through items; `Enter` scrolls to the card or opens
  the item's screen.

### Transcript

- `StreamingTranscript` over a `Pane`, plus keyed live blocks for cards (`Track`/`Update`) so confirm cards and work
  cards update in place without re-rendering the transcript.
- Message kinds rendered exactly as the dashboard defines them: `Text` (Markdown), `ActionProposal` (confirm card),
  `ActionResult` (result plus the live work card), `WorkUpdate` (compact progress line with "show live card"),
  `Summary`, `Error`; roles User, Assistant, System.
- Assistant extras: tool-call chips (running / arguments / result, expandable with `Enter` on the chip), collapsible
  thinking section, turn duration, metrics line (time to first token, tokens/sec, tokens, total).
- Live turn: `ask.chunk` appends tokens; `ask.thinking` appends to the thinking block; `ask.tool` adds or updates
  chips; `ask.turn started` shows the rotating "thinking" phrase (every 4 s, same phrases as the dashboard);
  `ask.turn` completed/failed/cancelled finalizes and refetches the newest page. "The captain turn failed: reason" on
  failure.
- Older messages: "Load earlier messages" at the top (30 per page with `beforeSequence`), also triggered by scrolling
  past the top.
- Transcript navigation: `PgUp`/`PgDn`, `Ctrl+Home`/`Ctrl+End`, `Ctrl+F` search within the conversation, `End` returns
  to live tail (smart scroll lock shows "N new below").
- Copy: `y` copies the focused message as Markdown; `Y` copies the whole conversation.

### Confirm cards and approvals

- Card content: "Approval needed" or "Action: tool", source ("Proposed by the captain" or "Quick action"), one-line
  summary, exact arguments (expand with `x`, copy with `y`), expiry time, status (Pending, Approved "Running now...",
  Rejected, Expired, Executed with run time and result, Failed with error). A decided status is never overwritten by
  a stale Pending copy.
- Decisions: `a` approve, `r` reject on the focused card; from anywhere, `Ctrl+A` opens the Approvals center.
- Every pending proposal also enters the `ApprovalService` queue, so approvals raised in a background thread show in the
  header count, ring the terminal bell (setting), and raise an actionable toast ("Approval needed in TUIKit fixes:
  dispatch. [a] approve [Enter] open").

### Work cards

- One live card per tracked entity (Voyage, Mission, FleetActionRun, Job, VesselImportBatch) from
  `getAskWorkSnapshot` and `ask.work` events: status, progress bar ("done of total", failed count), counts by status,
  per-mission rows (status, title, captain, persona or pipeline stage, check run status, merge queue status, landing
  outcome, branch, PR link, failure reason), per-target rows for fleet action runs, updated time.
- `Enter` on a row opens the mission/voyage/run screen; `o` opens the PR URL; `l` opens the mission log; `d` the diff.
- Snapshot fetches are capped at 20 tracked items per thread (dashboard parity).

### Composer

- Multi-line `TextEditor`: `Enter` sends, `Shift+Enter` or `Ctrl+J` newline (via `SubmitKeyResolver`; IME and paste
  respected), `Ctrl+E` opens `$EDITOR` for long prompts, up-arrow on an empty composer recalls the last message.
- `/` opens the quick-action menu (`AutocompleteOverlay`): `/dispatch`, `/fleet-action`, `/status`, `/health`, `/import`
  from `getAskQuickActions` merged with the built-in defaults. `Up`/`Down`, `Enter`/`Tab` choose, `Esc` dismisses.
- Quick-action forms inline above the composer: Dispatch (vessel, optional pipeline, optional voyage title, missions
  list with title and description, add/remove), Fleet action (action picker, vessel multi-select with filter and
  select/clear visible), Status and Health (no form), Import (opens the Import wizard). Submitting calls
  `runAskQuickAction`, creating the thread first if needed.
- "Show thinking" toggle (`Ctrl+Shift+T` or menu), persisted like `armada_ask_show_thinking`.
- Stop: `Ctrl+C` (TUIKit `CtrlCPolicy.InterruptFocusedPane`) or `Esc` twice while a turn runs: shows "Stopping...",
  calls `cancelAskTurn`, force-ends locally after 8 s.
- Optimistic user message with a local id, confirmed or dropped on response.
- Footer "AI can make mistakes".
- Empty state: random greeting (same list as the dashboard) and quick-action chips.

### Ask dock and global Ask access

- `Ctrl+J` toggles the Ask dock on any screen: shows the active thread's last messages, the live turn, and pending
  approvals; typing focuses its one-line composer.
- `Ctrl+Shift+A` (palette: "Ask about this") opens Ask with context from the current screen pre-filled (for example
  "On vessel TUIKit (vsl_...):"), so the conversation-scope guidance on the server picks up the right focus.

### Events handled

`ask.chunk`, `ask.thinking`, `ask.tool`, `ask.turn`, `ask.message`, `ask.proposal`, `ask.work`, `ask.thread`; after a
socket reconnect the thread list and the open conversation are refetched.

### API calls

`enumerateAskThreads`, `createAskThread`, `getAskThread`, `updateAskThread` (title, captainId, autoApprove, pinned,
archived), `deleteAskThread`, `enumerateAskMessages`, `sendAskMessage`, `cancelAskTurn`, `summarizeAskThread`,
`markAskThreadRead`, `runAskQuickAction`, `approveAskProposal`, `rejectAskProposal`, `getAskWorkSnapshot`,
`getAskQuickActions`, `listCaptains`, `getCaptainTools`.

## Menus, palette, notifications, approvals (W1, W3)

### Command palette (`Ctrl+K`)

Unlike the dashboard palette (navigation only), the TUI palette runs commands. Sources:

1. Every route (same labels and sections as the sidebar), including hub tabs (for example "Missions: Merge Queue").
2. Every command registered by the current screen (row actions on the selection, bulk actions, Refresh, New, Export).
3. Global commands: New conversation, Approvals center, Notifications, Dispatch, Run fleet action, Import repositories,
   Evaluate vessel health, Switch theme, Switch language, Switch server, Sign out, Help.
4. Entity jump: typing an ID prefix (`msn_`, `vyg_`, `vsl_`, `cpt_`, `far_`, `ath_`, ...) jumps to that entity.

Fuzzy matched with `FuzzyList`; shows the key binding next to each command; `Enter` runs, `Tab` fills the query.

### Menu bar

Built from the same `CommandService` registry. The Actions menu is contextual and always equals the current screen's
row and bulk actions plus its screen-level actions, so every action has a menu path in addition to its key.

### Notifications

- Toasts for `mission.changed`, `voyage.changed`, `captain.changed`, `deployment.changed`, `objective.changed`,
  `incident.changed` (same text and severity mapping as the dashboard), plus fleet-categorization completion and
  operation results.
- Notification center (`Ctrl+N` or the bell): latest 100 with severity, title, message, relative time, unread state;
  `Enter` opens the related item and marks read; Mark all read; Clear. Persisted locally.
- Actionable toasts: an Armada toast widget (W1.10) that can carry one action key ("[Enter] open", "[a] approve").
- Attention escalation (settings): terminal bell on approvals and failures; OS notification via OSC 9/777 or the
  platform notifier when the terminal is unfocused.

### Approvals center (`Ctrl+A`)

A single queue view, sorted by urgency, with single-key decisions:

| Item | Source | Keys |
|---|---|---|
| Ask proposal | `ask.proposal` Pending, thread detail `PendingProposals` | `a` approve, `r` reject, `x` arguments, `Enter` open thread |
| Mission review | missions in Review (`getInbox`, `mission.changed`) | `a` approve, `c` conditionally approve (feedback required), `m` more work required (feedback required), `d` deny, `Enter` open mission |
| Deployment approval | deployments PendingApproval | `a` approve, `d` deny (both confirm), `Enter` open |
| Failed landing | missions LandingFailed | `l` retry landing, `Enter` open |
| Stalled captain | captain stalled | `s` stop, `R` recall, `t` restart, `Enter` open |

Every decision uses the same API call and confirmation as the dashboard screen it comes from.

## Screens and full parity map

Conventions used in the tables: "list" screens have the standard toolbar (Refresh `F5`, auto-refresh interval, page
controls, page sizes 10/25/50/100/250), `/` filter, `.` row menu, `Space` select, `Ctrl+A` select all on page,
`Enter` open, `n` new (where creation exists), `Del` delete (confirm), `j` View JSON. Permission gating follows the
dashboard (tenant admin for writes unless noted; global admin where noted; scoped items per `lib/scoping.ts`).
API names are the dashboard `api/client.ts` functions; `Armada.Client` implements each with the same name.

### Top level

| Dashboard | TUI screen | Contents and actions | API |
|---|---|---|---|
| Home `/` | `HomeScreen` | Setup Wizard, Dispatch, New Voyage buttons; Ask band (Ask, Needs You, Dispatch, Diagnostics); alert banners (stalled captains, Failed, LandingFailed, pending with idle captains, pending with no captains); KPI cards (captains idle/working/stalled; active voyages + deferred for memory pressure; missions by status; active fleet action runs running/pending); tenant-admin CTA cards (Import repositories, Run fleet action); vessel health tiles; Mission History chart (range hour/day/week/month, fleet and vessel filters, series total/complete/failed/other); Voyage Progress grid (row: View JSON; Enter opens); Recent Missions grid (status/vessel/captain filters; row: View Detail, View JSON, Restart, Delete; View All); Recent Signals. Reloads on any socket message, polls every 30 s, plus auto-refresh. | getStatus, listMissionSummaries, listVessels, listCaptains, listFleets, listSignals, deleteMission, restartMission, enumerateFleetActionRuns, getVesselHealthSummary, getMissionHistory |
| Ask Armada `/ask/:threadId?` | `AskScreen` | See "Ask Armada". | see above |

### OPERATIONS

| Dashboard | TUI screen | Contents and actions | API |
|---|---|---|---|
| Needs You `/inbox` | `InboxScreen` | KPIs (Total, Critical, Warning, Unread alerts); item list (kind, severity, title, detail; Enter opens target); Recent alerts from the notification store with Mark all read; also feeds the Approvals center. Sidebar badge polling as described in the shell. | getInbox |
| Planning `/planning`, `/planning/:id` | `PlanningScreen` | Start form (title, captain, fleet filter, vessel, pipeline or inherit; supported runtimes note; long-start warning; pre-fill from Backlog, Incident, Setup Wizard, Workspace); Vessel Readiness panel; Recent Sessions grid (title, captain, vessel, pipeline, status, updated; End Session, Delete; Delete All); Transcript (stream toggle, show thinking; header with captain, runtime, vessel, branch, pipeline, playbooks, updated, message count; tool calls, thinking, metrics; Send, Stop, Clear, End Session; select an assistant reply to use for dispatch or open in Dispatch); Dispatch card (voyage title, mission description; Summarize Draft, Open In Dispatch, Dispatch). Live `planning-session.*` events and `captain.changed`; rotating thinking text. | listPlanningSessions, getPlanningSession, createPlanningSession, sendPlanningSessionMessage, stopPlanningTurn, stopPlanningSession, summarizePlanningSession, dispatchPlanningSession, deletePlanningSession, listCaptains, listFleets, listVessels, listPipelines, getVesselReadiness |
| Dispatch tab `/dispatch` | `DispatchScreen` | Form: vessel (required), pipeline (or inherit; link to Pipelines), priority (default 100), voyage title, description (multiple tasks become multiple missions; `Ctrl+E` editor), playbooks with delivery mode (InlineFullContent, InstructionWithReference, AttachIntoWorktree), captain assignments per pipeline step (preferred captain, fallback tier Auto/Economy/Standard/Premium), readiness panel; pre-fill from Backlog, Planning, Incident, Workspace. Action: Dispatch. | listVessels, listPipelines, listCaptains, listPersonas, createVoyage, getVesselReadiness |
| Backlog tab `/dispatch?tab=backlog` | `BacklogScreen` | Group pills (All, Inbox, Ready For Planning, Ready For Dispatch, Blocked); filters (search, kind, priority P0-P3, backlog state, effort XS-XL, status, fleet, vessel, owner, target version, user scope); sort (rank, priority, updated, due); grid (rank with move up/down `Alt+Up/Down`, item, shape, state, scope, due/updated); row menu (Open, Duplicate, View JSON, Move Up, Move Down, Delete); Import GitHub modal (vessel, Issue or Pull Request, number); New backlog item. | listBacklog, createBacklogItem, deleteBacklogItem, reorderBacklog, importObjectiveFromGitHub, listFleets, listVessels |
| Backlog item `/backlog/:id` | `BacklogItemScreen` | Header actions (View JSON, History, Refresh GitHub, Start Planning, Open In Dispatch, Draft Release, Duplicate, Delete); full form (title, vessel, status, description, owner, tags key/value, refinement summary, acceptance criteria, non-goals, rollout constraints, evidence links, kind, category, priority, backlog state, effort, rank, target version, due, parent, suggested pipeline, blocked by, suggested playbooks); linked lists (fleets/vessels, planning sessions, voyages, missions, checks, releases, deployments, incidents); GitHub source panel; Refinement panel (session list; start form with captain, vessel context, fleet context, title, initial prompt; transcript with Stop, Delete, select message, Send; Summarize to a draft with Summary and Method, Apply To Backlog Item); Save, Back. Live `objective.changed`, `objective-refinement-session.*`, `captain.changed`. | getBacklogItem, createBacklogItem, updateBacklogItem, deleteBacklogItem, importObjectiveFromGitHub, listBacklog, listBacklogRefinementSessions, createBacklogRefinementSession, getObjectiveRefinementSession, sendObjectiveRefinementMessage, summarizeObjectiveRefinementSession, applyObjectiveRefinementSummary, stopObjectiveRefinementSession, deleteObjectiveRefinementSession, listCaptains, listFleets, listVessels, listPipelines |
| Fleet Actions: Actions tab | `FleetActionsScreen` | Grid (name, kind, source built-in/custom, timeout, concurrency, created sortable, updated); row menu (Run, Edit, Duplicate, View JSON, Delete; built-in delete explains hide); form (name, description, kind Command/Mission, command text or prompt template + pipeline + persona, timeout 5-7200, concurrency 1-32, requires clean tree, template variable help with insert); Run flow: vessel picker (name/path filter, fleet filter, select all, max 500) then run modal (saved or ad hoc, ad hoc fields, skip dirty vessels, per-vessel preview, review and run) then navigate to the run. | enumerateFleetActions, createFleetAction, updateFleetAction, deleteFleetAction, runFleetAction, runAdHocFleetAction, getSettings, listPersonas, listPipelines, listVessels, listFleets, getVessel |
| Fleet Actions: Runs tab | `FleetActionRunsScreen` | Status filter, grid (action, kind, status, progress, created sortable, started, completed, duration); row menu (View, Cancel with confirm while active, View JSON); auto-refresh paused while a modal is open. | enumerateFleetActionRuns, cancelFleetActionRun |
| Run detail `/fleet-actions/runs/:id` | `FleetActionRunScreen` | Live until finished (paused while the target drawer is open); progress bar (succeeded/failed/skipped/cancelled); fields (concurrency, duration, created/started/completed, timeout, clean-tree check, command or prompt snapshot); actions (Refresh, View JSON, Re-run failed targets, Cancel run); targets grid (status filter; vessel, status, reason, exit code, duration, voyage, output with Truncated badge); target drawer (status, reason, exit code, timing, voyage, rendered command copy, stdout/stderr in LogViewer, open full output). | getFleetActionRun, enumerateFleetActionRunTargets, getFleetActionRunTarget, getFleetAction, cancelFleetActionRun |
| Missions tab `/missions` | `MissionsScreen` | Filters (search, status, user scope); server paging; grid (select, title, ID, status, priority sortable, vessel, captain, voyage, branch); bulk Delete Selected (purge); New Mission modal (title, description, vessel, mode Implementation/Audit/Research, priority); row menu (View Detail, Edit, Restart, Retry Landing when WorkProduced/LandingFailed, View Diff, View Log, Transition Status, View JSON, Cancel, Purge). | listMissionSummaries, createMission, updateMission, deleteMission, purgeMission, restartMission, retryMissionLanding, transitionMission, getMissionDiff, getMissionLog, listVessels, listCaptains, listVoyages |
| Mission detail `/missions/:id` | `MissionScreen` | Header (Retry Landing/Land, Resolve Review, Diff, Log, Instructions, Run Check); menu (Edit, Resolve Review, Mark Complete, View Diff, View Log, View Instructions, Run Check, Transition Status, View JSON, Restart, Purge, Delete); Review modal (feedback; Approve, Conditionally Approve, More Work Required, Deny; feedback required where the dashboard requires it); panels (Landing Preview, GitHub Pull Request with refresh and open, all fields listed in the dashboard, description Markdown with copy raw, linked checks, linked deployments, playbooks). | getMission, updateMission, deleteMission, purgeMission, getMissionDiff, getMissionGitHubPullRequest, getMissionLog, getMissionInstructions, getMissionLandingPreview, restartMission, retryMissionLanding, transitionMission, approveMissionReview, denyMissionReview, listCheckRuns, listVessels, listCaptains, listDeployments |
| Voyages tab | `VoyagesScreen` | Grid (select, title, ID, status sortable, auto push, auto create PRs, landing mode); search, user scope; bulk Cancel Selected; row menu (View Detail, View Status modal, View JSON, Cancel, Purge). | listVoyages, cancelVoyage, purgeVoyage, getVoyageStatus |
| Voyage detail `/voyages/:id` | `VoyageScreen` | Actions (Run Check, Draft Release, Retry Failed, View JSON, Cancel, Delete); fields (status, ID, description, auto-push, auto-create PRs, auto-merge PRs, landing mode, captain assignments with fallback tier, created/completed, progress, playbook snapshots); missions grid (mission, status, vessel, captain, branch; Diff, Log, Detail). | getVoyage, cancelVoyage, purgeVoyage, listMissions, getMissionDiff, getMissionLog, createMission, listVessels, listCaptains |
| Create Voyage `/voyages/create` | `VoyageCreateScreen` | Title, description, vessel, pipeline, auto-push, auto-create PRs, auto-merge PRs, playbooks; missions list (add/remove; title, priority, description). | listVessels, listPipelines, createVoyage |
| Merge Queue tab | `MergeQueueScreen` | Toolbar (Process All with confirm, Enqueue, Delete Selected); Enqueue form (branch, target, mission ID, vessel, test command, priority); filters (text, vessel, user scope); grid (ID, branch with copy, target, status, priority; sortable); row menu (View Detail, Process, Cancel, Mission Diff, Mission Log, View JSON, Delete). | listMergeQueue, enqueueMerge, deleteMergeEntry, processMergeEntry, processAllMergeQueue, cancelMergeEntry, listVessels, getMissionDiff, getMissionLog |
| Merge entry `/merge-queue/:id` | `MergeEntryScreen` | Row-menu actions plus Landing Preview; fields (ID, status, branch, target, priority, vessel, mission, batch ID, test exit code, tenant, test command, timestamps, test output in LogViewer). | as above, getVesselLandingPreview |

### DELIVERY (`/delivery` hub; all writes tenant admin; read-only hint for others)

| Dashboard | TUI screen | Contents and actions | API |
|---|---|---|---|
| Deployments tab | `DeploymentsScreen` | KPIs (total, pending approval, running, succeeded, failed/verification failed); filters (search, status, verification, vessel); grid (deployment, status, verification, checks, updated); row menu (Open, Edit, View JSON, Delete); create/edit modal (vessel, workflow profile, environment or name, release, source ref, mission ID, voyage ID, title, summary, notes, execute immediately when approval not required). | listDeployments, createDeployment, updateDeployment, deleteDeployment, listEnvironments, listReleases, listVessels, listWorkflowProfiles |
| Deployment detail | `DeploymentScreen` | Actions (Open Workspace, Run Check, Sync GitHub Actions, Runbook, Create Incident, Open Release, View JSON, Approve, Deny, Verify, Rollback with confirms, Delete); panels (overview incl. approved by, monitoring window, last monitored, regression alerts, last alert; verification monitoring summary; linked checks; runbook executions; request summary with total, success rate, average duration, buckets as a bar chart; identifiers). | getDeployment, createDeployment, updateDeployment, deleteDeployment, approveDeployment, denyDeployment, verifyDeployment, rollbackDeployment, syncGitHubActions, listRunbookExecutions, listEnvironments, listReleases, listVessels, listWorkflowProfiles |
| Environments tab | `EnvironmentsScreen` | KPIs (total, default targets, require approval); filters (search, kind, vessel, active); grid (environment, health, policy, updated); row menu (Open, Edit, Duplicate, View JSON, Delete); form (vessel, name, kind, configuration source, base URL, health endpoint, description, access notes, deployment rules, requires approval, default for vessel, active). | listEnvironments, createEnvironment, updateEnvironment, deleteEnvironment, listVessels |
| Environment detail | `EnvironmentScreen` | Actions (Deploy, Run Check, Create Incident, Runbook, Open Workspace, View JSON, Duplicate, Delete); extra fields (rollout monitoring window, monitoring interval, record regression alerts); verification definitions editor (method, path, expected status, must contain text, headers, request body, active; add/remove). | getEnvironment, createEnvironment, updateEnvironment, deleteEnvironment, listVessels |
| Releases tab | `ReleasesScreen` | KPIs (total, shipped, candidates, failed/rolled back); filters (search, status, vessel); grid (release, status, workflow, linked work, published, updated); row menu (Open, Edit, View JSON, Delete); form (title, status, vessel, workflow profile, version, tag, summary, notes, voyage IDs, mission IDs, check run IDs). | listReleases, createRelease, updateRelease, deleteRelease, listVessels, listWorkflowProfiles |
| Release detail | `ReleaseScreen` | Pre-fill from backlog items; actions (Deploy, Run Check, View JSON, Refresh Derived Fields, Delete); linked voyages, missions, checks, backlog items (View History); deployment evidence; artifacts (source, path, size, last write); GitHub pull requests (checks, reviews, reviewers, updated). | getRelease, createRelease, updateRelease, deleteRelease, refreshRelease, getReleaseGitHubPullRequests, listCheckRuns, listDeployments, listObjectives, listVessels, listVoyages, listWorkflowProfiles |
| Incidents tab | `IncidentsScreen` | KPIs (total, open, monitoring, mitigated, closed/rolled back); filters (search, status, severity); grid (incident, status, severity, updated); row menu (Open, View JSON, Delete); create modal (title, status, severity, vessel, environment, deployment, release, summary, impact). | listIncidents, createIncident, deleteIncident, listDeployments, listEnvironments, listReleases, listVessels |
| Incident detail | `IncidentScreen` | Actions (Plan Hotfix, Dispatch Hotfix, Runbook, Open Deployment, Open Environment, Rollback Deployment with confirm, View JSON, Delete); extra fields (environment name, mission/voyage IDs, detected/mitigated/closed, root cause, recovery notes, postmortem, failure kind, rescue attempts and missions); runbook executions. | getIncident, createIncident, updateIncident, deleteIncident, rollbackDeployment, listDeployments, listEnvironments, listReleases, listRunbookExecutions, listVessels |
| Checks tab | `ChecksScreen` | KPIs (total, passed, failed, running); filters (vessel, status, source Armada/External, check type); grid (check, status, source, duration, created); row menu (Open, Draft Release, View JSON); Run Check modal (vessel, workflow profile, check type with all 19 types, environment, label, mission/voyage/deployment IDs, branch, commit, command override, resolved-profile preview, preflight readiness). | listCheckRuns, runCheck, listVessels, listWorkflowProfiles, previewWorkflowProfileForVessel, getVesselReadiness |
| Check run detail | `CheckRunScreen` | Actions (Retry, Draft Release, View JSON, Delete); all fields; comparison with previous run (regression/improvement, deltas for duration, artifacts, passed/failed/skipped/total, coverage line/branch/function/statement); test results; coverage; artifacts; output (LogViewer). | getCheckRun, retryCheckRun, deleteCheckRun, listCheckRuns, getVessel, getWorkflowProfile |
| Runbooks tab | `RunbooksScreen` | KPIs (total, executions, running); grid (runbook, binding, steps, visibility, updated); row menu (Open, Duplicate, View JSON, Delete); create modal (file name, title, description, workflow profile, environment, default check type, active, scope). | listRunbooks, createRunbook, deleteRunbook, listWorkflowProfiles, listEnvironments |
| Runbook detail (`?executionId=`) | `RunbookScreen` | Edit (overview Markdown via editor, parameters name/label/default/required, steps title/instructions); Start Execution (title, workflow profile, environment, check type, notes, parameter values); Execution Progress (status, deployment, incident, step checkboxes, step notes; Save Progress, Mark Completed, Cancel Execution); Run Check, View JSON, Duplicate, Delete. | getRunbook, createRunbook, updateRunbook, deleteRunbook, startRunbookExecution, updateRunbookExecution, listRunbookExecutions, listEnvironments, listWorkflowProfiles |

### BUILD

| Dashboard | TUI screen | Contents and actions | API |
|---|---|---|---|
| Vessels tab | `VesselsScreen` | Header (Import repositories, New Vessel); filters (search, fleet, landing mode, user scope); grid (select, name, ID, fleet, repository URL with default branch, landing mode, sync ahead/behind, branches count; sortable); bulk (Run action, Delete Selected, Clear); row menu (Manage Branches, Manage Objectives, Manage Fleet, Open Workspace, View Detail, Edit, Duplicate, View JSON, Delete); full create/edit form (every field listed in the inventory: name, fleet, repo URL, default branch, local path, working directory, landing mode, branch cleanup, default pipeline, concurrent missions, model context, secret scan, protected paths, identifier denylist, auto-land max files/lines/allowed/denied, DoD build/test commands and timeout, project context, style guide, model context); Branches modal (branch, ahead/behind, last commit, Push, copy; merge source to target with push after merge); Build Context modal (captain, guidance; Build or Refine). | listVessels, listFleets, listPipelines, createVessel, updateVessel, deleteVessel, getVesselGitStatus, getVesselBranches, pushVesselBranch, mergeVesselBranch, buildVesselContext |
| Import wizard `/vessels/import` | `ImportWizard` | Source (paste paths or browse the Admiral host tree with git/worktree markers, allow worktrees, max depth 1-16); Discovering (background, progress, closable); Review (status filter, text filter, select all new/clear, grid name/status/path/remote/branch, defaults fleet/pipeline/landing mode, categorization: captain (idle only), editable instructions with reset, apply automatically); Results (progress, outcome filter, grid name/outcome/reason/path/vessel); fleet recommendations (rename, move repos, add/remove fleets, apply with confirm, stop captain, retry, edit and re-apply); History (status filter; created, paths, candidates, skipped, failed; Continue/View); "Leave the import review?" guard. | browseVesselImport, discoverVesselImport, importVessels, getVesselImportBatch, enumerateVesselImportBatches, getFleetCategorizationDefaultPrompt, categorizeVesselImport, applyFleetRecommendations, cancelJob, listCaptains, listFleets, listPipelines |
| Health tab `/vessels/health` | `VesselHealthScreen` | Summary chips (Fail, Warn, Pass, Unknown, NotApplicable, Not evaluated; select to filter), evaluation progress and last run, Evaluate all; filters (name, fleet, overall/dependency/test status multi, dirty, CI, divergence, branch count range, last commit range, Clear); column chooser with Show all (Vessel and Overall pinned); server sort on all 17 fields; bulk (Re-evaluate selected, Run action); row menu (Details, Override, Re-evaluate, Branches, View JSON); detail view with tabs Summary, Findings (10 criteria with status and detail sentence), Dependencies (ecosystem, project, package, version, drift, vulnerability, advisory), Overrides (add/edit/remove per criterion or Overall with note), Raw JSON; Re-evaluate, Open vessel. Filter state round-trips through the route query like the dashboard. | enumerateVesselHealth, getVesselHealthSummary, getVesselHealth, evaluateVesselHealth, setVesselHealthOverride, deleteVesselHealthOverride, getJob, listFleets, listVessels |
| Vessel detail `/vessels/:id` | `VesselScreen` | Menu (Manage Objectives, Manage Fleet, Onboarding, Run Check, Open Workspace, Health, Edit, Duplicate, View JSON, Delete); extra fields (release/hotfix prefixes, protected branch patterns, require passing checks, require PR for protected, require merge queue for release); panels (Readiness, Landing Preview, Dock Boundary / Secret Scan, Auto-Land Gate, Definition-of-Done Gate, recent missions). | listVessels, listFleets, listMissionSummaries, listPipelines, createVessel, updateVessel, deleteVessel, getVesselReadiness, getVesselLandingPreview |
| Vessel onboarding | `VesselOnboardingScreen` | Readiness checklist (completed, blocking issues, warnings, environments, next step, open issues); Open Workspace, Run Check, Back. | getVessel, getVesselReadiness |
| Fleets tab, `/fleets/:id` | `FleetsScreen`, `FleetScreen` | Grid (select, name, ID, description, vessels, active, created; sortable); bulk delete; row menu (View Detail, Edit, Duplicate, View JSON, Delete); form (name, description, default pipeline); detail with vessels grid (vessel, repo URL copy, branch). | listFleets, listVessels, listPipelines, createFleet, updateFleet, deleteFleet |
| Workspace `/workspace/:vesselId` | `WorkspaceScreen` | Status bar (branch, clean/dirty, active missions, ahead/behind); actions (Refresh, Save `Ctrl+S`, Run Check, Plan, Dispatch with selection pre-fill, Context, Switch vessel); file tree (`Tree`; New File, New Folder, Rename, Delete, Metadata); editor (`TextEditor` with `SyntaxHighlighter`, binary/too-large/read-only handling, tabs for open files, or `$EDITOR` via `SuspendAsync`); Vessel Context modal (project context, style guide, model context with Append Selection); terminal panel (run a command; exit code; timeout; Clear); Review Diff (DiffViewer). | getWorkspaceStatus, getWorkspaceTree, getWorkspaceFile, saveWorkspaceFile, createWorkspaceDirectory, renameWorkspaceEntry, deleteWorkspaceEntry, execWorkspaceCommand, getWorkspaceDiff, getVesselReadiness, listVessels, updateVessel |
| Captains tab | `CaptainsScreen` | Toolbar (Delete Selected, Stop All with confirm, New Captain); grid (select, name, ID, runtime, state with quarantine badge, current mission, heartbeat, created); row menu (View Detail, Start Planning, Edit, Duplicate, View Tools, View JSON, View Notifications, Stop, Recall, Restart, Delete); form (name, runtime, model, inference endpoint for ApiEndpoint, reasoning effort, capability tier, system instructions, Mux fields: config directory, endpoint with discovery and refresh, base URL, adapter type, temperature, max tokens, system prompt path, approval policy); Tools modal (MCP servers, runtime sources, internal tools, MCP tools, reachability). | listCaptains, createCaptain, updateCaptain, deleteCaptain, stopCaptain, recallCaptain, stopAllCaptains, restartCaptain, getCaptainTools, listModelEndpoints, listMuxEndpoints |
| Captain detail | `CaptainScreen` | Menu (Edit, Duplicate, View Tools, View Log, View JSON, Recall, Stop, Remove); Lift Quarantine; extra form fields (allowed personas JSON, preferred persona); fields (state, quarantine, current mission/dock, PID, recovery attempts, heartbeat, current mission); captain log (LogViewer with Readable toggle, follow); recent missions. | getCaptain, getCaptainTools, getCaptainLog, getMission, listMissionSummaries, unquarantineCaptain |
| Docks tab, `/docks/:id` | `DocksScreen`, `DockScreen` | Grid (select, ID, vessel, captain, branch copy, worktree path, active, created); row menu (View Detail, View JSON, Delete with cleanup); detail extras (starting point, start commit, target/working branch, recent commits on relevant paths, subject terms already in tree). | listDocks, getDock, deleteDock, listCaptains, listVessels |

### CONFIGURATION (`/configuration` hub; scoped editing per `lib/scoping.ts`)

| Dashboard | TUI screen | Contents and actions | API |
|---|---|---|---|
| Workflow Profiles | `WorkflowProfilesScreen`, `WorkflowProfileScreen` | KPIs; filters (search, scope Global/Fleet/Vessel, status); grid (profile, visibility, capabilities, targets, status, updated); row menu (Open, Edit, Duplicate, View JSON, Delete); quick-create; detail: required inputs (provider list, environment scope, key/path, note), all command fields (lint through changelog), per-environment commands (deploy, rollback, smoke test, health check), Validate (errors and warnings), resolved-command preview. | listWorkflowProfiles, getWorkflowProfile, createWorkflowProfile, updateWorkflowProfile, deleteWorkflowProfile, validateWorkflowProfile, listFleets, listVessels |
| Project Profiles | `ProjectProfilesScreen`, `ProjectProfileScreen` | KPIs; grid (profile, visibility, overrides, status, updated); form (name, description, scope, default pipeline, workflow profile, skills, default, active); detail: persona overrides (persona, prompt template, enabled, additional instructions), skills, Persona Prompt Diff preview (DiffViewer of base vs effective). | listProjectProfiles, getProjectProfile, createProjectProfile, updateProjectProfile, deleteProjectProfile, previewPersonaPrompt |
| Skills | `SkillsScreen`, `SkillScreen` | KPIs; category filter; grid (skill, visibility, status, updated); form (name, category, description, content via editor, active, scope). | listSkills, getSkill, createSkill, updateSkill, deleteSkill |
| Personas | `PersonasScreen`, `PersonaScreen` | Grid (name, ID, description, prompt template, visibility, built-in, active, created; sortable); row menu (View Detail, Edit, Duplicate, Edit Backing Prompt, View JSON, Delete); form (name, description, prompt template, scope); detail: default captain, backing prompt editor (description, content; Save Prompt, Reset to Default, Open Full Template); built-ins not deletable. | getPersona, listPersonas, createPersona, updatePersona, deletePersona, getPromptTemplate, updatePromptTemplate, resetPromptTemplate, listCaptains, listPromptTemplates |
| Pipelines | `PipelinesScreen`, `PipelineScreen` | Grid (name, ID, description, stages, visibility, built-in, active, created); form (name, description, stages list: persona, optional, review gate, on deny retry/fail, reorder `Alt+Up/Down`, remove); detail flow table (order, persona, required, review, on deny, description); Run Pipeline modal (vessel, title, objective; Launch Voyage). | listPipelines, getPipeline, createPipeline, updatePipeline, deletePipeline, createVoyage |
| Prompts | `PromptTemplatesScreen`, `PromptTemplateScreen` | Category pills; grid (name, description, category, built-in, content length, active, updated); row menu (Edit/Open, Duplicate, View JSON, Reset to Default for built-ins); create; detail (name, category, description, content with character count and unsaved marker; parameter palette inserting at the cursor grouped Mission/Vessel/Captain/Pipeline Context/System; Duplicate, Reset). | listPromptTemplates, createPromptTemplate, getPromptTemplate, updatePromptTemplate, resetPromptTemplate |
| Playbooks | `PlaybooksScreen`, `PlaybookScreen` | KPIs; grid (file, visibility, status, content, updated); form (file name, description, Markdown content via editor, active, scope); detail statistics (characters, lines, headings) and Markdown preview. | listPlaybooks, getPlaybook, createPlaybook, updatePlaybook, deletePlaybook |
| Endpoints | `EndpointsScreen` | Run Health Sweep; KPIs; filters (search, kind); grid (endpoint, visibility, health, last checked); row menu (Health, Edit, View JSON, Delete); form (name, kind, provider list, region, project, access key ID, base URL, deployment, model, API version, secret fields with "blank keeps stored", dimensionality, timeout, enabled, scope); Validate Now (result, latency, status, dimensions, sample, error); health detail (status, uptime, history span, consecutive OK/fail, last error, history histogram). | listModelEndpoints, createModelEndpoint, updateModelEndpoint, deleteModelEndpoint, validateModelEndpoint, healthCheckModelEndpoints |
| Harbors | `HarborsScreen` | KPIs (total, connected, disconnected); filters (search, status); grid (harbor, status, enabled, capacity, platform, protocol, last seen); row menu (Details with capabilities, Edit, View JSON, Delete); Enable/Disable toggle; register/edit form (name, max concurrent jobs, enabled for routing). | listHarbors, createHarbor, updateHarbor, deleteHarbor, enableHarbor, disableHarbor |
| Memory | `MemoryScreen` | Filters (type Episodic/Semantic/Procedural, search); grid (type, topic, summary, salience, vessel, visibility, updated); Delete, View JSON; paging. | listMemories, deleteMemory |

### ACTIVITY

| Dashboard | TUI screen | Contents and actions | API |
|---|---|---|---|
| All Activity | `ActivityScreen` | KPIs (visible, errors, warnings, source types); filters (text, backlog item, actor, vessel, source type, postmortem only; Apply); saved views (save with name, delete; stored in TUI preferences); export JSON, CSV, Markdown (to a file path); grid (when, title, source, status, actor, vessel); row menu (View, View JSON, Open Workspace, Delete for request entries). | enumerateHistoryTimeline, listObjectives, listVessels, deleteRequestHistoryEntry |
| API Requests | `RequestHistoryScreen` | KPIs (total, success rate, failures, average duration); activity chart (hour/day/week/month); filters (method, status code, route, principal, credential, result, tenant, user, from, to; Reset); grid (select, when, method, route, principal, status, duration, payloads); row menu (View, Replay in API Explorer, Delete); bulk (Delete Selected; Delete Visible Range, or Delete Filtered when filters are set; not role-gated, as in the dashboard); request detail drawer (entry ID, auth method, captured, params, headers, bodies with truncation flag; Replay, Delete); deep link `/requests/:id`. | listRequestHistory, getRequestHistorySummary, getRequestHistoryEntry, deleteRequestHistoryEntry, deleteRequestHistoryEntries, deleteRequestHistoryByFilter |
| Events, `/events/:id` | `EventsScreen`, `EventScreen` | Grid (select, ID, event type, entity type, entity ID, captain, mission, vessel, voyage, message, created; sortable); row menu (View Detail, View JSON, Delete); bulk delete; detail adds payload and tenant. | listEvents, getEvent, deleteEventsBatch, listCaptains, listVessels |
| Signals, `/signals/:id` | `SignalsScreen`, `SignalScreen` | Filters (type, captain, unread only); grid (ID, type, from, to, read, payload, time); row menu (View Detail, Mark Read, View JSON, Delete); bulk delete; Send Signal modal (type, payload, to captain or Admiral broadcast). | listSignals, getSignal, sendSignal, markSignalRead, deleteSignalsBatch, listCaptains |
| Token Usage | `TokenUsageScreen` | Range (hour/day/week/month); metric by token type or by model; stacked bars or lines (`BarChart`/`LineChart`); copy chart as text (the dashboard copies an image; the TUI copies a text table and offers CSV export); estimated-records note. | getTokenUsage |
| Jobs `/jobs` | `JobsScreen` | Grid (name with error reason, kind, status, progress, created, updated); Cancel while unfinished; auto-refresh. | listJobs, cancelJob |

### SYSTEM

| Dashboard | TUI screen | Contents and actions | API |
|---|---|---|---|
| API Explorer | `ApiExplorerScreen` | Loads `/openapi.json`; operation list filtered by category and text (`FuzzyList`); request builder (path params, query params, headers, body via `TextEditor` with JSON highlighting); request preview as curl, fetch, C# with copy; Send/Abort; response viewer (status, headers, highlighted body, copy, save); accepts replay from API Requests. | (OpenAPI document; generic HTTP through `Armada.Client`) |
| Settings: Server tab | `ServerSettingsScreen` | Status cards (health, uptime, connection Live/HTTP, remote tunnel; info fields version, API URL, ports, tunnel state/instance/latency/heartbeat); Server Configuration (admiral port, MCP port, max captains); Rebuild Armada settings (self vessel, slot retention); Agent Settings (heartbeat interval, stall threshold, idle captain timeout, auto-create PRs); Planning Session settings (idle, abandonment, transcript retention); Vessel Import settings (allowed roots list, excluded folder names list, max depth, inline batch limit, categorization time limit; Save/Discard); Fleet Actions settings; Repository Health settings (global admin; fetch before evaluating, scored criteria checklist, interval, concurrency, dependency max age and timeout, stale branch age, mission failure window, thresholds with fail >= warn validation); Remote Control (enabled, tunnel URL, instance ID override, enrollment token and proxy shared password with reveal, timeouts, reconnect delays, allow invalid certificates; tunnel status); MCP Configuration snippets (copy HTTP and STDIO for Claude Code, Codex, Gemini, Cursor); System Paths (copyable); Database Backup (Backup Now saves the ZIP to a chosen path; Restore from a chosen ZIP with confirm); Server Actions (Setup Wizard, Health Check, Restart with confirm, Rebuild Armada with branch/ref picker, confirm, live build log polling every 1.5 s, Roll Back, Stop Server, Factory Reset, all confirmed). Proxy mode banner and local-only action blocking identical to the dashboard. | getHealth, getSettings, updateSettings, stopServer, restartServer, resetServer, rebuildServer, getRebuildStatus, rollbackServer, getVesselBranches, listVessels, downloadBackup, restoreBackup, getProxySessionContext |
| Settings: Diagnostics | `DiagnosticsScreen` | Run Checks; summary (healthy/unhealthy/warnings, passed/failed); grid (check, status, message). | getDoctor |
| Settings: Tenants (admin) | `TenantsScreen` | Grid (select, name, ID, active, created, updated); search; create/edit (name, active), delete, bulk delete, View JSON; writes disabled behind the proxy; non-admins see their own tenant. | listTenants, createTenant, updateTenant, deleteTenant |
| Settings: Users (tenant admin) | `UsersScreen` | Grid (select, email, ID, name, global admin, tenant admin, active, created); search; tenant filter; form (email, first, last, password + confirm with blank-keeps on edit, tenant admin-only, global admin admin-only, tenant admin, active); bulk delete. | listUsers, createUser, updateUser, deleteUser, listTenants |
| Settings: Credentials (tenant admin) | `CredentialsScreen` | Grid (select, name, ID, user, bearer token, active, created); filters (user, tenant); row menu (Edit, Copy Token, View JSON, Delete); form (user, tenant admin-only, name, active); bulk delete. | listCredentials, createCredential, updateCredential, deleteCredential, listUsers, listTenants |

### Global flows

| Dashboard | TUI | Notes |
|---|---|---|
| Login: email, tenant lookup, tenant picker, password | `LoginScreen` | Email then `lookupTenants`; zero (error), one (skip), many (picker with Back); password field with masking (reveal while `Ctrl+R` held toggles); `authenticate`; token stored per server profile. |
| Login: API key tab | `LoginScreen` | Paste a key or bearer token; validated with `whoami`. |
| Login extras | `LoginScreen` | Language picker, theme toggle, version, GitHub link, default-credentials hint. |
| Session | `SessionService` | Token validated on start (`whoami`); any 401 returns to login; roles from `whoami`. Multiple server profiles (name, URL, last user), selectable at login and from File menu; `--server` and `--profile` flags; `ARMADA_URL` and `ARMADA_TOKEN` environment variables for scripted starts. |
| Setup wizard | `SetupWizard` | Same auto-open rules (no fleets/vessels/captains, or missing pieces without the completed flag; the flag lives in TUI preferences); steps Objective, Fleet, Vessel, Captain, Dispatch, Handoff with the same fields and links; Skip Setup, Back; highlights the related sidebar items. |
| Command palette | `CommandPalette` | Superset of the dashboard (runs commands, not just navigation). |
| Keyboard shortcuts | `KeyMap` | Dashboard shortcuts preserved where they apply (`Ctrl+K`, `Ctrl+S`, `Esc`, arrows on tabs); full map in "Key map". |
| Toasts | `NotificationService` | Same severities and 5 s auto-dismiss; plus history and actions. |
| Theme | `ThemeService` | Light, Dark, HighContrast, Auto. |
| i18n | `LocalizationService` | Same 9 locales and catalog; CJK width-safe rendering in Armada widgets. |
| Background activity indicator | header | Same polling and popover (running jobs with friendly names, Queued/Running, start time, links; Open Jobs), plus the fleet-categorization completion toasts. |
| Notification bell | header + `NotificationCenter` | Latest entries with severity, title, message, relative time; open marks read; Mark all read; Clear. |
| Proxy mode strip | header | Instance ID, state, version; Switch Deployment; Proxy Logout. |

### WebSocket events (parity checklist)

| Event | TUI reaction |
|---|---|
| `mission.changed`, `voyage.changed`, `captain.changed`, `deployment.changed`, `objective.changed`, `incident.changed` | Notification + toast (dashboard text and severity); refresh affected open screens; Approvals center update for reviews, landings, approvals, stalls. |
| `planning-session.changed`, `.message.created`, `.message.updated`, `.tool`, `.thinking`, `.summary.created`, `.dispatch.created`, `.deleted` | Planning screen. |
| `objective-refinement-session.changed`, `.message.created`, `.message.updated`, `.summary.created`, `.applied`, `.deleted` | Backlog item refinement panel. |
| `ask.chunk`, `ask.thinking`, `ask.tool`, `ask.turn`, `ask.message`, `ask.proposal`, `ask.work`, `ask.thread` | Ask Armada, Ask dock, Approvals center, thread list badges. |
| any message | Home reloads; Needs You badge refetch (at most every 4 s). |
| connection state | Header Live/Offline; Server settings Live vs HTTP. |
| reconnect | Ask reloads threads and the open conversation; every open screen refetches. |

### Dashboard API functions not used by the dashboard UI

The dashboard client defines 38 functions no screen calls (for example `searchWorkspace`, `getWorkspaceChanges`,
`createMemory`, `updateMemory`, `importCheckRun`, `deleteRunbookExecution`, `getRunbookExecution`,
`resolveWorkflowProfile`, `validateProjectProfile`, `resolveProjectProfileForVessel`, `enumerate*` variants). They are
implemented in `Armada.Client` for completeness; the TUI exposes the ones with clear user value as extensions beyond
parity (workspace search `Ctrl+Shift+F`, memory create/edit, check-run import, runbook execution delete) and lists them
in the parity manifest as `extension`.

## Key map

| Scope | Keys |
|---|---|
| Global | `Ctrl+K` palette; `F10` menu bar; `?` help; `Ctrl+A` approvals; `Ctrl+N` notifications; `Ctrl+J` Ask dock; `Ctrl+Shift+A` ask about this; `Ctrl+B` sidebar; `Alt+Left`/`Alt+Right` back/forward; `g` then letter go-to (`g a` Ask, `g h` Home, `g i` Needs You, `g m` Missions, `g v` Vessels, `g c` Captains, `g f` Fleet Actions, `g d` Delivery, `g s` Settings); `Ctrl+Q` quit (confirm when a turn or wizard is active); `F12` toggle mouse capture (terminal text selection) |
| Lists | `Up`/`Down`/`PgUp`/`PgDn`/`Home`/`End`; `Enter` open; `.` or `Shift+F10` row menu; `Space` select; `Shift+Up/Down` extend; `Ctrl+A` select page; `Esc` clear selection; `/` filter; `s` cycle sort on the focused column, `S` reverse; `c` columns; `F5` refresh; `n` new; `Del` delete; `j` JSON; `y` copy ID; `[`/`]` previous/next tab; `<`/`>` previous/next page |
| Forms | `Tab`/`Shift+Tab` fields; `Enter` on a select opens it; `Ctrl+S` save; `Esc` cancel (confirm if dirty); `Ctrl+E` edit long text in `$EDITOR` |
| Ask | `Enter` send; `Shift+Enter`/`Ctrl+J` newline; `/` quick actions; `a`/`r` approve/reject focused card; `x` arguments; `Ctrl+C` stop turn; `n` new conversation; `e` rename; `s` summarize; `Ctrl+Y` auto-approve; `Ctrl+T` thread list |
| Logs and transcripts | `f` follow; `Ctrl+F` search, `n`/`N` next/previous; `End` live tail; `y` copy |

All bindings are registered in `CommandService`, shown in the help overlay, listed in the menu bar, and remappable via
TUIKit's `KeyBindingSet` and `KeyBindingEditor` (Settings > Key bindings, stored in TUI preferences).

## Server changes

The TUI uses the existing API. Two small server-side improvements are in scope because the TUI benefits most:

- `GET /api/v1/jobs` paging and a `status` filter (the header indicator polls it; the dashboard benefits too).
- A run-only read for fleet action runs without the full target list (`GET /api/v1/fleet-action-runs/{id}?includeTargets=false`),
  so live run views do not refetch every target every few seconds.

No other server change is required. Any change is made in the server and dashboard together and recorded in
REST_API.md.

## Workstreams and tasks

### W0. Client library

- [x] **W0.1** `Armada.Client` project: HTTP plumbing (base URL, bearer/token/API-key auth, 401 hook, JSON with the
  server's PascalCase, error mapping to a typed `ArmadaApiException` with code, message, request id), typed models.
- [x] **W0.2** One method per dashboard API function (317), generated where possible from OpenAPI and hand-finished,
  grouped by area (Missions, Voyages, Vessels, Ask, ...). Each async method takes a `CancellationToken`.
- [x] **W0.3** `ArmadaSocket`: `?token=` auth, subscribe, typed events for all 29 types, reconnect with exponential
  backoff (1 s to 30 s, jitter), reconnect counter.
- [x] **W0.4** Contract tests: every client method exercised against `E2EServerFixture`; a test that compares the method
  list with `api/client.ts` exports (parity of the client itself).
  Done: export parity (Tui.Parity); Client.Contract.* (Contract, Config, Delivery, Work, AskPlanning, Vessels) call
  314 of the 320 methods against live servers, missions and Ask turns through a scripted stub captain
  (`StubCaptainRuntime`); the six destructive server calls (stop, restart, reset, rebuild, rollback, restore) are
  checked against a recording stub (Client.Destructive). Client.Coverage fails on a method that is neither exercised
  nor listed; Client.RouteSurface maps every method to a `docs/api-surface-1.0.json` route and fails on a route
  without a method (unless listed with a reason) or a method without a route.

### W1. Shell and foundation

- [x] **W1.1** `Armada.Tui` project, `armada tui` command in Helm, startup flags, preferences file, server profiles.
- [x] **W1.2** Shell layout (header, menu bar, sidebar, main, Ask dock, status bar) with stable regions and swappable
  roots.
- [x] **W1.3** Responsive breakpoints from a size watcher; minimum size screen.
- [x] **W1.4** Focus router: hierarchical focus inside hubs, tabs, split and scroll views; consistent `Tab` order.
- [x] **W1.5** Binding layer: change events for selection, text, and validation over TUIKit widgets.
- [x] **W1.6** `ArmadaGrid<T>` with every feature listed under "Shared widgets"; virtualization; CJK-safe widths.
- [x] **W1.7** `SelectField`, `MultiSelectField`, `ActionMenu`, `Button`, `FormView`, `DetailView`, `Drawer`, `Wizard`,
  `TriStateField`, `DateField`.
- [x] **W1.8** `ThemeService` with Light, Dark, HighContrast, Auto, pushing styles to every widget instance.
- [x] **W1.9** Command palette modal and help overlay generated from `CommandService`.
- [x] **W1.10** Notification center with history, actionable toasts, bell and OS notification hooks.
- [x] **W1.11** `EventPump` with `app.Post` marshaling and burst coalescing; `RefreshService`.
- [x] **W1.12** `Router` with deep links and back/forward; `ClipboardService`; `ExternalService`.
- [x] **W1.13** `LocalizationService` using the server catalog; ICU plurals; locale formatting.
- [x] **W1.14** Login (email/tenant/password and API key), session handling, roles, proxy-mode strip.
- [x] **W1.15** Shared viewers: JSON, diff, log, Markdown, charts; confirm and error dialogs.

### W2. Ask Armada

- [x] **W2.1** Thread list with search, archive filter, paging, pin/unread/working/replying indicators, row menu, keys.
- [x] **W2.2** Conversation header, captain picker, auto-approve with warning and banner, summarize, no-MCP note, work strip.
- [x] **W2.3** Transcript with all message kinds, streaming (incremental block rendering while streaming), tool chips,
  thinking, metrics, older-page loading, search, copy.
- [x] **W2.4** Confirm cards with approve/reject/arguments/expiry and status transitions; integration with
  `ApprovalService`.
- [x] **W2.5** Live work cards for all tracked entity types with row drill-down.
- [x] **W2.6** Composer with slash quick actions and inline forms, `$EDITOR`, history recall, stop, optimistic send.
- [x] **W2.7** Ask dock and "Ask about this" context handoff.
- [x] **W2.8** All `ask.*` events and reconnect behavior.
  Notes: `Ctrl+Shift+A` and `Ctrl+Shift+T` are not distinguishable from `Ctrl+A`/`Ctrl+T` in most terminals, so
  "Ask about this" is `Alt+A` and "Show thinking" is `Alt+T` (both also in the palette and Actions menu). The
  metrics line is measured by the TUI (time to first token, total) with tokens estimated at four characters each,
  because Ask turns report no token counts. The proposal toast's action is Open (approving from a toast would be
  one keystroke away from an accident); single-key approval lives on the card and in the Approvals center.
  Pending proposals of conversations that were never opened in this session reach the queue through
  `ask.proposal` events only (no endpoint lists them across threads).

### W3. Operations

- [x] **W3.1** Home. **W3.2** Needs You. **W3.3** Approvals center (done: `ApprovalsScreen`, `ApprovalSources`,
  `ApprovalActions`, Resolve Review dialog). **W3.4** Planning. **W3.5** Dispatch.
  **W3.6** Backlog and Backlog item with refinement. **W3.7** Fleet Actions (actions, runs, run detail, run flow).
  **W3.8** Missions and Mission detail with review. **W3.9** Voyages, Voyage detail, Create voyage.
  **W3.10** Merge Queue and entry detail. **W3.11** Jobs.
  Notes: built on shared OPERATIONS bases in `Screens/Operations` (`OpsListScreen`, `OpsDetailScreen`,
  `OpsFormDialog`, `OpsTextArea`, `OpsLogModal`, `MissionOps`). Pre-fill handoffs travel in the route query
  (`OpsHandoff`; keys documented in docs/TUI.md) instead of router state. Planning, the Backlog item, and Mission
  detail use tabbed panels instead of one stacked page; Home is one scrolling page of focusable sections. The
  Mission History chart is ASCII (`#` complete, `x` failed, `.` other). Fleet action run detail polls every 5 s with
  its own timer (the refresh service has no 5 s step). Mission Edit sends the full record (the dashboard's partial
  body blanks vessel, voyage, branch, and PR URL on the server). Dispatch sends the description as one mission, as
  the dashboard does (the line above that said "multiple tasks become multiple missions" was older than the page).

### W4. Build

- [x] **W4.1** Vessels (grid, form, branches, build context). **W4.2** Import wizard (all steps, recommendations,
  history). **W4.3** Vessel Health (grid, filters, columns, detail tabs, overrides, evaluation). **W4.4** Vessel detail
  and onboarding. **W4.5** Fleets. **W4.6** Workspace (tree, editor, terminal, context, diff). **W4.7** Captains
  (grid, form incl. Mux, tools, detail, log, quarantine). **W4.8** Docks.
  Notes: built in `Screens/Build` on the OPERATIONS bases (`OpsListScreen`, `OpsDetailScreen`, `OpsFormDialog`,
  `OpsTextArea`, `OpsLogModal`); registered by `BuildScreens.Register`. The vessel form is one dialog with every field
  of both dashboard vessel forms, and vessel, fleet, and captain edits send the full record (the dashboard's partial
  bodies would clear unshown fields on the server's full-replace PUT). The GitHub token override travels in
  `VesselUpsertRequest` (`Armada.Client.Models`), because `Vessel` only deserializes that field. The import wizard is
  the `/vessels/import` screen rather than a modal over Vessels; its Review and Results steps use panels (`[`/`]`)
  for the candidates and the defaults, and for the items and the fleet recommendations. Vessel Health mirrors its
  filters, sort, and page into the route query through `Router.ReplaceQuietly` (no screen rebuild). The Workspace is
  a tree and a tabbed editor side by side, with the terminal, diff, context, metadata, and search as dialogs; the
  editor is plain text (TUIKit's `TextEditor` has no highlighting), while read-only previews are highlighted. Captain
  logs use the server's readable formatting (`OpsLogModal` gained a toggle and an initial line count). Fixed on the
  way: the client's `evaluateVesselHealth` now accepts 409 like the dashboard; a pending `g` prefix completes before
  the screen sees the key, so `g s` works from a grid.

### W5. Delivery

- [x] **W5.1** Deployments and detail. **W5.2** Environments and detail. **W5.3** Releases and detail.
  **W5.4** Incidents and detail. **W5.5** Checks and check run detail. **W5.6** Runbooks and detail with executions.

### W6. Configuration

- [x] **W6.1** Workflow Profiles. **W6.2** Project Profiles with prompt diff. **W6.3** Skills. **W6.4** Personas.
  **W6.5** Pipelines with Run Pipeline. **W6.6** Prompts with parameter palette. **W6.7** Playbooks.
  **W6.8** Endpoints with validate and health. **W6.9** Harbors. **W6.10** Memory.

### W7. Activity and System

- [x] **W7.1** All Activity with saved views and export. **W7.2** API Requests with chart, filters, bulk deletes,
  replay. **W7.3** Events. **W7.4** Signals with send. **W7.5** Token Usage. **W7.6** API Explorer.
  **W7.7** Settings Server tab (every section and action). **W7.8** Diagnostics. **W7.9** Tenants, Users,
  Credentials. **W7.10** Setup wizard. Also Jobs (`/jobs`, listed under W3.11). Notes: saved views live in
  `tui-activity-views.json` next to `tui.json`; Token Usage copies charts as text tables and adds CSV export;
  API Requests bulk deletes are not role-gated (the dashboard shows Delete Selected and Delete Visible Range /
  Delete Filtered to anyone who can list requests; only the tenant and user filters are gated); the setup
  wizard cannot highlight sidebar items yet (it shows a "Related:" line; needs a shell hook).

### W8. Quality

- [x] **W8.1** Parity enforcement (below). Manifest generated (`scripts/tui/generate-parity-manifest.py`) and the
  coverage checks run in Tui.Parity. Release gate: `generate-parity-manifest.py --check` (the `tui-parity` job in
  `.github/workflows/ci.yml`) fails when `parity.json` differs from what the generator would write, when any entry is
  `planned`, or when a `not-applicable` or `extension` entry has no notes; Tui.Parity enforces the same rules on every
  OS (`no_planned_entries`, `release_gate_rejects`), fails on entries for surfaces the dashboard no longer has
  (`no_stale_entries`), and checks that the generator and the suite skip the same non-server `client.ts` exports
  (`generator_in_sync`; the generator was missing `isApiStatus`). The release workflow itself does not run the check;
  CI on the tagged commit does.
- [x] **W8.2** Headless test suites: one keyboard-flow test per screen (open, filter, select, row action, modal,
  confirm) with `HeadlessBackend` and `WidgetTester`; Ask streaming and approval tests with a scripted event source.
  Done for the built screens: Tui.KeyboardFlows (Delivery and Configuration) and Tui.KeyboardFlows.Ops (Operations,
  Activity, System lists) run one table-driven flow per screen (open, filter, select, row menu, create form or page,
  open the row, back); Ask, Approvals, Login, Shell, Setup, Settings, API Explorer, Token Usage, Home, Planning, and
  Dispatch keep their own suites. Build: Tui.KeyboardFlows.Build runs the same flow for Vessels, Vessel Health
  (server-side filter), Fleets, Workspace, Captains, and Docks through `TuiFlowRunner` (new `TuiFlowSpec` hooks: stub
  factory, request checks, server-filter wait, terminal size), and by hand for the import wizard (I from Vessels,
  paths, discover, review search and selection, options, import, open a result, back) and the vessel page with
  onboarding (tabs, action menu, edit form, `g` onboarding, next step, back); requests are checked with
  `RequestsFor`, `BodiesFor`, `LastBody<T>`, and `QueryValue`, never by text. Note: on editor pages that open in a
  text field (Create Voyage, backlog item) Alt+Left moves by word and Esc goes back.
- [x] **W8.3** End-to-end suite against `E2EServerFixture`: login, Ask dispatch with approval through landing (stub
  captain), Fleet Action run, import, health evaluation, settings save.
  Done (Tui.EndToEnd, Tui.EndToEnd.Flows): API key and password login; a captain-proposed Ask dispatch approved with
  `a` that lands on the vessel's bare origin through the real completion and LocalMerge pipeline; Approvals center
  approve/reject of live Ask proposals and a deployment approval; notifications from live WebSocket events and the
  notification center; a Fleet Action run followed to completion; a server settings save. Tui.EndToEnd.Build: the
  import wizard imports two local git repositories (paste, discover, review, default fleet, import, results, View
  vessels) and the vessels are checked on the server (fleet, working directory); Vessel Health filters to a vessel on
  the server, re-evaluates it, follows the job until the server has an evaluation, and opens the inspector and the
  vessel. Both run with the stub captain runtime installed; no agent CLI runs.
- [~] **W8.4** Accessibility and display: HighContrast theme, ASCII icon mode, no color-only states, 80x24 minimum,
  tmux/SSH validation, Windows Terminal, iTerm2, Terminal.app, GNOME Terminal, conhost (degraded). Done: High contrast
  uses reverse video and underline for every selected state and is picked by Auto under `NO_COLOR`; Icons
  Auto/Unicode/ASCII (`Glyphs` preference, View menu) with Auto from the terminal encoding and every glyph written to
  the terminal transliterated in ASCII mode; status, tab, sidebar, and wizard states carry text or a symbol; every route
  and hub tab checked at 80x24 (sidebar hidden, `? Help` always shown, header keeps approvals and the bell); a localized
  terminal-too-small screen; tables give the title column room and shrink, elide, then drop ID columns first (checked at
  80, 100, 120, and 160 columns). Tests: Tui.Display.
  Scripted terminal checks (2026-10-05): `scripts/tui/terminal-check.sh` runs the real `armada tui` in a
  pseudo-terminal through a VT100 emulator (pyte) against a throwaway Admiral, and with Docker repeats it in a Linux
  container (`mcr.microsoft.com/dotnet/aspnet:10.0`) with its own Admiral plus tmux. Result: macOS 52 of 52 checks,
  Linux 76 of 76. Covered: `TERM=xterm-256color` UTF-8 (Unicode borders, `g v`, `Ctrl+K` palette running a
  destination, `?` help and Esc, resize 120x40 to 80x24 to 70x20 "Terminal too small" and back, `Ctrl+Q` exit 0,
  cursor shown and alternate screen left on exit); `TERM=xterm LANG=C`, `TERM=dumb`, and (Linux) `TERM=linux
  LANG=POSIX` render ASCII only with ASCII borders; `NO_COLOR=1` gives high contrast with reverse-video selection;
  an SSH-like session (`SSH_CONNECTION`, `SSH_TTY`, no locale variables) renders Unicode and handles keys; 80x24 from
  the start; `TERM=linux LANG=C.UTF-8`; tmux with `TERM=screen-256color` (render, `g v`, palette, Esc, Alt+Left back,
  `resize-window` to 80x24, `Ctrl+Q` exit 0). Frames are written for review. Found and fixed on the way: Esc in the
  import wizard's last text field did nothing; the picker cut off its own key hints; the Ask header said `[c]` for
  the captain while `c` types a letter in the composer (now `[Esc c]`); the Workspace file tree swallowed Alt+Left
  (Back), found by the Build keyboard flow. Found, not fixed: `Ctrl+Q` does not quit while
  a dialog or picker is open (TUIKit gives modals every key before `KeyFilter`; documented in Troubleshooting); on the
  Vessels table at 120 columns the fixed-width Landing Mode, Sync, and Branches columns leave Fleet and Repository at
  6 to 7 cells (the grid layout keeps fixed widths and squeezes proportional columns to their minimum; needs a layout
  rule, not a one-screen tweak).
  Left for a person (visual and real-terminal checks the emulator cannot make): Windows Terminal and conhost on
  Windows (Auto icons, colors, Alt keys, mouse, resize), iTerm2 and Terminal.app on macOS (colors and the focus
  border, mouse and F12, OSC 52 copy, Option as Alt), GNOME Terminal on Linux, a real SSH session from another machine
  (locked keychain falls back to the file store, locale forwarding), and tmux by hand (OSC 52 with `set-clipboard`,
  mouse).
- [x] **W8.5** Performance: 10,000-row grids stay responsive (virtualization, server paging), Ask transcripts of 5,000
  lines, idle CPU under 2 percent (lower `TargetFps` when idle), memory caps for scrollback. Frame governor and Armada
  run loop (idle about 4 frames per second, 1 percent of a core), Ask block cache (streaming chunk 51 ms to 4 ms at
  5,000 messages), `AskConversation.MaxMessages` cap. Numbers and harness in `docs/TUI_PERFORMANCE.md`; tests:
  Tui.Performance.
- [x] **W8.6** Telemetry: TUIKit meter and activity source plus `armada_tui_*` metrics (screen views, command use,
  approval latency) behind the server's telemetry settings. The `Telemetry` section of `tui.json` has the server's
  fields with TUI defaults (off, `armada-tui`, no scrape endpoint, port 9465); when on, Helm starts the Admiral's
  `ArmadaTelemetryHost` with the `Armada.Tui` and `TUIKit` sources added. Metrics: sessions, screen views (route
  pattern), commands (id, source, outcome), approval decisions and latency, Ask messages; `armada.tui.command` spans.
- [~] **W8.7** Simulated user testing session per `SIMULATED_USER_TESTING.md` using the TUI as the primary surface.
  First unattended run (Release Helm in a 160x48 pty driven with pyte, stub captain; results in
  `docs/SIMULATED_USER_TESTING_RESULTS_1.0.md`): first-run sign-in and the setup wizard on a fresh Admiral, Dispatch,
  Ask Armada with the Approvals center (`a` approve, card tracked to Landed), mission detail and log, API-key sign-in
  with a credential and revocation, and screen checks of Missions, Merge Queue, Vessel Health, Fleet Actions,
  Planning, Needs You, Backlog, Users, Credentials, Diagnostics, and Events. TUI findings: the prefilled masked
  password makes typing the documented default fail (F9, fixed: the prefill starts selected), no forced
  default-password change (F10, decided: the TUI warns and does not force a change; the dashboard forces it), sign-out
  on every Admiral restart (F8, fixed: the session key is persisted), toasts over the Approvals center (F31). Left for a human: the remaining flows end to end in
  the TUI with real captains, and visual checks in real terminals.

### W9. Docs and distribution

- [x] **W9.1** `docs/TUI.md`: install, start, profiles, key map, screens, approvals, notifications, troubleshooting.
  Install, start, profiles, login (including the localhost prefill and server editing), navigation, notifications, Ask Armada,
  the Approvals center, the key map, every screen section (Operations, Build, Delivery, Configuration, Activity
  and System), and Troubleshooting (cannot connect and wrong port, Ask not connected over MCP and the MCP host
  mismatch, login messages, terminal too small, glyphs and ASCII mode, colors and `NO_COLOR`, SSH and tmux, keychain
  and `ARMADA_TUI_CREDENTIAL_STORE=file`, where files live, resetting preferences, performance, `Ctrl+Q` with a dialog
  open, and bug reports with Help, Save screen snapshot...).
- [x] **W9.2** README section and screenshots (text captures from `Snapshot`). Captures in `docs/tui-screens`,
  regenerated with `ARMADA_TUI_README_DIR=docs/tui-screens ARMADA_TEST_SUITES=Tui.ReadmeFrames`.
- [x] **W9.3** Ships with Helm (`armada tui`) in every channel Helm ships in; CHANGELOG entry. Helm ships in the NuGet
  tool (also used by `scripts/*/install`) and the Linux Deb/Rpm CLI packages (single-file self-contained publish);
  the Homebrew, Scoop, Chocolatey, Winget, and AppImage CLI channels are disabled stubs; Pkg/Wix/Inno/Dmg and the
  Docker image carry the server or Harbor, not Helm.

### U. TUIKit upstream (in `~/Code/Tuikit`)

Each item lands in TUIKit with its own tests, then Armada switches to it and deletes its local copy.

Status (2026-10-05): U1-U10 and the TUIKit-side gaps reported by the screen workstreams are implemented on branch
`work/armada-upstream` in `~/Code/Tuikit` (commit `ce362e6`, 681 tests passing, additive APIs, version unchanged).
Not released: Armada moves to them after a TUIKit release, then removes its workarounds (closed-modal cleanup,
per-chunk Markdown re-render, local FocusScope/ValueChanged/ButtonRow copies).

- [~] **U1** Key and mouse forwarding into `TabView`, `SplitView`, `ScrollView` children; hierarchical focus scopes.
- [~] **U2** Change events on `ListView`, `DataTable`, `TextField`, `TextEditor`, `Checkbox`, `RadioGroup`.
- [~] **U3** `DataTable` improvements: column sizing, typed sort with indicators and header click, multi-select, per-cell
  styles, paging hooks, CJK-safe widths.
- [~] **U4** `Dropdown`/`ComboBox`, `Button`, `ContextMenu`, `Tooltip`, `Badge`.
- [~] **U5** Theme-aware widgets (read `app.Theme` roles), theme-driven Markdown and toast colors.
- [~] **U6** Notification center history, actions, dismiss, wider toasts.
- [~] **U7** Incremental Markdown rendering for `StreamingTranscript` while a block streams.
- [~] **U8** `Pane` wrap cache and render cost reduction; idle frame throttling.
- [~] **U9** `TextWidth`-based width math across all widgets.
- [~] **U10** Built-in command palette modal and key-help overlay.

## Parity enforcement

A parity manifest, `src/Armada.Tui/parity.json`, maps every dashboard surface to its TUI implementation:

- every route in `src/Armada.Dashboard/src/App.tsx` (including redirects, which map to the same TUI route),
- every hub tab,
- every exported server-calling function in `src/Armada.Dashboard/src/api/client.ts`,
- every WebSocket event type the dashboard handles,
- every settings field on the Server page.

Each entry has a status (`implemented`, `planned`, `not-applicable` with a reason, `extension`) and the TUI screen or
command id. A `Tui.Parity` test suite parses the dashboard source and the manifest and fails when:

- a dashboard route, tab, API function, event, or settings field has no manifest entry,
- an entry marked `implemented` points to a screen or command that does not exist,
- a release build has any entry still `planned` (enforced: `no_planned_entries` in Tui.Parity and
  `generate-parity-manifest.py --check` in CI, which also fails when the committed manifest is out of date or a
  `not-applicable` or `extension` entry has no notes).

This turns "parity" into a build check: when someone adds a page or API function to the dashboard, the TUI build tells
them what is missing.

## Delivery order

1. **Milestone A: foundation and Ask.** W0, W1, W2, W3.3 (Approvals), W1.10 notifications. The TUI is useful from day
   one as an Ask-first operator console with approvals and live work tracking.
2. **Milestone B: operations.** W3 (all), Jobs, Needs You.
3. **Milestone C: build.** W4.
4. **Milestone D: delivery and configuration.** W5, W6.
5. **Milestone E: activity, system, quality, docs.** W7, W8, W9, parity at 100 percent.

The TUIKit upstream workstream (U) runs alongside, prioritized by which Armada workstream needs it next (U1, U3, U4
before Milestone B; U5, U6 before Milestone E).

## Requirements compliance

- **CODE_STYLE.md:** usings inside namespaces, no `var`, no tuples, one class per file, XML docs on public members,
  `_PascalCase` privates, `CancellationToken` and `ConfigureAwait(false)` in library code (`Armada.Client`), no
  `Console.WriteLine` in library code, specific exceptions with `<exception>` tags, configurable values as members.
- **BACKEND_TEST_ARCHITECTURE.md:** all TUI and client suites live in `Test.Shared` and run under Test.Automated,
  Test.Xunit, and Test.Nunit on net8.0 and net10.0; tests use `127.0.0.1`.
- **I18N.md:** every user-visible string goes through the shared catalog; no hard-coded strings in screens; ICU plurals;
  locale formatting; CJK rendering checked in headless tests.
- **DASHBOARD_STYLE_AND_USABILITY.md (adapted):** grouped navigation, server-side filter/sort/paging, row and bulk
  actions, confirmations, empty/loading/error states, no color-only status.
- **TELEMETRY_REQUIREMENTS.md:** low-cardinality metric labels; telemetry failures never break the UI.
- **VERSIONING.md:** the TUI ships with Helm's version; agents do not change versions without explicit approval.
- **WRITING_DOCUMENTS.md:** `docs/TUI.md` written as prose, no em-dashes.

## Decisions (resolved 2026-10-04: the maintainer approved the plan and its recommendations)

- **D1. Packaging.** Resolved: `Armada.Tui` is a library hosted by Helm as `armada tui`, so one install provides the
  CLI and the TUI. Alternative: a separate `armada-tui` binary and packages.
- **D2. Client library scope.** Resolved: `Armada.Client` is generated from OpenAPI and hand-finished, and Helm moves
  to it over time. Alternative: a TUI-private client.
- **D3. TUIKit upstream.** Resolved: upstream the general widgets and fixes (workstream U) and keep Armada-specific
  widgets in `Armada.Tui`. Alternative: keep everything in Armada.
- **D4. Credentials at rest.** Resolved: OS keychain (macOS Keychain, Windows Credential Manager, libsecret) with a
  `0600` file fallback.
- **D5. Release timing.** Resolved: built in parallel with the v1.0 work; whether it is covered by the 1.0 compatibility promise is decided at the first release candidate.

## Progress Log

| Date | Author | Task(s) | Change |
|------|--------|---------|--------|
| 2026-10-04 | (design) | -- | Plan drafted from a full inventory of the dashboard (routes, tabs, modals, API functions, WebSocket events, settings) and a survey of TUIKit 1.2.1. |
| 2026-10-04 | Claude (tui-foundation) | W0, W1, W8.1, W9.1 | Armada.Client (all 317 client.ts functions as typed async methods, generated by `scripts/tui/generate-client-methods.py` and hand-finished; ArmadaSocket with typed events, backoff 1-30 s with jitter, reconnect counter; paging helpers). Armada.Tui foundation: `armada tui` in Helm, preferences and profiles, keychain/wincred/secret-tool credential stores with a 0600 file fallback, single-root shell with responsive layout and focus router, binding layer, ArmadaGrid, form widgets, viewers, dialogs, themes, commands/menu/palette/help, notifications and approvals skeleton, event pump, refresh, router with every dashboard route (placeholders for unbuilt screens), clipboard, external editor, i18n over the dashboard catalog, login and session. Parity manifest and Tui.Parity suite. Tests: Tui.* and Client.* suites (headless and live server). Deviations: the dashboard handles 28 WebSocket event types plus `status.snapshot` (the plan said 29); `GET /fleets/{id}` returns `{ Fleet, Vessels }`, so `GetFleetAsync` returns `FleetDetail`; API keys pasted on the login screen are tried as X-Token, bearer, then X-Api-Key. |
| 2026-10-04 | Claude (tui-ask) | W2.1-W2.8, W3.3, W1.14, W8.1 | Ask Armada: `AskController` session (thread list, conversation reducer ported from `lib/askConversation.ts`, optimistic send, stop with the 8 s local force-end, read marking only while the screen shows and the terminal is focused, reconnect refetch, `ask.*` events for every thread), `AskScreen` (list, header, banner, no-MCP note, work strip, transcript, inline Dispatch and Fleet action forms, composer on TUIKit `TextEditor` and `SubmitKeyResolver`), live work cards for all five entity types with row drill-down (mission, PR, log, diff), the Ask dock, and Ask about this. Approvals center fed by the inbox, entity events, and Ask proposals, with the dashboard's calls and confirmations. Login: forced default-password change step and the default-credentials header warning. Client fix: `ArmadaRawJson` now serializes as its raw value (quick-action arguments were sent as `{"Json": ...}`). Tests: Tui.Ask (16), Tui.Approvals (5), new Tui.Login and Tui.Notifications cases. Real run through `armada tui` in a pty against a throwaway server with a Claude Code captain: forced password change, captain-proposed dispatch approved with `a`, mission landed on the bare origin, Approvals center, dock. TUIKit gaps worked around: `StreamingTranscript` flattens newlines while streaming (U7; the live reply re-renders Markdown per chunk), `TextEditor` swallows every Ctrl chord and measures `string.Length` (wrapped so palette and global keys still work; CJK widths in the composer remain U9), `Pane` has no keyed multi-line blocks for cards (the transcript is an Armada widget over block layouts). |
| 2026-10-04 | Claude (tui-delivery-config) | W5, W6 | Delivery (Deployments, Environments, Releases, Incidents, Checks, Runbooks; list and detail each) and Configuration (Workflow Profiles, Project Profiles, Skills, Personas, Pipelines, Prompts, Playbooks, Endpoints, Harbors, Memory) screens on shared `EntityListScreen`/`EntityDetailScreen` bases with `FormDialog`, `FilterBar`, `KpiStrip`, `ActionBar`, `TextAreaField` (inline plus `$EDITOR`), `RecordListField`, `CheckField`, `LinkDetailView`, `StackPanel`; server filters and paging, local sort over all matches where the server cannot sort; scoping per `lib/scoping.ts`; navigation prefill hand-offs; live refresh on `deployment.changed`/`incident.changed`. Parity: 17 routes and 16 tabs flipped to implemented. Deviations: detail editors are panels or dialogs rather than side-by-side forms; FormView cannot hide or disable rows, so scope-dependent fields are always shown with hints. TUIKit gaps: a modal closed from a posted callback stays drawn until the next input (worked around with `Modals.RemoveClosed`); no read-only text input or select. |
| 2026-10-04 | Claude (tui-activity-system) | W7.1-W7.10, W3.11 Jobs, W8.1 | Activity and System screens on a shared kit (`Armada.Tui.Screens.Kit`: StackScreen/GridScreen, ScreenHeader, FilterStrip, KpiBar, FormModal, PathPrompt, ToggleField, MultilineField, MultiSeriesChart, RecordDetailView, UserScopeField, DataExport, ScreenOps): All Activity, API Requests (+drawer, `/requests/:id`, replay), Events (+detail), Signals (+detail, send), Token Usage, Jobs, API Explorer, Settings Server tab (every section incl. Vessel Import, Fleet Actions, Repository Health, Data Retention, Remote Control, backup/restore to chosen files, restart/stop/reset/rebuild with live log and rollback, proxy restrictions), Diagnostics, Tenants, Users, Credentials, Setup wizard with auto-open. Parity: 8 routes, 10 tabs, 55 settings fields implemented (parser now covers Repository Health fields and thresholds and Data Retention fields). Fixes: modals closed outside key handling are dropped from the stack at once (TUIKit swallowed the next key); ArmadaGrid passes Alt+arrows through for history navigation; HubScreen forwards the tab's status hints. Armada.Client SettingsData gains Retention. |
| 2026-10-04 | Claude (tui-operations) | W3.1-W3.11, W8.1, W9.1 | OPERATIONS screens: Home, Needs You, Planning (live `planning-session.*`), Dispatch (pre-fill handoffs), Backlog and Backlog item (GitHub import, refinement with live `objective-refinement-session.*`), Fleet Actions (actions, runs, run detail with target drawer, vessel picker and run flow), Missions and Mission detail (diff, log, instructions, review, transition, landing preview, PR panel), Voyages, Voyage detail, Create Voyage, Merge Queue and entry detail, Jobs. Shared bases in `Screens/Operations`. Parity: 16 routes, 7 tabs, and 14 events flipped to implemented. Client: `GetVoyageDetailAsync` (the server returns `{ Voyage, Missions }`), `PlanningSessionEvent.Draft`. Fixes: Alt+arrows pass through `ArmadaGrid` (history navigation), hub screens forward the tab's status hints, `FormView` skips hidden rows. TUIKit gaps worked around: a modal closed without a key stays on the stack and swallows the next key (`RemoveClosed` after programmatic closes); `FormView` has no checkbox field, hidden rows, or a way to turn off dirty tracking. Tests: Tui.Ops.* suites (Home, Jobs, Missions, MissionDetail, Voyages, MergeQueue, Planning, Dispatch, Backlog, BacklogItem, FleetActions). |
| 2026-10-04 | Claude (tui-telemetry-ship) | W8.6, W9.2, W9.3 | Telemetry: `TuiTelemetry` (`Armada.Tui` meter and activity source; `armada_tui_sessions_total`, `armada_tui_screen_views_total`, `armada_tui_commands_total`, `armada_tui_approval_decisions_total`, `armada_tui_approval_latency_seconds`, `armada_tui_ask_messages_total`; `armada.tui.command` spans), `TuiTelemetrySettings` in `tui.json` (off by default), `TuiStartOptions.TelemetryHostFactory`, Helm's `TuiCommand.StartTelemetryHost` over `ArmadaTelemetryHost.AdditionalSources` (`Armada.Tui`, `TUIKit`); `TuiTelemetry.Configure` also switches `TuiKitTelemetry.Enabled`. README Terminal UI section with five 120x40 captures from the new Tui.ReadmeFrames suite. Distribution: packed `Armada.Helm` 0.9.0 to a temp folder (tools/net8.0 and net10.0 contain Armada.Tui, TUIKit, Armada.Client, Radiant), installed it with `--tool-path` from a local-only source, ran `armada tui --help`, and ran `armada tui` in a 120x40 pty with telemetry on (Prometheus on a 370xx port served `armada_tui_*` and `tuikit_*`); single-file self-contained publishes (the Deb/Rpm recipe) for osx-arm64 (run the same way) and linux-x64 (bundle inspected) contain the same assemblies. No channel needed a fix; docs now say which installers carry the CLI. Tests: Tui.Telemetry (9, MeterListener and ActivityListener, plus a Prometheus scrape of Helm's host), Tui.ReadmeFrames (1). Note: the Prometheus listener binds `localhost`, which on macOS answers on ::1 only (scrape `localhost`, not `127.0.0.1`). |
| 2026-10-04 | Claude (tui-build) | W4.1-W4.8, W8.1, W9.1 | BUILD screens in `Screens/Build`: Vessels (grid with background sync and branch counts, the full vessel form, Manage Branches with push and merge, Build/Refine Context, bulk Run action), the import wizard (paste or browse sources, background discovery, review with chips, selection, defaults, and the fleet recommendation opt-in, background import with progress, recommendation editing and apply, import history, `?batch=`), Vessel Health (summary chips, server filters, sort, and paging round-tripping through the route query, column chooser, health inspector with findings, dependencies, and overrides, evaluation tracking), the vessel page and onboarding, Fleets and the fleet page, the Workspace and its vessel picker (tree, tabbed editor with hash-checked save and `$EDITOR`, previews, terminal, diff, context with Append Selection, search, Plan and Dispatch handoffs), Captains and the captain page (Mux fields with discovery, tools viewer, readable captain log, quarantine), Docks and the dock page. Parity: the last 11 routes and 6 tabs flipped to implemented (no planned entries remain). Client: `VesselUpsertRequest` (token override), `evaluateVesselHealth` accepts 409. Shell: `Router.ReplaceQuietly`, pending go-to prefixes complete before the screen. Tests: Tui.Build.* (FleetsDocks, Captains, Vessels, Health, Workspace, Import; 18 cases). Real run against a throwaway Admiral on 33010/33011: vessel create, import of temp git repos (twice, with the leave-review guard), health evaluation and inspector, a Workspace edit with diff and terminal, captain create with the tools viewer, and a dock view. |
| 2026-10-04 | Claude (tui-a11y-perf) | W8.4, W8.5 | Display: Icons Auto/Unicode/ASCII (`GlyphModeEnum`, `TuiPreferences.Glyphs`, View menu `view.icons.*`), `TerminalEncoding` for Auto, `AsciiGlyphs` width-preserving map applied by `TerminalBackendAdapter` to everything written to the terminal and by `TuiSnapshot`; high contrast with reverse video for selection and Auto picking it under `NO_COLOR`; localized terminal-too-small screen; status bar keeps `? Help`; header gives approvals and the bell priority; `ArmadaGrid` column layout gives the primary (title or name) column room and shrinks with middle elision, then drops, ID columns first (`GridColumn.Identifier`, `GridColumn.Primary`, `TextCells.ElideMiddle`); wizard steps marked `+`. Performance: `FrameGovernor` plus `TuiRunLoop` (compose on input, posted work, resize, theme change, or a 250 ms idle tick; 25 ms idle input polling), `TuiContext.Quit`, `AskTranscriptBuilder` per-message block cache, `AskConversation.MaxMessages`. Measured idle CPU 10.2 to 1.1 percent of a core (Ask, 5,000 messages) and 4.1 to 0.6 (Jobs); streaming chunk at 5,000 messages 51 ms to 4.4 ms. Tests: Tui.Display (10), Tui.Performance (8). TUIKit gaps: `RunAsync` has no frame-skip hook and no public stop flag (U8; Armada runs its own loop and routes Ctrl+Q through `TuiContext.Quit`); the terminal-too-small screen is not localizable; no post-compose hook, so ASCII mode transliterates at the backend. |
| 2026-10-05 | Claude (tui-e2e) | W0.4, W8.2, W8.3 | Client contract: Client.Contract.Config/Delivery/Work/AskPlanning/Vessels exercise 314 of 320 client methods live; Client.Destructive covers the other six against a recording stub; Client.Coverage and Client.RouteSurface (client vs. `docs/api-surface-1.0.json`) keep it that way. `StubCaptainRuntime` (installed through the new `AgentRuntimeFactory.Override` and `ArmadaServer.RuntimeFactory`; `BaseAgentRuntime` gains protected raise helpers) answers Ask turns through MCP with the turn's session token, commits a file for missions, and replies to planning, refinement, context, chat, and categorization prompts. Client fixes: `DispatchMissionAsync` returns `MissionDispatchResult` (Mission and Warning) and `CreateMissionAsync` unwraps the `{ Mission, Warning }` reply (the setup wizard's raw-call workaround is gone); `ProcessAllMergeQueueAsync`, `CancelMergeEntryAsync`, `RecallCaptainAsync`, and `GetVoyageStatusAsync` called routes the server does not have (copied from api/client.ts) and now call the real ones; `GetVoyageAsync` unwraps `{ Voyage, Missions }`; `UnquarantineCaptainAsync` no longer returns a made-up captain for `{ Status: "not_quarantined" }`. Server: new `GET /api/v1/events/{id}` (the TUI and dashboard event detail pages called it); the vessel landing preview unescapes `sourceBranch`. Tests: Tui.EndToEnd.Flows (5), Tui.KeyboardFlows (16), Tui.KeyboardFlows.Ops (13), Client.* (8 new suites), approvals and setup wizard cases. Not done: TUI flows for the Build screens and the import/health end-to-end flows (Build screens in progress). |
| 2026-10-05 | Claude (tui-final) | W8.1, W8.2, W8.3, W8.4, W9.1 | Parity release gate: `generate-parity-manifest.py --check` and a `tui-parity` CI job; Tui.Parity `no_planned_entries`, `release_gate_rejects`, `no_stale_entries`, `generator_in_sync` (the generator now skips `isApiStatus` like the suite). Tui.KeyboardFlows.Build (8 flows: Vessels, Vessel Health, Fleets, Workspace, Captains, Docks, import wizard, vessel page and onboarding) with structured request checks. Tui.EndToEnd.Build (import wizard to imported vessels, health evaluation and inspector; live server, stub runtimes). `scripts/tui/terminal-check.sh` (pty and VT emulator matrix on macOS, plus tmux and Linux console settings in a Linux container; 52 + 76 checks pass). Fixes: Esc in the import wizard's last text field; picker key hints cut off; Ask captain hint `[Esc c]` while typing; Alt+Left in the Workspace tree; new Help, Save screen snapshot... command. Docs: Troubleshooting in `docs/TUI.md`; README frames regenerated. Not fixed: `Ctrl+Q` with a dialog open (TUIKit routes every key to the modal first); Vessels table column squeeze at 120 columns (grid layout rule). |
| 2026-10-05 | Claude (sim-testing) | W8.7 | First simulated user session with the TUI (Release Helm in a pty with pyte, stub captain, throwaway Admiral): sign-in on a fresh server, setup wizard, Dispatch, Ask proposal approved from the Approvals center and tracked to Landed, mission log, API-key sign-in and revocation, screen checks of eleven screens. Findings F8, F9, F10, F31 recorded in `docs/SIMULATED_USER_TESTING_RESULTS_1.0.md` for the TUI owner (no TUI files changed). Stays open for the full human run. |
| 2026-10-05 | Claude (sim-dashboard) | W8.7 | Setup wizard handoff for a failed first mission (F37, F21): full mission id, failure reason, rescue missions from the mission's incidents (rechecked on the poll until they settle), and a View Mission Log action (OpsLogModal). Test `Tui.System.Setup/handoff_explains_failed_mission`. |
| 2026-10-05 | Claude (fx-intermittents) | W8.3 | Approvals center: a deployment approval from a `deployment.changed` event is named like the inbox item (environment, else id), so the row and confirmation no longer change with the source; Tui.EndToEnd.Flows Fleet Actions waits for `LastFlow.SelectedAction()` and `PreviewVessel` before Ctrl+S; new cases Tui.Approvals `deployment_name_agrees_across_sources` and Tui.Ops.FleetActions `run_flow_waits_for_actions`. |
| 2026-10-05 | Claude (sim-server) | W8.7 | F9: login prefill (password, API key) starts selected so typing replaces it (`TextInput.Prefill`); F10 documented as decided (TUI warns, dashboard forces). Correction to the 2026-10-04 tui-ask row: the forced default-password change step it describes was later removed when the default password became flagged rather than enforced; the TUI does not force a change. Mission screen: Why This Mission Is Waiting for Pending missions (server `AssignmentBlocker`), status-aware landing pill (Not Ready Yet, Merge By Hand, Landed), Merge in Manage Branches (`b`) instead of Land for Landing Mode None with the branch preselected in `VesselBranchesDialog`. Tests: Tui.Login (2 new), Tui.Ops.MissionDetail (2 new). |
| 2026-10-05 | Claude (coordinator) | W8.3 | Approvals center: a decision on one Ask proposal was dropped while another proposal's approve call was in flight (one global busy id in `AskController.Decide`); CI windows net10 `approvals_center_decisions` failed at "rejected proposal recorded on the server". Busy state is now per proposal (`IsProposalBusy`). Regression test `Tui.Approvals` `decisions_do_not_block_each_other` holds the approve response and fails on the old code. |
| 2026-10-05 | Claude (tui-focus-hints) | W8.4, W8.7 | Focus-aware status bar hints (owner report: approving in Ask needed an unexplained `Esc`): `ITextEntry` marks text-entry widgets, `IFocusHintSource` lets a container (FilterBar, OpsFilterBar, FormView, ScrollTextView search, the Ask dock) describe its focused field, and `ScreenBase.ResolveHints` combines that with the screen's own keys (`TypingHints` lists the chords that still work while typing; Ask and Workspace resolve per child). Text fields lead with how to leave them, help is `F1` while typing. Ask: pending-approval strip above the composer (three ICU plural keys in all eight catalog locales), `Alt+Down` to the oldest pending card, clickable card buttons, `[typing]` markers on the composer and dock, and the transcript follow model (follows at the bottom with a selection; End, decisions, and sends return to the tail). Approvals center rows have clickable decision buttons. Tests: new Tui.FocusHints (9, including every catalog locale for the new keys), Tui.Approvals `mouse_buttons`; Tui.Display expects `F1 Help` where a text field has focus. TUIKit note: the headless host never composes through TUIKit's renderer, so mouse tests deliver clicks to the shell the way the full-screen region routing would. |
