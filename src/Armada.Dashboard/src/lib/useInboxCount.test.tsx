import { act, renderHook } from '@testing-library/react';
import type { InboxItem, WebSocketMessage } from '../types/models';
import { INBOX_POLL_INTERVAL_MS, INBOX_SOCKET_REFRESH_THROTTLE_MS } from './inboxSummary';

const getInbox = vi.fn();
let socketHandler: ((msg: WebSocketMessage) => void) | null = null;

vi.mock('../api/client', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../api/client')>()),
  getInbox: (...args: unknown[]) => getInbox(...args),
}));

vi.mock('../context/WebSocketContext', () => ({
  useWebSocket: () => ({
    subscribe: (handler: (msg: WebSocketMessage) => void) => {
      socketHandler = handler;
      return () => { socketHandler = null; };
    },
  }),
}));

import { useInboxCount } from './useInboxCount';

function item(title: string): InboxItem {
  return { kind: 'k', severity: 'Info', title, detail: '', entityType: null, entityId: null, href: '/' };
}

function emitSocket(): void {
  socketHandler?.({ type: 'mission.changed', data: {} });
}

async function flush(): Promise<void> {
  await act(async () => {
    await Promise.resolve();
    await Promise.resolve();
  });
}

describe('useInboxCount', () => {
  beforeEach(() => {
    getInbox.mockReset();
    socketHandler = null;
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('keeps one inbox read in flight and applies a later result only after that read settles', async () => {
    let releaseFirst: (items: InboxItem[]) => void = () => {};
    getInbox.mockImplementation(() => new Promise<InboxItem[]>((resolve) => { releaseFirst = resolve; }));

    const { result, unmount } = renderHook(() => useInboxCount());
    await flush();
    expect(getInbox).toHaveBeenCalledTimes(1);
    expect(socketHandler).not.toBeNull();

    await act(async () => {
      await vi.advanceTimersByTimeAsync(INBOX_SOCKET_REFRESH_THROTTLE_MS + 1);
      emitSocket();
      await vi.advanceTimersByTimeAsync(INBOX_POLL_INTERVAL_MS);
    });
    expect(getInbox).toHaveBeenCalledTimes(1);

    await act(async () => {
      releaseFirst([item('a')]);
    });
    await flush();
    expect(result.current.count).toBe(1);

    getInbox.mockResolvedValue([item('a'), item('b')]);
    await act(async () => {
      await vi.advanceTimersByTimeAsync(INBOX_SOCKET_REFRESH_THROTTLE_MS);
      emitSocket();
    });
    await flush();
    expect(getInbox).toHaveBeenCalledTimes(2);
    expect(result.current.count).toBe(2);
    unmount();
  });

  it('keeps the last count when a read fails and ignores a socket message inside the throttle', async () => {
    getInbox.mockResolvedValueOnce([item('a')]);
    const { result, unmount } = renderHook(() => useInboxCount());
    await flush();
    expect(result.current.count).toBe(1);
    expect(getInbox).toHaveBeenCalledTimes(1);

    getInbox.mockRejectedValueOnce(new Error('offline'));
    await act(async () => {
      await vi.advanceTimersByTimeAsync(INBOX_SOCKET_REFRESH_THROTTLE_MS);
      emitSocket();
    });
    await flush();
    expect(getInbox).toHaveBeenCalledTimes(2);
    expect(result.current.count).toBe(1);

    await act(async () => {
      emitSocket();
    });
    await flush();
    expect(getInbox).toHaveBeenCalledTimes(2);
    expect(result.current.count).toBe(1);
    unmount();
  });
});
