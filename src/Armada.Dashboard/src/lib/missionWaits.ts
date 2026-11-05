import type { MissionWaitGate, MissionWaitInterval } from '../types/models';

/** Display names for scheduling gates (MissionWaitGateEnum). */
export const WAIT_GATE_LABELS: Record<MissionWaitGate, string> = {
  Dependency: 'Dependency',
  Parent: 'Parent mission',
  IntegrationBarrier: 'Other pipelines',
  BroadScope: 'Broad-scope mission',
  BranchLock: 'Branch lock',
  VesselVoyageSlot: 'Vessel voyage slot',
  VoyageMissionSlot: 'Voyage mission slot',
  CaptainSlot: 'Captain slot',
  EndpointSlot: 'Endpoint slot',
  RuntimeSlot: 'Runtime slot',
  GlobalSlot: 'Global mission limit',
  Placement: 'Harbor placement',
  LaunchBackoff: 'Launch retry',
  Other: 'Other',
};

/** Gates whose time counts as slot wait (capacity, not readiness). */
export const CAPACITY_GATES: ReadonlySet<MissionWaitGate> = new Set<MissionWaitGate>([
  'VesselVoyageSlot', 'VoyageMissionSlot', 'BranchLock', 'GlobalSlot', 'CaptainSlot', 'EndpointSlot', 'RuntimeSlot', 'Placement',
]);

/** A label for a gate, falling back to the raw value for one this client does not know. */
export function waitGateLabel(gate: string | null | undefined): string {
  if (!gate) return WAIT_GATE_LABELS.Other;
  return (WAIT_GATE_LABELS as Record<string, string>)[gate] ?? gate;
}

/** Compact duration: 45s, 12m, 1h 5m, 2d 3h. Negative or missing values read as 0s. */
export function formatWaitDuration(seconds: number | null | undefined): string {
  const total = Math.max(0, Math.round(seconds ?? 0));
  if (total < 60) return `${total}s`;
  const minutes = Math.floor(total / 60);
  if (minutes < 60) return `${minutes}m`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return minutes % 60 === 0 ? `${hours}h` : `${hours}h ${minutes % 60}m`;
  const days = Math.floor(hours / 24);
  return hours % 24 === 0 ? `${days}d` : `${days}d ${hours % 24}h`;
}

export interface GateWait {
  gate: MissionWaitGate;
  seconds: number;
}

export interface WaitSummary {
  /** Seconds across every recorded interval. */
  totalSeconds: number;
  /** Seconds on capacity gates. */
  slotSeconds: number;
  /** Seconds per gate, largest first. */
  byGate: GateWait[];
  /** The gate the mission still waits on, or null when no interval is open. */
  current: MissionWaitGate | null;
  /** When the open wait began, or null. */
  currentSinceUtc: string | null;
}

/** Sum a mission's wait intervals; an open interval counts up to <paramref name="nowMs"/>. */
export function summarizeWaits(intervals: MissionWaitInterval[], nowMs: number = Date.now()): WaitSummary {
  const totals = new Map<MissionWaitGate, number>();
  let current: MissionWaitInterval | null = null;
  for (const interval of intervals) {
    const start = Date.parse(interval.startedUtc);
    const end = interval.endedUtc ? Date.parse(interval.endedUtc) : nowMs;
    if (Number.isNaN(start) || Number.isNaN(end)) continue;
    const seconds = Math.max(0, (end - start) / 1000);
    totals.set(interval.gate, (totals.get(interval.gate) ?? 0) + seconds);
    if (!interval.endedUtc && (!current || Date.parse(current.startedUtc) < start)) current = interval;
  }
  const byGate = [...totals.entries()].map(([gate, seconds]) => ({ gate, seconds })).sort((a, b) => b.seconds - a.seconds);
  return {
    totalSeconds: byGate.reduce((sum, g) => sum + g.seconds, 0),
    slotSeconds: byGate.filter(g => CAPACITY_GATES.has(g.gate)).reduce((sum, g) => sum + g.seconds, 0),
    byGate,
    current: current?.gate ?? null,
    currentSinceUtc: current?.startedUtc ?? null,
  };
}

/** "Voyage mission slot 3m, Captain slot 9m": the per-gate breakdown, largest first, at most four gates. */
export function waitBreakdown(summary: WaitSummary, translate: (text: string) => string = (text) => text): string {
  const shown = summary.byGate.filter(g => g.seconds >= 1).slice(0, 4);
  const rest = summary.byGate.length - shown.length;
  const parts = shown.map(g => `${translate(waitGateLabel(g.gate))} ${formatWaitDuration(g.seconds)}`);
  return rest > 0 ? `${parts.join(', ')}, +${rest}` : parts.join(', ');
}

/**
 * Slot wait from a mission's timestamps: assignment minus the first capacity hold. Null before assignment or when the
 * mission predates wait recording.
 */
export function slotWaitSeconds(readyUtc: string | null | undefined, assignedUtc: string | null | undefined): number | null {
  if (!readyUtc || !assignedUtc) return null;
  const ready = Date.parse(readyUtc);
  const assigned = Date.parse(assignedUtc);
  if (Number.isNaN(ready) || Number.isNaN(assigned)) return null;
  return Math.max(0, (assigned - ready) / 1000);
}
