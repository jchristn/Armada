import { useCallback, useEffect, useState } from 'react';

/** Page sizes offered by the shared Pagination component. */
const ALLOWED_PAGE_SIZES = [10, 25, 50, 100, 250];

interface StoredTablePrefs {
  pageSize?: number;
  hiddenColumns?: string[];
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
 * Per-table view preferences persisted in localStorage (same pattern as `useAutoRefresh`): page size and
 * the set of hidden columns. Pinned columns are never hidden even if storage says otherwise.
 */
export function useTablePrefs(key: string, options: { defaultPageSize?: number; defaultHidden?: string[]; pinned?: string[] } = {}) {
  const { defaultPageSize = 25, defaultHidden = [], pinned = [] } = options;
  const [pageSize, setPageSizeState] = useState<number>(() => {
    const stored = readStored(key).pageSize;
    return stored && ALLOWED_PAGE_SIZES.includes(stored) ? stored : defaultPageSize;
  });
  const [hiddenColumns, setHiddenColumns] = useState<string[]>(() => {
    const stored = readStored(key).hiddenColumns;
    const list = Array.isArray(stored) ? stored.filter((c) => typeof c === 'string') : defaultHidden;
    return list.filter((c) => !pinned.includes(c));
  });

  useEffect(() => {
    try {
      localStorage.setItem(storageKey(key), JSON.stringify({ pageSize, hiddenColumns }));
    } catch {
      // Non-fatal: preferences still apply for this session.
    }
  }, [key, pageSize, hiddenColumns]);

  const setPageSize = useCallback((size: number) => {
    setPageSizeState(ALLOWED_PAGE_SIZES.includes(size) ? size : defaultPageSize);
  }, [defaultPageSize]);

  const isVisible = useCallback((column: string) => pinned.includes(column) || !hiddenColumns.includes(column), [hiddenColumns, pinned]);

  const toggleColumn = useCallback((column: string) => {
    if (pinned.includes(column)) return;
    setHiddenColumns((current) => (current.includes(column) ? current.filter((c) => c !== column) : [...current, column]));
  }, [pinned]);

  const resetColumns = useCallback(() => setHiddenColumns(defaultHidden.filter((c) => !pinned.includes(c))), [defaultHidden, pinned]);

  return { pageSize, setPageSize, hiddenColumns, isVisible, toggleColumn, resetColumns };
}
