import { createContext, useContext, useState, useCallback, useEffect, useRef, type ReactNode } from 'react';
import { useWebSocket } from './WebSocketContext';
import { useLocale } from './LocaleContext';
import type { WebSocketMessage } from '../types/models';
import type { Severity } from '../lib/notificationSeverity';
import {
  MAX_NOTIFICATIONS,
  TOAST_TIMEOUT_MS,
  buildEntityNotification,
  entityChangeDedupe,
  entityChangeFromMessage,
  newNotificationId,
  prependNotification,
  type Notification,
} from '../lib/notificationEvents';

// ── Types ──

export type { Severity, Notification };

export interface Toast {
  id: number;
  severity: Severity;
  message: string;
  onClick?: () => void;
}

interface NotificationState {
  notifications: Notification[];
  unreadCount: number;
  toasts: Toast[];
  markRead: (id: string) => void;
  markAllRead: () => void;
  clearHistory: () => void;
  dismissToast: (id: number) => void;
  pushToast: (severity: Severity, message: string) => void;
}

const NotificationContext = createContext<NotificationState | null>(null);

// ── localStorage keys ──

const STORAGE_KEY = 'armada_notifications';

function loadStoredNotifications(): Notification[] {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (raw) return JSON.parse(raw) as Notification[];
  } catch {
    // ignore
  }
  return [];
}

function saveNotifications(notifications: Notification[]) {
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(notifications.slice(0, MAX_NOTIFICATIONS)));
  } catch {
    // ignore
  }
}

// ── Provider ──

export function NotificationProvider({ children }: { children: ReactNode }) {
  const { subscribe } = useWebSocket();
  const { t } = useLocale();

  const [notifications, setNotifications] = useState<Notification[]>(loadStoredNotifications);
  const [toasts, setToasts] = useState<Toast[]>([]);
  const toastCounterRef = useRef(0);
  // Track last-seen states to avoid duplicate notifications for the same state
  const lastSeenRef = useRef<Map<string, string>>(new Map());

  // Persist notifications to localStorage whenever they change
  useEffect(() => {
    saveNotifications(notifications);
  }, [notifications]);

  const unreadCount = notifications.filter(n => !n.read).length;

  // ── Subscribe to WebSocket state-change messages; each new state raises a notification and a toast ──

  useEffect(() => {
    const unsubscribe = subscribe((msg: WebSocketMessage) => {
      const entry = entityChangeFromMessage(msg, t);
      if (!entry) return;
      const { key, seen } = entityChangeDedupe(entry);
      if (lastSeenRef.current.get(key) === seen) return;
      lastSeenRef.current.set(key, seen);

      const notification = buildEntityNotification(entry, t, newNotificationId(), new Date().toISOString());
      setNotifications(prev => prependNotification(prev, notification));

      const toastId = ++toastCounterRef.current;
      const toast: Toast = { id: toastId, severity: notification.severity, message: notification.message };
      setToasts(prev => [...prev, toast]);
      setTimeout(() => {
        setToasts(prev => prev.filter(t => t.id !== toastId));
      }, TOAST_TIMEOUT_MS);
    });
    return unsubscribe;
  }, [subscribe, t]);

  // ── Actions ──

  const markRead = useCallback((id: string) => {
    setNotifications(prev => prev.map(n => n.id === id ? { ...n, read: true } : n));
  }, []);

  const markAllRead = useCallback(() => {
    setNotifications(prev => prev.map(n => ({ ...n, read: true })));
  }, []);

  const clearHistory = useCallback(() => {
    setNotifications([]);
    lastSeenRef.current.clear();
  }, []);

  const dismissToast = useCallback((id: number) => {
    setToasts(prev => prev.filter(t => t.id !== id));
  }, []);

  const pushToast = useCallback((severity: Severity, message: string) => {
    const toastId = ++toastCounterRef.current;
    const toast: Toast = { id: toastId, severity, message };
    setToasts(prev => [...prev, toast]);
    setTimeout(() => {
      setToasts(prev => prev.filter(t => t.id !== toastId));
    }, TOAST_TIMEOUT_MS);
  }, []);

  return (
    <NotificationContext.Provider value={{
      notifications,
      unreadCount,
      toasts,
      markRead,
      markAllRead,
      clearHistory,
      dismissToast,
      pushToast,
    }}>
      {children}
    </NotificationContext.Provider>
  );
}

export function useNotifications(): NotificationState {
  const ctx = useContext(NotificationContext);
  if (!ctx) throw new Error('useNotifications must be used within NotificationProvider');
  return ctx;
}
