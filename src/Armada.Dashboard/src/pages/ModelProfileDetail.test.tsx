import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import ModelProfileDetail from './ModelProfileDetail';
import ModelProfiles from './ModelProfiles';
import { getModelProfile, listModelProfiles, updateModelProfile } from '../api/client';
import { translateTemplate } from '../i18n/runtime';
import type { ModelProfile } from '../types/models';

vi.mock('../api/client', () => ({
  getModelProfile: vi.fn(),
  listModelProfiles: vi.fn(),
  updateModelProfile: vi.fn(),
  apiErrorMessage: (err: unknown, fallback: string) => (err instanceof Error ? err.message : fallback),
}));

const auth = { isAdmin: true, isTenantAdmin: true, user: null };
vi.mock('../context/AuthContext', () => ({ useAuth: () => auth }));
const localeValue = {
  t: (text: string, params?: Record<string, string | number | null | undefined>) => translateTemplate('en', text, null, params),
  locale: 'en',
  formatDateTime: (v: string | null | undefined) => v ?? '',
  formatRelativeTime: (v: string | null | undefined) => v ?? '',
};
vi.mock('../context/LocaleContext', () => ({ useLocale: () => localeValue }));
vi.mock('../context/NotificationContext', () => ({ useNotifications: () => ({ pushToast: vi.fn() }) }));

function profile(): ModelProfile {
  return {
    id: 'mdl_1', tenantId: null,
    identity: { runtime: 'ApiEndpoint', provider: 'Ollama', model: 'qwen3.8', servedContextTokens: 32768, servedContextSource: 'Discovered' },
    capabilities: { toolsAvailable: null, mcpAvailable: null, probedToolsAvailable: true, probedMcpAvailable: false, probedUtc: '2026-11-20T00:00:00Z' },
    rendering: { template: null, repeatContractAtEnd: null, maxFilesPerMission: null },
    recovery: { maxResumes: 4, unclassifiedResumes: null, transientGraceMinutes: null, judgeReworkRounds: null, outputRepairAttempts: null, escalateAfterResumes: null },
    effectiveRecovery: { modelProfileId: 'mdl_1', maxResumes: 4, unclassifiedResumes: 1, transientGraceMinutes: 15, judgeReworkRounds: 2, outputRepairAttempts: 1, escalateAfterResumes: 4 },
    tiering: { tier: null, suggestedTier: 'Premium', suggestedReason: 'High success', suggestedSamples: 24, suggestedUtc: null },
    timeouts: { stallThresholdMinutes: null, toolWaitHardLimitMinutes: null, maxRuntimeMinutes: null, firstOutputMinutes: null },
    effectiveTimeouts: {
      modelProfileId: 'mdl_1', speedScale: 2,
      stallThresholdMinutes: 20, stallThresholdSource: 'Measured', toolWaitHardLimitMinutes: 60, toolWaitHardLimitSource: 'Global',
      maxRuntimeMinutes: 240, maxRuntimeSource: 'Measured', firstOutputMinutes: 30, firstOutputSource: 'Measured',
    },
    speed: { tokensPerSecond: 15, firstOutputMs: 1200, measuredUtc: '2026-11-20T00:00:00Z' },
    notes: null, revisions: [], createdUtc: '2026-11-01T00:00:00Z', lastUpdateUtc: '2026-11-02T00:00:00Z',
  };
}

function renderDetail() {
  return render(
    <MemoryRouter initialEntries={['/model-profiles/mdl_1']}>
      <Routes><Route path="/model-profiles/:id" element={<ModelProfileDetail />} /></Routes>
    </MemoryRouter>,
  );
}

describe('ModelProfileDetail', () => {
  beforeEach(() => {
    vi.mocked(getModelProfile).mockReset().mockResolvedValue(profile());
    vi.mocked(updateModelProfile).mockReset();
    auth.isAdmin = true;
    auth.isTenantAdmin = true;
  });

  it('shows served context, probed capabilities, the suggested tier and the timeouts in effect with their source', async () => {
    renderDetail();
    expect(await screen.findByRole('heading', { name: 'qwen3.8' })).toBeInTheDocument();
    expect(screen.getByText(`${(32768).toLocaleString()} tokens (Discovered)`)).toBeInTheDocument();
    expect(screen.getByText('Yes (Probed)')).toBeInTheDocument();
    expect(screen.getByText('Premium from 24 attempts')).toBeInTheDocument();
    expect(screen.getByText('Measured speed scales the global timeouts by 2.00.')).toBeInTheDocument();
    const stallRow = screen.getByText('Stall threshold (minutes)').closest('tr')!;
    expect(within(stallRow).getByText('20')).toBeInTheDocument();
    expect(within(stallRow).getByText('Measured speed')).toBeInTheDocument();
  });

  it('saves the tier and a timeout override through the profile PUT', async () => {
    vi.mocked(updateModelProfile).mockResolvedValue({ ...profile(), tiering: { ...profile().tiering, tier: 'Premium' } });
    renderDetail();
    fireEvent.click(await screen.findByRole('button', { name: 'Edit' }));
    const dialog = screen.getByRole('form', { name: 'Edit model profile' });
    fireEvent.change(within(dialog).getByLabelText('Tier'), { target: { value: 'Premium' } });
    fireEvent.change(within(dialog).getByLabelText('First output (minutes)'), { target: { value: '45' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Save' }));
    await waitFor(() => expect(updateModelProfile).toHaveBeenCalledTimes(1));
    const [id, body] = vi.mocked(updateModelProfile).mock.calls[0];
    expect(id).toBe('mdl_1');
    expect(body.tiering).toEqual({ tier: 'Premium' });
    expect(body.timeouts?.firstOutputMinutes).toBe(45);
    expect(body.recovery?.maxResumes).toBe(4);
  });

  it('blocks a save that breaks a bound and does not call the API', async () => {
    renderDetail();
    fireEvent.click(await screen.findByRole('button', { name: 'Edit' }));
    const dialog = screen.getByRole('form', { name: 'Edit model profile' });
    fireEvent.change(within(dialog).getByLabelText('Escalate after resumes'), { target: { value: '9' } });
    fireEvent.submit(dialog);
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('Unclassified resumes and Escalate after resumes cannot be more than Resumes.');
    expect(updateModelProfile).not.toHaveBeenCalled();
  });

  it('hides Edit from non-admins', async () => {
    auth.isAdmin = false;
    auth.isTenantAdmin = false;
    renderDetail();
    await screen.findByRole('heading', { name: 'qwen3.8' });
    expect(screen.queryByRole('button', { name: 'Edit' })).toBeNull();
  });
});

describe('ModelProfiles list', () => {
  it('lists profiles with served context, tier suggestion and capability source', async () => {
    vi.mocked(listModelProfiles).mockResolvedValue({ success: true, pageNumber: 1, pageSize: 25, totalPages: 1, totalRecords: 1, totalMs: 1, objects: [profile()] });
    render(<MemoryRouter><ModelProfiles /></MemoryRouter>);
    expect(await screen.findByText('qwen3.8')).toBeInTheDocument();
    expect(screen.getByText('Suggested: Premium')).toBeInTheDocument();
    expect(screen.getByText('Yes (Probed)')).toBeInTheDocument();
    expect(vi.mocked(listModelProfiles).mock.calls[0][0]).toMatchObject({ pageNumber: 1 });
  });
});
