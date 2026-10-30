import { act, fireEvent, screen, waitFor, within } from '@testing-library/react-native';
import * as client from '@dashboard/api/client';
import type { BlackBoxCascadeGraph, BlackBoxNearbySuccess, BlackBoxTimelineItem, MissionOverlapPreview } from '@dashboard/types/blackbox';
import type { AskQuickAction, Voyage } from '@dashboard/types/models';
import BlackBoxEntryRoute from '../app/(app)/(work)/blackbox/[id]';
import { Composer } from '../components/ask/Composer';
import { blackBoxFormArguments } from '../components/ask/BlackBoxForm';
import { OVERLAP_PREVIEW_DEBOUNCE_MS } from '../screens/blackbox/DispatchWarnings';
import {
  canPreview,
  cascadeHasFailures,
  cascadeRelations,
  dispatchWarningsOf,
  firstWarningMessage,
  formatHoursFromFailure,
  mergeWarnings,
  orderedCascadeNodes,
  overlapKindLabel,
  previewWarnings,
  timelineEntityRoute,
  timelineItemKey,
  warningCount,
} from '../screens/blackbox/insightModel';
import { VoyageCascadeList } from '../screens/blackbox/VoyageCascadeSection';
import { DispatchForm } from '../screens/operations/DispatchForm';
import { VoyageDetail } from '../screens/operations/VoyageDetail';
import { blackBoxEntry, blackBoxSummary } from '../test/blackBoxFixtures';
import { emptyOverlapPreview } from '../test/mockClient';
import { page } from '../test/operationsClient';
import { mockRouter, setMockParams } from '../test/routerMock';
import { renderScreen } from '../test/screen';

jest.mock('@dashboard/api/client', () => ({
  ...require('../test/operationsClient').operationsClientMockFactory(),
  getVesselReadiness: jest.fn(async () => null),
  listCheckRuns: jest.fn(async () => ({ objects: [], totalRecords: 0 })),
  listDeployments: jest.fn(async () => ({ objects: [], totalRecords: 0 })),
}));
jest.mock('expo-router', () => require('../test/routerMock').routerMockFactory());

const api = client as jest.Mocked<typeof client>;
const { ApiError } = jest.requireActual('@dashboard/api/client') as typeof client;

function pageOf<T>(objects: T[], totalRecords: number, pageNumber = 1, pageSize = 10) {
  return { success: true, pageNumber, pageSize, totalPages: Math.max(1, Math.ceil(totalRecords / pageSize)), totalRecords, objects, totalMs: 1 };
}

function timelineItem(n: number, overrides: Partial<BlackBoxTimelineItem> = {}): BlackBoxTimelineItem {
  return { timestampUtc: `2026-10-28T10:${String(n).padStart(2, '0')}:00Z`, source: 'Mission', kind: 'mission.status', message: `Event ${n}`, entityId: 'msn_1', ...overrides };
}

const SUCCESS: BlackBoxNearbySuccess = {
  successId: 'bbs_1', missionId: 'msn_ok', missionTitle: 'Tidy the login form', landedUtc: '2026-10-28T09:00:00Z', hoursFromFailure: -1.26,
  baseCommit: 'abc', targetCommit: 'def', runMs: 1000, overlappingFiles: ['src/login.ts', 'src/auth.ts'], overlappingFileCount: 2,
};

const GRAPH: BlackBoxCascadeGraph = {
  voyageId: 'vyg_1', voyageTitle: 'Auth voyage', voyageStatus: 'Failed', entryId: 'bbx_v', rootMissionId: 'msn_a', rootEntryId: 'bbx_a',
  nodes: [
    { missionId: 'msn_c', title: 'Docs', status: 'Complete', isRoot: false },
    { missionId: 'msn_b', title: 'Wire the API', status: 'Cancelled', failureKind: null, entryId: 'bbx_b', dependsOnMissionId: 'msn_a', isRoot: false },
    { missionId: 'msn_a', title: 'Schema change', status: 'Failed', failureKind: 'MergeConflict', entryId: 'bbx_a', isRoot: true },
  ],
  edges: [
    { fromMissionId: 'msn_a', toMissionId: 'msn_b', kind: 'CancelledBecause' },
    { fromMissionId: 'msn_a', toMissionId: 'msn_c', kind: 'Sibling' },
  ],
  missionCount: 3,
  truncated: false,
};

const PREVIEW_WITH_WARNINGS: MissionOverlapPreview = {
  ...emptyOverlapPreview('vsl_a'),
  warnings: [{ kind: 'ActiveMission', overlappingMissionId: 'msn_busy', overlappingMissionTitle: 'Refactor auth', files: ['src/auth.ts'], fileCount: 1, message: 'Overlaps msn_busy on 1 file' }],
  warningCount: 1,
  suggestedDependsOnMissionId: 'msn_busy',
  briefLint: { findings: [{ ruleId: 'BRIEF-DEP-001', severity: 'Warning', message: 'Mentions another mission without a dependency', suggestion: 'Set dependsOnMissionId' }], findingCount: 1, clean: false, pathsChecked: true },
};

const BLACKBOX_ACTION: AskQuickAction = { name: 'blackbox', command: '/blackbox', toolName: 'get_blackbox', title: 'Black box', description: 'Show the black box of a failed mission' };

beforeEach(() => {
  jest.clearAllMocks();
  setMockParams({});
  api.getBlackBoxTimeline.mockReset().mockImplementation(async (entryId: string) => ({ ...pageOf([], 0), entryId, sources: [], truncated: false }));
  api.listBlackBoxNearbySuccesses.mockReset().mockImplementation(async () => pageOf([], 0) as never);
  api.getVoyageBlackBoxCascade.mockReset().mockRejectedValue(new ApiError('Not found.', 404, null));
  api.previewMissionOverlap.mockReset().mockImplementation(async (vesselId: string) => emptyOverlapPreview(vesselId));
  api.getBlackBox.mockResolvedValue(blackBoxEntry());
  api.listVessels.mockResolvedValue(page([{ id: 'vsl_a', name: 'api' }]) as never);
  api.listPipelines.mockResolvedValue(page([]) as never);
  api.listCaptains.mockResolvedValue(page([]) as never);
  api.listPersonas.mockResolvedValue(page([]) as never);
  api.listPlaybooks.mockResolvedValue(page([]) as never);
});

describe('black box insight model', () => {
  it('reads dispatch warnings from a create response or a preview, and counts them', () => {
    expect(dispatchWarningsOf(null)).toEqual({ overlap: [], lint: [] });
    expect(dispatchWarningsOf({ id: 'vyg_1' } as Voyage)).toEqual({ overlap: [], lint: [] });
    const created = dispatchWarningsOf({ overlapWarnings: PREVIEW_WITH_WARNINGS.warnings, briefLintWarnings: PREVIEW_WITH_WARNINGS.briefLint!.findings });
    expect(warningCount(created)).toBe(2);
    expect(firstWarningMessage(created)).toBe('Overlaps msn_busy on 1 file');
    expect(firstWarningMessage({ overlap: [], lint: created.lint })).toBe('Mentions another mission without a dependency');
    expect(warningCount(previewWarnings(PREVIEW_WITH_WARNINGS))).toBe(2);
    expect(warningCount(previewWarnings(null))).toBe(0);
    expect(warningCount(mergeWarnings([created, created]))).toBe(4);
    expect(warningCount(null)).toBe(0);
  });

  it('previews only with a vessel and some text, and labels the overlap kinds', () => {
    expect(canPreview('vsl_a', '', 'Fix it')).toBe(true);
    expect(canPreview('vsl_a', 'Title', '')).toBe(true);
    expect(canPreview('', 'Title', 'Fix it')).toBe(false);
    expect(canPreview('vsl_a', '  ', ' ')).toBe(false);
    expect(overlapKindLabel('ActiveMission')).toBe('Active mission');
    expect(overlapKindLabel('Reservation')).toBe('Reserved directory');
    expect(overlapKindLabel('Hotspot')).toBe('Conflict hotspot');
    expect(overlapKindLabel('Unknown')).toBe('Overlap');
  });

  it('keys and links timeline rows and formats the offset from the failure', () => {
    expect(timelineItemKey(timelineItem(1))).not.toBe(timelineItemKey(timelineItem(2)));
    expect(timelineEntityRoute('msn_1')).toBe('/missions/msn_1');
    expect(timelineEntityRoute('vyg_1')).toBe('/voyages/vyg_1');
    expect(timelineEntityRoute('dck_1')).toBe('/docks/dck_1');
    expect(timelineEntityRoute('mrg_1')).toBe('/merge-queue/mrg_1');
    expect(timelineEntityRoute('hbr_1')).toBeNull();
    expect(timelineEntityRoute(null)).toBeNull();
    expect(formatHoursFromFailure(2.46)).toBe('+2.5h');
    expect(formatHoursFromFailure(-1.26)).toBe('-1.3h');
    expect(formatHoursFromFailure(0)).toBe('0h');
  });

  it('orders the cascade root first and lists each mission\'s causes and effects', () => {
    expect(orderedCascadeNodes(GRAPH).map((n) => n.missionId)).toEqual(['msn_a', 'msn_b', 'msn_c']);
    expect(cascadeRelations(GRAPH, 'msn_a').map((r) => [r.otherMissionId, r.label])).toEqual([
      ['msn_b', '{{title}} was cancelled because this failed'],
      ['msn_c', '{{title}} also failed independently'],
    ]);
    expect(cascadeRelations(GRAPH, 'msn_b').map((r) => r.label)).toEqual(['Cancelled because {{title}} failed']);
    expect(cascadeRelations(GRAPH, 'msn_x')).toEqual([]);
    expect(cascadeHasFailures(GRAPH)).toBe(true);
    expect(cascadeHasFailures(null)).toBe(false);
    expect(cascadeHasFailures({ ...GRAPH, nodes: [] })).toBe(false);
    expect(cascadeHasFailures({
      ...GRAPH, rootMissionId: null, rootEntryId: null,
      nodes: [{ missionId: 'msn_1', status: 'Complete', isRoot: false }, { missionId: 'msn_2', status: 'Pending', isRoot: false }],
      edges: [{ fromMissionId: 'msn_1', toMissionId: 'msn_2', kind: 'DependsOn' }],
    })).toBe(false);
  });

  it('builds /blackbox arguments and rejects anything that is not a mission id', () => {
    expect(blackBoxFormArguments(' msn_1 ', '')).toEqual({ args: { missionId: 'msn_1' } });
    expect(blackBoxFormArguments('msn_1', ' Why? ')).toEqual({ args: { missionId: 'msn_1', question: 'Why?' } });
    expect(blackBoxFormArguments('vyg_1', '')).toEqual({ error: 'Choose a failed mission or type its id (msn_...).' });
    expect(blackBoxFormArguments('', 'Why?')).toHaveProperty('error');
  });
});

describe('black box entry screen insight sections', () => {
  async function renderEntry() {
    setMockParams({ id: 'bbx_1' });
    await renderScreen(<BlackBoxEntryRoute />);
    await waitFor(() => expect(screen.getByTestId('bbx-panel')).toBeTruthy());
  }

  it('pages the combined timeline and links rows to their entities', async () => {
    api.getBlackBoxTimeline
      .mockResolvedValueOnce({ ...pageOf(Array.from({ length: 10 }, (_, i) => timelineItem(i)), 12), entryId: 'bbx_1', sources: [], truncated: false })
      .mockResolvedValueOnce({ ...pageOf([timelineItem(10), timelineItem(11, { source: 'Harbor', entityId: null })], 12, 2), entryId: 'bbx_1', sources: [], truncated: false });
    await renderEntry();
    await waitFor(() => expect(screen.getByTestId('bbx-timeline-9')).toBeTruthy());
    expect(api.getBlackBoxTimeline).toHaveBeenCalledWith('bbx_1', { pageNumber: 1, pageSize: 10 });
    expect(screen.getByTestId('bbx-timeline-count')).toHaveTextContent('Showing 10 of 12');
    await act(async () => { await fireEvent.press(screen.getByTestId('bbx-timeline-more')); });
    await waitFor(() => expect(screen.getByTestId('bbx-timeline-11')).toBeTruthy());
    expect(api.getBlackBoxTimeline).toHaveBeenLastCalledWith('bbx_1', { pageNumber: 2, pageSize: 10 });
    expect(screen.queryByTestId('bbx-timeline-more')).toBeNull();
    await act(async () => { await fireEvent.press(screen.getByTestId('bbx-timeline-0')); });
    expect(mockRouter.push).toHaveBeenCalledWith('/missions/msn_1');
  });

  it('shows an empty timeline and no nearby successes', async () => {
    await renderEntry();
    await waitFor(() => expect(screen.getByTestId('bbx-timeline-empty')).toBeTruthy());
    expect(screen.getByTestId('bbx-nearby-empty')).toBeTruthy();
    expect(screen.queryByTestId('bbx-timeline-more')).toBeNull();
  });

  it('shows a timeline error with Retry', async () => {
    api.getBlackBoxTimeline
      .mockRejectedValueOnce(new ApiError('Timeline unavailable', 500, null))
      .mockResolvedValueOnce({ ...pageOf([timelineItem(1)], 1), entryId: 'bbx_1', sources: [], truncated: false });
    await renderEntry();
    await waitFor(() => expect(screen.getByTestId('bbx-timeline-error')).toHaveTextContent('Timeline unavailable'));
    expect(screen.queryByTestId('bbx-timeline-empty')).toBeNull();
    await act(async () => { await fireEvent.press(screen.getByTestId('bbx-timeline-retry')); });
    await waitFor(() => expect(screen.getByTestId('bbx-timeline-0')).toBeTruthy());
  });

  it('lists nearby successes linked to their missions', async () => {
    api.listBlackBoxNearbySuccesses.mockResolvedValue(pageOf([SUCCESS], 1) as never);
    await renderEntry();
    await waitFor(() => expect(screen.getByTestId('bbx-nearby-0')).toBeTruthy());
    expect(api.listBlackBoxNearbySuccesses).toHaveBeenCalledWith('bbx_1', { pageNumber: 1, pageSize: 10 });
    expect(screen.getByTestId('bbx-nearby-0')).toHaveTextContent(/Tidy the login form/);
    expect(screen.getByTestId('bbx-nearby-0')).toHaveTextContent(/-1.3h/);
    expect(screen.getByTestId('bbx-nearby-0')).toHaveTextContent(/2 overlapping file\(s\)/);
    await act(async () => { await fireEvent.press(screen.getByTestId('bbx-nearby-0')); });
    expect(mockRouter.push).toHaveBeenCalledWith('/missions/msn_ok');
  });

  it('a voyage entry has a timeline but no nearby successes', async () => {
    api.getBlackBox.mockResolvedValue(blackBoxEntry({ id: 'bbx_v', subjectType: 'Voyage', missionId: null, voyageId: 'vyg_1' }));
    await renderEntry();
    await waitFor(() => expect(screen.getByTestId('bbx-timeline')).toBeTruthy());
    expect(screen.queryByTestId('bbx-nearby')).toBeNull();
    expect(api.listBlackBoxNearbySuccesses).not.toHaveBeenCalled();
  });

  it('an entry that is not found shows no insight sections', async () => {
    api.getBlackBox.mockRejectedValue(new ApiError('No black box entry.', 404, { code: 'BlackBoxNotFound' }));
    setMockParams({ id: 'bbx_9' });
    await renderScreen(<BlackBoxEntryRoute />);
    await waitFor(() => expect(screen.getByText('Black box entry not found.')).toBeTruthy());
    expect(screen.queryByTestId('bbx-timeline')).toBeNull();
    expect(api.getBlackBoxTimeline).not.toHaveBeenCalled();
  });

  it('shows the linked runbook and the success rate on a recovery path', async () => {
    const entry = blackBoxEntry();
    const path = { ...entry.record!.analysis!.recoveryPaths![0], runbookId: 'rbk_1', runbookTitle: 'Clean a dirty checkout', successRate: 0.75, trackedRuns: 4 };
    api.getBlackBox.mockResolvedValue({ ...entry, record: { ...entry.record!, analysis: { ...entry.record!.analysis!, recoveryPaths: [path] } } });
    await renderEntry();
    const row = screen.getByTestId(`bbx-path-${path.id}`);
    expect(within(row).getByText('75% recovered (4 runs)')).toBeTruthy();
    await act(async () => { await fireEvent.press(within(row).getByTestId(`bbx-runbook-${path.id}`)); });
    expect(mockRouter.push).toHaveBeenCalledWith('/runbooks/rbk_1');
  });
});

describe('voyage cascade', () => {
  function failedVoyage() {
    api.getVoyage.mockResolvedValue({ id: 'vyg_1', title: 'Auth voyage', status: 'Failed', createdUtc: '2026-10-07T10:00:00Z' } as Voyage as never);
    api.listMissions.mockResolvedValue(page([]) as never);
  }

  it('lists each mission with status, failure kind, root marker and edges, linked to missions and entries', async () => {
    failedVoyage();
    api.getVoyageBlackBoxCascade.mockResolvedValue(GRAPH);
    await renderScreen(<VoyageDetail id="vyg_1" embedded />);
    await waitFor(() => expect(screen.getByTestId('voyage-cascade')).toBeTruthy());
    expect(api.getVoyageBlackBoxCascade).toHaveBeenCalledWith('vyg_1');
    const root = screen.getByTestId('cascade-node-msn_a');
    expect(within(root).getByText('Root cause')).toBeTruthy();
    expect(within(root).getByText('Merge Conflict')).toBeTruthy();
    expect(within(root).getByTestId('cascade-edge-msn_a-0')).toHaveTextContent('Wire the API was cancelled because this failed');
    expect(within(screen.getByTestId('cascade-node-msn_b')).getByTestId('cascade-edge-msn_b-0')).toHaveTextContent('Cancelled because Schema change failed');
    expect(screen.queryByTestId('cascade-entry-msn_c')).toBeNull();
    await act(async () => { await fireEvent.press(screen.getByTestId('cascade-edge-msn_b-0')); });
    expect(mockRouter.push).toHaveBeenLastCalledWith('/missions/msn_a');
    await act(async () => { await fireEvent.press(screen.getByTestId('cascade-open-msn_c')); });
    expect(mockRouter.push).toHaveBeenLastCalledWith('/missions/msn_c');
    await act(async () => { await fireEvent.press(screen.getByTestId('cascade-entry-msn_b')); });
    expect(mockRouter.push).toHaveBeenLastCalledWith('/blackbox/bbx_b');
  });

  it('is hidden when the graph cannot be read or has nothing to explain', async () => {
    failedVoyage();
    await renderScreen(<VoyageDetail id="vyg_1" embedded />);
    await waitFor(() => expect(api.getVoyageBlackBoxCascade).toHaveBeenCalled());
    expect(screen.queryByTestId('voyage-cascade')).toBeNull();
  });

  it('an open voyage with no failures does not read the cascade', async () => {
    api.getVoyage.mockResolvedValue({ id: 'vyg_1', title: 'Auth voyage', status: 'InProgress', createdUtc: '2026-10-07T10:00:00Z' } as Voyage as never);
    api.listMissions.mockResolvedValue(page([]) as never);
    await renderScreen(<VoyageDetail id="vyg_1" embedded />);
    await waitFor(() => expect(screen.getByTestId('voyage-detail-title')).toBeTruthy());
    expect(api.getVoyageBlackBoxCascade).not.toHaveBeenCalled();
  });

  it('notes a truncated graph', async () => {
    await renderScreen(<VoyageCascadeList graph={{ ...GRAPH, truncated: true }} />);
    expect(screen.getByTestId('voyage-cascade-truncated')).toHaveTextContent('Only the first 3 missions are shown.');
  });
});

describe('dispatch warnings', () => {
  beforeEach(() => { jest.useFakeTimers(); });
  afterEach(() => { jest.useRealTimers(); });

  async function renderDispatch(schedule: (fn: () => void, ms: number) => unknown = () => 0) {
    await renderScreen(<DispatchForm prefill={{ fromVessel: true, vesselId: 'vsl_a' } as never} schedule={schedule} />);
    await act(async () => { await Promise.resolve(); });
  }

  it('previews overlaps and lint after typing pauses, and never before', async () => {
    api.previewMissionOverlap.mockResolvedValue(PREVIEW_WITH_WARNINGS);
    await renderDispatch();
    expect(screen.queryByTestId('dispatch-overlap-preview')).toBeNull();
    await act(async () => { fireEvent.changeText(screen.getByTestId('dispatch-prompt'), 'Change src/auth.ts after mission 2 lands'); });
    expect(screen.getByTestId('dispatch-overlap-preview-loading')).toBeTruthy();
    expect(api.previewMissionOverlap).not.toHaveBeenCalled();
    await act(async () => { jest.advanceTimersByTime(OVERLAP_PREVIEW_DEBOUNCE_MS); });
    await act(async () => { await Promise.resolve(); });
    expect(api.previewMissionOverlap).toHaveBeenCalledTimes(1);
    expect(api.previewMissionOverlap).toHaveBeenCalledWith('vsl_a', { title: '', description: 'Change src/auth.ts after mission 2 lands' });
    expect(screen.getByTestId('dispatch-overlap-preview')).toHaveTextContent(/2 warning\(s\)/);
    expect(screen.getByTestId('dispatch-overlap-preview-list-overlap-0')).toHaveTextContent(/Overlaps msn_busy on 1 file/);
    expect(screen.getByTestId('dispatch-overlap-preview-list-lint-0')).toHaveTextContent(/BRIEF-DEP-001/);
    expect(screen.getByTestId('dispatch-overlap-preview-suggested')).toHaveTextContent('Consider making this mission depend on msn_busy.');
    await act(async () => { await fireEvent.press(screen.getByTestId('dispatch-overlap-preview-list-overlap-0')); });
    expect(mockRouter.push).toHaveBeenCalledWith('/missions/msn_busy');
  });

  it('says when the preview found nothing and when it failed', async () => {
    await renderDispatch();
    await act(async () => { fireEvent.changeText(screen.getByTestId('dispatch-prompt'), 'Fix the login bug'); });
    await act(async () => { jest.advanceTimersByTime(OVERLAP_PREVIEW_DEBOUNCE_MS); });
    await act(async () => { await Promise.resolve(); });
    expect(screen.getByTestId('dispatch-overlap-preview')).toHaveTextContent(/No warnings/);

    api.previewMissionOverlap.mockRejectedValueOnce(new ApiError('Vessel not found', 404, null));
    await act(async () => { fireEvent.changeText(screen.getByTestId('dispatch-prompt'), 'Fix the login bug now'); });
    await act(async () => { jest.advanceTimersByTime(OVERLAP_PREVIEW_DEBOUNCE_MS); });
    await act(async () => { await Promise.resolve(); });
    expect(screen.getByTestId('dispatch-overlap-preview-error')).toHaveTextContent('Preview unavailable: Vessel not found');
  });

  it('shows the warnings a dispatch returned before opening the voyage', async () => {
    api.createVoyage.mockResolvedValue({ id: 'vyg_9', overlapWarnings: PREVIEW_WITH_WARNINGS.warnings, briefLintWarnings: [] } as never);
    const scheduled: (() => void)[] = [];
    await renderDispatch((fn) => { scheduled.push(fn); });
    await act(async () => { fireEvent.changeText(screen.getByTestId('dispatch-prompt'), 'Fix the login bug'); });
    await act(async () => { await fireEvent.press(screen.getByTestId('dispatch-submit')); });
    await act(async () => { await Promise.resolve(); });
    expect(api.createVoyage).toHaveBeenCalled();
    expect(screen.getByTestId('dispatch-warnings-sheet-list-overlap-0')).toHaveTextContent(/Overlaps msn_busy/);
    expect(scheduled).toHaveLength(0);
    await act(async () => { await fireEvent.press(screen.getByTestId('dispatch-warnings-sheet-continue')); });
    expect(mockRouter.push).toHaveBeenCalledWith('/voyages/vyg_9');
  });

  it('a dispatch without warnings opens the voyage as before', async () => {
    api.createVoyage.mockResolvedValue({ id: 'vyg_9' } as never);
    const scheduled: (() => void)[] = [];
    await renderDispatch((fn) => { scheduled.push(fn); });
    await act(async () => { fireEvent.changeText(screen.getByTestId('dispatch-prompt'), 'Fix the login bug'); });
    await act(async () => { await fireEvent.press(screen.getByTestId('dispatch-submit')); });
    await act(async () => { await Promise.resolve(); });
    expect(screen.queryByTestId('dispatch-warnings-sheet-list')).toBeNull();
    expect(scheduled).toHaveLength(1);
  });
});

describe('Ask /blackbox', () => {
  function renderComposer(onQuickAction = jest.fn(async () => true)) {
    return renderScreen(
      <Composer
        quickActions={[BLACKBOX_ACTION]}
        turnActive={false}
        stopping={false}
        onStop={jest.fn()}
        onSend={jest.fn()}
        onQuickAction={onQuickAction}
        onLocalCommand={jest.fn(async () => ({ ok: true }))}
        actionBusy={false}
        onOpenImport={jest.fn()}
        noCaptain={false}
        showThinking={false}
        onShowThinkingChange={jest.fn()}
      />,
    ).then(() => onQuickAction);
  }

  it('choosing /blackbox from the menu opens the form, which lists recent failures and runs get_blackbox', async () => {
    api.listBlackBox.mockResolvedValue(page([blackBoxSummary('bbx_1', { missionId: 'msn_1', missionTitle: 'Login fix' })]) as never);
    const onQuickAction = await renderComposer();
    await act(async () => { fireEvent.changeText(screen.getByTestId('ask-input'), '/black'); });
    await act(async () => { await fireEvent.press(screen.getByTestId('ask-quick-blackbox')); });
    await waitFor(() => expect(screen.getByTestId('ask-blackbox-form')).toBeTruthy());
    expect(api.listBlackBox).toHaveBeenCalledWith(expect.objectContaining({ subjectType: 'Mission', isCurrent: true }));
    expect(onQuickAction).not.toHaveBeenCalled();

    await act(async () => { await fireEvent.press(screen.getByTestId('ask-blackbox-submit')); });
    expect(screen.getByText('Choose a failed mission or type its id (msn_...).')).toBeTruthy();
    expect(onQuickAction).not.toHaveBeenCalled();

    await act(async () => { fireEvent.changeText(screen.getByTestId('ask-blackbox-mission'), 'msn_1'); });
    await act(async () => { fireEvent.changeText(screen.getByTestId('ask-blackbox-question'), 'Why did it fail?'); });
    await act(async () => { await fireEvent.press(screen.getByTestId('ask-blackbox-submit')); });
    expect(onQuickAction).toHaveBeenCalledWith(BLACKBOX_ACTION, { missionId: 'msn_1', question: 'Why did it fail?' });
  });

  it('a typed /blackbox with a mission id runs at once', async () => {
    const onQuickAction = await renderComposer();
    await act(async () => { fireEvent.changeText(screen.getByTestId('ask-input'), '/blackbox msn_7 what went wrong'); });
    await act(async () => { await fireEvent.press(screen.getByTestId('ask-send')); });
    expect(onQuickAction).toHaveBeenCalledWith(BLACKBOX_ACTION, { missionId: 'msn_7', question: 'what went wrong' });
    expect(screen.queryByTestId('ask-blackbox-form')).toBeNull();
  });

  it('the form still works when recent failures cannot be listed', async () => {
    api.listBlackBox.mockRejectedValue(new ApiError('Forbidden', 403, null));
    const onQuickAction = await renderComposer();
    await act(async () => { fireEvent.changeText(screen.getByTestId('ask-input'), '/blackbox'); });
    await act(async () => { await fireEvent.press(screen.getByTestId('ask-send')); });
    await waitFor(() => expect(screen.getByTestId('ask-blackbox-form')).toBeTruthy());
    await act(async () => { fireEvent.changeText(screen.getByTestId('ask-blackbox-mission'), 'msn_2'); });
    await act(async () => { await fireEvent.press(screen.getByTestId('ask-blackbox-submit')); });
    expect(onQuickAction).toHaveBeenCalledWith(BLACKBOX_ACTION, { missionId: 'msn_2' });
  });
});
