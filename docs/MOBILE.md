# Armada Mobile

Armada for iPhone, iPad, and Android phones and tablets. The app is a client of the Admiral, like the web dashboard:
it talks to the same REST API and WebSocket, shows the same data, and offers the same actions, laid out for touch.
Developer setup, the code layout, and the test tooling are in
[src/Armada.Mobile/README.md](../src/Armada.Mobile/README.md).

- [What the app does](#what-the-app-does)
- [Getting the app](#getting-the-app)
- [Connecting to an Admiral](#connecting-to-an-admiral)
- [Face ID, Touch ID, and fingerprint](#face-id-touch-id-and-fingerprint)
- [Push notifications](#push-notifications)
- [Security](#security)
- [Building and store submission](#building-and-store-submission)
- [Troubleshooting](#troubleshooting)

## What the app does

| Tab (phone) | What is there |
|---|---|
| **Ask** | Ask Armada conversations: streaming replies with thinking and tool steps, `/` quick actions and forms, the captain picker, confirm cards, CLI permission cards, live work cards, and the captain's report when work finishes (tagged "Report") |
| **Approvals** | Everything that needs you, decided in place: Ask proposals, CLI permission requests, mission reviews, deployment approvals, failed landings (retry), failed missions (restart), and stalled captains (stop); recent alerts below |
| **Work** | Home (status, KPIs, health, mission history, voyage progress, recent missions and signals), Missions, Voyages and Create Voyage, Dispatch, Merge Queue, and the Build, Delivery, Configuration, Activity, and Server sections |
| **More** | Server profiles, preferences (theme, language, Face ID unlock and saved password, notifications), the notification center, and sign-out |

On a tablet (768 dp and wider) the tabs become the dashboard's sidebar and lists open their detail side by side.
Links work both ways: `armada://` links and pasted dashboard URLs (`https://admiral.example.com/dashboard/missions/msn_...`)
open the same screen in the app, and every screen can open its page on the web dashboard.

## Getting the app

Until the app is published in the App Store and Google Play, install a build from the person who builds it for
your organization (TestFlight on iOS, an internal-testing link or an APK on Android), or build it yourself (see
[Building and store submission](#building-and-store-submission)).

## Connecting to an Admiral

The first launch asks for a server. A device can keep several server profiles and switch between them in **More >
Servers**; each profile keeps its own sign-in.

### Direct (LAN or a public URL)

1. Enter the Admiral's URL, for example `http://192.168.1.20:7890` or `https://armada.example.com`.
2. Sign in with your email and password (the app looks up your tenant), or with an API key.
3. If the server still uses the default admin password, the app asks you to change it; **Skip for now** is allowed
   after a confirmation.

The Admiral must be reachable from the phone:

- By default the Admiral listens on `localhost` only. Set `rest.hostname` to the machine's LAN address (or `*` for all
  interfaces) in `settings.json`, or put a TLS reverse proxy in front of it (see [OPERATIONS.md](OPERATIONS.md)).
- The Admiral refuses to listen beyond loopback while the default credentials are in use: change the admin password
  first.
- On macOS, allow incoming connections for the Admiral when the firewall asks.
- Plain `http://` works (on a LAN or through a public host name such as a dynamic DNS name), and the app warns
  about it on every such profile: the password and session token travel unencrypted. While you are signed in over
  `http://` a small **Not encrypted** mark stays in the header (in the sidebar on a tablet); tap it for what that
  exposes. Use `https://` for anything that leaves your network.

### Through Armada.Proxy (away from your network)

Choose **Armada.Proxy** when adding a server to reach an Admiral that is not directly reachable:

1. Enter the proxy's URL and its password. The app proves it knows the password with a challenge, so the password
   itself is not sent. Over `http://` that is not enough: someone who can watch the exchange can guess the password
   offline from the challenge and the proof (a fast, unsalted hash), and everything after it (the proxy session, your
   Admiral password and token) travels in plain text anyway. Use an `https://` proxy address.
2. Pick one of the Admirals connected to the proxy.
3. Sign in to that Admiral as usual.

When the proxy session ends (after 24 hours), the app asks only for the proxy password and returns to the same
Admiral. **Change Admiral** and **Sign out of Armada.Proxy** are on the sign-in screen. Setting up the proxy and
connecting an Admiral to it is described in [REMOTE_SERVER.md](REMOTE_SERVER.md).

## Face ID, Touch ID, and fingerprint

Two independent settings per server profile, both in **More > Preferences > Sign-in and security** (for the current
server) and in the server's edit form (**More > Servers**). They appear once Face ID, Touch ID, or a fingerprint is set
up on the device; the labels use the device's own method.

| Setting | What it does |
|---|---|
| **Saved password for Face ID sign-in** | Signs you in with Face ID after you sign out or your session expires, instead of typing the password |
| **Unlock with Face ID** | Locks the app while you stay signed in: asks for Face ID when Armada opens and after 5 minutes in the background (also while the server is unreachable, and before a stored session is resumed after an Armada.Proxy sign-in) |

Saving the password:

- On the password step of sign-in, turn on **Save password and use Face ID** (also offered for the Armada.Proxy
  password). It is saved only after the sign-in succeeds. If you sign in without it, the app asks once, **Use Face ID
  next time?**; **Not now** is remembered for that server.
- Next time, the sign-in screen shows **Sign in with Face ID** and asks right away when it opens (except right after
  you signed out yourself). Cancelling, or a failed scan, leaves the normal password form.
- If the server no longer accepts the saved password (it was changed elsewhere), the app removes it and asks for the
  password. Changing the default password in the app updates the saved one.
- Adding a face or fingerprint on the device makes the saved password unreadable (by design); the app then forgets it
  and asks for the password once.
- Sign-out keeps the saved password; the sign-out confirmation offers **Sign out and forget saved password**.
  **Forget saved password** is also in Preferences and the server form. Deleting a server, or changing its address or
  connection kind, deletes it.

## Push notifications

The Admiral can notify your phone about the things that need a person, the same items the Approvals tab shows:

| Category | When | Who gets it |
|---|---|---|
| Ask proposal | An Ask proposal is waiting for approval | The conversation owner |
| CLI permission | A captain asks to use a tool | The owner and the tenant's admins |
| Mission review | A mission is waiting for review | The owner and the tenant's admins |
| Deployment approval | A deployment is waiting for approval | The owner and the tenant's admins |
| Mission failed, landing failed, captain stalled, voyage finished | As named | The owner (the tenant's admins when there is no active owner) |

- The app asks for permission once after sign-in (or from **More > Preferences**), never at launch.
- Each signed-in server registers the device separately. Preferences shows whether this device receives
  notifications from the current server, a switch for each category, and **Send a test notification**.
- Tapping a notification opens the matching screen. Ask proposals and CLI permission requests carry **Approve** and
  **Deny** buttons, which require an unlocked device. **Deny** is sent at once (after Face ID, Touch ID, or a
  fingerprint when biometric unlock is on for the profile). **Approve** never decides from the notification, whose
  text is only a short summary (a long command is cut): it opens the app to the full request (the whole command or
  the proposal's arguments, who asked, where) with the Approvals center's controls, and you approve there.
- Only a notification that names a device this app registered with that server can approve or deny anything; any
  other notification just opens its (validated) link.
- The app icon badge is your number of pending approvals.
- Notification text is short (titles up to 64 characters, bodies up to 178) and passes the Admiral's secret
  redactor, but it is not content-free: it names missions, voyages, captains, and tools, and a CLI permission
  request includes the start of the command (at most 60 characters). It travels through the Expo Push Service and
  Apple or Google and shows on the lock screen; to keep it off the lock screen, set notification previews to "When
  Unlocked" (iOS) or hide sensitive content on the lock screen (Android). It never contains diffs, failure details,
  or full tool input.

Notifications are delivered through the Expo Push Service, which relays to Apple (APNs) and Google (FCM), so they
also work when the app reaches the Admiral through Armada.Proxy. The Admiral needs outbound HTTPS access to
`exp.host`.

### Admiral settings

Push is on by default. The `push` section of `settings.json` (also in the dashboard under **Server > Settings** and
in `GET`/`PUT /api/v1/settings`):

| Setting | Default | Meaning |
|---|---|---|
| `enabled` | `true` | Send push notifications at all |
| `expoAccessToken` | none | Only needed when "enhanced push security" is turned on for the Expo project; redacted on reads |
| `categories` | all | The categories a newly registered device receives |
| `maxPerUserPerMinute` | `20` | Per-user rate limit |
| `dedupeWindowSeconds` | `300` | Identical notifications within the window are sent once |
| `maxDevicesPerUser` | `10` | Active devices per user; registering one more deactivates that user's least recently seen device |

For production, turn on **enhanced push security** for the Expo project and set `expoAccessToken`: without it,
anyone who learns a device's Expo push token can send that device notifications that look like the Admiral's (the
app never acts on one that does not name a device it registered, but it can still be shown). A phone's push token
belongs to one user at a time: when another account registers the same phone, the previous registration is deleted
and the new one gets a new device id.

The device API (`/api/v1/push/devices`) and the payload format are documented in
[REST_API.md](REST_API.md#push-notifications).

## Security

- Admiral and proxy session tokens are kept in the platform's secure storage (iOS Keychain, Android Keystore), never
  in plain preferences. Biometric unlock can be required per profile.
- A saved password (email, tenant, and password, per profile) is stored only in a Keychain item bound to the device's
  current biometric set (this device only, never backed up, removed if the device passcode is removed) or, on Android,
  encrypted with a Keystore key that requires biometric authentication for each use. Reading it needs Face ID,
  Touch ID, or a fingerprint; the device passcode is not accepted. It is never written to preferences or logs.
- Notification taps and deep links are validated before they navigate: only known app routes and Armada-shaped
  ids are accepted, and an Approve or Deny on a notification is only sent for a device this app registered with
  that server.
- Signing out removes this device's push registration from the server. If the server cannot be reached then (or
  the session had already expired), the device is remembered as retired: its notifications are no longer shown while
  the app is open or acted on, and the removal is retried the next time you sign in to that server. Notifications
  delivered while the app is closed can still appear until then.
- Push device tokens are masked in every API response; a device is managed by its owner, the tenant's admins, and
  global admins, and deleting a user or tenant deletes its devices.
- Admin-only screens follow the same role rules as the dashboard; secrets such as stored credentials and
  `expoAccessToken` are never shown in full.
- Biometric unlock is an app lock, not hardware-bound encryption of the session: session tokens are stored with
  "after first unlock, this device only" protection (so push cleanup can run in the background), and the app asks
  for Face ID, Touch ID, or a fingerprint before it uses them. On a jailbroken, rooted, or instrumented device the
  stored tokens can be read without it.
- While the app is not in the foreground it is covered by a plain screen, so the app switcher (iOS) and Recents
  (Android) show no conversation, code, or log. On Android the Recents thumbnail can be taken before the cover is
  drawn; the app does not set FLAG_SECURE, because that would also block your own screenshots and screen sharing.
- The notification center's history is kept per server profile and user, is never shown to another user or for
  another server, and is deleted when you sign out or remove the profile.
- Links from the server (pull requests, advisories, an objective's source link, links in captain replies) open
  outside the app only when they are `http://` or `https://` (no other app schemes, no `user@host` addresses); links in
  captain replies and objective source links first show the destination host and ask before leaving the app.
- Armada.Proxy sessions are kept only in secure storage: the app logs in to the proxy without a cookie and never
  sends or stores cookies for the proxy.

## Building and store submission

Builds are made with Expo Application Services (EAS) from `src/Armada.Mobile`. `eas.json` has three profiles:
`development` (simulator builds and an Android APK), `preview` (internal distribution: ad hoc iOS installs on
registered devices and APKs), and `production` (App Store and Google Play).

One-time setup by the maintainer who publishes the app:

1. **App identity.** Choose a bundle id you own (for example `com.example.armada`) and set
   `ARMADA_MOBILE_BUNDLE_ID`, or replace the placeholder `com.armada.mobile` in `app.config.ts`.
2. **Expo project.** Run `npx eas-cli login` and `npx eas-cli init` in `src/Armada.Mobile`, then set
   `ARMADA_MOBILE_EAS_PROJECT_ID` (or put the id in `app.config.ts`). Without a project id the app works but cannot
   receive push notifications.
3. **Apple.** An Apple Developer Program membership, an App Store Connect app record for the bundle id, and an APNs
   auth key (`.p8`). Attach the key with `npx eas-cli credentials` (iOS, Push Notifications). Fill in the
   `submit.production.ios` placeholders in `eas.json` (Apple ID, App Store Connect app id, team id).
4. **Google.** A Firebase project with an Android app for the bundle id: add its `google-services.json` and set
   `android.googleServicesFile` in `app.config.ts`, and upload the FCM V1 service account key with
   `npx eas-cli credentials` (Android, FCM V1). For Play submission, a Play Console app and a service account key at
   `src/Armada.Mobile/secrets/google-play-service-account.json` (ignored by git).

Then:

```bash
cd src/Armada.Mobile
npx eas-cli build --profile preview --platform all      # internal builds for testers
npx eas-cli build --profile production --platform all   # store builds
npx eas-cli submit --profile production --platform ios      # to App Store Connect / TestFlight
npx eas-cli submit --profile production --platform android  # to the Play internal track
```

To run the app on your own phone from a Mac without EAS: connect it with USB, turn on Developer Mode (iOS) or USB
debugging (Android), and run `npx expo run:ios --device` or `npx expo run:android --device`. On iOS this needs an
Apple ID signed in to Xcode (**Settings > Accounts**); with a free Apple ID the build expires after 7 days and push
notifications are not available.

## Troubleshooting

| Symptom | Check |
|---|---|
| "Server unreachable" | The phone and the Admiral are on the same network, `rest.hostname` is not `localhost`, the firewall allows the port, and the URL includes the port |
| The Admiral will not start on a LAN address | Change the default admin password first (or set `AllowDefaultCredentialsOnNetwork`, not recommended) |
| No notifications | Preferences says the device receives notifications from this server; the category switch is on; iOS or Android notification permission is granted; `push.enabled` is true; the Admiral can reach `exp.host`; the build has an EAS project id |
| Approve or Deny on a notification does nothing | Unlock the device; complete the biometric prompt; the notification must come from a server this app is signed in to. Approve opens the request for you to approve in the app; if the request was already decided the app says so |
| Asked for the proxy password again | The 24-hour proxy session ended; the Admiral sign-in is kept |
| **Sign in with Face ID** is gone and the app asks for the password | A face or fingerprint was added on the device, or the server rejected the saved password; sign in once with the switch on to save it again |
| No **Save password and use Face ID** switch | Set up Face ID, Touch ID, or a strong (class 3) fingerprint on the device; Android face unlock that is not class 3 cannot protect a keystore key |
| An Ask reply says the CLI "is not installed on the Admiral host" | The Admiral (for example a Docker image) has no agent CLI. Connect a Harbor on a machine where the CLI and its login live: Ask turns then run there (see [CAPTAINS.md](CAPTAINS.md#where-interactive-turns-run)) |
| An Ask reply says "No Harbor is connected to run this captain" | `requireHarborForLaunch` is on and none of your Harbors that advertises the captain's runtime is connected. Start your Harbor, or check that its `AccessKey` is one of your credentials |
