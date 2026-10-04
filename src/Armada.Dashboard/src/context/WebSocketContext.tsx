import { createContext, useContext, useState, useEffect, useRef, useCallback, type ReactNode } from 'react';
import type { WebSocketMessage } from '../types/models';
import { useAuth } from './AuthContext';
import { ArmadaSocket, buildSocketUrl } from '../lib/armadaSocket';

type MessageHandler = (msg: WebSocketMessage) => void;

interface WebSocketState {
  connected: boolean;
  /** Increments every time the socket reconnects after a drop; pages refetch when it changes. */
  reconnectCount: number;
  subscribe: (handler: MessageHandler) => () => void;
  send: (data: unknown) => void;
}

const WebSocketContext = createContext<WebSocketState | null>(null);

/**
 * Owns the dashboard's single WebSocket. The connection authenticates with the current session token (sent as
 * the `token` query parameter), reconnects with backoff, and fans every event out to subscribers.
 */
export function WebSocketProvider({ children }: { children: ReactNode }) {
  const { isAuthenticated, sessionToken } = useAuth();
  const [connected, setConnected] = useState(false);
  const [reconnectCount, setReconnectCount] = useState(0);
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
        handlersRef.current.forEach((handler) => {
          try { handler(data as WebSocketMessage); } catch { /* one bad consumer must not break the others */ }
        });
      },
      onStatus: setConnected,
      onReconnected: () => setReconnectCount((count) => count + 1),
    });
    socketRef.current = socket;
    socket.start();
    return () => {
      socket.stop();
      if (socketRef.current === socket) socketRef.current = null;
    };
  }, [isAuthenticated, sessionToken]);

  return (
    <WebSocketContext.Provider value={{ connected, reconnectCount, subscribe, send }}>
      {children}
    </WebSocketContext.Provider>
  );
}

export function useWebSocket(): WebSocketState {
  const ctx = useContext(WebSocketContext);
  if (!ctx) throw new Error('useWebSocket must be used within WebSocketProvider');
  return ctx;
}
