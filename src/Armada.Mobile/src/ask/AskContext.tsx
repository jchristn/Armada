import { createContext, useCallback, useContext, useEffect, useLayoutEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { AppState } from 'react-native';
import {
  deleteAskThread,
  enumerateAskThreads,
  getAskQuickActions,
  getCaptainTools,
  listCaptains,
  markAskThreadRead,
  setAskThreadCliPermissionPolicy,
  summarizeAskThread,
  updateAskThread,
} from '@dashboard/api/client';
import type {
  AskQuickAction,
  AskThread,
  AskThreadUpdateRequest,
  Captain,
  CaptainToolAccessResult,
  CliPermissionPolicy,
  WebSocketMessage,
} from '@dashboard/types/models';
import { parseAskEvent } from '@dashboard/lib/askEvents';
import { DEFAULT_QUICK_ACTIONS, mergeQuickActions } from '@dashboard/lib/askQuickActions';
import { applyActivityEvent, applyThreadUpdate, sortThreads, type ThreadActivityMap, type ThreadListFilter } from '@dashboard/lib/askThreads';
import { useLocale } from '../i18n/LocaleContext';
import { useNotifications } from '../notifications/NotificationContext';
import { useSocket } from '../socket/SocketContext';
import { readPref, writePref } from '../storage/prefs';
import { setOpenAskThread } from './openThread';

/** Preference keys of Ask Armada (non-secret, per device). */
export const ASK_PREF_KEYS = {
  captain: 'armada.ask.captain',
  showThinking: 'armada.ask.showThinking',
  lastThread: 'armada.ask.lastThread',
} as const;

export const THREAD_PAGE_SIZE = 50;
const SEARCH_DEBOUNCE_MS = 300;

export function errorText(err: unknown, fallback: string): string {
  return err instanceof Error && err.message ? err.message : fallback;
}

export interface AskState {
  captains: Captain[];
  captainNames: Record<string, string>;
  /** Captain for a conversation that does not exist yet (remembered on the device, like the dashboard). */
  draftCaptainId: string;
  setDraftCaptainId: (id: string) => void;
  quickActions: AskQuickAction[];
  showThinking: boolean;
  setShowThinking: (value: boolean) => void;
  /** Tool access of a captain (cached); resolves null when it cannot be read. */
  loadCaptainTools: (captainId: string) => Promise<CaptainToolAccessResult | null>;

  threads: AskThread[];
  listLoading: boolean;
  listError: string | null;
  search: string;
  setSearch: (value: string) => void;
  includeArchived: boolean;
  setIncludeArchived: (value: boolean) => void;
  listHasMore: boolean;
  loadMoreThreads: () => void;
  reloadThreads: () => void;
  activity: ThreadActivityMap;

  /** The conversation on screen (unread stays zero, the last one reopens on the next launch). */
  openThreadId: string | null;
  setOpenThreadId: (id: string | null) => void;
  /** The conversation that was open when the app last closed (null until read, or when there is none). */
  lastThreadId: string | null;
  lastThreadLoaded: boolean;
  markRead: (id: string) => void;
  /** Fold a created or updated thread into the list. */
  upsertThread: (thread: AskThread) => void;
  /** Change a thread; resolves the merged thread, or null after reporting the error. */
  updateThread: (target: AskThread, patch: AskThreadUpdateRequest) => Promise<AskThread | null>;
  changeCliPolicy: (target: AskThread, policy: CliPermissionPolicy | null) => Promise<AskThread | null>;
  summarize: (target: AskThread) => Promise<void>;
  deleteThread: (target: AskThread) => Promise<boolean>;
  /** Report a failure (an error toast, the mobile form of the dashboard's error modal). */
  reportError: (message: string) => void;
}

const AskContext = createContext<AskState | null>(null);

/**
 * Session state of Ask Armada shared by the thread list and the conversation (the dashboard keeps it in the
 * AskArmada page): captains, the quick-action catalog, the server-searched thread list kept live from `ask.*`
 * events, per-thread activity (working / replying), and the thread actions (rename, pin, archive, CLI policy,
 * summarize, delete). The logic is the dashboard's shared lib (askThreads, askEvents, askQuickActions).
 */
export function AskProvider({ children }: { children: ReactNode }) {
  const { t } = useLocale();
  const { subscribe, reconnectCount } = useSocket();
  const { pushToast } = useNotifications();

  const [captains, setCaptains] = useState<Captain[]>([]);
  const [draftCaptainId, setDraftCaptainIdState] = useState('');
  const [quickActions, setQuickActions] = useState<AskQuickAction[]>(DEFAULT_QUICK_ACTIONS);
  const [showThinking, setShowThinkingState] = useState(false);
  const toolsCache = useRef<Record<string, CaptainToolAccessResult>>({});

  const [threads, setThreads] = useState<AskThread[]>([]);
  const [listLoading, setListLoading] = useState(true);
  const [listError, setListError] = useState<string | null>(null);
  const [search, setSearch] = useState('');
  const [query, setQuery] = useState('');
  const [includeArchived, setIncludeArchived] = useState(false);
  const [listPage, setListPage] = useState(1);
  const [listHasMore, setListHasMore] = useState(false);
  const [activity, setActivity] = useState<ThreadActivityMap>({});
  const [openThreadId, setOpenThreadIdState] = useState<string | null>(null);
  const [lastThreadId, setLastThreadId] = useState<string | null>(null);
  const [lastThreadLoaded, setLastThreadLoaded] = useState(false);

  const filterRef = useRef<ThreadListFilter>({ search: '', includeArchived: false });
  const openRef = useRef<string | null>(null);
  const tRef = useRef(t);
  useLayoutEffect(() => {
    filterRef.current = { search: query, includeArchived };
    tRef.current = t;
  }, [query, includeArchived, t]);

  const reportError = useCallback((message: string) => pushToast('error', message), [pushToast]);

  // Remembered choices.
  useEffect(() => {
    let cancelled = false;
    void Promise.all([
      readPref<string>(ASK_PREF_KEYS.captain),
      readPref<boolean>(ASK_PREF_KEYS.showThinking),
      readPref<string>(ASK_PREF_KEYS.lastThread),
    ]).then(([captain, thinking, last]) => {
      if (cancelled) return;
      if (typeof captain === 'string') setDraftCaptainIdState((current) => current || captain);
      if (thinking === true) setShowThinkingState(true);
      setLastThreadId(typeof last === 'string' && last ? last : null);
      setLastThreadLoaded(true);
    });
    return () => { cancelled = true; };
  }, []);

  const setDraftCaptainId = useCallback((id: string) => {
    setDraftCaptainIdState(id);
    void writePref(ASK_PREF_KEYS.captain, id);
  }, []);

  const setShowThinking = useCallback((value: boolean) => {
    setShowThinkingState(value);
    void writePref(ASK_PREF_KEYS.showThinking, value);
  }, []);

  const setOpenThreadId = useCallback((id: string | null) => {
    openRef.current = id;
    setOpenThreadIdState(id);
    setOpenAskThread(id);
    if (id) {
      setLastThreadId(id);
      void writePref(ASK_PREF_KEYS.lastThread, id);
    }
  }, []);

  // Captains and quick actions (once per session; the captain list also refreshes after a reconnect).
  useEffect(() => {
    listCaptains({ pageSize: 200 })
      .then((result) => {
        const list = result?.objects || [];
        setCaptains(list);
        setDraftCaptainIdState((current) => (current && list.some((c) => c.id === current) ? current : list[0]?.id ?? ''));
      })
      .catch(() => setCaptains([]));
    getAskQuickActions()
      .then((catalog) => setQuickActions(mergeQuickActions(catalog)))
      .catch(() => setQuickActions(DEFAULT_QUICK_ACTIONS));
  }, [reconnectCount]);

  const loadCaptainTools = useCallback(async (captainId: string): Promise<CaptainToolAccessResult | null> => {
    const cached = toolsCache.current[captainId];
    if (cached) return cached;
    try {
      const result = await getCaptainTools(captainId);
      if (result) toolsCache.current[captainId] = result;
      return result ?? null;
    } catch {
      return null;
    }
  }, []);

  // Search is matched server-side, debounced as on the dashboard.
  useEffect(() => {
    const handle = setTimeout(() => setQuery(search.trim()), SEARCH_DEBOUNCE_MS);
    return () => clearTimeout(handle);
  }, [search]);

  const loadThreads = useCallback(async (page: number) => {
    setListLoading(true);
    setListError(null);
    try {
      const result = await enumerateAskThreads({ pageNumber: page, pageSize: THREAD_PAGE_SIZE, search: filterRef.current.search, includeArchived: filterRef.current.includeArchived });
      const openId = openRef.current;
      const incoming = (result?.objects || []).map((th) => (th.id === openId ? { ...th, unreadCount: 0 } : th));
      setThreads((prev) => (page === 1 ? incoming : sortThreads([...prev.filter((p) => !incoming.some((i) => i.id === p.id)), ...incoming])));
      setListPage(page);
      setListHasMore(page < (result?.totalPages || 1));
    } catch (err: unknown) {
      setListError(errorText(err, tRef.current('Failed to load conversations.')));
    } finally {
      setListLoading(false);
    }
  }, []);

  // Reload the first page when the filter changes and after every reconnect (the dashboard's behavior).
  // eslint-disable-next-line react-hooks/set-state-in-effect -- a fetch keyed on the filter; it sets loading state first
  useEffect(() => { void loadThreads(1); }, [query, includeArchived, reconnectCount, loadThreads]);

  const markRead = useCallback((id: string) => {
    if (AppState.currentState !== 'active') return;
    setThreads((prev) => prev.map((th) => (th.id === id && th.unreadCount ? { ...th, unreadCount: 0 } : th)));
    markAskThreadRead(id).catch(() => { /* best effort; the badge resyncs on the next ask.thread */ });
  }, []);

  // Thread list and activity follow every ask.* event (the conversation hook handles its own thread's events).
  useEffect(() => subscribe((msg: WebSocketMessage) => {
    const event = parseAskEvent(msg);
    if (!event) return;
    setActivity((prev) => applyActivityEvent(prev, event));
    if (event.type === 'ask.thread') {
      const openId = openRef.current;
      setThreads((prev) => applyThreadUpdate(prev, event.thread, filterRef.current, openId));
      if (event.threadId === openId && (event.thread.unreadCount ?? 0) > 0) markRead(event.threadId);
    }
  }), [subscribe, markRead]);

  // Returning to the app marks the open conversation read (the dashboard does it on tab visibility).
  useEffect(() => {
    const sub = AppState.addEventListener('change', (next) => {
      if (next === 'active' && openRef.current) markRead(openRef.current);
    });
    return () => sub.remove();
  }, [markRead]);

  const upsertThread = useCallback((thread: AskThread) => {
    setThreads((prev) => applyThreadUpdate(prev, thread, filterRef.current, openRef.current));
  }, []);

  const updateThread = useCallback(async (target: AskThread, patch: AskThreadUpdateRequest): Promise<AskThread | null> => {
    try {
      const updated = await updateAskThread(target.id, patch);
      const merged = { ...target, ...patch, ...updated } as AskThread;
      setThreads((prev) => applyThreadUpdate(prev, merged, filterRef.current, openRef.current));
      if (patch.captainId) setDraftCaptainId(patch.captainId);
      return merged;
    } catch (err: unknown) {
      reportError(errorText(err, tRef.current('The conversation could not be updated.')));
      return null;
    }
  }, [reportError, setDraftCaptainId]);

  const changeCliPolicy = useCallback(async (target: AskThread, policy: CliPermissionPolicy | null): Promise<AskThread | null> => {
    try {
      const updated = await setAskThreadCliPermissionPolicy(target.id, policy);
      const merged = { ...target, ...updated, cliPermissionPolicy: updated?.cliPermissionPolicy ?? policy } as AskThread;
      setThreads((prev) => applyThreadUpdate(prev, merged, filterRef.current, openRef.current));
      if (policy === 'Bypass') pushToast('warning', tRef.current('CLI tools bypass is on: the captain runs any command in this conversation without asking.'));
      return merged;
    } catch (err: unknown) {
      reportError(errorText(err, tRef.current('The CLI tools policy could not be changed.')));
      return null;
    }
  }, [pushToast, reportError]);

  const summarize = useCallback(async (target: AskThread) => {
    try {
      await summarizeAskThread(target.id);
      pushToast('info', tRef.current('Summarizing "{{title}}". The summary will appear in the conversation.', { title: target.title || tRef.current('New conversation') }));
    } catch (err: unknown) {
      reportError(errorText(err, tRef.current('The conversation could not be summarized.')));
    }
  }, [pushToast, reportError]);

  const deleteThread = useCallback(async (target: AskThread): Promise<boolean> => {
    try {
      await deleteAskThread(target.id);
      setThreads((prev) => prev.filter((th) => th.id !== target.id));
      pushToast('success', tRef.current('Conversation deleted.'));
      setLastThreadId((current) => (current === target.id ? null : current));
      return true;
    } catch (err: unknown) {
      reportError(errorText(err, tRef.current('The conversation could not be deleted.')));
      return false;
    }
  }, [pushToast, reportError]);

  const captainNames = useMemo(() => Object.fromEntries(captains.map((c) => [c.id, c.name])), [captains]);

  const value = useMemo<AskState>(() => ({
    captains, captainNames, draftCaptainId, setDraftCaptainId, quickActions, showThinking, setShowThinking, loadCaptainTools,
    threads, listLoading, listError, search, setSearch, includeArchived, setIncludeArchived, listHasMore,
    loadMoreThreads: () => { void loadThreads(listPage + 1); },
    reloadThreads: () => { void loadThreads(1); },
    activity, openThreadId, setOpenThreadId, lastThreadId, lastThreadLoaded, markRead, upsertThread, updateThread,
    changeCliPolicy, summarize, deleteThread, reportError,
  }), [captains, captainNames, draftCaptainId, setDraftCaptainId, quickActions, showThinking, setShowThinking, loadCaptainTools,
    threads, listLoading, listError, search, includeArchived, listHasMore, loadThreads, listPage, activity, openThreadId,
    setOpenThreadId, lastThreadId, lastThreadLoaded, markRead, upsertThread, updateThread, changeCliPolicy, summarize,
    deleteThread, reportError]);

  return <AskContext.Provider value={value}>{children}</AskContext.Provider>;
}

export function useAsk(): AskState {
  const ctx = useContext(AskContext);
  if (!ctx) throw new Error('useAsk must be used within AskProvider');
  return ctx;
}
