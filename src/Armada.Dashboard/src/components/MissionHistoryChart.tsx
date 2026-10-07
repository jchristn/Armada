import { useEffect, useMemo, useState } from 'react';
import { getMissionHistory } from '../api/client';
import type { MissionHistorySummaryResult, Vessel, Fleet } from '../types/models';
import { useLocale } from '../context/LocaleContext';
import {
  computeYTicks,
  formatBucketLabel,
  formatTooltipTime,
  historyBuckets,
  missionHistoryQuery,
  MISSION_HISTORY_RANGES as TIME_RANGES,
  type MissionHistoryBucketView as Bucket,
  type MissionHistoryRangeValue as TimeRangeValue,
} from '../lib/missionHistory';

interface MissionHistoryChartProps {
  vessels: Vessel[];
  fleets: Fleet[];
  onRefresh?: () => void;
}

export default function MissionHistoryChart({ vessels, fleets, onRefresh }: MissionHistoryChartProps) {
  const { t } = useLocale();
  const [timeRange, setTimeRange] = useState<TimeRangeValue>('week');
  const [fleetId, setFleetId] = useState('');
  const [vesselId, setVesselId] = useState('');
  const [hoveredBar, setHoveredBar] = useState<number | null>(null);
  const [history, setHistory] = useState<MissionHistorySummaryResult | null>(null);
  const [loading, setLoading] = useState(true);

  const filteredVessels = useMemo(() => {
    if (!fleetId) return vessels;
    return vessels.filter(v => v.fleetId === fleetId);
  }, [vessels, fleetId]);

  const range = TIME_RANGES.find(r => r.value === timeRange)!;

  useEffect(() => {
    if (vesselId && !filteredVessels.some(v => v.id === vesselId)) {
      setVesselId('');
    }
  }, [filteredVessels, vesselId]);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    getMissionHistory(missionHistoryQuery(range, fleetId, vesselId, new Date()))
      .then((result) => {
        if (!cancelled) setHistory(result);
      })
      .catch(() => {
        if (!cancelled) setHistory(null);
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });

    return () => {
      cancelled = true;
    };
  }, [fleetId, range.hours, range.stepMinutes, timeRange, vesselId]);

  const buckets = useMemo<Bucket[]>(() => historyBuckets(history), [history]);

  const totalComplete = history?.completeCount ?? 0;
  const totalFailed = history?.failedCount ?? 0;
  const totalOther = history?.otherCount ?? 0;
  const maxCount = Math.max(1, ...buckets.map(b => b.complete + b.failed + b.other));
  const yTicks = computeYTicks(maxCount);
  const yMax = yTicks[yTicks.length - 1] || 1;

  const chartHeight = 200;
  const padTop = 20, padBot = 40, padLeft = 50, padRight = 16;
  const barAreaHeight = chartHeight - padTop - padBot;
  const barAreaWidth = 800 - padLeft - padRight;

  const refresh = () => {
    onRefresh?.();
    setLoading(true);
    getMissionHistory(missionHistoryQuery(range, fleetId, vesselId, new Date()))
      .then(setHistory)
      .catch(() => setHistory(null))
      .finally(() => setLoading(false));
  };

  return (
    <div className="mission-history-section">
      <div className="mission-history-header">
        <span className="mission-history-title">{t('Mission History')}</span>
        <div className="mission-history-controls">
          <select aria-label={t('Filter by fleet')} value={fleetId} onChange={e => { setFleetId(e.target.value); setVesselId(''); }} title={t('Filter by fleet')}>
            <option value="">{t('All Fleets')}</option>
            {fleets.map(f => <option key={f.id} value={f.id}>{f.name}</option>)}
          </select>
          <select aria-label={t('Filter by vessel')} value={vesselId} onChange={e => setVesselId(e.target.value)} title={t('Filter by vessel')}>
            <option value="">{t('All Vessels')}</option>
            {filteredVessels.map(v => <option key={v.id} value={v.id}>{v.name}</option>)}
          </select>
          <div className="mission-history-time-tabs">
            {TIME_RANGES.map(r => (
              <button
                key={r.value}
                className={'mission-history-time-tab' + (timeRange === r.value ? ' active' : '')}
                onClick={() => setTimeRange(r.value)}
              >
                {t(r.label)}
              </button>
            ))}
          </div>
          <button className="mission-history-refresh-btn" onClick={refresh} title={t('Refresh')}>
            &#x21bb;
          </button>
        </div>
      </div>

      <div className="mission-history-stats">
        <span><span className="mission-history-stat-value">{history?.totalCount ?? 0}</span> {t('Total')}</span>
        <span><span className="mission-history-stat-value" style={{ color: 'var(--green)' }}>{totalComplete}</span> {t('Complete')}</span>
        <span><span className="mission-history-stat-value" style={{ color: 'var(--red)' }}>{totalFailed}</span> {t('Failed')}</span>
        {totalOther > 0 && <span><span className="mission-history-stat-value" style={{ color: 'var(--text-dim)' }}>{totalOther}</span> {t('Other')}</span>}
      </div>

      {loading ? (
        <div className="mission-history-empty">{t('Loading mission history...')}</div>
      ) : buckets.length === 0 ? (
        <div className="mission-history-empty">{t('No mission data for this time range')}</div>
      ) : (
        <div className="mission-history-chart-container">
          <svg width="100%" viewBox={`0 0 800 ${chartHeight}`} preserveAspectRatio="xMidYMid meet" style={{ display: 'block' }}>
            {yTicks.map(tick => {
              const y = padTop + barAreaHeight - (tick / yMax) * barAreaHeight;
              return (
                <g key={tick}>
                  <line x1={padLeft} y1={y} x2={800 - padRight} y2={y} stroke="var(--border)" strokeDasharray={tick === 0 ? 'none' : '4,4'} strokeWidth={0.5} />
                  <text x={padLeft - 8} y={y + 3} textAnchor="end" fontSize="6.5" fill="var(--text-dim)">{tick}</text>
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
                      <text x={padLeft + i * barGroupWidth + barGroupWidth / 2} y={chartHeight - 8} textAnchor="middle" fontSize="6.5" fill="var(--text-dim)">
                        {formatBucketLabel(bucket.timestampMs, range.stepMinutes, range.hours)}
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
              <div><span style={{ color: 'var(--green)' }}>{t('Complete')}:</span> {buckets[hoveredBar].complete}</div>
              <div><span style={{ color: 'var(--red)' }}>{t('Failed')}:</span> {buckets[hoveredBar].failed}</div>
              {buckets[hoveredBar].other > 0 && <div><span style={{ color: 'var(--text-dim)' }}>{t('Other')}:</span> {buckets[hoveredBar].other}</div>}
              <div>{t('Total')}: {buckets[hoveredBar].complete + buckets[hoveredBar].failed + buckets[hoveredBar].other}</div>
            </div>
          )}
        </div>
      )}

      <div className="mission-history-legend">
        <span><span className="mission-history-legend-color" style={{ backgroundColor: 'var(--green)' }} /> {t('Complete')}</span>
        <span><span className="mission-history-legend-color" style={{ backgroundColor: 'var(--red)' }} /> {t('Failed')}</span>
        <span><span className="mission-history-legend-color" style={{ backgroundColor: 'var(--text-dim)' }} /> {t('Other')}</span>
      </div>
    </div>
  );
}
