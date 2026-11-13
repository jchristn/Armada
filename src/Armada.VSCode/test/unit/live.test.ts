import * as assert from 'assert';
import { describe, it } from 'node:test';
import { BlackBoxSocketEventData } from '../../src/api/types';
import { LiveMode, LiveUpdates, LiveUpdatesOptions, SocketLike } from '../../src/live/liveUpdates';

class FakeSocket implements SocketLike {
  public onopen: ((ev: unknown) => void) | null = null;
  public onmessage: ((ev: { data: unknown }) => void) | null = null;
  public onclose: ((ev: unknown) => void) | null = null;
  public onerror: ((ev: unknown) => void) | null = null;
  public sent: string[] = [];
  public closed = false;
  public constructor(public readonly url: string) {}
  public send(data: string): void {
    this.sent.push(data);
  }
  public close(): void {
    this.closed = true;
  }
}

class Timers {
  public pending: Array<{ fn: () => void; ms: number; id: number }> = [];
  private _Next = 1;
  public set = (fn: () => void, ms: number): unknown => {
    const id = this._Next++;
    this.pending.push({ fn, ms, id });
    return id;
  };
  public clear = (handle: unknown): void => {
    this.pending = this.pending.filter((t) => t.id !== handle);
  };
  public runAll(): void {
    const due = this.pending;
    this.pending = [];
    for (const t of due) t.fn();
  }
}

function harness(overrides: Partial<LiveUpdatesOptions> = {}) {
  const sockets: FakeSocket[] = [];
  const events: Array<{ type: string; data: BlackBoxSocketEventData }> = [];
  const modes: LiveMode[] = [];
  const timers = new Timers();
  let polls = 0;
  const live = new LiveUpdates({
    url: 'ws://armada.test/ws?token=k',
    useWebSocket: true,
    pollIntervalMs: 60000,
    onEvent: (type, data) => events.push({ type, data }),
    onPoll: () => polls++,
    onModeChange: (mode) => modes.push(mode),
    socketFactory: (url) => {
      const s = new FakeSocket(url);
      sockets.push(s);
      return s;
    },
    setTimer: timers.set,
    clearTimer: timers.clear,
    ...overrides,
  });
  return { live, sockets, events, modes, timers, polls: () => polls };
}

describe('LiveUpdates', () => {
  it('subscribes on open and delivers only black box events', () => {
    const h = harness();
    h.live.start();
    const socket = h.sockets[0];
    assert.strictEqual(socket.url, 'ws://armada.test/ws?token=k');
    socket.onopen?.({});
    assert.deepStrictEqual(socket.sent.map((s) => JSON.parse(s)), [{ Route: 'subscribe' }]);
    assert.strictEqual(h.live.mode, 'websocket');
    socket.onmessage?.({ data: JSON.stringify({ type: 'blackbox.captured', data: { entryId: 'bbx_1', missionId: 'msn_x' } }) });
    socket.onmessage?.({ data: JSON.stringify({ type: 'mission.changed', data: { id: 'msn_x' } }) });
    socket.onmessage?.({ data: 'not json' });
    socket.onmessage?.({ data: Buffer.from(JSON.stringify({ type: 'blackbox.resolved', data: { entryId: 'bbx_1' } })) });
    assert.deepStrictEqual(h.events.map((e) => e.type), ['blackbox.captured', 'blackbox.resolved']);
    assert.strictEqual(h.events[0].data.missionId, 'msn_x');
    assert.strictEqual(h.polls(), 0);
  });

  it('falls back to polling when the socket closes, and reconnects with backoff', () => {
    const h = harness();
    h.live.start();
    h.sockets[0].onclose?.({});
    assert.strictEqual(h.live.mode, 'polling');
    const poll = h.timers.pending.find((t) => t.ms === 60000);
    const reconnect = h.timers.pending.find((t) => t.ms === 5000);
    assert.ok(poll && reconnect);
    poll.fn();
    assert.strictEqual(h.polls(), 1);
    reconnect.fn();
    assert.strictEqual(h.sockets.length, 2);
    h.sockets[1].onclose?.({});
    assert.ok(h.timers.pending.some((t) => t.ms === 10000), 'second reconnect waits longer');
    h.sockets[1].onopen?.({});
    assert.strictEqual(h.live.mode, 'polling', 'a stale socket does not switch modes');
  });

  it('stops polling once a reconnect succeeds', () => {
    const h = harness();
    h.live.start();
    h.sockets[0].onclose?.({});
    h.timers.pending.find((t) => t.ms === 5000)?.fn();
    h.sockets[1].onopen?.({});
    assert.strictEqual(h.live.mode, 'websocket');
    assert.ok(!h.timers.pending.some((t) => t.ms === 60000));
  });

  it('polls when the socket factory throws', () => {
    const h = harness({
      socketFactory: () => {
        throw new Error('401 Unauthorized');
      },
    });
    h.live.start();
    assert.strictEqual(h.live.mode, 'polling');
  });

  it('polls only when WebSocket is disabled', () => {
    const h = harness({ useWebSocket: false });
    h.live.start();
    assert.strictEqual(h.sockets.length, 0);
    assert.strictEqual(h.live.mode, 'polling');
    h.timers.runAll();
    h.timers.runAll();
    assert.strictEqual(h.polls(), 2);
  });

  it('stops everything', () => {
    const h = harness();
    h.live.start();
    h.live.stop();
    assert.ok(h.sockets[0].closed);
    assert.strictEqual(h.live.mode, 'stopped');
    assert.strictEqual(h.timers.pending.length, 0);
    h.sockets[0].onclose?.({});
    assert.strictEqual(h.timers.pending.length, 0);
  });

  it('keeps the socket open when an event handler throws', () => {
    const h = harness({
      onEvent: () => {
        throw new Error('handler failed');
      },
    });
    h.live.start();
    h.sockets[0].onopen?.({});
    h.sockets[0].onmessage?.({ data: JSON.stringify({ type: 'blackbox.deleted', data: {} }) });
    assert.strictEqual(h.live.mode, 'websocket');
    assert.ok(!h.sockets[0].closed);
  });

  it('parses messages defensively', () => {
    assert.strictEqual(LiveUpdates.parseMessage(42), undefined);
    assert.strictEqual(LiveUpdates.parseMessage('{"type":5}'), undefined);
    assert.deepStrictEqual(LiveUpdates.parseMessage('{"type":"blackbox.analyzed"}'), { type: 'blackbox.analyzed', data: {} });
  });
});
