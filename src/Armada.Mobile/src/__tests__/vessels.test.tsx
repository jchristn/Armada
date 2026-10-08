import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react-native';
import * as client from '@dashboard/api/client';
import type {
  Fleet,
  LandingPreviewResult,
  MissionSummary,
  Pipeline,
  Vessel,
  VesselCommit,
  VesselCommitActivity,
  VesselReadinessResult,
} from '@dashboard/types/models';
import { ToastHost } from '../components/app/ToastHost';
import { VesselHistoryScreen } from '../screens/vessels/history/VesselHistoryScreen';
import { VesselDetailView } from '../screens/vessels/VesselDetailView';
import { VesselOnboardingScreen } from '../screens/vessels/VesselOnboardingScreen';
import { VesselsTab } from '../screens/vessels/VesselsTab';
import { syncParts, vesselLinks } from '../screens/vessels/vesselLinks';
import { BuildProviders, deliver, buildSockets, page } from '../test/buildFixtures';
import { rowActionTarget } from '../test/a11y';

jest.mock('@dashboard/api/client', () => require('../test/buildClientMock').buildClientMockFactory());

// The swipe container has no gesture runtime under Jest; render rows directly (SwipeRow keeps its actions as
// accessibility actions, which the tests use).
jest.mock('react-native-gesture-handler/ReanimatedSwipeable', () => {
  const { forwardRef } = jest.requireActual('react');
  return { __esModule: true, default: forwardRef(({ children }: { children: unknown }, _ref: unknown) => children) };
});

const mockRouter = { push: jest.fn(), replace: jest.fn(), back: jest.fn(), setParams: jest.fn(), canGoBack: jest.fn(() => true) };
jest.mock('expo-router', () => ({
  ...jest.requireActual('expo-router'),
  useRouter: () => mockRouter,
  useLocalSearchParams: () => ({}),
  // Rendered outside a navigator: focus effects (hardware back in selection mode) do not run.
  useFocusEffect: () => undefined,
  Stack: { Screen: () => null },
}));

let mockTablet = false;
jest.mock('../navigation/useLayout', () => {
  const actual = jest.requireActual('../navigation/useLayout');
  return { ...actual, useLayout: () => actual.layoutFor(mockTablet ? 1024 : 390, 800) };
});

const api = client as jest.Mocked<typeof client>;
const NOW = '2026-10-07T12:00:00Z';

function vessel(over: Partial<Vessel> = {}): Vessel {
  return {
    id: 'vsl_1', tenantId: 'ten_1', fleetId: 'flt_1', name: 'api', repoUrl: 'https://git/api.git', localPath: null, workingDirectory: '/work/api',
    defaultBranch: 'main', projectContext: 'Project notes', styleGuide: null, enableModelContext: true, modelContext: null,
    hasGitHubTokenOverride: false, landingMode: 'LocalMerge', branchCleanupPolicy: null, requirePassingChecksToLand: false,
    protectedBranchPatterns: [], releaseBranchPrefix: 'release/', hotfixBranchPrefix: 'hotfix/', requirePullRequestForProtectedBranches: false,
    requireMergeQueueForReleaseBranches: false, allowConcurrentMissions: false, defaultPipelineId: null, active: true, createdUtc: NOW, lastUpdateUtc: NOW,
    ...over,
  };
}

const FLEET = { id: 'flt_1', name: 'core', tenantId: 'ten_1', description: null, defaultPipelineId: null, active: true, createdUtc: NOW, lastUpdateUtc: NOW } as Fleet;
const API = vessel();
const WEB = vessel({ id: 'vsl_2', name: 'web', repoUrl: 'https://git/web.git', landingMode: 'PullRequest', fleetId: null });

function readiness(over: Partial<VesselReadinessResult> = {}): VesselReadinessResult {
  return {
    vesselId: 'vsl_1', hasWorkingDirectory: true, hasRepositoryContext: true, workflowProfileId: null, workflowProfileName: 'Default',
    workflowProfileScope: null, requestedCheckType: null, requestedEnvironmentName: null, availableCheckTypes: ['Build'], currentBranch: 'main',
    hasUncommittedChanges: false, isDetachedHead: false, commitsAhead: 1, commitsBehind: 0, detectedToolchains: ['dotnet'], toolchainProbes: [],
    deploymentEnvironments: [], deploymentMetadata: null,
    setupChecklist: [
      { code: 'working_directory', severity: 'Error', title: 'Working directory', message: 'Set', isSatisfied: true, actionLabel: null, actionRoute: null },
      { code: 'workflow_profile', severity: 'Warning', title: 'Workflow profile', message: 'Pick one', isSatisfied: false, actionLabel: 'Create profile', actionRoute: '/workflow-profiles/new' },
    ],
    issues: [{ code: 'x', severity: 'Warning', title: 'No profile', message: 'Add a workflow profile', relatedValue: 'TOKEN', inputProvider: 'OnePassword' }],
    setupChecklistSatisfiedCount: 1, setupChecklistTotalCount: 2, errorCount: 0, warningCount: 1, isReady: false, ...over,
  };
}

const PREVIEW: LandingPreviewResult = {
  vesselId: 'vsl_1', missionId: null, sourceBranch: null, targetBranch: 'main', branchCategory: 'Default', targetBranchProtected: false,
  protectedBranchMatch: null, landingMode: 'LocalMerge', branchCleanupPolicy: null, requirePassingChecksToLand: false,
  requirePullRequestForProtectedBranches: false, requireMergeQueueForReleaseBranches: false, expectedLandingAction: 'Merge locally',
  hasPassingChecks: false, latestCheckRunId: null, latestCheckStatus: null, latestCheckSummary: null, isReadyToLand: true, issues: [],
};

function mission(over: Partial<MissionSummary> = {}): MissionSummary {
  return { id: 'msn_1', title: 'Fix login', status: 'InProgress', captainId: 'cpt_1', branchName: 'armada/fix', vesselId: 'vsl_1', ...over } as MissionSummary;
}

beforeEach(() => {
  jest.clearAllMocks();
  mockTablet = false;
  api.listVessels.mockResolvedValue(page([WEB, API]));
  api.listFleets.mockResolvedValue(page([FLEET]));
  api.listPipelines.mockResolvedValue(page([{ id: 'ppl_1', name: 'Reviewed', stages: [{ personaName: 'Worker' }] } as Pipeline]));
  api.getVesselGitStatus.mockResolvedValue({ vesselId: 'vsl_1', commitsAhead: 2, commitsBehind: 0 });
  api.getVesselBranches.mockResolvedValue({ vesselId: 'vsl_1', defaultBranch: 'main', branchCount: 3, branches: [
    { name: 'main', isCurrent: true, isDefault: true, commitHash: 'abc1234', commitSubject: 'init', commitDate: NOW, ahead: 0, behind: 0 },
    { name: 'feature', isCurrent: false, isDefault: false, commitHash: 'def5678', commitSubject: 'work', commitDate: NOW, ahead: 2, behind: 1 },
  ] });
  api.getVessel.mockResolvedValue(API);
  api.listMissionSummaries.mockResolvedValue(page([mission()]));
  api.getVesselReadiness.mockResolvedValue(readiness());
  api.getVesselLandingPreview.mockResolvedValue(PREVIEW);
  api.listUsers.mockResolvedValue(page([]));
});

async function settle() {
  await act(async () => { await Promise.resolve(); });
}

describe('vessel links', () => {
  it('builds the app paths the vessel screens open', () => {
    expect(vesselLinks.runAction(['vsl_1', 'vsl 2'])).toBe('/fleet-actions?run=new&vessels=vsl_1,vsl%202');
    expect(vesselLinks.objectives({ id: 'vsl_1', fleetId: 'flt_1' })).toBe('/dispatch?tab=backlog&vesselId=vsl_1&fleetId=flt_1');
    expect(vesselLinks.runCheck({ id: 'vsl_1', defaultBranch: 'main' })).toBe('/delivery?tab=checks&vesselId=vsl_1&branchName=main');
    expect(vesselLinks.edit('vsl_1')).toBe('/vessels/vsl_1?edit=1');
    expect(syncParts(undefined).known).toBe(false);
    expect(syncParts({ ahead: null, behind: 3 })).toEqual({ known: true, ahead: 0, behind: 3 });
  });
});

describe('Vessels tab', () => {
  it('lists vessels sorted by name with sync and branch counts, and searches', async () => {
    await render(<BuildProviders><VesselsTab /></BuildProviders>);
    await waitFor(() => expect(screen.getByTestId('vessel-row-api')).toBeTruthy());
    const rows = screen.getAllByTestId(/^vessel-row-/).map((r) => r.props.testID);
    expect(rows).toEqual(['vessel-row-api', 'vessel-row-web']);
    await waitFor(() => expect(screen.getAllByText('2 ahead').length).toBe(2));
    expect(screen.getAllByText(/3 branches/).length).toBe(2);
    await fireEvent.changeText(screen.getByTestId('vessels-list-search'), 'web');
    await waitFor(() => expect(screen.queryByTestId('vessel-row-api')).toBeNull());
    expect(screen.getByTestId('vessel-row-web')).toBeTruthy();
  });

  it('filters by landing mode in the filter sheet', async () => {
    await render(<BuildProviders><VesselsTab /></BuildProviders>);
    await waitFor(() => expect(screen.getByTestId('vessel-row-api')).toBeTruthy());
    await fireEvent.press(screen.getByTestId('vessels-list-filters'));
    await fireEvent.press(screen.getByTestId('vessels-filter-landing-mode'));
    await fireEvent.press(screen.getByTestId('vessels-filter-landing-mode-option-PullRequest'));
    await waitFor(() => expect(screen.queryByTestId('vessel-row-api')).toBeNull());
    expect(screen.getByTestId('vessel-row-web')).toBeTruthy();
  });

  it('creates a vessel with a landing mode, and with the global default when unset', async () => {
    api.createVessel.mockResolvedValue(vessel({ id: 'vsl_9', name: 'new' }));
    await render(<BuildProviders><VesselsTab /></BuildProviders>);
    await waitFor(() => expect(screen.getByTestId('vessel-row-api')).toBeTruthy());
    await fireEvent.press(screen.getByTestId('vessels-list-new'));
    // Save stays in the sheet's footer (disabled until name and repository are set), reachable without scrolling.
    expect(within(screen.getByTestId('vessel-form-footer')).getByTestId('vessel-form-save')).toBeDisabled();
    await fireEvent.changeText(screen.getByTestId('vessel-form-name'), 'new');
    await fireEvent.changeText(screen.getByTestId('vessel-form-repo-url'), 'https://git/new.git');
    await fireEvent.press(screen.getByTestId('vessel-form-landing-mode'));
    await fireEvent.press(screen.getByTestId('vessel-form-landing-mode-option-PullRequest'));
    expect(screen.getByText(/opens a pull request on the remote/)).toBeTruthy();
    await fireEvent.press(screen.getByTestId('vessel-form-save'));
    await waitFor(() => expect(api.createVessel).toHaveBeenCalledTimes(1));
    expect(api.createVessel.mock.calls[0][0]).toMatchObject({ name: 'new', repoUrl: 'https://git/new.git', landingMode: 'PullRequest', branchCleanupPolicy: 'LocalAndRemote' });

    await fireEvent.press(screen.getByTestId('vessels-list-new'));
    await fireEvent.changeText(screen.getByTestId('vessel-form-name'), 'other');
    await fireEvent.changeText(screen.getByTestId('vessel-form-repo-url'), 'https://git/other.git');
    await fireEvent.press(screen.getByTestId('vessel-form-landing-mode'));
    await fireEvent.press(screen.getByTestId('vessel-form-landing-mode-option-none'));
    await fireEvent.press(screen.getByTestId('vessel-form-save'));
    await waitFor(() => expect(api.createVessel).toHaveBeenCalledTimes(2));
    expect(api.createVessel.mock.calls[1][0]).toMatchObject({ name: 'other', landingMode: null });
  });

  it('shows the server message when the create is rejected as a duplicate', async () => {
    api.createVessel.mockRejectedValue(new client.ApiError('A vessel named api already exists in this fleet', 409,
      { code: 'DuplicateEntity', entityType: 'Vessel', field: 'Name', value: 'api' }));
    // ToastHost renders the error toast the form's onError pushes.
    await render(<BuildProviders><VesselsTab /><ToastHost /></BuildProviders>);
    await waitFor(() => expect(screen.getByTestId('vessel-row-api')).toBeTruthy());
    await fireEvent.press(screen.getByTestId('vessels-list-new'));
    await fireEvent.changeText(screen.getByTestId('vessel-form-name'), 'api');
    await fireEvent.changeText(screen.getByTestId('vessel-form-repo-url'), 'https://git/api.git');
    await fireEvent.press(screen.getByTestId('vessel-form-save'));
    await waitFor(() => expect(api.createVessel).toHaveBeenCalledTimes(1));
    expect(await screen.findByText('A vessel named api already exists in this fleet')).toBeTruthy();
    expect(screen.queryByText('Save failed.')).toBeNull();
  });

  it('deletes from the swipe action after confirmation', async () => {
    api.deleteVessel.mockResolvedValue(undefined);
    await render(<BuildProviders><VesselsTab /></BuildProviders>);
    await waitFor(() => expect(screen.getByTestId('vessel-swipe-web')).toBeTruthy());
    await fireEvent(rowActionTarget(screen.getByTestId('vessel-swipe-web'), 'delete'), 'accessibilityAction', { nativeEvent: { actionName: 'delete' } });
    expect(screen.getByText('Delete vessel "web"? This cannot be undone.')).toBeTruthy();
    expect(api.deleteVessel).not.toHaveBeenCalled();
    await fireEvent.press(screen.getByTestId('vessel-delete-confirm-confirm'));
    await waitFor(() => expect(api.deleteVessel).toHaveBeenCalledWith('vsl_2'));
  });

  it('opens the action menu and runs navigation and duplicate actions', async () => {
    api.createVessel.mockResolvedValue(vessel({ id: 'vsl_7', name: 'api (copy)' }));
    await render(<BuildProviders><VesselsTab /></BuildProviders>);
    await waitFor(() => expect(screen.getByTestId('vessel-swipe-api')).toBeTruthy());
    await fireEvent(rowActionTarget(screen.getByTestId('vessel-swipe-api'), 'more'), 'accessibilityAction', { nativeEvent: { actionName: 'more' } });
    await fireEvent.press(screen.getByTestId('vessel-action-history'));
    expect(mockRouter.push).toHaveBeenLastCalledWith('/vessels/vsl_1/history');
    await fireEvent(rowActionTarget(screen.getByTestId('vessel-swipe-api'), 'more'), 'accessibilityAction', { nativeEvent: { actionName: 'more' } });
    await fireEvent.press(screen.getByTestId('vessel-action-duplicate'));
    await waitFor(() => expect(mockRouter.push).toHaveBeenLastCalledWith('/vessels/vsl_7?edit=1'));
    expect(api.createVessel.mock.calls[0][0]).toMatchObject({ fleetId: 'flt_1', landingMode: 'LocalMerge' });
  });

  it('bulk-deletes the long-pressed selection after confirmation', async () => {
    api.deleteVessel.mockResolvedValue(undefined);
    await render(<BuildProviders><VesselsTab /></BuildProviders>);
    await waitFor(() => expect(screen.getByTestId('vessel-row-api')).toBeTruthy());
    await fireEvent(screen.getByTestId('vessel-row-api'), 'longPress');
    await fireEvent.press(screen.getByTestId('vessel-row-web'));
    await fireEvent.press(screen.getByTestId('vessels-bulk-delete'));
    await fireEvent.press(screen.getByTestId('vessels-bulk-confirm-confirm'));
    await waitFor(() => expect(api.deleteVessel).toHaveBeenCalledTimes(2));
    expect(api.deleteVessel.mock.calls.map((c) => c[0]).sort()).toEqual(['vsl_1', 'vsl_2']);
  });

  it('shows the vessel beside the list on tablets', async () => {
    mockTablet = true;
    await render(<BuildProviders><VesselsTab /></BuildProviders>);
    await waitFor(() => expect(screen.getByText('Select a vessel')).toBeTruthy());
    await fireEvent.press(screen.getByTestId('vessel-row-api'));
    await waitFor(() => expect(screen.getByTestId('vessel-detail')).toBeTruthy());
    expect(mockRouter.push).not.toHaveBeenCalled();
    expect(screen.getByTestId('split-view')).toBeTruthy();
  });
});

describe('Vessel detail', () => {
  it('shows fields, readiness, landing preview, and missions; edits the landing mode', async () => {
    api.updateVessel.mockResolvedValue(vessel({ landingMode: 'MergeQueue' }));
    await render(<BuildProviders><VesselDetailView id="vsl_1" /></BuildProviders>);
    await waitFor(() => expect(screen.getByTestId('vessel-detail')).toBeTruthy());
    expect(within(screen.getByTestId('vessel-detail-landing-mode')).getByText('LocalMerge')).toBeTruthy();
    await waitFor(() => expect(screen.getByText('Needs Attention')).toBeTruthy());
    expect(screen.getByText('TOKEN (1Password)')).toBeTruthy();
    expect(screen.getByText('Ready To Land')).toBeTruthy();
    expect(screen.getByTestId('vessel-mission-msn_1')).toBeTruthy();

    await fireEvent.press(screen.getByTestId('vessel-detail-edit'));
    expect(screen.getByTestId('vessel-form-name').props.value).toBe('api');
    await fireEvent.press(screen.getByTestId('vessel-form-landing-mode'));
    await fireEvent.press(screen.getByTestId('vessel-form-landing-mode-option-MergeQueue'));
    await fireEvent.press(screen.getByTestId('vessel-form-save'));
    await waitFor(() => expect(api.updateVessel).toHaveBeenCalledTimes(1));
    expect(api.updateVessel.mock.calls[0][0]).toBe('vsl_1');
    expect(api.updateVessel.mock.calls[0][1]).toMatchObject({ id: 'vsl_1', landingMode: 'MergeQueue', workingDirectory: '/work/api' });
  });

  it('opens the edit form for ?edit=1 once', async () => {
    const opened = jest.fn();
    await render(<BuildProviders><VesselDetailView id="vsl_1" openEdit onEditOpened={opened} /></BuildProviders>);
    await waitFor(() => expect(screen.getByTestId('vessel-form-name')).toBeTruthy());
    expect(opened).toHaveBeenCalledTimes(1);
  });

  it('reloads missions on mission events', async () => {
    await render(<BuildProviders><VesselDetailView id="vsl_1" /></BuildProviders>);
    await waitFor(() => expect(screen.getByTestId('vessel-mission-msn_1')).toBeTruthy());
    api.listMissionSummaries.mockResolvedValue(page([mission(), mission({ id: 'msn_2', title: 'Add tests' })]));
    await act(async () => { buildSockets()[0]?.open(); });
    await act(async () => deliver({ type: 'mission.changed' }));
    await waitFor(() => expect(screen.getByTestId('vessel-mission-msn_2')).toBeTruthy());
  });

  it('pushes and merges branches from the branches sheet', async () => {
    api.pushVesselBranch.mockResolvedValue({ vesselId: 'vsl_1', branch: 'feature', pushed: true });
    api.mergeVesselBranch.mockResolvedValue({ vesselId: 'vsl_1', source: 'feature', target: 'main', merged: true, pushed: false });
    await render(<BuildProviders><VesselDetailView id="vsl_1" /></BuildProviders>);
    await waitFor(() => expect(screen.getByTestId('vessel-detail-branches')).toBeTruthy());
    await fireEvent.press(screen.getByTestId('vessel-detail-branches'));
    await waitFor(() => expect(screen.getByTestId('vessel-branch-feature')).toBeTruthy());
    await fireEvent.press(screen.getByTestId('vessel-branch-push-feature'));
    await waitFor(() => expect(api.pushVesselBranch).toHaveBeenCalledWith('vsl_1', 'feature'));
    await fireEvent.press(screen.getByTestId('vessel-merge-source'));
    await fireEvent.press(screen.getByTestId('vessel-merge-source-option-feature'));
    await fireEvent.press(screen.getByTestId('vessel-merge-target'));
    await fireEvent.press(screen.getByTestId('vessel-merge-target-option-main'));
    await fireEvent(screen.getByTestId('vessel-merge-push'), 'valueChange', false);
    await fireEvent.press(screen.getByTestId('vessel-merge'));
    await waitFor(() => expect(api.mergeVesselBranch).toHaveBeenCalledWith('vsl_1', 'feature', 'main', false));
  });
});

describe('Vessel onboarding', () => {
  it('shows progress, the next step, and the grouped checklist', async () => {
    await render(<BuildProviders><VesselOnboardingScreen id="vsl_1" /></BuildProviders>);
    await waitFor(() => expect(screen.getByTestId('vessel-onboarding')).toBeTruthy());
    expect(screen.getByText('Repository Basics')).toBeTruthy();
    expect(screen.getByText('Workflow Profile')).toBeTruthy();
    expect(within(screen.getByTestId('vessel-onboarding-next')).getByText('Workflow profile')).toBeTruthy();
    await fireEvent.press(within(screen.getByTestId('vessel-onboarding-next')).getByText('Create profile'));
    expect(mockRouter.push).toHaveBeenCalledWith('/workflow-profiles/new');
  });
});

function commit(sha: string, committedUtc: string, over: Partial<VesselCommit> = {}): VesselCommit {
  return {
    sha, shortSha: sha.slice(0, 7), subject: `Commit ${sha}`, body: '', authorName: 'Ada', authorEmail: 'ada@x', authoredUtc: committedUtc,
    committerName: 'Ada', committerEmail: 'ada@x', committedUtc, parentShas: [], isMerge: false, filesChanged: 1, addedLines: 3, deletedLines: 1,
    files: [{ path: 'src/a.ts', oldPath: null, kind: 'Modified', addedLines: 3, deletedLines: 1, isBinary: false }], filesTruncated: false, ...over,
  };
}

describe('View History', () => {
  const activity: VesselCommitActivity = {
    vesselId: 'vsl_1', branch: 'main', from: '2026-10-04', to: '2026-10-07', utcOffsetMinutes: 0,
    days: [{ date: '2026-10-04', count: 0 }, { date: '2026-10-05', count: 2 }, { date: '2026-10-06', count: 0 }, { date: '2026-10-07', count: 1 }],
    totalCommits: 3, maxDayCount: 2, firstCommitUtc: '2025-03-01T00:00:00Z', lastCommitUtc: NOW, error: null,
  };

  it('loads the heatmap and timeline, scrolls for more, and jumps to a day', async () => {
    api.getVesselCommitActivity.mockResolvedValue(activity);
    api.getVesselCommits.mockImplementation(async (_id, params) => {
      if (params?.cursor) return { vesselId: 'vsl_1', branch: 'main', commits: [commit('ccc3333', '2026-10-01T10:00:00Z')], nextCursor: null, error: null };
      if (params?.before) return { vesselId: 'vsl_1', branch: 'main', commits: [commit('bbb2222', '2026-10-05T10:00:00Z')], nextCursor: null, error: null };
      return { vesselId: 'vsl_1', branch: 'main', commits: [commit('aaa1111', '2026-10-07T10:00:00Z')], nextCursor: 'cur1', error: null };
    });
    await render(<BuildProviders><VesselHistoryScreen id="vsl_1" /></BuildProviders>);
    await waitFor(() => expect(screen.getByTestId('vessel-heatmap')).toBeTruthy());
    expect(screen.getByTestId('vessel-heatmap-total')).toHaveTextContent('3 commits in the last year');
    await waitFor(() => expect(screen.getByText('Commit aaa1111')).toBeTruthy());
    expect(api.getVesselCommits).toHaveBeenCalledWith('vsl_1', { branch: 'main', before: null, limit: 50 });

    await fireEvent.press(screen.getByTestId('vessel-history-load-more'));
    await waitFor(() => expect(screen.getByText('Commit ccc3333')).toBeTruthy());
    expect(api.getVesselCommits).toHaveBeenLastCalledWith('vsl_1', { cursor: 'cur1', limit: 50 });
    expect(screen.getByTestId('vessel-history-end')).toBeTruthy();

    await fireEvent.press(screen.getByTestId('heat-2026-10-05'));
    await waitFor(() => expect(screen.getByText('Commit bbb2222')).toBeTruthy());
    const call = api.getVesselCommits.mock.calls[api.getVesselCommits.mock.calls.length - 1];
    expect(call[1]?.before).toBeTruthy();
    expect(screen.getByTestId('vessel-history-jump-banner')).toBeTruthy();

    await fireEvent.press(screen.getByTestId('vessel-history-commit-bbb2222'));
    expect(screen.getByText('src/a.ts')).toBeTruthy();

    await fireEvent.press(screen.getByTestId('vessel-history-latest'));
    await waitFor(() => expect(screen.getByText('Commit aaa1111')).toBeTruthy());
    expect(screen.queryByTestId('vessel-history-jump-banner')).toBeNull();
  });

  it('jumps to a typed date and changes the heatmap year for an older date', async () => {
    api.getVesselCommitActivity.mockResolvedValue(activity);
    api.getVesselCommits.mockResolvedValue({ vesselId: 'vsl_1', branch: 'main', commits: [], nextCursor: null, error: null });
    await render(<BuildProviders><VesselHistoryScreen id="vsl_1" /></BuildProviders>);
    await waitFor(() => expect(screen.getByTestId('vessel-history-jump')).toBeTruthy());
    await fireEvent.changeText(screen.getByTestId('vessel-history-jump'), '2025-03-02');
    await fireEvent.press(screen.getByTestId('vessel-history-go'));
    await waitFor(() => expect(api.getVesselCommitActivity).toHaveBeenLastCalledWith('vsl_1', expect.objectContaining({ from: '2025-01-01', to: '2025-12-31' })));
    await waitFor(() => expect(screen.getByText('No commits on or before this date')).toBeTruthy());
  });

  it('explains a repository that cannot be read', async () => {
    api.getVesselCommitActivity.mockResolvedValue({ ...activity, error: 'not cloned' });
    api.getVesselCommits.mockResolvedValue({ vesselId: 'vsl_1', branch: 'main', commits: [], nextCursor: null, error: 'not cloned' });
    await render(<BuildProviders><VesselHistoryScreen id="vsl_1" /></BuildProviders>);
    await waitFor(() => expect(screen.getByText('History is not available')).toBeTruthy());
    await settle();
  });
});
