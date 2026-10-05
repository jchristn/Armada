import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import VesselDetail from './VesselDetail';
import { listFleets, listMissionSummaries, listPipelines, listVessels, getVesselReadiness, getVesselLandingPreview, updateVessel } from '../api/client';
import { translateTemplate } from '../i18n/runtime';
import type { Vessel } from '../types/models';

vi.mock('../api/client', () => ({
  listVessels: vi.fn(),
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
  allowConcurrentMissions: true, autoApprove: false, defaultPipelineId: null, active: true,
  preferredHarborId: 'hbr_1',
  createdUtc: '2026-10-01T00:00:00Z', lastUpdateUtc: '2026-10-01T00:00:00Z',
} as unknown as Vessel;

describe('VesselDetail Edit (F13)', () => {
  beforeEach(() => {
    vi.mocked(listVessels).mockResolvedValue(page([vessel]) as never);
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
    expect(screen.getByLabelText('Allow Concurrent Missions')).toBeChecked();
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
      allowConcurrentMissions: true,
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
