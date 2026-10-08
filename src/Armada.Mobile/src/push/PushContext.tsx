import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { AppState } from 'react-native';
import { sessionFor, storedSessionFor, type ServerSession } from '../api/serverSession';
import { useAuth, type AuthHooks } from '../auth/AuthContext';
import { useLocale } from '../i18n/LocaleContext';
import { notePendingLink } from '../navigation/pendingLink';
import { useApprovals } from '../notifications/ApprovalsContext';
import { PREF_KEYS, readPref, writePref } from '../storage/prefs';
import { defaultPushNative, type PushNative, type PushResponse } from './nativeAdapter';
import { parsePushData, responseAction, type PushPayload, type PushResponseAction } from './payload';
import { pushApi as defaultPushApi } from './pushApi';
import {
  flushRetiredDevices,
  isRetiredDevice,
  profileForDevice,
  registerDevice,
  reRegisterProfiles,
  unregisterDevice,
  updateDeviceCategories,
  type PermissionState,
  type RegistrationDeps,
  type RegistrationOutcome,
} from './registration';
import { secureRegistrationStore } from './registrationStore';
import type { PushCategory, PushTestResult } from './types';

/** A notification the user tapped (or acted on), waiting until the app can open it. */
export interface PendingPushResponse {
  payload: PushPayload;
  action: PushResponseAction;
  /** Profile the push came from (by device id), or null when the push names no device this app registered. */
  profileId: string | null;
  /**
   * True only when the push names a device this app registered with that profile's server. Untrusted pushes (no
   * deviceId, or one this app never registered) may be opened, never acted on.
   */
  trusted: boolean;
  receivedAt: number;
}

/** Pending responses older than this are dropped instead of opened later (and never acted on). */
export const PENDING_RESPONSE_TTL_MS = 10 * 60 * 1000;

export const PUSH_PROMPT_SEEN_KEY = `${PREF_KEYS.notifications}.promptSeen`;

export interface PushState {
  permission: PermissionState | 'unknown';
  /** Registration with the active profile's server (null until tried). */
  registration: RegistrationOutcome | null;
  /** Ask the OS (user-initiated), then register with the active server when granted. */
  requestPermission: () => Promise<PermissionState>;
  setCategoryEnabled: (category: PushCategory, enabled: boolean) => Promise<void>;
  sendTest: () => Promise<PushTestResult | null>;
  /** The one-time explanation sheet before the OS prompt. */
  promptVisible: boolean;
  dismissPrompt: () => void;
  /** The tapped notification waiting to be opened, if any; take it to handle it once. */
  pending: PendingPushResponse | null;
  takePending: () => PendingPushResponse | null;
}

const PushContext = createContext<PushState | null>(null);

export interface PushDeps extends RegistrationDeps {
  native: PushNative;
}

export function defaultPushDeps(): PushDeps {
  return { env: defaultPushNative.environment, api: defaultPushApi, store: secureRegistrationStore, native: defaultPushNative };
}

/** Auth hooks that remove this device's registration whenever a profile's Admiral session ends. */
export function createPushAuthHooks(deps: () => RegistrationDeps): AuthHooks {
  return {
    onSessionEnding: (profile, session) => unregisterDevice(deps(), profile.id, session),
  };
}

export interface PushProviderProps {
  children: ReactNode;
  deps?: PushDeps;
  now?: () => number;
}

/**
 * Push notifications for the signed-in profile: permission (asked from a user action, never at launch), registration
 * with the server on sign-in and on token changes, per-category settings, the app badge (the Approvals count), and
 * capture of notification taps and Approve / Deny actions (handled by PushResponseHandler once the app is ready).
 */
export function PushProvider({ children, deps: injected, now = Date.now }: PushProviderProps) {
  const deps = useMemo(() => injected ?? defaultPushDeps(), [injected]);
  const { status, activeProfile, profiles, sessionToken, proxyToken, user, mustChangePassword, selectProfile } = useAuth();
  const { count } = useApprovals();
  const { t } = useLocale();
  const [permission, setPermission] = useState<PermissionState | 'unknown'>('unknown');
  // Kept with the profile it belongs to, so switching servers never shows another server's registration.
  const [registrationEntry, setRegistration] = useState<{ profileId: string; outcome: RegistrationOutcome } | null>(null);
  const [promptSeen, setPromptSeen] = useState<boolean | null>(null);
  const [pending, setPending] = useState<PendingPushResponse | null>(null);

  const signedIn = status === 'signedIn' && !!sessionToken && !!activeProfile;
  const userId = user?.user?.id ?? null;
  const session: ServerSession | null = useMemo(
    () => (signedIn && activeProfile && sessionToken ? sessionFor(activeProfile, sessionToken, proxyToken) : null),
    [signedIn, activeProfile, sessionToken, proxyToken],
  );

  const activeProfileIdEarly = activeProfile?.id ?? null;
  const currentRegistration = registrationEntry && registrationEntry.profileId === activeProfileIdEarly ? registrationEntry.outcome : null;

  const profilesRef = useRef(profiles);
  const pendingRef = useRef<PendingPushResponse | null>(null);
  useEffect(() => { profilesRef.current = profiles; }, [profiles]);

  // Native setup once: foreground presentation, the Approve / Deny category, the Android channel.
  const labelsRef = useRef({ approve: t('Approve'), deny: t('Deny'), channel: t('Armada') });
  useEffect(() => {
    // Pushes for a device retired at sign-out (removal still pending on the server) are not shown in the foreground.
    deps.native.configurePresentation((deviceId) => isRetiredDevice(deps.store, deviceId));
    void deps.native.registerCategory(labelsRef.current.approve, labelsRef.current.deny);
    void deps.native.ensureChannel(labelsRef.current.channel);
  }, [deps]);

  // Permission now and on every return to the foreground (the user may have changed it in Settings).
  useEffect(() => {
    let cancelled = false;
    const read = () => { void deps.env.permission().then((p) => { if (!cancelled) setPermission(p); }); };
    read();
    const sub = AppState.addEventListener('change', (next) => { if (next === 'active') read(); });
    return () => { cancelled = true; sub.remove(); };
  }, [deps]);

  useEffect(() => {
    let cancelled = false;
    void readPref<boolean>(PUSH_PROMPT_SEEN_KEY).then((seen) => { if (!cancelled) setPromptSeen(!!seen); });
    return () => { cancelled = true; };
  }, []);

  // On sign-in: first finish removing devices retired while the server could not be reached (before registering,
  // which may hand back the same row), then register (and refresh on every return) once permission is granted.
  const activeProfileId = activeProfileIdEarly;
  useEffect(() => {
    if (!session || !activeProfileId) return undefined;
    let cancelled = false;
    void (async () => {
      await flushRetiredDevices(deps, activeProfileId, session);
      if (cancelled || permission !== 'granted') return;
      const outcome = await registerDevice(deps, activeProfileId, session, userId);
      if (!cancelled) setRegistration({ profileId: activeProfileId, outcome });
    })();
    return () => { cancelled = true; };
  }, [deps, session, activeProfileId, userId, permission]);

  // A new push token from the platform: re-register every profile that had registered.
  useEffect(() => {
    const sub = deps.native.addTokenListener(() => {
      void deps.env.expoToken().then(async (result) => {
        if (!result.token) return;
        const ids = profilesRef.current.map((p) => p.id);
        await reRegisterProfiles(deps, ids, async (profileId) => {
          const profile = profilesRef.current.find((p) => p.id === profileId);
          const stored = profile ? await storedSessionFor(profile) : null;
          const record = await deps.store.read(profileId);
          return stored ? { session: stored, userId: record?.userId ?? null } : null;
        }, result.token);
      });
    });
    return () => sub.remove();
  }, [deps]);

  // The app badge mirrors the Approvals count while signed in.
  useEffect(() => {
    if (status === 'loading') return;
    void deps.native.setBadge(signedIn ? count : 0);
  }, [deps, signedIn, status, count]);

  // Notification taps and actions, including the one that launched the app.
  const capture = useCallback(async (response: PushResponse) => {
    const payload = parsePushData(response.data);
    if (!payload) return;
    // A device this app signed out of (its server-side removal still pending): not handled at all.
    if (payload.deviceId && await isRetiredDevice(deps.store, payload.deviceId)) return;
    // Only a push naming a device this app registered (for exactly one profile) may carry Approve / Deny. The server
    // always sets deviceId, so a push without one did not come from an Admiral this app registered with (anyone
    // holding the Expo token can send one when the Expo project has no access token): it may only open its link.
    let profileId: string | null = null;
    if (payload.deviceId) {
      profileId = await profileForDevice(deps.store, profilesRef.current.map((p) => p.id), payload.deviceId);
    }
    const trusted = !!payload.deviceId && !!profileId;
    const next: PendingPushResponse = { payload, action: responseAction(response.actionIdentifier), profileId, trusted, receivedAt: now() };
    pendingRef.current = next;
    setPending(next);
  }, [deps, now]);

  useEffect(() => {
    const last = deps.native.getLastResponse();
    if (last) {
      void capture(last);
      deps.native.clearLastResponse();
    }
    const sub = deps.native.addResponseListener((response) => {
      void capture(response);
      deps.native.clearLastResponse();
    });
    return () => sub.remove();
  }, [deps, capture]);

  // Route the pending response: switch to the profile it came from; while signed out keep only the link (it opens
  // after sign-in) and drop any action, since acting later would surprise the user.
  useEffect(() => {
    const current = pending;
    if (!current) return undefined;
    let cancelled = false;
    // Decided after the render that delivered the change (state updates happen in the callback, not the effect body).
    void Promise.resolve().then(async () => {
      if (cancelled || pendingRef.current !== current) return;
      if (now() - current.receivedAt > PENDING_RESPONSE_TTL_MS) {
        pendingRef.current = null;
        setPending(null);
        return;
      }
      if (current.profileId && activeProfile && current.profileId !== activeProfile.id) {
        await selectProfile(current.profileId);
        return;
      }
      if (status === 'signedOut' || status === 'unreachable' || (status === 'signedIn' && mustChangePassword)) {
        if (current.payload.path) notePendingLink(current.payload.path);
        pendingRef.current = null;
        setPending(null);
      }
    });
    return () => { cancelled = true; };
  }, [pending, activeProfile, status, mustChangePassword, selectProfile, now]);

  const takePending = useCallback(() => {
    const value = pendingRef.current;
    pendingRef.current = null;
    setPending(null);
    return value;
  }, []);

  const requestPermission = useCallback(async () => {
    const result = await deps.native.requestPermission();
    setPermission(result);
    return result;
  }, [deps]);

  const setCategoryEnabled = useCallback(async (category: PushCategory, enabled: boolean) => {
    if (!session || !activeProfileId) return;
    const current = currentRegistration?.state === 'registered' ? currentRegistration.record.categories : [];
    const next = enabled ? Array.from(new Set([...current, category])) : current.filter((c) => c !== category);
    const outcome = await updateDeviceCategories(deps, activeProfileId, session, userId, next);
    setRegistration({ profileId: activeProfileId, outcome });
  }, [deps, session, activeProfileId, userId, currentRegistration]);

  const sendTest = useCallback(async () => {
    if (!session || currentRegistration?.state !== 'registered') return null;
    try {
      return await deps.api.sendTest(session, currentRegistration.record.deviceId);
    } catch {
      return null;
    }
  }, [deps, session, currentRegistration]);

  const dismissPrompt = useCallback(() => {
    setPromptSeen(true);
    void writePref(PUSH_PROMPT_SEEN_KEY, true);
  }, []);

  const ready = signedIn && !mustChangePassword;
  const promptVisible = ready && permission === 'undetermined' && promptSeen === false;

  const value = useMemo<PushState>(() => ({
    permission,
    registration: signedIn ? currentRegistration : null,
    requestPermission,
    setCategoryEnabled,
    sendTest,
    promptVisible,
    dismissPrompt,
    pending,
    takePending,
  }), [permission, signedIn, currentRegistration, requestPermission, setCategoryEnabled, sendTest, promptVisible, dismissPrompt, pending, takePending]);

  return <PushContext.Provider value={value}>{children}</PushContext.Provider>;
}

export function usePush(): PushState {
  const ctx = useContext(PushContext);
  if (!ctx) throw new Error('usePush must be used within PushProvider');
  return ctx;
}
