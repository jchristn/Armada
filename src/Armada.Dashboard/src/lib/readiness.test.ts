import { describe, expect, it } from 'vitest';
import type { VesselReadinessResult, VesselSetupChecklistItem } from '../types/models';
import {
  formatInputProvider,
  readinessLabel,
  readinessTone,
  groupSetupChecklist,
  nextChecklistItem,
  readinessBranchSummary,
  readinessDriftSummary,
} from './readiness';

function item(code: string, isSatisfied: boolean): VesselSetupChecklistItem {
  return { code, severity: 'Warning', title: code, message: '', isSatisfied, actionLabel: null, actionRoute: null };
}

function readiness(over: Partial<VesselReadinessResult> = {}): VesselReadinessResult {
  return {
    vesselId: 'vsl_1', hasWorkingDirectory: true, hasRepositoryContext: true, workflowProfileId: null, workflowProfileName: null,
    workflowProfileScope: null, requestedCheckType: null, requestedEnvironmentName: null, availableCheckTypes: [], currentBranch: null,
    hasUncommittedChanges: null, isDetachedHead: null, commitsAhead: null, commitsBehind: null, detectedToolchains: [], toolchainProbes: [],
    deploymentEnvironments: [], deploymentMetadata: null, setupChecklist: [], issues: [], setupChecklistSatisfiedCount: 0,
    setupChecklistTotalCount: 0, errorCount: 0, warningCount: 0, isReady: true, ...over,
  };
}

describe('readiness helpers', () => {
  it('derives tone and label from error and warning counts', () => {
    expect(readinessTone(null)).toBe('warning');
    expect(readinessLabel(null)).toBe('Unknown');
    expect(readinessTone(readiness({ errorCount: 1, warningCount: 2 }))).toBe('error');
    expect(readinessLabel(readiness({ errorCount: 1 }))).toBe('Blocked');
    expect(readinessTone(readiness({ warningCount: 1 }))).toBe('warning');
    expect(readinessLabel(readiness({ warningCount: 1 }))).toBe('Needs Attention');
    expect(readinessTone(readiness())).toBe('ready');
    expect(readinessLabel(readiness())).toBe('Ready');
  });

  it('names input providers', () => {
    expect(formatInputProvider('OnePassword')).toBe('1Password');
    expect(formatInputProvider('AzureKeyVaultSecret')).toBe('Azure Key Vault');
    expect(formatInputProvider('Custom')).toBe('Custom');
    expect(formatInputProvider(null)).toBe('Input');
  });

  it('summarizes branch and drift', () => {
    expect(readinessBranchSummary(readiness())).toBeNull();
    expect(readinessBranchSummary(readiness({ currentBranch: 'main', isDetachedHead: true }))).toBe('main (detached HEAD)');
    expect(readinessDriftSummary(readiness())).toBeNull();
    expect(readinessDriftSummary(readiness({ commitsAhead: 2 }))).toBe('2 ahead / 0 behind');
  });

  it('groups the checklist and finds the next step', () => {
    const r = readiness({ setupChecklist: [item('working_directory', true), item('workflow_profile', false), item('unknown', false)] });
    const groups = groupSetupChecklist(r);
    expect(groups.map((g) => g.key)).toEqual(['repository', 'workflow']);
    expect(groups[1].items.map((i) => i.code)).toEqual(['workflow_profile']);
    expect(nextChecklistItem(r)?.code).toBe('workflow_profile');
    expect(nextChecklistItem(readiness())).toBeNull();
    expect(groupSetupChecklist(null)).toEqual([]);
  });
});
