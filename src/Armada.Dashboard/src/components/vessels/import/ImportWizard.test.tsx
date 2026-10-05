import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import ImportWizard, { IMPORT_POLL_MS, parsePastedPaths } from './ImportWizard';
import {
  apiErrorCode,
  discoverVesselImport,
  getFleetCategorizationDefaultPrompt,
  getVesselImportBatch,
  importVessels,
  listCaptains,
  listFleets,
  listPipelines,
} from '../../../api/client';
import { translateTemplate } from '../../../i18n/runtime';
import type { Captain, VesselImportBatch, VesselImportItem } from '../../../types/models';
import { onlyCallArgs } from '../../../test/mockCalls';

vi.mock('../../../api/client', () => ({
  apiErrorCode: vi.fn(() => null),
  browseVesselImport: vi.fn(),
  discoverVesselImport: vi.fn(),
  enumerateVesselImportBatches: vi.fn(),
  getVesselImportBatch: vi.fn(),
  importVessels: vi.fn(),
  listCaptains: vi.fn(),
  listFleets: vi.fn(),
  listPipelines: vi.fn(),
  getFleetCategorizationDefaultPrompt: vi.fn(),
  applyFleetRecommendations: vi.fn(),
  categorizeVesselImport: vi.fn(),
  cancelJob: vi.fn(),
}));

const pushToast = vi.fn();
const localeValue = {
  t: (text: string, params?: Record<string, string | number | null | undefined>) => translateTemplate('en', text, null, params),
  locale: 'en',
  formatDateTime: (v: string | null | undefined) => v ?? '',
  formatRelativeTime: (v: string | null | undefined) => v ?? '',
};

vi.mock('../../../context/LocaleContext', () => ({ useLocale: () => localeValue }));
vi.mock('../../../context/NotificationContext', () => ({ useNotifications: () => ({ pushToast }) }));

function batch(overrides: Partial<VesselImportBatch> = {}): VesselImportBatch {
  return {
    id: 'vib_1', tenantId: 'ten', userId: 'usr', status: 'Discovered', harborId: null, fleetId: null, jobId: null,
    requestedPathCount: 1, candidateCount: 4, createdCount: 0, skippedCount: 0, failedCount: 0,
    createdUtc: '2026-10-03T00:00:00Z', lastUpdateUtc: '2026-10-03T00:00:00Z', completedUtc: null, ...overrides,
  };
}

function item(name: string, status: VesselImportItem['candidateStatus'], overrides: Partial<VesselImportItem> = {}): VesselImportItem {
  return {
    id: `vii_${name}`, tenantId: 'ten', batchId: 'vib_1', path: `/code/${name}`, proposedName: name, remoteUrl: null, defaultBranch: 'main',
    candidateStatus: status, existingVesselId: status === 'AlreadyOnboarded' ? 'vsl_existing' : null, outcome: 'Pending', outcomeReason: null,
    outcomeMessage: null, vesselId: null, createdUtc: '2026-10-03T00:00:00Z', lastUpdateUtc: '2026-10-03T00:00:00Z', ...overrides,
  };
}

const candidates = [item('api', 'New'), item('web', 'New'), item('api-wt', 'Worktree'), item('old', 'AlreadyOnboarded')];

function renderWizard(onImported = vi.fn()) {
  render(
    <MemoryRouter>
      <ImportWizard open onClose={vi.fn()} onImported={onImported} />
    </MemoryRouter>,
  );
  return onImported;
}

async function discoverTwoPaths() {
  const textarea = screen.getByPlaceholderText(/\/Users\/alex\/Code/);
  fireEvent.change(textarea, { target: { value: '/code\n\n/code/api\n/code' } });
  expect(screen.getByText(/2 paths/)).toBeInTheDocument();
  fireEvent.click(screen.getByRole('button', { name: 'Discover' }));
  await screen.findByText('Import 2 repositories');
}

describe('parsePastedPaths', () => {
  it('trims, drops blanks and quotes, and de-duplicates', () => {
    expect(parsePastedPaths(' /a \n\n"/b"\n/a\r\n')).toEqual(['/a', '/b']);
  });
});

describe('ImportWizard', () => {
  beforeEach(() => {
    vi.mocked(apiErrorCode).mockReturnValue(null);
    vi.mocked(listFleets).mockResolvedValue({ success: true, pageNumber: 1, pageSize: 1, totalPages: 1, totalRecords: 1, totalMs: 1, objects: [{ id: 'flt_1', name: 'Main fleet', tenantId: 'ten', description: null, defaultPipelineId: null, active: true, createdUtc: '', lastUpdateUtc: '' }] });
    vi.mocked(listPipelines).mockResolvedValue({ success: true, pageNumber: 1, pageSize: 1, totalPages: 1, totalRecords: 0, totalMs: 1, objects: [] });
    vi.mocked(discoverVesselImport).mockResolvedValue({ batchId: 'vib_1', batch: batch(), candidates, truncated: false, hints: [] });
  });

  afterEach(() => {
    vi.resetAllMocks();
    vi.useRealTimers();
  });

  it('runs discover -> review -> inline import -> results', async () => {
    vi.mocked(importVessels).mockResolvedValue({
      batchId: 'vib_1', jobId: null, runsInBackground: false,
      batch: batch({ status: 'Completed', createdCount: 2, skippedCount: 2 }),
      items: [
        item('api', 'New', { outcome: 'Created', vesselId: 'vsl_api' }),
        item('web', 'New', { outcome: 'Created', vesselId: 'vsl_web' }),
        item('api-wt', 'Worktree', { outcome: 'SkippedNotSelected', outcomeReason: 'NotSelected' }),
        item('old', 'AlreadyOnboarded', { outcome: 'SkippedExisting', outcomeReason: 'VesselAlreadyExists' }),
      ],
    });
    const onImported = renderWizard();
    await discoverTwoPaths();

    expect(discoverVesselImport).toHaveBeenCalledWith({ Directories: ['/code', '/code/api'], RunInBackground: true });
    // New candidates are preselected; worktree is selectable but not selected; onboarded is disabled.
    expect(screen.getByLabelText('Select api')).toBeChecked();
    expect(screen.getByLabelText('Select web')).toBeChecked();
    expect(screen.getByLabelText('Select api-wt')).not.toBeChecked();
    expect(screen.getByLabelText('Select api-wt')).toBeEnabled();
    expect(screen.getByLabelText('Select old')).toBeDisabled();
    expect(screen.getByText('Open existing vessel')).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText(/^Fleet/), { target: { value: 'flt_1' } });
    fireEvent.click(screen.getByRole('button', { name: 'Import 2 repositories' }));

    await screen.findByText('Not selected for import');
    expect(importVessels).toHaveBeenCalledWith({ BatchId: 'vib_1', Paths: ['/code/api', '/code/web'], FleetId: 'flt_1', Defaults: null });
    expect(screen.getByText('A matching vessel already exists')).toBeInTheDocument();
    expect(onImported).toHaveBeenCalledTimes(1);
    expect(pushToast).toHaveBeenCalledWith('success', 'Import finished: 2 vessels created.');
  });

  it('polls a background import until the batch leaves Importing', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    vi.mocked(importVessels).mockResolvedValue({
      batchId: 'vib_1', jobId: 'job_9', runsInBackground: true,
      batch: batch({ status: 'Importing', jobId: 'job_9' }), items: [],
    });
    vi.mocked(getVesselImportBatch)
      .mockResolvedValueOnce({ batch: batch({ status: 'Importing', createdCount: 1, jobId: 'job_9' }), items: [item('api', 'New', { outcome: 'Created', vesselId: 'vsl_api' })] })
      .mockResolvedValueOnce({
        batch: batch({ status: 'Completed', createdCount: 2, jobId: 'job_9' }),
        items: [item('api', 'New', { outcome: 'Created', vesselId: 'vsl_api' }), item('web', 'New', { outcome: 'Created', vesselId: 'vsl_web' })],
      });

    const onImported = renderWizard();
    await discoverTwoPaths();
    fireEvent.click(screen.getByRole('button', { name: 'Import 2 repositories' }));

    expect(await screen.findByText('Importing 2 repositories in the background.')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Open the Jobs page' })).toHaveAttribute('href', '/jobs');

    await act(async () => { await vi.advanceTimersByTimeAsync(IMPORT_POLL_MS); });
    await waitFor(() => expect(getVesselImportBatch).toHaveBeenCalledTimes(1));
    expect(screen.getByText(/1 of 2 processed/)).toBeInTheDocument();

    await act(async () => { await vi.advanceTimersByTimeAsync(IMPORT_POLL_MS); });
    await waitFor(() => expect(getVesselImportBatch).toHaveBeenCalledTimes(2));
    await waitFor(() => expect(screen.queryByText('Importing 2 repositories in the background.')).not.toBeInTheDocument());
    expect(onImported).toHaveBeenCalledTimes(1);

    // Polling stopped once the batch completed.
    await act(async () => { await vi.advanceTimersByTimeAsync(IMPORT_POLL_MS * 3); });
    expect(getVesselImportBatch).toHaveBeenCalledTimes(2);
    const stats = screen.getByText('Created', { selector: '.fleet-run-stat-label' }).parentElement!;
    expect(within(stats).getByText('2')).toBeInTheDocument();
  });

  it('keeps the inputs and offers retry when discovery fails', async () => {
    vi.mocked(discoverVesselImport).mockRejectedValueOnce(new Error('Path /etc is outside the allowed roots.'));
    vi.mocked(apiErrorCode).mockReturnValueOnce('PathNotAllowed');
    renderWizard();
    const textarea = screen.getByPlaceholderText(/\/Users\/alex\/Code/) as HTMLTextAreaElement;
    fireEvent.change(textarea, { target: { value: '/etc' } });
    fireEvent.click(screen.getByRole('button', { name: 'Discover' }));

    expect(await screen.findByText(/outside the allowed import roots/)).toBeInTheDocument();
    expect(textarea.value).toBe('/etc');
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }));
    await screen.findByText('Import 2 repositories');
    expect(discoverVesselImport).toHaveBeenCalledTimes(2);
  });

  it('explains an empty discovery, including the containerized Admiral case', async () => {
    vi.mocked(discoverVesselImport).mockResolvedValueOnce({
      batchId: 'vib_2', batch: batch({ id: 'vib_2', candidateCount: 0 }), candidates: [], truncated: false,
      hints: [{ code: 'PathNotVisibleToAdmiral', message: 'raw server text' }],
    });
    renderWizard();
    fireEvent.change(screen.getByPlaceholderText(/\/Users\/alex\/Code/), { target: { value: '/host/code' } });
    fireEvent.click(screen.getByRole('button', { name: 'Discover' }));
    expect(await screen.findByText('No repositories found')).toBeInTheDocument();
    expect(screen.getByText(/runs in a container/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Import repositories' })).toBeDisabled();
  });
  it('runs discovery in the background, says so, and resumes on the review step', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    vi.mocked(discoverVesselImport).mockResolvedValueOnce({
      batchId: 'vib_1', jobId: 'job_d', runsInBackground: true, batch: batch({ status: 'Discovering', discoveryJobId: 'job_d', candidateCount: 0 }),
      candidates: [], truncated: false, hints: [],
    });
    vi.mocked(getVesselImportBatch)
      .mockResolvedValueOnce({ batch: batch({ status: 'Discovering' }), items: [] })
      .mockResolvedValueOnce({ batch: batch({ status: 'Discovered', truncated: true }), items: candidates, hints: [] });
    renderWizard();
    fireEvent.change(screen.getByPlaceholderText(/\/Users\/alex\/Code/), { target: { value: '/code' } });
    fireEvent.click(screen.getByRole('button', { name: 'Discover' }));

    expect(await screen.findByText('Scanning for repositories in the background.')).toBeInTheDocument();
    expect(screen.getByText(/You can close this dialog; discovery keeps running/)).toBeInTheDocument();

    await act(async () => { await vi.advanceTimersByTimeAsync(IMPORT_POLL_MS); });
    await act(async () => { await vi.advanceTimersByTimeAsync(IMPORT_POLL_MS); });
    await screen.findByText('Import 2 repositories');
    expect(getVesselImportBatch).toHaveBeenCalledTimes(2);
    expect(screen.getByText(/candidate limit/)).toBeInTheDocument();
  });

  it('shows the error when background discovery fails', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    vi.mocked(discoverVesselImport).mockResolvedValueOnce({
      batchId: 'vib_1', jobId: 'job_d', runsInBackground: true, batch: batch({ status: 'Discovering' }), candidates: [], truncated: false, hints: [],
    });
    vi.mocked(getVesselImportBatch).mockResolvedValueOnce({ batch: batch({ status: 'Failed', errorMessage: 'Disk vanished' }), items: [] });
    renderWizard();
    fireEvent.change(screen.getByPlaceholderText(/\/Users\/alex\/Code/), { target: { value: '/code' } });
    fireEvent.click(screen.getByRole('button', { name: 'Discover' }));
    await screen.findByText('Scanning for repositories in the background.');
    await act(async () => { await vi.advanceTimersByTimeAsync(IMPORT_POLL_MS); });
    expect(await screen.findByText('Disk vanished')).toBeInTheDocument();
  });

  it('offers captain categorization with a prefilled, resettable prompt and validates the captain', async () => {
    vi.mocked(listCaptains).mockResolvedValue({ success: true, pageNumber: 1, pageSize: 2, totalPages: 1, totalRecords: 2, totalMs: 1, objects: [captain('cpt_idle', 'Ada', 'Idle'), captain('cpt_busy', 'Bo', 'Working')] });
    vi.mocked(getFleetCategorizationDefaultPrompt).mockResolvedValue({ templateName: 'import.fleet_categorization', prompt: 'Group the repos.', timeoutMinutes: 20 });
    vi.mocked(importVessels).mockResolvedValue({
      batchId: 'vib_1', jobId: null, runsInBackground: false,
      batch: batch({ status: 'Completed', createdCount: 2, categorizationStatus: 'Pending', categorizationJobId: 'job_c' }),
      items: [item('api', 'New', { outcome: 'Created', vesselId: 'vsl_api' }), item('web', 'New', { outcome: 'Created', vesselId: 'vsl_web' })],
    });
    vi.mocked(getVesselImportBatch).mockResolvedValue({ batch: batch({ status: 'Completed', createdCount: 2, categorizationStatus: 'Running', categorizationJobId: 'job_c' }), items: [], fleetRecommendations: [] });
    renderWizard();
    await discoverTwoPaths();

    expect(screen.queryByLabelText('Captain that recommends fleets')).not.toBeInTheDocument();
    fireEvent.click(screen.getByLabelText('Recommend fleets with a captain'));
    const picker = await screen.findByLabelText('Captain that recommends fleets');
    await waitFor(() => expect(within(picker).getByRole('option', { name: /Bo/ })).toBeDisabled());
    expect(within(picker).getByRole('option', { name: /Ada/ })).toBeEnabled();
    const prompt = await screen.findByDisplayValue('Group the repos.');

    fireEvent.click(screen.getByRole('button', { name: 'Import 2 repositories' }));
    expect(await screen.findByText('Choose the captain that will recommend fleets.')).toBeInTheDocument();
    expect(importVessels).not.toHaveBeenCalled();

    fireEvent.change(picker, { target: { value: 'cpt_idle' } });
    fireEvent.change(prompt, { target: { value: 'Group by team.' } });
    fireEvent.click(screen.getByRole('button', { name: 'Reset to default' }));
    expect(screen.getByDisplayValue('Group the repos.')).toBeInTheDocument();
    fireEvent.change(screen.getByDisplayValue('Group the repos.'), { target: { value: 'Group by team.' } });
    fireEvent.click(screen.getByLabelText('Apply recommendations automatically'));
    fireEvent.click(screen.getByRole('button', { name: 'Import 2 repositories' }));

    await waitFor(() => expect(importVessels).toHaveBeenCalledTimes(1));
    expect(importVessels).toHaveBeenCalledWith(expect.objectContaining({
      Categorization: { Enabled: true, CaptainId: 'cpt_idle', Prompt: 'Group by team.', ApplyAutomatically: true },
    }));
    expect(await screen.findByText(/Fleet recommendations will start when the import finishes/)).toBeInTheDocument();
    expect(screen.getByText(/You can close this dialog; the header shows the task/)).toBeInTheDocument();
  });

  it('sends no custom prompt when the default instructions are unchanged', async () => {
    vi.mocked(listCaptains).mockResolvedValue({ success: true, pageNumber: 1, pageSize: 1, totalPages: 1, totalRecords: 1, totalMs: 1, objects: [captain('cpt_idle', 'Ada', 'Idle')] });
    vi.mocked(getFleetCategorizationDefaultPrompt).mockResolvedValue({ templateName: 'import.fleet_categorization', prompt: 'Group the repos.', timeoutMinutes: 20 });
    vi.mocked(importVessels).mockResolvedValue({ batchId: 'vib_1', jobId: null, runsInBackground: false, batch: batch({ status: 'Completed', createdCount: 2 }), items: [] });
    renderWizard();
    await discoverTwoPaths();
    fireEvent.click(screen.getByLabelText('Recommend fleets with a captain'));
    await screen.findByDisplayValue('Group the repos.');
    const picker = screen.getByLabelText('Captain that recommends fleets');
    await waitFor(() => expect(within(picker).getByRole('option', { name: /Ada/ })).toBeInTheDocument());
    fireEvent.change(picker, { target: { value: 'cpt_idle' } });
    fireEvent.click(screen.getByRole('button', { name: 'Import 2 repositories' }));
    await waitFor(() => expect(importVessels).toHaveBeenCalledTimes(1));
    expect(onlyCallArgs(vi.mocked(importVessels))[0].Categorization).toEqual({ Enabled: true, CaptainId: 'cpt_idle', Prompt: null, ApplyAutomatically: false });
  });

  it('stays on the current step when the parent re-renders with a new onImported callback', async () => {
    const { rerender } = render(
      <MemoryRouter>
        <ImportWizard open onClose={vi.fn()} onImported={() => undefined} />
      </MemoryRouter>,
    );
    await discoverTwoPaths();
    rerender(
      <MemoryRouter>
        <ImportWizard open onClose={vi.fn()} onImported={() => undefined} />
      </MemoryRouter>,
    );
    expect(screen.getByRole('button', { name: 'Import 2 repositories' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Discover' })).not.toBeInTheDocument();
  });

  it('only offers captains from the caller\'s tenant', async () => {
    vi.mocked(listCaptains).mockResolvedValue({ success: true, pageNumber: 1, pageSize: 2, totalPages: 1, totalRecords: 2, totalMs: 1, objects: [captain('cpt_mine', 'Mine', 'Idle'), { ...captain('cpt_other', 'Other', 'Idle'), tenantId: 'ten_other' }] });
    vi.mocked(getFleetCategorizationDefaultPrompt).mockResolvedValue({ templateName: 'import.fleet_categorization', prompt: 'Group the repos.', timeoutMinutes: 20 });
    render(
      <MemoryRouter>
        <ImportWizard open onClose={vi.fn()} tenantId="ten" />
      </MemoryRouter>,
    );
    await discoverTwoPaths();
    fireEvent.click(screen.getByLabelText('Recommend fleets with a captain'));
    const picker = await screen.findByLabelText('Captain that recommends fleets');
    await waitFor(() => expect(within(picker).getByRole('option', { name: /Mine/ })).toBeInTheDocument());
    expect(within(picker).queryByRole('option', { name: /Other/ })).not.toBeInTheDocument();
  });

  it('resumes a categorizing batch from history', async () => {
    vi.mocked(getVesselImportBatch).mockResolvedValue({
      batch: batch({ status: 'Completed', createdCount: 1, categorizationStatus: 'Failed', categorizationError: 'Captain exited without writing fleet-recommendations.json.' }),
      items: [item('api', 'New', { outcome: 'Created', vesselId: 'vsl_api', selected: true })],
      fleetRecommendations: [],
    });
    render(
      <MemoryRouter>
        <ImportWizard open onClose={vi.fn()} initialBatchId="vib_1" />
      </MemoryRouter>,
    );
    expect(await screen.findByText('The captain could not recommend fleets.')).toBeInTheDocument();
    expect(screen.getByText(/without writing fleet-recommendations.json/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Retry categorization' })).toBeInTheDocument();
  });
});

function captain(id: string, name: string, state: string): Captain {
  return {
    id, tenantId: 'ten', name, runtime: 'ClaudeCode', supportsPlanningSessions: true, planningSessionSupportReason: null, systemInstructions: null,
    model: null, allowedPersonas: null, preferredPersona: null, state, currentMissionId: null, currentDockId: null, processId: null,
    recoveryAttempts: 0, lastHeartbeatUtc: null, createdUtc: '', lastUpdateUtc: '',
  };
}
