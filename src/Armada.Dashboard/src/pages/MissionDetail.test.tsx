import { render, screen, within } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import MissionDetail from './MissionDetail';
import { getMission, getMissionLandingPreview, listCaptains, listCheckRuns, listDeployments, listVessels } from '../api/client';
import { translateTemplate } from '../i18n/runtime';
import type { LandingPreviewResult, Mission } from '../types/models';

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
