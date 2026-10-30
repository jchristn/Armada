import { act, fireEvent, screen, waitFor, within } from '@testing-library/react-native';
import * as Clipboard from 'expo-clipboard';
import type { ReactElement } from 'react';
import { Share, Text } from 'react-native';
import * as client from '@dashboard/api/client';
import type { Mission, Voyage } from '@dashboard/types/models';
import BlackBoxEntryRoute from '../app/(app)/(work)/blackbox/[id]';
import { useNotifications } from '../notifications/NotificationContext';
import { BlackBoxPanel, type BlackBoxSource } from '../screens/blackbox/BlackBoxPanel';
import {
  askPrefillRoute,
  blackBoxAskPrompt,
  bounded,
  isBlackBoxNotFound,
  missionMayHaveBlackBox,
  pathIsRunnable,
  pathLosesData,
  rankedRecoveryPaths,
  voyageMayHaveBlackBox,
} from '../screens/blackbox/blackBoxModel';
import { MissionDetail } from '../screens/operations/MissionDetail';
import { MissionsHubScreen } from '../screens/operations/MissionsHubScreen';
import { VoyageDetail } from '../screens/operations/VoyageDetail';
import { AGENT_BRIEF, CANCEL_PATH, MANUAL_PATH, RETRY_PATH, USER_BRIEF, blackBoxEntry, blackBoxSummary } from '../test/blackBoxFixtures';
import { page } from '../test/operationsClient';
import { mockRouter, setMockParams } from '../test/routerMock';
import { renderScreen } from '../test/screen';

jest.mock('@dashboard/api/client', () => ({
  ...require('../test/operationsClient').operationsClientMockFactory(),
  listCheckRuns: jest.fn(async () => ({ objects: [], totalRecords: 0 })),
  listDeployments: jest.fn(async () => ({ objects: [], totalRecords: 0 })),
}));
jest.mock('expo-router', () => require('../test/routerMock').routerMockFactory());

const api = client as jest.Mocked<typeof client>;
const clipboard = Clipboard as jest.Mocked<typeof Clipboard>;
const { ApiError } = jest.requireActual('@dashboard/api/client') as typeof client;

const CANONICAL = 'Show me the pertinent details from the black box for mission msn_1 and give me your opinion on why it failed and what could have been done differently.';

/** The toasts on screen, as text. */
function ToastProbe() {
  const { toasts } = useNotifications();
  return <>{toasts.map((toast) => <Text key={toast.id} testID={`toast-${toast.severity}`}>{toast.message}</Text>)}</>;
}

async function renderWithToasts(ui: ReactElement) {
  return renderScreen(<>{ui}<ToastProbe /></>);
}

async function renderPanel(source: BlackBoxSource = { kind: 'mission', id: 'msn_1' }, fallback: ReactElement | null = null) {
  const result = await renderWithToasts(<BlackBoxPanel source={source} fallback={fallback} />);
  await act(async () => { await Promise.resolve(); });
  return result;
}

async function press(testID: string) {
  await act(async () => { await fireEvent.press(screen.getByTestId(testID)); });
}

function notFound() {
  return new ApiError('No black box entry for mission msn_1.', 404, { code: 'BlackBoxNotFound' });
}

beforeEach(() => {
  jest.clearAllMocks();
  setMockParams({});
  api.listVessels.mockResolvedValue(page([{ id: 'vsl_1', name: 'api' }, { id: 'vsl_2', name: 'web' }] as never));
  api.listCaptains.mockResolvedValue(page([]));
  api.getMissionBlackBox.mockResolvedValue(blackBoxEntry());
  api.getVoyageBlackBox.mockResolvedValue(blackBoxEntry({ id: 'bbx_v', subjectType: 'Voyage', missionId: null, voyageId: 'vyg_1', phase: 'Voyage', step: 'StageFailed', failureKind: null }));
  api.getBlackBox.mockResolvedValue(blackBoxEntry());
  api.getBlackBoxBrief.mockImplementation(async (_id, opts) => (opts?.audience === 'user' ? USER_BRIEF : AGENT_BRIEF));
});

describe('black box model', () => {
  it('builds the canonical Ask prompt for missions and voyages and the prefilled Ask route', () => {
    expect(blackBoxAskPrompt(blackBoxEntry())).toBe(CANONICAL);
    expect(blackBoxAskPrompt(blackBoxEntry({ subjectType: 'Voyage', missionId: null, voyageId: 'vyg_9' }))).toContain('for voyage vyg_9 and give me');
    expect(askPrefillRoute('a b&c')).toBe('/ask?prompt=a%20b%26c');
  });

  it('decides which subjects can have an entry', () => {
    expect(missionMayHaveBlackBox('LandingFailed')).toBe(true);
    expect(missionMayHaveBlackBox('Cancelled')).toBe(true);
    expect(missionMayHaveBlackBox('InProgress')).toBe(false);
    expect(missionMayHaveBlackBox('Pending', 'restarted after a crash')).toBe(true);
    expect(voyageMayHaveBlackBox('Failed')).toBe(true);
    expect(voyageMayHaveBlackBox('Complete')).toBe(false);
  });

  it('tells a missing entry from other failures', () => {
    expect(isBlackBoxNotFound(notFound())).toBe(true);
    expect(isBlackBoxNotFound(new ApiError('Mission not found', 404, null))).toBe(true);
    expect(isBlackBoxNotFound(new ApiError('Nope', 404, { code: 'OtherThing' }))).toBe(false);
    expect(isBlackBoxNotFound(new ApiError('Boom', 500, null))).toBe(false);
    expect(isBlackBoxNotFound(new Error('offline'))).toBe(false);
  });

  it('ranks recovery paths, flags data loss and manual paths, and bounds long lists', () => {
    expect(rankedRecoveryPaths(blackBoxEntry()).map((p) => p.id)).toEqual(['rp_retry', 'rp_cancel', 'rp_manual']);
    expect(rankedRecoveryPaths(blackBoxEntry({ record: null }))).toEqual([]);
    expect(pathLosesData(CANCEL_PATH)).toBe(true);
    expect(pathLosesData({ ...CANCEL_PATH, dataLossRisk: false })).toBe(true);
    expect(pathLosesData(RETRY_PATH)).toBe(false);
    expect(pathIsRunnable(RETRY_PATH)).toBe(true);
    expect(pathIsRunnable(MANUAL_PATH)).toBe(false);
    expect(bounded(['a', 'b', 'c'], 30, 2)).toEqual({ shown: ['a', 'b'], more: 28 });
    expect(bounded(null)).toEqual({ shown: [], more: 0 });
  });
});

describe('black box panel', () => {
  it('renders the user-audience summary from a fixture', async () => {
    await renderPanel();
    expect(api.getMissionBlackBox).toHaveBeenCalledWith('msn_1');
    expect(screen.getByTestId('bbx-what-happened')).toHaveTextContent('The mission produced work, but landing stopped because the checkout had 52 staged paths.');
    expect(screen.getByTestId('bbx-where')).toHaveTextContent(/Landing \/ Dirty Checkout/);
    expect(screen.getByText('LandingService.LandAsync')).toBeTruthy();
    expect(screen.getByText('src/Armada.Server/LandingService.cs:412')).toBeTruthy();
    expect(screen.getByTestId('bbx-status-change')).toHaveTextContent(/WorkProduced -> LandingFailed/);
    expect(screen.getByTestId('bbx-how')).toHaveTextContent(/Git: merge/);
    expect(screen.getByText('error: Your local changes would be overwritten by merge.')).toBeTruthy();
    expect(screen.getByTestId('bbx-why-0')).toHaveTextContent(/The vessel checkout was dirty when landing started\./);
    expect(screen.getByTestId('bbx-conflict-mission-0')).toHaveTextContent(/Refactor auth/);
    // 11 of 12 files came back; ten are shown and the rest are counted from the server's total.
    const files = within(screen.getByTestId('bbx-conflict-files'));
    expect(files.getByText('src/Armada.Core/File1.cs')).toBeTruthy();
    expect(files.queryByText('src/Armada.Core/File11.cs')).toBeNull();
    expect(files.getByText('and 2 more')).toBeTruthy();
    expect(within(screen.getByTestId('bbx-conflict-directories')).getByText('src/Armada.Core (12)')).toBeTruthy();
    // Paths in rank order; a manual path has no run button.
    expect(screen.getByTestId('bbx-path-rp_retry')).toBeTruthy();
    expect(screen.getByTestId('bbx-recover-rp_cancel')).toBeTruthy();
    expect(screen.queryByTestId('bbx-recover-rp_manual')).toBeNull();
    expect(within(screen.getByTestId('bbx-path-rp_cancel')).getByText('Can lose work')).toBeTruthy();
  });

  it('opens a conflicting mission, the vessel and the entry', async () => {
    await renderPanel();
    await press('bbx-conflict-mission-0');
    expect(mockRouter.push).toHaveBeenLastCalledWith('/missions/msn_2');
    await press('bbx-open-entry');
    expect(mockRouter.push).toHaveBeenLastCalledWith('/blackbox/bbx_1');
  });

  it('renders an entry without a record from the index row alone', async () => {
    api.getMissionBlackBox.mockResolvedValue(blackBoxEntry({ record: null, summary: null }));
    await renderPanel();
    expect(screen.getByTestId('bbx-what-happened')).toHaveTextContent('Landing blocked: the checkout has 52 staged paths.');
    expect(screen.getByTestId('bbx-status-change')).toHaveTextContent(/WorkProduced -> LandingFailed/);
    expect(screen.queryByTestId('bbx-path-rp_retry')).toBeNull();
    expect(screen.queryByTestId('bbx-conflict-files')).toBeNull();
  });

  it('dry runs a recovery path, then runs it as Operator after confirmation', async () => {
    api.runBlackBoxRecovery
      .mockResolvedValueOnce({ entryId: 'bbx_1', pathId: 'rp_retry', dryRun: true, confirmToken: 'bbc_1', plan: RETRY_PATH.steps, succeeded: true })
      .mockResolvedValueOnce({ entryId: 'bbx_1', pathId: 'rp_retry', dryRun: false, succeeded: true, stepResults: [{ order: 1, tool: 'retry_landing', succeeded: true, message: 'Landed.' }] });
    await renderPanel();
    await press('bbx-recover-rp_retry');
    expect(api.runBlackBoxRecovery).toHaveBeenNthCalledWith(1, 'bbx_1', 'rp_retry', { dryRun: true, authorKind: 'Operator' });
    expect(screen.getByTestId('bbx-recovery-step-1')).toHaveTextContent(/Retry landing the mission/);
    expect(screen.queryByTestId('bbx-recovery-acknowledge')).toBeNull();
    await press('bbx-recovery-confirm');
    expect(api.runBlackBoxRecovery).toHaveBeenNthCalledWith(2, 'bbx_1', 'rp_retry', { dryRun: false, confirmToken: 'bbc_1', acknowledgeDataLoss: undefined, authorKind: 'Operator' });
    await waitFor(() => expect(screen.getByTestId('toast-success')).toHaveTextContent('Recovery "Clean the checkout and retry landing" ran.'));
    expect(within(screen.getByTestId('bbx-recovery-result')).getByText('Landed.')).toBeTruthy();
    // The entry reloads after the run.
    await waitFor(() => expect(api.getMissionBlackBox).toHaveBeenCalledTimes(2));
  });

  it('needs the data-loss acknowledgement before running a path that can lose work', async () => {
    api.runBlackBoxRecovery
      .mockResolvedValueOnce({ entryId: 'bbx_1', pathId: 'rp_cancel', dryRun: true, confirmToken: 'bbc_2', plan: CANCEL_PATH.steps, succeeded: true })
      .mockResolvedValueOnce({ entryId: 'bbx_1', pathId: 'rp_cancel', dryRun: false, succeeded: true, stepResults: [] });
    await renderPanel();
    await press('bbx-recover-rp_cancel');
    expect(screen.getByTestId('bbx-recovery-data-loss')).toBeTruthy();
    expect(screen.getByTestId('bbx-recovery-confirm')).toBeDisabled();
    await press('bbx-recovery-confirm');
    expect(api.runBlackBoxRecovery).toHaveBeenCalledTimes(1);
    await act(async () => { await fireEvent(screen.getByTestId('bbx-recovery-acknowledge'), 'valueChange', true); });
    expect(screen.getByTestId('bbx-recovery-confirm')).not.toBeDisabled();
    await press('bbx-recovery-confirm');
    expect(api.runBlackBoxRecovery).toHaveBeenLastCalledWith('bbx_1', 'rp_cancel', { dryRun: false, confirmToken: 'bbc_2', acknowledgeDataLoss: true, authorKind: 'Operator' });
  });

  it('cancelling the confirmation runs nothing', async () => {
    api.runBlackBoxRecovery.mockResolvedValueOnce({ entryId: 'bbx_1', pathId: 'rp_retry', dryRun: true, confirmToken: 'bbc_1', succeeded: true });
    await renderPanel();
    await press('bbx-recover-rp_retry');
    await press('bbx-recovery-cancel');
    expect(screen.queryByTestId('bbx-recovery-confirm')).toBeNull();
    expect(api.runBlackBoxRecovery).toHaveBeenCalledTimes(1);
  });

  it('a refused confirmation closes the dialog and asks for a new dry run', async () => {
    api.runBlackBoxRecovery
      .mockResolvedValueOnce({ entryId: 'bbx_1', pathId: 'rp_retry', dryRun: true, confirmToken: 'bbc_1', succeeded: true })
      .mockRejectedValueOnce(new ApiError('The confirmation token expired.', 409, { code: 'BlackBoxConfirmationRequired' }));
    await renderPanel();
    await press('bbx-recover-rp_retry');
    await press('bbx-recovery-confirm');
    await waitFor(() => expect(screen.getByTestId('toast-error')).toHaveTextContent('Recovery refused: The confirmation token expired. Run a new dry run.'));
    expect(screen.queryByTestId('bbx-recovery-confirm')).toBeNull();
  });

  it('reports a failed run and a dry run that fails or returns no confirmation', async () => {
    api.runBlackBoxRecovery
      .mockRejectedValueOnce(new ApiError('Forbidden', 403, null))
      .mockResolvedValueOnce({ entryId: 'bbx_1', pathId: 'rp_retry', dryRun: true, confirmToken: null, succeeded: false, message: 'Mission changed since analysis.' })
      .mockResolvedValueOnce({ entryId: 'bbx_1', pathId: 'rp_retry', dryRun: true, confirmToken: 'bbc_3', succeeded: true })
      .mockResolvedValueOnce({ entryId: 'bbx_1', pathId: 'rp_retry', dryRun: false, succeeded: false, message: 'Landing failed again.', stepResults: [{ order: 1, tool: 'retry_landing', succeeded: false, message: 'still dirty' }] });
    await renderPanel();
    await press('bbx-recover-rp_retry');
    await waitFor(() => expect(screen.getByText('Dry run failed: Forbidden')).toBeTruthy());
    expect(screen.queryByTestId('bbx-recovery-confirm')).toBeNull();
    await press('bbx-recover-rp_retry');
    await waitFor(() => expect(screen.getByText('The dry run did not return a confirmation. Mission changed since analysis.')).toBeTruthy());
    expect(screen.queryByTestId('bbx-recovery-confirm')).toBeNull();
    await press('bbx-recover-rp_retry');
    await press('bbx-recovery-confirm');
    await waitFor(() => expect(screen.getByText('Recovery "Clean the checkout and retry landing" failed: Landing failed again.')).toBeTruthy());
    expect(within(screen.getByTestId('bbx-recovery-result')).getByText('still dirty')).toBeTruthy();
  });

  it('copies the FIX THIS brief (agent audience) to the clipboard', async () => {
    await renderPanel();
    await press('bbx-copy');
    expect(api.getBlackBoxBrief).toHaveBeenCalledWith('bbx_1', { audience: 'agent' });
    expect(clipboard.setStringAsync).toHaveBeenCalledWith(AGENT_BRIEF);
    await waitFor(() => expect(screen.getByTestId('toast-success')).toHaveTextContent('FIX THIS brief copied.'));
  });

  it('shares the agent brief through the platform share sheet', async () => {
    const share = jest.spyOn(Share, 'share').mockResolvedValue({ action: Share.sharedAction });
    await renderPanel();
    await press('bbx-share');
    expect(share).toHaveBeenCalledWith({ message: AGENT_BRIEF, title: 'Black box: msn_1' }, { subject: 'Black box: msn_1', dialogTitle: 'Black box: msn_1' });
    share.mockRestore();
  });

  it('does not copy or share when the brief cannot be read', async () => {
    const share = jest.spyOn(Share, 'share').mockResolvedValue({ action: Share.sharedAction });
    api.getBlackBoxBrief.mockRejectedValue(new ApiError('Object missing', 409, { code: 'BlackBoxObjectMissing' }));
    await renderPanel();
    await press('bbx-copy');
    await waitFor(() => expect(screen.getByText('Copy failed: Object missing')).toBeTruthy());
    await press('bbx-share');
    await waitFor(() => expect(screen.getByText('Share failed: Object missing')).toBeTruthy());
    expect(clipboard.setStringAsync).not.toHaveBeenCalled();
    expect(share).not.toHaveBeenCalled();
    share.mockRestore();
  });

  it('opens Ask Armada with the canonical prompt prefilled', async () => {
    await renderPanel();
    await press('bbx-ask');
    expect(mockRouter.push).toHaveBeenCalledWith(`/ask?prompt=${encodeURIComponent(CANONICAL)}`);
  });

  it('shows the full user-audience report', async () => {
    await renderPanel();
    await press('bbx-report');
    expect(api.getBlackBoxBrief).toHaveBeenCalledWith('bbx_1', { audience: 'user' });
    expect(within(screen.getByTestId('bbx-report-text')).getByText('What happened')).toBeTruthy();
  });

  it('resolves an entry with a note and a link', async () => {
    api.resolveBlackBox.mockResolvedValue(blackBoxEntry({ resolution: 'Resolved' }));
    await renderPanel();
    await press('bbx-resolve');
    await act(async () => { fireEvent.changeText(screen.getByTestId('bbx-resolve-note'), ' cleaned the checkout '); });
    await act(async () => { fireEvent.changeText(screen.getByTestId('bbx-resolve-link'), 'abc123'); });
    await press('bbx-resolve-submit');
    expect(api.resolveBlackBox).toHaveBeenCalledWith('bbx_1', { resolution: 'Resolved', note: 'cleaned the checkout', link: 'abc123' });
    await waitFor(() => expect(screen.getByTestId('toast-success')).toHaveTextContent('Black box entry marked Resolved.'));
  });

  it('keeps the resolve sheet open when resolving fails', async () => {
    api.resolveBlackBox.mockRejectedValue(new ApiError('Forbidden', 403, null));
    await renderPanel();
    await press('bbx-resolve');
    await press('bbx-resolve-submit');
    await waitFor(() => expect(screen.getByText('Resolve failed: Forbidden')).toBeTruthy());
    expect(screen.getByTestId('bbx-resolve-submit')).toBeTruthy();
  });

  it('adds a note, and refuses an empty one', async () => {
    api.addBlackBoxNote.mockResolvedValue({ id: 'bbn_1', entryId: 'bbx_1', authorKind: 'Operator', body: 'x', createdUtc: '2026-10-28T10:00:00Z' });
    await renderPanel();
    await press('bbx-add-note');
    expect(screen.getByTestId('bbx-note-submit')).toBeDisabled();
    await act(async () => { fireEvent.changeText(screen.getByTestId('bbx-note-body'), '   '); });
    expect(screen.getByTestId('bbx-note-submit')).toBeDisabled();
    await act(async () => { fireEvent.changeText(screen.getByTestId('bbx-note-body'), ' Stashed the staged paths. '); });
    await press('bbx-note-submit');
    expect(api.addBlackBoxNote).toHaveBeenCalledWith('bbx_1', 'Stashed the staged paths.');
    await waitFor(() => expect(screen.getByTestId('toast-success')).toHaveTextContent('Note added.'));
  });

  it('shows the fallback when the subject has no entry (404)', async () => {
    api.getMissionBlackBox.mockRejectedValue(notFound());
    await renderPanel({ kind: 'mission', id: 'msn_1' }, <Text testID="fallback">plain failure reason</Text>);
    await waitFor(() => expect(screen.getByTestId('fallback')).toBeTruthy());
    expect(screen.queryByTestId('bbx-panel')).toBeNull();
    expect(screen.queryByTestId('bbx-panel-error')).toBeNull();
  });

  it('shows the fallback and a retry when the entry fails to load', async () => {
    api.getMissionBlackBox.mockRejectedValueOnce(new ApiError('Server error', 500, null)).mockResolvedValueOnce(blackBoxEntry());
    await renderPanel({ kind: 'mission', id: 'msn_1' }, <Text testID="fallback">plain failure reason</Text>);
    await waitFor(() => expect(screen.getByTestId('bbx-panel-error')).toHaveTextContent(/Black box unavailable: Server error/));
    expect(screen.getByTestId('fallback')).toBeTruthy();
    await press('bbx-panel-retry');
    await waitFor(() => expect(screen.getByTestId('bbx-panel')).toBeTruthy());
  });

  it('reloads on black box socket events and ignores others', async () => {
    const { socket } = await renderPanel();
    expect(api.getMissionBlackBox).toHaveBeenCalledTimes(1);
    await act(async () => { socket.message({ type: 'captain.changed', data: {} }); });
    api.getMissionBlackBox.mockResolvedValue(blackBoxEntry({ resolution: 'Resolved' }));
    await act(async () => { socket.message({ type: 'blackbox.resolved', data: { entryId: 'bbx_1', missionId: 'msn_1', resolution: 'Resolved' } }); });
    await waitFor(() => expect(screen.getByText('Change resolution')).toBeTruthy());
    expect(api.getMissionBlackBox).toHaveBeenCalledTimes(2);
  });
});

describe('mission and voyage detail', () => {
  function mission(overrides: Partial<Mission> = {}): Mission {
    return {
      id: 'msn_1', tenantId: null, userId: null, voyageId: null, vesselId: 'vsl_1', captainId: null, title: 'Fix login', status: 'LandingFailed',
      priority: 100, parentMissionId: null, persona: null, dependsOnMissionId: null, branchName: 'armada/msn_1', dockId: null, processId: null,
      prUrl: null, commitHash: null, failureReason: 'Landing blocked.', requiresReview: false, reviewDenyAction: 'RetryStage', reviewComment: null,
      reviewedByUserId: null, reviewRequestedUtc: null, reviewedUtc: null, createdUtc: '2026-10-07T10:00:00Z', startedUtc: null, completedUtc: null,
      totalRuntimeMs: null, lastUpdateUtc: '2026-10-07T10:00:00Z', description: 'Do the thing', mode: 'Implementation', diffSnapshot: null, ...overrides,
    } as Mission;
  }

  beforeEach(() => {
    api.getMissionLandingPreview.mockResolvedValue(null as never);
  });

  it('a failed mission shows its black box in place of the bare failure reason', async () => {
    api.getMission.mockResolvedValue(mission());
    await renderScreen(<MissionDetail id="msn_1" embedded />);
    await waitFor(() => expect(screen.getByTestId('bbx-panel')).toBeTruthy());
    expect(screen.queryByTestId('mission-failure-reason')).toBeNull();
  });

  it('a failed mission without an entry keeps the failure reason', async () => {
    api.getMission.mockResolvedValue(mission());
    api.getMissionBlackBox.mockRejectedValue(notFound());
    await renderScreen(<MissionDetail id="msn_1" embedded />);
    await waitFor(() => expect(api.getMissionBlackBox).toHaveBeenCalledWith('msn_1'));
    expect(screen.getByTestId('mission-failure-reason')).toBeTruthy();
    expect(screen.queryByTestId('bbx-panel')).toBeNull();
  });

  it('a running mission does not look up a black box', async () => {
    api.getMission.mockResolvedValue(mission({ status: 'InProgress', failureReason: null }));
    await renderScreen(<MissionDetail id="msn_1" embedded />);
    await waitFor(() => expect(screen.getByTestId('mission-detail-title')).toBeTruthy());
    expect(api.getMissionBlackBox).not.toHaveBeenCalled();
  });

  it('a failed voyage shows its black box', async () => {
    api.getVoyage.mockResolvedValue({ id: 'vyg_1', title: 'Auth voyage', status: 'Failed', createdUtc: '2026-10-07T10:00:00Z' } as Voyage as never);
    api.listMissions.mockResolvedValue(page([]) as never);
    await renderScreen(<VoyageDetail id="vyg_1" embedded />);
    await waitFor(() => expect(screen.getByTestId('bbx-panel')).toBeTruthy());
    expect(api.getVoyageBlackBox).toHaveBeenCalledWith('vyg_1');
    expect(screen.getByTestId('bbx-where')).toHaveTextContent(/Voyage \/ Stage Failed/);
  });

  it('an open voyage does not look up a black box', async () => {
    api.getVoyage.mockResolvedValue({ id: 'vyg_1', title: 'Auth voyage', status: 'InProgress', createdUtc: '2026-10-07T10:00:00Z' } as Voyage as never);
    api.listMissions.mockResolvedValue(page([]) as never);
    await renderScreen(<VoyageDetail id="vyg_1" embedded />);
    await waitFor(() => expect(screen.getByTestId('voyage-detail-title')).toBeTruthy());
    expect(api.getVoyageBlackBox).not.toHaveBeenCalled();
  });
});

describe('black box entry screen', () => {
  it('shows one entry by id, without the Open entry action', async () => {
    setMockParams({ id: 'bbx_1' });
    await renderScreen(<BlackBoxEntryRoute />);
    await waitFor(() => expect(screen.getByTestId('bbx-panel')).toBeTruthy());
    expect(api.getBlackBox).toHaveBeenCalledWith('bbx_1');
    expect(screen.queryByTestId('bbx-open-entry')).toBeNull();
  });

  it('shows not found for an unknown or hidden entry (404)', async () => {
    setMockParams({ id: 'bbx_9' });
    api.getBlackBox.mockRejectedValue(new ApiError('No black box entry bbx_9.', 404, { code: 'BlackBoxNotFound' }));
    await renderScreen(<BlackBoxEntryRoute />);
    await waitFor(() => expect(screen.getByText('Black box entry not found.')).toBeTruthy());
  });

  it('shows an error with retry when the entry fails to load', async () => {
    setMockParams({ id: 'bbx_1' });
    api.getBlackBox.mockRejectedValueOnce(new ApiError('Server error', 500, null)).mockResolvedValueOnce(blackBoxEntry());
    await renderScreen(<BlackBoxEntryRoute />);
    await waitFor(() => expect(screen.getByText('Failed to load the black box.')).toBeTruthy());
    expect(screen.getByText('Server error')).toBeTruthy();
    await act(async () => { await fireEvent.press(screen.getByText('Retry')); });
    await waitFor(() => expect(screen.getByTestId('bbx-panel')).toBeTruthy());
  });
});

describe('black box list (the Missions hub Black Box tab)', () => {
  beforeEach(() => { setMockParams({ tab: 'blackbox' }); });

  it('is the fourth Missions tab', async () => {
    api.listBlackBox.mockResolvedValue(page([blackBoxSummary('1')]));
    await renderScreen(<MissionsHubScreen />);
    await waitFor(() => expect(screen.getByTestId('bbx-row-1')).toBeTruthy());
    expect(screen.getByTestId('missions-hub-tab-blackbox')).toBeTruthy();
  });

  it('pages through recent entries and opens one', async () => {
    api.listBlackBox.mockImplementation(async (query) => {
      if (query?.pageNumber === 2) return { ...page([blackBoxSummary('26')], 26), pageNumber: 2 };
      return page(Array.from({ length: 25 }, (_v, i) => blackBoxSummary(String(i + 1))), 26);
    });
    await renderScreen(<MissionsHubScreen />);
    await waitFor(() => expect(screen.getByTestId('bbx-row-1')).toBeTruthy());
    expect(api.listBlackBox).toHaveBeenCalledWith({ pageNumber: 1, pageSize: 25, vesselId: undefined, kind: undefined });
    await act(async () => { fireEvent(screen.getByTestId('bbx-entries'), 'endReached'); });
    await waitFor(() => expect(api.listBlackBox).toHaveBeenCalledWith(expect.objectContaining({ pageNumber: 2 })));
    await waitFor(() => expect(screen.getByTestId('bbx-entries-count')).toHaveTextContent('Showing 26 of 26'));
    await act(async () => { await fireEvent.press(screen.getByTestId('bbx-row-1')); });
    expect(mockRouter.push).toHaveBeenCalledWith('/blackbox/1');
  });

  it('filters by vessel and failure kind on the server', async () => {
    api.listBlackBox.mockResolvedValue(page([blackBoxSummary('1')]));
    await renderScreen(<MissionsHubScreen />);
    await waitFor(() => expect(screen.getByTestId('bbx-row-1')).toBeTruthy());
    await act(async () => { await fireEvent.press(screen.getByTestId('bbx-filter-vessel')); });
    await act(async () => { await fireEvent.press(screen.getByTestId('bbx-filter-vessel-option-vsl_2')); });
    await waitFor(() => expect(api.listBlackBox).toHaveBeenLastCalledWith({ pageNumber: 1, pageSize: 25, vesselId: 'vsl_2', kind: undefined }));
    api.listBlackBox.mockResolvedValue(page([]));
    await act(async () => { await fireEvent.press(screen.getByTestId('bbx-filter-kind')); });
    await act(async () => { await fireEvent.press(screen.getByTestId('bbx-filter-kind-option-LandingConflict')); });
    await waitFor(() => expect(api.listBlackBox).toHaveBeenLastCalledWith({ pageNumber: 1, pageSize: 25, vesselId: 'vsl_2', kind: 'LandingConflict' }));
    await waitFor(() => expect(screen.getByText('No black box entries match the current filters.')).toBeTruthy());
  });

  it('shows the empty state with no entries', async () => {
    api.listBlackBox.mockResolvedValue(page([]));
    await renderScreen(<MissionsHubScreen />);
    await waitFor(() => expect(screen.getByText('No black box entries yet.')).toBeTruthy());
    expect(screen.getByText('Armada records an entry here whenever a mission or voyage fails.')).toBeTruthy();
  });

  it('shows an error state when the list fails', async () => {
    api.listBlackBox.mockRejectedValue(new ApiError('Forbidden', 403, null));
    await renderScreen(<MissionsHubScreen />);
    await waitFor(() => expect(screen.getByText('Forbidden')).toBeTruthy());
  });

  it('reloads on black box socket events', async () => {
    api.listBlackBox.mockResolvedValue(page([blackBoxSummary('1')]));
    const { socket } = await renderScreen(<MissionsHubScreen />);
    await waitFor(() => expect(screen.getByTestId('bbx-row-1')).toBeTruthy());
    const before = api.listBlackBox.mock.calls.length;
    await act(async () => { socket.message({ type: 'blackbox.captured', data: { entryId: 'bbx_2' } }); });
    await waitFor(() => expect(api.listBlackBox.mock.calls.length).toBe(before + 1));
  });
});
