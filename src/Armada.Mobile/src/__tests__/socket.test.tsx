import { act, render, waitFor } from '@testing-library/react-native';
import { AppState } from 'react-native';
import { SocketProvider, useSocket, type SocketState } from '../socket/SocketContext';
import { socketFactory } from '../test/fakeSocket';

type Handler = (state: string) => void;
let handlers: Handler[] = [];
let currentState = 'active';

beforeEach(() => {
  handlers = [];
  currentState = 'active';
  jest.spyOn(AppState, 'addEventListener').mockImplementation(((_t: string, h: Handler) => {
    handlers.push(h);
    return { remove: () => { handlers = handlers.filter((x) => x !== h); } };
  }) as unknown as typeof AppState.addEventListener);
  Object.defineProperty(AppState, 'currentState', { get: () => currentState, configurable: true });
});

function setAppState(next: string) {
  currentState = next;
  handlers.forEach((h) => h(next));
}

let state: SocketState | null = null;
function Probe() {
  state = useSocket();
  return null;
}

describe('socket lifecycle', () => {
  it('does not connect without a server and token', async () => {
    const { sockets, factory } = socketFactory();
    await render(<SocketProvider serverUrl="http://h:1" token={null} factory={factory}><Probe /></SocketProvider>);
    expect(sockets).toHaveLength(0);
  });

  it('connects to /ws with the token, subscribes, and fans out events', async () => {
    const { sockets, factory } = socketFactory();
    const seen: unknown[] = [];
    await render(<SocketProvider serverUrl="https://armada.example/relay" token="tok 1" factory={factory}><Probe /></SocketProvider>);
    expect(sockets[0].url).toBe('wss://armada.example/relay/ws?token=tok%201');
    await act(async () => { state!.subscribe((m) => seen.push(m)); sockets[0].open(); });
    expect(state!.connected).toBe(true);
    expect(sockets[0].sent).toEqual([JSON.stringify({ Route: 'subscribe' })]);
    await act(async () => { sockets[0].message({ type: 'mission.changed', data: { id: 'msn_1' } }); });
    expect(seen).toEqual([{ type: 'mission.changed', data: { id: 'msn_1' } }]);
  });

  it('closes in the background and reconnects on return to the foreground, bumping reconnectCount', async () => {
    const { sockets, factory } = socketFactory();
    await render(<SocketProvider serverUrl="http://h:1" token="t" factory={factory}><Probe /></SocketProvider>);
    await act(async () => { sockets[0].open(); });
    const before = state!.reconnectCount;
    await act(async () => { setAppState('background'); });
    expect(sockets[0].closed).toBe(true);
    expect(state!.connected).toBe(false);
    await act(async () => { setAppState('active'); });
    expect(sockets).toHaveLength(2);
    await act(async () => { sockets[1].open(); });
    expect(state!.connected).toBe(true);
    expect(state!.reconnectCount).toBe(before + 1);
  });

  it('inactive (app switcher, control center) does not drop the socket', async () => {
    const { sockets, factory } = socketFactory();
    await render(<SocketProvider serverUrl="http://h:1" token="t" factory={factory}><Probe /></SocketProvider>);
    await act(async () => { sockets[0].open(); setAppState('inactive'); setAppState('active'); });
    expect(sockets).toHaveLength(1);
    expect(sockets[0].closed).toBe(false);
  });

  it('a dropped connection reconnects with backoff', async () => {
    jest.useFakeTimers();
    try {
      const { sockets, factory } = socketFactory();
      await render(<SocketProvider serverUrl="http://h:1" token="t" factory={factory}><Probe /></SocketProvider>);
      await act(async () => { sockets[0].open(); sockets[0].drop(); });
      expect(sockets).toHaveLength(1);
      await act(async () => { jest.advanceTimersByTime(1300); });
      await waitFor(() => expect(sockets).toHaveLength(2));
    } finally {
      jest.useRealTimers();
    }
  });

  it('a new token reconnects with the new token; unmount closes', async () => {
    const { sockets, factory } = socketFactory();
    const view = await render(<SocketProvider serverUrl="http://h:1" token="a" factory={factory}><Probe /></SocketProvider>);
    await view.rerender(<SocketProvider serverUrl="http://h:1" token="b" factory={factory}><Probe /></SocketProvider>);
    expect(sockets[0].closed).toBe(true);
    expect(sockets[1].url).toBe('ws://h:1/ws?token=b');
    await view.unmount();
    expect(sockets[1].closed).toBe(true);
  });
});
