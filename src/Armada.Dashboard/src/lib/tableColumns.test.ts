import { describe, expect, it } from 'vitest';
import {
  defaultHiddenColumns,
  isColumnShown,
  isDefaultColumnState,
  resolveHiddenColumns,
  toggleHiddenColumn,
  type ColumnVisibilitySpec,
} from './tableColumns';

const COLUMNS: ColumnVisibilitySpec[] = [
  { key: 'name', required: true },
  { key: 'id', required: true },
  { key: 'fleet' },
  { key: 'repo' },
  { key: 'created', defaultHidden: true },
];

describe('tableColumns', () => {
  it('uses the default-hidden columns when nothing usable is stored', () => {
    expect(defaultHiddenColumns(COLUMNS)).toEqual(['created']);
    for (const stored of [null, undefined, 'junk', 42, [], {}, { hidden: 'fleet' }, { hidden: null }]) {
      expect(resolveHiddenColumns(stored, COLUMNS)).toEqual(['created']);
    }
  });

  it('keeps a valid stored choice, including turning a default-hidden column on', () => {
    expect(resolveHiddenColumns({ hidden: ['fleet'], version: 0 }, COLUMNS)).toEqual(['fleet']);
  });

  it('ignores unknown, removed, duplicate, non-string and required keys from older saved state', () => {
    const stored = { hidden: ['fleet', 'removedColumn', 7, null, 'fleet', 'name', 'id'] };
    expect(resolveHiddenColumns(stored, COLUMNS)).toEqual(['fleet']);
  });

  it('hides newly default-hidden columns once when the stored state is older than the defaults version', () => {
    expect(resolveHiddenColumns({ hidden: ['fleet'], version: 1 }, COLUMNS, 2)).toEqual(['fleet', 'created']);
    expect(resolveHiddenColumns({ hidden: ['fleet'] }, COLUMNS, 1)).toEqual(['fleet', 'created']);
    expect(resolveHiddenColumns({ hidden: [], version: 2 }, COLUMNS, 2)).toEqual([]);
  });

  it('never hides a required column', () => {
    expect(isColumnShown(COLUMNS, ['name'], 'name')).toBe(true);
    expect(toggleHiddenColumn(COLUMNS, [], 'name')).toEqual([]);
    expect(toggleHiddenColumn(COLUMNS, [], 'unknown')).toEqual([]);
  });

  it('toggles optional columns and reports the default state', () => {
    const hidden = toggleHiddenColumn(COLUMNS, ['created'], 'fleet');
    expect(hidden).toEqual(['created', 'fleet']);
    expect(isColumnShown(COLUMNS, hidden, 'fleet')).toBe(false);
    expect(isDefaultColumnState(COLUMNS, hidden)).toBe(false);
    expect(isDefaultColumnState(COLUMNS, toggleHiddenColumn(COLUMNS, hidden, 'fleet'))).toBe(true);
  });
});
