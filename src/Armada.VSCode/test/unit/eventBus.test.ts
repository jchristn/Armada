import * as assert from 'assert';
import { describe, it } from 'node:test';
import type { SocketLike } from '@dashboard/lib/armadaSocket';
import { BlackBoxEventData, BlackBoxLive, REFRESH_DEBOUNCE_MS } from '../../src/surfaces/blackbox/blackBoxLive';
import { ArmadaEventBus, ArmadaEventMessage, LiveMode, RECONNECT_INITIAL_MS } from '../../src/transport/eventBus';

class FakeSocket implements SocketLike {
  public readyState = 0;
  public sent: string[] = [];
  public closed = false;
  public onopen: ((ev: unknown) => void) | null = null;
  public onmessage: ((ev: { data: unknown }) => void) | null = null;
  public onclose: ((ev: unknown) => void) | null = null;
  public onerror: ((ev: unknown) => void) | null = null;
  public constructor(public readonly url: string) {}
  public send(data: string): void {
    this.sent.push(data);
  }
  public close(): void {
    this.closed = true;
  }
  public open(): void {
    this.readyState = 1;
    this.onopen?.({});
  }
  public message(data: unknown): void {
    this.onmessage?.({ data: typeof data === 'string' ? data : JSON.stringify(data) });
  }
  public drop(): void {
    this.readyState = 3;
    this.onclose?.({});
  }
}

interface Timer {
  fn: () => void;
  ms: number;
  cleared: boolean;
}

class Clock {
  public readonly timers: Timer[] = [];
  public readonly set = (fn: () => void, ms: number): Timer => {
    const t = { fn, ms, cleared: false };
    this.timers.push(t);
    return t;
  };
  public readonly clear = (handle: unknown): void => {
    if (handle) (handle as Timer).cleared = true;
  };
  public pending(): Timer[] {
    return this.timers.filter((t) => !t.cleared);
  }
  /** Runs the oldest pending timer with the delay. */
  public fire(ms: number): void {
    const t = this.pending().find((x) => x.ms === ms);
    assert.ok(t, `a pending ${ms} ms timer (pending: ${this.pending().map((x) => x.ms).join(', ')})`);
    t.cleared = true;
    t.fn();
  }
}

function bus(options: { useWebSocket?: boolean; factoryThrows?: boolean } = {}): { bus: ArmadaEventBus; sockets: FakeSocket[]; clock: Clock; modes: LiveMode[] } {
  const sockets: FakeSocket[] = [];
  const clock = new Clock();
  const modes: LiveMode[] = [];
  const b = new ArmadaEventBus({
    useWebSocket: options.useWebSocket ?? true,
    pollIntervalMs: 60000,
    socketFactory: (url) => {
      if (options.factoryThrows) throw new Error('no sockets here');
      const s = new FakeSocket(url);
      sockets.push(s);
      return s;
    },
    setTimer: clock.set,
    clearTimer: clock.clear,
  });
  b.onModeChange((m) => modes.push(m));
  return { bus: b, sockets, clock, modes };
}

describe('ArmadaEventBus', () => {
  it('opens one socket with the token, subscribes once, and fans every event out to all subscribers', () => {
    const { bus: b, sockets } = bus();
    const first: ArmadaEventMessage[] = [];
    const second: string[] = [];
    b.subscribe((e) => first.push(e));
    b.subscribe((e) => second.push(e.type));
    b.start({ baseUrl: 'http://127.0.0.1:7890', token: 'ak_1' });
    assert.strictEqual(sockets.length, 1);
    assert.strictEqual(sockets[0].url, 'ws://127.0.0.1:7890/ws?token=ak_1');
    sockets[0].open();
    assert.deepStrictEqual(sockets[0].sent, [JSON.stringify({ Route: 'subscribe' })]);
    assert.strictEqual(b.mode, 'websocket');
    sockets[0].message({ type: 'mission.changed', data: { id: 'msn_1' }, timestamp: '2026-11-19T10:00:00Z' });
    sockets[0].message({ type: 'blackbox.captured', data: { entryId: 'bbx_1' } });
    sockets[0].message({ Route: 'subscribe', Success: true });
    sockets[0].message('not json');
    assert.deepStrictEqual(first.map((e) => e.type), ['mission.changed', 'blackbox.captured']);
    assert.deepStrictEqual(first[0].data, { id: 'msn_1' });
    assert.deepStrictEqual(second, ['mission.changed', 'blackbox.captured']);
    assert.strictEqual(b.received, 2);
  });

  it('polls while the socket is down and reconnects with the 5 s doubling backoff', () => {
    const { bus: b, sockets, clock, modes } = bus();
    let polls = 0;
    b.onPoll(() => polls++);
    b.start({ baseUrl: 'https://armada.example.com', token: undefined });
    assert.strictEqual(sockets[0].url, 'wss://armada.example.com/ws');
    sockets[0].open();
    sockets[0].drop();
    assert.strictEqual(b.mode, 'polling');
    clock.fire(60000);
    assert.strictEqual(polls, 1);
    clock.fire(RECONNECT_INITIAL_MS);
    assert.strictEqual(sockets.length, 2);
    sockets[1].drop();
    clock.fire(RECONNECT_INITIAL_MS * 2);
    sockets[2].drop();
    clock.fire(RECONNECT_INITIAL_MS * 4);
    sockets[3].open();
    assert.strictEqual(b.mode, 'websocket');
    assert.strictEqual(clock.pending().filter((t) => t.ms === 60000).length, 0, 'polling stops once connected');
    assert.strictEqual(polls, 2, 'a reconnect reloads once, since events may have been missed');
    assert.deepStrictEqual(modes, ['connecting', 'websocket', 'polling', 'websocket']);
  });

  it('caps the backoff at 300 s and resets it after a successful open', () => {
    const { bus: b, sockets, clock } = bus();
    b.start({ baseUrl: 'http://h:1', token: 't' });
    const delays: number[] = [];
    for (let i = 0; i < 8; i++) {
      sockets[sockets.length - 1].drop();
      const reconnect = clock.pending().find((t) => t.ms !== 60000);
      assert.ok(reconnect);
      delays.push(reconnect.ms);
      clock.fire(reconnect.ms);
    }
    assert.deepStrictEqual(delays, [5000, 10000, 20000, 40000, 80000, 160000, 300000, 300000]);
    sockets[sockets.length - 1].open();
    sockets[sockets.length - 1].drop();
    assert.ok(clock.pending().some((t) => t.ms === 5000));
  });

  it('polls when the socket cannot be created, when WebSocket is off, and stops everything on stop', () => {
    const throwing = bus({ factoryThrows: true });
    throwing.bus.start({ baseUrl: 'http://h:1', token: 't' });
    assert.strictEqual(throwing.bus.mode, 'polling');
    const off = bus({ useWebSocket: false });
    off.bus.start({ baseUrl: 'http://h:1', token: 't' });
    assert.strictEqual(off.sockets.length, 0);
    assert.strictEqual(off.bus.mode, 'polling');
    assert.match(off.bus.detail, /disabled/);
    off.bus.stop();
    assert.strictEqual(off.bus.mode, 'stopped');
    assert.strictEqual(off.clock.pending().length, 0);

    const live = bus();
    live.bus.start({ baseUrl: 'http://h:1', token: 't' });
    live.sockets[0].open();
    live.bus.stop();
    assert.ok(live.sockets[0].closed);
    assert.strictEqual(live.bus.mode, 'stopped');
  });

  it('keeps delivering when a subscriber throws, and drops a disposed subscriber', () => {
    const { bus: b, sockets } = bus();
    const seen: string[] = [];
    b.subscribe(() => {
      throw new Error('bad subscriber');
    });
    const sub = b.subscribe((e) => seen.push(e.type));
    b.start({ baseUrl: 'http://h:1', token: 't' });
    sockets[0].open();
    sockets[0].message({ type: 'a.b' });
    sub.dispose();
    sockets[0].message({ type: 'c.d' });
    assert.deepStrictEqual(seen, ['a.b']);
  });

  it('parses events defensively', () => {
    assert.strictEqual(ArmadaEventBus.toEvent(null), undefined);
    assert.strictEqual(ArmadaEventBus.toEvent({ data: {} }), undefined);
    assert.deepStrictEqual(ArmadaEventBus.toEvent({ type: 'x.y' }), { type: 'x.y', data: null, message: undefined, timestamp: undefined });
  });
});

describe('BlackBoxLive', () => {
  function live(matched: string[] = []): { sink: ArmadaEventBus; sockets: FakeSocket[]; clock: Clock; refreshes: () => number; captured: BlackBoxEventData[] } {
    const { bus: b, sockets, clock } = bus();
    let refreshes = 0;
    const captured: BlackBoxEventData[] = [];
    new BlackBoxLive(b, { refresh: () => refreshes++, onCaptured: (d) => captured.push(d), matchedVesselIds: () => matched, setTimer: clock.set, clearTimer: clock.clear });
    b.start({ baseUrl: 'http://h:1', token: 't' });
    sockets[0].open();
    return { sink: b, sockets, clock, refreshes: () => refreshes, captured };
  }

  it('coalesces black box events into one refresh and ignores other events', () => {
    const { sockets, clock, refreshes } = live();
    sockets[0].message({ type: 'blackbox.captured', data: { entryId: 'bbx_1' } });
    sockets[0].message({ type: 'blackbox.analyzed', data: { entryId: 'bbx_1' } });
    sockets[0].message({ type: 'mission.changed', data: { id: 'msn_1' } });
    assert.strictEqual(clock.pending().filter((t) => t.ms === REFRESH_DEBOUNCE_MS).length, 1);
    clock.fire(REFRESH_DEBOUNCE_MS);
    assert.strictEqual(refreshes(), 1);
  });

  it('announces a capture for the workspace vessel only', () => {
    const { sockets, captured } = live(['vsl_a']);
    sockets[0].message({ type: 'blackbox.captured', data: { entryId: 'bbx_1', vesselId: 'vsl_a', missionId: 'msn_1' } });
    sockets[0].message({ type: 'blackbox.captured', data: { entryId: 'bbx_2', vesselId: 'vsl_b' } });
    sockets[0].message({ type: 'blackbox.resolved', data: { entryId: 'bbx_1', vesselId: 'vsl_a' } });
    assert.deepStrictEqual(captured.map((c) => c.entryId), ['bbx_1']);
  });

  it('refreshes on a polling tick', () => {
    const { sockets, clock, refreshes } = live();
    sockets[0].drop();
    clock.fire(60000);
    clock.fire(REFRESH_DEBOUNCE_MS);
    assert.strictEqual(refreshes(), 1);
  });
});
