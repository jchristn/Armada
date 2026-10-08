// Jest setup: native modules the app uses get in-memory fakes so tests run in Node, deterministically.
require('react-native-gesture-handler/jestSetup');

jest.mock('@react-native-async-storage/async-storage', () =>
  require('@react-native-async-storage/async-storage/jest/async-storage-mock'));

// Secure store: one map of values plus the options each key was written with (tests check requireAuthentication).
jest.mock('expo-secure-store', () => {
  const store = new Map();
  const writeOptions = new Map();
  return {
    AFTER_FIRST_UNLOCK_THIS_DEVICE_ONLY: 'AFTER_FIRST_UNLOCK_THIS_DEVICE_ONLY',
    WHEN_PASSCODE_SET_THIS_DEVICE_ONLY: 'WHEN_PASSCODE_SET_THIS_DEVICE_ONLY',
    getItemAsync: jest.fn(async (key) => (store.has(key) ? store.get(key) : null)),
    setItemAsync: jest.fn(async (key, value, options) => { store.set(key, value); writeOptions.set(key, options ?? {}); }),
    deleteItemAsync: jest.fn(async (key) => { store.delete(key); writeOptions.delete(key); }),
    canUseBiometricAuthentication: jest.fn(() => true),
    __store: store,
    __writeOptions: writeOptions,
  };
});

// Face ID enrolled by default; tests change these to cover other devices.
jest.mock('expo-local-authentication', () => ({
  AuthenticationType: { FINGERPRINT: 1, FACIAL_RECOGNITION: 2, IRIS: 3 },
  hasHardwareAsync: jest.fn(async () => true),
  isEnrolledAsync: jest.fn(async () => true),
  supportedAuthenticationTypesAsync: jest.fn(async () => [2]),
  authenticateAsync: jest.fn(async () => ({ success: true })),
}));

jest.mock('expo-localization', () => ({
  getLocales: () => [{ languageTag: 'en-US' }],
}));

jest.mock('expo-splash-screen', () => ({
  preventAutoHideAsync: jest.fn(async () => undefined),
  hideAsync: jest.fn(async () => undefined),
}));

jest.mock('react-native-safe-area-context', () => require('react-native-safe-area-context/jest/mock').default);

// Reanimated / worklets have no native runtime under Jest; use the library's own mocks.
jest.mock('react-native-worklets', () => require('react-native-worklets/src/mock'));
jest.mock('react-native-reanimated', () => require('react-native-reanimated/mock'));

// expo-notifications has native modules; the push code reaches it through src/push/nativeAdapter.ts, which tests
// replace with a fake. This mock only keeps imports of the real adapter loadable.
jest.mock('expo-notifications', () => ({
  IosAuthorizationStatus: { NOT_DETERMINED: 0, DENIED: 1, AUTHORIZED: 2, PROVISIONAL: 3, EPHEMERAL: 4 },
  AndroidImportance: { HIGH: 4, DEFAULT: 3 },
  DEFAULT_ACTION_IDENTIFIER: 'expo.modules.notifications.actions.DEFAULT',
  getPermissionsAsync: jest.fn(async () => ({ granted: false, status: 'undetermined', canAskAgain: true })),
  requestPermissionsAsync: jest.fn(async () => ({ granted: true, status: 'granted', canAskAgain: true })),
  getExpoPushTokenAsync: jest.fn(async () => ({ type: 'expo', data: 'ExponentPushToken[test]' })),
  setNotificationHandler: jest.fn(),
  setNotificationCategoryAsync: jest.fn(async () => undefined),
  setNotificationChannelAsync: jest.fn(async () => undefined),
  setBadgeCountAsync: jest.fn(async () => true),
  addPushTokenListener: jest.fn(() => ({ remove: jest.fn() })),
  addNotificationResponseReceivedListener: jest.fn(() => ({ remove: jest.fn() })),
  getLastNotificationResponse: jest.fn(() => null),
  clearLastNotificationResponse: jest.fn(),
}));

jest.mock('expo-device', () => ({ deviceName: 'Test iPhone', modelName: 'iPhone', isDevice: false }));
