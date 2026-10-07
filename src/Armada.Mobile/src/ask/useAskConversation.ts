import { useCallback, useEffect, useLayoutEffect, useReducer, useRef, useState } from 'react';
import { AccessibilityInfo } from 'react-native';
import {
  ApiError,
  approveAskProposal,
  cancelAskTurn,
  createAskThread,
  enumerateAskMessages,
  getAskThread,
  getAskWorkSnapshot,
  rejectAskProposal,
  runAskQuickAction,
  sendAskMessage,
} from '@dashboard/api/client';
import type { AskActionProposal, AskMessage, AskQuickAction, AskThread, AskTrackedWork, CliPermissionRequest, WebSocketMessage } from '@dashboard/types/models';
import { parseAskEvent } from '@dashboard/lib/askEvents';
import { conversationReducer, initialConversation, isLocalMessage, type ConversationState } from '@dashboard/lib/askConversation';
import { isWorkActive } from '@dashboard/lib/askWork';
import { isPendingRequest, parseCliPermissionEvent } from '@dashboard/lib/cliPermissions';
import { randomThinkingMessage } from '@dashboard/lib/askThinkingMessages';
import { useLocale } from '../i18n/LocaleContext';
import { useNotifications } from '../notifications/NotificationContext';
import { useSocket } from '../socket/SocketContext';
import { errorText, useAsk } from './AskContext';

export const MESSAGE_PAGE_SIZE = 30;
const SNAPSHOT_FETCH_LIMIT = 20;
/** The server confirms a cancel with ask.turn; if that never arrives, the UI settles after this grace period. */
export const STOP_GRACE_MS = 8000;
const WAITING_ROTATE_MS = 4000;

export interface AskConversation {
  conv: ConversationState;
  loading: boolean;
  error: string | null;
  loadingOlder: boolean;
  busyProposalId: string | null;
  actionBusy: boolean;
  stopping: boolean;
  /** Rotating "waiting" text while the captain has not produced output yet. */
  waitingText: string;
  reload: () => void;
  send: (text: string) => Promise<void>;
  stop: () => void;
  runQuickAction: (action: AskQuickAction, args: Record<string, unknown>) => Promise<boolean>;
  decide: (proposal: AskActionProposal, approve: boolean) => Promise<void>;
  cliDecided: (request: CliPermissionRequest) => void;
  /** Fold an updated copy of the open thread (rename, captain, auto-approve, CLI policy) into the conversation. */
  applyThread: (thread: AskThread) => void;
  loadOlder: () => Promise<void>;
}

export interface UseAskConversationOptions {
  /** The conversation to show; null is the new-conversation screen. */
  threadId: string | null;
  /** A conversation was created by the first message or quick action: show it (no remount). */
  onCreated: (id: string) => void;
  /** Injectable timers for tests. */
  schedule?: (fn: () => void, ms: number) => unknown;
}

/**
 * The open conversation, driven exactly as the dashboard's AskArmada page drives it: the shared
 * conversationReducer folds REST pages and `ask.*` / `cli_permission.*` socket events (streaming text, thinking,
 * tool chips, proposals, tracked work, CLI permission requests) into one state; optimistic user messages; stop
 * with a grace period; quick actions; approve / reject; older pages; reconciliation after every reconnect.
 */
export function useAskConversation({ threadId, onCreated, schedule = (fn, ms) => setTimeout(fn, ms) }: UseAskConversationOptions): AskConversation {
  const { t } = useLocale();
  const { subscribe, reconnectCount } = useSocket();
  const { pushToast } = useNotifications();
  const { draftCaptainId, showThinking, markRead, upsertThread, reportError } = useAsk();

  const [conv, dispatch] = useReducer(conversationReducer, threadId, initialConversation);
  const convRef = useRef(conv);
  // Event handlers and async continuations read the latest state through this ref (synced before effects run).
  useLayoutEffect(() => { convRef.current = conv; }, [conv]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [loadingOlder, setLoadingOlder] = useState(false);
  const [busyProposalId, setBusyProposalId] = useState<string | null>(null);
  const [actionBusy, setActionBusy] = useState(false);
  const [stopping, setStopping] = useState(false);
  const [waitingText, setWaitingText] = useState('');
  const tRef = useRef(t);
  useLayoutEffect(() => { tRef.current = t; }, [t]);
  const announce = useCallback((text: string) => { AccessibilityInfo.announceForAccessibility(text); }, []);

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
    if (!silent) setLoading(true);
    setError(null);
    try {
      const [detail, page] = await Promise.all([getAskThread(id), enumerateAskMessages(id, { pageSize: MESSAGE_PAGE_SIZE })]);
      if (convRef.current.threadId !== id) return;
      dispatch({ type: 'loaded', threadId: id, detail, messages: page?.messages ?? [], hasMore: !!page?.hasMore });
      if (detail?.thread) upsertThread(detail.thread);
      markRead(id);
      fetchSnapshots(id, detail?.trackedWork ?? []);
    } catch (err: unknown) {
      if (convRef.current.threadId !== id) return;
      if (err instanceof ApiError && err.status === 404) setError(tRef.current('This conversation was not found. It may have been deleted.'));
      else setError(errorText(err, tRef.current('Failed to load the conversation.')));
    } finally {
      if (convRef.current.threadId === id) setLoading(false);
    }
  }, [fetchSnapshots, markRead, upsertThread]);

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

  // A different conversation (or the new-conversation screen).
  useEffect(() => {
    if (convRef.current.threadId !== threadId) dispatch({ type: 'reset', threadId });
    // eslint-disable-next-line react-hooks/set-state-in-effect -- opening a conversation clears its error and starts its fetch
    setError(null);
    setStopping(false);
    if (threadId) void loadConversation(threadId);
    else setLoading(false);
  }, [threadId, loadConversation]);

  // After a reconnect or a return to the foreground, refetch so nothing missed while offline is lost.
  useEffect(() => {
    if (reconnectCount === 0) return;
    const id = convRef.current.threadId;
    if (id) void loadConversation(id, true);
  }, [reconnectCount, loadConversation]);

  // Rotate the waiting message while a captain turn has not produced text yet.
  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- a turn ending settles Stop; a turn starting picks the first waiting text
    if (!conv.turnActive) { setStopping(false); return undefined; }
    setWaitingText((prev) => randomThinkingMessage(prev));
    const id = setInterval(() => setWaitingText((prev) => randomThinkingMessage(prev)), WAITING_ROTATE_MS);
    return () => clearInterval(id);
  }, [conv.turnActive]);

  useEffect(() => subscribe((msg: WebSocketMessage) => {
    const cli = parseCliPermissionEvent(msg);
    if (cli) {
      if (!cli.request.threadId || cli.request.threadId !== convRef.current.threadId) return;
      dispatch({ type: 'cliPermission', request: cli.request });
      if (cli.type === 'cli_permission.requested' && isPendingRequest(cli.request)) {
        announce(tRef.current('The captain needs permission to run {{tool}}: {{summary}}', { tool: cli.request.toolName, summary: cli.request.summaryText || cli.request.toolName }));
      }
      return;
    }
    const event = parseAskEvent(msg);
    if (!event) return;
    const openId = convRef.current.threadId;
    if (event.threadId !== openId) return;

    const before = convRef.current;
    dispatch({ type: 'event', event });
    const tr = tRef.current;

    if (event.type === 'ask.turn' && event.state !== 'started') {
      void refreshLatest(event.threadId);
    } else if (event.type === 'ask.message' && event.message.role !== 'User') {
      const kind = String(event.message.kind);
      const snippet = (event.message.contentText ?? '').replace(/\s+/g, ' ').slice(0, 160);
      if (kind === 'WorkUpdate') announce(tr('Work update: {{text}}', { text: snippet }));
      else if (kind === 'Error') announce(tr('Error: {{text}}', { text: snippet }));
      else if (kind !== 'ActionProposal' && kind !== 'CliPermission') announce(tr('New message: {{text}}', { text: snippet }));
    } else if (event.type === 'ask.proposal' && String(event.proposal.status).toLowerCase() === 'pending') {
      announce(tr('An action needs your approval: {{summary}}', { summary: event.proposal.summaryText || event.proposal.toolName }));
    } else if (event.type === 'ask.work' && event.snapshot) {
      const prior = before.trackedWork.find((w) => w.id === event.trackedWorkId);
      if (!prior || prior.status !== event.snapshot.status) {
        announce(tr('{{title}} is now {{status}}', { title: prior?.title || event.snapshot.title || event.snapshot.entityId, status: tr(event.snapshot.status) }));
      }
      if (!prior) void refreshDetail(event.threadId);
    }
  }), [subscribe, refreshLatest, refreshDetail, announce]);

  const ensureThread = useCallback(async (): Promise<string> => {
    const current = convRef.current.threadId;
    if (current) return current;
    const created = await createAskThread({ captainId: draftCaptainId || null });
    dispatch({ type: 'reset', threadId: created.id });
    dispatch({ type: 'thread', thread: created });
    convRef.current = { ...initialConversation(created.id), thread: created };
    upsertThread(created);
    onCreated(created.id);
    return created.id;
  }, [draftCaptainId, upsertThread, onCreated]);

  const send = useCallback(async (text: string) => {
    let id: string;
    try {
      id = await ensureThread();
    } catch (err: unknown) {
      reportError(errorText(err, tRef.current('Failed to start a conversation.')));
      return;
    }
    const localId = `local-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 8)}`;
    const maxSeq = convRef.current.messages.reduce((max, m) => Math.max(max, m.sequence), 0);
    const optimistic: AskMessage = {
      id: localId, threadId: id, sequence: maxSeq + 1, role: 'User', kind: 'Text', contentText: text, createdUtc: new Date().toISOString(), isLocal: true,
    };
    dispatch({ type: 'optimisticUser', message: optimistic });
    try {
      const result = await sendAskMessage(id, text, showThinking);
      dispatch({ type: 'confirmUser', localId, messageId: result?.messageId ?? null, turnId: result?.turnId ?? null });
    } catch (err: unknown) {
      dispatch({ type: 'dropOptimistic', localId });
      reportError(errorText(err, tRef.current('The message could not be sent.')));
    }
  }, [ensureThread, showThinking, reportError]);

  const stop = useCallback(() => {
    const id = convRef.current.threadId;
    if (!id) return;
    setStopping(true);
    cancelAskTurn(id)
      .then(() => {
        schedule(() => {
          if (convRef.current.threadId === id && convRef.current.turnActive) {
            dispatch({ type: 'turnEnded' });
            void refreshLatest(id);
          }
        }, STOP_GRACE_MS);
      })
      .catch((err: unknown) => { setStopping(false); reportError(errorText(err, tRef.current('Could not stop the captain.'))); });
  }, [refreshLatest, reportError, schedule]);

  const runQuickAction = useCallback(async (action: AskQuickAction, args: Record<string, unknown>): Promise<boolean> => {
    if (!action.toolName) return false;
    setActionBusy(true);
    try {
      const id = await ensureThread();
      const proposal = await runAskQuickAction(id, action.toolName, args);
      if (proposal?.id) dispatch({ type: 'proposal', proposal });
      await Promise.all([refreshLatest(id), refreshDetail(id)]);
      if (proposal && String(proposal.status).toLowerCase() === 'failed') {
        pushToast('error', tRef.current('{{command}} failed: {{reason}}', { command: action.command || action.name, reason: proposal.errorText || tRef.current('unknown error') }));
      }
      return true;
    } catch (err: unknown) {
      reportError(errorText(err, tRef.current('The quick action failed.')));
      return false;
    } finally {
      setActionBusy(false);
    }
  }, [ensureThread, refreshLatest, refreshDetail, pushToast, reportError]);

  const decide = useCallback(async (proposal: AskActionProposal, approve: boolean) => {
    const id = convRef.current.threadId;
    if (!id) return;
    setBusyProposalId(proposal.id);
    try {
      const updated = approve ? await approveAskProposal(id, proposal.id) : await rejectAskProposal(id, proposal.id);
      if (updated?.id) dispatch({ type: 'proposal', proposal: updated });
      void refreshLatest(id);
      void refreshDetail(id);
    } catch (err: unknown) {
      reportError(errorText(err, approve ? tRef.current('The action could not be approved.') : tRef.current('The action could not be rejected.')));
    } finally {
      setBusyProposalId(null);
    }
  }, [refreshLatest, refreshDetail, reportError]);

  const cliDecided = useCallback((request: CliPermissionRequest) => {
    dispatch({ type: 'cliPermission', request });
  }, []);

  const applyThread = useCallback((thread: AskThread) => {
    dispatch({ type: 'thread', thread });
  }, []);

  const loadOlder = useCallback(async () => {
    const id = convRef.current.threadId;
    if (!id || loadingOlder) return;
    const oldest = convRef.current.messages.find((m) => !isLocalMessage(m));
    if (!oldest) return;
    setLoadingOlder(true);
    try {
      const page = await enumerateAskMessages(id, { beforeSequence: oldest.sequence, pageSize: MESSAGE_PAGE_SIZE });
      dispatch({ type: 'older', threadId: id, messages: page?.messages ?? [], hasMore: !!page?.hasMore });
    } catch (err: unknown) {
      reportError(errorText(err, tRef.current('Failed to load earlier messages.')));
    } finally {
      setLoadingOlder(false);
    }
  }, [loadingOlder, reportError]);

  const reload = useCallback(() => {
    const id = convRef.current.threadId;
    if (id) void loadConversation(id);
  }, [loadConversation]);

  return { conv, loading, error, loadingOlder, busyProposalId, actionBusy, stopping, waitingText, reload, send, stop, runQuickAction, decide, cliDecided, applyThread, loadOlder };
}
