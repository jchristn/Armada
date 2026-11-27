import type { MissionAttempt, StageFinding, StageSubmission } from '../types/models';

type Translate = (key: string, values?: Record<string, string | number>) => string;

/** Short duration from milliseconds: 850 ms, 42s, 3m 10s, 1h 5m. Dash when unknown. */
export function formatDurationMs(ms: number | null | undefined): string {
  if (ms === null || ms === undefined || !Number.isFinite(ms) || ms < 0) return '-';
  if (ms < 1000) return `${Math.round(ms)} ms`;
  const totalSeconds = Math.round(ms / 1000);
  if (totalSeconds < 60) return `${totalSeconds}s`;
  const minutes = Math.floor(totalSeconds / 60);
  const seconds = totalSeconds % 60;
  if (minutes < 60) return seconds ? `${minutes}m ${seconds}s` : `${minutes}m`;
  const hours = Math.floor(minutes / 60);
  const rest = minutes % 60;
  return rest ? `${hours}h ${rest}m` : `${hours}h`;
}

/** A 0..1 rate as a whole percent ("87%"); dash with no attempts. */
export function formatRate(rate: number | null | undefined, attempts = 1): string {
  if (!attempts || rate === null || rate === undefined || !Number.isFinite(rate)) return '-';
  return `${Math.round(rate * 100)}%`;
}

/** Failure kind counts, most frequent first, as "Kind 3, Other 1"; empty when none. */
export function summarizeKinds(kinds: Record<string, number> | null | undefined, limit = 3): string {
  const entries = Object.entries(kinds ?? {}).filter(([, n]) => n > 0).sort((a, b) => b[1] - a[1] || a[0].localeCompare(b[0]));
  const shown = entries.slice(0, limit).map(([k, n]) => `${k} ${n}`);
  if (entries.length > limit) shown.push(`+${entries.length - limit}`);
  return shown.join(', ');
}

/**
 * Tag class for a StageDispositionEnum value: completed, failed, cancelled, pending, or warning (Relaunched,
 * OutputRepair, Escalated, Interrupted).
 */
export function dispositionClass(disposition: string): string {
  if (disposition === 'Completed') return 'completed';
  if (disposition === 'Failed') return 'failed';
  if (disposition === 'Cancelled') return 'cancelled';
  if (disposition === 'Pending') return 'pending';
  return 'warning';
}

/** What happened after the attempt, for the timeline's "Then" column. */
export function attemptNextStep(a: MissionAttempt, t: Translate): string {
  if (a.escalated) return a.nextCaptainId ? t('Escalated to {{captain}}', { captain: a.nextCaptainId }) : t('Escalated');
  if (a.isRepair) return t('Repair rerun');
  if (a.contractRepairKind && a.contractRepairKind !== 'None') return t('Repaired: {{kind}}', { kind: a.contractRepairKind });
  return '-';
}

/** True when the finding blocks the stage. */
export function isBlockingFinding(f: StageFinding): boolean {
  return f.severity.toLowerCase() === 'blocking';
}

/** Findings of a submission's result or verdict, blocking first. */
export function submissionFindings(s: StageSubmission): StageFinding[] {
  const list = [...(s.submission.result?.findings ?? []), ...(s.submission.verdict?.findings ?? [])];
  return list.sort((a, b) => Number(isBlockingFinding(b)) - Number(isBlockingFinding(a)));
}

/** Headline of a submission: the verdict or result status plus its summary. */
export function submissionHeadline(s: StageSubmission): { label: string; summary: string } {
  if (s.submission.verdict) return { label: s.submission.verdict.verdict, summary: s.submission.verdict.summary ?? '' };
  if (s.submission.result) return { label: s.submission.result.status, summary: s.submission.result.summary };
  return { label: s.kind, summary: '' };
}

/** ISO start of a window of `days` days ending now. */
export function windowStartUtc(days: number, now: Date = new Date()): string {
  return new Date(now.getTime() - days * 86400000).toISOString();
}
