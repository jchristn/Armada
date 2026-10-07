import { useCallback, useEffect, useRef, useState } from 'react';
import { errorMessage } from './errors';
import { hasMorePages, mergePage, type PageResult } from './pagedList';

export interface PagedListState<T> {
  items: T[];
  totalRecords: number;
  error: string | null;
  loading: boolean;
  refreshing: boolean;
  loadingMore: boolean;
  hasMore: boolean;
  /** Pull-to-refresh: reloads from page 1 with the spinner. */
  refresh: () => Promise<void>;
  /** Quiet reload of everything shown so far (live updates, after an action). */
  reload: () => Promise<void>;
  /** Next page (endless scroll). */
  loadMore: () => Promise<void>;
}

export const DEFAULT_PAGE_SIZE = 25;

/**
 * An endless-scroll list over a paginated endpoint (the mobile form of the dashboard's server-side Pagination).
 * Resets to page 1 when `deps` (filters) change. A quiet reload refetches as many rows as are shown in one request,
 * so live updates keep the scroll position. Out-of-order responses are dropped.
 */
export function usePagedList<T>(
  fetchPage: (pageNumber: number, pageSize: number) => Promise<PageResult<T>>,
  getId: (item: T) => string,
  deps: readonly unknown[],
  pageSize = DEFAULT_PAGE_SIZE,
  fallbackError = 'Request failed.',
): PagedListState<T> {
  const [items, setItems] = useState<T[]>([]);
  const [totalRecords, setTotalRecords] = useState(0);
  const [page, setPage] = useState(0);
  const [lastResult, setLastResult] = useState<PageResult<T> | null>(null);
  const [error, setError] = useState<string | null>(null);
  // Loading until a first page for the current filters settles.
  const depsKey = JSON.stringify(deps);
  const [settledKey, setSettledKey] = useState<string | null>(null);
  const depsKeyRef = useRef(depsKey);
  useEffect(() => { depsKeyRef.current = depsKey; });
  const [refreshing, setRefreshing] = useState(false);
  const [loadingMore, setLoadingMore] = useState(false);
  const fetchRef = useRef(fetchPage);
  useEffect(() => { fetchRef.current = fetchPage; });
  const getIdRef = useRef(getId);
  useEffect(() => { getIdRef.current = getId; });
  const seqRef = useRef(0);
  const shownRef = useRef(0);
  const mountedRef = useRef(true);
  useEffect(() => {
    mountedRef.current = true;
    return () => { mountedRef.current = false; };
  }, []);

  const loadFirst = useCallback(async (size: number) => {
    const seq = ++seqRef.current;
    const key = depsKeyRef.current;
    try {
      const result = await fetchRef.current(1, size);
      if (!mountedRef.current || seq !== seqRef.current) return;
      const objects = result.objects ?? [];
      setItems(objects);
      shownRef.current = objects.length;
      setTotalRecords(result.totalRecords ?? objects.length);
      // A quiet reload of N rows counts as however many default-size pages it covered.
      const pagesCovered = Math.max(1, Math.ceil(size / pageSize));
      setPage(pagesCovered);
      setLastResult({ ...result, totalPages: Math.max(1, Math.ceil((result.totalRecords ?? 0) / pageSize)) });
      setError(null);
    } catch (e) {
      if (!mountedRef.current || seq !== seqRef.current) return;
      setError(errorMessage(e, fallbackError));
    } finally {
      if (mountedRef.current && seq === seqRef.current) setSettledKey(key);
    }
  }, [pageSize, fallbackError]);

  const reload = useCallback(
    () => loadFirst(Math.max(pageSize, Math.ceil(shownRef.current / pageSize) * pageSize)),
    [loadFirst, pageSize],
  );

  const refresh = useCallback(async () => {
    setRefreshing(true);
    try {
      await loadFirst(pageSize);
    } finally {
      if (mountedRef.current) setRefreshing(false);
    }
  }, [loadFirst, pageSize]);

  const loading = settledKey !== depsKey;
  const hasMore = hasMorePages(lastResult, page);

  const loadMore = useCallback(async () => {
    if (loadingMore || loading || !hasMore) return;
    const seq = seqRef.current;
    const next = page + 1;
    setLoadingMore(true);
    try {
      const result = await fetchRef.current(next, pageSize);
      if (!mountedRef.current || seq !== seqRef.current) return;
      setItems((current) => {
        const merged = mergePage(current, result.objects ?? [], next, getIdRef.current);
        shownRef.current = merged.length;
        return merged;
      });
      setTotalRecords(result.totalRecords ?? 0);
      setPage(next);
      setLastResult(result);
    } catch (e) {
      if (mountedRef.current && seq === seqRef.current) setError(errorMessage(e, fallbackError));
    } finally {
      if (mountedRef.current) setLoadingMore(false);
    }
  }, [loadingMore, loading, hasMore, page, pageSize, fallbackError]);

  useEffect(() => {
    depsKeyRef.current = depsKey;
    shownRef.current = 0;
    void loadFirst(pageSize);
    // eslint-disable-next-line react-hooks/exhaustive-deps -- the caller lists the filters the list depends on
  }, [...deps, pageSize]);

  return { items, totalRecords, error, loading, refreshing, loadingMore, hasMore, refresh, reload, loadMore };
}
