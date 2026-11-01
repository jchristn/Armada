import { renderHook, waitFor } from '@testing-library/react';
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

  it('makes no request for an empty page', async () => {
    const { result: hook } = renderHook(() => useLatestUsageForCaptains([]));
    await waitFor(() => expect(hook.current.status).toBe('ready'));
    expect(getUsageLatest).not.toHaveBeenCalled();
  });
});
