import { act, fireEvent, screen, waitFor } from '@testing-library/react-native';
import * as client from '@dashboard/api/client';
import type { Deployment, DeploymentEnvironment, Release } from '@dashboard/types/models';
import WorkLayout from '../app/(app)/(work)/_layout';
import DeliveryRoute from '../app/(app)/(work)/delivery';
import DeploymentRoute from '../app/(app)/(work)/deployments/[id]';
import { deploymentPrefillFrom } from '../screens/delivery/DeploymentDetail';
import { deploymentPayload, linkDeploymentValues, newDeploymentValues } from '../screens/delivery/deploymentForm';
import { emit, page, renderW4Routes, resetW4 } from '../test/w4';

jest.mock('@dashboard/api/client', () => require('../test/w4Client').autoMockClient());

const api = client as jest.Mocked<typeof client>;

function dep(over: Partial<Deployment> = {}): Deployment {
  return {
    id: 'dpl_1', tenantId: 'ten_1', userId: 'usr_1', vesselId: 'vsl_1', workflowProfileId: null, environmentId: 'env_1', environmentName: 'production',
    releaseId: null, missionId: null, voyageId: null, title: 'Release 2.3', sourceRef: 'main', summary: null, notes: null, status: 'PendingApproval',
    verificationStatus: 'NotRun', approvalRequired: true, approvedByUserId: null, approvedUtc: null, approvalComment: null, deployCheckRunId: null,
    smokeTestCheckRunId: null, healthCheckRunId: null, deploymentVerificationCheckRunId: null, rollbackCheckRunId: null, rollbackVerificationCheckRunId: null,
    checkRunIds: [], requestHistorySummary: null, createdUtc: '2026-10-01T00:00:00Z', startedUtc: null, completedUtc: null, verifiedUtc: null,
    rolledBackUtc: null, monitoringWindowEndsUtc: null, lastMonitoredUtc: null, lastRegressionAlertUtc: null, latestMonitoringSummary: null,
    monitoringFailureCount: 0, lastUpdateUtc: '2026-10-02T00:00:00Z', ...over,
  };
}

const ENV = { id: 'env_1', name: 'production', vesselId: 'vsl_1' } as DeploymentEnvironment;
const ENV2 = { id: 'env_2', name: 'staging', vesselId: 'vsl_2' } as DeploymentEnvironment;
const REL = { id: 'rel_1', title: 'R1', vesselId: 'vsl_3' } as Release;

const ROUTES = {
  '(work)/_layout': WorkLayout,
  '(work)/home': () => null,
  '(work)/delivery': DeliveryRoute,
  '(work)/deployments/[id]': DeploymentRoute,
};

beforeEach(async () => {
  await resetW4();
  jest.clearAllMocks();
  api.listVessels.mockResolvedValue(page([{ id: 'vsl_1', name: 'armada' }]) as never);
  api.listEnvironments.mockResolvedValue(page([ENV]) as never);
  api.listReleases.mockResolvedValue(page([]) as never);
  api.listWorkflowProfiles.mockResolvedValue(page([]) as never);
  api.listRunbookExecutions.mockResolvedValue(page([]) as never);
  api.listDeployments.mockResolvedValue(page([dep(), dep({ id: 'dpl_2', title: 'Hotfix', status: 'Succeeded', verificationStatus: 'Passed', environmentName: 'staging' })]) as never);
});

describe('deployment form', () => {
  it('links environment, vessel, and release like the dashboard', () => {
    const base = newDeploymentValues();
    const withEnv = linkDeploymentValues({ ...base, environmentId: 'env_1' }, base, [ENV, ENV2], [REL]);
    expect(withEnv).toMatchObject({ environmentName: 'production', vesselId: 'vsl_1' });
    const otherVessel = linkDeploymentValues({ ...withEnv, vesselId: 'vsl_2' }, withEnv, [ENV, ENV2], [REL]);
    expect(otherVessel.environmentId).toBe('');
    const withRelease = linkDeploymentValues({ ...base, releaseId: 'rel_1' }, base, [ENV], [REL]);
    expect(withRelease.vesselId).toBe('vsl_3');
  });

  it('builds the payload and reads the prefill from a link', () => {
    const prefill = deploymentPrefillFrom({ id: 'new', environmentId: 'env_1', title: 'X Deploy', bogus: 'y' });
    expect(prefill).toEqual({ environmentId: 'env_1', title: 'X Deploy' });
    expect(deploymentPayload({ ...newDeploymentValues(prefill), sourceRef: '  ' })).toMatchObject({ environmentId: 'env_1', title: 'X Deploy', sourceRef: null, autoExecute: true });
  });
});

describe('Delivery > Deployments', () => {
  it('is the default tab, lists deployments with counts and filters, and reloads live', async () => {
    const h = await renderW4Routes(ROUTES, '/delivery');
    await waitFor(() => expect(screen.getByTestId('deployment-row-dpl_1')).toBeTruthy());
    expect(screen.getByLabelText('Total Deployments: 2')).toBeTruthy();
    expect(screen.getByLabelText('Pending Approval: 1')).toBeTruthy();
    await fireEvent.changeText(screen.getByTestId('deployments-search'), 'hotfix');
    await waitFor(() => expect(screen.queryByTestId('deployment-row-dpl_1')).toBeNull());
    expect(api.listDeployments).toHaveBeenCalledTimes(1);
    await emit(h, { type: 'deployment.changed', data: { id: 'dpl_1' } });
    await waitFor(() => expect(api.listDeployments.mock.calls.length).toBeGreaterThanOrEqual(2));
  });

  it('creates a deployment (tenant admin) and hides create from users', async () => {
    api.createDeployment.mockResolvedValue(dep({ id: 'dpl_9', title: 'New' }));
    await renderW4Routes(ROUTES, '/delivery?tab=deployments', 'tenantAdmin');
    await waitFor(() => expect(screen.getByTestId('deployment-row-dpl_1')).toBeTruthy());
    await fireEvent.press(screen.getByTestId('deployments-create'));
    await waitFor(() => expect(screen.getByTestId('deployment-form-title')).toBeTruthy());
    await fireEvent.changeText(screen.getByTestId('deployment-form-title'), 'New');
    await act(async () => { await fireEvent.press(screen.getByTestId('deployment-form-submit')); });
    await waitFor(() => expect(api.createDeployment).toHaveBeenCalledWith(expect.objectContaining({ title: 'New', autoExecute: true })));
  });

  it('regular users see no create button or row actions', async () => {
    await renderW4Routes(ROUTES, '/delivery?tab=deployments', 'user');
    await waitFor(() => expect(screen.getByTestId('deployment-row-dpl_1')).toBeTruthy());
    expect(screen.queryByTestId('deployments-create')).toBeNull();
  });
});

describe('deployment detail', () => {
  it('approves with the dashboard confirmation and shows the new status', async () => {
    api.getDeployment.mockResolvedValue(dep());
    api.approveDeployment.mockResolvedValue(dep({ status: 'Running', approvedByUserId: 'usr_1' }));
    await renderW4Routes(ROUTES, '/deployments/dpl_1');
    await waitFor(() => expect(screen.getByTestId('deployment-title')).toHaveTextContent('Release 2.3'));
    expect(screen.queryByTestId('deployment-verify')).toBeNull();
    await fireEvent.press(screen.getByTestId('deployment-approve'));
    await waitFor(() => expect(screen.getByText('Approve and execute "Deploy to production: Release 2.3"?')).toBeTruthy());
    await act(async () => { await fireEvent.press(screen.getByTestId('deployment-action-confirm-confirm')); });
    expect(api.approveDeployment).toHaveBeenCalledWith('dpl_1');
    await waitFor(() => expect(screen.getByTestId('deployment-status')).toHaveTextContent('StatusRunning'));
    expect(screen.getByTestId('deployment-verify')).toBeTruthy();
    expect(screen.getByTestId('deployment-rollback')).toBeTruthy();
  });

  it('denies, and verifies or rolls back once decided', async () => {
    api.getDeployment.mockResolvedValue(dep());
    api.denyDeployment.mockResolvedValue(dep({ status: 'Denied' }));
    api.rollbackDeployment.mockResolvedValue(dep({ status: 'RollingBack' }));
    await renderW4Routes(ROUTES, '/deployments/dpl_1');
    await waitFor(() => expect(screen.getByTestId('deployment-deny')).toBeTruthy());
    await fireEvent.press(screen.getByTestId('deployment-deny'));
    await waitFor(() => expect(screen.getByText('Deny "Deploy to production: Release 2.3" without executing it?')).toBeTruthy());
    await act(async () => { await fireEvent.press(screen.getByTestId('deployment-action-confirm-confirm')); });
    expect(api.denyDeployment).toHaveBeenCalledWith('dpl_1');
    await waitFor(() => expect(screen.getByTestId('deployment-rollback')).toBeTruthy());
    await fireEvent.press(screen.getByTestId('deployment-rollback'));
    await waitFor(() => expect(screen.getByText('Run rollback for "Release 2.3"?')).toBeTruthy());
    await act(async () => { await fireEvent.press(screen.getByTestId('deployment-action-confirm-confirm')); });
    expect(api.rollbackDeployment).toHaveBeenCalledWith('dpl_1');
  });

  it('regular users cannot approve', async () => {
    api.getDeployment.mockResolvedValue(dep());
    await renderW4Routes(ROUTES, '/deployments/dpl_1', 'user');
    await waitFor(() => expect(screen.getByTestId('deployment-title')).toBeTruthy());
    expect(screen.queryByTestId('deployment-approve')).toBeNull();
    expect(screen.queryByTestId('deployment-delete')).toBeNull();
  });

  it('syncs GitHub Actions with the deployment context', async () => {
    api.getDeployment.mockResolvedValue(dep({ status: 'Succeeded', workflowProfileId: 'wfp_1' }));
    api.syncGitHubActions.mockResolvedValue({ providerName: 'GitHub', vesselId: 'vsl_1', deploymentId: 'dpl_1', createdCount: 2, updatedCount: 1, checkRuns: [] });
    await renderW4Routes(ROUTES, '/deployments/dpl_1');
    await waitFor(() => expect(screen.getByTestId('deployment-sync-github')).toBeTruthy());
    await act(async () => { await fireEvent.press(screen.getByTestId('deployment-sync-github')); });
    await waitFor(() => expect(api.syncGitHubActions).toHaveBeenCalledWith({
      vesselId: 'vsl_1', workflowProfileId: 'wfp_1', deploymentId: 'dpl_1', environmentName: 'production', branchName: 'main', runCount: 20,
    }));
  });

  it('new opens the create form prefilled from the link', async () => {
    api.createDeployment.mockResolvedValue(dep({ id: 'dpl_7' }));
    api.getDeployment.mockResolvedValue(dep({ id: 'dpl_7' }));
    const h = await renderW4Routes(ROUTES, '/deployments/new?environmentId=env_1&environmentName=production&title=production%20Deploy');
    await waitFor(() => expect(screen.getByTestId('deployment-form-submit')).toBeTruthy());
    await act(async () => { await fireEvent.press(screen.getByTestId('deployment-form-submit')); });
    expect(api.createDeployment).toHaveBeenCalledWith(expect.objectContaining({ environmentId: 'env_1', environmentName: 'production', title: 'production Deploy' }));
    await waitFor(() => expect(h.getPathname()).toBe('/deployments/dpl_7'));
  });
});
