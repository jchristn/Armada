import * as SecureStore from 'expo-secure-store';

/**
 * Secrets in the iOS Keychain / Android Keystore. One session token (or API key) per server profile. Items are
 * readable only after the first unlock of the device and never migrate to another device through backups.
 */

const OPTIONS: SecureStore.SecureStoreOptions = {
  keychainAccessible: SecureStore.AFTER_FIRST_UNLOCK_THIS_DEVICE_ONLY,
};

/** Secure-store key for a profile's token (keys allow only letters, digits, '.', '-', '_'). */
export function tokenKey(profileId: string): string {
  return `armada.token.${profileId.replace(/[^A-Za-z0-9._-]/g, '_')}`;
}

export async function readToken(profileId: string): Promise<string | null> {
  try {
    return await SecureStore.getItemAsync(tokenKey(profileId), OPTIONS);
  } catch {
    return null;
  }
}

export async function writeToken(profileId: string, token: string): Promise<void> {
  await SecureStore.setItemAsync(tokenKey(profileId), token, OPTIONS);
}

export async function deleteToken(profileId: string): Promise<void> {
  try {
    await SecureStore.deleteItemAsync(tokenKey(profileId), OPTIONS);
  } catch {
    // Already absent.
  }
}
