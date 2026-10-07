import { ApiError } from '@dashboard/api/client';
import type { ServerSession } from '../api/serverSession';
import type { PushApi } from './pushApi';
import type { RegistrationStore } from './registrationStore';
import type { PushCategory, PushPlatform, PushRegistrationRecord } from './types';

/**
 * The push registration lifecycle, per server profile, free of React and native modules so it can be tested:
 *
 * - register on sign-in (and on every return to a signed-in profile): POST the Expo push token; the server answers
 *   with the device (`pdv_`), whose id is kept because the server masks the token in every response;
 * - re-register when the Expo token changes, and remove the stale device the old token left behind;
 * - remove the device on sign-out and when the profile is deleted (best effort: a server that cannot be reached
 *   still loses the local record, and the push service prunes a dead token by itself);
 * - category changes go to PUT on the stored device id.
 *
 * Registration never prompts for permission; the prompt is a separate, user-initiated step.
 */

export type PermissionState = 'granted' | 'denied' | 'undetermined';

/** Why there is no Expo push token: no EAS project id in this build, or the platform did not give one. */
export type TokenUnavailableReason = 'noProject' | 'unavailable';

export type TokenResult = { token: string; reason: null } | { token: null; reason: TokenUnavailableReason };

export interface PushEnvironment {
  permission: () => Promise<PermissionState>;
  expoToken: () => Promise<TokenResult>;
  platform: PushPlatform;
  deviceName: string | null;
  appVersion: string | null;
  locale: () => string | null;
  nowUtc: () => string;
}

export interface RegistrationDeps {
  env: PushEnvironment;
  api: PushApi;
  store: RegistrationStore;
}

export type RegistrationOutcome =
  | { state: 'registered'; record: PushRegistrationRecord }
  | { state: 'permission'; permission: PermissionState }
  | { state: 'unavailable'; reason: TokenUnavailableReason }
  | { state: 'error'; status: number | null };

function statusOf(err: unknown): number | null {
  return err instanceof ApiError ? err.status : null;
}

/** Register (or refresh) this device with the profile's server. `token` skips asking the platform again. */
export async function registerDevice(
  deps: RegistrationDeps,
  profileId: string,
  session: ServerSession,
  userId: string | null,
  token?: string,
): Promise<RegistrationOutcome> {
  const permission = await deps.env.permission();
  if (permission !== 'granted') return { state: 'permission', permission };
  let expoToken = token ?? null;
  if (!expoToken) {
    const result = await deps.env.expoToken();
    if (result.token === null) return { state: 'unavailable', reason: result.reason };
    expoToken = result.token;
  }

  const existing = await deps.store.read(profileId);
  const sameUser = !!existing && existing.userId === userId;
  try {
    const device = await deps.api.register(session, {
      platform: deps.env.platform,
      expoPushToken: expoToken,
      deviceName: deps.env.deviceName,
      appVersion: deps.env.appVersion,
      locale: deps.env.locale(),
      // The user's own choices travel with a new token; null lets the server keep or default them.
      categories: sameUser && existing && existing.expoPushToken !== expoToken ? existing.categories : null,
    });
    if (existing && existing.deviceId !== device.id && sameUser) {
      // The token changed: the old device row would keep receiving pushes for a token that no longer exists.
      try { await deps.api.remove(session, existing.deviceId); } catch { /* already gone or not ours */ }
    }
    const record: PushRegistrationRecord = {
      deviceId: device.id,
      expoPushToken: expoToken,
      userId,
      categories: Array.isArray(device.categories) ? device.categories : [],
      registeredUtc: deps.env.nowUtc(),
    };
    await deps.store.write(profileId, record);
    return { state: 'registered', record };
  } catch (err) {
    return { state: 'error', status: statusOf(err) };
  }
}

/** Remove this device from the profile's server (when a session is available) and forget the local record. */
export async function unregisterDevice(deps: RegistrationDeps, profileId: string, session: ServerSession | null): Promise<void> {
  const existing = await deps.store.read(profileId);
  if (!existing) return;
  if (session) {
    try {
      await deps.api.remove(session, existing.deviceId);
    } catch {
      // 404 (already removed) or unreachable: nothing more to do from here.
    }
  }
  await deps.store.remove(profileId);
}

/** Change which categories this device receives from the profile's server. */
export async function updateDeviceCategories(
  deps: RegistrationDeps,
  profileId: string,
  session: ServerSession,
  userId: string | null,
  categories: PushCategory[],
): Promise<RegistrationOutcome> {
  const existing = await deps.store.read(profileId);
  if (!existing) return registerDevice(deps, profileId, session, userId);
  try {
    const device = await deps.api.updateCategories(session, existing.deviceId, categories);
    const record: PushRegistrationRecord = { ...existing, categories: Array.isArray(device.categories) ? device.categories : categories };
    await deps.store.write(profileId, record);
    return { state: 'registered', record };
  } catch (err) {
    if (statusOf(err) !== 404) return { state: 'error', status: statusOf(err) };
    // The server no longer has the device (removed by an admin or with its user): register afresh, then apply the choice.
    await deps.store.remove(profileId);
    const again = await registerDevice(deps, profileId, session, userId);
    if (again.state !== 'registered') return again;
    try {
      const device = await deps.api.updateCategories(session, again.record.deviceId, categories);
      const record: PushRegistrationRecord = { ...again.record, categories: Array.isArray(device.categories) ? device.categories : categories };
      await deps.store.write(profileId, record);
      return { state: 'registered', record };
    } catch (retryErr) {
      return { state: 'error', status: statusOf(retryErr) };
    }
  }
}

/**
 * The platform issued a new push token: re-register every profile that had registered, using each one's stored
 * session (profiles without one re-register at their next sign-in).
 */
export async function reRegisterProfiles(
  deps: RegistrationDeps,
  profileIds: string[],
  sessionFor: (profileId: string) => Promise<{ session: ServerSession; userId: string | null } | null>,
  token: string,
): Promise<Record<string, RegistrationOutcome['state'] | 'skipped'>> {
  const result: Record<string, RegistrationOutcome['state'] | 'skipped'> = {};
  for (const profileId of profileIds) {
    const existing = await deps.store.read(profileId);
    if (!existing || existing.expoPushToken === token) { result[profileId] = 'skipped'; continue; }
    const target = await sessionFor(profileId);
    if (!target) { result[profileId] = 'skipped'; continue; }
    result[profileId] = (await registerDevice(deps, profileId, target.session, target.userId ?? existing.userId, token)).state;
  }
  return result;
}

/** The profile whose registration produced this device id, if any. */
export async function profileForDevice(store: RegistrationStore, profileIds: string[], deviceId: string): Promise<string | null> {
  for (const id of profileIds) {
    const record = await store.read(id);
    if (record?.deviceId === deviceId) return id;
  }
  return null;
}
