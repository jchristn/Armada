import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import FleetActionRunDetail, { RUN_DETAIL_REFRESH_SECONDS } from './FleetActionRunDetail';
import {
  cancelFleetActionRun,
  enumerateFleetActionRunTargets,
  getFleetActionRun,
  getFleetActionRunTarget,
} from '../api/client';
import { translateTemplate } from '../i18n/runtime';
import type { FleetActionRun, FleetActionRunStatus, FleetActionRunTargetSummary } from '../types/models';

vi.mock('../api/client', () => ({
  cancelFleetActionRun: vi.fn(),
  enumerateFleetActionRunTargets: vi.fn(),
  getFleetAction: vi.fn(),
  getFleetActionRun: vi.fn(),
  getFleetActionRunTarget: vi.fn(),
  enumerateFleetActions: vi.fn(),
  getVessel: vi.fn(),
  listPipelines: vi.fn(),
  runFleetAction: vi.fn(),
  runAdHocFleetAction: vi.fn(),
}));

const localeValue = {
  t: (text: string, params?: Record<string, string | number | null | undefined>) => translateTemplate('en', text, null, params),
  locale: 'en',
  formatDateTime: (v: string | null | undefined) => v ?? '',
  formatRelativeTime: (v: string | null | undefined) => v ?? '',
};
vi.mock('../context/LocaleContext', () => ({ useLocale: () => localeValue }));
vi.mock('../context/NotificationContext', () => ({ useNotifications: () => ({ pushToast: vi.fn() }) }));
vi.mock('../context/AuthContext', () => ({ useAuth: () => ({ isAdmin: true, isTenantAdmin: true }) }));

function run(status: FleetActionRunStatus, overrides: Partial<FleetActionRun> = {}): FleetActionRun {
  return {
    id: 'far_1', tenantId: 'ten', userId: 'usr', actionId: 'fac_1', actionName: 'Build', kind: 'Command',
    commandText: 'dotnet build', promptTemplate: null, pipelineId: null, persona: null, timeoutSeconds: 300,
    requiresCleanWorkingTree: true, concurrency: 2, status, targetCount: 2, succeededCount: status === 'Completed' ? 2 : 1,
    failedCount: 0, skippedCount: 0, cancelledCount: 0, startedUtc: '2026-10-03T00:00:00Z',
    completedUtc: status === 'Completed' ? '2026-10-03T00:00:10Z' : null, createdUtc: '2026-10-03T00:00:00Z', lastUpdateUtc: '2026-10-03T00:00:00Z',
    ...overrides,
  };
}

const target: FleetActionRunTargetSummary = {
  id: 'fat_1', runId: 'far_1', vesselId: 'vsl_1', vesselName: 'api', status: 'Skipped', skipReason: 'DirtyTree', failureReason: null,
  exitCode: null, outputTruncated: false, outputLength: 0, errorLength: 0, renderedLength: 12, voyageId: null,
  startedUtc: null, completedUtc: null, durationMs: null, createdUtc: '2026-10-03T00:00:00Z', lastUpdateUtc: '2026-10-03T00:00:00Z',
};

function renderPage() {
  render(
    <MemoryRouter initialEntries={['/fleet-actions/runs/far_1']}>
      <Routes>
        <Route path="/fleet-actions/runs/:id" element={<FleetActionRunDetail />} />
      </Routes>
    </MemoryRouter>,
  );
}

const tick = async (seconds: number) => {
  await act(async () => { await vi.advanceTimersByTimeAsync(seconds * 1000); });
};

describe('FleetActionRunDetail', () => {
  beforeEach(() => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    vi.mocked(enumerateFleetActionRunTargets).mockResolvedValue({ success: true, pageNumber: 1, pageSize: 25, totalPages: 1, totalRecords: 1, totalMs: 1, objects: [target] });
  });

  afterEach(() => {
    vi.clearAllMocks();
    vi.useRealTimers();
  });

  it('auto-refreshes every 5 s while active and stops once the run completes', async () => {
    vi.mocked(getFleetActionRun)
      .mockResolvedValueOnce({ run: run('Running'), targets: [] })
      .mockResolvedValueOnce({ run: run('Running'), targets: [] })
      .mockResolvedValue({ run: run('Completed'), targets: [] });

    renderPage();
    expect(await screen.findByText('Live: refreshing every 5 s.')).toBeInTheDocument();
    expect(screen.getByText('Working tree has uncommitted changes')).toBeInTheDocument();
    expect(getFleetActionRun).toHaveBeenCalledTimes(1);

    await tick(RUN_DETAIL_REFRESH_SECONDS);
    await waitFor(() => expect(getFleetActionRun).toHaveBeenCalledTimes(2));

    await tick(RUN_DETAIL_REFRESH_SECONDS);
    await waitFor(() => expect(getFleetActionRun).toHaveBeenCalledTimes(3));
    expect(await screen.findByText('Run finished; auto-refresh stopped.')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Cancel run' })).not.toBeInTheDocument();

    await tick(RUN_DETAIL_REFRESH_SECONDS * 4);
    expect(getFleetActionRun).toHaveBeenCalledTimes(3);
  });

  it('pauses auto-refresh while the output drawer is open', async () => {
    vi.mocked(getFleetActionRun).mockResolvedValue({ run: run('Running'), targets: [] });
    vi.mocked(getFleetActionRunTarget).mockResolvedValue({
      ...target, tenantId: 'ten', renderedText: 'dotnet build', outputText: 'Build succeeded.', errorText: null,
    });
    renderPage();
    await screen.findByText('Live: refreshing every 5 s.');

    fireEvent.click(screen.getByRole('button', { name: 'View output for api' }));
    expect(await screen.findByText('Build succeeded.')).toBeInTheDocument();
    expect(screen.getByText('Auto-refresh paused while a panel is open.')).toBeInTheDocument();
    const calls = vi.mocked(getFleetActionRun).mock.calls.length;
    await tick(RUN_DETAIL_REFRESH_SECONDS * 3);
    expect(getFleetActionRun).toHaveBeenCalledTimes(calls);
  });

  it('cancels an active run after confirmation', async () => {
    vi.mocked(getFleetActionRun).mockResolvedValue({ run: run('Running'), targets: [] });
    vi.mocked(cancelFleetActionRun).mockResolvedValue(run('Cancelled'));
    renderPage();
    fireEvent.click(await screen.findByRole('button', { name: 'Cancel run' }));
    // Custom confirm dialog, not window.confirm.
    expect(screen.getByText(/Pending targets are cancelled/)).toBeInTheDocument();
    const buttons = screen.getAllByRole('button', { name: 'Cancel run' });
    fireEvent.click(buttons[buttons.length - 1]);
    await waitFor(() => expect(cancelFleetActionRun).toHaveBeenCalledWith('far_1'));
  });
});
