import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import DeploymentDetail from './DeploymentDetail';
import {
  getDeployment,
  listEnvironments,
  listReleases,
  listRunbookExecutions,
  listVessels,
  listWorkflowProfiles,
} from '../api/client';
import type { Deployment } from '../types/models';

const page = { success: true, pageNumber: 1, pageSize: 9999, totalPages: 1, totalRecords: 0, totalMs: 1, objects: [] };

vi.mock('../api/client', () => ({
  approveDeployment: vi.fn(),
  createDeployment: vi.fn(),
  deleteDeployment: vi.fn(),
  denyDeployment: vi.fn(),
  getDeployment: vi.fn(),
  listRunbookExecutions: vi.fn(),
  listEnvironments: vi.fn(),
  listReleases: vi.fn(),
  listVessels: vi.fn(),
  listWorkflowProfiles: vi.fn(),
  rollbackDeployment: vi.fn(),
  syncGitHubActions: vi.fn(),
  updateDeployment: vi.fn(),
  verifyDeployment: vi.fn(),
}));

vi.mock('../context/AuthContext', () => ({ useAuth: () => ({ isAdmin: true, isTenantAdmin: true }) }));
const localeValue = {
  t: (text: string, params?: Record<string, string>) =>
    Object.entries(params ?? {}).reduce((acc, [k, v]) => acc.split(`{{${k}}}`).join(String(v)), text),
  formatDateTime: (value: string | null | undefined) => value ?? '',
  formatRelativeTime: (value: string | null | undefined) => value ?? '',
};
vi.mock('../context/LocaleContext', () => ({ useLocale: () => localeValue }));
vi.mock('../context/NotificationContext', () => ({ useNotifications: () => ({ pushToast: vi.fn() }) }));

function deployment(overrides: Partial<Deployment>): Deployment {
  return {
    id: 'dpl_1', tenantId: null, userId: null, vesselId: null, workflowProfileId: null, environmentId: null,
    environmentName: 'production', releaseId: null, missionId: null, voyageId: null, title: 'Release 2.3 hotfix',
    sourceRef: null, summary: null, notes: null, status: 'PendingApproval', verificationStatus: 'NotStarted',
    approvalRequired: true, approvedByUserId: null, approvedUtc: null, approvalComment: null, deployCheckRunId: null,
    smokeTestCheckRunId: null, healthCheckRunId: null, deploymentVerificationCheckRunId: null, rollbackCheckRunId: null,
    rollbackVerificationCheckRunId: null, checkRunIds: [], requestHistorySummary: null, createdUtc: '2026-10-05T00:00:00Z',
    startedUtc: null, completedUtc: null, verifiedUtc: null, rolledBackUtc: null, monitoringWindowEndsUtc: null,
    lastMonitoredUtc: null, lastRegressionAlertUtc: null, latestMonitoringSummary: null, monitoringFailureCount: 0,
    lastUpdateUtc: '2026-10-05T00:00:00Z',
    ...overrides,
  } as Deployment;
}

async function openDecision(button: 'Approve' | 'Deny', value: Deployment) {
  vi.mocked(getDeployment).mockResolvedValue(value);
  render(
    <MemoryRouter initialEntries={['/deployments/dpl_1']}>
      <Routes>
        <Route path="/deployments/:id" element={<DeploymentDetail />} />
      </Routes>
    </MemoryRouter>,
  );
  fireEvent.click(await screen.findByRole('button', { name: button }));
}

describe('DeploymentDetail approval confirm dialog', () => {
  beforeEach(() => {
    for (const fn of [listEnvironments, listReleases, listRunbookExecutions, listVessels, listWorkflowProfiles]) {
      vi.mocked(fn).mockResolvedValue(page as never);
    }
  });

  it('names the deployment environment first, then the title, when approving', async () => {
    await openDecision('Approve', deployment({}));
    expect(await screen.findByText('Approve and execute "Deploy to production: Release 2.3 hotfix"?')).toBeInTheDocument();
  });

  it('uses the same label when denying', async () => {
    await openDecision('Deny', deployment({}));
    expect(await screen.findByText('Deny "Deploy to production: Release 2.3 hotfix" without executing it?')).toBeInTheDocument();
  });

  it('falls back to the title alone when the deployment has no environment', async () => {
    await openDecision('Approve', deployment({ environmentName: null }));
    expect(await screen.findByText('Approve and execute "Deploy: Release 2.3 hotfix"?')).toBeInTheDocument();
  });
});
