import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import MergeQueue from './MergeQueue';
import { deleteMergeEntry, listMergeQueue, listVessels } from '../api/client';
import type { MergeEntry } from '../types/models';

const translate = (text: string, params?: Record<string, string | number | null | undefined>) => {
  if (!params) return text;
  return Object.entries(params).reduce(
    (current, [key, value]) => current.split(`{{${key}}}`).join(value == null ? '' : String(value)),
    text,
  );
};
const localeValue = { t: translate, formatDateTime: (v: string | null | undefined) => v ?? '', formatRelativeTime: (v: string | null | undefined) => v ?? '' };
const notifications = { pushToast: vi.fn() };

vi.mock('../api/client', () => ({
  listMergeQueue: vi.fn(),
  listVessels: vi.fn(),
  enqueueMerge: vi.fn(),
  deleteMergeEntry: vi.fn(),
  processMergeEntry: vi.fn(),
  processAllMergeQueue: vi.fn(),
  cancelMergeEntry: vi.fn(),
  getMissionDiff: vi.fn(),
  getMissionLog: vi.fn(),
}));
vi.mock('../context/LocaleContext', () => ({ useLocale: () => localeValue }));
vi.mock('../context/NotificationContext', () => ({ useNotifications: () => notifications }));
vi.mock('../lib/useAutoRefresh', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../lib/useAutoRefresh')>()),
  useAutoRefresh: () => ({ seconds: 30, setSeconds: vi.fn() }),
}));
vi.mock('../components/shared/UserScopeFilter', () => ({ default: () => null }));

const MISSION_ID = 'msn_0123456789abcdefghijklmnop';
const entry: MergeEntry = {
  id: 'mrg_1', tenantId: null, missionId: MISSION_ID, vesselId: null, branchName: 'armada/feature-x', targetBranch: 'main',
  status: 'Queued', priority: 0, batchId: null, testCommand: null, testOutput: null, testExitCode: null,
  createdUtc: '2026-10-01T00:00:00Z', lastUpdateUtc: '2026-10-01T00:00:00Z', testStartedUtc: null, completedUtc: null,
};

function page<T>(objects: T[]) {
  return { success: true, pageNumber: 1, pageSize: 25, totalPages: 1, totalRecords: objects.length, totalMs: 1, objects } as never;
}

describe('MergeQueue table', () => {
  beforeEach(() => {
    localStorage.clear();
    vi.mocked(listMergeQueue).mockResolvedValue(page([entry]));
    vi.mocked(listVessels).mockResolvedValue(page([]));
    vi.mocked(deleteMergeEntry).mockResolvedValue(undefined as never);
    notifications.pushToast.mockClear();
  });

  it('shows the mission ID on one line with the full ID in the tooltip', async () => {
    render(<MemoryRouter><MergeQueue /></MemoryRouter>);
    const link = await screen.findByText(MISSION_ID);
    expect(link).toHaveAttribute('title', MISSION_ID);
    expect(link.parentElement).toHaveClass('cell-clip');
  });

  it('puts refresh controls in the toolbar and locks ID and Branch in the column chooser', async () => {
    const { container } = render(<MemoryRouter><MergeQueue /></MemoryRouter>);
    await screen.findAllByText('armada/feature-x');
    const bar = container.querySelector('.data-table .pagination-bar') as HTMLElement;
    expect(within(bar).getByTitle('Reload the merge queue from the server now.')).toBeInTheDocument();
    expect(within(bar).getByLabelText('Auto-refresh interval')).toBeInTheDocument();
    await act(async () => { fireEvent.click(within(bar).getByRole('button', { name: /^Columns/ })); });
    const menu = screen.getByRole('menu', { name: 'Choose visible columns' });
    expect(within(menu).getByRole('menuitemcheckbox', { name: /^ID/ })).toHaveAttribute('aria-disabled', 'true');
    expect(within(menu).getByRole('menuitemcheckbox', { name: /^Branch/ })).toHaveAttribute('aria-disabled', 'true');
    expect(within(menu).getByRole('menuitemcheckbox', { name: /^Target/ })).not.toHaveAttribute('aria-disabled');
  });

  it('explains the predicted outcome for active and terminal entries', async () => {
    const landed = { ...entry, id: 'mrg_landed', status: 'Landed' };
    vi.mocked(listMergeQueue).mockResolvedValue(page([entry, landed]));
    render(<MemoryRouter><MergeQueue /></MemoryRouter>);

    await screen.findAllByText('armada/feature-x');
    const menus = screen.getAllByRole('button', { name: 'Actions' });
    fireEvent.click(menus[0]);
    fireEvent.click(screen.getByRole('menuitem', { name: 'Delete' }));
    let dialog = screen.getByRole('alertdialog');
    expect(within(dialog).getByText(/cancel merge entry .* The entry will be kept/i)).toBeInTheDocument();
    expect(within(dialog).queryByText(/cannot be undone/i)).not.toBeInTheDocument();
    fireEvent.click(within(dialog).getByRole('button', { name: 'Yes' }));
    await waitFor(() => expect(deleteMergeEntry).toHaveBeenCalledWith('mrg_1'));
    expect(notifications.pushToast).toHaveBeenCalledWith('warning', 'Merge entry mrg_1 cancelled. The entry is expected to remain.');

    fireEvent.click(menus[1]);
    fireEvent.click(screen.getByRole('menuitem', { name: 'Delete' }));
    dialog = screen.getByRole('alertdialog');
    expect(within(dialog).getByText(/Permanently delete merge entry/i)).toBeInTheDocument();
    fireEvent.click(within(dialog).getByRole('button', { name: 'Yes' }));
    await waitFor(() => expect(deleteMergeEntry).toHaveBeenCalledWith('mrg_landed'));
    expect(notifications.pushToast).toHaveBeenCalledWith('warning', 'Merge entry mrg_landed deleted.');
  });

  it('reports separate bulk outcomes and failed requests', async () => {
    const landed = { ...entry, id: 'mrg_landed', status: 'Landed' };
    const failed = { ...entry, id: 'mrg_failed', status: 'Failed' };
    vi.mocked(listMergeQueue).mockResolvedValue(page([entry, landed, failed]));
    vi.mocked(deleteMergeEntry).mockImplementation(async (id: string) => {
      if (id === 'mrg_failed') throw new Error('request failed');
      return undefined as never;
    });
    render(<MemoryRouter><MergeQueue /></MemoryRouter>);
    await screen.findAllByText('armada/feature-x');

    fireEvent.click(screen.getByRole('checkbox', { name: 'Select all entries' }));
    fireEvent.click(screen.getByRole('button', { name: /Delete Selected/ }));
    const dialog = screen.getByRole('alertdialog');
    expect(within(dialog).getByText(/2 expected to be deleted and 1 expected to be cancelled/i)).toBeInTheDocument();
    fireEvent.click(within(dialog).getByRole('button', { name: 'Yes' }));

    await waitFor(() => expect(deleteMergeEntry).toHaveBeenCalledTimes(3));
    expect(notifications.pushToast).toHaveBeenCalledWith('warning', expect.stringMatching(/1 expected deleted.*1 expected cancelled.*1 failed/i));
  });
});
