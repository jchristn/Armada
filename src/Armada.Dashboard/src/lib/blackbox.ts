import type {
  BlackBoxAudience,
  BlackBoxCaptureMode,
  BlackBoxEntry,
  BlackBoxEnumerateQuery,
  BlackBoxResolution,
  BlackBoxState,
  BlackBoxStorageTarget,
  BlackBoxSubjectType,
  BlackBoxTriage,
} from '../types/blackbox';
import type { AskNavigationState } from './askNavigation';

// Shared helpers of the dashboard's black box surfaces (components/blackbox, pages/BlackBox*.tsx, Inbox).

/** WebSocket event prefix of the black box events (blackbox.captured, .analyzed, .resolved, .deleted). */
export const BLACKBOX_EVENT_PREFIX = 'blackbox.';

/** The black box events the dashboard follows live. */
export const BLACKBOX_LIVE_EVENTS = ['blackbox.captured', 'blackbox.analyzed', 'blackbox.resolved', 'blackbox.deleted'] as const;

/** Audiences of the FIX THIS brief, in toggle order. */
export const BLACKBOX_AUDIENCES: { key: BlackBoxAudience; label: string; tooltip: string }[] = [
  { key: 'user', label: 'User', tooltip: 'The brief for the person who owns the work: what happened and what to do next.' },
  { key: 'agent', label: 'Agent', tooltip: 'The brief to paste into a coding agent and say FIX THIS: evidence, commands and recovery steps.' },
  { key: 'maintainer', label: 'Maintainer', tooltip: 'The brief for Armada maintainers: rule ids, source pointers and what Armada could change.' },
];

export const BLACKBOX_PHASES = ['Dispatch', 'Provision', 'Execution', 'Completion', 'Review', 'Handoff', 'Landing', 'MergeQueue', 'Recovery', 'Voyage', 'External', 'Unknown'];

export const BLACKBOX_STEPS = [
  'VoyageParentNotRunnable', 'AssignmentFailed', 'NoEligibleCaptain', 'HarborUnavailable', 'MemoryPressure', 'DockProvision',
  'WorktreeCreate', 'BranchCreate', 'StartCommitRecord', 'ProcessCrash', 'UsageLimit', 'AuthFailure', 'ModelUnavailable', 'Stall',
  'MaxRuntime', 'RecoveryExhausted', 'Interrupted', 'OperatorStop', 'NoOp', 'Unfinished', 'CaptainBlocked', 'InvalidOutput',
  'ScopeViolation', 'BoundaryViolation', 'DefinitionOfDone', 'StageCheck', 'JudgeRejected', 'ReviewDenied', 'ReviewTimeout',
  'ReviewApprovedDockMissing', 'PlanParse', 'StageBranchHarden', 'DependencyCancelled', 'IntegrationBarrier', 'DockMissing',
  'TargetIntegration', 'Verification', 'DirtyCheckout', 'UnpublishedAncestry', 'Push', 'PullRequest', 'FastForward', 'LocalMerge',
  'RetryLanding', 'DeferralExpired', 'QueueResolveRepo', 'QueueMergeConflict', 'QueueTests', 'QueueProcessing', 'QueuePush',
  'RescueFailed', 'RedispatchExhausted', 'OrphanedExit', 'UnconfirmedStop', 'MissingMergeHandoff', 'SettlementFailed', 'Halted',
  'Cancelled', 'StageFailed', 'VesselDeleted', 'OperatorTransition', 'Unknown',
];

export const BLACKBOX_KINDS = [
  'Compile', 'TestFail', 'Timeout', 'LandingConflict', 'Crash', 'NoOp', 'Boundary', 'ScopeViolation', 'JudgeRejected', 'Infra',
  'ReviewDenied', 'DependencyFailed', 'MaxRuntimeExceeded', 'StallRecoveryExhausted', 'OperatorAction', 'InvalidOutput', 'Blocked', 'Unknown',
];

export const BLACKBOX_TRIAGES: BlackBoxTriage[] = ['UserConfig', 'MissionBrief', 'Transient', 'ArmadaDefect', 'External', 'Unknown'];
export const BLACKBOX_RESOLUTIONS: BlackBoxResolution[] = ['Open', 'Resolved', 'Duplicate', 'WontFix', 'ArmadaFixed'];
export const BLACKBOX_STATES: BlackBoxState[] = ['Captured', 'Enriched', 'Analyzed', 'EnrichmentFailed', 'ObjectMissing'];
export const BLACKBOX_CAPTURE_MODES: BlackBoxCaptureMode[] = ['Inline', 'Sweeper', 'Backfill'];
export const BLACKBOX_SUBJECT_TYPES: BlackBoxSubjectType[] = ['Mission', 'Voyage'];
export const BLACKBOX_STORAGE_TARGETS: BlackBoxStorageTarget[] = ['Database', 'Disk', 'S3', 'AzureBlob', 'GoogleCloud', 'Cifs', 'Nfs'];

/** Bytes per object read in the evidence viewer (the server allows up to 1 MB per read). */
export const BLACKBOX_OBJECT_CHUNK_BYTES = 64 * 1024;

/** The Ask Armada prompt for a failed mission or voyage (MISSION_BLACKBOX_PLAN.md 10.3); sent to the captain as written. */
export function blackBoxAskPrompt(subjectId: string, kind: 'mission' | 'voyage' = 'mission'): string {
  return `Show me the pertinent details from the black box for ${kind} ${subjectId} and give me your opinion on why it failed and what could have been done differently`;
}

/**
 * The Ask Armada prompt for an entry: the mission wording when the entry has a mission, otherwise the voyage wording
 * (with the entry id when it names no voyage either).
 */
export function blackBoxEntryAskPrompt(subject: { id: string; missionId?: string | null; voyageId?: string | null }): string {
  if (subject.missionId) return blackBoxAskPrompt(subject.missionId);
  return blackBoxAskPrompt(subject.voyageId || subject.id, 'voyage');
}

/** Ask Armada URL with the prompt prefilled (the Ask page reads ?prompt=; router state carries it too). */
export function blackBoxAskHref(prompt: string): string {
  return `/ask?prompt=${encodeURIComponent(prompt)}`;
}

/** Router state that opens Ask Armada on a new conversation with the prompt in the message box. */
export function blackBoxAskState(prompt: string): AskNavigationState & { prompt: string } {
  return { draft: prompt, prompt };
}

/** True while the entry was captured but the analysis has not finished (Captured or Enriched). */
export function isBlackBoxPending(entry: Pick<BlackBoxEntry, 'state'> | null | undefined): boolean {
  return !!entry && (entry.state === 'Captured' || entry.state === 'Enriched');
}

/** File name of a downloaded brief. */
export function blackBoxBriefFileName(entryId: string, audience: BlackBoxAudience): string {
  return `${entryId}-${audience}.md`;
}

/** File name of a brief downloaded from the entries table (the server's default audience). */
export function blackBoxEntryBriefFileName(entryId: string): string {
  return `blackbox-${entryId}.md`;
}

/** Human-readable byte count (B, KB, MB, GB). */
export function formatBlackBoxBytes(bytes: number | null | undefined): string {
  if (bytes == null || !Number.isFinite(bytes)) return '-';
  if (bytes < 1024) return `${bytes} B`;
  const units = ['KB', 'MB', 'GB', 'TB'];
  let value = bytes / 1024;
  let unit = 0;
  while (value >= 1024 && unit < units.length - 1) { value /= 1024; unit++; }
  return `${value.toFixed(value >= 10 ? 0 : 1)} ${units[unit]}`;
}

/** True when a WebSocket event's data names one of the ids (entry, mission or voyage); events without data match. */
export function blackBoxEventMatches(data: unknown, ids: ReadonlyArray<string | null | undefined>): boolean {
  const wanted = ids.filter((id): id is string => !!id);
  if (wanted.length === 0) return true;
  if (data === null || data === undefined) return true;
  let text: string;
  try {
    text = typeof data === 'string' ? data : JSON.stringify(data);
  } catch {
    return true;
  }
  return wanted.some((id) => text.includes(id));
}

/** The filter fields of a query that are set (paging removed), for saving and for the URL. */
export function activeBlackBoxFilters(query: BlackBoxEnumerateQuery & { search?: string }): BlackBoxEnumerateQuery & { search?: string } {
  const result: Record<string, unknown> = {};
  for (const [key, value] of Object.entries(query)) {
    if (key === 'pageNumber' || key === 'pageSize') continue;
    if (value === undefined || value === null || value === '') continue;
    result[key] = value;
  }
  return result as BlackBoxEnumerateQuery & { search?: string };
}

/** HTTP status of a failed request (ApiError.status), or null. */
export function blackBoxErrorStatus(err: unknown): number | null {
  if (err && typeof err === 'object' && typeof (err as { status?: unknown }).status === 'number') return (err as { status: number }).status;
  return null;
}

/** Machine-readable code of a failed black box request (ApiError.data.code, for example BlackBoxNotFound), or null. */
export function blackBoxErrorCode(err: unknown): string | null {
  if (!err || typeof err !== 'object') return null;
  const data = (err as { data?: unknown }).data;
  if (data && typeof data === 'object' && typeof (data as { code?: unknown }).code === 'string') return (data as { code: string }).code;
  return null;
}
