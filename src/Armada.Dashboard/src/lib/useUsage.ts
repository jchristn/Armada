import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { ApiError, captureCaptainUsage, getUsageLatest } from '../api/client';
import { useUsageRevision } from '../context/WebSocketContext';
import type { UsageCaptureReceipt, UsageSnapshotView } from '../types/models';
import { USAGE_LATEST_BATCH_LIMIT, chunkIds } from './usage';

/** Most account pages one batch follows before reporting truncation instead of fanning out further. */
export const USAGE_MAX_PAGES_PER_BATCH = 5;
/** Delay before the follow-up read after a queued capture (the probe runs on the host in the background). */
export const USAGE_POST_CAPTURE_REFRESH_MS = 5000;

export type UsageLoadStatus = 'loading' | 'error' | 'ready';

export interface LatestUsageByCaptain {
  status: UsageLoadStatus;
  /** Observations keyed by member captain id. A captain with no entry has no observation (not zero usage). */
  byCaptain: Map<string, UsageSnapshotView[]>;
  /** True when more account pages existed than the bounded batch followed. */
  truncated: boolean;
  /** Number of latest requests issued for the current id set. */
  requests: number;
  retry: () => void;
}

/**
 * Hydrate the visible captain page with ONE latest request per 100 ids (plus bounded account pages), never one
 * request per row. Refetches when the id set changes, after coalesced usage events and on reconnect.
 */
export function useLatestUsageForCaptains(captainIds: string[], enabled = true): LatestUsageByCaptain {
  const revision = useUsageRevision();
  const [attempt, setAttempt] = useState(0);
  const [state, setState] = useState<{ status: UsageLoadStatus; byCaptain: Map<string, UsageSnapshotView[]>; truncated: boolean; requests: number }>(
    { status: 'loading', byCaptain: new Map(), truncated: false, requests: 0 });
  const key = useMemo(() => Array.from(new Set(captainIds)).sort().join(','), [captainIds]);
  const sequence = useRef(0);

  useEffect(() => {
    if (!enabled || !key) {
      setState({ status: 'ready', byCaptain: new Map(), truncated: false, requests: 0 });
      return;
    }
    const current = ++sequence.current;
    setState((prev) => ({ ...prev, status: prev.byCaptain.size > 0 ? prev.status : 'loading' }));
    (async () => {
      const ids = key.split(',');
      const views: UsageSnapshotView[] = [];
      let truncated = false;
      let requests = 0;
      for (const chunk of chunkIds(ids, USAGE_LATEST_BATCH_LIMIT)) {
        let page = 1;
        for (;;) {
          const result = await getUsageLatest({ captainIds: chunk.join(','), pageNumber: page, pageSize: USAGE_LATEST_BATCH_LIMIT });
          requests += 1;
          views.push(...(result.objects ?? []));
          if (page >= (result.totalPages ?? 1)) break;
          if (page >= USAGE_MAX_PAGES_PER_BATCH) { truncated = true; break; }
          page += 1;
        }
      }
      if (current !== sequence.current) return;
      const wanted = new Set(ids);
      const byCaptain = new Map<string, UsageSnapshotView[]>();
      const seen = new Set<string>();
      for (const view of views) {
        const identity = view.id ?? `${view.hostId}|${view.runtime}|${view.provider}|${view.accountKey}`;
        if (seen.has(identity)) continue;
        seen.add(identity);
        for (const member of view.captainIds ?? []) {
          if (!wanted.has(member)) continue;
          const list = byCaptain.get(member) ?? [];
          list.push(view);
          byCaptain.set(member, list);
        }
      }
      setState({ status: 'ready', byCaptain, truncated, requests });
    })().catch(() => {
      if (current !== sequence.current) return;
      setState((prev) => ({ ...prev, status: 'error' }));
    });
  }, [key, enabled, revision, attempt]);

  const retry = useCallback(() => setAttempt((n) => n + 1), []);
  return { ...state, retry };
}

export type CaptureFeedback =
  | { kind: 'idle' }
  | { kind: 'capturing' }
  | { kind: 'queued'; receipt: UsageCaptureReceipt }
  | { kind: 'observed' }
  | { kind: 'disabled'; message: string }
  | { kind: 'cooldown'; nextEligibleUtc: string | null }
  | { kind: 'offline'; message: string }
  | { kind: 'error'; message: string };

export interface CaptainUsage {
  status: UsageLoadStatus;
  views: UsageSnapshotView[];
  truncated: boolean;
  error: string;
  retry: () => void;
  capture: () => Promise<void>;
  feedback: CaptureFeedback;
}

/** Latest observations for one captain (first page of accounts) with capture-now feedback and event refresh. */
export function useCaptainUsage(captainId: string | undefined): CaptainUsage {
  const revision = useUsageRevision();
  const [attempt, setAttempt] = useState(0);
  const [status, setStatus] = useState<UsageLoadStatus>('loading');
  const [views, setViews] = useState<UsageSnapshotView[]>([]);
  const [truncated, setTruncated] = useState(false);
  const [error, setError] = useState('');
  const [feedback, setFeedback] = useState<CaptureFeedback>({ kind: 'idle' });
  const sequence = useRef(0);
  const followUp = useRef<ReturnType<typeof setTimeout> | null>(null);

  useEffect(() => {
    if (!captainId) return;
    const current = ++sequence.current;
    setStatus((prev) => (prev === 'ready' ? prev : 'loading'));
    getUsageLatest({ captainId, pageSize: 25 })
      .then((result) => {
        if (current !== sequence.current) return;
        setViews(result.objects ?? []);
        setTruncated((result.totalPages ?? 1) > 1);
        setError('');
        setStatus('ready');
      })
      .catch((err: unknown) => {
        if (current !== sequence.current) return;
        setError(err instanceof Error ? err.message : String(err));
        setStatus('error');
      });
  }, [captainId, revision, attempt]);

  useEffect(() => () => { if (followUp.current) clearTimeout(followUp.current); }, []);

  const retry = useCallback(() => setAttempt((n) => n + 1), []);

  const capture = useCallback(async () => {
    if (!captainId) return;
    setFeedback({ kind: 'capturing' });
    try {
      const result = await captureCaptainUsage(captainId);
      if (result && typeof result === 'object' && 'jobId' in result) {
        setFeedback({ kind: 'queued', receipt: result as UsageCaptureReceipt });
        if (followUp.current) clearTimeout(followUp.current);
        followUp.current = setTimeout(() => setAttempt((n) => n + 1), USAGE_POST_CAPTURE_REFRESH_MS);
      } else {
        setFeedback({ kind: 'observed' });
      }
      setAttempt((n) => n + 1);
    } catch (err: unknown) {
      if (err instanceof ApiError) {
        const data = (err.data ?? {}) as { nextEligibleUtc?: string | null; data?: { nextEligibleUtc?: string | null } };
        if (err.status === 409) { setFeedback({ kind: 'disabled', message: err.message }); return; }
        if (err.status === 429) { setFeedback({ kind: 'cooldown', nextEligibleUtc: data.nextEligibleUtc ?? data.data?.nextEligibleUtc ?? null }); return; }
        if (err.status === 503) { setFeedback({ kind: 'offline', message: err.message }); return; }
      }
      setFeedback({ kind: 'error', message: err instanceof Error ? err.message : String(err) });
    }
  }, [captainId]);

  return { status, views, truncated, error, retry, capture, feedback };
}
