import { useState, useMemo } from 'react';
import type { Mission, Vessel, Fleet } from '../types/models';

const TIME_RANGES = [
  { label: 'Last Hour', value: 'hour', hours: 1, stepMs: 60000 },
  { label: 'Last Day', value: 'day', hours: 24, stepMs: 900000 },
  { label: 'Last Week', value: 'week', hours: 168, stepMs: 3600000 },
  { label: 'Last Month', value: 'month', hours: 720, stepMs: 21600000 },
] as const;

type TimeRangeValue = typeof TIME_RANGES[number]['value'];

interface Bucket {
  timestampMs: number;
  complete: number;
  failed: number;
  other: number;
}

interface MissionHistoryChartProps {
  missions: Mission[];
  vessels: Vessel[];
  fleets: Fleet[];
  onRefresh?: () => void;
}

function floorToStep(ts: number, stepMs: number): number {
  return Math.floor(ts / stepMs) * stepMs;
}

function generateAllBuckets(startMs: number, endMs: number, stepMs: number): Bucket[] {
  const buckets: Bucket[] = [];
  const flooredStart = floorToStep(startMs, stepMs);
  for (let t = flooredStart; t < endMs; t += stepMs) {
    buckets.push({ timestampMs: t, complete: 0, failed: 0, other: 0 });
  }
  return buckets;
}

function computeYTicks(max: number): number[] {
  if (max <= 0) return [0];
  const step = Math.max(1, Math.ceil(max / 4));
  const ticks: number[] = [];
  for (let i = 0; i <= max; i += step) ticks.push(i);
  if (ticks[ticks.length - 1] < max) ticks.push(ticks[ticks.length - 1] + step);
  return ticks;
}

function formatBucketLabel(ts: number, stepMs: number, hours: number): string {
  const d = new Date(ts);
  if (stepMs <= 900000) return d.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' });
  if (hours > 48) return d.toLocaleDateString(undefined, { month: 'short', day: 'numeric' }) + ' ' + d.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' });
  return d.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' });
}

function formatTooltipTime(ts: number): string {
  const d = new Date(ts);
  return d.toLocaleString(undefined, { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' });
}

export default function MissionHistoryChart({ missions, vessels, fleets, onRefresh }: MissionHistoryChartProps) {
  const [timeRange, setTimeRange] = useState<TimeRangeValue>('week');
  const [fleetId, setFleetId] = useState('');
  const [vesselId, setVesselId] = useState('');
  const [hoveredBar, setHoveredBar] = useState<number | null>(null);

  const filteredVessels = useMemo(() => {
    if (!fleetId) return vessels;
    return vessels.filter(v => v.fleetId === fleetId);
  }, [vessels, fleetId]);

  const range = TIME_RANGES.find(r => r.value === timeRange)!;

  const { buckets, totalComplete, totalFailed, totalOther } = useMemo(() => {
    const endMs = Date.now();
    const startMs = endMs - range.hours * 3600000;
    const allBuckets = generateAllBuckets(startMs, endMs, range.stepMs);

    const vesselIdsInFleet = fleetId ? new Set(filteredVessels.map(v => v.id)) : null;

    let tc = 0, tf = 0, to = 0;
    for (const m of missions) {
      const mTime = new Date(m.createdUtc).getTime();
      if (mTime < startMs || mTime >= endMs) continue;
      if (vesselId && m.vesselId !== vesselId) continue;
      if (vesselIdsInFleet && !vesselIdsInFleet.has(m.vesselId ?? '')) continue;

      const bucketKey = floorToStep(mTime, range.stepMs);
      const bucket = allBuckets.find(b => b.timestampMs === bucketKey);
      if (!bucket) continue;

      if (m.status === 'Complete') { bucket.complete++; tc++; }
      else if (m.status === 'Failed' || m.status === 'LandingFailed') { bucket.failed++; tf++; }
      else { bucket.other++; to++; }
    }

    return { buckets: allBuckets, totalComplete: tc, totalFailed: tf, totalOther: to };
  }, [missions, timeRange, fleetId, vesselId, filteredVessels, range]);

  const maxCount = Math.max(1, ...buckets.map(b => b.complete + b.failed + b.other));
  const yTicks = computeYTicks(maxCount);
  const yMax = yTicks[yTicks.length - 1] || 1;

  const chartHeight = 200;
  const padTop = 20, padBot = 40, padLeft = 50, padRight = 16;
  const barAreaHeight = chartHeight - padTop - padBot;
  const barAreaWidth = 800 - padLeft - padRight;

  return (
    <div className="mission-history-section">
      <div className="mission-history-header">
        <span className="mission-history-title">Mission History</span>
        <div className="mission-history-controls">
          <select value={fleetId} onChange={e => { setFleetId(e.target.value); setVesselId(''); }} title="Filter by fleet">
            <option value="">All Fleets</option>
            {fleets.map(f => <option key={f.id} value={f.id}>{f.name}</option>)}
          </select>
          <select value={vesselId} onChange={e => setVesselId(e.target.value)} title="Filter by vessel">
            <option value="">All Vessels</option>
            {filteredVessels.map(v => <option key={v.id} value={v.id}>{v.name}</option>)}
          </select>
          <div className="mission-history-time-tabs">
            {TIME_RANGES.map(r => (
              <button
                key={r.value}
                className={'mission-history-time-tab' + (timeRange === r.value ? ' active' : '')}
                onClick={() => setTimeRange(r.value)}
              >
                {r.label}
              </button>
            ))}
          </div>
          {onRefresh && (
            <button className="mission-history-refresh-btn" onClick={onRefresh} title="Refresh">
              &#x21bb;
            </button>
          )}
        </div>
      </div>

      <div className="mission-history-stats">
        <span><span className="mission-history-stat-value">{totalComplete + totalFailed + totalOther}</span> Total</span>
        <span><span className="mission-history-stat-value" style={{ color: 'var(--green)' }}>{totalComplete}</span> Complete</span>
        <span><span className="mission-history-stat-value" style={{ color: 'var(--red)' }}>{totalFailed}</span> Failed</span>
        {totalOther > 0 && <span><span className="mission-history-stat-value" style={{ color: 'var(--text-dim)' }}>{totalOther}</span> Other</span>}
      </div>

      {buckets.length === 0 ? (
        <div className="mission-history-empty">No mission data for this time range</div>
      ) : (
        <div className="mission-history-chart-container">
          <svg width="100%" viewBox={`0 0 800 ${chartHeight}`} preserveAspectRatio="xMidYMid meet" style={{ display: 'block' }}>
            {yTicks.map(tick => {
              const y = padTop + barAreaHeight - (tick / yMax) * barAreaHeight;
              return (
                <g key={tick}>
                  <line x1={padLeft} y1={y} x2={800 - padRight} y2={y} stroke="var(--border)" strokeDasharray={tick === 0 ? 'none' : '4,4'} strokeWidth={0.5} />
                  <text x={padLeft - 8} y={y + 3} textAnchor="end" fontSize="9" fill="var(--text-dim)">{tick}</text>
                </g>
              );
            })}
            {(() => {
              const barGroupWidth = barAreaWidth / buckets.length;
              const barWidth = Math.max(2, Math.min(40, barGroupWidth * 0.7));
              const isLongLabel = range.hours > 48;
              const estLabelPx = isLongLabel ? 110 : 70;
              const maxLabels = Math.max(1, Math.floor(barAreaWidth / estLabelPx));
              const labelInterval = Math.max(1, Math.ceil(buckets.length / maxLabels));

              return buckets.map((bucket, i) => {
                const total = bucket.complete + bucket.failed + bucket.other;
                const completeH = (bucket.complete / yMax) * barAreaHeight;
                const failedH = (bucket.failed / yMax) * barAreaHeight;
                const otherH = (bucket.other / yMax) * barAreaHeight;
                const x = padLeft + i * barGroupWidth + (barGroupWidth - barWidth) / 2;
                const completeY = padTop + barAreaHeight - completeH - failedH - otherH;
                const failedY = completeY + completeH;
                const otherY = failedY + failedH;
                const showLabel = i % labelInterval === 0;
                const isHovered = hoveredBar === i;

                return (
                  <g key={i} onMouseEnter={() => setHoveredBar(i)} onMouseLeave={() => setHoveredBar(null)} style={{ cursor: 'default' }}>
                    <rect x={padLeft + i * barGroupWidth} y={padTop} width={barGroupWidth} height={barAreaHeight + padBot} fill="transparent" />
                    {bucket.complete > 0 && <rect x={x} y={completeY} width={barWidth} height={completeH} rx={2} fill="var(--green)" opacity={isHovered ? 1 : 0.85} />}
                    {bucket.failed > 0 && <rect x={x} y={failedY} width={barWidth} height={failedH} rx={2} fill="var(--red)" opacity={isHovered ? 1 : 0.85} />}
                    {bucket.other > 0 && <rect x={x} y={otherY} width={barWidth} height={otherH} rx={2} fill="var(--text-dim)" opacity={isHovered ? 0.7 : 0.5} />}
                    {showLabel && (
                      <text x={padLeft + i * barGroupWidth + barGroupWidth / 2} y={chartHeight - 8} textAnchor="middle" fontSize="8" fill="var(--text-dim)">
                        {formatBucketLabel(bucket.timestampMs, range.stepMs, range.hours)}
                      </text>
                    )}
                  </g>
                );
              });
            })()}
          </svg>
          {hoveredBar !== null && buckets[hoveredBar] && (
            <div className="mission-history-tooltip" style={{ left: `${((hoveredBar + 0.5) / buckets.length) * 100}%` }}>
              <div style={{ fontWeight: 600, marginBottom: 4 }}>{formatTooltipTime(buckets[hoveredBar].timestampMs)}</div>
              <div><span style={{ color: 'var(--green)' }}>Complete:</span> {buckets[hoveredBar].complete}</div>
              <div><span style={{ color: 'var(--red)' }}>Failed:</span> {buckets[hoveredBar].failed}</div>
              {buckets[hoveredBar].other > 0 && <div><span style={{ color: 'var(--text-dim)' }}>Other:</span> {buckets[hoveredBar].other}</div>}
              <div>Total: {buckets[hoveredBar].complete + buckets[hoveredBar].failed + buckets[hoveredBar].other}</div>
            </div>
          )}
        </div>
      )}

      <div className="mission-history-legend">
        <span><span className="mission-history-legend-color" style={{ backgroundColor: 'var(--green)' }} /> Complete</span>
        <span><span className="mission-history-legend-color" style={{ backgroundColor: 'var(--red)' }} /> Failed</span>
        <span><span className="mission-history-legend-color" style={{ backgroundColor: 'var(--text-dim)' }} /> Other</span>
      </div>
    </div>
  );
}
