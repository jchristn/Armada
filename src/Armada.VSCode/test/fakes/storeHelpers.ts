import { BlackBoxStore, StoreSettings } from '../../src/surfaces/blackbox/blackBoxStore';
import { MockFetch, page, useMock } from './fetch';
import { SERVER_VESSEL_A, SERVER_VESSEL_B, serverSummary } from './fixtures';

export const SETTINGS: StoreSettings = { filterToWorkspaceVessel: true, includeResolved: false, maxEntries: 50, decorationsEnabled: true, hotspotLimit: 10 };

export function standardMock(): MockFetch {
  return new MockFetch()
    .on('GET', '/api/v1/vessels', { json: page([SERVER_VESSEL_A, SERVER_VESSEL_B]) })
    .on('POST', '/api/v1/blackbox/enumerate', (req) => {
      const body = req.body as { VesselId?: string };
      const all = [
        serverSummary({ Id: 'bbx_1', VesselId: 'vsl_a', MissionId: 'msn_x', CreatedUtc: '2026-10-28T10:00:00Z' }),
        serverSummary({ Id: 'bbx_2', VesselId: 'vsl_a', MissionId: 'msn_w', CreatedUtc: '2026-10-27T10:00:00Z' }),
        serverSummary({ Id: 'bbx_3', VesselId: 'vsl_b', VesselName: 'bravo', MissionId: 'msn_q', CreatedUtc: '2026-10-29T10:00:00Z' }),
      ];
      const objects = body.VesselId ? all.filter((e) => e.VesselId === body.VesselId) : all;
      return { json: page(objects, objects.length + 10) };
    })
    .on('GET', '/api/v1/blackbox/bbx_1/sections/Conflict', { json: { EntryId: 'bbx_1', Section: 'Conflict', Record: { EntryId: 'bbx_1', SchemaVersion: 1, Conflict: { IsConflict: true, ConflictedFiles: ['src/a.ts', './src\\b.ts'], ConflictedFileTotal: 2 } } } })
    .on('GET', '/api/v1/blackbox/bbx_2/sections/Conflict', { json: { EntryId: 'bbx_2', Section: 'Conflict', Record: { EntryId: 'bbx_2', SchemaVersion: 1, Conflict: { IsConflict: true, ConflictedFiles: ['src/a.ts'] } } } })
    .on('POST', '/api/v1/blackbox/hotspots/enumerate', { json: page([{ Directory: 'src/', FileCount: 2, EntryCount: 3 }]) });
}

/** A store reading through the mock (or disconnected when mock is undefined). */
export function makeStore(mock: MockFetch | undefined, options: { folders?: string[]; remotes?: Record<string, string[]>; settings?: Partial<StoreSettings>; tenant?: string } = {}): BlackBoxStore {
  if (mock) useMock(mock);
  return new BlackBoxStore({
    getConnection: () => (mock ? { tenant: options.tenant } : undefined),
    getWorkspaceFolderPaths: () => options.folders ?? ['/work/alpha'],
    getSettings: () => ({ ...SETTINGS, ...options.settings }),
    readRemotes: (folder) => (options.remotes ?? { '/work/alpha': ['git@github.com:org/alpha.git'] })[folder] ?? [],
  });
}
