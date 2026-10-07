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
  ];
  const mocks: Record<string, unknown> = {};
  for (const name of names) mocks[name] = jest.fn(async () => undefined);
  return { ...base, ...mocks };
}
