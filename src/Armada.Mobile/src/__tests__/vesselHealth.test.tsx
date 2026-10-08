import AsyncStorage from '@react-native-async-storage/async-storage';
import { act, fireEvent, screen, waitFor } from '@testing-library/react-native';
import { Slot } from 'expo-router';
import { renderRouter } from 'expo-router/testing-library';
import { Text } from 'react-native';
import * as client from '@dashboard/api/client';
import type { VesselHealth, VesselHealthDetail } from '@dashboard/types/models';
import * as auth from '../auth/AuthContext';
import { VesselHealthTab, filtersFromParams } from '../screens/vessels/health/VesselHealthTab';
import { BuildProviders, buildSockets, page } from '../test/buildFixtures';

jest.mock('@dashboard/api/client', () => require('../test/buildClientMock').buildClientMockFactory());

const api = client as jest.Mocked<typeof client>;
const realUseAuth = auth.useAuth;

function row(over: Partial<VesselHealth> = {}): VesselHealth {
  return {
    id: 'vhl_1', vesselId: 'vsl_1', vesselName: 'demo-api', overallStatus: 'Warn', currentBranch: 'main', fleetName: 'Core',
    isDirty: true, untrackedCount: 2, aheadOfDefault: 1, behindDefault: 3, evaluatedUtc: '2026-10-07T10:00:00Z',
    dependencyStatus: 'Pass', vulnerabilityStatus: 'Fail', testInfraStatus: 'Warn', ciStatus: 'Pass', divergenceStatus: 'Warn',
    workingTreeStatus: 'Warn', branchStatus: 'Pass', readinessStatus: 'Pass', missionOutcomeStatus: 'Pass', ...over,
  };
}

function detail(over: Partial<VesselHealthDetail> = {}): VesselHealthDetail {
  return {
    health: row(),
    findings: [{ vesselId: 'vsl_1', criterion: 'TestInfrastructure', status: 'Warn', detailCode: null }],
    dependencies: [{ vesselId: 'vsl_1', ecosystem: 'npm', packageName: 'left-pad', currentVersion: '1.0.0', latestVersion: '2.0.0', drift: 'Major', isVulnerable: false }],
    overrides: [],
    ...over,
  };
}

const ROUTES = {
  _layout: () => <BuildProviders><Slot /></BuildProviders>,
  '(work)/vessels/health': () => <VesselHealthTab />,
  '(work)/vessels/[id]': () => <Text>Vessel screen</Text>,
  '(work)/vessels/import': () => <Text>Import screen</Text>,
  '(work)/fleet-actions/index': () => <Text>Fleet actions screen</Text>,
};

async function renderHealth(url = '/vessels/health') {
  const result = renderRouter(ROUTES, { initialUrl: url });
  await result;
  await act(async () => { buildSockets()[0]?.open(); });
  return result;
}

function lastRequest() {
  const calls = api.enumerateVesselHealth.mock.calls;
  return calls[calls.length - 1][0];
}

describe('vessel health tab', () => {
  let admin = false;
  beforeEach(async () => {
    await AsyncStorage.clear();
    admin = false;
    jest.spyOn(auth, 'useAuth').mockImplementation(() => ({ ...realUseAuth(), isTenantAdmin: admin, isAdmin: admin }));
    api.enumerateVesselHealth.mockResolvedValue(page([row()]));
    api.getVesselHealthSummary.mockResolvedValue({ totalVessels: 1, pass: 0, warn: 1, fail: 0, unknown: 0, notApplicable: 0, notEvaluated: 0, outdatedMajorVessels: 0, highOrCriticalVulnerabilityVessels: 0 });
    api.listFleets.mockResolvedValue(page([{ id: 'flt_1', name: 'Core' } as never]));
    api.listVessels.mockResolvedValue(page([{ id: 'vsl_1', name: 'demo-api', defaultBranch: 'main' } as never]));
    api.listJobs.mockResolvedValue(page([]));
    api.getVesselHealth.mockResolvedValue(detail());
  });
  afterEach(() => jest.restoreAllMocks());

  it('reads Home KPI filters from the route query', () => {
    expect(filtersFromParams({ overall: 'Fail,Warn', dirty: 'yes', tab: 'health' })).toMatchObject({ overall: ['Warn', 'Fail'], dirty: 'yes', page: 1 });
  });

  it('lists vessels with their statuses and filters by a summary tile', async () => {
    await renderHealth('/vessels/health?overall=Warn');
    await waitFor(() => expect(screen.getByText('demo-api')).toBeTruthy());
    expect(lastRequest()).toMatchObject({ OverallStatus: ['Warn'], PageNumber: 1 });
    expect(screen.getByText('Vulnerabilities: Fail')).toBeTruthy();
    expect(screen.queryByTestId('health-evaluate-all')).toBeNull();

    await act(async () => { await fireEvent.press(screen.getByTestId('health-tile-fail')); });
    await waitFor(() => expect(lastRequest()).toMatchObject({ OverallStatus: ['Fail'] }));
  });

  it('applies filters from the sheet', async () => {
    await renderHealth();
    await waitFor(() => expect(screen.getByText('demo-api')).toBeTruthy());
    await act(async () => { await fireEvent.press(screen.getByTestId('vessel-health-list-filters')); });
    await act(async () => { await fireEvent.press(screen.getByTestId('health-filter-overall-Fail')); });
    await act(async () => { await fireEvent.press(screen.getByLabelText('Has CI')); });
    await act(async () => { await fireEvent.press(screen.getByTestId('health-filters-apply')); });
    await waitFor(() => expect(lastRequest()).toMatchObject({ OverallStatus: ['Fail'], HasCiConfig: true }));
  });

  it('shows the detail and lets a tenant admin set and remove overrides and evaluate', async () => {
    admin = true;
    api.evaluateVesselHealth.mockResolvedValue({ jobId: 'job_1', alreadyRunning: false, vesselCount: 1 });
    api.getJob.mockResolvedValue({ id: 'job_1', status: 'Running', progress: 40 } as never);
    api.setVesselHealthOverride.mockResolvedValue(detail({ overrides: [{ vesselId: 'vsl_1', criterion: 'Overall', status: 'Pass', note: 'known' }] }));
    api.deleteVesselHealthOverride.mockResolvedValue(detail());
    await renderHealth();
    await waitFor(() => expect(screen.getByText('demo-api')).toBeTruthy());

    await act(async () => { await fireEvent.press(screen.getByTestId('health-evaluate-all')); });
    expect(api.evaluateVesselHealth).toHaveBeenCalledWith({ Force: true });
    await waitFor(() => expect(screen.getByTestId('health-evaluating')).toBeTruthy());

    await act(async () => { await fireEvent.press(screen.getByTestId('health-open-demo-api')); });
    await waitFor(() => expect(api.getVesselHealth).toHaveBeenCalledWith('vsl_1'));
    await act(async () => { await fireEvent.press(screen.getByTestId('hub-tab-dependencies')); });
    expect(screen.getByText('left-pad')).toBeTruthy();
    await act(async () => { await fireEvent.press(screen.getByTestId('hub-tab-overrides')); });
    await act(async () => { await fireEvent.changeText(screen.getByTestId('health-override-note'), ' known '); });
    await act(async () => { await fireEvent.press(screen.getByTestId('health-override-save')); });
    expect(api.setVesselHealthOverride).toHaveBeenCalledWith('vsl_1', 'Overall', 'Pass', 'known');
    await waitFor(() => expect(screen.getByTestId('health-override-Overall')).toBeTruthy());

    await act(async () => { await fireEvent.press(screen.getByTestId('health-override-remove-Overall')); });
    await act(async () => { await fireEvent.press(screen.getByTestId('health-override-remove-confirm-confirm')); });
    expect(api.deleteVesselHealthOverride).toHaveBeenCalledWith('vsl_1', 'Overall');
    await waitFor(() => expect(screen.queryByTestId('health-override-Overall')).toBeNull());
  });

  it('selects vessels with a long press for bulk re-evaluation', async () => {
    admin = true;
    api.evaluateVesselHealth.mockResolvedValue({ jobId: 'job_2', alreadyRunning: false, vesselCount: 1 });
    api.getJob.mockResolvedValue({ id: 'job_2', status: 'Running', progress: 0 } as never);
    await renderHealth();
    await waitFor(() => expect(screen.getByText('demo-api')).toBeTruthy());
    await act(async () => { await fireEvent(screen.getByTestId('health-open-demo-api'), 'longPress'); });
    expect(screen.getByText('1 vessel selected')).toBeTruthy();
    await act(async () => { await fireEvent.press(screen.getByText('Re-evaluate selected')); });
    expect(api.evaluateVesselHealth).toHaveBeenCalledWith({ VesselIds: ['vsl_1'], Force: true });
    await waitFor(() => expect(screen.queryByTestId('health-bulk')).toBeNull());
  });
});
