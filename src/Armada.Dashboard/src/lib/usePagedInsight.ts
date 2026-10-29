import { useCallback, useEffect, useRef, useState } from 'react';

/** The paging fields every insight page carries (EnumerationResult and its subclasses). */
export interface PagedInsightResult<T> {
  objects?: T[];
  totalRecords?: number;
  totalPages?: number;
}

export interface PagedInsightState<T, P extends PagedInsightResult<T>> {
  pageNumber: number;
  pageSize: number;
  setPageNumber: (page: number) => void;
  setPageSize: (size: number) => void;
  result: P | null;
  rows: T[];
  totalRecords: number;
  totalPages: number;
  loading: boolean;
  error: string;
  reload: () => Promise<void>;
}

/**
 * One server page of an insight collection (reservations, doctor findings, timeline, nearby successes, recovery
 * statistics). `key` identifies the query: changing it goes back to page 1. A response for an older page or key is
 * dropped.
 */
export function usePagedInsight<T, P extends PagedInsightResult<T>>(
  key: string,
  fetchPage: (pageNumber: number, pageSize: number) => Promise<P>,
  initialPageSize = 10,
  enabled = true,
): PagedInsightState<T, P> {
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSizeState] = useState(initialPageSize);
  const [result, setResult] = useState<P | null>(null);
  const [loading, setLoading] = useState(enabled);
  const [error, setError] = useState('');
  const [shownKey, setShownKey] = useState(key);
  const sequence = useRef(0);
  const fetchRef = useRef(fetchPage);
  fetchRef.current = fetchPage;

  if (shownKey !== key) {
    setShownKey(key);
    setPageNumber(1);
  }

  const reload = useCallback(async () => {
    if (!enabled) { setLoading(false); return; }
    const current = ++sequence.current;
    setLoading(true);
    try {
      const next = await fetchRef.current(pageNumber, pageSize);
      if (current !== sequence.current) return;
      setResult(next);
      setError('');
    } catch (err: unknown) {
      if (current !== sequence.current) return;
      setError(err instanceof Error ? err.message : String(err));
    } finally {
      if (current === sequence.current) setLoading(false);
    }
  }, [pageNumber, pageSize, enabled, shownKey]); // eslint-disable-line react-hooks/exhaustive-deps

  useEffect(() => { void reload(); }, [reload]);

  return {
    pageNumber,
    pageSize,
    setPageNumber,
    setPageSize: (size: number) => { setPageSizeState(size); setPageNumber(1); },
    result,
    rows: result?.objects ?? [],
    totalRecords: result?.totalRecords ?? 0,
    totalPages: Math.max(1, result?.totalPages ?? 1),
    loading,
    error,
    reload,
  };
}
