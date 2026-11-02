import '../vessels/history/vesselHistory.css';
import { forwardRef, useCallback, useEffect, useImperativeHandle, useLayoutEffect, useMemo, useRef, useState, type CSSProperties, type KeyboardEvent } from 'react';
import RefreshButton from '../shared/RefreshButton';
import { getTokenUsage } from '../../api/client';
import type { TokenUsageSummaryResult } from '../../types/models';
import { useLocale } from '../../context/LocaleContext';
import { buildTokenHeatGrid, heatLevel, tokenHeatGridStats, tokenHeatmapQuery, TOKEN_HEATMAP_METRICS, type TokenHeatCell, type TokenHeatmapMetric, type TokenHeatmapSpec } from '../../lib/tokenHeatmap';
import { buildMonthLabels, formatIsoDay } from '../../lib/vesselHistory';
import { formatTokens } from '../../lib/tokenUsage';

interface TokenActivityHeatmapProps {
  harborId?: string;
  captainId?: string;
  model?: string;
  /** The window to show, from the page's time range. Without it the heatmap offers its own 1/3/12 month range. */
  spec?: TokenHeatmapSpec;
}

export interface TokenActivityHeatmapHandle { refresh: () => Promise<void> }

const LABEL_WIDTH = 56;
const GAP = 3;

/** Token activity heatmap: UTC days, hours, or minutes, colored by the chosen token metric relative to the peak. */
const TokenActivityHeatmap = forwardRef<TokenActivityHeatmapHandle, TokenActivityHeatmapProps>(function TokenActivityHeatmap({ harborId = '', captainId = '', model = '', spec }, ref) {
  const { t, locale } = useLocale();
  const [ownDays, setOwnDays] = useState(365);
  const [metric, setMetric] = useState<TokenHeatmapMetric>('total');
  const [data, setData] = useState<TokenUsageSummaryResult | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(false);
  const [selected, setSelected] = useState('');
  const requestVersion = useRef(0);
  const [now, setNow] = useState(() => new Date());
  const [cellHeight, setCellHeight] = useState(14);
  const grid = useRef<HTMLDivElement>(null);
  const scroll = useRef<HTMLDivElement>(null);
  const specKind = spec?.kind ?? 'calendar';
  const specSize = !spec ? ownDays : spec.kind === 'calendar' ? spec.days : spec.kind === 'hours' ? spec.hours : spec.minutes;
  const active = useMemo<TokenHeatmapSpec>(
    () => (specKind === 'calendar' ? { kind: 'calendar', days: specSize } : specKind === 'hours' ? { kind: 'hours', hours: specSize } : { kind: 'minutes', minutes: specSize }),
    [specKind, specSize],
  );
  const load = useCallback(async () => {
    const version = ++requestVersion.current;
    const end = new Date();
    const query = tokenHeatmapQuery(active, end);
    setNow(end);
    setLoading(true); setError(false);
    try {
      const value = await getTokenUsage({ fromUtc: query.fromUtc, toUtc: query.toUtc, bucketMinutes: query.bucketMinutes, harborId: harborId || undefined, captainId: captainId || undefined, model: model || undefined });
      if (version === requestVersion.current) setData(value);
    } catch (error: unknown) {
      if (version === requestVersion.current) setError(true);
      throw error;
    } finally { if (version === requestVersion.current) setLoading(false); }
  }, [active, harborId, captainId, model]);
  useImperativeHandle(ref, () => ({ refresh: load }), [load]);
  useEffect(() => {
    void load().catch(() => {});
    return () => { requestVersion.current++; };
  }, [load]);

  const heat = useMemo(() => buildTokenHeatGrid(data, active, metric, now), [data, active, metric, now]);
  const stats = tokenHeatGridStats(heat);
  const cells = useMemo(() => heat.rows.flatMap(r => r.cells).filter((c): c is TokenHeatCell => c !== null), [heat]);
  const lastCell = cells.length ? cells.reduce((a, b) => (a.startUtc > b.startUtc ? a : b)) : null;
  const selectedCell = cells.find(c => c.key === selected) ?? lastCell;
  const calendar = active.kind === 'calendar';
  const showDayNumbers = calendar && active.days <= 31;

  // Cells fill the card's width: the column width sets the cell height, between a legible minimum and a cap.
  useLayoutEffect(() => {
    const host = scroll.current;
    if (!host) return undefined;
    const measure = () => {
      const columnWidth = (host.clientWidth - LABEL_WIDTH - GAP * heat.columns) / Math.max(1, heat.columns);
      const cap = calendar ? (heat.columns <= 6 ? 44 : 30) : 26;
      setCellHeight(Math.round(Math.max(12, Math.min(cap, columnWidth))));
    };
    measure();
    if (typeof ResizeObserver === 'undefined') return undefined;
    const observer = new ResizeObserver(measure);
    observer.observe(host);
    return () => observer.disconnect();
  }, [heat.columns, calendar, loading]);

  useEffect(() => {
    const host = scroll.current;
    const cell = selectedCell ? grid.current?.querySelector<HTMLElement>(`[data-key="${selectedCell.key}"]`) : null;
    if (!loading && host && cell) {
      const x = cell.getBoundingClientRect().left - host.getBoundingClientRect().left + host.scrollLeft;
      if (x < host.scrollLeft) host.scrollLeft = x;
      if (x + 20 > host.scrollLeft + host.clientWidth) host.scrollLeft = x + 20 - host.clientWidth;
    }
  }, [selectedCell, loading]);

  const timeOf = (iso: string, withMinutes: boolean) => `${iso.slice(11, 13)}:${withMinutes ? iso.slice(14, 16) : '00'}`;
  const cellWhen = (cell: TokenHeatCell) => {
    const date = cell.startUtc.slice(0, 10);
    if (active.kind === 'calendar') return `${formatIsoDay(locale, date, { dateStyle: 'full' })} (UTC)`;
    return `${formatIsoDay(locale, date, { dateStyle: 'medium' })} ${timeOf(cell.startUtc, active.kind === 'minutes')} UTC`;
  };
  const describe = (cell: TokenHeatCell) => `${cellWhen(cell)}: ${cell.count.toLocaleString(locale)} ${t('Tokens')}`;
  const rowLabel = (rowKey: string, index: number) => {
    if (active.kind === 'calendar') return formatIsoDay(locale, `2023-01-0${index + 1}`, { weekday: 'short' });
    if (active.kind === 'hours') return formatIsoDay(locale, rowKey.slice(0, 10), { month: 'short', day: 'numeric' });
    return timeOf(rowKey, false);
  };

  function focusAt(row: number, column: number) {
    const cell = heat.rows[row]?.cells[column];
    if (!cell) return;
    setSelected(cell.key);
    grid.current?.querySelector<HTMLElement>(`[data-key="${cell.key}"]`)?.focus();
  }
  function keyDown(e: KeyboardEvent<HTMLDivElement>) {
    const target = e.target as HTMLElement;
    const row = Number(target.dataset.row);
    const column = Number(target.dataset.column);
    if (Number.isNaN(row) || Number.isNaN(column)) return;
    const moves: Record<string, [number, number]> = { ArrowLeft: [0, -1], ArrowRight: [0, 1], ArrowUp: [-1, 0], ArrowDown: [1, 0] };
    if (e.key in moves) { e.preventDefault(); focusAt(row + moves[e.key][0], column + moves[e.key][1]); }
    if ((e.key === 'Home' || e.key === 'End') && cells.length) {
      e.preventDefault();
      const target2 = e.key === 'Home' ? cells.reduce((a, b) => (a.startUtc < b.startUtc ? a : b)) : lastCell!;
      setSelected(target2.key);
      grid.current?.querySelector<HTMLElement>(`[data-key="${target2.key}"]`)?.focus();
    }
  }

  const subtitle = active.kind === 'calendar' ? t('Daily token totals (UTC)') : active.kind === 'hours' ? t('Hourly token totals (UTC)') : t('Token totals per minute (UTC)');
  const peakLabel = active.kind === 'calendar' ? t('Peak day') : active.kind === 'hours' ? t('Peak hour') : t('Peak minute');
  const activeLabel = active.kind === 'calendar' ? t('Active days') : active.kind === 'hours' ? t('Active hours') : t('Active minutes');
  const gridStyle = { '--tok-heat-cols': heat.columns, '--tok-heat-h': `${cellHeight}px`, '--tok-heat-label': `${LABEL_WIDTH}px`, '--tok-heat-gap': `${GAP}px` } as CSSProperties;
  const columnLabels: Array<{ column: number; label: string; span: number }> = calendar
    ? buildMonthLabels(heat.weeks).map(m => ({ column: m.column, span: 3, label: formatIsoDay(locale, `${m.year}-${String(m.month).padStart(2, '0')}-01`, { month: 'short' }) }))
    : Array.from({ length: heat.columns }, (_, c) => c).filter(c => c % (active.kind === 'hours' ? 3 : 10) === 0)
      .map(c => ({ column: c, span: active.kind === 'hours' ? 3 : 10, label: active.kind === 'hours' ? String(c).padStart(2, '0') : `:${String(c).padStart(2, '0')}` }));

  return <section className="token-usage-chart-card token-activity-card vhist-heatmap" aria-label={t('Token activity')}>
    <div className="token-activity-header"><div><h3 title={t('Each square is one UTC day, hour, or minute; darker squares mean more tokens. Click or use arrow keys to read a square\'s total.')}>{t('Token activity')}</h3><p className="text-dim">{subtitle}</p></div>
      <div className="token-activity-controls">
        {!spec && <label title={t('How many days of history the heatmap covers, ending today (UTC).')}>{t('Heatmap range')}<select title={t('How many days of history the heatmap covers, ending today (UTC).')} value={ownDays} onChange={e => setOwnDays(Number(e.target.value))}>
          <option value={31}>{t('Last Month')}</option><option value={90}>{t('Last 3 months')}</option><option value={365}>{t('Last 12 months')}</option>
        </select></label>}
        <label title={t('Which tokens each square\'s color counts: total, input (prompt), output (completion), or cached (served from the prompt cache).')}>{t('Heatmap metric')}<select title={t('Which tokens each square\'s color counts: total, input (prompt), output (completion), or cached (served from the prompt cache).')} value={metric} onChange={e => setMetric(e.target.value as TokenHeatmapMetric)}>
          {TOKEN_HEATMAP_METRICS.map(m => <option key={m} value={m}>{t(m[0].toUpperCase() + m.slice(1))}</option>)}
        </select></label>
        <RefreshButton onRefresh={load} title="Refresh token activity" />
      </div>
    </div>
    {loading ? <p role="status">{t('Loading token usage...')}</p> : error ? <p role="alert">{t('Unable to load token activity.')}</p> : <>
      <div className="token-activity-stats"><span title={t('Sum of the chosen metric over the heatmap window.')}>{t('Total')}: <strong>{formatTokens(stats.total)}</strong></span><span title={t('Highest single-square count in the window. It sets the darkest color.')}>{peakLabel}: <strong>{formatTokens(stats.peak)}</strong></span><span title={t('Squares in the window with any token usage.')}>{activeLabel}: <strong>{stats.active}</strong></span></div>
      <div ref={scroll} className="vhist-heatmap-scroll token-activity-scroll">
        <div className="tok-heat-columns" style={gridStyle} aria-hidden="true">
          <span className="tok-heat-corner" />{columnLabels.map(l => <span className="tok-heat-column-label" key={l.column} style={{ gridColumn: `${l.column + 2} / span ${Math.min(l.span, heat.columns - l.column)}` }}>{l.label}</span>)}
        </div>
        <div ref={grid} className={`tok-heat-grid is-${active.kind}`} style={gridStyle} role="grid" aria-label={t('Token activity')} onKeyDown={keyDown}>
          {heat.rows.map((row, r) => <div role="row" className="vhist-heatmap-row" key={row.key}>
            <span role="rowheader" className="tok-heat-row-label">{rowLabel(row.key, r)}</span>
            {row.cells.map((cell, c) => {
              if (!cell) return <span aria-hidden="true" className="vhist-heat-cell tok-heat-cell vhist-heat-pad" key={c} />;
              const level = heatLevel(cell.count, stats.peak);
              const isSelected = cell.key === selectedCell?.key;
              return <button type="button" role="gridcell" key={cell.key} data-key={cell.key} data-date={calendar ? cell.key : undefined} data-row={r} data-column={c} data-level={level} title={describe(cell)} aria-label={describe(cell)} aria-selected={isSelected} tabIndex={isSelected ? 0 : -1} className={`vhist-heat-cell tok-heat-cell level-${level}${isSelected ? ' selected' : ''}`} onFocus={() => setSelected(cell.key)} onClick={() => setSelected(cell.key)}>
                {showDayNumbers && <span className="tok-heat-day" aria-hidden="true">{Number(cell.key.slice(8, 10))}</span>}
              </button>;
            })}
          </div>)}
        </div>
      </div>
      <div className="token-activity-footer"><span aria-live="polite">{selectedCell ? describe(selectedCell) : ''}</span><span className="token-activity-legend" title={t('Color scale: lighter squares had fewer tokens, darker squares had more, relative to the peak.')}>{t('Less')}{[0, 1, 2, 3, 4].map(level => <span key={level} className={`vhist-heat-cell level-${level}`} />)}{t('More')}</span></div>
      {stats.active === 0 && <p className="text-dim">{t('No token usage for this time range')}</p>}
      {(data?.syntheticCount ?? 0) > 0 && <p className="text-dim" title={t('Simulated usage an administrator imported, for example for screenshots. It is counted in these totals but is not real model usage.')}>{t('{{count}} synthetic usage records included', { count: data!.syntheticCount })}</p>}
    </>}
  </section>;
});

export default TokenActivityHeatmap;
