import type { EnumerationResult } from '../types/models';

/**
 * Wrap a mocked array-helper result as the one-page EnumerationResult the paged helpers return, so tests that mock the
 * array helper keep driving pages that now read the enumerate helper. A value that is already a page passes through.
 */
export function asPage<T>(value: T[] | EnumerationResult<T> | null | undefined): EnumerationResult<T> {
  if (value && !Array.isArray(value) && 'objects' in value) return value;
  const objects = (value as T[] | null | undefined) ?? [];
  return { success: true, pageNumber: 1, pageSize: 1000, totalPages: objects.length > 0 ? 1 : 0, totalRecords: objects.length, totalMs: 0, objects };
}
