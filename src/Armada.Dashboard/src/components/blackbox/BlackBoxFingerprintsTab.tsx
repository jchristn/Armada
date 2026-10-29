import { useCallback, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { enumerateBlackBoxFingerprints, getBlackBoxFingerprintTrend } from '../../api/client';
import type { BlackBoxFingerprintSummary, BlackBoxFingerprintTrend, BlackBoxResolution } from '../../types/blackbox';
import type { Vessel } from '../../types/models';
import { useLocale } from '../../context/LocaleContext';
import { BLACKBOX_RESOLUTIONS } from '../../lib/blackbox';
import DataTable, { type DataTableColumn } from '../shared/DataTable';
import { EmptyState, ErrorState, LoadingState } from '../shared/StateBlocks';

const SPARK_WIDTH = 96;
const SPARK_HEIGHT = 22;

/** Occurrences of one failure mode per Armada version, oldest version first, as a small line. */
export function FingerprintSparkline({ fingerprint }: { fingerprint: string }) {
  const { t } = useLocale();
  const [trend, setTrend] = useState<BlackBoxFingerprintTrend | null>(null);
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    let cancelled = false;
    getBlackBoxFingerprintTrend(fingerprint)
      .then((r) => { if (!cancelled) setTrend(r); })
      .catch(() => { if (!cancelled) setFailed(true); });
    return () => { cancelled = true; };
  }, [fingerprint]);

  if (failed) return <span className="text-dim">-</span>;
  if (!trend) return <span className="text-dim">...</span>;
  const versions = [...(trend.versions ?? [])].sort((a, b) => a.firstSeenUtc.localeCompare(b.firstSeenUtc));
  if (versions.length === 0) return <span className="text-dim">-</span>;
  const max = Math.max(1, ...versions.map((v) => v.count));
  const step = versions.length > 1 ? SPARK_WIDTH / (versions.length - 1) : 0;
  const points = versions.map((v, i) => `${(versions.length > 1 ? i * step : SPARK_WIDTH / 2).toFixed(1)},${(SPARK_HEIGHT - 2 - (v.count / max) * (SPARK_HEIGHT - 4)).toFixed(1)}`).join(' ');
  const label = versions.map((v) => t('{{version}}: {{count}} ({{open}} open)', { version: v.armadaVersion, count: v.count, open: v.openCount })).join('; ');
  return (
    <svg className="blackbox-sparkline" width={SPARK_WIDTH} height={SPARK_HEIGHT} viewBox={`0 0 ${SPARK_WIDTH} ${SPARK_HEIGHT}`} role="img" aria-label={label} data-testid={`sparkline-${fingerprint}`}>
      <title>{label}</title>
      {versions.length > 1
        ? <polyline points={points} fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinejoin="round" />
        : <circle cx={SPARK_WIDTH / 2} cy={SPARK_HEIGHT / 2} r="2.5" fill="currentColor" />}
    </svg>
  );
}

/**
 * Fingerprints tab: failure modes (entries grouped by fingerprint) with counts, open counts, first and last seen,
 * and a trend line of occurrences per Armada version (to see whether a release fixed it). Each links to its entries.
 */
export default function BlackBoxFingerprintsTab({ vessels }: { vessels: Vessel[] }) {
  const { t, formatDateTime } = useLocale();
  const [vesselId, setVesselId] = useState('');
  const [resolution, setResolution] = useState<BlackBoxResolution | ''>('');
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(25);
  const [rows, setRows] = useState<BlackBoxFingerprintSummary[]>([]);
  const [totalRecords, setTotalRecords] = useState(0);
  const [totalPages, setTotalPages] = useState(1);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const result = await enumerateBlackBoxFingerprints({ pageNumber, pageSize, vesselId: vesselId || undefined, resolution: resolution || undefined });
      setRows(result.objects ?? []);
      setTotalRecords(result.totalRecords ?? 0);
      setTotalPages(Math.max(1, result.totalPages ?? 1));
      setError('');
    } catch (err: unknown) {
      setError(t('Fingerprints could not be loaded: {{message}}', { message: err instanceof Error ? err.message : String(err) }));
    } finally {
      setLoading(false);
    }
  }, [pageNumber, pageSize, vesselId, resolution, t]);

  useEffect(() => { void load(); }, [load]);

  const columns: DataTableColumn<BlackBoxFingerprintSummary>[] = [
    { key: 'fingerprint', label: t('Fingerprint'), required: true, render: (r) => <Link to={`/blackbox?tab=entries&fingerprint=${encodeURIComponent(r.fingerprint)}`} className="mono" title={r.fingerprint}>{r.fingerprint.slice(0, 16)}</Link>, interactive: true },
    { key: 'where', label: t('Phase / step'), render: (r) => `${t(r.phase)} / ${t(r.step)}` },
    { key: 'kind', label: t('Kind'), render: (r) => r.failureKind ? t(r.failureKind) : '-' },
    { key: 'count', label: t('Entries'), render: (r) => r.count },
    { key: 'open', label: t('Open'), render: (r) => r.openCount },
    { key: 'trend', label: t('Trend by version'), render: (r) => <FingerprintSparkline fingerprint={r.fingerprint} /> },
    { key: 'firstSeen', label: t('First seen'), render: (r) => formatDateTime(r.firstSeenUtc) },
    { key: 'lastSeen', label: t('Last seen'), render: (r) => formatDateTime(r.lastSeenUtc) },
    { key: 'latest', label: t('Latest entry'), render: (r) => r.latestEntryId ? <Link to={`/blackbox/${encodeURIComponent(r.latestEntryId)}`} className="mono">{r.latestEntryId}</Link> : '-', interactive: true },
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
        <label>{t('Resolution')}
          <select value={resolution} onChange={(e) => { setResolution(e.target.value as BlackBoxResolution | ''); setPageNumber(1); }} aria-label={t('Resolution')}>
            <option value="">{t('Any')}</option>
            {BLACKBOX_RESOLUTIONS.map((r) => <option key={r} value={r}>{t(r)}</option>)}
          </select>
        </label>
      </div>
      {error ? <ErrorState message={error} onRetry={() => void load()} /> : (
        <DataTable
          tableKey="blackbox-fingerprints"
          columns={columns}
          rows={rows}
          rowKey={(r) => r.fingerprint}
          placeholder={loading && rows.length === 0 ? <LoadingState /> : rows.length === 0 ? <EmptyState title={t('No failure modes yet')}>{t('Fingerprints group entries that failed the same way.')}</EmptyState> : undefined}
          pagination={{ pageNumber, pageSize, totalPages, totalRecords, onPageChange: setPageNumber, onPageSizeChange: (s) => { setPageSize(s); setPageNumber(1); } }}
          onRefresh={load}
          refreshTitle="Reload the failure modes from the server now."
          ariaLabel={t('Fingerprints')}
        />
      )}
    </div>
  );
}
