import type { InboxItem } from '../types/models';

/**
 * The "Needs You" attention count shared by the dashboard sidebar badge and the mobile Approvals badge: the
 * consolidated inbox (missions in review, failed landings and missions, failed merges, deployments awaiting
 * approval, stalled captains, CLI permission requests) polled on an interval and refreshed, throttled, when
 * WebSocket activity arrives.
 */

/** Poll interval for the inbox count. */
export const INBOX_POLL_INTERVAL_MS = 20000;

/** Minimum gap between socket-triggered refreshes. */
export const INBOX_SOCKET_REFRESH_THROTTLE_MS = 4000;

export interface InboxCountState {
  items: InboxItem[];
  count: number;
  hasCritical: boolean;
  hasWarning: boolean;
}

/** Count and worst severity of the inbox items (a non-array response counts as empty). */
export function summarizeInbox(items: InboxItem[] | null | undefined): InboxCountState {
  const list = Array.isArray(items) ? items : [];
  return {
    items: list,
    count: list.length,
    hasCritical: list.some((item) => item.severity === 'Critical'),
    hasWarning: list.some((item) => item.severity === 'Warning'),
  };
}

/** True when socket activity should refresh the inbox now (the last load is older than the throttle). */
export function shouldRefreshInboxOnSocket(lastLoadMs: number, nowMs: number): boolean {
  return nowMs - lastLoadMs >= INBOX_SOCKET_REFRESH_THROTTLE_MS;
}
