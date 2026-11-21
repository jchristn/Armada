import { useMemo, useState } from 'react';
import { createPortal } from 'react-dom';
import type { RequestHistorySummaryBucket, RequestHistorySummaryResult } from '../../types/models';
import { normalizeSummaryBuckets, type ActivityRangeId } from '../../lib/requestHistory';

/*
 * The API request chart: requests per time bucket over a range, split into successes and failures, with a tooltip per
 * bucket. Shown on Request History and on the Overview.
 */

interface HistoryChartTooltipState {
  bucket: RequestHistorySummaryBucket;
  clientX: number;
  clientY: number;
}

function formatChartLabel(value: string, rangeId: ActivityRangeId) {
  const date = new Date(value);
  if (rangeId === 'lastHour' || rangeId === 'lastDay') {
    return date.toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' });
  }
  if (rangeId === 'lastWeek') {
    return date.toLocaleString([], { weekday: 'short', hour: 'numeric' });
  }
  return date.toLocaleString([], { month: 'short', day: 'numeric' });
}

function getTooltipPosition(clientX: number, clientY: number) {
  const tooltipWidth = 280;
  const tooltipHeight = 136;
  const offset = 14;
  const viewportWidth = window.innerWidth;
  const viewportHeight = window.innerHeight;
  let left = clientX + offset;
  let top = clientY + offset;
  if (left + tooltipWidth > viewportWidth - 12) left = clientX - tooltipWidth - offset;
  if (top + tooltipHeight > viewportHeight - 12) top = clientY - tooltipHeight - offset;
  return {
    left: Math.max(12, left),
    top: Math.max(12, top),
  };
}

export default function RequestHistoryChart({
  summary,
  rangeId,
  t,
  formatDateTime,
}: {
  summary: RequestHistorySummaryResult | null;
  rangeId: ActivityRangeId;
  t: (text: string, params?: Record<string, string | number | null | undefined>) => string;
  formatDateTime: (utc: string | null | undefined) => string;
}) {
  const [tooltip, setTooltip] = useState<HistoryChartTooltipState | null>(null);
  const buckets = normalizeSummaryBuckets(summary, rangeId);
  const windowTotal = buckets.reduce((sum, bucket) => sum + bucket.totalCount, 0);
  const maxCount = Math.max(...buckets.map((bucket) => bucket.totalCount), 1);
  const labels = useMemo(() => {
    if (buckets.length === 0) return [];
    const labelCount = Math.min(8, buckets.length);
    const indices = new Set<number>();
    if (labelCount === 1) {
      indices.add(0);
    } else {
      for (let index = 0; index < labelCount; index += 1) {
        indices.add(Math.round((index * (buckets.length - 1)) / (labelCount - 1)));
      }
    }

    return Array.from(indices)
      .sort((left, right) => left - right)
      .map((index, position, values) => ({
        key: buckets[index].bucketStartUtc,
        label: formatChartLabel(buckets[index].bucketStartUtc, rangeId),
        left: values.length === 1 ? 0 : (position / (values.length - 1)) * 100,
        align: position === 0 ? 'start' : position === values.length - 1 ? 'end' : 'center',
      }));
  }, [buckets, rangeId]);

  if (windowTotal === 0) {
    return (
      <div className="request-history-empty">
        {t('No requests in this time range. Widen the range above to see older traffic.')}
      </div>
    );
  }

  return (
    <>
      <div className="request-chart-shell">
        <div className="request-chart-axis-label">{t('Requests')}</div>
        <div className="request-chart-main">
          <div className="request-chart-axis-values">
            {[maxCount, Math.round(maxCount / 2), 0]
              .filter((value, index, values) => values.indexOf(value) === index)
              .map((value) => (
                <span key={value}>{value}</span>
              ))}
          </div>
          <div className="request-chart-plot">
            {buckets.map((bucket) => {
              const totalHeight = `${Math.max((bucket.totalCount / maxCount) * 100, bucket.totalCount > 0 ? 6 : 2)}%`;
              const successHeight = bucket.totalCount > 0 ? `${(bucket.successCount / bucket.totalCount) * 100}%` : '0%';
              const failureHeight = bucket.totalCount > 0 ? `${(bucket.failureCount / bucket.totalCount) * 100}%` : '0%';
              return (
                <div
                  key={bucket.bucketStartUtc}
                  className={`request-chart-column${tooltip?.bucket.bucketStartUtc === bucket.bucketStartUtc ? ' is-hovered' : ''}`}
                  onMouseEnter={(event) => setTooltip({ bucket, clientX: event.clientX, clientY: event.clientY })}
                  onMouseMove={(event) => setTooltip({ bucket, clientX: event.clientX, clientY: event.clientY })}
                  onMouseLeave={() => setTooltip(null)}
                >
                  <div className="request-chart-track">
                    <div className="request-chart-bar" style={{ height: totalHeight }}>
                      <span className="request-chart-success" style={{ height: successHeight }} />
                      <span className="request-chart-failure" style={{ height: failureHeight }} />
                    </div>
                  </div>
                </div>
              );
            })}
          </div>
          <div className="request-chart-label-row">
            {labels.map((label) => (
              <span
                key={label.key}
                className={`request-chart-label request-chart-label-${label.align}`}
                style={{ left: `${label.left}%` }}
              >
                {label.label}
              </span>
            ))}
          </div>
        </div>
      </div>
      {tooltip &&
        createPortal(
          <div
            className="request-chart-tooltip"
            style={{
              left: `${getTooltipPosition(tooltip.clientX, tooltip.clientY).left}px`,
              top: `${getTooltipPosition(tooltip.clientX, tooltip.clientY).top}px`,
            }}
          >
            <strong>
              {formatDateTime(tooltip.bucket.bucketStartUtc)} - {formatDateTime(tooltip.bucket.bucketEndUtc)}
            </strong>
            <span>{t('{{count}} total', { count: tooltip.bucket.totalCount })}</span>
            <span>{t('{{count}} success', { count: tooltip.bucket.successCount })}</span>
            <span>{t('{{count}} failed', { count: tooltip.bucket.failureCount })}</span>
            <span>{t('{{ms}} ms avg', { ms: tooltip.bucket.averageDurationMs.toFixed(2) })}</span>
          </div>,
          document.body,
        )}
    </>
  );
}
