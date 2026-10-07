import { useEffect, useRef } from 'react';
import type { WebSocketMessage } from '@dashboard/types/models';
import { useSocket } from '../socket/SocketContext';

/** Delay that coalesces a burst of events (a voyage landing emits several mission events) into one reload. */
export const LIVE_REFRESH_DEBOUNCE_MS = 300;

/** True when a socket message type starts with one of the prefixes (an empty list matches every message). */
export function matchesLivePrefixes(type: string | null | undefined, prefixes: readonly string[]): boolean {
  if (prefixes.length === 0) return true;
  const value = type ?? '';
  return prefixes.some((prefix) => value.startsWith(prefix));
}

/**
 * The mobile form of the dashboard's lib/useLiveRefresh: calls `onRefresh` (debounced) for every WebSocket event
 * whose type starts with one of `typePrefixes`, and once after every reconnect or return to the foreground (events
 * sent while the socket was down are lost). The latest `onRefresh` is always used. Pass `enabled: false` to pause.
 */
export function useLiveRefresh(typePrefixes: readonly string[], onRefresh: () => void, enabled = true): void {
  const { subscribe, reconnectCount } = useSocket();
  const refreshRef = useRef(onRefresh);
  useEffect(() => { refreshRef.current = onRefresh; });
  const prefixKey = typePrefixes.join('|');

  useEffect(() => {
    if (!enabled) return undefined;
    const prefixes = prefixKey.split('|').filter(Boolean);
    let timer: ReturnType<typeof setTimeout> | null = null;
    const unsubscribe = subscribe((message: WebSocketMessage) => {
      if (!matchesLivePrefixes(message?.type, prefixes)) return;
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
  }, [subscribe, prefixKey, enabled]);

  const firstReconnectCount = useRef(reconnectCount);
  useEffect(() => {
    if (enabled && reconnectCount !== firstReconnectCount.current) refreshRef.current();
  }, [reconnectCount, enabled]);
}
