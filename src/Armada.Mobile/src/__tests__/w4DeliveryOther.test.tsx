import { act, fireEvent, screen, waitFor } from '@testing-library/react-native';
import * as client from '@dashboard/api/client';
import type { CheckRun, Deployment, Incident, Release, Runbook, RunbookExecution } from '@dashboard/types/models';
import WorkLayout from '../app/(app)/(work)/_layout';
import CheckRoute from '../app/(app)/(work)/checks/[id]';
import DeliveryRoute from '../app/(app)/(work)/delivery';
import IncidentRoute from '../app/(app)/(work)/incidents/[id]';
import ReleaseRoute from '../app/(app)/(work)/releases/[id]';
import ReleaseNewRoute from '../app/(app)/(work)/releases/new';
import RunbookRoute from '../app/(app)/(work)/runbooks/[id]';
import { checkPrefillFrom, draftReleaseLink } from '../screens/delivery/checkLinks';
import { incidentPayload, linkIncidentValues, newIncidentValues } from '../screens/delivery/incidentForm';
import { executionPrefillFrom } from '../screens/delivery/runbookForm';
import { emit, page, renderW4Routes, resetW4 } from '../test/w4';

jest.mock('@dashboard/api/client', () => require('../test/w4Client').autoMockClient());

const api = client as jest.Mocked<typeof client>;

const ROUTES = {
  '(work)/_layout': WorkLayout,
  '(work)/home': () => null,
  '(work)/delivery': DeliveryRoute,
  '(work)/checks/[id]': CheckRoute,
  '(work)/incidents/[id]': IncidentRoute,
  '(work)/releases/new': ReleaseNewRoute,
  '(work)/releases/[id]': ReleaseRoute,
  '(work)/runbooks/[id]': RunbookRoute,
};

function release(over: Partial<Release> = {}): Release {
  return {
    id: 'rel_1', tenantId: 'ten_1', userId: 'usr_1', vesselId: 'vsl_1', workflowProfileId: null, title: 'Release 2.3', version: '2.3.0', tagName: 'v2.3.0',
    summary: null, notes: null, status: 'Draft', voyageIds: [], missionIds: ['msn_1'], checkRunIds: [], artifacts: [],
    createdUtc: '2026-10-01T00:00:00Z', lastUpdateUtc: '2026-10-02T00:00:00Z', publishedUtc: null, ...over,
  };
}

function incident(over: Partial<Incident> = {}): Incident {
  return {
    id: 'inc_1', tenantId: 'ten_1', userId: 'usr_1', title: 'Outage', summary: 'Down', status: 'Open', severity: 'High', environmentId: null,
    environmentName: 'production', deploymentId: 'dpl_1', releaseId: null, vesselId: 'vsl_1', missionId: null, voyageId: null, rollbackDeploymentId: null,
    impact: null, rootCause: null, recoveryNotes: null, postmortem: null, failureKind: null, recoveryAttempts: 0, rescueMissionIds: [],
    detectedUtc: '2026-10-01T00:00:00Z', mitigatedUtc: null, closedUtc: null, lastUpdateUtc: '2026-10-02T00:00:00Z', ...over,
  };
}

function check(over: Partial<CheckRun> = {}): CheckRun {
  return {
    id: 'chk_2', tenantId: null, userId: null, workflowProfileId: null, vesselId: 'vsl_1', missionId: null, voyageId: null, deploymentId: null, label: 'Nightly',
    type: 'UnitTest', source: 'Armada', status: 'Failed', providerName: null, externalId: null, externalUrl: null, environmentName: null, command: 'npm test',
    workingDirectory: null, branchName: 'main', commitHash: null, exitCode: 1, output: 'boom', summary: null,
    testSummary: { format: 'junit', total: 10, passed: 8, failed: 2, skipped: 0, durationMs: 100 }, coverageSummary: null, artifacts: [], durationMs: 1200,
    startedUtc: null, completedUtc: null, createdUtc: '2026-10-02T00:00:00Z', lastUpdateUtc: '2026-10-02T00:00:00Z', ...over,
  };
}

function runbook(over: Partial<Runbook> = {}): Runbook {
  return {
    id: 'rbk_1', playbookId: 'pbk_1', tenantId: 'ten_1', userId: 'usr_1', scope: 'TenantWide', fileName: 'RUNBOOK.md', title: 'Rollback', description: null,
    workflowProfileId: null, environmentId: null, environmentName: null, defaultCheckType: null,
    parameters: [{ name: 'version', label: 'Version', description: null, defaultValue: '1.0', required: true }],
    steps: [{ id: 'rbs_1', title: 'Drain', instructions: 'Drain traffic' }, { id: 'rbs_2', title: 'Restore', instructions: 'Restore' }],
    overviewMarkdown: '', active: true, createdUtc: '2026-10-01T00:00:00Z', lastUpdateUtc: '2026-10-02T00:00:00Z', ...over,
  };
}

function execution(over: Partial<RunbookExecution> = {}): RunbookExecution {
  return {
    id: 'rbx_1', runbookId: 'rbk_1', playbookId: 'pbk_1', tenantId: 'ten_1', userId: 'usr_1', title: 'Run 1', status: 'Running', workflowProfileId: null,
    environmentId: null, environmentName: null, checkType: null, deploymentId: null, incidentId: null, parameterValues: {}, completedStepIds: [],
    stepNotes: {}, notes: null, startedUtc: '2026-10-02T00:00:00Z', completedUtc: null, lastUpdateUtc: '2026-10-02T00:00:00Z', ...over,
  };
}

beforeEach(async () => {
  await resetW4();
  jest.clearAllMocks();
  api.listVessels.mockResolvedValue(page([{ id: 'vsl_1', name: 'armada' }]) as never);
  for (const fn of [api.listEnvironments, api.listReleases, api.listWorkflowProfiles, api.listRunbookExecutions, api.listDeployments, api.listVoyages, api.listObjectives, api.listCheckRuns, api.listIncidents, api.listRunbooks]) {
    fn.mockResolvedValue(page([]) as never);
  }
  api.getReleaseGitHubPullRequests.mockResolvedValue([]);
});

describe('link helpers', () => {
  it('reads run-check and runbook-execution prefills, and builds Draft Release links', () => {
    expect(checkPrefillFrom({ tab: 'checks', vesselId: 'vsl_1' })).toBeNull();
    expect(checkPrefillFrom({ run: '1', vesselId: 'vsl_1', type: 'Bogus', label: 'L' })).toEqual({ vesselId: 'vsl_1', label: 'L' });
    expect(checkPrefillFrom({ run: '1', type: 'HealthCheck' })).toEqual({ type: 'HealthCheck' });
    expect(executionPrefillFrom({ tab: 'runbooks' })).toBeNull();
    expect(executionPrefillFrom({ execEnvironmentId: 'env_1', execCheckType: 'HealthCheck' })).toEqual({ environmentId: 'env_1', checkType: 'HealthCheck' });
    expect(draftReleaseLink(check({ voyageId: 'vyg_1' }))).toBe('/releases/new?vesselId=vsl_1&voyageIds=vyg_1&checkRunIds=chk_2&title=Nightly%20Release');
  });

  it('builds the incident payload with UTC times and links a deployment', () => {
    const v = { ...newIncidentValues({ severity: 'Critical', status: 'Bogus' }), detectedUtc: '2026-10-01T10:30', closedUtc: 'nope' };
    const p = incidentPayload(v);
    expect(p).toMatchObject({ severity: 'Critical', status: 'Open', closedUtc: null });
    expect(p.detectedUtc).toBe(new Date('2026-10-01T10:30').toISOString());
    const d = { id: 'dpl_1', environmentId: 'env_1', environmentName: 'production', vesselId: 'vsl_1', releaseId: 'rel_1', missionId: null, voyageId: null } as Deployment;
    const base = newIncidentValues();
    expect(linkIncidentValues({ ...base, deploymentId: 'dpl_1' }, base, [], [d])).toMatchObject({ environmentId: 'env_1', vesselId: 'vsl_1', releaseId: 'rel_1' });
  });
});

describe('releases', () => {
  it('lists releases and creates one', async () => {
    api.listReleases.mockResolvedValue(page([release()]) as never);
    api.createRelease.mockResolvedValue(release({ id: 'rel_2' }));
    await renderW4Routes(ROUTES, '/delivery?tab=releases');
    await waitFor(() => expect(screen.getByTestId('release-row-rel_1')).toBeTruthy());
    expect(screen.getByLabelText('Total Releases: 1')).toBeTruthy();
    await fireEvent.press(screen.getByTestId('releases-create'));
    await waitFor(() => expect(screen.getByTestId('release-form-missionIds')).toBeTruthy());
    await fireEvent.changeText(screen.getByTestId('release-form-missionIds'), 'msn_1, msn_2');
    await act(async () => { await fireEvent.press(screen.getByTestId('release-form-submit')); });
    await waitFor(() => expect(api.createRelease).toHaveBeenCalledWith(expect.objectContaining({ title: 'Draft Release', status: 'Draft', missionIds: ['msn_1', 'msn_2'], objectiveIds: [] })));
  });

  it('refreshes derived fields and shows linked pull requests', async () => {
    api.getRelease.mockResolvedValue(release());
    api.refreshRelease.mockResolvedValue(release({ title: 'Release 2.3 (refreshed)' }));
    api.getReleaseGitHubPullRequests.mockResolvedValue([{ repository: 'org/repo', number: 7, title: 'Fix', url: 'https://x', state: 'open', reviewStatus: 'Approved', checks: [], reviews: [], requestedReviewers: [], updatedUtc: null } as never]);
    await renderW4Routes(ROUTES, '/releases/rel_1');
    await waitFor(() => expect(screen.getByTestId('release-title')).toHaveTextContent('Release 2.3'));
    await waitFor(() => expect(screen.getByText('Fix')).toBeTruthy());
    await act(async () => { await fireEvent.press(screen.getByTestId('release-refresh')); });
    expect(api.refreshRelease).toHaveBeenCalledWith('rel_1');
    await waitFor(() => expect(screen.getByTestId('release-title')).toHaveTextContent('Release 2.3 (refreshed)'));
  });

  it('/releases/new takes the prefill and backlog items from the link', async () => {
    api.createRelease.mockResolvedValue(release({ id: 'rel_5' }));
    api.getRelease.mockResolvedValue(release({ id: 'rel_5' }));
    const h = await renderW4Routes(ROUTES, '/releases/new?checkRunIds=chk_1&title=Nightly%20Release&objectiveIds=obj_1');
    await waitFor(() => expect(screen.getByTestId('release-form-submit')).toBeTruthy());
    await act(async () => { await fireEvent.press(screen.getByTestId('release-form-submit')); });
    expect(api.createRelease).toHaveBeenCalledWith(expect.objectContaining({ title: 'Nightly Release', checkRunIds: ['chk_1'], objectiveIds: ['obj_1'] }));
    await waitFor(() => expect(h.getPathname()).toBe('/releases/rel_5'));
  });
});

describe('incidents', () => {
  it('lists incidents and reloads on incident events', async () => {
    api.listIncidents.mockResolvedValue(page([incident()]) as never);
    const h = await renderW4Routes(ROUTES, '/delivery?tab=incidents');
    await waitFor(() => expect(screen.getByTestId('incident-row-inc_1')).toBeTruthy());
    expect(screen.getByLabelText('Open: 1')).toBeTruthy();
    await emit(h, { type: 'incident.changed', data: { id: 'inc_1' } });
    await waitFor(() => expect(api.listIncidents.mock.calls.length).toBeGreaterThanOrEqual(2));
  });

  it('rolls back the linked deployment and marks the incident rolled back', async () => {
    api.getIncident.mockResolvedValue(incident());
    api.rollbackDeployment.mockResolvedValue({} as never);
    api.updateIncident.mockResolvedValue(incident({ status: 'RolledBack', rollbackDeploymentId: 'dpl_1' }));
    await renderW4Routes(ROUTES, '/incidents/inc_1');
    await waitFor(() => expect(screen.getByTestId('incident-rollback')).toBeTruthy());
    await fireEvent.press(screen.getByTestId('incident-rollback'));
    await waitFor(() => expect(screen.getByText('Rollback deployment "dpl_1" and attach the result to this incident?')).toBeTruthy());
    await act(async () => { await fireEvent.press(screen.getByTestId('incident-confirm-confirm')); });
    expect(api.rollbackDeployment).toHaveBeenCalledWith('dpl_1');
    expect(api.updateIncident).toHaveBeenCalledWith('inc_1', { rollbackDeploymentId: 'dpl_1', status: 'RolledBack' });
  });

  it('new opens the full form prefilled from an environment link', async () => {
    api.createIncident.mockResolvedValue(incident({ id: 'inc_9' }));
    api.getIncident.mockResolvedValue(incident({ id: 'inc_9' }));
    await renderW4Routes(ROUTES, '/incidents/new?vesselId=vsl_1&environmentName=production&title=production%20Incident&severity=Medium');
    await waitFor(() => expect(screen.getByTestId('incident-form-submit')).toBeTruthy());
    await act(async () => { await fireEvent.press(screen.getByTestId('incident-form-submit')); });
    expect(api.createIncident).toHaveBeenCalledWith(expect.objectContaining({ title: 'production Incident', severity: 'Medium', status: 'Open', vesselId: 'vsl_1', environmentName: 'production' }));
  });
});

describe('checks', () => {
  it('a Run Check link opens the prefilled sheet and runs the check', async () => {
    api.runCheck.mockResolvedValue(check({ id: 'chk_9', status: 'Passed' }));
    api.getCheckRun.mockResolvedValue(check({ id: 'chk_9', status: 'Passed' }));
    const h = await renderW4Routes(ROUTES, '/delivery?tab=checks&run=1&vesselId=vsl_1&label=production&type=Build');
    await waitFor(() => expect(screen.getByTestId('run-check-submit')).toBeTruthy());
    await act(async () => { await fireEvent.press(screen.getByTestId('run-check-submit')); });
    expect(api.runCheck).toHaveBeenCalledWith(expect.objectContaining({ vesselId: 'vsl_1', type: 'Build', label: 'production', environmentName: null }));
    await waitFor(() => expect(h.getPathname()).toBe('/checks/chk_9'));
  });

  it('requires a vessel before running', async () => {
    await renderW4Routes(ROUTES, '/delivery?tab=checks');
    await waitFor(() => expect(screen.getByTestId('checks-run')).toBeTruthy());
    await fireEvent.press(screen.getByTestId('checks-run'));
    await waitFor(() => expect(screen.getByTestId('run-check-submit')).toBeTruthy());
    await act(async () => { await fireEvent.press(screen.getByTestId('run-check-submit')); });
    expect(screen.getByTestId('run-check-error')).toHaveTextContent('Select a vessel before running a check.');
    expect(api.runCheck).not.toHaveBeenCalled();
  });

  it('shows the comparison to the previous run and retries', async () => {
    const previous = check({ id: 'chk_1', status: 'Passed', createdUtc: '2026-10-01T00:00:00Z', testSummary: { format: 'junit', total: 10, passed: 10, failed: 0, skipped: 0, durationMs: 90 } });
    api.getCheckRun.mockResolvedValue(check());
    api.listCheckRuns.mockResolvedValue(page([check(), previous]) as never);
    api.retryCheckRun.mockResolvedValue(check({ id: 'chk_3', status: 'Passed' }));
    const h = await renderW4Routes(ROUTES, '/checks/chk_2');
    await waitFor(() => expect(screen.getByTestId('check-title')).toHaveTextContent('Nightly'));
    await waitFor(() => expect(screen.getByText('Regression detected')).toBeTruthy());
    expect(api.listCheckRuns).toHaveBeenCalledWith({ pageSize: 1000, filters: { vesselId: 'vsl_1', type: 'UnitTest' } });
    await act(async () => { await fireEvent.press(screen.getByTestId('check-retry')); });
    expect(api.retryCheckRun).toHaveBeenCalledWith('chk_2');
    await waitFor(() => expect(h.getPathname()).toBe('/checks/chk_3'));
  });
});

describe('runbooks', () => {
  it('creates a runbook with the tenant-wide default for admins', async () => {
    api.createRunbook.mockResolvedValue(runbook({ id: 'rbk_9' }));
    await renderW4Routes(ROUTES, '/delivery?tab=runbooks');
    await waitFor(() => expect(screen.getByTestId('runbooks-create')).toBeTruthy());
    await fireEvent.press(screen.getByTestId('runbooks-create'));
    await waitFor(() => expect(screen.getByTestId('runbook-form-submit')).toBeTruthy());
    await act(async () => { await fireEvent.press(screen.getByTestId('runbook-form-submit')); });
    expect(api.createRunbook).toHaveBeenCalledWith(expect.objectContaining({ fileName: 'RUNBOOK.md', title: 'Runbook', scope: 'TenantWide', steps: [], parameters: [] }));
  });

  it('regular users create personal runbooks', async () => {
    api.createRunbook.mockResolvedValue(runbook({ id: 'rbk_9' }));
    await renderW4Routes(ROUTES, '/delivery?tab=runbooks', 'user');
    await waitFor(() => expect(screen.getByTestId('runbooks-create')).toBeTruthy());
    await fireEvent.press(screen.getByTestId('runbooks-create'));
    await waitFor(() => expect(screen.getByTestId('runbook-form-submit')).toBeTruthy());
    await act(async () => { await fireEvent.press(screen.getByTestId('runbook-form-submit')); });
    expect(api.createRunbook).toHaveBeenCalledWith(expect.objectContaining({ scope: 'UserSpecific' }));
  });

  it('starts an execution with the handed-over context and completes a step', async () => {
    api.getRunbook.mockResolvedValue(runbook());
    api.listRunbookExecutions.mockResolvedValueOnce(page([]) as never).mockResolvedValue(page([execution()]) as never);
    api.startRunbookExecution.mockResolvedValue(execution());
    api.getRunbookExecution.mockResolvedValue(execution());
    api.updateRunbookExecution.mockResolvedValue(execution({ status: 'Completed', completedStepIds: ['rbs_1'] }));
    await renderW4Routes(ROUTES, '/runbooks/rbk_1?execIncidentId=inc_1&execEnvironmentName=production&execCheckType=Custom');
    await waitFor(() => expect(screen.getByTestId('runbook-title')).toHaveTextContent('Rollback'));
    await fireEvent.press(screen.getByTestId('runbook-start'));
    await waitFor(() => expect(screen.getByTestId('runbook-start-form-submit')).toBeTruthy());
    await act(async () => { await fireEvent.press(screen.getByTestId('runbook-start-form-submit')); });
    expect(api.startRunbookExecution).toHaveBeenCalledWith('rbk_1', expect.objectContaining({
      environmentName: 'production', checkType: 'Custom', incidentId: 'inc_1', deploymentId: null, parameterValues: { version: '1.0' },
    }));
    await waitFor(() => expect(screen.getByTestId('runbook-step-done-0')).toBeTruthy());
    await fireEvent(screen.getByTestId('runbook-step-done-0'), 'valueChange', true);
    await act(async () => { await fireEvent.press(screen.getByTestId('runbook-mark-completed')); });
    expect(api.updateRunbookExecution).toHaveBeenCalledWith('rbx_1', expect.objectContaining({ status: 'Completed', completedStepIds: ['rbs_1'] }));
  });

  it('adds a step and saves the runbook', async () => {
    api.getRunbook.mockResolvedValue(runbook());
    api.updateRunbook.mockImplementation(async (_id, body) => runbook({ steps: body.steps ?? [] }));
    await renderW4Routes(ROUTES, '/runbooks/rbk_1');
    await waitFor(() => expect(screen.getByTestId('runbook-add-step')).toBeTruthy());
    await fireEvent.press(screen.getByTestId('runbook-add-step'));
    await waitFor(() => expect(screen.getByTestId('runbook-step-form-title')).toBeTruthy());
    await fireEvent.changeText(screen.getByTestId('runbook-step-form-title'), 'Verify');
    await act(async () => { await fireEvent.press(screen.getByTestId('runbook-step-form-submit')); });
    expect(api.updateRunbook).toHaveBeenCalledWith('rbk_1', expect.objectContaining({ steps: [expect.objectContaining({ id: 'rbs_1' }), expect.objectContaining({ id: 'rbs_2' }), expect.objectContaining({ title: 'Verify' })] }));
  });
});
