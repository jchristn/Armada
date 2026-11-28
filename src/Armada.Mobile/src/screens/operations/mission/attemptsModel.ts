import type { MissionAttempt, StageFinding, StageSubmission } from '@dashboard/types/models';
import type { StatusTone } from '../../../components/ui/StatusBadge';

/** Pure: badge tone of an attempt disposition (StageDispositionEnum). */
export function dispositionTone(disposition: string | null | undefined): StatusTone {
  switch (disposition) {
    case 'Completed': return 'success';
    case 'Failed': return 'failed';
    case 'Relaunched':
    case 'OutputRepair':
    case 'Escalated':
    case 'Interrupted': return 'warning';
    case 'Pending': return 'running';
    default: return 'cancelled';
  }
}

/** Pure: badge tone of a finding severity (Critical, High, Medium, Low, Info; anything else is neutral). */
export function severityTone(severity: string | null | undefined): StatusTone {
  switch ((severity ?? '').toLowerCase()) {
    case 'critical':
    case 'high': return 'failed';
    case 'medium': return 'warning';
    case 'low':
    case 'info': return 'info';
    default: return 'cancelled';
  }
}

/** Pure: a short duration ("850 ms", "42 s", "3 m 05 s", "1 h 02 m"); null when unknown. */
export function formatAttemptDuration(ms: number | null | undefined): string | null {
  if (ms == null || !Number.isFinite(ms) || ms < 0) return null;
  if (ms < 1000) return `${Math.round(ms)} ms`;
  const total = Math.round(ms / 1000);
  if (total < 60) return `${total} s`;
  const hours = Math.floor(total / 3600);
  const minutes = Math.floor((total % 3600) / 60);
  const seconds = total % 60;
  if (hours > 0) return `${hours} h ${String(minutes).padStart(2, '0')} m`;
  return `${minutes} m ${String(seconds).padStart(2, '0')} s`;
}

/**
 * Pure: the labels that describe what happened in an attempt beyond its disposition (failure kind, runtime failure,
 * contract repair, escalation, Judge verdict). Returned as English source strings for t(); values from the server
 * (enum names) are shown as sent.
 */
export function attemptFacts(attempt: MissionAttempt): { label: string; value: string }[] {
  const facts: { label: string; value: string }[] = [];
  if (attempt.failureKind) facts.push({ label: 'Failure kind', value: attempt.failureKind });
  if (attempt.runtimeFailureKind && attempt.runtimeFailureKind !== 'None') facts.push({ label: 'Runtime failure', value: attempt.runtimeFailureKind });
  if (attempt.contractRepairKind && attempt.contractRepairKind !== 'None') facts.push({ label: 'Repair', value: attempt.contractRepairKind });
  else if (attempt.isRepair) facts.push({ label: 'Repair', value: 'Rerun' });
  if (attempt.judgeVerdict) facts.push({ label: 'Judge verdict', value: attempt.judgeVerdict });
  return facts;
}

/** Pure: attempts newest first (the server sends them oldest first); ties keep the server order. */
export function attemptsNewestFirst(attempts: readonly MissionAttempt[]): MissionAttempt[] {
  return attempts.map((a, i) => ({ a, i })).sort((x, y) => (y.a.attempt - x.a.attempt) || (y.a.reworkRound - x.a.reworkRound) || (y.i - x.i)).map((x) => x.a);
}

/** A submission reduced to what the findings section shows. */
export interface SubmissionView {
  attemptId: string;
  persona: string;
  kind: string;
  source: string;
  createdUtc: string;
  /** Verdict (PASS, FAIL, NEEDS_REVISION) or result status (Completed, Blocked), when the submission has one. */
  outcome: string | null;
  summary: string | null;
  findings: StageFinding[];
}

/**
 * Pure: the submission that decided each attempt (the server lists newest first and the latest submission of an
 * attempt decides it), in the server's order. Plans carry no findings and are shown with their kind only.
 */
export function decidingSubmissions(submissions: readonly StageSubmission[]): SubmissionView[] {
  const seen = new Set<string>();
  const out: SubmissionView[] = [];
  for (const s of submissions) {
    const key = s.attemptId || `${s.createdUtc}-${out.length}`;
    if (seen.has(key)) continue;
    seen.add(key);
    const verdict = s.submission?.verdict ?? null;
    const result = s.submission?.result ?? null;
    out.push({
      attemptId: s.attemptId,
      persona: s.persona,
      kind: s.kind,
      source: s.source,
      createdUtc: s.createdUtc,
      outcome: verdict?.verdict ?? result?.status ?? null,
      summary: verdict?.summary ?? result?.summary ?? null,
      findings: verdict?.findings ?? result?.findings ?? [],
    });
  }
  return out;
}

/** Pure: "file:line", "file", or null for a finding's location. */
export function findingLocation(finding: StageFinding): string | null {
  if (!finding.file) return null;
  return finding.line != null && finding.line > 0 ? `${finding.file}:${finding.line}` : finding.file;
}
