import { describe, expect, it } from 'vitest';
import type { WebSocketMessage } from '../types/models';
import { buildEntityNotification, entityChangeFromMessage } from './notificationEvents';
import { entityRoute, notificationRoute } from './routing';

const t = (text: string, params?: Record<string, string | number | null | undefined>) =>
  Object.entries(params ?? {}).reduce((acc, [key, value]) => acc.split(`{{${key}}}`).join(String(value)), text);

function message(type: string, data: unknown): WebSocketMessage {
  return { type, data };
}

function builtNotification(type: string, data: Record<string, unknown>) {
  const entry = entityChangeFromMessage(message(type, data), t);
  if (!entry) throw new Error('The event did not produce an entity change.');
  return buildEntityNotification(entry, t, 'ntf_1', '2026-10-07T00:00:00.000Z');
}

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

  it('opens a deployment from the built notification', () => {
    expect(notificationRoute(builtNotification('deployment.changed', { id: 'dpl_1', status: 'Succeeded' }))).toBe('/deployments/dpl_1');
  });

  it('opens an objective from the built notification', () => {
    expect(notificationRoute(builtNotification('objective.changed', { id: 'obj_1', status: 'Active' }))).toBe('/objectives/obj_1');
  });

  it('opens an incident from the built notification', () => {
    expect(notificationRoute(builtNotification('incident.changed', { id: 'inc_1', status: 'Open' }))).toBe('/incidents/inc_1');
  });

  it('opens a mission notification and an older entry that has only a mission id', () => {
    expect(notificationRoute(builtNotification('mission.changed', { id: 'msn_1', status: 'Failed' }))).toBe('/missions/msn_1');
    expect(notificationRoute({ missionId: 'msn_1', voyageId: null, captainId: null })).toBe('/missions/msn_1');
  });
});
