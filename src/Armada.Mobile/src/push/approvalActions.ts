import { approveAskProposal, decideCliPermissionRequest, rejectAskProposal } from '@dashboard/api/client';

/**
 * The approve / deny calls a notification action performs. A small interface so the Approvals center (W1, in
 * src/approvals/actions.ts) can supply its own implementation at merge time; until then this default calls the
 * shared dashboard client directly (the same routes the dashboard's Approvals page uses).
 */
export interface ApprovalActions {
  approveAskProposal: (threadId: string, proposalId: string) => Promise<unknown>;
  rejectAskProposal: (threadId: string, proposalId: string) => Promise<unknown>;
  allowCliPermissionOnce: (requestId: string) => Promise<unknown>;
  denyCliPermission: (requestId: string) => Promise<unknown>;
}

export const defaultApprovalActions: ApprovalActions = {
  approveAskProposal: (threadId, proposalId) => approveAskProposal(threadId, proposalId),
  rejectAskProposal: (threadId, proposalId) => rejectAskProposal(threadId, proposalId),
  allowCliPermissionOnce: (requestId) => decideCliPermissionRequest(requestId, { decision: 'AllowOnce' }),
  denyCliPermission: (requestId) => decideCliPermissionRequest(requestId, { decision: 'Deny' }),
};
