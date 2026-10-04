import { useCallback, useEffect, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import { getJob, listJobs } from '../../api/client';
import type { Job } from '../../types/models';
import { useLocale } from '../../context/LocaleContext';
import { useNotifications } from '../../context/NotificationContext';
import { jobFriendlyName, jobRoute } from '../../lib/vesselImportLabels';

/** Poll interval while at least one background job is running, in milliseconds. */
export const ACTIVITY_FAST_POLL_MS = 5000;
/** Poll interval while nothing is running, in milliseconds. */
export const ACTIVITY_IDLE_POLL_MS = 30000;
/** Most active jobs fetched per poll; more than this running at once is not expected. */
export const ACTIVITY_PAGE_SIZE = 100;
/** Window event other components dispatch right after they start background work, to refresh immediately. */
export const BACKGROUND_ACTIVITY_EVENT = 'armada:background-activity';

/** Tell the header indicator that background work just started so it refreshes now instead of at the next poll. */
export function notifyBackgroundActivity(): void {
  try {
    window.dispatchEvent(new Event(BACKGROUND_ACTIVITY_EVENT));
  } catch {
    // Non-browser environments have no window events; the next poll picks the job up.
  }
}

function isActive(job: Job): boolean {
  return job.status === 'Queued' || job.status === 'Running';
}

/**
 * Header indicator for background work in the tenant (discovery, import, fleet categorization, vessel health
 * evaluation, and any other job). Polls the Jobs API every 5 seconds while something runs and every 30 seconds
 * otherwise. Shows a spinner and a count with visible text, opens a popover listing running jobs with friendly
 * names and links, announces starts and finishes through a polite live region, and toasts when a fleet
 * categorization finishes or fails.
 */
export default function BackgroundActivityIndicator() {
  const { t, formatRelativeTime } = useLocale();
  const { pushToast } = useNotifications();
  const [running, setRunning] = useState<Job[]>([]);
  const [open, setOpen] = useState(false);
  const [announcement, setAnnouncement] = useState('');
  const containerRef = useRef<HTMLDivElement>(null);
  const previous = useRef<Map<string, Job> | null>(null);
  const timer = useRef<number | null>(null);
  const mounted = useRef(true);

  const handleFinished = useCallback(async (finished: Job[]) => {
    const messages: string[] = [];
    for (const job of finished) {
      let latest: Job = job;
      try {
        latest = await getJob(job.id);
      } catch {
        // Fall back to the last known state.
      }
      const name = jobFriendlyName(t, latest);
      messages.push(t('Background task finished: {{name}}', { name }));
      if (latest.kind === 'FleetCategorization') {
        if (latest.status === 'Succeeded') pushToast('success', t('Fleet recommendations are ready to review. Open the import from the header or the import history.'));
        else if (latest.status === 'Failed') pushToast('error', t('Fleet categorization failed: {{reason}}', { reason: latest.errorReason || t('unknown error') }));
        else if (latest.status === 'Cancelled') pushToast('warning', t('Fleet categorization was stopped.'));
      }
    }
    if (mounted.current && messages.length > 0) setAnnouncement(messages.join(' '));
  }, [pushToast, t]);

  const poll = useCallback(async () => {
    if (timer.current !== null) {
      window.clearTimeout(timer.current);
      timer.current = null;
    }
    let active: Job[] = [];
    try {
      // Ask the server for active jobs only: the full job history grows without bound and this runs every 5-30 s.
      const result = await listJobs({ status: ['Queued', 'Running'], pageSize: ACTIVITY_PAGE_SIZE });
      active = (result.objects || []).filter(isActive);
      if (!mounted.current) return;
      const before = previous.current;
      const now = new Map(active.map((j) => [j.id, j]));
      if (before) {
        const started = active.filter((j) => !before.has(j.id));
        const finished = [...before.values()].filter((j) => !now.has(j.id));
        if (started.length > 0) setAnnouncement(started.map((j) => t('Background task started: {{name}}', { name: jobFriendlyName(t, j) })).join(' '));
        if (finished.length > 0) void handleFinished(finished);
      }
      previous.current = now;
      setRunning(active);
    } catch {
      // Keep the last known state; the next poll retries.
      active = running;
    }
    if (!mounted.current) return;
    timer.current = window.setTimeout(() => void poll(), active.length > 0 ? ACTIVITY_FAST_POLL_MS : ACTIVITY_IDLE_POLL_MS);
    // `running` is only a fallback for failed polls; excluding it keeps the poll loop stable.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [handleFinished, t]);

  useEffect(() => {
    mounted.current = true;
    void poll();
    const onActivity = () => { window.setTimeout(() => void poll(), 300); };
    window.addEventListener(BACKGROUND_ACTIVITY_EVENT, onActivity);
    return () => {
      mounted.current = false;
      window.removeEventListener(BACKGROUND_ACTIVITY_EVENT, onActivity);
      if (timer.current !== null) window.clearTimeout(timer.current);
    };
  }, [poll]);

  useEffect(() => {
    if (!open) return undefined;
    function onPointerDown(event: MouseEvent) {
      if (containerRef.current && !containerRef.current.contains(event.target as Node)) setOpen(false);
    }
    function onKeyDown(event: globalThis.KeyboardEvent) {
      if (event.key === 'Escape') setOpen(false);
    }
    document.addEventListener('mousedown', onPointerDown);
    document.addEventListener('keydown', onKeyDown);
    return () => {
      document.removeEventListener('mousedown', onPointerDown);
      document.removeEventListener('keydown', onKeyDown);
    };
  }, [open]);

  const count = running.length;
  const label = t('{count, plural, one {# background task running} other {# background tasks running}}', { count });

  return (
    <div className="bg-activity" ref={containerRef}>
      <span className="sr-only" aria-live="polite" role="status">{announcement}</span>
      {count > 0 && (
        <button
          type="button"
          className="bg-activity-btn"
          onClick={() => setOpen((v) => !v)}
          aria-haspopup="true"
          aria-expanded={open}
          aria-label={label}
          title={label}
        >
          <span className="bg-activity-spinner" aria-hidden="true" />
          <span className="bg-activity-text">{t('{count, plural, one {# running} other {# running}}', { count })}</span>
        </button>
      )}
      {open && count > 0 && (
        <div className="bg-activity-popover" role="dialog" aria-label={t('Background tasks')}>
          <div className="bg-activity-popover-head">{t('Background tasks')}</div>
          <ul className="bg-activity-list">
            {running.map((job) => (
              <li key={job.id}>
                <Link to={jobRoute(job)} className="bg-activity-item" onClick={() => setOpen(false)}>
                  <span className="bg-activity-item-name">{jobFriendlyName(t, job)}</span>
                  <span className="bg-activity-item-meta">
                    {job.status === 'Queued' ? t('Queued') : t('Running')}
                    {job.startedUtc ? ` - ${formatRelativeTime(job.startedUtc)}` : ''}
                  </span>
                  <span className="bg-activity-item-detail mono" data-i18n-skip="true">{job.name}</span>
                </Link>
              </li>
            ))}
          </ul>
          <Link to="/jobs" className="bg-activity-all" onClick={() => setOpen(false)}>{t('Open the Jobs page')}</Link>
        </div>
      )}
    </div>
  );
}
