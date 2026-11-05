import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { getMission, getMissionWaits, getVesselConcurrency, getVoyageTree, listMissionSummaries } from '../../api/client';
import { translateTemplate } from '../../i18n/runtime';
import type { MissionSummary, MissionTreeNode, VesselConcurrency, VoyageTree } from '../../types/models';
import VoyageMissionTree, { countRunning, treeIsInformative } from './VoyageMissionTree';
import MissionWaitsCard from './MissionWaitsCard';
import MissionHierarchyCard from './MissionHierarchyCard';
import VesselConcurrencyCard from '../vessels/VesselConcurrencyCard';

vi.mock('../../api/client', () => ({
  getVoyageTree: vi.fn(), getMissionWaits: vi.fn(), getMission: vi.fn(), listMissionSummaries: vi.fn(), getVesselConcurrency: vi.fn(),
}));
vi.mock('../../lib/useLiveRefresh', () => ({ useLiveRefresh: () => {} }));
const locale = {
  locale: 'en', t: (key: string, params?: Record<string, string | number | null | undefined>) => translateTemplate('en', key, null, params),
  formatDateTime: (value: string | null | undefined) => value ?? '',
};
vi.mock('../../context/LocaleContext', () => ({ useLocale: () => locale }));

function summary(id: string, title: string, status: string, extra: Partial<MissionSummary> = {}): MissionSummary {
  return { id, title, status, tenantId: null, userId: null, voyageId: 'vyg_1', vesselId: 'vsl_1', captainId: null, priority: 100, parentMissionId: null, persona: null,
    dependsOnMissionId: null, branchName: null, dockId: null, processId: null, prUrl: null, commitHash: null, failureReason: null, requiresReview: false,
    reviewDenyAction: 'RetryStage', reviewComment: null, reviewedByUserId: null, reviewRequestedUtc: null, reviewedUtc: null, descriptionLength: 0,
    diffSnapshotLength: 0, agentOutputLength: 0, createdUtc: '2026-11-05T10:00:00Z', startedUtc: null, completedUtc: null, totalRuntimeMs: null,
    lastUpdateUtc: '2026-11-05T10:00:00Z', ...extra } as MissionSummary;
}
function node(mission: MissionSummary, stages: MissionSummary[] = [], children: MissionTreeNode[] = []): MissionTreeNode { return { mission, stages, children }; }

const parent = summary('msn_p', 'Add the audit table [Worker]', 'Complete');
const child = summary('msn_c', 'Write audit entries [Worker]', 'InProgress', { parentMissionId: 'msn_p', branchStrategy: 'ContinueParent' });
const fork = summary('msn_f', 'Show the audit page [Worker]', 'Pending', { parentMissionId: 'msn_p', branchStrategy: 'ForkFromParent' });
const tree: VoyageTree = { voyageId: 'vyg_1', title: 'Audit', totalMissions: 4, truncated: false, maxDepth: 1,
  roots: [node(parent, [summary('msn_pj', 'Add the audit table [Judge]', 'Complete')], [node(child), node(fork)])] };

describe('voyage mission tree', () => {
  beforeEach(() => { vi.mocked(getVoyageTree).mockReset().mockResolvedValue(structuredClone(tree)); });

  it('renders parents, indented children, branch markers, stage pills and running against the limit', async () => {
    render(<MemoryRouter><VoyageMissionTree voyageId="vyg_1" missionLimit={2} /></MemoryRouter>);
    expect(await screen.findByText('Add the audit table')).toHaveAttribute('title', 'Add the audit table [Worker]');
    expect(screen.getAllByText('Worker', { selector: 'a.mission-tree-stage' })[0]).toHaveAttribute('href', '/missions/msn_p');
    expect(screen.getByText('continues parent')).toBeInTheDocument();
    expect(screen.getByText('forks from parent')).toBeInTheDocument();
    expect(screen.getByText('Judge')).toHaveAttribute('href', '/missions/msn_pj');
    expect(screen.getByText('1 / 2 running')).toBeInTheDocument();
    expect(screen.getByText('4 missions, 2 levels deep')).toBeInTheDocument();
    const childItem = screen.getByText('Write audit entries').closest('li');
    expect(childItem?.parentElement).toHaveClass('mission-tree-children');
  });

  it('is hidden for a flat voyage without stages and notes a truncated tree', async () => {
    vi.mocked(getVoyageTree).mockResolvedValue({ ...tree, maxDepth: 0, roots: [node(summary('msn_a', 'Alone', 'Pending'))] });
    const { container } = render(<MemoryRouter><VoyageMissionTree voyageId="vyg_1" /></MemoryRouter>);
    await waitFor(() => expect(getVoyageTree).toHaveBeenCalled());
    expect(container.querySelector('[data-testid="voyage-mission-tree"]')).toBeNull();
    vi.mocked(getVoyageTree).mockResolvedValue({ ...tree, truncated: true, totalMissions: 1500 });
    render(<MemoryRouter><VoyageMissionTree voyageId="vyg_2" missionLimit={0} /></MemoryRouter>);
    expect(await screen.findByText('Showing the oldest 1000 of 1500 missions; the table below lists them all.')).toBeInTheDocument();
    expect(screen.getByText('1 running (no limit)')).toBeInTheDocument();
  });

  it('counts running logical missions and decides when the tree is worth showing', () => {
    expect(countRunning(tree.roots)).toBe(1);
    expect(treeIsInformative(tree)).toBe(true);
    expect(treeIsInformative(null)).toBe(false);
  });
});

describe('mission waits and hierarchy', () => {
  it('summarizes an ongoing wait and lists the history', async () => {
    vi.mocked(getMissionWaits).mockResolvedValue({ success: true, pageNumber: 1, pageSize: 100, totalPages: 1, totalRecords: 2, totalMs: 1, objects: [
      { id: 'mwi_1', tenantId: null, missionId: 'msn_c', vesselId: null, voyageId: null, captainId: null, gate: 'Parent', startedUtc: '2026-11-05T10:00:00Z', endedUtc: '2026-11-05T10:05:00Z', detail: 'ParentNotFinished' },
      { id: 'mwi_2', tenantId: null, missionId: 'msn_c', vesselId: null, voyageId: null, captainId: null, gate: 'CaptainSlot', startedUtc: '2026-11-05T10:05:00Z', endedUtc: '2026-11-05T10:08:00Z', detail: null },
    ] });
    render(<MemoryRouter><MissionWaitsCard missionId="msn_c" readyUtc="2026-11-05T10:05:00Z" assignedUtc="2026-11-05T10:08:00Z" /></MemoryRouter>);
    expect(await screen.findByText('Waited 8m before starting (Parent mission 5m, Captain slot 3m)')).toBeInTheDocument();
    expect(screen.getByText('Slot wait 3m')).toBeInTheDocument();
    expect(screen.getByText('Wait history (2)')).toBeInTheDocument();
    expect(screen.getByText('ParentNotFinished')).toBeInTheDocument();
  });

  it('hides the waits card when nothing was recorded', async () => {
    vi.mocked(getMissionWaits).mockResolvedValue({ success: true, pageNumber: 1, pageSize: 100, totalPages: 0, totalRecords: 0, totalMs: 1, objects: [] });
    const { container } = render(<MemoryRouter><MissionWaitsCard missionId="msn_x" /></MemoryRouter>);
    await waitFor(() => expect(getMissionWaits).toHaveBeenCalledWith('msn_x', 1, 100));
    expect(container.querySelector('[data-testid="mission-waits"]')).toBeNull();
  });

  it('shows the parent chain and the children with their branch markers', async () => {
    vi.mocked(getMission).mockImplementation(async (id: string) => ({ id, title: id === 'msn_p' ? 'Parent work' : 'Grandparent work', parentMissionId: id === 'msn_p' ? 'msn_g' : null }) as never);
    vi.mocked(listMissionSummaries).mockResolvedValue({ success: true, pageNumber: 1, pageSize: 50, totalPages: 1, totalRecords: 1, totalMs: 1, objects: [fork] });
    render(<MemoryRouter><MissionHierarchyCard mission={{ id: 'msn_c', parentMissionId: 'msn_p', lastUpdateUtc: 'x' }} /></MemoryRouter>);
    expect(await screen.findByText('Grandparent work')).toHaveAttribute('href', '/missions/msn_g');
    expect(screen.getByText('Parent work')).toHaveAttribute('href', '/missions/msn_p');
    expect(await screen.findByText('Children (1)')).toBeInTheDocument();
    expect(screen.getByText('forks from parent')).toBeInTheDocument();
    expect(listMissionSummaries).toHaveBeenCalledWith(expect.objectContaining({ filters: expect.objectContaining({ parentMissionId: 'msn_c' }) }));
  });
});

describe('vessel concurrency card', () => {
  it('shows slots against the limit, holders, waiting missions and slot wait', async () => {
    const report: VesselConcurrency = { vesselId: 'vsl_1', name: 'api', maxParallelVoyages: 2, maxParallelMissionsPerVoyage: 3, voyageSlotsInUse: 2, runningMissions: 3,
      holdingVoyages: [{ voyageId: 'vyg_1', missionId: null, title: 'Audit', runningMissions: 2, pendingMissions: 1 }],
      waiting: [{ missionId: 'msn_w', title: 'Queued work', voyageId: 'vyg_2', gate: 'VesselVoyageSlot', detail: null, waitingSinceUtc: '2026-11-05T10:00:00Z', waitedSeconds: 540 }],
      waitingCount: 4,
      slotWait24h: { windowHours: 24, missions: 3, p50Seconds: 60, p95Seconds: 600, maxSeconds: 900 },
      slotWait7d: { windowHours: 168, missions: 0, p50Seconds: null, p95Seconds: null, maxSeconds: null },
      computedUtc: '2026-11-05T10:09:00Z' };
    vi.mocked(getVesselConcurrency).mockResolvedValue(report);
    render(<MemoryRouter><VesselConcurrencyCard vesselId="vsl_1" /></MemoryRouter>);
    expect(await screen.findByText('2 / 2 in use')).toHaveClass('warning');
    expect(screen.getByText('3 missions running; up to 3 per voyage')).toBeInTheDocument();
    expect(screen.getByText('p50 1m, p95 10m, max 15m (3 missions)')).toBeInTheDocument();
    expect(screen.getByText('No waits recorded')).toBeInTheDocument();
    expect(screen.getByText('Audit')).toHaveAttribute('href', '/voyages/vyg_1');
    expect(screen.getByText('Waiting (1 of 4)')).toBeInTheDocument();
    expect(screen.getByText('Vessel voyage slot')).toBeInTheDocument();
    expect(screen.getByText('9m')).toBeInTheDocument();
  });
});
