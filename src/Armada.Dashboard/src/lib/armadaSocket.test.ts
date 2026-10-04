import { ArmadaSocket, buildSocketUrl, reconnectDelay, RECONNECT_MAX_MS, type SocketLike } from './armadaSocket';

class FakeSocket implements SocketLike {
  readyState = 0;
  sent: string[] = [];
  closed = false;
  onopen: ((ev: unknown) => void) | null = null;
  onmessage: ((ev: { data: unknown }) => void) | null = null;
  onclose: ((ev: unknown) => void) | null = null;
  onerror: ((ev: unknown) => void) | null = null;
  constructor(public url: string) {}
  send(data: string) { this.sent.push(data); }
  close() { this.closed = true; this.readyState = 3; }
  open() { this.readyState = 1; this.onopen?.({}); }
  drop() { this.readyState = 3; this.onclose?.({}); }
  receive(data: unknown) { this.onmessage?.({ data: JSON.stringify(data) }); }
}

function harness(token: { value: string | null }) {
  const sockets: FakeSocket[] = [];
  const timers: Array<{ fn: () => void; ms: number; cancelled: boolean }> = [];
  const messages: unknown[] = [];
  const status: boolean[] = [];
  let reconnects = 0;
  const socket = new ArmadaSocket({
    url: () => buildSocketUrl({ protocol: 'https:', host: 'armada.example:7890' }, token.value),
    factory: (url) => { const s = new FakeSocket(url); sockets.push(s); return s; },
    onMessage: (data) => messages.push(data),
    onStatus: (connected) => status.push(connected),
    onReconnected: () => { reconnects += 1; },
    schedule: (fn, ms) => { const timer = { fn, ms, cancelled: false }; timers.push(timer); return timer; },
    cancel: (handle) => { (handle as { cancelled: boolean }).cancelled = true; },
    random: () => 0.5,
  });
  return { socket, sockets, timers, messages, status, reconnects: () => reconnects };
}

describe('buildSocketUrl', () => {
  it('sends the session token as the token query parameter, URL-encoded', () => {
    expect(buildSocketUrl({ protocol: 'https:', host: 'h:1' }, 'a b/c+d')).toBe('wss://h:1/ws?token=a%20b%2Fc%2Bd');
    expect(buildSocketUrl({ protocol: 'http:', host: 'localhost:5173' }, 'tok')).toBe('ws://localhost:5173/ws?token=tok');
  });

  it('omits the parameter when there is no token', () => {
    expect(buildSocketUrl({ protocol: 'http:', host: 'h' }, null)).toBe('ws://h/ws');
  });
});

describe('reconnectDelay', () => {
  it('grows exponentially and caps', () => {
    const mid = () => 0.5;
    expect(reconnectDelay(0, mid)).toBe(1000);
    expect(reconnectDelay(1, mid)).toBe(2000);
    expect(reconnectDelay(3, mid)).toBe(8000);
    expect(reconnectDelay(20, mid)).toBe(RECONNECT_MAX_MS);
  });
});

describe('ArmadaSocket', () => {
  it('connects with the token, subscribes on open, and delivers parsed messages', () => {
    const h = harness({ value: 'tok_123' });
    h.socket.start();
    expect(h.sockets).toHaveLength(1);
    expect(h.sockets[0].url).toBe('wss://armada.example:7890/ws?token=tok_123');
    h.sockets[0].open();
    expect(h.status).toEqual([true]);
    expect(JSON.parse(h.sockets[0].sent[0])).toEqual({ Route: 'subscribe' });
    h.sockets[0].receive({ type: 'ask.thread', data: { threadId: 'ath_1' } });
    expect(h.messages).toEqual([{ type: 'ask.thread', data: { threadId: 'ath_1' } }]);
  });

  it('reconnects with backoff, picks up a new token, and reports the reconnect', () => {
    const token = { value: 'tok_1' as string | null };
    const h = harness(token);
    h.socket.start();
    h.sockets[0].open();
    h.sockets[0].drop();
    expect(h.status).toEqual([true, false]);
    expect(h.timers[0].ms).toBe(1000);

    // First retry fails before opening: the next delay doubles.
    h.timers[0].fn();
    h.sockets[1].drop();
    expect(h.timers[1].ms).toBe(2000);

    token.value = 'tok_2';
    h.timers[1].fn();
    expect(h.sockets[2].url).toContain('token=tok_2');
    expect(h.reconnects()).toBe(0);
    h.sockets[2].open();
    expect(h.reconnects()).toBe(1);

    // Backoff resets after a successful open.
    h.sockets[2].drop();
    expect(h.timers[2].ms).toBe(1000);
  });

  it('stops cleanly: closes the socket and cancels a pending reconnect', () => {
    const h = harness({ value: 't' });
    h.socket.start();
    h.sockets[0].open();
    h.sockets[0].drop();
    h.socket.stop();
    expect(h.timers[0].cancelled).toBe(true);
    h.timers[0].fn();
    expect(h.sockets).toHaveLength(1);
  });

  it('ignores frames that are not JSON', () => {
    const h = harness({ value: 't' });
    h.socket.start();
    h.sockets[0].open();
    h.sockets[0].onmessage?.({ data: 'not json' });
    expect(h.messages).toEqual([]);
  });
});
