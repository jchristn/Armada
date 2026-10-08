import { fireEvent, screen, waitFor } from '@testing-library/react-native';
import * as client from '@dashboard/api/client';
import type { MergeEntry } from '@dashboard/types/models';
import { MergeEntryDetail } from '../screens/operations/MergeEntryDetail';
import { MergeQueueList, filterMergeEntries } from '../screens/operations/MergeQueueList';
import { page } from '../test/operationsClient';
import { mockRouter, setMockParams } from '../test/routerMock';
import { renderScreen } from '../test/screen';
import { rowActionTarget } from '../test/a11y';

jest.mock('@dashboard/api/client', () => ({
  ...require('../test/operationsClient').operationsClientMockFactory(),
  getVesselLandingPreview: jest.fn(),
}));
jest.mock('expo-router', () => require('../test/routerMock').routerMockFactory());

const api = client as jest.Mocked<typeof client>;

// SwipeRow's gesture under the Jest reanimated mock logs a worklet warning on every row; it is not about this screen.
const realError = console.error;
beforeAll(() => {
  jest.spyOn(console, 'error').mockImplementation((...args: unknown[]) => {
    if (typeof args[0] === 'string' && args[0].includes('[react-native-gesture-handler]')) return;
    realError(...args);
  });
});
afterAll(() => { (console.error as jest.Mock).mockRestore(); });

function entry(id: string, over: Partial<MergeEntry> = {}): MergeEntry {
  return {
    id, tenantId: 'ten_1', missionId: 'msn_1', vesselId: 'vsl_1', branchName: `armada/${id}`, targetBranch: 'main', status: 'Queued', priority: 0,
    batchId: null, testCommand: 'npm test', testOutput: 'ok', testExitCode: 0, createdUtc: '2026-10-07T10:00:00Z', lastUpdateUtc: '2026-10-07T10:00:00Z',
    testStartedUtc: null, completedUtc: null, ...over,
  };
}

/** Runs a row's swipe action the way VoiceOver and TalkBack do (accessibility actions). */
async function rowAction(testID: string, action: string) {
  await fireEvent(rowActionTarget(screen.getByTestId(`${testID}-swipe`), action), 'accessibilityAction', { nativeEvent: { actionName: action } });
}

beforeEach(() => {
  setMockParams({});
  api.listMergeQueue.mockResolvedValue(page([entry('mrg_1'), entry('mrg_2', { status: 'Failed', vesselId: 'vsl_2', missionId: null })]) as never);
  api.listVessels.mockResolvedValue(page([{ id: 'vsl_1', name: 'api' }, { id: 'vsl_2', name: 'web' }]) as never);
  api.processMergeEntry.mockResolvedValue(undefined);
  api.processAllMergeQueue.mockResolvedValue(undefined);
  api.cancelMergeEntry.mockResolvedValue(undefined);
  api.deleteMergeEntry.mockResolvedValue(undefined);
  api.enqueueMerge.mockResolvedValue(entry('mrg_3'));
  api.getMergeEntry.mockResolvedValue(entry('mrg_1'));
  (api.getVesselLandingPreview as jest.Mock).mockResolvedValue({
    vesselId: 'vsl_1', branchCategory: 'Feature', landingMode: 'LocalMerge', branchCleanupPolicy: null, expectedLandingAction: 'Merge',
    targetBranchProtected: true, protectedBranchMatch: 'main', requirePullRequestForProtectedBranches: true, requireMergeQueueForReleaseBranches: false,
    isReadyToLand: false, issues: [{ code: 'x', severity: 'Warning', title: 'Checks missing', message: 'No passing checks' }],
  });
});

describe('merge queue list', () => {
  it('filters like the dashboard columns', () => {
    const rows = [entry('a', { branchName: 'feat/x' }), entry('b', { branchName: 'fix/y', status: 'Landed', vesselId: 'vsl_2' })];
    expect(filterMergeEntries(rows, { search: 'fix', status: '', vesselId: '' }).map((e) => e.id)).toEqual(['b']);
    expect(filterMergeEntries(rows, { search: '', status: 'land', vesselId: '' }).map((e) => e.id)).toEqual(['b']);
    expect(filterMergeEntries(rows, { search: '', status: '', vesselId: 'vsl_1' }).map((e) => e.id)).toEqual(['a']);
  });

  it('lists entries, opens one, and runs row actions with confirmation', async () => {
    const onSelect = jest.fn();
    await renderScreen(<MergeQueueList onSelect={onSelect} />);
    await fireEvent.press(await screen.findByTestId('merge-row-mrg_1'));
    expect(onSelect).toHaveBeenCalledWith('mrg_1');

    await rowAction('merge-row-mrg_1', 'process');
    await fireEvent.press(await screen.findByTestId('merge-confirm-confirm'));
    await waitFor(() => expect(api.processMergeEntry).toHaveBeenCalledWith('mrg_1'));

    await rowAction('merge-row-mrg_1', 'cancel');
    await fireEvent.press(await screen.findByTestId('merge-confirm-confirm'));
    await waitFor(() => expect(api.cancelMergeEntry).toHaveBeenCalledWith('mrg_1'));

    await rowAction('merge-row-mrg_2', 'delete');
    await fireEvent.press(await screen.findByTestId('merge-confirm-confirm'));
    await waitFor(() => expect(api.deleteMergeEntry).toHaveBeenCalledWith('mrg_2'));

    await fireEvent.press(screen.getByTestId('merge-process-all'));
    await fireEvent.press(await screen.findByTestId('merge-confirm-confirm'));
    await waitFor(() => expect(api.processAllMergeQueue).toHaveBeenCalled());
  });

  it('opens the mission diff from a row', async () => {
    api.getMissionDiff.mockResolvedValue({ diff: 'diff --git a/x b/x\n--- a/x\n+++ b/x\n@@ -1 +1 @@\n-a\n+b\n' } as never);
    await renderScreen(<MergeQueueList onSelect={jest.fn()} />);
    await screen.findByTestId('merge-row-mrg_1');
    await rowAction('merge-row-mrg_1', 'diff');
    expect(await screen.findByTestId('mission-output-diff-summary')).toHaveTextContent('1 file(s), +1 -1');
    expect(api.getMissionDiff).toHaveBeenCalledWith('msn_1');
  });

  it('bulk deletes the long-pressed selection', async () => {
    await renderScreen(<MergeQueueList onSelect={jest.fn()} />);
    await fireEvent(await screen.findByTestId('merge-row-mrg_1'), 'longPress');
    await fireEvent.press(screen.getByTestId('merge-row-mrg_2'));
    expect(screen.getByTestId('merge-selection')).toBeTruthy();
    await fireEvent.press(screen.getByTestId('merge-selection-delete'));
    await fireEvent.press(await screen.findByTestId('merge-confirm-confirm'));
    await waitFor(() => expect(api.deleteMergeEntry).toHaveBeenCalledTimes(2));
    expect(api.deleteMergeEntry).toHaveBeenCalledWith('mrg_1');
    expect(api.deleteMergeEntry).toHaveBeenCalledWith('mrg_2');
  });

  it('enqueues a merge from the form', async () => {
    await renderScreen(<MergeQueueList onSelect={jest.fn()} />);
    await fireEvent.press(await screen.findByTestId('merge-enqueue'));
    await fireEvent.changeText(await screen.findByTestId('enqueue-branch'), 'feature/x');
    await fireEvent.changeText(screen.getByTestId('enqueue-test'), 'npm test');
    await fireEvent.changeText(screen.getByTestId('enqueue-priority'), '5');
    await fireEvent.press(screen.getByTestId('enqueue-submit'));
    await waitFor(() => expect(api.enqueueMerge).toHaveBeenCalledWith({
      branchName: 'feature/x', targetBranch: 'main', missionId: undefined, vesselId: undefined, testCommand: 'npm test', priority: 5,
    }));
  });
});

describe('merge entry detail', () => {
  it('shows the landing preview and fields, and runs actions', async () => {
    await renderScreen(<MergeEntryDetail id="mrg_1" />);
    expect(await screen.findByTestId('merge-entry-landing')).toBeTruthy();
    expect(screen.getByText('Needs Review')).toBeTruthy();
    expect(screen.getByText('Checks missing')).toBeTruthy();
    expect(screen.getByTestId('merge-entry-test-output')).toHaveTextContent('ok');
    expect(api.getVesselLandingPreview).toHaveBeenCalledWith('vsl_1', 'armada/mrg_1');

    await fireEvent.press(screen.getByTestId('merge-entry-mission'));
    expect(mockRouter.push).toHaveBeenCalledWith('/missions/msn_1');

    await fireEvent.press(screen.getByTestId('merge-entry-process'));
    await fireEvent.press(await screen.findByTestId('merge-entry-confirm-confirm'));
    await waitFor(() => expect(api.processMergeEntry).toHaveBeenCalledWith('mrg_1'));

    await fireEvent.press(screen.getByTestId('merge-entry-delete'));
    await fireEvent.press(await screen.findByTestId('merge-entry-confirm-confirm'));
    await waitFor(() => expect(api.deleteMergeEntry).toHaveBeenCalledWith('mrg_1'));
    expect(mockRouter.back).toHaveBeenCalled();
  });

  it('reports a missing entry', async () => {
    api.getMergeEntry.mockRejectedValue(new Error('Not found'));
    await renderScreen(<MergeEntryDetail id="mrg_x" />);
    expect(await screen.findByText('Not found')).toBeTruthy();
  });
});
