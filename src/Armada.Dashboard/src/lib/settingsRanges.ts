import type {
  CliPermissionSettingsData,
  FleetActionSettingsData,
  RepositoryHealthSettings,
  RepositoryHealthThresholds,
  RetentionSettingsData,
  VesselImportSettingsData,
} from '../types/models';
import { PROMPT_TIMEOUT_RANGE } from './cliPermissions';
import { msg } from './health/healthText';

/**
 * Defaults, ranges, and validation of the Server settings sections (Vessel Import, Fleet Actions, Retention, CLI tool
 * permissions, Repository Health), shared by the dashboard sections and the mobile app. Ranges mirror the backend
 * clamps.
 */

export interface NumberField {
  key: string;
  min: number;
  max: number;
}

export const IMPORT_RANGES: Record<'maxDepth' | 'inlineBatchLimit' | 'categorizationTimeoutMinutes', NumberField> = {
  maxDepth: { key: 'maxDepth', min: 1, max: 16 },
  inlineBatchLimit: { key: 'inlineBatchLimit', min: 1, max: 500 },
  categorizationTimeoutMinutes: { key: 'categorizationTimeoutMinutes', min: 1, max: 240 },
};

export const FLEET_ACTION_RANGES: Record<keyof FleetActionSettingsData, NumberField> = {
  maxConcurrency: { key: 'maxConcurrency', min: 1, max: 32 },
  defaultTimeoutSeconds: { key: 'defaultTimeoutSeconds', min: 5, max: 7200 },
  maxOutputBytes: { key: 'maxOutputBytes', min: 1024, max: 1048576 },
  runRetentionDays: { key: 'runRetentionDays', min: 1, max: 3650 },
};

/** Returns 'range' for an out-of-range or non-integer value, or '' when valid. */
export function rangeError(value: string, range: NumberField): string {
  const n = Number(value);
  if (value.trim() === '' || !Number.isInteger(n) || n < range.min || n > range.max) return 'range';
  return '';
}

export const IMPORT_DEFAULTS: VesselImportSettingsData = {
  allowedRoots: [],
  maxDepth: 6,
  excludedDirectoryNames: ['bin', 'obj', 'node_modules', 'dist', '.git', '.vs', 'packages', 'TestResults', '.armada', 'target', 'venv', '.venv', '__pycache__'],
  inlineBatchLimit: 25,
  categorizationTimeoutMinutes: 20,
};

export const FLEET_DEFAULTS: FleetActionSettingsData = { maxConcurrency: 8, defaultTimeoutSeconds: 300, maxOutputBytes: 65536, runRetentionDays: 30 };

/** Every retention field is a number of days; 0 means never. */
export const RETENTION_RANGE = { key: 'days', min: 0, max: 3650 };

export const RETENTION_DEFAULTS: RetentionSettingsData = {
  askThreadArchiveAfterDays: 90,
  askThreadDeleteAfterDays: 0,
  jobRetentionDays: 30,
  importBatchRetentionDays: 90,
  cliPermissionRequestRetentionDays: 90,
};

export type RetentionField = keyof RetentionSettingsData;
export type RetentionDraft = Record<RetentionField, string>;

export const RETENTION_FIELDS: RetentionField[] = ['askThreadArchiveAfterDays', 'askThreadDeleteAfterDays', 'jobRetentionDays', 'importBatchRetentionDays', 'cliPermissionRequestRetentionDays'];

/** Field errors for a retention draft: 'range' for a value that is not a whole number from 0 to 3650. */
export function validateRetentionDraft(draft: RetentionDraft): Partial<Record<RetentionField, string>> {
  const errors: Partial<Record<RetentionField, string>> = {};
  for (const field of RETENTION_FIELDS) {
    const error = rangeError(draft[field], RETENTION_RANGE);
    if (error) errors[field] = error;
  }
  return errors;
}

/** Server defaults of the Permissions group (CliPermissionSettings). */
export const CLI_PERMISSION_DEFAULTS: CliPermissionSettingsData = {
  askDefaultPolicy: 'ApproveInArmada',
  missionDefaultPolicy: 'Bypass',
  allowOwnerApproval: false,
  promptTimeoutSeconds: 600,
};

/** True when the timeout is a whole number of seconds from 10 to 3600. */
export function validPromptTimeout(value: string): boolean {
  if (!/^\d+$/.test(value.trim())) return false;
  const n = Number(value);
  return n >= PROMPT_TIMEOUT_RANGE.min && n <= PROMPT_TIMEOUT_RANGE.max;
}

export type RepositoryHealthNumericKey = 'intervalMinutes' | 'maxConcurrency' | 'dependencyMaxAgeHours' | 'dependencyCommandTimeoutSeconds' | 'staleBranchDays' | 'missionWindowDays';
export type RepositoryHealthThresholdKey = keyof RepositoryHealthThresholds;

export interface NumericFieldDef<K extends string> {
  key: K;
  label: string;
  help: string;
  min: number;
  max: number;
}

/** Ranges mirror RepositoryHealthSettings.cs and RepositoryHealthThresholds.cs (the server clamps too). */
export const REPOSITORY_HEALTH_FIELDS: NumericFieldDef<RepositoryHealthNumericKey>[] = [
  { key: 'intervalMinutes', label: msg('Evaluation interval (minutes)'), help: msg('Minutes between scheduled evaluations; 0 turns the schedule off.'), min: 0, max: 10080 },
  { key: 'maxConcurrency', label: msg('Max concurrency'), help: msg('Vessels evaluated at the same time within one job.'), min: 1, max: 32 },
  { key: 'dependencyMaxAgeHours', label: msg('Dependency result max age (hours)'), help: msg('Refresh dependency results after this long even when manifests are unchanged.'), min: 1, max: 720 },
  { key: 'dependencyCommandTimeoutSeconds', label: msg('Dependency command timeout (seconds)'), help: msg('Timeout for each dotnet or npm call; a timeout grades Unknown.'), min: 10, max: 900 },
  { key: 'staleBranchDays', label: msg('Stale branch age (days)'), help: msg('A branch whose last commit is older than this counts as stale.'), min: 1, max: 3650 },
  { key: 'missionWindowDays', label: msg('Mission failure window (days)'), help: msg('Window for counting failed and landing-failed missions.'), min: 1, max: 90 },
];

export const REPOSITORY_HEALTH_THRESHOLD_FIELDS: NumericFieldDef<RepositoryHealthThresholdKey>[] = [
  { key: 'behindWarn', label: msg('Behind: warn at'), help: msg('Commits behind the default branch that warn.'), min: 1, max: 100000 },
  { key: 'behindFail', label: msg('Behind: fail at'), help: msg('Commits behind the default branch that fail.'), min: 1, max: 100000 },
  { key: 'staleBranchWarn', label: msg('Stale branches: warn at'), help: msg('Stale branches that warn.'), min: 1, max: 10000 },
  { key: 'staleBranchFail', label: msg('Stale branches: fail at'), help: msg('Stale branches that fail.'), min: 1, max: 10000 },
  { key: 'missionFailureWarn', label: msg('Failed missions: warn at'), help: msg('Recent failed missions that warn.'), min: 1, max: 1000 },
  { key: 'missionFailureFail', label: msg('Failed missions: fail at'), help: msg('Recent failed missions that fail.'), min: 1, max: 1000 },
];

export const REPOSITORY_HEALTH_DEFAULTS: RepositoryHealthSettings = {
  intervalMinutes: 360,
  maxConcurrency: 4,
  fetchBeforeEvaluate: true,
  dependencyMaxAgeHours: 24,
  dependencyCommandTimeoutSeconds: 120,
  staleBranchDays: 90,
  missionWindowDays: 7,
  scoredCriteria: ['GitDivergence', 'WorkingTree', 'Branches', 'Dependencies', 'Vulnerabilities', 'TestInfrastructure', 'ArmadaReadiness', 'MissionOutcomes'],
  thresholds: { behindWarn: 1, behindFail: 21, staleBranchWarn: 4, staleBranchFail: 11, missionFailureWarn: 1, missionFailureFail: 3 },
};

/** Settings from the server merged over the defaults (thresholds field by field). */
export function mergeRepositoryHealth(source: Partial<RepositoryHealthSettings> | null | undefined): RepositoryHealthSettings {
  return {
    ...REPOSITORY_HEALTH_DEFAULTS,
    ...(source ?? {}),
    thresholds: { ...REPOSITORY_HEALTH_DEFAULTS.thresholds, ...(source?.thresholds ?? {}) },
    scoredCriteria: Array.isArray(source?.scoredCriteria) ? source!.scoredCriteria : REPOSITORY_HEALTH_DEFAULTS.scoredCriteria,
  };
}

/** Returns an English error key for a numeric draft, or null when valid. */
export function validateRange(raw: string, min: number, max: number): string | null {
  const value = raw.trim();
  if (!/^-?\d+$/.test(value)) return msg('Enter a whole number.');
  const n = parseInt(value, 10);
  if (n < min || n > max) return msg('Must be between {{min}} and {{max}}.');
  return null;
}

/** English cross-field errors (fail below warn) for repository health threshold drafts keyed 'thresholds.<key>'. */
export function repositoryHealthCrossErrors(drafts: Record<string, string>, errors: Record<string, string | null>): string[] {
  const num = (key: string) => parseInt(drafts[key] ?? '', 10);
  const out: string[] = [];
  if (!errors['thresholds.behindWarn'] && !errors['thresholds.behindFail'] && num('thresholds.behindFail') < num('thresholds.behindWarn')) out.push(msg('Behind: fail at must be at least the warn value.'));
  if (!errors['thresholds.staleBranchWarn'] && !errors['thresholds.staleBranchFail'] && num('thresholds.staleBranchFail') < num('thresholds.staleBranchWarn')) out.push(msg('Stale branches: fail at must be at least the warn value.'));
  if (!errors['thresholds.missionFailureWarn'] && !errors['thresholds.missionFailureFail'] && num('thresholds.missionFailureFail') < num('thresholds.missionFailureWarn')) out.push(msg('Failed missions: fail at must be at least the warn value.'));
  return out;
}
