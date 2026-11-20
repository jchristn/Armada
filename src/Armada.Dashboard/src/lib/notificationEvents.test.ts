import { createElement } from 'react';
import { act, render } from '@testing-library/react';
import { beforeEach, describe, expect, it } from 'vitest';
import type { WebSocketMessage } from '../types/models';
import { NotificationProvider, useNotifications, type Notification as StoredNotification } from '../context/NotificationContext';
import {
  MAX_NOTIFICATIONS,
  buildEntityNotification,
  entityChangeDedupe,
  entityChangeFromMessage,
  newNotificationId,
  prependNotification,
  type Notification,
} from './notificationEvents';

const listeners: Array<(message: WebSocketMessage) => void> = [];

vi.mock('../context/WebSocketContext', () => ({
  useWebSocket: () => ({
    subscribe: (cb: (message: WebSocketMessage) => void) => {
      listeners.push(cb);
      return () => {};
    },
  }),
}));

vi.mock('../context/LocaleContext', () => ({
  useLocale: () => ({
    t: (text: string, params?: Record<string, string>) =>
      Object.entries(params ?? {}).reduce((acc, [key, value]) => acc.split(`{{${key}}}`).join(String(value)), text),
  }),
}));

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

  it('keeps the entity kind and id for a deployment, an objective, and an incident', () => {
    const deployment = buildEntityNotification(
      entityChangeFromMessage(msg('deployment.changed', { id: 'dpl_1', title: 'Ship', status: 'Succeeded' }), t)!,
      t, 'ntf_d', '2026-10-07T00:00:00.000Z');
    const objective = buildEntityNotification(
      entityChangeFromMessage(msg('objective.changed', { id: 'obj_1', title: 'Goal', status: 'Active' }), t)!,
      t, 'ntf_o', '2026-10-07T00:00:00.000Z');
    const incident = buildEntityNotification(
      entityChangeFromMessage(msg('incident.changed', { id: 'inc_1', title: 'Outage', status: 'Open' }), t)!,
      t, 'ntf_i', '2026-10-07T00:00:00.000Z');
    expect(field(deployment, 'kind')).toBe('Deployment');
    expect(field(deployment, 'entityId')).toBe('dpl_1');
    expect(field(objective, 'kind')).toBe('Objective');
    expect(field(objective, 'entityId')).toBe('obj_1');
    expect(field(incident, 'kind')).toBe('Incident');
    expect(field(incident, 'entityId')).toBe('inc_1');
  });
});

describe('notification event time', () => {
  let latest: StoredNotification[] = [];

  function Probe() {
    latest = useNotifications().notifications;
    return null;
  }

  beforeEach(() => {
    listeners.length = 0;
    latest = [];
    localStorage.clear();
  });

  function deliver(message: WebSocketMessage) {
    render(createElement(NotificationProvider, null, createElement(Probe)));
    act(() => {
      for (const listener of listeners) listener(message);
    });
  }

  it('stores the event timestamp', () => {
    deliver({
      type: 'deployment.changed',
      data: { id: 'dpl_2', title: 'Ship', status: 'Succeeded' },
      timestamp: '2020-01-02T03:04:05.000Z',
    });
    expect(latest).toHaveLength(1);
    expect(latest[0].timestampUtc).toBe('2020-01-02T03:04:05.000Z');
  });

  it('stores a browser time when the event has no timestamp', () => {
    deliver({ type: 'objective.changed', data: { id: 'obj_2', title: 'Goal', status: 'Active' } });
    expect(latest).toHaveLength(1);
    expect(latest[0].timestampUtc.length).toBeGreaterThan(0);
    expect(Number.isNaN(Date.parse(latest[0].timestampUtc))).toBe(false);
  });

  it('stores a browser time when the event timestamp is not a time', () => {
    deliver({
      type: 'incident.changed',
      data: { id: 'inc_2', title: 'Outage', status: 'Open' },
      timestamp: 'not-a-time',
    });
    expect(latest).toHaveLength(1);
    expect(latest[0].timestampUtc).not.toBe('not-a-time');
    expect(Number.isNaN(Date.parse(latest[0].timestampUtc))).toBe(false);
  });
});

function field(value: object, name: string): unknown {
  return (value as Record<string, unknown>)[name];
}
