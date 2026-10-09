import { describe, expect, it, vi } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import ReadinessPanel from './ReadinessPanel';
import { translateTemplate } from '../../i18n/runtime';
import type { VesselReadinessIssue, VesselReadinessResult } from '../../types/models';

vi.mock('../../context/LocaleContext', () => ({
  useLocale: () => ({ t: (text: string, params?: Record<string, string | number>) => translateTemplate('en', text, null, params) }),
}));

function readiness(issues: VesselReadinessIssue[]): VesselReadinessResult {
  return {
    vesselId: 'vsl_1',
    hasWorkingDirectory: true,
    checkoutPath: '/srv/repos/app',
    harborId: null,
    harborName: null,
    checkoutErrorCode: null,
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

describe('ReadinessPanel checkout', () => {
  it('names the Harbor, its path, and a copyable Harbor ID', () => {
    render(
      <MemoryRouter>
        <ReadinessPanel title="Readiness" readiness={{ ...readiness([]), harborId: 'hbr_mac', harborName: 'Joels-MacBook-Pro', checkoutPath: '/Users/joel/Code/DocConverter' }} />
      </MemoryRouter>,
    );
    const line = screen.getByTestId('readiness-checkout');
    expect(line.textContent).toContain('Checkout:');
    expect(line.textContent).toContain('on Harbor Joels-MacBook-Pro at /Users/joel/Code/DocConverter');
    expect(within(line).getByText('hbr_mac').tagName).toBe('CODE');
    expect(within(line).getByTitle('Copy Harbor ID')).toBeTruthy();
  });

  it('says the checkout is on the Admiral with its working directory', () => {
    render(<MemoryRouter><ReadinessPanel title="Readiness" readiness={readiness([])} /></MemoryRouter>);
    const line = screen.getByTestId('readiness-checkout');
    expect(line.textContent).toContain('on the Admiral at /srv/repos/app');
    expect(within(line).queryByTitle('Copy Harbor ID')).toBeNull();
  });

  it('gives the typed reason when no checkout is available', () => {
    render(
      <MemoryRouter>
        <ReadinessPanel title="Readiness" readiness={{ ...readiness([]), hasWorkingDirectory: false, checkoutPath: null, checkoutErrorCode: 'NoHarborCheckout' }} />
      </MemoryRouter>,
    );
    expect(screen.getByTestId('readiness-checkout').textContent).toContain('no connected Harbor has a checkout of this vessel');
  });
});
