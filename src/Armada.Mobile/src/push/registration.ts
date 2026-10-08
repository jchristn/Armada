import { ApiError } from '@dashboard/api/client';
import type { ServerSession } from '../api/serverSession';
import type { PushApi } from './pushApi';
import type { RegistrationStore, RetiredDevice } from './registrationStore';
import type { PushCategory, PushPlatform, PushRegistrationRecord } from './types';

/**
 * The push registration lifecycle, per server profile, free of React and native modules so it can be tested:
 *
 * - register on sign-in (and on every return to a signed-in profile): POST the Expo push token; the server answers
 *   with the device (`pdv_`), whose id is kept because the server masks the token in every response;
 * - re-register when the Expo token changes, and remove the stale device the old token left behind;
 * - remove the device on sign-out and when the profile is deleted. When the server cannot be reached (or the token
 *   was already rejected) the device is queued as retired: the removal is retried at the next contact with that
 *   server (before registering again), and pushes naming a retired device are neither shown nor handled;
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
    // The server may hand back a row that was retired earlier (same push token): it is in use again.
    await dropRetired(deps.store, (r) => r.deviceId === device.id);
    return { state: 'registered', record };
  } catch (err) {
    return { state: 'error', status: statusOf(err) };
  }
}

async function dropRetired(store: RegistrationStore, match: (r: RetiredDevice) => boolean): Promise<void> {
  if (!store.readRetired || !store.writeRetired) return;
  const list = await store.readRetired();
  const kept = list.filter((r) => !match(r));
  if (kept.length !== list.length) await store.writeRetired(kept);
}

/** Whether a push names a device this app retired (signed out) and has not yet removed from its server. */
export async function isRetiredDevice(store: RegistrationStore, deviceId: string): Promise<boolean> {
  if (!store.readRetired) return false;
  return (await store.readRetired()).some((r) => r.deviceId === deviceId);
}

/**
 * Retry the removal of this profile's retired devices now that its server can be reached with `session`. A removal
 * that succeeds (or finds the device gone, 404) leaves the queue; any other failure stays for the next contact.
 */
export async function flushRetiredDevices(deps: RegistrationDeps, profileId: string, session: ServerSession): Promise<void> {
  if (!deps.store.readRetired || !deps.store.writeRetired) return;
  const list = await deps.store.readRetired();
  if (!list.some((r) => r.profileId === profileId)) return;
  const kept: RetiredDevice[] = [];
  for (const entry of list) {
    if (entry.profileId !== profileId) { kept.push(entry); continue; }
    try {
      await deps.api.remove(session, entry.deviceId);
    } catch (err) {
      // 404: gone. 403: another account's device (this server's user changed); the client cannot remove it.
      const status = statusOf(err);
      if (status !== 404 && status !== 403) kept.push(entry);
    }
  }
  await deps.store.writeRetired(kept);
}

/** Remove this device from the profile's server (when a session is available) and forget the local record. */
export async function unregisterDevice(deps: RegistrationDeps, profileId: string, session: ServerSession | null): Promise<void> {
  const existing = await deps.store.read(profileId);
  if (!existing) return;
  let removed = false;
  if (session) {
    try {
      await deps.api.remove(session, existing.deviceId);
      removed = true;
    } catch (err) {
      // 404: already removed. Anything else (unreachable, rejected): retry at the next contact.
      removed = statusOf(err) === 404;
    }
  }
  if (!removed && deps.store.readRetired && deps.store.writeRetired) {
    const list = await deps.store.readRetired();
    if (!list.some((r) => r.deviceId === existing.deviceId)) {
      await deps.store.writeRetired([...list, { profileId, deviceId: existing.deviceId }]);
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

/**
 * The profile whose registration produced this device id, if any. Null when no profile, or more than one, holds it:
 * an id shared by two profiles (a stale record from before the server issued a new id on a change of owner) cannot
 * say which user the push is for, so it is not trusted.
 */
export async function profileForDevice(store: RegistrationStore, profileIds: string[], deviceId: string): Promise<string | null> {
  let match: string | null = null;
  for (const id of profileIds) {
    const record = await store.read(id);
    if (record?.deviceId !== deviceId) continue;
    if (match !== null) return null;
    match = id;
  }
  return match;
}
