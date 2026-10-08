import { useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { useSearchParams } from 'react-router-dom';
import { getTokenUsage } from '../api/client';
import type { TokenUsageSummaryResult } from '../types/models';
import { useLocale } from '../context/LocaleContext';
import { copySvgToClipboard } from '../lib/chartImage';
import { computeYTicks, formatTokens, TOKEN_USAGE_TIME_RANGES } from '../lib/tokenUsage';

const TIME_RANGES = TOKEN_USAGE_TIME_RANGES;

type TimeRangeValue = typeof TIME_RANGES[number]['value'];
type Metric = 'total' | 'byType';
type Shape = 'bars' | 'lines';
type CopyState = 'idle' | 'ok' | 'fail';

const MODEL_COLORS = ['var(--accent)', 'var(--green)', 'var(--red)', '#a855f7', '#06b6d4', '#ec4899', 'var(--text-dim)', '#14b8a6'];
const TYPE_COLORS = { input: 'var(--accent)', output: 'var(--green)', cached: 'var(--orange)' } as const;

interface SeriesDef { key: string; label: string; color: string }
interface TooltipRow { label: string; color: string; value: number }
interface TooltipData { x: number; y: number; title: string; rows: TooltipRow[]; total: number | null }

function formatBucketLabel(ts: number, stepMinutes: number, hours: number): string {
  const d = new Date(ts);
  if (stepMinutes <= 15) return d.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' });
  if (hours > 48) return d.toLocaleDateString(undefined, { month: 'short', day: 'numeric' }) + ' ' + d.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' });
  return d.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' });
}

function formatTooltipTime(ts: number): string {
  const d = new Date(ts);
  return d.toLocaleString(undefined, { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' });
}

function truncate(text: string, max: number): string {
  return text.length > max ? text.slice(0, max - 1) + '…' : text;
}

export default function TokenUsage() {
  const { t } = useLocale();
  // ?harborId= (from a Harbor's charts) limits the page to captains run on that Harbor.
  const [searchParams, setSearchParams] = useSearchParams();
  const harborId = searchParams.get('harborId') || '';
  const [timeRange, setTimeRange] = useState<TimeRangeValue>('day');
  const [metric, setMetric] = useState<Metric>('total');
  const [shape, setShape] = useState<Shape>('bars');
  const [data, setData] = useState<TokenUsageSummaryResult | null>(null);
  const [loading, setLoading] = useState(true);
  const [tooltip, setTooltip] = useState<TooltipData | null>(null);
  const [copiedTime, setCopiedTime] = useState<CopyState>('idle');
  const [copiedModel, setCopiedModel] = useState<CopyState>('idle');

  const timeChartRef = useRef<SVGSVGElement | null>(null);
  const modelChartRef = useRef<SVGSVGElement | null>(null);
  const tipRef = useRef<HTMLDivElement | null>(null);
  const [tipPos, setTipPos] = useState<{ left: number; top: number }>({ left: -9999, top: -9999 });

  const range = TIME_RANGES.find(r => r.value === timeRange)!;

  useLayoutEffect(() => {
    if (!tooltip || !tipRef.current) return;
    const rect = tipRef.current.getBoundingClientRect();
    const offset = 14;
    let left = tooltip.x + offset;
    let top = tooltip.y + offset;
    if (left + rect.width > window.innerWidth - 8) left = tooltip.x - rect.width - offset;
    if (top + rect.height > window.innerHeight - 8) top = tooltip.y - rect.height - offset;
    left = Math.min(Math.max(8, left), Math.max(8, window.innerWidth - rect.width - 8));
    top = Math.min(Math.max(8, top), Math.max(8, window.innerHeight - rect.height - 8));
    setTipPos({ left, top });
  }, [tooltip]);

  useEffect(() => {
    let cancelled = false;
    const end = new Date();
    const start = new Date(end.getTime() - range.hours * 3600000);
    setLoading(true);
    getTokenUsage({ fromUtc: start.toISOString(), toUtc: end.toISOString(), bucketMinutes: range.stepMinutes, harborId: harborId || undefined })
      .then((result) => { if (!cancelled) setData(result); })
      .catch(() => { if (!cancelled) setData(null); })
      .finally(() => { if (!cancelled) setLoading(false); });
    return () => { cancelled = true; };
  }, [range.hours, range.stepMinutes, timeRange, harborId]);

  const bucketTimestamps = useMemo<number[]>(
    () => (data?.buckets || []).map(b => new Date(b.bucketStartUtc).getTime()),
    [data],
  );

  const series = useMemo<SeriesDef[]>(() => {
    if (metric === 'byType') {
      return [
        { key: 'input', label: t('Input'), color: TYPE_COLORS.input },
        { key: 'output', label: t('Output'), color: TYPE_COLORS.output },
        { key: 'cached', label: t('Cached'), color: TYPE_COLORS.cached },
      ];
    }
    return (data?.byModel || []).map((m, i) => ({ key: m.model, label: m.model, color: MODEL_COLORS[i % MODEL_COLORS.length] }));
  }, [data, metric, t]);

  const bucketValues = useMemo<number[][]>(() => {
    const buckets = data?.buckets || [];
    if (metric === 'byType') return buckets.map(b => [b.inputTokens, b.outputTokens, b.cachedTokens]);
    return buckets.map(b => series.map(s => {
      const entry = b.models.find(m => m.model === s.key);
      return entry ? entry.totalTokens : 0;
    }));
  }, [data, metric, series]);

  const stacked = shape === 'bars';
  const maxVal = useMemo(() => {
    if (bucketValues.length === 0) return 1;
    if (stacked) return Math.max(1, ...bucketValues.map(row => row.reduce((a, b) => a + b, 0)));
    return Math.max(1, ...bucketValues.flat());
  }, [bucketValues, stacked]);

  const yTicks = computeYTicks(maxVal);
  const yMax = yTicks[yTicks.length - 1] || 1;

  // Time-series chart geometry (title band on top, X-axis title below tick labels, Y-axis title at left).
  const CH = 232;
  const padTop = 34, padBot = 52, padLeft = 64, padRight = 16;
  const plotH = CH - padTop - padBot;
  const plotW = 800 - padLeft - padRight;

  const byModel = data?.byModel || [];
  const maxModelTotal = Math.max(1, ...byModel.map(m => m.totalTokens));

  const totalInput = data?.inputTokens ?? 0;
  const totalOutput = data?.outputTokens ?? 0;
  const totalCached = data?.cachedTokens ?? 0;
  const totalTokens = data?.totalTokens ?? 0;

  const hasTimeData = bucketTimestamps.length > 0 && series.length > 0;
  const hasModelData = byModel.length > 0;

  const showTip = (clientX: number, clientY: number, title: string, rows: TooltipRow[], total: number | null) => {
    setTooltip({ x: clientX, y: clientY, title, rows: rows.filter(r => r.value > 0), total });
  };
  const hideTip = () => setTooltip(null);

  const copyChart = async (ref: React.RefObject<SVGSVGElement | null>, set: (s: CopyState) => void) => {
    const ok = await copySvgToClipboard(ref.current, { background: 'var(--bg-card)' });
    set(ok ? 'ok' : 'fail');
    window.setTimeout(() => set('idle'), 1600);
  };
  const copyLabel = (state: CopyState) => state === 'ok' ? t('Copied!') : state === 'fail' ? t('Copy failed') : t('Copy image');

  // Model chart geometry (title band on top).
  const titleBand = 24;
  const rowH = 22, modelPadBot = 6, labelW = 160, valueW = 66;
  const modelBarX = labelW + 8, modelBarW = 800 - (labelW + 8) - valueW - 8;
  const modelChartHeight = titleBand + Math.max(1, byModel.length) * rowH + modelPadBot;

  const clearHarbor = () => {
    const next = new URLSearchParams(searchParams);
    next.delete('harborId');
    setSearchParams(next);
  };

  return (
    <div className="token-usage-page">
      {harborId && (
        <div className="token-usage-filter-note">
          <span>{t('Showing captains run on Harbor {{id}}', { id: harborId })}</span>
          <button type="button" className="btn btn-sm" onClick={clearHarbor}>{t('Show all')}</button>
        </div>
      )}
      {/* Shared controls -- apply to both charts, live outside them. */}
      <div className="token-usage-controls">
        <div className="mission-history-time-tabs">
          {TIME_RANGES.map(r => (
            <button key={r.value} className={'mission-history-time-tab' + (timeRange === r.value ? ' active' : '')} onClick={() => setTimeRange(r.value)}>{t(r.label)}</button>
          ))}
        </div>
        <div className="mission-history-time-tabs">
          <button className={'mission-history-time-tab' + (metric === 'total' ? ' active' : '')} onClick={() => setMetric('total')}>{t('Total')}</button>
          <button className={'mission-history-time-tab' + (metric === 'byType' ? ' active' : '')} onClick={() => setMetric('byType')}>{t('By token type')}</button>
        </div>
        <div className="mission-history-time-tabs">
          <button className={'mission-history-time-tab' + (shape === 'bars' ? ' active' : '')} onClick={() => setShape('bars')}>{t('Stacked bars')}</button>
          <button className={'mission-history-time-tab' + (shape === 'lines' ? ' active' : '')} onClick={() => setShape('lines')}>{t('Lines')}</button>
        </div>
      </div>

      <div className="mission-history-stats token-usage-stats">
        <span><span className="mission-history-stat-value">{formatTokens(totalTokens)}</span> {t('Total')}</span>
        <span><span className="mission-history-stat-value" style={{ color: TYPE_COLORS.input }}>{formatTokens(totalInput)}</span> {t('Input')}</span>
        <span><span className="mission-history-stat-value" style={{ color: TYPE_COLORS.output }}>{formatTokens(totalOutput)}</span> {t('Output')}</span>
        <span><span className="mission-history-stat-value" style={{ color: TYPE_COLORS.cached }}>{formatTokens(totalCached)}</span> {t('Cached')}</span>
        {(data?.estimatedCount ?? 0) > 0 && (
          <span className="token-usage-estimated-note">{t('{{estimated}} of {{total}} records estimated', { estimated: data!.estimatedCount, total: data!.recordCount })}</span>
        )}
      </div>

      {/* Chart 1 -- Usage over time. Title + axis labels live inside the SVG so the copied image includes them. */}
      <div className="token-usage-chart-card">
        <div className="token-usage-chart-header">
          <button className={'token-usage-copy-btn' + (copiedTime === 'fail' ? ' failed' : '')} onClick={() => copyChart(timeChartRef, setCopiedTime)} disabled={!hasTimeData}>{copyLabel(copiedTime)}</button>
        </div>
        {loading ? (
          <div className="mission-history-empty">{t('Loading token usage...')}</div>
        ) : !hasTimeData ? (
          <div className="mission-history-empty">{t('No token usage for this time range')}</div>
        ) : (
          <>
            <svg ref={timeChartRef} width="100%" viewBox={`0 0 800 ${CH}`} preserveAspectRatio="xMidYMid meet" style={{ display: 'block' }}>
              <text x={12} y={18} fontSize="11" fontWeight="600" fill="var(--text)">{t('Usage over time')}</text>
              <text x={16} y={padTop + plotH / 2} fontSize="8" fill="var(--text-dim)" textAnchor="middle" transform={`rotate(-90 16 ${padTop + plotH / 2})`}>{t('Tokens')}</text>
              <text x={padLeft + plotW / 2} y={CH - 8} fontSize="8" fill="var(--text-dim)" textAnchor="middle">{t('Time')}</text>
              {yTicks.map(tick => {
                const y = padTop + plotH - (tick / yMax) * plotH;
                return (
                  <g key={tick}>
                    <line x1={padLeft} y1={y} x2={800 - padRight} y2={y} stroke="var(--border)" strokeDasharray={tick === 0 ? 'none' : '4,4'} strokeWidth={0.5} />
                    <text x={padLeft - 8} y={y + 3} textAnchor="end" fontSize="6.5" fill="var(--text-dim)">{formatTokens(tick)}</text>
                  </g>
                );
              })}
              {(() => {
                const groupW = plotW / bucketTimestamps.length;
                const barWidth = Math.max(1, Math.min(40, groupW * 0.7));
                const estLabelPx = range.hours > 48 ? 110 : 70;
                const labelInterval = Math.max(1, Math.ceil(bucketTimestamps.length / Math.max(1, Math.floor(plotW / estLabelPx))));
                const lines = !stacked ? series.map((s, si) => {
                  const pts = bucketValues.map((row, i) => `${(padLeft + i * groupW + groupW / 2).toFixed(2)},${(padTop + plotH - (row[si] / yMax) * plotH).toFixed(2)}`).join(' ');
                  return <polyline key={s.key} points={pts} fill="none" stroke={s.color} strokeWidth={1.3} strokeLinejoin="round" strokeLinecap="round" opacity={0.9} />;
                }) : null;
                return (
                  <>
                    {stacked && bucketValues.map((row, i) => {
                      const x = padLeft + i * groupW + (groupW - barWidth) / 2;
                      let cursor = padTop + plotH;
                      return (
                        <g key={i}>
                          {row.map((value, si) => {
                            if (value <= 0) return null;
                            const h = (value / yMax) * plotH;
                            cursor -= h;
                            return <rect key={series[si].key} x={x} y={cursor} width={barWidth} height={h} rx={1} fill={series[si].color} opacity={0.85} />;
                          })}
                        </g>
                      );
                    })}
                    {lines}
                    {bucketTimestamps.map((ts, i) => (
                      <g key={'hit' + i}
                        onMouseMove={(e) => showTip(e.clientX, e.clientY, formatTooltipTime(ts), series.map((s, si) => ({ label: s.label, color: s.color, value: bucketValues[i][si] })), bucketValues[i].reduce((a, b) => a + b, 0))}
                        onMouseLeave={hideTip}>
                        <rect x={padLeft + i * groupW} y={padTop} width={groupW} height={plotH + padBot - 16} fill="transparent" />
                        {i % labelInterval === 0 && (
                          <text x={padLeft + i * groupW + groupW / 2} y={padTop + plotH + 16} textAnchor="middle" fontSize="6.5" fill="var(--text-dim)">{formatBucketLabel(ts, range.stepMinutes, range.hours)}</text>
                        )}
                      </g>
                    ))}
                  </>
                );
              })()}
            </svg>
            <div className="mission-history-legend">
              {series.map(s => (<span key={s.key}><span className="mission-history-legend-color" style={{ backgroundColor: s.color }} /> {s.label}</span>))}
            </div>
          </>
        )}
      </div>

      {/* Chart 2 -- Usage by model. Title inside the SVG so the copied image includes it. */}
      <div className="token-usage-chart-card">
        <div className="token-usage-chart-header">
          <button className={'token-usage-copy-btn' + (copiedModel === 'fail' ? ' failed' : '')} onClick={() => copyChart(modelChartRef, setCopiedModel)} disabled={!hasModelData}>{copyLabel(copiedModel)}</button>
        </div>
        {loading ? (
          <div className="mission-history-empty">{t('Loading token usage...')}</div>
        ) : !hasModelData ? (
          <div className="mission-history-empty">{t('No token usage for this time range')}</div>
        ) : (
          <>
            <svg ref={modelChartRef} width="100%" viewBox={`0 0 800 ${modelChartHeight}`} preserveAspectRatio="xMidYMid meet" style={{ display: 'block' }}>
              <text x={12} y={16} fontSize="11" fontWeight="600" fill="var(--text)">{t('Usage by model')}</text>
              {byModel.map((m, i) => {
                const y = titleBand + i * rowH;
                const cy = y + rowH / 2;
                const segs = metric === 'byType'
                  ? [{ v: m.inputTokens, c: TYPE_COLORS.input }, { v: m.outputTokens, c: TYPE_COLORS.output }, { v: m.cachedTokens, c: TYPE_COLORS.cached }]
                  : [{ v: m.totalTokens, c: 'var(--accent)' }];
                let cursor = modelBarX;
                return (
                  <g key={m.model}
                    onMouseMove={(e) => showTip(e.clientX, e.clientY, m.model, [
                      { label: t('Input'), color: TYPE_COLORS.input, value: m.inputTokens },
                      { label: t('Output'), color: TYPE_COLORS.output, value: m.outputTokens },
                      { label: t('Cached'), color: TYPE_COLORS.cached, value: m.cachedTokens },
                    ], m.totalTokens)}
                    onMouseLeave={hideTip}>
                    <rect x={0} y={y} width={800} height={rowH} fill="transparent" />
                    <text x={labelW - 4} y={cy + 2.5} textAnchor="end" fontSize="7.5" fill="var(--text)">{truncate(m.model, 28)}</text>
                    {segs.map((seg, si) => {
                      if (seg.v <= 0) return null;
                      const w = (seg.v / maxModelTotal) * modelBarW;
                      const x = cursor;
                      cursor += w;
                      return <rect key={si} x={x} y={y + 4} width={Math.max(0.5, w)} height={rowH - 8} rx={2} fill={seg.c} opacity={0.9} />;
                    })}
                    <text x={800 - 4} y={cy + 2.5} textAnchor="end" fontSize="7.5" fill="var(--text-dim)">{formatTokens(m.totalTokens)}</text>
                  </g>
                );
              })}
            </svg>
            {metric === 'byType' && (
              <div className="mission-history-legend">
                <span><span className="mission-history-legend-color" style={{ backgroundColor: TYPE_COLORS.input }} /> {t('Input')}</span>
                <span><span className="mission-history-legend-color" style={{ backgroundColor: TYPE_COLORS.output }} /> {t('Output')}</span>
                <span><span className="mission-history-legend-color" style={{ backgroundColor: TYPE_COLORS.cached }} /> {t('Cached')}</span>
              </div>
            )}
          </>
        )}
      </div>

      {tooltip && createPortal(
        <div ref={tipRef} className="token-usage-tooltip" style={{ left: tipPos.left, top: tipPos.top }}>
          <div className="token-usage-tooltip-title">{tooltip.title}</div>
          {tooltip.rows.map(r => (<div key={r.label}><span style={{ color: r.color }}>{r.label}:</span> {formatTokens(r.value)}</div>))}
          {tooltip.total !== null && <div>{t('Total')}: {formatTokens(tooltip.total)}</div>}
        </div>,
        document.body,
      )}
    </div>
  );
}
