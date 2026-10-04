import type { BadgeIcon, BadgeTone } from '../components/shared/CodeStatusBadge';
import type { FleetActionKind, FleetActionRunStatus, FleetActionTargetStatus } from '../types/models';

/** Translator signature shared with `useLocale().t`. */
export type Translate = (text: string, params?: Record<string, string | number | null | undefined>) => string;

export interface StatusMeta {
  /** English source label; pass through `t` before rendering. */
  label: string;
  /** English source tooltip; pass through `t` before rendering. */
  description: string;
  tone: BadgeTone;
  icon: BadgeIcon;
}

export const RUN_STATUSES: FleetActionRunStatus[] = ['Pending', 'Running', 'Completed', 'CompletedWithFailures', 'Cancelled', 'Failed'];
export const TARGET_STATUSES: FleetActionTargetStatus[] = ['Pending', 'Skipped', 'Running', 'Succeeded', 'Failed', 'Cancelled', 'TimedOut'];

export const RUN_STATUS_META: Record<FleetActionRunStatus, StatusMeta> = {
  Pending: { label: 'Pending', description: 'The run is queued and no target has started yet.', tone: 'pending', icon: 'clock' },
  Running: { label: 'Running', description: 'At least one target is pending or running.', tone: 'running', icon: 'spinner' },
  Completed: { label: 'Completed', description: 'Every target finished and none failed.', tone: 'success', icon: 'check' },
  CompletedWithFailures: { label: 'Completed with failures', description: 'Every target finished and at least one failed or timed out.', tone: 'warning', icon: 'alert' },
  Cancelled: { label: 'Cancelled', description: 'The run was cancelled; pending targets were not executed.', tone: 'cancelled', icon: 'stop' },
  Failed: { label: 'Failed', description: 'The runner itself hit an unexpected error.', tone: 'failed', icon: 'x' },
};

export const TARGET_STATUS_META: Record<FleetActionTargetStatus, StatusMeta> = {
  Pending: { label: 'Pending', description: 'Waiting for a concurrency slot.', tone: 'pending', icon: 'clock' },
  Skipped: { label: 'Skipped', description: 'Armada declined to act on this vessel; see the reason.', tone: 'skipped', icon: 'skip' },
  Running: { label: 'Running', description: 'The command or voyage for this vessel is in progress.', tone: 'running', icon: 'spinner' },
  Succeeded: { label: 'Succeeded', description: 'The command exited with code 0, or the voyage completed.', tone: 'success', icon: 'check' },
  Failed: { label: 'Failed', description: 'The command failed or the voyage did not land; see the reason.', tone: 'failed', icon: 'x' },
  Cancelled: { label: 'Cancelled', description: 'The target was cancelled before it finished.', tone: 'cancelled', icon: 'stop' },
  TimedOut: { label: 'Timed out', description: 'The command ran past the action timeout and was killed.', tone: 'failed', icon: 'clock' },
};

/** Skip reason codes (`FleetActionReasonCodes`) mapped to English source labels. */
export const SKIP_REASON_LABELS: Record<string, string> = {
  DirtyTree: 'Working tree has uncommitted changes',
  NoWorkingDirectory: 'Vessel has no working directory',
  NoBuildCommand: 'Vessel has no build command',
  DispatchRejected: 'Dispatch was rejected',
  NotAuthorized: 'Not authorized',
  VesselNotFound: 'Vessel no longer exists',
  HarborUnavailable: 'Preferred harbor is not connected',
};

/** Failure reason codes (`FleetActionReasonCodes`) mapped to English source labels. */
export const FAILURE_REASON_LABELS: Record<string, string> = {
  Interrupted: 'Interrupted by an Admiral restart',
  NonZeroExit: 'Command exited with a non-zero code',
  Timeout: 'Command timed out',
  GitStatusFailed: 'Could not check the working tree',
  TemplateError: 'Template could not be rendered',
  ExecutionError: 'Command could not be started',
  DispatchFailed: 'Voyage dispatch failed',
  DispatchUnavailable: 'Dispatch is unavailable',
  VoyageFailed: 'Voyage failed or did not land',
  VoyageMissing: 'Voyage no longer exists',
};

export const KIND_LABELS: Record<FleetActionKind, string> = {
  Command: 'Command',
  Mission: 'Mission',
};

export const KIND_DESCRIPTIONS: Record<FleetActionKind, string> = {
  Command: 'Runs a shell command in each vessel working directory and captures the exit code and output.',
  Mission: 'Dispatches one voyage per vessel with the rendered prompt as the mission description.',
};

/** Template variables the server renders, with English source descriptions. */
export const TEMPLATE_VARIABLES: Array<{ name: string; description: string }> = [
  { name: 'vessel.name', description: 'The vessel name' },
  { name: 'vessel.id', description: 'The vessel ID (vsl_ prefix)' },
  { name: 'vessel.defaultBranch', description: 'The vessel default branch' },
  { name: 'vessel.workingDirectory', description: 'The vessel working directory, or empty' },
  { name: 'vessel.buildCommand', description: 'The definition-of-done build command, or empty (vessels without one are skipped)' },
  { name: 'health.summary', description: 'Failing, warning, and unknown health findings; rendered on the server' },
];

/** Run statuses that can still change (auto-refresh and Cancel apply). */
export function isRunActive(status: FleetActionRunStatus | string | null | undefined): boolean {
  return status === 'Pending' || status === 'Running';
}

/** Localized label for a skip or failure reason code; unknown codes fall back to the raw code. */
export function reasonLabel(t: Translate, skipReason: string | null | undefined, failureReason: string | null | undefined): string {
  if (skipReason) return SKIP_REASON_LABELS[skipReason] ? t(SKIP_REASON_LABELS[skipReason]) : skipReason;
  if (failureReason) return FAILURE_REASON_LABELS[failureReason] ? t(FAILURE_REASON_LABELS[failureReason]) : failureReason;
  return '';
}

/** Localized badge props for a run status. */
export function runStatusBadge(t: Translate, status: FleetActionRunStatus) {
  const meta = RUN_STATUS_META[status] ?? { label: status, description: '', tone: 'info' as BadgeTone, icon: 'dot' as BadgeIcon };
  return { label: t(meta.label), title: meta.description ? t(meta.description) : undefined, tone: meta.tone, icon: meta.icon };
}

/** Localized badge props for a target status. */
export function targetStatusBadge(t: Translate, status: FleetActionTargetStatus) {
  const meta = TARGET_STATUS_META[status] ?? { label: status, description: '', tone: 'info' as BadgeTone, icon: 'dot' as BadgeIcon };
  return { label: t(meta.label), title: meta.description ? t(meta.description) : undefined, tone: meta.tone, icon: meta.icon };
}

/** Format a duration in milliseconds compactly with locale-aware numbers, e.g. "850 ms", "4.2 s", "3 min 05 s". */
export function formatDurationMs(t: Translate, locale: string, ms: number | null | undefined): string {
  if (ms === null || ms === undefined || ms < 0) return '-';
  const nf = (value: number, digits = 0) => new Intl.NumberFormat(locale, { maximumFractionDigits: digits, minimumFractionDigits: digits }).format(value);
  if (ms < 1000) return t('{{value}} ms', { value: nf(ms) });
  const seconds = ms / 1000;
  if (seconds < 60) return t('{{value}} s', { value: nf(seconds, seconds < 10 ? 1 : 0) });
  const minutes = Math.floor(seconds / 60);
  const rest = Math.round(seconds % 60);
  if (minutes < 60) return t('{{minutes}} min {{seconds}} s', { minutes: nf(minutes), seconds: nf(rest) });
  const hours = Math.floor(minutes / 60);
  return t('{{hours}} h {{minutes}} min', { hours: nf(hours), minutes: nf(minutes % 60) });
}

/** Duration between two UTC timestamps (or until now when `end` is missing and `live` is true). */
export function durationBetween(start: string | null | undefined, end: string | null | undefined, live = false): number | null {
  if (!start) return null;
  const startMs = new Date(start).getTime();
  const endMs = end ? new Date(end).getTime() : (live ? Date.now() : NaN);
  if (Number.isNaN(startMs) || Number.isNaN(endMs)) return null;
  return Math.max(0, endMs - startMs);
}
