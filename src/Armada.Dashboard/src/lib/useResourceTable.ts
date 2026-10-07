import { useMemo, useState } from 'react';

export type SortDir = 'asc' | 'desc';

interface UseResourceTableOptions<T> {
  rows: T[];
  getId: (row: T) => string;
  /** Column key -> value extractor, used both for the per-column filter inputs and (optionally) sorting. */
  columnValues?: Record<string, (row: T) => string | number | null | undefined>;
  /** Optional free-text search over these extractors (any match keeps the row). */
  searchFields?: Array<(row: T) => string | null | undefined>;
  initialSortField?: string;
  initialSortDir?: SortDir;
  initialPageSize?: number;
}

/**
 * Owns the state and derivations every list page re-implemented by hand: free-text search,
 * per-column filters, sorting, pagination, and row selection. Pages keep their own table markup
 * but read `paginated`, `colFilters`, `handleSort`, selection, etc. from here instead of copying
 * ~40 lines of boilerplate. Consolidation target #1 in SIMPLIFICATION.md.
 */
export function useResourceTable<T>(options: UseResourceTableOptions<T>) {
  const {
    rows,
    getId,
    columnValues = {},
    searchFields = [],
    initialSortField = '',
    initialSortDir = 'asc',
    initialPageSize = 25,
  } = options;

  const [search, setSearch] = useState('');
  const [colFilters, setColFiltersState] = useState<Record<string, string>>({});
  const [sortField, setSortField] = useState<string>(initialSortField);
  const [sortDir, setSortDir] = useState<SortDir>(initialSortDir);
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(initialPageSize);
  const [selected, setSelected] = useState<string[]>([]);

  function setColFilter(key: string, value: string) {
    setColFiltersState((current) => ({ ...current, [key]: value }));
    setPageNumber(1);
  }

  function updateSearch(value: string) {
    setSearch(value);
    setPageNumber(1);
  }

  const filtered = useMemo(() => {
    const term = search.trim().toLowerCase();
    return rows.filter((row) => {
      if (term && searchFields.length > 0) {
        const hit = searchFields.some((accessor) => (accessor(row) ?? '').toString().toLowerCase().includes(term));
        if (!hit) return false;
      }
      for (const [key, value] of Object.entries(colFilters)) {
        if (!value) continue;
        const extractor = columnValues[key];
        const cell = (extractor ? extractor(row) : '') ?? '';
        if (!cell.toString().toLowerCase().includes(value.toLowerCase())) return false;
      }
      return true;
    });
  }, [rows, search, colFilters, searchFields, columnValues]);

  const sorted = useMemo(() => {
    if (!sortField || !columnValues[sortField]) return filtered;
    const accessor = columnValues[sortField];
    const arr = [...filtered];
    arr.sort((a, b) => {
      const va = accessor(a);
      const vb = accessor(b);
      const na = va ?? '';
      const nb = vb ?? '';
      if (na < nb) return sortDir === 'asc' ? -1 : 1;
      if (na > nb) return sortDir === 'asc' ? 1 : -1;
      return 0;
    });
    return arr;
  }, [filtered, sortField, sortDir, columnValues]);

  const totalPages = Math.max(1, Math.ceil(sorted.length / pageSize));
  const currentPage = Math.min(pageNumber, totalPages);
  const paginated = useMemo(() => {
    const start = (currentPage - 1) * pageSize;
    return sorted.slice(start, start + pageSize);
  }, [sorted, currentPage, pageSize]);

  function handleSort(field: string) {
    if (sortField === field) setSortDir((d) => (d === 'asc' ? 'desc' : 'asc'));
    else {
      setSortField(field);
      setSortDir('asc');
    }
  }

  function sortIcon(field: string) {
    if (sortField !== field) return '';
    return sortDir === 'asc' ? ' \u25B2' : ' \u25BC';
  }

  const allSelected = selected.length > 0 && selected.length === filtered.length;
  function toggleSelect(id: string) {
    setSelected((s) => (s.includes(id) ? s.filter((x) => x !== id) : [...s, id]));
  }
  function selectAll() {
    setSelected(filtered.map(getId));
  }
  function clearSelection() {
    setSelected([]);
  }

  /** Props for DataTable's `sort`. */
  const sortState = { field: sortField, dir: sortDir, onSort: handleSort };

  /** Props for DataTable's `pagination` (client-side paging over the filtered rows). */
  const paginationProps = {
    pageNumber: currentPage,
    pageSize,
    totalPages,
    totalRecords: sorted.length,
    onPageChange: (page: number) => setPageNumber(Math.max(1, Math.min(totalPages, page))),
    onPageSizeChange: (size: number) => {
      setPageSize(size);
      setPageNumber(1);
    },
  };

  return {
    sortState,
    paginationProps,
    search,
    setSearch: updateSearch,
    colFilters,
    setColFilter,
    sortField,
    sortDir,
    handleSort,
    sortIcon,
    pageNumber,
    setPageNumber,
    pageSize,
    setPageSize,
    totalPages,
    currentPage,
    filtered,
    sorted,
    paginated,
    selected,
    setSelected,
    toggleSelect,
    selectAll,
    clearSelection,
    allSelected,
  };
}
