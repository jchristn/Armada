import { act, fireEvent, render, screen, within } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import ReleaseDetail from './ReleaseDetail';
import { getRelease, getReleaseGitHubPullRequests, listCheckRuns, listDeployments, listObjectives, listVessels, listVoyages, listWorkflowProfiles } from '../api/client';

const page = { success: true, pageNumber: 1, pageSize: 9999, totalPages: 1, totalRecords: 0, totalMs: 1, objects: [] };

vi.mock('../api/client', () => ({
  createRelease: vi.fn(),
  deleteRelease: vi.fn(),
  getRelease: vi.fn(),
  getReleaseGitHubPullRequests: vi.fn(),
  listCheckRuns: vi.fn(),
  listDeployments: vi.fn(),
  listObjectives: vi.fn(),
  listVessels: vi.fn(),
  listVoyages: vi.fn(),
  listWorkflowProfiles: vi.fn(),
  refreshRelease: vi.fn(),
  updateRelease: vi.fn(),
}));

vi.mock('../context/AuthContext', () => ({ useAuth: () => ({ isAdmin: true, isTenantAdmin: true }) }));
const localeValue = {
  t: (text: string) => text,
  formatDateTime: (value: string | null | undefined) => value ?? '',
  formatRelativeTime: (value: string | null | undefined) => value ?? '',
};
vi.mock('../context/LocaleContext', () => ({ useLocale: () => localeValue }));
vi.mock('../context/NotificationContext', () => ({ useNotifications: () => ({ pushToast: vi.fn() }) }));

describe('ReleaseDetail create mode', () => {
  beforeEach(() => {
    for (const fn of [listCheckRuns, listDeployments, listObjectives, listVessels, listVoyages, listWorkflowProfiles]) {
      vi.mocked(fn).mockResolvedValue(page as never);
    }
  });

  it('opens the create form on the static releases/new route (Draft Release from a voyage, backlog item, or check)', async () => {
    render(
      <MemoryRouter initialEntries={['/releases/new']}>
        <Routes>
          <Route path="/releases/new" element={<ReleaseDetail />} />
          <Route path="/releases/:id" element={<ReleaseDetail />} />
        </Routes>
      </MemoryRouter>,
    );

    expect((await screen.findAllByText('Create Release')).length).toBeGreaterThan(0);
    expect(screen.queryByText('Loading...')).toBeNull();
    expect(vi.mocked(getRelease)).not.toHaveBeenCalled();
  });
});

describe('ReleaseDetail artifacts table', () => {
  beforeEach(() => {
    localStorage.clear();
    for (const fn of [listCheckRuns, listDeployments, listObjectives, listVessels, listVoyages, listWorkflowProfiles]) {
      vi.mocked(fn).mockResolvedValue(page as never);
    }
    vi.mocked(getReleaseGitHubPullRequests).mockResolvedValue([] as never);
    vi.mocked(getRelease).mockResolvedValue({
      id: 'rel_1', tenantId: 'default', userId: null, vesselId: null, workflowProfileId: null, title: 'v1.2.0',
      version: '1.2.0', tagName: null, summary: null, notes: null, status: 'Draft', voyageIds: [], missionIds: [], checkRunIds: [],
      artifacts: [{ sourceType: 'CheckRun', sourceId: 'chk_1', path: 'artifacts/build/output/very/deep/folder/package-1.2.0.nupkg', sizeBytes: 2048, lastWriteUtc: '2026-10-01T00:00:00Z' }],
      createdUtc: '2026-10-01T00:00:00Z', lastUpdateUtc: '2026-10-01T00:00:00Z', publishedUtc: null,
    } as never);
  });

  it('renders artifacts through the shared table with Path locked and long paths on one line', async () => {
    render(
      <MemoryRouter initialEntries={['/releases/rel_1']}>
        <Routes><Route path="/releases/:id" element={<ReleaseDetail />} /></Routes>
      </MemoryRouter>,
    );
    const path = await screen.findByText('artifacts/build/output/very/deep/folder/package-1.2.0.nupkg');
    expect(path).toHaveClass('cell-one-line');
    expect(path).toHaveAttribute('title', 'artifacts/build/output/very/deep/folder/package-1.2.0.nupkg');
    // Source type and check link share one non-wrapping line.
    expect(screen.getByRole('link', { name: 'chk_1' }).closest('td')).toHaveClass('cell-nowrap');

    const wrap = document.querySelector('[data-table="release-detail-artifacts"]') as HTMLElement;
    await act(async () => { fireEvent.click(within(wrap).getByRole('button', { name: /^Columns/ })); });
    const menu = screen.getByRole('menu', { name: 'Choose visible columns' });
    expect(within(menu).getByRole('menuitemcheckbox', { name: /Path/ })).toHaveAttribute('aria-disabled', 'true');
    expect(within(menu).getByRole('menuitemcheckbox', { name: /Size/ })).not.toHaveAttribute('aria-disabled');
  });
});
