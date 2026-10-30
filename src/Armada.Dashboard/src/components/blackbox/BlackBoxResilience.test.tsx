import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import BlackBoxPanel from './BlackBoxPanel';
import BlackBoxResilienceGapsTab from './BlackBoxResilienceGapsTab';
import {
  enumerateBlackBoxResilienceGaps,
  getBlackBoxBrief,
  getMissionBlackBox,
  listBlackBoxNotes,
  listBlackBoxObjects,
  listMissionBlackBoxEntries,
  listVessels,
} from '../../api/client';
import { translateTemplate } from '../../i18n/runtime';
import type { BlackBoxEntry, BlackBoxResilienceGap } from '../../types/blackbox';
import { findArmadaVessel, proposedChangeBrief, topProposedChange } from '../../lib/blackboxResilience';
import { blackBoxAttemptsFixture, blackBoxEntryFixture, blackBoxResilienceFixture, page } from '../../test/fixtures/blackbox';

vi.mock('../../api/client', () => ({
  addBlackBoxNote: vi.fn(),
  applyBlackBoxRecommendation: vi.fn(),
  downloadBlackBox: vi.fn(),
  getBlackBox: vi.fn(),
  getBlackBoxBrief: vi.fn(),
  getMissionBlackBox: vi.fn(),
  getVoyageBlackBox: vi.fn(),
  listBlackBoxNotes: vi.fn(),
  listBlackBoxObjects: vi.fn(),
  listMissionBlackBoxEntries: vi.fn(),
  pinBlackBox: vi.fn(),
  unpinBlackBox: vi.fn(),
  reanalyzeBlackBox: vi.fn(),
  readBlackBoxObject: vi.fn(),
  resolveBlackBox: vi.fn(),
  runBlackBoxRecovery: vi.fn(),
  getBlackBoxTimeline: vi.fn(),
  diffBlackBoxEntries: vi.fn(),
  listBlackBoxNearbySuccesses: vi.fn(),
  listVessels: vi.fn(),
  enumerateBlackBoxResilienceGaps: vi.fn(),
}));

const localeValue = {
  t: (text: string, params?: Record<string, string | number | null | undefined>) => translateTemplate('en', text, null, params),
  locale: 'en',
  formatDateTime: (v: string | null | undefined) => v ?? '',
  formatRelativeTime: (v: string | null | undefined) => v ?? '',
};
vi.mock('../../context/LocaleContext', () => ({ useLocale: () => localeValue }));
vi.mock('../../context/NotificationContext', () => ({ useNotifications: () => ({ pushToast: vi.fn() }) }));
vi.mock('../../context/AuthContext', () => ({ useAuth: () => ({ isAdmin: true, isTenantAdmin: true }) }));
const socket = { connected: true, reconnectCount: 0, send: vi.fn(), subscribe: () => () => undefined };
vi.mock('../../context/WebSocketContext', () => ({ useWebSocket: () => socket }));

function WhereAmI() {
  const location = useLocation();
  return <div data-testid="where">{location.pathname}{location.search}|{JSON.stringify(location.state)}</div>;
}

function inScopeEntry(suspected: boolean): BlackBoxEntry {
  const base = blackBoxEntryFixture({ triage: 'ArmadaDefect', step: 'TargetIntegration' });
  return {
    ...base,
    record: {
      ...base.record!,
      analysis: {
        ...base.record!.analysis!,
        triage: { verdict: 'ArmadaDefect', confidence: suspected ? 'Low' : 'High', priorApplied: true, suspected },
        resilience: blackBoxResilienceFixture(),
      },
      handoff: {
        stepTotal: 2,
        steps: [{ timestampUtc: '2026-10-28T10:00:00Z', source: 'Mission', kind: 'mission.work_produced', message: 'work produced' }, { timestampUtc: '2026-10-28T10:01:00Z', source: 'Mission', kind: 'mission.landing_failed' }],
        autoRecoveries: [{ kind: 'LandingRetry', attempts: 2, outcome: 'Expired' }],
        targetMoved: true,
      },
    },
  };
}

function renderPanel() {
  return render(
    <MemoryRouter initialEntries={['/missions/msn_1']}>
      <Routes>
        <Route path="/missions/:id" element={<BlackBoxPanel missionId="msn_1" />} />
        <Route path="/ask" element={<WhereAmI />} />
        <Route path="/dispatch" element={<WhereAmI />} />
      </Routes>
    </MemoryRouter>,
  );
}

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(getBlackBoxBrief).mockResolvedValue('# FIX THIS\nbrief body');
  vi.mocked(listMissionBlackBoxEntries).mockResolvedValue(page(blackBoxAttemptsFixture()) as never);
  vi.mocked(listBlackBoxNotes).mockResolvedValue(page([]) as never);
  vi.mocked(listBlackBoxObjects).mockResolvedValue(page([]) as never);
});

describe('Armada resilience section', () => {
  it('sits right after the Summary and shows the full Armada angle in scope', async () => {
    vi.mocked(getMissionBlackBox).mockResolvedValue(inScopeEntry(true));
    renderPanel();
    const section = await screen.findByTestId('blackbox-resilience');
    expect(section).toHaveAttribute('data-in-scope', 'true');
    const sections = Array.from(document.querySelectorAll('section.blackbox-section')).map((s) => s.getAttribute('data-testid'));
    expect(sections.indexOf('blackbox-resilience')).toBe(sections.indexOf('blackbox-summary') + 1);
    expect(sections.indexOf('blackbox-resilience')).toBeLessThan(sections.indexOf('blackbox-prevent'));
    expect(within(section).getByTestId('blackbox-self-healable')).toHaveTextContent('Self-healable: Yes');
    expect(within(section).getByText(/recreated the worktree from armada\/request-logging/)).toBeInTheDocument();
    expect(within(section).getByText('The dock was reclaimed while the mission still had to land.')).toBeInTheDocument();
    expect(within(section).getByText('Race')).toBeInTheDocument();
    expect(within(section).getByText(/stays on disk until the landing finishes/)).toBeInTheDocument();
    expect(within(section).getByText(/DockService.cs:ReclaimAsync/)).toBeInTheDocument();
    expect(within(section).getByText(/1\.2\.0 commit abc9999/)).toBeInTheDocument();
    expect(within(section).getByText('Severity: Critical')).toBeInTheDocument();
    expect(within(section).getByText('Seen in 2 Armada versions')).toBeInTheDocument();
    expect(within(section).getByText('LandingRetry x2 (Expired)')).toBeInTheDocument();
    expect(within(section).getByText(/Remove the dock directory before landing/)).toBeInTheDocument();
    const titles = within(screen.getByTestId('blackbox-proposed-changes')).getAllByText(/^\d\. /).map((n) => n.textContent);
    expect(titles).toEqual(['1. Recreate a missing dock worktree from the branch before landing', '2. Never reclaim a dock whose mission still has to land']);
    expect(within(section).getByTestId('blackbox-handoff-trail')).toHaveTextContent('mission.landing_failed');
  });

  it('shows a short line outside the handoff and landing scope and no Armada defect badge', async () => {
    const base = blackBoxEntryFixture();
    vi.mocked(getMissionBlackBox).mockResolvedValue({ ...base, record: { ...base.record!, analysis: { ...base.record!.analysis!, resilience: { inScope: false, selfHealable: 'No', severity: 'Low', proposedChanges: [], noChangeReason: 'No Armada change suggested: the failure is outside Armada\'s handoff and landing (phase Execution, step UsageLimit) and the triage is Transient.' } } } });
    renderPanel();
    const section = await screen.findByTestId('blackbox-resilience');
    expect(section).toHaveAttribute('data-in-scope', 'false');
    expect(within(section).getByTestId('blackbox-resilience-none')).toHaveTextContent('No Armada change suggested');
    expect(within(section).queryByTestId('blackbox-draft-armada-fix')).toBeNull();
    expect(screen.queryByTestId('blackbox-armada-defect-badge')).toBeNull();
  });

  it('badges a suspected Armada defect and a proven one differently', async () => {
    vi.mocked(getMissionBlackBox).mockResolvedValue(inScopeEntry(true));
    const { unmount } = renderPanel();
    const suspected = await screen.findByTestId('blackbox-armada-defect-badge');
    expect(suspected).toHaveTextContent('Suspected Armada defect');
    expect(suspected).toHaveAttribute('data-suspected', 'true');
    expect(suspected.getAttribute('title')).toMatch(/BBX-PRIOR-ARMADA/);
    unmount();
    vi.mocked(getMissionBlackBox).mockResolvedValue(inScopeEntry(false));
    renderPanel();
    const proven = await screen.findByTestId('blackbox-armada-defect-badge');
    expect(proven).toHaveTextContent('Armada defect');
    expect(proven).toHaveAttribute('data-suspected', 'false');
  });

  it('drafts the Armada fix mission on Dispatch, targeted at the Armada vessel', async () => {
    vi.mocked(getMissionBlackBox).mockResolvedValue(inScopeEntry(true));
    vi.mocked(listVessels).mockResolvedValue(page([{ id: 'vsl_app', name: 'api-server', repoUrl: 'https://github.com/o/api' }, { id: 'vsl_armada', name: 'core', repoUrl: 'https://github.com/jchristn/Armada.git' }]) as never);
    renderPanel();
    fireEvent.click(await screen.findByTestId('blackbox-draft-armada-fix'));
    const where = await screen.findByTestId('where');
    expect(where.textContent).toMatch(/^\/dispatch\|/);
    const state = JSON.parse(where.textContent!.split('|').slice(1).join('|'));
    expect(state).toMatchObject({ fromBlackBox: true, vesselId: 'vsl_armada', voyageTitle: 'Recreate a missing dock worktree from the branch before landing' });
    expect(state.prompt).toContain('Restore the worktree.');
  });

  it('falls back to Ask Armada when there is no Armada vessel', async () => {
    vi.mocked(getMissionBlackBox).mockResolvedValue(inScopeEntry(true));
    vi.mocked(listVessels).mockResolvedValue(page([{ id: 'vsl_app', name: 'api-server', repoUrl: 'https://github.com/o/api' }]) as never);
    renderPanel();
    fireEvent.click(await screen.findByTestId('blackbox-draft-armada-fix'));
    const where = await screen.findByTestId('where');
    expect(where.textContent).toMatch(/^\/ask\?prompt=/);
    expect(decodeURIComponent(where.textContent!)).toContain('Draft an Armada fix mission for black box entry bbx_2');
    expect(decodeURIComponent(where.textContent!)).toContain('Recreate a missing dock worktree');
  });
});

describe('resilience helpers', () => {
  it('finds the Armada vessel by name first, then by repository URL', () => {
    expect(findArmadaVessel([{ id: 'a', name: 'x', repoUrl: 'git@github.com:o/armada.git' }, { id: 'b', name: 'Armada', repoUrl: null }])?.id).toBe('b');
    expect(findArmadaVessel([{ id: 'a', name: 'x', repoUrl: 'git@github.com:o/armada.git' }])?.id).toBe('a');
    expect(findArmadaVessel([{ id: 'a', name: 'armada-docs', repoUrl: 'https://github.com/o/armada-docs' }])).toBeNull();
  });

  it('ranks changes and builds a brief when the change has none', () => {
    const res = blackBoxResilienceFixture();
    expect(topProposedChange(res)?.rank).toBe(1);
    const brief = proposedChangeBrief(res.proposedChanges![0], { id: 'bbx_2' });
    expect(brief).toContain('Acceptance test: Services.DockLease');
    expect(brief).toContain('black box entry bbx_2 (rule BBX-LAND-002)');
  });
});

describe('Armada resilience gaps tab', () => {
  function gap(i: number, overrides: Partial<BlackBoxResilienceGap> = {}): BlackBoxResilienceGap {
    return {
      fingerprint: `fp_gap_${i}_0123456789`, phase: 'Landing', step: 'TargetIntegration', occurrences: 6 - i, openCount: 2, firstSeenUtc: '2026-10-01T00:00:00Z', lastSeenUtc: '2026-10-28T00:00:00Z',
      latestEntryId: `bbx_${i}`, armadaVersions: ['1.1.0', '1.2.0'], severity: i === 1 ? 'Critical' : 'Medium', selfHealable: 'Yes', suspected: i !== 1,
      topProposedChange: { rank: 1, title: `Change ${i}`, change: 'Do it.', severity: 'High' }, detectionGap: 'No preflight check.', ...overrides,
    };
  }

  it('lists the gaps grouped by fingerprint and pages through them', async () => {
    vi.mocked(enumerateBlackBoxResilienceGaps).mockResolvedValue(page([gap(1), gap(2)], { totalPages: 2, totalRecords: 30 }) as never);
    render(<MemoryRouter><BlackBoxResilienceGapsTab vessels={[]} /></MemoryRouter>);
    expect(await screen.findByText('Change 1')).toBeInTheDocument();
    expect(screen.getByText('Change 2')).toBeInTheDocument();
    expect(screen.getByText('Critical')).toBeInTheDocument();
    expect(screen.getByText('Suspected')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'bbx_1' })).toHaveAttribute('href', '/blackbox/bbx_1');
    expect(vi.mocked(enumerateBlackBoxResilienceGaps)).toHaveBeenLastCalledWith(expect.objectContaining({ pageNumber: 1, resolution: 'Open' }));
    const next = screen.getAllByRole('button').find((b) => /next/i.test(b.getAttribute('aria-label') ?? b.textContent ?? ''));
    expect(next).toBeTruthy();
    fireEvent.click(next!);
    await waitFor(() => expect(vi.mocked(enumerateBlackBoxResilienceGaps)).toHaveBeenLastCalledWith(expect.objectContaining({ pageNumber: 2 })));
  });

  it('shows the empty state', async () => {
    vi.mocked(enumerateBlackBoxResilienceGaps).mockResolvedValue(page([]) as never);
    render(<MemoryRouter><BlackBoxResilienceGapsTab vessels={[]} /></MemoryRouter>);
    expect(await screen.findByText('No Armada resilience gaps')).toBeInTheDocument();
  });
});
