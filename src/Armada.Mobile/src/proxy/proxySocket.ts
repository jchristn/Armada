import type { SocketFactory, SocketLike } from '@dashboard/lib/armadaSocket';
import { PROXY_SESSION_HEADER } from './proxyApi';
import { base64UrlUtf8 } from './sha256';

/**
 * The `/ws` connection through Armada.Proxy. The Admiral token stays in the URL (`?token=`, relayed untouched); the
 * proxy session goes either in a `Sec-WebSocket-Protocol` entry `armada-proxy-session.<base64url(token)>` (preferred)
 * or in the `X-Armada-Proxy-Session` header on the upgrade. If a connection in one mode closes before it ever opens
 * (an intermediary that rejects unknown subprotocols, for example), the next attempt uses the other mode; a mode that
 * opened once is kept.
 */

export type ProxySocketMode = 'subprotocol' | 'header';

/** The constructor React Native provides: `new WebSocket(url, protocols, { headers })`. */
export type NativeWebSocketCtor = new (
  url: string,
  protocols?: string | string[] | null,
  options?: { headers: Record<string, string> } | null,
) => SocketLike;

export function proxySubprotocol(proxyToken: string): string {
  return `armada-proxy-session.${base64UrlUtf8(proxyToken)}`;
}

export interface ProxySocketFactoryOptions {
  /** The proxy session token; read on every attempt so a renewed session is picked up. */
  proxyToken: () => string | null;
  /** Defaults to the global WebSocket (React Native's accepts the headers option). */
  ctor?: NativeWebSocketCtor;
  initialMode?: ProxySocketMode;
  /** Observes mode changes (for diagnostics and tests). */
  onModeChange?: (mode: ProxySocketMode) => void;
}

export interface ProxySocketFactory {
  factory: SocketFactory;
  mode: () => ProxySocketMode;
}

/** Wraps a socket so the factory sees whether it opened, without changing what ArmadaSocket sees. */
function observe(socket: SocketLike, onOpened: () => void, onFailedBeforeOpen: () => void): SocketLike {
  let opened = false;
  let settled = false;
  const wrapper: SocketLike = {
    get readyState() { return socket.readyState; },
    send: (data: string) => socket.send(data),
    close: () => {
      // Closed by the client (stop, background): not evidence about the mode.
      settled = true;
      socket.close();
    },
    onopen: null,
    onmessage: null,
    onclose: null,
    onerror: null,
  };
  socket.onopen = (ev) => {
    opened = true;
    if (!settled) { settled = true; onOpened(); }
    wrapper.onopen?.(ev);
  };
  socket.onmessage = (ev) => wrapper.onmessage?.(ev);
  socket.onerror = (ev) => wrapper.onerror?.(ev);
  socket.onclose = (ev) => {
    if (!opened && !settled) { settled = true; onFailedBeforeOpen(); }
    wrapper.onclose?.(ev);
  };
  return wrapper;
}

export function createProxySocketFactory(options: ProxySocketFactoryOptions): ProxySocketFactory {
  let mode: ProxySocketMode = options.initialMode ?? 'subprotocol';
  let confirmed = false;
  const setMode = (next: ProxySocketMode) => {
    if (next === mode) return;
    mode = next;
    options.onModeChange?.(next);
  };

  const factory: SocketFactory = (url: string) => {
    const Ctor = options.ctor ?? (globalThis as unknown as { WebSocket: NativeWebSocketCtor }).WebSocket;
    const token = options.proxyToken();
    const attemptMode = mode;
    let socket: SocketLike;
    if (!token) {
      socket = new Ctor(url);
    } else if (attemptMode === 'subprotocol') {
      socket = new Ctor(url, ['armada', proxySubprotocol(token)]);
    } else {
      socket = new Ctor(url, null, { headers: { [PROXY_SESSION_HEADER]: token } });
    }
    return observe(
      socket,
      () => { confirmed = true; },
      () => {
        // A mode that worked before is not abandoned because of one drop (the network may be down); a mode that
        // has never opened is swapped for the other one.
        if (!confirmed && mode === attemptMode) setMode(attemptMode === 'subprotocol' ? 'header' : 'subprotocol');
      },
    );
  };

  return { factory, mode: () => mode };
}
