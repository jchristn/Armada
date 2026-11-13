import { apiErrorCode, isApiStatus } from '@dashboard/api/client';
import type {
  BlackBoxEntry,
  BlackBoxEntrySummary,
  BlackBoxRecoveryPath,
  BlackBoxResolution,
  BlackBoxTriage,
} from '@dashboard/types/blackbox';
import type { StatusTone } from '../../components/ui/StatusBadge';

// Pure helpers for the black box surfaces (MISSION_BLACKBOX_PLAN.md 11.5): which subjects can have an entry, the
// canonical Ask Armada prompt, badge tones, and the bounded lists the panel shows.

/** Mission statuses that a failure, stop or cancellation leaves behind (an entry is looked up only for these). */
export const BLACKBOX_MISSION_STATUSES: readonly string[] = ['Failed', 'LandingFailed', 'Cancelled'];

/** Voyage statuses that can carry a voyage entry. */
export const BLACKBOX_VOYAGE_STATUSES: readonly string[] = ['Failed', 'Cancelled'];

/** Failure kinds (MissionFailureKindEnum) for the list filter. */
export const BLACKBOX_FAILURE_KINDS: readonly string[] = [
  'Compile', 'TestFail', 'Timeout', 'LandingConflict', 'Crash', 'NoOp', 'Boundary', 'ScopeViolation', 'JudgeRejected',
  'Infra', 'ReviewDenied', 'DependencyFailed', 'MaxRuntimeExceeded', 'StallRecoveryExhausted', 'OperatorAction',
  'InvalidOutput', 'Blocked', 'Unknown',
];

/** Resolutions an operator can set from the app (Unknown is a wire value only). */
export const BLACKBOX_RESOLUTIONS: readonly BlackBoxResolution[] = ['Resolved', 'Duplicate', 'WontFix', 'ArmadaFixed', 'Open'];

/** Notes are 1 to 8000 characters (BlackBoxService.AddNoteAsync). */
export const BLACKBOX_NOTE_MAX = 8000;

/** Rows of a file or directory list shown before "and N more". */
export const BLACKBOX_LIST_LIMIT = 10;

/** The black box list's page size. */
export const BLACKBOX_PAGE_SIZE = 25;

/** True when a mission in this status may have a black box entry. */
export function missionMayHaveBlackBox(status: string | null | undefined, failureReason?: string | null): boolean {
  return BLACKBOX_MISSION_STATUSES.includes(status ?? '') || !!failureReason;
}

/** True when a voyage in this status may have a black box entry. */
export function voyageMayHaveBlackBox(status: string | null | undefined): boolean {
  return BLACKBOX_VOYAGE_STATUSES.includes(status ?? '');
}

/** True when a read failed because the subject has no entry (or the caller cannot see it): 404 BlackBoxNotFound. */
export function isBlackBoxNotFound(error: unknown): boolean {
  return isApiStatus(error, 404) && (apiErrorCode(error) === null || apiErrorCode(error) === 'BlackBoxNotFound');
}

/** The subject an entry describes, for the Ask prompt and titles. */
export function entrySubject(entry: Pick<BlackBoxEntrySummary, 'id' | 'subjectType' | 'missionId' | 'voyageId'>): { kind: 'mission' | 'voyage'; id: string } {
  if (entry.missionId) return { kind: 'mission', id: entry.missionId };
  if (entry.voyageId) return { kind: 'voyage', id: entry.voyageId };
  return { kind: entry.subjectType === 'Voyage' ? 'voyage' : 'mission', id: entry.id };
}

/**
 * The canonical "Ask Armada about this failure" prompt (MISSION_BLACKBOX_PLAN.md 10.3), the same text every surface
 * pre-fills. Kept in English: it is a message to the captain, not interface text.
 */
export function blackBoxAskPrompt(entry: Pick<BlackBoxEntrySummary, 'id' | 'subjectType' | 'missionId' | 'voyageId'>): string {
  const subject = entrySubject(entry);
  return `Show me the pertinent details from the black box for ${subject.kind} ${subject.id} and give me your opinion on why it failed and what could have been done differently.`;
}

/** The Ask route that opens a new conversation with `prompt` in the composer. */
export function askPrefillRoute(prompt: string): string {
  return `/ask?prompt=${encodeURIComponent(prompt)}`;
}

/** Badge tone of a triage verdict. */
export function triageTone(triage: BlackBoxTriage | null | undefined): StatusTone {
  switch (triage) {
    case 'ArmadaDefect': return 'failed';
    case 'UserConfig':
    case 'MissionBrief': return 'warning';
    case 'Transient':
    case 'External': return 'info';
    default: return 'skipped';
  }
}

/** Badge tone of a resolution. */
export function resolutionTone(resolution: BlackBoxResolution | null | undefined): StatusTone {
  switch (resolution) {
    case 'Open': return 'warning';
    case 'Resolved':
    case 'ArmadaFixed': return 'success';
    default: return 'skipped';
  }
}

/** Splits camel-case wire values for display ("LandingFailed" to "Landing Failed"). */
export function spaced(value: string | null | undefined): string {
  return (value ?? '').replace(/([a-z])([A-Z])/g, '$1 $2');
}

/** Recovery paths in the advisor's rank order (lowest rank first). */
export function rankedRecoveryPaths(entry: BlackBoxEntry | null | undefined): BlackBoxRecoveryPath[] {
  const paths = entry?.record?.analysis?.recoveryPaths ?? [];
  return [...paths].sort((a, b) => a.rank - b.rank);
}

/** True when running the path can lose work (the path or any of its steps is marked dataLossRisk). */
export function pathLosesData(path: BlackBoxRecoveryPath): boolean {
  return !!path.dataLossRisk || (path.steps ?? []).some((s) => !!s.dataLossRisk);
}

/** True when the path has something the server can run (a recorded action on the path or a step). */
export function pathIsRunnable(path: BlackBoxRecoveryPath): boolean {
  return !!path.action || (path.steps ?? []).some((s) => !!s.action);
}

/** The first `limit` values and how many more there are (`total` is the server's count when it truncated). */
export function bounded<T>(values: readonly T[] | null | undefined, total?: number | null, limit = BLACKBOX_LIST_LIMIT): { shown: T[]; more: number } {
  const list = values ?? [];
  const shown = list.slice(0, limit);
  const count = Math.max(total ?? 0, list.length);
  return { shown, more: Math.max(0, count - shown.length) };
}

/** What happened, in one paragraph: the analysis summary, else the entry summary, else the reason excerpt. */
export function whatHappened(entry: BlackBoxEntry): string {
  return entry.record?.analysis?.summary || entry.summary || entry.reasonExcerpt || entry.record?.classification?.rawReason || '';
}

/** The list row subtitle of an entry: subject, vessel, phase and step. */
export function entryRowSubtitle(entry: BlackBoxEntrySummary): string {
  const subject = entrySubject(entry);
  return [
    subject.id,
    entry.vesselName ?? entry.vesselId ?? null,
    `${spaced(entry.phase)} / ${spaced(entry.step)}`,
  ].filter(Boolean).join(' - ');
}
