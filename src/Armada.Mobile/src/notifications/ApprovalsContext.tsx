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
import { useSocket } from '../socket/SocketContext';

export interface ApprovalsState extends InboxCountState {
  refresh: () => Promise<void>;
}

const ApprovalsContext = createContext<ApprovalsState | null>(null);

/**
 * The Approvals badge: the same "Needs You" count the dashboard sidebar shows (shared lib/inboxSummary), polled on
 * the dashboard's interval while the app is in the foreground and refreshed (throttled) on WebSocket activity and
 * on every reconnect or return to the foreground.
 */
export function ApprovalsProvider({ children, enabled }: { children: ReactNode; enabled: boolean }) {
  const { subscribe, reconnectCount } = useSocket();
  const [items, setItems] = useState<InboxItem[]>([]);
  const lastLoadRef = useRef(0);

  const load = useCallback((): Promise<void> => getInbox()
    .then((result) => {
      lastLoadRef.current = Date.now();
      setItems(Array.isArray(result) ? result : []);
    })
    .catch(() => {
      // Best-effort: a failed poll leaves the last known count in place.
    }), []);

  useEffect(() => {
    if (!enabled) return undefined;
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
