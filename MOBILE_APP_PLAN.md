# Armada Mobile: iOS, iPadOS, and Android App Plan

> **Type:** implementation plan (work-tracking). Annotate task status and the progress log as you go; keep this
> document in sync with what actually shipped.
>
> **Status:** In progress (W0, W1, W2, W3 and W5 done; W4 in progress; W2 Android verification pending)
> **Built on:** React Native with Expo SDK 57 (TypeScript, Expo Router), in `src/Armada.Mobile`
> **Parity baseline:** the web dashboard at `src/Armada.Dashboard` as of 2026-10-07 (the same surfaces the TUI parity
> manifest tracks: page routes, hub tabs, server-calling API client functions, WebSocket events, Server-page settings)
> **Last updated:** 2026-10-07

Status values: `[ ]` not started, `[~]` in progress, `[x]` done, `[!]` blocked.

## Goal

Native apps for iPhone, iPad, and Android phones and tablets that can do everything the web dashboard can do, so an
operator can run Armada from a phone: talk to a captain in Ask Armada, approve what it proposes, watch the work land,
dispatch against a vessel, browse history, and administer the server. "Parity" is concrete, as for the TUI: every
dashboard route, tab, API function, WebSocket event, and Server setting has a mapped mobile equivalent, and a check
enforces the mapping so the app cannot silently fall behind (see "Parity enforcement").

Where a phone is a better fit than a browser, the app goes further: real push notifications for things that need you
(approvals, CLI permission requests, failed missions and landings, stalled captains), actionable from the notification,
biometric unlock of stored credentials, share-sheet dispatch, and offline-tolerant reading of the last loaded data.

## Decisions (2026-10-07, from the maintainer)

1. **Stack:** React Native + Expo, one TypeScript codebase for iOS, iPadOS, and Android.
2. **Scope:** full dashboard parity.
3. **Connectivity:** direct URL to an Admiral, Armada.Proxy remote access, and real push notifications.
4. **Distribution:** runnable on the iOS simulator and the Android emulator with automated tests and CI; build
   configuration ready for TestFlight and Google Play internal testing. Store accounts, signing credentials, and
   submission stay with the maintainer.

## Design principles

1. **Ask-first, like the TUI.** The app opens into Ask Armada with the last conversation. A persistent Approvals badge
   and the notification inbox are one tap away on every screen.
2. **Adaptive layout.** Phones get a tab bar plus stack navigation; tablets (iPad, Android tablets, foldables) get a
   sidebar with list-detail split views, mirroring the dashboard's navigation groups. Landscape and split-screen work.
3. **Same server, same rules.** A thin client over the existing REST API and WebSocket. Server changes only where
   listed under "Server changes". The app hides or disables what the server would refuse (from `whoami`).
4. **Share, do not fork.** The API client, models, i18n catalog, and the dashboard's pure logic (`src/lib`) are
   shared with the dashboard, not copied (see "Code sharing"). A dashboard change to a shared module is a mobile
   change too, and both test suites run.
5. **Live by default.** Screens subscribe to the WebSocket and update in place; pull-to-refresh and the dashboard's
   auto-refresh intervals are the fallback. The socket reconnects with backoff and on app foreground.
6. **Safe actions.** Destructive actions use the dashboard's confirmations (including typed `delete`); approvals
   always show the exact arguments. Push notification actions that change state require an unlocked app.
7. **Secrets stay in the keychain.** Tokens live in the iOS Keychain / Android Keystore (`expo-secure-store`), never
   in AsyncStorage; optional biometric unlock.
8. **Accessible and localized.** VoiceOver and TalkBack labels, Dynamic Type / font scaling, sufficient contrast in
   light, dark, and high-contrast themes, and every language the dashboard supports (en, es, zh-Hans, zh-Hant,
   yue-Hant, ja, de, fr, it), from the same catalog.

## Architecture

### Projects

| Path | What |
|---|---|
| `src/Armada.Mobile` | Expo app (Expo Router, TypeScript, Hermes). `app.config.ts`, `eas.json`, native config via config plugins (no committed `ios/`/`android/`; `expo prebuild` generates them) |
| `src/Armada.Mobile/src` | screens (`app/` routes), components, mobile services (auth/profiles, secure storage, push, socket lifecycle, theming) |
| `src/Armada.Dashboard/src` (shared modules) | `api/client.ts`, `types/models.ts`, `lib/*` pure logic, i18n helpers, consumed by Metro through `watchFolders` and an alias (`@dashboard/*`) |
| `src/Armada.Server` | push device registration and delivery (see "Server changes") |
| `src/Armada.Proxy` | token-based (cookie-free) proxy session for native clients |
| `scripts/mobile/` | parity manifest generator and check, E2E runner (Maestro) |

### Code sharing

- `api/client.ts` becomes host-agnostic: the base URL and token are set through a small `configureClient()` API (the
  dashboard sets them from `import.meta.env` in its entry point; no `import.meta` in shared modules). Browser-only
  helpers (file download, file upload) move behind an injectable platform adapter so mobile can use the share sheet
  and document picker.
- `lib/armadaSocket.ts` already takes injectable timers; mobile supplies the server URL and token explicitly.
- Shared modules must not import React DOM, `window`, `document`, or `localStorage`; a lint rule and a Jest test in
  the mobile project that imports every shared module enforce it.
- The i18n catalog (`src/Armada.Server/wwwroot/i18n/armada.json`) is bundled into the app at build time and also
  fetched from the connected server (as the dashboard does), so new strings arrive without an app update.

### Connections and sign-in

- **Server profiles** (like the TUI): name, kind (`Direct` or `Proxy`), URL, sign-in method, last tenant and user.
  Multiple profiles, one active. Tokens in secure storage keyed by profile.
- **Direct:** email/tenant/password (with tenant lookup and the "Change the default password" screen with its Skip
  confirmation), or API key / bearer token. HTTPS strongly encouraged; plain HTTP allowed for LAN with a warning
  (iOS ATS and Android cleartext exceptions scoped by config).
- **Proxy:** sign in to the proxy portal, pick a remote instance, then use the relayed REST API and `/ws` with a
  bearer session token instead of the browser cookie (server change P1).
- **Biometric unlock** (optional per profile) before using a stored token.

### Navigation

Mirrors the dashboard's nav groups (`navConfig.tsx`): Ask, Approvals, Notifications, then OPERATIONS, BUILD,
DELIVERY, CONFIGURATION, ACTIVITY, SYSTEM. Phone: bottom tabs (Ask, Approvals, Work, More) with grouped "More".
Tablet: sidebar plus split views. Deep links `armada://` and universal links map to dashboard paths
(`/missions/msn_...`), so a push notification or a pasted dashboard URL opens the same item.

## Screens and parity map

The screen inventory is the dashboard's, the same map as `TUI_APP_PLAN.md` ("Screens and full parity map"): Home,
Ask Armada, Approvals, Notifications, Missions hub, Voyages, Dispatch hub (including dispatch from a vessel), Merge
Queue, Docks, Signals, Vessels hub (including View History), Fleets, Captains hub, Fleet Actions, Planning, Backlog
and refinement, Delivery hub, Configuration hub, Activity hub, CLI Tool Permissions, Server hub (settings, users,
tenants, credentials, backup and restore, rebuild), Setup wizard, and every modal and drawer. Mobile-specific
adaptations (bottom sheets for modals, swipe row actions in place of the dashboard's row menu, long-press for bulk
selection) are recorded per entry in the manifest notes.

## Server changes

- **S1. Push devices:** `POST/GET/DELETE /api/v1/push/devices` (per user: platform, Expo push token, device name,
  app version, enabled categories, last seen), new table `push_devices` (migration on SQLite, PostgreSQL, MySQL, SQL
  Server), ID prefix `pdv_`.
- **S2. Push delivery:** a `PushNotificationService` that sends the inbox-worthy events (Ask proposals and CLI
  permission requests awaiting the user, mission review, deployment approval, mission failed, landing failed, captain
  stalled, voyage finished) to the owner's (and approvers') devices through the Expo Push Service (which relays to
  APNs and FCM), with receipts, pruning of dead tokens, per-user categories, rate limiting, and no secrets or code in
  the payload (title, short text, deep link, entity id). Settings `Push.Enabled`, `Push.ExpoAccessToken` (optional),
  `Push.Categories`. Works through the proxy since delivery is outbound from the Admiral.
- **P1. Proxy bearer sessions:** the proxy's login returns a session token usable as `Authorization: Bearer` (and in
  the `/ws` subprotocol) for relayed requests, so native clients do not depend on cookies.

## Workstreams and tasks

### W0. Foundation
- [x] W0.1 Expo app scaffold (SDK 57, Expo Router, TypeScript strict, Hermes, ESLint), Metro sharing of dashboard
  modules, `configureClient()` refactor in the dashboard (dashboard tests stay green)
- [x] W0.2 Theme (light, dark, high contrast, system), typography with font scaling, shared UI kit (list rows,
  sections, status badges, empty/error/loading states, bottom sheet, confirm dialog, form fields, segmented control,
  search, swipe actions)
- [x] W0.3 i18n (bundled plus server catalog), locale picker
- [x] W0.4 Server profiles, secure storage, sign-in flows (direct password with tenant lookup, API key), password
  change with Skip, sign-out, biometric unlock
- [x] W0.5 Adaptive navigation shell (phone tabs, tablet sidebar and split view), deep links
- [x] W0.6 WebSocket lifecycle (foreground/background, reconnect), notification center (in-app), Approvals badge
- [x] W0.7 Test infrastructure: Jest + React Native Testing Library with the shared API client mocked; Maestro E2E
  flows against a throwaway Admiral on the iOS simulator and the Android emulator
- [x] W0.8 Parity manifest `src/Armada.Mobile/parity.json` and `scripts/mobile/generate-parity-manifest.py --check`
- [x] W0.9 CI: typecheck, lint, Jest, parity check on every push; EAS build profiles (development, preview,
  production) and `eas.json`; store metadata skeleton

### W1. Ask Armada and Approvals
- [x] W1.1 Thread list, conversation, streaming transcript (Markdown, tool chips, thinking), composer with `/` quick
  actions and inline forms, captain picker, auto-approve, summarize
- [x] W1.2 Confirm cards, CLI permission cards, live work cards
- [x] W1.3 Approvals center (Ask proposals, CLI permissions, mission reviews, deployment approvals, failed landings,
  stalled captains)
- [x] W1.4 Notifications inbox and in-app toasts

### W2. Operations
- [x] W2.1 Home (status, KPIs, health, mission history chart, voyage progress, recent missions and signals)
- [x] W2.2 Missions hub and mission detail (diff, log, instructions, landing preview, land, retry, review)
- [x] W2.3 Voyages, voyage create (landing mode), voyage detail
- [x] W2.4 Dispatch hub (dispatch, fleet dispatch, from a vessel), Merge Queue, Docks, Signals, Events

### W3. Build
- [x] W3.1 Vessels hub, vessel detail, vessel form, branches, import wizard, View History (heatmap, timeline, jump
  to date, endless scroll)
- [x] W3.2 Fleets, Captains hub and captain detail (chat, tool access, CLI permission policy), Fleet Actions
- [x] W3.3 Planning sessions, Backlog and objective refinement

### W4. Delivery, Configuration, Activity, System
- [ ] W4.1 Delivery hub (environments, deployments, incidents, runbooks, releases, check runs)
- [ ] W4.2 Configuration hub (personas, pipelines, prompts, playbooks, skills, memories, project and workflow
  profiles, harbors, model endpoints)
- [ ] W4.3 Activity hub (all activity, API requests, events, signals, token usage)
- [ ] W4.4 CLI Tool Permissions, Server hub (settings incl. Default Landing Mode, users, tenants, credentials,
  backup/restore via share sheet and document picker, rebuild), Setup wizard

### W5. Push and remote access
- [x] W5.1 Server S1 and S2 with tests on all four database providers
- [x] W5.2 Proxy P1 with tests (`X-Armada-Proxy-Session` on every route, Bearer on `/proxy-api`, `armada-proxy-session.<b64url>` subprotocol on `/ws`; proxy credentials never relayed to the Admiral; request sequence in docs/REMOTE_SERVER.md)
- [x] W5.3 App: permission prompt, device registration per profile, categories in settings, notification tap and
  action handling (deep link, approve/deny with unlock), badge counts
- [x] W5.4 Proxy profiles in the app (portal sign-in, instance picker)

### W6. Quality and release readiness
- [ ] W6.1 Accessibility pass (VoiceOver, TalkBack, font scaling, contrast)
- [ ] W6.2 Tablet and landscape pass (iPad split view, Stage Manager, Android foldables)
- [ ] W6.3 E2E suite green on both platforms; performance on large lists (virtualized lists, pagination)
- [~] W6.4 Security review (token storage, ATS/cleartext, deep link validation, push payload content)
- [ ] W6.5 Docs: `docs/MOBILE.md` (install, connect, push setup, building and store submission), README, CHANGELOG

## Parity enforcement

`src/Armada.Mobile/parity.json` lists every dashboard surface (generated from the same sources as the TUI manifest:
`App.tsx` routes, hub tabs, server-calling `api/client.ts` exports, WebSocket event types, Server-page settings) with
a status (`implemented`, `planned`, `not-applicable` with a reason, `extension`), the mobile screen, and the
workstream. `scripts/mobile/generate-parity-manifest.py --check` runs in CI and fails when the dashboard gains a
surface without a manifest entry, or (from W6) when any entry is still `planned`.

## Delivery order

W0 and W5.1/W5.2 first (in parallel), then W1, then W2 through W4 in parallel waves, then W5.3/W5.4, then W6.
Milestone A: sign in, Ask Armada, approvals, notifications on both platforms. Milestone B: every route implemented.
Milestone C: push, proxy, quality pass, store-ready builds.

## Progress Log

| Date | Who | Item | Notes |
|---|---|---|---|
| 2026-10-07 | orchestrator | plan | Plan written from the maintainer's decisions (Expo, full parity, direct + proxy + push, store-ready) |
| 2026-10-07 | W5.2 | work/mobile-proxy | Proxy bearer sessions for native clients; proxy session cookie and header no longer enter the tunnel (F-38); sessions stored by SHA-256; Armada.Client ProxySessionToken; 46/46 proxy and 5/5 relay tests |
| 2026-10-07 | W5.1 | work/mobile-push | S1+S2: push_devices (migration 79, 4 providers), /api/v1/push/devices, PushNotificationService over Expo (batched, retry, DeviceNotRegistered cleanup, rate limit, dedupe), Push settings with redacted token; DB parity OK on all four providers. Recipients: failures to owner only, approvals to owner and tenant admins; iOS category armada_approve_deny |
| 2026-10-07 | W0 | work/mobile-w0 | Expo SDK 57 app; dashboard client host-agnostic (configureClient); profiles, sign-in, Skip, biometric; adaptive shell + deep links + placeholders for all 85 routes; socket lifecycle, notifications, Approvals badge; 171 Jest, Maestro 3/3 on iOS and Android; parity.json 552 entries; CI mobile job; iOS scene plugin (remove on SDK 58) |
| 2026-10-07 | W1 | work/mobile-w1 | Ask Armada (threads, streaming, / quick actions and forms, confirm/CLI/work cards, options), approvals center (/approvals, /inbox) with approvals/actions.ts for push, approval toasts, notification filter; shared lib extractions (askFormat, askCaptain, inboxKinds); Hermes timestamp and Android keyboard fixes; Jest 221; Maestro 5/5 iOS + Android; iPad split checked |
| 2026-10-07 | W5.3/W5.4 | work/mobile-push-app | Push: permission sheet after sign-in, per-profile registration (pdv_ kept in keychain; re-register on token change; delete on sign-out/removal), category switches + test push, validated tap deep links, Approve/Deny with unlock + biometric via W1 decideApproval, badge; server adds data.deviceId. Proxy profiles: portal proof login, instance picker, X-Armada-Proxy-Session / Bearer / WS subprotocol with header fallback, expiry resumes with proxy password only. Dashboard configureClient({headers}). Jest 323; Maestro iPhone main+proxy+simctl push, iPad and Armada_Tablet proxy flows |
| 2026-10-07 | W2 | work/mobile-w2 | Home, Missions hub and detail, Voyages and Create Voyage (landing mode), Dispatch, Merge Queue, Docks, Signals, Events; tablet split views; Home/mission/voyage/dispatch logic moved to dashboard lib/; Hermes relative-time fix; 436 Jest, 618 vitest, Maestro 6/6 on iPhone Air with a seeded Admiral, iPad split view checked by hand; parity 129 implemented; Android pending (verify in W6.3) |
| 2026-10-07 | W3 | work/mobile-w3 | Build: Vessels hub (vessels, health, fleets, workspace), vessel page and form with landing mode, branches, import wizard, View History; Captains (chat, tool access, CLI policy, Docks tab embeds W2's list), Fleet Actions; Planning, Backlog and refinement (embedded in Dispatch Backlog tab); shared lib extractions; Jest 543; Maestro 9/9 on iPhone 17 and Armada_Phone; iPad split checked. For W6: iPad portrait list density, Save under the iOS tab bar on long forms, captain create auto-approve default, W4 /releases/new and /delivery?tab=checks must read Backlog/Vessels query params |
| 2026-10-07 | W6.4 | work/mobile-security | Review: 18 findings F-39..F-56 (3 Medium, 10 Low, 5 Info). Fixed: registered-device-only push actions, Approve opens the full request, new device id on owner change, per-user device cap, no proxy cookie for native logins, bounded proxy challenges, privacy cover, Not encrypted mark, scoped notification history, http(s)-only links with host confirmation, safe deep-link ids, mobile npm audit in CI; Jest 589, server suites 92/92, PushDevices on 4 providers. Open: F-41/F-50 (work/mobile-faceid), F-56 (W4) |
