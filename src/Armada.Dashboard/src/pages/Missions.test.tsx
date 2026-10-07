import { act, fireEvent, render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import Missions from './Missions';
import { listCaptains, listMissionSummaries, listVessels, listVoyages } from '../api/client';
import type { MissionSummary } from '../types/models';

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
  listMissionSummaries: vi.fn(),
  listVessels: vi.fn(),
  listCaptains: vi.fn(),
  listVoyages: vi.fn(),
  createMission: vi.fn(),
  updateMission: vi.fn(),
  deleteMission: vi.fn(),
  purgeMission: vi.fn(),
  restartMission: vi.fn(),
  retryMissionLanding: vi.fn(),
  transitionMission: vi.fn(),
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

const BRANCH = 'armada/captain-1/msn_1-a-very-long-branch-name';
const mission = {
  id: 'msn_1', tenantId: null, userId: null, voyageId: 'vyg_1', vesselId: null, captainId: null, title: 'Fix the login flow',
  status: 'InProgress', priority: 100, branchName: BRANCH, createdUtc: '2026-10-01T00:00:00Z',
} as unknown as MissionSummary;

function page<T>(objects: T[]) {
  return { success: true, pageNumber: 1, pageSize: 25, totalPages: 1, totalRecords: objects.length, totalMs: 1, objects } as never;
}

describe('Missions table', () => {
  beforeEach(() => {
    localStorage.clear();
    vi.mocked(listMissionSummaries).mockResolvedValue(page([mission]));
    vi.mocked(listVessels).mockResolvedValue(page([]));
    vi.mocked(listCaptains).mockResolvedValue(page([]));
    vi.mocked(listVoyages).mockResolvedValue(page([]));
  });

  it('puts refresh controls in the table toolbar and locks Title and ID in the column chooser', async () => {
    const { container } = render(<MemoryRouter><Missions /></MemoryRouter>);
    await screen.findByText('Fix the login flow');
    const bar = container.querySelector('.data-table .pagination-bar') as HTMLElement;
    expect(within(bar).getByText('1 record')).toBeInTheDocument();
    expect(within(bar).getByLabelText('Auto-refresh interval')).toBeInTheDocument();
    expect(within(bar).getByTitle('Refresh mission data')).toBeInTheDocument();
    expect(container.querySelector('.page-header .auto-refresh-select')).toBeNull();

    await act(async () => { fireEvent.click(within(bar).getByRole('button', { name: /^Columns/ })); });
    const menu = screen.getByRole('menu', { name: 'Choose visible columns' });
    expect(within(menu).getByRole('menuitemcheckbox', { name: /^Title/ })).toHaveAttribute('aria-disabled', 'true');
    expect(within(menu).getByRole('menuitemcheckbox', { name: /^ID/ })).toHaveAttribute('aria-disabled', 'true');
    fireEvent.click(within(menu).getByRole('menuitemcheckbox', { name: /^Branch/ }));
    expect(screen.queryByText(BRANCH)).not.toBeInTheDocument();
  });

  it('keeps the branch on one line with the full name in the tooltip', async () => {
    render(<MemoryRouter><Missions /></MemoryRouter>);
    const branch = await screen.findByText(BRANCH);
    expect(branch).toHaveClass('url-value');
    expect(branch.closest('td')).toHaveClass('table-url-cell');
    expect(branch.closest('td')).toHaveAttribute('title', BRANCH);
  });
});
