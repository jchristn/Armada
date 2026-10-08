import { useCallback, useEffect, useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { getHarborMetrics } from '../../api/client';
import type { HarborLaunchSpeed, HarborMetrics, HarborMetricsRange } from '../../types/models';
import { useLocale } from '../../context/LocaleContext';
import DataTable, { type DataTableColumn } from '../shared/DataTable';
import { formatTokens } from '../../lib/tokenUsage';
import {
  HARBOR_METRICS_RANGES,
  JOB_SERIES,
  LINK_STATE_COLORS,
  LINK_STATE_LABELS,
  bucketTimes,
  formatDurationMs,
  hasAnyData,
  linkStrip,
  rangeHours,
  sparklineRuns,
  tokenRows,
  tokenSeries,
  tokenUsageLink,
} from '../../lib/harborMetrics';
import HarborBucketChart from './HarborBucketChart';

interface HarborMetricsPanelProps {
  harborId: string;
  /** Initial range; the selector changes it from there. */
  initialRange?: HarborMetricsRange;
}

const LINK_STATES = ['Connected', 'Reconnecting', 'Down', 'Unknown'] as const;

function Sparkline({ values, label }: { values: (number | null)[]; label: string }) {
  const runs = sparklineRuns(values, 96, 18);
  if (runs.length === 0) return <span className="text-dim">-</span>;
  return (
    <svg className="harbor-metrics-sparkline" width="96" height="22" viewBox="-2 -2 100 22" role="img" aria-label={label}>
      {runs.map((points, i) => (
        points.includes(' ')
          ? <polyline key={i} points={points} fill="none" stroke="var(--accent)" strokeWidth={1.4} strokeLinejoin="round" strokeLinecap="round" />
          : <circle key={i} cx={Number(points.split(',')[0])} cy={Number(points.split(',')[1])} r={1.5} fill="var(--accent)" />
      ))}
    </svg>
  );
}

/**
 * Charts for one Harbor over 1h, 24h, or 7d: jobs over time, slot usage against capacity, link health, launch speed
 * per runtime, and token usage. Every number comes from the Admiral (GET /api/v1/harbors/{id}/metrics).
 */
export default function HarborMetricsPanel({ harborId, initialRange = '24h' }: HarborMetricsPanelProps) {
  const { t, formatDateTime } = useLocale();
  const [range, setRange] = useState<HarborMetricsRange>(initialRange);
  const [metrics, setMetrics] = useState<HarborMetrics | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [reloadKey, setReloadKey] = useState(0);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    setError('');
    Promise.resolve()
      .then(() => getHarborMetrics(harborId, range))
      .then((result) => {
        if (cancelled) return;
        if (!result) {
          setMetrics(null);
          setError(t('Failed to load Harbor metrics.'));
          return;
        }
        setMetrics(result);
      })
      .catch((err: unknown) => {
        if (cancelled) return;
        setMetrics(null);
        setError(err instanceof Error && err.message ? err.message : t('Failed to load Harbor metrics.'));
      })
      .finally(() => { if (!cancelled) setLoading(false); });
    return () => { cancelled = true; };
  }, [harborId, range, reloadKey, t]);

  const refresh = useCallback(() => setReloadKey((k) => k + 1), []);

  const times = useMemo(() => (metrics ? bucketTimes(metrics) : []), [metrics]);
  const hours = rangeHours(range);
  const stepMinutes = metrics?.bucketMinutes ?? 30;

  const jobSeries = JOB_SERIES.map((s) => ({ key: s.key, label: t(s.label), color: s.color }));
  const jobRows = (metrics?.jobs.buckets ?? []).map((b) => JOB_SERIES.map((s) => b[s.key]));

  const slotRows = (metrics?.slots.buckets ?? []).map((b) => [b.peak, b.average]);
  const rttRows = (metrics?.link.roundTrip ?? []).map((b) => [b.averageMs, b.maxMs]);
  const tokenDefs = metrics ? tokenSeries(metrics) : [];
  const tokenValueRows = metrics ? tokenRows(metrics.tokens.buckets, tokenDefs) : [];
  const strip = metrics ? linkStrip(metrics.link.segments, metrics.fromUtc, metrics.toUtc) : [];

  const speedColumns: DataTableColumn<HarborLaunchSpeed>[] = [
    { key: 'runtime', label: t('Runtime'), required: true, cellClassName: 'cell-nowrap', render: (row) => <strong>{row.runtime}</strong> },
    { key: 'jobs', label: t('Jobs'), cellClassName: 'text-dim', render: (row) => row.jobCount },
    { key: 'firstMedian', label: t('First output (median)'), cellClassName: 'cell-nowrap', render: (row) => formatDurationMs(row.firstOutputMedianMs) },
    { key: 'firstP95', label: t('First output (p95)'), cellClassName: 'cell-nowrap', render: (row) => formatDurationMs(row.firstOutputP95Ms) },
    { key: 'durationMedian', label: t('Runtime (median)'), cellClassName: 'cell-nowrap', render: (row) => formatDurationMs(row.durationMedianMs) },
    { key: 'durationP95', label: t('Runtime (p95)'), cellClassName: 'cell-nowrap', render: (row) => formatDurationMs(row.durationP95Ms) },
    {
      key: 'trend', label: t('First output trend'), cellClassName: 'cell-nowrap',
      render: (row) => <Sparkline values={row.firstOutputMedianMsByBucket} label={t('First output trend for {{runtime}}', { runtime: row.runtime })} />,
    },
  ];

  const empty = metrics !== null && !hasAnyData(metrics);

  return (
    <section className="mission-history-section harbor-metrics" aria-label={t('Harbor activity')}>
      <div className="mission-history-header">
        <span className="mission-history-title">{t('Harbor activity')}</span>
        <div className="mission-history-controls">
          <div className="mission-history-time-tabs" role="group" aria-label={t('Time range')}>
            {HARBOR_METRICS_RANGES.map((r) => (
              <button
                key={r.value}
                type="button"
                className={'mission-history-time-tab' + (range === r.value ? ' active' : '')}
                aria-pressed={range === r.value}
                onClick={() => setRange(r.value)}
              >
                {t(r.label)}
              </button>
            ))}
          </div>
          <button type="button" className="mission-history-refresh-btn" onClick={refresh} title={t('Refresh')} aria-label={t('Refresh')}>&#x21bb;</button>
        </div>
      </div>

      {loading && !metrics ? (
        <div className="mission-history-empty">{t('Loading Harbor metrics...')}</div>
      ) : error ? (
        <div className="mission-history-empty harbor-metrics-error" role="alert">{error}</div>
      ) : !metrics ? null : (
        <>
          <div className="mission-history-stats harbor-metrics-stats">
            <span><span className="mission-history-stat-value" style={{ color: 'var(--green)' }}>{metrics.jobs.missionsFinished + metrics.jobs.interactiveFinished}</span> {t('Finished')}</span>
            <span><span className="mission-history-stat-value" style={{ color: 'var(--red)' }}>{metrics.jobs.missionsFailed + metrics.jobs.interactiveFailed}</span> {t('Failed')}</span>
            <span><span className="mission-history-stat-value">{metrics.jobs.running}</span> {t('Running')}</span>
            <span><span className="mission-history-stat-value">{metrics.slots.peak}/{metrics.slots.maxConcurrentJobs}</span> {t('Peak slots')}</span>
            <span><span className="mission-history-stat-value">{metrics.link.connectedPercent === null ? '-' : `${Math.round(metrics.link.connectedPercent * 10) / 10}%`}</span> {t('Connected')}</span>
            <span><span className="mission-history-stat-value">{formatDurationMs(metrics.link.roundTripMedianMs)}</span> {t('Median round trip')}</span>
          </div>

          {empty && <div className="mission-history-empty">{t('No activity on this Harbor in this time range.')}</div>}

          <div className="harbor-metrics-grid">
            <HarborBucketChart
              title={t('Jobs over time')}
              ariaLabel={t('Finished and failed jobs per time bucket')}
              times={times}
              rows={jobRows}
              series={jobSeries}
              mode="stack"
              stepMinutes={stepMinutes}
              hours={hours}
            />
            <HarborBucketChart
              title={t('Slot usage')}
              ariaLabel={t('Concurrent jobs per time bucket against the Harbor capacity')}
              times={times}
              rows={slotRows}
              series={[
                { key: 'peak', label: t('Peak'), color: 'var(--accent)' },
                { key: 'average', label: t('Average'), color: 'var(--green)', dashed: true },
              ]}
              mode="line"
              stepMinutes={stepMinutes}
              hours={hours}
              reference={{ value: metrics.slots.maxConcurrentJobs, label: t('Max slots ({{count}})', { count: metrics.slots.maxConcurrentJobs }), color: 'var(--red)' }}
            />
          </div>

          <div className="harbor-metrics-block">
            <div className="harbor-metrics-block-title">{t('Link health')}</div>
            <svg className="harbor-metrics-strip" width="100%" height="18" viewBox="0 0 1000 18" preserveAspectRatio="none" role="img" aria-label={t('Link state over the time range')}>
              <rect x={0} y={0} width={1000} height={18} fill="var(--bg-hover)" />
              {strip.map((part, i) => (
                <rect key={i} data-state={part.state} x={part.start * 1000} y={0} width={Math.max(part.state === 'Connected' || part.state === 'Unknown' ? 0.5 : 3, part.width * 1000)} height={18} fill={LINK_STATE_COLORS[part.state]}>
                  <title>{`${t(LINK_STATE_LABELS[part.state])}: ${formatDateTime(part.startUtc)} - ${formatDateTime(part.endUtc)}`}</title>
                </rect>
              ))}
            </svg>
            <div className="mission-history-legend harbor-metrics-legend">
              {LINK_STATES.map((state) => (
                <span key={state}><span className="mission-history-legend-color" style={{ backgroundColor: LINK_STATE_COLORS[state] }} /> {t(LINK_STATE_LABELS[state])}</span>
              ))}
            </div>
            <div className="mission-history-stats harbor-metrics-stats harbor-metrics-link-stats">
              <span><span className="mission-history-stat-value">{metrics.link.disconnects}</span> {t('Disconnects')}</span>
              <span><span className="mission-history-stat-value">{metrics.link.reconnectCount ?? '-'}</span> {t('Reconnects reported by the Harbor')}</span>
              <span><span className="mission-history-stat-value">{metrics.link.lastReconnectUtc ? formatDateTime(metrics.link.lastReconnectUtc) : '-'}</span> {t('Last reconnect')}</span>
            </div>
            <HarborBucketChart
              title={t('Heartbeat round trip')}
              ariaLabel={t('Average and largest heartbeat round trip per time bucket')}
              times={times}
              rows={rttRows}
              series={[
                { key: 'average', label: t('Average'), color: 'var(--accent)' },
                { key: 'max', label: t('Largest'), color: 'var(--orange)', dashed: true },
              ]}
              mode="line"
              stepMinutes={stepMinutes}
              hours={hours}
              formatValue={(value) => formatDurationMs(value)}
            />
          </div>

          <div className="harbor-metrics-block">
            <div className="harbor-metrics-block-title">{t('Launch speed')}</div>
            <DataTable
              tableKey="harbor-launch-speed"
              columns={speedColumns}
              rows={metrics.launchSpeed}
              rowKey={(row) => row.runtime}
              ariaLabel={t('Launch speed per runtime')}
              emptyMessage={<span className="text-dim">{t('No jobs ended on this Harbor in this time range.')}</span>}
            />
          </div>

          <div className="harbor-metrics-block">
            <div className="harbor-metrics-block-header">
              <div className="harbor-metrics-block-title">{t('Token usage')}</div>
              <Link to={tokenUsageLink(harborId)} className="harbor-metrics-link">{t('Open in Token Usage')}</Link>
            </div>
            <div className="mission-history-stats harbor-metrics-stats">
              <span><span className="mission-history-stat-value">{formatTokens(metrics.tokens.totalTokens)}</span> {t('Total')}</span>
              <span><span className="mission-history-stat-value">{formatTokens(metrics.tokens.inputTokens)}</span> {t('Input')}</span>
              <span><span className="mission-history-stat-value">{formatTokens(metrics.tokens.outputTokens)}</span> {t('Output')}</span>
              <span><span className="mission-history-stat-value">{formatTokens(metrics.tokens.cachedTokens)}</span> {t('Cached')}</span>
              {metrics.tokens.estimatedCount > 0 && (
                <span className="token-usage-estimated-note">{t('{{estimated}} of {{total}} records estimated', { estimated: metrics.tokens.estimatedCount, total: metrics.tokens.recordCount })}</span>
              )}
            </div>
            {tokenDefs.length === 0 ? (
              <div className="mission-history-empty">{t('No token usage for this time range')}</div>
            ) : (
              <HarborBucketChart
                title={t('Tokens by runtime and model')}
                ariaLabel={t('Tokens per time bucket by runtime and model')}
                times={times}
                rows={tokenValueRows}
                series={tokenDefs}
                mode="stack"
                stepMinutes={stepMinutes}
                hours={hours}
                formatValue={formatTokens}
              />
            )}
          </div>
        </>
      )}
    </section>
  );
}
