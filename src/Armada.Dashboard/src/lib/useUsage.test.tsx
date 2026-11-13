import { act, renderHook, waitFor } from '@testing-library/react';
import { getUsageLatest } from '../api/client';
import { USAGE_MAX_PAGES_PER_BATCH, useLatestUsageForCaptains } from './useUsage';

vi.mock('../api/client', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../api/client')>()),
  getUsageLatest: vi.fn(),
}));
vi.mock('../context/WebSocketContext', () => ({ useUsageRevision: () => 0 }));

function result(captainIds: string[], totalPages: number, id: string) {
  return { success: true, pageNumber: 1, pageSize: 100, totalPages, totalRecords: totalPages, totalMs: 0,
    objects: [{ id, captainIds, status: 'Ok', windows: [], stale: false }] };
}

describe('useLatestUsageForCaptains', () => {
  beforeEach(() => vi.mocked(getUsageLatest).mockReset());

  it('issues one request per 100 ids and maps shared accounts to every visible member', async () => {
    const ids = Array.from({ length: 150 }, (_, i) => `cpt_${i}`);
    vi.mocked(getUsageLatest).mockImplementation((filter) => { const chunk = (filter?.captainIds ?? '').split(','); return Promise.resolve(result([chunk[0], 'cpt_hidden'], 1, `uss_${chunk[0]}`) as never); });
    const { result: hook } = renderHook(() => useLatestUsageForCaptains(ids));
    await waitFor(() => expect(hook.current.status).toBe('ready'));
    expect(getUsageLatest).toHaveBeenCalledTimes(2);
    expect(vi.mocked(getUsageLatest).mock.calls.map((c) => (c[0]?.captainIds ?? '').split(',').length)).toEqual([100, 50]);
    expect(vi.mocked(getUsageLatest).mock.calls.every((c) => c[0]?.pageSize === 100 && c[0]?.pageNumber === 1 && c[0]?.captainId === undefined)).toBe(true);
    expect(hook.current.byCaptain.has('cpt_hidden')).toBe(false);
    expect(hook.current.byCaptain.size).toBe(2);
  });

  it('follows account pages up to the bound and then reports truncation', async () => {
    vi.mocked(getUsageLatest).mockImplementation((filter) => Promise.resolve(result(['cpt_a'], 9, `uss_${filter?.pageNumber}`) as never));
    const { result: hook } = renderHook(() => useLatestUsageForCaptains(['cpt_a']));
    await waitFor(() => expect(hook.current.status).toBe('ready'));
    expect(getUsageLatest).toHaveBeenCalledTimes(USAGE_MAX_PAGES_PER_BATCH);
    expect(hook.current.truncated).toBe(true);
    expect(hook.current.byCaptain.get('cpt_a')?.length).toBe(USAGE_MAX_PAGES_PER_BATCH);
  });

  it('reports an HTTP failure distinctly from empty success and retries the same batch', async () => {
    vi.mocked(getUsageLatest).mockRejectedValueOnce(new Error('service unavailable'))
      .mockResolvedValueOnce(result([], 1, 'empty') as never);
    const { result: hook } = renderHook(() => useLatestUsageForCaptains(['cpt_a']));
    await waitFor(() => expect(hook.current.status).toBe('error'));
    expect(hook.current.byCaptain.size).toBe(0);
    act(() => hook.current.retry());
    await waitFor(() => expect(hook.current.status).toBe('ready'));
    expect(hook.current.byCaptain.size).toBe(0);
    expect(getUsageLatest).toHaveBeenCalledTimes(2);
    expect(vi.mocked(getUsageLatest).mock.calls[0]).toEqual(vi.mocked(getUsageLatest).mock.calls[1]);
  });

  it('ignores a late response from the previous nonempty captain page', async () => {
    let resolveOld!: (value: never) => void;
    vi.mocked(getUsageLatest).mockImplementationOnce(() => new Promise((resolve) => { resolveOld = resolve; }))
      .mockResolvedValueOnce(result(['cpt_b'], 1, 'uss_b') as never);
    const { result: hook, rerender } = renderHook(({ ids }) => useLatestUsageForCaptains(ids), { initialProps: { ids: ['cpt_a'] } });
    expect(getUsageLatest).toHaveBeenCalledWith({ captainIds: 'cpt_a', pageNumber: 1, pageSize: 100 });
    rerender({ ids: ['cpt_b'] });
    await waitFor(() => expect(hook.current.byCaptain.has('cpt_b')).toBe(true));
    await act(async () => resolveOld(result(['cpt_a'], 1, 'uss_a') as never));
    expect(hook.current.byCaptain.has('cpt_a')).toBe(false);
    expect(hook.current.byCaptain.has('cpt_b')).toBe(true);
    expect(hook.current.requests).toBe(1);
  });

  it('makes no request for an empty page', async () => {
    const { result: hook } = renderHook(() => useLatestUsageForCaptains([]));
    await waitFor(() => expect(hook.current.status).toBe('ready'));
    expect(getUsageLatest).not.toHaveBeenCalled();
  });
});
