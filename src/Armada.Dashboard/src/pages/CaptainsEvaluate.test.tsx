import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import Captains from './Captains';
import { evaluateCaptain, getCaptain, listBatteryPacks, listCaptains } from '../api/client';
import { translateTemplate } from '../i18n/runtime';
import type { Captain, ModelProbeRun } from '../types/models';

vi.mock('../api/client', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../api/client')>()),
  listCaptains: vi.fn(),
  getCaptain: vi.fn(),
  listBatteryPacks: vi.fn(),
  evaluateCaptain: vi.fn(),
  enumerateModelEndpoints: vi.fn(),
}));

const localeValue = {
  t: (text: string, params?: Record<string, string | number | null | undefined>) => translateTemplate('en', text, null, params),
  locale: 'en',
  formatDateTime: (v: string | null | undefined) => v ?? '',
  formatRelativeTime: (v: string | null | undefined) => v ?? '',
};
const pushToast = vi.fn();
const auth = { isAdmin: false, isTenantAdmin: true, user: { id: 'usr_1' } };
vi.mock('../context/LocaleContext', () => ({ useLocale: () => localeValue }));
vi.mock('../context/NotificationContext', () => ({ useNotifications: () => ({ pushToast }) }));
vi.mock('../context/AuthContext', () => ({ useAuth: () => auth }));
vi.mock('../components/shared/UserScopeFilter', () => ({ default: () => null }));

function page<T>(objects: T[]) {
  return { success: true, pageNumber: 1, pageSize: 9999, totalPages: 1, totalRecords: objects.length, totalMs: 1, objects };
}

const captain = {
  id: 'cpt_1', tenantId: 'default', name: 'claude-one', runtime: 'ClaudeCode', model: null, state: 'Idle',
  allowedPersonas: null, createdUtc: '2026-10-01T00:00:00Z', lastUpdateUtc: '2026-10-01T00:00:00Z',
} as unknown as Captain;

function DetailProbe() {
  const location = useLocation();
  return <div>detail at {location.pathname}{location.search}</div>;
}

function renderCaptains() {
  render(
    <MemoryRouter initialEntries={['/captains']}>
      <Routes>
        <Route path="/captains" element={<Captains />} />
        <Route path="/captains/:id" element={<DetailProbe />} />
      </Routes>
    </MemoryRouter>,
  );
}

describe('Captains Evaluate... row action', () => {
  beforeEach(() => {
    localStorage.clear();
    pushToast.mockReset();
    auth.isAdmin = false;
    auth.isTenantAdmin = true;
    vi.mocked(listCaptains).mockReset().mockResolvedValue(page([captain]) as never);
    vi.mocked(getCaptain).mockReset().mockResolvedValue(captain);
    vi.mocked(listBatteryPacks).mockReset().mockResolvedValue(page([]) as never);
    vi.mocked(evaluateCaptain).mockReset().mockResolvedValue({ id: 'mpr_42', status: 'Queued' } as unknown as ModelProbeRun);
  });

  it('opens the Evaluate dialog, starts the run and opens the captain page with the run id', async () => {
    renderCaptains();
    await screen.findByText('claude-one');
    fireEvent.click(screen.getByRole('button', { name: 'Actions' }));
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Evaluate...' }));

    expect(await screen.findByRole('dialog')).toBeInTheDocument();
    expect(await screen.findByText('Evaluate claude-one')).toBeInTheDocument();
    expect(getCaptain).toHaveBeenCalledWith('cpt_1');

    fireEvent.click(screen.getByLabelText('Quick'));
    const start = screen.getByRole('button', { name: 'Start evaluation' });
    await waitFor(() => expect(start).not.toBeDisabled());
    fireEvent.click(start);

    expect(await screen.findByText('detail at /captains/cpt_1?evaluationRun=mpr_42')).toBeInTheDocument();
    expect(evaluateCaptain).toHaveBeenCalledTimes(1);
    expect(evaluateCaptain).toHaveBeenCalledWith('cpt_1', 'Quick', {});
    expect(pushToast).toHaveBeenCalledWith('success', 'Evaluation mpr_42 started for captain "claude-one".');
  });

  it('submits the Full options the dialog collected', async () => {
    renderCaptains();
    await screen.findByText('claude-one');
    fireEvent.click(screen.getByRole('button', { name: 'Actions' }));
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Evaluate...' }));
    await screen.findByText('Evaluate claude-one');

    fireEvent.change(screen.getByLabelText('Run count'), { target: { value: '2' } });
    const start = screen.getByRole('button', { name: 'Start evaluation' });
    await waitFor(() => expect(start).not.toBeDisabled());
    fireEvent.click(start);

    await screen.findByText('detail at /captains/cpt_1?evaluationRun=mpr_42');
    expect(evaluateCaptain).toHaveBeenCalledWith('cpt_1', 'Full', { evaluatorCaptainId: null, packs: [], personas: [], runCount: 2 });
  });

  it('keeps the dialog open and shows the error when the start fails', async () => {
    vi.mocked(evaluateCaptain).mockRejectedValue(new Error('Evaluate needs an idle captain.'));
    renderCaptains();
    await screen.findByText('claude-one');
    fireEvent.click(screen.getByRole('button', { name: 'Actions' }));
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Evaluate...' }));
    await screen.findByText('Evaluate claude-one');

    const start = screen.getByRole('button', { name: 'Start evaluation' });
    await waitFor(() => expect(start).not.toBeDisabled());
    fireEvent.click(start);

    expect(await screen.findByText('Evaluate needs an idle captain.')).toBeInTheDocument();
    expect(screen.getByText('Evaluate claude-one')).toBeInTheDocument();
    expect(screen.queryByText(/detail at/)).toBeNull();
    expect(pushToast).not.toHaveBeenCalledWith('success', expect.anything());
  });

  it('shows Evaluate... to a global admin', async () => {
    auth.isAdmin = true;
    auth.isTenantAdmin = false;
    renderCaptains();
    await screen.findByText('claude-one');
    fireEvent.click(screen.getByRole('button', { name: 'Actions' }));
    expect(await screen.findByRole('menuitem', { name: 'Evaluate...' })).toBeInTheDocument();
  });

  it('hides Evaluate... from users who are not an admin', async () => {
    auth.isTenantAdmin = false;
    renderCaptains();
    await screen.findByText('claude-one');
    fireEvent.click(screen.getByRole('button', { name: 'Actions' }));
    expect(await screen.findByRole('menuitem', { name: 'View Tools' })).toBeInTheDocument();
    expect(screen.queryByRole('menuitem', { name: 'Evaluate...' })).toBeNull();
  });
});
