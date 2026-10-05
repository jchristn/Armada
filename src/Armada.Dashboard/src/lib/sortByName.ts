/**
 * Returns a copy of the items ordered by name (case-insensitive, numbers in natural order). Vessel and fleet pickers
 * use this so they are alphabetical rather than in the server's newest-first order.
 */
export function sortByName<T extends { name: string }>(items: T[] | null | undefined): T[] {
  return (items || []).slice().sort((a, b) => (a.name || '').localeCompare(b.name || '', undefined, { sensitivity: 'base', numeric: true }));
}
