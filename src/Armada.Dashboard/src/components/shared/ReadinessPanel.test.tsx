import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import ReadinessPanel from './ReadinessPanel';
import type { VesselReadinessIssue, VesselReadinessResult } from '../../types/models';

function readiness(issues: VesselReadinessIssue[]): VesselReadinessResult {
  return {
    vesselId: 'vsl_1',
    hasWorkingDirectory: true,
    hasRepositoryContext: true,
    workflowProfileId: null,
    workflowProfileName: null,
    workflowProfileScope: null,
    requestedCheckType: null,
    requestedEnvironmentName: null,
    availableCheckTypes: [],
    currentBranch: null,
    hasUncommittedChanges: null,
    isDetachedHead: null,
    commitsAhead: null,
    commitsBehind: null,
    detectedToolchains: [],
    toolchainProbes: [],
    deploymentEnvironments: [],
    deploymentMetadata: null,
    setupChecklist: [],
    issues,
    setupChecklistSatisfiedCount: 0,
    setupChecklistTotalCount: 0,
    errorCount: issues.length,
    warningCount: 0,
    isReady: false,
  };
}

describe('ReadinessPanel input provider', () => {
  it('labels the provider from the typed inputProvider field', () => {
    render(
      <MemoryRouter>
        <ReadinessPanel title="Readiness" readiness={readiness([
          { code: 'MissingInput', severity: 'Error', title: 'Missing input', message: 'm', relatedValue: 'API_TOKEN', inputProvider: 'EnvironmentVariable' },
        ])} />
      </MemoryRouter>,
    );
    expect(screen.getByText(/API_TOKEN/).textContent).toBe('API_TOKEN (Environment variable)');
  });

  it('does not derive a provider from a value prefix', () => {
    render(
      <MemoryRouter>
        <ReadinessPanel title="Readiness" readiness={readiness([
          { code: 'Other', severity: 'Error', title: 'Other', message: 'm', relatedValue: 'env:LOOKS_LIKE_INPUT', inputProvider: null },
        ])} />
      </MemoryRouter>,
    );
    expect(screen.getByText(/LOOKS_LIKE_INPUT/).textContent).toBe('env:LOOKS_LIKE_INPUT');
  });
});
