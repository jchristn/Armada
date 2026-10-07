import { useState, type ReactNode } from 'react';
import { useLocale } from '../../context/LocaleContext';

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
}

const PAGE_SIZES = [10, 25, 50, 100, 250];

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
        <span className="pagination-records">
          {t(totalRecords === 1 ? '{{count}} record' : '{{count}} records', {
            count: totalRecords.toLocaleString(),
          })}
        </span>
        {totalMs !== undefined && (
          <span className="pagination-timing">{totalMs}ms</span>
        )}
      </div>
      <div className="pagination-controls">
        {tools && <div className="table-tools">{tools}</div>}
        <div className="pagination-pager">
          <button
            type="button"
            className="btn btn-sm"
            disabled={pageNumber <= 1}
            onClick={() => { onPageChange(pageNumber - 1); setPageInput(String(pageNumber - 1)); }}
          >
            {t('Prev')}
          </button>
          <div className="pagination-page">
            <input
              type="number"
              className="page-input"
              aria-label={t('Page number')}
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
            onClick={() => { onPageChange(pageNumber + 1); setPageInput(String(pageNumber + 1)); }}
          >
            {t('Next')}
          </button>
          <select
            className="page-size-select"
            aria-label={t('Items per page')}
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
