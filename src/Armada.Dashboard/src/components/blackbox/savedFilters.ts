import type { BlackBoxEnumerateQuery } from '../../types/blackbox';

// Saved filters of the Black Box page, kept in this browser (dashboard only: localStorage).

/** A named set of Black Box page filters kept in this browser. */
export interface BlackBoxSavedFilter {
  name: string;
  query: BlackBoxEnumerateQuery & { search?: string };
}

export const BLACKBOX_SAVED_FILTERS_KEY = 'armada_blackbox_saved_filters';

/** The saved filters (empty when storage is unavailable or holds something unreadable). */
export function loadBlackBoxSavedFilters(): BlackBoxSavedFilter[] {
  try {
    const raw = localStorage.getItem(BLACKBOX_SAVED_FILTERS_KEY);
    if (!raw) return [];
    const parsed: unknown = JSON.parse(raw);
    if (!Array.isArray(parsed)) return [];
    return parsed.filter((f): f is BlackBoxSavedFilter => !!f && typeof f === 'object' && typeof (f as BlackBoxSavedFilter).name === 'string' && typeof (f as BlackBoxSavedFilter).query === 'object');
  } catch {
    return [];
  }
}

/** Store the saved filters; false when storage is unavailable. */
export function storeBlackBoxSavedFilters(filters: BlackBoxSavedFilter[]): boolean {
  try {
    localStorage.setItem(BLACKBOX_SAVED_FILTERS_KEY, JSON.stringify(filters));
    return true;
  } catch {
    return false;
  }
}
