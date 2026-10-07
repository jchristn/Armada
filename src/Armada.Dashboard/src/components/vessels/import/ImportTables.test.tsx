import { beforeEach, describe, expect, it, vi } from 'vitest';
import { act, fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import ImportHistory from './ImportHistory';
import ImportResultsStep from './ImportResultsStep';
import ImportReviewStep from './ImportReviewStep';
import { enumerateVesselImportBatches } from '../../../api/client';
import { translateTemplate } from '../../../i18n/runtime';
import type { VesselImportBatch, VesselImportItem } from '../../../types/models';

vi.mock('../../../api/client', () => ({ enumerateVesselImportBatches: vi.fn() }));
// Stable across renders (components keep `t` in effect dependencies).
const localeValue = {
  t: (text: string, params?: Record<string, string | number>) => translateTemplate('en', text, null, params),
  formatDateTime: (v: string | null | undefined) => v ?? '',
  formatRelativeTime: (v: string | null | undefined) => v ?? '',
};
vi.mock('../../../context/LocaleContext', () => ({ useLocale: () => localeValue }));

const batch = {
  id: 'vib_1', tenantId: null, userId: null, status: 'Completed', harborId: null, fleetId: null, jobId: null,
  requestedPathCount: 1, candidateCount: 3, createdCount: 1, skippedCount: 1, failedCount: 1,
  createdUtc: '2026-10-01', lastUpdateUtc: '2026-10-01', completedUtc: '2026-10-01', categorizationStatus: 'Completed',
} as unknown as VesselImportBatch;

function item(overrides: Partial<VesselImportItem>): VesselImportItem {
  return {
    id: 'vii_1', tenantId: null, batchId: 'vib_1', path: '/src/repos/gateway', proposedName: 'gateway', remoteUrl: 'https://example.com/gateway.git',
    defaultBranch: 'main', candidateStatus: 'New', existingVesselId: null, outcome: 'Created', outcomeReason: null, outcomeMessage: null,
    vesselId: null, createdUtc: '2026-10-01', lastUpdateUtc: '2026-10-01', ...overrides,
  } as VesselImportItem;
}

describe('ImportHistory', () => {
  beforeEach(() => {
    localStorage.clear();
    vi.mocked(enumerateVesselImportBatches).mockReset();
    vi.mocked(enumerateVesselImportBatches).mockResolvedValue({ success: true, pageNumber: 1, pageSize: 10, totalPages: 1, totalRecords: 1, totalMs: 1, objects: [batch] } as never);
  });

  it('shows the batch ID and categorization in their own columns instead of stacked sublines', async () => {
    render(<ImportHistory onOpen={() => undefined} />);
    const idCell = (await screen.findByTitle('vib_1')).closest('td') as HTMLElement;
    expect(idCell).toHaveAttribute('data-col', 'id');
    const created = screen.getAllByRole('row')[1].querySelector('td[data-col="createdUtc"]') as HTMLElement;
    expect(created).not.toHaveTextContent('vib_1');
    const status = screen.getAllByRole('row')[1].querySelector('td[data-col="status"]') as HTMLElement;
    expect(status.querySelectorAll('div.cell-subline')).toHaveLength(0);
    expect(screen.getAllByRole('row')[1].querySelector('td[data-col="categorization"]')).not.toBeNull();
  });

  it('refreshes from the toolbar and distinguishes the two Created columns in the chooser', async () => {
    render(<ImportHistory onOpen={() => undefined} />);
    await screen.findByTitle('vib_1');
    await act(async () => { fireEvent.click(screen.getByTitle('Refresh')); });
    expect(enumerateVesselImportBatches).toHaveBeenCalledTimes(2);
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: /^Columns/ })); });
    expect(screen.getByRole('menuitemcheckbox', { name: /Vessels created/ })).toBeInTheDocument();
    expect(screen.getByRole('menuitemcheckbox', { name: /^\W*ID/ })).toHaveAttribute('aria-disabled', 'true');
  });
});

describe('ImportResultsStep', () => {
  beforeEach(() => localStorage.clear());

  it('keeps the outcome message out of the reason cell (tooltip plus a hidden-by-default column)', () => {
    render(
      <MemoryRouter>
        <ImportResultsStep batch={batch} items={[item({ outcome: 'Failed', outcomeReason: 'CreateFailed', outcomeMessage: 'Name already taken' })]} />
      </MemoryRouter>,
    );
    const row = screen.getAllByRole('row')[1];
    const reason = row.querySelector('td[data-col="reason"]') as HTMLElement;
    expect(reason).toHaveAttribute('title', 'Name already taken');
    expect(reason).not.toHaveTextContent('Name already taken');
    expect(row.querySelector('td[data-col="message"]')).toBeNull();
  });
});

describe('ImportReviewStep', () => {
  beforeEach(() => localStorage.clear());

  function renderReview(onSelectedChange = vi.fn()) {
    render(
      <MemoryRouter>
        <ImportReviewStep
          candidates={[
            item({ id: 'a', path: '/a', proposedName: 'alpha' }),
            item({ id: 'b', path: '/b', proposedName: 'bravo', candidateStatus: 'AlreadyOnboarded', existingVesselId: 'vsl_9' }),
          ]}
          truncated={false}
          hints={[]}
          selected={[]}
          onSelectedChange={onSelectedChange}
          fleets={[]}
          pipelines={[]}
          defaults={{ fleetId: '', pipelineId: '', landingMode: '' }}
          onDefaultsChange={() => undefined}
        />
      </MemoryRouter>,
    );
    return onSelectedChange;
  }

  it('puts the existing-vessel link beside the status badge on one line', () => {
    renderReview();
    const link = screen.getByRole('link', { name: 'Open existing vessel' });
    const cell = link.closest('td') as HTMLElement;
    expect(cell).toHaveClass('cell-nowrap');
    expect(cell.querySelectorAll('div.cell-subline')).toHaveLength(0);
  });

  it('keeps non-importable rows unselectable and locks the Name column', async () => {
    const onSelectedChange = renderReview();
    expect(screen.getByLabelText('Select bravo')).toBeDisabled();
    fireEvent.click(screen.getByText('bravo'));
    expect(onSelectedChange).not.toHaveBeenCalled();
    fireEvent.click(screen.getByText('alpha'));
    expect(onSelectedChange).toHaveBeenCalledWith(['/a']);
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: /^Columns/ })); });
    expect(screen.getByRole('menuitemcheckbox', { name: /Name/ })).toHaveAttribute('aria-disabled', 'true');
    expect(screen.queryByRole('menuitemcheckbox', { name: /Select/ })).not.toBeInTheDocument();
  });
});
