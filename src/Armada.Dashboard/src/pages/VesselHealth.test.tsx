import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import VesselHealth from './VesselHealth';
import {
  enumerateVesselHealth,
  evaluateVesselHealth,
  getJob,
  getVesselHealthSummary,
  listFleets,
  listJobs,
  listVessels,
} from '../api/client';
import type { VesselHealth as Row, VesselHealthSummary } from '../types/models';

const translate = (text: string, params?: Record<string, string | number | null | undefined>) => {
  if (!params) return text;
  return Object.entries(params).reduce((acc, [k, v]) => acc.split(`{{${k}}}`).join(v == null ? '' : String(v)), text);
};

const pushToast = vi.fn();
let tenantAdmin = true;

vi.mock('../api/client', () => ({
  enumerateVesselHealth: vi.fn(),
  getVesselHealthSummary: vi.fn(),
  evaluateVesselHealth: vi.fn(),
  getJob: vi.fn(),
  listJobs: vi.fn(),
  listFleets: vi.fn(),
  listVessels: vi.fn(),
  getVesselHealth: vi.fn(),
  getVesselBranches: vi.fn(),
}));

vi.mock('../components/fleetActions/RunActionModal', () => ({
  default: (props: { vesselIds: string[]; initialKind?: string }) => (
    <div data-testid="run-action-modal">{props.vesselIds.join(',') + '|' + (props.initialKind ?? '')}</div>
  ),
}));

vi.mock('../context/LocaleContext', () => ({
  useLocale: () => ({
    t: translate,
    locale: 'en',
    formatDateTime: (v: string | null | undefined) => v ?? '',
    formatRelativeTime: (v: string | null | undefined) => (v ? `rel(${v})` : '-'),
  }),
}));

vi.mock('../context/NotificationContext', () => ({
  useNotifications: () => ({ pushToast }),
}));

vi.mock('../context/AuthContext', () => ({
  useAuth: () => ({ isAdmin: tenantAdmin, isTenantAdmin: tenantAdmin }),
}));

function page<T>(objects: T[], totalRecords = objects.length) {
  return { success: true, pageNumber: 1, pageSize: 25, totalPages: Math.max(1, Math.ceil(totalRecords / 25)), totalRecords, totalMs: 1, objects };
}

function summary(partial: Partial<VesselHealthSummary>): VesselHealthSummary {
  return { totalVessels: 0, pass: 0, warn: 0, fail: 0, unknown: 0, notApplicable: 0, notEvaluated: 0, outdatedMajorVessels: 0, highOrCriticalVulnerabilityVessels: 0, ...partial };
}

const unknownRow: Row = {
  vesselId: 'vsl_new',
  vesselName: 'never-evaluated',
  overallStatus: 'Unknown',
  dependencyStatus: 'Unknown',
  vulnerabilityStatus: 'Unknown',
  testInfraStatus: 'Unknown',
  ciStatus: 'Unknown',
  divergenceStatus: 'Unknown',
  workingTreeStatus: 'Unknown',
  branchStatus: 'Unknown',
  readinessStatus: 'Unknown',
  missionOutcomeStatus: 'Unknown',
};

const failingRow: Row = {
  ...unknownRow,
  id: 'vhl_1',
  vesselId: 'vsl_app',
  vesselName: 'app',
  fleetId: 'flt_1',
  fleetName: 'Default',
  overallStatus: 'Fail',
  divergenceStatus: 'Warn',
  aheadOfDefault: 2,
  behindDefault: 7,
  isDirty: true,
  untrackedCount: 3,
  branchCount: 5,
  staleBranchCount: 1,
  outdatedCount: 23,
  outdatedMajorCount: 5,
  dependencyStatus: 'Fail',
  vulnerableCount: 3,
  maxVulnerabilitySeverity: 'High',
  vulnerabilityStatus: 'Fail',
  evaluatedUtc: '2026-10-01T00:00:00Z',
};

function LocationProbe() {
  const location = useLocation();
  return <div data-testid="location">{location.pathname}{location.search}</div>;
}

function renderPage(entry = '/vessels/health') {
  return render(
    <MemoryRouter initialEntries={[entry]}>
      <Routes>
        <Route path="/vessels/health" element={<><VesselHealth /><LocationProbe /></>} />
        <Route path="*" element={<LocationProbe />} />
      </Routes>
    </MemoryRouter>,
  );
}

describe('VesselHealth page', () => {
  beforeEach(() => {
    tenantAdmin = true;
    try { localStorage.clear(); } catch { /* ignore */ }
    vi.mocked(listFleets).mockResolvedValue(page([{ id: 'flt_1', name: 'Default' } as never]));
    vi.mocked(listVessels).mockResolvedValue(page([{ id: 'vsl_app', name: 'app', defaultBranch: 'main' } as never]));
    vi.mocked(listJobs).mockResolvedValue(page([]));
  });
  afterEach(() => vi.clearAllMocks());

  it('shows the Import repositories CTA when the tenant has no vessels', async () => {
    vi.mocked(enumerateVesselHealth).mockResolvedValue(page([]));
    vi.mocked(getVesselHealthSummary).mockResolvedValue(summary({}));
    renderPage();
    const cta = await screen.findByRole('link', { name: 'Import repositories' });
    expect(cta).toHaveAttribute('href', '/vessels/import');
    expect(screen.getByText('No vessels yet')).toBeInTheDocument();
  });

  it('offers Evaluate now when vessels exist but none were evaluated, and handles a 202', async () => {
    vi.mocked(enumerateVesselHealth).mockResolvedValue(page([unknownRow]));
    vi.mocked(getVesselHealthSummary).mockResolvedValue(summary({ totalVessels: 1, notEvaluated: 1 }));
    vi.mocked(evaluateVesselHealth).mockResolvedValue({ jobId: 'job_1', alreadyRunning: false, vesselCount: 1 });
    vi.mocked(getJob).mockResolvedValue({ id: 'job_1', status: 'Running', progress: 40 } as never);
    renderPage();
    expect(await screen.findByText('No vessel has been evaluated yet.')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Evaluate now' }));
    await waitFor(() => expect(evaluateVesselHealth).toHaveBeenCalledWith({ Force: true }));
    await waitFor(() => expect(pushToast).toHaveBeenCalledWith('success', 'Evaluation started for 1 vessel.'));
    expect(await screen.findByText('Evaluating... 40%')).toBeInTheDocument();
  });

  it('on 409 warns and still tracks the running job', async () => {
    vi.mocked(enumerateVesselHealth).mockResolvedValue(page([failingRow]));
    vi.mocked(getVesselHealthSummary).mockResolvedValue(summary({ totalVessels: 1, fail: 1 }));
    vi.mocked(evaluateVesselHealth).mockResolvedValue({ jobId: 'job_other', alreadyRunning: true, vesselCount: 0 });
    vi.mocked(getJob).mockResolvedValue({ id: 'job_other', status: 'Running', progress: 70 } as never);
    renderPage();
    fireEvent.click(await screen.findByRole('button', { name: 'Evaluate all' }));
    await waitFor(() => expect(pushToast).toHaveBeenCalledWith('warning', 'An evaluation is already running. Showing its progress instead.'));
    await waitFor(() => expect(getJob).toHaveBeenCalledWith('job_other'));
    expect(await screen.findByText('Evaluating... 70%')).toBeInTheDocument();
  });

  it('hides admin-only evaluation controls from non-admins', async () => {
    tenantAdmin = false;
    vi.mocked(enumerateVesselHealth).mockResolvedValue(page([unknownRow]));
    vi.mocked(getVesselHealthSummary).mockResolvedValue(summary({ totalVessels: 1, notEvaluated: 1 }));
    renderPage();
    expect(await screen.findByText('No vessel has been evaluated yet.')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Evaluate now' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Evaluate all' })).not.toBeInTheDocument();
  });

  it('reads filters from the URL into the enumerate request and renders rows', async () => {
    vi.mocked(enumerateVesselHealth).mockResolvedValue(page([failingRow]));
    vi.mocked(getVesselHealthSummary).mockResolvedValue(summary({ totalVessels: 1, fail: 1 }));
    renderPage('/vessels/health?overall=Fail&deps=Fail&dirty=yes&sort=BehindDefault&dir=desc&page=2');
    expect(await screen.findByText('app')).toBeInTheDocument();
    expect(enumerateVesselHealth).toHaveBeenCalledWith(expect.objectContaining({
      OverallStatus: ['Fail'],
      DependencyStatus: ['Fail'],
      IsDirty: true,
      SortBy: 'BehindDefault',
      SortDescending: true,
      PageNumber: 2,
    }));
    // Divergence cell names the base branch in its tooltip.
    expect(screen.getByLabelText('2 ahead and 7 behind main')).toBeInTheDocument();
    // Sortable header exposes aria-sort.
    expect(screen.getByRole('columnheader', { name: /Divergence/ })).toHaveAttribute('aria-sort', 'none');
  });

  it('clicking a summary tile writes the overall filter to the URL and resets the page', async () => {
    vi.mocked(enumerateVesselHealth).mockResolvedValue(page([failingRow]));
    vi.mocked(getVesselHealthSummary).mockResolvedValue(summary({ totalVessels: 3, fail: 1, warn: 1, pass: 1 }));
    renderPage('/vessels/health?page=3');
    const failTile = await screen.findByTitle('Show vessels that fail');
    fireEvent.click(failTile);
    await waitFor(() => expect(screen.getByTestId('location').textContent).toBe('/vessels/health?overall=Fail'));
  });

  it('keeps filters and offers retry when the fetch fails', async () => {
    vi.mocked(getVesselHealthSummary).mockResolvedValue(summary({ totalVessels: 1, fail: 1 }));
    vi.mocked(enumerateVesselHealth)
      .mockRejectedValueOnce(new Error('boom'))
      .mockResolvedValueOnce(page([failingRow]));
    renderPage('/vessels/health?overall=Fail');
    expect(await screen.findByText('Could not load vessel health: boom')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }));
    expect(await screen.findByText('app')).toBeInTheDocument();
    // Both the failed fetch and the retry carry the filter (selecting by position would also accept a retry
    // that dropped it if a third call happened).
    await waitFor(() => expect(enumerateVesselHealth).toHaveBeenCalledTimes(2));
    expect(vi.mocked(enumerateVesselHealth).mock.calls.map(([query]) => query.OverallStatus)).toEqual([['Fail'], ['Fail']]);
    expect(screen.getByTestId('location').textContent).toBe('/vessels/health?overall=Fail');
  });

  it('shows a filtered empty state with a clear-filters action', async () => {
    vi.mocked(enumerateVesselHealth).mockResolvedValue(page([]));
    vi.mocked(getVesselHealthSummary).mockResolvedValue(summary({ totalVessels: 2, pass: 2 }));
    renderPage('/vessels/health?overall=Fail');
    expect(await screen.findByText('No vessels match the current filters.')).toBeInTheDocument();
    expect(screen.queryByText('No vessels yet')).not.toBeInTheDocument();
  });

  it('bulk bar: Run action opens the RunActionModal with the selected vessels and Mission kind', async () => {
    vi.mocked(enumerateVesselHealth).mockResolvedValue(page([failingRow]));
    vi.mocked(getVesselHealthSummary).mockResolvedValue(summary({ totalVessels: 1, fail: 1 }));
    renderPage();
    fireEvent.click(await screen.findByLabelText('Select app'));
    expect(screen.getByText('1 vessel selected')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Run action...' }));
    expect(await screen.findByTestId('run-action-modal')).toHaveTextContent('vsl_app|Mission');
  });
  it('uses the shared column chooser: identity columns locked, secondary columns hidden by default', async () => {
    vi.mocked(enumerateVesselHealth).mockResolvedValue(page([failingRow]));
    vi.mocked(getVesselHealthSummary).mockResolvedValue(summary({ totalVessels: 1, fail: 1 }));
    renderPage();
    await screen.findByText('app');
    const headers = screen.getAllByRole('columnheader').map((h) => h.textContent ?? '');
    expect(headers.some((h) => h.startsWith('Last commit'))).toBe(false);
    expect(headers.some((h) => h.startsWith('Evaluated'))).toBe(false);
    fireEvent.click(screen.getByRole('button', { name: /^Columns/ }));
    const menu = await screen.findByRole('menu', { name: 'Choose visible columns' });
    expect(within(menu).getByRole('menuitemcheckbox', { name: /^Vessel/ })).toHaveAttribute('aria-disabled', 'true');
    expect(within(menu).getByRole('menuitemcheckbox', { name: /^Overall/ })).toHaveAttribute('aria-disabled', 'true');
    expect(within(menu).getByRole('menuitemcheckbox', { name: /^Last commit/ })).toHaveAttribute('aria-checked', 'false');
    // The old page-local chooser is gone: only one Columns button.
    expect(screen.getAllByRole('button', { name: /^Columns/ })).toHaveLength(1);
  });

  it('honors a column choice saved by the previous chooser (armada_table_vessel-health)', async () => {
    localStorage.setItem('armada_table_vessel-health', JSON.stringify({ pageSize: 25, hiddenColumns: ['ci', 'fleet'], defaultsVersion: 1 }));
    vi.mocked(enumerateVesselHealth).mockResolvedValue(page([failingRow]));
    vi.mocked(getVesselHealthSummary).mockResolvedValue(summary({ totalVessels: 1, fail: 1 }));
    renderPage();
    await screen.findByText('app');
    const headers = screen.getAllByRole('columnheader').map((h) => h.textContent ?? '');
    expect(headers.some((h) => h.startsWith('CI'))).toBe(false);
    expect(headers.some((h) => h.startsWith('Fleet'))).toBe(false);
    expect(headers.some((h) => h.startsWith('Divergence'))).toBe(true);
    // The stored list was complete for the same defaults version, so default-hidden columns the user had turned on stay on.
    expect(headers.some((h) => h.startsWith('Last commit'))).toBe(true);
  });

  it('shows the checked-out branch in its own one-line column, not under the vessel name', async () => {
    vi.mocked(enumerateVesselHealth).mockResolvedValue(page([{ ...failingRow, currentBranch: 'feature/a-very-long-branch-name' }]));
    vi.mocked(getVesselHealthSummary).mockResolvedValue(summary({ totalVessels: 1, fail: 1 }));
    renderPage();
    const branch = await screen.findByText('feature/a-very-long-branch-name');
    expect(branch).toHaveClass('cell-one-line');
    expect(branch).toHaveAttribute('title', 'Checked-out branch: feature/a-very-long-branch-name');
    expect(branch.closest('td')).toHaveAttribute('data-col', 'branch');
    expect(screen.getByText('app').closest('td')).not.toContainElement(branch);
    // The mono cell style applies to the cells only; the header keeps the header font.
    expect(branch.closest('td')).toHaveClass('mono');
    expect(document.querySelector('th[data-col="branch"]')).not.toHaveClass('mono');
  });
});
