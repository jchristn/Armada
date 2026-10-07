import type { StatusTone } from '../components/ui/StatusBadge';

/**
 * The badge tone for an entity status (missions, voyages, captains, merge entries, docks, signals, events). The
 * dashboard colors these with CSS classes named after the status; the app maps them onto its semantic tones.
 */
const TONES: Record<string, StatusTone> = {
  pending: 'pending',
  queued: 'pending',
  open: 'pending',
  assigned: 'running',
  inprogress: 'running',
  testing: 'running',
  running: 'running',
  working: 'running',
  landing: 'running',
  passed: 'running',
  workproduced: 'warning',
  review: 'warning',
  pendingapproval: 'warning',
  stalled: 'warning',
  stopping: 'warning',
  warn: 'warning',
  partial: 'warning',
  nudge: 'warning',
  complete: 'success',
  completed: 'success',
  succeeded: 'success',
  landed: 'success',
  active: 'success',
  pass: 'success',
  idle: 'success',
  completion: 'success',
  failed: 'failed',
  landingfailed: 'failed',
  verificationfailed: 'failed',
  denied: 'failed',
  error: 'failed',
  fail: 'failed',
  cancelled: 'cancelled',
  inactive: 'cancelled',
  skipped: 'skipped',
  notrun: 'skipped',
};

export function statusTone(status: string | null | undefined): StatusTone {
  return TONES[(status ?? '').toLowerCase()] ?? 'info';
}
