import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { AppState } from 'react-native';
import { getInbox } from '@dashboard/api/client';
import {
  INBOX_POLL_INTERVAL_MS,
  shouldRefreshInboxOnSocket,
  summarizeInbox,
  type InboxCountState,
} from '@dashboard/lib/inboxSummary';
import type { InboxItem } from '@dashboard/types/models';
import { inboxItemTitle } from '@dashboard/lib/deploymentApprovalLabel';
import { getOpenAskThread } from '../ask/openThread';
import { approvalKey, newApprovalItems } from '../approvals/approvalToasts';
import { useLocale } from '../i18n/LocaleContext';
import { useSocket } from '../socket/SocketContext';
import { useNotifications } from './NotificationContext';

export interface ApprovalsState extends InboxCountState {
  refresh: () => Promise<void>;
}

const ApprovalsContext = createContext<ApprovalsState | null>(null);

/**
 * The Approvals badge: the same "Needs You" count the dashboard sidebar shows (shared lib/inboxSummary), polled on
 * the dashboard's interval while the app is in the foreground and refreshed (throttled) on WebSocket activity and
 * on every reconnect or return to the foreground. Its items feed the approvals center, and a new item that waits on
 * a decision raises an in-app toast that opens the approvals center.
 */
export function ApprovalsProvider({ children, enabled }: { children: ReactNode; enabled: boolean }) {
  const { subscribe, reconnectCount } = useSocket();
  const { pushToast } = useNotifications();
  const { t } = useLocale();
  const [items, setItems] = useState<InboxItem[]>([]);
  const lastLoadRef = useRef(0);
  const seenRef = useRef<Set<string> | null>(null);
  const tRef = useRef(t);
  // Read through refs so a new toast (which re-renders the notification provider) does not restart the poll.
  const pushToastRef = useRef(pushToast);
  useEffect(() => { tRef.current = t; pushToastRef.current = pushToast; }, [t, pushToast]);

  const load = useCallback((): Promise<void> => getInbox()
    .then((result) => {
      lastLoadRef.current = Date.now();
      const list = Array.isArray(result) ? result : [];
      for (const item of newApprovalItems(seenRef.current, list, getOpenAskThread())) {
        pushToastRef.current(item.severity === 'Critical' ? 'error' : 'warning', tRef.current('Approval needed: {{title}}', { title: inboxItemTitle(tRef.current, item) }), '/approvals');
      }
      seenRef.current = new Set(list.map(approvalKey));
      setItems(list);
    })
    .catch(() => {
      // Best-effort: a failed poll leaves the last known count in place.
    }), []);

  useEffect(() => {
    if (!enabled) { seenRef.current = null; return undefined; }
    void load();
    const timer = setInterval(() => {
      if (AppState.currentState === 'active') void load();
    }, INBOX_POLL_INTERVAL_MS);
    return () => clearInterval(timer);
  }, [enabled, load, reconnectCount]);

  useEffect(() => {
    if (!enabled) return undefined;
    return subscribe(() => {
      if (shouldRefreshInboxOnSocket(lastLoadRef.current, Date.now())) void load();
    });
  }, [enabled, subscribe, load]);

  // Signed out: nothing is counted (the last session's items are not shown).
  const value = useMemo<ApprovalsState>(() => ({ ...summarizeInbox(enabled ? items : []), refresh: load }), [enabled, items, load]);
  return <ApprovalsContext.Provider value={value}>{children}</ApprovalsContext.Provider>;
}

export function useApprovals(): ApprovalsState {
  const ctx = useContext(ApprovalsContext);
  if (!ctx) throw new Error('useApprovals must be used within ApprovalsProvider');
  return ctx;
}
