import type { Voyage } from '@dashboard/types/models';

/** Voyage statuses, in lifecycle order (the status filter's choices). */
export const VOYAGE_STATUSES = ['Open', 'InProgress', 'Complete', 'Failed', 'Cancelled'] as const;

export type VoyageSort = 'createdUtc:desc' | 'createdUtc:asc' | 'title:asc' | 'title:desc' | 'status:asc' | 'status:desc';

export interface VoyageListFilters {
  search: string;
  status: string;
  sort: VoyageSort;
}

export const DEFAULT_VOYAGE_SORT: VoyageSort = 'createdUtc:desc';

/**
 * The dashboard's client-side column filters and sorting over the loaded page (title and status search; sort by
 * title, status, or created, newest first by default).
 */
export function filterAndSortVoyages(voyages: readonly Voyage[], filters: VoyageListFilters): Voyage[] {
  const term = filters.search.trim().toLowerCase();
  const rows = voyages.filter((v) => {
    if (term && !v.title.toLowerCase().includes(term) && !v.id.toLowerCase().includes(term)) return false;
    if (filters.status && (v.status ?? '') !== filters.status) return false;
    return true;
  });
  const [field, dir] = filters.sort.split(':') as ['createdUtc' | 'title' | 'status', 'asc' | 'desc'];
  const value = (v: Voyage): string => (field === 'createdUtc' ? v.createdUtc : (v[field] ?? '').toLowerCase());
  return [...rows].sort((a, b) => {
    const va = value(a);
    const vb = value(b);
    if (va < vb) return dir === 'asc' ? -1 : 1;
    if (va > vb) return dir === 'asc' ? 1 : -1;
    return 0;
  });
}
