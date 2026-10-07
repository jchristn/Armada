import { useState } from 'react';
import { REDACTED_SECRET, type RemoteControlSettings, type ServerSettings } from '@dashboard/lib/serverSettings';
import type { RepositoryHealthSettings } from '@dashboard/types/models';
import { PUSH_CATEGORIES, type PushCategory } from '../../push/types';

/**
 * Mobile logic of the Server settings tab: section drafts, the payload of each section save, and the redaction
 * rules for secrets. Secrets (remote control enrollment token and password, Push.ExpoAccessToken) come back from
 * GET /api/v1/settings as "********" (or null when unset) and are never shown; sending "********" back keeps the
 * stored value, so a save sends the redacted value unless the user typed a new secret or asked to clear it.
 */

/** Settings as GET /api/v1/settings returns them, with the Push group the dashboard does not edit. */
export type MobileServerSettings = ServerSettings & { push?: Partial<PushSettingsData> | null; repositoryHealth?: Partial<RepositoryHealthSettings> | null };

/** The server's Push settings group (Armada.Core PushSettings, camelCase). */
export interface PushSettingsData {
  enabled: boolean;
  expoAccessToken: string | null;
  categories: PushCategory[];
  maxPerUserPerMinute: number;
  dedupeWindowSeconds: number;
}

export const PUSH_DEFAULTS: PushSettingsData = {
  enabled: true,
  expoAccessToken: null,
  categories: [...PUSH_CATEGORIES],
  maxPerUserPerMinute: 20,
  dedupeWindowSeconds: 300,
};

/** Push settings from the server merged over the defaults (unknown categories dropped). */
export function mergePush(source: Partial<PushSettingsData> | null | undefined): PushSettingsData {
  const categories = Array.isArray(source?.categories)
    ? PUSH_CATEGORIES.filter((c) => (source!.categories as string[]).includes(c))
    : PUSH_DEFAULTS.categories;
  return { ...PUSH_DEFAULTS, ...(source ?? {}), categories };
}

/** What a secret field holds: the server's stored state and what the user typed or chose. */
export interface SecretDraft {
  /** The value GET returned: "********" when a secret is stored, null or '' when not. */
  stored: string | null;
  /** What the user typed ('' when untouched). */
  typed: string;
  /** The user asked to remove the stored secret. */
  clear: boolean;
}

export function secretDraft(stored: string | null | undefined): SecretDraft {
  return { stored: stored ?? null, typed: '', clear: false };
}

/** True when the server reports a stored secret (it is never sent in clear). */
export function hasStoredSecret(stored: string | null | undefined): boolean {
  return !!stored;
}

/**
 * The value a save sends for a secret: the typed value when the user entered one, null to clear it, otherwise the
 * redacted value the server returned (which keeps the stored secret) or null when nothing is stored.
 */
export function secretToSend(draft: SecretDraft): string | null {
  const typed = draft.typed.trim();
  if (typed) return typed;
  if (draft.clear) return null;
  return draft.stored ? REDACTED_SECRET : null;
}

/** A whole number from a text field; blank, not a number, or 0 gives the fallback (the dashboard's parseInt(v) || x). */
export function intOr(value: string, fallback: number): number {
  return parseInt(value, 10) || fallback;
}

/** The remote control payload: the edited settings with each secret resolved by secretToSend. */
export function remoteControlPayload(
  edited: RemoteControlSettings,
  enrollmentToken: SecretDraft,
  password: SecretDraft,
): RemoteControlSettings {
  return { ...edited, enrollmentToken: secretToSend(enrollmentToken), password: secretToSend(password) };
}

/** The Push payload; a cleared or blank token is sent as '' (the server stores blank as no token). */
export function pushPayload(edited: PushSettingsData, token: SecretDraft): PushSettingsData {
  const value = secretToSend(token);
  return { ...edited, expoAccessToken: value ?? '' };
}

/**
 * A section's local draft: shows the server's values until the user edits, then keeps the edits (a refresh or
 * another section's save never overwrites unsaved changes) until they are saved or discarded.
 */
export function useDraft<T>(source: T): { value: T; dirty: boolean; set: (patch: Partial<T>) => void; reset: () => void } {
  const [draft, setDraft] = useState<T | null>(null);
  const value = draft ?? source;
  return {
    value,
    dirty: draft !== null,
    set: (patch) => setDraft({ ...value, ...patch }),
    reset: () => setDraft(null),
  };
}
