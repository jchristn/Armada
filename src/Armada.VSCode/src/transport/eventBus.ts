/*
 * One /ws connection per Admiral, shared by every surface. It uses the dashboard's ArmadaSocket (subscribe on open,
 * reconnect on close) with the extension's own reconnect backoff (5 s doubling to 300 s), and while the socket is down
 * it ticks a polling timer so subscribers can reload. Subscribers never open sockets of their own. No `vscode` import.
 */

import { ArmadaSocket, buildSocketUrlForServer, SocketFactory, SocketLike } from '@dashboard/lib/armadaSocket';
import type { WebSocketMessage } from '@dashboard/types/models';

/** First reconnect delay. */
export const RECONNECT_INITIAL_MS = 5000;

/** Longest reconnect delay. */
export const RECONNECT_MAX_MS = 300000;

/** Current live mode. */
export type LiveMode = 'stopped' | 'connecting' | 'websocket' | 'polling';

/** One event the server pushed: { type, data, message, timestamp } (docs/WEBSOCKET_API.md), the dashboard's type. */
export type ArmadaEventMessage = WebSocketMessage;

/** A subscriber's handle. */
export interface Subscription {
  dispose(): void;
}

/** Where to connect. */
export interface EventBusTarget {
  baseUrl: string;
  /** Sent as the `token` query parameter (browsers and Node cannot set headers on a WebSocket). */
  token: string | undefined;
}

/** Options; the timer and socket hooks are for tests. */
export interface EventBusOptions {
  useWebSocket: boolean;
  pollIntervalMs: number;
  socketFactory?: SocketFactory;
  setTimer?: (fn: () => void, ms: number) => unknown;
  clearTimer?: (handle: unknown) => void;
  maxReconnectMs?: number;
  /** Diagnostics (mode changes); never receives the token. */
  log?: (line: string) => void;
}

type Listener<T> = (value: T) => void;

/** The event bus for the connected Admiral. */
export class ArmadaEventBus {
  private readonly _Options: EventBusOptions;
  private readonly _SetTimer: (fn: () => void, ms: number) => unknown;
  private readonly _ClearTimer: (handle: unknown) => void;
  private readonly _EventListeners = new Set<Listener<ArmadaEventMessage>>();
  private readonly _PollListeners = new Set<Listener<void>>();
  private readonly _ModeListeners = new Set<Listener<LiveMode>>();
  private _Socket: ArmadaSocket | undefined;
  private _PollTimer: unknown;
  private _ReconnectDelayMs = RECONNECT_INITIAL_MS;
  private _Mode: LiveMode = 'stopped';
  private _Detail = '';
  private _Received = 0;

  /**
   * @throws Error when options are missing.
   */
  public constructor(options: EventBusOptions) {
    if (!options) throw new Error('options are required');
    this._Options = { ...options };
    this._SetTimer = options.setTimer ?? ((fn, ms) => setTimeout(fn, ms));
    this._ClearTimer = options.clearTimer ?? ((h) => clearTimeout(h as ReturnType<typeof setTimeout>));
  }

  /** The current mode. */
  public get mode(): LiveMode {
    return this._Mode;
  }

  /** Why the bus is polling, when it is. */
  public get detail(): string {
    return this._Detail;
  }

  /** Events delivered since start, for diagnostics and tests. */
  public get received(): number {
    return this._Received;
  }

  /** Subscribes to every event. A throwing listener does not affect the others. */
  public subscribe(listener: Listener<ArmadaEventMessage>): Subscription {
    return add(this._EventListeners, listener);
  }

  /** Subscribes to polling ticks (and to the reload after a reconnect, when events may have been missed). */
  public onPoll(listener: Listener<void>): Subscription {
    return add(this._PollListeners, listener);
  }

  /** Subscribes to mode changes. */
  public onModeChange(listener: Listener<LiveMode>): Subscription {
    return add(this._ModeListeners, listener);
  }

  /** Changes the WebSocket switch and polling interval; they apply from the next start. */
  public setOptions(useWebSocket: boolean, pollIntervalMs: number): void {
    this._Options.useWebSocket = useWebSocket;
    this._Options.pollIntervalMs = pollIntervalMs;
  }

  /** Connects to an Admiral, replacing any earlier connection. Never throws. */
  public start(target: EventBusTarget): void {
    this.stop();
    const factory = this.factory();
    if (!this._Options.useWebSocket || !factory) {
      this.startPolling(this._Options.useWebSocket ? 'WebSocket is not available in this host' : 'WebSocket disabled in settings');
      return;
    }
    let url: string;
    try {
      url = buildSocketUrlForServer(target.baseUrl, target.token);
    } catch (err) {
      this.startPolling(err instanceof Error ? err.message : String(err));
      return;
    }
    this._ReconnectDelayMs = RECONNECT_INITIAL_MS;
    this.setMode('connecting', '');
    const socket = new ArmadaSocket({
      url: () => url,
      factory,
      onMessage: (data) => this.deliver(data),
      onStatus: (connected) => this.onStatus(socket, connected),
      onReconnected: () => this.emitPoll(),
      // ArmadaSocket's own backoff is 1 s to 30 s for a browser tab; the extension keeps its gentler 5 s to 300 s.
      schedule: (fn) => {
        // A reconnect is only scheduled while the socket is down (also when opening it threw), so poll meanwhile.
        if (this._Socket === socket && this._Mode !== 'polling') this.startPolling('WebSocket unavailable; reconnecting');
        const delay = this._ReconnectDelayMs;
        this._ReconnectDelayMs = Math.min(this._ReconnectDelayMs * 2, this._Options.maxReconnectMs ?? RECONNECT_MAX_MS);
        return this._SetTimer(fn, delay);
      },
      cancel: (handle) => this._ClearTimer(handle),
    });
    this._Socket = socket;
    socket.start();
  }

  /** Closes the socket and stops every timer. */
  public stop(): void {
    const socket = this._Socket;
    this._Socket = undefined;
    socket?.stop();
    this.stopPolling();
    this.setMode('stopped', '');
  }

  /** Stops and drops every listener. */
  public dispose(): void {
    this.stop();
    this._EventListeners.clear();
    this._PollListeners.clear();
    this._ModeListeners.clear();
  }

  /** Parses one server message into an event, or undefined for anything else (replies, snapshots without a type). */
  public static toEvent(raw: unknown): ArmadaEventMessage | undefined {
    if (!raw || typeof raw !== 'object') return undefined;
    const envelope = raw as { type?: unknown; data?: unknown; message?: unknown; timestamp?: unknown };
    if (typeof envelope.type !== 'string' || !envelope.type) return undefined;
    return {
      type: envelope.type,
      data: envelope.data ?? null,
      message: typeof envelope.message === 'string' ? envelope.message : undefined,
      timestamp: typeof envelope.timestamp === 'string' ? envelope.timestamp : undefined,
    };
  }

  private factory(): SocketFactory | undefined {
    if (this._Options.socketFactory) return this._Options.socketFactory;
    const ctor = (globalThis as { WebSocket?: new (url: string) => SocketLike }).WebSocket;
    return ctor ? (url) => new ctor(url) : undefined;
  }

  private onStatus(socket: ArmadaSocket, connected: boolean): void {
    if (this._Socket !== socket) return;
    if (connected) {
      this._ReconnectDelayMs = RECONNECT_INITIAL_MS;
      this.stopPolling();
      this.setMode('websocket', '');
    } else {
      this.startPolling('WebSocket closed; reconnecting');
    }
  }

  private deliver(raw: unknown): void {
    const event = ArmadaEventBus.toEvent(raw);
    if (!event) return;
    this._Received++;
    for (const listener of Array.from(this._EventListeners)) {
      try {
        listener(event);
      } catch {
        // a failing subscriber must not affect the others or the socket
      }
    }
  }

  private startPolling(reason: string): void {
    this.setMode('polling', reason);
    if (this._PollTimer !== undefined) return;
    const tick = (): void => {
      this._PollTimer = undefined;
      if (this._Mode !== 'polling') return;
      this.emitPoll();
      this._PollTimer = this._SetTimer(tick, this._Options.pollIntervalMs);
    };
    this._PollTimer = this._SetTimer(tick, this._Options.pollIntervalMs);
  }

  private stopPolling(): void {
    if (this._PollTimer !== undefined) this._ClearTimer(this._PollTimer);
    this._PollTimer = undefined;
  }

  private emitPoll(): void {
    for (const listener of Array.from(this._PollListeners)) {
      try {
        listener();
      } catch {
        // keep polling
      }
    }
  }

  private setMode(mode: LiveMode, detail: string): void {
    if (this._Mode === mode && this._Detail === detail) return;
    const changed = this._Mode !== mode;
    this._Mode = mode;
    this._Detail = detail;
    if (!changed) return;
    this._Options.log?.(`[live] mode ${mode}${detail ? `: ${detail}` : ''}`);
    for (const listener of Array.from(this._ModeListeners)) {
      try {
        listener(mode);
      } catch {
        // ignore
      }
    }
  }
}

function add<T>(set: Set<Listener<T>>, listener: Listener<T>): Subscription {
  set.add(listener);
  return { dispose: () => set.delete(listener) };
}
