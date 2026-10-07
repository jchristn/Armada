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
      // Plain HTTP is allowed only for local networks (unqualified and .local host names); IP address literals
      // are not subject to ATS. Public hosts must use HTTPS. The app warns before saving an http:// profile.
      NSAppTransportSecurity: { NSAllowsLocalNetworking: true },
      NSLocalNetworkUsageDescription: 'Armada connects to your Admiral server on the local network.',
      NSFaceIDUsageDescription: 'Unlock your saved Armada sign-in with Face ID.',
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
    [
      'expo-splash-screen',
      {
        backgroundColor: SPLASH_BACKGROUND,
        image: './assets/images/splash-icon.png',
        imageWidth: 160,
      },
    ],
    'expo-secure-store',
    'expo-localization',
    ['expo-local-authentication', { faceIDPermission: 'Unlock your saved Armada sign-in with Face ID.' }],
    // Declared now so native projects carry the entitlement and channel setup; registration and delivery are W5.3.
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
