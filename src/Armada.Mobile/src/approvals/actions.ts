/**
 * Approve / deny / act on the things that wait on the user, as plain async functions with no React or navigation
 * dependency. Screens (the Approvals center, Ask confirm cards, CLI permission cards) and push notification
 * actions (W5.3) call the same functions, so a decision behaves the same wherever it is made.
 *
 * Public API (keep it small; W5.3 depends on it):
 *
 *   approvalTargetFromInboxItem(item)   InboxItem (GET /api/v1/inbox) -> ApprovalTarget | null
 *   approvalTargetFromPush(data)        push payload { kind, entityId, threadId?, url? } -> ApprovalTarget | null
 *   decideApproval(target, decision, options?)   'approve' | 'deny' -> Promise<ApprovalResult>; throws ApiError
 *   interventionFor(item) / runIntervention(intervention)   the one-tap fix for failed landings, failed
 *                                       missions, and stalled captains
 *
 * Every function calls the shared dashboard API client (@dashboard/api/client), which must already be configured
 * with the active server and token (AuthContext does this on sign-in). Failures throw the client's ApiError
 * (`status` is the HTTP status, `message` the server's text); callers show the message and never branch on it.
 */
import {
  approveAskProposal,
  approveDeployment,
  approveMissionReview,
  decideCliPermissionRequest,
  denyDeployment,
  denyMissionReview,
  rejectAskProposal,
  restartMission,
  retryMissionLanding,
  stopCaptain,
} from '@dashboard/api/client';
import type { AskActionProposal, CliPermissionRequest, CliPermissionRuleScope, InboxItem } from '@dashboard/types/models';
import { INBOX_KINDS } from '@dashboard/lib/inboxKinds';

/** Something that waits on an approve / deny decision. */
export type ApprovalTarget =
  /** An Ask Armada action proposal (InboxItemKinds.AskProposal): approve runs the tool, deny rejects it. */
  | { kind: 'ask_proposal'; threadId: string; proposalId: string }
  /** A CLI captain's permission prompt (InboxItemKinds.CliPermission): approve is "Allow once". */
  | { kind: 'cli_permission'; requestId: string }
  /** A mission awaiting review (InboxItemKinds.Review). */
  | { kind: 'review'; missionId: string }
  /** A deployment awaiting approval (InboxItemKinds.DeploymentApproval). */
  | { kind: 'deployment_approval'; deploymentId: string };

export type ApprovalDecision = 'approve' | 'deny';

export interface ApprovalOptions {
  /**
   * Free text sent with the decision: the review comment (mission reviews), the deployment comment, or the message
   * the captain sees on a denied CLI permission request. Ignored for Ask proposals.
   */
  comment?: string;
  /** Mission reviews only. approve: 'conditional' (needs a comment). deny: 'RetryStage' (more work) or 'FailPipeline' (default). */
  reviewVariant?: 'conditional' | 'RetryStage' | 'FailPipeline';
  /** CLI permission requests only: allow and create an allow rule (pattern and scope) instead of allowing once. */
  remember?: { pattern: string; scope: CliPermissionRuleScope };
}

/** What a decision changed: the target and the status the server reported back (when it returned one). */
export interface ApprovalResult {
  target: ApprovalTarget;
  decision: ApprovalDecision;
  status: string | null;
  /** ask_proposal: the proposal as the server returned it. */
  proposal?: AskActionProposal | null;
  /** cli_permission: the request as the server returned it. */
  cliPermission?: CliPermissionRequest | null;
}

function lastPathSegment(path: string | null | undefined, prefix: string): string | null {
  if (!path) return null;
  const clean = path.split(/[?#]/)[0];
  if (!clean.startsWith(prefix)) return null;
  const rest = clean.slice(prefix.length).split('/')[0];
  if (!rest) return null;
  try { return decodeURIComponent(rest); } catch { return rest; }
}

/** The approval target of an inbox item, or null for kinds that are not decisions (failures, stalls). */
export function approvalTargetFromInboxItem(item: Pick<InboxItem, 'kind' | 'entityId' | 'href' | 'cliPermission'>): ApprovalTarget | null {
  const id = item.entityId ?? null;
  switch (item.kind) {
    case INBOX_KINDS.askProposal: {
      // The inbox links the proposal's conversation: href is /ask/{threadId}.
      const threadId = lastPathSegment(item.href, '/ask/');
      return id && threadId ? { kind: 'ask_proposal', threadId, proposalId: id } : null;
    }
    case INBOX_KINDS.cliPermission: {
      const requestId = item.cliPermission?.id ?? id;
      return requestId ? { kind: 'cli_permission', requestId } : null;
    }
    case INBOX_KINDS.review:
      return id ? { kind: 'review', missionId: id } : null;
    case INBOX_KINDS.deploymentApproval:
      return id ? { kind: 'deployment_approval', deploymentId: id } : null;
    default:
      return null;
  }
}

/** Data of an Armada push notification (Armada.Core PushMessageData, camelCase). */
export interface PushApprovalData {
  kind?: string | null;
  entityId?: string | null;
  threadId?: string | null;
  url?: string | null;
}

/** The approval target a push notification announces, or null when it is not an approvable kind. */
export function approvalTargetFromPush(data: PushApprovalData | null | undefined): ApprovalTarget | null {
  if (!data?.kind || !data.entityId) return null;
  const threadId = data.threadId || lastPathSegment(data.url, '/ask/');
  return approvalTargetFromInboxItem({ kind: data.kind, entityId: data.entityId, href: threadId ? `/ask/${threadId}` : (data.url ?? ''), cliPermission: null });
}

/** Stable key of a target (deduplicates in-flight decisions across screens). */
export function approvalTargetKey(target: ApprovalTarget): string {
  switch (target.kind) {
    case 'ask_proposal': return `ask_proposal:${target.proposalId}`;
    case 'cli_permission': return `cli_permission:${target.requestId}`;
    case 'review': return `review:${target.missionId}`;
    case 'deployment_approval': return `deployment_approval:${target.deploymentId}`;
  }
}

function statusOf(value: unknown): string | null {
  if (value && typeof value === 'object' && 'status' in value) {
    const status = (value as { status?: unknown }).status;
    return typeof status === 'string' ? status : null;
  }
  return null;
}

/**
 * Approve or deny a target through the same endpoints the dashboard uses:
 *   ask_proposal         POST /ask/threads/{t}/proposals/{p}/approve | reject
 *   cli_permission       POST /cli-permissions/requests/{id}/decide  (AllowOnce | AllowAndRemember | Deny)
 *   review               POST /missions/{id}/review/approve | deny
 *   deployment_approval  POST /deployments/{id}/approve | deny
 */
export async function decideApproval(target: ApprovalTarget, decision: ApprovalDecision, options: ApprovalOptions = {}): Promise<ApprovalResult> {
  const approve = decision === 'approve';
  const comment = options.comment?.trim() || undefined;
  let result: unknown;
  switch (target.kind) {
    case 'ask_proposal':
      result = approve
        ? await approveAskProposal(target.threadId, target.proposalId)
        : await rejectAskProposal(target.threadId, target.proposalId);
      break;
    case 'cli_permission':
      if (approve && options.remember) {
        result = await decideCliPermissionRequest(target.requestId, {
          decision: 'AllowAndRemember',
          rulePattern: options.remember.pattern.trim(),
          ruleScope: options.remember.scope,
        });
      } else if (approve) {
        result = await decideCliPermissionRequest(target.requestId, { decision: 'AllowOnce' });
      } else {
        result = await decideCliPermissionRequest(target.requestId, comment ? { decision: 'Deny', message: comment } : { decision: 'Deny' });
      }
      break;
    case 'review':
      if (approve) {
        result = options.reviewVariant === 'conditional'
          ? await approveMissionReview(target.missionId, { comment, conditional: true })
          : await approveMissionReview(target.missionId, { comment });
      } else {
        const action = options.reviewVariant === 'RetryStage' ? 'RetryStage' : 'FailPipeline';
        result = await denyMissionReview(target.missionId, { comment, action });
      }
      break;
    case 'deployment_approval':
      result = approve ? await approveDeployment(target.deploymentId, comment) : await denyDeployment(target.deploymentId, comment);
      break;
  }
  const outcome: ApprovalResult = { target, decision, status: statusOf(result) };
  if (target.kind === 'ask_proposal') outcome.proposal = (result as AskActionProposal | null) ?? null;
  if (target.kind === 'cli_permission') outcome.cliPermission = (result as CliPermissionRequest | null) ?? null;
  return outcome;
}

/** A one-tap fix offered for an intervention item. */
export type Intervention =
  /** landing_failed: retry landing the mission's branch. */
  | { kind: 'retry_landing'; missionId: string }
  /** failed: restart the mission. */
  | { kind: 'restart_mission'; missionId: string }
  /** stalled_captain: stop the captain (the status help's next step; it can then be restarted from its page). */
  | { kind: 'stop_captain'; captainId: string };

/** The fix the Approvals center offers for an intervention item, or null when it only links to the page. */
export function interventionFor(item: Pick<InboxItem, 'kind' | 'entityId'>): Intervention | null {
  const id = item.entityId;
  if (!id) return null;
  switch (item.kind) {
    case INBOX_KINDS.landingFailed: return { kind: 'retry_landing', missionId: id };
    case INBOX_KINDS.failed: return { kind: 'restart_mission', missionId: id };
    case INBOX_KINDS.stalledCaptain: return { kind: 'stop_captain', captainId: id };
    default: return null;
  }
}

/** Run an intervention through the dashboard's endpoints (retry-landing, mission restart, captain stop). */
export async function runIntervention(intervention: Intervention): Promise<void> {
  switch (intervention.kind) {
    case 'retry_landing': await retryMissionLanding(intervention.missionId); return;
    case 'restart_mission': await restartMission(intervention.missionId); return;
    case 'stop_captain': await stopCaptain(intervention.captainId); return;
  }
}
