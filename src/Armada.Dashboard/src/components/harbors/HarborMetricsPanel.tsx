import TokenActivityHeatmap from '../activity/TokenActivityHeatmap';
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
  const tokenDefs = metrics ? tokenSeries(metrics, t('Other')) : [];
  const tokenValueRows = metrics ? tokenRows(metrics.tokens.buckets, tokenDefs) : [];
  const strip = metrics ? linkStrip(metrics.link.segments, metrics.fromUtc, metrics.toUtc) : [];

  const speedColumns: DataTableColumn<HarborLaunchSpeed>[] = [
    { key: 'runtime', label: t('Runtime'), required: true, headerTitle: t('Agent runtime the jobs used, such as ClaudeCode or Codex.'), cellClassName: 'cell-nowrap', render: (row) => <strong>{row.isOther ? `${t('Other')} (${row.runtimeCount ?? 0})` : row.runtime}</strong> },
    { key: 'jobs', label: t('Jobs'), headerTitle: t('Jobs of this runtime that ended in this range.'), cellClassName: 'text-dim', render: (row) => row.jobCount },
    { key: 'firstMedian', label: t('First output (median)'), headerTitle: t('Typical time from launch until the agent produced its first output. Lower means faster starts.'), cellClassName: 'cell-nowrap', render: (row) => formatDurationMs(row.firstOutputMedianMs) },
    { key: 'firstP95', label: t('First output (p95)'), headerTitle: t('Time to first output that 95 percent of jobs beat. Shows the slow starts.'), cellClassName: 'cell-nowrap', render: (row) => formatDurationMs(row.firstOutputP95Ms) },
    { key: 'durationMedian', label: t('Runtime (median)'), headerTitle: t('Typical time a job ran from launch to finish.'), cellClassName: 'cell-nowrap', render: (row) => formatDurationMs(row.durationMedianMs) },
    { key: 'durationP95', label: t('Runtime (p95)'), headerTitle: t('Run time that 95 percent of jobs beat. Shows the longest jobs.'), cellClassName: 'cell-nowrap', render: (row) => formatDurationMs(row.durationP95Ms) },
    {
      key: 'trend', label: t('First output trend'), headerTitle: t('Median time to first output across the range, oldest on the left. A rising line means starts are getting slower.'), cellClassName: 'cell-nowrap',
      render: (row) => <Sparkline values={row.firstOutputMedianMsByBucket} label={t('First output trend for {{runtime}}', { runtime: row.runtime })} />,
    },
  ];

  const empty = metrics !== null && !hasAnyData(metrics);

  return (
    <section className="mission-history-section harbor-metrics" aria-label={t('Harbor activity')}>
      <div className="mission-history-header">
        <span className="mission-history-title" title={t('What this Harbor did over the chosen time range, as recorded by the Admiral.')}>{t('Harbor activity')}</span>
        <div className="mission-history-controls">
          <div className="mission-history-time-tabs" role="group" aria-label={t('Time range')}>
            {HARBOR_METRICS_RANGES.map((r) => (
              <button
                key={r.value}
                type="button"
                className={'mission-history-time-tab' + (range === r.value ? ' active' : '')}
                aria-pressed={range === r.value}
                title={t('Show this Harbor\'s activity over this time range: {{range}}.', { range: t(r.label) })}
                onClick={() => setRange(r.value)}
              >
                {t(r.label)}
              </button>
            ))}
          </div>
          <button type="button" className="mission-history-refresh-btn" onClick={refresh} title={t('Reload these charts now. They also refresh on their own while shown.')} aria-label={t('Refresh')}>&#x21bb;</button>
        </div>
      </div>

      {loading && !metrics ? (
        <div className="mission-history-empty">{t('Loading Harbor metrics...')}</div>
      ) : error ? (
        <div className="mission-history-empty harbor-metrics-error" role="alert">{error}</div>
      ) : !metrics ? null : (
        <>
          <div className="mission-history-stats harbor-metrics-stats">
            <span title={t('Jobs (missions and interactive sessions) that ended successfully in this range.')}><span className="mission-history-stat-value" style={{ color: 'var(--green)' }}>{metrics.jobs.missionsFinished + metrics.jobs.interactiveFinished}</span> {t('Finished')}</span>
            <span title={t('Jobs (missions and interactive sessions) that ended in failure in this range.')}><span className="mission-history-stat-value" style={{ color: 'var(--red)' }}>{metrics.jobs.missionsFailed + metrics.jobs.interactiveFailed}</span> {t('Failed')}</span>
            <span title={t('Jobs running on this Harbor right now.')}><span className="mission-history-stat-value">{metrics.jobs.running}</span> {t('Running')}</span>
            <span title={t('Most jobs running at once in this range, out of the Harbor\'s capacity. Reaching capacity means work had to wait.')}><span className="mission-history-stat-value">{metrics.slots.peak}/{metrics.slots.maxConcurrentJobs}</span> {t('Peak slots')}</span>
            <span title={t('Share of this range the Harbor\'s link was connected, counting only time with a known state.')}><span className="mission-history-stat-value">{metrics.link.connectedPercent === null ? '-' : `${Math.round(metrics.link.connectedPercent * 10) / 10}%`}</span> {t('Connected')}</span>
            <span title={t('Typical time for a heartbeat to travel between the Harbor and the Admiral and back. Higher means a slower link.')}><span className="mission-history-stat-value">{formatDurationMs(metrics.link.roundTripMedianMs)}</span> {t('Median round trip')}</span>
          </div>

          {empty && <div className="mission-history-empty">{t('No activity on this Harbor in this time range.')}</div>}

          <div className="harbor-metrics-grid">
            <HarborBucketChart
              title={t('Jobs over time')}
              ariaLabel={t('Finished and failed jobs per time bucket')}
              description={t('Missions and interactive jobs that finished or failed in each time slice. Hover a bar for the counts.')}
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
              description={t('Most and average jobs running at once in each time slice, against the Harbor\'s capacity line.')}
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
            <div className="harbor-metrics-block-title" title={t('When the Harbor\'s connection to the Admiral was up, reconnecting or down. Hover a stretch of the bar for its times.')}>{t('Link health')}</div>
            <svg className="harbor-metrics-strip" width="100%" height="18" viewBox="0 0 1000 18" preserveAspectRatio="none" role="img" aria-label={t('Link state over the time range')}>
              <rect x={0} y={0} width={1000} height={18} fill="var(--bg-hover)" />
              {strip.map((part, i) => (
                <rect key={i} data-state={part.state} x={part.start * 1000} y={0} width={Math.max(part.state === 'Connected' || part.state === 'Unknown' ? 0.5 : 3, part.width * 1000)} height={18} fill={LINK_STATE_COLORS[part.state]}>
                  <title>{`${t(LINK_STATE_LABELS[part.state])}: ${formatDateTime(part.startUtc)} - ${formatDateTime(part.endUtc)}`}</title>
                </rect>
              ))}
            </svg>
            {metrics.link.segmentsCoalesced && (
              <p className="text-dim" data-testid="harbor-link-coalesced">{t('Too many link changes to draw one by one; each time slice shows its main state.')}</p>
            )}
            <div className="mission-history-legend harbor-metrics-legend">
              {LINK_STATES.map((state) => (
                <span key={state}><span className="mission-history-legend-color" style={{ backgroundColor: LINK_STATE_COLORS[state] }} /> {t(LINK_STATE_LABELS[state])}</span>
              ))}
            </div>
            <div className="mission-history-stats harbor-metrics-stats harbor-metrics-link-stats">
              <span title={t('Times the Admiral saw the link drop in this range.')}><span className="mission-history-stat-value">{metrics.link.disconnects}</span> {t('Disconnects')}</span>
              <span title={t('The Harbor\'s own count of reconnects, from its latest heartbeat. A dash means it does not report one.')}><span className="mission-history-stat-value">{metrics.link.reconnectCount ?? '-'}</span> {t('Reconnects reported by the Harbor')}</span>
              <span title={t('When the Harbor last reconnected, by its own clock.')}><span className="mission-history-stat-value">{metrics.link.lastReconnectUtc ? formatDateTime(metrics.link.lastReconnectUtc) : '-'}</span> {t('Last reconnect')}</span>
            </div>
            <HarborBucketChart
              title={t('Heartbeat round trip')}
              ariaLabel={t('Average and largest heartbeat round trip per time bucket')}
              description={t('Average and largest heartbeat round-trip time in each time slice. Spikes point to a slow or busy link.')}
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
            <div className="harbor-metrics-block-title" title={t('Per runtime, how long jobs took to produce their first output and to finish, for jobs that ended in this range.')}>{t('Launch speed')}</div>
            <DataTable
              tableKey="harbor-launch-speed"
              columns={speedColumns}
              rows={metrics.launchSpeed}
              rowKey={(row) => (row.isOther ? '__other' : row.runtime)}
              ariaLabel={t('Launch speed per runtime')}
              emptyMessage={<span className="text-dim">{t('No jobs ended on this Harbor in this time range.')}</span>}
            />
          </div>

          <div className="harbor-metrics-block">
            <div className="harbor-metrics-block-header">
              <div className="harbor-metrics-block-title" title={t('Model tokens used by jobs on this Harbor in this range, by runtime and model.')}>{t('Token usage')}</div>
              <Link to={tokenUsageLink(harborId)} className="harbor-metrics-link" title={t('Open the Token Usage page filtered to this Harbor, with more breakdowns.')}>{t('Open in Token Usage')}</Link>
            </div>
            <div className="mission-history-stats harbor-metrics-stats">
              <span title={t('Input plus output tokens. Cached input is part of input, so it is not counted twice.')}><span className="mission-history-stat-value">{formatTokens(metrics.tokens.totalTokens)}</span> {t('Total')}</span>
              <span title={t('Tokens sent to the model, including cached input.')}><span className="mission-history-stat-value">{formatTokens(metrics.tokens.inputTokens)}</span> {t('Input')}</span>
              <span title={t('Tokens the model generated.')}><span className="mission-history-stat-value">{formatTokens(metrics.tokens.outputTokens)}</span> {t('Output')}</span>
              <span title={t('Input tokens read from the provider\'s prompt cache, usually billed at a lower rate.')}><span className="mission-history-stat-value">{formatTokens(metrics.tokens.cachedTokens)}</span> {t('Cached')}</span>
              {metrics.tokens.estimatedCount > 0 && (
                <span className="token-usage-estimated-note" title={t('Some jobs did not report exact token counts, so Armada estimated them.')}>{t('{{estimated}} of {{total}} records estimated', { estimated: metrics.tokens.estimatedCount, total: metrics.tokens.recordCount })}</span>
              )}
            </div>
            {tokenDefs.length === 0 ? (
              <div className="mission-history-empty">{t('No token usage for this time range')}</div>
            ) : (
              <HarborBucketChart
                title={t('Tokens by runtime and model')}
                ariaLabel={t('Tokens per time bucket by runtime and model')}
                description={t('Tokens used in each time slice, stacked by runtime and model. Hover a bar for each part.')}
                times={times}
                rows={tokenValueRows}
                series={tokenDefs}
                mode="stack"
                stepMinutes={stepMinutes}
                hours={hours}
                formatValue={formatTokens}
              />
            )}
            <TokenActivityHeatmap harborId={harborId} />
          </div>
        </>
      )}
    </section>
  );
}
