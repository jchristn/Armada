import { act, fireEvent, render, screen, within } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import type { ReactElement } from 'react';
import CaptainDetail from './CaptainDetail';
import CheckRunDetail from './CheckRunDetail';
import Dashboard from './Dashboard';
import Doctor from './Doctor';
import FleetDetail from './FleetDetail';
import PipelineDetail from './PipelineDetail';
import {
  getCaptain,
  getCheckRun,
  getDoctor,
  getPipeline,
  getStatus,
  listCaptains,
  listFleets,
  listMissionSummaries,
  listPersonas,
  listPipelines,
  listVessels,
  enumerateFleetActionRuns,
} from '../api/client';
import { translateTemplate } from '../i18n/runtime';

// Shared-table migration checks for detail pages and Home: each table renders through DataTable (column chooser,
// identity columns locked) and keeps secondary values off a second line.

vi.mock('../api/client', () => ({
  createCaptain: vi.fn(), getCaptain: vi.fn(), getCaptainTools: vi.fn(), getCaptainLog: vi.fn(), stopCaptain: vi.fn(),
  unquarantineCaptain: vi.fn(), recallCaptain: vi.fn(), getMission: vi.fn(), listMissionSummaries: vi.fn(),
  updateCaptain: vi.fn(), deleteCaptain: vi.fn(), setCaptainCliPermissionPolicy: vi.fn(),
  deleteCheckRun: vi.fn(), getCheckRun: vi.fn(), getVessel: vi.fn(), getWorkflowProfile: vi.fn(), listCheckRuns: vi.fn(), retryCheckRun: vi.fn(),
  getDoctor: vi.fn(),
  listFleets: vi.fn(), listVessels: vi.fn(), listPipelines: vi.fn(), createFleet: vi.fn(), updateFleet: vi.fn(), deleteFleet: vi.fn(),
  createPipeline: vi.fn(), getPipeline: vi.fn(), updatePipeline: vi.fn(), deletePipeline: vi.fn(), listPersonas: vi.fn(), createVoyage: vi.fn(),
  getStatus: vi.fn(), listCaptains: vi.fn(), listSignals: vi.fn(), deleteMission: vi.fn(), restartMission: vi.fn(), enumerateFleetActionRuns: vi.fn(),
}));

const localeValue = {
  t: (text: string, params?: Record<string, string | number | null | undefined>) => translateTemplate('en', text, null, params),
  locale: 'en',
  formatDateTime: (v: string | null | undefined) => v ?? '',
  formatRelativeTime: (v: string | null | undefined) => v ?? '',
};
vi.mock('../context/LocaleContext', () => ({ useLocale: () => localeValue }));
vi.mock('../context/NotificationContext', () => ({ useNotifications: () => ({ pushToast: vi.fn() }) }));
vi.mock('../context/AuthContext', () => ({ useAuth: () => ({ isAdmin: true, isTenantAdmin: true, user: null }) }));
vi.mock('../context/WebSocketContext', () => ({ useWebSocket: () => ({ connected: true, subscribe: () => () => {}, send: vi.fn() }) }));
vi.mock('../components/MissionHistoryChart', () => ({ default: () => null }));
vi.mock('../components/vessels/health/HealthKpiCards', () => ({ default: () => null }));
vi.mock('../components/captains/CaptainToolViewer', () => ({ default: () => null }));

function page<T>(objects: T[]) {
  return { success: true, pageNumber: 1, pageSize: 9999, totalPages: 1, totalRecords: objects.length, totalMs: 1, objects };
}

function renderAt(path: string, route: string, element: ReactElement) {
  render(
    <MemoryRouter initialEntries={[path]}>
      <Routes><Route path={route} element={element} /></Routes>
    </MemoryRouter>,
  );
}

async function openChooser(tableKey: string) {
  const wrap = document.querySelector(`[data-table="${tableKey}"]`) as HTMLElement;
  expect(wrap).not.toBeNull();
  await act(async () => { fireEvent.click(within(wrap).getByRole('button', { name: /^Columns/ })); });
  return screen.getByRole('menu', { name: 'Choose visible columns' });
}

function cellOf(text: string): HTMLElement {
  return screen.getByText(text).closest('td') as HTMLElement;
}

const mission = {
  id: 'msn_1', title: 'Add request logging', status: 'InProgress', captainId: 'cpt_1', vesselId: 'vsl_1', voyageId: null,
  branchName: 'armada/captain-1/msn_1-a-very-long-branch-name', createdUtc: '2026-10-01T00:00:00Z', completedUtc: null,
};

beforeEach(() => {
  localStorage.clear();
  vi.mocked(listVessels).mockResolvedValue(page([]) as never);
  vi.mocked(listFleets).mockResolvedValue(page([]) as never);
  vi.mocked(listPipelines).mockResolvedValue(page([]) as never);
  vi.mocked(listCaptains).mockResolvedValue(page([]) as never);
  vi.mocked(listPersonas).mockResolvedValue(page([]) as never);
  vi.mocked(listMissionSummaries).mockResolvedValue(page([mission]) as never);
});

describe('Doctor checks table', () => {
  it('locks Check and lets Message be hidden', async () => {
    vi.mocked(getDoctor).mockResolvedValue([{ name: 'Database', status: 'Pass', message: 'Connected' }] as never);
    render(<Doctor />);
    await screen.findByText('Connected');
    const menu = await openChooser('doctor-checks');
    expect(within(menu).getByRole('menuitemcheckbox', { name: /Check/ })).toHaveAttribute('aria-disabled', 'true');
    fireEvent.click(within(menu).getByRole('menuitemcheckbox', { name: /Message/ }));
    expect(screen.queryByText('Connected')).toBeNull();
  });
});

describe('FleetDetail vessels table', () => {
  it('shows the vessel ID in its own locked column, not under the name', async () => {
    vi.mocked(listFleets).mockResolvedValue(page([{ id: 'flt_1', name: 'Main', description: null, defaultPipelineId: null, active: true, createdUtc: '', lastUpdateUtc: '' }]) as never);
    vi.mocked(listVessels).mockResolvedValue(page([{ id: 'vsl_1', fleetId: 'flt_1', name: 'gateway', repoUrl: 'https://example.com/gateway.git', defaultBranch: 'main' }]) as never);
    renderAt('/fleets/flt_1', '/fleets/:id', <FleetDetail />);
    await screen.findByText('gateway');
    expect(cellOf('gateway')).toHaveAttribute('data-col', 'name');
    expect(within(cellOf('gateway')).queryByText('vsl_1')).toBeNull();
    expect(cellOf('vsl_1')).toHaveAttribute('data-col', 'id');
    const menu = await openChooser('fleet-detail-vessels');
    expect(within(menu).getByRole('menuitemcheckbox', { name: /Name/ })).toHaveAttribute('aria-disabled', 'true');
    expect(within(menu).getByRole('menuitemcheckbox', { name: /^ID/ })).toHaveAttribute('aria-disabled', 'true');
  });
});

describe('PipelineDetail stages table', () => {
  it('locks Order and Persona Name and clamps long descriptions with a tooltip', async () => {
    const description = 'Reviews the change for correctness, security and style before it lands on the default branch.';
    vi.mocked(getPipeline).mockResolvedValue({
      id: 'ppl_1', name: 'Reviewed', description: null, isBuiltIn: false, active: true, stages: [
        { id: 'pps_1', order: 1, personaName: 'Worker', isOptional: false, requiresReview: false, reviewDenyAction: null, description },
      ],
    } as never);
    renderAt('/pipelines/Reviewed', '/pipelines/:name', <PipelineDetail />);
    const text = await screen.findByText(description);
    expect(text).toHaveClass('line-clamp-2');
    expect(text.closest('td')).toHaveAttribute('title', description);
    const menu = await openChooser('pipeline-detail-stages');
    expect(within(menu).getByRole('menuitemcheckbox', { name: /Order/ })).toHaveAttribute('aria-disabled', 'true');
    expect(within(menu).getByRole('menuitemcheckbox', { name: /Persona Name/ })).toHaveAttribute('aria-disabled', 'true');
  });
});

describe('CheckRunDetail artifacts table', () => {
  it('keeps long artifact paths on one line and locks Path', async () => {
    const path = 'artifacts/test-results/very/deep/folder/results.trx';
    vi.mocked(getCheckRun).mockResolvedValue({
      id: 'chk_1', type: 'Test', label: 'Unit tests', status: 'Passed', vesselId: null, workflowProfileId: null,
      command: 'dotnet test', output: '', artifacts: [{ path, sizeBytes: 10, lastWriteUtc: '2026-10-01T00:00:00Z' }],
      createdUtc: '2026-10-01T00:00:00Z',
    } as never);
    renderAt('/checks/chk_1', '/checks/:id', <CheckRunDetail />);
    const value = await screen.findByText(path);
    expect(value).toHaveClass('cell-one-line');
    expect(value).toHaveAttribute('title', path);
    const menu = await openChooser('check-run-artifacts');
    expect(within(menu).getByRole('menuitemcheckbox', { name: /Path/ })).toHaveAttribute('aria-disabled', 'true');
  });
});

describe('CaptainDetail missions table', () => {
  it('shows the mission ID in its own column and the branch on one line', async () => {
    vi.mocked(getCaptain).mockResolvedValue({ id: 'cpt_1', name: 'claude-1', runtime: 'ClaudeCode', state: 'Idle', currentMissionId: null, currentDockId: null } as never);
    renderAt('/captains/cpt_1', '/captains/:id', <CaptainDetail />);
    await screen.findByText('Add request logging');
    const missionCell = screen.getByText('Add request logging').closest('td') as HTMLElement;
    expect(within(missionCell).queryByText('msn_1')).toBeNull();
    expect(cellOf('msn_1')).toHaveAttribute('data-col', 'id');
    expect(screen.getByText(mission.branchName)).toHaveClass('cell-one-line');
    const menu = await openChooser('captain-detail-missions');
    expect(within(menu).getByRole('menuitemcheckbox', { name: /Mission/ })).toHaveAttribute('aria-disabled', 'true');
  });
});

describe('Dashboard (home) tables', () => {
  it('migrates voyage progress and recent missions; voyage ID gets its own column and progress stays on one line', async () => {
    vi.mocked(getStatus).mockResolvedValue({
      voyages: [{ voyage: { id: 'vyg_1', title: 'Gateway hardening', status: 'InProgress' }, vesselIds: [], totalMissions: 4, completedMissions: 1, failedMissions: 1 }],
      recentSignals: [],
    } as never);
    vi.mocked(enumerateFleetActionRuns).mockResolvedValue(page([]) as never);
    render(<MemoryRouter><Dashboard /></MemoryRouter>);
    await screen.findByText('Gateway hardening');
    const voyageCell = screen.getByText('Gateway hardening').closest('td') as HTMLElement;
    expect(within(voyageCell).queryByText('vyg_1')).toBeNull();
    expect(cellOf('vyg_1')).toHaveAttribute('data-col', 'id');
    const progressCell = document.querySelector('[data-table="dashboard-voyage-progress"] td[data-col="progress"]') as HTMLElement;
    // The bar and the percentage share one .dashboard-progress-cell row (one-line flex layout in App.css).
    expect(progressCell.querySelector('.dashboard-progress-cell > .progress-bar')).not.toBeNull();
    expect(progressCell.querySelectorAll('.dashboard-progress-cell')).toHaveLength(1);
    expect(document.querySelector('[data-table="dashboard-voyage-progress"] td[data-col="missions"]')).toHaveClass('cell-nowrap');

    const voyageMenu = await openChooser('dashboard-voyage-progress');
    expect(within(voyageMenu).getByRole('menuitemcheckbox', { name: /Voyage/ })).toHaveAttribute('aria-disabled', 'true');
    await act(async () => { fireEvent.keyDown(document.activeElement as Element, { key: 'Escape' }); });

    await screen.findByText('Add request logging');
    const missionMenu = await openChooser('dashboard-recent-missions');
    expect(within(missionMenu).getByRole('menuitemcheckbox', { name: /Mission/ })).toHaveAttribute('aria-disabled', 'true');
    expect(within(missionMenu).getByRole('menuitemcheckbox', { name: /^ID/ })).toHaveAttribute('aria-disabled', 'true');
  });
});
