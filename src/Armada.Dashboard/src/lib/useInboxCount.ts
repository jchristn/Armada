import { useCallback, useEffect, useRef, useState } from 'react';
import { getInbox } from '../api/client';
import type { InboxItem } from '../types/models';
import { useWebSocket } from '../context/WebSocketContext';
import {
  INBOX_POLL_INTERVAL_MS,
  shouldRefreshInboxOnSocket,
  summarizeInbox,
  type InboxCountState,
} from './inboxSummary';

export type { InboxCountState };

/**
 * Live "Needs You" attention count. Polls the consolidated inbox (missions in review, failed
 * landings/missions, failed merges, deployments awaiting approval, stalled captains) on an interval and
 * refreshes promptly (throttled) when WebSocket activity arrives, so the sidebar badge tracks work that
 * needs a human without the user opening the Needs You page. The counting rules live in lib/inboxSummary.ts,
 * shared with the mobile app's Approvals badge.
 */
export function useInboxCount(): InboxCountState {
  const { subscribe } = useWebSocket();
  const [items, setItems] = useState<InboxItem[]>([]);
  const lastLoadRef = useRef(0);

  const load = useCallback(async () => {
    try {
      const result = await getInbox();
      lastLoadRef.current = Date.now();
      setItems(Array.isArray(result) ? result : []);
    } catch {
      // Best-effort: a failed poll leaves the last known count in place.
    }
  }, []);

  useEffect(() => {
    void load();
    const timer = window.setInterval(() => { void load(); }, INBOX_POLL_INTERVAL_MS);
    return () => window.clearInterval(timer);
  }, [load]);

  useEffect(() => {
    return subscribe(() => {
      if (!shouldRefreshInboxOnSocket(lastLoadRef.current, Date.now())) return;
      void load();
    });
  }, [subscribe, load]);

  return summarizeInbox(items);
}
