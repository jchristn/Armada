import { Link } from 'react-router-dom';
import { listVesselReservations } from '../../api/client';
import type { VesselReservation, VesselReservationPage } from '../../types/blackbox';
import { useLocale } from '../../context/LocaleContext';
import { usePagedInsight } from '../../lib/usePagedInsight';
import DataTable, { type DataTableColumn } from '../shared/DataTable';
import { EmptyState, ErrorState, LoadingState } from '../shared/StateBlocks';
import './BlackBox.css';

/**
 * Reservations card on the vessel page: the soft directory reservations of the vessel's active missions (a mission
 * working in a directory the black box marks as a conflict hotspot reserves it; a second mission there is warned at
 * dispatch, or chained when the vessel serializes overlapping missions).
 */
export default function VesselReservationsCard({ vesselId, serializeOverlappingMissions }: { vesselId: string; serializeOverlappingMissions?: boolean }) {
  const { t, formatDateTime } = useLocale();
  const paged = usePagedInsight<VesselReservation, VesselReservationPage>(
    vesselId,
    (pageNumber, pageSize) => listVesselReservations(vesselId, { pageNumber, pageSize }),
    10,
  );
  const page = paged.result;

  const columns: DataTableColumn<VesselReservation>[] = [
    { key: 'directory', label: t('Directory'), required: true, cellClassName: 'mono', render: (r) => r.directory },
    { key: 'mission', label: t('Mission'), required: true, interactive: true, render: (r) => <Link to={`/missions/${encodeURIComponent(r.missionId)}`} title={t('Open the reserving mission.')}>{r.missionTitle || r.missionId}</Link> },
    { key: 'status', label: t('Status'), render: (r) => r.missionStatus ? t(r.missionStatus) : '-' },
    { key: 'entries', label: t('Black box entries'), headerTitle: t('Black box entries that recorded a conflict in this directory.'), render: (r) => r.hotspotEntryCount },
    { key: 'files', label: t('Files'), render: (r) => r.fileCount },
    { key: 'since', label: t('Since'), cellClassName: 'cell-nowrap', render: (r) => formatDateTime(r.sinceUtc) },
  ];

  return (
    <div className="card" style={{ marginTop: '1rem' }} data-testid="vessel-reservations">
      <h3 title={t('Directories active missions reserve because failures keep conflicting there. A second mission touching one is warned at dispatch, or chained when the vessel serializes overlapping missions.')}>{t('Reservations')}</h3>
      <p className="text-dim" style={{ margin: '0 0 0.5rem', fontSize: '0.85rem' }}>
        {page && page.threshold === 0
          ? t('Reservations are off (blackBox.hotspotReservationThreshold is 0).')
          : page ? t('A hotspot directory with at least {{threshold}} black box entries is reserved by the active missions working in it.', { threshold: page.threshold }) : null}
        {' '}
        <span title={t('When on, dispatch chains a mission that overlaps an active one after it. Change it with Edit.')}>
          {serializeOverlappingMissions ? t('Overlapping missions are chained.') : t('Overlapping missions are warned only.')}
        </span>
      </p>
      {paged.error ? <ErrorState message={t('Reservations could not be loaded: {{message}}', { message: paged.error })} onRetry={() => void paged.reload()} /> : (
        <DataTable
          tableKey="vessel-reservations"
          columns={columns}
          rows={paged.rows}
          rowKey={(r) => `${r.directory}|${r.missionId}`}
          placeholder={paged.loading && paged.rows.length === 0 ? <LoadingState /> : paged.rows.length === 0 ? <EmptyState title={t('No reservations')}>{t('No active mission is working in a conflict hotspot directory on this vessel.')}</EmptyState> : undefined}
          pagination={{ pageNumber: paged.pageNumber, pageSize: paged.pageSize, totalPages: paged.totalPages, totalRecords: paged.totalRecords, onPageChange: paged.setPageNumber, onPageSizeChange: paged.setPageSize }}
          onRefresh={paged.reload}
          refreshTitle="Reload the reservations from the server now."
          ariaLabel={t('Reservations')}
        />
      )}
      {page?.scanTruncated && <p className="text-dim" style={{ fontSize: '0.8rem', margin: '0.3rem 0 0' }}>{t('Only the newest {{count}} active missions were scanned.', { count: page.activeMissionsScanned })}</p>}
    </div>
  );
}
