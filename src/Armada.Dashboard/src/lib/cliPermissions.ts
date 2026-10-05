import { useEffect, useState } from 'react';
import { camelizeKeys } from '../api/client';
import type {
  CliPermissionFallbackReason,
  CliPermissionPolicy,
  CliPermissionPolicySource,
  CliPermissionRequest,
  CliPermissionResolution,
  WebSocketMessage,
} from '../types/models';

/** Translator signature shared with `useLocale().t`. */
export type Translate = (text: string, params?: Record<string, string | number | null | undefined>) => string;

/** Every policy, in the order the pickers list them. */
export const CLI_PERMISSION_POLICIES: readonly CliPermissionPolicy[] = ['Refuse', 'ApproveInArmada', 'Bypass'];

/** Server bounds of Permissions.PromptTimeoutSeconds (CliPermissionSettings clamps to these). */
export const PROMPT_TIMEOUT_RANGE = { min: 10, max: 3600 };

/** WebSocket event types of the CLI permission feature. */
export const CLI_PERMISSION_EVENT_PREFIX = 'cli_permission.';

/** Display label of a policy (null or empty means Inherit). */
export function policyLabel(t: Translate, policy: CliPermissionPolicy | string | null | undefined): string {
  switch (policy) {
    case 'Refuse': return t('Refuse');
    case 'ApproveInArmada': return t('Approve in Armada');
    case 'Bypass': return t('Bypass');
    case null:
    case undefined:
    case '': return t('Inherit');
    default: return String(policy);
  }
}

/** Where an effective policy came from, phrased to follow "from". */
export function policySourceLabel(t: Translate, source: CliPermissionPolicySource | string | null | undefined): string {
  switch (source) {
    case 'AskThread': return t('this conversation');
    case 'VesselAutoApprove': return t('the vessel\'s auto-approve setting');
    case 'Captain': return t('the captain');
    case 'CaptainAutoApprove': return t('the captain\'s auto-approve setting');
    case 'ServerDefault': return t('the server default');
    default: return String(source ?? '');
  }
}

/** Why Approve in Armada fell back to Refuse. */
export function fallbackReasonText(t: Translate, reason: CliPermissionFallbackReason | string | null | undefined): string | null {
  switch (reason) {
    case 'RuntimeUnsupported': return t('Approve in Armada fell back to Refuse because this runtime cannot ask Armada for permission.');
    case 'NoSessionToken': return t('Approve in Armada fell back to Refuse because the turn had no Armada session token.');
    case 'RemoteHarbor': return t('Approve in Armada fell back to Refuse because the captain runs on a remote harbor.');
    case null:
    case undefined:
    case '': return null;
    default: return String(reason);
  }
}

/** Display label of a request status. */
export function requestStatusLabel(t: Translate, status: string | null | undefined): string {
  switch (status) {
    case 'Pending': return t('Pending');
    case 'Allowed': return t('Allowed');
    case 'Denied': return t('Denied');
    case 'Expired': return t('Expired');
    case 'Cancelled': return t('Cancelled');
    default: return String(status ?? '');
  }
}

/** How a decided request was decided. */
export function decisionSourceText(t: Translate, request: Pick<CliPermissionRequest, 'decisionSource'>): string | null {
  switch (request.decisionSource) {
    case 'Approver': return t('Decided by an approver.');
    case 'AllowRule': return t('Allowed by a saved rule.');
    case 'DenyRule': return t('Denied by a saved rule.');
    default: return null;
  }
}

/** The strong warning every Bypass selection must confirm. */
export function bypassWarning(t: Translate): string {
  return t('Bypass lets the captain run any command on the Admiral host as the Armada service user without asking. Shell commands, file edits, and network fetches all run immediately with no approval and no rules applied. Only choose Bypass for captains and work you fully trust.');
}

/** Effective policy, source, and (when set) the fallback explanation, as one or two sentences. */
export function resolutionSummary(t: Translate, resolution: CliPermissionResolution | null | undefined): string | null {
  if (!resolution) return null;
  const main = t('Effective: {{policy}} (from {{source}}).', { policy: policyLabel(t, resolution.effective), source: policySourceLabel(t, resolution.source) });
  const fallback = fallbackReasonText(t, resolution.fallbackReason);
  return fallback ? `${main} ${fallback}` : main;
}

/**
 * Explanation for a tool call the CLI refused for lack of permission, built from the thread's typed resolution
 * (never from the tool's result text): a Refuse policy says where it came from and where to change it; Approve in
 * Armada points at the permission card that denied it.
 */
export function permissionDeniedExplanation(t: Translate, resolution: CliPermissionResolution | null | undefined): string {
  if (resolution?.effective === 'ApproveInArmada') return t('Denied in Armada (see the permission card).');
  if (resolution?.effective === 'Refuse') {
    const refused = t('Refused: CLI tools run with policy Refuse (from {{source}}). Change it in the conversation header (CLI tools), on the captain, or in Settings > CLI Tool Permissions.', { source: policySourceLabel(t, resolution.source) });
    const fallback = fallbackReasonText(t, resolution.fallbackReason);
    return fallback ? `${refused} ${fallback}` : refused;
  }
  return t('Refused: the CLI did not have permission to run this tool.');
}

/** True while the request still waits on a decision. */
export function isPendingRequest(request: Pick<CliPermissionRequest, 'status'> | null | undefined): boolean {
  return String(request?.status ?? '').toLowerCase() === 'pending';
}

function timeOf(value: string | null | undefined): number {
  if (!value) return 0;
  const ms = Date.parse(value);
  return Number.isNaN(ms) ? 0 : ms;
}

/** Merge two copies of a request: a decided copy never goes back to Pending, otherwise the newer copy wins. */
export function mergeCliRequest(prev: CliPermissionRequest | undefined, next: CliPermissionRequest): CliPermissionRequest {
  if (!prev) return next;
  const prevPending = isPendingRequest(prev);
  const nextPending = isPendingRequest(next);
  if (!prevPending && nextPending) return { ...next, ...prev };
  if (prevPending && !nextPending) return { ...prev, ...next };
  return timeOf(next.lastUpdateUtc) >= timeOf(prev.lastUpdateUtc) ? { ...prev, ...next } : { ...next, ...prev };
}

/** A parsed cli_permission.requested / cli_permission.resolved event. */
export interface CliPermissionEvent {
  type: 'cli_permission.requested' | 'cli_permission.resolved';
  requestId: string;
  request: CliPermissionRequest;
}

/** Parse a socket message into a CLI permission event, or null when it is not one (or carries no request). */
export function parseCliPermissionEvent(msg: WebSocketMessage | null | undefined): CliPermissionEvent | null {
  if (!msg || (msg.type !== 'cli_permission.requested' && msg.type !== 'cli_permission.resolved')) return null;
  const data = camelizeKeys(msg.data ?? null) as { requestId?: unknown; status?: unknown; request?: unknown } | null;
  if (!data || typeof data !== 'object') return null;
  const request = data.request && typeof data.request === 'object' && !Array.isArray(data.request) ? (data.request as CliPermissionRequest) : null;
  if (!request) return null;
  const requestId = typeof data.requestId === 'string' && data.requestId ? data.requestId : request.id;
  if (!requestId) return null;
  const status = typeof data.status === 'string' && data.status ? data.status : request.status;
  return { type: msg.type, requestId, request: { ...request, id: request.id || requestId, status } };
}

/** Whole seconds until `expiresUtc` (never negative), or null when there is no expiry. */
export function secondsUntil(expiresUtc: string | null | undefined, now: number = Date.now()): number | null {
  if (!expiresUtc) return null;
  const at = Date.parse(expiresUtc);
  if (Number.isNaN(at)) return null;
  return Math.max(0, Math.floor((at - now) / 1000));
}

/** m:ss (or h:mm:ss) for a countdown. */
export function formatCountdown(seconds: number): string {
  const h = Math.floor(seconds / 3600);
  const m = Math.floor((seconds % 3600) / 60);
  const s = seconds % 60;
  const ss = String(s).padStart(2, '0');
  return h > 0 ? `${h}:${String(m).padStart(2, '0')}:${ss}` : `${m}:${ss}`;
}

/** Live seconds remaining until `expiresUtc`, ticking once a second while `active`. */
export function useCountdown(expiresUtc: string | null | undefined, active: boolean): number | null {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    if (!active || !expiresUtc) return undefined;
    setNow(Date.now());
    const id = window.setInterval(() => setNow(Date.now()), 1000);
    return () => window.clearInterval(id);
  }, [active, expiresUtc]);
  return secondsUntil(expiresUtc, now);
}
