import { useCallback, useEffect, useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { enumerateBlackBoxHotspots } from '../../api/client';
import type { BlackBoxFileRole, BlackBoxHotspot } from '../../types/blackbox';
import type { Vessel } from '../../types/models';
import { useLocale } from '../../context/LocaleContext';
import DataTable, { type DataTableColumn } from '../shared/DataTable';
import { EmptyState, ErrorState, LoadingState } from '../shared/StateBlocks';

const FILE_ROLES: BlackBoxFileRole[] = ['Conflicted', 'DirtyInCheckout', 'OutOfScope', 'ChangedByMission', 'ChangedByConflictingMission', 'BoundaryViolation'];

/** Hotspots tab: per-vessel directories that recur in failures (conflicts, dirty checkouts, scope violations). */
export default function BlackBoxHotspotsTab({ vessels }: { vessels: Vessel[] }) {
  const { t, formatDateTime } = useLocale();
  const [vesselId, setVesselId] = useState('');
  const [role, setRole] = useState<BlackBoxFileRole | ''>('');
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(25);
  const [rows, setRows] = useState<BlackBoxHotspot[]>([]);
  const [totalRecords, setTotalRecords] = useState(0);
  const [totalPages, setTotalPages] = useState(1);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const vesselName = useMemo(() => new Map(vessels.map((v) => [v.id, v.name])), [vessels]);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const result = await enumerateBlackBoxHotspots({ pageNumber, pageSize, vesselId: vesselId || undefined, role: role || undefined });
      setRows(result.objects ?? []);
      setTotalRecords(result.totalRecords ?? 0);
      setTotalPages(Math.max(1, result.totalPages ?? 1));
      setError('');
    } catch (err: unknown) {
      setError(t('Hotspots could not be loaded: {{message}}', { message: err instanceof Error ? err.message : String(err) }));
    } finally {
      setLoading(false);
    }
  }, [pageNumber, pageSize, vesselId, role, t]);

  useEffect(() => { void load(); }, [load]);

  const columns: DataTableColumn<BlackBoxHotspot>[] = [
    { key: 'vessel', label: t('Vessel'), required: true, render: (r) => r.vesselId ? <Link to={`/vessels/${r.vesselId}`}>{vesselName.get(r.vesselId) ?? r.vesselId}</Link> : '-', interactive: true },
    { key: 'directory', label: t('Directory'), required: true, render: (r) => <Link to={`/blackbox?tab=entries&path=${encodeURIComponent(r.directory)}${r.vesselId ? `&vesselId=${encodeURIComponent(r.vesselId)}` : ''}`} className="mono" title={t('List the entries that touched this directory.')}>{r.directory}</Link>, interactive: true },
    { key: 'entries', label: t('Entries'), render: (r) => r.entryCount },
    { key: 'files', label: t('Files'), render: (r) => r.fileCount },
    { key: 'lastSeen', label: t('Last seen'), render: (r) => r.lastSeenUtc ? formatDateTime(r.lastSeenUtc) : '-' },
  ];

  return (
    <div>
      <div className="blackbox-filters">
        <label>{t('Vessel')}
          <select value={vesselId} onChange={(e) => { setVesselId(e.target.value); setPageNumber(1); }} aria-label={t('Vessel')}>
            <option value="">{t('Any')}</option>
            {vessels.map((v) => <option key={v.id} value={v.id}>{v.name}</option>)}
          </select>
        </label>
        <label>{t('File role')}
          <select value={role} onChange={(e) => { setRole(e.target.value as BlackBoxFileRole | ''); setPageNumber(1); }} aria-label={t('File role')}>
            <option value="">{t('Any')}</option>
            {FILE_ROLES.map((r) => <option key={r} value={r}>{t(r)}</option>)}
          </select>
        </label>
      </div>
      {error ? <ErrorState message={error} onRetry={() => void load()} /> : (
        <DataTable
          tableKey="blackbox-hotspots"
          columns={columns}
          rows={rows}
          rowKey={(r) => `${r.vesselId ?? ''}|${r.directory}`}
          placeholder={loading && rows.length === 0 ? <LoadingState /> : rows.length === 0 ? <EmptyState title={t('No hotspots yet')}>{t('Directories show up here when failures keep touching them.')}</EmptyState> : undefined}
          pagination={{ pageNumber, pageSize, totalPages, totalRecords, onPageChange: setPageNumber, onPageSizeChange: (s) => { setPageSize(s); setPageNumber(1); } }}
          onRefresh={load}
          refreshTitle="Reload the hotspots from the server now."
          ariaLabel={t('Hotspots')}
        />
      )}
    </div>
  );
}
