import type { Captain, UsageSnapshotView, UsageWindow } from '../types/models';

/** Distinct presentation states. Each one has its own text and next action; none of them is rendered as zero usage. */
export type UsageDisplayKind =
  | 'loading'
  | 'error'
  | 'disabled'
  | 'empty'
  | 'ok'
  | 'stale'
  | 'estimated'
  | 'unsupported'
  | 'authMissing'
  | 'failed';

/** Percent thresholds: below 75 normal, 75 up to 90 warning, 90 and above danger. */
export const USAGE_WARNING_PERCENT = 75;
export const USAGE_DANGER_PERCENT = 90;

/** Most captain ids one latest batch request may carry (the server rejects 101). */
export const USAGE_LATEST_BATCH_LIMIT = 100;

export type UsageLevel = 'normal' | 'warning' | 'danger';

export function usageLevel(percent: number): UsageLevel {
  if (percent >= USAGE_DANGER_PERCENT) return 'danger';
  if (percent >= USAGE_WARNING_PERCENT) return 'warning';
  return 'normal';
}

/** Text that carries the same meaning as the bar color. */
export const USAGE_LEVEL_TEXT: Record<UsageLevel, string> = {
  normal: 'Normal',
  warning: 'Warning',
  danger: 'Near limit',
};

/** True when the captain's own switch and mode let the scheduler capture it. */
export function captainCaptureActive(captain: Pick<Captain, 'usageCaptureEnabled' | 'usageCaptureMode' | 'usageCaptureActive'>): boolean {
  if (typeof captain.usageCaptureActive === 'boolean') return captain.usageCaptureActive;
  return captain.usageCaptureEnabled !== false && captain.usageCaptureMode !== 'Disabled';
}

/** Presentation state of one observation. Ok becomes stale once the server marks it stale. */
export function viewKind(view: UsageSnapshotView): UsageDisplayKind {
  switch (view.status) {
    case 'Unsupported': return 'unsupported';
    case 'AuthMissing': return 'authMissing';
    case 'Failed': return 'failed';
    case 'Estimated': return view.stale ? 'stale' : 'estimated';
    default: return view.stale ? 'stale' : 'ok';
  }
}

/**
 * The highest window that still applies: it has a known percentage and its reset time (when reported) has not
 * passed. A window past its reset is no longer the current limit, so it never wins. Returns null when no window
 * qualifies; the caller shows "unknown", never 0%.
 */
export function highestApplicableWindow(view: UsageSnapshotView, nowMs: number = Date.now()): UsageWindow | null {
  let best: UsageWindow | null = null;
  for (const quotaWindow of view.windows ?? []) {
    if (quotaWindow.usedPercent === null || quotaWindow.usedPercent === undefined || Number.isNaN(quotaWindow.usedPercent)) continue;
    if (quotaWindow.resetsUtc && Date.parse(quotaWindow.resetsUtc) <= nowMs) continue;
    if (!best || (quotaWindow.usedPercent as number) > (best.usedPercent as number)) best = quotaWindow;
  }
  return best;
}

/** The highest applicable window across a captain's observations, preferring fresh observations over stale ones. */
export function captainHeadline(views: UsageSnapshotView[], nowMs: number = Date.now()): { view: UsageSnapshotView; window: UsageWindow | null } | null {
  if (views.length === 0) return null;
  const ranked = [...views].sort((a, b) => rank(a) - rank(b));
  let chosen: { view: UsageSnapshotView; window: UsageWindow | null } | null = null;
  for (const view of ranked) {
    const quotaWindow = highestApplicableWindow(view, nowMs);
    if (!chosen) { chosen = { view, window: quotaWindow }; continue; }
    if (rank(view) > rank(chosen.view)) break;
    if (quotaWindow && (!chosen.window || (quotaWindow.usedPercent as number) > (chosen['window'].usedPercent as number))) chosen = { view, window: quotaWindow };
  }
  return chosen;
}

function rank(view: UsageSnapshotView): number {
  const kind = viewKind(view);
  if (kind === 'ok' || kind === 'estimated') return 0;
  if (kind === 'stale') return 1;
  if (kind === 'failed') return 2;
  return 3;
}

/** Status text for a state (catalog keys). */
export const USAGE_KIND_TEXT: Record<UsageDisplayKind, string> = {
  loading: 'Loading usage...',
  error: 'Usage could not be loaded.',
  disabled: 'Capture disabled',
  empty: 'No usage observed yet',
  ok: 'Fresh',
  stale: 'Stale',
  estimated: 'Estimated',
  unsupported: 'Unsupported',
  authMissing: 'Sign-in needed',
  failed: 'Capture failed',
};

/** Explanation and next action for a state (catalog keys; {{host}} and {{age}} are interpolated). */
export const USAGE_KIND_HINT: Record<UsageDisplayKind, string> = {
  loading: 'Reading the latest account observation.',
  error: 'The usage service did not answer. Retry, or check the server log if it keeps failing.',
  disabled: 'Usage capture is off for this captain. An administrator can enable it in the capture settings below; nothing is measured while it is off.',
  empty: 'Capture is on but no observation has been stored yet. This is not zero usage; capture now or wait for the next scheduled read.',
  ok: 'Observed within the capture interval.',
  stale: 'The last good observation is older than two capture intervals. Values may no longer match the provider.',
  estimated: 'Estimated from the configured budget and Armada-recorded tokens, not read from the provider.',
  unsupported: 'This runtime has no verified non-prompt interface for account limits, so Armada cannot read them.',
  authMissing: 'Sign in to the runtime on host {{host}}. Armada never asks you to paste a credential here.',
  failed: 'The last capture failed. Last good data is from {{age}} and is shown below with a warning.',
};

/** Split a captain id list into bounded batches for the latest-usage batch filter. */
export function chunkIds(ids: string[], size: number): string[][] {
  const distinct = Array.from(new Set(ids.filter((id) => !!id)));
  const chunks: string[][] = [];
  for (let i = 0; i < distinct.length; i += size) chunks.push(distinct.slice(i, i + size));
  return chunks;
}

/** Short display alias for a pseudonymous account key; the key itself is an HMAC, never an email. */
export function accountAlias(accountKey: string | null | undefined): string {
  if (!accountKey) return '-';
  return accountKey.length > 10 ? `${accountKey.slice(0, 8)}...` : accountKey;
}

/** Path to the captain detail usage section; the section reads `section=usage` and scrolls to it. */
export function captainUsagePath(captainId: string): string {
  return `/captains/${encodeURIComponent(captainId)}?section=usage`;
}

export const CAPTAIN_USAGE_SECTION_ID = 'captain-usage';
