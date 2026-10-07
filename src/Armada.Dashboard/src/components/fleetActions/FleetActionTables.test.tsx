import { beforeEach, describe, expect, it, vi } from 'vitest';
import { act, fireEvent, render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import FleetActionsTable from './FleetActionsTable';
import FleetActionRunsTable from './FleetActionRunsTable';
import { enumerateFleetActionRuns, enumerateFleetActions } from '../../api/client';
import { translateTemplate } from '../../i18n/runtime';

vi.mock('../../api/client', () => ({
  enumerateFleetActions: vi.fn(),
  enumerateFleetActionRuns: vi.fn(),
  cancelFleetActionRun: vi.fn(),
  deleteFleetAction: vi.fn(),
  getSettings: vi.fn().mockResolvedValue({}),
}));

const localeValue = {
  t: (text: string, params?: Record<string, string | number | null | undefined>) => translateTemplate('en', text, null, params),
  locale: 'en',
  formatDateTime: (v: string | null | undefined) => v ?? '',
  formatRelativeTime: (v: string | null | undefined) => v ?? '',
};
vi.mock('../../context/LocaleContext', () => ({ useLocale: () => localeValue }));
vi.mock('../../context/NotificationContext', () => ({ useNotifications: () => ({ pushToast: vi.fn() }) }));
vi.mock('../../context/AuthContext', () => ({ useAuth: () => ({ isTenantAdmin: true }) }));
vi.mock('./FleetActionFormModal', () => ({ default: () => null }));
vi.mock('./VesselPickerModal', () => ({ default: () => null }));
vi.mock('./RunActionModal', () => ({ default: () => null }));

function page<T>(objects: T[]) {
  return { success: true, pageNumber: 1, pageSize: 25, totalPages: 1, totalRecords: objects.length, totalMs: 3, objects };
}

const action = {
  id: 'fla_1', tenantId: null, userId: null, name: 'Build', description: 'Builds the solution in each vessel', kind: 'Command',
  commandText: 'dotnet build', promptTemplate: null, pipelineId: null, persona: null, timeoutSeconds: 300, defaultConcurrency: 4,
  requiresCleanWorkingTree: false, isBuiltIn: true, builtInKey: 'build', active: true, createdUtc: '2026-10-01', lastUpdateUtc: '2026-10-02',
};

const run = {
  id: 'far_1', tenantId: null, userId: null, actionId: null, actionName: 'Ad hoc build', kind: 'Command', commandText: 'make',
  promptTemplate: null, pipelineId: null, persona: null, timeoutSeconds: 60, requiresCleanWorkingTree: false, concurrency: 2,
  status: 'Completed', targetCount: 2, succeededCount: 2, failedCount: 0, skippedCount: 0, cancelledCount: 0,
  startedUtc: '2026-10-01', completedUtc: '2026-10-01', createdUtc: '2026-10-01', lastUpdateUtc: '2026-10-01',
};

describe('FleetActionsTable', () => {
  beforeEach(() => {
    localStorage.clear();
    vi.mocked(enumerateFleetActions).mockResolvedValue(page([action]) as never);
  });

  it('shows the ID and description in their own one-line columns, not stacked under the name', async () => {
    render(<MemoryRouter><FleetActionsTable /></MemoryRouter>);
    const nameCell = (await screen.findByText('Build')).closest('td') as HTMLElement;
    expect(nameCell).not.toHaveTextContent('fla_1');
    expect(nameCell).not.toHaveTextContent('Builds the solution');
    expect(screen.getByTitle('fla_1').closest('td')).toHaveAttribute('data-col', 'id');
    const description = screen.getByText('Builds the solution in each vessel');
    expect(description).toHaveClass('truncate-text');
    expect(description.closest('td')).toHaveAttribute('title', 'Builds the solution in each vessel');
  });

  it('puts refresh and the column chooser in the pager row, with Name locked', async () => {
    const { container } = render(<MemoryRouter><FleetActionsTable /></MemoryRouter>);
    await screen.findByText('Build');
    const bar = container.querySelector('.pagination-bar') as HTMLElement;
    expect(within(bar).getByText('1 record')).toBeInTheDocument();
    expect(within(bar).getByTitle('Refresh fleet actions')).toBeInTheDocument();
    await act(async () => { fireEvent.click(within(bar).getByRole('button', { name: /^Columns/ })); });
    expect(screen.getByRole('menuitemcheckbox', { name: /Name/ })).toHaveAttribute('aria-disabled', 'true');
    fireEvent.click(screen.getByRole('menuitemcheckbox', { name: /Concurrency/ }));
    expect(screen.queryByRole('columnheader', { name: 'Concurrency' })).not.toBeInTheDocument();
  });
});

describe('FleetActionRunsTable', () => {
  beforeEach(() => {
    localStorage.clear();
    vi.mocked(enumerateFleetActionRuns).mockResolvedValue(page([run]) as never);
  });

  it('moves auto-refresh and refresh into the pager row and shows the run ID in its own column', async () => {
    const { container } = render(<MemoryRouter><FleetActionRunsTable /></MemoryRouter>);
    const nameCell = (await screen.findByText('Ad hoc build')).closest('td') as HTMLElement;
    expect(nameCell).not.toHaveTextContent('far_1');
    expect(screen.getByTitle('far_1').closest('td')).toHaveAttribute('data-col', 'id');
    const bars = container.querySelectorAll('.pagination-bar');
    expect(bars).toHaveLength(1);
    expect(within(bars[0] as HTMLElement).getByLabelText('Auto-refresh interval')).toBeInTheDocument();
    expect(within(bars[0] as HTMLElement).getByTitle('Refresh runs')).toBeInTheDocument();
    expect(container.querySelectorAll('.auto-refresh-select')).toHaveLength(1);
  });

  it('sorts by created date from the header button', async () => {
    render(<MemoryRouter><FleetActionRunsTable /></MemoryRouter>);
    await screen.findByText('Ad hoc build');
    const header = screen.getByRole('columnheader', { name: /Created/ });
    expect(header).toHaveAttribute('aria-sort', 'descending');
    await act(async () => { fireEvent.click(within(header).getByRole('button')); });
    const calls = vi.mocked(enumerateFleetActionRuns).mock.calls;
    expect(calls[calls.length - 1]?.[0]).toMatchObject({ order: 'CreatedAscending' });
  });
});
