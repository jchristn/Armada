import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { AppState, type AppStateStatus } from 'react-native';
import { ApiError, configureClient, setAuthToken, setOnUnauthorized, whoami } from '@dashboard/api/client';
import type { WhoAmIResult } from '@dashboard/types/models';
import {
  addPasswordSkip,
  createProfile,
  loadProfiles,
  newProfileId,
  passwordSkipKey,
  readPasswordSkips,
  removePasswordSkip,
  removeProfile,
  saveProfiles,
  updateProfile,
  type ProfileState,
} from '../profiles/profileStore';
import type { ServerProfile, ServerProfileDraft, SignInMethod } from '../profiles/types';
import { deleteToken, readToken, writeToken } from '../storage/secure';
import { authenticateBiometric } from './biometrics';

/**
 * Session state for the mobile app. Mirrors the dashboard's AuthContext (token, whoami user, admin flags, the
 * default-password skip) and adds what a native client needs: server profiles, tokens in secure storage, a
 * biometric lock, and an "unreachable" state so a server that is briefly offline does not force a new sign-in.
 */
export type AuthStatus = 'loading' | 'signedOut' | 'locked' | 'unreachable' | 'signedIn';

/** Details remembered on the profile after a successful sign-in. */
export interface SignInDetails {
  method: SignInMethod;
  email?: string | null;
  tenantId?: string | null;
  tenantName?: string | null;
}

export interface AuthState {
  status: AuthStatus;
  profiles: ServerProfile[];
  activeProfile: ServerProfile | null;
  sessionToken: string | null;
  user: WhoAmIResult | null;
  isAuthenticated: boolean;
  isAdmin: boolean;
  isTenantAdmin: boolean;
  /** The server reports the default password and the user has not chosen to skip changing it. */
  mustChangePassword: boolean;
  passwordChangeSkipped: boolean;
  /** Keep the default password for now (remembered per profile and user); the default credentials banner stays. */
  skipPasswordChange: () => Promise<void>;
  /** Validate a token with whoami and store it for the active profile. Throws when the token is rejected. */
  login: (token: string, details: SignInDetails) => Promise<void>;
  logout: () => Promise<void>;
  /** Re-read whoami (for example after a password change). */
  refresh: () => Promise<void>;
  /** Biometric unlock of the stored token. Resolves false when the user cancels or verification fails. */
  unlock: (promptMessage: string, cancelLabel: string) => Promise<boolean>;
  /** Retry the stored session after the server was unreachable. */
  retry: () => Promise<void>;
  /** Create (no id) or update a profile; a new profile becomes active. */
  saveProfile: (draft: ServerProfileDraft, id?: string) => Promise<ServerProfile>;
  deleteProfile: (id: string) => Promise<void>;
  /** Switch servers: the current session is set aside (its token stays stored) and the new profile's is restored. */
  selectProfile: (id: string) => Promise<void>;
}

const AuthContext = createContext<AuthState | null>(null);

/** Re-lock a biometric profile after the app was in the background this long. */
export const LOCK_AFTER_BACKGROUND_MS = 5 * 60 * 1000;

function isUnauthorized(err: unknown): boolean {
  return err instanceof ApiError && (err.status === 401 || err.status === 403);
}

export interface AuthProviderProps {
  children: ReactNode;
  /** Injectable clock for tests. */
  now?: () => number;
}

export function AuthProvider({ children, now = Date.now }: AuthProviderProps) {
  const [status, setStatus] = useState<AuthStatus>('loading');
  const [profileState, setProfileState] = useState<ProfileState>({ profiles: [], activeId: null });
  const [sessionToken, setSessionToken] = useState<string | null>(null);
  const [user, setUser] = useState<WhoAmIResult | null>(null);
  const [skips, setSkips] = useState<string[]>([]);

  const profileStateRef = useRef(profileState);
  const statusRef = useRef(status);
  const backgroundSinceRef = useRef<number | null>(null);

  const activeProfile = useMemo(
    () => profileState.profiles.find((p) => p.id === profileState.activeId) ?? null,
    [profileState],
  );
  const activeProfileRef = useRef(activeProfile);

  // Callbacks read the latest values through refs so they stay stable across renders.
  useEffect(() => {
    profileStateRef.current = profileState;
    activeProfileRef.current = activeProfile;
  }, [profileState, activeProfile]);
  useEffect(() => { statusRef.current = status; }, [status]);

  const persist = useCallback(async (next: ProfileState) => {
    profileStateRef.current = next;
    activeProfileRef.current = next.profiles.find((p) => p.id === next.activeId) ?? null;
    setProfileState(next);
    await saveProfiles(next);
  }, []);

  const clearSession = useCallback(() => {
    setAuthToken(null);
    setSessionToken(null);
    setUser(null);
  }, []);

  /** Validate a stored token for a profile and enter the matching state. */
  const restore = useCallback(async (profile: ServerProfile | null, skipLock: boolean) => {
    clearSession();
    if (!profile) {
      configureClient({ baseUrl: '' });
      setStatus('signedOut');
      return;
    }
    configureClient({ baseUrl: profile.url });
    const token = await readToken(profile.id);
    if (!token) {
      setStatus('signedOut');
      return;
    }
    if (profile.biometricUnlock && !skipLock) {
      setStatus('locked');
      return;
    }
    setAuthToken(token);
    try {
      const me = await whoami();
      setSessionToken(token);
      setUser(me);
      setStatus('signedIn');
    } catch (err) {
      setAuthToken(null);
      if (isUnauthorized(err)) {
        await deleteToken(profile.id);
        setStatus('signedOut');
      } else {
        setStatus('unreachable');
      }
    }
  }, [clearSession]);

  const logout = useCallback(async () => {
    const profile = activeProfileRef.current;
    clearSession();
    setStatus('signedOut');
    if (profile) await deleteToken(profile.id);
  }, [clearSession]);

  // A 401 from any request means the stored token is no longer valid: sign out of this profile.
  useEffect(() => {
    setOnUnauthorized(() => {
      if (statusRef.current === 'signedIn') void logout();
    });
  }, [logout]);

  useEffect(() => {
    let cancelled = false;
    void (async () => {
      const [loaded, loadedSkips] = await Promise.all([loadProfiles(), readPasswordSkips()]);
      if (cancelled) return;
      profileStateRef.current = loaded;
      activeProfileRef.current = loaded.profiles.find((p) => p.id === loaded.activeId) ?? null;
      setProfileState(loaded);
      setSkips(loadedSkips);
      await restore(loaded.profiles.find((p) => p.id === loaded.activeId) ?? null, false);
    })();
    return () => { cancelled = true; };
  }, [restore]);

  // Biometric profiles lock again after a long stay in the background.
  useEffect(() => {
    const sub = AppState.addEventListener('change', (next: AppStateStatus) => {
      if (next === 'background') {
        backgroundSinceRef.current = now();
        return;
      }
      if (next !== 'active') return;
      const since = backgroundSinceRef.current;
      backgroundSinceRef.current = null;
      const profile = activeProfileRef.current;
      if (since === null || !profile?.biometricUnlock || statusRef.current !== 'signedIn') return;
      if (now() - since >= LOCK_AFTER_BACKGROUND_MS) {
        clearSession();
        setStatus('locked');
      }
    });
    return () => sub.remove();
  }, [clearSession, now]);

  const login = useCallback(async (token: string, details: SignInDetails) => {
    const profile = activeProfileRef.current;
    if (!profile) throw new Error('No server profile is selected.');
    configureClient({ baseUrl: profile.url });
    setAuthToken(token);
    let me: WhoAmIResult;
    try {
      me = await whoami();
    } catch (err) {
      setAuthToken(null);
      throw err;
    }
    await writeToken(profile.id, token);
    const updated: ServerProfile = {
      ...profile,
      signInMethod: details.method,
      lastEmail: details.email ?? profile.lastEmail,
      lastTenantId: details.tenantId ?? profile.lastTenantId,
      lastTenantName: details.tenantName ?? profile.lastTenantName,
      lastUserEmail: me.user?.email ?? profile.lastUserEmail,
    };
    const state = profileStateRef.current;
    await persist({ ...state, profiles: state.profiles.map((p) => (p.id === profile.id ? updated : p)) });
    setSessionToken(token);
    setUser(me);
    setStatus('signedIn');
  }, [persist]);

  const refresh = useCallback(async () => {
    const me = await whoami();
    setUser(me);
    // Once the password is changed the skip is moot; forget it so a later reset to the default asks again.
    const profile = activeProfileRef.current;
    if (profile && me.user?.id && !me.passwordChangeRequired) {
      const key = passwordSkipKey(profile.id, me.user.id);
      setSkips((prev) => prev.filter((k) => k !== key));
      await removePasswordSkip(key);
    }
  }, []);

  const unlock = useCallback(async (promptMessage: string, cancelLabel: string) => {
    const ok = await authenticateBiometric(promptMessage, cancelLabel);
    if (!ok) return false;
    await restore(activeProfileRef.current, true);
    return true;
  }, [restore]);

  const retry = useCallback(async () => {
    setStatus('loading');
    await restore(activeProfileRef.current, true);
  }, [restore]);

  const saveProfile = useCallback(async (draft: ServerProfileDraft, id?: string) => {
    const state = profileStateRef.current;
    const existing = id ? state.profiles.find((p) => p.id === id) : undefined;
    if (existing) {
      const updated = updateProfile(existing, draft);
      await persist({ ...state, profiles: state.profiles.map((p) => (p.id === id ? updated : p)) });
      if (state.activeId === id && existing.url !== updated.url) {
        // A different server: the stored token belonged to the old one.
        await deleteToken(updated.id);
        await restore(updated, true);
      }
      return updated;
    }
    const created = createProfile(draft, newProfileId(), new Date(now()).toISOString());
    await persist({ profiles: [...state.profiles, created], activeId: created.id });
    await restore(created, true);
    return created;
  }, [now, persist, restore]);

  const deleteProfile = useCallback(async (id: string) => {
    const state = profileStateRef.current;
    const wasActive = state.activeId === id;
    const next = await removeProfile(state, id);
    await persist(next);
    if (wasActive) await restore(next.profiles.find((p) => p.id === next.activeId) ?? null, false);
  }, [persist, restore]);

  const selectProfile = useCallback(async (id: string) => {
    const state = profileStateRef.current;
    if (state.activeId === id) return;
    const next = { ...state, activeId: id };
    await persist(next);
    setStatus('loading');
    await restore(next.profiles.find((p) => p.id === id) ?? null, false);
  }, [persist, restore]);

  const skipKey = activeProfile && user?.user?.id ? passwordSkipKey(activeProfile.id, user.user.id) : null;
  const passwordChangeSkipped = !!skipKey && skips.includes(skipKey);

  const skipPasswordChange = useCallback(async () => {
    if (!skipKey) return;
    setSkips((prev) => (prev.includes(skipKey) ? prev : [...prev, skipKey]));
    await addPasswordSkip(skipKey);
  }, [skipKey]);

  const isAuthenticated = status === 'signedIn' && !!sessionToken && !!user;
  const isAdmin = user?.user?.isAdmin ?? false;
  const isTenantAdmin = isAdmin || (user?.user?.isTenantAdmin ?? false);

  const value = useMemo<AuthState>(() => ({
    status,
    profiles: profileState.profiles,
    activeProfile,
    sessionToken,
    user,
    isAuthenticated,
    isAdmin,
    isTenantAdmin,
    mustChangePassword: isAuthenticated && !!user?.passwordChangeRequired && !passwordChangeSkipped,
    passwordChangeSkipped,
    skipPasswordChange,
    login,
    logout,
    refresh,
    unlock,
    retry,
    saveProfile,
    deleteProfile,
    selectProfile,
  }), [status, profileState.profiles, activeProfile, sessionToken, user, isAuthenticated, isAdmin, isTenantAdmin,
    passwordChangeSkipped, skipPasswordChange, login, logout, refresh, unlock, retry, saveProfile, deleteProfile, selectProfile]);

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthState {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used within AuthProvider');
  return ctx;
}
