import { act, fireEvent, render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import Missions from './Missions';
import { getMission, createMission, listCaptains, listMissionSummaries, listVessels, listVoyages, previewMissionOverlap } from '../api/client';
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
  getMission: vi.fn(),
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
  previewMissionOverlap: vi.fn(),
  lintMissionBrief: vi.fn(),
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
    expect(within(bar).getByTitle('Reload the mission list from the server now.')).toBeInTheDocument();
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


describe('Mission duplication', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(listMissionSummaries).mockResolvedValue(page([mission]));
    vi.mocked(listVessels).mockResolvedValue(page([{ id: 'vsl_1', name: 'Vessel' }]));
    vi.mocked(listCaptains).mockResolvedValue(page([]));
    vi.mocked(listVoyages).mockResolvedValue(page([]));
    vi.mocked(getMission).mockResolvedValue({ ...mission, vesselId: 'vsl_1', mode: 'Audit', description: 'Complete untruncated instructions', persona: 'Judge', requiresReview: true, reviewDenyAction: 'FailPipeline' } as never);
    vi.mocked(createMission).mockResolvedValue({ ...mission, id: 'msn_copy' } as never);
  });

  it('loads full instructions and creates an edited copy without modifying the original execution', async () => {
    render(<MemoryRouter><Missions /></MemoryRouter>);
    await screen.findByText(mission.title);
    fireEvent.click(screen.getByRole('button', { name: 'Actions' }));
    fireEvent.click(screen.getByRole('menuitem', { name: 'Duplicate' }));
    await screen.findByRole('heading', { name: 'Duplicate Mission' });
    expect(screen.getByLabelText('Description')).toHaveValue('Complete untruncated instructions');
    expect(createMission).not.toHaveBeenCalled();
    fireEvent.change(screen.getByLabelText('Title'), { target: { value: 'Independent audit' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save Copy' }));
    await vi.waitFor(() => expect(createMission).toHaveBeenCalledWith(expect.objectContaining({ title: 'Independent audit', description: 'Complete untruncated instructions', mode: 'Audit', persona: 'Judge' })));
    const payload = vi.mocked(createMission).mock.calls[0][0];
    expect(payload).not.toHaveProperty('id'); expect(payload).not.toHaveProperty('branchName'); expect(payload).not.toHaveProperty('voyageId');
  });

  it('related-table links open a cancellable draft without creating anything', async () => {
    render(<MemoryRouter initialEntries={['/missions?duplicate=msn_1']}><Missions /></MemoryRouter>);
    await screen.findByRole('heading', { name: 'Duplicate Mission' });
    expect(getMission).toHaveBeenCalledTimes(1);
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    expect(createMission).not.toHaveBeenCalled();
  });
});

describe('Mission create dispatch warnings', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    localStorage.clear();
    vi.mocked(listMissionSummaries).mockResolvedValue(page([mission]));
    vi.mocked(listVessels).mockResolvedValue(page([{ id: 'vsl_1', name: 'Vessel' }]));
    vi.mocked(listCaptains).mockResolvedValue(page([]));
    vi.mocked(listVoyages).mockResolvedValue(page([]));
    vi.mocked(previewMissionOverlap).mockResolvedValue({
      vesselId: 'vsl_1', serializeOverlappingMissions: false, likelyFiles: ['src/a.ts'], likelyFileCount: 1, scopedFileCount: 1, briefPathCount: 0, trackedFilesKnown: true,
      warnings: [{ kind: 'ActiveMission', overlappingMissionId: 'msn_1', message: 'Preview overlap with msn_1', files: ['src/a.ts'], fileCount: 1 }], warningCount: 1,
      suggestedDependsOnMissionId: 'msn_1', activeMissionsCompared: 1, activeMissionsTruncated: false, briefLint: null,
    } as never);
  });

  async function fillAndCreate() {
    render(<MemoryRouter><Missions /></MemoryRouter>);
    await screen.findByText(mission.title);
    fireEvent.click(screen.getByRole('button', { name: '+ Mission' }));
    fireEvent.change(screen.getByLabelText('Title'), { target: { value: 'Touch the parser' } });
    fireEvent.change(screen.getByLabelText('Description'), { target: { value: 'Touch only src/a.ts' } });
    fireEvent.change(screen.getByLabelText(/^Vessel/), { target: { value: 'vsl_1' } });
  }

  it('previews overlaps in the form before submit', async () => {
    vi.mocked(createMission).mockResolvedValue({ ...mission, id: 'msn_new' } as never);
    await fillAndCreate();
    expect(await screen.findByText('Preview overlap with msn_1', undefined, { timeout: 3000 })).toBeInTheDocument();
    const calls = vi.mocked(previewMissionOverlap).mock.calls;
    expect(calls[calls.length - 1][0]).toBe('vsl_1');
  });

  it('shows the warnings the create response carried, and nothing when it has none', async () => {
    vi.mocked(createMission).mockResolvedValueOnce({ ...mission, id: 'msn_new', overlapWarnings: [{ kind: 'Hotspot', directory: 'src', message: 'src is a conflict hotspot' }], briefLintWarnings: [{ ruleId: 'BRIEF-DEP-001', severity: 'Warning', message: 'Dependency without dependsOnMissionId' }] } as never);
    await fillAndCreate();
    fireEvent.click(screen.getByRole('button', { name: 'Create' }));
    const banner = await screen.findByTestId('dispatch-warnings-banner');
    expect(within(banner).getByText('src is a conflict hotspot')).toBeInTheDocument();
    expect(within(banner).getByText('BRIEF-DEP-001')).toBeInTheDocument();
    fireEvent.click(within(banner).getByRole('button', { name: 'Dismiss' }));
    expect(screen.queryByTestId('dispatch-warnings-banner')).not.toBeInTheDocument();

    vi.mocked(createMission).mockResolvedValueOnce({ ...mission, id: 'msn_clean' } as never);
    fireEvent.click(screen.getByRole('button', { name: '+ Mission' }));
    fireEvent.change(screen.getByLabelText('Title'), { target: { value: 'Clean' } });
    fireEvent.change(screen.getByLabelText(/^Vessel/), { target: { value: 'vsl_1' } });
    fireEvent.click(screen.getByRole('button', { name: 'Create' }));
    await vi.waitFor(() => expect(createMission).toHaveBeenCalledTimes(2));
    expect(screen.queryByTestId('dispatch-warnings-banner')).not.toBeInTheDocument();
  });
});
