import { defaultApprovalActions } from '../push/approvalActions';
import { decideApproval } from '../approvals/actions';

jest.mock('../approvals/actions', () => ({ decideApproval: jest.fn(async () => ({ status: 'Approved' })) }));

const decide = decideApproval as jest.MockedFunction<typeof decideApproval>;

describe('notification actions use the Approvals center decisions', () => {
  it('maps Approve / Deny on Ask proposals and CLI permission requests to decideApproval targets', async () => {
    await defaultApprovalActions.approveAskProposal('ath_t1', 'aap_p1');
    await defaultApprovalActions.rejectAskProposal('ath_t1', 'aap_p1');
    await defaultApprovalActions.allowCliPermissionOnce('cpr_r1');
    await defaultApprovalActions.denyCliPermission('cpr_r1');
    expect(decide.mock.calls).toEqual([
      [{ kind: 'ask_proposal', threadId: 'ath_t1', proposalId: 'aap_p1' }, 'approve'],
      [{ kind: 'ask_proposal', threadId: 'ath_t1', proposalId: 'aap_p1' }, 'deny'],
      [{ kind: 'cli_permission', requestId: 'cpr_r1' }, 'approve'],
      [{ kind: 'cli_permission', requestId: 'cpr_r1' }, 'deny'],
    ]);
  });
});
