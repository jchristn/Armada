/**
 * Planning and refinement session views: the server's detail (counts only) plus the newest transcript page, and the
 * helpers that load older pages and fold REST responses in without dropping the loaded transcript. Shared by the
 * dashboard and the mobile app.
 */
import {
  getObjectiveRefinementSession,
  getPlanningSession,
  listObjectiveRefinementMessages,
  listPlanningSessionMessages,
} from '../api/client';
import type {
  ObjectiveRefinementSessionDetail,
  ObjectiveRefinementSessionView,
  PlanningSessionDetail,
  PlanningSessionView,
} from '../types/models';
import { mergeSessionDetail } from './liveMerge';
import { loadNewestTranscript, loadOlderTranscript, mergeTranscript } from './pagedTranscript';

/** A planning session's detail with the newest page of its transcript. */
export async function loadPlanningSessionView(sessionId: string): Promise<PlanningSessionView> {
  const [detail, transcript] = await Promise.all([
    getPlanningSession(sessionId),
    loadNewestTranscript((query) => listPlanningSessionMessages(sessionId, query)),
  ]);
  return { ...detail, messages: transcript.messages, messageCount: Math.max(detail.messageCount ?? 0, transcript.total) };
}

/** A refinement session's detail with the newest page of its transcript. */
export async function loadRefinementSessionView(sessionId: string): Promise<ObjectiveRefinementSessionView> {
  const [detail, transcript] = await Promise.all([
    getObjectiveRefinementSession(sessionId),
    loadNewestTranscript((query) => listObjectiveRefinementMessages(sessionId, query)),
  ]);
  return { ...detail, messages: transcript.messages, messageCount: Math.max(detail.messageCount ?? 0, transcript.total) };
}

/** A planning detail from a REST response as a view, keeping the transcript already loaded for the session. */
export function asPlanningView(detail: PlanningSessionDetail, current: PlanningSessionView | null): PlanningSessionView {
  return mergeSessionDetail(current, detail);
}

/** A refinement detail from a REST response as a view, keeping the transcript already loaded for the session. */
export function asRefinementView(detail: ObjectiveRefinementSessionDetail, current: ObjectiveRefinementSessionView | null): ObjectiveRefinementSessionView {
  return mergeSessionDetail(current, detail);
}

/**
 * A detail from a stop or stop-turn response applied as the authoritative session (the stop is the newest change),
 * keeping the transcript already loaded and merging any messages the response carries.
 */
export function withStoppedSession<V extends { session: { id: string }; messages: M[]; messageCount: number }, M extends { id: string; sequence: number; lastUpdateUtc: string }>(
  current: V | null,
  detail: Omit<V, 'messages'> & { messages?: M[] },
): V {
  if (!current || current.session.id !== detail.session.id) return { ...detail, messages: detail.messages ?? [] } as V;
  const messages = mergeTranscript(current.messages, detail.messages ?? []);
  return { ...current, ...detail, messages, messageCount: Math.max(detail.messageCount ?? 0, current.messageCount ?? 0, messages.length) } as V;
}

/** The view with the next older planning transcript page merged in. */
export async function withOlderPlanningMessages(view: PlanningSessionView): Promise<PlanningSessionView> {
  const transcript = await loadOlderTranscript((query) => listPlanningSessionMessages(view.session.id, query), view.messages);
  return { ...view, messages: mergeTranscript(view.messages, transcript.messages), messageCount: Math.max(transcript.total, view.messageCount ?? 0) };
}

/** The view with the next older refinement transcript page merged in. */
export async function withOlderRefinementMessages(view: ObjectiveRefinementSessionView): Promise<ObjectiveRefinementSessionView> {
  const transcript = await loadOlderTranscript((query) => listObjectiveRefinementMessages(view.session.id, query), view.messages);
  return { ...view, messages: mergeTranscript(view.messages, transcript.messages), messageCount: Math.max(transcript.total, view.messageCount ?? 0) };
}
