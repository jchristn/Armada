import { appPathFromLink, isRootLink } from '../navigation/deepLinks';
import { matchRoute } from '../navigation/routeMatch';
import { isPushCategory, type PushCategory } from './types';

/**
 * Validation of what arrives in a push. A push payload is outside input (anyone holding the Expo token could send
 * one when the Expo project has no access token), so nothing from it is trusted: the link must map to a known app
 * route, ids must look like Armada ids, and only the kinds the server sends are acted on.
 */

/** iOS category id the server sets on actionable pushes; registered with Approve and Deny actions. */
export const APPROVE_DENY_CATEGORY = 'armada_approve_deny';
export const ACTION_APPROVE = 'armada.approve';
export const ACTION_DENY = 'armada.deny';

/** data.kind values the server sends (PushNotificationKinds). */
export const PUSH_KINDS = [
  'ask_proposal',
  'cli_permission',
  'review',
  'deployment_approval',
  'failed',
  'landing_failed',
  'stalled_captain',
  'voyage_finished',
  'test',
] as const;

export type PushKind = (typeof PUSH_KINDS)[number];

/** Kinds that carry Approve / Deny. */
export const ACTIONABLE_KINDS: readonly PushKind[] = ['ask_proposal', 'cli_permission'];

/** App-only screens a link may open besides the dashboard routes (same list as app/+native-intent.tsx). */
export const APP_ONLY_ROUTES = ['/approvals', '/notification-center', '/more', '/preferences', '/profiles'];

export interface PushPayload {
  kind: PushKind;
  /** Validated app path to open, or null when the link was missing or not acceptable. */
  path: string | null;
  entityId: string | null;
  threadId: string | null;
  deviceId: string | null;
  category: PushCategory | 'Test' | null;
}

// Prefix, underscore, then the PrettyId body, which itself may contain '_' and '-' (for example pdv_muyliuwq_Fg2p67toB2W).
const ID_PATTERN = /^[a-z]{2,4}_[A-Za-z0-9_-]{1,96}$/;

function idOrNull(value: unknown, prefix?: string): string | null {
  if (typeof value !== 'string' || !ID_PATTERN.test(value)) return null;
  if (prefix && !value.startsWith(`${prefix}_`)) return null;
  return value;
}

/**
 * Map a link from a push to an app path, or null when it is unsafe, names no known screen, or names no page at all
 * (a test push links to "/": it just opens the app).
 */
export function appPathForPushLink(link: unknown): string | null {
  if (typeof link !== 'string' || link.length > 2048 || /[\u0000-\u001f\u007f]/.test(link) || isRootLink(link)) return null;
  try {
    const mapped = appPathFromLink(link);
    if (!mapped) return null;
    const pathOnly = mapped.split(/[?#]/)[0];
    if (!matchRoute(pathOnly) && !APP_ONLY_ROUTES.includes(pathOnly)) return null;
    return mapped;
  } catch {
    // Never let a malformed link (for example bad percent-encoding) drop the whole push: it just opens the app.
    return null;
  }
}

/** Parse a notification's data object; null when it is not an Armada push. */
export function parsePushData(data: unknown): PushPayload | null {
  if (!data || typeof data !== 'object' || Array.isArray(data)) return null;
  const record = data as Record<string, unknown>;
  const kind = record.kind;
  if (typeof kind !== 'string' || !(PUSH_KINDS as readonly string[]).includes(kind)) return null;
  const category = record.category === 'Test' ? 'Test' : isPushCategory(record.category) ? record.category : null;
  return {
    kind: kind as PushKind,
    path: appPathForPushLink(record.url),
    entityId: idOrNull(record.entityId),
    threadId: idOrNull(record.threadId, 'ath'),
    deviceId: idOrNull(record.deviceId, 'pdv'),
    category,
  };
}

export type PushResponseAction = 'open' | 'approve' | 'deny';

/** Which action a notification response asks for; unknown action identifiers are treated as a plain open. */
export function responseAction(actionIdentifier: string | null | undefined): PushResponseAction {
  if (actionIdentifier === ACTION_APPROVE) return 'approve';
  if (actionIdentifier === ACTION_DENY) return 'deny';
  return 'open';
}

/** True when Approve / Deny can be performed for this payload (the right kind and the ids the routes need). */
export function canAct(payload: PushPayload): boolean {
  if (payload.kind === 'ask_proposal') return !!payload.threadId && !!payload.entityId && payload.entityId.startsWith('aap_');
  if (payload.kind === 'cli_permission') return !!payload.entityId && payload.entityId.startsWith('cpr_');
  return false;
}
