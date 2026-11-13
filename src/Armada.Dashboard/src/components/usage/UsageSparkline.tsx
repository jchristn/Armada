import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { getUsageSeries } from '../../api/client';
import { useLocale } from '../../context/LocaleContext';
import { useUsageRevision } from '../../context/WebSocketContext';
import { accountAlias } from '../../lib/usage';
import type { UsageSeries, UsageSnapshotView } from '../../types/models';

interface UsageSparklineProps {
  view: UsageSnapshotView;
  /** Provider history route for this account (the Limits tab filtered to it). */
  historyPath: string;
}

const WIDTH = 120;
const HEIGHT = 28;
const RANGE_MS = 7 * 24 * 3600000;
const HOUR_MS = 3600000;

/** Polyline point lists for one series; a missing sample ends the current line (no interpolation). */
export function sparklineSegments(series: UsageSeries, fromMs: number, toMs: number): string[] {
  const span = Math.max(1, toMs - fromMs);
  const segments: string[] = [];
  let current: string[] = [];
  for (const point of series.points) {
    if (point.lastPercent === null || point.lastPercent === undefined || Number.isNaN(point.lastPercent)) {
      if (current.length > 0) segments.push(current.join(' '));
      current = [];
      continue;
    }
    const mid = (Date.parse(point.bucketStartUtc) + Date.parse(point.bucketEndUtc)) / 2;
    const px = ((mid - fromMs) / span) * WIDTH;
    const py = HEIGHT - 2 - (Math.max(0, Math.min(100, point.lastPercent)) / 100) * (HEIGHT - 4);
    current.push(`${px.toFixed(1)},${py.toFixed(1)}`);
  }
  if (current.length > 0) segments.push(current.join(' '));
  return segments;
}

/** Seven-day limit history for one account; links to the provider history. */
export default function UsageSparkline({ view, historyPath }: UsageSparklineProps) {
  const { t } = useLocale();
  const revision = useUsageRevision();
  const [state, setState] = useState<{ status: 'loading' | 'error' | 'ready'; series: UsageSeries[]; fromMs: number; toMs: number }>(
    { status: 'loading', series: [], fromMs: 0, toMs: 0 });

  const { provider, accountKey, hostId } = view;

  useEffect(() => {
    let cancelled = false;
    const toMs = Math.ceil(Date.now() / HOUR_MS) * HOUR_MS;
    const fromMs = toMs - RANGE_MS;
    // A throw while starting the request becomes a rejection, so the sparkline degrades to 'History unavailable'.
    Promise.resolve().then(() => getUsageSeries({
      from: new Date(fromMs).toISOString(),
      to: new Date(toMs).toISOString(),
      metric: 'limits',
      groupBy: 'account',
      bucketMinutes: 60,
      provider: provider ?? undefined,
      accountKey: accountKey ?? undefined,
      hostId: hostId ?? undefined,
    }))
      .then((result) => { if (!cancelled) setState({ status: 'ready', series: result.series ?? [], fromMs, toMs }); })
      .catch(() => { if (!cancelled) setState((prev) => ({ ...prev, status: 'error' })); });
    return () => { cancelled = true; };
  }, [provider, accountKey, hostId, revision]);

  if (state.status === 'error') return <span className="usage-sparkline-unavailable text-dim">{t('History unavailable')}</span>;

  const label = t('7-day limit history for account {{account}} on host {{host}}', { account: accountAlias(accountKey), host: view.harborId ?? hostId ?? '-' });
  const hasData = state.series.some((series) => series.points.some((point) => point.lastPercent !== null && point.lastPercent !== undefined));

  return (
    <Link to={historyPath} className="usage-sparkline" aria-label={label} title={label}>
      <svg viewBox={`0 0 ${WIDTH} ${HEIGHT}`} width={WIDTH} height={HEIGHT} aria-hidden="true" focusable="false">
        <line className="usage-sparkline-base" x1={0} x2={WIDTH} y1={HEIGHT - 2} y2={HEIGHT - 2} />
        {state.series.map((series, s) => sparklineSegments(series, state.fromMs, state.toMs).map((points, k) => (
          <polyline key={`${s}-${k}`} className="usage-sparkline-line" points={points} fill="none" />
        )))}
      </svg>
      {state.status === 'ready' && !hasData && <span className="usage-sparkline-empty text-dim">{t('No history yet')}</span>}
    </Link>
  );
}
