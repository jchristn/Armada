import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import CaptainDetail from './CaptainDetail';
import { getCaptain, getUsageLatest, listMissionSummaries } from '../api/client';
import { translateTemplate } from '../i18n/runtime';
import type { Captain } from '../types/models';

vi.mock('../api/client', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../api/client')>()),
  getCaptain: vi.fn(),
  listMissionSummaries: vi.fn(),
  getUsageLatest: vi.fn(),
  getUsageSeries: vi.fn(() => Promise.reject(new Error('no series'))),
  getCaptainLog: vi.fn(() => Promise.resolve({ log: '', lines: 0, totalLines: 0 })),
  enumerateModelEndpoints: vi.fn(() => Promise.resolve({ objects: [], totalRecords: 0, totalPages: 0, pageNumber: 1, pageSize: 100, success: true, totalMs: 0 })),
  listModelProbeRuns: vi.fn(() => Promise.resolve({ objects: [], totalRecords: 0, totalPages: 0, pageNumber: 1, pageSize: 10, success: true, totalMs: 0 })),
}));

const localeValue = {
  t: (text: string, params?: Record<string, string | number | null | undefined>) => translateTemplate('en', text, null, params),
  locale: 'en',
  formatDateTime: (v: string | null | undefined) => v ?? '',
  formatDate: (v: string | null | undefined) => v ?? '',
  formatRelativeTime: (v: string | null | undefined) => v ?? '',
};
vi.mock('../context/LocaleContext', () => ({ useLocale: () => localeValue }));
vi.mock('../context/NotificationContext', () => ({ useNotifications: () => ({ pushToast: vi.fn() }) }));
vi.mock('../context/AuthContext', () => ({ useAuth: () => ({ isAdmin: false, isTenantAdmin: false, user: { id: 'usr_1' } }) }));
vi.mock('../components/ModelProbeEvaluate', () => ({ default: () => null }));

const captain = {
  id: 'cpt_sel', tenantId: 'default', name: 'selected-captain', runtime: 'Gemini', model: null, state: 'Idle', allowedPersonas: null,
  createdUtc: '2026-10-01T00:00:00Z', lastUpdateUtc: '2026-10-01T00:00:00Z', currentMissionId: null,
  usageCaptureEnabled: false, usageCaptureActive: false, usageCaptureMode: 'Auto', usageCaptureIntervalSeconds: 300,
} as unknown as Captain;

function renderAt(path: string) {
  render(
    <MemoryRouter initialEntries={[path]}>
      <Routes><Route path="/captains/:id" element={<CaptainDetail />} /></Routes>
    </MemoryRouter>,
  );
}

describe('CaptainDetail usage section', () => {
  beforeEach(() => {
    vi.mocked(getCaptain).mockReset().mockResolvedValue(captain);
    vi.mocked(listMissionSummaries).mockReset().mockResolvedValue({ objects: [], totalRecords: 0, totalPages: 0, pageNumber: 1, pageSize: 100, success: true, totalMs: 0 } as never);
    vi.mocked(getUsageLatest).mockReset().mockResolvedValue({ objects: [], totalRecords: 0, totalPages: 0, pageNumber: 1, pageSize: 25, success: true, totalMs: 0 } as never);
  });

  it('section=usage focuses the selected captain usage section, even with capture disabled and no data', async () => {
    const scroll = vi.fn();
    Element.prototype.scrollIntoView = scroll;
    renderAt('/captains/cpt_sel?section=usage');
    const section = await screen.findByRole('region', { name: 'Usage' });
    await waitFor(() => expect(section).toHaveFocus());
    expect(section.id).toBe('captain-usage');
    expect(scroll).toHaveBeenCalled();
    expect(getCaptain).toHaveBeenCalledWith('cpt_sel');
    expect(getUsageLatest).toHaveBeenCalledWith({ captainId: 'cpt_sel', pageSize: 25 });
    expect(await screen.findByText('Capture disabled')).toBeInTheDocument();
  });

  it('does not steal focus without the section parameter', async () => {
    renderAt('/captains/cpt_sel');
    const section = await screen.findByRole('region', { name: 'Usage' });
    expect(section).not.toHaveFocus();
  });
});
