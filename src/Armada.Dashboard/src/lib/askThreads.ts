import type { AskThread } from '../types/models';
import type { AskEvent } from './askEvents';
import { isWorkActive } from './askWork';

/** Live per-thread activity learned from socket events (overrides the server's counts once known). */
export interface ThreadActivity {
  /** Tracked work id -> still active. */
  work: Record<string, boolean>;
  /** A captain turn is running. */
  replying: boolean;
}

export type ThreadActivityMap = Record<string, ThreadActivity>;

function time(value: string | null | undefined): number {
  if (!value) return 0;
  const parsed = Date.parse(value);
  return Number.isNaN(parsed) ? 0 : parsed;
}

/** Pinned first, then most recent message first (mirrors the server's order so live inserts land correctly). */
export function sortThreads(threads: AskThread[]): AskThread[] {
  return [...threads].sort((a, b) => {
    if (!!a.pinned !== !!b.pinned) return a.pinned ? -1 : 1;
    return time(b.lastMessageUtc ?? b.lastUpdateUtc ?? b.createdUtc) - time(a.lastMessageUtc ?? a.lastUpdateUtc ?? a.createdUtc);
  });
}

export interface ThreadListFilter {
  search: string;
  includeArchived: boolean;
}

function matchesFilter(thread: AskThread, filter: ThreadListFilter): boolean {
  if (thread.archived && !filter.includeArchived) return false;
  const q = filter.search.trim().toLowerCase();
  if (!q) return true;
  return (thread.title ?? '').toLowerCase().includes(q) || (thread.summaryText ?? '').toLowerCase().includes(q);
}

/**
 * Fold an `ask.thread` update into the visible list: replace the row, insert a new thread that matches the
 * current filter, drop one that no longer matches (archived), and keep the open thread's unread count at zero.
 */
export function applyThreadUpdate(threads: AskThread[], thread: AskThread, filter: ThreadListFilter, openThreadId: string | null): AskThread[] {
  const incoming: AskThread = thread.id === openThreadId ? { ...thread, unreadCount: 0 } : thread;
  const idx = threads.findIndex((t) => t.id === incoming.id);
  if (idx === -1) {
    if (!matchesFilter(incoming, filter)) return threads;
    return sortThreads([...threads, incoming]);
  }
  const merged = { ...threads[idx], ...incoming };
  if (!matchesFilter(merged, { ...filter, search: '' })) return threads.filter((t) => t.id !== incoming.id);
  const copy = [...threads];
  copy[idx] = merged;
  return sortThreads(copy);
}

/** Track live "working" / "replying" state per thread from socket events. Returns the same map when unchanged. */
export function applyActivityEvent(map: ThreadActivityMap, event: AskEvent): ThreadActivityMap {
  const current: ThreadActivity = map[event.threadId] ?? { work: {}, replying: false };
  if (event.type === 'ask.work') {
    const source = event.snapshot ?? event.trackedWork;
    const active = isWorkActive(source);
    if (current.work[event.trackedWorkId] === active) return map;
    return { ...map, [event.threadId]: { ...current, work: { ...current.work, [event.trackedWorkId]: active } } };
  }
  if (event.type === 'ask.turn') {
    const replying = event.state === 'started';
    if (current.replying === replying) return map;
    return { ...map, [event.threadId]: { ...current, replying } };
  }
  if (event.type === 'ask.chunk' || event.type === 'ask.tool' || event.type === 'ask.thinking') {
    if (current.replying) return map;
    return { ...map, [event.threadId]: { ...current, replying: true } };
  }
  return map;
}

/** Whether the thread has active tracked work: live events win, otherwise the server's `activeWorkCount`. */
export function isThreadWorking(thread: AskThread, activity: ThreadActivityMap): boolean {
  const live = activity[thread.id];
  if (live && Object.keys(live.work).length > 0) {
    if (Object.values(live.work).some(Boolean)) return true;
    // Live data only covers items that changed since load; fall back to the server count for the rest.
    return false;
  }
  return (thread.activeWorkCount ?? 0) > 0;
}

export function isThreadReplying(thread: AskThread, activity: ThreadActivityMap): boolean {
  const live = activity[thread.id];
  if (live) return live.replying;
  return !!thread.activeTurnId;
}
