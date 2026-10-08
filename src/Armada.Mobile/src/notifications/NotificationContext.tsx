import AsyncStorage from '@react-native-async-storage/async-storage';
import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import type { WebSocketMessage } from '@dashboard/types/models';
import type { Severity } from '@dashboard/lib/notificationSeverity';
import {
  MAX_NOTIFICATIONS,
  TOAST_TIMEOUT_MS,
  buildEntityNotification,
  entityChangeDedupe,
  entityChangeFromMessage,
  newNotificationId,
  prependNotification,
  type Notification,
} from '@dashboard/lib/notificationEvents';
import type { AuthHooks } from '../auth/AuthContext';
import { useLocale } from '../i18n/LocaleContext';
import { useScreenReaderEnabled } from '../lib/accessibility';
import { useSocket } from '../socket/SocketContext';
import { PREF_KEYS, readPref, removePref, writePref } from '../storage/prefs';

export type { Notification, Severity };

/** With VoiceOver or TalkBack on, toasts stay this many times longer (reading one and reaching it takes time). */
export const SCREEN_READER_TOAST_FACTOR = 3;

export interface Toast {
  id: number;
  severity: Severity;
  message: string;
  /** App path opened when the toast is tapped. */
  href?: string | null;
}

export interface NotificationState {
  notifications: Notification[];
  unreadCount: number;
  toasts: Toast[];
  markRead: (id: string) => void;
  markAllRead: () => void;
  clearHistory: () => void;
  dismissToast: (id: number) => void;
  pushToast: (severity: Severity, message: string, href?: string | null) => void;
}

const NotificationContext = createContext<NotificationState | null>(null);

/** The app path a notification links to (mission, voyage, or captain), or null. */
export function notificationHref(n: Pick<Notification, 'missionId' | 'voyageId' | 'captainId'>): string | null {
  if (n.missionId) return `/missions/${encodeURIComponent(n.missionId)}`;
  if (n.voyageId) return `/voyages/${encodeURIComponent(n.voyageId)}`;
  if (n.captainId) return `/captains/${encodeURIComponent(n.captainId)}`;
  return null;
}

/** Storage key prefix of the per-profile, per-user notification history. */
export const NOTIFICATION_HISTORY_PREFIX = 'armada.notificationHistory.';

function keyPart(value: string): string {
  return value.replace(/[^A-Za-z0-9_-]/g, '_');
}

/**
 * The history scope of a signed-in session: one profile (server) and one user. History never crosses users or
 * servers: another user on the same phone, or another server, starts with an empty list.
 */
export function notificationScope(profileId: string, userId: string): string {
  return `${keyPart(profileId)}.${keyPart(userId)}`;
}

function historyKey(scope: string): string {
  return `${NOTIFICATION_HISTORY_PREFIX}${scope}`;
}

/**
 * Forget the stored notification history of a profile (every user who signed in to it). Called when a profile's
 * session ends (sign-out, profile removal, server change, rejected token): see createNotificationAuthHooks.
 */
export async function clearNotificationHistory(profileId: string): Promise<void> {
  const prefix = `${NOTIFICATION_HISTORY_PREFIX}${keyPart(profileId)}.`;
  try {
    const keys = await AsyncStorage.getAllKeys();
    const mine = keys.filter((key) => key.startsWith(prefix));
    if (mine.length > 0) await AsyncStorage.multiRemove(mine);
  } catch {
    // Best effort: the provider shows nothing for a session that is not signed in anyway.
  }
}

/** Auth hooks that forget a profile's notification history whenever its Admiral session ends. */
export function createNotificationAuthHooks(): AuthHooks {
  return { onSessionEnding: (profile) => clearNotificationHistory(profile.id) };
}

export interface NotificationProviderProps {
  children: ReactNode;
  /**
   * Whose history this is (notificationScope(profileId, userId)), or null while signed out: then nothing is shown or
   * stored. Defaults to one local scope (tests and previews).
   */
  scope?: string | null;
  /** Injectable timers for tests. */
  schedule?: (fn: () => void, ms: number) => unknown;
}

interface History {
  scope: string | null;
  items: Notification[];
  /** The stored history of this scope has been read (writes wait for it, so they never overwrite it). */
  loaded: boolean;
}

/**
 * In-app notification center and toasts. The events and text come from the dashboard's shared
 * lib/notificationEvents (the same notifications, worded the same, for the same WebSocket events); history is kept
 * on the device (preferences storage, capped) like the dashboard keeps it in localStorage, separately for each
 * profile and user (`scope`), and is cleared when that profile's session ends.
 */
export function NotificationProvider({ children, scope = 'local', schedule = (fn, ms) => setTimeout(fn, ms) }: NotificationProviderProps) {
  const { subscribe } = useSocket();
  const { t } = useLocale();
  const screenReader = useScreenReaderEnabled();
  const screenReaderRef = useRef(screenReader);
  useEffect(() => { screenReaderRef.current = screenReader; }, [screenReader]);
  const [history, setHistory] = useState<History>({ scope, items: [], loaded: false });
  const [toasts, setToasts] = useState<Toast[]>([]);
  const toastCounterRef = useRef(0);
  const lastSeenRef = useRef<Map<string, string>>(new Map());
  const tRef = useRef(t);
  const scopeRef = useRef(scope);

  useEffect(() => { tRef.current = t; }, [t]);
  useEffect(() => { scopeRef.current = scope; }, [scope]);

  // The history before it was scoped (one list for every user and server) is never shown again.
  useEffect(() => { void removePref(PREF_KEYS.notifications); }, []);

  // Load the scope's stored history; a change of scope (sign-out, another user or server) starts from that one.
  useEffect(() => {
    let cancelled = false;
    lastSeenRef.current.clear();
    const read: Promise<Notification[] | null> = scope ? readPref<Notification[]>(historyKey(scope)) : Promise.resolve(null);
    void read.then((stored) => {
      if (cancelled) return;
      const saved = Array.isArray(stored) ? stored : [];
      setHistory((prev) => ({
        scope,
        items: (prev.scope === scope ? [...prev.items, ...saved] : saved).slice(0, MAX_NOTIFICATIONS),
        loaded: true,
      }));
    });
    return () => { cancelled = true; };
  }, [scope]);

  useEffect(() => {
    if (history.loaded && history.scope) void writePref(historyKey(history.scope), history.items.slice(0, MAX_NOTIFICATIONS));
  }, [history]);

  const notifications = useMemo(() => (history.scope === scope ? history.items : []), [history, scope]);

  const updateItems = useCallback((change: (items: Notification[]) => Notification[]) => {
    setHistory((prev) => {
      const current = scopeRef.current;
      if (prev.scope !== current) return { scope: current, items: change([]), loaded: false };
      return { ...prev, items: change(prev.items) };
    });
  }, []);

  const pushToast = useCallback((severity: Severity, message: string, href: string | null = null) => {
    const id = ++toastCounterRef.current;
    setToasts((prev) => [...prev, { id, severity, message, href }]);
    const timeout = screenReaderRef.current ? TOAST_TIMEOUT_MS * SCREEN_READER_TOAST_FACTOR : TOAST_TIMEOUT_MS;
    schedule(() => setToasts((prev) => prev.filter((toast) => toast.id !== id)), timeout);
  }, [schedule]);

  useEffect(() => subscribe((msg: WebSocketMessage) => {
    const entry = entityChangeFromMessage(msg, tRef.current);
    if (!entry) return;
    const { key, seen } = entityChangeDedupe(entry);
    if (lastSeenRef.current.get(key) === seen) return;
    lastSeenRef.current.set(key, seen);
    const notification = buildEntityNotification(entry, tRef.current, newNotificationId(), new Date().toISOString());
    updateItems((prev) => prependNotification(prev, notification));
    pushToast(notification.severity, notification.message, notificationHref(notification));
  }), [subscribe, pushToast, updateItems]);

  const markRead = useCallback((id: string) => {
    updateItems((prev) => prev.map((n) => (n.id === id ? { ...n, read: true } : n)));
  }, [updateItems]);

  const markAllRead = useCallback(() => {
    updateItems((prev) => prev.map((n) => ({ ...n, read: true })));
  }, [updateItems]);

  const clearHistory = useCallback(() => {
    updateItems(() => []);
    lastSeenRef.current.clear();
  }, [updateItems]);

  const dismissToast = useCallback((id: number) => {
    setToasts((prev) => prev.filter((toast) => toast.id !== id));
  }, []);

  const value = useMemo<NotificationState>(() => ({
    notifications,
    unreadCount: notifications.filter((n) => !n.read).length,
    toasts,
    markRead,
    markAllRead,
    clearHistory,
    dismissToast,
    pushToast,
  }), [notifications, toasts, markRead, markAllRead, clearHistory, dismissToast, pushToast]);

  return <NotificationContext.Provider value={value}>{children}</NotificationContext.Provider>;
}

export function useNotifications(): NotificationState {
  const ctx = useContext(NotificationContext);
  if (!ctx) throw new Error('useNotifications must be used within NotificationProvider');
  return ctx;
}
