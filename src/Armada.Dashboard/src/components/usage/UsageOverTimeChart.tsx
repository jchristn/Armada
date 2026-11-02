import { useEffect, useId, useLayoutEffect, useMemo, useRef, useState, type KeyboardEvent, type MouseEvent } from 'react';
import { createPortal } from 'react-dom';
import { useLocale } from '../../context/LocaleContext';
import { usageWindowLabel } from '../../lib/usageWindowLabel';
import type { UsageSeries, UsageSeriesPoint } from '../../types/models';
import { USAGE_DANGER_PERCENT, USAGE_WARNING_PERCENT, accountAlias } from '../../lib/usage';

/** One chart selection: exactly the bucket time range and the provider/account/host of the selected series. */
export interface UsageBucketSelection {
  bucketStartUtc: string;
  bucketEndUtc: string;
  provider: string;
  accountKey: string | null;
  hostId: string | null;
}

type PanelKind = 'limits' | 'tokens';

interface UsageOverTimeChartProps {
  /** Limit series grouped by account: one independent line per account and window. */
  limits: UsageSeries[];
  /** Token series grouped by provider: bars recorded at completion. */
  tokens: UsageSeries[];
  fromUtc: string;
  toUtc: string;
  /** Current selection, highlighted on both panels. */
  selected?: UsageBucketSelection | null;
  onSelectBucket: (selection: UsageBucketSelection) => void;
  /** Which panels to draw. Defaults to both. */
  show?: 'both' | 'limits' | 'tokens';
}

const WIDTH = 800;
const PAD_LEFT = 52;
const PAD_RIGHT = 12;
const PLOT_WIDTH = WIDTH - PAD_LEFT - PAD_RIGHT;
const LIMITS_HEIGHT = 190;
const TOKENS_HEIGHT = 150;
const PAD_TOP = 10;
const PAD_BOTTOM = 22;
const SERIES_COLORS = ['var(--accent)', 'var(--green)', '#a855f7', '#06b6d4', 'var(--orange)', '#ec4899', '#14b8a6', 'var(--red)'];

function percentKnown(point: UsageSeriesPoint): boolean {
  return point.lastPercent !== null && point.lastPercent !== undefined && !Number.isNaN(point.lastPercent);
}

/** True when the provider window reset inside this bucket, or reset since the previous bucket and the percent dropped. */
export function isResetPoint(previous: UsageSeriesPoint | null, current: UsageSeriesPoint): boolean {
  if (current.lastResetUtc) {
    const reset = Date.parse(current.lastResetUtc);
    if (reset >= Date.parse(current.bucketStartUtc) && reset < Date.parse(current.bucketEndUtc)) return true;
  }
  if (!previous || !percentKnown(previous) || !percentKnown(current)) return false;
  return !!current.lastResetUtc
    && previous.lastResetUtc !== current.lastResetUtc
    && (current.lastPercent as number) < (previous.lastPercent as number);
}

/**
 * Split one limit series into drawable segments of point indices. A missing sample ends the segment (no
 * interpolation through gaps) and a reset starts a new one (no line drawn across a reset).
 */
export function limitSegments(points: UsageSeriesPoint[]): { segments: number[][]; resets: number[] } {
  const segments: number[][] = [];
  const resets: number[] = [];
  let current: number[] = [];
  let previousKnown: UsageSeriesPoint | null = null;
  points.forEach((point, index) => {
    if (!percentKnown(point)) {
      if (current.length > 0) segments.push(current);
      current = [];
      if (point.lastResetUtc && isResetPoint(null, point)) resets.push(index);
      return;
    }
    if (isResetPoint(previousKnown, point)) {
      resets.push(index);
      if (current.length > 0) segments.push(current);
      current = [];
    }
    current.push(index);
    previousKnown = point;
  });
  if (current.length > 0) segments.push(current);
  return { segments, resets };
}

function seriesEstimated(series: UsageSeries): boolean {
  return series.points.some((point) => point.estimated);
}

function sameSelection(a: UsageBucketSelection | null | undefined, series: UsageSeries, point: UsageSeriesPoint, kind: PanelKind): boolean {
  if (!a) return false;
  return a.bucketStartUtc === point.bucketStartUtc
    && a.provider === (series.provider ?? '')
    && (a.accountKey ?? null) === (kind === 'limits' ? series.accountKey ?? null : null)
    && (a.hostId ?? null) === (kind === 'limits' ? series.hostId ?? null : null);
}

/** Two aligned panels on one time scale: limit utilization lines and token bars recorded at completion. */
export default function UsageOverTimeChart({ limits, tokens, fromUtc, toUtc, selected, onSelectBucket, show = 'both' }: UsageOverTimeChartProps) {
  const { t, formatDateTime } = useLocale();
  const [cursor, setCursor] = useState<Record<PanelKind, { s: number; b: number }>>({ limits: { s: 0, b: 0 }, tokens: { s: 0, b: 0 } });
  const [showTable, setShowTable] = useState(false);
  const [tooltip, setTooltip] = useState<{ x: number; y: number; label: string } | null>(null);
  const tooltipId = useId();
  const tooltipRef = useRef<HTMLDivElement | null>(null);
  const [tooltipPosition, setTooltipPosition] = useState({ left: 0, top: 0 });
  useLayoutEffect(() => {
    if (!tooltip || !tooltipRef.current) return;
    const bounds = tooltipRef.current.getBoundingClientRect();
    setTooltipPosition({
      left: Math.max(8, Math.min(tooltip.x + 14, window.innerWidth - bounds.width - 8)),
      top: Math.max(8, Math.min(tooltip.y + 14, window.innerHeight - bounds.height - 8)),
    });
  }, [tooltip]);
  useEffect(() => { setTooltip(null); }, [limits, fromUtc, toUtc, show]);
  useEffect(() => {
    const dismiss = () => setTooltip(null);
    window.addEventListener('scroll', dismiss, true);
    window.addEventListener('resize', dismiss);
    return () => {
      window.removeEventListener('scroll', dismiss, true);
      window.removeEventListener('resize', dismiss);
    };
  }, []);
  const rootRef = useRef<HTMLDivElement | null>(null);
  const pendingFocus = useRef<string | null>(null);

  const fromMs = Date.parse(fromUtc);
  const toMs = Date.parse(toUtc);
  const span = Math.max(1, toMs - fromMs);
  const x = (utc: string) => PAD_LEFT + ((Date.parse(utc) - fromMs) / span) * PLOT_WIDTH;
  const percentFormat = useMemo(() => new Intl.NumberFormat(undefined, { maximumFractionDigits: 1 }), []);
  const tokenFormat = useMemo(() => new Intl.NumberFormat(), []);

  useEffect(() => {
    if (!pendingFocus.current || !rootRef.current) return;
    const el = rootRef.current.querySelector<SVGElement>(`[data-mark="${pendingFocus.current}"]`);
    pendingFocus.current = null;
    el?.focus();
  }, [cursor]);

  const seriesName = (series: UsageSeries, kind: PanelKind): string => {
    if (kind === 'tokens') return series.provider ?? t('Unknown provider');
    return t('{{provider}} account {{account}} on {{host}}, {{window}}', {
      provider: series.provider ?? t('Unknown provider'),
      account: accountAlias(series.accountKey),
      host: series.hostId ?? '-',
      window: series.window ? usageWindowLabel({ name: series.window, model: series.model }, t) : t('all windows'),
    });
  };

  const valueText = (point: UsageSeriesPoint, kind: PanelKind): string => {
    if (kind === 'tokens') return t('{{count}} tokens', { count: tokenFormat.format(point.totalTokens) });
    return percentKnown(point) ? `${percentFormat.format(point.lastPercent as number)}%` : t('No sample');
  };

  const flagText = (series: UsageSeries, index: number, kind: PanelKind, resets: Set<number>): string => {
    const flags: string[] = [];
    if (kind === 'limits' && resets.has(index)) flags.push(t('Reset'));
    if (series.points[index].estimated) flags.push(t('Estimated'));
    return flags.join(', ');
  };

  const selectionFor = (series: UsageSeries, point: UsageSeriesPoint, kind: PanelKind): UsageBucketSelection | null => {
    if (!series.provider) return null;
    return {
      bucketStartUtc: point.bucketStartUtc,
      bucketEndUtc: point.bucketEndUtc,
      provider: series.provider,
      accountKey: kind === 'limits' ? series.accountKey ?? null : null,
      hostId: kind === 'limits' ? series.hostId ?? null : null,
    };
  };

  const choose = (series: UsageSeries, point: UsageSeriesPoint, kind: PanelKind) => {
    const selection = selectionFor(series, point, kind);
    if (selection) onSelectBucket(selection);
  };

  const markLabel = (series: UsageSeries, index: number, kind: PanelKind, resets: Set<number>) => {
    const point = series.points[index];
    return t('{{series}}, {{start}} to {{end}}: {{value}} {{flags}}', {
      series: seriesName(series, kind),
      start: formatDateTime(point.bucketStartUtc),
      end: formatDateTime(point.bucketEndUtc),
      value: valueText(point, kind),
      flags: flagText(series, index, kind, resets),
    }).trim();
  };

  const onKeyDown = (kind: PanelKind, list: UsageSeries[]) => (event: KeyboardEvent<SVGGElement>) => {
    if (event.key === 'Escape') { setTooltip(null); return; }
    if (list.length === 0) return;
    const current = cursor[kind];
    const series = list[Math.min(current.s, list.length - 1)];
    if (event.key === 'Enter' || event.key === ' ') {
      event.preventDefault();
      const point = series.points[Math.min(current.b, series.points.length - 1)];
      if (point) choose(series, point, kind);
      return;
    }
    let s = current.s;
    let b = current.b;
    if (event.key === 'ArrowRight') b += 1;
    else if (event.key === 'ArrowLeft') b -= 1;
    else if (event.key === 'ArrowDown') s += 1;
    else if (event.key === 'ArrowUp') s -= 1;
    else if (event.key === 'Home') b = 0;
    else if (event.key === 'End') b = Number.MAX_SAFE_INTEGER;
    else return;
    event.preventDefault();
    s = Math.max(0, Math.min(list.length - 1, s));
    b = Math.max(0, Math.min(list[s].points.length - 1, b));
    pendingFocus.current = `${kind}-${s}-${b}`;
    setCursor((prev) => ({ ...prev, [kind]: { s, b } }));
  };

  const tabIndexFor = (kind: PanelKind, s: number, b: number, list: UsageSeries[]) => {
    const current = cursor[kind];
    const cs = Math.min(current.s, list.length - 1);
    const cb = Math.min(current.b, Math.max(0, list[cs].points.length - 1));
    return cs === s && cb === b ? 0 : -1;
  };

  const selectedBand = (height: number) => {
    if (!selected) return null;
    const start = x(selected.bucketStartUtc);
    const end = x(selected.bucketEndUtc);
    return <rect className="usage-chart-selected-band" x={start} y={PAD_TOP} width={Math.max(2, end - start)} height={height - PAD_TOP - PAD_BOTTOM} />;
  };

  const timeTicks = useMemo(() => {
    const ticks: number[] = [];
    for (let i = 0; i <= 4; i++) ticks.push(fromMs + (span * i) / 4);
    return ticks;
  }, [fromMs, span]);

  const tickLabel = (ms: number) => {
    const d = new Date(ms);
    if (span > 2 * 86400000) return d.toLocaleDateString(undefined, { month: 'short', day: 'numeric' });
    return d.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' });
  };

  const xAxis = (height: number) => (
    <g className="usage-chart-axis" aria-hidden="true">
      {timeTicks.map((ms, i) => {
        const tx = PAD_LEFT + (PLOT_WIDTH * i) / 4;
        return <text key={ms} x={tx} y={height - 6} textAnchor={i === 0 ? 'start' : i === 4 ? 'end' : 'middle'}>{tickLabel(ms)}</text>;
      })}
    </g>
  );

  const limitResets = useMemo(() => limits.map((series) => limitSegments(series.points)), [limits]);

  const nearestPoint = (event: MouseEvent<SVGPolylineElement>, series: UsageSeries, segment: number[]): number => {
    const bounds = event.currentTarget.ownerSVGElement!.getBoundingClientRect();
    const chartX = ((event.clientX - bounds.left) / Math.max(1, bounds.width)) * WIDTH;
    return segment.reduce((nearest, index) => {
      const center = (i: number) => (x(series.points[i].bucketStartUtc) + x(series.points[i].bucketEndUtc)) / 2;
      return Math.abs(center(index) - chartX) < Math.abs(center(nearest) - chartX) ? index : nearest;
    }, segment[0]);
  };


  const limitsPanel = () => {
    const plotHeight = LIMITS_HEIGHT - PAD_TOP - PAD_BOTTOM;
    const y = (percent: number) => PAD_TOP + plotHeight - (Math.max(0, Math.min(100, percent)) / 100) * plotHeight;
    return (
      <section className="usage-chart-panel" aria-labelledby="usage-chart-limits-title">
        <h4 id="usage-chart-limits-title" title={t('Limit samples are grouped by the time they were observed. A gap means no sample; a reset starts a new line.')}>{t('Limit utilization (%)')}</h4>
        {limits.length === 0 ? (
          <p className="text-dim usage-chart-empty">{t('No limit samples in this range. This is not zero usage.')}</p>
        ) : (
          <svg className="usage-chart-svg usage-chart-limits" viewBox={`0 0 ${WIDTH} ${LIMITS_HEIGHT}`} role="group" aria-label={t('Limit utilization by account and window')}>
            <g className="usage-chart-grid" aria-hidden="true">
              {[0, 25, 50, 75, 100].map((tick) => (
                <g key={tick}>
                  <line x1={PAD_LEFT} x2={WIDTH - PAD_RIGHT} y1={y(tick)} y2={y(tick)} />
                  <text x={PAD_LEFT - 6} y={y(tick) + 4} textAnchor="end">{tick}%</text>
                </g>
              ))}
              <line className="usage-chart-threshold usage-chart-threshold-warning" x1={PAD_LEFT} x2={WIDTH - PAD_RIGHT} y1={y(USAGE_WARNING_PERCENT)} y2={y(USAGE_WARNING_PERCENT)} />
              <line className="usage-chart-threshold usage-chart-threshold-danger" x1={PAD_LEFT} x2={WIDTH - PAD_RIGHT} y1={y(USAGE_DANGER_PERCENT)} y2={y(USAGE_DANGER_PERCENT)} />
            </g>
            {selectedBand(LIMITS_HEIGHT)}
            {limits.map((series, s) => {
              const color = SERIES_COLORS[s % SERIES_COLORS.length];
              const estimated = seriesEstimated(series);
              const { segments, resets } = limitResets[s];
              return (
                <g key={`line-${s}`} className={`usage-chart-series${estimated ? ' usage-chart-series-estimated' : ''}`} data-series={s}>
                  {segments.map((segment, k) => (
                    <polyline
                      key={k}
                      className="usage-line-segment"
                      data-series={s}
                      points={segment.map((i) => {
                        const p = series.points[i];
                        return `${(x(p.bucketStartUtc) + x(p.bucketEndUtc)) / 2},${y(p.lastPercent as number)}`;
                      }).join(' ')}
                      stroke={color}
                      fill="none"
                      strokeDasharray={estimated ? '5 4' : undefined}
                      onMouseMove={(event) => setTooltip({ x: event.clientX, y: event.clientY, label: markLabel(series, nearestPoint(event, series, segment), 'limits', new Set(resets)) })}
                      onMouseLeave={() => setTooltip(null)}
                      onClick={(event) => choose(series, series.points[nearestPoint(event, series, segment)], 'limits')}
                    />
                  ))}
                  {resets.map((i) => {
                    const rx = x(series.points[i].bucketStartUtc);
                    return (
                      <g key={`reset-${i}`} className="usage-chart-reset" data-series={s} aria-hidden="true">
                        <line x1={rx} x2={rx} y1={PAD_TOP} y2={LIMITS_HEIGHT - PAD_BOTTOM} />
                        <text x={rx + 3} y={PAD_TOP + 10}>{t('Reset')}</text>
                      </g>
                    );
                  })}
                </g>
              );
            })}
            <g onKeyDown={onKeyDown('limits', limits)}>
              {limits.map((series, s) => {
                const color = SERIES_COLORS[s % SERIES_COLORS.length];
                const resets = new Set(limitResets[s].resets);
                return series.points.map((point, b) => {
                  const known = percentKnown(point);
                  const cx = (x(point.bucketStartUtc) + x(point.bucketEndUtc)) / 2;
                  const cy = known ? y(point.lastPercent as number) : LIMITS_HEIGHT - PAD_BOTTOM;
                  const label = markLabel(series, b, 'limits', resets);
                  return (
                    <g
                      key={`${s}-${b}`}
                      data-mark={`limits-${s}-${b}`}
                      className={`usage-chart-hit${known ? '' : ' usage-chart-hit-gap'}${point.estimated ? ' usage-chart-hit-estimated' : ''}`}
                      role="button"
                      tabIndex={tabIndexFor('limits', s, b, limits)}
                      aria-label={label}
                      aria-pressed={sameSelection(selected, series, point, 'limits')}
                      aria-describedby={tooltip?.label === label ? tooltipId : undefined}
                      onMouseMove={(event) => setTooltip({ x: event.clientX, y: event.clientY, label })}
                      onMouseLeave={() => setTooltip(null)}
                      onBlur={() => setTooltip(null)}
                      onFocus={(event) => {
                        setCursor((prev) => (prev.limits.s === s && prev.limits.b === b ? prev : { ...prev, limits: { s, b } }));
                        const bounds = event.currentTarget.getBoundingClientRect();
                        setTooltip({ x: bounds.left + bounds.width / 2, y: bounds.bottom, label });
                      }}
                      onClick={() => choose(series, point, 'limits')}
                    >
                      <circle className="usage-chart-point" cx={cx} cy={cy} r={known ? 1.25 : 1} stroke={color} fill={known && !point.estimated ? color : 'transparent'} />
                      <circle className="usage-chart-point-target" cx={cx} cy={cy} r={7} fill="transparent" stroke="none" />
                    </g>
                  );
                });
              })}
            </g>
            {xAxis(LIMITS_HEIGHT)}
          </svg>
        )}
        {limits.length > 0 && (
          <ul className="usage-chart-legend">
            {limits.map((series, s) => (
              <li key={`legend-${s}`}>
                <span className={`usage-chart-swatch${seriesEstimated(series) ? ' usage-chart-swatch-estimated' : ''}`} style={{ borderColor: SERIES_COLORS[s % SERIES_COLORS.length] }} aria-hidden="true" />
                {seriesName(series, 'limits')}
                {seriesEstimated(series) && <span className="usage-chart-flag">{t('Estimated')}</span>}
              </li>
            ))}
          </ul>
        )}
      </section>
    );
  };

  const tokensPanel = () => {
    const plotHeight = TOKENS_HEIGHT - PAD_TOP - PAD_BOTTOM;
    const stackTotals = new Map<string, number>();
    for (const series of tokens) {
      for (const point of series.points) stackTotals.set(point.bucketStartUtc, (stackTotals.get(point.bucketStartUtc) ?? 0) + point.totalTokens);
    }
    const max = Math.max(1, ...Array.from(stackTotals.values()));
    const offsets = new Map<string, number>();
    return (
      <section className="usage-chart-panel" aria-labelledby="usage-chart-tokens-title">
        <h4 id="usage-chart-tokens-title" title={t('Token records are grouped by the time the mission completed, not when the tokens were spent. A still-running mission is not counted yet.')}>{t('Tokens recorded at completion')}</h4>
        {tokens.length === 0 ? (
          <p className="text-dim usage-chart-empty">{t('No token records in this range.')}</p>
        ) : (
          <svg className="usage-chart-svg" viewBox={`0 0 ${WIDTH} ${TOKENS_HEIGHT}`} role="group" aria-label={t('Tokens recorded at completion by provider')}>
            <g className="usage-chart-grid" aria-hidden="true">
              <line x1={PAD_LEFT} x2={WIDTH - PAD_RIGHT} y1={PAD_TOP + plotHeight} y2={PAD_TOP + plotHeight} />
              <text x={PAD_LEFT - 6} y={PAD_TOP + 8} textAnchor="end">{tokenFormat.format(max)}</text>
              <text x={PAD_LEFT - 6} y={PAD_TOP + plotHeight} textAnchor="end">0</text>
            </g>
            {selectedBand(TOKENS_HEIGHT)}
            <g onKeyDown={onKeyDown('tokens', tokens)}>
              {tokens.map((series, s) => {
                const color = SERIES_COLORS[s % SERIES_COLORS.length];
                const none = new Set<number>();
                return series.points.map((point, b) => {
                  const below = offsets.get(point.bucketStartUtc) ?? 0;
                  offsets.set(point.bucketStartUtc, below + point.totalTokens);
                  const h = (point.totalTokens / max) * plotHeight;
                  const bx = x(point.bucketStartUtc);
                  const bw = Math.max(1, x(point.bucketEndUtc) - bx - 1);
                  const by = PAD_TOP + plotHeight - ((below + point.totalTokens) / max) * plotHeight;
                  const label = markLabel(series, b, 'tokens', none);
                  return (
                    <rect
                      key={`${s}-${b}`}
                      data-mark={`tokens-${s}-${b}`}
                      className={`usage-chart-bar${point.totalTokens === 0 ? ' usage-chart-bar-zero' : ''}${point.estimated ? ' usage-chart-bar-estimated' : ''}`}
                      x={bx}
                      y={point.totalTokens === 0 ? PAD_TOP + plotHeight - 3 : by}
                      width={bw}
                      height={point.totalTokens === 0 ? 3 : Math.max(1, h)}
                      fill={point.totalTokens === 0 ? 'transparent' : color}
                      role="button"
                      tabIndex={tabIndexFor('tokens', s, b, tokens)}
                      aria-label={label}
                      aria-pressed={sameSelection(selected, series, point, 'tokens')}
                      onFocus={() => setCursor((prev) => (prev.tokens.s === s && prev.tokens.b === b ? prev : { ...prev, tokens: { s, b } }))}
                      onClick={() => choose(series, point, 'tokens')}
                    >
                      <title>{label}</title>
                    </rect>
                  );
                });
              })}
            </g>
            {xAxis(TOKENS_HEIGHT)}
          </svg>
        )}
        {tokens.length > 0 && (
          <ul className="usage-chart-legend">
            {tokens.map((series, s) => (
              <li key={`legend-t-${s}`}>
                <span className="usage-chart-swatch usage-chart-swatch-bar" style={{ background: SERIES_COLORS[s % SERIES_COLORS.length] }} aria-hidden="true" />
                {seriesName(series, 'tokens')}
                {seriesEstimated(series) && <span className="usage-chart-flag">{t('Estimated')}</span>}
              </li>
            ))}
          </ul>
        )}
      </section>
    );
  };

  const tableRows: { kind: PanelKind; series: UsageSeries; index: number; resets: Set<number> }[] = [];
  if (show !== 'tokens') limits.forEach((series, s) => {
    const resets = new Set(limitResets[s].resets);
    series.points.forEach((_, index) => tableRows.push({ kind: 'limits', series, index, resets }));
  });
  if (show !== 'limits') tokens.forEach((series) => series.points.forEach((_, index) => tableRows.push({ kind: 'tokens', series, index, resets: new Set() })));

  return (
    <div className="usage-chart" ref={rootRef}>
      <p className="text-dim usage-chart-help">{t('Select a point, a bar or a row in the data table to list the missions completed in that bucket. Use the arrow keys to move between buckets and Enter to select.')}</p>
      {tooltip && createPortal(
        <div ref={tooltipRef} id={tooltipId} role="tooltip" className="usage-chart-tooltip" style={tooltipPosition}>{tooltip.label}</div>,
        document.body,
      )}
      {show !== 'tokens' && limitsPanel()}
      {show !== 'limits' && tokensPanel()}
      <button type="button" className="btn btn-sm usage-chart-table-toggle" aria-expanded={showTable} onClick={() => setShowTable((v) => !v)} title={t('Show every bucket of the chart as a table.')}>
        {showTable ? t('Hide chart data table') : t('Show chart data table')}
      </button>
      {showTable && (
        <div className="usage-chart-table-wrap">
          <table className="usage-chart-table">
            <caption>{t('Chart data: limits are grouped by observation time, tokens by completion time.')}</caption>
            <thead>
              <tr>
                <th scope="col">{t('Metric')}</th>
                <th scope="col">{t('Bucket')}</th>
                <th scope="col">{t('Series')}</th>
                <th scope="col">{t('Value')}</th>
                <th scope="col">{t('Flags')}</th>
                <th scope="col">{t('Action')}</th>
              </tr>
            </thead>
            <tbody>
              {tableRows.map(({ kind, series, index, resets }, row) => {
                const point = series.points[index];
                const range = t('{{start}} to {{end}}', { start: formatDateTime(point.bucketStartUtc), end: formatDateTime(point.bucketEndUtc) });
                const name = seriesName(series, kind);
                return (
                  <tr key={row} data-kind={kind}>
                    <td>{kind === 'limits' ? t('Limit utilization (%)') : t('Tokens recorded at completion')}</td>
                    <td>{range}</td>
                    <td>{name}</td>
                    <td>{valueText(point, kind)}</td>
                    <td>{flagText(series, index, kind, resets) || '-'}</td>
                    <td>
                      <button
                        type="button"
                        className="btn btn-sm"
                        disabled={!series.provider}
                        onClick={() => choose(series, point, kind)}
                        aria-label={t('Show missions for {{series}}, {{range}}', { series: name, range })}
                      >
                        {t('Select')}
                      </button>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
