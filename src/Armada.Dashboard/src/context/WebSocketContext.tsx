import { createContext, useContext, useState, useEffect, useRef, useCallback, type ReactNode } from 'react';
import type { WebSocketMessage } from '../types/models';
import { getStatus } from '../api/client';
import { useAuth } from './AuthContext';

type MessageHandler = (msg: WebSocketMessage) => void;

interface WebSocketState {
  connected: boolean;
  subscribe: (handler: MessageHandler) => () => void;
  send: (data: unknown) => void;
}

const WebSocketContext = createContext<WebSocketState | null>(null);

const RECONNECT_DELAY = 3000;

export function WebSocketProvider({ children }: { children: ReactNode }) {
  const { isAuthenticated } = useAuth();
  const [connected, setConnected] = useState(false);
  const wsRef = useRef<WebSocket | null>(null);
  const handlersRef = useRef<Set<MessageHandler>>(new Set());
  const reconnectTimerRef = useRef<number | null>(null);
  const wsPortRef = useRef<number | null>(null);
  const mountedRef = useRef(true);

  const subscribe = useCallback((handler: MessageHandler) => {
    handlersRef.current.add(handler);
    return () => {
      handlersRef.current.delete(handler);
    };
  }, []);

  const send = useCallback((data: unknown) => {
    if (wsRef.current?.readyState === WebSocket.OPEN) {
      wsRef.current.send(JSON.stringify(data));
    }
  }, []);

  const connectWs = useCallback((port: number) => {
    if (!mountedRef.current) return;
    try {
      const protocol = window.location.protocol === 'https:' ? 'wss:' : 'ws:';
      const url = `${protocol}//${window.location.hostname}:${port}`;
      const ws = new WebSocket(url);

      ws.onopen = () => {
        if (!mountedRef.current) { ws.close(); return; }
        setConnected(true);
        ws.send(JSON.stringify({ Route: 'subscribe' }));
      };

      ws.onmessage = (evt) => {
        try {
          const data = JSON.parse(evt.data) as WebSocketMessage;
          // Camelize the type field but pass data through
          handlersRef.current.forEach(handler => handler(data));
        } catch {
          // ignore parse errors
        }
      };

      ws.onclose = () => {
        if (!mountedRef.current) return;
        setConnected(false);
        wsRef.current = null;
        reconnectTimerRef.current = window.setTimeout(() => {
          if (mountedRef.current) connectWs(port);
        }, RECONNECT_DELAY);
      };

      ws.onerror = () => {
        setConnected(false);
      };

      wsRef.current = ws;
    } catch {
      setConnected(false);
      reconnectTimerRef.current = window.setTimeout(() => {
        if (mountedRef.current) connectWs(port);
      }, RECONNECT_DELAY);
    }
  }, []);

  useEffect(() => {
    mountedRef.current = true;

    if (!isAuthenticated) {
      return;
    }

    // Get WS port from status endpoint
    getStatus()
      .then((status) => {
        const port = (status.wsPort as number) || 7892;
        wsPortRef.current = port;
        connectWs(port);
      })
      .catch(() => {
        // Default port fallback
        wsPortRef.current = 7892;
        connectWs(7892);
      });

    return () => {
      mountedRef.current = false;
      if (reconnectTimerRef.current) {
        clearTimeout(reconnectTimerRef.current);
        reconnectTimerRef.current = null;
      }
      if (wsRef.current) {
        wsRef.current.close();
        wsRef.current = null;
      }
    };
  }, [isAuthenticated, connectWs]);

  return (
    <WebSocketContext.Provider value={{ connected, subscribe, send }}>
      {children}
    </WebSocketContext.Provider>
  );
}

export function useWebSocket(): WebSocketState {
  const ctx = useContext(WebSocketContext);
  if (!ctx) throw new Error('useWebSocket must be used within WebSocketProvider');
  return ctx;
}
