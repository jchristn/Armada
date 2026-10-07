import { fireEvent, render, screen, within } from '@testing-library/react';
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

describe('Vessels table layout', () => {
  const long = {
    ...vessel,
    id: 'vsl_2',
    name: 'platform',
    repoUrl: 'https://github.example.com/some-very-long-organization-name/a-repository-with-a-really-long-name.git',
    defaultBranch: 'release/2026-10',
    landingMode: 'MergeAndPush',
  } as unknown as Vessel;

  beforeEach(() => {
    localStorage.clear();
    vi.mocked(listVessels).mockResolvedValue(page([vessel, long]) as never);
    vi.mocked(listFleets).mockResolvedValue(page([]) as never);
    vi.mocked(listPipelines).mockResolvedValue(page([]) as never);
    vi.mocked(getVesselGitStatus).mockResolvedValue({ vesselId: 'vsl_1', commitsAhead: 0, commitsBehind: 0 });
    vi.mocked(getVesselBranches).mockResolvedValue({ vesselId: 'vsl_1', branches: [], branchCount: 0 });
  });

  function renderPage() {
    return render(
      <MemoryRouter initialEntries={['/vessels']}>
        <Vessels />
      </MemoryRouter>,
    );
  }

  function cell(row: HTMLElement, col: string): HTMLElement {
    return row.querySelector(`td[data-col="${col}"]`) as HTMLElement;
  }

  it('renders the default landing mode on one line with the detail in the tooltip', async () => {
    renderPage();
    const row = (await screen.findByText('gateway')).closest('tr') as HTMLElement;
    const landing = cell(row, 'landingMode');
    expect(landing.textContent).toBe('Default (global)');
    expect(landing.querySelectorAll('div')).toHaveLength(0);
    expect(landing.querySelector('.cell-one-line')?.getAttribute('title')).toContain('global default');
  });

  it('renders a set landing mode as one line without the stacked summary', async () => {
    renderPage();
    const row = (await screen.findByText('platform')).closest('tr') as HTMLElement;
    const landing = cell(row, 'landingMode');
    expect(landing.textContent).toBe('MergeAndPush');
    expect(landing.querySelector('.cell-one-line')?.getAttribute('title')).toContain('local + push');
  });

  it('keeps the repository on one truncating line and moves the branch to its own column', async () => {
    renderPage();
    const row = (await screen.findByText('platform')).closest('tr') as HTMLElement;
    const repo = cell(row, 'repoUrl');
    expect(repo).toHaveClass('table-url-cell');
    const value = repo.querySelector('.url-value') as HTMLElement;
    expect(value.getAttribute('title')).toBe(long.repoUrl);
    expect(repo.querySelector('.cell-subline')).toBeNull();
    expect(repo.textContent).not.toContain('release/2026-10');
    expect(cell(row, 'defaultBranch').textContent).toBe('release/2026-10');
  });

  it('puts auto-refresh and refresh in the table toolbar next to the record count', async () => {
    const { container } = renderPage();
    await screen.findByText('gateway');
    expect(container.querySelector('.page-header select')).toBeNull();
    const bar = container.querySelector('.data-table .pagination-bar') as HTMLElement;
    expect(bar.textContent).toContain('2 records');
    expect(bar.querySelector('select[aria-label="Auto-refresh interval"]')).not.toBeNull();
    expect(bar.querySelector('.refresh-btn')).not.toBeNull();
  });

  it('offers a column chooser that locks Name and ID and persists hidden columns', async () => {
    renderPage();
    await screen.findByText('gateway');
    fireEvent.click(screen.getByRole('button', { name: /^Columns/ }));
    const menu = await screen.findByRole('menu');
    expect(within(menu).getByRole('menuitemcheckbox', { name: /^Name/ })).toHaveAttribute('aria-disabled', 'true');
    expect(within(menu).getByRole('menuitemcheckbox', { name: /^ID/ })).toHaveAttribute('aria-disabled', 'true');
    fireEvent.click(within(menu).getByRole('menuitemcheckbox', { name: /Sync/ }));
    expect(screen.queryByRole('columnheader', { name: 'Sync' })).not.toBeInTheDocument();
    expect(JSON.parse(localStorage.getItem('armada_columns_vessels') ?? '{}').hidden).toEqual(['sync']);
  });
});
