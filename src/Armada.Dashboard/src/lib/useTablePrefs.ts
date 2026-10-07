import { useCallback, useEffect, useState } from 'react';

/** Page sizes offered by the shared Pagination component. */
const ALLOWED_PAGE_SIZES = [10, 25, 50, 100, 250];

interface StoredTablePrefs {
  pageSize?: number;
  [legacy: string]: unknown;
}

function storageKey(key: string): string {
  return `armada_table_${key}`;
}

function readStored(key: string): StoredTablePrefs {
  try {
    const raw = localStorage.getItem(storageKey(key));
    if (!raw) return {};
    const parsed = JSON.parse(raw) as StoredTablePrefs;
    return parsed && typeof parsed === 'object' ? parsed : {};
  } catch {
    return {};
  }
}

/**
 * Per-table page size persisted in localStorage (same pattern as `useAutoRefresh`), for server-paged tables.
 * Column visibility lives in the shared DataTable (`useColumnVisibility`), which reads the hidden columns older
 * builds kept in this entry once.
 */
export function useTablePrefs(key: string, options: { defaultPageSize?: number } = {}) {
  const { defaultPageSize = 25 } = options;
  const [pageSize, setPageSizeState] = useState<number>(() => {
    const stored = readStored(key).pageSize;
    return typeof stored === 'number' && ALLOWED_PAGE_SIZES.includes(stored) ? stored : defaultPageSize;
  });

  useEffect(() => {
    try {
      // Merge: older builds kept the hidden columns in this entry, and the DataTable may mount later than this
      // hook (after the first load) and still needs to read them once.
      localStorage.setItem(storageKey(key), JSON.stringify({ ...readStored(key), pageSize }));
    } catch {
      // Non-fatal: the page size still applies for this session.
    }
  }, [key, pageSize]);

  const setPageSize = useCallback((size: number) => {
    setPageSizeState(ALLOWED_PAGE_SIZES.includes(size) ? size : defaultPageSize);
  }, [defaultPageSize]);

  return { pageSize, setPageSize };
}
