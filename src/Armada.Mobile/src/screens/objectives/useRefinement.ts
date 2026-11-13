import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import {
  applyObjectiveRefinementSummary,
  createBacklogRefinementSession,
  deleteObjectiveRefinementSession,
  listBacklogRefinementSessions,
  sendObjectiveRefinementMessage,
  stopObjectiveRefinementSession,
  summarizeObjectiveRefinementSession,
} from '@dashboard/api/client';
import type {
  Objective,
  ObjectiveRefinementMessage,
  ObjectiveRefinementSession,
  ObjectiveRefinementSessionCreateRequest,
  ObjectiveRefinementSessionDetail,
  ObjectiveRefinementSessionView,
  ObjectiveRefinementSummaryResponse,
  WebSocketMessage,
} from '@dashboard/types/models';
import {
  getLatestAssistantRefinementMessage,
  removeRefinementSession,
  upsertRefinementMessage,
  upsertRefinementSession,
} from '@dashboard/lib/backlogRefinement';
import { refinementSummaryFromEvent } from '@dashboard/lib/refinementSummary';
import { mergeSessionDetail, newerOf } from '@dashboard/lib/liveMerge';
import { loadRefinementSessionView, withOlderRefinementMessages, withStoppedSession } from '@dashboard/lib/sessionViews';
import { useSocket } from '../../socket/SocketContext';

/** Refinement sessions read per page. */
const SESSION_PAGE_SIZE = 100;

export interface Refinement {
  sessions: ObjectiveRefinementSession[];
  /** Number of the item's refinement sessions on the server (every page). */
  sessionsTotal: number;
  /** Loads the next page of sessions (offered while fewer than sessionsTotal are loaded). */
  loadMoreSessions: () => Promise<void>;
  selectedSessionId: string;
  selectSession: (id: string) => void;
  /** The session with the transcript pages loaded so far (oldest first); messageCount is the full length. */
  detail: ObjectiveRefinementSessionView | null;
  /** Loads the next older transcript page (offered while fewer than messageCount messages are loaded). */
  loadOlder: () => Promise<void>;
  /** Transcript message the summary / apply actions use (defaults to the latest assistant reply). */
  selectedMessageId: string;
  selectMessage: (id: string) => void;
  summaryDraft: ObjectiveRefinementSummaryResponse | null;
  start: (request: ObjectiveRefinementSessionCreateRequest) => Promise<ObjectiveRefinementSessionDetail>;
  send: (content: string) => Promise<void>;
  summarize: () => Promise<ObjectiveRefinementSummaryResponse>;
  /** Apply the summary to the item; resolves to the updated item. */
  apply: () => Promise<Objective>;
  stop: () => Promise<void>;
  remove: () => Promise<void>;
}

/**
 * Backlog refinement for one item (the dashboard ObjectiveDetail's refinement half): the item's sessions, the
 * selected transcript, and the actions, kept live through the objective-refinement-session.* events (changed,
 * message.created / message.updated, summary.created, applied, deleted). `onObjective` receives the item whenever
 * a refinement event or apply changes it.
 */
export function useRefinement(objectiveId: string | null, requestedSessionId: string, onObjective: (objective: Objective) => void): Refinement {
  const { subscribe, reconnectCount } = useSocket();
  const [sessions, setSessions] = useState<ObjectiveRefinementSession[]>([]);
  const [sessionsTotal, setSessionsTotal] = useState(0);
  const [selectedSessionId, setSelectedSessionId] = useState('');
  const [detail, setDetail] = useState<ObjectiveRefinementSessionView | null>(null);
  const [chosenMessageId, setChosenMessageId] = useState('');
  const [summaryDraft, setSummaryDraft] = useState<ObjectiveRefinementSummaryResponse | null>(null);
  const onObjectiveRef = useRef(onObjective);
  const selectedRef = useRef('');
  useEffect(() => {
    onObjectiveRef.current = onObjective;
    selectedRef.current = selectedSessionId;
  });

  const loadSessions = useCallback(async (preferred?: string) => {
    if (!objectiveId) return;
    const page = await listBacklogRefinementSessions(objectiveId, { pageNumber: 1, pageSize: SESSION_PAGE_SIZE });
    const list = page?.objects ?? [];
    setSessions(list);
    setSessionsTotal(page?.totalRecords ?? list.length);
    const current = selectedRef.current;
    const next = preferred || (list.some((s) => s.id === current) ? current : '') || list[0]?.id || '';
    selectedRef.current = next;
    setSelectedSessionId(next);
    setDetail(next ? (await loadRefinementSessionView(next)) ?? null : null);
  }, [objectiveId]);

  useEffect(() => {
    if (!objectiveId) return;
    // eslint-disable-next-line react-hooks/set-state-in-effect -- a fetch keyed on the item and the requested session
    void loadSessions(requestedSessionId || undefined).catch(() => { /* the item page shows its own load errors */ });
  }, [objectiveId, requestedSessionId, reconnectCount, loadSessions]);

  const selectSession = useCallback((id: string) => {
    selectedRef.current = id;
    setSelectedSessionId(id);
    setSummaryDraft(null);
    setChosenMessageId('');
    void loadRefinementSessionView(id).then((d) => setDetail(d ?? null)).catch(() => setDetail(null));
  }, []);

  // The selected message: the user's choice while it exists in the transcript, else the latest assistant reply.
  const selectedMessageId = useMemo(() => {
    if (!detail) return '';
    if (chosenMessageId && detail.messages.some((m) => m.id === chosenMessageId)) return chosenMessageId;
    return getLatestAssistantRefinementMessage(detail.messages)?.id ?? '';
  }, [detail, chosenMessageId]);

  useEffect(() => {
    if (!objectiveId) return undefined;
    return subscribe((msg: WebSocketMessage) => {
      const type = typeof msg.type === 'string' ? msg.type : '';
      if (!type.startsWith('objective-refinement-session.')) return;
      if (type === 'objective-refinement-session.changed') {
        const payload = msg.data as { session?: ObjectiveRefinementSession } | undefined;
        const session = payload?.session;
        if (!session || session.objectiveId !== objectiveId) return;
        setSessions((current) => upsertRefinementSession(current, session));
        setDetail((current) => (current && current.session.id === session.id ? { ...current, session } : current));
        return;
      }
      if (type === 'objective-refinement-session.message.created' || type === 'objective-refinement-session.message.updated') {
        const payload = msg.data as { sessionId?: string; objectiveId?: string; message?: ObjectiveRefinementMessage } | undefined;
        const message = payload?.message;
        if (!payload?.sessionId || payload.objectiveId !== objectiveId || !message) return;
        setDetail((current) => (current && current.session.id === payload.sessionId
          ? {
            ...current,
            messages: upsertRefinementMessage(current.messages, message),
            messageCount: current.messages.some((m) => m.id === message.id) ? current.messageCount : (current.messageCount ?? 0) + 1,
          }
          : current));
        return;
      }
      if (type === 'objective-refinement-session.summary.created') {
        const draft = refinementSummaryFromEvent(msg.data);
        if (!draft || draft.sessionId !== selectedRef.current) return;
        setSummaryDraft(draft);
        if (draft.messageId) setChosenMessageId(draft.messageId);
        return;
      }
      if (type === 'objective-refinement-session.applied') {
        const payload = msg.data as { objective?: Objective; summary?: ObjectiveRefinementSummaryResponse } | undefined;
        if (!payload?.objective || payload.objective.id !== objectiveId) return;
        onObjectiveRef.current(payload.objective);
        if (payload.summary) setSummaryDraft(payload.summary);
        return;
      }
      if (type === 'objective-refinement-session.deleted') {
        const payload = msg.data as { sessionId?: string; objectiveId?: string } | undefined;
        if (!payload?.sessionId || payload.objectiveId !== objectiveId) return;
        const deletedId = payload.sessionId;
        setSessions((current) => removeRefinementSession(current, deletedId));
        if (selectedRef.current === deletedId) {
          selectedRef.current = '';
          setSelectedSessionId('');
          setDetail(null);
          setSummaryDraft(null);
        }
      }
    });
  }, [objectiveId, subscribe]);

  const start = useCallback(async (request: ObjectiveRefinementSessionCreateRequest) => {
    if (!objectiveId) throw new Error('No backlog item');
    const created = await createBacklogRefinementSession(objectiveId, request);
    setSessions((current) => upsertRefinementSession(current, created.session));
    selectedRef.current = created.session.id;
    setSelectedSessionId(created.session.id);
    setDetail({ ...created, messages: [] });
    setSummaryDraft(null);
    await loadSessions(created.session.id);
    return created;
  }, [objectiveId, loadSessions]);

  const send = useCallback(async (content: string) => {
    if (!detail) return;
    const next = await sendObjectiveRefinementMessage(detail.session.id, { content });
    // A fast captain can finish the turn (over the socket) before this response arrives; keep the newer state.
    setDetail((current) => mergeSessionDetail(current, next));
    setSessions((current) => {
      const existing = current.find((s) => s.id === next.session.id);
      return upsertRefinementSession(current, existing ? newerOf(existing, next.session) : next.session);
    });
  }, [detail]);

  const summarize = useCallback(async () => {
    if (!detail) throw new Error('No refinement session');
    const summary = await summarizeObjectiveRefinementSession(detail.session.id, { messageId: selectedMessageId || undefined });
    setSummaryDraft(summary);
    if (summary.messageId) setChosenMessageId(summary.messageId);
    return summary;
  }, [detail, selectedMessageId]);

  const apply = useCallback(async () => {
    if (!detail) throw new Error('No refinement session');
    const result = await applyObjectiveRefinementSummary(detail.session.id, {
      messageId: selectedMessageId || undefined,
      markMessageSelected: true,
      promoteBacklogState: true,
    });
    setSummaryDraft(result.summary);
    onObjectiveRef.current(result.objective);
    await loadSessions(detail.session.id);
    return result.objective;
  }, [detail, selectedMessageId, loadSessions]);

  const stop = useCallback(async () => {
    if (!detail) return;
    const next = await stopObjectiveRefinementSession(detail.session.id);
    setDetail((current) => withStoppedSession(current, next));
    setSessions((current) => upsertRefinementSession(current, next.session));
  }, [detail]);

  const remove = useCallback(async () => {
    if (!detail) return;
    await deleteObjectiveRefinementSession(detail.session.id);
    setSessions((current) => removeRefinementSession(current, detail.session.id));
    selectedRef.current = '';
    setSelectedSessionId('');
    setDetail(null);
    setSummaryDraft(null);
    await loadSessions();
  }, [detail, loadSessions]);

  const loadOlder = useCallback(async () => {
    if (!detail || detail.messages.length >= (detail.messageCount ?? 0)) return;
    const next = await withOlderRefinementMessages(detail);
    setDetail((current) => (current && current.session.id === next.session.id ? { ...current, messages: next.messages, messageCount: next.messageCount } : current));
  }, [detail]);

  const loadMoreSessions = useCallback(async () => {
    if (!objectiveId) return;
    const page = await listBacklogRefinementSessions(objectiveId, { pageNumber: Math.floor(sessions.length / SESSION_PAGE_SIZE) + 1, pageSize: SESSION_PAGE_SIZE });
    setSessions((current) => {
      let merged = current;
      for (const s of page?.objects ?? []) if (!merged.some((x) => x.id === s.id)) merged = [...merged, s];
      return merged;
    });
    setSessionsTotal(page?.totalRecords ?? sessionsTotal);
  }, [objectiveId, sessions.length, sessionsTotal]);

  return {
    sessions, sessionsTotal, loadMoreSessions, selectedSessionId, selectSession, detail, loadOlder, selectedMessageId, selectMessage: setChosenMessageId, summaryDraft,
    start, send, summarize, apply, stop, remove,
  };
}
