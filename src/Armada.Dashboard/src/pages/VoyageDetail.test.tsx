import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import VoyageDetail from './VoyageDetail';
import { getMission, getVoyage, getVoyageBlackBoxCascade, listCaptains, listMissions, listVessels } from '../api/client';
import { translateTemplate } from '../i18n/runtime';
import { LIVE_REFRESH_DEBOUNCE_MS } from '../lib/useLiveRefresh';
import type { Mission, Voyage, WebSocketMessage } from '../types/models';

vi.mock('../api/client', () => ({
  getVoyage: vi.fn(),
  getMissionBlackBox: vi.fn(() => Promise.reject(Object.assign(new Error('not found'), { status: 404, data: { code: 'BlackBoxNotFound' } }))),
  getVoyageBlackBox: vi.fn(() => Promise.reject(Object.assign(new Error('not found'), { status: 404, data: { code: 'BlackBoxNotFound' } }))),
  getBlackBox: vi.fn(),
  getBlackBoxBrief: vi.fn(() => Promise.resolve('# FIX THIS')),
  listMissionBlackBoxEntries: vi.fn(() => Promise.resolve({ objects: [] })),
  getMission: vi.fn(),
  purgeVoyage: vi.fn(),
  cancelVoyage: vi.fn(),
  listMissions: vi.fn(),
  getMissionDiff: vi.fn(),
  getMissionLog: vi.fn(),
  createMission: vi.fn(),
  listVessels: vi.fn(),
  listCaptains: vi.fn(),
  getVoyageBlackBoxCascade: vi.fn(),
}));

const localeValue = {
  t: (text: string, params?: Record<string, string | number | null | undefined>) => translateTemplate('en', text, null, params),
  locale: 'en',
  formatDateTime: (v: string | null | undefined) => v ?? '',
  formatRelativeTime: (v: string | null | undefined) => v ?? '',
};
vi.mock('../context/LocaleContext', () => ({ useLocale: () => localeValue }));
vi.mock('../context/NotificationContext', () => ({ useNotifications: () => ({ pushToast: vi.fn() }) }));
vi.mock('../context/AuthContext', () => ({ useAuth: () => ({ isAdmin: true, isTenantAdmin: true }) }));

const handlers = new Set<(message: WebSocketMessage) => void>();
vi.mock('../context/WebSocketContext', () => ({
  useWebSocket: () => ({
    connected: true,
    reconnectCount: 0,
    send: vi.fn(),
    subscribe: (handler: (message: WebSocketMessage) => void) => {
      handlers.add(handler);
      return () => { handlers.delete(handler); };
    },
  }),
}));

function voyage(status: string, landingMode: string | null = null): Voyage {
  return {
    id: 'vyg_1', tenantId: 'default', title: 'Gateway hardening', description: null, status,
    createdUtc: '2026-10-04T00:00:00Z', completedUtc: null, lastUpdateUtc: '2026-10-04T00:00:00Z',
    autoPush: true, autoCreatePullRequests: true, autoMergePullRequests: null, landingMode,
  };
}

function mission(status: string): Mission {
  return { id: 'msn_1', tenantId: 'default', voyageId: 'vyg_1', vesselId: 'vsl_1', captainId: null, title: 'Add request logging', status } as unknown as Mission;
}

/** The voyage detail as the server serves it now: counts only (missions come from the paged list). */
function detailOf(v: Voyage, missions: Mission[]) {
  const counts: Record<string, number> = {};
  for (const m of missions) counts[m.status] = (counts[m.status] ?? 0) + 1;
  return { voyage: v, totalMissions: missions.length, completedMissions: counts['Complete'] ?? 0, failedMissions: counts['Failed'] ?? 0, inProgressMissions: counts['InProgress'] ?? 0, missionCountsByStatus: counts, vesselId: missions[0]?.vesselId ?? null, vesselCount: missions.length ? 1 : 0 };
}

function missionsPage(missions: Mission[]) {
  return { success: true, pageNumber: 1, pageSize: 50, totalPages: missions.length ? 1 : 0, totalRecords: missions.length, totalMs: 1, objects: missions };
}

/** Serve a voyage and its missions the way the server does. */
function serve(v: Voyage, missions: Mission[]) {
  vi.mocked(getVoyage).mockResolvedValue(detailOf(v, missions) as never);
  vi.mocked(listMissions).mockResolvedValue(missionsPage(missions) as never);
  vi.mocked(getMission).mockResolvedValue((missions[0] ?? null) as never);
}

describe('VoyageDetail live refresh', () => {
  beforeEach(() => {
    handlers.clear();
    vi.mocked(listVessels).mockResolvedValue({ success: true, pageNumber: 1, pageSize: 1000, totalPages: 1, totalRecords: 0, totalMs: 1, objects: [] });
    vi.mocked(listCaptains).mockResolvedValue({ success: true, pageNumber: 1, pageSize: 1000, totalPages: 1, totalRecords: 0, totalMs: 1, objects: [] });
  });

  it('reloads when a mission or voyage event arrives, without a manual refresh', async () => {
    vi.mocked(getVoyage)
      .mockResolvedValueOnce(detailOf(voyage('InProgress'), [mission('InProgress')]) as never)
      .mockResolvedValue(detailOf(voyage('Complete'), [mission('Complete')]) as never);
    vi.mocked(listMissions)
      .mockResolvedValueOnce(missionsPage([mission('InProgress')]) as never)
      .mockResolvedValue(missionsPage([mission('Complete')]) as never);
    vi.mocked(getMission).mockResolvedValue(mission('InProgress') as never);

    render(
      <MemoryRouter initialEntries={['/voyages/vyg_1']}>
        <Routes>
          <Route path="/voyages/:id" element={<VoyageDetail />} />
        </Routes>
      </MemoryRouter>,
    );

    await screen.findByText('0/1 complete, 0 failed');
    expect(handlers.size).toBeGreaterThan(0);

    // An unrelated event does not reload.
    act(() => { handlers.forEach((h) => h({ type: 'ask.chunk', data: {} })); });
    await new Promise((r) => setTimeout(r, LIVE_REFRESH_DEBOUNCE_MS + 50));
    expect(vi.mocked(getVoyage)).toHaveBeenCalledTimes(1);

    act(() => { handlers.forEach((h) => h({ type: 'mission.changed', data: { id: 'msn_1', status: 'Complete' } })); });
    act(() => { handlers.forEach((h) => h({ type: 'voyage.completed', data: { id: 'vyg_1' } })); });

    await waitFor(() => expect(screen.getByText('1/1 complete, 0 failed')).toBeTruthy());
    expect(vi.mocked(getVoyage)).toHaveBeenCalledTimes(2);
  });
});

describe('VoyageDetail configuration', () => {
  beforeEach(() => {
    handlers.clear();
    vi.mocked(getVoyage).mockReset();
    vi.mocked(listVessels).mockResolvedValue({ success: true, pageNumber: 1, pageSize: 1000, totalPages: 1, totalRecords: 0, totalMs: 1, objects: [] });
    vi.mocked(listCaptains).mockResolvedValue({ success: true, pageNumber: 1, pageSize: 1000, totalPages: 1, totalRecords: 0, totalMs: 1, objects: [] });
  });

  function renderDetail() {
    render(
      <MemoryRouter initialEntries={['/voyages/vyg_1']}>
        <Routes>
          <Route path="/voyages/:id" element={<VoyageDetail />} />
        </Routes>
      </MemoryRouter>,
    );
  }

  it('shows the landing mode with its short label and not the legacy push and pull request flags', async () => {
    serve(voyage('InProgress', 'MergeAndPush'), []);
    renderDetail();
    expect(await screen.findByText('(local + push)')).toBeInTheDocument();
    expect(screen.getByText(/MergeAndPush/)).toBeInTheDocument();
    expect(screen.queryByText('Auto-Push')).toBeNull();
    expect(screen.queryByText('Auto-Create PRs')).toBeNull();
  });

  it('shows Default when the voyage inherits its landing mode', async () => {
    serve(voyage('InProgress'), []);
    renderDetail();
    expect(await screen.findByText('(vessel or global default)')).toBeInTheDocument();
  });
});

describe('VoyageDetail missions table', () => {
  beforeEach(() => {
    localStorage.clear();
    vi.mocked(listVessels).mockResolvedValue({ success: true, pageNumber: 1, pageSize: 1000, totalPages: 1, totalRecords: 0, totalMs: 1, objects: [] });
    vi.mocked(listCaptains).mockResolvedValue({ success: true, pageNumber: 1, pageSize: 1000, totalPages: 1, totalRecords: 0, totalMs: 1, objects: [] });
    serve(voyage('InProgress'), [mission('InProgress')]);
  });

  it('reads the missions one server page at a time and takes progress from the server counts', async () => {
    const v = voyage('InProgress');
    vi.mocked(getVoyage).mockResolvedValue({ ...detailOf(v, [mission('Complete')]), totalMissions: 120, missionCountsByStatus: { Complete: 60, Failed: 2, InProgress: 58 } } as never);
    vi.mocked(listMissions).mockResolvedValue({ ...missionsPage([mission('InProgress')]), totalPages: 3, totalRecords: 120 } as never);
    render(
      <MemoryRouter initialEntries={['/voyages/vyg_1']}>
        <Routes><Route path="/voyages/:id" element={<VoyageDetail />} /></Routes>
      </MemoryRouter>,
    );
    expect(await screen.findByText('60/120 complete, 2 failed')).toBeInTheDocument();
    expect(vi.mocked(listMissions)).toHaveBeenCalledWith({ pageNumber: 1, pageSize: 50, filters: { voyageId: 'vyg_1', order: 'CreatedAscending' } });
  });

  it('locks Mission and ID in the column chooser and shows the ID in its own column, not under the title', async () => {
    render(
      <MemoryRouter initialEntries={['/voyages/vyg_1']}>
        <Routes><Route path="/voyages/:id" element={<VoyageDetail />} /></Routes>
      </MemoryRouter>,
    );
    const title = await screen.findByText('Add request logging');
    const missionCell = title.closest('td') as HTMLElement;
    expect(missionCell).toHaveAttribute('data-col', 'mission');
    expect(within(missionCell).queryByText('msn_1')).toBeNull();
    expect(within(missionCell.closest('tr') as HTMLElement).getByText('msn_1').closest('td')).toHaveAttribute('data-col', 'id');

    const wrap = document.querySelector('[data-table="voyage-detail-missions"]') as HTMLElement;
    await act(async () => { fireEvent.click(within(wrap).getByRole('button', { name: /^Columns/ })); });
    const menu = screen.getByRole('menu', { name: 'Choose visible columns' });
    expect(within(menu).getByRole('menuitemcheckbox', { name: /Mission/ })).toHaveAttribute('aria-disabled', 'true');
    // The row action buttons are a fixed column, not offered in the chooser.
    expect(within(menu).queryByRole('menuitemcheckbox', { name: /Actions/ })).toBeNull();
  });
});

describe('VoyageDetail black box insights', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    localStorage.clear();
    vi.mocked(listVessels).mockResolvedValue(missionsPage([]) as never);
    vi.mocked(listCaptains).mockResolvedValue(missionsPage([]) as never);
    vi.mocked(getVoyageBlackBoxCascade).mockResolvedValue({
      voyageId: 'vyg_1', voyageStatus: 'Failed', missionCount: 1, truncated: false, rootMissionId: 'msn_1',
      nodes: [{ missionId: 'msn_1', title: 'Add request logging', status: 'Failed', isRoot: true }], edges: [],
    });
  });

  function renderAt(state?: unknown) {
    render(
      <MemoryRouter initialEntries={[{ pathname: '/voyages/vyg_1', state }]}>
        <Routes>
          <Route path="/voyages/:id" element={<VoyageDetail />} />
        </Routes>
      </MemoryRouter>,
    );
  }

  it('shows the dispatch warnings passed from the create flow', async () => {
    serve(voyage('InProgress'), [mission('InProgress')]);
    renderAt({ dispatchWarnings: { overlapWarnings: [{ kind: 'Reservation', directory: 'src/api', message: 'src/api is reserved by msn_9' }], briefLintWarnings: [] } });
    const banner = await screen.findByTestId('dispatch-warnings-banner');
    expect(within(banner).getByText('src/api is reserved by msn_9')).toBeInTheDocument();
    expect(within(banner).getByText('Reserved directory')).toBeInTheDocument();
  });

  it('has no warnings banner or cascade card for a healthy voyage', async () => {
    serve(voyage('InProgress'), [mission('InProgress')]);
    renderAt();
    await screen.findByRole('heading', { name: /Gateway hardening/ });
    expect(screen.queryByTestId('dispatch-warnings-banner')).not.toBeInTheDocument();
    expect(screen.queryByTestId('voyage-cascade-card')).not.toBeInTheDocument();
    expect(getVoyageBlackBoxCascade).not.toHaveBeenCalled();
  });

  it('shows the cascade graph for a failed voyage', async () => {
    serve(voyage('Failed'), [mission('Failed')]);
    renderAt();
    const card = await screen.findByTestId('voyage-cascade-card');
    expect(await within(card).findByRole('img', { name: 'Cascade graph of 1 missions and 0 edges' })).toBeInTheDocument();
    expect(getVoyageBlackBoxCascade).toHaveBeenCalledWith('vyg_1');
  });
});
