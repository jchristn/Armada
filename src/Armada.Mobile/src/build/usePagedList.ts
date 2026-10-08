import { useCallback, useEffect, useRef, useState } from 'react';
import type { EnumerationResult } from '@dashboard/types/models';
import { useSocket } from '../socket/SocketContext';
import { errorMessage, matchesLive, type LiveMatcher } from './useLiveResource';

/** Page size the lists request (the dashboard's default table page size). */
export const PAGE_SIZE = 25;

export interface PagedList<T> {
  items: T[];
  totalRecords: number;
  error: string | null;
  loading: boolean;
  refreshing: boolean;
  loadingMore: boolean;
  hasMore: boolean;
  /** Next page (FlatList onEndReached). No-op while loading or at the end. */
  loadMore: () => void;
  /** Pull to refresh: reloads from the first page. */
  refresh: () => Promise<void>;
  /** Reload the pages loaded so far in the background (live updates, after a mutation). */
  reload: () => Promise<void>;
}

export interface PagedListOptions {
  live?: LiveMatcher;
  pageSize?: number;
  enabled?: boolean;
}

/**
 * Endless scrolling over a server enumeration (pageNumber / pageSize / totalPages): the first page loads when `deps`
 * change, `loadMore` appends the next one, and live reloads (matching socket messages, reconnects) refetch as many
 * rows as are already shown so the list keeps its length and scroll position.
 */
export function usePagedList<T>(
  loadPage: (pageNumber: number, pageSize: number) => Promise<EnumerationResult<T>>,
  deps: unknown[],
  options: PagedListOptions = {},
): PagedList<T> {
  const { live, pageSize = PAGE_SIZE, enabled = true } = options;
  const { subscribe, reconnectCount } = useSocket();
  const [items, setItems] = useState<T[]>([]);
  const [totalRecords, setTotalRecords] = useState(0);
  const [pagesLoaded, setPagesLoaded] = useState(0);
  const [totalPages, setTotalPages] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(enabled);
  const [refreshing, setRefreshing] = useState(false);
  const [loadingMore, setLoadingMore] = useState(false);
  const loadRef = useRef(loadPage);
  const liveRef = useRef(live);
  const pagesRef = useRef(0);
  useEffect(() => {
    loadRef.current = loadPage;
    liveRef.current = live;
  });
  const generation = useRef(0);
  const busy = useRef(false);

  /** Load pages 1..count (as one request of count * pageSize rows when count > 1). */
  const loadFirst = useCallback(async (count: number) => {
    const gen = generation.current;
    busy.current = true;
    try {
      const pages = Math.max(1, count);
      const result = await loadRef.current(1, pageSize * pages);
      if (gen !== generation.current) return;
      setItems(result.objects ?? []);
      setTotalRecords(result.totalRecords ?? (result.objects ?? []).length);
      const total = Math.ceil((result.totalRecords ?? 0) / pageSize);
      setTotalPages(total);
      const shown = Math.min(pages, Math.max(1, total));
      pagesRef.current = shown;
      setPagesLoaded(shown);
      setError(null);
    } catch (e) {
      if (gen === generation.current) setError(errorMessage(e));
    } finally {
      if (gen === generation.current) {
        busy.current = false;
        setLoading(false);
      }
    }
  }, [pageSize]);

  useEffect(() => {
    generation.current += 1;
    busy.current = false;
    // eslint-disable-next-line react-hooks/set-state-in-effect -- a fetch keyed on deps; it sets loading state first
    if (!enabled) { setLoading(false); return; }
    setLoading(true);
    setItems([]);
    pagesRef.current = 0;
    setPagesLoaded(0);
    setTotalPages(0);
    setError(null);
    void loadFirst(1);
    // eslint-disable-next-line react-hooks/exhaustive-deps -- deps are the caller's (like useEffect)
  }, [enabled, loadFirst, ...deps]);

  const reload = useCallback(() => loadFirst(pagesRef.current || 1), [loadFirst]);

  const loadMore = useCallback(() => {
    if (busy.current || pagesRef.current >= totalPages || pagesRef.current === 0) return;
    const gen = generation.current;
    const next = pagesRef.current + 1;
    busy.current = true;
    setLoadingMore(true);
    void (async () => {
      try {
        const result = await loadRef.current(next, pageSize);
        if (gen !== generation.current) return;
        setItems((prev) => [...prev, ...(result.objects ?? [])]);
        setTotalRecords(result.totalRecords ?? 0);
        setTotalPages(Math.ceil((result.totalRecords ?? 0) / pageSize));
        pagesRef.current = next;
        setPagesLoaded(next);
      } catch (e) {
        if (gen === generation.current) setError(errorMessage(e));
      } finally {
        if (gen === generation.current) {
          busy.current = false;
          setLoadingMore(false);
        }
      }
    })();
  }, [pageSize, totalPages]);

  const firstReconnect = useRef(reconnectCount);
  useEffect(() => {
    if (reconnectCount === firstReconnect.current || !enabled) return;
    void reload();
  }, [reconnectCount, enabled, reload]);

  useEffect(() => {
    if (!enabled) return undefined;
    return subscribe((msg) => { if (matchesLive(liveRef.current, msg) && !busy.current) void reload(); });
  }, [subscribe, enabled, reload]);

  const refresh = useCallback(async () => {
    setRefreshing(true);
    try { await loadFirst(1); } finally { setRefreshing(false); }
  }, [loadFirst]);

  return { items, totalRecords, error, loading, refreshing, loadingMore, hasMore: pagesLoaded < totalPages, loadMore, refresh, reload };
}
