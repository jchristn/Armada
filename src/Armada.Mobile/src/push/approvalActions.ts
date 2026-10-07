import { decideApproval } from '../approvals/actions';

/**
 * The approve / deny calls a notification action performs. They go through the Approvals center's decideApproval
 * (src/approvals/actions.ts), so a decision made from a notification behaves exactly like one made in the app. The
 * small interface keeps the push handling testable with a fake.
 */
export interface ApprovalActions {
  approveAskProposal: (threadId: string, proposalId: string) => Promise<unknown>;
  rejectAskProposal: (threadId: string, proposalId: string) => Promise<unknown>;
  allowCliPermissionOnce: (requestId: string) => Promise<unknown>;
  denyCliPermission: (requestId: string) => Promise<unknown>;
}

export const defaultApprovalActions: ApprovalActions = {
  approveAskProposal: (threadId, proposalId) => decideApproval({ kind: 'ask_proposal', threadId, proposalId }, 'approve'),
  rejectAskProposal: (threadId, proposalId) => decideApproval({ kind: 'ask_proposal', threadId, proposalId }, 'deny'),
  allowCliPermissionOnce: (requestId) => decideApproval({ kind: 'cli_permission', requestId }, 'approve'),
  denyCliPermission: (requestId) => decideApproval({ kind: 'cli_permission', requestId }, 'deny'),
};
