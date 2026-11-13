import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useLocation, useNavigate } from 'react-router-dom';
import AskArmada from './AskArmada';
import {
  approveAskProposal,
  deleteAskThread,
  enumerateAskMessages,
  enumerateAskThreads,
  getAskThread,
  markAskThreadRead,
  markAskThreadUnread,
  ApiError,
  rejectAskProposal,
  runAskQuickAction,
  sendAskMessage,
} from '../api/client';
import { translateTemplate } from '../i18n/runtime';
import type { AskMessage, AskThread, AskThreadView, EnumerationResult, WebSocketMessage } from '../types/models';

vi.mock('../api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../api/client')>();
  return {
    ...actual,
    enumerateAskThreads: vi.fn(),
    createAskThread: vi.fn(),
    getAskThread: vi.fn(),
    updateAskThread: vi.fn(),
    deleteAskThread: vi.fn(),
    deleteAskThreads: vi.fn(),
    enumerateAskMessages: vi.fn(),
    sendAskMessage: vi.fn(),
    cancelAskTurn: vi.fn(),
    summarizeAskThread: vi.fn(),
    markAskThreadRead: vi.fn(),
    markAskThreadUnread: vi.fn(),
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
    listBlackBox: vi.fn(),
  };
});

const localeValue = {
  t: (text: string, params?: Record<string, string | number | null | undefined>) => translateTemplate('en', text, null, params),
  locale: 'en',
  formatDateTime: (v: string | null | undefined) => v ?? '',
  formatDate: (v: string | null | undefined) => v ?? '',
  formatRelativeTime: (v: string | null | undefined) => (v ? 'recently' : ''),
};
vi.mock('../lib/askPaging', async (importOriginal) => {
  // The page reads the thread's collections through lib/askPaging (covered by askPaging.test.ts); these tests feed
  // the composed view straight from the mocked getAskThread.
  const actual = await importOriginal<typeof import('../lib/askPaging')>();
  const client = await import('../api/client');
  return {
    ...actual,
    loadAskThreadView: (id: string) => client.getAskThread(id),
    withToolCalls: async (_id: string, m: unknown) => m,
    withWorkRows: async (_threadId: string, _workId: string, snapshot: unknown) => snapshot,
    loadToolCalls: async () => [],
  };
});
vi.mock('../context/LocaleContext', () => ({ useLocale: () => localeValue }));

const handlers = new Set<(msg: WebSocketMessage) => void>();
const socketValue = {
  connected: true,
  reconnectCount: 0,
  subscribe: (handler: (msg: WebSocketMessage) => void) => { handlers.add(handler); return () => handlers.delete(handler); },
  send: vi.fn(),
};
vi.mock('../context/WebSocketContext', () => ({ useWebSocket: () => socketValue }));
vi.mock('../context/AuthContext', () => ({ useAuth: () => ({ user: { user: { id: 'usr_1', tenantId: 'ten_1' } } }) }));
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
const detail: AskThreadView = {
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

// Shows the router location and offers a Back button, so tests can see redirects and history moves.
function LocationProbe() {
  const location = useLocation();
  const navigate = useNavigate();
  return (
    <>
      <output data-testid="location">{location.pathname}</output>
      <button type="button" onClick={() => navigate(-1)}>probe back</button>
    </>
  );
}

function renderAt(path: string | { pathname: string; state?: unknown }, entries: Array<string | { pathname: string; state?: unknown }> = []) {
  return render(
    <MemoryRouter initialEntries={[...entries, path]}>
      <Routes>
        <Route path="/ask/:threadId?" element={<AskArmada />} />
      </Routes>
      <LocationProbe />
    </MemoryRouter>,
  );
}

const LAST_THREAD_KEY = 'armada_ask_last_thread:usr_1';

function currentPath(): string {
  return screen.getByTestId('location').textContent ?? '';
}

beforeEach(() => {
  localStorage.clear();
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
  it('refreshes with the current filters and preserves the open conversation and selection', async () => {
    renderAt('/ask/ath_1');
    await screen.findByText('Checkout tests');
    await screen.findByText('Fix the billing retry bug');
    fireEvent.change(screen.getByLabelText('Search conversations'), { target: { value: 'Billing' } });
    await waitFor(() => expect(enumerateAskThreads).toHaveBeenLastCalledWith(expect.objectContaining({ search: 'Billing' })));
    fireEvent.click(screen.getByLabelText('Show archived'));
    await waitFor(() => expect(enumerateAskThreads).toHaveBeenLastCalledWith(expect.objectContaining({ includeArchived: true })));
    const refresh = screen.getByRole('button', { name: 'Refresh conversations' });
    await waitFor(() => expect(refresh).toBeEnabled());
    const calls = vi.mocked(enumerateAskThreads).mock.calls.length;
    let finish!: (value: EnumerationResult<AskThread>) => void;
    vi.mocked(enumerateAskThreads).mockReturnValueOnce(new Promise((resolve) => { finish = resolve; }));
    fireEvent.click(refresh);
    expect(enumerateAskThreads).toHaveBeenCalledTimes(calls + 1);
    expect(enumerateAskThreads).toHaveBeenLastCalledWith(expect.objectContaining({ pageNumber: 1, search: 'Billing', includeArchived: true }));
    expect(refresh).toBeDisabled();
    expect(refresh).toHaveAttribute('aria-busy', 'true');
    expect(refresh).toHaveClass('refreshing');
    expect(refresh).toHaveAttribute('title', 'Refresh conversations');
    fireEvent.click(refresh);
    expect(enumerateAskThreads).toHaveBeenCalledTimes(calls + 1);
    await act(async () => { finish(page([{ ...threads[0], title: 'Billing refreshed' }])); });
    expect(await screen.findByText('Billing refreshed')).toBeInTheDocument();
    expect(refresh).toBeEnabled();
    expect(currentPath()).toBe('/ask/ath_1');
    expect(threadRows()[0]).toHaveAttribute('aria-current', 'page');
    expect(screen.getByText('Fix the billing retry bug')).toBeInTheDocument();
    expect(getAskThread).toHaveBeenCalledTimes(1);
    expect(screen.getByLabelText('Search conversations')).toHaveValue('Billing');
    expect(screen.getByLabelText('Show archived')).toBeChecked();
  });

  it('keeps the previous list and conversation visible when refresh fails and allows retry', async () => {
    renderAt('/ask/ath_1');
    await screen.findByText('Checkout tests');
    await screen.findByText('Fix the billing retry bug');
    const refresh = screen.getByRole('button', { name: 'Refresh conversations' });
    await waitFor(() => expect(refresh).toBeEnabled());
    vi.mocked(enumerateAskThreads).mockRejectedValueOnce(new Error('Refresh unavailable'));
    fireEvent.click(refresh);
    expect(await screen.findByText('Refresh unavailable')).toBeInTheDocument();
    expect(screen.getByText('Checkout tests')).toBeInTheDocument();
    expect(screen.getByText('Fix the billing retry bug')).toBeInTheDocument();
    expect(currentPath()).toBe('/ask/ath_1');
    expect(refresh).toBeEnabled();
    vi.mocked(enumerateAskThreads).mockResolvedValueOnce({} as EnumerationResult<AskThread>);
    fireEvent.click(refresh);
    await waitFor(() => expect(threadRows()).toHaveLength(0));
    expect(screen.queryByText('Refresh unavailable')).not.toBeInTheDocument();
    expect(screen.getByText('Fix the billing retry bug')).toBeInTheDocument();
  });

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
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Rename' }));
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
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Pin' }));
    await waitFor(() => expect(api.updateAskThread).toHaveBeenCalledWith('ath_3', { pinned: true }));
    await waitFor(() => expect(threadRows().map((r) => r.getAttribute('title'))).toEqual(['Billing fix', 'Checkout tests', 'Dependency sweep']));
  });

  it('deletes only after the custom confirm', async () => {
    vi.mocked(deleteAskThread).mockResolvedValue(undefined);
    renderAt('/ask');
    await screen.findByText('Checkout tests');
    fireEvent.click(screen.getByRole('button', { name: 'Actions for Checkout tests' }));
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Delete' }));
    expect(deleteAskThread).not.toHaveBeenCalled();
    expect(screen.getByText(/Work it started keeps running/)).toBeInTheDocument();
    const dialog = screen.getByText('Delete conversation').closest('.modal-box') as HTMLElement;
    fireEvent.click(within(dialog).getByRole('button', { name: 'Delete' }));
    await waitFor(() => expect(deleteAskThread).toHaveBeenCalledWith('ath_3'));
    await waitFor(() => expect(screen.queryByText('Checkout tests')).not.toBeInTheDocument());
  });

  function deferred<T>() {
    let resolve!: (value: T) => void;
    let reject!: (reason: unknown) => void;
    const promise = new Promise<T>((res, rej) => { resolve = res; reject = rej; });
    return { promise, resolve, reject };
  }

  async function settleDelete() {
    await act(async () => { await new Promise((resolve) => setTimeout(resolve, 50)); });
  }

  const DELETING_TEXT = 'This conversation is being deleted. This may take a few moments.';

  it('keeps the progress modal up until a slow delete completes', async () => {
    const pending = deferred<void>();
    vi.mocked(deleteAskThread).mockReturnValue(pending.promise);
    renderAt('/ask');
    await screen.findByText('Checkout tests');
    fireEvent.click(screen.getByRole('button', { name: 'Actions for Checkout tests' }));
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Delete' }));
    const confirm = screen.getByText('Delete conversation').closest('.modal-box') as HTMLElement;
    fireEvent.click(within(confirm).getByRole('button', { name: 'Delete' }));

    const progress = await screen.findByRole('alertdialog', { name: 'Deleting conversation' });
    expect(progress).toHaveTextContent(DELETING_TEXT);
    expect(progress).toHaveAttribute('aria-busy', 'true');
    expect(within(progress).queryAllByRole('button')).toHaveLength(0);
    expect(screen.queryByText(/Work it started keeps running/)).not.toBeInTheDocument();
    // Escape and the backdrop do not hide it while the request is in flight.
    fireEvent.keyDown(document, { key: 'Escape' });
    fireEvent.click(progress.parentElement as HTMLElement);
    await settleDelete();
    expect(screen.getByText(DELETING_TEXT)).toBeInTheDocument();
    expect(screen.getByText('Checkout tests')).toBeInTheDocument();

    await act(async () => { pending.resolve(undefined); await pending.promise; });
    await waitFor(() => expect(screen.queryByText(DELETING_TEXT)).not.toBeInTheDocument());
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
    expect(screen.queryByText('Checkout tests')).not.toBeInTheDocument();
    expect(deleteAskThread).toHaveBeenCalledTimes(1);
  });

  it('submits a delete only once while it is pending', async () => {
    const pending = deferred<void>();
    vi.mocked(deleteAskThread).mockReturnValue(pending.promise);
    renderAt('/ask');
    await screen.findByText('Checkout tests');
    fireEvent.click(screen.getByRole('button', { name: 'Actions for Checkout tests' }));
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Delete' }));
    const confirm = screen.getByText('Delete conversation').closest('.modal-box') as HTMLElement;
    const button = within(confirm).getByRole('button', { name: 'Delete' });
    // A double click lands both clicks on the confirm button before React re-renders.
    fireEvent.click(button);
    fireEvent.click(button);
    await screen.findByText(DELETING_TEXT);
    // Opening the row menu again while pending cannot start a second delete.
    fireEvent.click(screen.getByRole('button', { name: 'Actions for Checkout tests' }));
    const again = screen.queryByRole('menuitem', { name: 'Delete' });
    if (again) fireEvent.click(again);
    await settleDelete();
    expect(screen.queryByText('Delete conversation')).not.toBeInTheDocument();
    expect(deleteAskThread).toHaveBeenCalledTimes(1);

    await act(async () => { pending.resolve(undefined); await pending.promise; });
    await waitFor(() => expect(screen.queryByText(DELETING_TEXT)).not.toBeInTheDocument());
    expect(deleteAskThread).toHaveBeenCalledTimes(1);
  });

  it('turns a failed delete into an error with Dismiss and Retry', async () => {
    const first = deferred<void>();
    vi.mocked(deleteAskThread).mockReturnValueOnce(first.promise);
    renderAt('/ask');
    await screen.findByText('Checkout tests');
    fireEvent.click(screen.getByRole('button', { name: 'Actions for Checkout tests' }));
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Delete' }));
    let confirm = screen.getByText('Delete conversation').closest('.modal-box') as HTMLElement;
    fireEvent.click(within(confirm).getByRole('button', { name: 'Delete' }));
    await screen.findByText(DELETING_TEXT);

    await act(async () => { first.reject(new ApiError('Database is locked', 500, null)); await first.promise.catch(() => undefined); });
    const failure = await screen.findByRole('alertdialog', { name: 'Error' });
    expect(failure).toHaveTextContent('Database is locked');
    expect(failure).toHaveAttribute('aria-busy', 'false');
    expect(screen.queryByText(DELETING_TEXT)).not.toBeInTheDocument();
    expect(within(failure).getByRole('button', { name: 'Dismiss' })).toHaveFocus();
    expect(screen.getByText('Checkout tests')).toBeInTheDocument();

    // Retry shows the progress state again and removes the row once it succeeds.
    const second = deferred<void>();
    vi.mocked(deleteAskThread).mockReturnValueOnce(second.promise);
    fireEvent.click(within(failure).getByRole('button', { name: 'Retry' }));
    expect(await screen.findByText(DELETING_TEXT)).toBeInTheDocument();
    expect(deleteAskThread).toHaveBeenCalledTimes(2);
    await act(async () => { second.resolve(undefined); await second.promise; });
    await waitFor(() => expect(screen.queryByText('Checkout tests')).not.toBeInTheDocument());
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();

    // Dismiss after a failure clears the pending state and allows deleting again.
    vi.mocked(deleteAskThread).mockRejectedValueOnce(new ApiError('Still locked', 500, null));
    fireEvent.click(screen.getByRole('button', { name: 'Actions for Billing fix' }));
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Delete' }));
    confirm = screen.getByText('Delete conversation').closest('.modal-box') as HTMLElement;
    fireEvent.click(within(confirm).getByRole('button', { name: 'Delete' }));
    const again = await screen.findByRole('alertdialog', { name: 'Error' });
    fireEvent.click(within(again).getByRole('button', { name: 'Dismiss' }));
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
    expect(screen.getByText('Billing fix')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Actions for Billing fix' }));
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Delete' }));
    expect(screen.getByText('Delete conversation')).toBeInTheDocument();
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

describe('AskArmada list resizing', () => {
  it('offers a keyboard-operable splitter that sizes the list and remembers the width', async () => {
    const { unmount } = renderAt('/ask/ath_1');
    const splitter = await screen.findByRole('separator', { name: 'Resize conversation list' });
    expect(splitter).toHaveAttribute('aria-orientation', 'vertical');
    expect(splitter).toHaveAttribute('aria-controls', 'ask-thread-drawer');
    const home = splitter.closest<HTMLElement>('.ask-home')!;
    expect(home.style.getPropertyValue('--ask-list-width')).toBe('');
    fireEvent.keyDown(splitter, { key: 'End' });
    expect(home.style.getPropertyValue('--ask-list-width')).toBe('640px');
    expect(splitter).toHaveAttribute('aria-valuenow', '640');
    unmount();

    renderAt('/ask/ath_1');
    const again = await screen.findByRole('separator', { name: 'Resize conversation list' });
    expect(again.closest<HTMLElement>('.ask-home')!.style.getPropertyValue('--ask-list-width')).toBe('640px');
    fireEvent.doubleClick(again);
    expect(again.closest<HTMLElement>('.ask-home')!.style.getPropertyValue('--ask-list-width')).toBe('');
    expect(again).not.toHaveAttribute('aria-valuenow');
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

  it('a reply that finishes before the send returns ends the turn and shows the reply', async () => {
    let resolveSend: (value: { messageId: string; turnId: string }) => void = () => undefined;
    vi.mocked(sendAskMessage).mockReturnValue(new Promise((resolve) => { resolveSend = resolve; }));
    renderAt('/ask/ath_1');
    await screen.findByText('Fix the billing retry bug');
    const input = screen.getByRole('combobox', { name: 'Message' });
    fireEvent.change(input, { target: { value: 'ping' } });
    fireEvent.keyDown(input, { key: 'Enter' });
    await waitFor(() => expect(sendAskMessage).toHaveBeenCalledWith('ath_1', 'ping', false));
    expect(screen.getByRole('button', { name: 'Stop' })).toBeInTheDocument();

    // The whole turn arrives on the socket while the send's HTTP response is still in flight.
    const user: AskMessage = { id: 'amg_20', threadId: 'ath_1', sequence: 9, role: 'User', kind: 'Text', contentText: 'ping' };
    const reply: AskMessage = { id: 'amg_21', threadId: 'ath_1', sequence: 10, role: 'Assistant', kind: 'Text', contentText: 'pong' };
    vi.mocked(enumerateAskMessages).mockResolvedValue({ messages: [...messages, user, reply], hasMore: false });
    emit('ask.turn', { threadId: 'ath_1', turnId: 'turn_fast', state: 'started' });
    emit('ask.message', { threadId: 'ath_1', message: user });
    emit('ask.message', { threadId: 'ath_1', message: reply });
    emit('ask.turn', { threadId: 'ath_1', turnId: 'turn_fast', state: 'completed', messageId: 'amg_21' });

    await act(async () => { resolveSend({ messageId: 'amg_20', turnId: 'turn_fast' }); });
    expect(await screen.findByText('pong')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Stop' })).toBeNull();
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
    const menu = screen.getByRole('listbox', { name: 'Commands' });
    expect(within(menu).getAllByRole('option').map((o) => o.querySelector('code')?.textContent)).toEqual([
      '/dispatch', '/fleet-action', '/status', '/health', '/import', '/new', '/help', '/summarize', '/rename <title>', '/archive', '/captain <name>', '/thinking on|off',
    ]);
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

describe('AskArmada commands', () => {
  async function openThread() {
    renderAt('/ask/ath_1');
    await screen.findByText('Fix the billing retry bug');
    return screen.getByRole('combobox', { name: 'Message' }) as HTMLTextAreaElement;
  }

  function type(input: HTMLTextAreaElement, value: string) {
    fireEvent.change(input, { target: { value } });
  }

  it('runs an exact command with Return, and Send is enabled for it', async () => {
    vi.mocked(api.summarizeAskThread).mockResolvedValue(undefined as never);
    const input = await openThread();
    type(input, '/summarize');
    expect(screen.getByRole('button', { name: 'Send' })).toBeEnabled();
    fireEvent.keyDown(input, { key: 'Enter' });
    await waitFor(() => expect(api.summarizeAskThread).toHaveBeenCalledWith('ath_1'));
    expect(input.value).toBe('');
    expect(sendAskMessage).not.toHaveBeenCalled();
  });

  it('runs a command with arguments from the Send button', async () => {
    vi.mocked(api.updateAskThread).mockImplementation(async (id, data) => ({ ...threads.find((t) => t.id === id)!, ...data } as AskThread));
    const input = await openThread();
    type(input, '/RENAME Billing retry fix');
    fireEvent.click(screen.getByRole('button', { name: 'Send' }));
    await waitFor(() => expect(api.updateAskThread).toHaveBeenCalledWith('ath_1', { title: 'Billing retry fix' }));
    expect(input.value).toBe('');
  });

  it('runs the highlighted menu entry with Return', async () => {
    vi.mocked(runAskQuickAction).mockResolvedValue({ id: 'aap_h', threadId: 'ath_1', toolName: 'evaluate_vessel_health', source: 'QuickAction', status: 'Executed' });
    const input = await openThread();
    type(input, '/he');
    const menu = screen.getByRole('listbox', { name: 'Commands' });
    expect(within(menu).getAllByRole('option').map((o) => o.querySelector('code')?.textContent)).toEqual(['/health', '/help']);
    fireEvent.keyDown(input, { key: 'ArrowDown' });
    fireEvent.keyDown(input, { key: 'Enter' });
    // /help opens the full menu with descriptions.
    expect(input.value).toBe('/');
    expect(within(screen.getByRole('listbox', { name: 'Commands' })).getByText('List the commands you can type here')).toBeInTheDocument();
    type(input, '/he');
    fireEvent.keyDown(input, { key: 'Enter' });
    await waitFor(() => expect(runAskQuickAction).toHaveBeenCalledWith('ath_1', 'evaluate_vessel_health', {}));
  });

  it('shows a hint for an unknown command and keeps the text', async () => {
    const input = await openThread();
    type(input, '/foo bar');
    expect(screen.getByRole('button', { name: 'Send' })).toBeDisabled();
    fireEvent.keyDown(input, { key: 'Enter' });
    expect(screen.getByText('Unknown command /foo. Type / to see commands')).toBeInTheDocument();
    expect(input.value).toBe('/foo bar');
    expect(sendAskMessage).not.toHaveBeenCalled();
    type(input, '/foo');
    expect(screen.queryByText('Unknown command /foo. Type / to see commands')).not.toBeInTheDocument();
  });

  it('/clear starts a new conversation with an empty composer and keeps the captain', async () => {
    const input = await openThread();
    type(input, '/clear');
    fireEvent.keyDown(input, { key: 'Enter' });
    await waitFor(() => expect(screen.getByRole('heading', { name: 'New conversation' })).toBeInTheDocument());
    const fresh = screen.getByRole('combobox', { name: 'Message' }) as HTMLTextAreaElement;
    expect(fresh.value).toBe('');
    expect(document.activeElement).toBe(fresh);
    expect((screen.getByLabelText('Captain', { selector: 'select' }) as HTMLSelectElement).value).toBe('cpt_1');
    // The old conversation stays in the list.
    expect(threadRows().map((r) => r.getAttribute('title'))).toContain('Billing fix');
  });

  it('New conversation clears the draft, and each conversation keeps its own', async () => {
    const input = await openThread();
    type(input, 'half-written question');
    fireEvent.click(screen.getByRole('button', { name: 'New conversation' }));
    await waitFor(() => expect(screen.getByRole('heading', { name: 'New conversation' })).toBeInTheDocument());
    const fresh = screen.getByRole('combobox', { name: 'Message' }) as HTMLTextAreaElement;
    expect(fresh.value).toBe('');
    type(fresh, 'draft for a new one');
    fireEvent.click(screen.getByRole('button', { name: 'New conversation' }));
    expect(fresh.value).toBe('');
    fireEvent.click(threadRows()[0]);
    await screen.findByText('Fix the billing retry bug');
    expect((screen.getByRole('combobox', { name: 'Message' }) as HTMLTextAreaElement).value).toBe('half-written question');
    fireEvent.click(threadRows()[1]);
    await waitFor(() => expect((screen.getByRole('combobox', { name: 'Message' }) as HTMLTextAreaElement).value).toBe(''));
  });

  it('thread commands on a new conversation explain why nothing happened', async () => {
    renderAt('/ask');
    await screen.findByText('Checkout tests');
    const input = screen.getByRole('combobox', { name: 'Message' }) as HTMLTextAreaElement;
    type(input, '/summarize');
    fireEvent.keyDown(input, { key: 'Enter' });
    expect(await screen.findByText('Nothing to summarize yet.')).toBeInTheDocument();
    expect(api.summarizeAskThread).not.toHaveBeenCalled();
    expect(input.value).toBe('/summarize');
  });

  it('/thinking and /captain change the composer settings', async () => {
    vi.mocked(api.listCaptains).mockResolvedValue(page([
      { id: 'cpt_1', name: 'Ada', runtime: 'ClaudeCode', model: 'opus' } as never,
      { id: 'cpt_2', name: 'Grace Hopper', runtime: 'ClaudeCode', model: 'opus' } as never,
    ]));
    vi.mocked(api.updateAskThread).mockImplementation(async (id, data) => ({ ...threads.find((t) => t.id === id)!, ...data } as AskThread));
    const input = await openThread();
    await waitFor(() => expect(screen.getByRole('option', { name: /Grace Hopper/ })).toBeInTheDocument());
    type(input, '/thinking on');
    fireEvent.keyDown(input, { key: 'Enter' });
    expect(await screen.findByText('Show thinking is on.')).toBeInTheDocument();
    expect(screen.getByRole('checkbox', { name: 'Show thinking' })).toBeChecked();
    type(input, '/captain hopper');
    fireEvent.keyDown(input, { key: 'Enter' });
    await waitFor(() => expect(api.updateAskThread).toHaveBeenCalledWith('ath_1', { captainId: 'cpt_2' }));
    expect(await screen.findByText('Captain: Grace Hopper')).toBeInTheDocument();
  });
});

describe('AskArmada last open conversation', () => {
  afterEach(() => { vi.restoreAllMocks(); });

  async function settle() {
    await act(async () => { await new Promise((resolve) => setTimeout(resolve, 50)); });
  }

  it('stores the open conversation and reopens it when /ask is entered again', async () => {
    const first = renderAt('/ask/ath_1');
    await screen.findByText('Fix the billing retry bug');
    expect(localStorage.getItem(LAST_THREAD_KEY)).toBe('ath_1');
    first.unmount();

    vi.mocked(getAskThread).mockClear();
    renderAt('/ask');
    await waitFor(() => expect(currentPath()).toBe('/ask/ath_1'));
    expect(await screen.findByText('Fix the billing retry bug')).toBeInTheDocument();
    expect(getAskThread).toHaveBeenCalledWith('ath_1');
  });

  it('replaces the bare /ask entry, so Back does not bounce through it', async () => {
    localStorage.setItem(LAST_THREAD_KEY, 'ath_1');
    renderAt('/ask', ['/missions']);
    await screen.findByText('Fix the billing retry bug');
    fireEvent.click(screen.getByRole('button', { name: 'probe back' }));
    await waitFor(() => expect(currentPath()).toBe('/missions'));
  });

  it('Back from a conversation to an empty /ask stays on the empty /ask', async () => {
    renderAt('/ask');
    await screen.findByText('Checkout tests');
    fireEvent.click(threadRows()[0]);
    await screen.findByText('Fix the billing retry bug');
    expect(localStorage.getItem(LAST_THREAD_KEY)).toBe('ath_1');
    fireEvent.click(screen.getByRole('button', { name: 'probe back' }));
    await waitFor(() => expect(currentPath()).toBe('/ask'));
    await settle();
    expect(currentPath()).toBe('/ask');
    expect(screen.getByRole('heading', { name: 'New conversation' })).toBeInTheDocument();
  });

  it('does not reopen another user\'s conversation', async () => {
    localStorage.setItem('armada_ask_last_thread:usr_2', 'ath_2');
    localStorage.setItem('armada_ask_last_thread', 'ath_2');
    renderAt('/ask');
    await screen.findByText('Checkout tests');
    await settle();
    expect(currentPath()).toBe('/ask');
    expect(getAskThread).not.toHaveBeenCalled();
  });

  it('New conversation does not redirect back to the stored conversation', async () => {
    renderAt('/ask/ath_1');
    await screen.findByText('Fix the billing retry bug');
    expect(localStorage.getItem(LAST_THREAD_KEY)).toBe('ath_1');
    fireEvent.click(screen.getByRole('button', { name: 'New conversation' }));
    await waitFor(() => expect(currentPath()).toBe('/ask'));
    await settle();
    expect(currentPath()).toBe('/ask');
    expect(screen.getByRole('heading', { name: 'New conversation' })).toBeInTheDocument();
    expect(localStorage.getItem(LAST_THREAD_KEY)).toBeNull();
    expect(getAskThread).toHaveBeenCalledTimes(1);
  });

  it('a draft handed over from another page opens a new conversation, not the stored one', async () => {
    localStorage.setItem(LAST_THREAD_KEY, 'ath_1');
    renderAt({ pathname: '/ask', state: { draft: 'Why did the build fail?' } });
    await screen.findByText('Checkout tests');
    await waitFor(() => expect((screen.getByRole('combobox', { name: 'Message' }) as HTMLTextAreaElement).value).toBe('Why did the build fail?'));
    await settle();
    expect(currentPath()).toBe('/ask');
    expect(getAskThread).not.toHaveBeenCalled();
  });

  it('a stored conversation that no longer loads is forgotten and the page stays on an empty /ask', async () => {
    localStorage.setItem(LAST_THREAD_KEY, 'ath_gone');
    vi.mocked(getAskThread).mockRejectedValue(new ApiError('Not found', 404, null));
    renderAt('/ask');
    await waitFor(() => expect(getAskThread).toHaveBeenCalledWith('ath_gone'));
    await waitFor(() => expect(currentPath()).toBe('/ask'));
    await settle();
    expect(currentPath()).toBe('/ask');
    expect(localStorage.getItem(LAST_THREAD_KEY)).toBeNull();
    expect(getAskThread).toHaveBeenCalledTimes(1);
    expect(screen.getByRole('heading', { name: 'New conversation' })).toBeInTheDocument();
    expect(screen.queryByText(/This conversation was not found/)).toBeNull();
  });

  it('a conversation opened by URL that is not found is forgotten but still shows the error', async () => {
    localStorage.setItem(LAST_THREAD_KEY, 'ath_gone');
    vi.mocked(getAskThread).mockRejectedValue(new ApiError('Not found', 404, null));
    renderAt('/ask/ath_gone');
    expect(await screen.findByText(/This conversation was not found/)).toBeInTheDocument();
    expect(currentPath()).toBe('/ask/ath_gone');
    expect(localStorage.getItem(LAST_THREAD_KEY)).toBeNull();
  });

  it('a transient failure keeps the stored conversation for a retry', async () => {
    localStorage.setItem(LAST_THREAD_KEY, 'ath_1');
    vi.mocked(getAskThread).mockRejectedValue(new ApiError('Server error', 500, null));
    renderAt('/ask');
    expect(await screen.findByText('Server error')).toBeInTheDocument();
    expect(currentPath()).toBe('/ask/ath_1');
    expect(localStorage.getItem(LAST_THREAD_KEY)).toBe('ath_1');
  });

  it('deleting the open conversation forgets it', async () => {
    vi.mocked(deleteAskThread).mockResolvedValue(undefined);
    renderAt('/ask/ath_1');
    await screen.findByText('Fix the billing retry bug');
    expect(localStorage.getItem(LAST_THREAD_KEY)).toBe('ath_1');
    fireEvent.click(screen.getByRole('button', { name: 'Actions for Billing fix' }));
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Delete' }));
    const dialog = screen.getByText('Delete conversation').closest('.modal-box') as HTMLElement;
    fireEvent.click(within(dialog).getByRole('button', { name: 'Delete' }));
    await waitFor(() => expect(currentPath()).toBe('/ask'));
    await settle();
    expect(currentPath()).toBe('/ask');
    expect(localStorage.getItem(LAST_THREAD_KEY)).toBeNull();
  });

  it('bulk deleting the stored conversation forgets it', async () => {
    vi.mocked(api.deleteAskThreads).mockResolvedValue({ status: 'Deleted', deleted: 1, skipped: [], deletedIds: ['ath_1'] } as never);
    localStorage.setItem(LAST_THREAD_KEY, 'ath_1');
    renderAt({ pathname: '/ask', state: { newConversation: true } });
    await screen.findByText('Checkout tests');
    fireEvent.click(screen.getByRole('checkbox', { name: 'Select Billing fix' }));
    fireEvent.click(screen.getByRole('button', { name: 'Delete (1)' }));
    const dialog = document.querySelector('.modal-box') as HTMLElement;
    fireEvent.click(within(dialog).getByRole('button', { name: 'Delete' }));
    await waitFor(() => expect(api.deleteAskThreads).toHaveBeenCalledWith(['ath_1']));
    await waitFor(() => expect(localStorage.getItem(LAST_THREAD_KEY)).toBeNull());
  });

  it('archiving the open conversation while archived ones are hidden forgets it', async () => {
    vi.mocked(api.updateAskThread).mockImplementation(async (id, data) => ({ ...threads.find((t) => t.id === id)!, ...data } as AskThread));
    renderAt('/ask/ath_1');
    await screen.findByText('Fix the billing retry bug');
    expect(localStorage.getItem(LAST_THREAD_KEY)).toBe('ath_1');
    fireEvent.click(screen.getByRole('button', { name: 'Actions for Billing fix' }));
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Archive' }));
    await waitFor(() => expect(api.updateAskThread).toHaveBeenCalledWith('ath_1', { archived: true }));
    await waitFor(() => expect(localStorage.getItem(LAST_THREAD_KEY)).toBeNull());
  });

  it('keeps working when localStorage throws', async () => {
    const fail = () => { throw new Error('storage disabled'); };
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(fail);
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(fail);
    vi.spyOn(Storage.prototype, 'removeItem').mockImplementation(fail);
    const first = renderAt('/ask/ath_1');
    expect(await screen.findByText('Fix the billing retry bug')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'New conversation' }));
    await waitFor(() => expect(currentPath()).toBe('/ask'));
    first.unmount();
    renderAt('/ask');
    await screen.findByText('Checkout tests');
    await settle();
    expect(currentPath()).toBe('/ask');
    expect(screen.getByRole('heading', { name: 'New conversation' })).toBeInTheDocument();
  });
});

describe('AskArmada unsent drafts across visits', () => {
  const DRAFTS_KEY = 'armada_ask_drafts:usr_1';

  function composer(): HTMLTextAreaElement {
    return screen.getByRole('combobox', { name: 'Message' }) as HTMLTextAreaElement;
  }

  it('keeps an unsent draft when the page is left and restores it on the next visit', async () => {
    const first = renderAt('/ask/ath_1');
    await screen.findByText('Fix the billing retry bug');
    fireEvent.change(composer(), { target: { value: 'half-written question' } });
    expect(JSON.parse(localStorage.getItem(DRAFTS_KEY) ?? '{}')).toEqual({ ath_1: 'half-written question' });
    first.unmount();

    renderAt('/ask/ath_1');
    await screen.findByText('Fix the billing retry bug');
    expect(composer().value).toBe('half-written question');
  });

  it('restores the draft when the nav reopens the last conversation from /ask', async () => {
    const first = renderAt('/ask/ath_1');
    await screen.findByText('Fix the billing retry bug');
    fireEvent.change(composer(), { target: { value: 'come back to this' } });
    first.unmount();

    renderAt('/ask');
    await waitFor(() => expect(currentPath()).toBe('/ask/ath_1'));
    await screen.findByText('Fix the billing retry bug');
    await waitFor(() => expect(composer().value).toBe('come back to this'));
  });

  it('a draft belongs to its own conversation only', async () => {
    localStorage.setItem(DRAFTS_KEY, JSON.stringify({ ath_2: 'for the other one' }));
    renderAt('/ask/ath_1');
    await screen.findByText('Fix the billing retry bug');
    expect(composer().value).toBe('');
  });

  it('clearing the text removes the stored draft', async () => {
    renderAt('/ask/ath_1');
    await screen.findByText('Fix the billing retry bug');
    fireEvent.change(composer(), { target: { value: 'something' } });
    fireEvent.change(composer(), { target: { value: '' } });
    expect(localStorage.getItem(DRAFTS_KEY)).toBeNull();
  });

  it('sending the message removes the stored draft', async () => {
    vi.mocked(sendAskMessage).mockResolvedValue({ messageId: 'amg_20', turnId: 'turn_1' });
    renderAt('/ask/ath_1');
    await screen.findByText('Fix the billing retry bug');
    fireEvent.change(composer(), { target: { value: 'How is it going?' } });
    fireEvent.keyDown(composer(), { key: 'Enter' });
    await waitFor(() => expect(sendAskMessage).toHaveBeenCalledWith('ath_1', 'How is it going?', false));
    expect(localStorage.getItem(DRAFTS_KEY)).toBeNull();
    expect(composer().value).toBe('');
  });

  it('deleting a conversation removes its stored draft', async () => {
    vi.mocked(deleteAskThread).mockResolvedValue(undefined);
    localStorage.setItem(DRAFTS_KEY, JSON.stringify({ ath_3: 'never sent', ath_2: 'keep me' }));
    renderAt('/ask');
    await screen.findByText('Checkout tests');
    fireEvent.click(screen.getByRole('button', { name: 'Actions for Checkout tests' }));
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Delete' }));
    const dialog = screen.getByText('Delete conversation').closest('.modal-box') as HTMLElement;
    fireEvent.click(within(dialog).getByRole('button', { name: 'Delete' }));
    await waitFor(() => expect(deleteAskThread).toHaveBeenCalledWith('ath_3'));
    await waitFor(() => expect(JSON.parse(localStorage.getItem(DRAFTS_KEY) ?? '{}')).toEqual({ ath_2: 'keep me' }));
  });

  it('ignores a corrupt stored drafts value', async () => {
    localStorage.setItem(DRAFTS_KEY, '{not json');
    renderAt('/ask/ath_1');
    await screen.findByText('Fix the billing retry bug');
    expect(composer().value).toBe('');
    fireEvent.change(composer(), { target: { value: 'fresh' } });
    expect(JSON.parse(localStorage.getItem(DRAFTS_KEY) ?? '{}')).toEqual({ ath_1: 'fresh' });
  });
});

describe('AskArmada mark read and unread', () => {
  it('Mark unread on another conversation calls the server and shows an unread badge', async () => {
    vi.mocked(markAskThreadUnread).mockResolvedValue({ ...threads[2], unreadCount: 1 });
    renderAt('/ask/ath_1');
    await screen.findByText('Fix the billing retry bug');
    fireEvent.click(screen.getByRole('button', { name: 'Actions for Checkout tests' }));
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Mark unread' }));
    await waitFor(() => expect(markAskThreadUnread).toHaveBeenCalledWith('ath_3'));
    const row = threadRows().find((r) => r.getAttribute('title') === 'Checkout tests')!;
    await waitFor(() => expect(within(row).getByLabelText('1 unread')).toBeInTheDocument());
    expect(currentPath()).toBe('/ask/ath_1');
  });

  it('Mark unread on the open conversation closes it, so it is not marked read again', async () => {
    vi.mocked(markAskThreadUnread).mockResolvedValue({ ...threads[0], unreadCount: 1 });
    renderAt('/ask/ath_1');
    await screen.findByText('Fix the billing retry bug');
    expect(localStorage.getItem(LAST_THREAD_KEY)).toBe('ath_1');
    vi.mocked(markAskThreadRead).mockClear();
    fireEvent.click(screen.getByRole('button', { name: 'Actions for Billing fix' }));
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Mark unread' }));
    await waitFor(() => expect(markAskThreadUnread).toHaveBeenCalledWith('ath_1'));
    await waitFor(() => expect(currentPath()).toBe('/ask'));
    expect(localStorage.getItem(LAST_THREAD_KEY)).toBeNull();
    const row = threadRows().find((r) => r.getAttribute('title') === 'Billing fix')!;
    await waitFor(() => expect(within(row).getByLabelText('1 unread')).toBeInTheDocument());
    emit('ask.thread', { threadId: 'ath_1', thread: { ...threads[0], unreadCount: 1 } });
    expect(markAskThreadRead).not.toHaveBeenCalled();
  });

  it('Mark unread on the open conversation survives an ask.thread event that arrives before the page re-renders', async () => {
    // The server emits ask.thread from MarkUnreadAsync; deliver it synchronously, before React re-renders after the navigation.
    vi.mocked(markAskThreadUnread).mockImplementation(async () => {
      handlers.forEach((h) => h({ type: 'ask.thread', data: { threadId: 'ath_1', thread: { ...threads[0], unreadCount: 1 } } }));
      return { ...threads[0], unreadCount: 1 };
    });
    renderAt('/ask/ath_1');
    await screen.findByText('Fix the billing retry bug');
    vi.mocked(markAskThreadRead).mockClear();
    fireEvent.click(screen.getByRole('button', { name: 'Actions for Billing fix' }));
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Mark unread' }));
    await waitFor(() => expect(markAskThreadUnread).toHaveBeenCalledTimes(1));
    await waitFor(() => expect(currentPath()).toBe('/ask'));
    const row = threadRows().find((r) => r.getAttribute('title') === 'Billing fix')!;
    await waitFor(() => expect(within(row).getByLabelText('1 unread')).toBeInTheDocument());
    expect(markAskThreadRead).not.toHaveBeenCalled();
    // A list refresh after closing keeps the server's unread count too.
    emit('ask.thread', { threadId: 'ath_1', thread: { ...threads[0], unreadCount: 1 } });
    expect(within(row).getByLabelText('1 unread')).toBeInTheDocument();
    expect(markAskThreadRead).not.toHaveBeenCalled();
  });

  it('reopening a conversation marked unread marks it read again', async () => {
    vi.mocked(markAskThreadUnread).mockResolvedValue({ ...threads[0], unreadCount: 1 });
    renderAt('/ask/ath_1');
    await screen.findByText('Fix the billing retry bug');
    fireEvent.click(screen.getByRole('button', { name: 'Actions for Billing fix' }));
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Mark unread' }));
    await waitFor(() => expect(currentPath()).toBe('/ask'));
    const row = threadRows().find((r) => r.getAttribute('title') === 'Billing fix')!;
    await waitFor(() => expect(within(row).getByLabelText('1 unread')).toBeInTheDocument());
    vi.mocked(markAskThreadRead).mockClear();
    fireEvent.click(row);
    await waitFor(() => expect(currentPath()).toBe('/ask/ath_1'));
    await waitFor(() => expect(markAskThreadRead).toHaveBeenCalledWith('ath_1'));
  });

  it('Mark read clears the badge and calls the server', async () => {
    renderAt('/ask');
    await screen.findByText('Dependency sweep');
    expect(screen.getByLabelText('3 unread')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Actions for Dependency sweep' }));
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Mark read' }));
    await waitFor(() => expect(markAskThreadRead).toHaveBeenCalledWith('ath_2'));
    expect(screen.queryByLabelText('3 unread')).not.toBeInTheDocument();
  });

  it('a failed Mark unread shows an error and reloads the list', async () => {
    vi.mocked(markAskThreadUnread).mockRejectedValue(new Error('server down'));
    renderAt('/ask');
    await screen.findByText('Checkout tests');
    vi.mocked(enumerateAskThreads).mockClear();
    fireEvent.click(screen.getByRole('button', { name: 'Actions for Checkout tests' }));
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Mark unread' }));
    expect(await screen.findByText('server down')).toBeInTheDocument();
    await waitFor(() => expect(enumerateAskThreads).toHaveBeenCalled());
  });

  it('a failed Mark read shows an error and reloads the list', async () => {
    vi.mocked(markAskThreadRead).mockRejectedValue(new Error('read failed'));
    renderAt('/ask');
    await screen.findByText('Dependency sweep');
    vi.mocked(enumerateAskThreads).mockClear();
    fireEvent.click(screen.getByRole('button', { name: 'Actions for Dependency sweep' }));
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Mark read' }));
    expect(await screen.findByText('read failed')).toBeInTheDocument();
    await waitFor(() => expect(enumerateAskThreads).toHaveBeenCalled());
  });
});

describe('AskArmada conversation cards survive navigating away and back', () => {
  function chipNamed(name: string): HTMLElement[] {
    return Array.from(document.querySelectorAll<HTMLElement>('details.chat-tool')).filter((d) => d.querySelector('.chat-tool-name')?.textContent === name);
  }

  it('a turn still running restores its tool calls (expandable), text, and thinking, without duplicates when it completes', async () => {
    vi.mocked(sendAskMessage).mockResolvedValue({ messageId: 'amg_20', turnId: 'turn_live' });
    const first = renderAt('/ask/ath_1');
    await screen.findByText('Fix the billing retry bug');
    const input = screen.getByRole('combobox', { name: 'Message' });
    fireEvent.change(input, { target: { value: 'How is it going?' } });
    fireEvent.keyDown(input, { key: 'Enter' });
    await waitFor(() => expect(sendAskMessage).toHaveBeenCalled());
    emit('ask.tool', { threadId: 'ath_1', turnId: 'turn_live', phase: 'started', id: 'c9', name: 'mcp__armada__status', arguments: '{"verbose":true}' });
    emit('ask.tool', { threadId: 'ath_1', turnId: 'turn_live', phase: 'completed', id: 'c9', name: 'mcp__armada__status', ok: true, result: '{"health":"ok"}', elapsedMs: 40 });
    emit('ask.chunk', { threadId: 'ath_1', turnId: 'turn_live', delta: 'Two of four ' });
    expect(chipNamed('mcp__armada__status')).toHaveLength(1);
    first.unmount();

    // Back on the page while the turn is still running: the reply is not persisted yet, the detail carries its progress.
    const user: AskMessage = { id: 'amg_20', threadId: 'ath_1', sequence: 9, role: 'User', kind: 'Text', contentText: 'How is it going?' };
    const reservedReply: AskMessage = { id: 'amg_21', threadId: 'ath_1', sequence: 10, role: 'Assistant', kind: 'Text', contentText: '', toolCalls: [] };
    const liveCall = { id: 'atc_9', callId: 'c9', toolName: 'mcp__armada__status', argumentsText: '{"verbose":true}', resultText: '{"health":"ok"}', ok: true, elapsedMs: 40 };
    vi.mocked(enumerateAskMessages).mockResolvedValue({ messages: [...messages, user, reservedReply], hasMore: false });
    vi.mocked(getAskThread).mockResolvedValue({
      ...detail,
      thread: { ...threads[0], activeTurnId: 'turn_live' },
      activeTurn: { turnId: 'turn_live', messageId: 'amg_21', text: 'Two of four ', thinkingText: 'Counting missions.', toolCalls: [liveCall] },
    });
    renderAt('/ask');
    await waitFor(() => expect(currentPath()).toBe('/ask/ath_1'));
    await waitFor(() => expect(chipNamed('mcp__armada__status')).toHaveLength(1));
    const chip = chipNamed('mcp__armada__status')[0];
    expect(chip).toHaveClass('chat-tool-success');
    fireEvent.click(chip.querySelector('summary')!);
    expect(chip).toHaveAttribute('open');
    expect(chip.querySelector('.chat-tool-detail')!.textContent).toContain('"verbose": true');
    expect(chip.querySelector('.chat-tool-detail')!.textContent).toContain('"health": "ok"');
    expect(screen.getByText('Two of four')).toBeInTheDocument();
    expect(screen.getByText('Counting missions.')).toBeInTheDocument();
    // The other cards are back too.
    expect(screen.getByText('enumerate')).toBeInTheDocument();
    expect(screen.getByText('Dispatch voyage "Billing fix"')).toBeInTheDocument();
    expect(screen.getByRole('region', { name: 'Voyage: Billing fix voyage' })).toBeInTheDocument();

    // A tool event the page sees after returning updates the same card; the persisted reply then replaces the stream.
    emit('ask.tool', { threadId: 'ath_1', turnId: 'turn_live', phase: 'completed', id: 'c9', name: 'mcp__armada__status', ok: true, result: '{"health":"ok"}', elapsedMs: 40 });
    expect(chipNamed('mcp__armada__status')).toHaveLength(1);
    const finalReply: AskMessage = { ...reservedReply, contentText: 'Two of four missions are done.', toolCalls: [liveCall] };
    vi.mocked(enumerateAskMessages).mockResolvedValue({ messages: [...messages, user, finalReply], hasMore: false });
    emit('ask.message', { threadId: 'ath_1', message: finalReply });
    emit('ask.turn', { threadId: 'ath_1', turnId: 'turn_live', state: 'completed', messageId: 'amg_21' });
    expect(await screen.findByText('Two of four missions are done.')).toBeInTheDocument();
    await waitFor(() => expect(document.querySelector('.is-streaming')).toBeNull());
    expect(chipNamed('mcp__armada__status')).toHaveLength(1);
  });

  it('switching to another conversation and back shows the persisted tool call, confirm, and work cards again', async () => {
    const other: AskMessage[] = [{ id: 'amg_90', threadId: 'ath_2', sequence: 1, role: 'User', kind: 'Text', contentText: 'Sweep the dependencies' }];
    vi.mocked(getAskThread).mockImplementation(async (id: string) => (id === 'ath_2' ? { thread: threads[1], trackedWork: [], pendingProposals: [] } : detail));
    vi.mocked(enumerateAskMessages).mockImplementation(async (id: string) => ({ messages: id === 'ath_2' ? other : messages, hasMore: false }));
    renderAt('/ask/ath_1');
    await screen.findByText('Fix the billing retry bug');
    expect(chipNamed('enumerate')).toHaveLength(1);

    fireEvent.click(within(threadRows()[1]).getByText('Dependency sweep'));
    await screen.findByText('Sweep the dependencies');
    expect(chipNamed('enumerate')).toHaveLength(0);

    fireEvent.click(within(threadRows()[0]).getByText('Billing fix'));
    await screen.findByText('Fix the billing retry bug');
    expect(chipNamed('enumerate')).toHaveLength(1);
    const chip = chipNamed('enumerate')[0];
    fireEvent.click(chip.querySelector('summary')!);
    expect(chip).toHaveAttribute('open');
    expect(screen.getAllByText('Dispatch voyage "Billing fix"')).toHaveLength(1);
    expect(screen.getByText('Approval needed')).toBeInTheDocument();
    expect(screen.getByRole('region', { name: 'Voyage: Billing fix voyage' })).toBeInTheDocument();
    expect(screen.getByText('Captain runtime crashed.')).toBeInTheDocument();
  });
});

describe('AskArmada black box', () => {
  const BLACKBOX_ACTION = { name: 'blackbox', command: '/blackbox', toolName: 'get_blackbox', title: 'Black box of a failure', description: 'Show the black box of a failed mission here, then ask the captain your question about it.', readOnly: true };

  function SearchProbe() {
    const location = useLocation();
    return <output data-testid="search">{location.search}</output>;
  }

  async function settle() {
    await act(async () => { await new Promise((resolve) => setTimeout(resolve, 50)); });
  }

  it('pre-fills a new conversation from ?prompt= and clears it from the URL', async () => {
    localStorage.setItem(LAST_THREAD_KEY, 'ath_1');
    const prompt = 'Show me the pertinent details from the black box for mission msn_9 and give me your opinion on why it failed and what could have been done differently';
    render(
      <MemoryRouter initialEntries={[`/ask?prompt=${encodeURIComponent(prompt)}`]}>
        <Routes>
          <Route path="/ask/:threadId?" element={<AskArmada />} />
        </Routes>
        <LocationProbe />
        <SearchProbe />
      </MemoryRouter>,
    );
    await screen.findByText('Checkout tests');
    await waitFor(() => expect((screen.getByRole('combobox', { name: 'Message' }) as HTMLTextAreaElement).value).toBe(prompt));
    await settle();
    expect(currentPath()).toBe('/ask');
    expect(screen.getByTestId('search').textContent).toBe('');
    expect(getAskThread).not.toHaveBeenCalled();
    expect(sendAskMessage).not.toHaveBeenCalled();
  });

  it('/blackbox with a mission id runs get_blackbox, then sends the question to the captain', async () => {
    vi.mocked(api.getAskQuickActions).mockResolvedValue([BLACKBOX_ACTION]);
    vi.mocked(runAskQuickAction).mockResolvedValue({ id: 'aap_bb', threadId: 'ath_1', toolName: 'get_blackbox', source: 'QuickAction', status: 'Executed' });
    vi.mocked(sendAskMessage).mockResolvedValue({ messageId: 'amg_30', turnId: 'turn_bb' });
    renderAt('/ask/ath_1');
    await screen.findByText('Fix the billing retry bug');
    const input = screen.getByRole('combobox', { name: 'Message' }) as HTMLTextAreaElement;
    fireEvent.change(input, { target: { value: '/' } });
    await waitFor(() => expect(within(screen.getByRole('listbox', { name: 'Commands' })).getByText('/blackbox')).toBeInTheDocument());
    fireEvent.change(input, { target: { value: '/blackbox msn_77 why did it fail?' } });
    fireEvent.keyDown(input, { key: 'Enter' });
    await waitFor(() => expect(runAskQuickAction).toHaveBeenCalledWith('ath_1', 'get_blackbox', { missionId: 'msn_77', question: 'why did it fail?' }));
    await waitFor(() => expect(sendAskMessage).toHaveBeenCalledWith('ath_1', 'why did it fail?', false));
  });

  it('/blackbox without a mission id opens its form', async () => {
    vi.mocked(api.getAskQuickActions).mockResolvedValue([BLACKBOX_ACTION]);
    vi.mocked(api.listBlackBox).mockResolvedValue(page([{ id: 'bbx_1', missionId: 'msn_5', missionTitle: 'Add retry' } as never]));
    vi.mocked(runAskQuickAction).mockResolvedValue({ id: 'aap_bb', threadId: 'ath_1', toolName: 'get_blackbox', source: 'QuickAction', status: 'Executed' });
    renderAt('/ask/ath_1');
    await screen.findByText('Fix the billing retry bug');
    const input = screen.getByRole('combobox', { name: 'Message' }) as HTMLTextAreaElement;
    fireEvent.change(input, { target: { value: '/' } });
    await waitFor(() => expect(within(screen.getByRole('listbox', { name: 'Commands' })).getByText('/blackbox')).toBeInTheDocument());
    fireEvent.change(input, { target: { value: '/blackbox' } });
    fireEvent.keyDown(input, { key: 'Enter' });
    const form = await screen.findByRole('form', { name: 'Black box of a failure' });
    fireEvent.click(within(form).getByRole('button', { name: 'Show black box' }));
    expect(within(form).getByText('Choose a failed mission or type its id (msn_...).')).toBeInTheDocument();
    expect(runAskQuickAction).not.toHaveBeenCalled();
    fireEvent.change(within(form).getByRole('combobox'), { target: { value: 'msn_5' } });
    fireEvent.click(within(form).getByRole('button', { name: 'Show black box' }));
    await waitFor(() => expect(runAskQuickAction).toHaveBeenCalledWith('ath_1', 'get_blackbox', { missionId: 'msn_5' }));
    expect(sendAskMessage).not.toHaveBeenCalled();
  });

  it('a black box card proposes Fix it through the actions endpoint (a Pending proposal, nothing runs)', async () => {
    const bbWork = { id: 'atw_bb', threadId: 'ath_1', entityType: 'BlackBox', entityId: 'bbx_1', title: 'Add retry', status: 'LandingFailed', state: 'Failed',
      snapshot: { entityType: 'BlackBox', entityId: 'bbx_1', status: 'LandingFailed', state: 'Failed', missions: [], targets: [], blackBox: {
        digest: { entryId: 'bbx_1', missionId: 'msn_1', recoveryPaths: [{ id: 'p1', rank: 1, title: 'Rebase and retry' }] },
        followUps: [{ name: 'fix', label: 'Fix it', toolName: 'run_blackbox_recovery', argumentsText: '{"entryId":"bbx_1","pathId":"p1"}' }],
      } } };
    vi.mocked(getAskThread).mockResolvedValue({ ...detail, trackedWork: [...(detail.trackedWork ?? []), bbWork] } as never);
    vi.mocked(enumerateAskMessages).mockResolvedValue({ messages: [...messages, { id: 'amg_bb', threadId: 'ath_1', sequence: 9, role: 'System', kind: 'WorkUpdate', trackedWorkId: 'atw_bb', contentText: 'Black box of mission msn_1.' }], hasMore: false });
    vi.mocked(runAskQuickAction).mockResolvedValue({ id: 'aap_fix', threadId: 'ath_1', toolName: 'run_blackbox_recovery', summaryText: 'Run recovery path p1', source: 'QuickAction', status: 'Pending' });
    renderAt('/ask/ath_1');
    const followUps = await screen.findByRole('group', { name: 'Follow-ups' });
    fireEvent.click(within(followUps).getByRole('button', { name: 'Fix it' }));
    await waitFor(() => expect(runAskQuickAction).toHaveBeenCalledWith('ath_1', 'run_blackbox_recovery', { entryId: 'bbx_1', pathId: 'p1' }, { propose: true }));
    expect(approveAskProposal).not.toHaveBeenCalled();
  });
});
