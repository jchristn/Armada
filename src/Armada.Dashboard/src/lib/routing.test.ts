import { describe, expect, it } from 'vitest';
import { entityRoute, notificationRoute } from './routing';

describe('entityRoute', () => {
  it('routes by entity type, not by the id prefix', () => {
    expect(entityRoute('mission', 'abc')).toBe('/missions/abc');
    expect(entityRoute('Vessel', 'xyz')).toBe('/vessels/xyz');
    expect(entityRoute('captain', 'msn_looks_like_mission')).toBe('/captains/msn_looks_like_mission');
    expect(entityRoute('deployment', 'dpl_1')).toBe('/deployments/dpl_1');
  });

  it('accepts the server spellings of one kind', () => {
    expect(entityRoute('MergeEntry', 'm1')).toBe('/merge-queue/m1');
    expect(entityRoute('merge-entry', 'm1')).toBe('/merge-queue/m1');
    expect(entityRoute('merge_entry', 'm1')).toBe('/merge-queue/m1');
    expect(entityRoute('check-run', 'c1')).toBe('/checks/c1');
    expect(entityRoute('CheckRun', 'c1')).toBe('/checks/c1');
  });

  it('returns null without a known type', () => {
    expect(entityRoute(null, 'msn_1')).toBeNull();
    expect(entityRoute('Harbor', 'h1')).toBeNull();
    expect(entityRoute('mission', null)).toBeNull();
    expect(entityRoute('constructor', 'x')).toBeNull();
  });
});

describe('notificationRoute', () => {
  it('uses whichever typed id field is set', () => {
    expect(notificationRoute({ missionId: 'a', voyageId: null, captainId: null })).toBe('/missions/a');
    expect(notificationRoute({ missionId: null, voyageId: 'b', captainId: null })).toBe('/voyages/b');
    expect(notificationRoute({ missionId: null, voyageId: null, captainId: 'c' })).toBe('/captains/c');
    expect(notificationRoute({ missionId: null, voyageId: null, captainId: null })).toBeNull();
  });
});
