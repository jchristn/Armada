import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useNavigate } from 'react-router-dom';
import MissionDetail from './MissionDetail';
import { getHarbor, getMission, getMissionLandingPreview, listCaptains, listCheckRuns, listDeployments, listVessels, purgeMission, retryMissionLanding } from '../api/client';
import { translateTemplate } from '../i18n/runtime';
import type { Captain, Harbor, LandingPreviewResult, Mission } from '../types/models';

vi.mock('../api/client', () => ({
  getMission: vi.fn(),
  updateMission: vi.fn(),
  deleteMission: vi.fn(),
  purgeMission: vi.fn(),
  getMissionDiff: vi.fn(),
  getMissionGitHubPullRequest: vi.fn(),
  getMissionLog: vi.fn(),
  getMissionInstructions: vi.fn(),
  getMissionLandingPreview: vi.fn(),
  restartMission: vi.fn(),
  retryMissionLanding: vi.fn(),
  transitionMission: vi.fn(),
  approveMissionReview: vi.fn(),
  denyMissionReview: vi.fn(),
  listCheckRuns: vi.fn(),
  listVessels: vi.fn(),
  listCaptains: vi.fn(),
  listDeployments: vi.fn(),
  getHarbor: vi.fn(),
  getVesselBranches: vi.fn(),
  pushVesselBranch: vi.fn(),
  mergeVesselBranch: vi.fn(),
}));

const localeValue = {
  t: (text: string, params?: Record<string, string | number | null | undefined>) => translateTemplate('en', text, null, params),
  locale: 'en',
  formatDateTime: (v: string | null | undefined) => v ?? '',
  formatRelativeTime: (v: string | null | undefined) => v ?? '',
};
vi.mock('../context/LocaleContext', () => ({ useLocale: () => localeValue }));
vi.mock('../context/NotificationContext', () => ({ useNotifications: () => ({ pushToast: vi.fn() }) }));
vi.mock('../context/AuthContext', () => ({ useAuth: () => ({ isAdmin: true, isTenantAdmin: true }) }));
vi.mock('../context/WebSocketContext', () => ({
  useWebSocket: () => ({ connected: true, reconnectCount: 0, send: vi.fn(), subscribe: () => () => {} }),
}));

const emptyPage = { success: true, pageNumber: 1, pageSize: 1000, totalPages: 1, totalRecords: 0, totalMs: 1, objects: [] };

function mission(overrides: Partial<Mission>): Mission {
  return {
    id: 'msn_1', tenantId: 'default', voyageId: null, vesselId: 'vsl_1', captainId: null, title: 'Add request logging',
    status: 'Pending', branchName: 'armada/request-logging', requiresReview: false,
    ...overrides,
  } as unknown as Mission;
}

function preview(overrides: Partial<LandingPreviewResult>): LandingPreviewResult {
  return {
    vesselId: 'vsl_1', missionId: 'msn_1', sourceBranch: 'armada/request-logging', targetBranch: 'main', branchCategory: 'Feature',
    targetBranchProtected: false, protectedBranchMatch: null, landingMode: 'LocalMerge', branchCleanupPolicy: null,
    requirePassingChecksToLand: false, requirePullRequestForProtectedBranches: false, requireMergeQueueForReleaseBranches: false,
    expectedLandingAction: null, hasPassingChecks: false, latestCheckRunId: null, latestCheckStatus: null, latestCheckSummary: null,
    isReadyToLand: false, issues: [],
    ...overrides,
  } as LandingPreviewResult;
}

function renderPage() {
  render(
    <MemoryRouter initialEntries={['/missions/msn_1']}>
      <Routes>
        <Route path="/missions/:id" element={<MissionDetail />} />
      </Routes>
    </MemoryRouter>,
  );
}

describe('MissionDetail waiting and landing', () => {
  beforeEach(() => {
    vi.mocked(listVessels).mockResolvedValue(emptyPage as never);
    vi.mocked(listCaptains).mockResolvedValue(emptyPage as never);
    vi.mocked(listCheckRuns).mockResolvedValue(emptyPage as never);
    vi.mocked(listDeployments).mockResolvedValue(emptyPage as never);
  });

  it('shows the concrete runtime limit and remediation on the failed mission', async () => {
    const reason = 'The inference response hit the output-token limit (4096).\nRemediation: Increase Max Output Tokens in the inference endpoint.';
    vi.mocked(getMission).mockResolvedValue(mission({ status: 'Failed', failureReason: reason }));
    vi.mocked(getMissionLandingPreview).mockResolvedValue(preview({ missionStatus: 'Failed' }));
    renderPage();
    expect(await screen.findByText('Failure Reason')).toBeInTheDocument();
    expect(screen.getByText(/The inference response hit the output-token limit/)).toHaveTextContent('4096');
    expect(screen.getByText(/The inference response hit the output-token limit/)).toHaveTextContent('Remediation: Increase Max Output Tokens');
  });

  it('links a failed API captain to the relevant configuration surfaces', async () => {
    vi.mocked(listCaptains).mockResolvedValue({ ...emptyPage, objects: [{ id: 'cpt_api', name: 'DeepSeek', runtime: 'ApiEndpoint', modelEndpointId: 'mep_deepseek' }] } as never);
    vi.mocked(getMission).mockResolvedValue(mission({ status: 'Failed', captainId: 'cpt_api', failureReason: 'Provider error HTTP 401. Remediation: Check endpoint credentials.' }));
    vi.mocked(getMissionLandingPreview).mockResolvedValue(preview({ missionStatus: 'Failed' }));
    renderPage();
    expect(await screen.findByRole('link', { name: 'Open inference endpoints' })).toHaveAttribute('href', '/configuration?tab=endpoints');
    expect(screen.getByRole('link', { name: 'Open captain configuration' })).toHaveAttribute('href', '/captains/cpt_api');
    expect(screen.getByRole('link', { name: 'Open vessel configuration' })).toHaveAttribute('href', '/vessels/vsl_1');
  });

  it('does not offer an inference endpoint link for a CLI captain', async () => {
    vi.mocked(listCaptains).mockResolvedValue({ ...emptyPage, objects: [{ id: 'cpt_cli', name: 'Codex', runtime: 'Codex' }] } as never);
    vi.mocked(getMission).mockResolvedValue(mission({ status: 'Failed', captainId: 'cpt_cli', failureReason: 'CLI is not installed.' }));
    vi.mocked(getMissionLandingPreview).mockResolvedValue(preview({ missionStatus: 'Failed' }));
    renderPage();
    expect(await screen.findByRole('link', { name: 'Open captain configuration' })).toHaveAttribute('href', '/captains/cpt_cli');
    expect(screen.queryByRole('link', { name: 'Open inference endpoints' })).not.toBeInTheDocument();
  });

  it('explains why a Pending mission waits and never shows Ready To Land (F11)', async () => {
    vi.mocked(getMission).mockResolvedValue(mission({
      status: 'Pending',
      assignmentBlocker: {
        reason: 'NoIdleCaptain',
        summary: "Waiting for a captain: Setup Captain is refining backlog item 'Add retries'.",
        untilUtc: null,
        dependsOnMissionId: null,
        blockingMissionIds: [],
        captains: [{
          captainId: 'cpt_1', captainName: 'Setup Captain', state: 'Refining', detail: "refining backlog item 'Add retries'",
          missionId: null, planningSessionId: null, refinementSessionId: 'ors_1', objectiveId: 'obj_1', quarantineUntilUtc: null, quarantineReason: null,
        }],
        computedUtc: '2026-10-05T00:00:00Z',
      },
    }));
    vi.mocked(getMissionLandingPreview).mockResolvedValue(preview({
      isReadyToLand: false,
      missionStatus: 'Pending',
      issues: [{ code: 'mission_not_landable', severity: 'Warning', title: 'Mission is not in a landing state', message: 'Pending' }],
    }));

    renderPage();

    expect(await screen.findByText('Why This Mission Is Waiting')).toBeInTheDocument();
    expect(screen.getByText("Waiting for a captain: Setup Captain is refining backlog item 'Add retries'.")).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Setup Captain' })).toHaveAttribute('href', '/captains/cpt_1');
    expect(screen.getByRole('link', { name: 'Open backlog item' })).toHaveAttribute('href', '/backlog/obj_1');
    expect(await screen.findByText('Not Ready Yet')).toBeInTheDocument();
    expect(screen.queryByText('Ready To Land')).not.toBeInTheDocument();
  });

  it('dims idle captains that may not take the mission and keeps eligible ones plain', async () => {
    const idle = { missionId: null, planningSessionId: null, refinementSessionId: null, objectiveId: null, quarantineUntilUtc: null, quarantineReason: null };
    vi.mocked(getMission).mockResolvedValue(mission({
      status: 'Pending',
      assignmentBlocker: {
        reason: 'NoEligibleCaptain',
        summary: 'Waiting for a captain: no idle captain may take this mission.',
        untilUtc: null,
        dependsOnMissionId: null,
        blockingMissionIds: [],
        captains: [
          { ...idle, captainId: 'cpt_flint', captainName: 'Mux flint', state: 'Idle', detail: 'idle (not assigned: persona not allowed)', eligibleForMission: false },
          { ...idle, captainId: 'cpt_opus', captainName: 'Opus', state: 'Idle', detail: 'idle (assigned)', eligibleForMission: true },
        ],
        computedUtc: '2026-10-05T00:00:00Z',
      },
    }));
    vi.mocked(getMissionLandingPreview).mockResolvedValue(preview({ isReadyToLand: false, missionStatus: 'Pending' }));

    renderPage();

    const notAssigned = await screen.findByText('idle (not assigned: persona not allowed)');
    expect(notAssigned).toHaveClass('text-dim');
    expect(notAssigned.closest('li')).toHaveAttribute('data-eligible', 'false');
    const assigned = screen.getByText('idle (assigned)');
    expect(assigned).not.toHaveClass('text-dim');
    expect(assigned.closest('li')).toHaveAttribute('data-eligible', 'true');
  });

  it('offers Merge in Manage Branches instead of Land when landing is manual only (F12)', async () => {
    vi.mocked(getMission).mockResolvedValue(mission({ status: 'WorkProduced' }));
    vi.mocked(getMissionLandingPreview).mockResolvedValue(preview({ landingMode: 'None', effectiveLandingMode: 'None', manualLandingOnly: true, isReadyToLand: false, missionStatus: 'WorkProduced' }));

    renderPage();

    const header = await screen.findByRole('heading', { name: 'Add request logging' });
    expect(await screen.findByRole('button', { name: 'Merge in Manage Branches' })).toBeInTheDocument();
    expect(screen.getByText('Merge By Hand')).toBeInTheDocument();
    const actions = header.closest('.page-header') ?? document.body;
    expect(within(actions as HTMLElement).queryByRole('button', { name: 'Land' })).not.toBeInTheDocument();
    expect(screen.queryByText('Ready To Land')).not.toBeInTheDocument();
  });

  it('keeps Land for an automatic landing mode', async () => {
    vi.mocked(getMission).mockResolvedValue(mission({ status: 'WorkProduced' }));
    vi.mocked(getMissionLandingPreview).mockResolvedValue(preview({ effectiveLandingMode: 'LocalMerge', manualLandingOnly: false, isReadyToLand: true, missionStatus: 'WorkProduced' }));

    renderPage();

    expect(await screen.findByRole('button', { name: 'Land' })).toBeInTheDocument();
    expect(await screen.findByText('Ready To Land')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Merge in Manage Branches' })).not.toBeInTheDocument();
  });
});

describe('MissionDetail Harbor and captain runtime', () => {
  beforeEach(() => {
    vi.mocked(listVessels).mockResolvedValue(emptyPage as never);
    vi.mocked(listCheckRuns).mockResolvedValue(emptyPage as never);
    vi.mocked(listDeployments).mockResolvedValue(emptyPage as never);
    vi.mocked(getMissionLandingPreview).mockResolvedValue(preview({ missionStatus: 'InProgress' }));
  });

  it('shows the Harbor name with a link and the captain runtime', async () => {
    const captain = { id: 'cpt_9', name: 'mux-1', runtime: 'Mux', tier: null } as unknown as Captain;
    vi.mocked(listCaptains).mockResolvedValue({ ...emptyPage, totalRecords: 1, objects: [captain] } as never);
    vi.mocked(getHarbor).mockResolvedValue({ id: 'hbr_1', name: 'Joel Laptop' } as unknown as Harbor);
    vi.mocked(getMission).mockResolvedValue(mission({ status: 'InProgress', captainId: 'cpt_9', assignedHarborId: 'hbr_1' }));

    renderPage();

    const link = await screen.findByRole('link', { name: 'Joel Laptop' });
    expect(link).toHaveAttribute('href', '/configuration?tab=harbors');
    expect(await screen.findByTestId('mission-captain-runtime')).toHaveTextContent('(Mux)');
  });

  it('says Admiral host when no Harbor took the mission', async () => {
    vi.mocked(listCaptains).mockResolvedValue(emptyPage as never);
    vi.mocked(getMission).mockResolvedValue(mission({ status: 'Pending', assignedHarborId: null }));

    renderPage();

    expect(await screen.findByText('Admiral host')).toBeInTheDocument();
    expect(screen.queryByTestId('mission-harbor')).not.toBeInTheDocument();
  });
  it('shows an infra landing failure in full with Retry Landing next to it (A4)', async () => {
    const reason = 'Landing was blocked: the checkout at /Users/dev/Code/app has uncommitted changes.\n M src/app.ts\n?? notes.txt';
    vi.mocked(getMission).mockResolvedValue(mission({ status: 'LandingFailed', failureKind: 'Infra', failureReason: reason }));
    vi.mocked(getMissionLandingPreview).mockResolvedValue(preview({ missionStatus: 'LandingFailed' }));
    vi.mocked(retryMissionLanding).mockResolvedValue({} as never);
    renderPage();
    await screen.findByText('Failure Reason');
    const block = document.querySelector('pre.mission-failure-reason') as HTMLElement;
    expect(block).not.toBeNull();
    expect(block.textContent).toBe(reason);
    expect(block).toHaveAttribute('data-failure-kind', 'Infra');
    const field = block.closest('.detail-field') as HTMLElement;
    fireEvent.click(within(field).getByRole('button', { name: 'Retry Landing' }));
    await waitFor(() => expect(retryMissionLanding).toHaveBeenCalledWith('msn_1'));
  });

  it('does not repeat Retry Landing beside the reason for other landing failures', async () => {
    vi.mocked(getMission).mockResolvedValue(mission({ status: 'LandingFailed', failureKind: 'LandingConflict', failureReason: 'Merge conflict in src/app.ts' }));
    vi.mocked(getMissionLandingPreview).mockResolvedValue(preview({ missionStatus: 'LandingFailed' }));
    renderPage();
    await screen.findByText('Failure Reason');
    const field = (document.querySelector('pre.mission-failure-reason') as HTMLElement).closest('.detail-field') as HTMLElement;
    expect(within(field).queryByRole('button', { name: 'Retry Landing' })).not.toBeInTheDocument();
  });

  it('shows the delete progress modal while a purge runs', async () => {
    let finish!: () => void;
    vi.mocked(purgeMission).mockReturnValue(new Promise<void>((resolve) => { finish = resolve; }));
    vi.mocked(getMission).mockResolvedValue(mission({ status: 'Failed', failureReason: 'x' }));
    vi.mocked(getMissionLandingPreview).mockResolvedValue(preview({ missionStatus: 'Failed' }));
    renderPage();
    await screen.findByText('Failure Reason');
    fireEvent.click(screen.getAllByRole('button', { name: 'Actions' })[0]);
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Purge' }));
    const confirm = screen.getByText('Purge Mission').closest('.modal-box') as HTMLElement;
    fireEvent.click(within(confirm).getByRole('button', { name: 'Yes' }));
    const progress = await screen.findByRole('alertdialog', { name: 'Purging mission' });
    expect(progress).toHaveTextContent('This may take some time while related records are cleaned up.');
    expect(progress).toHaveAttribute('aria-busy', 'true');
    await act(async () => { finish(); });
    await waitFor(() => expect(screen.queryByRole('alertdialog', { name: 'Purging mission' })).not.toBeInTheDocument());
    expect(purgeMission).toHaveBeenCalledTimes(1);
  });
});

interface Deferred<T> { promise: Promise<T>; resolve: (value: T) => void }

function deferred<T>(): Deferred<T> {
  let resolve: (value: T) => void = () => {};
  const promise = new Promise<T>((res) => { resolve = res; });
  return { promise, resolve };
}

describe('MissionDetail route load order', () => {
  const missions = new Map<string, Deferred<Mission>>();
  const previews = new Map<string, Deferred<LandingPreviewResult>>();

  function gate<T>(store: Map<string, Deferred<T>>, id: string): Deferred<T> {
    let entry = store.get(id);
    if (!entry) {
      entry = deferred<T>();
      store.set(id, entry);
    }
    return entry;
  }

  beforeEach(() => {
    missions.clear();
    previews.clear();
    vi.mocked(listVessels).mockResolvedValue(emptyPage as never);
    vi.mocked(listCaptains).mockResolvedValue(emptyPage as never);
    vi.mocked(listCheckRuns).mockResolvedValue(emptyPage as never);
    vi.mocked(listDeployments).mockResolvedValue(emptyPage as never);
    vi.mocked(getMission).mockImplementation((id: string) => gate(missions, id).promise);
    vi.mocked(getMissionLandingPreview).mockImplementation((id: string) => gate(previews, id).promise);
  });

  it('keeps the newer mission and its landing preview when the older request finishes last', async () => {
    function Harness() {
      const navigate = useNavigate();
      return (
        <>
          <button type="button" onClick={() => navigate('/missions/msn_b')}>Open route B</button>
          <Routes>
            <Route path="/missions/:id" element={<MissionDetail />} />
          </Routes>
        </>
      );
    }

    render(
      <MemoryRouter initialEntries={['/missions/msn_a']}>
        <Harness />
      </MemoryRouter>,
    );

    fireEvent.click(screen.getByRole('button', { name: 'Open route B' }));

    await act(async () => {
      gate(missions, 'msn_b').resolve(mission({ id: 'msn_b', title: 'Mission Beta', status: 'WorkProduced', branchName: 'feature-b' }));
    });
    expect(await screen.findByRole('heading', { name: 'Mission Beta' })).toBeInTheDocument();

    await act(async () => {
      gate(previews, 'msn_b').resolve(preview({
        missionId: 'msn_b', sourceBranch: 'feature-b', targetBranch: 'main',
        isReadyToLand: true, effectiveLandingMode: 'LocalMerge', manualLandingOnly: false, missionStatus: 'WorkProduced',
      }));
    });
    expect(await screen.findByText('feature-b -> main')).toBeInTheDocument();

    await act(async () => {
      gate(missions, 'msn_a').resolve(mission({ id: 'msn_a', title: 'Mission Alpha', status: 'WorkProduced', branchName: 'feature-a' }));
    });
    await act(async () => {
      const latePreview = previews.get('msn_a');
      if (latePreview) {
        latePreview.resolve(preview({
          missionId: 'msn_a', sourceBranch: 'feature-a', targetBranch: 'main',
          isReadyToLand: false, effectiveLandingMode: 'None', manualLandingOnly: true, missionStatus: 'WorkProduced',
        }));
      }
    });

    await waitFor(() => {
      expect(screen.getByRole('heading', { name: 'Mission Beta' })).toBeInTheDocument();
      expect(screen.queryByRole('heading', { name: 'Mission Alpha' })).not.toBeInTheDocument();
      expect(screen.getByText('feature-b -> main')).toBeInTheDocument();
      expect(screen.queryByText('feature-a -> main')).not.toBeInTheDocument();
    });
  });
});
