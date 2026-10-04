import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import FleetRecommendationsPanel, { buildApplyPayload, moveVessel, toDrafts, validateDrafts, type FleetDraft } from './FleetRecommendationsPanel';
import { applyFleetRecommendations, cancelJob, categorizeVesselImport } from '../../../api/client';
import { translateTemplate } from '../../../i18n/runtime';
import type { VesselImportBatch, VesselImportFleetRecommendation, VesselImportItem } from '../../../types/models';

vi.mock('../../../api/client', () => ({
  apiErrorCode: vi.fn(() => null),
  applyFleetRecommendations: vi.fn(),
  cancelJob: vi.fn(),
  categorizeVesselImport: vi.fn(),
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
    id: 'vib_1', tenantId: 'ten', userId: 'usr', status: 'Completed', harborId: null, fleetId: null, jobId: null,
    requestedPathCount: 1, candidateCount: 3, createdCount: 3, skippedCount: 0, failedCount: 0,
    createdUtc: '', lastUpdateUtc: '', completedUtc: null, categorizationStatus: 'Completed', categorizationJobId: 'job_c', ...overrides,
  };
}

function item(name: string, vesselId: string): VesselImportItem {
  return {
    id: `vii_${name}`, tenantId: 'ten', batchId: 'vib_1', path: `/code/${name}`, proposedName: name, remoteUrl: null, defaultBranch: 'main',
    candidateStatus: 'New', existingVesselId: null, outcome: 'Created', outcomeReason: null, outcomeMessage: null, vesselId,
    createdUtc: '', lastUpdateUtc: '',
  };
}

function rec(id: string, name: string, vesselIds: string[], rationale: string | null = null): VesselImportFleetRecommendation {
  return { id, tenantId: 'ten', batchId: 'vib_1', name, description: null, rationale, sortOrder: 0, appliedFleetId: null, vesselIds, createdUtc: '', lastUpdateUtc: '' };
}

const items = [item('api', 'vsl_api'), item('ledger', 'vsl_ledger'), item('cli', 'vsl_cli')];
const recs = [rec('vfr_1', 'Payments', ['vsl_api', 'vsl_ledger'], 'They move money.'), rec('vfr_2', 'Uncategorized', ['vsl_cli'])];

function renderPanel(b: VesselImportBatch, r: VesselImportFleetRecommendation[] = recs, onChanged = vi.fn()) {
  render(
    <MemoryRouter>
      <FleetRecommendationsPanel batch={b} items={items} recommendations={r} onChanged={onChanged} />
    </MemoryRouter>,
  );
  return onChanged;
}

describe('fleet recommendation helpers', () => {
  it('moves vessels, validates names, and builds the apply payload', () => {
    let drafts: FleetDraft[] = toDrafts(recs);
    drafts = moveVessel(drafts, 'vsl_cli', drafts[0].key);
    expect(drafts[0].vesselIds).toEqual(['vsl_api', 'vsl_ledger', 'vsl_cli']);
    expect(drafts[1].vesselIds).toEqual([]);

    const dup = [{ ...drafts[0] }, { ...drafts[0], key: 'other', name: ' payments ' }];
    expect(Object.values(validateDrafts(dup))).toContain('Another fleet already uses this name.');
    expect(Object.values(validateDrafts([{ ...drafts[0], name: '  ' }]))).toContain('Give this fleet a name.');

    expect(buildApplyPayload([{ ...drafts[0], name: ' Payments ', description: ' ' }, drafts[1]])).toEqual({
      Fleets: [{ Name: 'Payments', Description: null, VesselIds: ['vsl_api', 'vsl_ledger', 'vsl_cli'] }],
    });
  });
});

describe('FleetRecommendationsPanel', () => {
  afterEach(() => vi.resetAllMocks());

  it('renders nothing when categorization was not requested', () => {
    const { container } = render(<MemoryRouter><FleetRecommendationsPanel batch={batch({ categorizationStatus: 'None' })} items={items} recommendations={[]} onChanged={vi.fn()} /></MemoryRouter>);
    expect(container).toBeEmptyDOMElement();
  });

  it('shows the running state with a stop button that cancels the job', async () => {
    vi.mocked(cancelJob).mockResolvedValue({} as never);
    const onChanged = renderPanel(batch({ categorizationStatus: 'Running' }), []);
    expect(screen.getByText(/A captain is reading the imported repositories/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Stop the captain' }));
    await waitFor(() => expect(cancelJob).toHaveBeenCalledWith('job_c'));
    expect(onChanged).toHaveBeenCalled();
  });

  it('shows the error and retries a failed run', async () => {
    vi.mocked(categorizeVesselImport).mockResolvedValue(batch({ categorizationStatus: 'Pending' }));
    const onChanged = renderPanel(batch({ categorizationStatus: 'Failed', categorizationError: 'No JSON written.' }), []);
    expect(screen.getByText('No JSON written.')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Retry categorization' }));
    await waitFor(() => expect(categorizeVesselImport).toHaveBeenCalledWith('vib_1'));
    expect(onChanged).toHaveBeenCalled();
  });

  it('edits fleets (rename, move, add, remove) and applies the edited payload after confirmation', async () => {
    vi.mocked(applyFleetRecommendations).mockResolvedValue({
      batchId: 'vib_1', fleets: [], createdFleetIds: [], batch: null,
      assignments: [{ vesselId: 'vsl_api', vesselName: 'api', fleetId: 'flt_1', fleetName: 'Money', previousFleetId: null }],
    });
    const onChanged = renderPanel(batch());
    expect(screen.getByText('They move money.')).toBeInTheDocument();
    expect(screen.getByText('Repositories in Uncategorized are left without a fleet when you apply.')).toBeInTheDocument();

    const nameInputs = screen.getAllByLabelText('Fleet name');
    fireEvent.change(nameInputs[0], { target: { value: 'Money' } });

    fireEvent.click(screen.getByRole('button', { name: '+ Add fleet' }));
    const newName = screen.getAllByLabelText('Fleet name')[2];
    fireEvent.change(newName, { target: { value: 'Tools' } });

    const uncategorized = screen.getByRole('article', { name: 'Uncategorized' });
    const move = within(uncategorized).getByLabelText('Move cli to another fleet');
    const toolsOption = within(move).getByRole('option', { name: 'Tools' }) as HTMLOptionElement;
    fireEvent.change(move, { target: { value: toolsOption.value } });

    const emptied = screen.getByRole('article', { name: 'Uncategorized' });
    fireEvent.click(within(emptied).getByRole('button', { name: 'Remove fleet' }));
    expect(screen.queryByRole('article', { name: 'Uncategorized' })).not.toBeInTheDocument();
    expect(within(screen.getByRole('article', { name: 'Money' })).getByRole('button', { name: 'Remove fleet' })).toBeDisabled();

    fireEvent.click(screen.getByRole('button', { name: 'Apply fleets' }));
    expect(await screen.findByText('Apply these fleets?')).toBeInTheDocument();
    const applyButtons = screen.getAllByRole('button', { name: 'Apply fleets' });
    fireEvent.click(applyButtons[applyButtons.length - 1]);

    await waitFor(() => expect(applyFleetRecommendations).toHaveBeenCalledTimes(1));
    expect(applyFleetRecommendations).toHaveBeenCalledWith('vib_1', {
      Fleets: [
        { Name: 'Money', Description: null, VesselIds: ['vsl_api', 'vsl_ledger'] },
        { Name: 'Tools', Description: null, VesselIds: ['vsl_cli'] },
      ],
    });
    expect(pushToast).toHaveBeenCalledWith('success', 'Fleets applied: 1 vessel assigned.');
    expect(onChanged).toHaveBeenCalled();
  });

  it('blocks apply when a fleet with repositories has no name', async () => {
    renderPanel(batch());
    fireEvent.change(screen.getAllByLabelText('Fleet name')[0], { target: { value: '' } });
    fireEvent.click(screen.getByRole('button', { name: 'Apply fleets' }));
    expect(await screen.findByText('Give this fleet a name.')).toBeInTheDocument();
    expect(screen.queryByText('Apply these fleets?')).not.toBeInTheDocument();
    expect(applyFleetRecommendations).not.toHaveBeenCalled();
  });

  it('lists applied fleets with links and allows editing again', () => {
    renderPanel(batch({ categorizationStatus: 'Applied' }), [{ ...recs[0], appliedFleetId: 'flt_9' }]);
    expect(screen.getByRole('link', { name: 'Payments' })).toHaveAttribute('href', '/fleets/flt_9');
    fireEvent.click(screen.getByRole('button', { name: 'Edit and apply again' }));
    expect(screen.getByLabelText('Fleet name')).toHaveValue('Payments');
  });
});
