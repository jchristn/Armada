/** Pure helpers for endless-scroll lists over the server's paginated enumerate endpoints. */

export interface PageResult<T> {
  objects: T[];
  totalRecords: number;
  totalPages: number;
  pageNumber: number;
}

/**
 * Appends a page to what is already shown. Page 1 replaces the list; later pages append, skipping items already
 * present (rows shift between pages while new records arrive, so the same id can come back on the next page).
 */
export function mergePage<T>(existing: T[], page: T[], pageNumber: number, getId: (item: T) => string): T[] {
  if (pageNumber <= 1) return page;
  const seen = new Set(existing.map(getId));
  const added = page.filter((item) => !seen.has(getId(item)));
  return added.length ? [...existing, ...added] : existing;
}

/** Whether another page exists after `pageNumber`. */
export function hasMorePages(result: Pick<PageResult<unknown>, 'totalPages'> | null, pageNumber: number): boolean {
  return !!result && pageNumber < (result.totalPages || 1);
}
