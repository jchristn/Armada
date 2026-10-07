import { describe, expect, it } from 'vitest';
import { findLandingMode, getLandingModes } from './vesselForm';

const identity = (text: string) => text;

describe('getLandingModes', () => {
  const modes = getLandingModes(identity);

  it('lists the modes in the UI order with Merge and Push after Local Merge', () => {
    expect(modes.map((m) => m.value)).toEqual(['', 'LocalMerge', 'MergeAndPush', 'PullRequest', 'MergeQueue', 'None']);
  });

  it('says Local Merge does not push', () => {
    const local = findLandingMode(modes, 'LocalMerge');
    expect(local.label).toBe('Local Merge -- into your working directory, no push');
    expect(local.short).toBe('local, no push');
    expect(local.description).toContain('Nothing is pushed.');
    expect(local.description).not.toMatch(/pushes/);
  });

  it('offers Merge and Push with its own labels', () => {
    const push = findLandingMode(modes, 'MergeAndPush');
    expect(push.value).toBe('MergeAndPush');
    expect(push.label).toBe('Merge and Push -- local merge, then push to the remote');
    expect(push.short).toBe('local + push');
    expect(push.description).toBe("Merges the mission branch into the default branch in your local working directory, then pushes it to the working directory's remote. Requires the vessel to have a working directory and local path configured, and the working directory needs a remote.");
  });

  it('translates every label through t', () => {
    const tagged = getLandingModes((text) => `[${text}]`);
    expect(findLandingMode(tagged, 'MergeAndPush').label).toBe('[Merge and Push -- local merge, then push to the remote]');
    expect(findLandingMode(tagged, 'MergeAndPush').short).toBe('[local + push]');
  });
});
