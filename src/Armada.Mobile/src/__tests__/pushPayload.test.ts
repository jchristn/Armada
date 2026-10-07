import { ApiError } from '@dashboard/api/client';
import { performPushAction } from '../push/actions';
import type { ApprovalActions } from '../push/approvalActions';
import {
  ACTION_APPROVE,
  ACTION_DENY,
  appPathForPushLink,
  canAct,
  parsePushData,
  responseAction,
  type PushPayload,
} from '../push/payload';

describe('push deep link validation', () => {
  it.each([
    ['/missions/msn_abc123', '/missions/msn_abc123'],
    ['/voyages/vyg_1', '/voyages/vyg_1'],
    ['/captains/cpt_1', '/captains/cpt_1'],
    ['/deployments/dpl_1', '/deployments/dpl_1'],
    ['/ask/ath_1', '/ask/ath_1'],
    ['/cli-permissions?request=cpr_1', '/cli-permissions?request=cpr_1'],
    ['/dashboard/missions/msn_1', '/missions/msn_1'],
    ['armada://missions/msn_1', '/missions/msn_1'],
    ['https://admiral.example/dashboard/missions/msn_1', '/missions/msn_1'],
    ['/approvals', '/approvals'],
  ])('accepts %s', (link, expected) => {
    expect(appPathForPushLink(link)).toBe(expected);
  });

  it.each([
    ['javascript:alert(1)'],
    ['file:///etc/passwd'],
    ['/missions/../admin/users'],
    ['/missions/./x'],
    ['/no-such-page/x'],
    ['/missions\u0000/msn_1'],
    ['/missions/msn_1\n'],
    [''],
    ['/'],
    ['armada://'],
    [42],
    [null],
    [{ url: '/missions/msn_1' }],
    ['/missions/' + 'a'.repeat(3000)],
  ])('rejects %#', (link) => {
    expect(appPathForPushLink(link)).toBeNull();
  });
});

describe('push payload parsing', () => {
  const base = { url: '/ask/ath_t1', kind: 'ask_proposal', entityId: 'aap_p1', category: 'AskProposal', threadId: 'ath_t1', deviceId: 'pdv_d1' };

  it('parses the server data object', () => {
    expect(parsePushData(base)).toEqual({
      kind: 'ask_proposal', path: '/ask/ath_t1', entityId: 'aap_p1', threadId: 'ath_t1', deviceId: 'pdv_d1', category: 'AskProposal',
    });
  });

  it('ignores anything that is not an Armada push', () => {
    expect(parsePushData(null)).toBeNull();
    expect(parsePushData('x')).toBeNull();
    expect(parsePushData([base])).toBeNull();
    expect(parsePushData({ ...base, kind: 'rm_rf' })).toBeNull();
    expect(parsePushData({ url: '/missions/msn_1' })).toBeNull();
  });

  it('drops malformed ids and links instead of trusting them', () => {
    const parsed = parsePushData({ ...base, url: 'javascript:x', entityId: 'aap_1; DROP', threadId: 'msn_wrongprefix', deviceId: '../pdv', category: 'Nope' })!;
    expect(parsed.path).toBeNull();
    expect(parsed.entityId).toBeNull();
    expect(parsed.threadId).toBeNull();
    expect(parsed.deviceId).toBeNull();
    expect(parsed.category).toBeNull();
    expect(canAct(parsed)).toBe(false);
  });

  it('accepts real PrettyId ids, whose body can contain _ and -', () => {
    const parsed = parsePushData({ url: '/ask/ath_k2j3_Ab-9', kind: 'ask_proposal', entityId: 'aap_muyliuwq_Fg2p67toB2W', category: 'AskProposal', threadId: 'ath_k2j3_Ab-9', deviceId: 'pdv_muyliuwq_Fg2p67toB2W' })!;
    expect(parsed.entityId).toBe('aap_muyliuwq_Fg2p67toB2W');
    expect(parsed.threadId).toBe('ath_k2j3_Ab-9');
    expect(parsed.deviceId).toBe('pdv_muyliuwq_Fg2p67toB2W');
    expect(canAct(parsed)).toBe(true);
  });

  it('a test push links to the root: it just opens the app', () => {
    expect(parsePushData({ url: '/', kind: 'test', entityId: 'pdv_1', category: 'Test', deviceId: 'pdv_1' })).toMatchObject({ kind: 'test', path: null, category: 'Test' });
  });

  it('maps action identifiers', () => {
    expect(responseAction(ACTION_APPROVE)).toBe('approve');
    expect(responseAction(ACTION_DENY)).toBe('deny');
    expect(responseAction('expo.modules.notifications.actions.DEFAULT')).toBe('open');
    expect(responseAction('something.else')).toBe('open');
    expect(responseAction(null)).toBe('open');
  });

  it('only Ask proposals (with their thread) and CLI permission requests are actionable', () => {
    expect(canAct(parsePushData(base)!)).toBe(true);
    expect(canAct(parsePushData({ ...base, threadId: undefined })!)).toBe(false);
    expect(canAct(parsePushData({ url: '/cli-permissions?request=cpr_1', kind: 'cli_permission', entityId: 'cpr_1', category: 'CliPermission' })!)).toBe(true);
    expect(canAct(parsePushData({ url: '/cli-permissions?request=cpr_1', kind: 'cli_permission', entityId: 'msn_1', category: 'CliPermission' })!)).toBe(false);
    expect(canAct(parsePushData({ url: '/missions/msn_1', kind: 'failed', entityId: 'msn_1', category: 'MissionFailed' })!)).toBe(false);
  });
});

describe('Approve / Deny from a notification', () => {
  function fakeActions(): jest.Mocked<ApprovalActions> {
    return {
      approveAskProposal: jest.fn(async (_threadId: string, _proposalId: string): Promise<unknown> => ({})),
      rejectAskProposal: jest.fn(async (_threadId: string, _proposalId: string): Promise<unknown> => ({})),
      allowCliPermissionOnce: jest.fn(async (_requestId: string): Promise<unknown> => ({})),
      denyCliPermission: jest.fn(async (_requestId: string): Promise<unknown> => ({})),
    };
  }
  const proposal = parsePushData({ url: '/ask/ath_t1', kind: 'ask_proposal', entityId: 'aap_p1', category: 'AskProposal', threadId: 'ath_t1' }) as PushPayload;
  const permission = parsePushData({ url: '/cli-permissions?request=cpr_r1', kind: 'cli_permission', entityId: 'cpr_r1', category: 'CliPermission' }) as PushPayload;

  it('approves and rejects Ask proposals through the thread routes', async () => {
    const actions = fakeActions();
    expect(await performPushAction(proposal, 'approve', actions)).toBe('approved');
    expect(actions.approveAskProposal).toHaveBeenCalledWith('ath_t1', 'aap_p1');
    expect(await performPushAction(proposal, 'deny', actions)).toBe('denied');
    expect(actions.rejectAskProposal).toHaveBeenCalledWith('ath_t1', 'aap_p1');
  });

  it('allows once or denies CLI permission requests', async () => {
    const actions = fakeActions();
    expect(await performPushAction(permission, 'approve', actions)).toBe('approved');
    expect(actions.allowCliPermissionOnce).toHaveBeenCalledWith('cpr_r1');
    expect(await performPushAction(permission, 'deny', actions)).toBe('denied');
    expect(actions.denyCliPermission).toHaveBeenCalledWith('cpr_r1');
  });

  it('verification comes first and a failed one sends nothing', async () => {
    const actions = fakeActions();
    const order: string[] = [];
    actions.approveAskProposal.mockImplementation(async () => { order.push('approve'); return {}; });
    expect(await performPushAction(proposal, 'approve', actions, { verify: async () => { order.push('verify'); return true; } })).toBe('approved');
    expect(order).toEqual(['verify', 'approve']);
    expect(await performPushAction(proposal, 'approve', actions, { verify: async () => false })).toBe('verificationFailed');
    expect(await performPushAction(proposal, 'deny', actions, { verify: async () => { throw new Error('no hardware'); } })).toBe('verificationFailed');
    expect(actions.approveAskProposal).toHaveBeenCalledTimes(1);
    expect(actions.rejectAskProposal).not.toHaveBeenCalled();
  });

  it('maps 409 to already decided, 404/403 to not allowed, anything else to failed', async () => {
    const actions = fakeActions();
    actions.allowCliPermissionOnce.mockRejectedValueOnce(new ApiError('Already decided', 409, null));
    expect(await performPushAction(permission, 'approve', actions)).toBe('alreadyDecided');
    actions.allowCliPermissionOnce.mockRejectedValueOnce(new ApiError('Forbidden', 403, null));
    expect(await performPushAction(permission, 'approve', actions)).toBe('notAllowed');
    actions.allowCliPermissionOnce.mockRejectedValueOnce(new TypeError('Network request failed'));
    expect(await performPushAction(permission, 'approve', actions)).toBe('failed');
  });

  it('refuses kinds that are not actionable', async () => {
    const actions = fakeActions();
    const failed = parsePushData({ url: '/missions/msn_1', kind: 'failed', entityId: 'msn_1', category: 'MissionFailed' }) as PushPayload;
    expect(await performPushAction(failed, 'approve', actions)).toBe('notActionable');
    expect(Object.values(actions).every((fn) => (fn as jest.Mock).mock.calls.length === 0)).toBe(true);
  });
});
