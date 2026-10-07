/** Setup wizard landing mode options (value and English short name), in the wizard's order. */
export const SETUP_LANDING_MODES: Array<{ value: string; label: string }> = [
  { value: '', label: 'Default' },
  { value: 'None', label: 'None (safest for setup)' },
  { value: 'LocalMerge', label: 'Local Merge' },
  { value: 'MergeAndPush', label: 'Merge and Push' },
  { value: 'PullRequest', label: 'Pull Request' },
  { value: 'MergeQueue', label: 'Merge Queue' },
];

/** English hint shown under the setup wizard's landing mode picker, keyed by mode ('' is the global default). */
export const SETUP_LANDING_MODE_HINTS: Record<string, string> = {
  '': 'Uses the Admiral-wide landing settings.',
  None: 'Finished work stays on a branch for you to review. Choose Local Merge to land it automatically.',
  LocalMerge: 'Finished work is merged into the working directory. Nothing is pushed.',
  MergeAndPush: 'Finished work is merged into the working directory and pushed to its origin remote, so the checkout needs one.',
  PullRequest: 'Finished work is pushed and opened as a pull request (needs the GitHub CLI).',
  MergeQueue: 'Finished work is queued; processing the merge queue tests and merges it.',
};

/** The English hint for a landing mode, falling back to the global default's hint. */
export function setupLandingModeHint(mode: string): string {
  return SETUP_LANDING_MODE_HINTS[mode] || SETUP_LANDING_MODE_HINTS[''];
}

/**
 * The English validation error when a landing mode that merges into the working directory (LocalMerge, MergeAndPush)
 * has no working directory, or null when the combination is valid.
 */
export function setupLandingWorkingDirectoryError(mode: string, workingDirectory: string): string | null {
  if (workingDirectory.trim()) return null;
  if (mode === 'LocalMerge') return 'Local Merge needs a working directory to merge into.';
  if (mode === 'MergeAndPush') return 'Merge and Push needs a working directory to merge into.';
  return null;
}
