import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import VesselDetail from './VesselDetail';
import { getVessel, listFleets, listMissionSummaries, listPipelines, getVesselReadiness, getVesselLandingPreview, updateVessel } from '../api/client';
import { translateTemplate } from '../i18n/runtime';
import type { Vessel } from '../types/models';

vi.mock('../api/client', () => ({
  getVessel: vi.fn(),
  listFleets: vi.fn(),
  listMissionSummaries: vi.fn(),
  listPipelines: vi.fn(),
  createVessel: vi.fn(),
  updateVessel: vi.fn(),
  deleteVessel: vi.fn(),
  getVesselReadiness: vi.fn(),
  getVesselLandingPreview: vi.fn(),
}));

const localeValue = {
  t: (text: string, params?: Record<string, string | number | null | undefined>) => translateTemplate('en', text, null, params),
  locale: 'en',
  formatDateTime: (v: string | null | undefined) => v ?? '',
  formatRelativeTime: (v: string | null | undefined) => v ?? '',
};
vi.mock('../context/LocaleContext', () => ({ useLocale: () => localeValue }));
vi.mock('../context/NotificationContext', () => ({ useNotifications: () => ({ pushToast: vi.fn() }) }));
vi.mock('../components/vessels/health/VesselHealthButton', () => ({ default: () => null }));

function page<T>(objects: T[]) {
  return { success: true, pageNumber: 1, pageSize: 1000, totalPages: 1, totalRecords: objects.length, totalMs: 1, objects };
}

const vessel = {
  id: 'vsl_1', tenantId: 'default', fleetId: 'flt_1', name: 'gateway', repoUrl: 'https://example.com/gateway.git',
  localPath: null, workingDirectory: null, defaultBranch: 'main', projectContext: null, styleGuide: null,
  enableModelContext: true, modelContext: null, hasGitHubTokenOverride: false,
  landingMode: 'MergeQueue', branchCleanupPolicy: 'LocalOnly', requirePassingChecksToLand: true,
  protectedBranchPatterns: ['main'], releaseBranchPrefix: 'rel/', hotfixBranchPrefix: 'fix/',
  requirePullRequestForProtectedBranches: false, requireMergeQueueForReleaseBranches: true,
  autoLandEnabled: true, autoLandMaxFiles: 5, autoLandMaxLines: 0, autoLandPathAllowGlobs: ['src/**'], autoLandPathDenyGlobs: [],
  maxParallelVoyages: 0, maxParallelMissionsPerVoyage: 0, autoApprove: false, defaultPipelineId: null, active: true,
  preferredHarborId: 'hbr_1',
  createdUtc: '2026-10-01T00:00:00Z', lastUpdateUtc: '2026-10-01T00:00:00Z',
} as unknown as Vessel;

describe('VesselDetail Edit (F13)', () => {
  beforeEach(() => {
    vi.mocked(getVessel).mockResolvedValue(vessel);
    vi.mocked(listFleets).mockResolvedValue(page([{ id: 'flt_1', name: 'Main' }]) as never);
    vi.mocked(listMissionSummaries).mockResolvedValue(page([]) as never);
    vi.mocked(listPipelines).mockResolvedValue(page([]) as never);
    vi.mocked(getVesselReadiness).mockResolvedValue(null as never);
    vi.mocked(getVesselLandingPreview).mockResolvedValue(null as never);
    vi.mocked(updateVessel).mockResolvedValue(vessel);
  });

  it('shows the landing settings and keeps them on save', async () => {
    render(
      <MemoryRouter initialEntries={['/vessels/vsl_1?edit=1']}>
        <Routes>
          <Route path="/vessels/:id" element={<VesselDetail />} />
        </Routes>
      </MemoryRouter>,
    );

    const form = await screen.findByRole('form', { name: 'Edit Vessel' });
    expect(form).toBeInTheDocument();
    expect(screen.getByLabelText(/Landing Mode/)).toHaveValue('MergeQueue');
    expect(screen.getByLabelText(/Branch Cleanup/)).toHaveValue('LocalOnly');
    expect(screen.getByLabelText(/Agent Auto-Approve/)).toHaveValue('off');
    expect(screen.getByLabelText('Parallel voyages')).toHaveValue(0);
    expect(screen.getByLabelText('Parallel missions per voyage')).toHaveValue(0);
    expect(screen.getByLabelText('Auto-land small changes')).toBeChecked();
    expect(screen.getByLabelText(/Release Branch Prefix/)).toHaveValue('rel/');

    fireEvent.change(screen.getByLabelText(/Landing Mode/), { target: { value: 'PullRequest' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(updateVessel).toHaveBeenCalledTimes(1));
    const [id, payload] = vi.mocked(updateVessel).mock.calls[0];
    expect(id).toBe('vsl_1');
    expect(payload).toMatchObject({
      landingMode: 'PullRequest',
      branchCleanupPolicy: 'LocalOnly',
      autoApprove: false,
      autoLandEnabled: true,
      autoLandMaxFiles: 5,
      autoLandPathAllowGlobs: ['src/**'],
      releaseBranchPrefix: 'rel/',
      hotfixBranchPrefix: 'fix/',
      requirePassingChecksToLand: true,
      requireMergeQueueForReleaseBranches: true,
      protectedBranchPatterns: ['main'],
      preferredHarborId: 'hbr_1',
    });
    expect(payload).not.toHaveProperty('gitHubTokenOverride');
    expect(payload).not.toHaveProperty('hasGitHubTokenOverride');
  });
});

describe('VesselDetail Dispatch', () => {
  beforeEach(() => {
    vi.mocked(getVessel).mockResolvedValue(vessel);
    vi.mocked(listFleets).mockResolvedValue(page([{ id: 'flt_1', name: 'Main' }]) as never);
    vi.mocked(listMissionSummaries).mockResolvedValue(page([]) as never);
    vi.mocked(listPipelines).mockResolvedValue(page([]) as never);
    vi.mocked(getVesselReadiness).mockResolvedValue(null as never);
    vi.mocked(getVesselLandingPreview).mockResolvedValue(null as never);
  });

  it('opens Dispatch with this vessel preselected', async () => {
    function DispatchProbe() {
      const state = useLocation().state as { fromVessel?: boolean; vesselId?: string } | null;
      return <div>dispatch for {state?.fromVessel ? state.vesselId : 'none'}</div>;
    }
    render(
      <MemoryRouter initialEntries={['/vessels/vsl_1']}>
        <Routes>
          <Route path="/vessels/:id" element={<VesselDetail />} />
          <Route path="/dispatch" element={<DispatchProbe />} />
        </Routes>
      </MemoryRouter>,
    );

    fireEvent.click(await screen.findByRole('button', { name: 'Dispatch' }));
    expect(await screen.findByText('dispatch for vsl_1')).toBeInTheDocument();
  });
});

describe('VesselDetail View History', () => {
  beforeEach(() => {
    vi.mocked(getVessel).mockResolvedValue(vessel);
    vi.mocked(listFleets).mockResolvedValue(page([{ id: 'flt_1', name: 'Main' }]) as never);
    vi.mocked(listMissionSummaries).mockResolvedValue(page([]) as never);
    vi.mocked(listPipelines).mockResolvedValue(page([]) as never);
    vi.mocked(getVesselReadiness).mockResolvedValue(null as never);
    vi.mocked(getVesselLandingPreview).mockResolvedValue(null as never);
  });

  function renderDetail() {
    function HistoryProbe() {
      return <div>history at {useLocation().pathname}</div>;
    }
    render(
      <MemoryRouter initialEntries={['/vessels/vsl_1']}>
        <Routes>
          <Route path="/vessels/:id" element={<VesselDetail />} />
          <Route path="/vessels/:id/history" element={<HistoryProbe />} />
        </Routes>
      </MemoryRouter>,
    );
  }

  it('the header button opens the vessel history', async () => {
    renderDetail();
    fireEvent.click(await screen.findByRole('button', { name: 'View History' }));
    expect(await screen.findByText('history at /vessels/vsl_1/history')).toBeInTheDocument();
  });

  it('the action menu item opens the vessel history', async () => {
    renderDetail();
    fireEvent.click(await screen.findByRole('button', { name: 'Actions' }));
    fireEvent.click(await screen.findByRole('menuitem', { name: 'View History' }));
    expect(await screen.findByText('history at /vessels/vsl_1/history')).toBeInTheDocument();
  });
});

describe('VesselDetail missions table', () => {
  beforeEach(() => {
    localStorage.clear();
    vi.mocked(getVessel).mockResolvedValue(vessel);
    vi.mocked(listFleets).mockResolvedValue(page([{ id: 'flt_1', name: 'Main' }]) as never);
    vi.mocked(listMissionSummaries).mockResolvedValue(page([{
      id: 'msn_1', title: 'Add request logging', status: 'InProgress', captainId: 'cpt_1',
      branchName: 'armada/captain-1/msn_1-a-very-long-branch-name', vesselId: 'vsl_1', createdUtc: '2026-10-01T00:00:00Z',
    }]) as never);
    vi.mocked(listPipelines).mockResolvedValue(page([]) as never);
    vi.mocked(getVesselReadiness).mockResolvedValue(null as never);
    vi.mocked(getVesselLandingPreview).mockResolvedValue(null as never);
  });

  it('uses the shared table: Mission and ID are locked in the column chooser', async () => {
    render(
      <MemoryRouter initialEntries={['/vessels/vsl_1']}>
        <Routes><Route path="/vessels/:id" element={<VesselDetail />} /></Routes>
      </MemoryRouter>,
    );
    await screen.findByText('Add request logging');
    const wrap = document.querySelector('[data-table="vessel-detail-missions"]') as HTMLElement;
    await act(async () => { fireEvent.click(within(wrap).getByRole('button', { name: /^Columns/ })); });
    const menu = screen.getByRole('menu', { name: 'Choose visible columns' });
    expect(within(menu).getByRole('menuitemcheckbox', { name: /Mission/ })).toHaveAttribute('aria-disabled', 'true');
    expect(within(menu).getByRole('menuitemcheckbox', { name: /^ID/ })).toHaveAttribute('aria-disabled', 'true');
    expect(within(menu).getByRole('menuitemcheckbox', { name: /Branch/ })).not.toHaveAttribute('aria-disabled');
  });

  it('keeps the mission ID in its own column instead of a second line under the title, and branches on one line', async () => {
    render(
      <MemoryRouter initialEntries={['/vessels/vsl_1']}>
        <Routes><Route path="/vessels/:id" element={<VesselDetail />} /></Routes>
      </MemoryRouter>,
    );
    const title = await screen.findByText('Add request logging');
    const missionCell = title.closest('td') as HTMLElement;
    expect(missionCell).toHaveAttribute('data-col', 'mission');
    expect(within(missionCell).queryByText('msn_1')).toBeNull();
    const row = missionCell.closest('tr') as HTMLElement;
    expect(within(row.querySelector('td[data-col="id"]') as HTMLElement).getByText('msn_1')).toBeInTheDocument();
    const branch = within(row.querySelector('td[data-col="branch"]') as HTMLElement).getByText('armada/captain-1/msn_1-a-very-long-branch-name');
    expect(branch).toHaveClass('cell-one-line');
    expect(branch).toHaveAttribute('title', 'armada/captain-1/msn_1-a-very-long-branch-name');
  });
});

describe('VesselDetail completeness (lists are never silently capped)', () => {
  beforeEach(() => {
    localStorage.clear();
    vi.mocked(getVessel).mockReset();
    vi.mocked(listFleets).mockResolvedValue(page([{ id: 'flt_1', name: 'Main' }]) as never);
    vi.mocked(listPipelines).mockResolvedValue(page([]) as never);
    vi.mocked(getVesselReadiness).mockResolvedValue(null as never);
    vi.mocked(getVesselLandingPreview).mockResolvedValue(null as never);
  });

  it('reads the vessel by id, so a vessel beyond the first page of the vessel list still opens', async () => {
    vi.mocked(getVessel).mockResolvedValue({ ...vessel, id: 'vsl_1500', name: 'vessel-1500' } as Vessel);
    vi.mocked(listMissionSummaries).mockResolvedValue(page([]) as never);
    render(
      <MemoryRouter initialEntries={['/vessels/vsl_1500']}>
        <Routes><Route path="/vessels/:id" element={<VesselDetail />} /></Routes>
      </MemoryRouter>,
    );
    expect(await screen.findAllByText('vessel-1500')).not.toHaveLength(0);
    expect(getVessel).toHaveBeenCalledWith('vsl_1500');
    expect(screen.queryByText('Vessel not found.')).toBeNull();
  });

  it('pages the missions table on the server and shows the true total', async () => {
    vi.mocked(getVessel).mockResolvedValue(vessel);
    vi.mocked(listMissionSummaries).mockResolvedValue({
      success: true, pageNumber: 1, pageSize: 25, totalPages: 60, totalRecords: 1500, totalMs: 1,
      objects: [{ id: 'msn_1', title: 'Newest mission', status: 'Complete', captainId: null, branchName: null, vesselId: 'vsl_1', createdUtc: '2026-10-01T00:00:00Z' }],
    } as never);
    render(
      <MemoryRouter initialEntries={['/vessels/vsl_1']}>
        <Routes><Route path="/vessels/:id" element={<VesselDetail />} /></Routes>
      </MemoryRouter>,
    );
    await screen.findByText('Newest mission');
    expect(listMissionSummaries).toHaveBeenCalledWith({ pageNumber: 1, pageSize: 25, filters: { vesselId: 'vsl_1' } });
    const wrap = document.querySelector('[data-table="vessel-detail-missions"]') as HTMLElement;
    expect(within(wrap).getByText(/1,500 records/)).toBeInTheDocument();
  });
});
