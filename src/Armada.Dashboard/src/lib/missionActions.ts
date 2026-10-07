/**
 * Mission rules shared by the dashboard's Missions and MissionDetail pages and the mobile app: which statuses the
 * lists filter and transition to, which actions a status allows (land, retry landing, resolve review, mark
 * complete), the landing readiness pill, runtime formatting, and the assignment-blocker titles. Pure: no React.
 */
import type { LandingPreviewResult, Mission, MissionAssignmentBlockerReason, MissionSummary } from '../types/models';

/** Statuses offered by the Missions list's status filter and row Transition Status modal. */
export const MISSION_LIST_STATUSES = ['Pending', 'Assigned', 'InProgress', 'WorkProduced', 'Testing', 'Review', 'Complete', 'Failed', 'Cancelled'];

/** Statuses offered by the mission detail's Transition Status modal. */
export const MISSION_TRANSITION_STATUSES = [
  'Pending', 'Assigned', 'InProgress', 'WorkProduced', 'Testing', 'Review', 'Complete', 'Failed', 'LandingFailed', 'Cancelled',
];

/** Statuses in which a mission log no longer grows (the log viewer stops following). */
export const MISSION_LOG_COMPLETED_STATUSES = ['Complete', 'Failed', 'Cancelled', 'WorkProduced', 'LandingFailed', 'Review'];

export function isMissionLogCompleted(status: string | null | undefined): boolean {
  return !!status && MISSION_LOG_COMPLETED_STATUSES.includes(status);
}

/** The Missions list offers Retry Landing on these statuses. */
export function canRetryLanding(status: string | null | undefined): boolean {
  return status === 'WorkProduced' || status === 'LandingFailed';
}

/** Restart is offered on these statuses by the dashboard home's recent missions. */
export function canRestartFromHome(status: string | null | undefined): boolean {
  return status === 'Failed' || status === 'Cancelled' || status === 'LandingFailed';
}

export interface MissionColumnFilters {
  title: string;
  status: string;
  branch: string;
}

/** The Missions table's per-column text filters (case-insensitive substring; empty filters match). */
export function matchesMissionColumnFilters(
  mission: Pick<MissionSummary, 'title' | 'status' | 'branchName'>,
  filters: MissionColumnFilters,
): boolean {
  return (!filters.title || mission.title.toLowerCase().includes(filters.title.toLowerCase()))
    && (!filters.status || (mission.status ?? '').toLowerCase().includes(filters.status.toLowerCase()))
    && (!filters.branch || (mission.branchName ?? '').toLowerCase().includes(filters.branch.toLowerCase()));
}

/** A translate function (the clients' `t`). */
export type MissionTranslate = (text: string, params?: Record<string, string | number>) => string;

/** A mission's total runtime as the detail page shows it ("N/A", "12.5s", "3m 4s", "1h 2m"). */
export function formatMissionDuration(totalRuntimeMs: number | null | undefined, t: MissionTranslate): string {
  if (totalRuntimeMs == null || totalRuntimeMs < 0) return t('N/A');
  const totalSeconds = totalRuntimeMs / 1000;
  if (totalSeconds < 60) return t('{{seconds}}s', { seconds: totalSeconds.toFixed(1) });

  const hours = Math.floor(totalSeconds / 3600);
  const minutes = Math.floor((totalSeconds % 3600) / 60);
  const seconds = Math.floor(totalSeconds % 60);

  if (hours > 0) return t('{{hours}}h {{minutes}}m', { hours, minutes });
  if (minutes > 0 && seconds > 0) return t('{{minutes}}m {{seconds}}s', { minutes, seconds });
  return t('{{minutes}}m', { minutes });
}

/** Tone and English label of the landing preview pill. */
export interface LandingPill {
  tone: 'ready' | 'warning';
  label: string;
}

/** What the mission detail page may offer for a mission, given its landing preview. */
export interface MissionLandingState {
  /** Review gate waiting: offer Resolve Review (approve / conditional / more work / deny). */
  canResolveReview: boolean;
  /** In Review without a gate: offer Mark Complete. */
  canMarkComplete: boolean;
  /** The status allows landing. */
  canLand: boolean;
  /** Landing failed before: the land action reads "Retry Landing" instead of "Land". */
  isRetry: boolean;
  /** Landing Mode None: Armada never lands the branch. */
  manualLandingOnly: boolean;
  /** Offer "Merge in Manage Branches" instead of Land. */
  showManualMerge: boolean;
  /** Offer Land / Retry Landing. */
  showLand: boolean;
  pill: LandingPill;
}

export function missionLandingState(
  mission: Pick<Mission, 'status' | 'requiresReview' | 'vesselId' | 'branchName'>,
  preview: Pick<LandingPreviewResult, 'manualLandingOnly' | 'isReadyToLand'> | null | undefined,
): MissionLandingState {
  const canResolveReview = mission.status === 'Review' && mission.requiresReview;
  const canMarkComplete = mission.status === 'Review' && !mission.requiresReview;
  // A mission is landable when work is produced, a prior landing failed, or it sits in Review with no explicit
  // review gate to resolve (requiresReview=false) -- in which case landing is how it graduates out of Review.
  const canLand = mission.status === 'WorkProduced' || mission.status === 'LandingFailed'
    || (mission.status === 'Review' && !mission.requiresReview);
  // Landing Mode None: Armada never lands the branch, so Land can only be refused. Offer the merge instead.
  const manualLandingOnly = !!preview?.manualLandingOnly;
  const showManualMerge = canLand && manualLandingOnly && !!mission.vesselId && !!mission.branchName;
  const showLand = canLand && !showManualMerge;
  const landableStatus = mission.status === 'WorkProduced' || mission.status === 'LandingFailed' || mission.status === 'PullRequestOpen'
    || (mission.status === 'Review' && !mission.requiresReview);
  let pill: LandingPill;
  if (mission.status === 'Complete') pill = { tone: 'ready', label: 'Landed' };
  else if (!landableStatus) pill = { tone: 'warning', label: 'Not Ready Yet' };
  else if (manualLandingOnly) pill = { tone: 'warning', label: 'Merge By Hand' };
  else if (preview?.isReadyToLand) pill = { tone: 'ready', label: 'Ready To Land' };
  else pill = { tone: 'warning', label: 'Needs Review' };
  return {
    canResolveReview,
    canMarkComplete,
    canLand,
    isRetry: mission.status === 'LandingFailed',
    manualLandingOnly,
    showManualMerge,
    showLand,
    pill,
  };
}

/** Review verdicts of the Resolve Review modal. */
export type ReviewVerdict = 'approve' | 'conditional' | 'morework' | 'deny';

/** Conditional approval and "more work required" need feedback; approve and deny do not. */
export function reviewVerdictNeedsComment(verdict: ReviewVerdict): boolean {
  return verdict === 'conditional' || verdict === 'morework';
}

/** English titles of the assignment-blocker reasons (why a Pending mission is waiting). */
export const ASSIGNMENT_BLOCKER_TITLES: Record<MissionAssignmentBlockerReason, string> = {
  AwaitingDispatch: 'About to start',
  VesselMissing: 'No vessel',
  VesselMisconfigured: 'Vessel needs attention',
  DependencyNotFinished: 'Waiting for an earlier mission',
  DependencyHandoffPending: 'Preparing the handoff',
  WaitingForVoyageWorkers: 'Waiting for the other missions in this voyage',
  VesselBroadScopeMissionActive: 'Vessel is held by a broad-scope mission',
  BroadScopeWaitingForVessel: 'Waiting for the vessel to be free',
  VesselConcurrencyLimit: 'Vessel runs one mission at a time',
  NoCaptains: 'No captains',
  NoIdleCaptain: 'Waiting for a captain',
  NoEligibleCaptain: 'No captain can take this mission',
};

export function assignmentBlockerTitle(reason: string): string {
  return (ASSIGNMENT_BLOCKER_TITLES as Record<string, string>)[reason] ?? 'Waiting';
}
