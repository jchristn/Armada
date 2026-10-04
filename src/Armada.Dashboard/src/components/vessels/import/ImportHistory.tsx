import { useCallback, useEffect, useState } from 'react';
import { enumerateVesselImportBatches } from '../../../api/client';
import type { VesselImportBatch, VesselImportBatchStatus } from '../../../types/models';
import { useLocale } from '../../../context/LocaleContext';
import CodeStatusBadge from '../../shared/CodeStatusBadge';
import Pagination from '../../shared/Pagination';
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
        <button type="button" className="btn btn-sm" onClick={() => void load()}>{t('Refresh')}</button>
      </div>
      {error && <ErrorState message={error} onRetry={() => void load()} />}
      {loading && rows.length === 0 && !error && <LoadingState />}
      {!loading && !error && totalRecords === 0 && (
        <EmptyState title={status ? t('No batches match this status') : t('No imports yet')}>
          <p>{t('Each discovery is saved as a batch here, so you can come back to a review or check what an import created.')}</p>
        </EmptyState>
      )}
      {totalRecords > 0 && (
        <>
          <Pagination pageNumber={pageNumber} pageSize={pageSize} totalPages={totalPages} totalRecords={totalRecords} onPageChange={setPageNumber} onPageSizeChange={(s) => { setPageSize(s); setPageNumber(1); }} />
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th scope="col">{t('Created')}</th>
                  <th scope="col">{t('Status')}</th>
                  <th scope="col" className="text-right">{t('Paths')}</th>
                  <th scope="col" className="text-right">{t('Candidates')}</th>
                  <th scope="col" className="text-right">{t('Created')}</th>
                  <th scope="col" className="text-right">{t('Skipped')}</th>
                  <th scope="col" className="text-right">{t('Failed')}</th>
                  <th scope="col" className="text-right">{t('Actions')}</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((b) => (
                  <tr key={b.id} className="clickable" onClick={() => onOpen(b)}>
                    <td className="nowrap" title={formatDateTime(b.createdUtc)}>
                      {formatRelativeTime(b.createdUtc)}
                      <div className="text-dim mono cell-subline">{b.id}</div>
                    </td>
                    <td>
                      <CodeStatusBadge {...batchStatusBadge(t, b.status)} />
                      {b.categorizationStatus && b.categorizationStatus !== 'None' && (
                        <div className="cell-subline"><CodeStatusBadge {...categorizationBadge(t, b.categorizationStatus)} /></div>
                      )}
                    </td>
                    <td className="text-right mono">{b.requestedPathCount.toLocaleString()}</td>
                    <td className="text-right mono">{b.candidateCount.toLocaleString()}</td>
                    <td className="text-right mono">{b.createdCount.toLocaleString()}</td>
                    <td className="text-right mono">{b.skippedCount.toLocaleString()}</td>
                    <td className="text-right mono">{b.failedCount.toLocaleString()}</td>
                    <td className="text-right" onClick={(e) => e.stopPropagation()}>
                      <button type="button" className="btn btn-sm" onClick={() => onOpen(b)}>{b.status === 'Discovered' || b.status === 'Discovering' || b.categorizationStatus === 'Completed' ? t('Continue') : t('View')}</button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </>
      )}
    </div>
  );
}
