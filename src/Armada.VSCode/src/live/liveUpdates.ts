import { BLACKBOX_EVENT_TYPES } from '../api/blackbox';
import { BlackBoxSocketEventData, SocketEnvelope } from '../api/types';

/** The subset of the WHATWG WebSocket the extension uses. */
export interface SocketLike {
  onopen: ((ev: unknown) => void) | null;
  onmessage: ((ev: { data: unknown }) => void) | null;
  onclose: ((ev: unknown) => void) | null;
  onerror: ((ev: unknown) => void) | null;
  send(data: string): void;
  close(): void;
}

/** Creates a socket; the default uses the host's global WebSocket. */
export type SocketFactory = (url: string) => SocketLike;

export interface LiveUpdatesOptions {
  /** WebSocket URL with the credential attached, or undefined to poll only. */
  url: string | undefined;
  useWebSocket: boolean;
  pollIntervalMs: number;
  /** Called for every black box event. */
  onEvent: (type: string, data: BlackBoxSocketEventData) => void;
  /** Called on each polling tick while the socket is down. */
  onPoll: () => void;
  /** Called when the mode changes, for the status bar or logs. */
  onModeChange?: (mode: LiveMode, detail?: string) => void;
  socketFactory?: SocketFactory;
  setTimer?: (fn: () => void, ms: number) => unknown;
  clearTimer?: (handle: unknown) => void;
  /** Longest delay between reconnect attempts. */
  maxReconnectMs?: number;
}

/** Current live update mode. */
export type LiveMode = 'stopped' | 'connecting' | 'websocket' | 'polling';

/** Listens for black box events over the Armada WebSocket and falls back to polling when the socket fails. */
export class LiveUpdates {
  private readonly _Options: LiveUpdatesOptions;
  private readonly _SetTimer: (fn: () => void, ms: number) => unknown;
  private readonly _ClearTimer: (handle: unknown) => void;
  private _Socket: SocketLike | undefined;
  private _PollTimer: unknown;
  private _ReconnectTimer: unknown;
  private _ReconnectDelayMs = 5000;
  private _Stopped = true;
  public mode: LiveMode = 'stopped';

  /**
   * @throws Error when options are missing.
   */
  public constructor(options: LiveUpdatesOptions) {
    if (!options) throw new Error('options are required');
    this._Options = options;
    this._SetTimer = options.setTimer ?? ((fn, ms) => setTimeout(fn, ms));
    this._ClearTimer = options.clearTimer ?? ((h) => clearTimeout(h as ReturnType<typeof setTimeout>));
  }

  /** Starts listening. Never throws. */
  public start(): void {
    this._Stopped = false;
    if (this._Options.useWebSocket && this._Options.url && this.factory()) this.openSocket();
    else this.startPolling(this._Options.useWebSocket ? 'WebSocket is not available in this host' : 'WebSocket disabled in settings');
  }

  /** Stops the socket and all timers. */
  public stop(): void {
    this._Stopped = true;
    this.stopPolling();
    if (this._ReconnectTimer !== undefined) this._ClearTimer(this._ReconnectTimer);
    this._ReconnectTimer = undefined;
    const socket = this._Socket;
    this._Socket = undefined;
    if (socket) {
      socket.onclose = null;
      socket.onerror = null;
      socket.onmessage = null;
      try {
        socket.close();
      } catch {
        // already closed
      }
    }
    this.setMode('stopped');
  }

  /** Parses one server message; returns the black box event it carries, or undefined. Exposed for tests. */
  public static parseMessage(raw: unknown): { type: string; data: BlackBoxSocketEventData } | undefined {
    let text: string;
    if (typeof raw === 'string') text = raw;
    else if (raw instanceof Uint8Array) text = Buffer.from(raw).toString('utf8');
    else if (raw instanceof ArrayBuffer) text = Buffer.from(new Uint8Array(raw)).toString('utf8');
    else return undefined;
    let envelope: SocketEnvelope;
    try {
      envelope = JSON.parse(text) as SocketEnvelope;
    } catch {
      return undefined;
    }
    if (!envelope || typeof envelope.type !== 'string' || !BLACKBOX_EVENT_TYPES.includes(envelope.type)) return undefined;
    const data = envelope.data && typeof envelope.data === 'object' ? (envelope.data as BlackBoxSocketEventData) : {};
    return { type: envelope.type, data };
  }

  private factory(): SocketFactory | undefined {
    if (this._Options.socketFactory) return this._Options.socketFactory;
    const ctor = (globalThis as { WebSocket?: new (url: string) => SocketLike }).WebSocket;
    return ctor ? (url) => new ctor(url) : undefined;
  }

  private openSocket(): void {
    const factory = this.factory();
    if (!factory || !this._Options.url) {
      this.startPolling('WebSocket is not available');
      return;
    }
    this.setMode('connecting');
    let socket: SocketLike;
    try {
      socket = factory(this._Options.url);
    } catch (err) {
      this.onSocketDown(err instanceof Error ? err.message : String(err));
      return;
    }
    this._Socket = socket;
    socket.onopen = () => {
      if (this._Socket !== socket) return;
      this._ReconnectDelayMs = 5000;
      this.stopPolling();
      try {
        socket.send(JSON.stringify({ Route: 'subscribe' }));
      } catch {
        // the snapshot is not needed; events arrive without it
      }
      this.setMode('websocket');
    };
    socket.onmessage = (ev) => {
      const parsed = LiveUpdates.parseMessage(ev?.data);
      if (!parsed) return;
      try {
        this._Options.onEvent(parsed.type, parsed.data);
      } catch {
        // a failing handler must not close the socket
      }
    };
    socket.onerror = () => {
      // onclose follows and handles the fallback
    };
    socket.onclose = () => {
      if (this._Socket !== socket) return;
      this._Socket = undefined;
      this.onSocketDown('WebSocket closed');
    };
  }

  private onSocketDown(reason: string): void {
    if (this._Stopped) return;
    this.startPolling(reason);
    const delay = this._ReconnectDelayMs;
    this._ReconnectDelayMs = Math.min(this._ReconnectDelayMs * 2, this._Options.maxReconnectMs ?? 300000);
    if (this._ReconnectTimer !== undefined) this._ClearTimer(this._ReconnectTimer);
    this._ReconnectTimer = this._SetTimer(() => {
      this._ReconnectTimer = undefined;
      if (!this._Stopped) this.openSocket();
    }, delay);
  }

  private startPolling(reason: string): void {
    if (this._Stopped) return;
    this.setMode('polling', reason);
    if (this._PollTimer !== undefined) return;
    const tick = (): void => {
      this._PollTimer = undefined;
      if (this._Stopped || this.mode === 'websocket') return;
      try {
        this._Options.onPoll();
      } catch {
        // keep polling
      }
      this._PollTimer = this._SetTimer(tick, this._Options.pollIntervalMs);
    };
    this._PollTimer = this._SetTimer(tick, this._Options.pollIntervalMs);
  }

  private stopPolling(): void {
    if (this._PollTimer !== undefined) this._ClearTimer(this._PollTimer);
    this._PollTimer = undefined;
  }

  private setMode(mode: LiveMode, detail?: string): void {
    if (this.mode === mode) return;
    this.mode = mode;
    this._Options.onModeChange?.(mode, detail);
  }
}
