import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useNavigate } from 'react-router-dom';
import CaptainDetail from './CaptainDetail';
import { getCaptain, getMission, listMissionSummaries } from '../api/client';
import { translateTemplate } from '../i18n/runtime';
import type { Captain, Mission } from '../types/models';

vi.mock('../api/client', () => ({
  createCaptain: vi.fn(),
  getCaptain: vi.fn(),
  getCaptainTools: vi.fn(),
  getCaptainLog: vi.fn(),
  stopCaptain: vi.fn(),
  unquarantineCaptain: vi.fn(),
  recallCaptain: vi.fn(),
  getMission: vi.fn(),
  listMissionSummaries: vi.fn(),
  updateCaptain: vi.fn(),
  deleteCaptain: vi.fn(),
  setCaptainCliPermissionPolicy: vi.fn(),
  listModelEndpoints: vi.fn(),
  getModelProbeRun: vi.fn(),
  evaluateCaptain: vi.fn(),
  evaluateModelEndpoint: vi.fn(),
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

interface Deferred<T> { promise: Promise<T>; resolve: (value: T) => void }

function deferred<T>(): Deferred<T> {
  let resolve: (value: T) => void = () => {};
  const promise = new Promise<T>((res) => { resolve = res; });
  return { promise, resolve };
}

function captain(id: string, name: string, missionId: string): Captain {
  return { id, name, state: 'Idle', runtime: 'ClaudeCode', currentMissionId: missionId } as unknown as Captain;
}

function mission(id: string, title: string): Mission {
  return { id, title, status: 'InProgress' } as unknown as Mission;
}

describe('CaptainDetail route load order', () => {
  const captains = new Map<string, Deferred<Captain>>();
  const missions = new Map<string, Deferred<Mission>>();

  function gate<T>(store: Map<string, Deferred<T>>, id: string): Deferred<T> {
    let entry = store.get(id);
    if (!entry) {
      entry = deferred<T>();
      store.set(id, entry);
    }
    return entry;
  }

  beforeEach(() => {
    captains.clear();
    missions.clear();
    vi.mocked(listMissionSummaries).mockResolvedValue({ success: true, pageNumber: 1, pageSize: 100, totalPages: 1, totalRecords: 0, totalMs: 1, objects: [] } as never);
    vi.mocked(getCaptain).mockImplementation((id: string) => gate(captains, id).promise);
    vi.mocked(getMission).mockImplementation((id: string) => gate(missions, id).promise);
  });

  it('keeps the newer captain when the older request finishes last', async () => {
    function Harness() {
      const navigate = useNavigate();
      return (
        <>
          <button type="button" onClick={() => navigate('/captains/cpt_b')}>Open route B</button>
          <Routes>
            <Route path="/captains/:id" element={<CaptainDetail />} />
          </Routes>
        </>
      );
    }

    render(
      <MemoryRouter initialEntries={['/captains/cpt_a']}>
        <Harness />
      </MemoryRouter>,
    );
    fireEvent.click(screen.getByRole('button', { name: 'Open route B' }));

    await act(async () => {
      gate(captains, 'cpt_b').resolve(captain('cpt_b', 'Captain Beta', 'msn_b'));
    });
    await act(async () => {
      gate(missions, 'msn_b').resolve(mission('msn_b', 'Mission Beta'));
    });
    expect(await screen.findByRole('heading', { name: 'Captain Beta' })).toBeInTheDocument();
    expect(screen.getByText('Mission Beta')).toBeInTheDocument();

    await act(async () => {
      gate(captains, 'cpt_a').resolve(captain('cpt_a', 'Captain Alpha', 'msn_a'));
    });
    await act(async () => {
      const lateMission = missions.get('msn_a');
      if (lateMission) lateMission.resolve(mission('msn_a', 'Mission Alpha'));
    });

    await waitFor(() => {
      expect(screen.getByRole('heading', { name: 'Captain Beta' })).toBeInTheDocument();
      expect(screen.queryByRole('heading', { name: 'Captain Alpha' })).not.toBeInTheDocument();
      expect(screen.getByText('Mission Beta')).toBeInTheDocument();
      expect(screen.queryByText('Mission Alpha')).not.toBeInTheDocument();
    });
  });
});
