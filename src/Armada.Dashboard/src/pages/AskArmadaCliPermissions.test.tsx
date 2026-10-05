import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import AskArmada from './AskArmada';
import {
  decideCliPermissionRequest,
  enumerateAskMessages,
  enumerateAskThreads,
  getAskThread,
  markAskThreadRead,
  setAskThreadCliPermissionPolicy,
} from '../api/client';
import { translateTemplate } from '../i18n/runtime';
import type { AskMessage, AskThread, AskThreadDetail, CliPermissionRequest, EnumerationResult, WebSocketMessage } from '../types/models';

vi.mock('../api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../api/client')>();
  return {
    ...actual,
    enumerateAskThreads: vi.fn(),
    createAskThread: vi.fn(),
    getAskThread: vi.fn(),
    updateAskThread: vi.fn(),
    deleteAskThread: vi.fn(),
    enumerateAskMessages: vi.fn(),
    sendAskMessage: vi.fn(),
    cancelAskTurn: vi.fn(),
    summarizeAskThread: vi.fn(),
    markAskThreadRead: vi.fn(),
    runAskQuickAction: vi.fn(),
    approveAskProposal: vi.fn(),
    rejectAskProposal: vi.fn(),
    getAskWorkSnapshot: vi.fn(),
    getAskQuickActions: vi.fn(),
    getCaptainTools: vi.fn(),
    listCaptains: vi.fn(),
    listVessels: vi.fn(),
    listPipelines: vi.fn(),
    enumerateFleetActions: vi.fn(),
    decideCliPermissionRequest: vi.fn(),
    setAskThreadCliPermissionPolicy: vi.fn(),
  };
});

const localeValue = {
  t: (text: string, params?: Record<string, string | number | null | undefined>) => translateTemplate('en', text, null, params),
  locale: 'en',
  formatDateTime: (v: string | null | undefined) => v ?? '',
  formatDate: (v: string | null | undefined) => v ?? '',
  formatRelativeTime: (v: string | null | undefined) => (v ? 'recently' : ''),
};
vi.mock('../context/LocaleContext', () => ({ useLocale: () => localeValue }));

const handlers = new Set<(msg: WebSocketMessage) => void>();
const socketValue = {
  connected: true,
  reconnectCount: 0,
  subscribe: (handler: (msg: WebSocketMessage) => void) => { handlers.add(handler); return () => handlers.delete(handler); },
  send: vi.fn(),
};
vi.mock('../context/WebSocketContext', () => ({ useWebSocket: () => socketValue }));
const authValue = { user: { user: { tenantId: 'ten_1' } }, isAdmin: false, isTenantAdmin: false };
vi.mock('../context/AuthContext', () => ({ useAuth: () => authValue }));
vi.mock('../context/NotificationContext', () => ({ useNotifications: () => ({ pushToast: vi.fn() }) }));
vi.mock('../components/vessels/import/ImportWizard', () => ({ default: () => null }));

const api = await import('../api/client');

function emit(type: string, data: unknown) {
  act(() => { handlers.forEach((h) => h({ type, data })); });
}

const page = <T,>(objects: T[]): EnumerationResult<T> => ({ success: true, pageNumber: 1, pageSize: 50, totalPages: 1, totalRecords: objects.length, objects, totalMs: 1 });

const thread: AskThread = {
  id: 'ath_1', title: 'Release prep', captainId: 'cpt_1', autoApprove: false, pinned: false, archived: false, unreadCount: 0,
  cliPermissionPolicy: null,
  cliPermission: { requested: 'ApproveInArmada', effective: 'ApproveInArmada', source: 'ServerDefault', fallbackReason: null },
};

const pending: CliPermissionRequest = {
  id: 'cpr_1', threadId: 'ath_1', messageId: 'amg_3', toolName: 'Bash', summaryText: 'npm publish', inputText: '{"command":"npm publish"}',
  suggestedRule: 'Bash(npm publish:*)', status: 'Pending', captainId: 'cpt_1', captainName: 'Ada',
  expiresUtc: new Date(Date.now() + 300000).toISOString(), canDecide: true, canRemember: true,
};

function messages(request: CliPermissionRequest = pending, extra: AskMessage[] = []): AskMessage[] {
  return [
    { id: 'amg_1', threadId: 'ath_1', sequence: 1, role: 'User', kind: 'Text', contentText: 'Publish the package' },
    { id: 'amg_3', threadId: 'ath_1', sequence: 2, role: 'System', kind: 'CliPermission', contentText: 'Bash: npm publish', cliPermissionRequest: request },
    ...extra,
  ];
}

function detail(th: AskThread = thread): AskThreadDetail {
  return { thread: th, trackedWork: [], pendingProposals: [], pendingCliPermissions: [] };
}

function renderAt(path: string) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="/ask/:threadId?" element={<AskArmada />} />
        <Route path="/captains/:id" element={<div>captain page</div>} />
      </Routes>
    </MemoryRouter>,
  );
}

beforeEach(() => {
  handlers.clear();
  vi.clearAllMocks();
  authValue.isAdmin = false;
  authValue.isTenantAdmin = false;
  vi.mocked(api.listCaptains).mockResolvedValue(page([{ id: 'cpt_1', name: 'Ada', runtime: 'ClaudeCode', model: 'opus' } as never]));
  vi.mocked(api.getCaptainTools).mockResolvedValue({ runtime: 'ClaudeCode', armadaToolCount: 10, askApprovalGated: true } as never);
  vi.mocked(api.getAskQuickActions).mockResolvedValue([]);
  vi.mocked(enumerateAskThreads).mockResolvedValue(page([thread]));
  vi.mocked(getAskThread).mockResolvedValue(detail());
  vi.mocked(enumerateAskMessages).mockResolvedValue({ messages: messages(), hasMore: false });
  vi.mocked(markAskThreadRead).mockResolvedValue(undefined);
});

describe('Ask CLI permission card', () => {
  it('renders the card in the transcript and decides through the API', async () => {
    vi.mocked(decideCliPermissionRequest).mockResolvedValue({ ...pending, status: 'Allowed', decisionSource: 'Approver', canDecide: false });
    renderAt('/ask/ath_1');
    const card = await screen.findByRole('group', { name: 'CLI permission: Bash' });
    expect(within(card).getByText('npm publish')).toBeInTheDocument();
    expect(within(card).getByText('Ada')).toBeInTheDocument();
    expect(within(card).getByText(/^Expires in \d+:\d\d$/)).toBeInTheDocument();
    fireEvent.click(within(card).getByRole('button', { name: 'Allow once' }));
    await waitFor(() => expect(decideCliPermissionRequest).toHaveBeenCalledWith('cpr_1', { decision: 'AllowOnce' }));
    expect(await within(card).findByText('Allowed. The captain ran the tool.')).toBeInTheDocument();
    expect(within(card).queryByRole('button', { name: 'Allow once' })).not.toBeInTheDocument();
  });

  it('shows the wait note to a user who cannot decide, and updates from cli_permission and ask.message events', async () => {
    vi.mocked(enumerateAskMessages).mockResolvedValue({ messages: messages({ ...pending, canDecide: false, canRemember: false }), hasMore: false });
    renderAt('/ask/ath_1');
    const card = await screen.findByRole('group', { name: 'CLI permission: Bash' });
    expect(within(card).getByText('Waiting for an admin to decide.')).toBeInTheDocument();

    // A resolved event for another thread is ignored.
    emit('cli_permission.resolved', { requestId: 'cpr_9', status: 'Denied', request: { ...pending, id: 'cpr_9', threadId: 'ath_2', status: 'Denied' } });
    expect(within(card).getByText('Waiting for an admin to decide.')).toBeInTheDocument();

    emit('cli_permission.resolved', { requestId: 'cpr_1', status: 'Denied', request: { ...pending, status: 'Denied', decisionSource: 'Approver', canDecide: false, lastUpdateUtc: new Date().toISOString() } });
    expect(await within(card).findByText('Denied. The tool did not run.')).toBeInTheDocument();
  });

  it('refreshes the card from ask.message', async () => {
    renderAt('/ask/ath_1');
    const card = await screen.findByRole('group', { name: 'CLI permission: Bash' });
    emit('ask.message', { threadId: 'ath_1', message: { ...messages()[1], cliPermissionRequest: { ...pending, status: 'Expired', decisionSource: 'Timeout', canDecide: false } } });
    expect(await within(card).findByText('Expired without a decision. The tool did not run.')).toBeInTheDocument();
  });
});

describe('Ask refused tool rows', () => {
  const refusedThread: AskThread = {
    ...thread,
    cliPermission: { requested: 'ApproveInArmada', effective: 'Refuse', source: 'Captain', fallbackReason: 'RuntimeUnsupported' },
  };

  it('explains a persisted refused call from the thread resolution and says where to change it', async () => {
    vi.mocked(getAskThread).mockResolvedValue(detail(refusedThread));
    vi.mocked(enumerateAskThreads).mockResolvedValue(page([refusedThread]));
    vi.mocked(enumerateAskMessages).mockResolvedValue({
      messages: [
        { id: 'amg_1', threadId: 'ath_1', sequence: 1, role: 'User', kind: 'Text', contentText: 'Run the tests' },
        { id: 'amg_2', threadId: 'ath_1', sequence: 2, role: 'Assistant', kind: 'Text', contentText: 'I could not run them.', toolCalls: [
          { callId: 'c1', toolName: 'Bash', ok: false, resultText: 'This command requires approval', permissionDenied: true },
          { callId: 'c2', toolName: 'Read', ok: true, resultText: 'ok' },
        ] },
      ],
      hasMore: false,
    });
    renderAt('/ask/ath_1');
    const notes = await screen.findAllByText(/^Refused: CLI tools run with policy Refuse \(from the captain\)\./);
    expect(notes).toHaveLength(1);
    expect(notes[0].textContent).toContain('Change it in the conversation header (CLI tools), on the captain, or in Settings > CLI Tool Permissions.');
    expect(notes[0].textContent).toContain('Approve in Armada fell back to Refuse because this runtime cannot ask Armada for permission.');
    // The fallback is also explained once at the top of the conversation.
    expect(screen.getByTestId('ask-cli-fallback-note')).toHaveTextContent('Approve in Armada fell back to Refuse because this runtime cannot ask Armada for permission.');
  });

  it('marks a live ask.tool call refused for permission and points at the card under Approve in Armada', async () => {
    vi.mocked(enumerateAskMessages).mockResolvedValue({ messages: messages(), hasMore: false });
    renderAt('/ask/ath_1');
    await screen.findByRole('group', { name: 'CLI permission: Bash' });
    emit('ask.turn', { threadId: 'ath_1', turnId: 'turn_1' });
    emit('ask.tool', { threadId: 'ath_1', turnId: 'turn_1', phase: 'started', id: 't1', name: 'Bash', arguments: '{"command":"npm publish"}' });
    emit('ask.tool', { threadId: 'ath_1', turnId: 'turn_1', phase: 'completed', id: 't1', name: 'Bash', ok: false, result: 'denied', permissionDenied: true });
    expect(await screen.findByText('Denied in Armada (see the permission card).')).toBeInTheDocument();
    expect(screen.getByText('Refused for lack of permission')).toBeInTheDocument();
  });
});

describe('Ask CLI tools policy control', () => {
  it('shows the effective policy and source, and sets the thread policy', async () => {
    const updated: AskThread = { ...thread, cliPermissionPolicy: 'Refuse', cliPermission: { requested: 'Refuse', effective: 'Refuse', source: 'AskThread' } };
    vi.mocked(setAskThreadCliPermissionPolicy).mockResolvedValue(updated);
    renderAt('/ask/ath_1');
    const select = await screen.findByRole('combobox', { name: 'CLI tools' }) as HTMLSelectElement;
    await waitFor(() => expect(screen.getByTestId('ask-cli-policy-effective')).toHaveTextContent('Effective: Approve in Armada (from the server default).'));
    // Not an admin: Bypass is not offered.
    expect(Array.from(select.options).map((o) => o.value)).toEqual(['', 'Refuse', 'ApproveInArmada']);

    fireEvent.change(select, { target: { value: 'Refuse' } });
    await waitFor(() => expect(setAskThreadCliPermissionPolicy).toHaveBeenCalledWith('ath_1', 'Refuse'));
    await waitFor(() => expect(screen.getByTestId('ask-cli-policy-effective')).toHaveTextContent('Effective: Refuse (from this conversation).'));
  });

  it('offers Bypass to admins only behind the strong warning', async () => {
    authValue.isTenantAdmin = true;
    vi.mocked(setAskThreadCliPermissionPolicy).mockResolvedValue({ ...thread, cliPermissionPolicy: 'Bypass', cliPermission: { requested: 'Bypass', effective: 'Bypass', source: 'AskThread' } });
    renderAt('/ask/ath_1');
    const select = await screen.findByRole('combobox', { name: 'CLI tools' }) as HTMLSelectElement;
    expect(Array.from(select.options).map((o) => o.value)).toContain('Bypass');

    fireEvent.change(select, { target: { value: 'Bypass' } });
    const dialog = await screen.findByRole('alertdialog');
    expect(dialog).toHaveTextContent('Bypass lets the captain run any command on the Admiral host as the Armada service user without asking.');
    expect(setAskThreadCliPermissionPolicy).not.toHaveBeenCalled();
    fireEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }));
    expect(setAskThreadCliPermissionPolicy).not.toHaveBeenCalled();

    fireEvent.change(select, { target: { value: 'Bypass' } });
    fireEvent.click(within(await screen.findByRole('alertdialog')).getByRole('button', { name: 'Use Bypass' }));
    await waitFor(() => expect(setAskThreadCliPermissionPolicy).toHaveBeenCalledWith('ath_1', 'Bypass'));
  });
});
