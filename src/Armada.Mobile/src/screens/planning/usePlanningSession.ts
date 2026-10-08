import { useCallback, useEffect, useRef, useState } from 'react';
import { getPlanningSession } from '@dashboard/api/client';
import type { PlanningSession, PlanningSessionDetail, PlanningSessionMessage, WebSocketMessage } from '@dashboard/types/models';
import { applyToolEvent, type ToolEvent, type ToolEventMessage } from '@dashboard/lib/toolEvents';
import { upsertMessage } from '@dashboard/lib/planningSessions';
import { errorMessage } from '../../build/useLiveResource';
import { useSocket } from '../../socket/SocketContext';

/** The planning-session.summary.created payload: a dispatch draft summarized from a reply. */
export interface PlanningSummaryEvent {
  sessionId?: string;
  messageId?: string;
  draft?: { title?: string; description?: string; method?: string };
}

export interface PlanningSessionHandlers {
  /** A summary draft arrived for this session (from this device or another). */
  onSummary?: (event: PlanningSummaryEvent) => void;
  /** A voyage was dispatched from this session. */
  onDispatched?: (voyageId: string) => void;
  /** The session was deleted (here or elsewhere). */
  onDeleted?: () => void;
}

export interface PlanningSessionState {
  detail: PlanningSessionDetail | null;
  setDetail: (next: PlanningSessionDetail | null | ((prev: PlanningSessionDetail | null) => PlanningSessionDetail | null)) => void;
  loading: boolean;
  error: string | null;
  /** Tool calls streamed for each assistant message (planning-session.tool). */
  tools: Record<string, ToolEvent[]>;
  /** Reasoning streamed for each assistant message (planning-session.thinking). */
  thinking: Record<string, string>;
  reload: () => Promise<void>;
}

/**
 * One planning session kept live the way the dashboard's Planning page does it: the detail loads once and then
 * follows the planning-session.* events in place (message.created/updated upsert by sequence, changed replaces the
 * session, tool and thinking stream into per-message state, captain.changed updates the reserved captain's state),
 * and reloads after a socket reconnect or a return to the foreground.
 */
export function usePlanningSession(sessionId: string, handlers: PlanningSessionHandlers = {}): PlanningSessionState {
  const { subscribe, reconnectCount } = useSocket();
  const [detail, setDetail] = useState<PlanningSessionDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [tools, setTools] = useState<Record<string, ToolEvent[]>>({});
  const [thinking, setThinking] = useState<Record<string, string>>({});
  const handlersRef = useRef(handlers);
  useEffect(() => { handlersRef.current = handlers; });
  const generation = useRef(0);

  const reload = useCallback(async () => {
    const gen = generation.current;
    try {
      const result = await getPlanningSession(sessionId);
      if (gen !== generation.current) return;
      setDetail(result);
      setError(null);
    } catch (e) {
      if (gen !== generation.current) return;
      setError(errorMessage(e));
    } finally {
      if (gen === generation.current) setLoading(false);
    }
  }, [sessionId]);

  useEffect(() => {
    generation.current += 1;
    /* eslint-disable react-hooks/set-state-in-effect -- a fetch keyed on the session id; it resets state first */
    setLoading(true);
    setDetail(null);
    setError(null);
    setTools({});
    setThinking({});
    /* eslint-enable react-hooks/set-state-in-effect */
    void reload();
  }, [reload]);

  const firstReconnect = useRef(reconnectCount);
  useEffect(() => {
    if (reconnectCount !== firstReconnect.current) void reload();
  }, [reconnectCount, reload]);

  useEffect(() => subscribe((msg: WebSocketMessage) => {
    const type = typeof msg.type === 'string' ? msg.type : '';
    if (type === 'planning-session.changed') {
      const session = (msg.data as { session?: PlanningSession } | undefined)?.session;
      if (!session || session.id !== sessionId) return;
      setDetail((current) => (current ? { ...current, session } : current));
      return;
    }
    if (type === 'captain.changed') {
      const d = msg.data as { id?: string; name?: string; state?: string } | undefined;
      if (!d?.id || !d.state) return;
      setDetail((current) => {
        if (!current?.captain || current.captain.id !== d.id) return current;
        return { ...current, captain: { ...current.captain, state: d.state!, name: d.name ?? current.captain.name } };
      });
      return;
    }
    if (type === 'planning-session.message.created' || type === 'planning-session.message.updated') {
      const d = msg.data as { sessionId?: string; message?: PlanningSessionMessage } | undefined;
      if (!d?.message || d.sessionId !== sessionId) return;
      setDetail((current) => (current ? { ...current, messages: upsertMessage(current.messages, d.message!) } : current));
      return;
    }
    if (type === 'planning-session.tool') {
      const d = msg.data as ({ sessionId?: string; messageId?: string } & ToolEventMessage) | undefined;
      if (!d || d.sessionId !== sessionId || !d.messageId || !d.id) return;
      setTools((current) => ({ ...current, [d.messageId!]: applyToolEvent(current[d.messageId!], d) }));
      return;
    }
    if (type === 'planning-session.thinking') {
      const d = msg.data as { sessionId?: string; messageId?: string; delta?: string } | undefined;
      if (!d || d.sessionId !== sessionId || !d.messageId || !d.delta) return;
      setThinking((current) => ({ ...current, [d.messageId!]: (current[d.messageId!] ?? '') + d.delta }));
      return;
    }
    if (type === 'planning-session.summary.created') {
      const d = msg.data as PlanningSummaryEvent | undefined;
      if (!d?.draft || d.sessionId !== sessionId) return;
      handlersRef.current.onSummary?.(d);
      return;
    }
    if (type === 'planning-session.dispatch.created') {
      const d = msg.data as { sessionId?: string; voyageId?: string } | undefined;
      if (!d?.voyageId || d.sessionId !== sessionId) return;
      handlersRef.current.onDispatched?.(d.voyageId);
      return;
    }
    if (type === 'planning-session.deleted') {
      const d = msg.data as { sessionId?: string } | undefined;
      if (d?.sessionId !== sessionId) return;
      handlersRef.current.onDeleted?.();
    }
  }), [subscribe, sessionId]);

  return { detail, setDetail, loading, error, tools, thinking, reload };
}
