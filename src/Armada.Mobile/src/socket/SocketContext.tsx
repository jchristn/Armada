import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { AppState, type AppStateStatus } from 'react-native';
import { ArmadaSocket, buildSocketUrlForServer, type SocketFactory } from '@dashboard/lib/armadaSocket';
import type { WebSocketMessage } from '@dashboard/types/models';

type MessageHandler = (msg: WebSocketMessage) => void;

export interface SocketState {
  connected: boolean;
  /** Increments after every reconnect and every return to the foreground; screens refetch when it changes. */
  reconnectCount: number;
  subscribe: (handler: MessageHandler) => () => void;
  send: (data: unknown) => void;
}

const SocketContext = createContext<SocketState | null>(null);

export interface SocketProviderProps {
  children: ReactNode;
  /** Server base URL; no connection while null. */
  serverUrl: string | null;
  /** Session token; no connection while null. */
  token: string | null;
  /** Injectable for tests (defaults to the global WebSocket). */
  factory?: SocketFactory;
}

/**
 * The app's single WebSocket, using the dashboard's shared ArmadaSocket (token in the query string, exponential
 * backoff with jitter, resubscribe on open). Mobile adds the app lifecycle: the socket closes when the app goes
 * to the background (iOS would suspend it anyway) and reopens on return to the foreground, which also bumps
 * `reconnectCount` so screens refetch what they missed.
 */
export function SocketProvider({ children, serverUrl, token, factory }: SocketProviderProps) {
  const [connected, setConnected] = useState(false);
  const [reconnectCount, setReconnectCount] = useState(0);
  const handlersRef = useRef<Set<MessageHandler>>(new Set());
  const socketRef = useRef<ArmadaSocket | null>(null);

  const subscribe = useCallback((handler: MessageHandler) => {
    handlersRef.current.add(handler);
    return () => { handlersRef.current.delete(handler); };
  }, []);

  const send = useCallback((data: unknown) => {
    socketRef.current?.send(data);
  }, []);

  useEffect(() => {
    if (!serverUrl || !token) return undefined;
    const socket = new ArmadaSocket({
      url: () => buildSocketUrlForServer(serverUrl, token),
      onMessage: (data) => {
        if (!data || typeof data !== 'object') return;
        handlersRef.current.forEach((handler) => {
          try { handler(data as WebSocketMessage); } catch { /* one bad consumer must not break the others */ }
        });
      },
      onStatus: setConnected,
      onReconnected: () => setReconnectCount((count) => count + 1),
      factory,
    });
    socketRef.current = socket;
    if (AppState.currentState !== 'background') socket.start();

    let wasBackground = AppState.currentState === 'background';
    const sub = AppState.addEventListener('change', (next: AppStateStatus) => {
      if (next === 'background') {
        wasBackground = true;
        socket.stop();
      } else if (next === 'active' && wasBackground) {
        wasBackground = false;
        socket.start();
        setReconnectCount((count) => count + 1);
      }
    });

    return () => {
      sub.remove();
      socket.stop();
      if (socketRef.current === socket) socketRef.current = null;
    };
  }, [serverUrl, token, factory]);

  const value = useMemo(() => ({ connected, reconnectCount, subscribe, send }), [connected, reconnectCount, subscribe, send]);
  return <SocketContext.Provider value={value}>{children}</SocketContext.Provider>;
}

export function useSocket(): SocketState {
  const ctx = useContext(SocketContext);
  if (!ctx) throw new Error('useSocket must be used within SocketProvider');
  return ctx;
}
