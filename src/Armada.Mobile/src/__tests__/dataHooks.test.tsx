import { act, renderHook, waitFor } from '@testing-library/react-native';
import { activeHubTab } from '../components/app/HubTabs';
import { activeFilterCount } from '../components/app/FilterSheet';
import { isEmptyDiff } from '../components/app/DiffView';
import { errorMessage } from '../data/errors';
import { hasMorePages, mergePage } from '../data/pagedList';
import { usePagedList } from '../data/usePagedList';
import { useQuery } from '../data/useQuery';
import { matchesLivePrefixes } from '../data/useLiveRefresh';
import { nameFrom } from '../data/useNameLookups';
import { statusTone } from '../lib/statusTone';

interface Row { id: string }
const rows = (...ids: string[]): Row[] => ids.map((id) => ({ id }));

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (error: unknown) => void;
  const promise = new Promise<T>((res, rej) => { resolve = res; reject = rej; });
  return { promise, resolve, reject };
}

describe('paged list helpers', () => {
  it('page 1 replaces, later pages append without duplicates', () => {
    expect(mergePage(rows('a'), rows('x', 'y'), 1, (r) => r.id)).toEqual(rows('x', 'y'));
    expect(mergePage(rows('a', 'b'), rows('b', 'c'), 2, (r) => r.id)).toEqual(rows('a', 'b', 'c'));
    const same = rows('a');
    expect(mergePage(same, rows('a'), 2, (r) => r.id)).toBe(same);
  });

  it('knows when more pages exist', () => {
    expect(hasMorePages(null, 1)).toBe(false);
    expect(hasMorePages({ totalPages: 3 }, 1)).toBe(true);
    expect(hasMorePages({ totalPages: 3 }, 3)).toBe(false);
    expect(hasMorePages({ totalPages: 0 }, 1)).toBe(false);
  });
});

describe('small pure helpers', () => {
  it('matches socket event prefixes', () => {
    expect(matchesLivePrefixes('mission.changed', ['mission.'])).toBe(true);
    expect(matchesLivePrefixes('voyage.changed', ['mission.'])).toBe(false);
    expect(matchesLivePrefixes('anything', [])).toBe(true);
    expect(matchesLivePrefixes(undefined, ['mission.'])).toBe(false);
  });

  it('resolves hub tabs like the dashboard (unknown values fall back)', () => {
    const keys = ['missions', 'voyages', 'merge-queue'] as const;
    expect(activeHubTab(keys, 'voyages', 'missions')).toBe('voyages');
    expect(activeHubTab(keys, ['merge-queue'], 'missions')).toBe('merge-queue');
    expect(activeHubTab(keys, 'bogus', 'missions')).toBe('missions');
    expect(activeHubTab(keys, undefined, 'missions')).toBe('missions');
  });

  it('maps statuses to tones', () => {
    expect(statusTone('Complete')).toBe('success');
    expect(statusTone('LandingFailed')).toBe('failed');
    expect(statusTone('InProgress')).toBe('running');
    expect(statusTone('WorkProduced')).toBe('warning');
    expect(statusTone('Cancelled')).toBe('cancelled');
    expect(statusTone('Pending')).toBe('pending');
    expect(statusTone('Whatever')).toBe('info');
  });

  it('names ids', () => {
    const items = [{ id: 'vsl_1', name: 'api' }];
    expect(nameFrom(items, 'vsl_1')).toBe('api');
    expect(nameFrom(items, 'vsl_unknown123')).toBe('vsl_unkn');
    expect(nameFrom(items, null)).toBe('-');
  });

  it('counts active filters, detects empty diffs, and words errors', () => {
    expect(activeFilterCount({ a: '', b: 'x', c: null })).toBe(1);
    expect(isEmptyDiff('No changes')).toBe(true);
    expect(isEmptyDiff('  ')).toBe(true);
    expect(isEmptyDiff('diff --git a/x b/x')).toBe(false);
    expect(errorMessage(new Error('boom'), 'fallback')).toBe('boom');
    expect(errorMessage({}, 'fallback')).toBe('fallback');
  });
});

describe('useQuery', () => {
  it('drops out-of-order responses and keeps data on error', async () => {
    const first = deferred<string>();
    const second = deferred<string>();
    const calls = [first, second];
    let i = 0;
    const { result, rerender } = await renderHook(({ id }: { id: number }) => useQuery(() => calls[i++].promise, [id]), { initialProps: { id: 1 } });
    expect(result.current.loading).toBe(true);
    await rerender({ id: 2 });
    await act(async () => { second.resolve('two'); });
    await act(async () => { first.resolve('one'); });
    expect(result.current.data).toBe('two');
    expect(result.current.loading).toBe(false);

    const failing = deferred<string>();
    calls.push(failing);
    await act(async () => { const p = result.current.reload(); failing.reject(new Error('down')); await p; });
    expect(result.current.data).toBe('two');
    expect(result.current.error).toBe('down');
  });
});

describe('usePagedList', () => {
  it('loads pages endlessly and reloads everything shown in one request', async () => {
    const fetchPage = jest.fn(async (pageNumber: number, pageSize: number) => {
      const all = rows('a', 'b', 'c', 'd', 'e');
      const start = (pageNumber - 1) * pageSize;
      return { objects: all.slice(start, start + pageSize), totalRecords: all.length, totalPages: Math.ceil(all.length / pageSize), pageNumber };
    });
    const { result } = await renderHook(() => usePagedList(fetchPage, (r: Row) => r.id, [], 2));
    await waitFor(() => expect(result.current.items).toHaveLength(2));
    expect(result.current.hasMore).toBe(true);
    await act(async () => { await result.current.loadMore(); });
    expect(result.current.items.map((r) => r.id)).toEqual(['a', 'b', 'c', 'd']);
    await act(async () => { await result.current.reload(); });
    expect(fetchPage).toHaveBeenLastCalledWith(1, 4);
    expect(result.current.items).toHaveLength(4);
    expect(result.current.hasMore).toBe(true);
    await act(async () => { await result.current.loadMore(); });
    expect(fetchPage).toHaveBeenLastCalledWith(3, 2);
    expect(result.current.items.map((r) => r.id)).toEqual(['a', 'b', 'c', 'd', 'e']);
    expect(result.current.hasMore).toBe(false);
  });

  it('starts over when the filters change', async () => {
    const pending = deferred<{ objects: Row[]; totalRecords: number; totalPages: number; pageNumber: number }>();
    const fetchPage = jest.fn(async (_p: number, _s: number, status: string) => (
      status ? pending.promise : { objects: rows('x', 'y'), totalRecords: 2, totalPages: 1, pageNumber: 1 }));
    const { result, rerender } = await renderHook(
      ({ status }: { status: string }) => usePagedList((p, s) => fetchPage(p, s, status), (r: Row) => r.id, [status], 25),
      { initialProps: { status: '' } },
    );
    await waitFor(() => expect(result.current.items).toHaveLength(2));
    await rerender({ status: 'Failed' });
    expect(result.current.loading).toBe(true);
    await act(async () => { pending.resolve({ objects: rows('f'), totalRecords: 1, totalPages: 1, pageNumber: 1 }); });
    expect(result.current.loading).toBe(false);
    expect(result.current.items.map((r) => r.id)).toEqual(['f']);
    expect(fetchPage).toHaveBeenLastCalledWith(1, 25, 'Failed');
  });
});
