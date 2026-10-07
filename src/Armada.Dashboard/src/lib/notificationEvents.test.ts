import { describe, expect, it } from 'vitest';
import type { WebSocketMessage } from '../types/models';
import {
  MAX_NOTIFICATIONS,
  buildEntityNotification,
  entityChangeDedupe,
  entityChangeFromMessage,
  newNotificationId,
  prependNotification,
  type Notification,
} from './notificationEvents';

const t = (text: string, params?: Record<string, string | number | null | undefined>) =>
  Object.entries(params ?? {}).reduce((acc, [k, v]) => acc.split(`{{${k}}}`).join(String(v)), text);

function msg(type: string, data: unknown): WebSocketMessage {
  return { type, data };
}

describe('notificationEvents', () => {
  it('reads each notifiable entity change, captains by state', () => {
    expect(entityChangeFromMessage(msg('mission.changed', { id: 'msn_1', title: 'Fix', status: 'Failed' }), t))
      .toEqual({ kind: 'Mission', id: 'msn_1', name: 'Fix', status: 'Failed', verificationStatus: null });
    expect(entityChangeFromMessage(msg('captain.changed', { id: 'cpt_1', name: 'c1', state: 'Stalled' }), t)?.status).toBe('Stalled');
    expect(entityChangeFromMessage(msg('incident.changed', { id: 'inc_1', status: 'Open' }), t)?.name).toBe('inc_1');
  });

  it('ignores events without a status and other event types', () => {
    expect(entityChangeFromMessage(msg('mission.changed', { id: 'msn_1' }), t)).toBeNull();
    expect(entityChangeFromMessage(msg('ask.message', { status: 'x' }), t)).toBeNull();
    expect(entityChangeFromMessage(msg('mission.changed', 'not-an-object'), t)).toBeNull();
  });

  it('dedupes on state plus verification state', () => {
    const a = entityChangeFromMessage(msg('deployment.changed', { id: 'dpl_1', status: 'Succeeded', verificationStatus: 'Failed' }), t)!;
    expect(entityChangeDedupe(a)).toEqual({ key: 'Deployment:dpl_1', seen: 'Succeeded|Failed' });
  });

  it('builds the notification with severity, keyed text, and the entity link', () => {
    const entry = entityChangeFromMessage(msg('voyage.changed', { id: 'vyg_1', title: 'Batch', status: 'Complete' }), t)!;
    const n = buildEntityNotification(entry, t, 'ntf_1', '2026-10-07T00:00:00Z');
    expect(n).toMatchObject({ id: 'ntf_1', severity: 'success', title: 'Voyage Complete', message: 'Voyage "Batch" - Complete', voyageId: 'vyg_1', read: false });
  });

  it('caps the history', () => {
    let list: Notification[] = [];
    const entry = entityChangeFromMessage(msg('mission.changed', { id: 'm', status: 'Failed' }), t)!;
    for (let i = 0; i < MAX_NOTIFICATIONS + 5; i++) list = prependNotification(list, buildEntityNotification(entry, t, `n${i}`, ''));
    expect(list).toHaveLength(MAX_NOTIFICATIONS);
    expect(list[0].id).toBe(`n${MAX_NOTIFICATIONS + 4}`);
  });

  it('ids carry the ntf_ prefix and the timestamp', () => {
    expect(newNotificationId(42, () => 0.5)).toMatch(/^ntf_42_/);
  });
});
