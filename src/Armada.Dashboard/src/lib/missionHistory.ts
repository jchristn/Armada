/**
 * Mission history chart logic shared by the dashboard (MissionHistoryChart) and the mobile Home: the time ranges
 * and their bucket sizes, the query for /api/v1/missions/history, bucket mapping, axis ticks, and labels.
 */
import type { MissionHistorySummaryResult } from '../types/models';

export const MISSION_HISTORY_RANGES = [
  { label: 'Last Hour', value: 'hour', hours: 1, stepMinutes: 1 },
  { label: 'Last Day', value: 'day', hours: 24, stepMinutes: 15 },
  { label: 'Last Week', value: 'week', hours: 168, stepMinutes: 60 },
  { label: 'Last Month', value: 'month', hours: 720, stepMinutes: 360 },
] as const;

export type MissionHistoryRange = typeof MISSION_HISTORY_RANGES[number];
export type MissionHistoryRangeValue = MissionHistoryRange['value'];

export interface MissionHistoryBucketView {
  timestampMs: number;
  complete: number;
  failed: number;
  other: number;
}

export interface MissionHistoryQuery {
  fromUtc: string;
  toUtc: string;
  bucketMinutes: number;
  fleetId?: string;
  vesselId?: string;
}

/** The history request for a range ending at `now`, optionally narrowed to a fleet or vessel. */
export function missionHistoryQuery(range: Pick<MissionHistoryRange, 'hours' | 'stepMinutes'>, fleetId: string, vesselId: string, now: Date): MissionHistoryQuery {
  const start = new Date(now.getTime() - range.hours * 3600000);
  return {
    fromUtc: start.toISOString(),
    toUtc: now.toISOString(),
    bucketMinutes: range.stepMinutes,
    fleetId: fleetId || undefined,
    vesselId: vesselId || undefined,
  };
}

/** The chart bars for a history result. */
export function historyBuckets(history: MissionHistorySummaryResult | null | undefined): MissionHistoryBucketView[] {
  return (history?.buckets || []).map(bucket => ({
    timestampMs: new Date(bucket.startUtc).getTime(),
    complete: bucket.completeCount,
    failed: bucket.failedCount,
    other: bucket.otherCount,
  }));
}

/** Y axis ticks: about four steps of a whole-number size, ending at or above `max`. */
export function computeYTicks(max: number): number[] {
  if (max <= 0) return [0];
  const step = Math.max(1, Math.ceil(max / 4));
  const ticks: number[] = [];
  for (let i = 0; i <= max; i += step) ticks.push(i);
  if (ticks[ticks.length - 1] < max) ticks.push(ticks[ticks.length - 1] + step);
  return ticks;
}

/** X axis label for a bucket: time of day for short buckets and ranges, date and time for ranges over two days. */
export function formatBucketLabel(ts: number, stepMinutes: number, hours: number): string {
  const d = new Date(ts);
  if (stepMinutes <= 15) return d.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' });
  if (hours > 48) return d.toLocaleDateString(undefined, { month: 'short', day: 'numeric' }) + ' ' + d.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' });
  return d.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' });
}

/** Tooltip heading for a bucket. */
export function formatTooltipTime(ts: number): string {
  const d = new Date(ts);
  return d.toLocaleString(undefined, { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' });
}
