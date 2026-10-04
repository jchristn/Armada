import type { AskTrackedWork, AskWorkSnapshot } from '../types/models';

/** Child statuses (missions and fleet action targets) that will not change again. */
const TERMINAL_CHILD = new Set([
  'complete', 'completed', 'landed', 'failed', 'landingfailed', 'cancelled', 'succeeded', 'skipped', 'timedout',
]);
/** Child statuses that count as a failure on the progress bar. */
const FAILED_CHILD = new Set(['failed', 'landingfailed', 'timedout']);
/** Top-level statuses that mean the whole item is finished. */
const TERMINAL_ITEM = new Set([
  'complete', 'completed', 'completedwithfailures', 'failed', 'cancelled', 'succeeded', 'landed',
]);

export function normalizeStatus(status: string | null | undefined): string {
  return (status ?? '').replace(/[\s_-]/g, '').toLowerCase();
}

export function isTerminalChildStatus(status: string | null | undefined): boolean {
  return TERMINAL_CHILD.has(normalizeStatus(status));
}

export function isFailedChildStatus(status: string | null | undefined): boolean {
  return FAILED_CHILD.has(normalizeStatus(status));
}

/** True while a tracked item is still running. Prefers the explicit `state`, falls back to the status text. */
export function isWorkActive(item: { state?: string | null; status?: string | null } | null | undefined): boolean {
  if (!item) return false;
  if (item.state) return normalizeStatus(item.state) === 'active';
  if (!item.status) return true;
  return !TERMINAL_ITEM.has(normalizeStatus(item.status));
}

export interface WorkProgress {
  total: number;
  done: number;
  failed: number;
  /** 0-100. */
  percent: number;
}

/** Look up a status count case-insensitively (the API client camelizes dictionary keys). */
function countFor(counts: Record<string, number> | null | undefined, match: (status: string) => boolean): number {
  if (!counts) return 0;
  let total = 0;
  for (const [key, value] of Object.entries(counts)) {
    if (typeof value === 'number' && match(key)) total += value;
  }
  return total;
}

/** Progress for the card's bar: from the child rows when present, else the counts, else explicit counters. */
export function workProgress(snapshot: AskWorkSnapshot | null | undefined): WorkProgress | null {
  if (!snapshot) return null;
  let total = 0;
  let done = 0;
  let failed = 0;
  const rows = (snapshot.missions && snapshot.missions.length > 0) ? snapshot.missions : (snapshot.targets ?? []);
  if (rows.length > 0) {
    total = rows.length;
    for (const row of rows) {
      if (isTerminalChildStatus(row.status)) done += 1;
      if (isFailedChildStatus(row.status)) failed += 1;
    }
  } else if (snapshot.counts && Object.keys(snapshot.counts).length > 0) {
    total = countFor(snapshot.counts, () => true);
    done = countFor(snapshot.counts, isTerminalChildStatus);
    failed = countFor(snapshot.counts, isFailedChildStatus);
  } else if (snapshot.totalCount != null && snapshot.totalCount > 0) {
    total = snapshot.totalCount;
    done = Math.min(total, snapshot.completedCount ?? 0);
    failed = snapshot.failedCount ?? 0;
  } else {
    return null;
  }
  if (snapshot.totalCount != null && snapshot.totalCount > total) total = snapshot.totalCount;
  const percent = total > 0 ? Math.round((done / total) * 100) : 0;
  return { total, done, failed, percent };
}

/** Status counts for the card header, from the rows when present (stable order: first appearance). */
export function statusCounts(snapshot: AskWorkSnapshot | null | undefined): Array<{ status: string; count: number }> {
  if (!snapshot) return [];
  const rows = (snapshot.missions && snapshot.missions.length > 0) ? snapshot.missions : (snapshot.targets ?? []);
  const map = new Map<string, number>();
  if (rows.length > 0) {
    for (const row of rows) map.set(row.status, (map.get(row.status) ?? 0) + 1);
  } else if (snapshot.counts) {
    for (const [key, value] of Object.entries(snapshot.counts)) {
      if (typeof value === 'number' && value > 0) map.set(key.charAt(0).toUpperCase() + key.slice(1), value);
    }
  }
  return [...map.entries()].map(([status, count]) => ({ status, count }));
}

/** The dashboard page for a tracked item. */
export function workRoute(entityType: string | null | undefined, entityId: string): string {
  const id = encodeURIComponent(entityId);
  switch (entityType) {
    case 'Voyage': return `/voyages/${id}`;
    case 'Mission': return `/missions/${id}`;
    case 'FleetActionRun': return `/fleet-actions/runs/${id}`;
    case 'VesselImportBatch': return `/vessels/import?batch=${id}`;
    case 'Job': return '/jobs';
    default: return '/activity';
  }
}

/** Fold a fresh snapshot into its tracked-work row (status, state, title). */
export function applySnapshotToWork(work: AskTrackedWork, snapshot: AskWorkSnapshot): AskTrackedWork {
  return {
    ...work,
    status: snapshot.status ?? work.status,
    state: snapshot.state ?? work.state,
    title: work.title || snapshot.title || work.title,
    lastChangeUtc: snapshot.capturedUtc ?? work.lastChangeUtc,
    completedUtc: snapshot.completedUtc ?? work.completedUtc,
    snapshot,
  };
}
