import type { ConcurrencyMetrics, MissionWaitGate } from '../types/models';

/** Four gate groups, in fixed categorical order (never cycled). */
export const WAIT_GROUPS = [
  { key: 'captain', label: 'Captain slot', color: 'var(--wait-series-1)', gates: ['CaptainSlot'] },
  { key: 'vessel', label: 'Vessel and voyage slots', color: 'var(--wait-series-2)', gates: ['VesselVoyageSlot', 'VoyageMissionSlot'] },
  { key: 'capacity', label: 'Other capacity', color: 'var(--wait-series-3)', gates: ['GlobalSlot', 'RuntimeSlot', 'EndpointSlot', 'BranchLock', 'Placement'] },
  { key: 'readiness', label: 'Dependencies and parents', color: 'var(--wait-series-4)', gates: ['Dependency', 'Parent', 'IntegrationBarrier', 'BroadScope', 'LaunchBackoff', 'Other'] },
] as const;

export type WaitGroupKey = typeof WAIT_GROUPS[number]['key'];

/** The group a gate belongs to; an unknown gate counts as readiness ("other"). */
export function waitGroupOf(gate: MissionWaitGate | string): WaitGroupKey {
  for (const group of WAIT_GROUPS) if ((group.gates as readonly string[]).includes(gate)) return group.key;
  return 'readiness';
}

export interface WaitBucketBar {
  startUtc: string;
  missionsWaiting: number;
  /** Minutes per group, in WAIT_GROUPS order. */
  minutes: Record<WaitGroupKey, number>;
  totalMinutes: number;
}

/** Per-bucket minutes by group, for a stacked bar per bucket. */
export function bucketBars(metrics: ConcurrencyMetrics | null): WaitBucketBar[] {
  if (!metrics) return [];
  return metrics.buckets.map(bucket => {
    const minutes: Record<WaitGroupKey, number> = { captain: 0, vessel: 0, capacity: 0, readiness: 0 };
    for (const g of bucket.byGate) minutes[waitGroupOf(g.gate)] += g.seconds / 60;
    return { startUtc: bucket.startUtc, missionsWaiting: bucket.missionsWaiting, minutes, totalMinutes: Object.values(minutes).reduce((a, b) => a + b, 0) };
  });
}

/** A round axis maximum at or above the largest value: 1, 2, 5 x 10^n. */
export function niceMax(value: number): number {
  if (!(value > 0)) return 1;
  const exp = Math.pow(10, Math.floor(Math.log10(value)));
  for (const step of [1, 2, 5, 10]) if (step * exp >= value) return step * exp;
  return 10 * exp;
}

/** Window presets for the panel's time range control. */
export const CONCURRENCY_RANGES = [
  { key: '6h', label: '6 hours', hours: 6 },
  { key: '24h', label: '24 hours', hours: 24 },
  { key: '7d', label: '7 days', hours: 168 },
  { key: '30d', label: '30 days', hours: 720 },
] as const;
