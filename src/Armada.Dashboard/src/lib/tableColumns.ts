/**
 * Column visibility rules for the shared data table. Pure and host-agnostic (no window, document or storage), so
 * the mobile app can share it; the dashboard persists the result through `useColumnVisibility`.
 */

/** What the visibility rules need to know about a table column. */
export interface ColumnVisibilitySpec {
  /** Stable key, used in persisted state. */
  key: string;
  /** Identity column (name, ID, or the table's primary identifier): always shown, cannot be hidden. */
  required?: boolean;
  /** Hidden until the user turns it on. */
  defaultHidden?: boolean;
}

/** The persisted shape: the keys the user hid, and the version of the table's defaults they were saved against. */
export interface StoredColumnState {
  hidden: string[];
  version: number;
}

/** Keys of the columns hidden by default (never a required column). */
export function defaultHiddenColumns(columns: ColumnVisibilitySpec[]): string[] {
  return columns.filter((c) => c.defaultHidden && !c.required).map((c) => c.key);
}

/**
 * Turns whatever was stored (possibly written by an older build, edited by hand, or corrupt) into a valid hidden
 * list for the current columns. Anything that is not `{ hidden: string[] }` falls back to the defaults; keys of
 * columns that no longer exist and required columns are dropped. When the stored state was saved against an older
 * `version` of the defaults, columns that are now hidden by default are hidden once more (the user can turn them
 * back on); other choices are kept.
 */
export function resolveHiddenColumns(stored: unknown, columns: ColumnVisibilitySpec[], version = 0): string[] {
  const defaults = defaultHiddenColumns(columns);
  if (!stored || typeof stored !== 'object' || !Array.isArray((stored as { hidden?: unknown }).hidden)) return defaults;
  const known = new Set(columns.filter((c) => !c.required).map((c) => c.key));
  const raw = (stored as { hidden: unknown[] }).hidden;
  const hidden: string[] = [];
  for (const key of raw) {
    if (typeof key === 'string' && known.has(key) && !hidden.includes(key)) hidden.push(key);
  }
  const storedVersion = Number((stored as { version?: unknown }).version);
  if ((Number.isFinite(storedVersion) ? storedVersion : 0) < version) {
    for (const key of defaults) if (!hidden.includes(key)) hidden.push(key);
  }
  return hidden;
}

/** Whether a column is shown: required columns always are; others unless hidden. */
export function isColumnShown(columns: ColumnVisibilitySpec[], hidden: string[], key: string): boolean {
  const column = columns.find((c) => c.key === key);
  if (column?.required) return true;
  return !hidden.includes(key);
}

/** The hidden list after toggling one column. Required and unknown columns are never hidden. */
export function toggleHiddenColumn(columns: ColumnVisibilitySpec[], hidden: string[], key: string): string[] {
  const column = columns.find((c) => c.key === key);
  if (!column || column.required) return hidden;
  return hidden.includes(key) ? hidden.filter((k) => k !== key) : [...hidden, key];
}

/** True when the hidden list equals the table's defaults (order does not matter). */
export function isDefaultColumnState(columns: ColumnVisibilitySpec[], hidden: string[]): boolean {
  const defaults = defaultHiddenColumns(columns);
  return defaults.length === hidden.length && defaults.every((k) => hidden.includes(k));
}
