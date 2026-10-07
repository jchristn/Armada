import { describe, it, expect, beforeEach, vi, afterEach } from 'vitest';
import { act, renderHook } from '@testing-library/react';
import { useTablePrefs } from './useTablePrefs';

const KEY = 'prefs-test';
const STORAGE = 'armada_table_' + KEY;

function stored(): { pageSize?: number } {
  return JSON.parse(localStorage.getItem(STORAGE) || '{}');
}

describe('useTablePrefs', () => {
  beforeEach(() => localStorage.clear());
  afterEach(() => vi.restoreAllMocks());

  it('uses the default page size for a new user and persists it', () => {
    const { result } = renderHook(() => useTablePrefs(KEY));
    expect(result.current.pageSize).toBe(25);
    expect(stored()).toEqual({ pageSize: 25 });
  });

  it('restores and persists a chosen page size', () => {
    localStorage.setItem(STORAGE, JSON.stringify({ pageSize: 50, hiddenColumns: ['ci'] }));
    const { result } = renderHook(() => useTablePrefs(KEY));
    expect(result.current.pageSize).toBe(50);
    act(() => result.current.setPageSize(100));
    expect(result.current.pageSize).toBe(100);
    // Other fields (the hidden columns older builds stored here) are kept for the DataTable to migrate.
    expect(stored()).toEqual({ pageSize: 100, hiddenColumns: ['ci'] });
  });

  it('rejects page sizes the pager does not offer', () => {
    localStorage.setItem(STORAGE, JSON.stringify({ pageSize: 37 }));
    const { result } = renderHook(() => useTablePrefs(KEY, { defaultPageSize: 10 }));
    expect(result.current.pageSize).toBe(10);
    act(() => result.current.setPageSize(999));
    expect(result.current.pageSize).toBe(10);
  });

  it('falls back to the default when storage is corrupt or unavailable', () => {
    localStorage.setItem(STORAGE, '{not json');
    expect(renderHook(() => useTablePrefs(KEY)).result.current.pageSize).toBe(25);
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => { throw new Error('SecurityError'); });
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('QuotaExceededError'); });
    const { result } = renderHook(() => useTablePrefs(KEY));
    expect(result.current.pageSize).toBe(25);
    act(() => result.current.setPageSize(50));
    expect(result.current.pageSize).toBe(50);
  });
});
