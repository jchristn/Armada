import { useCallback, useMemo, useState } from 'react';
import type { EnumerationResult } from '@dashboard/types/models';
import { errorText, useLoad, type LoadOptions } from '../../resource/useLoad';

export interface PagedLoad<T> {
  items: T[];
  /** The first page's result (totals); null before it arrives. */
  first: EnumerationResult<T> | null;
  loading: boolean;
  refreshing: boolean;
  loadingMore: boolean;
  error: string;
  hasMore: boolean;
  loadMore: () => void;
  reload: () => Promise<void>;
  refresh: () => Promise<void>;
}

interface Extra<T> {
  base: EnumerationResult<T> | null;
  items: T[];
  next: number;
  totalPages: number;
}

/**
 * Server-paged list (the dashboard's pager, as endless scroll): page 1 loads through useLoad (pull to refresh, live
 * reloads), later pages are appended as the list nears its end. Any reload of page 1 starts over.
 */
export function usePagedLoad<T>(fetchPage: (pageNumber: number) => Promise<EnumerationResult<T>>, deps: readonly unknown[], options: LoadOptions = {}): PagedLoad<T> {
  const first = useLoad(() => fetchPage(1), deps, options);
  const [extra, setExtra] = useState<Extra<T> | null>(null);
  const [loadingMore, setLoadingMore] = useState(false);
  const [moreError, setMoreError] = useState('');

  const current = extra && extra.base === first.data ? extra : null;
  const items = useMemo(() => [...(first.data?.objects ?? []), ...(current?.items ?? [])], [first.data, current]);
  const next = current?.next ?? 2;
  const totalPages = current?.totalPages ?? first.data?.totalPages ?? 1;
  const hasMore = !!first.data && next <= totalPages;

  const loadMore = useCallback(() => {
    if (!first.data || loadingMore || !hasMore) return;
    const base = first.data;
    setLoadingMore(true);
    fetchPage(next)
      .then((result) => {
        setExtra((prev) => {
          const kept = prev && prev.base === base ? prev.items : [];
          return { base, items: [...kept, ...(result.objects ?? [])], next: next + 1, totalPages: result.totalPages || totalPages };
        });
        setMoreError('');
      })
      .catch((err: unknown) => setMoreError(errorText(err, 'Failed to load more.')))
      .finally(() => setLoadingMore(false));
  }, [first.data, loadingMore, hasMore, fetchPage, next, totalPages]);

  return {
    items,
    first: first.data,
    loading: first.loading,
    refreshing: first.refreshing,
    loadingMore,
    error: first.error || moreError,
    hasMore,
    loadMore,
    reload: first.reload,
    refresh: first.refresh,
  };
}
