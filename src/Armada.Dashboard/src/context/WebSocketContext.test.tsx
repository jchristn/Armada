import { act, render } from '@testing-library/react';
import { useEffect } from 'react';
import { WebSocketProvider, useWebSocket } from './WebSocketContext';
import type { WebSocketMessage } from '../types/models';

const auth = { isAuthenticated: true, sessionToken: 'tok_abc' as string | null };
vi.mock('./AuthContext', () => ({ useAuth: () => auth }));

class StubSocket {
  static instances: StubSocket[] = [];
  readyState = 0;
  sent: string[] = [];
  onopen: (() => void) | null = null;
  onmessage: ((ev: { data: string }) => void) | null = null;
  onclose: (() => void) | null = null;
  onerror: (() => void) | null = null;
  constructor(public url: string) { StubSocket.instances.push(this); }
  send(data: string) { this.sent.push(data); }
  close() { this.readyState = 3; }
}

function Consumer({ onMessage, onReconnect }: { onMessage: (m: WebSocketMessage) => void; onReconnect: (n: number) => void }) {
  const { subscribe, reconnectCount } = useWebSocket();
  useEffect(() => subscribe(onMessage), [subscribe, onMessage]);
  useEffect(() => { onReconnect(reconnectCount); }, [reconnectCount, onReconnect]);
  return null;
}

describe('WebSocketProvider', () => {
  const original = globalThis.WebSocket;
  beforeEach(() => {
    StubSocket.instances = [];
    vi.useFakeTimers();
    (globalThis as unknown as { WebSocket: unknown }).WebSocket = StubSocket;
  });
  afterEach(() => {
    vi.useRealTimers();
    (globalThis as unknown as { WebSocket: unknown }).WebSocket = original;
  });

  it('authenticates with the session token, fans out events, and counts reconnects', () => {
    const received: WebSocketMessage[] = [];
    const reconnects: number[] = [];
    const onMessage = (m: WebSocketMessage) => received.push(m);
    const onReconnect = (n: number) => reconnects.push(n);
    const view = render(<WebSocketProvider><Consumer onMessage={onMessage} onReconnect={onReconnect} /></WebSocketProvider>);

    expect(StubSocket.instances).toHaveLength(1);
    const first = StubSocket.instances[0];
    const firstUrl = new URL(first.url);
    expect(firstUrl.pathname).toBe('/ws');
    expect([...firstUrl.searchParams.keys()]).toEqual(['token']);
    expect(firstUrl.searchParams.get('token')).toBe('tok_abc');
    act(() => { first.readyState = 1; first.onopen?.(); });
    expect(first.sent.map((frame) => JSON.parse(frame))).toContainEqual({ Route: 'subscribe' });

    // Existing consumers (mission.changed) and Ask events both arrive.
    act(() => { first.onmessage?.({ data: JSON.stringify({ type: 'mission.changed', data: { id: 'msn_1', status: 'Complete' } }) }); });
    act(() => { first.onmessage?.({ data: JSON.stringify({ type: 'ask.thread', data: { threadId: 'ath_1' } }) }); });
    expect(received.map((m) => m.type)).toEqual(['mission.changed', 'ask.thread']);

    act(() => { first.readyState = 3; first.onclose?.(); });
    act(() => { vi.advanceTimersByTime(1500); });
    expect(StubSocket.instances).toHaveLength(2);
    const second = StubSocket.instances[1];
    act(() => { second.readyState = 1; second.onopen?.(); });
    expect(reconnects[reconnects.length - 1]).toBe(1);
    view.unmount();
  });
});
