import { ArmadaBlackBoxClient } from '../../src/api/blackbox';
import { BlackBoxStore, StoreSettings } from '../../src/state/blackBoxStore';
import { MockFetch, page } from './fetch';
import { client, summary, VESSEL_A, VESSEL_B } from './fixtures';

export const SETTINGS: StoreSettings = { filterToWorkspaceVessel: true, includeResolved: false, maxEntries: 50, decorationsEnabled: true, hotspotLimit: 10 };

export function standardMock(): MockFetch {
  return new MockFetch()
    .on('GET', '/api/v1/vessels', { json: page([VESSEL_A, VESSEL_B]) })
    .on('POST', '/api/v1/blackbox/enumerate', (req) => {
      const body = req.body as { VesselId?: string };
      const all = [
        summary({ Id: 'bbx_1', VesselId: 'vsl_a', MissionId: 'msn_x', CreatedUtc: '2026-10-28T10:00:00Z' }),
        summary({ Id: 'bbx_2', VesselId: 'vsl_a', MissionId: 'msn_w', CreatedUtc: '2026-10-27T10:00:00Z' }),
        summary({ Id: 'bbx_3', VesselId: 'vsl_b', VesselName: 'bravo', MissionId: 'msn_q', CreatedUtc: '2026-10-29T10:00:00Z' }),
      ];
      const objects = body.VesselId ? all.filter((e) => e.VesselId === body.VesselId) : all;
      return { json: page(objects, objects.length + 10) };
    })
    .on('GET', '/api/v1/blackbox/bbx_1/sections/Conflict', { json: { IsConflict: true, ConflictedFiles: ['src/a.ts', { Path: './src\\b.ts' }] } })
    .on('GET', '/api/v1/blackbox/bbx_2/sections/Conflict', { json: { IsConflict: true, ConflictedFiles: ['src/a.ts'] } })
    .on('POST', '/api/v1/blackbox/hotspots/enumerate', { json: page([{ Directory: 'src/', ConflictCount: 3 }]) });
}

export function makeStore(mock: MockFetch | undefined, options: { folders?: string[]; remotes?: Record<string, string[]>; settings?: Partial<StoreSettings> } = {}): BlackBoxStore {
  const c: ArmadaBlackBoxClient | undefined = mock ? client(mock) : undefined;
  return new BlackBoxStore({
    getClient: () => c,
    getWorkspaceFolderPaths: () => options.folders ?? ['/work/alpha'],
    getSettings: () => ({ ...SETTINGS, ...options.settings }),
    readRemotes: (folder) => (options.remotes ?? { '/work/alpha': ['git@github.com:org/alpha.git'] })[folder] ?? [],
  });
}
