import { createContext, useContext, useState, useCallback, useEffect, useRef, type ReactNode } from 'react';
import { useWebSocket } from './WebSocketContext';
import { useLocale } from './LocaleContext';
import type { WebSocketMessage } from '../types/models';
import { entityStatusSeverity, type NotificationEntityKind, type Severity } from '../lib/notificationSeverity';
import { deploymentApprovalLabel } from '../lib/deploymentApprovalLabel';

// ── Types ──

export type { Severity };

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
  pushToast: (severity: Severity, message: string) => void;
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

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null;
}

// ── Provider ──

const TOAST_TIMEOUT = 5000;

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

  // ── Push a notification + toast (matches legacy _notifyStateChange) ──

  const pushNotification = useCallback((
    assetType: NotificationEntityKind,
    id: string,
    name: string,
    status: string,
    verificationStatus: string | null = null,
  ) => {
    const key = `${assetType}:${id}`;
    const seen = verificationStatus ? `${status}|${verificationStatus}` : status;
    if (lastSeenRef.current.get(key) === seen) return;
    lastSeenRef.current.set(key, seen);

    // Severity comes from the typed status values; the display text below is built only for people to read.
    const severity = entityStatusSeverity(assetType, status, verificationStatus);
    const statusText = verificationStatus ? `${t(status)} / ${t(verificationStatus)}` : t(status);
    const truncatedName = name.length > 80 ? name.substring(0, 80) + '...' : name;
    // Keyed templates: the entity name is a parameter, so it is inserted verbatim and never translated.
    const title = t('{{entity}} {{status}}', { entity: t(assetType), status: statusText });
    const message = t('{{entity}} "{{name}}" - {{status}}', { entity: t(assetType), name: truncatedName, status: statusText });

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
  }, [t]);

  // ── Subscribe to WebSocket state-change messages ──

  useEffect(() => {
    const unsubscribe = subscribe((msg: WebSocketMessage) => {
      const data = msg.data;
      if (!isRecord(data)) return;

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

      if (msg.type === 'deployment.changed' && data.status) {
        // A deployment waiting for approval reads like every other approval surface: environment first.
        const deploymentName = data.status === 'PendingApproval'
          ? deploymentApprovalLabel(
            t,
            typeof data.environmentName === 'string' ? data.environmentName : null,
            typeof data.title === 'string' ? data.title : null,
            typeof data.id === 'string' ? data.id : null,
          )
          : String(data.title || data.id || '');
        pushNotification(
          'Deployment',
          String(data.id || ''),
          deploymentName,
          String(data.status),
          typeof data.verificationStatus === 'string' ? data.verificationStatus : null,
        );
      }

      if (msg.type === 'objective.changed' && data.status) {
        pushNotification(
          'Objective',
          String(data.id || ''),
          String(data.title || data.id || ''),
          String(data.status),
        );
      }

      if (msg.type === 'incident.changed' && data.status) {
        pushNotification(
          'Incident',
          String(data.id || ''),
          String(data.title || data.id || ''),
          String(data.status),
        );
      }
    });
    return unsubscribe;
  }, [subscribe, pushNotification, t]);

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
    }, TOAST_TIMEOUT);
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
