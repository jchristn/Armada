/**
 * A saved Admiral connection (like the TUI's server profiles). Non-secret fields are stored with the preferences;
 * the token lives in secure storage keyed by `id`.
 */
export interface ServerProfile {
  id: string;
  name: string;
  /** `Direct` talks to an Admiral URL; `Proxy` (W5.4) signs in to an Armada.Proxy portal and picks an instance. */
  kind: ServerProfileKind;
  /** Admiral origin, e.g. https://armada.example.com or http://192.168.1.20:7890 (no trailing slash). */
  url: string;
  /** How the stored token was obtained. */
  signInMethod: SignInMethod;
  /** Ask for Face ID / Touch ID / fingerprint before using the stored token. */
  biometricUnlock: boolean;
  lastEmail: string | null;
  lastTenantId: string | null;
  lastTenantName: string | null;
  lastUserEmail: string | null;
  createdUtc: string;
  /**
   * Proxy profiles: the Admiral instance picked on the proxy (re-selected after the proxy session is renewed, so a
   * re-sign-in returns to the same Admiral). Absent on profiles saved before W5.4.
   */
  proxyInstanceId?: string | null;
  /**
   * The account whose password is saved for Face ID / Touch ID / fingerprint sign-in, or null/absent when none is.
   * Only this non-secret description lives here; the password itself is in the keychain / keystore behind biometrics
   * (auth/savedCredentials.ts).
   */
  savedSignIn?: SavedSignInInfo | null;
  /** Proxy profiles: the Armada.Proxy password is saved for biometric sign-in (the password is in the keychain). */
  proxyPasswordSaved?: boolean;
  /** The user answered "Not now" to the offer to save the password for biometric sign-in; it is not offered again. */
  savePasswordOfferDeclined?: boolean;
}

/** Who a saved password signs in as (non-secret; the password is stored separately behind biometrics). */
export interface SavedSignInInfo {
  email: string;
  tenantId: string;
  tenantName: string | null;
}

export type ServerProfileKind = 'Direct' | 'Proxy';

export type SignInMethod = 'password' | 'token';

/** The editable fields of a profile. */
export interface ServerProfileDraft {
  name: string;
  url: string;
  kind: ServerProfileKind;
  biometricUnlock: boolean;
}
