import { act, fireEvent, screen, waitFor } from '@testing-library/react-native';
import * as client from '@dashboard/api/client';
import type { DashboardStatusData } from '@dashboard/lib/dashboardStatus';
import type { MissionSummary } from '@dashboard/types/models';
import { HomeScreen } from '../screens/operations/HomeScreen';
import { page } from '../test/operationsClient';
import { mockRouter } from '../test/routerMock';
import { renderScreen } from '../test/screen';

jest.mock('@dashboard/api/client', () => require('../test/operationsClient').operationsClientMockFactory());
jest.mock('expo-router', () => require('../test/routerMock').routerMockFactory());

const mockWindow = { width: 390, height: 844 };
jest.mock('react-native/Libraries/Utilities/useWindowDimensions', () => ({
  __esModule: true,
  default: () => ({ width: mockWindow.width, height: mockWindow.height, scale: 2, fontScale: 1 }),
}));

const api = client as jest.Mocked<typeof client>;

const STATUS: DashboardStatusData = {
  totalCaptains: 3, idleCaptains: 1, workingCaptains: 1, stalledCaptains: 1, activeVoyages: 1,
  missionsByStatus: { InProgress: 2, Failed: 1 },
  voyages: [{ voyage: { id: 'vyg_1', title: 'Batch one', status: 'InProgress' }, totalMissions: 4, completedMissions: 1, failedMissions: 1, vesselIds: ['vsl_1'] }],
  recentSignals: [{ id: 'sig_1', type: 'Progress', message: 'Halfway there', createdUtc: '2026-10-07T10:00:00Z' }],
};

const MISSIONS = [
  { id: 'msn_1', title: 'Fix login', status: 'Failed', vesselId: 'vsl_1', captainId: 'cpt_1', createdUtc: '2026-10-07T09:00:00Z' },
  { id: 'msn_2', title: 'Add docs', status: 'InProgress', vesselId: 'vsl_1', captainId: 'cpt_1', createdUtc: '2026-10-07T09:30:00Z' },
] as unknown as MissionSummary[];

beforeEach(() => {
  mockWindow.width = 390;
  mockWindow.height = 844;
  api.getStatus.mockResolvedValue(STATUS as unknown as Awaited<ReturnType<typeof client.getStatus>>);
  api.listMissionSummaries.mockResolvedValue(page(MISSIONS as never[]) as never);
  api.enumerateFleetActionRuns.mockImplementation(async (q) => ({ ...page([]), totalRecords: q?.status === 'Running' ? 2 : 5 }) as never);
  api.getHealth.mockResolvedValue({ status: 'healthy', version: '1.0.0', uptime: '0.01:00:00' });
  api.getVesselHealthSummary.mockResolvedValue({ totalVessels: 2, fail: 1, warn: 0, notEvaluated: 1, outdatedMajorVessels: 0, highOrCriticalVulnerabilityVessels: 1 } as never);
  api.getMissionHistory.mockResolvedValue({ totalCount: 3, completeCount: 2, failedCount: 1, otherCount: 0, fromUtc: '', toUtc: '', bucketMinutes: 60,
    buckets: [{ startUtc: '2026-10-07T08:00:00Z', completeCount: 2, failedCount: 1, otherCount: 0, totalCount: 3 }] });
  api.listVessels.mockResolvedValue(page([{ id: 'vsl_1', name: 'api', fleetId: 'flt_1' }]) as never);
  api.listCaptains.mockResolvedValue(page([{ id: 'cpt_1', name: 'claude-1' }]) as never);
  api.listFleets.mockResolvedValue(page([{ id: 'flt_1', name: 'main' }]) as never);
  api.deleteMission.mockResolvedValue(undefined);
  api.restartMission.mockResolvedValue({} as never);
});

describe('Home', () => {
  it('shows status, KPIs, alerts, health, voyage progress, recent missions and signals', async () => {
    await renderScreen(<HomeScreen />);
    await screen.findByTestId('home-screen');
    expect(screen.getByTestId('home-kpi-captains-value')).toHaveTextContent('3');
    expect(screen.getByTestId('home-kpi-missions-value')).toHaveTextContent('3');
    expect(screen.getByTestId('home-kpi-fleet-actions-value')).toHaveTextContent('7');
    expect(screen.getByText('1 captain(s) stalled -- recovery attempts exhausted.')).toBeTruthy();
    expect(screen.getByText('1 mission(s) failed.')).toBeTruthy();
    expect(screen.getByText('Healthy')).toBeTruthy();
    expect(screen.getByText('v1.0.0')).toBeTruthy();
    expect(screen.getByTestId('home-health-failing-value')).toHaveTextContent('1');
    expect(screen.getByTestId('home-voyage-vyg_1')).toBeTruthy();
    expect(screen.getByText('1/4 done, 1 failed')).toBeTruthy();
    expect(screen.getByTestId('home-mission-msn_1')).toBeTruthy();
    expect(screen.getByText('Halfway there')).toBeTruthy();
    await waitFor(() => expect(screen.getAllByText('api - claude-1', { exact: false }).length).toBe(2));
    // Phones list the Work destinations after the overview.
    expect(screen.getByTestId('work-menu')).toBeTruthy();
  });

  it('KPIs and alerts open the related lists', async () => {
    await renderScreen(<HomeScreen />);
    await fireEvent.press(await screen.findByTestId('home-kpi-voyages'));
    expect(mockRouter.push).toHaveBeenCalledWith('/missions?tab=voyages');
    await fireEvent.press(screen.getByTestId('home-kpi-fleet-actions'));
    expect(mockRouter.push).toHaveBeenCalledWith('/fleet-actions?tab=runs&status=Running');
    await fireEvent.press(screen.getByText('1 captain(s) stalled -- recovery attempts exhausted.'));
    expect(mockRouter.push).toHaveBeenCalledWith('/captains');
  });

  it('filters recent missions by status', async () => {
    await renderScreen(<HomeScreen />);
    await screen.findByTestId('home-mission-msn_2');
    await fireEvent.press(screen.getByTestId('home-mission-filters'));
    await fireEvent.press(screen.getByTestId('home-filter-status'));
    await fireEvent.press(screen.getByTestId('home-filter-status-option-Failed'));
    expect(screen.queryByTestId('home-mission-msn_2')).toBeNull();
    expect(screen.getByTestId('home-mission-msn_1')).toBeTruthy();
  });

  it('row actions restart and delete (after confirming)', async () => {
    await renderScreen(<HomeScreen />);
    await fireEvent.press(await screen.findByTestId('home-mission-msn_1-menu'));
    await fireEvent.press(screen.getByTestId('home-mission-menu-restart'));
    await waitFor(() => expect(api.restartMission).toHaveBeenCalledWith('msn_1'));
    await fireEvent.press(screen.getByTestId('home-mission-msn_1-menu'));
    await fireEvent.press(screen.getByTestId('home-mission-menu-delete'));
    expect(api.deleteMission).not.toHaveBeenCalled();
    await fireEvent.press(screen.getByTestId('home-confirm-confirm'));
    await waitFor(() => expect(api.deleteMission).toHaveBeenCalledWith('msn_1'));
  });

  it('only failed, cancelled, or landing-failed missions offer Restart', async () => {
    await renderScreen(<HomeScreen />);
    await fireEvent.press(await screen.findByTestId('home-mission-msn_2-menu'));
    expect(screen.queryByTestId('home-mission-menu-restart')).toBeNull();
  });

  it('reloads on live socket activity', async () => {
    const { socket } = await renderScreen(<HomeScreen />);
    await screen.findByTestId('home-screen');
    const before = api.getStatus.mock.calls.length;
    api.getStatus.mockResolvedValue({ ...STATUS, totalCaptains: 9 } as never);
    await act(async () => { socket.message({ type: 'captain.changed', data: { id: 'cpt_1', state: 'Idle' } }); });
    await waitFor(() => expect(screen.getByTestId('home-kpi-captains-value')).toHaveTextContent('9'));
    expect(api.getStatus.mock.calls.length).toBeGreaterThan(before);
  });

  it('the mission history chart follows the time range', async () => {
    await renderScreen(<HomeScreen />);
    await screen.findByTestId('mission-history-chart');
    expect(api.getMissionHistory).toHaveBeenLastCalledWith(expect.objectContaining({ bucketMinutes: 60 }));
    await fireEvent.press(screen.getByTestId('mission-history-range-hour'));
    await waitFor(() => expect(api.getMissionHistory).toHaveBeenLastCalledWith(expect.objectContaining({ bucketMinutes: 1 })));
  });

  it('screen readers read the chart totals and step through its time buckets', async () => {
    await renderScreen(<HomeScreen />);
    const chart = await screen.findByTestId('mission-history-chart');
    expect(chart.props.accessibilityRole).toBe('adjustable');
    expect(chart.props.accessibilityLabel).toBe('Mission History: 3 total, 2 complete, 1 failed');
    await fireEvent(chart, 'accessibilityAction', { nativeEvent: { actionName: 'increment' } });
    expect(screen.getByTestId('mission-history-chart').props.accessibilityValue.text).toMatch(/Complete 2, Failed 1$/);
    await fireEvent(screen.getByTestId('mission-history-chart'), 'accessibilityAction', { nativeEvent: { actionName: 'decrement' } });
    expect(screen.getByTestId('mission-history-chart').props.accessibilityValue.text).toMatch(/Complete 2, Failed 1$/);
  });

  it('tablets rely on the sidebar instead of the Work list', async () => {
    mockWindow.width = 1024;
    mockWindow.height = 1366;
    await renderScreen(<HomeScreen />);
    await screen.findByTestId('home-screen');
    expect(screen.queryByTestId('work-menu')).toBeNull();
  });

  it('shows the error state when nothing loads', async () => {
    api.getStatus.mockRejectedValue(new Error('down'));
    api.listMissionSummaries.mockRejectedValue(new Error('down'));
    await renderScreen(<HomeScreen />);
    expect(await screen.findByText('Something went wrong')).toBeTruthy();
  });
});
