import { PREF_KEYS, readPref, writePref } from '../storage/prefs';
import { deleteProxyToken, deleteToken } from '../storage/secure';
import type { ServerProfile, ServerProfileDraft } from './types';

/** Profiles and which one is active, as persisted. */
export interface ProfileState {
  profiles: ServerProfile[];
  activeId: string | null;
}

/** A new profile id (`prf_` + random); ids never leave the device. */
export function newProfileId(random: () => number = Math.random): string {
  let id = 'prf_';
  for (let i = 0; i < 16; i++) id += Math.floor(random() * 36).toString(36);
  return id;
}

export async function loadProfiles(): Promise<ProfileState> {
  const profiles = (await readPref<ServerProfile[]>(PREF_KEYS.profiles)) ?? [];
  const activeId = await readPref<string>(PREF_KEYS.activeProfile);
  const valid = Array.isArray(profiles) ? profiles.filter((p) => p && typeof p.id === 'string' && typeof p.url === 'string') : [];
  return { profiles: valid, activeId: valid.some((p) => p.id === activeId) ? activeId : valid[0]?.id ?? null };
}

export async function saveProfiles(state: ProfileState): Promise<void> {
  await writePref(PREF_KEYS.profiles, state.profiles);
  await writePref(PREF_KEYS.activeProfile, state.activeId);
}

/** Create a profile from a draft (URL already normalized by the caller). */
export function createProfile(draft: ServerProfileDraft, id: string, nowUtc: string): ServerProfile {
  return {
    id,
    name: draft.name.trim() || draft.url,
    kind: draft.kind,
    url: draft.url,
    signInMethod: 'password',
    biometricUnlock: draft.biometricUnlock,
    lastEmail: null,
    lastTenantId: null,
    lastTenantName: null,
    lastUserEmail: null,
    createdUtc: nowUtc,
    proxyInstanceId: null,
  };
}

/**
 * Apply a draft to an existing profile. Changing the URL or the kind drops the remembered tenant and proxy instance
 * (they belonged to the old server).
 */
export function updateProfile(profile: ServerProfile, draft: ServerProfileDraft): ServerProfile {
  const urlChanged = profile.url !== draft.url || profile.kind !== draft.kind;
  return {
    ...profile,
    name: draft.name.trim() || draft.url,
    kind: draft.kind,
    url: draft.url,
    biometricUnlock: draft.biometricUnlock,
    lastTenantId: urlChanged ? null : profile.lastTenantId,
    lastTenantName: urlChanged ? null : profile.lastTenantName,
    proxyInstanceId: urlChanged ? null : profile.proxyInstanceId ?? null,
  };
}

/** Remove a profile and its stored tokens; the next remaining profile becomes active when it was the active one. */
export async function removeProfile(state: ProfileState, id: string): Promise<ProfileState> {
  await deleteToken(id);
  await deleteProxyToken(id);
  const profiles = state.profiles.filter((p) => p.id !== id);
  const activeId = state.activeId === id ? profiles[0]?.id ?? null : state.activeId;
  return { profiles, activeId };
}

/** Per profile and user: the user chose to keep the default password for now (mirrors the dashboard's skip). */
export function passwordSkipKey(profileId: string, userId: string): string {
  return `${profileId}:${userId}`;
}

export async function readPasswordSkips(): Promise<string[]> {
  const list = await readPref<string[]>(PREF_KEYS.passwordChangeSkipped);
  return Array.isArray(list) ? list : [];
}

export async function addPasswordSkip(key: string): Promise<void> {
  const list = await readPasswordSkips();
  if (!list.includes(key)) await writePref(PREF_KEYS.passwordChangeSkipped, [...list, key]);
}

export async function removePasswordSkip(key: string): Promise<void> {
  const list = await readPasswordSkips();
  await writePref(PREF_KEYS.passwordChangeSkipped, list.filter((k) => k !== key));
}
