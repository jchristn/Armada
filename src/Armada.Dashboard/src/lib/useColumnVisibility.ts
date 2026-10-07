import { useCallback, useEffect, useMemo, useState } from 'react';
import {
  defaultHiddenColumns,
  isColumnShown,
  isDefaultColumnState,
  resolveHiddenColumns,
  toggleHiddenColumn,
  type ColumnVisibilitySpec,
} from './tableColumns';

/** localStorage key for a table's column choice. */
export function columnStorageKey(tableKey: string): string {
  return `armada_columns_${tableKey}`;
}

/** Older builds kept hidden columns inside the table preferences entry (`useTablePrefs`); read once as a fallback. */
function legacyStorageKey(tableKey: string): string {
  return `armada_table_${tableKey}`;
}

function readJson(key: string): unknown {
  try {
    const raw = localStorage.getItem(key);
    return raw ? JSON.parse(raw) : null;
  } catch {
    // Storage disabled, private mode, quota, or corrupt JSON: use the defaults.
    return null;
  }
}

function readStored(tableKey: string): unknown {
  const current = readJson(columnStorageKey(tableKey));
  if (current) return current;
  const legacy = readJson(legacyStorageKey(tableKey)) as { hiddenColumns?: unknown; defaultsVersion?: unknown } | null;
  if (legacy && Array.isArray(legacy.hiddenColumns)) return { hidden: legacy.hiddenColumns, version: legacy.defaultsVersion };
  return null;
}

export interface ColumnVisibility {
  /** Keys of the columns the user hid. */
  hidden: string[];
  isVisible: (key: string) => boolean;
  toggle: (key: string) => void;
  reset: () => void;
  /** True when the current choice equals the table's defaults. */
  isDefault: boolean;
}

/**
 * Which columns of a table are shown, persisted per table in localStorage under `armada_columns_<tableKey>`.
 * Required columns are always shown; stored keys for columns that no longer exist are ignored; unreadable or
 * corrupt storage falls back to the defaults, and a failed write only loses persistence, never the choice.
 * Pass `version` and bump it when the table's default-hidden set changes (see `resolveHiddenColumns`).
 */
export function useColumnVisibility(tableKey: string, columns: ColumnVisibilitySpec[], version = 0): ColumnVisibility {
  // Columns are usually rebuilt every render; key the memo on their content.
  const signature = columns.map((c) => `${c.key}:${c.required ? 1 : 0}${c.defaultHidden ? 1 : 0}`).join('|');
  // eslint-disable-next-line react-hooks/exhaustive-deps
  const specs = useMemo(() => columns.map((c) => ({ key: c.key, required: c.required, defaultHidden: c.defaultHidden })), [signature]);

  const [hidden, setHidden] = useState<string[]>(() => resolveHiddenColumns(readStored(tableKey), specs, version));
  const [loadedKey, setLoadedKey] = useState(tableKey);

  // A different table key (rare: one component reused for two tables) loads that table's choice.
  if (loadedKey !== tableKey) {
    setLoadedKey(tableKey);
    setHidden(resolveHiddenColumns(readStored(tableKey), specs, version));
  }

  useEffect(() => {
    try {
      localStorage.setItem(columnStorageKey(tableKey), JSON.stringify({ hidden, version }));
    } catch {
      // Non-fatal: the choice still applies for this session.
    }
  }, [tableKey, hidden, version]);

  const isVisible = useCallback((key: string) => isColumnShown(specs, hidden, key), [specs, hidden]);
  const toggle = useCallback((key: string) => setHidden((current) => toggleHiddenColumn(specs, current, key)), [specs]);
  const reset = useCallback(() => setHidden(defaultHiddenColumns(specs)), [specs]);

  return { hidden, isVisible, toggle, reset, isDefault: isDefaultColumnState(specs, hidden) };
}
