import { Link } from 'react-router-dom';
import { listBlackBoxNearbySuccesses } from '../../api/client';
import type { BlackBoxNearbySuccess } from '../../types/blackbox';
import type { EnumerationResult } from '../../types/models';
import { useLocale } from '../../context/LocaleContext';
import { usePagedInsight } from '../../lib/usePagedInsight';
import DataTable, { type DataTableColumn } from '../shared/DataTable';
import { EmptyState, ErrorState, LoadingState } from '../shared/StateBlocks';

/** Hours before (negative) or after the failure, as a short label. */
export function formatHoursFromFailure(hours: number, t: (key: string, values?: Record<string, string | number>) => string): string {
  const value = Math.abs(hours) < 1 ? Math.round(Math.abs(hours) * 60) : Math.round(Math.abs(hours) * 10) / 10;
  const unit = Math.abs(hours) < 1 ? 'minutes' : 'hours';
  if (hours < 0) return unit === 'minutes' ? t('{{count}} minutes before', { count: value }) : t('{{count}} hours before', { count: value });
  return unit === 'minutes' ? t('{{count}} minutes after', { count: value }) : t('{{count}} hours after', { count: value });
}

/**
 * Nearby successes: the successful landings on the entry's vessel around the failure that changed files the failure
 * also touched, most overlap first. Each links to the mission that landed.
 */
export default function BlackBoxNearbySuccesses({ entryId }: { entryId: string }) {
  const { t, formatDateTime } = useLocale();
  const paged = usePagedInsight<BlackBoxNearbySuccess, EnumerationResult<BlackBoxNearbySuccess>>(
    entryId,
    (pageNumber, pageSize) => listBlackBoxNearbySuccesses(entryId, { pageNumber, pageSize }),
    10,
  );

  const columns: DataTableColumn<BlackBoxNearbySuccess>[] = [
    { key: 'mission', label: t('Mission'), required: true, interactive: true, render: (r) => <Link to={`/missions/${encodeURIComponent(r.missionId)}`} title={t('Open the mission that landed.')}>{r.missionTitle || r.missionId}</Link> },
    { key: 'landed', label: t('Landed'), cellClassName: 'cell-nowrap', render: (r) => <span title={formatDateTime(r.landedUtc)}>{formatHoursFromFailure(r.hoursFromFailure, t)}</span> },
    { key: 'files', label: t('Shared files'), cellClassName: 'blackbox-wrap-cell', render: (r) => <span className="mono" title={r.overlappingFiles.join('\n')}>{r.overlappingFileCount}{r.overlappingFiles.length ? ` · ${r.overlappingFiles.slice(0, 3).join(', ')}${r.overlappingFileCount > 3 ? ', ...' : ''}` : ''}</span> },
    { key: 'commit', label: t('Landed commit'), cellClassName: 'mono text-dim', render: (r) => r.targetCommit ? r.targetCommit.slice(0, 10) : '-' },
  ];

  return (
    <div data-testid="blackbox-nearby-successes">
      {paged.error ? <ErrorState message={t('Nearby successes could not be loaded: {{message}}', { message: paged.error })} onRetry={() => void paged.reload()} /> : (
        <DataTable
          tableKey="blackbox-nearby-successes"
          columns={columns}
          rows={paged.rows}
          rowKey={(r) => r.successId}
          placeholder={paged.loading && paged.rows.length === 0 ? <LoadingState /> : paged.rows.length === 0 ? <EmptyState title={t('No nearby successes')}>{t('No mission landed on this vessel near the failure with the same files.')}</EmptyState> : undefined}
          pagination={{ pageNumber: paged.pageNumber, pageSize: paged.pageSize, totalPages: paged.totalPages, totalRecords: paged.totalRecords, onPageChange: paged.setPageNumber, onPageSizeChange: paged.setPageSize }}
          onRefresh={paged.reload}
          refreshTitle="Reload the nearby successes from the server now."
          ariaLabel={t('Nearby successes')}
        />
      )}
    </div>
  );
}
