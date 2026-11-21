import { useCallback, useEffect, useState, type ReactNode } from 'react';
import { enumerateVesselImportBatches } from '../../../api/client';
import type { VesselImportBatch, VesselImportBatchStatus } from '../../../types/models';
import { useLocale } from '../../../context/LocaleContext';
import CodeStatusBadge from '../../shared/CodeStatusBadge';
import DataTable, { type DataTableColumn } from '../../shared/DataTable';
import { EmptyState, ErrorState, LoadingState } from '../../shared/StateBlocks';
import { BATCH_STATUSES, BATCH_STATUS_META, batchStatusBadge, categorizationBadge } from '../../../lib/vesselImportLabels';
import { usePersistedPageSize } from '../../../lib/usePersistedPageSize';

interface ImportHistoryProps {
  onOpen: (batch: VesselImportBatch) => void;
}

/** Server-paged import history (newest first) with a status filter. */
export default function ImportHistory({ onOpen }: ImportHistoryProps) {
  const { t, formatDateTime, formatRelativeTime } = useLocale();
  const [rows, setRows] = useState<VesselImportBatch[]>([]);
  const [status, setStatus] = useState<VesselImportBatchStatus | ''>('');
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = usePersistedPageSize('vessel-import-history', 10);
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
      // The batch ID used to be a second line under the date; it is its own one-line column now.
      key: 'id', label: t('ID'), headerTitle: t('Import batch identifier.'), required: true, cellClassName: 'mono text-dim table-id-cell',
      render: (b) => <span className="cell-one-line" title={b.id}>{b.id}</span>,
    },
    {
      key: 'createdUtc', label: t('Created'), headerTitle: t('When the discovery for this batch was started. Hover a cell for the exact time.'), cellClassName: 'nowrap',
      cellTitle: (b) => formatDateTime(b.createdUtc), render: (b) => formatRelativeTime(b.createdUtc),
    },
    { key: 'status', label: t('Status'), headerTitle: t('Where the batch is: discovering, waiting for review, importing, or finished. Hover a badge for details.'), cellClassName: 'cell-nowrap', render: (b) => <CodeStatusBadge {...batchStatusBadge(t, b.status)} /> },
    {
      // Categorization status used to be stacked under the batch status; it is its own column.
      key: 'categorization', label: t('Categorization'), headerTitle: t('State of the optional captain fleet recommendations for this batch. A dash means none were requested.'), cellClassName: 'cell-nowrap',
      render: (b) => (b.categorizationStatus && b.categorizationStatus !== 'None'
        ? <CodeStatusBadge {...categorizationBadge(t, b.categorizationStatus)} />
        : <span className="text-dim">-</span>),
    },
    { key: 'paths', label: t('Paths'), headerTitle: t('Folders you asked Armada to search.'), className: 'text-right', cellClassName: 'mono', render: (b) => b.requestedPathCount.toLocaleString() },
    { key: 'candidates', label: t('Candidates'), headerTitle: t('Repositories found under those folders.'), className: 'text-right', cellClassName: 'mono', render: (b) => b.candidateCount.toLocaleString() },
    { key: 'createdCount', label: t('Vessels created'), header: t('Created'), headerTitle: t('Vessels this import created.'), className: 'text-right', cellClassName: 'mono', render: (b) => b.createdCount.toLocaleString() },
    { key: 'skipped', label: t('Skipped'), headerTitle: t('Candidates not imported because they were not selected or a matching vessel already existed.'), className: 'text-right', cellClassName: 'mono', render: (b) => b.skippedCount.toLocaleString() },
    { key: 'failed', label: t('Failed'), headerTitle: t('Candidates whose vessel could not be created. Open the batch to see why.'), className: 'text-right', cellClassName: 'mono', render: (b) => b.failedCount.toLocaleString() },
    {
      key: 'actions', label: t('Actions'), headerTitle: t('Open a batch to continue its review or see what it created.'), fixed: true, interactive: true, className: 'text-right',
      render: (b) => (
        <button type="button" className="btn btn-sm" onClick={() => onOpen(b)} title={b.status === 'Discovered' || b.status === 'Discovering' || b.categorizationStatus === 'Completed' ? t('Reopen this batch where you left off: review and import its candidates, or review its fleet recommendations.') : t('Open this batch to see each candidate and what happened to it.')}>{b.status === 'Discovered' || b.status === 'Discovering' || b.categorizationStatus === 'Completed' ? t('Continue') : t('View')}</button>
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
        <label className="inline-filter" title={t('Show only import batches in this state, such as Discovered (waiting for your review) or Completed with failures.')}>
          <span>{t('Status')}</span>
          <select title={t('Show only import batches in this state, such as Discovered (waiting for your review) or Completed with failures.')} value={status} onChange={(e) => { setStatus(e.target.value as VesselImportBatchStatus | ''); setPageNumber(1); }} aria-label={t('Filter batches by status')}>
            <option value="">{t('All statuses')}</option>
            {BATCH_STATUSES.map((s) => <option key={s} value={s}>{t(BATCH_STATUS_META[s].label)}</option>)}
          </select>
        </label>
      </div>
      {error && <ErrorState message={error} onRetry={() => void load()} />}
      <DataTable
        loading={loading && !error}
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
        refreshTitle={t('Reload the import history.')}
        placeholder={placeholder}
      />
    </div>
  );
}
