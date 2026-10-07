import { describe, expect, it } from 'vitest';
import { SETUP_LANDING_MODES, setupLandingModeHint, setupLandingWorkingDirectoryError } from './setupLanding';

describe('setup wizard landing modes', () => {
  it('offers Merge and Push right after Local Merge', () => {
    expect(SETUP_LANDING_MODES.map((m) => m.value)).toEqual(['', 'None', 'LocalMerge', 'MergeAndPush', 'PullRequest', 'MergeQueue']);
    expect(SETUP_LANDING_MODES.find((m) => m.value === 'MergeAndPush')?.label).toBe('Merge and Push');
  });

  it('says Local Merge pushes nothing and Merge and Push needs an origin remote', () => {
    expect(setupLandingModeHint('LocalMerge')).toBe('Finished work is merged into the working directory. Nothing is pushed.');
    expect(setupLandingModeHint('MergeAndPush')).toBe('Finished work is merged into the working directory and pushed to its origin remote, so the checkout needs one.');
    expect(setupLandingModeHint('Unknown')).toBe(setupLandingModeHint(''));
  });

  it('requires a working directory for both Local Merge and Merge and Push', () => {
    expect(setupLandingWorkingDirectoryError('LocalMerge', '')).toBe('Local Merge needs a working directory to merge into.');
    expect(setupLandingWorkingDirectoryError('MergeAndPush', '   ')).toBe('Merge and Push needs a working directory to merge into.');
    expect(setupLandingWorkingDirectoryError('MergeAndPush', '/work/svc')).toBeNull();
    expect(setupLandingWorkingDirectoryError('LocalMerge', '/work/svc')).toBeNull();
    for (const mode of ['', 'None', 'PullRequest', 'MergeQueue']) {
      expect(setupLandingWorkingDirectoryError(mode, '')).toBeNull();
    }
  });
});
