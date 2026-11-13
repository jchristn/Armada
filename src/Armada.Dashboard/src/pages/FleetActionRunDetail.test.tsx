import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import FleetActionRunDetail, { RUN_DETAIL_REFRESH_SECONDS } from './FleetActionRunDetail';
import {
  cancelFleetActionRun,
  enumerateFleetActionRunTargets,
  enumerateFleetActions,
  getFleetAction,
  getFleetActionRun,
  getFleetActionRunTarget,
  getVessel,
  listPipelines,
  runFleetAction,
} from '../api/client';
import { downloadBlob } from '../lib/chartImage';
import { translateTemplate } from '../i18n/runtime';
import type { FleetAction, FleetActionRun, FleetActionRunStatus, FleetActionRunTargetSummary } from '../types/models';

vi.mock('../lib/chartImage', async (importOriginal) => ({ ...(await importOriginal<typeof import('../lib/chartImage')>()), downloadBlob: vi.fn() }));

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

function fleetAction(id: string, name: string, scanSourceActionKey: string | null, builtInKey: string | null = null): FleetAction {
  return {
    id, tenantId: 'ten', userId: 'usr', name, description: null, kind: 'Mission', commandText: null, promptTemplate: 'Fix {{scan.summary}}',
    pipelineId: null, persona: null, timeoutSeconds: 300, defaultConcurrency: 2, requiresCleanWorkingTree: false, isBuiltIn: true,
    builtInKey, active: true, scanSourceActionKey, createdUtc: '2026-10-01T00:00:00Z', lastUpdateUtc: '2026-10-01T00:00:00Z',
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
    vi.mocked(enumerateFleetActions).mockResolvedValue({ success: true, pageNumber: 1, pageSize: 500, totalPages: 1, totalRecords: 0, totalMs: 1, objects: [] });
    vi.mocked(getFleetAction).mockResolvedValue(fleetAction('fac_1', 'Build', null, 'build'));
    vi.mocked(listPipelines).mockResolvedValue({ success: true, pageNumber: 1, pageSize: 1, totalPages: 1, totalRecords: 0, totalMs: 1, objects: [] });
    vi.mocked(getVessel).mockResolvedValue({ id: 'vsl_1', name: 'api', workingDirectory: '/code/api', defaultBranch: 'main' } as never);
  });

  afterEach(() => {
    vi.clearAllMocks();
    vi.useRealTimers();
  });

  it('auto-refreshes every 5 s while active and stops once the run completes', async () => {
    vi.mocked(getFleetActionRun)
      .mockResolvedValueOnce({ run: run('Running') })
      .mockResolvedValueOnce({ run: run('Running') })
      .mockResolvedValue({ run: run('Completed') });

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
    vi.mocked(getFleetActionRun).mockResolvedValue({ run: run('Running') });
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
    vi.mocked(getFleetActionRun).mockResolvedValue({ run: run('Running') });
    vi.mocked(cancelFleetActionRun).mockResolvedValue(run('Cancelled'));
    renderPage();
    fireEvent.click(await screen.findByRole('button', { name: 'Cancel run' }));
    // Custom confirm dialog, not window.confirm.
    expect(screen.getByText(/Pending targets are cancelled/)).toBeInTheDocument();
    const buttons = screen.getAllByRole('button', { name: 'Cancel run' });
    fireEvent.click(buttons[buttons.length - 1]);
    await waitFor(() => expect(cancelFleetActionRun).toHaveBeenCalledWith('far_1'));
  });

  it('puts the targets pager and refresh in the shared table toolbar and keeps the vessel ID off a second line', async () => {
    localStorage.clear();
    vi.mocked(getFleetActionRun).mockResolvedValue({ run: run('Completed') });
    renderPage();
    const vesselLink = await screen.findByRole('link', { name: 'api' });
    const wrap = document.querySelector('[data-table="fleet-action-run-targets"]') as HTMLElement;
    const bar = wrap.querySelector('.pagination-bar') as HTMLElement;
    expect(within(bar).getByText('1 record')).toBeInTheDocument();
    expect(within(bar).getByTitle('Reload the run and its vessel results now.')).toBeInTheDocument();
    expect(screen.getAllByTitle('Reload the run and its vessel results now.')).toHaveLength(1);
    expect(within(bar).getByRole('button', { name: 'Next' })).toBeDisabled();

    // The vessel cell is one line; its ID is an optional column, hidden by default, and in the tooltip.
    const cell = vesselLink.closest('td') as HTMLElement;
    expect(cell).toHaveAttribute('title', 'vsl_1');
    expect(within(cell).queryByText('vsl_1')).toBeNull();
    expect(wrap.querySelector('th[data-col="vesselId"]')).toBeNull();

    await act(async () => { fireEvent.click(within(bar).getByRole('button', { name: /^Columns/ })); });
    const menu = screen.getByRole('menu', { name: 'Choose visible columns' });
    expect(within(menu).getByRole('menuitemcheckbox', { name: /^Vessel(?! ID)/ })).toHaveAttribute('aria-disabled', 'true');
    fireEvent.click(within(menu).getByRole('menuitemcheckbox', { name: /Vessel ID/ }));
    expect(wrap.querySelector('th[data-col="vesselId"]')).not.toBeNull();
  });

  it('offers the paired fix first and starts a follow-up on the failed and findings vessels', async () => {
    vi.mocked(getFleetActionRun).mockResolvedValue({ run: run('CompletedWithFailures', { actionName: 'Run tests', failedCount: 2, findingsCount: 1, targetCount: 4 }) });
    vi.mocked(getFleetAction).mockResolvedValue({ ...fleetAction('fac_1', 'Run tests', null, 'run-tests'), kind: 'Command', commandText: 'dotnet test' });
    vi.mocked(enumerateFleetActions).mockResolvedValue({ success: true, pageNumber: 1, pageSize: 500, totalPages: 1, totalRecords: 3, totalMs: 1, objects: [
      fleetAction('fac_lint', 'Lint', null, 'lint'),
      fleetAction('fac_fix', 'Fix failing tests', 'run-tests', 'fix-failing-tests'),
      fleetAction('fac_vuln', 'Fix vulnerabilities', 'vulnerability-audit', 'fix-vulnerabilities'),
    ] });
    vi.mocked(runFleetAction).mockResolvedValue({ runId: 'far_2', actionId: 'fac_fix', kind: 'Mission', status: 'Pending', targetCount: 3, concurrency: 2 });
    renderPage();

    expect(await screen.findByRole('button', { name: 'Fix 3 vessels with Fix failing tests' })).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Follow up...' }));
    const dialog = await screen.findByRole('dialog');
    const options = within(dialog).getAllByRole('radio').map((radio) => radio.closest('label')?.querySelector('.follow-up-option-name')?.textContent);
    expect(options).toEqual(['Fix failing tests', 'Lint', 'Fix vulnerabilities']);
    expect(within(dialog).getByText('Suggested')).toBeInTheDocument();
    expect(within(dialog).getByText('Other actions')).toBeInTheDocument();
    expect(within(dialog).getAllByRole('radio')[0]).toBeChecked();
    fireEvent.click(within(dialog).getByRole('button', { name: 'Continue' }));

    expect(await screen.findByText('Targets: vessels from run far_1 with status')).toBeInTheDocument();
    await waitFor(() => expect(screen.getByRole('button', { name: 'Review and run' })).toBeEnabled());
    fireEvent.click(screen.getByRole('button', { name: 'Review and run' }));
    fireEvent.click(screen.getByRole('button', { name: 'Start follow-up run' }));
    await waitFor(() => expect(runFleetAction).toHaveBeenCalledTimes(1));
    const [actionId, body] = vi.mocked(runFleetAction).mock.calls[0];
    expect(actionId).toBe('fac_fix');
    expect(body).toMatchObject({ VesselIds: [], FromRunId: 'far_1', FromTargetStatuses: ['Findings', 'Failed'] });
  });

  it('shows no fix call to action without a paired action and disables Follow up for a run with no targets', async () => {
    vi.mocked(getFleetActionRun).mockResolvedValue({ run: run('Completed', { failedCount: 1, targetCount: 0 }) });
    vi.mocked(enumerateFleetActions).mockResolvedValue({ success: true, pageNumber: 1, pageSize: 500, totalPages: 1, totalRecords: 1, totalMs: 1, objects: [fleetAction('fac_lint', 'Lint', null, 'lint')] });
    renderPage();
    expect(await screen.findByRole('button', { name: 'Follow up...' })).toBeDisabled();
    expect(screen.queryByRole('button', { name: /^Fix / })).not.toBeInTheDocument();
  });

  it('defaults a report-only run to the Report view, pages reports, and downloads them as Markdown', async () => {
    const auditRun = run('Completed', { kind: 'Mission', actionName: 'Security review', missionMode: 'Audit', commandText: null, promptTemplate: 'Review', targetCount: 3, succeededCount: 3 });
    vi.mocked(getFleetActionRun).mockResolvedValue({ run: auditRun });
    const reportTarget = (id: string, vesselName: string, outputText: string | undefined, outputLength: number): FleetActionRunTargetSummary => ({
      ...target, id, vesselId: `vsl_${id}`, vesselName, status: 'Succeeded', skipReason: null, outputText, outputLength,
    });
    const first = [reportTarget('a', 'api', '## Findings\n\n- **Hard-coded token** in config', 40), reportTarget('b', 'web', undefined, 12)];
    const second = [reportTarget('c', 'cli', 'No issues found.', 16)];
    vi.mocked(enumerateFleetActionRunTargets).mockImplementation(async (_runId, query) => {
      if (!query?.includeOutput) return { success: true, pageNumber: 1, pageSize: 25, totalPages: 1, totalRecords: 3, totalMs: 1, objects: [...first, ...second] };
      const size = query.pageSize ?? 10;
      const all = [...first, ...second];
      if (size >= 50) return { success: true, pageNumber: 1, pageSize: size, totalPages: 1, totalRecords: 3, totalMs: 1, objects: all };
      return { success: true, pageNumber: query.pageNumber ?? 1, pageSize: size, totalPages: 2, totalRecords: 3, totalMs: 1, objects: query.pageNumber === 2 ? second : first };
    });
    // A server that ignores IncludeOutput leaves outputText off; the view reads that target directly.
    vi.mocked(getFleetActionRunTarget).mockResolvedValue({ ...reportTarget('b', 'web', undefined, 12), tenantId: 'ten', renderedText: 'Review', outputText: 'Web looks fine.', errorText: null });
    renderPage();

    expect(await screen.findByText('Hard-coded token')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Findings' })).toBeInTheDocument();
    expect(await screen.findByText('Web looks fine.')).toBeInTheDocument();
    expect(screen.getAllByText('Report-only').length).toBeGreaterThan(0);
    expect(screen.getByLabelText('Report')).toBeChecked();
    expect(screen.queryByText('No issues found.')).not.toBeInTheDocument();
    const reportCalls = vi.mocked(enumerateFleetActionRunTargets).mock.calls.filter(([, q]) => q?.includeOutput);
    expect(reportCalls[0][1]).toMatchObject({ pageNumber: 1, pageSize: 10, includeOutput: true });

    fireEvent.click(screen.getByRole('button', { name: 'Load more' }));
    expect(await screen.findByText('No issues found.')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Load more' })).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Download report (.md)' }));
    await waitFor(() => expect(downloadBlob).toHaveBeenCalledTimes(1));
    const [blob, fileName] = vi.mocked(downloadBlob).mock.calls[0];
    expect(fileName).toBe('fleet-action-report-far_1.md');
    const text = await (blob as Blob).text();
    expect(text).toContain('# Security review');
    expect(text).toContain('## api (Succeeded)');
    expect(text).toContain('- **Hard-coded token** in config');
    expect(text).toContain('## web (Succeeded)\n\nWeb looks fine.');
    expect(text).toContain('## cli (Succeeded)\n\nNo issues found.');

    // The Targets view is one click away.
    fireEvent.click(screen.getByLabelText('Targets'));
    expect(await screen.findByRole('link', { name: 'cli' })).toBeInTheDocument();
  });

  it('keeps Implementation Mission runs on the Targets view with a Report option', async () => {
    vi.mocked(getFleetActionRun).mockResolvedValue({ run: run('Completed', { kind: 'Mission', missionMode: 'Implementation', commandText: null, promptTemplate: 'Fix' }) });
    renderPage();
    expect(await screen.findByRole('link', { name: 'api' })).toBeInTheDocument();
    expect(screen.getByLabelText('Targets')).toBeChecked();
    expect(screen.queryByText('Report-only')).not.toBeInTheDocument();
  });
});
