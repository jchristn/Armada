import { act, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import VoyageDetail from './VoyageDetail';
import { getVoyage, listCaptains, listVessels } from '../api/client';
import { translateTemplate } from '../i18n/runtime';
import { LIVE_REFRESH_DEBOUNCE_MS } from '../lib/useLiveRefresh';
import type { Mission, Voyage, WebSocketMessage } from '../types/models';

vi.mock('../api/client', () => ({
  getVoyage: vi.fn(),
  purgeVoyage: vi.fn(),
  cancelVoyage: vi.fn(),
  listMissions: vi.fn(),
  getMissionDiff: vi.fn(),
  getMissionLog: vi.fn(),
  createMission: vi.fn(),
  listVessels: vi.fn(),
  listCaptains: vi.fn(),
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

describe('VoyageDetail live refresh', () => {
  beforeEach(() => {
    handlers.clear();
    vi.mocked(listVessels).mockResolvedValue({ success: true, pageNumber: 1, pageSize: 1000, totalPages: 1, totalRecords: 0, totalMs: 1, objects: [] });
    vi.mocked(listCaptains).mockResolvedValue({ success: true, pageNumber: 1, pageSize: 1000, totalPages: 1, totalRecords: 0, totalMs: 1, objects: [] });
  });

  it('reloads when a mission or voyage event arrives, without a manual refresh', async () => {
    vi.mocked(getVoyage)
      .mockResolvedValueOnce({ voyage: voyage('InProgress'), missions: [mission('InProgress')] } as never)
      .mockResolvedValue({ voyage: voyage('Complete'), missions: [mission('Complete')] } as never);

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
    vi.mocked(getVoyage).mockResolvedValue({ voyage: voyage('InProgress', 'MergeAndPush'), missions: [] } as never);
    renderDetail();
    expect(await screen.findByText('(local + push)')).toBeInTheDocument();
    expect(screen.getByText(/MergeAndPush/)).toBeInTheDocument();
    expect(screen.queryByText('Auto-Push')).toBeNull();
    expect(screen.queryByText('Auto-Create PRs')).toBeNull();
  });

  it('shows Default when the voyage inherits its landing mode', async () => {
    vi.mocked(getVoyage).mockResolvedValue({ voyage: voyage('InProgress'), missions: [] } as never);
    renderDetail();
    expect(await screen.findByText('(vessel or global default)')).toBeInTheDocument();
  });
});
