import type { WebSocketMessage } from '../types/models';
import { deploymentApprovalLabel, type Translate } from './deploymentApprovalLabel';
import { entityStatusSeverity, type NotificationEntityKind, type Severity } from './notificationSeverity';

/**
 * Turns WebSocket state-change events into notification-center entries. Shared by the dashboard's
 * NotificationContext and the mobile app's notification center, so both raise the same notifications with the
 * same text for the same events.
 */

/** One entry in the notification history. */
export interface Notification {
  id: string;
  severity: Severity;
  title: string;
  message: string;
  timestampUtc: string;
  missionId: string | null;
  voyageId: string | null;
  captainId: string | null;
  read: boolean;
}

/** A state change of one entity, read from a WebSocket event. */
export interface EntityStateChange {
  kind: NotificationEntityKind;
  id: string;
  name: string;
  status: string;
  verificationStatus: string | null;
}

/** Most notifications kept in the history. */
export const MAX_NOTIFICATIONS = 100;

/** How long a toast stays on screen. */
export const TOAST_TIMEOUT_MS = 5000;

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null;
}

function change(
  kind: NotificationEntityKind,
  data: Record<string, unknown>,
  name: string,
  status: unknown,
  verificationStatus: string | null = null,
): EntityStateChange {
  return { kind, id: String(data.id || ''), name, status: String(status), verificationStatus };
}

/**
 * The entity state change an event announces, or null when the event is not a notifiable state change
 * (mission, voyage, captain, deployment, objective, and incident `.changed` events that carry a status).
 */
export function entityChangeFromMessage(msg: WebSocketMessage, t: Translate): EntityStateChange | null {
  const data = msg.data;
  if (!isRecord(data)) return null;

  if (msg.type === 'mission.changed' && data.status) {
    return change('Mission', data, String(data.title || data.id || ''), data.status);
  }
  if (msg.type === 'voyage.changed' && data.status) {
    return change('Voyage', data, String(data.title || data.id || ''), data.status);
  }
  // Captains report `state` (the legacy payload used `status`).
  if (msg.type === 'captain.changed' && (data.state || data.status)) {
    return change('Captain', data, String(data.name || data.id || ''), data.state || data.status);
  }
  if (msg.type === 'deployment.changed' && data.status) {
    // A deployment waiting for approval reads like every other approval surface: environment first.
    const name = data.status === 'PendingApproval'
      ? deploymentApprovalLabel(
        t,
        typeof data.environmentName === 'string' ? data.environmentName : null,
        typeof data.title === 'string' ? data.title : null,
        typeof data.id === 'string' ? data.id : null,
      )
      : String(data.title || data.id || '');
    return change('Deployment', data, name, data.status,
      typeof data.verificationStatus === 'string' ? data.verificationStatus : null);
  }
  if (msg.type === 'objective.changed' && data.status) {
    return change('Objective', data, String(data.title || data.id || ''), data.status);
  }
  if (msg.type === 'incident.changed' && data.status) {
    return change('Incident', data, String(data.title || data.id || ''), data.status);
  }
  return null;
}

/** Key and value used to drop repeats: the same entity reported again in the same state raises nothing. */
export function entityChangeDedupe(entry: EntityStateChange): { key: string; seen: string } {
  return {
    key: `${entry.kind}:${entry.id}`,
    seen: entry.verificationStatus ? `${entry.status}|${entry.verificationStatus}` : entry.status,
  };
}

/** Build the notification for a state change. `id` and `timestampUtc` come from the caller so tests are exact. */
export function buildEntityNotification(
  entry: EntityStateChange,
  t: Translate,
  id: string,
  timestampUtc: string,
): Notification {
  // Severity comes from the typed status values; the display text below is built only for people to read.
  const severity = entityStatusSeverity(entry.kind, entry.status, entry.verificationStatus);
  const statusText = entry.verificationStatus ? `${t(entry.status)} / ${t(entry.verificationStatus)}` : t(entry.status);
  const truncatedName = entry.name.length > 80 ? entry.name.substring(0, 80) + '...' : entry.name;
  // Keyed templates: the entity name is a parameter, so it is inserted verbatim and never translated.
  const title = t('{{entity}} {{status}}', { entity: t(entry.kind), status: statusText });
  const message = t('{{entity}} "{{name}}" - {{status}}', { entity: t(entry.kind), name: truncatedName, status: statusText });
  return {
    id,
    severity,
    title,
    message,
    timestampUtc,
    missionId: entry.kind === 'Mission' ? entry.id : null,
    voyageId: entry.kind === 'Voyage' ? entry.id : null,
    captainId: entry.kind === 'Captain' ? entry.id : null,
    read: false,
  };
}

/** A fresh notification id (`ntf_<ms>_<random>`). */
export function newNotificationId(now: number = Date.now(), random: () => number = Math.random): string {
  return `ntf_${now}_${random().toString(36).substring(2, 8)}`;
}

/** Prepend a notification and cap the history at MAX_NOTIFICATIONS. */
export function prependNotification(list: Notification[], notification: Notification): Notification[] {
  const next = [notification, ...list];
  if (next.length > MAX_NOTIFICATIONS) next.length = MAX_NOTIFICATIONS;
  return next;
}
