# Armada mobile: store submission checklist

What the App Store and Google Play review needs from this app, checked against the family list in
`~/Code/Agents/requirements/INSTALLERS.md` ("What review needs from every app"). Private values (reviewer
credentials, App Store Connect app id, Apple team id, Play account details) go only in the git-ignored
`store-private.local.md` next to this file and in the consoles, never here.

## Identity and versions

| Item | Value |
| --- | --- |
| Bundle id and Android package | `ai.latticetech.armada` (permanent once a store record exists) |
| Store name | Armada |
| Version | `1.5.0` in `app.config.ts`, the same as the Armada release; `extra.releaseVersion` is shown in Preferences |
| Build numbers | EAS remote versioning, `autoIncrement` on the `preview` and `production` profiles |
| Export compliance | `usesNonExemptEncryption: false` (HTTPS and platform crypto only) |
| Support contact | the support address from the private store notes (`store-private.local.md`), not committed |

## Review checks

**Real content on first launch.** Armada signs in to an Admiral that the user runs; it has no content of its own.
Reviewers need an always-on demo Admiral with representative fleets, vessels, voyages, missions (including a mixed
open and frontier pipeline with attempts), captains, and model profiles (RS-16.9a). Its address goes in the review
notes; the reviewer account goes in `store-private.local.md` and the consoles.

**Reviewer account.** One demo login on the demo Admiral, with a non-admin role so the reviewer cannot change the
server. Credentials only in `store-private.local.md` and the consoles.

**Account deletion (Apple 5.1.1(v), Google account deletion).** The app has no sign-up: there is no screen that
creates an account for the person using the app. Accounts exist only on the user's own self-hosted Admiral, created
by that server's administrator. An administrator can create and delete users of their own server in the app
(System > Users), which is server administration rather than account sign-up. Suggested review note:

> Armada is a client for a server the user installs and runs themselves (the open-source Armada Admiral). The app
> does not offer account creation; accounts are managed by the operator of each Admiral, who can delete any user in
> the app under System > Users or in the web dashboard. Lattice AI holds no user accounts or data for this app.

Decision for the maintainer before submission: if Apple or Google treat administrator user creation as account
creation, add a "Delete my account" action for the signed-in user (server route plus app action) and a public web
page that explains deletion on a self-hosted Admiral. Google's data deletion form asks for a URL either way; a page
on the project site that explains deletion on a self-hosted Admiral answers it.

**No outside purchase paths (Apple 3.1.1, 3.1.3(b)).** Armada is free, with no purchases, promo codes, pricing, or
links to pay elsewhere. `src/__tests__/storePolicy.test.ts` fails on purchase wording in the app source and on any
purchase, advertising, or tracking package.

**Privacy answers match the binary.** No tracking and no tracking prompt. No analytics, advertising, or crash
reporting SDK. Everything the app sends (sign-in, the push token with platform and device name, app content) goes
only to the Admiral the user configured; Lattice AI receives none of it. App Store privacy answer: Data Not Collected.
Play data safety: no data collected or shared with the developer; data in transit to the user's server uses the
transport the user chose (the app warns on every `http://` server profile).

**Android manifest.** `com.google.android.gms.permission.AD_ID` is in `android.blockedPermissions`, so no library can
merge it in (checked by `src/__tests__/appConfig.test.ts`). Confirm in the merged manifest of the first store build.
Target API level comes from the Expo SDK (57); confirm it meets Google Play's current requirement before the first
upload.

**Permissions and purpose strings.** Face ID (app lock and saved passwords), local network (reaching an Admiral on the
LAN), notifications (asked from a one-time sheet after sign-in, never at launch), biometrics on Android.

## Listing

Store copy is in [store/listing.md](store/listing.md) and the graphics and screenshots in [store/](store/README.md).
