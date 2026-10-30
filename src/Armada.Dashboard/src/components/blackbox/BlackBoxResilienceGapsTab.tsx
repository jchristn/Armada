import { useCallback, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { enumerateBlackBoxResilienceGaps } from '../../api/client';
import type { BlackBoxResilienceGap, BlackBoxResolution } from '../../types/blackbox';
import type { Vessel } from '../../types/models';
import { useLocale } from '../../context/LocaleContext';
import { BLACKBOX_RESOLUTIONS } from '../../lib/blackbox';
import { severityChipClass } from '../../lib/blackboxResilience';
import { blackBoxHref } from '../../lib/navTargets';
import DataTable, { type DataTableColumn } from '../shared/DataTable';
import { EmptyState, ErrorState, LoadingState } from '../shared/StateBlocks';

/** Server page size cap of the resilience gaps. */
const MAX_PAGE_SIZE = 50;

/**
 * Armada resilience gaps tab: failure modes in Armada's own handoff and landing that the black box triaged
 * ArmadaDefect, grouped by fingerprint, with occurrences, severity (escalated by recurrence), whether Armada could have
 * healed them, whether the verdict is only suspected, the top proposed Armada change and the newest entry.
 */
export default function BlackBoxResilienceGapsTab({ vessels }: { vessels: Vessel[] }) {
  const { t, formatDateTime } = useLocale();
  const [vesselId, setVesselId] = useState('');
  const [resolution, setResolution] = useState<BlackBoxResolution | ''>('Open');
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(25);
  const [rows, setRows] = useState<BlackBoxResilienceGap[]>([]);
  const [totalRecords, setTotalRecords] = useState(0);
  const [totalPages, setTotalPages] = useState(1);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const result = await enumerateBlackBoxResilienceGaps({ pageNumber, pageSize: Math.min(pageSize, MAX_PAGE_SIZE), vesselId: vesselId || undefined, resolution: resolution || undefined });
      setRows(result.objects ?? []);
      setTotalRecords(result.totalRecords ?? 0);
      setTotalPages(Math.max(1, result.totalPages ?? 1));
      setError('');
    } catch (err: unknown) {
      setError(t('Resilience gaps could not be loaded: {{message}}', { message: err instanceof Error ? err.message : String(err) }));
    } finally {
      setLoading(false);
    }
  }, [pageNumber, pageSize, vesselId, resolution, t]);

  useEffect(() => { void load(); }, [load]);

  const columns: DataTableColumn<BlackBoxResilienceGap>[] = [
    { key: 'severity', label: t('Severity'), required: true, render: (r) => <span className={severityChipClass(r.severity)}>{t(r.severity)}</span> },
    { key: 'where', label: t('Phase / step'), render: (r) => `${t(r.phase)} / ${t(r.step)}` },
    { key: 'change', label: t('Top proposed Armada change'), render: (r) => r.topProposedChange ? <span title={r.topProposedChange.change}>{r.topProposedChange.title}</span> : <span className="text-dim">-</span> },
    { key: 'selfHealable', label: t('Self-healable'), render: (r) => t(r.selfHealable) },
    { key: 'suspected', label: t('Verdict'), render: (r) => r.suspected ? <span className="blackbox-chip" title={t('Only the handoff and landing prior voted ArmadaDefect.')}>{t('Suspected')}</span> : <span className="blackbox-chip blackbox-chip-warning">{t('ArmadaDefect')}</span> },
    { key: 'occurrences', label: t('Occurrences'), render: (r) => r.occurrences },
    { key: 'open', label: t('Open'), render: (r) => r.openCount },
    { key: 'versions', label: t('Armada versions'), render: (r) => (r.armadaVersions ?? []).join(', ') || '-' },
    { key: 'gap', label: t('Detection gap'), render: (r) => r.detectionGap ? <span className="text-dim" title={r.detectionGap}>{r.detectionGap.length > 90 ? `${r.detectionGap.slice(0, 87)}...` : r.detectionGap}</span> : '-' },
    { key: 'lastSeen', label: t('Last seen'), render: (r) => formatDateTime(r.lastSeenUtc) },
    { key: 'fingerprint', label: t('Fingerprint'), render: (r) => <Link to={blackBoxHref('entries', { fingerprint: r.fingerprint })} className="mono" title={r.fingerprint}>{r.fingerprint.slice(0, 16)}</Link>, interactive: true },
    { key: 'latest', label: t('Latest entry'), render: (r) => r.latestEntryId ? <Link to={`/blackbox/${encodeURIComponent(r.latestEntryId)}`} className="mono" title={r.latestMissionTitle ?? undefined}>{r.latestEntryId}</Link> : '-', interactive: true },
  ];

  return (
    <div data-testid="blackbox-resilience-gaps">
      <p className="text-dim" style={{ margin: '0 0 0.5rem' }}>{t('Failures in Armada\'s own handoff and landing that the black box triaged ArmadaDefect, grouped by failure mode, most severe first. Each one is an Armada change waiting to be made.')}</p>
      <div className="blackbox-filters">
        <label>{t('Vessel')}
          <select value={vesselId} onChange={(e) => { setVesselId(e.target.value); setPageNumber(1); }} aria-label={t('Vessel')}>
            <option value="">{t('Any')}</option>
            {vessels.map((v) => <option key={v.id} value={v.id}>{v.name}</option>)}
          </select>
        </label>
        <label>{t('Resolution')}
          <select value={resolution} onChange={(e) => { setResolution(e.target.value as BlackBoxResolution | ''); setPageNumber(1); }} aria-label={t('Resolution')}>
            <option value="">{t('Any')}</option>
            {BLACKBOX_RESOLUTIONS.map((r) => <option key={r} value={r}>{t(r)}</option>)}
          </select>
        </label>
      </div>
      {error ? <ErrorState message={error} onRetry={() => void load()} /> : (
        <DataTable
          tableKey="blackbox-resilience-gaps"
          columns={columns}
          rows={rows}
          rowKey={(r) => r.fingerprint}
          placeholder={loading && rows.length === 0 ? <LoadingState /> : rows.length === 0 ? <EmptyState title={t('No Armada resilience gaps')}>{t('Handoff and landing failures triaged ArmadaDefect appear here, grouped by failure mode.')}</EmptyState> : undefined}
          pagination={{ pageNumber, pageSize, totalPages, totalRecords, onPageChange: setPageNumber, onPageSizeChange: (s) => { setPageSize(Math.min(s, MAX_PAGE_SIZE)); setPageNumber(1); } }}
          onRefresh={load}
          refreshTitle="Reload the resilience gaps from the server now."
          ariaLabel={t('Armada resilience gaps')}
        />
      )}
    </div>
  );
}
