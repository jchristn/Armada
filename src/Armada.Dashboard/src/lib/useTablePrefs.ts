import { useCallback, useEffect, useState } from 'react';

/** Page sizes offered by the shared Pagination component. */
const ALLOWED_PAGE_SIZES = [10, 25, 50, 100, 250];

interface StoredTablePrefs {
  pageSize?: number;
  hiddenColumns?: string[];
  /** Version of the table's default column set the stored selection was saved against. */
  defaultsVersion?: number;
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
 *
 * `defaultsVersion` lets a table change its default column set: when the stored selection was saved
 * against an older version, the new default-hidden columns are added to it once (other hidden columns
 * and the page size are kept), and the user can turn them back on from the column chooser.
 */
export function useTablePrefs(key: string, options: { defaultPageSize?: number; defaultHidden?: string[]; pinned?: string[]; defaultsVersion?: number } = {}) {
  const { defaultPageSize = 25, defaultHidden = [], pinned = [], defaultsVersion = 0 } = options;
  const [pageSize, setPageSizeState] = useState<number>(() => {
    const stored = readStored(key).pageSize;
    return stored && ALLOWED_PAGE_SIZES.includes(stored) ? stored : defaultPageSize;
  });
  const [hiddenColumns, setHiddenColumns] = useState<string[]>(() => {
    const stored = readStored(key);
    let list = Array.isArray(stored.hiddenColumns) ? stored.hiddenColumns.filter((c) => typeof c === 'string') : defaultHidden;
    if (Array.isArray(stored.hiddenColumns) && (stored.defaultsVersion ?? 0) < defaultsVersion) {
      list = [...list, ...defaultHidden.filter((c) => !list.includes(c))];
    }
    return list.filter((c) => !pinned.includes(c));
  });

  useEffect(() => {
    try {
      localStorage.setItem(storageKey(key), JSON.stringify({ pageSize, hiddenColumns, defaultsVersion }));
    } catch {
      // Non-fatal: preferences still apply for this session.
    }
  }, [key, pageSize, hiddenColumns, defaultsVersion]);

  const setPageSize = useCallback((size: number) => {
    setPageSizeState(ALLOWED_PAGE_SIZES.includes(size) ? size : defaultPageSize);
  }, [defaultPageSize]);

  const isVisible = useCallback((column: string) => pinned.includes(column) || !hiddenColumns.includes(column), [hiddenColumns, pinned]);

  const toggleColumn = useCallback((column: string) => {
    if (pinned.includes(column)) return;
    setHiddenColumns((current) => (current.includes(column) ? current.filter((c) => c !== column) : [...current, column]));
  }, [pinned]);

  const resetColumns = useCallback(() => setHiddenColumns(defaultHidden.filter((c) => !pinned.includes(c))), [defaultHidden, pinned]);

  const showAllColumns = useCallback(() => setHiddenColumns([]), []);

  return { pageSize, setPageSize, hiddenColumns, isVisible, toggleColumn, resetColumns, showAllColumns };
}
