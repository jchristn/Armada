/**
 * `jest.mock('@dashboard/api/client', ...)` factory for the Ask and approvals tests: the W0 mock (real error
 * classes and pure helpers, auth calls as jest.fn) plus every Ask, CLI permission, approval, and list call the
 * Ask Armada and Approvals screens make, each a jest.fn() the test configures.
 *
 *   jest.mock('@dashboard/api/client', () => require('../test/askFixtures').askClientMockFactory());
 */
export function askClientMockFactory(): Record<string, unknown> {
  const base = (jest.requireActual('./mockClient') as { clientMockFactory: () => Record<string, unknown> }).clientMockFactory();
  const names = [
    'listCaptains', 'getCaptainTools', 'getAskQuickActions', 'enumerateAskThreads', 'createAskThread', 'getAskThread',
    'updateAskThread', 'deleteAskThread', 'enumerateAskMessages', 'sendAskMessage', 'cancelAskTurn', 'summarizeAskThread',
    'markAskThreadRead', 'runAskQuickAction', 'approveAskProposal', 'rejectAskProposal', 'getAskWorkSnapshot',
    'setAskThreadCliPermissionPolicy', 'decideCliPermissionRequest', 'getCliPermissionRequest', 'listVessels', 'listPipelines',
    'enumerateFleetActions', 'approveMissionReview', 'denyMissionReview', 'approveDeployment', 'denyDeployment',
    'retryMissionLanding', 'restartMission', 'stopCaptain',
    'enumerateAskWork', 'enumerateAskWorkMissions', 'enumerateAskWorkTargets', 'enumerateAskProposals', 'enumerateAskToolCalls',
    'enumerateAskTurnToolCalls', 'listCliPermissionRequests',
  ];
  const mocks: Record<string, unknown> = {};
  for (const name of names) mocks[name] = jest.fn(async () => undefined);
  return { ...base, ...mocks };
}

/**
 * `jest.mock('@dashboard/lib/askPaging', ...)` factory: the screens read a thread's collections through askPaging (unit
 * tested in the dashboard); these tests describe the composed view directly through the mocked getAskThread.
 *
 *   jest.mock('@dashboard/lib/askPaging', () => require('../test/askFixtures').askPagingMockFactory());
 */
export function askPagingMockFactory(): Record<string, unknown> {
  const actual = jest.requireActual('@dashboard/lib/askPaging') as Record<string, unknown>;
  return {
    ...actual,
    loadAskThreadView: (id: string) => (jest.requireMock('@dashboard/api/client') as { getAskThread: (id: string) => unknown }).getAskThread(id),
    withToolCalls: async (_threadId: string, messages: unknown) => messages,
    withWorkRows: async (_threadId: string, _workId: string, snapshot: unknown) => snapshot,
    loadToolCalls: async () => [],
  };
}
