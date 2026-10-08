# Armada Mobile

Armada for iPhone, iPad, and Android phones and tablets. The app is a client of the Admiral, like the web dashboard:
it talks to the same REST API and WebSocket, shows the same data, and offers the same actions, laid out for touch.
Developer setup, the code layout, and the test tooling are in
[src/Armada.Mobile/README.md](../src/Armada.Mobile/README.md).

- [What the app does](#what-the-app-does)
- [Getting the app](#getting-the-app)
- [Connecting to an Admiral](#connecting-to-an-admiral)
- [Push notifications](#push-notifications)
- [Security](#security)
- [Building and store submission](#building-and-store-submission)
- [Troubleshooting](#troubleshooting)

## What the app does

| Tab (phone) | What is there |
|---|---|
| **Ask** | Ask Armada conversations: streaming replies with thinking and tool steps, `/` quick actions and forms, the captain picker, confirm cards, CLI permission cards, and live work cards |
| **Approvals** | Everything that needs you, decided in place: Ask proposals, CLI permission requests, mission reviews, deployment approvals, failed landings (retry), failed missions (restart), and stalled captains (stop); recent alerts below |
| **Work** | Home (status, KPIs, health, mission history, voyage progress, recent missions and signals), Missions, Voyages and Create Voyage, Dispatch, Merge Queue, and the Build, Delivery, Configuration, Activity, and Server sections |
| **More** | Server profiles, preferences (theme, language, biometric unlock, notifications), the notification center, and sign-out |

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
  about it on every such profile: the password and session token travel unencrypted. Use `https://` for anything
  that leaves your network.

### Through Armada.Proxy (away from your network)

Choose **Armada.Proxy** when adding a server to reach an Admiral that is not directly reachable:

1. Enter the proxy's URL and its password. The app proves it knows the password with a challenge; the password
   itself is never sent.
2. Pick one of the Admirals connected to the proxy.
3. Sign in to that Admiral as usual.

When the proxy session ends (after 24 hours), the app asks only for the proxy password and returns to the same
Admiral. **Change Admiral** and **Sign out of Armada.Proxy** are on the sign-in screen. Setting up the proxy and
connecting an Admiral to it is described in [REMOTE_SERVER.md](REMOTE_SERVER.md).

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
  **Deny** buttons; they require an unlocked device and, when biometric unlock is on for the profile, Face ID, Touch
  ID, or a fingerprint before the decision is sent.
- The app icon badge is your number of pending approvals.
- Notification text is short and never contains code, diffs, failure details, or secrets.

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

The device API (`/api/v1/push/devices`) and the payload format are documented in
[REST_API.md](REST_API.md#push-notifications).

## Security

- Admiral and proxy session tokens are kept in the platform's secure storage (iOS Keychain, Android Keystore), never
  in plain preferences. Biometric unlock can be required per profile.
- Notification taps and deep links are validated before they navigate: only known app routes and Armada-shaped
  ids are accepted, and an Approve or Deny on a notification is only sent for a device this app registered with
  that server.
- Push device tokens are masked in every API response; a device is managed by its owner, the tenant's admins, and
  global admins, and deleting a user or tenant deletes its devices.
- Admin-only screens follow the same role rules as the dashboard; secrets such as stored credentials and
  `expoAccessToken` are never shown in full.

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
| Approve or Deny on a notification does nothing | Unlock the device; complete the biometric prompt; the notification must come from a server this app is signed in to |
| Asked for the proxy password again | The 24-hour proxy session ended; the Admiral sign-in is kept |
