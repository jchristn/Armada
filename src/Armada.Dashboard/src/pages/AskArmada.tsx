import { useCallback, useEffect, useMemo, useReducer, useRef, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import {
  ApiError,
  approveAskProposal,
  cancelAskTurn,
  createAskThread,
  deleteAskThread,
  enumerateAskMessages,
  enumerateAskThreads,
  getAskQuickActions,
  getAskThread,
  getAskWorkSnapshot,
  getCaptainTools,
  listCaptains,
  markAskThreadRead,
  rejectAskProposal,
  runAskQuickAction,
  sendAskMessage,
  setAskThreadCliPermissionPolicy,
  summarizeAskThread,
  updateAskThread,
} from '../api/client';
import type {
  AskActionProposal,
  AskMessage,
  AskQuickAction,
  AskThread,
  AskThreadUpdateRequest,
  AskTrackedWork,
  Captain,
  CaptainToolAccessResult,
  CliPermissionPolicy,
  CliPermissionRequest,
  WebSocketMessage,
} from '../types/models';
import { useLocale } from '../context/LocaleContext';
import { useWebSocket } from '../context/WebSocketContext';
import { useAuth } from '../context/AuthContext';
import { useNotifications } from '../context/NotificationContext';
import ErrorModal from '../components/shared/ErrorModal';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import { ErrorState, LoadingState } from '../components/shared/StateBlocks';
import ImportWizard from '../components/vessels/import/ImportWizard';
import AskThreadList from '../components/ask/AskThreadList';
import AskConversationHeader, { CAPTAIN_SELECT_ID } from '../components/ask/AskConversationHeader';
import AskWorkStrip from '../components/ask/AskWorkStrip';
import AskMessageList, { type AskMessageListHandle } from '../components/ask/AskMessageList';
import AskComposer, { type AskComposerHandle } from '../components/ask/AskComposer';
import { randomThinkingMessage } from '../lib/askThinkingMessages';
import { randomGreeting } from '../lib/askGreetings';
import { parseAskEvent } from '../lib/askEvents';
import { conversationReducer, initialConversation, isLocalMessage } from '../lib/askConversation';
import { applyActivityEvent, applyThreadUpdate, sortThreads, type ThreadActivityMap, type ThreadListFilter } from '../lib/askThreads';
import { DEFAULT_QUICK_ACTIONS, mergeQuickActions } from '../lib/askQuickActions';
import { runLocalCommand, type AskCommandOutcome, type AskLocalCommand } from '../lib/askCommands';
import { isWorkActive, workRoute } from '../lib/askWork';
import { useFocusTrap } from '../lib/useFocusTrap';
import { askCaptainAccess, instructionsDocUrl } from '../lib/askCaptain';
import { fallbackReasonText, isPendingRequest, parseCliPermissionEvent } from '../lib/cliPermissions';

const CAPTAIN_STORAGE_KEY = 'armada_ask_captain';
const THINKING_STORAGE_KEY = 'armada_ask_show_thinking';
const THREAD_PAGE_SIZE = 50;
const MESSAGE_PAGE_SIZE = 30;
const SNAPSHOT_FETCH_LIMIT = 20;

function readStored(key: string): string {
  try { return localStorage.getItem(key) ?? ''; } catch { return ''; }
}

function writeStored(key: string, value: string) {
  try { localStorage.setItem(key, value); } catch { /* storage unavailable */ }
}

function errorText(err: unknown, fallback: string): string {
  return err instanceof Error && err.message ? err.message : fallback;
}

/**
 * Ask Armada home base: a list of saved conversations and the open conversation. Conversations stream captain
 * replies, show confirm cards for state-changing actions, and follow the work they start with live cards and
 * milestone messages, all driven by owner-scoped `ask.*` WebSocket events.
 */
export default function AskArmada() {
  const params = useParams();
  const routeThreadId = params.threadId ?? null;
  const navigate = useNavigate();
  const { t } = useLocale();
  const { subscribe, reconnectCount } = useWebSocket();
  const { user, isAdmin, isTenantAdmin } = useAuth();
  const { pushToast } = useNotifications();

  // Captains and quick actions.
  const [captains, setCaptains] = useState<Captain[]>([]);
  const [draftCaptainId, setDraftCaptainId] = useState(() => readStored(CAPTAIN_STORAGE_KEY));
  const [quickActions, setQuickActions] = useState<AskQuickAction[]>(DEFAULT_QUICK_ACTIONS);
  const [tools, setTools] = useState<CaptainToolAccessResult | null>(null);
  const toolsCache = useRef<Record<string, CaptainToolAccessResult>>({});

  // Thread list.
  const [threads, setThreads] = useState<AskThread[]>([]);
  const [listLoading, setListLoading] = useState(true);
  const [listError, setListError] = useState<string | null>(null);
  const [search, setSearch] = useState('');
  const [query, setQuery] = useState('');
  const [includeArchived, setIncludeArchived] = useState(false);
  const [listPage, setListPage] = useState(1);
  const [listHasMore, setListHasMore] = useState(false);
  const [activity, setActivity] = useState<ThreadActivityMap>({});
  const [drawerOpen, setDrawerOpen] = useState(false);
  const filterRef = useRef<ThreadListFilter>({ search: '', includeArchived: false });
  filterRef.current = { search: query, includeArchived };

  // Open conversation.
  const [conv, dispatch] = useReducer(conversationReducer, routeThreadId, initialConversation);
  const convRef = useRef(conv);
  convRef.current = conv;
  const [convLoading, setConvLoading] = useState(false);
  const [convError, setConvError] = useState<string | null>(null);
  const [loadingOlder, setLoadingOlder] = useState(false);
  const [busyProposalId, setBusyProposalId] = useState<string | null>(null);
  const [actionBusy, setActionBusy] = useState(false);
  const [stopping, setStopping] = useState(false);
  const [showThinking, setShowThinking] = useState(() => readStored(THINKING_STORAGE_KEY) === 'true');
  const [waitingText, setWaitingText] = useState('');
  const [highlightedWorkId, setHighlightedWorkId] = useState<string | null>(null);

  // Dialogs and announcements.
  const [error, setError] = useState('');
  const [deleteTarget, setDeleteTarget] = useState<AskThread | null>(null);
  const [importOpen, setImportOpen] = useState(false);
  const [announcement, setAnnouncement] = useState('');
  const [greeting] = useState(() => randomGreeting());
  const messageListRef = useRef<AskMessageListHandle>(null);
  const composerRef = useRef<AskComposerHandle>(null);
  // Each saved conversation keeps its own unsent draft; a new conversation always starts empty.
  const draftsRef = useRef<Record<string, string>>({});
  const drawerRef = useRef<HTMLDivElement>(null);
  // On narrow screens the list is a modal drawer: keep focus inside it and close it with Escape.
  useFocusTrap(drawerRef, drawerOpen, () => setDrawerOpen(false));

  const thread = conv.thread;
  const activeCaptainId = thread ? (thread.captainId ?? '') : draftCaptainId;
  const activeCaptain = captains.find((c) => c.id === activeCaptainId) ?? null;
  const captainNames = useMemo(() => Object.fromEntries(captains.map((c) => [c.id, c.name])), [captains]);

  // ---------------------------------------------------------------- loading

  useEffect(() => {
    listCaptains({ pageSize: 200 })
      .then((result) => {
        const list = result.objects || [];
        setCaptains(list);
        setDraftCaptainId((current) => (current && list.some((c) => c.id === current) ? current : list[0]?.id ?? ''));
      })
      .catch(() => setCaptains([]));
    getAskQuickActions()
      .then((catalog) => setQuickActions(mergeQuickActions(catalog)))
      .catch(() => setQuickActions(DEFAULT_QUICK_ACTIONS));
  }, []);

  // Whether the conversation's captain can reach Armada over MCP (it cannot propose actions otherwise).
  useEffect(() => {
    if (!activeCaptainId) { setTools(null); return undefined; }
    const cached = toolsCache.current[activeCaptainId];
    if (cached) { setTools(cached); return undefined; }
    let active = true;
    setTools(null);
    getCaptainTools(activeCaptainId)
      .then((result) => { toolsCache.current[activeCaptainId] = result; if (active) setTools(result); })
      .catch(() => { if (active) setTools(null); });
    return () => { active = false; };
  }, [activeCaptainId]);

  useEffect(() => {
    const handle = window.setTimeout(() => setQuery(search.trim()), 300);
    return () => window.clearTimeout(handle);
  }, [search]);

  const loadThreads = useCallback(async (page: number) => {
    setListLoading(true);
    setListError(null);
    try {
      const result = await enumerateAskThreads({ pageNumber: page, pageSize: THREAD_PAGE_SIZE, search: filterRef.current.search, includeArchived: filterRef.current.includeArchived });
      const openId = convRef.current.threadId;
      const incoming = (result.objects || []).map((th) => (th.id === openId ? { ...th, unreadCount: 0 } : th));
      setThreads((prev) => (page === 1 ? incoming : sortThreads([...prev.filter((p) => !incoming.some((i) => i.id === p.id)), ...incoming])));
      setListPage(page);
      setListHasMore(page < (result.totalPages || 1));
    } catch (err: unknown) {
      setListError(errorText(err, t('Failed to load conversations.')));
    } finally {
      setListLoading(false);
    }
  }, [t]);

  useEffect(() => { void loadThreads(1); }, [query, includeArchived, reconnectCount, loadThreads]);

  const markRead = useCallback((id: string) => {
    if (typeof document !== 'undefined' && document.visibilityState === 'hidden') return;
    setThreads((prev) => prev.map((th) => (th.id === id && th.unreadCount ? { ...th, unreadCount: 0 } : th)));
    markAskThreadRead(id).catch(() => { /* best effort; the badge resyncs on the next ask.thread */ });
  }, []);

  const fetchSnapshots = useCallback((id: string, work: AskTrackedWork[]) => {
    const missing = work.filter((w) => !convRef.current.snapshots[w.id] && !w.snapshot);
    missing.sort((a, b) => Number(isWorkActive(b)) - Number(isWorkActive(a)));
    for (const item of missing.slice(0, SNAPSHOT_FETCH_LIMIT)) {
      getAskWorkSnapshot(id, item.id)
        .then((snapshot) => { if (snapshot && convRef.current.threadId === id) dispatch({ type: 'snapshot', trackedWorkId: item.id, snapshot }); })
        .catch(() => { /* the card shows its loading state until an ask.work event arrives */ });
    }
  }, []);

  const loadConversation = useCallback(async (id: string, silent = false) => {
    if (!silent) setConvLoading(true);
    setConvError(null);
    try {
      const [detail, page] = await Promise.all([
        getAskThread(id),
        enumerateAskMessages(id, { pageSize: MESSAGE_PAGE_SIZE }),
      ]);
      if (convRef.current.threadId !== id) return;
      dispatch({ type: 'loaded', threadId: id, detail, messages: page?.messages ?? [], hasMore: !!page?.hasMore });
      if (detail?.thread) setThreads((prev) => applyThreadUpdate(prev, detail.thread, filterRef.current, id));
      markRead(id);
      fetchSnapshots(id, detail?.trackedWork ?? []);
    } catch (err: unknown) {
      if (convRef.current.threadId !== id) return;
      if (err instanceof ApiError && err.status === 404) setConvError(t('This conversation was not found. It may have been deleted.'));
      else setConvError(errorText(err, t('Failed to load the conversation.')));
    } finally {
      if (convRef.current.threadId === id) setConvLoading(false);
    }
  }, [fetchSnapshots, markRead, t]);

  const refreshLatest = useCallback(async (id: string) => {
    try {
      const page = await enumerateAskMessages(id, { pageSize: MESSAGE_PAGE_SIZE });
      dispatch({ type: 'latest', threadId: id, messages: page?.messages ?? [] });
    } catch { /* the socket will deliver the messages; this is only reconciliation */ }
  }, []);

  const refreshDetail = useCallback(async (id: string) => {
    try {
      const detail = await getAskThread(id);
      dispatch({ type: 'detail', threadId: id, detail });
      fetchSnapshots(id, detail?.trackedWork ?? []);
    } catch { /* reconciliation only */ }
  }, [fetchSnapshots]);

  // Route changes open a different conversation (or the new-conversation screen).
  useEffect(() => {
    setDrawerOpen(false);
    const previousId = convRef.current.threadId;
    // A conversation just created by a send or quick action already holds this id: its composer state carries over.
    if (previousId !== routeThreadId) {
      if (previousId) draftsRef.current[previousId] = composerRef.current?.getText() ?? '';
      composerRef.current?.reset({ text: routeThreadId ? draftsRef.current[routeThreadId] ?? '' : '', focus: !routeThreadId });
      dispatch({ type: 'reset', threadId: routeThreadId });
    }
    setConvError(null);
    setStopping(false);
    if (routeThreadId) void loadConversation(routeThreadId);
    else setConvLoading(false);
  }, [routeThreadId, loadConversation]);

  // After a reconnect, refetch the open conversation so nothing missed while offline is lost.
  useEffect(() => {
    if (reconnectCount === 0) return;
    const id = convRef.current.threadId;
    if (id) void loadConversation(id, true);
  }, [reconnectCount, loadConversation]);

  // Mark the open conversation read when the tab becomes visible again.
  useEffect(() => {
    const onVisible = () => {
      const id = convRef.current.threadId;
      if (id && document.visibilityState === 'visible') markRead(id);
    };
    document.addEventListener('visibilitychange', onVisible);
    return () => document.removeEventListener('visibilitychange', onVisible);
  }, [markRead]);

  // Rotate the waiting message while a captain turn has not produced text yet.
  useEffect(() => {
    if (!conv.turnActive) { setStopping(false); return undefined; }
    setWaitingText((prev) => randomThinkingMessage(prev));
    const id = window.setInterval(() => setWaitingText((prev) => randomThinkingMessage(prev)), 4000);
    return () => window.clearInterval(id);
  }, [conv.turnActive]);

  // ---------------------------------------------------------------- socket events

  useEffect(() => {
    return subscribe((msg: WebSocketMessage) => {
      const cli = parseCliPermissionEvent(msg);
      if (cli) {
        if (!cli.request.threadId || cli.request.threadId !== convRef.current.threadId) return;
        dispatch({ type: 'cliPermission', request: cli.request });
        if (cli.type === 'cli_permission.requested' && isPendingRequest(cli.request)) {
          setAnnouncement(t('The captain needs permission to run {{tool}}: {{summary}}', { tool: cli.request.toolName, summary: cli.request.summaryText || cli.request.toolName }));
        }
        return;
      }
      const event = parseAskEvent(msg);
      if (!event) return;
      setActivity((prev) => applyActivityEvent(prev, event));
      const openId = convRef.current.threadId;

      if (event.type === 'ask.thread') {
        setThreads((prev) => applyThreadUpdate(prev, event.thread, filterRef.current, openId));
        if (event.threadId === openId && (event.thread.unreadCount ?? 0) > 0) markRead(event.threadId);
      }
      if (event.threadId !== openId) return;

      const before = convRef.current;
      dispatch({ type: 'event', event });

      if (event.type === 'ask.turn' && event.state !== 'started') {
        void refreshLatest(event.threadId);
      } else if (event.type === 'ask.message' && event.message.role !== 'User') {
        const kind = String(event.message.kind);
        const snippet = (event.message.contentText ?? '').replace(/\s+/g, ' ').slice(0, 160);
        if (kind === 'WorkUpdate') setAnnouncement(t('Work update: {{text}}', { text: snippet }));
        else if (kind === 'WorkReport') setAnnouncement(t('Report: {{text}}', { text: snippet }));
        else if (kind === 'Error') setAnnouncement(t('Error: {{text}}', { text: snippet }));
        else if (kind !== 'ActionProposal' && kind !== 'CliPermission') setAnnouncement(t('New message: {{text}}', { text: snippet }));
      } else if (event.type === 'ask.proposal' && String(event.proposal.status).toLowerCase() === 'pending') {
        setAnnouncement(t('An action needs your approval: {{summary}}', { summary: event.proposal.summaryText || event.proposal.toolName }));
      } else if (event.type === 'ask.work' && event.snapshot) {
        const prior = before.trackedWork.find((w) => w.id === event.trackedWorkId);
        if (!prior || prior.status !== event.snapshot.status) {
          setAnnouncement(t('{{title}} is now {{status}}', { title: prior?.title || event.snapshot.title || event.snapshot.entityId, status: t(event.snapshot.status) }));
        }
        if (!prior) void refreshDetail(event.threadId);
      }
    });
  }, [subscribe, markRead, refreshLatest, refreshDetail, t]);

  // ---------------------------------------------------------------- actions

  const ensureThread = useCallback(async (): Promise<string> => {
    const current = convRef.current.threadId;
    if (current) return current;
    const created = await createAskThread({ captainId: draftCaptainId || null });
    dispatch({ type: 'reset', threadId: created.id });
    dispatch({ type: 'thread', thread: created });
    convRef.current = { ...initialConversation(created.id), thread: created };
    setThreads((prev) => applyThreadUpdate(prev, created, filterRef.current, created.id));
    navigate(`/ask/${encodeURIComponent(created.id)}`);
    return created.id;
  }, [draftCaptainId, navigate]);

  async function send(text: string) {
    let id: string;
    try {
      id = await ensureThread();
    } catch (err: unknown) {
      setError(errorText(err, t('Failed to start a conversation.')));
      return;
    }
    const localId = `local-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 8)}`;
    const maxSeq = convRef.current.messages.reduce((max, m) => Math.max(max, m.sequence), 0);
    const optimistic: AskMessage = {
      id: localId, threadId: id, sequence: maxSeq + 1, role: 'User', kind: 'Text', contentText: text, createdUtc: new Date().toISOString(), isLocal: true,
    };
    dispatch({ type: 'optimisticUser', message: optimistic });
    messageListRef.current?.scrollToBottom();
    try {
      const result = await sendAskMessage(id, text, showThinking);
      dispatch({ type: 'confirmUser', localId, messageId: result?.messageId ?? null, turnId: result?.turnId ?? null });
    } catch (err: unknown) {
      dispatch({ type: 'dropOptimistic', localId });
      setError(errorText(err, t('The message could not be sent.')));
    }
  }

  function stop() {
    const id = convRef.current.threadId;
    if (!id) return;
    setStopping(true);
    cancelAskTurn(id)
      .then(() => {
        // The server confirms with ask.turn; if that never arrives, settle the UI after a grace period.
        window.setTimeout(() => {
          if (convRef.current.threadId === id && convRef.current.turnActive) {
            dispatch({ type: 'turnEnded' });
            void refreshLatest(id);
          }
        }, 8000);
      })
      .catch((err: unknown) => { setStopping(false); setError(errorText(err, t('Could not stop the captain.'))); });
  }

  async function runQuickAction(action: AskQuickAction, args: Record<string, unknown>): Promise<boolean> {
    if (!action.toolName) return false;
    setActionBusy(true);
    try {
      const id = await ensureThread();
      const proposal = await runAskQuickAction(id, action.toolName, args);
      if (proposal?.id) dispatch({ type: 'proposal', proposal });
      await Promise.all([refreshLatest(id), refreshDetail(id)]);
      messageListRef.current?.scrollToBottom();
      if (proposal && String(proposal.status).toLowerCase() === 'failed') {
        pushToast('error', t('{{command}} failed: {{reason}}', { command: action.command || action.name, reason: proposal.errorText || t('unknown error') }));
      }
      return true;
    } catch (err: unknown) {
      setError(errorText(err, t('The quick action failed.')));
      return false;
    } finally {
      setActionBusy(false);
    }
  }

  async function decide(proposal: AskActionProposal, approve: boolean) {
    const id = convRef.current.threadId;
    if (!id) return;
    setBusyProposalId(proposal.id);
    try {
      const updated = approve ? await approveAskProposal(id, proposal.id) : await rejectAskProposal(id, proposal.id);
      if (updated?.id) dispatch({ type: 'proposal', proposal: updated });
      void refreshLatest(id);
      void refreshDetail(id);
    } catch (err: unknown) {
      setError(errorText(err, approve ? t('The action could not be approved.') : t('The action could not be rejected.')));
    } finally {
      setBusyProposalId(null);
    }
  }

  async function updateThread(target: AskThread, patch: AskThreadUpdateRequest): Promise<AskThread | null> {
    try {
      const updated = await updateAskThread(target.id, patch);
      const merged = { ...target, ...patch, ...updated } as AskThread;
      setThreads((prev) => applyThreadUpdate(prev, merged, filterRef.current, convRef.current.threadId));
      if (merged.id === convRef.current.threadId) dispatch({ type: 'thread', thread: merged });
      return merged;
    } catch (err: unknown) {
      setError(errorText(err, t('The conversation could not be updated.')));
      return null;
    }
  }

  async function changeCliPolicy(target: AskThread, policy: CliPermissionPolicy | null) {
    try {
      const updated = await setAskThreadCliPermissionPolicy(target.id, policy);
      const merged = { ...target, ...updated, cliPermissionPolicy: updated?.cliPermissionPolicy ?? policy } as AskThread;
      setThreads((prev) => applyThreadUpdate(prev, merged, filterRef.current, convRef.current.threadId));
      if (merged.id === convRef.current.threadId) dispatch({ type: 'thread', thread: merged });
      if (policy === 'Bypass') pushToast('warning', t('CLI tools bypass is on: the captain runs any command in this conversation without asking.'));
    } catch (err: unknown) {
      setError(errorText(err, t('The CLI tools policy could not be changed.')));
    }
  }

  function cliDecided(request: CliPermissionRequest) {
    dispatch({ type: 'cliPermission', request });
  }

  async function summarize(target: AskThread) {
    try {
      await summarizeAskThread(target.id);
      pushToast('info', t('Summarizing "{{title}}". The summary will appear in the conversation.', { title: target.title || t('New conversation') }));
    } catch (err: unknown) {
      setError(errorText(err, t('The conversation could not be summarized.')));
    }
  }

  /** Start a new conversation (the New button, /new, /clear): the composer is cleared and focused. */
  function newConversation(keepCaptain: boolean) {
    setDrawerOpen(false);
    if (keepCaptain && thread?.captainId) { setDraftCaptainId(thread.captainId); writeStored(CAPTAIN_STORAGE_KEY, thread.captainId); }
    if (convRef.current.threadId) navigate('/ask');
    else composerRef.current?.reset({ focus: true });
  }

  function openCaptainPicker() {
    const select = document.getElementById(CAPTAIN_SELECT_ID) as (HTMLSelectElement & { showPicker?: () => void }) | null;
    if (!select) return;
    select.focus();
    try { select.showPicker?.(); } catch { /* not supported (or no user activation): focus is enough */ }
  }

  function localCommand(command: AskLocalCommand, args: string): Promise<AskCommandOutcome> {
    return runLocalCommand(command, args, {
      hasThread: !!thread,
      archived: !!thread?.archived,
      turnActive: conv.turnActive,
      captains,
      showThinking,
      newConversation: () => newConversation(true),
      openHelp: () => composerRef.current?.reset({ text: '/', focus: true }),
      summarize: () => (thread ? summarize(thread) : undefined),
      rename: (title) => (thread ? updateThread(thread, { title }) : undefined),
      archive: () => (thread ? updateThread(thread, { archived: true }) : undefined),
      setCaptain: (captainId) => {
        writeStored(CAPTAIN_STORAGE_KEY, captainId);
        if (thread) void updateThread(thread, { captainId });
        else setDraftCaptainId(captainId);
      },
      openCaptainPicker,
      setShowThinking: changeShowThinking,
    });
  }

  function changeShowThinking(value: boolean) {
    setShowThinking(value);
    writeStored(THINKING_STORAGE_KEY, value ? 'true' : 'false');
  }

  async function confirmDelete() {
    const target = deleteTarget;
    setDeleteTarget(null);
    if (!target) return;
    try {
      await deleteAskThread(target.id);
      setThreads((prev) => prev.filter((th) => th.id !== target.id));
      pushToast('success', t('Conversation deleted.'));
      if (target.id === convRef.current.threadId) navigate('/ask');
    } catch (err: unknown) {
      setError(errorText(err, t('The conversation could not be deleted.')));
    }
  }

  function selectWork(work: AskTrackedWork) {
    if (messageListRef.current?.scrollToWork(work.id)) {
      setHighlightedWorkId(work.id);
      window.setTimeout(() => setHighlightedWorkId((current) => (current === work.id ? null : current)), 2000);
      return;
    }
    navigate(workRoute(work.entityType, work.entityId));
  }

  async function loadOlder() {
    const id = convRef.current.threadId;
    if (!id || loadingOlder) return;
    const oldest = convRef.current.messages.find((m) => !isLocalMessage(m));
    if (!oldest) return;
    setLoadingOlder(true);
    try {
      const page = await enumerateAskMessages(id, { beforeSequence: oldest.sequence, pageSize: MESSAGE_PAGE_SIZE });
      dispatch({ type: 'older', threadId: id, messages: page?.messages ?? [], hasMore: !!page?.hasMore });
    } catch (err: unknown) {
      setError(errorText(err, t('Failed to load earlier messages.')));
    } finally {
      setLoadingOlder(false);
    }
  }

  // ---------------------------------------------------------------- render

  // Whether the captain proposes actions as approval cards, runs Armada tools ungated, or is not connected (lib/askCaptain).
  const { mcpMissing, ungated, noCaptain } = askCaptainAccess(activeCaptainId, tools);

  const emptyState = thread ? (
    <p className="text-dim">{t('Send the first message to begin.')}</p>
  ) : (
    <div className="ask-empty">
      <p className="ask-empty-greeting">{greeting}</p>
      <p className="ask-empty-sub">
        {activeCaptain
          ? t('Ask {{name}} anything about your fleet, or start work with a quick action.', { name: activeCaptain.name })
          : t('Choose a captain to chat, or start work with a quick action.')}
      </p>
      <div className="ask-empty-actions">
        {quickActions.map((action) => (
          <button key={action.name} type="button" className="btn btn-sm" onClick={() => composerRef.current?.choose(action)} title={action.description ? t(action.description) : undefined}>
            <code>{action.command || `/${action.name}`}</code>
          </button>
        ))}
      </div>
    </div>
  );

  return (
    <div className={`ask-home${drawerOpen ? ' is-drawer-open' : ''}`}>
      <div className="ask-home-list" id="ask-thread-drawer" ref={drawerRef} role={drawerOpen ? 'dialog' : undefined} aria-modal={drawerOpen || undefined} aria-label={drawerOpen ? t('Conversations') : undefined}>
        <AskThreadList
          threads={threads}
          selectedId={routeThreadId}
          activity={activity}
          loading={listLoading}
          error={listError}
          search={search}
          onSearchChange={setSearch}
          includeArchived={includeArchived}
          onIncludeArchivedChange={setIncludeArchived}
          hasMore={listHasMore}
          onLoadMore={() => void loadThreads(listPage + 1)}
          onRetry={() => void loadThreads(1)}
          onSelect={(th) => { setDrawerOpen(false); navigate(`/ask/${encodeURIComponent(th.id)}`); }}
          onNew={() => newConversation(false)}
          onRename={(th, title) => void updateThread(th, { title })}
          onTogglePin={(th) => void updateThread(th, { pinned: !th.pinned })}
          onSummarize={(th) => void summarize(th)}
          onToggleArchive={(th) => void updateThread(th, { archived: !th.archived })}
          onDelete={(th) => setDeleteTarget(th)}
          onClose={drawerOpen ? () => setDrawerOpen(false) : undefined}
        />
      </div>
      {drawerOpen && <div className="ask-drawer-backdrop" onClick={() => setDrawerOpen(false)} aria-hidden="true" />}

      <section className="ask-home-conv" aria-label={thread?.title || t('New conversation')}>
        <AskConversationHeader
          thread={thread}
          captains={captains}
          draftCaptainId={draftCaptainId}
          onDraftCaptainChange={(id) => { setDraftCaptainId(id); writeStored(CAPTAIN_STORAGE_KEY, id); }}
          onRename={(title) => { if (thread) void updateThread(thread, { title }); }}
          onCaptainChange={(captainId) => {
            if (!thread) return;
            if (captainId) writeStored(CAPTAIN_STORAGE_KEY, captainId);
            void updateThread(thread, { captainId });
          }}
          onAutoApproveChange={(value) => {
            if (!thread) return;
            void updateThread(thread, { autoApprove: value }).then((updated) => {
              if (updated && value) pushToast('warning', t('Auto-approve is on. Actions the captain proposes in this conversation now run without asking.'));
            });
          }}
          onSummarize={() => { if (thread) void summarize(thread); }}
          onTogglePin={() => { if (thread) void updateThread(thread, { pinned: !thread.pinned }); }}
          onToggleArchive={() => { if (thread) void updateThread(thread, { archived: !thread.archived }); }}
          onDelete={() => { if (thread) setDeleteTarget(thread); }}
          onOpenList={() => setDrawerOpen(true)}
          busy={conv.turnActive}
          onCliPolicyChange={(policy) => { if (thread) void changeCliPolicy(thread, policy); }}
          canBypassCli={!!isAdmin || !!isTenantAdmin}
        />

        {ungated && (
          <div className="ask-ungated-note" role="note" data-testid="ask-ungated-note">
            <strong>{t('Actions from this captain run without approval cards.')}</strong>
            <span>{t('This runtime uses its own Armada connection, so anything it does through Armada tools happens immediately.')}</span>
          </div>
        )}
        {thread?.autoApprove && (
          <div className="ask-auto-banner" role="note">
            {t('Auto-approve is on: actions the captain proposes run immediately. Every action is still recorded below.')}
          </div>
        )}
        {thread?.cliPermission?.fallbackReason && (
          <div className="ask-mcp-note ask-cli-fallback-note" role="note" data-testid="ask-cli-fallback-note">
            <span>{fallbackReasonText(t, thread.cliPermission.fallbackReason)}</span>
          </div>
        )}
        {mcpMissing && (
          <div className="ask-mcp-note" role="note">
            <span>{t('This captain is not connected to Armada over MCP, so it can answer but cannot propose actions. Quick actions still work.')}</span>
            <a href={instructionsDocUrl(activeCaptain?.runtime)} target="_blank" rel="noopener noreferrer">{t('How to connect')}</a>
          </div>
        )}

        <AskWorkStrip work={conv.trackedWork} onSelect={selectWork} />

        {convError ? (
          <div className="ask-conv-error">
            <ErrorState message={convError} onRetry={routeThreadId ? () => void loadConversation(routeThreadId) : undefined} />
            <button type="button" className="btn btn-sm" onClick={() => navigate('/ask')}>{t('Start a new conversation')}</button>
          </div>
        ) : convLoading && conv.messages.length === 0 ? (
          <div className="ask-transcript ask-transcript-loading"><LoadingState label={t('Loading conversation...')} /></div>
        ) : (
          <AskMessageList
            ref={messageListRef}
            messages={conv.messages}
            proposals={conv.proposals}
            trackedWork={conv.trackedWork}
            snapshots={conv.snapshots}
            hasMore={conv.hasMore}
            loadingOlder={loadingOlder}
            onLoadOlder={() => void loadOlder()}
            streaming={conv.streaming}
            turnActive={conv.turnActive}
            waitingText={waitingText}
            captainName={activeCaptain?.name ?? null}
            captainNames={captainNames}
            busyProposalId={busyProposalId}
            onApprove={(p) => void decide(p, true)}
            onReject={(p) => void decide(p, false)}
            highlightedWorkId={highlightedWorkId}
            emptyState={emptyState}
            turnError={conv.turnError}
            cliPermissions={conv.cliPermissions}
            onCliDecided={cliDecided}
            cliResolution={thread?.cliPermission ?? null}
          />
        )}

        <AskComposer
          ref={composerRef}
          quickActions={quickActions}
          turnActive={conv.turnActive}
          stopping={stopping}
          onStop={stop}
          onSend={(text) => void send(text)}
          onQuickAction={runQuickAction}
          onLocalCommand={localCommand}
          actionBusy={actionBusy}
          onOpenImport={() => setImportOpen(true)}
          noCaptain={noCaptain}
          showThinking={showThinking}
          onShowThinkingChange={changeShowThinking}
        />
      </section>

      <div className="sr-only" role="status" aria-live="polite" aria-atomic="true">{announcement}</div>

      <ErrorModal error={error} onClose={() => setError('')} />
      <ConfirmDialog
        open={!!deleteTarget}
        title={t('Delete conversation')}
        message={t('Delete "{{title}}"? Its messages and action history are removed. Work it started keeps running and stays visible on the normal pages.', { title: deleteTarget?.title || t('New conversation') })}
        confirmLabel={t('Delete')}
        cancelLabel={t('Cancel')}
        danger
        onConfirm={() => void confirmDelete()}
        onCancel={() => setDeleteTarget(null)}
      />
      <ImportWizard open={importOpen} onClose={() => setImportOpen(false)} tenantId={user?.user?.tenantId ?? null} />
    </div>
  );
}
