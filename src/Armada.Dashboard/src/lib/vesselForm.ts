import type { Vessel } from '../types/models';

/**
 * Editable vessel fields as held by the shared vessel form (Vessels list Create/Edit and the vessel page Edit).
 * List-valued fields are kept as newline-separated text and converted by buildVesselPayload.
 */
export interface VesselFormState {
  name: string;
  fleetId: string;
  repoUrl: string;
  defaultBranch: string;
  localPath: string;
  workingDirectory: string;
  projectContext: string;
  styleGuide: string;
  enableModelContext: boolean;
  modelContext: string;
  gitHubTokenOverride: string;
  clearGitHubTokenOverride: boolean;
  landingMode: string;
  branchCleanupPolicy: string;
  allowConcurrentMissions: boolean;
  autoApproveMode: string;
  defaultPipelineId: string;
  requirePassingChecksToLand: boolean;
  protectedBranchPatterns: string;
  releaseBranchPrefix: string;
  hotfixBranchPrefix: string;
  requirePullRequestForProtectedBranches: boolean;
  requireMergeQueueForReleaseBranches: boolean;
  secretScanEnabled: boolean;
  protectedPathPatterns: string;
  privateIdentifierDenylist: string;
  autoLandEnabled: boolean;
  autoLandMaxFiles: string;
  autoLandMaxLines: string;
  autoLandPathAllowGlobs: string;
  autoLandPathDenyGlobs: string;
  definitionOfDoneEnabled: boolean;
  definitionOfDoneBuildCommand: string;
  definitionOfDoneTestCommand: string;
  definitionOfDoneTimeoutSeconds: string;
}

/** Landing-mode option with a short label and a full explanation. */
export interface LandingModeOption {
  value: string;
  label: string;
  short: string;
  description: string;
}

/** Defaults for a new vessel. */
export const emptyVesselForm: VesselFormState = {
  name: '', fleetId: '', repoUrl: '', defaultBranch: 'main', localPath: '', workingDirectory: '',
  projectContext: '', styleGuide: '', enableModelContext: true, modelContext: '', gitHubTokenOverride: '', clearGitHubTokenOverride: false,
  landingMode: 'LocalMerge', branchCleanupPolicy: 'LocalAndRemote', allowConcurrentMissions: false, autoApproveMode: 'inherit', defaultPipelineId: '',
  requirePassingChecksToLand: false, protectedBranchPatterns: '', releaseBranchPrefix: 'release/', hotfixBranchPrefix: 'hotfix/',
  requirePullRequestForProtectedBranches: false, requireMergeQueueForReleaseBranches: false,
  secretScanEnabled: false, protectedPathPatterns: '', privateIdentifierDenylist: '',
  autoLandEnabled: false, autoLandMaxFiles: '', autoLandMaxLines: '', autoLandPathAllowGlobs: '', autoLandPathDenyGlobs: '',
  definitionOfDoneEnabled: false, definitionOfDoneBuildCommand: '', definitionOfDoneTestCommand: '', definitionOfDoneTimeoutSeconds: '',
};

/** Landing-mode metadata shared by the vessel form, the Vessels filter, and the Vessels table. */
export function getLandingModes(t: (text: string) => string): LandingModeOption[] {
  return [
    { value: '', label: t('Default (use global setting)'), short: t('global default'), description: t('Uses the global default landing mode configured for the Admiral.') },
    { value: 'LocalMerge', label: t('Local Merge -- into your working directory, no push'), short: t('local, no push'), description: t('Merges the mission branch into the default branch in your local working directory. Nothing is pushed. Requires the vessel to have a working directory and local path configured.') },
    { value: 'MergeAndPush', label: t('Merge and Push -- local merge, then push to the remote'), short: t('local + push'), description: t('Merges the mission branch into the default branch in your local working directory, then pushes it to the working directory\'s remote. Requires the vessel to have a working directory and local path configured, and the working directory needs a remote.') },
    { value: 'PullRequest', label: t('Pull Request -- push and open a PR'), short: t('opens a PR'), description: t('Pushes the mission branch and opens a pull request on the remote. The mission stays open until the PR is merged.') },
    { value: 'MergeQueue', label: t('Merge Queue -- validated sequential merge'), short: t('merge queue'), description: t('Enqueues the mission branch for a validated merge. The merge queue runs tests and merges branches one at a time per vessel.') },
    { value: 'None', label: t('None -- manual integration'), short: t('manual only'), description: t('No automatic landing. Work stays as WorkProduced and the branch is kept in the repository for you to integrate manually.') },
  ];
}

/** Landing modes for a voyage: the inherit entry (vessel, then global setting) followed by every mode. */
export function getVoyageLandingModes(t: (text: string) => string): LandingModeOption[] {
  const modes = getLandingModes(t).filter(m => m.value);
  return [
    { value: '', label: t('Default (use vessel or global setting)'), short: t('vessel or global default'), description: t('Uses the vessel\'s landing mode, or the global default when the vessel does not set one.') },
    ...modes,
  ];
}

/** Landing modes for the global default (no inherit entry). */
export function getGlobalLandingModes(t: (text: string) => string): LandingModeOption[] {
  return getLandingModes(t).filter(m => m.value);
}

/** The landing mode the Admiral uses when the global setting is absent. */
export const DEFAULT_GLOBAL_LANDING_MODE = 'MergeAndPush';

/** Returns the metadata for a landing mode, falling back to the default entry. */
export function findLandingMode(modes: LandingModeOption[], mode: string | null | undefined): LandingModeOption {
  return modes.find(m => m.value === (mode ?? '')) ?? modes[0];
}

/** Converts a stored vessel into form state. */
export function vesselToForm(v: Vessel): VesselFormState {
  return {
    name: v.name,
    fleetId: v.fleetId ?? '',
    repoUrl: v.repoUrl ?? '',
    defaultBranch: v.defaultBranch || 'main',
    localPath: v.localPath ?? '',
    workingDirectory: v.workingDirectory ?? '',
    projectContext: v.projectContext ?? '',
    styleGuide: v.styleGuide ?? '',
    enableModelContext: v.enableModelContext,
    modelContext: v.modelContext ?? '',
    gitHubTokenOverride: '',
    clearGitHubTokenOverride: false,
    landingMode: v.landingMode ?? '',
    branchCleanupPolicy: v.branchCleanupPolicy ?? '',
    allowConcurrentMissions: v.allowConcurrentMissions,
    autoApproveMode: v.autoApprove === true ? 'on' : v.autoApprove === false ? 'off' : 'inherit',
    defaultPipelineId: v.defaultPipelineId ?? '',
    requirePassingChecksToLand: v.requirePassingChecksToLand ?? false,
    protectedBranchPatterns: (v.protectedBranchPatterns || []).join('\n'),
    releaseBranchPrefix: v.releaseBranchPrefix || 'release/',
    hotfixBranchPrefix: v.hotfixBranchPrefix || 'hotfix/',
    requirePullRequestForProtectedBranches: v.requirePullRequestForProtectedBranches ?? false,
    requireMergeQueueForReleaseBranches: v.requireMergeQueueForReleaseBranches ?? false,
    secretScanEnabled: v.secretScanEnabled ?? false,
    protectedPathPatterns: (v.protectedPathPatterns || []).join('\n'),
    privateIdentifierDenylist: (v.privateIdentifierDenylist || []).join('\n'),
    autoLandEnabled: v.autoLandEnabled ?? false,
    autoLandMaxFiles: v.autoLandMaxFiles ? String(v.autoLandMaxFiles) : '',
    autoLandMaxLines: v.autoLandMaxLines ? String(v.autoLandMaxLines) : '',
    autoLandPathAllowGlobs: (v.autoLandPathAllowGlobs || []).join('\n'),
    autoLandPathDenyGlobs: (v.autoLandPathDenyGlobs || []).join('\n'),
    definitionOfDoneEnabled: v.definitionOfDoneEnabled ?? false,
    definitionOfDoneBuildCommand: v.definitionOfDoneBuildCommand || '',
    definitionOfDoneTestCommand: v.definitionOfDoneTestCommand || '',
    definitionOfDoneTimeoutSeconds: v.definitionOfDoneTimeoutSeconds ? String(v.definitionOfDoneTimeoutSeconds) : '',
  };
}

function splitLines(text: string): string[] {
  return text.split(/\r?\n/).map((s) => s.trim()).filter((s) => s.length > 0);
}

/**
 * Builds the create or update request body. The vessel PUT replaces the whole record, so on edit the payload starts
 * from the stored vessel (keeping fields the form does not show, such as the preferred harbor) and overlays every
 * form field.
 */
export function buildVesselPayload(form: VesselFormState, existing: Vessel | null): Record<string, unknown> {
  const payload: Record<string, unknown> = existing ? { ...existing } : {};
  delete payload.hasGitHubTokenOverride;
  delete payload.gitHubTokenOverride;

  payload.name = form.name;
  payload.fleetId = form.fleetId || null;
  payload.repoUrl = form.repoUrl;
  payload.defaultBranch = form.defaultBranch;
  payload.localPath = form.localPath || null;
  payload.workingDirectory = form.workingDirectory || null;
  payload.projectContext = form.projectContext || null;
  payload.styleGuide = form.styleGuide || null;
  payload.enableModelContext = form.enableModelContext;
  payload.modelContext = form.modelContext || null;
  payload.landingMode = form.landingMode || null;
  payload.branchCleanupPolicy = form.branchCleanupPolicy || null;
  payload.allowConcurrentMissions = form.allowConcurrentMissions;
  payload.autoApprove = form.autoApproveMode === 'on' ? true : form.autoApproveMode === 'off' ? false : null;
  payload.defaultPipelineId = form.defaultPipelineId || null;
  payload.requirePassingChecksToLand = form.requirePassingChecksToLand;
  payload.protectedBranchPatterns = splitLines(form.protectedBranchPatterns);
  payload.releaseBranchPrefix = form.releaseBranchPrefix;
  payload.hotfixBranchPrefix = form.hotfixBranchPrefix;
  payload.requirePullRequestForProtectedBranches = form.requirePullRequestForProtectedBranches;
  payload.requireMergeQueueForReleaseBranches = form.requireMergeQueueForReleaseBranches;
  payload.secretScanEnabled = form.secretScanEnabled;
  payload.protectedPathPatterns = splitLines(form.protectedPathPatterns);
  payload.privateIdentifierDenylist = splitLines(form.privateIdentifierDenylist);
  payload.autoLandEnabled = form.autoLandEnabled;
  payload.autoLandMaxFiles = form.autoLandMaxFiles.trim() ? Math.max(0, parseInt(form.autoLandMaxFiles, 10) || 0) : 0;
  payload.autoLandMaxLines = form.autoLandMaxLines.trim() ? Math.max(0, parseInt(form.autoLandMaxLines, 10) || 0) : 0;
  payload.autoLandPathAllowGlobs = splitLines(form.autoLandPathAllowGlobs);
  payload.autoLandPathDenyGlobs = splitLines(form.autoLandPathDenyGlobs);
  payload.definitionOfDoneEnabled = form.definitionOfDoneEnabled;
  payload.definitionOfDoneBuildCommand = form.definitionOfDoneBuildCommand.trim();
  payload.definitionOfDoneTestCommand = form.definitionOfDoneTestCommand.trim();
  payload.definitionOfDoneTimeoutSeconds = form.definitionOfDoneTimeoutSeconds.trim()
    ? Math.max(30, parseInt(form.definitionOfDoneTimeoutSeconds, 10) || 1800)
    : 1800;

  // GitHub token override: omit to keep the stored value; an empty string clears it.
  if (existing && form.clearGitHubTokenOverride) payload.gitHubTokenOverride = '';
  else if (form.gitHubTokenOverride.trim()) payload.gitHubTokenOverride = form.gitHubTokenOverride.trim();

  return payload;
}
