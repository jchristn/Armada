import { useEffect, useRef } from 'react';
import { useWebSocket } from '../context/WebSocketContext';
import type { WebSocketMessage } from '../types/models';
import { BLACKBOX_EVENT_PREFIX, blackBoxEventMatches } from './blackbox';

/** Delay that coalesces a burst of black box events (captured, then analyzed) into one reload. */
export const BLACKBOX_LIVE_DEBOUNCE_MS = 300;

/**
 * Reload a black box view when the server reports a black box event (blackbox.captured, .analyzed, .resolved,
 * .deleted) about one of `ids` (an entry, mission or voyage id; no ids follows every event), and once after the
 * socket reconnects. The latest `onEvent` is always used.
 */
export function useBlackBoxLive(ids: ReadonlyArray<string | null | undefined>, onEvent: (type: string) => void): void {
  const { subscribe, reconnectCount } = useWebSocket();
  const callbackRef = useRef(onEvent);
  callbackRef.current = onEvent;
  const idKey = ids.filter(Boolean).join('|');

  useEffect(() => {
    const wanted = idKey ? idKey.split('|') : [];
    let timer: ReturnType<typeof setTimeout> | null = null;
    const unsubscribe = subscribe((message: WebSocketMessage) => {
      if (!message.type || !message.type.startsWith(BLACKBOX_EVENT_PREFIX)) return;
      if (!blackBoxEventMatches(message.data, wanted)) return;
      if (timer) clearTimeout(timer);
      const type = message.type;
      timer = setTimeout(() => {
        timer = null;
        callbackRef.current(type);
      }, BLACKBOX_LIVE_DEBOUNCE_MS);
    });
    return () => {
      if (timer) clearTimeout(timer);
      unsubscribe();
    };
  }, [subscribe, idKey]);

  const firstReconnect = useRef(reconnectCount);
  useEffect(() => {
    if (reconnectCount !== firstReconnect.current) callbackRef.current('reconnect');
  }, [reconnectCount]);
}
