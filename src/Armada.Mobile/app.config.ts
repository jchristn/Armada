import type { ConfigContext, ExpoConfig } from 'expo/config';

// ---------------------------------------------------------------------------------------------------------------
// MAINTAINER: replace these placeholders before the first store build.
//   - BUNDLE_ID: the iOS bundle identifier and Android application id. com.armada.mobile is a placeholder and is
//     almost certainly not available on the App Store or Google Play; use a reverse-DNS id you own.
//   - EAS_PROJECT_ID: from `npx eas-cli init` (leave undefined until then).
//   - APPLE_TEAM_ID is set in eas.json / EAS credentials, not here.
// ---------------------------------------------------------------------------------------------------------------
const BUNDLE_ID = process.env.ARMADA_MOBILE_BUNDLE_ID ?? 'com.armada.mobile';
const EAS_PROJECT_ID: string | undefined = process.env.ARMADA_MOBILE_EAS_PROJECT_ID;

// Kept in step with the Armada release (no independent app versioning).
const VERSION = '1.0.0';
const SPLASH_BACKGROUND = '#111827';
// One Face ID purpose string for the app lock (expo-local-authentication) and saved passwords (expo-secure-store).
const FACE_ID_USAGE = 'Armada uses Face ID to unlock the app and to sign in with the password you saved on this device.';

export default ({ config }: ConfigContext): ExpoConfig => ({
  ...config,
  name: 'Armada',
  slug: 'armada-mobile',
  platforms: ['ios', 'android'],
  version: VERSION,
  scheme: 'armada',
  orientation: 'default',
  icon: './assets/images/icon.png',
  userInterfaceStyle: 'automatic',
  ios: {
    bundleIdentifier: BUNDLE_ID,
    supportsTablet: true,
    buildNumber: '1',
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
    versionCode: 1,
    adaptiveIcon: {
      backgroundColor: SPLASH_BACKGROUND,
      foregroundImage: './assets/images/adaptive-foreground.png',
      monochromeImage: './assets/images/adaptive-monochrome.png',
    },
    predictiveBackGestureEnabled: false,
    permissions: ['USE_BIOMETRIC', 'USE_FINGERPRINT'],
  },
  plugins: [
    'expo-router',
    './plugins/withSceneLifecycle',
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
    eas: EAS_PROJECT_ID ? { projectId: EAS_PROJECT_ID } : undefined,
  },
});
