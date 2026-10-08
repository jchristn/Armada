import AsyncStorage from '@react-native-async-storage/async-storage';

/**
 * Non-secret preferences (theme, locale, server profile list, notification history). Secrets never go here:
 * tokens live in the keychain / keystore through storage/secure.ts.
 */

export const PREF_KEYS = {
  theme: 'armada.theme',
  locale: 'armada.locale',
  profiles: 'armada.profiles',
  activeProfile: 'armada.activeProfile',
  notifications: 'armada.notifications',
  passwordChangeSkipped: 'armada.passwordChangeSkipped',
  sidebar: 'armada.sidebar',
} as const;

/** Read and parse a JSON preference; null when missing or unreadable. */
export async function readPref<T>(key: string): Promise<T | null> {
  try {
    const raw = await AsyncStorage.getItem(key);
    return raw === null ? null : (JSON.parse(raw) as T);
  } catch {
    return null;
  }
}

/** Write a JSON preference; failures are swallowed (preferences are best-effort). */
export async function writePref<T>(key: string, value: T): Promise<void> {
  try {
    await AsyncStorage.setItem(key, JSON.stringify(value));
  } catch {
    // Preferences are best-effort; the in-memory value still applies for this session.
  }
}

export async function removePref(key: string): Promise<void> {
  try {
    await AsyncStorage.removeItem(key);
  } catch {
    // ignore
  }
}
