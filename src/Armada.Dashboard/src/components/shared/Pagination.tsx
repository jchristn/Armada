import { useState } from 'react';

interface PaginationProps {
  pageNumber: number;
  pageSize: number;
  totalPages: number;
  totalRecords: number;
  totalMs?: number;
  onPageChange: (page: number) => void;
  onPageSizeChange: (size: number) => void;
}

const PAGE_SIZES = [10, 25, 50, 100, 250];

export default function Pagination({
  pageNumber,
  pageSize,
  totalPages,
  totalRecords,
  totalMs,
  onPageChange,
  onPageSizeChange,
}: PaginationProps) {
  const [pageInput, setPageInput] = useState(String(pageNumber));

  const handlePageInputChange = (val: string) => {
    setPageInput(val);
  };

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

  // Sync the input when pageNumber changes externally
  if (String(pageNumber) !== pageInput && document.activeElement?.classList.contains('page-input') === false) {
    // Will update on next render cycle
  }

  return (
    <div className="pagination-bar">
      <div className="pagination-info">
        <span className="pagination-records">{totalRecords} record{totalRecords !== 1 ? 's' : ''}</span>
        {totalMs !== undefined && (
          <span className="pagination-timing">{totalMs}ms</span>
        )}
      </div>
      <div className="pagination-controls">
        <button
          className="btn btn-sm"
          disabled={pageNumber <= 1}
          onClick={() => { onPageChange(pageNumber - 1); setPageInput(String(pageNumber - 1)); }}
        >
          Prev
        </button>
        <div className="pagination-page">
          <input
            type="number"
            className="page-input"
            value={pageInput}
            onChange={e => handlePageInputChange(e.target.value)}
            onBlur={handlePageInputSubmit}
            onKeyDown={handleKeyDown}
            min={1}
            max={totalPages}
          />
          <span>of {totalPages}</span>
        </div>
        <button
          className="btn btn-sm"
          disabled={pageNumber >= totalPages}
          onClick={() => { onPageChange(pageNumber + 1); setPageInput(String(pageNumber + 1)); }}
        >
          Next
        </button>
        <select
          className="page-size-select"
          value={pageSize}
          onChange={e => onPageSizeChange(parseInt(e.target.value, 10))}
        >
          {PAGE_SIZES.map(size => (
            <option key={size} value={size}>{size} / page</option>
          ))}
        </select>
      </div>
    </div>
  );
}
