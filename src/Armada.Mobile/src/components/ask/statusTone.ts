import { normalizeStatus } from '@dashboard/lib/askWork';
import type { StatusTone } from '../ui/StatusBadge';

const SUCCESS = new Set(['complete', 'completed', 'landed', 'succeeded', 'executed', 'allowed', 'passed', 'approved', 'merged', 'idle']);
const FAILED = new Set(['failed', 'landingfailed', 'verificationfailed', 'timedout', 'denied', 'rejected', 'error', 'completedwithfailures']);
const WARNING = new Set(['pending', 'pendingapproval', 'review', 'stalled', 'expired', 'workproduced', 'queued']);
const RUNNING = new Set(['running', 'inprogress', 'working', 'testing', 'assigned', 'active', 'open', 'rollingback', 'stopping', 'landing']);

/** Badge tone for an entity or proposal status (the label is always shown; tone only adds color). */
export function statusTone(status: string | null | undefined): StatusTone {
  const s = normalizeStatus(status);
  if (SUCCESS.has(s)) return 'success';
  if (FAILED.has(s)) return 'failed';
  if (WARNING.has(s)) return 'warning';
  if (RUNNING.has(s)) return 'running';
  if (s === 'cancelled' || s === 'skipped') return 'cancelled';
  return 'info';
}
