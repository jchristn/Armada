import { useState, type ReactNode } from 'react';
import { useLocale } from '../../context/LocaleContext';
import { PAGE_SIZE_OPTIONS } from '../../lib/pageSizePrefs';
import LoadingValue from './LoadingValue';

interface PaginationProps {
  pageNumber: number;
  pageSize: number;
  totalPages: number;
  totalRecords: number;
  totalMs?: number;
  onPageChange: (page: number) => void;
  onPageSizeChange: (size: number) => void;
  /** Table tools (column chooser, auto-refresh, refresh) shown in the same row, before the pager. */
  tools?: ReactNode;
  /** True while the first page loads: the record count and timing show a spinner instead of 0. */
  loading?: boolean;
}

const PAGE_SIZES = PAGE_SIZE_OPTIONS;

/**
 * The table header bar: record count (and timing) on the left; table tools, Prev / page / Next and the page size
 * on the right. Usually rendered by DataTable rather than directly.
 */
export default function Pagination({
  pageNumber,
  pageSize,
  totalPages,
  totalRecords,
  totalMs,
  onPageChange,
  onPageSizeChange,
  tools,
  loading = false,
}: PaginationProps) {
  const { t } = useLocale();
  const [pageInput, setPageInput] = useState(String(pageNumber));
  const [shownPage, setShownPage] = useState(pageNumber);

  // Keep the page input in step when the page changes from outside (filters, page size, a parent reset).
  if (shownPage !== pageNumber) {
    setShownPage(pageNumber);
    setPageInput(String(pageNumber));
  }

  const handlePageInputSubmit = () => {
    const num = parseInt(pageInput, 10);
    if (num >= 1 && num <= totalPages) {
      onPageChange(num);
    } else {
      setPageInput(String(pageNumber));
    }
  };

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === 'Enter') handlePageInputSubmit();
  };

  return (
    <div className="pagination-bar">
      <div className="pagination-info">
        <span className="pagination-records" title={t('Total rows across all pages, after any filters.')}>
          <LoadingValue loading={loading}>
            {t(totalRecords === 1 ? '{{count}} record' : '{{count}} records', {
              count: totalRecords.toLocaleString(),
            })}
          </LoadingValue>
        </span>
        {totalMs !== undefined && !loading && (
          <span className="pagination-timing" title={t('Time the server took to answer this page of results, in milliseconds.')}>{totalMs}ms</span>
        )}
      </div>
      <div className="pagination-controls">
        {tools && <div className="table-tools">{tools}</div>}
        <div className="pagination-pager">
          <button
            type="button"
            className="btn btn-sm"
            disabled={pageNumber <= 1}
            title={t('Show the previous page of results.')}
            onClick={() => { onPageChange(pageNumber - 1); setPageInput(String(pageNumber - 1)); }}
          >
            {t('Prev')}
          </button>
          <div className="pagination-page">
            <input
              type="number"
              className="page-input"
              aria-label={t('Page number')}
              title={t('Type a page number from 1 to {{totalPages}} and press Enter to jump to it.', { totalPages: totalPages.toLocaleString() })}
              value={pageInput}
              onChange={e => setPageInput(e.target.value)}
              onBlur={handlePageInputSubmit}
              onKeyDown={handleKeyDown}
              min={1}
              max={totalPages}
            />
            <span>{t('of {{totalPages}}', { totalPages: totalPages.toLocaleString() })}</span>
          </div>
          <button
            type="button"
            className="btn btn-sm"
            disabled={pageNumber >= totalPages}
            title={t('Show the next page of results.')}
            onClick={() => { onPageChange(pageNumber + 1); setPageInput(String(pageNumber + 1)); }}
          >
            {t('Next')}
          </button>
          <select
            className="page-size-select"
            aria-label={t('Items per page')}
            title={t('Rows shown per page. Your choice is remembered for this table and becomes the default for tables you have not set.')}
            value={pageSize}
            onChange={e => onPageSizeChange(parseInt(e.target.value, 10))}
          >
            {PAGE_SIZES.map(size => (
              <option key={size} value={size}>
                {t('{{size}} / page', { size: size.toLocaleString() })}
              </option>
            ))}
          </select>
        </div>
      </div>
    </div>
  );
}
