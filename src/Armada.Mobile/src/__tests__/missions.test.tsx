import { act, fireEvent, screen, waitFor } from '@testing-library/react-native';
import * as client from '@dashboard/api/client';
import type { Mission, MissionSummary } from '@dashboard/types/models';
import { StyleSheet } from 'react-native';
import MissionRoute from '../app/(app)/(work)/missions/[id]';
import { MissionDetail, missionDetailTab } from '../screens/operations/MissionDetail';
import { MissionsHubScreen } from '../screens/operations/MissionsHubScreen';
import { missionServerFilters, userScopeLabel } from '../screens/operations/MissionsList';
import { missionFormValid } from '../screens/operations/mission/MissionFormSheet';
import { page } from '../test/operationsClient';
import { mockRouter, setMockParams } from '../test/routerMock';
import { renderScreen } from '../test/screen';

jest.mock('@dashboard/api/client', () => ({
  ...require('../test/operationsClient').operationsClientMockFactory(),
  listCheckRuns: jest.fn(async () => ({ objects: [], totalRecords: 0 })),
  listDeployments: jest.fn(async () => ({ objects: [], totalRecords: 0 })),
}));
jest.mock('expo-router', () => require('../test/routerMock').routerMockFactory());

const mockWindow = { width: 390, height: 844 };
jest.mock('react-native/Libraries/Utilities/useWindowDimensions', () => ({
  __esModule: true,
  default: () => ({ width: mockWindow.width, height: mockWindow.height, scale: 2, fontScale: 1 }),
}));

const api = client as jest.Mocked<typeof client>;

function summary(id: string, overrides: Partial<MissionSummary> = {}): MissionSummary {
  return {
    id, tenantId: null, userId: null, voyageId: null, vesselId: 'vsl_1', captainId: null, title: `Mission ${id}`, status: 'InProgress',
    priority: 100, parentMissionId: null, persona: null, dependsOnMissionId: null, branchName: `armada/${id}`, dockId: null,
    processId: null, prUrl: null, commitHash: null, failureReason: null, requiresReview: false, reviewDenyAction: 'RetryStage',
    reviewComment: null, reviewedByUserId: null, reviewRequestedUtc: null, reviewedUtc: null, descriptionLength: 0,
    diffSnapshotLength: 0, agentOutputLength: 0, createdUtc: '2026-10-07T10:00:00Z', startedUtc: null, completedUtc: null,
    totalRuntimeMs: null, lastUpdateUtc: '2026-10-07T10:00:00Z', ...overrides,
  };
}

function mission(overrides: Partial<Mission> = {}): Mission {
  const { descriptionLength: _d, diffSnapshotLength: _s, agentOutputLength: _a, ...rest } = summary('msn_1');
  return { ...rest, description: 'Do the thing', mode: 'Implementation', diffSnapshot: null, ...overrides };
}

beforeEach(() => {
  jest.clearAllMocks();
  mockWindow.width = 390;
  mockWindow.height = 844;
  setMockParams({});
  api.listVessels.mockResolvedValue(page([{ id: 'vsl_1', name: 'api' }] as never));
  api.listCaptains.mockResolvedValue(page([]));
  api.listUsers.mockResolvedValue(page([]) as never);
  api.getMissionLandingPreview.mockResolvedValue(null as never);
});

describe('mission helpers', () => {
  it('builds server filters, user labels, form validity, and tab keys', () => {
    expect(missionServerFilters('', '')).toEqual({});
    expect(missionServerFilters('Failed', 'usr_1')).toEqual({ status: 'Failed', userId: 'usr_1' });
    expect(userScopeLabel({ firstName: 'Ann', lastName: 'Lee', email: 'a@x' })).toBe('Ann Lee (a@x)');
    expect(userScopeLabel({ firstName: '', lastName: '', email: 'a@x' })).toBe('a@x');
    const values = { title: 'T', description: '', vesselId: '', priority: 100, mode: 'Implementation' as const };
    expect(missionFormValid(values, true)).toBe(false);
    expect(missionFormValid({ ...values, vesselId: 'vsl_1' }, true)).toBe(true);
    expect(missionFormValid({ ...values, priority: Number.NaN }, false)).toBe(false);
    expect(missionDetailTab('log')).toBe('log');
    expect(missionDetailTab('nope')).toBe('overview');
  });
});

describe('Missions list', () => {
  it('loads, pages endlessly, searches, and filters by status on the server', async () => {
    api.listMissionSummaries.mockImplementation(async (params) => {
      if (params?.filters?.status === 'Failed') return page([summary('msn_f', { status: 'Failed' })]);
      if (params?.pageNumber === 2) return { ...page([summary('msn_26')], 26), pageNumber: 2 };
      return page(Array.from({ length: 25 }, (_v, i) => summary(`msn_${i + 1}`, { title: i === 0 ? 'Fix login' : `Mission ${i + 1}` })), 26);
    });
    await renderScreen(<MissionsHubScreen />);
    expect(await screen.findByTestId('mission-row-msn_1')).toBeTruthy();
    expect(screen.getByTestId('missions-list-count')).toHaveTextContent('Showing 25 of 26');
    await act(async () => { fireEvent(screen.getByTestId('missions-list'), 'onEndReached'); });
    await waitFor(() => expect(screen.getByTestId('missions-list-count')).toHaveTextContent('Showing 26 of 26'));
    expect(api.listMissionSummaries).toHaveBeenLastCalledWith({ pageNumber: 2, pageSize: 25, filters: {} });

    await fireEvent.changeText(screen.getByTestId('missions-search'), 'login');
    expect(screen.getByTestId('mission-row-msn_1')).toBeTruthy();
    expect(screen.queryByTestId('mission-row-msn_2')).toBeNull();
    await fireEvent.changeText(screen.getByTestId('missions-search'), '');

    await fireEvent.press(screen.getByTestId('missions-filters'));
    await fireEvent.press(screen.getByTestId('missions-filter-status'));
    await fireEvent.press(screen.getByTestId('missions-filter-status-option-Failed'));
    expect(await screen.findByTestId('mission-row-msn_f')).toBeTruthy();
    expect(api.listMissionSummaries).toHaveBeenLastCalledWith({ pageNumber: 1, pageSize: 25, filters: { status: 'Failed' } });
  });

  it('row actions restart, retry landing, transition, and purge after confirmation', async () => {
    api.listMissionSummaries.mockResolvedValue(page([summary('msn_1', { status: 'LandingFailed' })]));
    api.restartMission.mockResolvedValue({} as never);
    api.retryMissionLanding.mockResolvedValue({} as never);
    api.transitionMission.mockResolvedValue({} as never);
    api.purgeMission.mockResolvedValue(undefined);
    await renderScreen(<MissionsHubScreen />);
    await screen.findByTestId("mission-row-menu-msn_1");
    const menu = () => screen.getByTestId("mission-row-menu-msn_1");

    await fireEvent.press(menu());
    await fireEvent.press(screen.getByTestId('mission-row-actions-restart'));
    await waitFor(() => expect(api.restartMission).toHaveBeenCalledWith('msn_1'));

    await fireEvent.press(menu());
    await fireEvent.press(screen.getByTestId('mission-row-actions-retry-landing'));
    await waitFor(() => expect(api.retryMissionLanding).toHaveBeenCalledWith('msn_1'));

    await fireEvent.press(menu());
    await fireEvent.press(screen.getByTestId('mission-row-actions-transition'));
    await fireEvent.press(screen.getByTestId('mission-transition-status'));
    await fireEvent.press(screen.getByTestId('mission-transition-status-option-Complete'));
    await fireEvent.press(screen.getByTestId('mission-transition-submit'));
    await waitFor(() => expect(api.transitionMission).toHaveBeenCalledWith('msn_1', { status: 'Complete' }));

    await fireEvent.press(menu());
    await fireEvent.press(screen.getByTestId('mission-row-actions-purge'));
    expect(api.purgeMission).not.toHaveBeenCalled();
    await fireEvent.press(screen.getByTestId('missions-confirm-confirm'));
    await waitFor(() => expect(api.purgeMission).toHaveBeenCalledWith('msn_1'));

    // View Log opens the detail on its Log tab (phones push the route).
    await fireEvent.press(menu());
    await fireEvent.press(screen.getByTestId('mission-row-actions-log'));
    expect(mockRouter.push).toHaveBeenCalledWith('/missions/msn_1?tab=log');
  });

  it('bulk-purges the selection and creates missions', async () => {
    api.listMissionSummaries.mockResolvedValue(page([summary('msn_1'), summary('msn_2')]));
    api.purgeMission.mockResolvedValue(undefined);
    api.createMission.mockResolvedValue({} as never);
    await renderScreen(<MissionsHubScreen />);
    await fireEvent(await screen.findByTestId('mission-row-msn_1'), 'onLongPress');
    await fireEvent.press(screen.getByTestId('mission-row-msn_2'));
    await fireEvent.press(screen.getByTestId('missions-delete-selected'));
    await fireEvent.press(screen.getByTestId('missions-confirm-confirm'));
    await waitFor(() => expect(api.purgeMission).toHaveBeenCalledTimes(2));

    await fireEvent.press(screen.getByTestId('missions-create'));
    await fireEvent.changeText(screen.getByTestId('mission-create-title'), 'New work');
    await fireEvent.press(screen.getByTestId('mission-create-vessel'));
    await fireEvent.press(await screen.findByTestId('mission-create-vessel-option-vsl_1'));
    await fireEvent.press(screen.getByTestId('mission-create-submit'));
    await waitFor(() => expect(api.createMission).toHaveBeenCalledWith({ title: 'New work', description: '', vesselId: 'vsl_1', priority: 100, mode: 'Implementation' }));
  });

  it('reloads on mission socket events', async () => {
    api.listMissionSummaries.mockResolvedValue(page([summary('msn_1')]));
    const { socket } = await renderScreen(<MissionsHubScreen />);
    await screen.findByTestId('mission-row-msn_1');
    const calls = api.listMissionSummaries.mock.calls.length;
    api.listMissionSummaries.mockResolvedValue(page([summary('msn_1'), summary('msn_2')]));
    await act(async () => { socket.message({ type: 'mission.changed', data: { id: 'msn_2' } }); });
    expect(await screen.findByTestId('mission-row-msn_2')).toBeTruthy();
    expect(api.listMissionSummaries.mock.calls.length).toBe(calls + 1);
  });

  it('shows the selected mission beside the list on tablets', async () => {
    mockWindow.width = 1024;
    api.listMissionSummaries.mockResolvedValue(page([summary('msn_1')]));
    api.getMission.mockResolvedValue(mission());
    await renderScreen(<MissionsHubScreen />);
    expect(screen.getByTestId('split-view')).toBeTruthy();
    await fireEvent.press(await screen.findByTestId('mission-row-msn_1'));
    expect(await screen.findByTestId('mission-detail-title')).toHaveTextContent('Mission msn_1');
    expect(mockRouter.push).not.toHaveBeenCalled();
  });

  it('shows the mission beside the list on an iPad mini in portrait (744 dp, rail beside the content)', async () => {
    mockWindow.width = 744;
    mockWindow.height = 1133;
    api.listMissionSummaries.mockResolvedValue(page([summary('msn_1')]));
    api.getMission.mockResolvedValue(mission());
    await renderScreen(<MissionsHubScreen />);
    expect(screen.getByTestId('split-view')).toBeTruthy();
    await fireEvent.press(await screen.findByTestId('mission-row-msn_1'));
    expect(await screen.findByTestId('mission-detail-title')).toHaveTextContent('Mission msn_1');
    expect(mockRouter.push).not.toHaveBeenCalled();
    // The list beside the detail is narrow: its row buttons stack so the title keeps its width.
    expect(screen.getByTestId('mission-row-buttons-stacked-msn_1')).toBeTruthy();
  });

  it('a mission deep link on a tablet opens the Missions list with the mission in the detail pane', async () => {
    mockWindow.width = 1194;
    mockWindow.height = 834;
    setMockParams({ id: 'msn_1', tab: 'log' });
    api.listMissionSummaries.mockResolvedValue(page([summary('msn_1'), summary('msn_2')]));
    api.getMission.mockResolvedValue(mission());
    api.getMissionLog.mockResolvedValue({ lines: [], totalLines: 0 } as never);
    await renderScreen(<MissionRoute />);
    expect(screen.getByTestId('split-view')).toBeTruthy();
    expect(await screen.findByTestId('mission-row-msn_2')).toBeTruthy();
    expect(await screen.findByTestId('mission-detail-title')).toHaveTextContent('Mission msn_1');
    expect(screen.getByTestId('mission-row-msn_1')).toHaveProp('accessibilityState', { selected: true });
  });

  it('a mission deep link on a phone opens the mission alone', async () => {
    setMockParams({ id: 'msn_1' });
    api.getMission.mockResolvedValue(mission());
    await renderScreen(<MissionRoute />);
    expect(await screen.findByTestId('mission-detail-title')).toHaveTextContent('Mission msn_1');
    expect(screen.queryByTestId('split-view')).toBeNull();
    expect(api.listMissionSummaries).not.toHaveBeenCalled();
  });

  it('switches hub tabs through the route query', async () => {
    api.listMissionSummaries.mockResolvedValue(page([]));
    await renderScreen(<MissionsHubScreen />);
    await fireEvent.press(screen.getByTestId('missions-hub-tab-voyages'));
    expect(mockRouter.setParams).toHaveBeenCalledWith({ tab: 'voyages' });
  });
});

describe('Mission detail', () => {
  it('shows the mission, lands it, and follows live updates', async () => {
    api.getMission.mockResolvedValue(mission({ status: 'WorkProduced' }));
    api.getMissionLandingPreview.mockResolvedValue({ isReadyToLand: true, issues: [], targetBranch: 'main', branchCategory: 'Feature' } as never);
    api.retryMissionLanding.mockResolvedValue({} as never);
    const { socket } = await renderScreen(<MissionDetail id="msn_1" />);
    expect(await screen.findByTestId('mission-detail-title')).toHaveTextContent('Mission msn_1');
    expect(await screen.findByText('Ready To Land')).toBeTruthy();
    await fireEvent.press(screen.getByTestId('mission-action-land'));
    await waitFor(() => expect(api.retryMissionLanding).toHaveBeenCalledWith('msn_1'));

    api.getMission.mockResolvedValue(mission({ status: 'Complete' }));
    await act(async () => { socket.message({ type: 'mission.changed', data: { id: 'msn_1', status: 'Complete' } }); });
    await waitFor(() => expect(screen.getByText('Landed')).toBeTruthy());
    expect(screen.queryByTestId('mission-action-land')).toBeNull();
  });

  it('keeps the overview in a readable column when it has the whole window', async () => {
    mockWindow.width = 1376;
    mockWindow.height = 1032;
    api.getMission.mockResolvedValue(mission());
    await renderScreen(<MissionDetail id="msn_1" />);
    expect(await screen.findByTestId('mission-detail-title')).toHaveTextContent('Mission msn_1');
    expect(StyleSheet.flatten(screen.getByTestId('mission-overview').props.contentContainerStyle)).toMatchObject({ maxWidth: 820, alignSelf: 'center' });
  });

  it('resolves a review gate with each verdict', async () => {
    api.getMission.mockResolvedValue(mission({ status: 'Review', requiresReview: true }));
    api.approveMissionReview.mockResolvedValue({} as never);
    api.denyMissionReview.mockResolvedValue({} as never);
    await renderScreen(<MissionDetail id="msn_1" />);
    await fireEvent.press(await screen.findByTestId('mission-action-review'));
    expect(screen.getByTestId('mission-review-conditional')).toBeDisabled();
    await fireEvent.changeText(screen.getByTestId('mission-review-comment'), 'Tighten the tests');
    await fireEvent.press(screen.getByTestId('mission-review-morework'));
    await waitFor(() => expect(api.denyMissionReview).toHaveBeenCalledWith('msn_1', { comment: 'Tighten the tests', action: 'RetryStage' }));

    await fireEvent.press(screen.getByTestId('mission-action-review'));
    await fireEvent.press(screen.getByTestId('mission-review-approve'));
    await waitFor(() => expect(api.approveMissionReview).toHaveBeenCalledWith('msn_1', { comment: undefined }));
  });

  it('opens the diff, log, and instructions tabs', async () => {
    api.getMission.mockResolvedValue(mission({ status: 'Complete' }));
    api.getMissionDiff.mockResolvedValue({ diff: 'diff --git a/x.txt b/x.txt\n--- a/x.txt\n+++ b/x.txt\n@@ -1 +1 @@\n-old\n+new\n' });
    api.getMissionLog.mockResolvedValue({ log: 'line one', lines: 1, totalLines: 1 });
    api.getMissionInstructions.mockResolvedValue({ fileName: 'CLAUDE.md', content: 'Instructions here' });
    await renderScreen(<MissionDetail id="msn_1" />);
    await fireEvent.press(await screen.findByTestId('mission-tab-diff'));
    expect(await screen.findByTestId('mission-diff-summary')).toHaveTextContent('1 file(s), +1 -1');
    await fireEvent.press(screen.getByTestId('mission-tab-log'));
    expect(await screen.findByTestId('mission-log-text')).toHaveTextContent('line one');
    await fireEvent.press(screen.getByTestId('mission-log-lines-500'));
    await waitFor(() => expect(api.getMissionLog).toHaveBeenLastCalledWith('msn_1', 500));
    await fireEvent.press(screen.getByTestId('mission-tab-instructions'));
    expect(await screen.findByTestId('mission-instructions')).toHaveTextContent('Instructions here');
  });

  it('runs actions from the menu: transition, edit, restart, delete', async () => {
    api.getMission.mockResolvedValue(mission({ status: 'Failed', failureReason: 'Tests failed' }));
    api.transitionMission.mockResolvedValue({} as never);
    api.updateMission.mockResolvedValue({} as never);
    api.restartMission.mockResolvedValue({} as never);
    api.deleteMission.mockResolvedValue(undefined);
    await renderScreen(<MissionDetail id="msn_1" />);
    expect(await screen.findByTestId('mission-failure-reason')).toHaveTextContent('Tests failed');

    await fireEvent.press(screen.getByTestId('mission-action-menu'));
    await fireEvent.press(screen.getByTestId('mission-actions-transition'));
    await fireEvent.press(screen.getByTestId('mission-transition-status'));
    await fireEvent.press(screen.getByTestId('mission-transition-status-option-Pending'));
    await fireEvent.press(screen.getByTestId('mission-transition-submit'));
    await waitFor(() => expect(api.transitionMission).toHaveBeenCalledWith('msn_1', { status: 'Pending' }));

    await fireEvent.press(screen.getByTestId('mission-action-menu'));
    await fireEvent.press(screen.getByTestId('mission-actions-edit'));
    await fireEvent.changeText(screen.getByTestId('mission-edit-title'), 'Renamed');
    await fireEvent.changeText(screen.getByTestId('mission-edit-priority'), '5');
    await fireEvent.press(screen.getByTestId('mission-edit-submit'));
    await waitFor(() => expect(api.updateMission).toHaveBeenCalledWith('msn_1', { title: 'Renamed', description: 'Do the thing', priority: 5 }));

    await fireEvent.press(screen.getByTestId('mission-action-menu'));
    await fireEvent.press(screen.getByTestId('mission-actions-restart'));
    await fireEvent.press(screen.getByTestId('mission-confirm-confirm'));
    await waitFor(() => expect(api.restartMission).toHaveBeenCalledWith('msn_1'));

    await fireEvent.press(screen.getByTestId('mission-action-menu'));
    await fireEvent.press(screen.getByTestId('mission-actions-delete'));
    await fireEvent.press(screen.getByTestId('mission-confirm-confirm'));
    await waitFor(() => expect(api.deleteMission).toHaveBeenCalledWith('msn_1'));
    expect(mockRouter.back).toHaveBeenCalled();
  });

  it('offers the manual merge when the landing mode is None', async () => {
    api.getMission.mockResolvedValue(mission({ status: 'WorkProduced' }));
    api.getMissionLandingPreview.mockResolvedValue({ isReadyToLand: false, manualLandingOnly: true, issues: [], targetBranch: 'main', branchCategory: 'Feature' } as never);
    await renderScreen(<MissionDetail id="msn_1" />);
    await fireEvent.press(await screen.findByTestId('mission-action-merge'));
    expect(mockRouter.push).toHaveBeenCalledWith('/vessels/vsl_1');
    expect(screen.queryByTestId('mission-action-land')).toBeNull();
  });
});
