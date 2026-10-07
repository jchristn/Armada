import { ApiError } from '@dashboard/api/client';
import type { ApprovalActions } from './approvalActions';
import { canAct, type PushPayload } from './payload';

/** Outcome of an Approve / Deny chosen on a notification. */
export type PushActionOutcome =
  | 'approved'
  | 'denied'
  /** 409: someone already decided it, or it expired. */
  | 'alreadyDecided'
  /** 404 / 403: gone, or this user may not decide it. */
  | 'notAllowed'
  /** The payload is not an actionable kind or lacks the ids. */
  | 'notActionable'
  /** The user did not pass the device or biometric check. */
  | 'verificationFailed'
  | 'failed';

export interface PerformActionOptions {
  /** Biometric / device-passcode check; run first when the profile asks for it. Resolves true when verified. */
  verify?: (() => Promise<boolean>) | null;
}

/** Perform Approve or Deny for a validated payload. Never throws. */
export async function performPushAction(
  payload: PushPayload,
  action: 'approve' | 'deny',
  actions: ApprovalActions,
  options: PerformActionOptions = {},
): Promise<PushActionOutcome> {
  if (!canAct(payload)) return 'notActionable';
  if (options.verify) {
    let verified = false;
    try { verified = await options.verify(); } catch { verified = false; }
    if (!verified) return 'verificationFailed';
  }
  try {
    if (payload.kind === 'ask_proposal') {
      const threadId = payload.threadId as string;
      const proposalId = payload.entityId as string;
      if (action === 'approve') await actions.approveAskProposal(threadId, proposalId);
      else await actions.rejectAskProposal(threadId, proposalId);
    } else {
      const requestId = payload.entityId as string;
      if (action === 'approve') await actions.allowCliPermissionOnce(requestId);
      else await actions.denyCliPermission(requestId);
    }
    return action === 'approve' ? 'approved' : 'denied';
  } catch (err) {
    if (err instanceof ApiError) {
      if (err.status === 409) return 'alreadyDecided';
      if (err.status === 404 || err.status === 403) return 'notAllowed';
    }
    return 'failed';
  }
}
