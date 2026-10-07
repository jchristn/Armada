import { Fragment, type ReactNode } from 'react';
import { useLocale } from '../../context/LocaleContext';
import { useColumnVisibility } from '../../lib/useColumnVisibility';
import AutoRefreshSelect from './AutoRefreshSelect';
import ColumnChooser from './ColumnChooser';
import Pagination from './Pagination';
import RefreshButton from './RefreshButton';

export interface DataTableColumn<T> {
  /** Stable key: persisted in the column choice and set as `data-col` on the header and cells. */
  key: string;
  /** Already-translated label, shown in the header (unless `header` is set) and in the column chooser. */
  label: string;
  /** Custom header content (defaults to `label`). */
  header?: ReactNode;
  /** Already-translated header tooltip. */
  headerTitle?: string;
  /** Sort field passed to `sort.onSort`; the header becomes a sort button with aria-sort. */
  sortKey?: string;
  /** Identity column (name, ID, or the table's primary identifier): always shown, locked in the chooser. */
  required?: boolean;
  /** Hidden until the user turns it on in the chooser. */
  defaultHidden?: boolean;
  /** Utility column (row actions, expanders, unlabeled controls): always shown and not listed in the chooser. */
  fixed?: boolean;
  /** Content of this column's cell in the filter row (the row appears when any visible column has one). */
  filter?: ReactNode;
  /** Called when the user hides this column, so a filter it holds stops applying invisibly. */
  clearFilter?: () => void;
  render: (row: T, index: number) => ReactNode;
  /** Class on both the header and the cells. */
  className?: string;
  headerClassName?: string;
  cellClassName?: string | ((row: T) => string | undefined);
  cellTitle?: (row: T) => string | undefined;
  /** Clicks inside the cell do not trigger `onRowClick` (buttons, links, menus). */
  interactive?: boolean;
}

export interface DataTablePagination {
  pageNumber: number;
  pageSize: number;
  totalPages: number;
  totalRecords: number;
  totalMs?: number;
  onPageChange: (page: number) => void;
  onPageSizeChange: (size: number) => void;
}

export interface DataTableSort {
  field: string;
  dir: 'asc' | 'desc';
  onSort: (field: string) => void;
}

export interface DataTableSelection<T> {
  isSelected: (row: T) => boolean;
  onToggle: (row: T) => void;
  allSelected: boolean;
  onToggleAll: (checked: boolean) => void;
  /** Already-translated label for the select-all checkbox. */
  selectAllLabel: string;
  /** Already-translated label for a row checkbox. */
  rowLabel: (row: T) => string;
}

export interface DataTableProps<T> {
  /** Persistence key for the column choice (`armada_columns_<tableKey>`); unique per table. */
  tableKey: string;
  columns: DataTableColumn<T>[];
  rows: T[];
  rowKey: (row: T) => string;
  onRowClick?: (row: T) => void;
  rowClassName?: (row: T) => string | undefined;
  rowTitle?: (row: T) => string | undefined;
  /** Extra content rendered after a row (an expanded detail row); receives the visible column count. */
  renderRowDetail?: (row: T, colSpan: number) => ReactNode;
  selection?: DataTableSelection<T>;
  sort?: DataTableSort;
  /** Shown in a full-width row when `rows` is empty. */
  emptyMessage?: ReactNode;
  /** Replaces the table (the toolbar stays), e.g. a loading indicator or an empty-state card. */
  placeholder?: ReactNode;
  /** Pager in the toolbar; without it the toolbar shows the record count only. */
  pagination?: DataTablePagination;
  /** Record count shown when there is no pager (defaults to rows.length); null hides it. */
  recordCount?: number | null;
  autoRefresh?: { seconds: number; onChange: (seconds: number) => void };
  onRefresh?: () => Promise<void>;
  /** Refresh button tooltip (a catalog key; RefreshButton translates it). */
  refreshTitle?: string;
  /** Extra toolbar content shown before the column chooser. */
  toolbarExtra?: ReactNode;
  /** Bump when the table's default-hidden columns change (see resolveHiddenColumns). */
  columnsVersion?: number;
  className?: string;
  wrapClassName?: string;
  /** Already-translated accessible name for the table. */
  ariaLabel?: string;
  busy?: boolean;
}

function sortIndicator(active: boolean, dir: 'asc' | 'desc'): string {
  if (!active) return '⇅';
  return dir === 'asc' ? '▲' : '▼';
}

/**
 * The dashboard's table. Every tabular view renders through it so they share one toolbar and one set of rules:
 *
 * - Toolbar (one row): record count on the left; extra tools, the column chooser, auto-refresh, refresh, and the
 *   pager on the right.
 * - Column chooser: users show or hide columns; the choice persists per `tableKey` (see useColumnVisibility).
 *   Required (identity) columns and fixed utility columns are always shown.
 * - Sortable headers are buttons with aria-sort; an optional filter row and selection column.
 */
export default function DataTable<T>(props: DataTableProps<T>) {
  const {
    tableKey, columns, rows, rowKey, onRowClick, rowClassName, rowTitle, renderRowDetail, selection, sort,
    emptyMessage, placeholder, pagination, recordCount, autoRefresh, onRefresh, refreshTitle, toolbarExtra,
    columnsVersion = 0, className, wrapClassName, ariaLabel, busy,
  } = props;
  const { t } = useLocale();
  const visibility = useColumnVisibility(
    tableKey,
    columns.map((c) => ({ key: c.key, required: c.required || c.fixed, defaultHidden: c.defaultHidden })),
    columnsVersion,
  );

  const visible = columns.filter((c) => visibility.isVisible(c.key));
  const choosable = columns.filter((c) => !c.fixed);
  const showChooser = choosable.some((c) => !c.required);
  const colSpan = visible.length + (selection ? 1 : 0);
  const hasFilters = visible.some((c) => c.filter !== undefined);

  function toggleColumn(key: string) {
    const column = columns.find((c) => c.key === key);
    if (column && visibility.isVisible(key)) column.clearFilter?.();
    visibility.toggle(key);
  }

  function resetColumns() {
    for (const column of columns) {
      if (column.defaultHidden && visibility.isVisible(column.key)) column.clearFilter?.();
    }
    visibility.reset();
  }

  const tools = (
    <>
      {toolbarExtra}
      {showChooser && (
        <ColumnChooser
          options={choosable.map((c) => ({ key: c.key, label: c.label, required: c.required }))}
          isVisible={visibility.isVisible}
          onToggle={toggleColumn}
          onReset={resetColumns}
          isDefault={visibility.isDefault}
        />
      )}
      {autoRefresh && <AutoRefreshSelect seconds={autoRefresh.seconds} onChange={autoRefresh.onChange} />}
      {onRefresh && <RefreshButton onRefresh={onRefresh} title={refreshTitle} />}
    </>
  );
  const hasTools = Boolean(toolbarExtra || showChooser || autoRefresh || onRefresh);
  const count = recordCount === undefined ? rows.length : recordCount;

  const toolbar = pagination ? (
    <Pagination {...pagination} tools={hasTools ? tools : undefined} />
  ) : (count !== null || hasTools) ? (
    <div className="pagination-bar table-toolbar">
      <div className="pagination-info">
        {count !== null && (
          <span className="pagination-records">
            {t(count === 1 ? '{{count}} record' : '{{count}} records', { count: count.toLocaleString() })}
          </span>
        )}
      </div>
      {hasTools && <div className="pagination-controls"><div className="table-tools">{tools}</div></div>}
    </div>
  ) : null;

  function cellClass(column: DataTableColumn<T>, row: T): string | undefined {
    const extra = typeof column.cellClassName === 'function' ? column.cellClassName(row) : column.cellClassName;
    const joined = [column.className, extra].filter(Boolean).join(' ');
    return joined || undefined;
  }

  function headerContent(column: DataTableColumn<T>): ReactNode {
    const content = column.header ?? column.label;
    if (!column.sortKey || !sort) return content;
    const active = sort.field === column.sortKey;
    return (
      <button type="button" className="th-sort-btn" title={column.headerTitle} onClick={() => sort.onSort(column.sortKey!)}>
        {content}
        <span aria-hidden="true" className={`th-sort-icon${active ? ' th-sort-active' : ''}`}>{sortIndicator(active, sort.dir)}</span>
      </button>
    );
  }

  function ariaSort(column: DataTableColumn<T>): 'ascending' | 'descending' | 'none' | undefined {
    if (!column.sortKey || !sort) return undefined;
    if (sort.field !== column.sortKey) return 'none';
    return sort.dir === 'asc' ? 'ascending' : 'descending';
  }

  return (
    <div className="data-table" data-table={tableKey}>
      {toolbar}
      {placeholder ?? (
        <div className={`table-wrap${wrapClassName ? ` ${wrapClassName}` : ''}`} aria-busy={busy || undefined}>
          <table className={className} aria-label={ariaLabel}>
            <thead>
              <tr>
                {selection && (
                  <th className="col-checkbox" scope="col">
                    <input
                      type="checkbox"
                      checked={selection.allSelected}
                      aria-label={selection.selectAllLabel}
                      title={selection.selectAllLabel}
                      onChange={(e) => selection.onToggleAll(e.target.checked)}
                    />
                  </th>
                )}
                {visible.map((column) => (
                  <th
                    key={column.key}
                    scope="col"
                    data-col={column.key}
                    aria-sort={ariaSort(column)}
                    title={column.sortKey && sort ? undefined : column.headerTitle}
                    className={[column.className, column.headerClassName].filter(Boolean).join(' ') || undefined}
                  >
                    {headerContent(column)}
                  </th>
                ))}
              </tr>
              {hasFilters && (
                <tr className="column-filter-row">
                  {selection && <td />}
                  {visible.map((column) => <td key={column.key} data-col={column.key}>{column.filter}</td>)}
                </tr>
              )}
            </thead>
            <tbody>
              {rows.map((row, index) => {
                const id = rowKey(row);
                const rowClasses = [onRowClick ? 'clickable' : '', rowClassName?.(row) ?? ''].filter(Boolean).join(' ');
                return (
                  <Fragment key={id}>
                    <tr className={rowClasses || undefined} title={rowTitle?.(row)} onClick={onRowClick ? () => onRowClick(row) : undefined}>
                      {selection && (
                        <td className="col-checkbox" onClick={(e) => e.stopPropagation()}>
                          <input
                            type="checkbox"
                            checked={selection.isSelected(row)}
                            aria-label={selection.rowLabel(row)}
                            title={selection.rowLabel(row)}
                            onChange={() => selection.onToggle(row)}
                          />
                        </td>
                      )}
                      {visible.map((column) => (
                        <td
                          key={column.key}
                          data-col={column.key}
                          className={cellClass(column, row)}
                          title={column.cellTitle?.(row)}
                          onClick={column.interactive ? (e) => e.stopPropagation() : undefined}
                        >
                          {column.render(row, index)}
                        </td>
                      ))}
                    </tr>
                    {renderRowDetail?.(row, colSpan)}
                  </Fragment>
                );
              })}
              {rows.length === 0 && emptyMessage !== undefined && (
                <tr className="data-table-empty"><td colSpan={colSpan} className="text-dim">{emptyMessage}</td></tr>
              )}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
