import { fireEvent, screen } from '@testing-library/react-native';
import * as client from '@dashboard/api/client';
import type { MissionSummary, VesselConcurrency, VoyageTree } from '@dashboard/types/models';
import { MissionChildrenSection, MissionWaitsSection } from '../screens/operations/mission/MissionWaitsSection';
import { VoyageTreeSection } from '../screens/operations/voyage/VoyageTreeSection';
import { VesselConcurrencySection } from '../screens/vessels/VesselConcurrencySection';
import { page } from '../test/operationsClient';
import { mockRouter } from '../test/routerMock';
import { renderScreen } from '../test/screen';

jest.mock('expo-router', () => require('../test/routerMock').routerMockFactory());
jest.mock('@dashboard/api/client', () => require('../test/operationsClient').operationsClientMockFactory());
const api = client as jest.Mocked<typeof client>;

function summary(id: string, title: string, extra: Partial<MissionSummary> = {}): MissionSummary {
  return { id, title, status: 'Pending', voyageId: 'vyg_1', vesselId: 'vsl_1', parentMissionId: null, dependsOnMissionId: null, branchStrategy: 'New', createdUtc: '2026-11-01T00:00:00Z', ...extra } as MissionSummary;
}

beforeEach(() => jest.clearAllMocks());

it('draws the voyage tree with stages, branch markers and the running count', async () => {
  const tree: VoyageTree = { voyageId: 'vyg_1', title: 'Voyage', totalMissions: 3, truncated: false, maxDepth: 1, roots: [{
    mission: summary('msn_a', 'Schema [Implementation]', { status: 'InProgress' }),
    stages: [summary('msn_b', 'Schema [Review]')],
    children: [{ mission: summary('msn_c', 'API', { parentMissionId: 'msn_a', branchStrategy: 'ForkFromParent' }), stages: [], children: [] }],
  }] };
  api.getVoyageTree.mockResolvedValue(tree);
  await renderScreen(<VoyageTreeSection voyageId="vyg_1" missionLimit={2} />);
  expect(await screen.findByTestId('voyage-tree-running')).toHaveTextContent('1 / 2 running');
  expect(screen.getByTestId('voyage-tree-node-msn_a')).toHaveTextContent(/Schema/);
  expect(screen.queryByTestId('voyage-tree-node-msn_b')).toBeNull();
  fireEvent.press(screen.getByTestId('voyage-tree-node-msn_c'));
  expect(mockRouter.push).toHaveBeenCalledWith('/missions/msn_c');
});

it('hides the tree for a flat voyage', async () => {
  api.getVoyageTree.mockResolvedValue({ voyageId: 'vyg_1', title: 'Voyage', totalMissions: 2, truncated: false, maxDepth: 0,
    roots: [{ mission: summary('msn_a', 'One'), stages: [], children: [] }, { mission: summary('msn_b', 'Two'), stages: [], children: [] }] });
  await renderScreen(<VoyageTreeSection voyageId="vyg_1" />);
  await new Promise(r => setTimeout(r, 0));
  expect(screen.queryByTestId('voyage-tree')).toBeNull();
});

it('summarizes waits and lists child missions', async () => {
  api.getMissionWaits.mockResolvedValue(page([
    { id: 'mwi_1', tenantId: null, missionId: 'msn_1', vesselId: 'vsl_1', voyageId: 'vyg_1', captainId: null, gate: 'CaptainSlot', startedUtc: '2026-11-01T00:00:00Z', endedUtc: '2026-11-01T00:02:00Z', detail: null },
  ]));
  api.listMissionSummaries.mockResolvedValue(page([summary('msn_2', 'Child', { parentMissionId: 'msn_1', branchStrategy: 'ForkFromParent' })]));
  await renderScreen(<>
    <MissionWaitsSection mission={{ id: 'msn_1', readyUtc: '2026-11-01T00:00:00Z', assignedUtc: '2026-11-01T00:02:00Z', lastUpdateUtc: '2026-11-01T00:02:00Z' }} />
    <MissionChildrenSection missionId="msn_1" />
  </>);
  expect(await screen.findByTestId('mission-waits')).toHaveTextContent(/Waited 2m/);
  expect(await screen.findByTestId('mission-child-msn_2')).toHaveTextContent(/Child/);
  expect(api.listMissionSummaries.mock.calls[0][0]).toMatchObject({ filters: { parentMissionId: 'msn_1' } });
});

it('shows vessel slots, slot wait and the waiting missions', async () => {
  const stats = { windowHours: 24, missions: 0, p50Seconds: null, p95Seconds: null, maxSeconds: null };
  const report: VesselConcurrency = { vesselId: 'vsl_1', name: 'Repo', maxParallelVoyages: 1, maxParallelMissionsPerVoyage: 2, voyageSlotsInUse: 1, runningMissions: 2,
    holdingVoyages: [], waiting: [{ missionId: 'msn_9', title: 'Queued', voyageId: 'vyg_2', gate: 'VesselVoyageSlot', detail: null, waitingSinceUtc: '2026-11-01T00:00:00Z', waitedSeconds: 90 }],
    waitingCount: 1, slotWait24h: stats, slotWait7d: { ...stats, windowHours: 168, missions: 3, p50Seconds: 30, p95Seconds: 120, maxSeconds: 200 }, computedUtc: '2026-11-01T00:00:00Z' };
  api.getVesselConcurrency.mockResolvedValue(report);
  await renderScreen(<VesselConcurrencySection vesselId="vsl_1" />);
  const section = await screen.findByTestId('vessel-concurrency');
  expect(section).toHaveTextContent(/1 \/ 1 in use/);
  expect(section).toHaveTextContent(/Full/);
  expect(section).toHaveTextContent(/No waits recorded/);
  expect(section).toHaveTextContent(/3 missions/);
  expect(screen.getByTestId('vessel-waiting-msn_9')).toHaveTextContent(/Queued/);
});
