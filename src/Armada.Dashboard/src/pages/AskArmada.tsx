import { useCallback, useEffect, useMemo, useReducer, useRef, useState, type CSSProperties } from 'react';
import { useLocation, useNavigate, useParams } from 'react-router-dom';
import { readAskPrefill, type AskNavigationState } from '../lib/askNavigation';
import {
  ApiError,
  approveAskProposal,
  cancelAskTurn,
  createAskThread,
  deleteAskThread,
  deleteAskThreads,
  enumerateAskMessages,
  enumerateAskThreads,
  getAskQuickActions,
  getAskWorkSnapshot,
  getCaptainTools,
  listCaptains,
  markAskThreadRead,
  markAskThreadUnread,
  rejectAskProposal,
  runAskQuickAction,
  sendAskMessage,
  setAskThreadCliPermissionPolicy,
  summarizeAskThread,
  updateAskThread,
} from '../api/client';
import { listAll } from '../api/paging';
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
import { useDeleteProgress } from '../lib/useDeleteProgress';
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
import { loadAskThreadView, loadToolCalls, withToolCalls, withWorkRows } from '../lib/askPaging';
import { conversationReducer, initialConversation, isLocalMessage } from '../lib/askConversation';
import { applyActivityEvent, applyThreadUpdate, sortThreads, type ThreadActivityMap, type ThreadListFilter } from '../lib/askThreads';
import { DEFAULT_QUICK_ACTIONS, mergeQuickActions, quickActionForm } from '../lib/askQuickActions';
import { AskBlackBoxActionsContext, type AskBlackBoxActionsValue } from '../components/ask/AskBlackBoxActions';
import { runLocalCommand, type AskCommandOutcome, type AskLocalCommand } from '../lib/askCommands';
import { isWorkActive, workRoute } from '../lib/askWork';
import { useFocusTrap } from '../lib/useFocusTrap';
import { ASK_LIST_MAX_WIDTH, ASK_LIST_MIN_WIDTH, useAskListResize } from '../lib/askListResize';
import { askCaptainAccess, instructionsDocUrl } from '../lib/askCaptain';
import { fallbackReasonText, isPendingRequest, parseCliPermissionEvent } from '../lib/cliPermissions';

const CAPTAIN_STORAGE_KEY = 'armada_ask_captain';
const THINKING_STORAGE_KEY = 'armada_ask_show_thinking';
// The conversation last opened, reopened when the page is entered on the bare /ask route. Suffixed with the signed-in
// user id so one user never reopens another user's conversation in a shared browser.
const LAST_THREAD_STORAGE_KEY = 'armada_ask_last_thread';
// Unsent drafts of saved conversations, a JSON map of thread id to text, kept across visits. Suffixed with the user id.
const DRAFTS_STORAGE_KEY = 'armada_ask_drafts';
const THREAD_PAGE_SIZE = 50;
const MESSAGE_PAGE_SIZE = 30;
const SNAPSHOT_FETCH_LIMIT = 20;

function readStored(key: string): string {
  try { return localStorage.getItem(key) ?? ''; } catch { return ''; }
}

function writeStored(key: string, value: string) {
  try { localStorage.setItem(key, value); } catch { /* storage unavailable */ }
}

function removeStored(key: string) {
  try { localStorage.removeItem(key); } catch { /* storage unavailable */ }
}

function readDrafts(key: string): Record<string, string> {
  try {
    const parsed: unknown = JSON.parse(readStored(key) || '{}');
    return parsed && typeof parsed === 'object' && !Array.isArray(parsed) ? parsed as Record<string, string> : {};
  } catch { return {}; }
}

function storeDraft(key: string, threadId: string, text: string) {
  const drafts = readDrafts(key);
  if (text) drafts[threadId] = text;
  else delete drafts[threadId];
  if (Object.keys(drafts).length > 0) writeStored(key, JSON.stringify(drafts));
  else removeStored(key);
}

/** Router state on /ask: `newConversation` marks an explicit empty conversation, so the last one is not reopened. */
type AskRouteState = AskNavigationState & { newConversation?: boolean };
const NEW_CONVERSATION_STATE: AskRouteState = { newConversation: true };

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
  const userId = user?.user?.id ?? null;
  const lastThreadKey = userId ? `${LAST_THREAD_STORAGE_KEY}:${userId}` : LAST_THREAD_STORAGE_KEY;
  const lastThreadKeyRef = useRef(lastThreadKey);
  lastThreadKeyRef.current = lastThreadKey;
  const draftsKeyRef = useRef('');
  draftsKeyRef.current = userId ? `${DRAFTS_STORAGE_KEY}:${userId}` : DRAFTS_STORAGE_KEY;
  // The id the bare /ask route was redirected to from storage; if it no longer loads, the page returns to /ask.
  const restoringThreadIdRef = useRef<string | null>(null);

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
  // Conversations closed by Mark unread: convRef keeps the old id until the next render, so nothing may mark them read
  // (socket events, list loads, a load in flight) until the user opens them again.
  const markedUnreadRef = useRef(new Set<string>());
  const openThreadId = useCallback((): string | null => {
    const id = convRef.current.threadId;
    return id && !markedUnreadRef.current.has(id) ? id : null;
  }, []);
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
  // Delete in flight (or just failed): the progress modal stays up until the server answers and blocks a second
  // submission before React re-renders.
  const deletion = useDeleteProgress();
  // Bulk delete: the rows ticked in the list, and the confirmation dialog they open.
  const [selectedThreadIds, setSelectedThreadIds] = useState<string[]>([]);
  const [deleteManyOpen, setDeleteManyOpen] = useState(false);
  const [importOpen, setImportOpen] = useState(false);
  const [announcement, setAnnouncement] = useState('');
  const [greeting] = useState(() => randomGreeting());
  const messageListRef = useRef<AskMessageListHandle>(null);
  const composerRef = useRef<AskComposerHandle>(null);
  // Each saved conversation keeps its own unsent draft, saved in the browser so it survives leaving the page; a new
  // conversation always starts empty. The ref covers browsers where storage is unavailable.
  const draftsRef = useRef<Record<string, string>>({});
  const saveDraft = useCallback((id: string, text: string) => {
    if (text) draftsRef.current[id] = text;
    else delete draftsRef.current[id];
    storeDraft(draftsKeyRef.current, id, text);
  }, []);
  const routeOpenedRef = useRef(false);
  const draftFor = useCallback((id: string) => draftsRef.current[id] ?? readDrafts(draftsKeyRef.current)[id] ?? '', []);
  const drawerRef = useRef<HTMLDivElement>(null);
  // On narrow screens the list is a modal drawer: keep focus inside it and close it with Escape.
  useFocusTrap(drawerRef, drawerOpen, () => setDrawerOpen(false));
  const homeRef = useRef<HTMLDivElement>(null);
  const listResize = useAskListResize(homeRef, drawerRef);

  const thread = conv.thread;
  const activeCaptainId = thread ? (thread.captainId ?? '') : draftCaptainId;
  const activeCaptain = captains.find((c) => c.id === activeCaptainId) ?? null;
  const captainNames = useMemo(() => Object.fromEntries(captains.map((c) => [c.id, c.name])), [captains]);

  // ---------------------------------------------------------------- loading

  useEffect(() => {
    listAll(listCaptains)
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
      const openId = openThreadId();
      const incoming = (result.objects || []).map((th) => (th.id === openId ? { ...th, unreadCount: 0 } : th));
      setThreads((prev) => (page === 1 ? incoming : sortThreads([...prev.filter((p) => !incoming.some((i) => i.id === p.id)), ...incoming])));
      setListPage(page);
      setListHasMore(page < (result.totalPages || 1));
    } catch (err: unknown) {
      setListError(errorText(err, t('Failed to load conversations.')));
    } finally {
      setListLoading(false);
    }
  }, [openThreadId, t]);

  useEffect(() => { void loadThreads(1); }, [query, includeArchived, reconnectCount, loadThreads]);

  // The selection is a view of the rows that are still listed: drop ids a search, filter, refresh, or delete removed.
  useEffect(() => {
    setSelectedThreadIds((prev) => {
      const next = prev.filter((id) => threads.some((th) => th.id === id));
      return next.length === prev.length ? prev : next;
    });
  }, [threads]);

  const markRead = useCallback((id: string) => {
    if (typeof document !== 'undefined' && document.visibilityState === 'hidden') return;
    if (markedUnreadRef.current.has(id)) return;
    setThreads((prev) => prev.map((th) => (th.id === id && th.unreadCount ? { ...th, unreadCount: 0 } : th)));
    markAskThreadRead(id).catch(() => { /* best effort; the badge resyncs on the next ask.thread */ });
  }, []);

  const fetchSnapshots = useCallback((id: string, work: AskTrackedWork[]) => {
    // Snapshots carry counts only: each card reads the first page of its rows (with the total) from the row routes.
    const pending = work.filter((w) => {
      const known = convRef.current.snapshots[w.id] ?? w.snapshot;
      return !known || (known.missions == null && known.targets == null);
    });
    pending.sort((a, b) => Number(isWorkActive(b)) - Number(isWorkActive(a)));
    for (const item of pending.slice(0, SNAPSHOT_FETCH_LIMIT)) {
      const known = convRef.current.snapshots[item.id] ?? item.snapshot;
      (known ? Promise.resolve(known) : getAskWorkSnapshot(id, item.id))
        .then((snapshot) => (snapshot ? withWorkRows(id, item.id, snapshot) : snapshot))
        .then((snapshot) => { if (snapshot && convRef.current.threadId === id) dispatch({ type: 'snapshot', trackedWorkId: item.id, snapshot }); })
        .catch(() => { /* the card shows its loading state until an ask.work event arrives */ });
    }
  }, []);

  const loadConversation = useCallback(async (id: string, silent = false) => {
    if (!silent) setConvLoading(true);
    setConvError(null);
    try {
      const [detail, page] = await Promise.all([
        loadAskThreadView(id),
        enumerateAskMessages(id, { pageSize: MESSAGE_PAGE_SIZE }).then(async (p) => (p ? { ...p, messages: await withToolCalls(id, p.messages ?? []) } : p)),
      ]);
      if (openThreadId() !== id) return;
      if (restoringThreadIdRef.current === id) restoringThreadIdRef.current = null;
      if (detail?.thread?.archived && !filterRef.current.includeArchived) forgetLastThread([id]);
      else writeStored(lastThreadKeyRef.current, id);
      dispatch({ type: 'loaded', threadId: id, detail, messages: page?.messages ?? [], hasMore: !!page?.hasMore });
      if (detail?.thread) setThreads((prev) => applyThreadUpdate(prev, detail.thread, filterRef.current, id));
      markRead(id);
      fetchSnapshots(id, detail?.trackedWork ?? []);
    } catch (err: unknown) {
      if (convRef.current.threadId !== id) return;
      const restoring = restoringThreadIdRef.current === id;
      if (restoring) restoringThreadIdRef.current = null;
      if (err instanceof ApiError && (err.status === 404 || err.status === 403)) {
        // Gone or not ours: forget it, and when it was only reopened from storage, go back to the empty /ask.
        forgetLastThread([id]);
        if (restoring) { navigate('/ask', { replace: true, state: NEW_CONVERSATION_STATE }); return; }
      }
      if (err instanceof ApiError && err.status === 404) setConvError(t('This conversation was not found. It may have been deleted.'));
      else setConvError(errorText(err, t('Failed to load the conversation.')));
    } finally {
      if (convRef.current.threadId === id) setConvLoading(false);
    }
  }, [fetchSnapshots, markRead, navigate, openThreadId, t]);

  const refreshLatest = useCallback(async (id: string) => {
    try {
      const page = await enumerateAskMessages(id, { pageSize: MESSAGE_PAGE_SIZE });
      dispatch({ type: 'latest', threadId: id, messages: await withToolCalls(id, page?.messages ?? []) });
    } catch { /* the socket will deliver the messages; this is only reconciliation */ }
  }, []);

  const refreshDetail = useCallback(async (id: string) => {
    try {
      const detail = await loadAskThreadView(id);
      dispatch({ type: 'detail', threadId: id, detail });
      fetchSnapshots(id, detail?.trackedWork ?? []);
    } catch { /* reconciliation only */ }
  }, [fetchSnapshots]);

  // Another page (Workspace's Ask Armada, the black box panel) can open a new conversation with a draft in the message
  // box, through router state or the ?prompt= query parameter (links from outside the dashboard, such as the VS Code
  // extension). The draft is cleared once used, so reloading or going back does not put it there again.
  // The bare /ask route (the nav link) reopens the conversation last open. An explicit new conversation carries
  // NEW_CONVERSATION_STATE; any other bare /ask entry is stamped with it once handled, so Back never bounces.
  const location = useLocation();
  useEffect(() => {
    if (routeThreadId) return;
    const state = location.state as AskRouteState | null;
    const prefill = readAskPrefill(location.search);
    const here = location.pathname + (prefill ? prefill.rest : location.search);
    const draft = state?.draft || prefill?.text;
    if (draft) {
      composerRef.current?.reset({ text: draft, focus: true });
      navigate(here, { replace: true, state: NEW_CONVERSATION_STATE });
      return;
    }
    if (state?.newConversation) return;
    const lastId = readStored(lastThreadKey);
    if (lastId) {
      restoringThreadIdRef.current = lastId;
      navigate(`/ask/${encodeURIComponent(lastId)}`, { replace: true });
      return;
    }
    navigate(here, { replace: true, state: NEW_CONVERSATION_STATE });
  }, [location.state, location.pathname, location.search, routeThreadId, lastThreadKey, navigate]);

  // Route changes open a different conversation (or the new-conversation screen).
  useEffect(() => {
    setDrawerOpen(false);
    const previousId = convRef.current.threadId;
    // The page opened straight on a conversation: put back the draft saved on an earlier visit.
    const firstOpen = !routeOpenedRef.current;
    routeOpenedRef.current = true;
    // A conversation just created by a send or quick action already holds this id: its composer state carries over.
    if (previousId !== routeThreadId) {
      if (previousId) saveDraft(previousId, composerRef.current?.getText() ?? '');
      composerRef.current?.reset({ text: routeThreadId ? draftFor(routeThreadId) : '', focus: !routeThreadId });
      dispatch({ type: 'reset', threadId: routeThreadId });
    } else if (firstOpen && routeThreadId) {
      composerRef.current?.reset({ text: draftFor(routeThreadId) });
    }
    setConvError(null);
    setStopping(false);
    if (routeThreadId) markedUnreadRef.current.delete(routeThreadId);
    if (routeThreadId) void loadConversation(routeThreadId);
    else setConvLoading(false);
  }, [routeThreadId, loadConversation, saveDraft, draftFor]);

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
      const openId = openThreadId();

      if (event.type === 'ask.thread') {
        setThreads((prev) => applyThreadUpdate(prev, event.thread, filterRef.current, openId));
        if (event.threadId === openId && (event.thread.unreadCount ?? 0) > 0) markRead(event.threadId);
      }
      if (event.threadId !== openId) return;

      const before = convRef.current;
      if (event.type === 'ask.work' && event.snapshot) {
        // The event's snapshot carries counts only; keep the card's rows until they are read again below.
        const prior = before.snapshots[event.trackedWorkId];
        if (prior && event.snapshot.missions == null && event.snapshot.targets == null) {
          event.snapshot = { ...event.snapshot, missions: prior.missions, targets: prior.targets, rowTotal: prior.rowTotal };
        }
        const workId = event.trackedWorkId;
        const threadId = event.threadId;
        void withWorkRows(threadId, workId, event.snapshot).then((snapshot) => {
          if (convRef.current.threadId === threadId) dispatch({ type: 'snapshot', trackedWorkId: workId, snapshot });
        });
      }
      if (event.type === 'ask.message' && (event.message.toolCallCount ?? 0) > 0 && !event.message.toolCalls) {
        const known = before.messages.find((m) => m.id === event.message.id);
        if (known?.toolCalls) event.message = { ...event.message, toolCalls: known.toolCalls };
        const message = event.message;
        const threadId = event.threadId;
        void loadToolCalls(threadId, message.id).then((toolCalls) => {
          if (convRef.current.threadId === threadId) dispatch({ type: 'event', event: { type: 'ask.message', threadId, message: { ...message, toolCalls } } });
        }).catch(() => { /* the reply shows without its tool chips until the next reconciliation */ });
      }
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
  }, [subscribe, markRead, openThreadId, refreshLatest, refreshDetail, t]);

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
      const failed = !!proposal && String(proposal.status).toLowerCase() === 'failed';
      if (failed) {
        pushToast('error', t('{{command}} failed: {{reason}}', { command: action.command || action.name, reason: proposal.errorText || t('unknown error') }));
      }
      // /blackbox: the card is shown; the question (optional) goes to the captain as a message.
      const question = typeof args.question === 'string' ? args.question.trim() : '';
      if (!failed && question && quickActionForm(action) === 'blackbox' && !convRef.current.turnActive) void send(question);
      return true;
    } catch (err: unknown) {
      setError(errorText(err, t('The quick action failed.')));
      return false;
    } finally {
      setActionBusy(false);
    }
  }

  /** Black box card buttons: Fix it, Apply and Propose create Pending proposals; the other follow-ups ask the captain. */
  async function proposeBlackBoxAction(toolName: string, args: Record<string, unknown>) {
    const id = convRef.current.threadId;
    if (!id) return;
    setActionBusy(true);
    try {
      const proposal = await runAskQuickAction(id, toolName, args, { propose: true });
      if (proposal?.id) dispatch({ type: 'proposal', proposal });
      await refreshLatest(id);
      messageListRef.current?.scrollToBottom();
    } catch (err: unknown) {
      setError(errorText(err, t('The action could not be proposed.')));
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
      if (merged.archived && !filterRef.current.includeArchived) forgetLastThread([merged.id]);
      return merged;
    } catch (err: unknown) {
      setError(errorText(err, t('The conversation could not be updated.')));
      return null;
    }
  }

  async function markThreadRead(target: AskThread) {
    setThreads((prev) => prev.map((th) => (th.id === target.id ? { ...th, unreadCount: 0 } : th)));
    try { await markAskThreadRead(target.id); }
    catch (err: unknown) { setError(errorText(err, t('The conversation could not be marked read.'))); void loadThreads(1); }
  }

  /** Mark a conversation unread. The open one would be marked read again at once, so it is closed first. */
  async function markThreadUnread(target: AskThread) {
    if (target.id === convRef.current.threadId) {
      markedUnreadRef.current.add(target.id);
      forgetLastThread([target.id]);
      navigate('/ask', { state: NEW_CONVERSATION_STATE });
    }
    setThreads((prev) => prev.map((th) => (th.id === target.id ? { ...th, unreadCount: Math.max(1, th.unreadCount ?? 0) } : th)));
    try {
      const updated = await markAskThreadUnread(target.id);
      if (updated?.id) setThreads((prev) => prev.map((th) => (th.id === updated.id ? { ...th, unreadCount: updated.unreadCount } : th)));
    } catch (err: unknown) {
      setError(errorText(err, t('The conversation could not be marked unread.')));
      void loadThreads(1);
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
    removeStored(lastThreadKeyRef.current);
    if (convRef.current.threadId) navigate('/ask', { state: NEW_CONVERSATION_STATE });
    else composerRef.current?.reset({ focus: true });
  }

  /** Stop reopening the stored last conversation when it is one of `ids` (deleted, archived out of view, or gone). */
  function forgetLastThread(ids: string[]) {
    const key = lastThreadKeyRef.current;
    if (ids.includes(readStored(key))) removeStored(key);
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

  function requestDelete(target: AskThread) {
    if (deletion.isBusy()) return;
    setDeleteTarget(target);
  }

  function confirmDelete() {
    const target = deleteTarget;
    setDeleteTarget(null);
    if (target) void runDelete(target);
  }

  async function runDelete(target: AskThread) {
    const done = await deletion.run({
      kind: 'conversation',
      message: t('This conversation is being deleted. This may take a few moments.'),
      action: () => deleteAskThread(target.id),
      errorMessage: (err) => errorText(err, t('The conversation could not be deleted.')),
    });
    if (!done) return;
    setThreads((prev) => prev.filter((th) => th.id !== target.id));
    pushToast('success', t('Conversation deleted.'));
    forgetLastThread([target.id]);
    saveDraft(target.id, '');
    if (target.id === convRef.current.threadId) navigate('/ask', { state: NEW_CONVERSATION_STATE });
  }

  async function confirmDeleteMany() {
    const ids = selectedThreadIds.slice();
    setDeleteManyOpen(false);
    if (ids.length === 0) return;
    const box: { result: Awaited<ReturnType<typeof deleteAskThreads>> | null } = { result: null };
    const done = await deletion.run({
      kind: 'conversation',
      count: ids.length,
      action: async () => { box.result = await deleteAskThreads(ids); },
      errorMessage: (err) => errorText(err, t('The conversations could not be deleted.')),
    });
    setSelectedThreadIds([]);
    if (!done) return;
    const result = box.result;
    const skipped = result?.skipped ?? [];
    const deleted = result?.deletedIds && result.deletedIds.length > 0
      ? result.deletedIds
      : ids.filter((id) => !skipped.some((entry) => entry.id === id));
    setThreads((prev) => prev.filter((th) => !deleted.includes(th.id)));
    if (skipped.length > 0) pushToast('warning', t('{{count}} conversations were not deleted.', { count: skipped.length }));
    else pushToast('success', t('Conversations deleted.'));
    forgetLastThread(deleted);
    for (const id of deleted) saveDraft(id, '');
    if (convRef.current.threadId && deleted.includes(convRef.current.threadId)) navigate('/ask', { state: NEW_CONVERSATION_STATE });
    void loadThreads(1);
  }

  function toggleSelected(target: AskThread, selected: boolean) {
    setSelectedThreadIds((prev) => (selected
      ? [...prev.filter((id) => id !== target.id), target.id]
      : prev.filter((id) => id !== target.id)));
  }

  function selectAllThreads(list: AskThread[]) {
    setSelectedThreadIds(list.map((th) => th.id));
  }

  const selectedThreads = threads.filter((th) => selectedThreadIds.includes(th.id));
  const selectedPinnedCount = selectedThreads.filter((th) => th.pinned).length;

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
      dispatch({ type: 'older', threadId: id, messages: await withToolCalls(id, page?.messages ?? []), hasMore: !!page?.hasMore });
    } catch (err: unknown) {
      setError(errorText(err, t('Failed to load earlier messages.')));
    } finally {
      setLoadingOlder(false);
    }
  }

  // ---------------------------------------------------------------- render

  // Whether the captain proposes actions as approval cards, runs Armada tools ungated, or is not connected (lib/askCaptain).
  const { mcpMissing, ungated, noCaptain } = askCaptainAccess(activeCaptainId, tools);
  const blackBoxActions: AskBlackBoxActionsValue = {
    propose: proposeBlackBoxAction,
    ask: (prompt: string) => { if (!convRef.current.turnActive) void send(prompt); },
    canAsk: !noCaptain && !conv.turnActive,
    busy: actionBusy,
  };

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
          <button key={action.name} type="button" className="btn btn-sm" onClick={() => composerRef.current?.choose(action)} title={action.description ? t(action.description) : t('Put this quick action in the message box so you can fill it in and send it.')}>
            <code>{action.command || `/${action.name}`}</code>
          </button>
        ))}
      </div>
    </div>
  );

  return (
    <div
      ref={homeRef}
      className={`ask-home${drawerOpen ? ' is-drawer-open' : ''}${listResize.dragging ? ' is-resizing' : ''}`}
      style={listResize.width !== null ? { '--ask-list-width': `${listResize.width}px` } as CSSProperties : undefined}
    >
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
          onRefresh={() => { if (!listLoading) void loadThreads(1); }}
          onSelect={(th) => { setDrawerOpen(false); navigate(`/ask/${encodeURIComponent(th.id)}`); }}
          onNew={() => newConversation(false)}
          onRename={(th, title) => void updateThread(th, { title })}
          onTogglePin={(th) => void updateThread(th, { pinned: !th.pinned })}
          onSummarize={(th) => void summarize(th)}
          onToggleArchive={(th) => void updateThread(th, { archived: !th.archived })}
          onMarkRead={(th) => void markThreadRead(th)}
          onMarkUnread={(th) => void markThreadUnread(th)}
          onDelete={(th) => requestDelete(th)}
          selectedIds={selectedThreadIds}
          onToggleSelected={toggleSelected}
          onSelectAll={selectAllThreads}
          onClearSelection={() => setSelectedThreadIds([])}
          onDeleteSelected={() => setDeleteManyOpen(true)}
          onClose={drawerOpen ? () => setDrawerOpen(false) : undefined}
        />
      </div>
      <div
        className="ask-list-splitter"
        role="separator"
        aria-orientation="vertical"
        aria-controls="ask-thread-drawer"
        aria-label={t('Resize conversation list')}
        aria-valuemin={ASK_LIST_MIN_WIDTH}
        aria-valuemax={ASK_LIST_MAX_WIDTH}
        aria-valuenow={listResize.width ?? undefined}
        title={t('Drag to resize. Double-click to reset.')}
        tabIndex={0}
        data-testid="ask-list-splitter"
        {...listResize.splitterProps}
      />
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
          onDelete={() => { if (thread) requestDelete(thread); }}
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
            <a href={instructionsDocUrl(activeCaptain?.runtime)} target="_blank" rel="noopener noreferrer" title={t('Open the setup guide, in a new tab, for connecting this captain runtime to the Armada MCP server.')}>{t('How to connect')}</a>
          </div>
        )}

        <AskWorkStrip work={conv.trackedWork} onSelect={selectWork} />

        {convError ? (
          <div className="ask-conv-error">
            <ErrorState message={convError} onRetry={routeThreadId ? () => void loadConversation(routeThreadId) : undefined} />
            <button type="button" className="btn btn-sm" onClick={() => { forgetLastThread(routeThreadId ? [routeThreadId] : []); navigate('/ask', { state: NEW_CONVERSATION_STATE }); }} title={t('Leave this conversation and open an empty one.')}>{t('Start a new conversation')}</button>
          </div>
        ) : convLoading && conv.messages.length === 0 ? (
          <div className="ask-transcript ask-transcript-loading"><LoadingState label={t('Loading conversation...')} /></div>
        ) : (
          <AskBlackBoxActionsContext.Provider value={blackBoxActions}>
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
          </AskBlackBoxActionsContext.Provider>
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
          onTextChange={(text) => { const id = convRef.current.threadId; if (id) saveDraft(id, text); }}
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
        onConfirm={confirmDelete}
        onCancel={() => setDeleteTarget(null)}
      />
      <ConfirmDialog
        open={deleteManyOpen}
        title={t('Delete conversations')}
        message={selectedPinnedCount > 0
          ? t('Delete {{count}} conversations? Their messages and action history are removed. Work they started keeps running and stays visible on the normal pages.', { count: selectedThreads.length }) + ' ' + t('{{count}} of them are pinned.', { count: selectedPinnedCount })
          : t('Delete {{count}} conversations? Their messages and action history are removed. Work they started keeps running and stays visible on the normal pages.', { count: selectedThreads.length })}
        confirmLabel={t('Delete')}
        cancelLabel={t('Cancel')}
        danger
        onConfirm={() => void confirmDeleteMany()}
        onCancel={() => setDeleteManyOpen(false)}
      />
      {deletion.dialog}
      <ImportWizard open={importOpen} onClose={() => setImportOpen(false)} tenantId={user?.user?.tenantId ?? null} />
    </div>
  );
}
