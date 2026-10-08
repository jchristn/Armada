import AsyncStorage from '@react-native-async-storage/async-storage';
import { act, fireEvent, screen, waitFor } from '@testing-library/react-native';
import { Slot } from 'expo-router';
import { renderRouter } from 'expo-router/testing-library';
import type { ReactNode } from 'react';
import { Alert, AppState, Linking, Text, type AlertButton } from 'react-native';
import * as client from '@dashboard/api/client';
import type { AskActionProposal, AskMessage, AskThread, CliPermissionRequest } from '@dashboard/types/models';
import AskLayout from '../app/(app)/(ask)/_layout';
import AskIndexRoute from '../app/(app)/(ask)/ask/index';
import AskThreadRoute from '../app/(app)/(ask)/ask/[threadId]';
import { ASK_PREF_KEYS } from '../ask/AskContext';
import { AuthProvider } from '../auth/AuthContext';
import { LocaleProvider } from '../i18n/LocaleContext';
import { ApprovalsProvider } from '../notifications/ApprovalsContext';
import { NotificationProvider } from '../notifications/NotificationContext';
import { resetAskSessionForTests } from '../screens/AskScreen';
import { SocketProvider } from '../socket/SocketContext';
import { socketFactory, type FakeSocket } from '../test/fakeSocket';
import { ThemeProvider } from '../theme/ThemeContext';

jest.mock('@dashboard/api/client', () => require('../test/askFixtures').askClientMockFactory());

const api = client as jest.Mocked<typeof client>;
const NOW = '2026-10-07T12:00:00Z';

function thread(over: Partial<AskThread> = {}): AskThread {
  return { id: 'thr_1', title: 'Fleet status', captainId: 'cpt_1', autoApprove: false, pinned: false, archived: false, lastMessageUtc: NOW, unreadCount: 0, ...over };
}

function message(over: Partial<AskMessage>): AskMessage {
  return { id: 'msg_1', threadId: 'thr_1', sequence: 1, role: 'User', kind: 'Text', contentText: 'hello', createdUtc: NOW, ...over };
}

function proposal(over: Partial<AskActionProposal> = {}): AskActionProposal {
  return { id: 'prp_1', threadId: 'thr_1', toolName: 'dispatch', argumentsText: '{"vesselId":"vsl_1"}', summaryText: 'Dispatch 1 mission to api', source: 'Captain', status: 'Pending', ...over };
}

let sockets: FakeSocket[] = [];

function Providers({ children }: { children: ReactNode }) {
  const { sockets: list, factory } = socketFactory();
  sockets = list;
  return (
    <ThemeProvider>
      <AuthProvider>
        <LocaleProvider serverUrl={null} bundledCatalog={() => ({ defaultLocale: 'en', supportedLocales: [], locales: {} })}>
          <SocketProvider serverUrl="http://h:1" token="tok" factory={factory}>
            <NotificationProvider>
              <ApprovalsProvider enabled={false}>{children}</ApprovalsProvider>
            </NotificationProvider>
          </SocketProvider>
        </LocaleProvider>
      </AuthProvider>
    </ThemeProvider>
  );
}

const ROUTES = {
  _layout: () => <Providers><Slot /></Providers>,
  '(ask)/_layout': AskLayout,
  '(ask)/ask/index': AskIndexRoute,
  '(ask)/ask/[threadId]': AskThreadRoute,
  '(work)/missions/[id]': () => <Text>Mission screen</Text>,
};

async function renderAsk(initialUrl = '/ask') {
  const result = renderRouter(ROUTES, { initialUrl });
  await result;
  await act(async () => { sockets[0]?.open(); });
  await act(async () => { await Promise.resolve(); });
  return { getPathname: () => result.getPathname() };
}

async function emit(type: string, data: unknown) {
  await act(async () => { sockets[0].message({ type, data }); });
}

beforeEach(async () => {
  await AsyncStorage.clear();
  jest.clearAllMocks();
  resetAskSessionForTests();
  Object.defineProperty(AppState, 'currentState', { get: () => 'active', configurable: true });
  api.listCaptains.mockResolvedValue({ objects: [{ id: 'cpt_1', name: 'Ada', runtime: 'ClaudeCode', model: 'opus' }] } as never);
  api.getCaptainTools.mockResolvedValue({ captainId: 'cpt_1', runtime: 'ClaudeCode', armadaToolCount: 0, askApprovalGated: true } as never);
  api.getAskQuickActions.mockResolvedValue([]);
  api.enumerateAskThreads.mockResolvedValue({ objects: [], totalPages: 1 } as never);
  api.getAskThread.mockResolvedValue({ thread: thread(), trackedWork: [], pendingProposals: [] });
  api.enumerateAskMessages.mockResolvedValue({ messages: [], hasMore: false });
  api.markAskThreadRead.mockResolvedValue(undefined);
});

describe('Ask Armada', () => {
  it('a new conversation shows the greeting and the quick actions', async () => {
    await renderAsk();
    expect(screen.getByTestId('ask-empty')).toBeTruthy();
    for (const name of ['dispatch', 'fleet-action', 'status', 'health', 'import']) expect(screen.getByTestId(`ask-empty-${name}`)).toBeTruthy();
    expect(screen.getByTestId('ask-send')).toBeDisabled();
  });

  it('a server without captains says so and links to Captains instead of leaving Send silently disabled', async () => {
    api.listCaptains.mockResolvedValue({ objects: [] } as never);
    await renderAsk();
    expect(await screen.findByTestId('ask-no-captains')).toBeTruthy();
    expect(screen.getByText('This server has no captains, so Ask Armada cannot answer yet. Quick actions still work.')).toBeTruthy();
    expect(screen.queryByTestId('ask-choose-captain')).toBeNull();
  });

  it('with captains available, no captain notice is shown once one is selected', async () => {
    await renderAsk();
    await waitFor(() => expect(api.listCaptains).toHaveBeenCalled());
    await waitFor(() => expect(screen.queryByTestId('ask-no-captains')).toBeNull());
    expect(screen.queryByTestId('ask-choose-captain')).toBeNull();
  });

  it('sending creates the conversation, shows the message at once, and streams the reply', async () => {
    api.createAskThread.mockResolvedValue(thread({ id: 'thr_new', title: 'New conversation' }));
    api.sendAskMessage.mockResolvedValue({ messageId: 'msg_10', turnId: 'turn_1' });
    api.getAskThread.mockResolvedValue({ thread: thread({ id: 'thr_new', activeTurnId: 'turn_1' }), trackedWork: [] });
    api.enumerateAskMessages.mockResolvedValue({ messages: [message({ id: 'msg_10', threadId: 'thr_new', contentText: 'What is running?' })], hasMore: false });
    await renderAsk();
    await waitFor(() => expect(api.listCaptains).toHaveBeenCalled());

    await fireEvent.changeText(screen.getByTestId('ask-input'), 'What is running?');
    await act(async () => { await fireEvent.press(screen.getByTestId('ask-send')); });

    expect(api.createAskThread).toHaveBeenCalledWith({ captainId: 'cpt_1' });
    expect(api.sendAskMessage).toHaveBeenCalledWith('thr_new', 'What is running?', false);
    await waitFor(() => expect(screen.getByText('What is running?')).toBeTruthy());
    expect(screen.getByTestId('ask-stop')).toBeTruthy();

    await emit('ask.chunk', { threadId: 'thr_new', turnId: 'turn_1', delta: 'Two **voyages** are ' });
    await emit('ask.tool', { threadId: 'thr_new', turnId: 'turn_1', phase: 'started', id: 'call_1', name: 'status' });
    await emit('ask.chunk', { threadId: 'thr_new', turnId: 'turn_1', delta: 'running.' });
    expect(screen.getByTestId('ask-streaming')).toBeTruthy();
    expect(screen.getByText('voyages')).toBeTruthy();
    expect(screen.getByTestId('tool-chip-call_1')).toBeTruthy();

    api.enumerateAskMessages.mockResolvedValue({
      messages: [
        message({ id: 'msg_10', threadId: 'thr_new', contentText: 'What is running?' }),
        message({ id: 'msg_11', threadId: 'thr_new', sequence: 2, role: 'Assistant', contentText: 'Two voyages are running.', captainId: 'cpt_1' }),
      ],
      hasMore: false,
    });
    await emit('ask.turn', { threadId: 'thr_new', turnId: 'turn_1', state: 'completed', messageId: 'msg_11' });
    await waitFor(() => expect(screen.getByText('Two voyages are running.')).toBeTruthy());
    expect(screen.queryByTestId('ask-streaming')).toBeNull();
    expect(screen.getByTestId('ask-send')).toBeTruthy();
  });

  it('a failed send drops the optimistic message and reports the error', async () => {
    api.createAskThread.mockResolvedValue(thread({ id: 'thr_new' }));
    api.sendAskMessage.mockRejectedValue(new Error('captain offline'));
    await renderAsk();
    await waitFor(() => expect(api.listCaptains).toHaveBeenCalled());
    await fireEvent.changeText(screen.getByTestId('ask-input'), 'hi');
    await act(async () => { await fireEvent.press(screen.getByTestId('ask-send')); });
    await waitFor(() => expect(screen.queryByText('hi')).toBeNull());
  });

  it('a pending proposal shows a confirm card with the exact arguments; Approve runs it', async () => {
    api.getAskThread.mockResolvedValue({ thread: thread(), trackedWork: [], pendingProposals: [proposal()] });
    api.enumerateAskMessages.mockResolvedValue({ messages: [message({ id: 'msg_2', sequence: 2, role: 'Assistant', kind: 'ActionProposal', proposalId: 'prp_1', contentText: '' })], hasMore: false });
    api.approveAskProposal.mockResolvedValue(proposal({ status: 'Executed', executedUtc: NOW, resultText: '{"ok":true}' }));
    await renderAsk('/ask/thr_1');
    await waitFor(() => expect(screen.getByTestId('confirm-card-prp_1')).toBeTruthy());
    expect(screen.getByText('Approval needed')).toBeTruthy();
    expect(screen.getByText('Dispatch 1 mission to api')).toBeTruthy();
    await fireEvent.press(screen.getByTestId('confirm-args-prp_1'));
    expect(screen.getByText(/"vesselId": "vsl_1"/)).toBeTruthy();

    await act(async () => { await fireEvent.press(screen.getByTestId('proposal-approve-prp_1')); });
    expect(api.approveAskProposal).toHaveBeenCalledWith('thr_1', 'prp_1');
    await waitFor(() => expect(screen.queryByTestId('proposal-approve-prp_1')).toBeNull());
  });

  it('a proposal decided elsewhere updates the card from the socket; Reject calls reject', async () => {
    api.getAskThread.mockResolvedValue({ thread: thread(), trackedWork: [], pendingProposals: [proposal({ id: 'prp_2' })] });
    api.enumerateAskMessages.mockResolvedValue({ messages: [message({ id: 'msg_2', sequence: 2, role: 'Assistant', kind: 'ActionProposal', proposalId: 'prp_2' })], hasMore: false });
    api.rejectAskProposal.mockResolvedValue(proposal({ id: 'prp_2', status: 'Rejected' }));
    await renderAsk('/ask/thr_1');
    await waitFor(() => expect(screen.getByTestId('proposal-reject-prp_2')).toBeTruthy());
    await act(async () => { await fireEvent.press(screen.getByTestId('proposal-reject-prp_2')); });
    expect(api.rejectAskProposal).toHaveBeenCalledWith('thr_1', 'prp_2');
    await waitFor(() => expect(screen.getByText('Rejected. Nothing was run.')).toBeTruthy());
  });

  it('a CLI permission request in the conversation can be allowed once', async () => {
    const request: CliPermissionRequest = { id: 'cpr_1', threadId: 'thr_1', messageId: 'msg_3', toolName: 'Bash', summaryText: 'git status', status: 'Pending', canDecide: true, canRemember: true, captainId: 'cpt_1', captainName: 'Ada', expiresUtc: '2099-01-01T00:00:00Z' };
    api.getAskThread.mockResolvedValue({ thread: thread(), trackedWork: [], pendingCliPermissions: [request] });
    api.enumerateAskMessages.mockResolvedValue({ messages: [message({ id: 'msg_3', sequence: 3, role: 'Assistant', kind: 'CliPermission' })], hasMore: false });
    api.decideCliPermissionRequest.mockResolvedValue({ ...request, status: 'Allowed', decisionSource: 'Approver' });
    await renderAsk('/ask/thr_1');
    await waitFor(() => expect(screen.getByTestId('cli-permission-cpr_1')).toBeTruthy());
    expect(screen.getByText('Permission needed')).toBeTruthy();
    expect(screen.getByText('git status')).toBeTruthy();
    await act(async () => { await fireEvent.press(screen.getByTestId('cli-allow-cpr_1')); });
    expect(api.decideCliPermissionRequest).toHaveBeenCalledWith('cpr_1', { decision: 'AllowOnce' });
    await waitFor(() => expect(screen.getByText(/Allowed. The captain ran the tool./)).toBeTruthy());
  });

  it('a CLI permission request can be denied with a message for the captain', async () => {
    const request: CliPermissionRequest = { id: 'cpr_2', threadId: 'thr_1', messageId: 'msg_3', toolName: 'Bash', summaryText: 'rm -rf build', status: 'Pending', canDecide: true };
    api.getAskThread.mockResolvedValue({ thread: thread(), trackedWork: [], pendingCliPermissions: [request] });
    api.enumerateAskMessages.mockResolvedValue({ messages: [message({ id: 'msg_3', sequence: 3, role: 'Assistant', kind: 'CliPermission' })], hasMore: false });
    api.decideCliPermissionRequest.mockResolvedValue({ ...request, status: 'Denied', decisionMessage: 'Use make clean' });
    await renderAsk('/ask/thr_1');
    await waitFor(() => expect(screen.getByTestId('cli-deny-cpr_2')).toBeTruthy());
    await fireEvent.press(screen.getByTestId('cli-deny-cpr_2'));
    await fireEvent.changeText(screen.getByTestId('cli-deny-message'), 'Use make clean');
    await act(async () => { await fireEvent.press(screen.getByTestId('cli-deny-confirm')); });
    expect(api.decideCliPermissionRequest).toHaveBeenCalledWith('cpr_2', { decision: 'Deny', message: 'Use make clean' });
  });

  it('the / menu runs a quick action that needs no input and shows its result', async () => {
    api.getAskQuickActions.mockResolvedValue([{ name: 'status', command: '/status', toolName: 'status', title: 'Status', description: 'Summarize all active work in Armada' }]);
    api.createAskThread.mockResolvedValue(thread({ id: 'thr_q' }));
    api.runAskQuickAction.mockResolvedValue(proposal({ id: 'prp_q', threadId: 'thr_q', toolName: 'status', source: 'QuickAction', status: 'Executed', summaryText: 'Status' }));
    await renderAsk();
    await waitFor(() => expect(api.getAskQuickActions).toHaveBeenCalled());
    api.getAskThread.mockResolvedValue({ thread: thread({ id: 'thr_q' }), trackedWork: [] });
    api.enumerateAskMessages.mockResolvedValue({
      messages: [message({ id: 'msg_r', threadId: 'thr_q', role: 'System', kind: 'ActionResult', proposalId: 'prp_q', contentText: 'No **active** work.' })],
      hasMore: false,
    });
    await fireEvent.changeText(screen.getByTestId('ask-input'), '/st');
    expect(screen.getByTestId('ask-quick-menu')).toBeTruthy();
    expect(screen.queryByTestId('ask-quick-dispatch')).toBeNull();
    expect(screen.getByTestId('ask-send')).toBeDisabled();
    await act(async () => { await fireEvent.press(screen.getByTestId('ask-quick-status')); });
    expect(api.runAskQuickAction).toHaveBeenCalledWith('thr_q', 'status', {});
    await waitFor(() => expect(screen.getByText('Action result')).toBeTruthy());
    expect(screen.getByText('active')).toBeTruthy();
  });

  it('the /dispatch form validates, then submits the shared dispatch arguments', async () => {
    api.listVessels.mockResolvedValue({ objects: [{ id: 'vsl_1', name: 'api' }] } as never);
    api.listPipelines.mockResolvedValue({ objects: [] } as never);
    api.createAskThread.mockResolvedValue(thread({ id: 'thr_d' }));
    api.runAskQuickAction.mockResolvedValue(proposal({ id: 'prp_d', threadId: 'thr_d', source: 'QuickAction', status: 'Executed' }));
    await renderAsk();
    await act(async () => { await fireEvent.press(screen.getByTestId('ask-empty-dispatch')); });
    await waitFor(() => expect(screen.getByTestId('ask-dispatch-form')).toBeTruthy());
    await act(async () => { await fireEvent.press(screen.getByTestId('dispatch-submit')); });
    expect(screen.getByText('Add at least one mission.')).toBeTruthy();
    expect(api.runAskQuickAction).not.toHaveBeenCalled();
    await fireEvent.changeText(screen.getByTestId('dispatch-mission-0-title'), 'Fix the login bug');
    await act(async () => { await fireEvent.press(screen.getByTestId('dispatch-submit')); });
    expect(api.runAskQuickAction).toHaveBeenCalledWith('thr_d', 'dispatch', {
      title: 'Fix the login bug', vesselId: 'vsl_1', missions: [{ title: 'Fix the login bug', description: 'Fix the login bug' }],
    });
  });

  it('the conversation list searches, opens a conversation, and keeps unread badges live', async () => {
    api.enumerateAskThreads.mockResolvedValue({ objects: [thread(), thread({ id: 'thr_2', title: 'Deploy plan', unreadCount: 3 })], totalPages: 1 } as never);
    await renderAsk();
    await act(async () => { await fireEvent.press(screen.getByTestId('ask-open-list')); });
    await waitFor(() => expect(screen.getByTestId('ask-thread-row-thr_2')).toBeTruthy());
    expect(screen.getByText('3')).toBeTruthy();

    await emit('ask.thread', { threadId: 'thr_2', thread: thread({ id: 'thr_2', title: 'Deploy plan', unreadCount: 4 }) });
    expect(screen.getByText('4')).toBeTruthy();

    api.getAskThread.mockResolvedValue({ thread: thread({ id: 'thr_2', title: 'Deploy plan' }), trackedWork: [] });
    api.enumerateAskMessages.mockResolvedValue({ messages: [message({ id: 'm', threadId: 'thr_2', contentText: 'ship it' })], hasMore: false });
    await act(async () => { await fireEvent.press(screen.getByTestId('ask-thread-row-thr_2')); });
    await waitFor(() => expect(screen.getByText('ship it')).toBeTruthy());
    expect(api.markAskThreadRead).toHaveBeenCalledWith('thr_2');
    expect(await AsyncStorage.getItem(ASK_PREF_KEYS.lastThread)).toBe('"thr_2"');
  });

  it('reopens the last conversation once per app session', async () => {
    await AsyncStorage.setItem(ASK_PREF_KEYS.lastThread, '"thr_1"');
    api.enumerateAskMessages.mockResolvedValue({ messages: [message({ contentText: 'from last time' })], hasMore: false });
    await renderAsk();
    await waitFor(() => expect(screen.getByText('from last time')).toBeTruthy());
    expect(api.getAskThread).toHaveBeenCalledWith('thr_1');
  });

  it('deleting the open conversation asks first and returns to a new conversation', async () => {
    api.deleteAskThread.mockResolvedValue(undefined);
    await renderAsk('/ask/thr_1');
    await waitFor(() => expect(api.getAskThread).toHaveBeenCalledWith('thr_1'));
    await act(async () => { await fireEvent.press(screen.getByTestId('ask-open-options')); });
    await act(async () => { await fireEvent.press(screen.getByTestId('ask-options-delete')); });
    expect(screen.getByText('Delete conversation')).toBeTruthy();
    await act(async () => { await fireEvent.press(screen.getByTestId('ask-delete-confirm-confirm')); });
    expect(api.deleteAskThread).toHaveBeenCalledWith('thr_1');
    await waitFor(() => expect(screen.getByTestId('ask-empty')).toBeTruthy());
  });

  it('auto-approve and the captain change through the thread update endpoint', async () => {
    api.updateAskThread.mockImplementation(async (_id, patch) => thread(patch as Partial<AskThread>));
    await renderAsk('/ask/thr_1');
    await waitFor(() => expect(api.getAskThread).toHaveBeenCalled());
    await act(async () => { await fireEvent.press(screen.getByTestId('ask-open-options')); });
    await act(async () => { await fireEvent(screen.getByTestId('ask-auto-approve'), 'valueChange', true); });
    expect(api.updateAskThread).toHaveBeenCalledWith('thr_1', { autoApprove: true });
    await waitFor(() => expect(screen.getByTestId('ask-auto-banner')).toBeTruthy());
  });

  it('live work cards follow ask.work events and link to the work', async () => {
    api.getAskThread.mockResolvedValue({ thread: thread(), trackedWork: [{ id: 'trk_1', threadId: 'thr_1', entityType: 'Voyage', entityId: 'vyg_1', title: 'Login fix', status: 'InProgress', state: 'Active' }] });
    api.getAskWorkSnapshot.mockResolvedValue(null as never);
    api.enumerateAskMessages.mockResolvedValue({ messages: [message({ id: 'msg_r', role: 'System', kind: 'ActionResult', trackedWorkId: 'trk_1', contentText: 'Dispatched.' })], hasMore: false });
    await renderAsk('/ask/thr_1');
    await waitFor(() => expect(screen.getByTestId('work-card-trk_1')).toBeTruthy());
    expect(screen.getByTestId('ask-work-strip')).toBeTruthy();
    await emit('ask.work', {
      threadId: 'thr_1', trackedWorkId: 'trk_1',
      snapshot: { trackedWorkId: 'trk_1', entityType: 'Voyage', entityId: 'vyg_1', title: 'Login fix', status: 'InProgress', state: 'Active', missions: [{ id: 'msn_1', title: 'Fix login', status: 'Complete' }, { id: 'msn_2', title: 'Add test', status: 'Failed' }] },
    });
    expect(screen.getByText(/2 of 2 finished/)).toHaveTextContent(/1 failed/);
    expect(screen.getByText('Fix login')).toBeTruthy();
  });

  it('a failed turn is shown inline', async () => {
    api.getAskThread.mockResolvedValue({ thread: thread({ activeTurnId: 'turn_9' }), trackedWork: [] });
    await renderAsk('/ask/thr_1');
    await waitFor(() => expect(screen.getByTestId('ask-waiting')).toBeTruthy());
    await emit('ask.turn', { threadId: 'thr_1', turnId: 'turn_9', state: 'failed', error: 'runtime not found' });
    await waitFor(() => expect(screen.getByTestId('ask-turn-error')).toBeTruthy());
    expect(screen.getByText('The captain turn failed: runtime not found')).toBeTruthy();
  });

  it('links in captain replies open app pages in the app and web pages outside, after naming the real host', async () => {
    const open = jest.spyOn(Linking, 'openURL').mockResolvedValue(true);
    const alert = jest.spyOn(Alert, 'alert').mockImplementation(() => undefined);
    api.enumerateAskMessages.mockResolvedValue({
      messages: [message({ role: 'Assistant', contentText: 'See [the mission](/missions/msn_5), [github.com/acme/repo/pull/12](https://evil.example/x) and [mail](mailto:a@b.example).' })],
      hasMore: false,
    });
    const app = await renderAsk('/ask/thr_1');
    await waitFor(() => expect(screen.getByText('github.com/acme/repo/pull/12')).toBeTruthy());
    await fireEvent.press(screen.getByText('github.com/acme/repo/pull/12'));
    // The link text claims GitHub; the confirmation names the real destination, and nothing opens before it.
    expect(open).not.toHaveBeenCalled();
    expect(alert).toHaveBeenCalledTimes(1);
    expect(alert.mock.calls[0][0]).toBe('Open evil.example?');
    expect(alert.mock.calls[0][1]).toContain('https://evil.example/x');
    const buttons = alert.mock.calls[0][2] as AlertButton[];
    await act(async () => { buttons.find((b) => b.text === 'Open')!.onPress!(); });
    await waitFor(() => expect(open).toHaveBeenCalledWith('https://evil.example/x'));
    await fireEvent.press(screen.getByText('mail'));
    expect(alert).toHaveBeenCalledTimes(1);
    await act(async () => { await fireEvent.press(screen.getByText('the mission')); });
    expect(app.getPathname()).toBe('/missions/msn_5');
    open.mockRestore();
    alert.mockRestore();
  });
});
