import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import BlackBox from './BlackBox';
import BlackBoxEntry from './BlackBoxEntry';
import { BlackBoxRedirect } from '../components/LegacyNavRedirects';
import {
  enumerateBlackBox,
  enumerateBlackBoxFingerprints,
  enumerateBlackBoxHotspots,
  getBlackBox,
  getBlackBoxFingerprintTrend,
  getBlackBoxRetention,
  getBlackBoxStorage,
  listVessels,
  purgeBlackBox,
  runBlackBoxGc,
  searchBlackBox,
  getBlackBoxRecoveryStats,
} from '../api/client';
import { translateTemplate } from '../i18n/runtime';
import { BLACKBOX_SAVED_FILTERS_KEY } from '../components/blackbox/savedFilters';
import type { BlackBoxGcReport, BlackBoxRetentionStatus } from '../types/blackbox';
import type { WebSocketMessage } from '../types/models';
import { blackBoxAttemptsFixture, page } from '../test/fixtures/blackbox';

vi.mock('../api/client', () => ({
  enumerateBlackBox: vi.fn(),
  enumerateBlackBoxFingerprints: vi.fn(),
  enumerateBlackBoxHotspots: vi.fn(),
  getBlackBox: vi.fn(),
  getBlackBoxBrief: vi.fn(() => Promise.resolve('')),
  getBlackBoxFingerprintTrend: vi.fn(),
  getBlackBoxRetention: vi.fn(),
  getBlackBoxStorage: vi.fn(),
  listMissionBlackBoxEntries: vi.fn(() => Promise.resolve({ objects: [] })),
  listVessels: vi.fn(),
  purgeBlackBox: vi.fn(),
  runBlackBoxGc: vi.fn(),
  searchBlackBox: vi.fn(),
  getBlackBoxRecoveryStats: vi.fn(),
}));

const localeValue = {
  t: (text: string, params?: Record<string, string | number | null | undefined>) => translateTemplate('en', text, null, params),
  locale: 'en',
  formatDateTime: (v: string | null | undefined) => v ?? '',
  formatRelativeTime: (v: string | null | undefined) => v ?? '',
};
const auth = { isAdmin: true, isTenantAdmin: true };
const pushToast = vi.fn();
let socketListeners: Array<(m: WebSocketMessage) => void> = [];
const socket = {
  connected: true, reconnectCount: 0, send: vi.fn(),
  subscribe: (fn: (m: WebSocketMessage) => void) => { socketListeners.push(fn); return () => { socketListeners = socketListeners.filter((l) => l !== fn); }; },
};
vi.mock('../context/LocaleContext', () => ({ useLocale: () => localeValue }));
vi.mock('../context/NotificationContext', () => ({ useNotifications: () => ({ pushToast }) }));
vi.mock('../context/AuthContext', () => ({ useAuth: () => auth }));
vi.mock('../context/WebSocketContext', () => ({ useWebSocket: () => socket }));

const gcReport: BlackBoxGcReport = { dryRun: true, startedUtc: '2026-10-28T00:00:00Z', expired: 3, evicted: 1, cascaded: 0, retriedDeletes: 0, orphansRemoved: 0, objectsMissing: 0, bytesFreed: 2048, failures: 0, candidateEntryIds: ['bbx_old1', 'bbx_old2'] };
const retention: BlackBoxRetentionStatus = {
  policy: { retentionDays: 180, resolvedRetentionDays: 30, keepLatestPerMission: true, maxEntriesPerVessel: 1000, maxTotalBytes: 1073741824, deleteWithMission: false, deleteWithVessel: true, gcIntervalMinutes: 60, gcBatchSize: 200, orphanGraceHours: 24 },
  totalEntries: 42, pinnedEntries: 2, deletingEntries: 0, expiredEntries: 3, totalBytes: 1048576, nextRunUtc: '2026-10-28T01:00:00Z', lastRun: { ...gcReport, dryRun: false },
};

function renderAt(url: string) {
  return render(
    <MemoryRouter initialEntries={[url]}>
      <Routes>
        <Route path="/missions" element={<BlackBox />} />
        <Route path="/blackbox" element={<BlackBoxRedirect />} />
        <Route path="/blackbox/:id" element={<BlackBoxEntry />} />
      </Routes>
    </MemoryRouter>,
  );
}

function apiError(status: number, code?: string) {
  return Object.assign(new Error(code ?? `HTTP ${status}`), { status, data: code ? { code } : null });
}

beforeEach(() => {
  vi.clearAllMocks();
  socketListeners = [];
  auth.isAdmin = true;
  auth.isTenantAdmin = true;
  localStorage.clear();
  vi.mocked(listVessels).mockResolvedValue(page([{ id: 'vsl_1', name: 'api-server' }]) as never);
  vi.mocked(enumerateBlackBox).mockResolvedValue(page(blackBoxAttemptsFixture(), { totalPages: 3, totalRecords: 60 }) as never);
  vi.mocked(searchBlackBox).mockResolvedValue(page([]) as never);
});

describe('Black Box page: entries', () => {
  it('lists entries with the ID column first and links to each entry', async () => {
    renderAt('/blackbox');
    const link = await screen.findByRole('link', { name: 'bbx_1' });
    expect(link).toHaveAttribute('href', '/blackbox/bbx_1');
    const headers = screen.getAllByRole('columnheader').map((h) => h.textContent);
    expect(headers[0]).toContain('ID');
    expect(enumerateBlackBox).toHaveBeenCalledWith({ pageNumber: 1, pageSize: 25 });
  });

  it('sends every applied filter, converting dates to UTC', async () => {
    renderAt('/blackbox');
    await screen.findByRole('link', { name: 'bbx_1' });
    await waitFor(() => expect(screen.getByRole('option', { name: 'api-server' })).toBeInTheDocument());
    const filters = screen.getByRole('search', { name: 'Black box filters' });
    fireEvent.change(within(filters).getByLabelText('Vessel'), { target: { value: 'vsl_1' } });
    fireEvent.change(within(filters).getByLabelText('Mission ID'), { target: { value: 'msn_1' } });
    fireEvent.change(within(filters).getByLabelText('Phase'), { target: { value: 'Landing' } });
    fireEvent.change(within(filters).getByLabelText('Step'), { target: { value: 'LocalMerge' } });
    fireEvent.change(within(filters).getByLabelText('Kind'), { target: { value: 'LandingConflict' } });
    fireEvent.change(within(filters).getByLabelText('Triage'), { target: { value: 'UserConfig' } });
    fireEvent.change(within(filters).getByLabelText('Resolution'), { target: { value: 'Open' } });
    fireEvent.change(within(filters).getByLabelText('Pinned'), { target: { value: 'false' } });
    fireEvent.change(within(filters).getByLabelText('Current attempt'), { target: { value: 'true' } });
    fireEvent.change(within(filters).getByLabelText('File path'), { target: { value: 'src/' } });
    fireEvent.change(within(filters).getByLabelText('Created after'), { target: { value: '2026-10-01' } });
    fireEvent.change(within(filters).getByLabelText('Created before'), { target: { value: '2026-10-31' } });
    fireEvent.click(screen.getByRole('button', { name: 'Apply filters' }));
    await waitFor(() => expect(enumerateBlackBox).toHaveBeenLastCalledWith({
      vesselId: 'vsl_1', missionId: 'msn_1', phase: 'Landing', step: 'LocalMerge', kind: 'LandingConflict', triage: 'UserConfig',
      resolution: 'Open', pinned: false, isCurrent: true, path: 'src/',
      createdAfter: '2026-10-01T00:00:00Z', createdBefore: '2026-10-31T23:59:59Z', pageNumber: 1, pageSize: 25,
    }));
  });

  it('uses full-text search when a search term is set', async () => {
    renderAt('/blackbox');
    await screen.findByRole('link', { name: 'bbx_1' });
    fireEvent.change(screen.getByLabelText('Search'), { target: { value: 'conflict' } });
    fireEvent.click(screen.getByRole('button', { name: 'Apply filters' }));
    await waitFor(() => expect(searchBlackBox).toHaveBeenCalledWith({ query: 'conflict', pageNumber: 1, pageSize: 25 }));
    expect(await screen.findByText('No black box entries')).toBeInTheDocument();
    expect(screen.getByText('No entry matches these filters.')).toBeInTheDocument();
  });

  it('pages through the entries', async () => {
    renderAt('/blackbox');
    await screen.findByRole('link', { name: 'bbx_1' });
    fireEvent.click(screen.getAllByRole('button', { name: /next/i })[0]);
    await waitFor(() => expect(enumerateBlackBox).toHaveBeenLastCalledWith({ pageNumber: 2, pageSize: 25 }));
  });

  it('reads the fingerprint filter from the URL', async () => {
    renderAt('/missions?tab=blackbox&bbtab=entries&fingerprint=fp_1');
    await waitFor(() => expect(enumerateBlackBox).toHaveBeenCalledWith({ fingerprint: 'fp_1', pageNumber: 1, pageSize: 25 }));
  });

  it('saves filters in this browser and applies them', async () => {
    renderAt('/blackbox');
    await screen.findByRole('link', { name: 'bbx_1' });
    fireEvent.change(screen.getByLabelText('Triage'), { target: { value: 'ArmadaDefect' } });
    fireEvent.change(screen.getByLabelText('Name for these filters'), { target: { value: 'Armada bugs' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save filters' }));
    expect(JSON.parse(localStorage.getItem(BLACKBOX_SAVED_FILTERS_KEY) ?? '[]')).toEqual([{ name: 'Armada bugs', query: { triage: 'ArmadaDefect' } }]);
    fireEvent.click(screen.getByRole('button', { name: 'Clear' }));
    await waitFor(() => expect(enumerateBlackBox).toHaveBeenLastCalledWith({ pageNumber: 1, pageSize: 25 }));
    fireEvent.change(screen.getByLabelText('Saved filters'), { target: { value: 'Armada bugs' } });
    await waitFor(() => expect(enumerateBlackBox).toHaveBeenLastCalledWith({ triage: 'ArmadaDefect', pageNumber: 1, pageSize: 25 }));
    fireEvent.click(screen.getByRole('button', { name: 'Delete the saved filters Armada bugs.' }));
    expect(JSON.parse(localStorage.getItem(BLACKBOX_SAVED_FILTERS_KEY) ?? '[]')).toEqual([]);
  });

  it('survives unreadable or unavailable saved-filter storage', async () => {
    localStorage.setItem(BLACKBOX_SAVED_FILTERS_KEY, '{not json');
    const setItem = vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('quota'); });
    renderAt('/blackbox');
    await screen.findByRole('link', { name: 'bbx_1' });
    expect(screen.getByRole('option', { name: 'Saved filters (0)' })).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText('Name for these filters'), { target: { value: 'x' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save filters' }));
    expect(screen.getByRole('option', { name: 'Saved filters (1)' })).toBeInTheDocument();
    setItem.mockRestore();
  });

  it('shows an error with retry', async () => {
    vi.mocked(enumerateBlackBox).mockRejectedValueOnce(new Error('offline'));
    renderAt('/blackbox');
    expect(await screen.findByText(/could not be loaded: offline/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }));
    expect(await screen.findByRole('link', { name: 'bbx_1' })).toBeInTheDocument();
  });

  it('shows the empty state', async () => {
    vi.mocked(enumerateBlackBox).mockResolvedValue(page([]) as never);
    renderAt('/blackbox');
    expect(await screen.findByText('No black box entries')).toBeInTheDocument();
    expect(screen.getByText(/whenever a mission or voyage fails/)).toBeInTheDocument();
  });

  it('reloads on a black box event', async () => {
    renderAt('/blackbox');
    await screen.findByRole('link', { name: 'bbx_1' });
    const calls = vi.mocked(enumerateBlackBox).mock.calls.length;
    act(() => { socketListeners.forEach((l) => l({ type: 'blackbox.captured', data: { entryId: 'bbx_3' } })); });
    await waitFor(() => expect(vi.mocked(enumerateBlackBox).mock.calls.length).toBe(calls + 1));
  });
});

describe('Black Box page: fingerprints, hotspots and retention', () => {
  it('lists fingerprints with a trend sparkline by version', async () => {
    vi.mocked(enumerateBlackBoxFingerprints).mockResolvedValue(page([{ fingerprint: 'fp_abc', count: 5, openCount: 2, firstSeenUtc: '2026-09-01T00:00:00Z', lastSeenUtc: '2026-10-28T00:00:00Z', latestEntryId: 'bbx_2', phase: 'Landing', step: 'LocalMerge', failureKind: 'LandingConflict' }]) as never);
    vi.mocked(getBlackBoxFingerprintTrend).mockResolvedValue({ fingerprint: 'fp_abc', phase: 'Landing', step: 'LocalMerge', total: 5, truncated: false, versions: [
      { armadaVersion: '1.1.0', count: 4, openCount: 1, armadaFixedCount: 0, firstSeenUtc: '2026-09-01T00:00:00Z', lastSeenUtc: '2026-09-30T00:00:00Z' },
      { armadaVersion: '1.2.0', count: 1, openCount: 1, armadaFixedCount: 0, firstSeenUtc: '2026-10-01T00:00:00Z', lastSeenUtc: '2026-10-28T00:00:00Z' },
    ] });
    renderAt('/missions?tab=blackbox&bbtab=fingerprints');
    expect(await screen.findByRole('link', { name: 'fp_abc' })).toHaveAttribute('href', '/missions?tab=blackbox&bbtab=entries&fingerprint=fp_abc');
    const spark = await screen.findByTestId('sparkline-fp_abc');
    expect(spark).toHaveAttribute('aria-label', '1.1.0: 4 (1 open); 1.2.0: 1 (1 open)');
    expect(spark.querySelector('polyline')).not.toBeNull();
    expect(getBlackBoxFingerprintTrend).toHaveBeenCalledWith('fp_abc');
  });

  it('shows a dash when a trend cannot be read', async () => {
    vi.mocked(enumerateBlackBoxFingerprints).mockResolvedValue(page([{ fingerprint: 'fp_x', count: 1, openCount: 1, firstSeenUtc: '', lastSeenUtc: '', phase: 'Execution', step: 'Stall' }]) as never);
    vi.mocked(getBlackBoxFingerprintTrend).mockRejectedValue(new Error('nope'));
    renderAt('/missions?tab=blackbox&bbtab=fingerprints');
    await screen.findByRole('link', { name: 'fp_x' });
    await waitFor(() => expect(screen.queryByTestId('sparkline-fp_x')).toBeNull());
  });

  it('lists hotspots per vessel and filters by vessel', async () => {
    vi.mocked(enumerateBlackBoxHotspots).mockResolvedValue(page([{ vesselId: 'vsl_1', directory: 'src/middleware', fileCount: 4, entryCount: 3 }]) as never);
    renderAt('/missions?tab=blackbox&bbtab=hotspots');
    expect(await screen.findByRole('link', { name: 'src/middleware' })).toHaveAttribute('href', '/missions?tab=blackbox&bbtab=entries&path=src%2Fmiddleware&vesselId=vsl_1');
    await waitFor(() => expect(screen.getByRole('link', { name: 'api-server' })).toBeInTheDocument());
    fireEvent.change(screen.getByLabelText('Vessel'), { target: { value: 'vsl_1' } });
    await waitFor(() => expect(enumerateBlackBoxHotspots).toHaveBeenLastCalledWith({ pageNumber: 1, pageSize: 25, vesselId: 'vsl_1', role: undefined }));
  });

  it('shows the retention policy, usage and a GC preview', async () => {
    vi.mocked(getBlackBoxRetention).mockResolvedValue(retention);
    vi.mocked(getBlackBoxStorage).mockResolvedValue({ target: 'Database', fallbackToDatabase: true, health: { target: 'Database', healthy: true, checkedUtc: '' }, usage: [{ target: 'Database', entries: 42, bytes: 1048576 }] });
    vi.mocked(runBlackBoxGc).mockResolvedValue(gcReport);
    renderAt('/missions?tab=blackbox&bbtab=retention');
    expect(await screen.findByText('180 days')).toBeInTheDocument();
    expect(screen.getByRole('table', { name: 'Usage by storage target' })).toHaveTextContent('Database');
    expect(screen.getByTestId('blackbox-gc-report')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Preview garbage collection' }));
    await waitFor(() => expect(runBlackBoxGc).toHaveBeenCalledWith(true));
    expect(await screen.findByTestId('blackbox-gc-preview')).toHaveTextContent('bbx_old1');
  });

  it('purges by filter only after a dry run and a typed confirmation', async () => {
    vi.mocked(getBlackBoxRetention).mockResolvedValue(retention);
    vi.mocked(getBlackBoxStorage).mockResolvedValue({ target: 'Database', fallbackToDatabase: true });
    vi.mocked(purgeBlackBox)
      .mockResolvedValueOnce({ dryRun: true, matched: 7, deleted: 0, pending: 0, skippedPinned: 1, bytesFreed: 0, sampleEntryIds: ['bbx_a'] })
      .mockResolvedValueOnce({ dryRun: false, matched: 7, deleted: 6, pending: 0, skippedPinned: 1, bytesFreed: 4096 });
    renderAt('/missions?tab=blackbox&bbtab=retention');
    await screen.findByText('180 days');
    expect(screen.getByRole('button', { name: 'Purge' })).toBeDisabled();
    fireEvent.change(screen.getByLabelText('Purge created before'), { target: { value: '2026-09-01' } });
    fireEvent.click(screen.getByRole('button', { name: 'Dry run purge' }));
    await waitFor(() => expect(purgeBlackBox).toHaveBeenCalledWith({ dryRun: true, vesselId: undefined, createdBefore: '2026-09-01T00:00:00Z', resolution: undefined, includePinned: undefined }));
    expect(await screen.findByTestId('blackbox-purge-preview')).toHaveTextContent('7 match, 1 pinned');
    fireEvent.click(screen.getByRole('button', { name: 'Purge 6 entries' }));
    const dialog = await screen.findByRole('alertdialog');
    const go = within(dialog).getByRole('button', { name: 'Purge' });
    expect(go).toBeDisabled();
    fireEvent.change(within(dialog).getByRole('textbox'), { target: { value: 'delete' } });
    fireEvent.click(go);
    await waitFor(() => expect(purgeBlackBox).toHaveBeenLastCalledWith(expect.objectContaining({ dryRun: false, createdBefore: '2026-09-01T00:00:00Z' })));
  });

  it('a changed purge filter needs a new dry run', async () => {
    vi.mocked(getBlackBoxRetention).mockResolvedValue(retention);
    vi.mocked(getBlackBoxStorage).mockResolvedValue({ target: 'Database', fallbackToDatabase: true });
    vi.mocked(purgeBlackBox).mockResolvedValue({ dryRun: true, matched: 2, deleted: 0, pending: 0, skippedPinned: 0, bytesFreed: 0 });
    renderAt('/missions?tab=blackbox&bbtab=retention');
    await screen.findByText('180 days');
    fireEvent.click(screen.getByRole('button', { name: 'Dry run purge' }));
    expect(await screen.findByRole('button', { name: 'Purge 2 entries' })).toBeEnabled();
    fireEvent.click(screen.getByLabelText('Include pinned entries'));
    expect(screen.getByRole('button', { name: 'Purge' })).toBeDisabled();
  });

  it('hides GC and purge controls from tenant admins and explains a refusal', async () => {
    auth.isAdmin = false;
    vi.mocked(getBlackBoxRetention).mockResolvedValue(retention);
    renderAt('/missions?tab=blackbox&bbtab=retention');
    await screen.findByText('180 days');
    expect(screen.queryByRole('button', { name: 'Preview garbage collection' })).toBeNull();
    expect(screen.queryByRole('button', { name: 'Dry run purge' })).toBeNull();
    expect(getBlackBoxStorage).not.toHaveBeenCalled();
  });

  it('tells a regular user retention is for administrators', async () => {
    auth.isAdmin = false;
    auth.isTenantAdmin = false;
    vi.mocked(getBlackBoxRetention).mockRejectedValue(apiError(403));
    renderAt('/missions?tab=blackbox&bbtab=retention');
    expect(await screen.findByText('Retention status is available to tenant administrators.')).toBeInTheDocument();
  });
});

describe('Black box entry page', () => {
  it('shows not found for an unknown entry', async () => {
    vi.mocked(getBlackBox).mockRejectedValue(apiError(404, 'BlackBoxNotFound'));
    renderAt('/blackbox/bbx_nope');
    expect(await screen.findByText(/Black box entry not found/)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Black Box' })).toHaveAttribute('href', '/missions?tab=blackbox');
  });
});

describe('Black Box page: old /blackbox links', () => {
  it('redirect to the Missions Black Box tab, keeping the section and the filters', async () => {
    vi.mocked(enumerateBlackBoxFingerprints).mockResolvedValue(page([]) as never);
    renderAt('/blackbox?tab=fingerprints&vesselId=vsl_1');
    expect(await screen.findByRole('tab', { name: 'Fingerprints' })).toHaveAttribute('aria-selected', 'true');
  });
});

describe('Black Box page: Recovery stats tab', () => {
  it('opens the recovery stats tab from the URL and lists the paths', async () => {
    vi.mocked(listVessels).mockResolvedValue(page([{ id: 'vsl_1', name: 'api-server' }]) as never);
    vi.mocked(getBlackBoxRecoveryStats).mockResolvedValue(page([{ ruleId: 'BBX-LAND-001', pathId: 'bbx-land-001.retry', runs: 3, succeeded: 3, failed: 0, pending: 0, expired: 0, successRate: 1 }]) as never);
    renderAt('/missions?tab=blackbox&bbtab=recovery');
    expect(await screen.findByText('bbx-land-001.retry')).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: 'Recovery stats' })).toHaveAttribute('aria-selected', 'true');
    expect(screen.getByLabelText('100% success')).toBeInTheDocument();
  });
});
