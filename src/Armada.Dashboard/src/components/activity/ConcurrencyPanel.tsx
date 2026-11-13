import { useCallback, useEffect, useMemo, useState } from 'react';
import { getConcurrencyMetrics, listVessels } from '../../api/client';
import { listAll } from '../../api/paging';
import type { ConcurrencyMetrics, Vessel } from '../../types/models';
import { useLocale } from '../../context/LocaleContext';
import RefreshButton from '../shared/RefreshButton';
import { bucketBars, CONCURRENCY_RANGES, niceMax, WAIT_GROUPS, type WaitBucketBar } from '../../lib/concurrencyCharts';
import { formatWaitDuration, waitGateLabel } from '../../lib/missionWaits';

const W = 800;
const PAD = { left: 44, right: 12, top: 12, bottom: 26 };

/**
 * Activity > Concurrency: how long missions waited and on what, from recorded wait intervals
 * (GET /api/v1/metrics/concurrency). Slot wait tiles, wait minutes by gate group per time bucket, missions waiting
 * per bucket, and a per-gate table view.
 */
export default function ConcurrencyPanel() {
  const { t, locale } = useLocale();
  const [rangeKey, setRangeKey] = useState<string>('24h');
  const [vesselId, setVesselId] = useState('');
  const [vessels, setVessels] = useState<Vessel[]>([]);
  const [metrics, setMetrics] = useState<ConcurrencyMetrics | null>(null);
  const [error, setError] = useState('');
  const [hover, setHover] = useState<number | null>(null);

  const load = useCallback(async () => {
    const range = CONCURRENCY_RANGES.find(r => r.key === rangeKey) ?? CONCURRENCY_RANGES[1];
    const to = new Date();
    const from = new Date(to.getTime() - range.hours * 3600 * 1000);
    try {
      setMetrics(await getConcurrencyMetrics({ from: from.toISOString(), to: to.toISOString(), vesselId: vesselId || undefined }));
      setError('');
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : String(e));
    }
  }, [rangeKey, vesselId]);

  useEffect(() => { void load(); }, [load]);
  useEffect(() => { listAll(listVessels).then(r => setVessels(r.objects || [])).catch(() => {}); }, []);

  const bars = useMemo(() => bucketBars(metrics), [metrics]);
  const maxMinutes = niceMax(Math.max(0, ...bars.map(b => b.totalMinutes)));
  const maxWaiting = niceMax(Math.max(0, ...bars.map(b => b.missionsWaiting)));
  const hasWaits = (metrics?.byGate.length ?? 0) > 0;
  const plotW = W - PAD.left - PAD.right;
  const slot = bars.length > 0 ? plotW / bars.length : plotW;
  const barW = Math.max(2, Math.min(28, slot - 2));
  const range = CONCURRENCY_RANGES.find(r => r.key === rangeKey) ?? CONCURRENCY_RANGES[1];
  // Short axis ticks: the time within a day, the date and hour across days.
  const label = (b: WaitBucketBar) => new Date(b.startUtc).toLocaleString(locale, range.hours <= 24
    ? { hour: '2-digit', minute: '2-digit' }
    : { month: 'short', day: 'numeric', hour: range.hours <= 168 ? '2-digit' : undefined });

  const chart = (height: number, value: (b: WaitBucketBar) => number, max: number, unit: (v: number) => string, stacked: boolean, title: string) => {
    const plotH = height - PAD.top - PAD.bottom;
    const ticks = [0, max / 2, max];
    return (
      <svg viewBox={`0 0 ${W} ${height}`} className="concurrency-chart" role="img" aria-label={title} onMouseLeave={() => setHover(null)}>
        {ticks.map(tick => {
          const y = PAD.top + plotH - (tick / max) * plotH;
          return <g key={tick}>
            <line x1={PAD.left} x2={W - PAD.right} y1={y} y2={y} stroke="var(--border)" strokeWidth={0.5} strokeDasharray={tick === 0 ? undefined : '3,4'} />
            <text x={PAD.left - 6} y={y + 3} fontSize="9" textAnchor="end" fill="var(--text-dim)">{unit(tick)}</text>
          </g>;
        })}
        {bars.map((b, i) => {
          const x = PAD.left + i * slot + (slot - barW) / 2;
          let base = PAD.top + plotH;
          const segments = stacked
            ? WAIT_GROUPS.map(g => ({ key: g.key, color: g.color, h: (b.minutes[g.key] / max) * plotH }))
            : [{ key: 'waiting', color: 'var(--wait-series-1)', h: (value(b) / max) * plotH }];
          return (
            <g key={b.startUtc} onMouseEnter={() => setHover(i)} opacity={hover === null || hover === i ? 1 : 0.55}>
              <rect x={PAD.left + i * slot} y={PAD.top} width={slot} height={plotH} fill="transparent" />
              {segments.filter(s => s.h > 0).map((s, si, visible) => {
                // A 2px surface gap separates stacked segments; the top segment carries the rounded end.
                const gap = si < visible.length - 1 ? 2 : 0;
                const h = Math.max(0.5, s.h - gap);
                base -= s.h;
                return <rect key={s.key} x={x} y={base + gap} width={barW} height={h} rx={si === visible.length - 1 ? 2 : 0} fill={s.color} />;
              })}
            </g>
          );
        })}
        {bars.length > 0 && [0, Math.floor(bars.length / 2), bars.length - 1].filter((v, i, a) => a.indexOf(v) === i).map((i, n, all) => (
          <text key={i} x={n === 0 ? PAD.left : n === all.length - 1 && all.length > 1 ? W - PAD.right : PAD.left + i * slot + slot / 2} y={height - 8} fontSize="9"
            textAnchor={n === 0 ? 'start' : n === all.length - 1 && all.length > 1 ? 'end' : 'middle'} fill="var(--text-dim)">{label(bars[i])}</text>
        ))}
      </svg>
    );
  };

  const hovered = hover !== null ? bars[hover] : null;

  return (
    <div className="concurrency-panel" data-testid="concurrency-panel">
      <div className="concurrency-filters">
        <label>
          <span className="text-dim">{t('Window')}</span>
          <select value={rangeKey} onChange={e => setRangeKey(e.target.value)}>
            {CONCURRENCY_RANGES.map(r => <option key={r.key} value={r.key}>{t(r.label)}</option>)}
          </select>
        </label>
        <label>
          <span className="text-dim">{t('Vessel')}</span>
          <select value={vesselId} onChange={e => setVesselId(e.target.value)}>
            <option value="">{t('All vessels')}</option>
            {vessels.map(v => <option key={v.id} value={v.id}>{v.name}</option>)}
          </select>
        </label>
        <RefreshButton onRefresh={load} title="Refresh concurrency" />
      </div>

      {error && <p className="text-dim" role="status">{t('Concurrency metrics unavailable: {{message}}', { message: error })}</p>}

      {metrics && (
        <>
          <div className="concurrency-tiles">
            <div className="card" title={t('Per mission, the time spent waiting for capacity (voyage, mission, captain, endpoint, runtime and global slots) in the window.')}>
              <h3>{t('Slot wait p50')}</h3><div className="concurrency-tile-value">{metrics.slotWait.p50Seconds === null ? '-' : formatWaitDuration(metrics.slotWait.p50Seconds)}</div>
            </div>
            <div className="card"><h3>{t('Slot wait p95')}</h3><div className="concurrency-tile-value">{metrics.slotWait.p95Seconds === null ? '-' : formatWaitDuration(metrics.slotWait.p95Seconds)}</div></div>
            <div className="card"><h3>{t('Longest slot wait')}</h3><div className="concurrency-tile-value">{metrics.slotWait.maxSeconds === null ? '-' : formatWaitDuration(metrics.slotWait.maxSeconds)}</div></div>
            <div className="card" title={t('Missions that waited for capacity at least once in the window.')}>
              <h3>{t('Missions that waited')}</h3><div className="concurrency-tile-value">{metrics.slotWait.missions}</div>
            </div>
          </div>

          {!hasWaits ? (
            <p className="text-dim" style={{ padding: '1rem 0' }}>{t('No waits recorded in this window.')}</p>
          ) : (
            <>
              <div className="card concurrency-chart-card">
                <div className="concurrency-chart-head">
                  <h3 title={t('Minutes missions spent waiting in each time bucket, by what held them back.')}>{t('Wait time by cause')}</h3>
                  <ul className="concurrency-legend" aria-label={t('Legend')}>
                    {WAIT_GROUPS.map(g => <li key={g.key}><span className="concurrency-swatch" style={{ background: g.color }} />{t(g.label)}</li>)}
                  </ul>
                </div>
                {chart(220, b => b.totalMinutes, maxMinutes, v => formatWaitDuration(v * 60), true, t('Wait time by cause'))}
                <div className="concurrency-tooltip" aria-live="polite">
                  {hovered
                    ? <>
                        <strong>{new Date(hovered.startUtc).toLocaleString(locale, { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' })}</strong>
                        {WAIT_GROUPS.filter(g => hovered.minutes[g.key] > 0).map(g => (
                          <span key={g.key}><span className="concurrency-swatch" style={{ background: g.color }} />{t(g.label)} {formatWaitDuration(hovered.minutes[g.key] * 60)}</span>
                        ))}
                        <span className="text-dim">{t('{{count}} missions waiting', { count: hovered.missionsWaiting })}</span>
                      </>
                    : <span className="text-dim">{t('Hover a bar for its breakdown.')}</span>}
                </div>
              </div>
              <div className="card concurrency-chart-card">
                <h3 title={t('Distinct missions that waited on any gate during each time bucket.')}>{t('Missions waiting')}</h3>
                {chart(150, b => b.missionsWaiting, maxWaiting, v => String(Math.round(v)), false, t('Missions waiting'))}
              </div>
              <div className="card">
                <h3>{t('Wait by gate')}</h3>
                <div className="table-wrap">
                  <table className="table">
                    <thead><tr><th>{t('Gate')}</th><th>{t('Time waited')}</th><th>{t('Missions')}</th></tr></thead>
                    <tbody>
                      {metrics.byGate.map(g => (
                        <tr key={g.gate}><td>{t(waitGateLabel(g.gate))}</td><td>{formatWaitDuration(g.seconds)}</td><td>{g.missions}</td></tr>
                      ))}
                    </tbody>
                  </table>
                </div>
                {metrics.truncated && <p className="text-dim">{t('More than 50000 waits matched; totals are a lower bound. Narrow the window or pick a vessel.')}</p>}
              </div>
            </>
          )}
        </>
      )}
    </div>
  );
}
