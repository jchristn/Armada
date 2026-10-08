import { useEffect, useRef, useState } from 'react';
import { computeYTicks, formatBucketLabel, formatTooltipTime } from '../../lib/missionHistory';

export interface BucketChartSeries {
  key: string;
  label: string;
  color: string;
  /** Draw this series dashed (lines only). */
  dashed?: boolean;
}

interface HarborBucketChartProps {
  /** Already-translated chart title, drawn inside the SVG. */
  title: string;
  /** Bucket start times in ms. */
  times: number[];
  /** One row per bucket, one value (or null for no data) per series. */
  rows: (number | null)[][];
  series: BucketChartSeries[];
  /** Stacked bars, or one line per series. */
  mode: 'stack' | 'line';
  stepMinutes: number;
  hours: number;
  /** Optional horizontal reference line (for example the Harbor's slot capacity). */
  reference?: { value: number; label: string; color: string };
  /** Format axis ticks and tooltip values. */
  formatValue?: (value: number) => string;
  /** Accessible summary of the chart. */
  ariaLabel: string;
}

const DEFAULT_WIDTH = 800;
const HEIGHT = 220;
const PAD_TOP = 26;
const PAD_BOTTOM = 30;
const PAD_LEFT = 54;
const PAD_RIGHT = 16;

/**
 * A time-bucketed chart in the style of the Mission History chart: stacked bars or lines over shared buckets, with an
 * optional reference line and a hover tooltip. Colors come from theme variables, so it follows light and dark mode.
 */
export default function HarborBucketChart({ title, times, rows, series, mode, stepMinutes, hours, reference, formatValue, ariaLabel }: HarborBucketChartProps) {
  const [hovered, setHovered] = useState<number | null>(null);
  // Draw at the container's real pixel width, so text and strokes keep the same size whether the chart spans the
  // page or half of it.
  const containerRef = useRef<HTMLDivElement>(null);
  const [WIDTH, setWidth] = useState(DEFAULT_WIDTH);
  useEffect(() => {
    const element = containerRef.current;
    if (!element || typeof ResizeObserver === 'undefined') return undefined;
    const observer = new ResizeObserver((entries) => {
      const measured = Math.round(entries[0]?.contentRect.width ?? 0);
      if (measured > 0) setWidth(measured);
    });
    observer.observe(element);
    return () => observer.disconnect();
  }, []);
  const fmt = formatValue ?? ((value: number) => String(Math.round(value * 100) / 100));

  const values = rows.map((row) => row.map((v) => v ?? 0));
  const dataMax = mode === 'stack'
    ? Math.max(0, ...values.map((row) => row.reduce((a, b) => a + b, 0)))
    : Math.max(0, ...values.flat());
  const yTicks = computeYTicks(Math.max(1, dataMax, reference?.value ?? 0));
  const yMax = yTicks[yTicks.length - 1] || 1;
  const plotH = HEIGHT - PAD_TOP - PAD_BOTTOM;
  const plotW = WIDTH - PAD_LEFT - PAD_RIGHT;
  const groupW = times.length > 0 ? plotW / times.length : plotW;
  const barW = Math.max(1, Math.min(40, groupW * 0.7));
  const labelInterval = Math.max(1, Math.ceil(times.length / Math.max(1, Math.floor(plotW / (hours > 48 ? 110 : 70)))));
  const yOf = (value: number) => PAD_TOP + plotH - (value / yMax) * plotH;
  const xMid = (i: number) => PAD_LEFT + i * groupW + groupW / 2;

  return (
    <div className="mission-history-chart-container harbor-metrics-chart" ref={containerRef}>
      <svg width="100%" height={HEIGHT} viewBox={`0 0 ${WIDTH} ${HEIGHT}`} preserveAspectRatio="none" style={{ display: 'block' }} role="img" aria-label={ariaLabel}>
        <text x={4} y={14} fontSize="12" fontWeight="600" fill="var(--text)">{title}</text>
        {yTicks.map((tick) => (
          <g key={tick}>
            <line x1={PAD_LEFT} y1={yOf(tick)} x2={WIDTH - PAD_RIGHT} y2={yOf(tick)} stroke="var(--border)" strokeDasharray={tick === 0 ? 'none' : '4,4'} strokeWidth={1} />
            <text x={PAD_LEFT - 8} y={yOf(tick) + 3} textAnchor="end" fontSize="11" fill="var(--text-dim)">{fmt(tick)}</text>
          </g>
        ))}
        {mode === 'stack' && values.map((row, i) => {
          let cursor = PAD_TOP + plotH;
          const x = PAD_LEFT + i * groupW + (groupW - barW) / 2;
          return (
            <g key={`bar-${i}`}>
              {row.map((value, si) => {
                if (value <= 0) return null;
                const h = (value / yMax) * plotH;
                cursor -= h;
                return <rect key={series[si].key} data-series={series[si].key} x={x} y={cursor} width={barW} height={h} rx={1.5} fill={series[si].color} opacity={hovered === i ? 1 : 0.85} />;
              })}
            </g>
          );
        })}
        {mode === 'line' && series.map((s, si) => {
          // Nulls break the line, so a gap in the data reads as a gap.
          const runs: Array<Array<[number, number]>> = [];
          let run: Array<[number, number]> = [];
          rows.forEach((row, i) => {
            const value = row[si];
            if (value === null || value === undefined) {
              if (run.length > 0) runs.push(run);
              run = [];
              return;
            }
            run.push([xMid(i), yOf(value)]);
          });
          if (run.length > 0) runs.push(run);
          return runs.map((points, ri) => (
            points.length === 1
              ? <circle key={`${s.key}-${ri}`} data-series={s.key} cx={points[0][0]} cy={points[0][1]} r={1.6} fill={s.color} />
              : <polyline key={`${s.key}-${ri}`} data-series={s.key} points={points.map(([x, y]) => `${x.toFixed(2)},${y.toFixed(2)}`).join(' ')} fill="none" stroke={s.color} strokeWidth={1.4} strokeDasharray={s.dashed ? '3,3' : undefined} strokeLinejoin="round" strokeLinecap="round" />
          ));
        })}
        {reference && (
          <g data-series="reference">
            <line x1={PAD_LEFT} y1={yOf(reference.value)} x2={WIDTH - PAD_RIGHT} y2={yOf(reference.value)} stroke={reference.color} strokeWidth={1} strokeDasharray="6,3" />
            <text x={WIDTH - PAD_RIGHT} y={yOf(reference.value) - 3} textAnchor="end" fontSize="11" fill={reference.color}>{reference.label}</text>
          </g>
        )}
        {times.map((ts, i) => (
          <g key={`hit-${i}`} onMouseEnter={() => setHovered(i)} onMouseLeave={() => setHovered(null)}>
            <rect x={PAD_LEFT + i * groupW} y={PAD_TOP} width={groupW} height={plotH + PAD_BOTTOM - 6} fill="transparent" />
            {i % labelInterval === 0 && (
              <text x={xMid(i)} y={HEIGHT - 10} textAnchor="middle" fontSize="11" fill="var(--text-dim)">{formatBucketLabel(ts, stepMinutes, hours)}</text>
            )}
          </g>
        ))}
      </svg>
      {hovered !== null && times[hovered] !== undefined && (
        <div className="mission-history-tooltip" style={{ left: `${((hovered + 0.5) / times.length) * 100}%` }}>
          <div style={{ fontWeight: 600, marginBottom: 4 }}>{formatTooltipTime(times[hovered])}</div>
          {series.map((s, si) => (
            <div key={s.key}><span style={{ color: s.color }}>{s.label}:</span> {rows[hovered][si] === null ? '-' : fmt(rows[hovered][si] as number)}</div>
          ))}
        </div>
      )}
      <div className="mission-history-legend harbor-metrics-legend">
        {series.map((s) => (
          <span key={s.key}><span className="mission-history-legend-color" style={{ backgroundColor: s.color }} /> {s.label}</span>
        ))}
        {reference && <span><span className="mission-history-legend-color" style={{ backgroundColor: reference.color }} /> {reference.label}</span>}
      </div>
    </div>
  );
}
