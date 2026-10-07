/**
 * Client mock for the Operations screens (W2): the foundation mock plus every call those screens make as a
 * jest.fn() (each test sets the results it needs).
 *
 *   jest.mock('@dashboard/api/client', () => require('../test/operationsClient').operationsClientMockFactory());
 */
const OPERATIONS_CALLS = [
  'getStatus', 'getHealth', 'getDoctor', 'getSettings',
  'listMissions', 'listMissionSummaries', 'getMission', 'getMissionHistory', 'getTokenUsage', 'getMissionLandingPreview',
  'getMissionGitHubPullRequest', 'createMission', 'updateMission', 'deleteMission', 'purgeMission', 'dispatchMission',
  'restartMission', 'retryMissionLanding', 'transitionMission', 'approveMissionReview', 'denyMissionReview',
  'getMissionDiff', 'getMissionLog', 'getMissionInstructions', 'getEntity',
  'listVoyages', 'getVoyage', 'getVoyageStatus', 'createVoyage', 'cancelVoyage', 'purgeVoyage',
  'listMergeQueue', 'getMergeEntry', 'enqueueMerge', 'deleteMergeEntry', 'processMergeEntry', 'processAllMergeQueue', 'cancelMergeEntry',
  'listDocks', 'getDock', 'deleteDock',
  'listSignals', 'getSignal', 'sendSignal', 'markSignalRead', 'deleteSignalsBatch',
  'listEvents', 'getEvent', 'deleteEventsBatch',
  'listVessels', 'getVessel', 'listCaptains', 'getCaptain', 'listFleets', 'getFleet',
  'enumerateFleetActionRuns', 'getVesselHealthSummary', 'enumerateVesselHealth',
  'listPipelines', 'listPlaybooks', 'listPersonas', 'listUsers',
];

export function operationsClientMockFactory(): Record<string, unknown> {
  const base = (jest.requireActual('./mockClient') as { clientMockFactory: () => Record<string, unknown> }).clientMockFactory();
  const actual = jest.requireActual('@dashboard/api/client') as Record<string, unknown>;
  const calls: Record<string, unknown> = {};
  for (const name of OPERATIONS_CALLS) {
    if (typeof actual[name] !== 'function') throw new Error(`operationsClient: ${name} is not a client export`);
    calls[name] = jest.fn(async () => { throw new Error(`${name} not mocked`); });
  }
  return { ...base, ...calls };
}

/** An enumeration result for list mocks. */
export function page<T>(objects: T[], totalRecords = objects.length, pageSize = 25) {
  return { success: true, pageNumber: 1, pageSize, totalPages: Math.max(1, Math.ceil(totalRecords / pageSize)), totalRecords, objects, totalMs: 1 };
}
