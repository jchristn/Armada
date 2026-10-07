import type { BadgeIcon, BadgeTone } from './badgeTypes';
import type { Job, VesselImportBatch, VesselImportBatchStatus, VesselImportCandidateStatus, VesselImportCategorizationStatus, VesselImportOutcome } from '../types/models';
import type { StatusMeta, Translate } from './fleetActionLabels';

export const CANDIDATE_STATUSES: VesselImportCandidateStatus[] = ['New', 'AlreadyOnboarded', 'Worktree', 'ArmadaManaged', 'NotFound', 'NotGit', 'AccessDenied'];
export const BATCH_STATUSES: VesselImportBatchStatus[] = ['Discovering', 'Discovered', 'Importing', 'Completed', 'CompletedWithFailures', 'Failed'];
export const OUTCOMES: VesselImportOutcome[] = ['Pending', 'Created', 'SkippedExisting', 'SkippedNotSelected', 'Failed'];

/** Candidate statuses that the import endpoint will create a vessel for when selected. */
export const IMPORTABLE_STATUSES: VesselImportCandidateStatus[] = ['New', 'Worktree'];

export const CANDIDATE_STATUS_META: Record<VesselImportCandidateStatus, StatusMeta> = {
  New: { label: 'New', description: 'A git repository that is not a vessel yet. Selected by default.', tone: 'success', icon: 'dot' },
  AlreadyOnboarded: { label: 'Already a vessel', description: 'A vessel with this working directory or remote already exists.', tone: 'info', icon: 'link' },
  Worktree: { label: 'Worktree', description: 'The .git entry is a file, so this is a worktree or submodule. Not selected by default.', tone: 'warning', icon: 'alert' },
  ArmadaManaged: { label: 'Managed by Armada', description: 'Armada repos, docks, or data directory. Never imported.', tone: 'skipped', icon: 'lock' },
  NotFound: { label: 'Not found', description: 'The path does not exist on the Admiral host.', tone: 'failed', icon: 'x' },
  NotGit: { label: 'Not a repository', description: 'The directory exists but contains no git repository within the search depth.', tone: 'skipped', icon: 'skip' },
  AccessDenied: { label: 'Access denied', description: 'The directory could not be read by the Admiral.', tone: 'failed', icon: 'lock' },
};

export const OUTCOME_META: Record<VesselImportOutcome, StatusMeta> = {
  Pending: { label: 'Pending', description: 'Not processed yet.', tone: 'pending', icon: 'clock' },
  Created: { label: 'Created', description: 'A vessel was created.', tone: 'success', icon: 'check' },
  SkippedExisting: { label: 'Skipped (exists)', description: 'A matching vessel already exists, so nothing was created.', tone: 'info', icon: 'link' },
  SkippedNotSelected: { label: 'Not selected', description: 'The candidate was not selected for import.', tone: 'skipped', icon: 'skip' },
  Failed: { label: 'Failed', description: 'The vessel could not be created; see the reason.', tone: 'failed', icon: 'x' },
};

export const BATCH_STATUS_META: Record<VesselImportBatchStatus, StatusMeta> = {
  Discovering: { label: 'Discovering', description: 'Repositories are being scanned in the background.', tone: 'running', icon: 'spinner' },
  Discovered: { label: 'Discovered', description: 'Candidates were found; nothing has been imported yet.', tone: 'pending', icon: 'clock' },
  Importing: { label: 'Importing', description: 'Vessels are being created in the background.', tone: 'running', icon: 'spinner' },
  Completed: { label: 'Completed', description: 'The import finished without failures.', tone: 'success', icon: 'check' },
  CompletedWithFailures: { label: 'Completed with failures', description: 'The import finished and at least one item failed.', tone: 'warning', icon: 'alert' },
  Failed: { label: 'Failed', description: 'The import as a whole failed or the background job was cancelled.', tone: 'failed', icon: 'x' },
};

export const CATEGORIZATION_STATUS_META: Record<VesselImportCategorizationStatus, StatusMeta> = {
  None: { label: 'No fleet recommendations', description: 'Fleet categorization was not requested for this import.', tone: 'skipped', icon: 'skip' },
  Pending: { label: 'Fleet recommendations queued', description: 'A captain will analyze the repositories when the import finishes.', tone: 'pending', icon: 'clock' },
  Running: { label: 'Recommending fleets', description: 'A captain is analyzing the repositories in the background.', tone: 'running', icon: 'spinner' },
  Completed: { label: 'Fleets recommended', description: 'Recommendations are ready to review and apply.', tone: 'info', icon: 'dot' },
  Failed: { label: 'Fleet recommendation failed', description: 'The captain could not produce recommendations; see the error and retry.', tone: 'failed', icon: 'x' },
  Applied: { label: 'Fleets applied', description: 'Recommended fleets were created or reused and the vessels assigned.', tone: 'success', icon: 'check' },
};

/** Background job kinds mapped to friendly English source names for the activity indicator. */
export const JOB_KIND_LABELS: Record<string, string> = {
  VesselDiscovery: 'Discovering repositories',
  VesselImport: 'Importing repositories',
  FleetCategorization: 'Recommending fleets',
  Report: 'Building a report',
  Cleanup: 'Cleaning up',
  Sync: 'Syncing',
  Generic: 'Background task',
};

/** Item outcome reason codes mapped to English source labels. */
export const OUTCOME_REASON_LABELS: Record<string, string> = {
  VesselAlreadyExists: 'A matching vessel already exists',
  NotSelected: 'Not selected for import',
  NotImportable: 'This candidate cannot be imported',
  PathMissing: 'The directory no longer exists',
  CreateFailed: 'Vessel creation failed',
  Cancelled: 'The import was cancelled first',
};

/** Discovery hint codes mapped to English source explanations. */
export const HINT_LABELS: Record<string, string> = {
  PathNotVisibleToAdmiral: 'None of the requested paths exist on the Admiral host. If the Admiral runs in a container it cannot see your host directories: mount them into the container, or run discovery through a Harbor on that machine.',
  CandidateLimitReached: 'Discovery stopped at the candidate limit, so the list is truncated. Narrow the roots or lower the max depth and discover again to see the rest.',
};

/** Error codes from import endpoints (`data.code`) mapped to English source explanations. */
export const IMPORT_ERROR_LABELS: Record<string, string> = {
  InvalidRequest: 'The request was not valid. Check the paths and try again.',
  HarborNotSupported: 'Discovery through a Harbor is not supported yet.',
  PathNotAllowed: 'This path is outside the allowed import roots. An administrator can add roots under Settings > Import.',
  DirectoryNotFound: 'That directory does not exist on the Admiral host.',
  BatchNotFound: 'This import batch no longer exists. Run discovery again.',
  BatchBusy: 'This batch is already being imported. Wait for it to finish or open it from the import history.',
  CategorizationCaptainRequired: 'Choose a captain to recommend fleets, or turn fleet recommendations off.',
  CategorizationCaptainNotFound: 'The selected categorization captain no longer exists. Choose another captain.',
};

function badge(t: Translate, meta: StatusMeta | undefined, fallback: string) {
  const m = meta ?? { label: fallback, description: '', tone: 'info' as BadgeTone, icon: 'dot' as BadgeIcon };
  return { label: t(m.label), title: m.description ? t(m.description) : undefined, tone: m.tone, icon: m.icon };
}

export function candidateStatusBadge(t: Translate, status: VesselImportCandidateStatus) {
  return badge(t, CANDIDATE_STATUS_META[status], status);
}

export function outcomeBadge(t: Translate, outcome: VesselImportOutcome) {
  return badge(t, OUTCOME_META[outcome], outcome);
}

export function batchStatusBadge(t: Translate, status: VesselImportBatchStatus) {
  return badge(t, BATCH_STATUS_META[status], status);
}

export function categorizationBadge(t: Translate, status: VesselImportCategorizationStatus) {
  return badge(t, CATEGORIZATION_STATUS_META[status], status);
}

/** True while discovery, the import, or fleet categorization of a batch is still running. */
export function isBatchBusy(batch: VesselImportBatch | null | undefined): boolean {
  if (!batch) return false;
  return batch.status === 'Discovering' || batch.status === 'Importing' || isCategorizing(batch);
}

/** True while fleet categorization of a batch is queued or running. */
export function isCategorizing(batch: VesselImportBatch | null | undefined): boolean {
  return !!batch && (batch.categorizationStatus === 'Pending' || batch.categorizationStatus === 'Running');
}

/** Import batch identifier mentioned in a job name (import, discovery, and categorization jobs name their batch). */
export function jobBatchId(job: Pick<Job, 'name'>): string | null {
  const match = /vib_[A-Za-z0-9_-]+/.exec(job.name || '');
  return match ? match[0] : null;
}

/** Friendly, translated name for a background job. */
export function jobFriendlyName(t: Translate, job: Pick<Job, 'kind' | 'name'>): string {
  if (job.kind === 'Report' && job.name === 'Vessel health evaluation') return t('Evaluating vessel health');
  const label = JOB_KIND_LABELS[job.kind];
  return label ? t(label) : job.name;
}

/** Dashboard route for a background job: import-related jobs open their batch, everything else the Jobs page. */
export function jobRoute(job: Pick<Job, 'kind' | 'name'>): string {
  const batchId = jobBatchId(job);
  if (batchId && (job.kind === 'VesselDiscovery' || job.kind === 'VesselImport' || job.kind === 'FleetCategorization')) {
    return `/vessels/import?batch=${encodeURIComponent(batchId)}`;
  }
  return '/jobs';
}

export function outcomeReasonLabel(t: Translate, code: string | null | undefined): string {
  if (!code) return '';
  return OUTCOME_REASON_LABELS[code] ? t(OUTCOME_REASON_LABELS[code]) : code;
}

export function hintLabel(t: Translate, code: string, fallback: string): string {
  return HINT_LABELS[code] ? t(HINT_LABELS[code]) : fallback;
}

export function importErrorLabel(t: Translate, code: string | null, fallback: string): string {
  if (code && IMPORT_ERROR_LABELS[code]) return t(IMPORT_ERROR_LABELS[code]);
  return fallback;
}
