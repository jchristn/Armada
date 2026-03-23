import { createContext, useContext, useState, useCallback, useEffect, useRef, type ReactNode } from 'react';
import { useWebSocket } from './WebSocketContext';
import type { WebSocketMessage } from '../types/models';

// ── Types ──

export type Severity = 'info' | 'success' | 'warning' | 'error';

export interface Notification {
  id: string;
  severity: Severity;
  title: string;
  message: string;
  timestampUtc: string;
  missionId: string | null;
  voyageId: string | null;
  captainId: string | null;
  read: boolean;
}

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
}

const NotificationContext = createContext<NotificationState | null>(null);

// ── localStorage keys ──

const STORAGE_KEY = 'armada_notifications';
const MAX_NOTIFICATIONS = 100;

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

// ── Severity mapping (matches legacy _stateToastSeverity) ──

function statusToSeverity(status: string): Severity {
  if (!status) return 'info';
  const s = status.toLowerCase();
  if (s === 'completed' || s === 'complete' || s === 'landed' || s === 'passed') return 'success';
  if (s === 'failed' || s === 'error') return 'error';
  if (s === 'cancelled' || s === 'stalled' || s === 'stopping') return 'warning';
  return 'info';
}

// ── Provider ──

const TOAST_TIMEOUT = 5000;

export function NotificationProvider({ children }: { children: ReactNode }) {
  const { subscribe } = useWebSocket();

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

  // ── Push a notification + toast (matches legacy _notifyStateChange) ──

  const pushNotification = useCallback((
    assetType: string,
    id: string,
    name: string,
    status: string,
  ) => {
    const key = `${assetType}:${id}`;
    if (lastSeenRef.current.get(key) === status) return;
    lastSeenRef.current.set(key, status);

    const truncatedName = name.length > 80 ? name.substring(0, 80) + '...' : name;
    const title = `${assetType} ${status}`;
    const message = `${assetType} "${truncatedName}" \u2014 ${status}`;
    const severity = statusToSeverity(status);

    const notification: Notification = {
      id: `ntf_${Date.now()}_${Math.random().toString(36).substring(2, 8)}`,
      severity,
      title,
      message,
      timestampUtc: new Date().toISOString(),
      missionId: assetType === 'Mission' ? id : null,
      voyageId: assetType === 'Voyage' ? id : null,
      captainId: assetType === 'Captain' ? id : null,
      read: false,
    };

    setNotifications(prev => {
      const next = [notification, ...prev];
      if (next.length > MAX_NOTIFICATIONS) next.length = MAX_NOTIFICATIONS;
      return next;
    });

    // Toast
    const toastId = ++toastCounterRef.current;
    const toast: Toast = { id: toastId, severity, message };
    setToasts(prev => [...prev, toast]);
    setTimeout(() => {
      setToasts(prev => prev.filter(t => t.id !== toastId));
    }, TOAST_TIMEOUT);
  }, []);

  // ── Subscribe to WebSocket state-change messages ──

  useEffect(() => {
    const unsubscribe = subscribe((msg: WebSocketMessage) => {
      const data = msg.data;
      if (!data) return;

      // Mission state changes (matches legacy: data.type === 'mission.changed')
      if (msg.type === 'mission.changed' && data.status) {
        pushNotification(
          'Mission',
          String(data.id || ''),
          String(data.title || data.id || ''),
          String(data.status),
        );
      }

      // Voyage state changes
      if (msg.type === 'voyage.changed' && data.status) {
        pushNotification(
          'Voyage',
          String(data.id || ''),
          String(data.title || data.id || ''),
          String(data.status),
        );
      }

      // Captain state changes (legacy uses c.state, not c.status)
      if (msg.type === 'captain.changed' && (data.state || data.status)) {
        pushNotification(
          'Captain',
          String(data.id || ''),
          String(data.name || data.id || ''),
          String(data.state || data.status),
        );
      }
    });
    return unsubscribe;
  }, [subscribe, pushNotification]);

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

  return (
    <NotificationContext.Provider value={{
      notifications,
      unreadCount,
      toasts,
      markRead,
      markAllRead,
      clearHistory,
      dismissToast,
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
