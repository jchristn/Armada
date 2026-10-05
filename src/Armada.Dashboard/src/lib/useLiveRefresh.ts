import { useEffect, useRef } from 'react';
import { useWebSocket } from '../context/WebSocketContext';
import type { WebSocketMessage } from '../types/models';

/** Delay that coalesces a burst of events (a voyage landing emits several mission events at once) into one reload. */
export const LIVE_REFRESH_DEBOUNCE_MS = 300;

/**
 * Reload a detail view when the server reports a change it may show: calls `onRefresh` (debounced) for every
 * WebSocket event whose type starts with one of `typePrefixes` (for example `mission.` and `voyage.`), and once
 * after the socket reconnects, since events sent while it was down are lost. The latest `onRefresh` is always used.
 */
export function useLiveRefresh(typePrefixes: readonly string[], onRefresh: () => void): void {
  const { subscribe, reconnectCount } = useWebSocket();
  const refreshRef = useRef(onRefresh);
  refreshRef.current = onRefresh;
  const prefixKey = typePrefixes.join('|');

  useEffect(() => {
    const prefixes = prefixKey.split('|').filter(Boolean);
    let timer: ReturnType<typeof setTimeout> | null = null;
    const unsubscribe = subscribe((message: WebSocketMessage) => {
      if (!prefixes.some((prefix) => message.type.startsWith(prefix))) return;
      if (timer) clearTimeout(timer);
      timer = setTimeout(() => {
        timer = null;
        refreshRef.current();
      }, LIVE_REFRESH_DEBOUNCE_MS);
    });
    return () => {
      if (timer) clearTimeout(timer);
      unsubscribe();
    };
  }, [subscribe, prefixKey]);

  const firstReconnectCount = useRef(reconnectCount);
  useEffect(() => {
    if (reconnectCount !== firstReconnectCount.current) refreshRef.current();
  }, [reconnectCount]);
}
