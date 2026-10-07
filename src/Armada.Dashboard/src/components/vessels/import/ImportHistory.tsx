import { useCallback, useEffect, useState, type ReactNode } from 'react';
import { enumerateVesselImportBatches } from '../../../api/client';
import type { VesselImportBatch, VesselImportBatchStatus } from '../../../types/models';
import { useLocale } from '../../../context/LocaleContext';
import CodeStatusBadge from '../../shared/CodeStatusBadge';
import DataTable, { type DataTableColumn } from '../../shared/DataTable';
import { EmptyState, ErrorState, LoadingState } from '../../shared/StateBlocks';
import { BATCH_STATUSES, BATCH_STATUS_META, batchStatusBadge, categorizationBadge } from '../../../lib/vesselImportLabels';

interface ImportHistoryProps {
  onOpen: (batch: VesselImportBatch) => void;
}

/** Server-paged import history (newest first) with a status filter. */
export default function ImportHistory({ onOpen }: ImportHistoryProps) {
  const { t, formatDateTime, formatRelativeTime } = useLocale();
  const [rows, setRows] = useState<VesselImportBatch[]>([]);
  const [status, setStatus] = useState<VesselImportBatchStatus | ''>('');
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(10);
  const [totalPages, setTotalPages] = useState(1);
  const [totalRecords, setTotalRecords] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const result = await enumerateVesselImportBatches({ pageNumber, pageSize, status });
      setRows(result.objects || []);
      setTotalPages(Math.max(1, result.totalPages || 1));
      setTotalRecords(result.totalRecords || 0);
      setError('');
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Failed to load import history.'));
    } finally {
      setLoading(false);
    }
  }, [pageNumber, pageSize, status, t]);

  useEffect(() => { void load(); }, [load]);

  const columns: DataTableColumn<VesselImportBatch>[] = [
    {
      key: 'createdUtc', label: t('Created'), cellClassName: 'nowrap',
      cellTitle: (b) => formatDateTime(b.createdUtc), render: (b) => formatRelativeTime(b.createdUtc),
    },
    {
      // The batch ID used to be a second line under the date; it is its own one-line column now.
      key: 'id', label: t('ID'), required: true, cellClassName: 'mono text-dim table-id-cell',
      render: (b) => <span className="cell-one-line" title={b.id}>{b.id}</span>,
    },
    { key: 'status', label: t('Status'), cellClassName: 'cell-nowrap', render: (b) => <CodeStatusBadge {...batchStatusBadge(t, b.status)} /> },
    {
      // Categorization status used to be stacked under the batch status; it is its own column.
      key: 'categorization', label: t('Categorization'), cellClassName: 'cell-nowrap',
      render: (b) => (b.categorizationStatus && b.categorizationStatus !== 'None'
        ? <CodeStatusBadge {...categorizationBadge(t, b.categorizationStatus)} />
        : <span className="text-dim">-</span>),
    },
    { key: 'paths', label: t('Paths'), className: 'text-right', cellClassName: 'mono', render: (b) => b.requestedPathCount.toLocaleString() },
    { key: 'candidates', label: t('Candidates'), className: 'text-right', cellClassName: 'mono', render: (b) => b.candidateCount.toLocaleString() },
    { key: 'createdCount', label: t('Vessels created'), header: t('Created'), className: 'text-right', cellClassName: 'mono', render: (b) => b.createdCount.toLocaleString() },
    { key: 'skipped', label: t('Skipped'), className: 'text-right', cellClassName: 'mono', render: (b) => b.skippedCount.toLocaleString() },
    { key: 'failed', label: t('Failed'), className: 'text-right', cellClassName: 'mono', render: (b) => b.failedCount.toLocaleString() },
    {
      key: 'actions', label: t('Actions'), fixed: true, interactive: true, className: 'text-right',
      render: (b) => (
        <button type="button" className="btn btn-sm" onClick={() => onOpen(b)}>{b.status === 'Discovered' || b.status === 'Discovering' || b.categorizationStatus === 'Completed' ? t('Continue') : t('View')}</button>
      ),
    },
  ];

  let placeholder: ReactNode = undefined;
  if (totalRecords === 0) {
    if (loading && !error) placeholder = <LoadingState />;
    else if (error) placeholder = <></>;
    else {
      placeholder = (
        <EmptyState title={status ? t('No batches match this status') : t('No imports yet')}>
          <p>{t('Each discovery is saved as a batch here, so you can come back to a review or check what an import created.')}</p>
        </EmptyState>
      );
    }
  }

  return (
    <div className="import-history">
      <div className="toolbar-row">
        <label className="inline-filter">
          <span>{t('Status')}</span>
          <select value={status} onChange={(e) => { setStatus(e.target.value as VesselImportBatchStatus | ''); setPageNumber(1); }} aria-label={t('Filter batches by status')}>
            <option value="">{t('All statuses')}</option>
            {BATCH_STATUSES.map((s) => <option key={s} value={s}>{t(BATCH_STATUS_META[s].label)}</option>)}
          </select>
        </label>
      </div>
      {error && <ErrorState message={error} onRetry={() => void load()} />}
      <DataTable
        tableKey="vessel-import-history"
        columns={columns}
        rows={rows}
        rowKey={(b) => b.id}
        onRowClick={onOpen}
        pagination={{
          pageNumber,
          pageSize,
          totalPages,
          totalRecords,
          onPageChange: setPageNumber,
          onPageSizeChange: (size) => { setPageSize(size); setPageNumber(1); },
        }}
        onRefresh={load}
        refreshTitle="Refresh"
        placeholder={placeholder}
      />
    </div>
  );
}
