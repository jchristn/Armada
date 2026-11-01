import { createContext, useContext, useState, useEffect, useRef, useCallback, type ReactNode } from 'react';
import type { WebSocketMessage } from '../types/models';
import { useAuth } from './AuthContext';
import { ArmadaSocket, buildSocketUrl } from '../lib/armadaSocket';

type MessageHandler = (msg: WebSocketMessage) => void;

interface WebSocketState {
  connected: boolean;
  /** Increments every time the socket reconnects after a drop; pages refetch when it changes. */
  reconnectCount: number;
  /**
   * Increments after committed `usage.snapshot` events (bursts coalesce into one bump) and on reconnect. Usage
   * views refetch their latest/series reads when it changes; they never append points from the event itself.
   */
  usageRevision: number;
  subscribe: (handler: MessageHandler) => () => void;
  send: (data: unknown) => void;
}

const WebSocketContext = createContext<WebSocketState | null>(null);

/** Committed account usage observation event name. */
export const USAGE_SNAPSHOT_EVENT = 'usage.snapshot';
/** Window that coalesces a burst of usage events into one refetch. */
export const USAGE_EVENT_COALESCE_MS = 750;

/**
 * Owns the dashboard's single WebSocket. The connection authenticates with the current session token (sent as
 * the `token` query parameter), reconnects with backoff, and fans every event out to subscribers.
 */
export function WebSocketProvider({ children }: { children: ReactNode }) {
  const { isAuthenticated, sessionToken } = useAuth();
  const [connected, setConnected] = useState(false);
  const [reconnectCount, setReconnectCount] = useState(0);
  const [usageRevision, setUsageRevision] = useState(0);
  const usageTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const handlersRef = useRef<Set<MessageHandler>>(new Set());
  const socketRef = useRef<ArmadaSocket | null>(null);
  const tokenRef = useRef<string | null>(sessionToken);
  tokenRef.current = sessionToken;

  const subscribe = useCallback((handler: MessageHandler) => {
    handlersRef.current.add(handler);
    return () => {
      handlersRef.current.delete(handler);
    };
  }, []);

  const send = useCallback((data: unknown) => {
    socketRef.current?.send(data);
  }, []);

  useEffect(() => {
    if (!isAuthenticated) return undefined;
    const socket = new ArmadaSocket({
      url: () => buildSocketUrl(window.location, tokenRef.current),
      onMessage: (data) => {
        if (!data || typeof data !== 'object') return;
        if ((data as WebSocketMessage).type === USAGE_SNAPSHOT_EVENT && usageTimerRef.current === null) {
          usageTimerRef.current = setTimeout(() => {
            usageTimerRef.current = null;
            setUsageRevision((revision) => revision + 1);
          }, USAGE_EVENT_COALESCE_MS);
        }
        handlersRef.current.forEach((handler) => {
          try { handler(data as WebSocketMessage); } catch { /* one bad consumer must not break the others */ }
        });
      },
      onStatus: setConnected,
      onReconnected: () => {
        setReconnectCount((count) => count + 1);
        setUsageRevision((revision) => revision + 1);
      },
    });
    socketRef.current = socket;
    socket.start();
    return () => {
      socket.stop();
      if (usageTimerRef.current !== null) { clearTimeout(usageTimerRef.current); usageTimerRef.current = null; }
      if (socketRef.current === socket) socketRef.current = null;
    };
  }, [isAuthenticated, sessionToken]);

  return (
    <WebSocketContext.Provider value={{ connected, reconnectCount, usageRevision, subscribe, send }}>
      {children}
    </WebSocketContext.Provider>
  );
}

export function useWebSocket(): WebSocketState {
  const ctx = useContext(WebSocketContext);
  if (!ctx) throw new Error('useWebSocket must be used within WebSocketProvider');
  return ctx;
}

/** Usage refetch key; 0 outside a provider (tests, unauthenticated shells) so usage views still load once. */
export function useUsageRevision(): number {
  const ctx = useContext(WebSocketContext);
  return ctx?.usageRevision ?? 0;
}
