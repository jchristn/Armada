import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import RunActionModal, { validateRunInputs } from './RunActionModal';
import { enumerateFleetActions, getVessel, listPipelines, runAdHocFleetAction, runFleetAction } from '../../api/client';
import { translateTemplate } from '../../i18n/runtime';
import type { FleetAction } from '../../types/models';
import { onlyCallArgs } from '../../test/mockCalls';

vi.mock('../../api/client', () => ({
  enumerateFleetActions: vi.fn(),
  getVessel: vi.fn(),
  listPipelines: vi.fn(),
  runFleetAction: vi.fn(),
  runAdHocFleetAction: vi.fn(),
}));

const pushToast = vi.fn();

// A stable locale object, like the real provider (whose `t` is memoized), so effects keyed on `t` do not loop.
const localeValue = {
  t: (text: string, params?: Record<string, string | number | null | undefined>) => translateTemplate('en', text, null, params),
  locale: 'en',
  formatDateTime: (v: string | null | undefined) => v ?? '',
  formatRelativeTime: (v: string | null | undefined) => v ?? '',
};

vi.mock('../../context/LocaleContext', () => ({
  useLocale: () => localeValue,
}));

vi.mock('../../context/NotificationContext', () => ({
  useNotifications: () => ({ pushToast }),
}));

const commandAction: FleetAction = {
  id: 'fac_1', tenantId: 'ten', userId: 'usr', name: 'Fast-forward', description: null, kind: 'Command',
  commandText: 'git -C {{vessel.workingDirectory}} pull --ff-only', promptTemplate: null, pipelineId: null, persona: null,
  timeoutSeconds: 300, defaultConcurrency: 4, requiresCleanWorkingTree: true, isBuiltIn: true, builtInKey: 'ff', active: true,
  createdUtc: '2026-10-01T00:00:00Z', lastUpdateUtc: '2026-10-01T00:00:00Z',
};

const adHocBase = { name: 'x', kind: 'Command' as const, commandText: 'git status', promptTemplate: '', pipelineId: '', timeoutSeconds: '300', requiresCleanWorkingTree: true };

describe('validateRunInputs', () => {
  it('requires vessels within the server limit', () => {
    expect(validateRunInputs({ vesselCount: 0, mode: 'saved', action: commandAction, concurrency: '4', adHoc: adHocBase }).vessels).toBeTruthy();
    expect(validateRunInputs({ vesselCount: 501, mode: 'saved', action: commandAction, concurrency: '4', adHoc: adHocBase }).vessels).toBeTruthy();
    expect(validateRunInputs({ vesselCount: 500, mode: 'saved', action: commandAction, concurrency: '4', adHoc: adHocBase })).toEqual({});
  });

  it('clamps concurrency to whole numbers from 1 to 32', () => {
    for (const bad of ['0', '33', '2.5', '', 'abc']) {
      expect(validateRunInputs({ vesselCount: 2, mode: 'saved', action: commandAction, concurrency: bad, adHoc: adHocBase }).concurrency).toBeTruthy();
    }
    expect(validateRunInputs({ vesselCount: 2, mode: 'saved', action: commandAction, concurrency: '32', adHoc: adHocBase }).concurrency).toBeUndefined();
  });

  it('requires a saved action in saved mode', () => {
    expect(validateRunInputs({ vesselCount: 1, mode: 'saved', action: null, concurrency: '1', adHoc: adHocBase }).action).toBeTruthy();
  });

  it('validates ad hoc name, body, unknown variables and timeout', () => {
    const errs = validateRunInputs({ vesselCount: 1, mode: 'adhoc', action: null, concurrency: '1', adHoc: { ...adHocBase, name: ' ', commandText: '', timeoutSeconds: '2' } });
    expect(errs.name).toBeTruthy();
    expect(errs.body).toBeTruthy();
    expect(errs.timeout).toBeTruthy();
    const unknown = validateRunInputs({ vesselCount: 1, mode: 'adhoc', action: null, concurrency: '1', adHoc: { ...adHocBase, commandText: 'echo {{vessel.nope}}' } });
    expect(unknown.body).toBe('unknown-variables');
    const mission = validateRunInputs({ vesselCount: 1, mode: 'adhoc', action: null, concurrency: '1', adHoc: { ...adHocBase, kind: 'Mission', promptTemplate: 'Fix {{health.summary}}', timeoutSeconds: '1' } });
    expect(mission).toEqual({});
  });
});

describe('RunActionModal', () => {
  beforeEach(() => {
    vi.mocked(enumerateFleetActions).mockResolvedValue({ success: true, pageNumber: 1, pageSize: 500, totalPages: 1, totalRecords: 1, totalMs: 1, objects: [commandAction] });
    vi.mocked(listPipelines).mockResolvedValue({ success: true, pageNumber: 1, pageSize: 1, totalPages: 1, totalRecords: 0, totalMs: 1, objects: [] });
    vi.mocked(getVessel).mockResolvedValue({ id: 'vsl_1', name: 'api', workingDirectory: '/code/api', defaultBranch: 'main' } as never);
    vi.mocked(runFleetAction).mockResolvedValue({ runId: 'far_1', actionId: 'fac_1', kind: 'Command', status: 'Pending', targetCount: 2, concurrency: 4 });
    vi.mocked(runAdHocFleetAction).mockResolvedValue({ runId: 'far_2', actionId: null, kind: 'Command', status: 'Pending', targetCount: 2, concurrency: 2 });
  });

  afterEach(() => vi.clearAllMocks());

  function renderModal(onClose = vi.fn()) {
    render(
      <MemoryRouter initialEntries={['/vessels']}>
        <Routes>
          <Route path="/vessels" element={<RunActionModal open vesselIds={['vsl_1', 'vsl_2']} onClose={onClose} />} />
          <Route path="/fleet-actions/runs/:id" element={<div>run detail page</div>} />
        </Routes>
      </MemoryRouter>,
    );
    return onClose;
  }

  it('previews the rendered command for the first vessel and confirms a saved Command run', async () => {
    renderModal();
    expect(await screen.findByText('git -C /code/api pull --ff-only')).toBeInTheDocument();
    expect(screen.getByText('Preview for api')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Review and run' }));
    expect(screen.getByText(/working directory of each of 2 vessels/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Run on 2 vessels' }));

    await waitFor(() => expect(runFleetAction).toHaveBeenCalledWith('fac_1', { VesselIds: ['vsl_1', 'vsl_2'], Concurrency: 4 }));
    expect(await screen.findByText('run detail page')).toBeInTheDocument();
  });

  it('blocks an ad hoc run with invalid input and shows inline errors', async () => {
    renderModal();
    await screen.findByText('git -C /code/api pull --ff-only');
    fireEvent.click(screen.getByLabelText('Ad hoc'));
    fireEvent.change(screen.getByLabelText(/^Concurrency/), { target: { value: '40' } });
    fireEvent.click(screen.getByRole('button', { name: 'Review and run' }));

    expect(screen.getByText('Name is required.')).toBeInTheDocument();
    expect(screen.getByText('Command text is required.')).toBeInTheDocument();
    expect(screen.getByText('Concurrency must be a whole number from 1 to 32.')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Run on/ })).not.toBeInTheDocument();

    fireEvent.change(screen.getByPlaceholderText('git status -sb'), { target: { value: 'echo {{vessel.bogus}}' } });
    expect(screen.getByText('Unknown template variable: vessel.bogus')).toBeInTheDocument();
    expect(runAdHocFleetAction).not.toHaveBeenCalled();
  });

  it('starts a valid ad hoc run with the inline definition', async () => {
    renderModal();
    await screen.findByText('git -C /code/api pull --ff-only');
    fireEvent.click(screen.getByLabelText('Ad hoc'));
    fireEvent.change(screen.getByPlaceholderText('e.g. Show git status'), { target: { value: 'Status' } });
    fireEvent.change(screen.getByPlaceholderText('git status -sb'), { target: { value: 'git status -sb' } });
    fireEvent.change(screen.getByLabelText(/^Concurrency/), { target: { value: '2' } });
    fireEvent.click(screen.getByRole('button', { name: 'Review and run' }));
    fireEvent.click(screen.getByRole('button', { name: 'Run on 2 vessels' }));

    await waitFor(() => expect(runAdHocFleetAction).toHaveBeenCalledTimes(1));
    const body = onlyCallArgs(vi.mocked(runAdHocFleetAction))[0];
    expect(body.VesselIds).toEqual(['vsl_1', 'vsl_2']);
    expect(body.Concurrency).toBe(2);
    expect(body.Definition?.Name).toBe('Status');
    expect(body.Definition?.Kind).toBe('Command');
    expect(body.Definition?.CommandText).toBe('git status -sb');
    expect(body.Definition?.RequiresCleanWorkingTree).toBe(true);
  });
});
