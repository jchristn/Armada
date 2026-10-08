import type {
  HarborLinkSegment,
  HarborLinkSegmentState,
  HarborMetrics,
  HarborMetricsRange,
  HarborTokenBucket,
} from '../types/models';

/**
 * Pure helpers for the per-Harbor charts (GET /api/v1/harbors/{id}/metrics). The Admiral computes every number; these
 * only shape the response for drawing. No DOM, so the mobile app can share them.
 */

/** Ranges offered by the selector; labels are catalog keys. */
export const HARBOR_METRICS_RANGES: ReadonlyArray<{ value: HarborMetricsRange; label: string }> = [
  { value: '1h', label: 'Last Hour' },
  { value: '24h', label: 'Last Day' },
  { value: '7d', label: 'Last Week' },
];

/** Theme color per link state (CSS variables, so light and dark both work). */
export const LINK_STATE_COLORS: Record<HarborLinkSegmentState, string> = {
  Connected: 'var(--green)',
  Reconnecting: 'var(--yellow)',
  Down: 'var(--red)',
  Unknown: 'var(--border)',
};

/** Catalog key per link state. */
export const LINK_STATE_LABELS: Record<HarborLinkSegmentState, string> = {
  Connected: 'Connected',
  Reconnecting: 'Reconnecting',
  Down: 'Down',
  Unknown: 'No data',
};

/** Theme colors of the jobs chart stack, bottom to top. */
export const JOB_SERIES = [
  { key: 'missionsFinished', label: 'Missions finished', color: 'var(--green)' },
  { key: 'missionsFailed', label: 'Missions failed', color: 'var(--red)' },
  { key: 'interactiveFinished', label: 'Interactive finished', color: 'var(--accent)' },
  { key: 'interactiveFailed', label: 'Interactive failed', color: 'var(--orange)' },
] as const;

export type JobSeriesKey = typeof JOB_SERIES[number]['key'];

/** Colors for token runtime/model series. */
export const TOKEN_SERIES_COLORS = ['var(--accent)', 'var(--green)', 'var(--red)', '#a855f7', '#06b6d4', '#ec4899', 'var(--text-dim)', '#14b8a6'];

/** Bucket start times (ms since epoch) shared by every series. */
export function bucketTimes(metrics: HarborMetrics): number[] {
  const from = Date.parse(metrics.fromUtc);
  const step = metrics.bucketMinutes * 60000;
  const times: number[] = [];
  for (let i = 0; i < metrics.bucketCount; i++) times.push(from + i * step);
  return times;
}

/** Window length in hours (for axis label formatting). */
export function rangeHours(range: HarborMetricsRange): number {
  return range === '1h' ? 1 : range === '7d' ? 168 : 24;
}

/** Whether the response holds anything worth charting. */
export function hasAnyData(metrics: HarborMetrics): boolean {
  const jobs = metrics.jobs;
  if (jobs.missionsFinished + jobs.missionsFailed + jobs.interactiveFinished + jobs.interactiveFailed + jobs.running > 0) return true;
  if (metrics.slots.peak > 0) return true;
  if (metrics.launchSpeed.length > 0) return true;
  if (metrics.tokens.recordCount > 0) return true;
  if (metrics.link.roundTrip.some((b) => b.heartbeatCount > 0)) return true;
  return metrics.link.segments.some((s) => s.state !== 'Unknown');
}

/** A link segment placed on a 0..1 horizontal scale over the window. */
export interface LinkStripPart {
  state: HarborLinkSegmentState;
  start: number;
  width: number;
  startUtc: string;
  endUtc: string;
}

/** Place link segments on the window [fromUtc, toUtc) as fractions; segments outside are clipped. */
export function linkStrip(segments: HarborLinkSegment[], fromUtc: string, toUtc: string): LinkStripPart[] {
  const from = Date.parse(fromUtc);
  const to = Date.parse(toUtc);
  const span = to - from;
  if (!(span > 0)) return [];
  const parts: LinkStripPart[] = [];
  for (const segment of segments) {
    const start = Math.max(from, Date.parse(segment.startUtc));
    const end = Math.min(to, Date.parse(segment.endUtc));
    if (!(end > start)) continue;
    parts.push({
      state: segment.state,
      start: (start - from) / span,
      width: (end - start) / span,
      startUtc: segment.startUtc,
      endUtc: segment.endUtc,
    });
  }
  return parts;
}

/** Compact duration: 850ms, 4.2s, 3m 12s, 1h 5m. Null renders as a dash. */
export function formatDurationMs(value: number | null | undefined): string {
  if (value === null || value === undefined || Number.isNaN(value)) return '-';
  if (value < 1000) return `${Math.round(value)}ms`;
  const seconds = value / 1000;
  if (seconds < 60) return `${seconds.toFixed(1)}s`;
  const totalSeconds = Math.round(seconds);
  const minutes = Math.floor(totalSeconds / 60);
  if (minutes < 60) return `${minutes}m ${totalSeconds % 60}s`;
  return `${Math.floor(minutes / 60)}h ${minutes % 60}m`;
}

/**
 * SVG polyline points for a sparkline of nullable values over a width x height box. Gaps (nulls) split the line,
 * so each returned string is one run of consecutive values.
 */
export function sparklineRuns(values: (number | null)[], width: number, height: number): string[] {
  const present = values.filter((v): v is number => v !== null);
  if (present.length === 0 || values.length === 0) return [];
  const max = Math.max(...present);
  const min = Math.min(...present);
  const range = max - min || 1;
  const step = values.length > 1 ? width / (values.length - 1) : 0;
  const runs: string[] = [];
  let current: string[] = [];
  values.forEach((value, i) => {
    if (value === null) {
      if (current.length > 0) runs.push(current.join(' '));
      current = [];
      return;
    }
    const x = values.length > 1 ? i * step : width / 2;
    const y = height - ((value - min) / range) * height;
    current.push(`${x.toFixed(1)},${y.toFixed(1)}`);
  });
  if (current.length > 0) runs.push(current.join(' '));
  return runs;
}

/** A token chart series: one runtime and model. */
export interface TokenSeriesDef {
  key: string;
  label: string;
  color: string;
}

/** The runtime/model series of the token chart, most tokens first (from the whole-window totals). */
export function tokenSeries(metrics: HarborMetrics): TokenSeriesDef[] {
  return metrics.tokens.series.map((s, i) => ({
    key: `${s.runtime}|${s.model}`,
    label: `${s.runtime} / ${s.model}`,
    color: TOKEN_SERIES_COLORS[i % TOKEN_SERIES_COLORS.length],
  }));
}

/** Per-bucket totals for each token series, in the order of `series`. */
export function tokenRows(buckets: HarborTokenBucket[], series: TokenSeriesDef[]): number[][] {
  return buckets.map((bucket) => series.map((def) => {
    const entry = bucket.series.find((s) => `${s.runtime}|${s.model}` === def.key);
    return entry ? entry.totalTokens : 0;
  }));
}

/** Dashboard link to the Token Usage page filtered to one Harbor. */
export function tokenUsageLink(harborId: string): string {
  return `/activity?source=tokens&harborId=${encodeURIComponent(harborId)}`;
}
