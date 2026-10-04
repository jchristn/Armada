import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import AskArmada from './AskArmada';
import {
  approveAskProposal,
  deleteAskThread,
  enumerateAskMessages,
  enumerateAskThreads,
  getAskThread,
  markAskThreadRead,
  rejectAskProposal,
  runAskQuickAction,
  sendAskMessage,
} from '../api/client';
import { translateTemplate } from '../i18n/runtime';
import type { AskMessage, AskThread, AskThreadDetail, EnumerationResult, WebSocketMessage } from '../types/models';

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
vi.mock('../context/AuthContext', () => ({ useAuth: () => ({ user: { user: { tenantId: 'ten_1' } } }) }));
const pushToast = vi.fn();
vi.mock('../context/NotificationContext', () => ({ useNotifications: () => ({ pushToast }) }));
vi.mock('../components/vessels/import/ImportWizard', () => ({ default: ({ open }: { open: boolean }) => (open ? <div>import wizard open</div> : null) }));

const api = await import('../api/client');

function emit(type: string, data: unknown) {
  act(() => { handlers.forEach((h) => h({ type, data })); });
}

const page = <T,>(objects: T[]): EnumerationResult<T> => ({ success: true, pageNumber: 1, pageSize: 50, totalPages: 1, totalRecords: objects.length, objects, totalMs: 1 });

const threads: AskThread[] = [
  { id: 'ath_1', title: 'Billing fix', captainId: 'cpt_1', autoApprove: false, pinned: true, archived: false, lastMessageUtc: '2026-10-04T10:00:00Z', unreadCount: 0, activeWorkCount: 1 },
  { id: 'ath_2', title: 'Dependency sweep', captainId: 'cpt_1', autoApprove: false, pinned: false, archived: false, lastMessageUtc: '2026-10-04T09:00:00Z', unreadCount: 3 },
  { id: 'ath_3', title: 'Checkout tests', captainId: null, autoApprove: false, pinned: false, archived: false, lastMessageUtc: '2026-10-03T09:00:00Z', unreadCount: 0 },
];

const pendingProposal = { id: 'aap_2', threadId: 'ath_1', toolName: 'create_mission', argumentsText: '{"title":"Raise limit"}', summaryText: 'Add mission "Raise limit"', source: 'Captain', status: 'Pending' };
const messages: AskMessage[] = [
  { id: 'amg_1', threadId: 'ath_1', sequence: 1, role: 'User', kind: 'Text', contentText: 'Fix the billing retry bug' },
  { id: 'amg_2', threadId: 'ath_1', sequence: 2, role: 'Assistant', kind: 'Text', contentText: 'I found **the bug**.', toolCalls: [{ callId: 'c1', toolName: 'enumerate', ok: true, resultText: '{}', elapsedMs: 10 }] },
  { id: 'amg_3', threadId: 'ath_1', sequence: 3, role: 'Assistant', kind: 'ActionProposal', proposalId: 'aap_1', proposal: { id: 'aap_1', threadId: 'ath_1', toolName: 'dispatch', argumentsText: '{"vesselId":"vsl_1"}', summaryText: 'Dispatch voyage "Billing fix"', source: 'Captain', status: 'Executed', resultText: '{"id":"vyg_1"}' } },
  { id: 'amg_4', threadId: 'ath_1', sequence: 4, role: 'System', kind: 'ActionResult', proposalId: 'aap_1', trackedWorkId: 'atw_1', contentText: 'Voyage started.' },
  { id: 'amg_5', threadId: 'ath_1', sequence: 5, role: 'System', kind: 'WorkUpdate', trackedWorkId: 'atw_1', contentText: 'Mission Tests failed: 2 failures.' },
  { id: 'amg_6', threadId: 'ath_1', sequence: 6, role: 'System', kind: 'Summary', contentText: 'So far: a voyage is running.' },
  { id: 'amg_7', threadId: 'ath_1', sequence: 7, role: 'System', kind: 'Error', contentText: 'Captain runtime crashed.' },
  { id: 'amg_8', threadId: 'ath_1', sequence: 8, role: 'Assistant', kind: 'ActionProposal', proposalId: 'aap_2', proposal: pendingProposal },
];
const detail: AskThreadDetail = {
  thread: threads[0],
  trackedWork: [{ id: 'atw_1', threadId: 'ath_1', entityType: 'Voyage', entityId: 'vyg_1', title: 'Billing fix voyage', status: 'InProgress', state: 'Active',
    snapshot: { entityType: 'Voyage', entityId: 'vyg_1', status: 'InProgress', state: 'Active', missions: [
      { id: 'msn_1', title: 'Add backoff', status: 'InProgress', captainId: 'cpt_1', captainName: 'Ada' },
      { id: 'msn_2', title: 'Write tests', status: 'Pending' },
    ] } }],
  pendingProposals: [pendingProposal],
};

function threadRows(): HTMLElement[] {
  return Array.from(document.querySelectorAll<HTMLElement>('[data-thread-row]'));
}

function renderAt(path: string) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="/ask/:threadId?" element={<AskArmada />} />
      </Routes>
    </MemoryRouter>,
  );
}

beforeEach(() => {
  handlers.clear();
  vi.clearAllMocks();
  vi.mocked(api.listCaptains).mockResolvedValue(page([{ id: 'cpt_1', name: 'Ada', runtime: 'ClaudeCode', model: 'opus' } as never]));
  vi.mocked(api.getCaptainTools).mockResolvedValue({ runtime: 'ClaudeCode', armadaToolCount: 10 } as never);
  vi.mocked(api.getAskQuickActions).mockResolvedValue([]);
  vi.mocked(enumerateAskThreads).mockResolvedValue(page(threads));
  vi.mocked(getAskThread).mockResolvedValue(detail);
  vi.mocked(enumerateAskMessages).mockResolvedValue({ messages, hasMore: false });
  vi.mocked(markAskThreadRead).mockResolvedValue(undefined);
  vi.mocked(api.listVessels).mockResolvedValue(page([{ id: 'vsl_1', name: 'payments-api' } as never, { id: 'vsl_2', name: 'billing-web' } as never]));
  vi.mocked(api.listPipelines).mockResolvedValue(page([{ id: 'ppl_1', name: 'Reviewed' } as never]));
});

describe('AskArmada thread list', () => {
  it('lists threads pinned first with unread badges and working indicators, and marks the open thread read', async () => {
    renderAt('/ask/ath_1');
    const list = await screen.findByRole('navigation', { name: 'Conversations' });
    await within(list).findByText('Checkout tests');
    const rows = threadRows();
    expect(rows.map((r) => r.getAttribute('title'))).toEqual(['Billing fix', 'Dependency sweep', 'Checkout tests']);
    expect(within(list).getByLabelText('3 unread')).toBeInTheDocument();
    expect(within(rows[0]).getByText('Working')).toBeInTheDocument();
    await waitFor(() => expect(markAskThreadRead).toHaveBeenCalledWith('ath_1'));
  });

  it('searches server-side (debounced)', async () => {
    renderAt('/ask');
    await screen.findByText('Dependency sweep');
    fireEvent.change(screen.getByLabelText('Search conversations'), { target: { value: 'deps' } });
    await waitFor(() => expect(enumerateAskThreads).toHaveBeenLastCalledWith(expect.objectContaining({ search: 'deps', includeArchived: false })), { timeout: 2000 });
  });

  it('renames inline from the row menu', async () => {
    vi.mocked(api.updateAskThread).mockImplementation(async (id, data) => ({ ...threads.find((t) => t.id === id)!, ...data } as AskThread));
    renderAt('/ask');
    await screen.findByText('Checkout tests');
    fireEvent.click(screen.getByRole('button', { name: 'Actions for Checkout tests' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Rename' }));
    const input = screen.getByLabelText('Conversation title');
    fireEvent.change(input, { target: { value: 'Checkout flake' } });
    fireEvent.submit(input.closest('form')!);
    await waitFor(() => expect(api.updateAskThread).toHaveBeenCalledWith('ath_3', { title: 'Checkout flake' }));
    expect(await screen.findByText('Checkout flake')).toBeInTheDocument();
  });

  it('pins from the row menu and moves the thread to the top', async () => {
    vi.mocked(api.updateAskThread).mockImplementation(async (id, data) => ({ ...threads.find((t) => t.id === id)!, ...data } as AskThread));
    renderAt('/ask');
    await screen.findByText('Checkout tests');
    fireEvent.click(screen.getByRole('button', { name: 'Actions for Checkout tests' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Pin' }));
    await waitFor(() => expect(api.updateAskThread).toHaveBeenCalledWith('ath_3', { pinned: true }));
    await waitFor(() => expect(threadRows().map((r) => r.getAttribute('title'))).toEqual(['Billing fix', 'Checkout tests', 'Dependency sweep']));
  });

  it('deletes only after the custom confirm', async () => {
    vi.mocked(deleteAskThread).mockResolvedValue(undefined);
    renderAt('/ask');
    await screen.findByText('Checkout tests');
    fireEvent.click(screen.getByRole('button', { name: 'Actions for Checkout tests' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Delete' }));
    expect(deleteAskThread).not.toHaveBeenCalled();
    expect(screen.getByText(/Work it started keeps running/)).toBeInTheDocument();
    const dialog = screen.getByText('Delete conversation').closest('.modal-box') as HTMLElement;
    fireEvent.click(within(dialog).getByRole('button', { name: 'Delete' }));
    await waitFor(() => expect(deleteAskThread).toHaveBeenCalledWith('ath_3'));
    await waitFor(() => expect(screen.queryByText('Checkout tests')).not.toBeInTheDocument());
  });

  it('badges another thread from an ask.thread event and re-sorts it', async () => {
    renderAt('/ask/ath_1');
    await screen.findByText('Checkout tests');
    emit('ask.thread', { threadId: 'ath_3', thread: { ...threads[2], unreadCount: 5, lastMessageUtc: '2026-10-04T11:00:00Z' } });
    const list = screen.getByRole('navigation', { name: 'Conversations' });
    expect(within(list).getByLabelText('5 unread')).toBeInTheDocument();
    const rows = threadRows();
    expect(rows.map((r) => r.getAttribute('title'))).toEqual(['Billing fix', 'Checkout tests', 'Dependency sweep']);
  });

  it('moves keyboard focus between rows with the arrow keys', async () => {
    renderAt('/ask');
    await screen.findByText('Checkout tests');
    const rows = threadRows();
    rows[0].focus();
    fireEvent.keyDown(rows[0], { key: 'ArrowDown' });
    expect(document.activeElement).toBe(rows[1]);
    fireEvent.keyDown(rows[1], { key: 'End' });
    expect(document.activeElement).toBe(rows[2]);
  });
});

describe('AskArmada conversation', () => {
  it('renders every message kind', async () => {
    renderAt('/ask/ath_1');
    expect(await screen.findByText('Fix the billing retry bug')).toBeInTheDocument();
    expect(screen.getByText('the bug')).toBeInTheDocument();
    expect(screen.getByText('enumerate')).toBeInTheDocument();
    expect(screen.getByText('Dispatch voyage "Billing fix"')).toBeInTheDocument();
    expect(screen.getByText('Action result')).toBeInTheDocument();
    expect(screen.getByText('Voyage started.')).toBeInTheDocument();
    expect(screen.getByText('Mission Tests failed: 2 failures.')).toBeInTheDocument();
    expect(screen.getByText('Progress update')).toBeInTheDocument();
    expect(screen.getByText('Conversation summary')).toBeInTheDocument();
    expect(screen.getByText('Captain runtime crashed.')).toBeInTheDocument();
    expect(screen.getByText('Approval needed')).toBeInTheDocument();
    // The executed proposal is shown once (on its proposal message), not again on the result.
    expect(screen.getAllByText('Dispatch voyage "Billing fix"')).toHaveLength(1);
    // The live card renders on the ActionResult message, the milestone links to it.
    const card = screen.getByRole('region', { name: 'Voyage: Billing fix voyage' });
    expect(within(card).getByText('Add backoff')).toBeInTheDocument();
    expect(within(card).getByRole('link', { name: 'Add backoff' })).toHaveAttribute('href', '/missions/msn_1');
    expect(screen.getByRole('button', { name: 'Show live card' })).toBeInTheDocument();
    expect(screen.getByRole('region', { name: 'Work in this conversation' })).toBeInTheDocument();
  });

  it('shows the exact arguments and approves a pending proposal', async () => {
    vi.mocked(approveAskProposal).mockResolvedValue({ ...pendingProposal, status: 'Executed', resultText: '{"id":"msn_9"}' });
    renderAt('/ask/ath_1');
    await screen.findByText('Approval needed');
    expect(screen.getAllByText('Exact arguments').length).toBeGreaterThan(0);
    expect(screen.getByText(/"title": "Raise limit"/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Approve' }));
    await waitFor(() => expect(approveAskProposal).toHaveBeenCalledWith('ath_1', 'aap_2'));
    await waitFor(() => expect(screen.queryByText('Approval needed')).not.toBeInTheDocument());
    expect(screen.getAllByText('Ran successfully.').length).toBeGreaterThan(0);
  });

  it('rejects a pending proposal', async () => {
    vi.mocked(rejectAskProposal).mockResolvedValue({ ...pendingProposal, status: 'Rejected' });
    renderAt('/ask/ath_1');
    await screen.findByText('Approval needed');
    fireEvent.click(screen.getByRole('button', { name: 'Reject' }));
    await waitFor(() => expect(rejectAskProposal).toHaveBeenCalledWith('ath_1', 'aap_2'));
    expect(await screen.findByText('Rejected. Nothing was run.')).toBeInTheDocument();
  });

  it('updates the work card from an ask.work event and announces it', async () => {
    renderAt('/ask/ath_1');
    const card = await screen.findByRole('region', { name: 'Voyage: Billing fix voyage' });
    expect(within(card).getByText('0 of 2 finished')).toBeInTheDocument();
    emit('ask.work', {
      threadId: 'ath_1', trackedWorkId: 'atw_1',
      snapshot: { entityType: 'Voyage', entityId: 'vyg_1', status: 'Complete', state: 'Succeeded', missions: [
        { id: 'msn_1', title: 'Add backoff', status: 'Complete', prUrl: 'https://example/pr/1' },
        { id: 'msn_2', title: 'Write tests', status: 'Failed', failureReason: 'Tests failed in RetryTests' },
      ] },
    });
    expect(within(card).getByText(/2 of 2 finished/)).toBeInTheDocument();
    expect(within(card).getByText('Tests failed in RetryTests')).toBeInTheDocument();
    expect(within(card).getByRole('link', { name: 'Open pull request' })).toHaveAttribute('href', 'https://example/pr/1');
    expect(screen.getByText('Billing fix voyage is now Complete')).toBeInTheDocument();
  });

  it('ignores ask.work events for other threads', async () => {
    renderAt('/ask/ath_1');
    const card = await screen.findByRole('region', { name: 'Voyage: Billing fix voyage' });
    emit('ask.work', { threadId: 'ath_2', trackedWorkId: 'atw_1', snapshot: { entityType: 'Voyage', entityId: 'vyg_1', status: 'Complete', missions: [{ id: 'msn_1', status: 'Complete' }] } });
    expect(within(card).getByText('0 of 2 finished')).toBeInTheDocument();
  });

  it('loads earlier messages with BeforeSequence', async () => {
    vi.mocked(enumerateAskMessages).mockImplementation(async (_id, query) => (query?.beforeSequence
      ? { messages: [{ id: 'amg_0', threadId: 'ath_1', sequence: 0, role: 'User', kind: 'Text', contentText: 'An older question' }], hasMore: false }
      : { messages, hasMore: true }));
    renderAt('/ask/ath_1');
    fireEvent.click(await screen.findByRole('button', { name: 'Load earlier messages' }));
    await waitFor(() => expect(enumerateAskMessages).toHaveBeenCalledWith('ath_1', { beforeSequence: 1, pageSize: 30 }));
    expect(await screen.findByText('An older question')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Load earlier messages' })).not.toBeInTheDocument();
  });

  it('sends a message and streams the reply from socket events', async () => {
    vi.mocked(sendAskMessage).mockResolvedValue({ messageId: 'amg_20', turnId: 'turn_1' });
    renderAt('/ask/ath_1');
    await screen.findByText('Fix the billing retry bug');
    const input = screen.getByRole('combobox', { name: 'Message' });
    fireEvent.change(input, { target: { value: 'How is it going?' } });
    fireEvent.keyDown(input, { key: 'Enter' });
    expect(await screen.findByText('How is it going?')).toBeInTheDocument();
    await waitFor(() => expect(sendAskMessage).toHaveBeenCalledWith('ath_1', 'How is it going?', false));
    emit('ask.chunk', { threadId: 'ath_1', turnId: 'turn_1', delta: 'Two of four ' });
    emit('ask.chunk', { threadId: 'ath_1', turnId: 'turn_1', delta: 'missions are done.' });
    expect(screen.getByText('Two of four missions are done.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Stop' })).toBeInTheDocument();
  });
});

describe('AskArmada integration fixes', () => {
  it('does not warn about MCP for a Claude Code captain (the server connects it per turn)', async () => {
    vi.mocked(api.getCaptainTools).mockResolvedValue({ runtime: 'ClaudeCode', armadaToolCount: 0 } as never);
    renderAt('/ask/ath_1');
    await screen.findByText('Checkout tests');
    await waitFor(() => expect(api.getCaptainTools).toHaveBeenCalled());
    expect(screen.queryByText(/not connected to Armada over MCP/)).not.toBeInTheDocument();
  });

  it('still warns for a runtime the server does not connect (e.g. Codex without Armada MCP)', async () => {
    vi.mocked(api.listCaptains).mockResolvedValue(page([{ id: 'cpt_1', name: 'Ada', runtime: 'Codex', model: 'gpt' } as never]));
    vi.mocked(api.getCaptainTools).mockResolvedValue({ runtime: 'Codex', armadaToolCount: 0 } as never);
    renderAt('/ask/ath_1');
    expect(await screen.findByText(/not connected to Armada over MCP/)).toBeInTheDocument();
  });

  it('treats a Codex captain as gated when the server reports askApprovalGated', async () => {
    vi.mocked(api.listCaptains).mockResolvedValue(page([{ id: 'cpt_1', name: 'Ada', runtime: 'Codex', model: 'gpt' } as never]));
    vi.mocked(api.getCaptainTools).mockResolvedValue({ runtime: 'Codex', armadaToolCount: 0, askApprovalGated: true } as never);
    renderAt('/ask/ath_1');
    await screen.findByText('Checkout tests');
    await waitFor(() => expect(api.getCaptainTools).toHaveBeenCalled());
    expect(screen.queryByText(/not connected to Armada over MCP/)).not.toBeInTheDocument();
    expect(screen.queryByTestId('ask-ungated-note')).not.toBeInTheDocument();
  });

  it('shows a persistent note when the captain\'s actions are not gated', async () => {
    vi.mocked(api.listCaptains).mockResolvedValue(page([{ id: 'cpt_1', name: 'Ada', runtime: 'Custom', model: null } as never]));
    vi.mocked(api.getCaptainTools).mockResolvedValue({ runtime: 'Custom', armadaToolCount: 12, askApprovalGated: false } as never);
    renderAt('/ask/ath_1');
    const note = await screen.findByTestId('ask-ungated-note');
    expect(note).toHaveAttribute('role', 'note');
    expect(note).toHaveTextContent('Actions from this captain run without approval cards.');
  });

  it('does not render the empty reply reserved while the captain is still writing', async () => {
    vi.mocked(enumerateAskMessages).mockResolvedValue({ messages: [...messages, { id: 'amg_reserved', threadId: 'ath_1', sequence: 999, role: 'Assistant', kind: 'Text', contentText: '' } as never], hasMore: false });
    renderAt('/ask/ath_1');
    await screen.findByText('Checkout tests');
    await waitFor(() => expect(enumerateAskMessages).toHaveBeenCalled());
    await waitFor(() => expect(document.querySelectorAll('[data-sequence]').length).toBeGreaterThan(0));
    expect(document.querySelector('[data-sequence="999"]')).toBeNull();
  });
});

describe('AskArmada quick actions', () => {
  it('opens the menu on /, and /dispatch submits the MCP dispatch arguments', async () => {
    vi.mocked(runAskQuickAction).mockResolvedValue({ id: 'aap_q', threadId: 'ath_1', toolName: 'dispatch', source: 'QuickAction', status: 'Executed' });
    renderAt('/ask/ath_1');
    await screen.findByText('Fix the billing retry bug');
    const input = screen.getByRole('combobox', { name: 'Message' });
    fireEvent.change(input, { target: { value: '/' } });
    const menu = screen.getByRole('listbox', { name: 'Quick actions' });
    expect(within(menu).getAllByRole('option').map((o) => o.querySelector('code')?.textContent)).toEqual(['/dispatch', '/fleet-action', '/status', '/health', '/import']);
    fireEvent.keyDown(input, { key: 'ArrowDown' });
    expect(within(menu).getAllByRole('option')[1]).toHaveAttribute('aria-selected', 'true');
    fireEvent.keyDown(input, { key: 'ArrowUp' });
    fireEvent.keyDown(input, { key: 'Enter' });

    const form = await screen.findByRole('form', { name: 'Dispatch a voyage' });
    await within(form).findByRole('option', { name: 'payments-api' });
    fireEvent.click(within(form).getByRole('button', { name: 'Dispatch' }));
    expect(within(form).getByText('Choose a vessel.')).toBeInTheDocument();
    expect(runAskQuickAction).not.toHaveBeenCalled();

    fireEvent.change(within(form).getAllByRole('combobox')[0], { target: { value: 'vsl_1' } });
    fireEvent.change(within(form).getByLabelText('Mission 1 title'), { target: { value: 'Add backoff' } });
    fireEvent.change(within(form).getByLabelText('Mission 1 description'), { target: { value: 'Retry with jitter' } });
    fireEvent.click(within(form).getByRole('button', { name: '+ Add mission' }));
    fireEvent.change(within(form).getByLabelText('Mission 2 title'), { target: { value: 'Add tests' } });
    fireEvent.change(within(form).getAllByRole('combobox')[1], { target: { value: 'ppl_1' } });
    fireEvent.click(within(form).getByRole('button', { name: 'Dispatch' }));

    await waitFor(() => expect(runAskQuickAction).toHaveBeenCalledWith('ath_1', 'dispatch', {
      title: 'Add backoff',
      vesselId: 'vsl_1',
      missions: [{ title: 'Add backoff', description: 'Retry with jitter' }, { title: 'Add tests', description: 'Add tests' }],
      pipelineId: 'ppl_1',
    }));
    await waitFor(() => expect(screen.queryByRole('form', { name: 'Dispatch a voyage' })).not.toBeInTheDocument());
  });

  it('runs /status immediately and opens the import wizard for /import', async () => {
    vi.mocked(runAskQuickAction).mockResolvedValue({ id: 'aap_s', threadId: 'ath_1', toolName: 'status', source: 'QuickAction', status: 'Executed' });
    renderAt('/ask/ath_1');
    await screen.findByText('Fix the billing retry bug');
    const input = screen.getByRole('combobox', { name: 'Message' });
    fireEvent.change(input, { target: { value: '/sta' } });
    fireEvent.keyDown(input, { key: 'Enter' });
    await waitFor(() => expect(runAskQuickAction).toHaveBeenCalledWith('ath_1', 'status', {}));
    fireEvent.change(input, { target: { value: '/imp' } });
    fireEvent.keyDown(input, { key: 'Enter' });
    expect(await screen.findByText('import wizard open')).toBeInTheDocument();
  });
});
