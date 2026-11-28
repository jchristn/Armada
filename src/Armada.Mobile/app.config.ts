import { existsSync } from 'node:fs';
import { join } from 'node:path';
import type { ConfigContext, ExpoConfig } from 'expo/config';
import { withEntitlementsPlist, type ConfigPlugin } from 'expo/config-plugins';

// ---------------------------------------------------------------------------------------------------------------
// Store identity and build settings. Private values (Apple team, App Store Connect app id, Expo owner, reviewer
// credentials) never go in this repository: set them as environment variables or EAS variables, and keep notes in
// the git-ignored store-private.local.md next to this file.
//   - BUNDLE_ID: ai.latticetech.armada, the iOS bundle identifier and Android application id. Permanent once a store
//     record exists. ARMADA_MOBILE_BUNDLE_ID overrides it for testing only.
//   - EAS_OWNER / EAS_PROJECT_ID: the Expo account and project from `npx eas-cli init`, set with
//     ARMADA_MOBILE_EAS_OWNER and ARMADA_MOBILE_EAS_PROJECT_ID (EAS variables for cloud builds). Without a project id
//     the app works but cannot receive push notifications.
//   - GOOGLE_SERVICES_JSON: Firebase config for Android push. EAS builds read it from the EAS file variable
//     GOOGLE_SERVICES_JSON; local builds use ./google-services.json when present (git-ignored).
// ---------------------------------------------------------------------------------------------------------------
const BUNDLE_ID = process.env.ARMADA_MOBILE_BUNDLE_ID || 'ai.latticetech.armada';
const EAS_OWNER: string | undefined = process.env.ARMADA_MOBILE_EAS_OWNER || undefined;
const EAS_PROJECT_ID: string | undefined = process.env.ARMADA_MOBILE_EAS_PROJECT_ID || undefined;
const LOCAL_GOOGLE_SERVICES = join(__dirname, 'google-services.json');
const GOOGLE_SERVICES_JSON: string | undefined =
  process.env.GOOGLE_SERVICES_JSON || (existsSync(LOCAL_GOOGLE_SERVICES) ? './google-services.json' : undefined);

// development, preview, or production; set per build profile in eas.json. Local runs are development.
const APP_VARIANT = process.env.APP_VARIANT || 'development';

// On-device testing with a free personal Apple team: set ARMADA_MOBILE_LOCAL_TEAM_ID to the personal team id. The
// build then uses the bundle id <BUNDLE_ID>.dev (so the personal team never registers the store id) and drops the
// push and associated-domains entitlements, which personal teams cannot sign. Never used for store builds.
const LOCAL_TEAM_ID: string | undefined = process.env.ARMADA_MOBILE_LOCAL_TEAM_ID || undefined;
const IOS_BUNDLE_ID = LOCAL_TEAM_ID ? BUNDLE_ID + '.dev' : BUNDLE_ID;
const withoutPersonalTeamEntitlements: ConfigPlugin = (cfg) =>
  withEntitlementsPlist(cfg, (mod) => {
    delete mod.modResults['aps-environment'];
    delete mod.modResults['com.apple.developer.associated-domains'];
    return mod;
  });

// Kept in step with the Armada release (no independent app versioning). The stores accept only numeric versions, so
// STORE_VERSION is major.minor.patch and any pre-release label stays in RELEASE_VERSION (extra.releaseVersion). Build
// numbers come from EAS remote versioning (eas.json appVersionSource remote, autoIncrement on store profiles).
const RELEASE_VERSION = '1.5.0';
const STORE_VERSION = '1.5.0';
const SPLASH_BACKGROUND = '#111827';
// One Face ID purpose string for the app lock (expo-local-authentication) and saved passwords (expo-secure-store).
const FACE_ID_USAGE = 'Armada uses Face ID to unlock the app and to sign in with the password you saved on this device.';

const buildConfig = ({ config }: ConfigContext): ExpoConfig => ({
  ...config,
  name: 'Armada',
  slug: 'armada-mobile',
  ...(EAS_OWNER ? { owner: EAS_OWNER } : null),
  platforms: ['ios', 'android'],
  version: STORE_VERSION,
  scheme: 'armada',
  orientation: 'default',
  icon: './assets/images/icon.png',
  userInterfaceStyle: 'automatic',
  ios: {
    bundleIdentifier: IOS_BUNDLE_ID,
    ...(LOCAL_TEAM_ID ? { appleTeamId: LOCAL_TEAM_ID } : null),
    supportsTablet: true,
    config: { usesNonExemptEncryption: false },
    infoPlist: {
      // The Admiral is self-hosted at an address the user types, often plain HTTP on a home or office network
      // reached by a public host name (dynamic DNS), so ATS cannot be scoped to known domains. Plain HTTP is
      // allowed (as on Android, where cleartext is allowed app-wide); the app warns on every http:// profile and
      // docs/MOBILE.md recommends https:// for anything that leaves the network. NSAllowsArbitraryLoads must be the
      // only ATS key: iOS ignores it when NSAllowsLocalNetworking (or another NSAllows* exception) is also present.
      NSAppTransportSecurity: { NSAllowsArbitraryLoads: true },
      NSLocalNetworkUsageDescription: 'Armada connects to your Admiral server on the local network.',
      NSFaceIDUsageDescription: FACE_ID_USAGE,
    },
  },
  android: {
    package: BUNDLE_ID,
    ...(GOOGLE_SERVICES_JSON ? { googleServicesFile: GOOGLE_SERVICES_JSON } : null),
    adaptiveIcon: {
      backgroundColor: SPLASH_BACKGROUND,
      foregroundImage: './assets/images/adaptive-foreground.png',
      monochromeImage: './assets/images/adaptive-monochrome.png',
    },
    predictiveBackGestureEnabled: false,
    permissions: ['USE_BIOMETRIC', 'USE_FINGERPRINT'],
    // The app shows no ads and does no tracking; keep the advertising id permission that Firebase libraries can
    // merge in out of the manifest (Play asks about it otherwise).
    blockedPermissions: ['com.google.android.gms.permission.AD_ID'],
  },
  plugins: [
    'expo-router',
    './plugins/withSceneLifecycle',
    // Recents shows no app content on Android 13+ (PrivacyOverlay draws too late there); screenshots still work.
    './plugins/withAndroidRecentsPrivacy',
    [
      'expo-splash-screen',
      {
        backgroundColor: SPLASH_BACKGROUND,
        image: './assets/images/splash-icon.png',
        imageWidth: 160,
      },
    ],
    // Saved passwords use requireAuthentication: a Keychain item bound to the current biometric set on iOS (needs
    // NSFaceIDUsageDescription), an auth-bound Keystore key on Android (USE_BIOMETRIC above). Android Auto Backup
    // excludes the secure store, so nothing protected is restored onto another device.
    ['expo-secure-store', { faceIDPermission: FACE_ID_USAGE }],
    'expo-localization',
    ['expo-local-authentication', { faceIDPermission: FACE_ID_USAGE }],
    // Push entitlement (aps-environment) and the Android notification setup. Expo push tokens need the EAS project
    // id above; without it the app runs normally and Preferences explains that push is not configured.
    ['expo-notifications', { color: '#2563eb' }],
    [
      'expo-build-properties',
      {
        // Android blocks cleartext HTTP by default and cannot scope an exception to arbitrary LAN addresses, so
        // cleartext is allowed app-wide; the app warns on every http:// server profile.
        android: { usesCleartextTraffic: true },
      },
    ],
  ],
  experiments: {
    reactCompiler: true,
    // metro.config.js resolves the @/, @dashboard/, and @armada-i18n/ aliases itself.
    tsconfigPaths: false,
  },
  extra: {
    releaseVersion: RELEASE_VERSION,
    appVariant: APP_VARIANT,
    eas: EAS_PROJECT_ID ? { projectId: EAS_PROJECT_ID } : undefined,
  },
});

export default (context: ConfigContext): ExpoConfig => {
  const built = buildConfig(context);
  return LOCAL_TEAM_ID ? withoutPersonalTeamEntitlements(built) : built;
};
