import AsyncStorage from '@react-native-async-storage/async-storage';
import { act, fireEvent, screen, waitFor, within } from '@testing-library/react-native';
import { Slot } from 'expo-router';
import { renderRouter } from 'expo-router/testing-library';
import type { ReactNode } from 'react';
import { AccessibilityInfo, Alert, AppState, Linking, StyleSheet, Text, type AlertButton } from 'react-native';
import * as client from '@dashboard/api/client';
import type { AskActionProposal, AskMessage, AskThread, CliPermissionRequest } from '@dashboard/types/models';
import AskLayout from '../app/(app)/(ask)/_layout';
import AskIndexRoute from '../app/(app)/(ask)/ask/index';
import AskThreadRoute from '../app/(app)/(ask)/ask/[threadId]';
import { ASK_PREF_KEYS } from '../ask/AskContext';
import { AuthProvider } from '../auth/AuthContext';
import { LocaleProvider } from '../i18n/LocaleContext';
import { ApprovalsProvider } from '../notifications/ApprovalsContext';
import { NotificationProvider, useNotifications } from '../notifications/NotificationContext';
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

/** The toasts on screen, as text (the app shows them through ToastHost). */
function ToastProbe() {
  const { toasts } = useNotifications();
  return <>{toasts.map((toast) => <Text key={toast.id} testID={`toast-${toast.severity}`}>{toast.message}</Text>)}</>;
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
              <ApprovalsProvider enabled={false}>{children}</ApprovalsProvider>
              <ToastProbe />
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

  it('the composer stays outside the region the banners share, so a short window (landscape with the keyboard) cannot push it off screen', async () => {
    api.listCaptains.mockResolvedValue({ objects: [] } as never);
    await renderAsk();
    expect(await screen.findByTestId('ask-no-captains')).toBeTruthy();
    const above = screen.getByTestId('ask-above-composer');
    expect(StyleSheet.flatten(above.props.style)).toMatchObject({ flex: 1, minHeight: 0, overflow: 'hidden' });
    expect(within(above).getByTestId('ask-no-captains')).toBeTruthy();
    expect(within(above).queryByTestId('ask-composer')).toBeNull();
    expect(screen.getByTestId('ask-composer')).toBeTruthy();
  });

  it('with captains available, no captain notice is shown once one is selected', async () => {
    await renderAsk();
    await waitFor(() => expect(api.listCaptains).toHaveBeenCalled());
    await waitFor(() => expect(screen.queryByTestId('ask-no-captains')).toBeNull());
    expect(screen.queryByTestId('ask-choose-captain')).toBeNull();
  });

  it('the conversation shows its captain at the top; tapping it opens an inline dropdown of captains under the bar', async () => {
    api.listCaptains.mockResolvedValue({ objects: [{ id: 'cpt_1', name: 'Ada', runtime: 'ClaudeCode', model: 'opus' }, { id: 'cpt_2', name: 'Grace', runtime: 'Codex' }] } as never);
    api.createAskThread.mockResolvedValue(thread({ id: 'thr_new', captainId: 'cpt_2' }));
    api.sendAskMessage.mockResolvedValue({ messageId: 'msg_1', turnId: 'turn_1' });
    await renderAsk();
    await waitFor(() => expect(screen.getByTestId('ask-captain-bar-name')).toHaveTextContent(/Ada/));
    const bar = screen.getByTestId('ask-captain-bar');
    expect(bar.props.accessibilityState).toMatchObject({ expanded: false });
    await act(async () => { fireEvent.press(bar); });

    // A dropdown in the conversation pane, not a sheet: no Modal, anchored to the bar, inside the pane above the composer.
    const menu = screen.getByTestId('ask-captain-menu');
    expect(within(screen.getByTestId('ask-above-composer')).getByTestId('ask-captain-menu')).toBeTruthy();
    expect(screen.queryByTestId('ask-options')).toBeNull();
    expect(screen.queryByTestId('modal-backdrop')).toBeNull();
    expect(menu.props.accessibilityRole).toBe('radiogroup');
    expect(menu.props.accessibilityLabel).toBe('Captain');
    expect(screen.getByTestId('ask-captain-bar').props.accessibilityState).toMatchObject({ expanded: true });
    const ada = screen.getByTestId('ask-captain-menu-option-cpt_1');
    expect(ada.props.accessibilityRole).toBe('radio');
    expect(ada.props.accessibilityState).toMatchObject({ checked: true });
    expect(screen.getByTestId('ask-captain-menu-option-none')).toBeTruthy();
    expect(screen.getByTestId('ask-captain-menu-option-cpt_2').props.accessibilityLabel).toBe('Grace (Codex)');

    // Choosing a captain closes the dropdown; the new conversation is created with it.
    await act(async () => { fireEvent.press(screen.getByTestId('ask-captain-menu-option-cpt_2')); });
    expect(screen.queryByTestId('ask-captain-menu')).toBeNull();
    expect(screen.getByTestId('ask-captain-bar-name')).toHaveTextContent(/Grace/);
    expect(screen.getByTestId('ask-captain-bar').props.accessibilityState).toMatchObject({ expanded: false });
    expect(await AsyncStorage.getItem(ASK_PREF_KEYS.captain)).toBe('"cpt_2"');
    await fireEvent.changeText(screen.getByTestId('ask-input'), 'hi');
    await act(async () => { await fireEvent.press(screen.getByTestId('ask-send')); });
    expect(api.createAskThread).toHaveBeenCalledWith({ captainId: 'cpt_2' });
  });

  it('the captain dropdown changes an existing conversation through the thread update, and closes on a tap outside or the escape gesture', async () => {
    api.listCaptains.mockResolvedValue({ objects: [{ id: 'cpt_1', name: 'Ada' }, { id: 'cpt_2', name: 'Grace' }] } as never);
    api.updateAskThread.mockImplementation(async (_id, patch) => thread(patch as Partial<AskThread>));
    await renderAsk('/ask/thr_1');
    await waitFor(() => expect(screen.getByTestId('ask-captain-bar-name')).toHaveTextContent(/Ada/));

    await act(async () => { fireEvent.press(screen.getByTestId('ask-captain-bar')); });
    await act(async () => { fireEvent.press(screen.getByTestId('ask-captain-menu-scrim')); });
    expect(screen.queryByTestId('ask-captain-menu')).toBeNull();

    await act(async () => { fireEvent.press(screen.getByTestId('ask-captain-bar')); });
    await act(async () => { fireEvent(screen.getByTestId('ask-captain-menu'), 'accessibilityEscape'); });
    expect(screen.queryByTestId('ask-captain-menu')).toBeNull();

    await act(async () => { fireEvent.press(screen.getByTestId('ask-captain-bar')); });
    await act(async () => { fireEvent.press(screen.getByTestId('ask-captain-menu-option-cpt_2')); });
    expect(api.updateAskThread).toHaveBeenCalledWith('thr_1', { captainId: 'cpt_2' });
    await waitFor(() => expect(screen.getByTestId('ask-captain-bar-name')).toHaveTextContent(/Grace/));
  });

  it('screen readers land on the chosen captain when the dropdown opens and back on the bar when it closes', async () => {
    const info = AccessibilityInfo as jest.Mocked<typeof AccessibilityInfo>;
    info.isScreenReaderEnabled.mockResolvedValue(true);
    await renderAsk();
    await waitFor(() => expect(screen.getByTestId('ask-captain-bar-name')).toHaveTextContent(/Ada/));
    const focused = () => info.sendAccessibilityEvent.mock.calls.filter(([, type]) => type === 'focus')
      .map(([node]) => (node as unknown as { props: { testID?: string } }).props.testID);
    await act(async () => { fireEvent.press(screen.getByTestId('ask-captain-bar')); });
    await waitFor(() => expect(focused()).toEqual(['ask-captain-menu-option-cpt_1']));
    await act(async () => { fireEvent.press(screen.getByTestId('ask-captain-bar')); });
    expect(screen.queryByTestId('ask-captain-menu')).toBeNull();
    await waitFor(() => expect(focused()).toEqual(['ask-captain-menu-option-cpt_1', 'ask-captain-bar']));
    info.isScreenReaderEnabled.mockResolvedValue(false);
  });

  it('the captain cannot change during a turn: the bar is disabled and an open dropdown closes', async () => {
    api.getAskThread.mockResolvedValue({ thread: thread(), trackedWork: [] });
    await renderAsk('/ask/thr_1');
    await waitFor(() => expect(screen.getByTestId('ask-captain-bar-name')).toHaveTextContent(/Ada/));
    await act(async () => { fireEvent.press(screen.getByTestId('ask-captain-bar')); });
    expect(screen.getByTestId('ask-captain-menu')).toBeTruthy();
    await emit('ask.turn', { threadId: 'thr_1', turnId: 'turn_5', state: 'started' });
    expect(screen.queryByTestId('ask-captain-menu')).toBeNull();
    expect(screen.getByTestId('ask-captain-bar').props.accessibilityState).toMatchObject({ disabled: true });
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

  it('a captain reply shows its turn duration and, behind an (i), its turn statistics with tool calls and tool time', async () => {
    api.enumerateAskMessages.mockResolvedValue({
      messages: [
        message({}),
        message({
          id: 'msg_2', sequence: 2, role: 'Assistant', contentText: 'All quiet.', captainId: 'cpt_1', durationMs: 6400,
          toolCalls: [
            { callId: 'c1', toolName: 'armada_status', ok: true, resultText: '{}', elapsedMs: 420 },
            { callId: 'c2', toolName: 'armada_enumerate', ok: true, resultText: '[]', elapsedMs: 1830 },
          ],
        }),
        message({ id: 'msg_3', sequence: 3, role: 'Assistant', contentText: 'No timing here.', captainId: 'cpt_1' }),
      ],
      hasMore: false,
    });
    await renderAsk('/ask/thr_1');
    await waitFor(() => expect(screen.getByText('All quiet.')).toBeTruthy());
    // The dashboard's header duration and per-tool times stay where they were.
    expect(screen.getByLabelText('Turn duration 6.4s')).toBeTruthy();
    expect(screen.getByTestId('tool-chip-c2').props.accessibilityLabel).toContain('1.83s');
    expect(screen.queryByTestId('ask-msg-3-stats-toggle')).toBeNull();

    await act(async () => { fireEvent.press(screen.getByTestId('ask-msg-2-stats-toggle')); });
    const panel = screen.getByTestId('ask-msg-2-stats');
    expect(within(panel).getAllByLabelText(/: /).map((cell) => cell.props.accessibilityLabel)).toEqual(['total: 6.40s', 'tool calls: 2', 'tool time: 2.25s']);
    expect(screen.getByTestId('ask-msg-2-stats-toggle').props.accessibilityState).toMatchObject({ expanded: true });
  });

  it('the transcript keeps its newest message in view when its viewport shrinks (the keyboard opening)', async () => {
    const { FlatList } = jest.requireActual<typeof import('react-native')>('react-native');
    const scrollToEnd = jest.spyOn(FlatList.prototype, 'scrollToEnd');
    api.enumerateAskMessages.mockResolvedValue({ messages: [message({}), message({ id: 'msg_2', sequence: 2, role: 'Assistant', contentText: 'Latest reply.' })], hasMore: false });
    await renderAsk('/ask/thr_1');
    await waitFor(() => expect(screen.getByText('Latest reply.')).toBeTruthy());
    scrollToEnd.mockClear();
    await act(async () => { fireEvent(screen.getByTestId('ask-transcript'), 'layout', { nativeEvent: { layout: { x: 0, y: 0, width: 390, height: 240 } } }); });
    expect(scrollToEnd).toHaveBeenCalledWith({ animated: false });
    // Scrolled up to read older messages: a layout change leaves the position alone.
    await act(async () => { fireEvent.scroll(screen.getByTestId('ask-transcript'), { nativeEvent: { contentOffset: { y: 0 }, contentSize: { height: 2000, width: 390 }, layoutMeasurement: { height: 240, width: 390 } } }); });
    scrollToEnd.mockClear();
    await act(async () => { fireEvent(screen.getByTestId('ask-transcript'), 'layout', { nativeEvent: { layout: { x: 0, y: 0, width: 390, height: 500 } } }); });
    expect(scrollToEnd).not.toHaveBeenCalled();
    scrollToEnd.mockRestore();
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
    // The waiting line rotates; its spoken label does not, so TalkBack's live region speaks it once.
    expect(screen.getByTestId('ask-waiting').props.accessibilityLabel).toBe('Thinking...');
    await emit('ask.turn', { threadId: 'thr_1', turnId: 'turn_9', state: 'failed', error: 'runtime not found' });
    await waitFor(() => expect(screen.getByTestId('ask-turn-error')).toBeTruthy());
    expect(screen.getByText('The captain turn failed: runtime not found')).toBeTruthy();
    // VoiceOver has no live regions: the failure is announced, queued behind current speech.
    expect(AccessibilityInfo.announceForAccessibilityWithOptions).toHaveBeenCalledWith('The captain turn failed: runtime not found', { queue: true });
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

describe('Ask Armada conversation list actions', () => {
  const two = () => [thread(), thread({ id: 'thr_2', title: 'Deploy plan', lastMessageUtc: '2026-10-06T12:00:00Z' })];

  async function openList() {
    api.enumerateAskThreads.mockResolvedValue({ objects: two(), totalPages: 1 } as never);
    await renderAsk();
    await act(async () => { await fireEvent.press(screen.getByTestId('ask-open-list')); });
    await waitFor(() => expect(screen.getByTestId('ask-thread-row-thr_2')).toBeTruthy());
  }

  async function longPress(id: string) {
    await act(async () => { await fireEvent(screen.getByTestId(`ask-thread-row-${id}`), 'longPress'); });
    expect(screen.getByTestId('ask-thread-actions')).toBeTruthy();
  }

  async function swipeAction(id: string, action: string) {
    await act(async () => { await fireEvent(screen.getByTestId(`ask-thread-row-${id}`), 'accessibilityAction', { nativeEvent: { actionName: action } }); });
  }

  /** The delete dialog must be inside the phone list's own modal: iOS cannot present it from the screen behind. */
  function expectDialogInsideList() {
    const sheet = screen.getByTestId('ask-list-sheet');
    expect(within(sheet).getByTestId('ask-delete-confirm')).toBeTruthy();
    expect(screen.getAllByTestId('ask-delete-confirm')).toHaveLength(1);
  }

  it('Delete from the row menu confirms inside the list, deletes on the server, and drops the row', async () => {
    api.deleteAskThread.mockResolvedValue(undefined);
    await openList();
    await longPress('thr_2');
    await act(async () => { await fireEvent.press(screen.getByTestId('ask-action-delete')); });
    expect(screen.queryByTestId('ask-thread-actions')).toBeNull();
    expectDialogInsideList();
    expect(api.deleteAskThread).not.toHaveBeenCalled();
    await act(async () => { await fireEvent.press(screen.getByTestId('ask-delete-confirm-confirm')); });
    expect(api.deleteAskThread).toHaveBeenCalledWith('thr_2');
    await waitFor(() => expect(screen.queryByTestId('ask-thread-row-thr_2')).toBeNull());
    expect(screen.getByTestId('ask-thread-row-thr_1')).toBeTruthy();
    expect(screen.getByTestId('toast-success')).toHaveTextContent('Conversation deleted.');
    expect(screen.queryByTestId('ask-delete-confirm')).toBeNull();
  });

  it('the swipe Delete confirms inside the list too; Cancel deletes nothing', async () => {
    api.deleteAskThread.mockResolvedValue(undefined);
    await openList();
    await swipeAction('thr_2', 'delete');
    expectDialogInsideList();
    await act(async () => { await fireEvent.press(screen.getByTestId('ask-delete-confirm-cancel')); });
    expect(api.deleteAskThread).not.toHaveBeenCalled();
    expect(screen.getByTestId('ask-thread-row-thr_2')).toBeTruthy();
    await swipeAction('thr_2', 'delete');
    await act(async () => { await fireEvent.press(screen.getByTestId('ask-delete-confirm-confirm')); });
    expect(api.deleteAskThread).toHaveBeenCalledWith('thr_2');
    await waitFor(() => expect(screen.queryByTestId('ask-thread-row-thr_2')).toBeNull());
  });

  it('a failed delete keeps the row and reports the server error', async () => {
    api.deleteAskThread.mockRejectedValue(new Error('Conversation is locked'));
    await openList();
    await swipeAction('thr_2', 'delete');
    await act(async () => { await fireEvent.press(screen.getByTestId('ask-delete-confirm-confirm')); });
    expect(api.deleteAskThread).toHaveBeenCalledWith('thr_2');
    await waitFor(() => expect(screen.getByTestId('toast-error')).toHaveTextContent('Conversation is locked'));
    expect(screen.getByTestId('ask-thread-row-thr_2')).toBeTruthy();
  });

  it('deleting the open conversation from the list returns to a new conversation', async () => {
    api.deleteAskThread.mockResolvedValue(undefined);
    api.enumerateAskThreads.mockResolvedValue({ objects: two(), totalPages: 1 } as never);
    api.enumerateAskMessages.mockResolvedValue({ messages: [message({ contentText: 'open one' })], hasMore: false });
    await renderAsk('/ask/thr_1');
    await waitFor(() => expect(screen.getByText('open one')).toBeTruthy());
    await act(async () => { await fireEvent.press(screen.getByTestId('ask-open-list')); });
    await swipeAction('thr_1', 'delete');
    await act(async () => { await fireEvent.press(screen.getByTestId('ask-delete-confirm-confirm')); });
    expect(api.deleteAskThread).toHaveBeenCalledWith('thr_1');
    await waitFor(() => expect(screen.getByTestId('ask-empty')).toBeTruthy());
  });

  it('while the phone list is open, every sheet and dialog it opens is presented from inside it (iOS: a screen presenting the list cannot present another modal; that left the composer unable to take focus)', async () => {
    await openList();
    type HostNode = { props: Record<string, unknown>; parent: HostNode | null; children: (HostNode | string)[] };
    /** Visible Modal hosts (they carry the Modal's props) other than the list's own page sheet. */
    const otherModals = (): HostNode[] => {
      const out: HostNode[] = [];
      const walk = (n: HostNode | string) => {
        if (typeof n === 'string') return;
        if (n.props.animationType !== undefined && n.props.presentationStyle !== 'pageSheet') out.push(n);
        n.children.forEach(walk);
      };
      walk(screen.root as unknown as HostNode);
      return out;
    };
    const insideList = (node: HostNode) => {
      for (let p = node.parent; p; p = p.parent) if (p.props.testID === 'ask-list-sheet') return true;
      return false;
    };
    const check = () => {
      const modals = otherModals();
      expect(modals.length).toBeGreaterThan(0);
      for (const modal of modals) expect(insideList(modal)).toBe(true);
    };
    await longPress('thr_2');
    check();
    await act(async () => { await fireEvent.press(screen.getByTestId('ask-action-delete')); });
    check();
    await act(async () => { await fireEvent.press(screen.getByTestId('ask-delete-confirm-cancel')); });
    await swipeAction('thr_1', 'delete');
    check();
    await act(async () => { await fireEvent.press(screen.getByTestId('ask-delete-confirm-cancel')); });
    // Nothing is left open behind the list, and the composer still takes text once the list closes.
    expect(otherModals()).toHaveLength(0);
    await act(async () => { await fireEvent.press(screen.getByTestId('ask-close-list')); });
    expect(otherModals()).toHaveLength(0);
    expect(screen.getByTestId('ask-input').props.editable).not.toBe(false);
  });

  it('Pin, Archive, Rename, and Summarize reach the server with the dashboard payloads and update the list', async () => {
    api.updateAskThread.mockImplementation(async (id, patch) => thread({ id, title: id === 'thr_2' ? 'Deploy plan' : 'Fleet status', ...(patch as Partial<AskThread>) }));
    api.summarizeAskThread.mockResolvedValue(undefined);
    await openList();

    // Pin (swipe): the pinned row moves to the top.
    await swipeAction('thr_2', 'pin');
    expect(api.updateAskThread).toHaveBeenLastCalledWith('thr_2', { pinned: true });
    await waitFor(() => expect(screen.getByTestId('ask-thread-row-thr_2').props.accessibilityLabel).toContain('Pinned'));
    const rows = screen.getAllByTestId(/^ask-thread-row-/).map((r) => r.props.testID);
    expect(rows).toEqual(['ask-thread-row-thr_2', 'ask-thread-row-thr_1']);

    // Rename (row menu, inline field).
    await longPress('thr_2');
    await act(async () => { await fireEvent.press(screen.getByTestId('ask-action-rename')); });
    await fireEvent.changeText(screen.getByTestId('ask-rename-input'), 'Release plan');
    await act(async () => { await fireEvent.press(screen.getByTestId('ask-rename-save')); });
    expect(api.updateAskThread).toHaveBeenLastCalledWith('thr_2', { title: 'Release plan' });
    await waitFor(() => expect(screen.getByText('Release plan')).toBeTruthy());

    // Summarize (row menu).
    await longPress('thr_1');
    await act(async () => { await fireEvent.press(screen.getByTestId('ask-action-summarize')); });
    expect(api.summarizeAskThread).toHaveBeenCalledWith('thr_1');
    expect(screen.getByTestId('toast-info')).toHaveTextContent('Summarizing "Fleet status". The summary will appear in the conversation.');

    // Archive (row menu): hidden while archived conversations are not shown.
    await longPress('thr_1');
    await act(async () => { await fireEvent.press(screen.getByTestId('ask-action-archive')); });
    expect(api.updateAskThread).toHaveBeenLastCalledWith('thr_1', { archived: true });
    await waitFor(() => expect(screen.queryByTestId('ask-thread-row-thr_1')).toBeNull());
  });

  it('a failed update keeps the row as it was and reports the error', async () => {
    api.updateAskThread.mockRejectedValue(new Error('Forbidden'));
    await openList();
    await swipeAction('thr_2', 'archive');
    expect(api.updateAskThread).toHaveBeenCalledWith('thr_2', { archived: true });
    await waitFor(() => expect(screen.getByTestId('toast-error')).toHaveTextContent('Forbidden'));
    expect(screen.getByTestId('ask-thread-row-thr_2')).toBeTruthy();
  });

  it('Show archived reloads the list from the server with archived conversations', async () => {
    await openList();
    await act(async () => { await fireEvent(screen.getByTestId('ask-show-archived'), 'valueChange', true); });
    await waitFor(() => expect(api.enumerateAskThreads).toHaveBeenLastCalledWith(expect.objectContaining({ pageNumber: 1, includeArchived: true })));
  });
});
