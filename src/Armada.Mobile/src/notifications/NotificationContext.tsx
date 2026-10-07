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
import { useLocale } from '../i18n/LocaleContext';
import { useSocket } from '../socket/SocketContext';
import { PREF_KEYS, readPref, writePref } from '../storage/prefs';

export type { Notification, Severity };

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

export interface NotificationProviderProps {
  children: ReactNode;
  /** Injectable timers for tests. */
  schedule?: (fn: () => void, ms: number) => unknown;
}

/**
 * In-app notification center and toasts. The events and text come from the dashboard's shared
 * lib/notificationEvents (the same notifications, worded the same, for the same WebSocket events); history is kept
 * on the device (preferences storage, capped) like the dashboard keeps it in localStorage.
 */
export function NotificationProvider({ children, schedule = (fn, ms) => setTimeout(fn, ms) }: NotificationProviderProps) {
  const { subscribe } = useSocket();
  const { t } = useLocale();
  const [notifications, setNotifications] = useState<Notification[]>([]);
  const [toasts, setToasts] = useState<Toast[]>([]);
  const [loaded, setLoaded] = useState(false);
  const toastCounterRef = useRef(0);
  const lastSeenRef = useRef<Map<string, string>>(new Map());
  const tRef = useRef(t);

  useEffect(() => { tRef.current = t; }, [t]);

  useEffect(() => {
    let cancelled = false;
    void readPref<Notification[]>(PREF_KEYS.notifications).then((stored) => {
      if (cancelled) return;
      if (Array.isArray(stored)) setNotifications((current) => [...current, ...stored].slice(0, MAX_NOTIFICATIONS));
      setLoaded(true);
    });
    return () => { cancelled = true; };
  }, []);

  useEffect(() => {
    if (loaded) void writePref(PREF_KEYS.notifications, notifications.slice(0, MAX_NOTIFICATIONS));
  }, [notifications, loaded]);

  const pushToast = useCallback((severity: Severity, message: string, href: string | null = null) => {
    const id = ++toastCounterRef.current;
    setToasts((prev) => [...prev, { id, severity, message, href }]);
    schedule(() => setToasts((prev) => prev.filter((toast) => toast.id !== id)), TOAST_TIMEOUT_MS);
  }, [schedule]);

  useEffect(() => subscribe((msg: WebSocketMessage) => {
    const entry = entityChangeFromMessage(msg, tRef.current);
    if (!entry) return;
    const { key, seen } = entityChangeDedupe(entry);
    if (lastSeenRef.current.get(key) === seen) return;
    lastSeenRef.current.set(key, seen);
    const notification = buildEntityNotification(entry, tRef.current, newNotificationId(), new Date().toISOString());
    setNotifications((prev) => prependNotification(prev, notification));
    pushToast(notification.severity, notification.message, notificationHref(notification));
  }), [subscribe, pushToast]);

  const markRead = useCallback((id: string) => {
    setNotifications((prev) => prev.map((n) => (n.id === id ? { ...n, read: true } : n)));
  }, []);

  const markAllRead = useCallback(() => {
    setNotifications((prev) => prev.map((n) => ({ ...n, read: true })));
  }, []);

  const clearHistory = useCallback(() => {
    setNotifications([]);
    lastSeenRef.current.clear();
  }, []);

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
