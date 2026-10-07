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
