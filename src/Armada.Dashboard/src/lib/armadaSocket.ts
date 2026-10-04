/**
 * The dashboard's WebSocket connection, kept free of React so it can be unit-tested with a fake socket.
 *
 * - The server requires authentication at upgrade; browsers cannot set `Authorization` on a WebSocket, so the
 *   current session token travels as the `token` query parameter.
 * - Dropped connections reconnect with exponential backoff (1s, 2s, 4s, ... capped at 30s, with jitter), and
 *   the backoff resets once a connection opens.
 * - Every open after the first is reported through `onReconnected` so pages can refetch what they missed.
 */

export interface SocketLike {
  readyState: number;
  send: (data: string) => void;
  close: () => void;
  onopen: ((ev: unknown) => void) | null;
  onmessage: ((ev: { data: unknown }) => void) | null;
  onclose: ((ev: unknown) => void) | null;
  onerror: ((ev: unknown) => void) | null;
}

export type SocketFactory = (url: string) => SocketLike;

export interface ArmadaSocketOptions {
  /** Builds the URL for each connection attempt, so a refreshed token is picked up on reconnect. */
  url: () => string;
  onMessage: (data: unknown) => void;
  onStatus?: (connected: boolean) => void;
  onReconnected?: () => void;
  factory?: SocketFactory;
  schedule?: (fn: () => void, ms: number) => unknown;
  cancel?: (handle: unknown) => void;
  random?: () => number;
}

/** WebSocket readyState for an open socket (the constant is not available in every test environment). */
const OPEN = 1;

export const RECONNECT_BASE_MS = 1000;
export const RECONNECT_MAX_MS = 30000;

/** `ws(s)://host/ws?token=...`, matching the page protocol; the token is omitted when there is none. */
export function buildSocketUrl(location: { protocol: string; host: string }, token: string | null | undefined): string {
  const protocol = location.protocol === 'https:' ? 'wss:' : 'ws:';
  const base = `${protocol}//${location.host}/ws`;
  return token ? `${base}?token=${encodeURIComponent(token)}` : base;
}

/** Delay before reconnect attempt `attempt` (0-based): exponential, capped, with +/-20% jitter. */
export function reconnectDelay(attempt: number, random: () => number = Math.random): number {
  const safeAttempt = Math.max(0, Math.min(attempt, 10));
  const raw = Math.min(RECONNECT_MAX_MS, RECONNECT_BASE_MS * 2 ** safeAttempt);
  const jitter = 0.8 + random() * 0.4;
  return Math.round(Math.min(RECONNECT_MAX_MS, raw * jitter));
}

export class ArmadaSocket {
  private readonly options: ArmadaSocketOptions;
  private socket: SocketLike | null = null;
  private timer: unknown = null;
  private attempt = 0;
  private opened = false;
  private stopped = true;

  constructor(options: ArmadaSocketOptions) {
    this.options = options;
  }

  /** Open the connection (no-op when already started). */
  start(): void {
    if (!this.stopped) return;
    this.stopped = false;
    this.connect();
  }

  /** Close the connection and stop reconnecting. */
  stop(): void {
    this.stopped = true;
    this.clearTimer();
    const socket = this.socket;
    this.socket = null;
    if (socket) {
      socket.onclose = null;
      socket.onerror = null;
      socket.onmessage = null;
      socket.onopen = null;
      try { socket.close(); } catch { /* already closed */ }
    }
    this.opened = false;
    this.attempt = 0;
    this.options.onStatus?.(false);
  }

  /** Send a JSON command when connected; dropped silently otherwise. */
  send(data: unknown): void {
    if (this.socket && this.socket.readyState === OPEN) this.socket.send(JSON.stringify(data));
  }

  get connected(): boolean {
    return !!this.socket && this.socket.readyState === OPEN;
  }

  private connect(): void {
    if (this.stopped) return;
    const factory: SocketFactory = this.options.factory ?? ((url) => new WebSocket(url) as unknown as SocketLike);
    let socket: SocketLike;
    try {
      socket = factory(this.options.url());
    } catch {
      this.scheduleReconnect();
      return;
    }
    this.socket = socket;

    socket.onopen = () => {
      if (this.stopped || this.socket !== socket) { try { socket.close(); } catch { /* ignore */ } return; }
      const isReconnect = this.opened;
      this.opened = true;
      this.attempt = 0;
      this.options.onStatus?.(true);
      socket.send(JSON.stringify({ Route: 'subscribe' }));
      if (isReconnect) this.options.onReconnected?.();
    };

    socket.onmessage = (event) => {
      if (typeof event.data !== 'string') return;
      let parsed: unknown;
      try { parsed = JSON.parse(event.data); } catch { return; }
      this.options.onMessage(parsed);
    };

    socket.onclose = () => {
      if (this.socket !== socket) return;
      this.socket = null;
      this.options.onStatus?.(false);
      this.scheduleReconnect();
    };

    socket.onerror = () => {
      this.options.onStatus?.(false);
    };
  }

  private scheduleReconnect(): void {
    if (this.stopped) return;
    this.clearTimer();
    const delay = reconnectDelay(this.attempt, this.options.random);
    this.attempt += 1;
    const schedule = this.options.schedule ?? ((fn: () => void, ms: number) => window.setTimeout(fn, ms));
    this.timer = schedule(() => { this.timer = null; this.connect(); }, delay);
  }

  private clearTimer(): void {
    if (this.timer === null) return;
    const cancel = this.options.cancel ?? ((handle: unknown) => window.clearTimeout(handle as number));
    cancel(this.timer);
    this.timer = null;
  }
}
