import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import Dashboard from './Dashboard';
import {
  enumerateFleetActionRuns,
  enumerateVoyageProgress,
  getMissionHistory,
  getStatus,
  getVesselHealthSummary,
  listCaptains,
  listFleets,
  listMissionSummaries,
  listVessels,
} from '../api/client';
import type {
  EnumerationResult,
  MissionHistorySummaryResult,
  MissionSummary,
  VesselHealthSummary,
  WebSocketMessage,
} from '../types/models';

const { handlers, socketValue, localeValue, authValue } = vi.hoisted(() => {
  const handlers = new Set<(message: WebSocketMessage) => void>();
  const localeValue = {
    t: (text: string, params?: Record<string, string | number | null | undefined>) => {
      let result = text;
      for (const [key, value] of Object.entries(params ?? {})) {
        result = result.split(`{{${key}}}`).join(String(value));
      }
      return result;
    },
    locale: 'en',
    formatRelativeTime: (value: string | null | undefined) => value ?? '',
    formatDateTime: (value: string | null | undefined) => value ?? '',
  };
  const socketValue = {
    connected: true,
    reconnectCount: 0,
    subscribe: (handler: (message: WebSocketMessage) => void) => {
      handlers.add(handler);
      return () => handlers.delete(handler);
    },
    send: () => undefined,
  };
  const authValue = { isTenantAdmin: false, isAdmin: false };
  return { handlers, socketValue, localeValue, authValue };
});

vi.mock('../api/client', () => ({
  getStatus: vi.fn(),
  listMissionSummaries: vi.fn(),
  listVessels: vi.fn(),
  listCaptains: vi.fn(),
  listSignals: vi.fn(),
  listFleets: vi.fn(),
  deleteMission: vi.fn(),
  restartMission: vi.fn(),
  enumerateFleetActionRuns: vi.fn(),
  enumerateVoyageProgress: vi.fn(),
  getMissionHistory: vi.fn(),
  getVesselHealthSummary: vi.fn(),
}));

vi.mock('../context/WebSocketContext', () => ({
  useWebSocket: () => socketValue,
}));

vi.mock('../context/LocaleContext', () => ({
  useLocale: () => localeValue,
}));

vi.mock('../context/AuthContext', () => ({
  useAuth: () => authValue,
}));

const AUTO_REFRESH_KEY = 'armada_autorefresh_dashboard';
const PAGE_REFRESH = 'Reload every panel on this page from the server now.';

function emptyPage<T>(): EnumerationResult<T> {
  return {
    success: true,
    pageNumber: 1,
    pageSize: 0,
    totalPages: 1,
    totalRecords: 0,
    objects: [],
    totalMs: 0,
  };
}

function mission(id: string, title: string): MissionSummary {
  return {
    id,
    tenantId: null,
    userId: null,
    voyageId: null,
    vesselId: null,
    captainId: null,
    title,
    status: 'Complete',
    priority: 0,
    parentMissionId: null,
    persona: null,
    dependsOnMissionId: null,
    branchName: null,
    dockId: null,
    processId: null,
    prUrl: null,
    commitHash: null,
    failureReason: null,
    requiresReview: false,
    reviewDenyAction: 'FailPipeline',
    reviewComment: null,
    reviewedByUserId: null,
    reviewRequestedUtc: null,
    reviewedUtc: null,
    descriptionLength: 0,
    diffSnapshotLength: 0,
    agentOutputLength: 0,
    createdUtc: '2026-10-27T12:00:00Z',
    startedUtc: null,
    completedUtc: null,
    totalRuntimeMs: null,
    lastUpdateUtc: '2026-10-27T12:00:00Z',
  };
}

function missionPage(row: MissionSummary): EnumerationResult<MissionSummary> {
  return { ...emptyPage<MissionSummary>(), pageSize: 1, totalRecords: 1, objects: [row] };
}

function historyResult(total: number): MissionHistorySummaryResult {
  return {
    totalCount: total,
    completeCount: total,
    failedCount: 0,
    otherCount: 0,
    fromUtc: '2026-10-20T12:00:00.000Z',
    toUtc: '2026-10-27T12:00:00.000Z',
    bucketMinutes: 60,
    buckets: [{
      startUtc: '2026-10-26T12:00:00.000Z',
      completeCount: total,
      failedCount: 0,
      otherCount: 0,
      totalCount: total,
    }],
  };
}

function statusBody() {
  return {
    totalCaptains: 1,
    idleCaptains: 1,
    workingCaptains: 0,
    stalledCaptains: 0,
    activeVoyages: 0,
    missionsByStatus: { Complete: 1 },
    voyages: [],
    recentSignals: [],
  };
}

function renderDashboard() {
  render(
    <MemoryRouter>
      <Dashboard />
    </MemoryRouter>,
  );
}

async function emitSocket(times: number) {
  const current = [...handlers];
  await act(async () => {
    for (let index = 0; index < times; index += 1) {
      for (const handler of current) handler({ type: 'mission.updated' });
    }
  });
}

function historySection(): HTMLElement {
  const section = document.querySelector('.mission-history-section');
  if (!section) throw new Error('Mission history section is not on the page.');
  return section as HTMLElement;
}

describe('Dashboard home refresh', () => {
  beforeEach(() => {
    handlers.clear();
    localStorage.removeItem(AUTO_REFRESH_KEY);
    localStorage.setItem(AUTO_REFRESH_KEY, '0');
    vi.mocked(getStatus).mockResolvedValue(statusBody());
    vi.mocked(listMissionSummaries).mockResolvedValue(missionPage(mission('msn_1', 'Older mission')));
    vi.mocked(listVessels).mockResolvedValue(emptyPage());
    vi.mocked(listCaptains).mockResolvedValue(emptyPage());
    vi.mocked(listFleets).mockResolvedValue(emptyPage());
    vi.mocked(enumerateFleetActionRuns).mockResolvedValue(emptyPage());
    vi.mocked(enumerateVoyageProgress).mockResolvedValue(emptyPage());
    vi.mocked(getMissionHistory).mockResolvedValue(historyResult(4));
    vi.mocked(getVesselHealthSummary).mockResolvedValue({ totalVessels: 0 } as VesselHealthSummary);
  });

  afterEach(() => {
    localStorage.removeItem(AUTO_REFRESH_KEY);
    vi.clearAllMocks();
    vi.useRealTimers();
  });

  it('runs one follow-up home load after a socket burst and keeps the later missions', async () => {
    let releaseFirst: () => void = () => undefined;
    const firstGate = new Promise<void>((resolve) => {
      releaseFirst = resolve;
    });
    vi.mocked(getStatus).mockImplementation(() => {
      if (vi.mocked(getStatus).mock.calls.length === 1) return firstGate.then(() => statusBody());
      return Promise.resolve(statusBody());
    });
    vi.mocked(listMissionSummaries).mockImplementation(() => {
      if (vi.mocked(listMissionSummaries).mock.calls.length === 1) {
        return firstGate.then(() => missionPage(mission('msn_1', 'Older mission')));
      }
      return Promise.resolve(missionPage(mission('msn_2', 'Newer mission')));
    });

    renderDashboard();
    await waitFor(() => expect(getStatus).toHaveBeenCalledTimes(1));
    expect(handlers.size).toBe(1);

    await emitSocket(3);

    expect(getStatus).toHaveBeenCalledTimes(1);
    expect(listMissionSummaries).toHaveBeenCalledTimes(1);
    expect(enumerateVoyageProgress).toHaveBeenCalledTimes(1);

    await act(async () => {
      releaseFirst();
    });

    expect(await screen.findByText('Newer mission')).toBeInTheDocument();
    expect(screen.queryByText('Older mission')).not.toBeInTheDocument();
    expect(getStatus).toHaveBeenCalledTimes(2);
    expect(listMissionSummaries).toHaveBeenCalledTimes(2);
    expect(enumerateVoyageProgress).toHaveBeenCalledTimes(2);
  });

  it('uses the same single follow-up when Refresh is clicked during a home load', async () => {
    let releaseRefresh: () => void = () => undefined;
    const refreshGate = new Promise<void>((resolve) => {
      releaseRefresh = resolve;
    });
    vi.mocked(getStatus).mockImplementation(() => {
      if (vi.mocked(getStatus).mock.calls.length === 2) return refreshGate.then(() => statusBody());
      return Promise.resolve(statusBody());
    });
    vi.mocked(listMissionSummaries).mockImplementation(() => {
      const calls = vi.mocked(listMissionSummaries).mock.calls.length;
      if (calls === 1) return Promise.resolve(missionPage(mission('msn_1', 'Older mission')));
      if (calls === 2) return refreshGate.then(() => missionPage(mission('msn_1', 'Older mission')));
      return Promise.resolve(missionPage(mission('msn_2', 'Newer mission')));
    });

    renderDashboard();
    fireEvent.click(await screen.findByRole('button', { name: PAGE_REFRESH }));
    await waitFor(() => expect(getStatus).toHaveBeenCalledTimes(2));
    expect(handlers.size).toBe(1);

    await emitSocket(3);

    expect(getStatus).toHaveBeenCalledTimes(2);
    expect(listMissionSummaries).toHaveBeenCalledTimes(2);
    expect(enumerateVoyageProgress).toHaveBeenCalledTimes(2);

    await act(async () => {
      releaseRefresh();
    });

    expect(await screen.findByText('Newer mission')).toBeInTheDocument();
    expect(screen.queryByText('Older mission')).not.toBeInTheDocument();
    expect(getStatus).toHaveBeenCalledTimes(3);
    expect(listMissionSummaries).toHaveBeenCalledTimes(3);
    expect(enumerateVoyageProgress).toHaveBeenCalledTimes(3);
  });

  it('stops periodic home reloads when auto-refresh is None and still reloads reference data', async () => {
    localStorage.setItem(AUTO_REFRESH_KEY, '15');
    vi.useFakeTimers({ shouldAdvanceTime: true });
    renderDashboard();

    expect(await screen.findByRole('combobox', { name: 'Auto-refresh interval' })).toHaveValue('15');
    await waitFor(() => expect(getStatus).toHaveBeenCalledTimes(1));
    await waitFor(() => expect(listVessels).toHaveBeenCalledTimes(1));

    fireEvent.change(screen.getByRole('combobox', { name: 'Auto-refresh interval' }), { target: { value: '0' } });
    expect(screen.getByRole('combobox', { name: 'Auto-refresh interval' })).toHaveValue('0');
    const homeCalls = vi.mocked(getStatus).mock.calls.length;
    const referenceCalls = vi.mocked(listVessels).mock.calls.length;

    await act(async () => {
      await vi.advanceTimersByTimeAsync(30000);
    });

    expect(getStatus).toHaveBeenCalledTimes(homeCalls);
    expect(listVessels).toHaveBeenCalledTimes(referenceCalls + 1);
    expect(listCaptains).toHaveBeenCalledTimes(referenceCalls + 1);
    expect(listFleets).toHaveBeenCalledTimes(referenceCalls + 1);
  });

  it('requests mission history again when the page Refresh control runs', async () => {
    let releaseHistory: (result: MissionHistorySummaryResult) => void = () => undefined;
    vi.mocked(getMissionHistory).mockImplementation(() => {
      const calls = vi.mocked(getMissionHistory).mock.calls.length;
      if (calls === 1) return Promise.resolve(historyResult(4));
      return new Promise((resolve) => {
        releaseHistory = resolve;
      });
    });

    renderDashboard();
    expect(await screen.findByText('Older mission')).toBeInTheDocument();
    await waitFor(() => expect(getMissionHistory).toHaveBeenCalledTimes(1));
    expect(within(historySection()).getAllByText('4').length).toBeGreaterThan(0);

    const firstQuery = vi.mocked(getMissionHistory).mock.calls[0][0];
    fireEvent.click(screen.getByRole('button', { name: PAGE_REFRESH }));
    await waitFor(() => expect(getMissionHistory).toHaveBeenCalledTimes(2));

    const secondQuery = vi.mocked(getMissionHistory).mock.calls[1][0];
    expect(secondQuery?.bucketMinutes).toBe(firstQuery?.bucketMinutes);
    expect(secondQuery?.fleetId).toBe(firstQuery?.fleetId);
    expect(secondQuery?.vesselId).toBe(firstQuery?.vesselId);
    expect(Date.parse(secondQuery?.toUtc ?? '') - Date.parse(secondQuery?.fromUtc ?? '')).toBe(168 * 60 * 60 * 1000);
    expect(screen.queryByText('Loading mission history...')).not.toBeInTheDocument();
    expect(within(historySection()).getAllByText('4').length).toBeGreaterThan(0);

    await act(async () => {
      releaseHistory(historyResult(9));
    });
    await waitFor(() => expect(within(historySection()).getAllByText('9').length).toBeGreaterThan(0));
  });

  it('reloads mission history once from the chart refresh control', async () => {
    renderDashboard();
    await waitFor(() => expect(getMissionHistory).toHaveBeenCalledTimes(1));
    const homeCalls = vi.mocked(getStatus).mock.calls.length;

    fireEvent.click(within(historySection()).getByRole('button', { name: 'Refresh' }));

    await waitFor(() => expect(getMissionHistory).toHaveBeenCalledTimes(2));
    expect(vi.mocked(getStatus).mock.calls.length).toBeGreaterThan(homeCalls);
    expect(getMissionHistory).toHaveBeenCalledTimes(2);
    const firstQuery = vi.mocked(getMissionHistory).mock.calls[0][0];
    const secondQuery = vi.mocked(getMissionHistory).mock.calls[1][0];
    expect(secondQuery?.bucketMinutes).toBe(firstQuery?.bucketMinutes);
    expect(secondQuery?.fleetId).toBe(firstQuery?.fleetId);
    expect(secondQuery?.vesselId).toBe(firstQuery?.vesselId);
  });
});
