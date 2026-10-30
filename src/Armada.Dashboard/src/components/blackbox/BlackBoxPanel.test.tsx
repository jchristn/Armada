import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import BlackBoxPanel from './BlackBoxPanel';
import {
  addBlackBoxNote,
  applyBlackBoxRecommendation,
  downloadBlackBox,
  getBlackBox,
  getBlackBoxBrief,
  getMissionBlackBox,
  getVoyageBlackBox,
  listBlackBoxNotes,
  listBlackBoxObjects,
  listMissionBlackBoxEntries,
  pinBlackBox,
  readBlackBoxObject,
  runBlackBoxRecovery,
  getBlackBoxTimeline,
  diffBlackBoxEntries,
  listBlackBoxNearbySuccesses,
} from '../../api/client';
import { browserPlatform } from '../../platform/browserPlatform';
import { translateTemplate } from '../../i18n/runtime';
import type { WebSocketMessage } from '../../types/models';
import { blackBoxAttemptsFixture, blackBoxEntryFixture, page } from '../../test/fixtures/blackbox';

vi.mock('../../api/client', () => ({
  addBlackBoxNote: vi.fn(),
  applyBlackBoxRecommendation: vi.fn(),
  downloadBlackBox: vi.fn(),
  getBlackBox: vi.fn(),
  getBlackBoxBrief: vi.fn(),
  getMissionBlackBox: vi.fn(),
  getVoyageBlackBox: vi.fn(),
  listBlackBoxNotes: vi.fn(),
  listBlackBoxObjects: vi.fn(),
  listMissionBlackBoxEntries: vi.fn(),
  pinBlackBox: vi.fn(),
  unpinBlackBox: vi.fn(),
  reanalyzeBlackBox: vi.fn(),
  readBlackBoxObject: vi.fn(),
  resolveBlackBox: vi.fn(),
  runBlackBoxRecovery: vi.fn(),
  getBlackBoxTimeline: vi.fn(),
  diffBlackBoxEntries: vi.fn(),
  listBlackBoxNearbySuccesses: vi.fn(),
}));

const localeValue = {
  t: (text: string, params?: Record<string, string | number | null | undefined>) => translateTemplate('en', text, null, params),
  locale: 'en',
  formatDateTime: (v: string | null | undefined) => v ?? '',
  formatRelativeTime: (v: string | null | undefined) => v ?? '',
};
const pushToast = vi.fn();
let socketListeners: Array<(m: WebSocketMessage) => void> = [];
vi.mock('../../context/LocaleContext', () => ({ useLocale: () => localeValue }));
vi.mock('../../context/NotificationContext', () => ({ useNotifications: () => ({ pushToast }) }));
vi.mock('../../context/AuthContext', () => ({ useAuth: () => ({ isAdmin: true, isTenantAdmin: true }) }));
// A stable socket like the real provider's (subscribe keeps its identity across renders).
const socket = {
  connected: true, reconnectCount: 0, send: vi.fn(),
  subscribe: (fn: (m: WebSocketMessage) => void) => { socketListeners.push(fn); return () => { socketListeners = socketListeners.filter((l) => l !== fn); }; },
};
vi.mock('../../context/WebSocketContext', () => ({ useWebSocket: () => socket }));

function apiError(status: number, code?: string) {
  return Object.assign(new Error(code ?? `HTTP ${status}`), { status, data: code ? { code } : null });
}

function WhereAmI() {
  const location = useLocation();
  return <div data-testid="where">{location.pathname}{location.search}|{JSON.stringify(location.state)}</div>;
}

function renderPanel(props: Partial<Parameters<typeof BlackBoxPanel>[0]> = { missionId: 'msn_1' }) {
  return render(
    <MemoryRouter initialEntries={['/missions/msn_1']}>
      <Routes>
        <Route path="/missions/:id" element={<BlackBoxPanel {...props} />} />
        <Route path="/ask" element={<WhereAmI />} />
      </Routes>
    </MemoryRouter>,
  );
}

const writeText = vi.fn(() => Promise.resolve());

beforeEach(() => {
  vi.clearAllMocks();
  socketListeners = [];
  Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true });
  vi.mocked(getMissionBlackBox).mockResolvedValue(blackBoxEntryFixture());
  vi.mocked(getBlackBoxBrief).mockResolvedValue('# FIX THIS\nbrief body');
  vi.mocked(listMissionBlackBoxEntries).mockResolvedValue(page(blackBoxAttemptsFixture()) as never);
  vi.mocked(listBlackBoxNotes).mockResolvedValue(page([]) as never);
  vi.mocked(listBlackBoxObjects).mockResolvedValue(page([]) as never);
});

describe('BlackBoxPanel sections', () => {
  it('renders every section from the record', async () => {
    renderPanel();
    expect(await screen.findByText('Another mission changed the same files before this one landed.')).toBeInTheDocument();
    const where = screen.getByTestId('blackbox-where');
    expect(within(where).getByText('Landing / LocalMerge')).toBeInTheDocument();
    expect(within(where).getByText('src/Armada.Core/Services/LandingService.cs:412')).toBeInTheDocument();
    expect(within(where).getByRole('link', { name: 'dck_1' })).toHaveAttribute('href', '/docks/dck_1');
    expect(within(screen.getByTestId('blackbox-how')).getByText(/GitMergeConflictException/)).toBeInTheDocument();
    expect(within(screen.getByTestId('blackbox-why')).getByText(/landed changes to src\/middleware\/request.ts first/)).toBeInTheDocument();
    const conflict = screen.getByTestId('blackbox-conflict');
    expect(within(conflict).getByRole('link', { name: 'Refactor middleware' })).toHaveAttribute('href', '/missions/msn_0');
    expect(within(conflict).getByText('src/logging.ts')).toBeInTheDocument();
    expect(within(conflict).getByRole('table', { name: 'Directories' })).toHaveTextContent('src/middleware');
    const config = screen.getByTestId('blackbox-configuration');
    expect(within(config).getByText('LocalMerge')).toBeInTheDocument();
    expect(within(config).getByText('Vessel')).toBeInTheDocument();
    expect(within(screen.getByTestId('blackbox-prevent')).getByText('PullRequest')).toBeInTheDocument();
    expect(within(screen.getByTestId('blackbox-improvements')).getByText(/Serialize missions/)).toBeInTheDocument();
    const recovery = screen.getByTestId('blackbox-recovery');
    const titles = within(recovery).getAllByText(/^\d\. /).map((n) => n.textContent);
    expect(titles).toEqual(['1. Retry the landing', '2. Reclaim the dock and redispatch']);
    expect(within(screen.getByTestId('blackbox-related')).getByRole('link', { name: 'bbx_9' })).toHaveAttribute('href', '/blackbox/bbx_9');
    expect(screen.getByRole('link', { name: 'Open full page' })).toHaveAttribute('href', '/blackbox/bbx_2');
  });

  it('shows the pending notice while the entry is only captured, and the sweeper notice', async () => {
    vi.mocked(getMissionBlackBox).mockResolvedValue(blackBoxEntryFixture({ state: 'Captured', captureMode: 'Sweeper', record: { entryId: 'bbx_2', schemaVersion: 1 } }));
    renderPanel();
    expect(await screen.findByTestId('blackbox-pending')).toBeInTheDocument();
    expect(screen.getByTestId('blackbox-sweeper')).toHaveTextContent(/volatile facts/);
    expect(within(screen.getByTestId('blackbox-recovery')).getByText('Waiting for the analysis.')).toBeInTheDocument();
  });

  it('says when no mechanism was recorded and shows the operator kind', async () => {
    vi.mocked(getMissionBlackBox).mockResolvedValue(blackBoxEntryFixture({
      phase: 'External', step: 'OperatorTransition', failureKind: 'OperatorAction',
      record: { entryId: 'bbx_2', schemaVersion: 1, mechanism: {}, classification: { kind: 'OperatorAction' } } as never,
    }));
    renderPanel();
    const how = await screen.findByTestId('blackbox-how');
    expect(within(how).getByText('No mechanism was recorded for this failure.')).toBeInTheDocument();
    expect(screen.getAllByText('OperatorAction').length).toBeGreaterThan(0);
  });

  it('does not say no mechanism when one was recorded', async () => {
    renderPanel();
    const how = await screen.findByTestId('blackbox-how');
    expect(within(how).queryByText('No mechanism was recorded for this failure.')).not.toBeInTheDocument();
  });

  it('does not show the pending notice once analyzed', async () => {
    renderPanel();
    await screen.findByTestId('blackbox-summary');
    expect(screen.queryByTestId('blackbox-pending')).not.toBeInTheDocument();
    expect(screen.queryByTestId('blackbox-sweeper')).not.toBeInTheDocument();
  });

  it('explains an ObjectMissing entry', async () => {
    vi.mocked(getMissionBlackBox).mockResolvedValue(blackBoxEntryFixture({ state: 'ObjectMissing' }));
    renderPanel();
    expect(await screen.findByText(/missing from the store \(ObjectMissing\)/)).toBeInTheDocument();
  });
});

describe('BlackBoxPanel empty, error and 404 states', () => {
  it('shows the recorded reason when the mission has no entry (404)', async () => {
    vi.mocked(getMissionBlackBox).mockRejectedValue(apiError(404, 'BlackBoxNotFound'));
    renderPanel({ missionId: 'msn_1', fallbackReason: 'CLI is not installed.', fallbackKind: 'Infra' });
    expect(await screen.findByText(/No black box entry was recorded/)).toBeInTheDocument();
    expect(document.querySelector('pre.mission-failure-reason')).toHaveTextContent('CLI is not installed.');
    expect(document.querySelector('pre.mission-failure-reason')).toHaveAttribute('data-failure-kind', 'Infra');
  });

  it('renders nothing for a 404 with no reason on an embedded panel', async () => {
    vi.mocked(getVoyageBlackBox).mockRejectedValue(apiError(404, 'BlackBoxNotFound'));
    const { container } = renderPanel({ voyageId: 'vyg_1' });
    await waitFor(() => expect(getVoyageBlackBox).toHaveBeenCalled());
    await waitFor(() => expect(container.querySelector('.blackbox-panel')).toBeNull());
  });

  it('shows not found on the full page view', async () => {
    vi.mocked(getBlackBox).mockRejectedValue(apiError(404, 'BlackBoxNotFound'));
    renderPanel({ entryId: 'bbx_missing', variant: 'page' });
    expect(await screen.findByText(/Black box entry not found/)).toBeInTheDocument();
  });

  it('shows the error with a retry that loads again', async () => {
    vi.mocked(getMissionBlackBox).mockRejectedValueOnce(apiError(503, 'BlackBoxStoreUnavailable'));
    renderPanel();
    expect(await screen.findByText(/could not be loaded/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }));
    expect(await screen.findByTestId('blackbox-summary')).toBeInTheDocument();
  });

  it('shows the loading state first', () => {
    vi.mocked(getMissionBlackBox).mockReturnValue(new Promise(() => {}));
    renderPanel();
    expect(document.querySelector('.blackbox-panel[aria-busy="true"]')).not.toBeNull();
  });
});

describe('BlackBoxPanel actions', () => {
  it('copies the single FIX THIS brief', async () => {
    renderPanel();
    await screen.findByTestId('blackbox-summary');
    await waitFor(() => expect(getBlackBoxBrief).toHaveBeenCalledWith('bbx_2'));
    fireEvent.click(screen.getByRole('button', { name: 'Copy FIX THIS' }));
    await waitFor(() => expect(writeText).toHaveBeenCalledWith('# FIX THIS\nbrief body'));
    expect(pushToast).toHaveBeenCalledWith('success', expect.stringContaining('FIX THIS brief copied'));
  });

  it('has no audience toggle and fetches the brief without an audience', async () => {
    renderPanel();
    await screen.findByTestId('blackbox-summary');
    for (const name of ['User', 'Agent', 'Maintainer']) expect(screen.queryByRole('button', { name })).not.toBeInTheDocument();
    expect(screen.queryByRole('group', { name: 'Brief audience' })).not.toBeInTheDocument();
    await waitFor(() => expect(getBlackBoxBrief).toHaveBeenCalled());
    for (const call of vi.mocked(getBlackBoxBrief).mock.calls) expect(call).toEqual(['bbx_2']);
    // The single brief view shows the stack frames the old User audience hid.
    expect(screen.getByLabelText('Stack')).toHaveTextContent('LandingService.MergeAsync');
  });

  it('reports a brief that cannot be copied', async () => {
    vi.mocked(getBlackBoxBrief).mockRejectedValue(new Error('boom'));
    renderPanel();
    await screen.findByTestId('blackbox-summary');
    fireEvent.click(screen.getByRole('button', { name: 'Copy FIX THIS' }));
    await waitFor(() => expect(pushToast).toHaveBeenCalledWith('error', expect.stringContaining('boom')));
    expect(writeText).not.toHaveBeenCalled();
  });

  it('downloads the brief as .md and the entry as .zip', async () => {
    const save = vi.spyOn(browserPlatform, 'saveFile').mockResolvedValue();
    renderPanel();
    await screen.findByTestId('blackbox-summary');
    fireEvent.click(screen.getByRole('button', { name: 'Download .md' }));
    await waitFor(() => expect(save).toHaveBeenCalledWith(expect.objectContaining({ filename: 'blackbox-bbx_2.md', contentType: 'text/markdown' })));
    fireEvent.click(screen.getByRole('button', { name: 'Download .zip' }));
    await waitFor(() => expect(downloadBlackBox).toHaveBeenCalledWith('bbx_2'));
  });

  it('reports a failed .zip download', async () => {
    vi.mocked(downloadBlackBox).mockRejectedValue(new Error('no adapter'));
    renderPanel();
    await screen.findByTestId('blackbox-summary');
    fireEvent.click(screen.getByRole('button', { name: 'Download .zip' }));
    await waitFor(() => expect(pushToast).toHaveBeenCalledWith('error', expect.stringContaining('no adapter')));
  });

  it('shows the JSON record', async () => {
    renderPanel();
    await screen.findByTestId('blackbox-summary');
    fireEvent.click(screen.getByRole('button', { name: 'View JSON' }));
    expect(document.querySelector('.json-viewer-body pre')).toHaveTextContent('"entryId": "bbx_2"');
  });

  it('opens Ask Armada with the prompt in the URL and in router state', async () => {
    renderPanel();
    await screen.findByTestId('blackbox-summary');
    fireEvent.click(screen.getByRole('button', { name: 'Ask Armada about this failure' }));
    const prompt = 'Show me the pertinent details from the black box for mission msn_1 and give me your opinion on why it failed and what could have been done differently';
    const where = await screen.findByTestId('where');
    expect(where.textContent).toContain(`/ask?prompt=${encodeURIComponent(prompt)}`);
    expect(where.textContent).toContain(`"draft":"${prompt}"`);
  });

  it('switches between attempts', async () => {
    vi.mocked(getBlackBox).mockResolvedValue(blackBoxEntryFixture({ id: 'bbx_1', attempt: 1, isCurrent: false, summary: 'First attempt failed its tests.', record: null }));
    renderPanel();
    await screen.findByTestId('blackbox-summary');
    const group = screen.getByRole('group', { name: 'Attempts' });
    expect(within(group).getByRole('button', { name: 'Attempt 2' })).toHaveAttribute('aria-pressed', 'true');
    fireEvent.click(within(group).getByRole('button', { name: 'Attempt 1' }));
    await waitFor(() => expect(getBlackBox).toHaveBeenCalledWith('bbx_1'));
    expect(await screen.findByText('First attempt failed its tests.')).toBeInTheDocument();
    expect(screen.getByText('Superseded')).toBeInTheDocument();
  });

  it('pins the entry', async () => {
    vi.mocked(pinBlackBox).mockResolvedValue(blackBoxEntryFixture({ pinned: true, record: null }));
    renderPanel();
    await screen.findByTestId('blackbox-summary');
    fireEvent.click(screen.getByRole('button', { name: 'Pin' }));
    await waitFor(() => expect(pinBlackBox).toHaveBeenCalledWith('bbx_2'));
    expect(await screen.findByRole('button', { name: 'Unpin' })).toBeInTheDocument();
    // The record is kept when the pin response omits it.
    expect(screen.getByTestId('blackbox-conflict')).toBeInTheDocument();
  });

  it('reloads when a black box event names the mission', async () => {
    renderPanel();
    await screen.findByTestId('blackbox-summary');
    expect(getMissionBlackBox).toHaveBeenCalledTimes(1);
    vi.mocked(getMissionBlackBox).mockResolvedValue(blackBoxEntryFixture({ resolution: 'Resolved' }));
    act(() => { socketListeners.forEach((l) => l({ type: 'blackbox.resolved', data: { entryId: 'bbx_2', missionId: 'msn_1', resolution: 'Resolved' } })); });
    await waitFor(() => expect(getMissionBlackBox).toHaveBeenCalledTimes(2));
    expect(await screen.findAllByText('Resolved')).not.toHaveLength(0);
  });

  it('ignores events about other missions and other event types', async () => {
    renderPanel();
    await screen.findByTestId('blackbox-summary');
    act(() => {
      socketListeners.forEach((l) => l({ type: 'blackbox.captured', data: { entryId: 'bbx_77', missionId: 'msn_other' } }));
      socketListeners.forEach((l) => l({ type: 'mission.updated', data: { missionId: 'msn_1' } }));
    });
    await new Promise((r) => setTimeout(r, 400));
    expect(getMissionBlackBox).toHaveBeenCalledTimes(1);
  });
});

describe('BlackBoxPanel recovery', () => {
  const plan = { entryId: 'bbx_2', pathId: 'rp1', dryRun: true, succeeded: true, confirmToken: 'bbc_1', confirmTokenExpiresUtc: '2026-10-28T10:15:00Z', plan: [{ order: 1, description: 'Retry landing', action: { tool: 'retry_landing', arguments: { missionId: 'msn_1' } } }] };

  async function openDryRun(title: RegExp) {
    renderPanel();
    await screen.findByTestId('blackbox-recovery');
    const path = screen.getByText(title).closest('.blackbox-path') as HTMLElement;
    fireEvent.click(within(path).getByRole('button', { name: 'Dry run...' }));
    return screen.findByRole('dialog');
  }

  it('dry runs first, then runs with the confirm token as Operator', async () => {
    vi.mocked(runBlackBoxRecovery)
      .mockResolvedValueOnce(plan)
      .mockResolvedValueOnce({ ...plan, dryRun: false, confirmToken: null, stepResults: [{ order: 1, tool: 'retry_landing', succeeded: true, message: 'Landed' }] });
    const dialog = await openDryRun(/Retry the landing/);
    expect(runBlackBoxRecovery).toHaveBeenCalledWith('bbx_2', 'rp1', { dryRun: true, authorKind: 'Operator' });
    expect(await within(dialog).findByText('Retry landing')).toBeInTheDocument();
    expect(within(dialog).queryByRole('checkbox')).not.toBeInTheDocument();
    fireEvent.click(within(dialog).getByRole('button', { name: 'Confirm and run' }));
    await waitFor(() => expect(runBlackBoxRecovery).toHaveBeenLastCalledWith('bbx_2', 'rp1', { dryRun: false, confirmToken: 'bbc_1', acknowledgeDataLoss: false, authorKind: 'Operator' }));
    expect(await within(dialog).findByText('Succeeded')).toBeInTheDocument();
    expect(within(dialog).getByText(/Landed/)).toBeInTheDocument();
  });

  it('requires the data loss acknowledgement for a destructive path', async () => {
    vi.mocked(runBlackBoxRecovery)
      .mockResolvedValueOnce({ ...plan, pathId: 'rp2', plan: [{ order: 1, description: 'Delete the dock', dataLossRisk: true, action: { tool: 'delete_dock', arguments: { dockId: 'dck_1' } } }] })
      .mockResolvedValueOnce({ ...plan, pathId: 'rp2', dryRun: false, stepResults: [] });
    const dialog = await openDryRun(/Reclaim the dock/);
    const confirm = await within(dialog).findByRole('button', { name: 'Confirm and run' });
    expect(confirm).toBeDisabled();
    fireEvent.click(within(dialog).getByRole('checkbox', { name: /can delete work/ }));
    expect(confirm).toBeEnabled();
    fireEvent.click(confirm);
    await waitFor(() => expect(runBlackBoxRecovery).toHaveBeenLastCalledWith('bbx_2', 'rp2', { dryRun: false, confirmToken: 'bbc_1', acknowledgeDataLoss: true, authorKind: 'Operator' }));
  });

  it('explains a refused confirmation and offers a new dry run', async () => {
    vi.mocked(runBlackBoxRecovery)
      .mockResolvedValueOnce(plan)
      .mockRejectedValueOnce(apiError(409, 'BlackBoxConfirmationRequired'))
      .mockResolvedValueOnce(plan);
    const dialog = await openDryRun(/Retry the landing/);
    fireEvent.click(await within(dialog).findByRole('button', { name: 'Confirm and run' }));
    expect(await within(dialog).findByText(/Run the dry run again/)).toBeInTheDocument();
    fireEvent.click(within(dialog).getByRole('button', { name: 'Dry run again' }));
    await waitFor(() => expect(runBlackBoxRecovery).toHaveBeenCalledTimes(3));
    expect(vi.mocked(runBlackBoxRecovery).mock.calls[2][2]).toEqual({ dryRun: true, authorKind: 'Operator' });
  });

  it('explains a refused dry run (no permission) and a server without the runner', async () => {
    vi.mocked(runBlackBoxRecovery).mockRejectedValueOnce(apiError(403));
    const dialog = await openDryRun(/Retry the landing/);
    expect(await within(dialog).findByText(/not allowed to run this action/)).toBeInTheDocument();
    expect(within(dialog).queryByRole('button', { name: 'Confirm and run' })).not.toBeInTheDocument();
    vi.mocked(runBlackBoxRecovery).mockRejectedValueOnce(apiError(503, 'BlackBoxActionsUnavailable'));
    fireEvent.click(within(dialog).getByRole('button', { name: 'Dry run again' }));
    expect(await within(dialog).findByText(/cannot run recovery actions yet/)).toBeInTheDocument();
  });

  it('applies a configuration recommendation after a dry run', async () => {
    vi.mocked(applyBlackBoxRecommendation)
      .mockResolvedValueOnce({ entryId: 'bbx_2', recommendationId: 'rec_1', dryRun: true, succeeded: true, confirmToken: 'bbc_r', action: { tool: 'update_vessel', arguments: { landingMode: 'PullRequest' } } })
      .mockResolvedValueOnce({ entryId: 'bbx_2', recommendationId: 'rec_1', dryRun: false, succeeded: true });
    renderPanel();
    const prevent = await screen.findByTestId('blackbox-prevent');
    fireEvent.click(within(prevent).getByRole('button', { name: 'Apply...' }));
    const dialog = await screen.findByRole('dialog');
    expect(await within(dialog).findByText(/update_vessel/)).toBeInTheDocument();
    fireEvent.click(within(dialog).getByRole('button', { name: 'Confirm and run' }));
    await waitFor(() => expect(applyBlackBoxRecommendation).toHaveBeenLastCalledWith('bbx_2', 'rec_1', { dryRun: false, confirmToken: 'bbc_r', authorKind: 'Operator' }));
  });
});

describe('BlackBoxPanel evidence and notes', () => {
  it('reads a large object in ranges', async () => {
    vi.mocked(listBlackBoxObjects).mockResolvedValue(page([{ name: 'evidence/mission.log', contentType: 'text/plain', contentEncoding: 'gzip', sizeBytes: 100, uncompressedBytes: 131072, isMissing: false, createdUtc: '2026-10-28T10:00:00Z' }]) as never);
    vi.mocked(readBlackBoxObject)
      .mockResolvedValueOnce({ contentType: 'text/plain', etag: null, offset: 0, totalBytes: 131072, nextOffset: 65536, text: 'first chunk ', body: new Blob(['x'.repeat(65536)]) })
      .mockResolvedValueOnce({ contentType: 'text/plain', etag: null, offset: 65536, totalBytes: 131072, nextOffset: null, text: 'second chunk', body: new Blob(['x'.repeat(65536)]) });
    renderPanel();
    await screen.findByTestId('blackbox-summary');
    fireEvent.click(screen.getByText('Evidence'));
    fireEvent.click(await screen.findByRole('button', { name: 'View' }));
    expect(readBlackBoxObject).toHaveBeenCalledWith('bbx_2', 'evidence/mission.log', { offset: 0, length: 65536 });
    const dialog = await screen.findByRole('dialog');
    expect(await within(dialog).findByText('first chunk')).toBeInTheDocument();
    fireEvent.click(within(dialog).getByRole('button', { name: 'Load more' }));
    await waitFor(() => expect(readBlackBoxObject).toHaveBeenLastCalledWith('bbx_2', 'evidence/mission.log', { offset: 65536, length: 65536 }));
    expect(await within(dialog).findByText('first chunk second chunk')).toBeInTheDocument();
    expect(within(dialog).queryByRole('button', { name: 'Load more' })).not.toBeInTheDocument();
  });

  it('explains an object the store lost (410 ObjectMissing)', async () => {
    vi.mocked(listBlackBoxObjects).mockResolvedValue(page([{ name: 'evidence/diff.patch', contentType: 'text/x-diff', contentEncoding: 'gzip', sizeBytes: 10, uncompressedBytes: 10, isMissing: true, createdUtc: '2026-10-28T10:00:00Z' }]) as never);
    vi.mocked(readBlackBoxObject).mockRejectedValue(apiError(410, 'BlackBoxObjectMissing'));
    renderPanel();
    await screen.findByTestId('blackbox-summary');
    fireEvent.click(screen.getByText('Evidence'));
    expect(await screen.findByText('Missing')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'View' }));
    expect(await screen.findByText(/missing from the black box store \(ObjectMissing\)/)).toBeInTheDocument();
  });

  it('lists notes and adds one', async () => {
    vi.mocked(listBlackBoxNotes).mockResolvedValueOnce(page([{ id: 'bbn_1', entryId: 'bbx_2', authorKind: 'Agent', body: 'Tried a rebase.', createdUtc: '2026-10-28T11:00:00Z' }]) as never)
      .mockResolvedValue(page([{ id: 'bbn_2', entryId: 'bbx_2', authorKind: 'Operator', body: 'Fixed by hand.', createdUtc: '2026-10-28T12:00:00Z' }]) as never);
    vi.mocked(addBlackBoxNote).mockResolvedValue({ id: 'bbn_2', entryId: 'bbx_2', authorKind: 'Operator', body: 'Fixed by hand.', createdUtc: '' });
    renderPanel();
    await screen.findByTestId('blackbox-summary');
    fireEvent.click(screen.getByText('Notes'));
    expect(await screen.findByText('Tried a rebase.')).toBeInTheDocument();
    fireEvent.change(screen.getByRole('textbox', { name: 'New note' }), { target: { value: 'Fixed by hand.' } });
    fireEvent.click(screen.getByRole('button', { name: 'Add note' }));
    await waitFor(() => expect(addBlackBoxNote).toHaveBeenCalledWith('bbx_2', 'Fixed by hand.'));
    expect(await screen.findByText('Fixed by hand.')).toBeInTheDocument();
  });
});

describe('BlackBoxPanel insight sections', () => {
  it('shows the runbook a path starts and its success rate', async () => {
    const entry = blackBoxEntryFixture();
    const paths = entry.record!.analysis!.recoveryPaths!.map((p) => p.id === 'rp1' ? { ...p, runbookId: 'rbk_1', runbookTitle: 'Landing conflict runbook', successRate: 0.8, trackedRuns: 5 } : p);
    vi.mocked(getMissionBlackBox).mockResolvedValue({ ...entry, record: { ...entry.record!, analysis: { ...entry.record!.analysis!, recoveryPaths: paths } } });
    renderPanel();
    const recovery = await screen.findByTestId('blackbox-recovery');
    expect(within(recovery).getByRole('link', { name: 'Landing conflict runbook' })).toHaveAttribute('href', '/runbooks/rbk_1');
    expect(within(recovery).getByText('Success rate 80% over 5 tracked runs')).toBeInTheDocument();
    expect(within(recovery).getAllByText(/Runs runbook/)).toHaveLength(1);
  });

  it('loads the timeline, attempt diff and nearby successes only when opened', async () => {
    vi.mocked(getBlackBoxTimeline).mockResolvedValue({ ...page([{ timestampUtc: '2026-10-28T10:00:00Z', source: 'Entry', kind: 'captured', message: 'Black box captured' }]), entryId: 'bbx_2', sources: [], truncated: false } as never);
    vi.mocked(diffBlackBoxEntries).mockResolvedValue({ entryId: 'bbx_2', otherEntryId: 'bbx_1', sameMission: true, attempt: 2, otherAttempt: 1, sameFingerprint: false, differences: [{ section: 'Subject', field: 'Model', value: 'b', otherValue: 'a' }], differenceCount: 1, filesOnlyInEntry: [], filesOnlyInEntryCount: 0, filesOnlyInOther: [], filesOnlyInOtherCount: 0, filesInBothCount: 0, changedFilesKnown: true });
    vi.mocked(listBlackBoxNearbySuccesses).mockResolvedValue(page([]) as never);
    renderPanel();
    await screen.findByTestId('blackbox-recovery');
    expect(getBlackBoxTimeline).not.toHaveBeenCalled();
    fireEvent.click(screen.getByText('Timeline'));
    expect(await screen.findByText('Black box captured')).toBeInTheDocument();
    fireEvent.click(screen.getByText('Attempt diff'));
    expect(await screen.findByRole('table', { name: 'Differences' })).toBeInTheDocument();
    fireEvent.click(screen.getByText('Nearby successes'));
    expect(await screen.findByText('No nearby successes')).toBeInTheDocument();
  });
});
