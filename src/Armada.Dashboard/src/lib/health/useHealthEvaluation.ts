import { useCallback, useEffect, useRef, useState } from 'react';
import { evaluateVesselHealth, getJob, listJobs } from '../../api/client';
import type { Job, VesselHealthEvaluateRequest, VesselHealthEvaluationStart } from '../../types/models';
import { msg } from './healthText';

/** Job name the server gives vessel health evaluations (see REST_API.md). */
export const HEALTH_JOB_NAME = 'Vessel health evaluation';

const TERMINAL_STATUSES = ['Succeeded', 'Failed', 'Cancelled', 'Canceled'];

export function isJobTerminal(job: Pick<Job, 'status'> | null | undefined): boolean {
  return !!job && TERMINAL_STATUSES.includes(job.status);
}

export interface EvaluationStartMessage {
  severity: 'success' | 'warning';
  /** English catalog key; translate with `t(key, params)`. */
  key: string;
  params: Record<string, number>;
}

/** Maps a 202 or 409 evaluate response to the toast the UI shows. */
export function describeEvaluationStart(start: VesselHealthEvaluationStart): EvaluationStartMessage {
  if (start.alreadyRunning) {
    return { severity: 'warning', key: msg('An evaluation is already running. Showing its progress instead.'), params: {} };
  }
  return {
    severity: 'success',
    key: start.vesselCount === 1 ? msg('Evaluation started for {{count}} vessel.') : msg('Evaluation started for {{count}} vessels.'),
    params: { count: start.vesselCount },
  };
}

interface UseHealthEvaluationOptions {
  /** Called once when a tracked job reaches a terminal status. */
  onFinished?: (job: Job) => void;
  /** Poll interval in milliseconds (default 2000). */
  pollMs?: number;
}

/**
 * Starts vessel health evaluations and tracks the resulting background job until it finishes. A 409
 * (an evaluation is already running) is not an error: the hook tracks the running job instead. On mount,
 * `discover()` finds the most recent evaluation job so a scheduled run in progress is shown too.
 */
export function useHealthEvaluation({ onFinished, pollMs = 2000 }: UseHealthEvaluationOptions = {}) {
  const [trackedJobId, setTrackedJobId] = useState<string | null>(null);
  const [activeJob, setActiveJob] = useState<Job | null>(null);
  const [lastJob, setLastJob] = useState<Job | null>(null);
  const [starting, setStarting] = useState(false);
  const onFinishedRef = useRef(onFinished);
  onFinishedRef.current = onFinished;

  const track = useCallback((jobId: string) => {
    setTrackedJobId(jobId);
  }, []);

  const start = useCallback(async (body: VesselHealthEvaluateRequest = {}): Promise<VesselHealthEvaluationStart> => {
    setStarting(true);
    try {
      const result = await evaluateVesselHealth(body);
      if (result?.jobId) setTrackedJobId(result.jobId);
      return result;
    } finally {
      setStarting(false);
    }
  }, []);

  const discover = useCallback(async () => {
    try {
      const jobs = await listJobs();
      const match = (jobs.objects ?? []).find((job) => job.name === HEALTH_JOB_NAME);
      if (!match) return;
      if (isJobTerminal(match)) setLastJob(match);
      else setTrackedJobId(match.id);
    } catch {
      // Jobs are informational here; the table still works without them.
    }
  }, []);

  useEffect(() => {
    if (!trackedJobId) return undefined;
    let cancelled = false;
    let timer: number | undefined;

    const poll = async () => {
      try {
        const job = await getJob(trackedJobId);
        if (cancelled) return;
        if (isJobTerminal(job)) {
          setActiveJob(null);
          setLastJob(job);
          setTrackedJobId(null);
          onFinishedRef.current?.(job);
          return;
        }
        setActiveJob(job);
      } catch {
        if (cancelled) return;
      }
      timer = window.setTimeout(poll, pollMs);
    };

    void poll();
    return () => {
      cancelled = true;
      if (timer !== undefined) window.clearTimeout(timer);
    };
  }, [trackedJobId, pollMs]);

  return {
    /** The running job (null when idle). */
    activeJob,
    /** True while a job is tracked, before or after its first poll. */
    running: trackedJobId !== null,
    /** The most recent finished evaluation job seen. */
    lastJob,
    starting,
    start,
    track,
    discover,
  };
}
