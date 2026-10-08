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
import type { SavedSignInInfo, ServerProfile, ServerProfileDraft, SignInMethod } from '../profiles/types';
import { sessionFor, storedSessionFor, type ServerSession } from '../api/serverSession';
import {
  createProxyClient,
  hasUsableInstance,
  isProxyError,
  proxySessionHeaders,
  type ProxyClient,
  type ProxyInstance,
  type ProxySessionContext,
} from '../proxy/proxyApi';
import { deleteProxyToken, deleteToken, readProxyToken, readToken, writeProxyToken, writeToken } from '../storage/secure';
import { authenticateBiometric } from './biometrics';
import {
  deleteSavedCredential,
  deleteSavedCredentials,
  readAdmiralCredentials,
  readProxyCredentials,
  saveAdmiralCredentials,
  saveProxyCredentials,
  type SavedAdmiralCredentials,
  type SavedCredentialKind,
  type SavedCredentialRead,
  type SavedProxyCredentials,
} from './savedCredentials';

/**
 * Session state for the mobile app. Mirrors the dashboard's AuthContext (token, whoami user, admin flags, the
 * default-password skip) and adds what a native client needs: server profiles, tokens in secure storage, a
 * biometric lock, and an "unreachable" state so a server that is briefly offline does not force a new sign-in.
 */
export type AuthStatus = 'loading' | 'signedOut' | 'locked' | 'unreachable' | 'signedIn';

/**
 * Where a Proxy profile's sign-in stands while signed out: sign in to the Armada.Proxy portal, pick an Admiral
 * instance, or sign in to that Admiral through the relay.
 */
export type ProxyStage = 'portal' | 'instance' | 'admiral';

/** Details remembered on the profile after a successful sign-in. */
export interface SignInDetails {
  method: SignInMethod;
  email?: string | null;
  tenantId?: string | null;
  tenantName?: string | null;
}

/**
 * The password of a password sign-in, passed so it can be saved behind biometrics. It is held in memory only: saved
 * to the biometric-protected keychain item when `save` is on, or kept until the user answers the one-time offer.
 */
export interface PasswordToRemember {
  password: string;
  /** The user turned on "Save password and use Face ID". */
  save: boolean;
  /**
   * The device can save passwords behind biometrics (the switch was offered). When false nothing is saved, offered,
   * or forgotten.
   */
  canSave: boolean;
  /** Prompt text for the Android biometric prompt that encrypts the item (iOS does not prompt to save). */
  prompt?: string;
}

/** After a password sign-in without the switch: offer once to save the password(s) for biometric sign-in. */
export interface SavePasswordOffer {
  profileId: string;
  profileName: string;
  /** The Admiral password can be saved. */
  admiral: boolean;
  /** The Armada.Proxy password can be saved. */
  proxy: boolean;
}

export interface LogoutOptions {
  /** Also delete the passwords saved for biometric sign-in (they are kept by default). */
  forgetSavedPassword?: boolean;
}

/** The password-bearing half of a pending offer (never leaves memory). */
interface PendingOffer {
  profileId: string;
  admiral: SavedAdmiralCredentials | null;
  proxy: SavedProxyCredentials | null;
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
  /**
   * Validate a token with whoami and store it for the active profile. Throws when the token is rejected. With
   * `remember` (password sign-ins), the password is saved behind biometrics, forgotten (switch turned off), or offered.
   */
  login: (token: string, details: SignInDetails, remember?: PasswordToRemember) => Promise<void>;
  /** Sign out. Passwords saved for biometric sign-in stay unless `forgetSavedPassword` is set. */
  logout: (options?: LogoutOptions) => Promise<void>;
  /** The user signed out in this app session (the sign-in screen then does not prompt for Face ID on its own). */
  signedOutByUser: boolean;
  /**
   * Read the active profile's saved Admiral password (the OS shows the biometric prompt). `none` (nothing saved, or
   * the item was invalidated by a biometric change) also clears the profile's saved-password flag.
   */
  readSavedPassword: (prompt: string) => Promise<SavedCredentialRead<SavedAdmiralCredentials>>;
  /** The same for a Proxy profile's saved Armada.Proxy password. */
  readSavedProxyPassword: (prompt: string) => Promise<SavedCredentialRead<SavedProxyCredentials>>;
  /** Delete saved passwords of a profile (default: the active one; default: both the Admiral and the proxy one). */
  forgetSavedPassword: (profileId?: string, kind?: SavedCredentialKind) => Promise<void>;
  /** After a successful password change: replace the saved password when it belongs to the signed-in account. */
  updateSavedPassword: (newPassword: string) => Promise<void>;
  /** The pending one-time offer to save the password for biometric sign-in, or null. */
  savePasswordOffer: SavePasswordOffer | null;
  /** Save the offered password(s). Resolves false when saving failed (for example, the Android prompt was cancelled). */
  acceptSavePasswordOffer: (prompt?: string) => Promise<boolean>;
  /** "Not now": remembered per profile, so the offer is not shown again. */
  declineSavePasswordOffer: () => Promise<void>;
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

  /** Proxy profiles: the sign-in step to show while signed out (null for Direct profiles and once signed in). */
  proxyStage: ProxyStage | null;
  /** Proxy profiles: the Armada.Proxy session token (relayed requests carry it in X-Armada-Proxy-Session). */
  proxyToken: string | null;
  /** The proxy session ended (expired or logged out elsewhere); sign in to the proxy again to continue. */
  proxyExpired: boolean;
  /** Headers for app-owned requests to the active server (the proxy session for Proxy profiles), or null. */
  requestHeaders: Record<string, string> | null;
  /**
   * Sign in to the Armada.Proxy portal with its password. Throws ProxyError (unauthorized, lockedOut, ...). With
   * `remember`, the proxy password is saved behind biometrics, forgotten, or offered, as for `login`.
   */
  proxySignIn: (password: string, remember?: PasswordToRemember) => Promise<void>;
  /** The Admiral instances connected to the proxy. */
  proxyListInstances: () => Promise<ProxyInstance[]>;
  /** Pick the Admiral instance this profile uses. Throws ProxyError (conflict when not connected). */
  proxySelectInstance: (instanceId: string) => Promise<void>;
  /** Back to the instance picker (before signing in to the Admiral). */
  proxyChangeInstance: () => Promise<void>;
  /** End the proxy session too (logs out of Armada.Proxy and forgets both tokens of this profile). */
  proxySignOut: () => Promise<void>;
}

/**
 * Hooks for services that hold per-session server state (push registration). `onSessionEnding` runs before a
 * profile's Admiral session ends: sign-out, profile deletion, a server change, or a token the server rejected
 * (then `session` is null because the server can no longer be called).
 */
export interface AuthHooks {
  onSessionEnding?: (profile: ServerProfile, session: ServerSession | null) => Promise<void>;
}

const AuthContext = createContext<AuthState | null>(null);

/** Re-lock a biometric profile after the app was in the background this long. */
export const LOCK_AFTER_BACKGROUND_MS = 5 * 60 * 1000;

function isUnauthorized(err: unknown): boolean {
  return err instanceof ApiError && (err.status === 401 || err.status === 403);
}

/** Session hooks get at most this long, so a slow server never holds up a sign-out. */
export const SESSION_HOOK_TIMEOUT_MS = 5000;

async function runHook(fn: () => Promise<void>): Promise<void> {
  let timer: ReturnType<typeof setTimeout> | null = null;
  try {
    await Promise.race([
      fn().catch(() => undefined),
      new Promise<void>((resolve) => { timer = setTimeout(resolve, SESSION_HOOK_TIMEOUT_MS); }),
    ]);
  } finally {
    if (timer) clearTimeout(timer);
  }
}

export interface AuthProviderProps {
  children: ReactNode;
  /** Injectable clock for tests. */
  now?: () => number;
  /** Session hooks (push registration cleanup). */
  hooks?: AuthHooks;
  /** Injectable Armada.Proxy client factory for tests. */
  proxyClientFactory?: (baseUrl: string) => ProxyClient;
}

const defaultProxyClientFactory = (baseUrl: string) => createProxyClient(baseUrl);

export function AuthProvider({ children, now = Date.now, hooks, proxyClientFactory = defaultProxyClientFactory }: AuthProviderProps) {
  const [status, setStatus] = useState<AuthStatus>('loading');
  const [profileState, setProfileState] = useState<ProfileState>({ profiles: [], activeId: null });
  const [sessionToken, setSessionToken] = useState<string | null>(null);
  const [user, setUser] = useState<WhoAmIResult | null>(null);
  const [skips, setSkips] = useState<string[]>([]);
  const [proxyStage, setProxyStage] = useState<ProxyStage | null>(null);
  const [proxyToken, setProxyTokenState] = useState<string | null>(null);
  const [proxyExpired, setProxyExpired] = useState(false);
  const [signedOutByUser, setSignedOutByUser] = useState(false);
  const [savePasswordOffer, setSavePasswordOffer] = useState<SavePasswordOffer | null>(null);

  const profileStateRef = useRef(profileState);
  const proxyTokenRef = useRef<string | null>(null);
  const sessionTokenRef = useRef<string | null>(null);
  const hooksRef = useRef(hooks);
  const proxyFactoryRef = useRef(proxyClientFactory);
  const handlingUnauthorizedRef = useRef(false);
  const statusRef = useRef(status);
  const backgroundSinceRef = useRef<number | null>(null);
  const userRef = useRef<WhoAmIResult | null>(null);
  const pendingOfferRef = useRef<PendingOffer | null>(null);
  /**
   * The profile whose biometric lock the user satisfied in this foreground (a biometric unlock, or a sign-in with a
   * password or key just now). Paths that resume a stored session for a biometric profile without it lock instead.
   */
  const lockSatisfiedRef = useRef<string | null>(null);

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
  useEffect(() => { userRef.current = user; }, [user]);
  useEffect(() => { hooksRef.current = hooks; }, [hooks]);
  useEffect(() => { proxyFactoryRef.current = proxyClientFactory; }, [proxyClientFactory]);

  const setProxyToken = useCallback((token: string | null) => {
    proxyTokenRef.current = token;
    setProxyTokenState(token);
    configureClient({ headers: proxySessionHeaders(token) });
  }, []);

  const setSession = useCallback((token: string | null) => {
    sessionTokenRef.current = token;
    setSessionToken(token);
  }, []);

  const persist = useCallback(async (next: ProfileState) => {
    profileStateRef.current = next;
    activeProfileRef.current = next.profiles.find((p) => p.id === next.activeId) ?? null;
    setProfileState(next);
    await saveProfiles(next);
  }, []);

  const clearSession = useCallback(() => {
    setAuthToken(null);
    setSession(null);
    setUser(null);
  }, [setSession]);

  /** Drop a pending save-password offer (and the password it holds in memory). */
  const clearOffer = useCallback(() => {
    pendingOfferRef.current = null;
    setSavePasswordOffer(null);
  }, []);

  /** Keep a password in memory for the one-time offer (merged with the other half of a Proxy profile's sign-in). */
  const addToOffer = useCallback((profile: ServerProfile, part: { admiral?: SavedAdmiralCredentials; proxy?: SavedProxyCredentials }) => {
    const prev = pendingOfferRef.current?.profileId === profile.id ? pendingOfferRef.current : null;
    const next: PendingOffer = {
      profileId: profile.id,
      admiral: part.admiral ?? prev?.admiral ?? null,
      proxy: part.proxy ?? prev?.proxy ?? null,
    };
    pendingOfferRef.current = next;
    setSavePasswordOffer({ profileId: profile.id, profileName: profile.name, admiral: !!next.admiral, proxy: !!next.proxy });
  }, []);

  /** Change fields of a stored profile (by id, against the latest state). */
  const patchProfile = useCallback(async (id: string, patch: Partial<ServerProfile>) => {
    const state = profileStateRef.current;
    if (!state.profiles.some((p) => p.id === id)) return;
    await persist({ ...state, profiles: state.profiles.map((p) => (p.id === id ? { ...p, ...patch } : p)) });
  }, [persist]);

  /** The Admiral half of a sign-in: validate the stored Admiral token (through the relay for Proxy profiles). */
  const enterAdmiral = useCallback(async (profile: ServerProfile) => {
    const token = await readToken(profile.id);
    if (!token) {
      setProxyStage(profile.kind === 'Proxy' ? 'admiral' : null);
      setStatus('signedOut');
      return;
    }
    setAuthToken(token);
    try {
      const me = await whoami();
      setSession(token);
      setUser(me);
      setProxyStage(null);
      setSignedOutByUser(false);
      setStatus('signedIn');
    } catch (err) {
      setAuthToken(null);
      if (isUnauthorized(err)) {
        await runHook(() => hooksRef.current?.onSessionEnding?.(profile, null) ?? Promise.resolve());
        await deleteToken(profile.id);
        setProxyStage(profile.kind === 'Proxy' ? 'admiral' : null);
        setStatus('signedOut');
      } else if (profile.kind === 'Proxy' && err instanceof ApiError && err.status === 409) {
        // The selected Admiral disconnected from the proxy: pick an instance again.
        setProxyStage('instance');
        setStatus('signedOut');
      } else {
        setStatus('unreachable');
      }
    }
  }, [setSession]);

  /**
   * Resume the stored Admiral session after a proxy sign-in or instance pick. The proxy password is shared, so it
   * does not stand in for the profile's biometric lock: a stored Admiral token of a biometric profile is locked
   * until the user unlocks (or signs in to the Admiral again).
   */
  const enterAdmiralChecked = useCallback(async (profile: ServerProfile) => {
    if (profile.biometricUnlock && lockSatisfiedRef.current !== profile.id && await readToken(profile.id)) {
      setProxyStage(null);
      setStatus('locked');
      return;
    }
    await enterAdmiral(profile);
  }, [enterAdmiral]);

  /** The proxy session ended: forget it (the Admiral token stays, so a proxy re-sign-in resumes the session). */
  const proxySessionEnded = useCallback(async (profile: ServerProfile) => {
    lockSatisfiedRef.current = null;
    clearSession();
    await deleteProxyToken(profile.id);
    setProxyToken(null);
    setProxyExpired(true);
    setSignedOutByUser(false);
    setProxyStage('portal');
    setStatus('signedOut');
  }, [clearSession, setProxyToken]);

  /** Proxy profiles: proxy session, then a usable instance (re-selecting the remembered one), then the Admiral. */
  const restoreProxy = useCallback(async (profile: ServerProfile, skipLock: boolean) => {
    const pToken = await readProxyToken(profile.id);
    if (!pToken) {
      setProxyStage('portal');
      setStatus('signedOut');
      return;
    }
    if (profile.biometricUnlock && !skipLock) {
      setStatus('locked');
      return;
    }
    setProxyToken(pToken);
    const proxy = proxyFactoryRef.current(profile.url);
    let ctx: ProxySessionContext | null = null;
    try {
      ctx = await proxy.sessionContext(pToken);
    } catch (err) {
      if (isProxyError(err, 'unauthorized')) await proxySessionEnded(profile);
      else setStatus('unreachable');
      return;
    }
    if (!hasUsableInstance(ctx) && profile.proxyInstanceId) {
      try {
        ctx = await proxy.selectInstance(pToken, profile.proxyInstanceId);
      } catch (err) {
        if (isProxyError(err, 'unauthorized')) { await proxySessionEnded(profile); return; }
        if (isProxyError(err, 'network')) { setStatus('unreachable'); return; }
        // Not connected right now (409) or gone (404): the picker shows what is available.
      }
    }
    if (!hasUsableInstance(ctx)) {
      setProxyStage('instance');
      setStatus('signedOut');
      return;
    }
    await enterAdmiral(profile);
  }, [enterAdmiral, proxySessionEnded, setProxyToken]);

  /** Validate a stored token for a profile and enter the matching state. */
  const restore = useCallback(async (profile: ServerProfile | null, skipLock: boolean) => {
    clearSession();
    clearOffer();
    setProxyStage(null);
    setProxyToken(null);
    setProxyExpired(false);
    if (!profile) {
      configureClient({ baseUrl: '' });
      setStatus('signedOut');
      return;
    }
    configureClient({ baseUrl: profile.url });
    if (profile.kind === 'Proxy') {
      await restoreProxy(profile, skipLock);
      return;
    }
    const token = await readToken(profile.id);
    if (!token) {
      setStatus('signedOut');
      return;
    }
    if (profile.biometricUnlock && !skipLock) {
      setStatus('locked');
      return;
    }
    await enterAdmiral(profile);
  }, [clearOffer, clearSession, enterAdmiral, restoreProxy, setProxyToken]);

  /** End the Admiral session of the active profile. `serverReachable` false: the token was rejected already. */
  const endAdmiralSession = useCallback(async (serverReachable: boolean) => {
    const profile = activeProfileRef.current;
    const token = sessionTokenRef.current;
    if (profile) {
      // Locked (no token in memory): the stored token still lets this device remove its own registration.
      const session = !serverReachable ? null
        : token ? sessionFor(profile, token, proxyTokenRef.current) : await storedSessionFor(profile);
      await runHook(() => hooksRef.current?.onSessionEnding?.(profile, session) ?? Promise.resolve());
    }
    lockSatisfiedRef.current = null;
    clearSession();
    clearOffer();
    setProxyStage(profile?.kind === 'Proxy' ? (proxyTokenRef.current ? 'admiral' : 'portal') : null);
    setStatus('signedOut');
    if (profile) await deleteToken(profile.id);
  }, [clearOffer, clearSession]);

  /** Delete saved passwords of a profile and clear its flags. */
  const forgetSavedPassword = useCallback(async (profileId?: string, kind?: SavedCredentialKind) => {
    const id = profileId ?? activeProfileRef.current?.id;
    if (!id) return;
    if (kind) await deleteSavedCredential(id, kind);
    else await deleteSavedCredentials(id);
    const patch: Partial<ServerProfile> = {};
    if (!kind || kind === 'admiral') patch.savedSignIn = null;
    if (!kind || kind === 'proxy') patch.proxyPasswordSaved = false;
    await patchProfile(id, patch);
  }, [patchProfile]);

  const logout = useCallback(async (options?: LogoutOptions) => {
    const profile = activeProfileRef.current;
    setSignedOutByUser(true);
    await endAdmiralSession(true);
    if (profile && options?.forgetSavedPassword) await forgetSavedPassword(profile.id);
  }, [endAdmiralSession, forgetSavedPassword]);

  /**
   * A 401 from any request means a credential is no longer valid. For a Proxy profile it may be the proxy session
   * (expired after 24 hours or logged out) rather than the Admiral token: the proxy's own session check tells which.
   */
  const handleUnauthorized = useCallback(async () => {
    if (statusRef.current !== 'signedIn' || handlingUnauthorizedRef.current) return;
    handlingUnauthorizedRef.current = true;
    try {
      const profile = activeProfileRef.current;
      const pToken = proxyTokenRef.current;
      if (profile?.kind === 'Proxy' && pToken) {
        try {
          await proxyFactoryRef.current(profile.url).sessionContext(pToken);
        } catch (err) {
          if (isProxyError(err, 'unauthorized')) await proxySessionEnded(profile);
          // Otherwise the proxy could not be asked; keep the session and let the next request tell.
          return;
        }
      }
      setSignedOutByUser(false);
      await endAdmiralSession(false);
    } finally {
      handlingUnauthorizedRef.current = false;
    }
  }, [endAdmiralSession, proxySessionEnded]);

  useEffect(() => {
    setOnUnauthorized(() => { void handleUnauthorized(); });
  }, [handleUnauthorized]);

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
      if (since === null || now() - since < LOCK_AFTER_BACKGROUND_MS) return;
      // A long absence ends any earlier biometric check, whatever the state.
      lockSatisfiedRef.current = null;
      if (!profile?.biometricUnlock) return;
      // 'unreachable' keeps the stored session too: lock it, or Retry would resume it without a check.
      if (statusRef.current === 'signedIn' || statusRef.current === 'unreachable') {
        clearSession();
        setStatus('locked');
      }
    });
    return () => sub.remove();
  }, [clearSession, now]);

  const login = useCallback(async (token: string, details: SignInDetails, remember?: PasswordToRemember) => {
    const profile = activeProfileRef.current;
    if (!profile) throw new Error('No server profile is selected.');
    configureClient({ baseUrl: profile.url });
    configureClient({ headers: profile.kind === 'Proxy' ? proxySessionHeaders(proxyTokenRef.current) : null });
    setAuthToken(token);
    let me: WhoAmIResult;
    try {
      me = await whoami();
    } catch (err) {
      setAuthToken(null);
      throw err;
    }
    await writeToken(profile.id, token);
    // Saved passwords: only after the server accepted the password (authenticate) and the token (whoami).
    let savedSignIn: SavedSignInInfo | null = profile.savedSignIn ?? null;
    const account: SavedSignInInfo | null = details.email && details.tenantId
      ? { email: details.email, tenantId: details.tenantId, tenantName: details.tenantName ?? null }
      : null;
    if (remember && remember.canSave && account && remember.password) {
      if (remember.save) {
        const saved = await saveAdmiralCredentials(profile.id, { ...account, password: remember.password }, remember.prompt);
        savedSignIn = saved ? account : null;
      } else if (savedSignIn) {
        // The switch was on (a password is saved) and the user turned it off.
        await deleteSavedCredential(profile.id, 'admiral');
        savedSignIn = null;
      } else if (!profile.savePasswordOfferDeclined) {
        addToOffer(profile, { admiral: { ...account, password: remember.password } });
      }
    }
    const latest = profileStateRef.current.profiles.find((p) => p.id === profile.id) ?? profile;
    const updated: ServerProfile = {
      ...latest,
      signInMethod: details.method,
      lastEmail: details.email ?? latest.lastEmail,
      lastTenantId: details.tenantId ?? latest.lastTenantId,
      lastTenantName: details.tenantName ?? latest.lastTenantName,
      lastUserEmail: me.user?.email ?? latest.lastUserEmail,
      savedSignIn,
    };
    const state = profileStateRef.current;
    await persist({ ...state, profiles: state.profiles.map((p) => (p.id === profile.id ? updated : p)) });
    setSession(token);
    setUser(me);
    setProxyStage(null);
    setSignedOutByUser(false);
    // The user just proved who they are with a password or key: no biometric prompt on top in this foreground.
    lockSatisfiedRef.current = profile.id;
    setStatus('signedIn');
  }, [addToOffer, persist, setSession]);

  const readSavedPassword = useCallback(async (prompt: string): Promise<SavedCredentialRead<SavedAdmiralCredentials>> => {
    const profile = activeProfileRef.current;
    if (!profile?.savedSignIn) return { status: 'none' };
    const result = await readAdmiralCredentials(profile.id, prompt);
    // Invalidated (a face or fingerprint was enrolled) or unreadable: treat as not saved.
    if (result.status === 'none') await forgetSavedPassword(profile.id, 'admiral');
    return result;
  }, [forgetSavedPassword]);

  const readSavedProxyPassword = useCallback(async (prompt: string): Promise<SavedCredentialRead<SavedProxyCredentials>> => {
    const profile = activeProfileRef.current;
    if (!profile?.proxyPasswordSaved) return { status: 'none' };
    const result = await readProxyCredentials(profile.id, prompt);
    if (result.status === 'none') await forgetSavedPassword(profile.id, 'proxy');
    return result;
  }, [forgetSavedPassword]);

  const updateSavedPassword = useCallback(async (newPassword: string) => {
    const profile = activeProfileRef.current;
    const me = userRef.current;
    const pending = pendingOfferRef.current;
    if (profile && pending?.profileId === profile.id && pending.admiral) {
      pendingOfferRef.current = { ...pending, admiral: { ...pending.admiral, password: newPassword } };
    }
    const saved = profile?.savedSignIn;
    if (!profile || !saved || !me?.user?.email) return;
    const sameAccount = me.user.email.toLowerCase() === saved.email.toLowerCase() && (!me.tenant?.id || me.tenant.id === saved.tenantId);
    if (!sameAccount) return;
    const ok = await saveAdmiralCredentials(profile.id, { ...saved, password: newPassword });
    if (!ok) await patchProfile(profile.id, { savedSignIn: null });
  }, [patchProfile]);

  const acceptSavePasswordOffer = useCallback(async (prompt?: string) => {
    const pending = pendingOfferRef.current;
    clearOffer();
    if (!pending) return false;
    const patch: Partial<ServerProfile> = {};
    let ok = true;
    if (pending.admiral) {
      const saved = await saveAdmiralCredentials(pending.profileId, pending.admiral, prompt);
      patch.savedSignIn = saved
        ? { email: pending.admiral.email, tenantId: pending.admiral.tenantId, tenantName: pending.admiral.tenantName }
        : null;
      ok = ok && saved;
    }
    if (pending.proxy) {
      const saved = await saveProxyCredentials(pending.profileId, pending.proxy, prompt);
      patch.proxyPasswordSaved = saved;
      ok = ok && saved;
    }
    await patchProfile(pending.profileId, patch);
    return ok;
  }, [clearOffer, patchProfile]);

  const declineSavePasswordOffer = useCallback(async () => {
    const pending = pendingOfferRef.current;
    clearOffer();
    if (pending) await patchProfile(pending.profileId, { savePasswordOfferDeclined: true });
  }, [clearOffer, patchProfile]);

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
    lockSatisfiedRef.current = activeProfileRef.current?.id ?? null;
    await restore(activeProfileRef.current, true);
    return true;
  }, [restore]);

  const retry = useCallback(async () => {
    setStatus('loading');
    const profile = activeProfileRef.current;
    // Skip the lock only when it was satisfied in this foreground; otherwise a biometric profile goes to 'locked'.
    await restore(profile, !!profile && lockSatisfiedRef.current === profile.id);
  }, [restore]);

  const saveProfile = useCallback(async (draft: ServerProfileDraft, id?: string) => {
    const state = profileStateRef.current;
    const existing = id ? state.profiles.find((p) => p.id === id) : undefined;
    if (existing) {
      const updated = updateProfile(existing, draft);
      const serverChanged = existing.url !== updated.url || existing.kind !== updated.kind;
      if (serverChanged) {
        // A different server: the stored tokens (and this device's push registration) belonged to the old one.
        const oldSession = await storedSessionFor(existing);
        await runHook(() => hooksRef.current?.onSessionEnding?.(existing, oldSession) ?? Promise.resolve());
        await deleteToken(updated.id);
        await deleteProxyToken(updated.id);
        await deleteSavedCredentials(updated.id);
        if (pendingOfferRef.current?.profileId === updated.id) clearOffer();
      }
      await persist({ ...state, profiles: state.profiles.map((p) => (p.id === id ? updated : p)) });
      if (state.activeId === id && serverChanged) await restore(updated, true);
      return updated;
    }
    const created = createProfile(draft, newProfileId(), new Date(now()).toISOString());
    await persist({ profiles: [...state.profiles, created], activeId: created.id });
    await restore(created, true);
    return created;
  }, [clearOffer, now, persist, restore]);

  const deleteProfile = useCallback(async (id: string) => {
    const state = profileStateRef.current;
    const wasActive = state.activeId === id;
    const profile = state.profiles.find((p) => p.id === id);
    if (profile) {
      const session = await storedSessionFor(profile);
      await runHook(() => hooksRef.current?.onSessionEnding?.(profile, session) ?? Promise.resolve());
      if (profile.kind === 'Proxy') {
        const pToken = await readProxyToken(profile.id);
        if (pToken) await runHook(() => proxyFactoryRef.current(profile.url).logout(pToken));
      }
    }
    if (pendingOfferRef.current?.profileId === id) clearOffer();
    const next = await removeProfile(state, id);
    await persist(next);
    if (wasActive) await restore(next.profiles.find((p) => p.id === next.activeId) ?? null, false);
  }, [clearOffer, persist, restore]);

  const selectProfile = useCallback(async (id: string) => {
    const state = profileStateRef.current;
    if (state.activeId === id) return;
    const next = { ...state, activeId: id };
    await persist(next);
    setSignedOutByUser(false);
    lockSatisfiedRef.current = null;
    setStatus('loading');
    await restore(next.profiles.find((p) => p.id === id) ?? null, false);
  }, [persist, restore]);

  const requireProxyProfile = useCallback((): ServerProfile => {
    const profile = activeProfileRef.current;
    if (!profile || profile.kind !== 'Proxy') throw new Error('The active server is not an Armada.Proxy profile.');
    return profile;
  }, []);

  const proxySignIn = useCallback(async (password: string, remember?: PasswordToRemember) => {
    const profile = requireProxyProfile();
    const proxy = proxyFactoryRef.current(profile.url);
    const result = await proxy.login(password);
    await writeProxyToken(profile.id, result.token);
    if (remember && remember.canSave && password) {
      if (remember.save) {
        const saved = await saveProxyCredentials(profile.id, { password }, remember.prompt);
        await patchProfile(profile.id, { proxyPasswordSaved: saved });
      } else if (profile.proxyPasswordSaved) {
        await forgetSavedPassword(profile.id, 'proxy');
      } else if (!profile.savePasswordOfferDeclined) {
        addToOffer(profile, { proxy: { password } });
      }
    }
    setProxyToken(result.token);
    setProxyExpired(false);
    let ctx: ProxySessionContext | null = null;
    if (profile.proxyInstanceId) {
      try {
        ctx = await proxy.selectInstance(result.token, profile.proxyInstanceId);
      } catch {
        // The remembered instance is not available now; the picker comes next.
      }
    }
    if (!hasUsableInstance(ctx)) {
      setProxyStage('instance');
      setStatus('signedOut');
      return;
    }
    // Same instance as before: a still-valid Admiral token resumes the session without a second sign-in (after the
    // biometric check for a profile that has one).
    await enterAdmiralChecked(profile);
  }, [addToOffer, enterAdmiralChecked, forgetSavedPassword, patchProfile, requireProxyProfile, setProxyToken]);

  const proxyListInstances = useCallback(async () => {
    const profile = requireProxyProfile();
    const pToken = proxyTokenRef.current;
    if (!pToken) throw new Error('Sign in to the proxy first.');
    try {
      return await proxyFactoryRef.current(profile.url).listInstances(pToken);
    } catch (err) {
      if (isProxyError(err, 'unauthorized')) await proxySessionEnded(profile);
      throw err;
    }
  }, [proxySessionEnded, requireProxyProfile]);

  const proxySelectInstance = useCallback(async (instanceId: string) => {
    const profile = requireProxyProfile();
    const pToken = proxyTokenRef.current;
    if (!pToken) throw new Error('Sign in to the proxy first.');
    try {
      await proxyFactoryRef.current(profile.url).selectInstance(pToken, instanceId);
    } catch (err) {
      if (isProxyError(err, 'unauthorized')) await proxySessionEnded(profile);
      throw err;
    }
    const changed = (profile.proxyInstanceId ?? null) !== instanceId;
    const updated: ServerProfile = changed
      ? { ...profile, proxyInstanceId: instanceId, lastTenantId: null, lastTenantName: null, savedSignIn: null }
      : profile;
    if (changed) {
      // A different Admiral: an Admiral token or password kept from the previous instance is not valid there.
      if (profile.proxyInstanceId) await runHook(() => hooksRef.current?.onSessionEnding?.(profile, null) ?? Promise.resolve());
      await deleteToken(profile.id);
      await deleteSavedCredential(profile.id, 'admiral');
      const state = profileStateRef.current;
      await persist({ ...state, profiles: state.profiles.map((p) => (p.id === profile.id ? updated : p)) });
    }
    await enterAdmiralChecked(updated);
  }, [enterAdmiralChecked, persist, proxySessionEnded, requireProxyProfile]);

  const proxyChangeInstance = useCallback(async () => {
    const profile = requireProxyProfile();
    const pToken = proxyTokenRef.current;
    if (pToken) await runHook(async () => { await proxyFactoryRef.current(profile.url).logoutInstance(pToken); });
    setProxyStage(pToken ? 'instance' : 'portal');
    setStatus('signedOut');
  }, [requireProxyProfile]);

  const proxySignOut = useCallback(async () => {
    const profile = requireProxyProfile();
    setSignedOutByUser(true);
    if (statusRef.current === 'signedIn') await endAdmiralSession(true);
    const pToken = proxyTokenRef.current ?? await readProxyToken(profile.id);
    if (pToken) await runHook(() => proxyFactoryRef.current(profile.url).logout(pToken));
    clearSession();
    await deleteProxyToken(profile.id);
    await deleteToken(profile.id);
    setProxyToken(null);
    setProxyExpired(false);
    setProxyStage('portal');
    setStatus('signedOut');
  }, [clearSession, endAdmiralSession, requireProxyProfile, setProxyToken]);

  const skipKey = activeProfile && user?.user?.id ? passwordSkipKey(activeProfile.id, user.user.id) : null;
  const passwordChangeSkipped = !!skipKey && skips.includes(skipKey);

  const skipPasswordChange = useCallback(async () => {
    if (!skipKey) return;
    setSkips((prev) => (prev.includes(skipKey) ? prev : [...prev, skipKey]));
    await addPasswordSkip(skipKey);
  }, [skipKey]);

  const isAuthenticated = status === 'signedIn' && !!sessionToken && !!user;
  const requestHeaders = useMemo(
    () => (activeProfile?.kind === 'Proxy' ? proxySessionHeaders(proxyToken) : null),
    [activeProfile?.kind, proxyToken],
  );
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
    signedOutByUser,
    readSavedPassword,
    readSavedProxyPassword,
    forgetSavedPassword,
    updateSavedPassword,
    savePasswordOffer,
    acceptSavePasswordOffer,
    declineSavePasswordOffer,
    refresh,
    unlock,
    retry,
    saveProfile,
    deleteProfile,
    selectProfile,
    proxyStage: activeProfile?.kind === 'Proxy' && status !== 'signedIn' ? proxyStage : null,
    proxyToken,
    proxyExpired,
    requestHeaders,
    proxySignIn,
    proxyListInstances,
    proxySelectInstance,
    proxyChangeInstance,
    proxySignOut,
  }), [status, profileState.profiles, activeProfile, sessionToken, user, isAuthenticated, isAdmin, isTenantAdmin,
    passwordChangeSkipped, skipPasswordChange, login, logout, signedOutByUser, readSavedPassword, readSavedProxyPassword,
    forgetSavedPassword, updateSavedPassword, savePasswordOffer, acceptSavePasswordOffer, declineSavePasswordOffer, refresh, unlock, retry, saveProfile, deleteProfile, selectProfile,
    proxyStage, proxyToken, proxyExpired, requestHeaders, proxySignIn, proxyListInstances, proxySelectInstance,
    proxyChangeInstance, proxySignOut]);

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthState {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used within AuthProvider');
  return ctx;
}
