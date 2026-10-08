import * as SecureStore from 'expo-secure-store';

/**
 * Passwords saved for Face ID / Touch ID / fingerprint sign-in, one item per profile (plus one for a Proxy profile's
 * Armada.Proxy password). Each item is stored with `requireAuthentication`: on iOS a Keychain item whose access
 * control is bound to the current biometric set (`biometryCurrentSet`, this device only, deleted if the passcode is
 * removed); on Android a value encrypted with a Keystore key that requires user authentication for every use. Reading
 * makes the OS show its biometric prompt. Enrolling a new face or fingerprint invalidates the item, which then reads as
 * absent.
 *
 * The password never goes anywhere else: not into AsyncStorage, the profile list, logs, or a plain secure-store item.
 * Profiles only remember, in non-secret form, which account is saved (ServerProfile.savedSignIn).
 */

/** The account and password saved for an Admiral sign-in. */
export interface SavedAdmiralCredentials {
  email: string;
  tenantId: string;
  tenantName: string | null;
  password: string;
}

/** The Armada.Proxy password saved for a Proxy profile. */
export interface SavedProxyCredentials {
  password: string;
}

/**
 * The outcome of reading a saved password: `ok` with the credentials; `none` when nothing is saved or the item was
 * invalidated (biometrics changed); `failed` when the biometric prompt was cancelled or did not succeed.
 */
export type SavedCredentialRead<T> =
  | { status: 'ok'; credentials: T }
  | { status: 'none' }
  | { status: 'failed' };

/** Which saved password: the Admiral account, or a Proxy profile's Armada.Proxy password. */
export type SavedCredentialKind = 'admiral' | 'proxy';

/** Kept apart from the session tokens' service, as expo-secure-store requires for authenticated items. */
export const SAVED_CREDENTIALS_SERVICE = 'armada.signin';

function safeId(profileId: string): string {
  return profileId.replace(/[^A-Za-z0-9._-]/g, '_');
}

/** Secure-store key of a profile's saved password (keys allow only letters, digits, '.', '-', '_'). */
export function savedCredentialsKey(profileId: string, kind: SavedCredentialKind): string {
  return kind === 'proxy' ? `armada.signin.proxy.${safeId(profileId)}` : `armada.signin.admiral.${safeId(profileId)}`;
}

function options(prompt?: string): SecureStore.SecureStoreOptions {
  return {
    keychainService: SAVED_CREDENTIALS_SERVICE,
    requireAuthentication: true,
    keychainAccessible: SecureStore.WHEN_PASSCODE_SET_THIS_DEVICE_ONLY,
    authenticationPrompt: prompt,
  };
}

/** Whether this device can store an item that only biometrics can read. */
export function canSaveCredentials(): boolean {
  try {
    return SecureStore.canUseBiometricAuthentication();
  } catch {
    return false;
  }
}

function isAdmiralCredentials(value: unknown): value is SavedAdmiralCredentials {
  if (!value || typeof value !== 'object') return false;
  const v = value as Record<string, unknown>;
  return typeof v.email === 'string' && v.email.length > 0
    && typeof v.tenantId === 'string' && v.tenantId.length > 0
    && (v.tenantName === null || typeof v.tenantName === 'string')
    && typeof v.password === 'string' && v.password.length > 0;
}

function isProxyCredentials(value: unknown): value is SavedProxyCredentials {
  if (!value || typeof value !== 'object') return false;
  const v = value as Record<string, unknown>;
  return typeof v.password === 'string' && v.password.length > 0;
}

async function deleteItem(key: string): Promise<void> {
  try {
    await SecureStore.deleteItemAsync(key, { keychainService: SAVED_CREDENTIALS_SERVICE });
  } catch {
    // Already absent.
  }
}

/**
 * Store a password behind biometrics, replacing any previous one. The old item is deleted first, so iOS adds a new
 * item (no prompt) instead of updating the protected one (which would prompt); Android prompts to encrypt.
 * Resolves false when the item could not be stored (no biometrics, or the Android prompt was cancelled).
 */
async function writeItem(key: string, value: SavedAdmiralCredentials | SavedProxyCredentials, prompt?: string): Promise<boolean> {
  await deleteItem(key);
  try {
    await SecureStore.setItemAsync(key, JSON.stringify(value), options(prompt));
    return true;
  } catch {
    await deleteItem(key);
    return false;
  }
}

async function readItem<T>(key: string, prompt: string, valid: (value: unknown) => value is T): Promise<SavedCredentialRead<T>> {
  let raw: string | null;
  try {
    raw = await SecureStore.getItemAsync(key, options(prompt));
  } catch {
    // Cancelled, failed, or locked out: the item stays for the next attempt.
    return { status: 'failed' };
  }
  if (raw === null) return { status: 'none' };
  let parsed: unknown = null;
  try {
    parsed = JSON.parse(raw);
  } catch {
    parsed = null;
  }
  if (!valid(parsed)) {
    await deleteItem(key);
    return { status: 'none' };
  }
  return { status: 'ok', credentials: parsed };
}

export function saveAdmiralCredentials(profileId: string, credentials: SavedAdmiralCredentials, prompt?: string): Promise<boolean> {
  const value: SavedAdmiralCredentials = {
    email: credentials.email,
    tenantId: credentials.tenantId,
    tenantName: credentials.tenantName,
    password: credentials.password,
  };
  return writeItem(savedCredentialsKey(profileId, 'admiral'), value, prompt);
}

export function saveProxyCredentials(profileId: string, credentials: SavedProxyCredentials, prompt?: string): Promise<boolean> {
  return writeItem(savedCredentialsKey(profileId, 'proxy'), { password: credentials.password }, prompt);
}

/** Read the saved Admiral password; the OS shows the biometric prompt with `prompt`. */
export function readAdmiralCredentials(profileId: string, prompt: string): Promise<SavedCredentialRead<SavedAdmiralCredentials>> {
  return readItem(savedCredentialsKey(profileId, 'admiral'), prompt, isAdmiralCredentials);
}

/** Read the saved Armada.Proxy password; the OS shows the biometric prompt with `prompt`. */
export function readProxyCredentials(profileId: string, prompt: string): Promise<SavedCredentialRead<SavedProxyCredentials>> {
  return readItem(savedCredentialsKey(profileId, 'proxy'), prompt, isProxyCredentials);
}

/** Delete one saved password of a profile (no prompt). */
export function deleteSavedCredential(profileId: string, kind: SavedCredentialKind): Promise<void> {
  return deleteItem(savedCredentialsKey(profileId, kind));
}

/** Delete every saved password of a profile (no prompt). */
export async function deleteSavedCredentials(profileId: string): Promise<void> {
  await deleteItem(savedCredentialsKey(profileId, 'admiral'));
  await deleteItem(savedCredentialsKey(profileId, 'proxy'));
}
