/**
 * Factory for `jest.mock('@dashboard/api/client', ...)`, mirroring the dashboard's vitest mocks: the real error
 * classes and pure helpers stay, every server call becomes a jest.fn() the test configures.
 *
 *   jest.mock('@dashboard/api/client', () => require('../test/mockClient').clientMockFactory());
 */
export function clientMockFactory(): Record<string, unknown> {
  const actual = jest.requireActual('@dashboard/api/client') as Record<string, unknown>;
  let authToken: string | null = null;
  let onUnauthorized: (() => void) | null = null;
  return {
    ...actual,
    configureClient: jest.fn(),
    setAuthToken: jest.fn((token: string | null) => { authToken = token; }),
    setOnUnauthorized: jest.fn((cb: () => void) => { onUnauthorized = cb; }),
    __getAuthToken: () => authToken,
    __fireUnauthorized: () => onUnauthorized?.(),
    whoami: jest.fn(),
    authenticate: jest.fn(),
    lookupTenants: jest.fn(),
    changePassword: jest.fn(),
    getInbox: jest.fn(async () => []),
    getHealth: jest.fn(),
    // Black box insight calls the dispatch forms, voyage detail and the entry screen make (BLACKBOX.md section 12):
    // a clean preview and lint, an empty timeline and nearby successes, and no cascade (404), unless a test says otherwise.
    previewMissionOverlap: jest.fn(async (vesselId: string) => emptyOverlapPreview(vesselId)),
    lintMissionBrief: jest.fn(async () => ({ findings: [], findingCount: 0, clean: true, pathsChecked: false })),
    getBlackBoxTimeline: jest.fn(async (entryId: string) => ({ ...emptyPage(), entryId, sources: [], truncated: false })),
    listBlackBoxNearbySuccesses: jest.fn(async () => emptyPage()),
    getVoyageBlackBoxCascade: jest.fn(async () => {
      const ApiError = actual.ApiError as new (message: string, status: number, data: unknown) => Error;
      throw new ApiError('Not found.', 404, null);
    }),
  };
}

/** An empty enumeration page. */
export function emptyPage() {
  return { success: true, pageNumber: 1, pageSize: 10, totalPages: 1, totalRecords: 0, objects: [], totalMs: 1 };
}

/** An overlap preview that found nothing. */
export function emptyOverlapPreview(vesselId = 'vsl_1') {
  return {
    vesselId, serializeOverlappingMissions: false, likelyFiles: [], likelyFileCount: 0, scopedFileCount: 0, briefPathCount: 0,
    trackedFilesKnown: true, warnings: [], warningCount: 0, suggestedDependsOnMissionId: null, activeMissionsCompared: 0,
    activeMissionsTruncated: false, briefLint: { findings: [], findingCount: 0, clean: true, pathsChecked: true },
  };
}
