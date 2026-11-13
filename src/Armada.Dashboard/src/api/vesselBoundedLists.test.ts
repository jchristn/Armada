import {
  browseAllVesselImport,
  deleteFleetsBatch,
  getFleet,
  listAllWorkspaceActiveMissions,
  listAllWorkspaceChanges,
  listVesselCleanupResources,
  listVesselDependencies,
  listWorkspaceDirectory,
  runVesselCleanup,
  setAuthToken,
} from './client';

function json(body: unknown) {
  return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } });
}

function body(call: unknown[]): Record<string, unknown> {
  return JSON.parse(String((call[1] as RequestInit).body));
}

describe('vessel, fleet, workspace and import clients never treat a first page as everything', () => {
  beforeEach(() => setAuthToken('tok'));
  afterEach(() => vi.unstubAllGlobals());

  it('getFleet unwraps the fleet from {Fleet, VesselCount}', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(json({ Fleet: { Id: 'flt_1', Name: 'core' }, VesselCount: 3 })));
    const fleet = await getFleet('flt_1');
    expect(fleet.id).toBe('flt_1');
    expect(fleet.name).toBe('core');
  });

  it('deleteFleetsBatch sends at most 1000 ids per request and merges the results', async () => {
    const fetchMock = vi.fn().mockImplementation(async (_url: string, init: RequestInit) => {
      const ids = JSON.parse(String(init.body)).Ids as string[];
      return json({ Deleted: ids.length, Skipped: [{ Id: ids[0], Reason: 'Not found' }] });
    });
    vi.stubGlobal('fetch', fetchMock);
    const ids = Array.from({ length: 2500 }, (_, i) => `flt_${i}`);
    const result = await deleteFleetsBatch(ids);
    expect(fetchMock).toHaveBeenCalledTimes(3);
    expect(fetchMock.mock.calls.map(c => (body(c).Ids as string[]).length)).toEqual([1000, 1000, 500]);
    expect(result.deleted).toBe(2500);
    expect(result.skipped).toHaveLength(3);
  });

  it('runVesselCleanup chunks keys by 1000 and aggregates the outcomes', async () => {
    const fetchMock = vi.fn().mockImplementation(async (_url: string, init: RequestInit) => {
      const keys = JSON.parse(String(init.body)).Keys as string[];
      return json({ Results: keys.map(k => ({ Key: k, Status: 'Deleted' })), Deleted: keys.length, Skipped: 0, Failed: 0, Inventory: { ResourceCount: 0 } });
    });
    vi.stubGlobal('fetch', fetchMock);
    const result = await runVesselCleanup('vsl_1', Array.from({ length: 1001 }, (_, i) => `dock:dck_${i}`));
    expect(fetchMock).toHaveBeenCalledTimes(2);
    expect(result.deleted).toBe(1001);
    expect(result.results).toHaveLength(1001);
  });

  it('cleanup resources and dependencies post one bounded page to their enumerate routes', async () => {
    const fetchMock = vi.fn().mockImplementation(async () => json({ PageNumber: 2, PageSize: 50, TotalPages: 3, TotalRecords: 120, Objects: [] }));
    vi.stubGlobal('fetch', fetchMock);
    await listVesselCleanupResources('vsl_1', { pageNumber: 2, pageSize: 50, status: 'Safe' });
    expect(fetchMock.mock.calls[0][0]).toBe('/api/v1/vessels/vsl_1/cleanup/resources/enumerate');
    expect(body(fetchMock.mock.calls[0])).toEqual({ PageNumber: 2, PageSize: 50, Status: 'Safe' });
    await listVesselDependencies('vsl_1', { pageNumber: 1, pageSize: 100 });
    expect(fetchMock.mock.calls[1][0]).toBe('/api/v1/vessels/vsl_1/health/dependencies/enumerate');
    expect(body(fetchMock.mock.calls[1])).toEqual({ PageNumber: 1, PageSize: 100 });
  });

  it('listWorkspaceDirectory requests pages of 1000 and follows TotalPages', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(json({ CurrentPath: '', PageNumber: 1, PageSize: 1000, TotalPages: 2, TotalRecords: 2, Entries: [{ Name: 'a' }] }))
      .mockResolvedValueOnce(json({ CurrentPath: '', PageNumber: 2, PageSize: 1000, TotalPages: 2, TotalRecords: 2, Entries: [{ Name: 'b' }] }));
    vi.stubGlobal('fetch', fetchMock);
    const tree = await listWorkspaceDirectory('vsl_1', 'src');
    expect(tree.entries.map(e => e.name)).toEqual(['a', 'b']);
    expect(fetchMock.mock.calls[0][0]).toBe('/api/v1/workspace/vessels/vsl_1/tree?path=src&pageNumber=1&pageSize=1000');
    expect(fetchMock.mock.calls[1][0]).toBe('/api/v1/workspace/vessels/vsl_1/tree?path=src&pageNumber=2&pageSize=1000');
  });

  it('listAllWorkspaceChanges and listAllWorkspaceActiveMissions walk every page', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(json({ IsDirty: true, PageNumber: 1, PageSize: 1000, TotalPages: 2, TotalRecords: 2, Changes: [{ Path: 'a' }] }))
      .mockResolvedValueOnce(json({ IsDirty: true, PageNumber: 2, PageSize: 1000, TotalPages: 2, TotalRecords: 2, Changes: [{ Path: 'b' }] }))
      .mockResolvedValueOnce(json({ PageNumber: 1, PageSize: 1000, TotalPages: 2, TotalRecords: 2, Objects: [{ MissionId: 'msn_1', ScopedFiles: [] }] }))
      .mockResolvedValueOnce(json({ PageNumber: 2, PageSize: 1000, TotalPages: 2, TotalRecords: 2, Objects: [{ MissionId: 'msn_2', ScopedFiles: [] }] }));
    vi.stubGlobal('fetch', fetchMock);
    const changes = await listAllWorkspaceChanges('vsl_1');
    expect(changes.changes.map(c => c.path)).toEqual(['a', 'b']);
    const missions = await listAllWorkspaceActiveMissions('vsl_1');
    expect(missions.map(m => m.missionId)).toEqual(['msn_1', 'msn_2']);
    expect(fetchMock.mock.calls[2][0]).toBe('/api/v1/workspace/vessels/vsl_1/active-missions/enumerate');
  });

  it('browseAllVesselImport follows TotalPages and keeps the truncated flag', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(json({ Path: '/r', PageNumber: 1, PageSize: 1000, TotalPages: 2, TotalRecords: 2, Truncated: true, Entries: [{ Name: 'a', Path: '/r/a' }] }))
      .mockResolvedValueOnce(json({ Path: '/r', PageNumber: 2, PageSize: 1000, TotalPages: 2, TotalRecords: 2, Truncated: true, Entries: [{ Name: 'b', Path: '/r/b' }] }));
    vi.stubGlobal('fetch', fetchMock);
    const result = await browseAllVesselImport('/r');
    expect(result.entries.map(e => e.name)).toEqual(['a', 'b']);
    expect(result.truncated).toBe(true);
    expect(String(fetchMock.mock.calls[1][0])).toContain('pageNumber=2&pageSize=1000');
  });
});
