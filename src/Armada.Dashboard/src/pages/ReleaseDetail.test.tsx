import { render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import ReleaseDetail from './ReleaseDetail';
import { getRelease, listCheckRuns, listDeployments, listObjectives, listVessels, listVoyages, listWorkflowProfiles } from '../api/client';

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
