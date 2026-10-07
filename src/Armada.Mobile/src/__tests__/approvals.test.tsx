import AsyncStorage from '@react-native-async-storage/async-storage';
import { act, fireEvent, screen, waitFor } from '@testing-library/react-native';
import { Slot } from 'expo-router';
import { renderRouter } from 'expo-router/testing-library';
import type { ReactNode } from 'react';
import { AppState, Text } from 'react-native';
import * as client from '@dashboard/api/client';
import type { InboxItem } from '@dashboard/types/models';
import ApprovalsLayout from '../app/(app)/(approvals)/_layout';
import ApprovalsRoute from '../app/(app)/(approvals)/approvals';
import InboxRoute from '../app/(app)/(approvals)/inbox';
import {
  approvalTargetFromInboxItem,
  approvalTargetFromPush,
  approvalTargetKey,
  decideApproval,
  interventionFor,
  runIntervention,
} from '../approvals/actions';
import { newApprovalItems, approvalKey } from '../approvals/approvalToasts';
import { setOpenAskThread } from '../ask/openThread';
import { AuthProvider } from '../auth/AuthContext';
import { LocaleProvider } from '../i18n/LocaleContext';
import { ApprovalsProvider } from '../notifications/ApprovalsContext';
import { NotificationProvider, useNotifications, type NotificationState } from '../notifications/NotificationContext';
import { SocketProvider } from '../socket/SocketContext';
import { socketFactory, type FakeSocket } from '../test/fakeSocket';
import { ThemeProvider } from '../theme/ThemeContext';

jest.mock('@dashboard/api/client', () => require('../test/askFixtures').askClientMockFactory());

const api = client as jest.Mocked<typeof client>;

function item(over: Partial<InboxItem>): InboxItem {
  return { kind: 'failed', severity: 'Warning', title: 'Failed: Fix login', detail: 'boom', entityType: 'mission', entityId: 'msn_1', href: '/missions/msn_1', ...over };
}

const ASK_ITEM = item({ kind: 'ask_proposal', title: 'Ask approval: Dispatch 1 mission', detail: 'Armada proposed an action', entityType: 'ask_proposal', entityId: 'prp_1', href: '/ask/thr_1' });
const CLI_ITEM = item({
  kind: 'cli_permission', title: 'CLI permission: Bash git push', entityType: 'cli_permission_request', entityId: 'cpr_1', href: '/cli-permissions?request=cpr_1',
  cliPermission: { id: 'cpr_1', toolName: 'Bash', summaryText: 'git push', status: 'Pending', canDecide: true, threadId: 'thr_9', threadTitle: 'Release' },
});
const REVIEW_ITEM = item({ kind: 'review', title: 'Review: Fix login', entityType: 'mission', entityId: 'msn_7', entityName: 'Fix login', href: '/missions/msn_7' });
const DEPLOY_ITEM = item({ kind: 'deployment_approval', title: 'Deploy', entityType: 'deployment', entityId: 'dpl_1', environmentName: 'production', deploymentTitle: 'Release 2.3', href: '/deployments/dpl_1' });
const LANDING_ITEM = item({ kind: 'landing_failed', severity: 'Critical', title: 'Landing failed: Fix login', entityId: 'msn_2', href: '/missions/msn_2' });
const STALLED_ITEM = item({ kind: 'stalled_captain', title: 'Stalled captain: Ada', entityType: 'captain', entityId: 'cpt_1', href: '/captains/cpt_1' });

describe('approval actions (shared with push actions)', () => {
  beforeEach(() => jest.clearAllMocks());

  it('maps inbox items to approval targets', () => {
    expect(approvalTargetFromInboxItem(ASK_ITEM)).toEqual({ kind: 'ask_proposal', threadId: 'thr_1', proposalId: 'prp_1' });
    expect(approvalTargetFromInboxItem(CLI_ITEM)).toEqual({ kind: 'cli_permission', requestId: 'cpr_1' });
    expect(approvalTargetFromInboxItem(REVIEW_ITEM)).toEqual({ kind: 'review', missionId: 'msn_7' });
    expect(approvalTargetFromInboxItem(DEPLOY_ITEM)).toEqual({ kind: 'deployment_approval', deploymentId: 'dpl_1' });
    expect(approvalTargetFromInboxItem(LANDING_ITEM)).toBeNull();
    expect(approvalTargetFromInboxItem({ ...ASK_ITEM, href: '/inbox' })).toBeNull();
  });

  it('maps push payloads (PushMessageData) to approval targets', () => {
    expect(approvalTargetFromPush({ kind: 'ask_proposal', entityId: 'prp_1', threadId: 'thr_1', url: '/ask/thr_1' })).toEqual({ kind: 'ask_proposal', threadId: 'thr_1', proposalId: 'prp_1' });
    expect(approvalTargetFromPush({ kind: 'ask_proposal', entityId: 'prp_1', url: '/ask/thr%201' })).toEqual({ kind: 'ask_proposal', threadId: 'thr 1', proposalId: 'prp_1' });
    expect(approvalTargetFromPush({ kind: 'cli_permission', entityId: 'cpr_1', url: '/cli-permissions?request=cpr_1' })).toEqual({ kind: 'cli_permission', requestId: 'cpr_1' });
    expect(approvalTargetFromPush({ kind: 'review', entityId: 'msn_1' })).toEqual({ kind: 'review', missionId: 'msn_1' });
    expect(approvalTargetFromPush({ kind: 'deployment_approval', entityId: 'dpl_1' })).toEqual({ kind: 'deployment_approval', deploymentId: 'dpl_1' });
    expect(approvalTargetFromPush({ kind: 'voyage_finished', entityId: 'vyg_1' })).toBeNull();
    expect(approvalTargetFromPush({ kind: 'ask_proposal', entityId: 'prp_1' })).toBeNull();
    expect(approvalTargetFromPush(null)).toBeNull();
  });

  it('decides through the dashboard endpoints', async () => {
    api.approveAskProposal.mockResolvedValue({ id: 'prp_1', status: 'Executed' } as never);
    const result = await decideApproval({ kind: 'ask_proposal', threadId: 'thr_1', proposalId: 'prp_1' }, 'approve');
    expect(api.approveAskProposal).toHaveBeenCalledWith('thr_1', 'prp_1');
    expect(result).toMatchObject({ decision: 'approve', status: 'Executed', proposal: { id: 'prp_1' } });

    await decideApproval({ kind: 'ask_proposal', threadId: 'thr_1', proposalId: 'prp_1' }, 'deny');
    expect(api.rejectAskProposal).toHaveBeenCalledWith('thr_1', 'prp_1');

    await decideApproval({ kind: 'cli_permission', requestId: 'cpr_1' }, 'approve');
    expect(api.decideCliPermissionRequest).toHaveBeenLastCalledWith('cpr_1', { decision: 'AllowOnce' });
    await decideApproval({ kind: 'cli_permission', requestId: 'cpr_1' }, 'approve', { remember: { pattern: ' Bash(git push:*) ', scope: 'Vessel' } });
    expect(api.decideCliPermissionRequest).toHaveBeenLastCalledWith('cpr_1', { decision: 'AllowAndRemember', rulePattern: 'Bash(git push:*)', ruleScope: 'Vessel' });
    await decideApproval({ kind: 'cli_permission', requestId: 'cpr_1' }, 'deny', { comment: '  no  ' });
    expect(api.decideCliPermissionRequest).toHaveBeenLastCalledWith('cpr_1', { decision: 'Deny', message: 'no' });
    await decideApproval({ kind: 'cli_permission', requestId: 'cpr_1' }, 'deny');
    expect(api.decideCliPermissionRequest).toHaveBeenLastCalledWith('cpr_1', { decision: 'Deny' });

    await decideApproval({ kind: 'review', missionId: 'msn_7' }, 'approve');
    expect(api.approveMissionReview).toHaveBeenLastCalledWith('msn_7', { comment: undefined });
    await decideApproval({ kind: 'review', missionId: 'msn_7' }, 'approve', { comment: 'tighten tests', reviewVariant: 'conditional' });
    expect(api.approveMissionReview).toHaveBeenLastCalledWith('msn_7', { comment: 'tighten tests', conditional: true });
    await decideApproval({ kind: 'review', missionId: 'msn_7' }, 'deny', { comment: 'redo', reviewVariant: 'RetryStage' });
    expect(api.denyMissionReview).toHaveBeenLastCalledWith('msn_7', { comment: 'redo', action: 'RetryStage' });
    await decideApproval({ kind: 'review', missionId: 'msn_7' }, 'deny');
    expect(api.denyMissionReview).toHaveBeenLastCalledWith('msn_7', { comment: undefined, action: 'FailPipeline' });

    await decideApproval({ kind: 'deployment_approval', deploymentId: 'dpl_1' }, 'approve');
    expect(api.approveDeployment).toHaveBeenLastCalledWith('dpl_1', undefined);
    await decideApproval({ kind: 'deployment_approval', deploymentId: 'dpl_1' }, 'deny', { comment: 'freeze' });
    expect(api.denyDeployment).toHaveBeenLastCalledWith('dpl_1', 'freeze');
  });

  it('propagates server errors to the caller', async () => {
    api.approveDeployment.mockRejectedValue(new client.ApiError('Deployment is not pending approval.', 409, null));
    await expect(decideApproval({ kind: 'deployment_approval', deploymentId: 'dpl_1' }, 'approve')).rejects.toMatchObject({ status: 409 });
  });

  it('offers and runs the intervention for failures and stalls', async () => {
    expect(interventionFor(LANDING_ITEM)).toEqual({ kind: 'retry_landing', missionId: 'msn_2' });
    expect(interventionFor(item({}))).toEqual({ kind: 'restart_mission', missionId: 'msn_1' });
    expect(interventionFor(STALLED_ITEM)).toEqual({ kind: 'stop_captain', captainId: 'cpt_1' });
    expect(interventionFor(item({ kind: 'merge_failed' }))).toBeNull();
    await runIntervention({ kind: 'retry_landing', missionId: 'msn_2' });
    expect(api.retryMissionLanding).toHaveBeenCalledWith('msn_2');
    await runIntervention({ kind: 'restart_mission', missionId: 'msn_1' });
    expect(api.restartMission).toHaveBeenCalledWith('msn_1');
    await runIntervention({ kind: 'stop_captain', captainId: 'cpt_1' });
    expect(api.stopCaptain).toHaveBeenCalledWith('cpt_1');
  });

  it('keys targets stably', () => {
    expect(approvalTargetKey({ kind: 'review', missionId: 'msn_1' })).toBe('review:msn_1');
  });
});

describe('approval toasts', () => {
  it('the first load raises none; later only new approval items raise one, except the open conversation', () => {
    expect(newApprovalItems(null, [REVIEW_ITEM], null)).toEqual([]);
    const seen = new Set([approvalKey(REVIEW_ITEM)]);
    expect(newApprovalItems(seen, [REVIEW_ITEM, DEPLOY_ITEM, LANDING_ITEM], null)).toEqual([DEPLOY_ITEM]);
    expect(newApprovalItems(seen, [ASK_ITEM], 'thr_1')).toEqual([]);
    expect(newApprovalItems(seen, [ASK_ITEM], 'thr_2')).toEqual([ASK_ITEM]);
    expect(newApprovalItems(seen, [CLI_ITEM], 'thr_9')).toEqual([]);
  });
});

let sockets: FakeSocket[] = [];
let notifications: NotificationState | null = null;
function Probe() {
  notifications = useNotifications();
  return null;
}

function Providers({ children }: { children: ReactNode }) {
  const { sockets: list, factory } = socketFactory();
  sockets = list;
  return (
    <ThemeProvider>
      <AuthProvider>
        <LocaleProvider serverUrl={null} bundledCatalog={() => ({ defaultLocale: 'en', supportedLocales: [], locales: {} })}>
          <SocketProvider serverUrl="http://h:1" token="tok" factory={factory}>
            <NotificationProvider>
              <ApprovalsProvider enabled><Probe />{children}</ApprovalsProvider>
            </NotificationProvider>
          </SocketProvider>
        </LocaleProvider>
      </AuthProvider>
    </ThemeProvider>
  );
}

const ROUTES = {
  _layout: () => <Providers><Slot /></Providers>,
  '(approvals)/_layout': ApprovalsLayout,
  '(approvals)/approvals': ApprovalsRoute,
  '(approvals)/inbox': InboxRoute,
  '(ask)/ask/[threadId]': () => <Text>Ask screen</Text>,
  '(work)/missions/[id]': () => <Text>Mission screen</Text>,
};

async function renderApprovals(url = '/approvals') {
  const result = renderRouter(ROUTES, { initialUrl: url });
  await result;
  await act(async () => { sockets[0]?.open(); });
  await act(async () => { await Promise.resolve(); });
  return { getPathname: () => result.getPathname() };
}

describe('approvals center', () => {
  beforeEach(async () => {
    await AsyncStorage.clear();
    jest.clearAllMocks();
    setOpenAskThread(null);
    Object.defineProperty(AppState, 'currentState', { get: () => 'active', configurable: true });
  });

  it('is all caught up when nothing waits', async () => {
    api.getInbox.mockResolvedValue([]);
    await renderApprovals();
    await waitFor(() => expect(screen.getByText('You are all caught up.')).toBeTruthy());
  });

  it('groups decisions and interventions with counts, and /inbox shows the same center', async () => {
    api.getInbox.mockResolvedValue([LANDING_ITEM, REVIEW_ITEM, DEPLOY_ITEM, STALLED_ITEM]);
    await renderApprovals('/inbox');
    await waitFor(() => expect(screen.getByText('Waiting for your approval (2)')).toBeTruthy());
    expect(screen.getByText('Needs intervention (2)')).toBeTruthy();
    expect(screen.getByLabelText('Total: 4')).toBeTruthy();
    expect(screen.getByLabelText('Critical: 1')).toBeTruthy();
    expect(screen.getByText('Deploy to production: Release 2.3')).toBeTruthy();
  });

  it('an Ask proposal shows its exact arguments and can be approved in place', async () => {
    api.getInbox.mockResolvedValue([ASK_ITEM]);
    api.getAskThread.mockResolvedValue({ thread: { id: 'thr_1' } as never, pendingProposals: [{ id: 'prp_1', threadId: 'thr_1', toolName: 'dispatch', argumentsText: '{"vesselId":"vsl_1"}', source: 'Captain', status: 'Pending' }] });
    api.approveAskProposal.mockResolvedValue({ id: 'prp_1', threadId: 'thr_1', toolName: 'dispatch', source: 'Captain', status: 'Executed' });
    await renderApprovals();
    await waitFor(() => expect(screen.getByTestId('proposal-approve-prp_1')).toBeTruthy());
    expect(screen.getByText(/"vesselId": "vsl_1"/)).toBeTruthy();
    api.getInbox.mockResolvedValue([]);
    await act(async () => { await fireEvent.press(screen.getByTestId('proposal-approve-prp_1')); });
    expect(api.approveAskProposal).toHaveBeenCalledWith('thr_1', 'prp_1');
    await waitFor(() => expect(screen.getByText('You are all caught up.')).toBeTruthy());
  });

  it('a deployment is approved only after the confirmation', async () => {
    api.getInbox.mockResolvedValue([DEPLOY_ITEM]);
    api.approveDeployment.mockResolvedValue({ id: 'dpl_1', status: 'Running' } as never);
    await renderApprovals();
    await waitFor(() => expect(screen.getByTestId('deployment-approve-dpl_1')).toBeTruthy());
    await act(async () => { await fireEvent.press(screen.getByTestId('deployment-approve-dpl_1')); });
    expect(screen.getByText('Approve and execute "Deploy to production: Release 2.3"?')).toBeTruthy();
    expect(api.approveDeployment).not.toHaveBeenCalled();
    await act(async () => { await fireEvent.press(screen.getByTestId('deployment-confirm-confirm')); });
    expect(api.approveDeployment).toHaveBeenCalledWith('dpl_1', undefined);
    expect(notifications!.toasts.map((x) => x.message)).toContain('Approved "Deploy to production: Release 2.3".');
  });

  it('a mission review needs feedback for the conditional verdicts', async () => {
    api.getInbox.mockResolvedValue([REVIEW_ITEM]);
    await renderApprovals();
    await waitFor(() => expect(screen.getByTestId('review-open-msn_7')).toBeTruthy());
    await act(async () => { await fireEvent.press(screen.getByTestId('review-open-msn_7')); });
    expect(screen.getByTestId('review-conditional')).toBeDisabled();
    expect(screen.getByTestId('review-more-work')).toBeDisabled();
    await act(async () => { await fireEvent.changeText(screen.getByTestId('review-comment'), 'Add a test'); });
    expect(screen.getByTestId('review-more-work')).toBeEnabled();
    await act(async () => { await fireEvent.press(screen.getByTestId('review-more-work')); });
    expect(api.denyMissionReview).toHaveBeenCalledWith('msn_7', { comment: 'Add a test', action: 'RetryStage' });
  });

  it('a CLI permission request is allowed from the center', async () => {
    api.getInbox.mockResolvedValue([CLI_ITEM]);
    api.decideCliPermissionRequest.mockResolvedValue({ ...CLI_ITEM.cliPermission!, status: 'Allowed' });
    await renderApprovals();
    await waitFor(() => expect(screen.getByTestId('cli-allow-cpr_1')).toBeTruthy());
    expect(screen.getByText('Release')).toBeTruthy();
    await act(async () => { await fireEvent.press(screen.getByTestId('cli-allow-cpr_1')); });
    expect(api.decideCliPermissionRequest).toHaveBeenCalledWith('cpr_1', { decision: 'AllowOnce' });
  });

  it('a failed landing is retried after the confirmation', async () => {
    api.getInbox.mockResolvedValue([LANDING_ITEM]);
    await renderApprovals();
    await waitFor(() => expect(screen.getByTestId('intervention-run-msn_2')).toBeTruthy());
    await act(async () => { await fireEvent.press(screen.getByTestId('intervention-run-msn_2')); });
    await act(async () => { await fireEvent.press(screen.getByTestId('intervention-confirm-confirm')); });
    expect(api.retryMissionLanding).toHaveBeenCalledWith('msn_2');
  });

  it('a new approval arriving later raises a toast that opens the center', async () => {
    api.getInbox.mockResolvedValue([]);
    await renderApprovals();
    await waitFor(() => expect(api.getInbox).toHaveBeenCalled());
    api.getInbox.mockResolvedValue([REVIEW_ITEM]);
    await act(async () => { sockets[0].message({ type: 'cli_permission.requested', data: {} }); });
    await waitFor(() => expect(notifications!.toasts.map((x) => x.message)).toContain('Approval needed: Review: Fix login'));
    expect(notifications!.toasts.find((x) => x.message === 'Approval needed: Review: Fix login')?.href).toBe('/approvals');
  });
});
