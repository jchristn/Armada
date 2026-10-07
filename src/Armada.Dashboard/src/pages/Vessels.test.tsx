import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import Vessels from './Vessels';
import { getVesselBranches, getVesselGitStatus, listFleets, listPipelines, listVessels } from '../api/client';
import { translateTemplate } from '../i18n/runtime';
import type { Vessel } from '../types/models';

vi.mock('../api/client', () => ({
  listVessels: vi.fn(),
  listFleets: vi.fn(),
  listPipelines: vi.fn(),
  createVessel: vi.fn(),
  deleteVessel: vi.fn(),
  getVesselGitStatus: vi.fn(),
  getVesselBranches: vi.fn(),
}));

const localeValue = {
  t: (text: string, params?: Record<string, string | number | null | undefined>) => translateTemplate('en', text, null, params),
  locale: 'en',
  formatDateTime: (v: string | null | undefined) => v ?? '',
  formatRelativeTime: (v: string | null | undefined) => v ?? '',
};
vi.mock('../context/LocaleContext', () => ({ useLocale: () => localeValue }));
vi.mock('../context/NotificationContext', () => ({ useNotifications: () => ({ pushToast: vi.fn() }) }));
vi.mock('../context/AuthContext', () => ({ useAuth: () => ({ isTenantAdmin: true, isAdmin: true, user: { id: 'usr_1' } }) }));
vi.mock('../components/vessels/BranchesModal', () => ({ default: () => null }));
vi.mock('../components/vessels/BuildContextModal', () => ({ default: () => null }));
vi.mock('../components/vessels/import/ImportWizard', () => ({ default: () => null }));
vi.mock('../components/fleetActions/RunActionModal', () => ({ default: () => null }));
vi.mock('../components/vessels/VesselFormModal', () => ({ default: () => null }));
vi.mock('../components/shared/UserScopeFilter', () => ({ default: () => null }));

function page<T>(objects: T[]) {
  return { success: true, pageNumber: 1, pageSize: 9999, totalPages: 1, totalRecords: objects.length, totalMs: 1, objects };
}

const vessel = {
  id: 'vsl_1', tenantId: 'default', fleetId: null, name: 'gateway', repoUrl: 'https://example.com/gateway.git',
  localPath: null, workingDirectory: null, defaultBranch: 'main', active: true,
  createdUtc: '2026-10-01T00:00:00Z', lastUpdateUtc: '2026-10-01T00:00:00Z',
} as unknown as Vessel;

describe('Vessels row actions', () => {
  beforeEach(() => {
    vi.mocked(listVessels).mockResolvedValue(page([vessel]) as never);
    vi.mocked(listFleets).mockResolvedValue(page([]) as never);
    vi.mocked(listPipelines).mockResolvedValue(page([]) as never);
    vi.mocked(getVesselGitStatus).mockResolvedValue({ vesselId: 'vsl_1', commitsAhead: 0, commitsBehind: 0 });
    vi.mocked(getVesselBranches).mockResolvedValue({ vesselId: 'vsl_1', branches: [], branchCount: 0 });
  });

  it('View History opens the vessel history page', async () => {
    function HistoryProbe() {
      return <div>history at {useLocation().pathname}</div>;
    }
    render(
      <MemoryRouter initialEntries={['/vessels']}>
        <Routes>
          <Route path="/vessels" element={<Vessels />} />
          <Route path="/vessels/:id/history" element={<HistoryProbe />} />
        </Routes>
      </MemoryRouter>,
    );

    await screen.findByText('gateway');
    fireEvent.click(screen.getByRole('button', { name: 'Actions' }));
    fireEvent.click(await screen.findByRole('menuitem', { name: 'View History' }));
    expect(await screen.findByText('history at /vessels/vsl_1/history')).toBeInTheDocument();
  });
});
