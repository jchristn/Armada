import { act, fireEvent, render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import MergeQueue from './MergeQueue';
import { listMergeQueue, listVessels } from '../api/client';
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
  });

  it('shows the mission ID on one line with the full ID in the tooltip', async () => {
    render(<MemoryRouter><MergeQueue /></MemoryRouter>);
    const link = await screen.findByText(MISSION_ID);
    expect(link).toHaveAttribute('title', MISSION_ID);
    expect(link.parentElement).toHaveClass('cell-clip');
  });

  it('puts refresh controls in the toolbar and locks ID and Branch in the column chooser', async () => {
    const { container } = render(<MemoryRouter><MergeQueue /></MemoryRouter>);
    await screen.findByText('armada/feature-x');
    const bar = container.querySelector('.data-table .pagination-bar') as HTMLElement;
    expect(within(bar).getByTitle('Refresh merge queue')).toBeInTheDocument();
    expect(within(bar).getByLabelText('Auto-refresh interval')).toBeInTheDocument();
    await act(async () => { fireEvent.click(within(bar).getByRole('button', { name: /^Columns/ })); });
    const menu = screen.getByRole('menu', { name: 'Choose visible columns' });
    expect(within(menu).getByRole('menuitemcheckbox', { name: /^ID/ })).toHaveAttribute('aria-disabled', 'true');
    expect(within(menu).getByRole('menuitemcheckbox', { name: /^Branch/ })).toHaveAttribute('aria-disabled', 'true');
    expect(within(menu).getByRole('menuitemcheckbox', { name: /^Target/ })).not.toHaveAttribute('aria-disabled');
  });
});
