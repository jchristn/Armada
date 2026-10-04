import { describe, it, expect, beforeEach } from 'vitest';
import { act, renderHook } from '@testing-library/react';
import { useTablePrefs } from './useTablePrefs';

const KEY = 'prefs-test';
const STORAGE = 'armada_table_' + KEY;
const DEFAULT_HIDDEN = ['lastCommit', 'evaluated'];

function stored(): { pageSize?: number; hiddenColumns?: string[]; defaultsVersion?: number } {
  return JSON.parse(localStorage.getItem(STORAGE) || '{}');
}

describe('useTablePrefs', () => {
  beforeEach(() => localStorage.clear());

  it('hides the default-hidden columns for a new user', () => {
    const { result } = renderHook(() => useTablePrefs(KEY, { defaultHidden: DEFAULT_HIDDEN, defaultsVersion: 1 }));
    expect(result.current.isVisible('lastCommit')).toBe(false);
    expect(result.current.isVisible('evaluated')).toBe(false);
    expect(result.current.isVisible('fleet')).toBe(true);
    expect(stored().defaultsVersion).toBe(1);
  });

  it('adds newly default-hidden columns once to a selection saved against an older version', () => {
    localStorage.setItem(STORAGE, JSON.stringify({ pageSize: 50, hiddenColumns: ['ci'] }));
    const { result } = renderHook(() => useTablePrefs(KEY, { defaultHidden: DEFAULT_HIDDEN, defaultsVersion: 1 }));
    expect(result.current.pageSize).toBe(50);
    expect(result.current.isVisible('ci')).toBe(false);
    expect(result.current.isVisible('lastCommit')).toBe(false);
    expect(stored()).toEqual({ pageSize: 50, hiddenColumns: ['ci', 'lastCommit', 'evaluated'], defaultsVersion: 1 });
  });

  it('keeps a selection saved against the current version, including columns the user turned back on', () => {
    localStorage.setItem(STORAGE, JSON.stringify({ pageSize: 25, hiddenColumns: [], defaultsVersion: 1 }));
    const { result } = renderHook(() => useTablePrefs(KEY, { defaultHidden: DEFAULT_HIDDEN, defaultsVersion: 1 }));
    expect(result.current.isVisible('lastCommit')).toBe(true);
    expect(result.current.isVisible('evaluated')).toBe(true);
  });

  it('shows every column with showAllColumns and restores the defaults with resetColumns', () => {
    const { result } = renderHook(() => useTablePrefs(KEY, { defaultHidden: DEFAULT_HIDDEN, defaultsVersion: 1 }));
    act(() => result.current.showAllColumns());
    expect(result.current.hiddenColumns).toEqual([]);
    act(() => result.current.resetColumns());
    expect(result.current.hiddenColumns).toEqual(DEFAULT_HIDDEN);
  });

  it('never hides pinned columns', () => {
    localStorage.setItem(STORAGE, JSON.stringify({ hiddenColumns: ['vessel'] }));
    const { result } = renderHook(() => useTablePrefs(KEY, { pinned: ['vessel'], defaultHidden: DEFAULT_HIDDEN, defaultsVersion: 1 }));
    expect(result.current.isVisible('vessel')).toBe(true);
    act(() => result.current.toggleColumn('vessel'));
    expect(result.current.isVisible('vessel')).toBe(true);
  });
});
