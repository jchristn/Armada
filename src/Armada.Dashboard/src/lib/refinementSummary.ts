import type { ObjectiveRefinementSummaryResponse } from '../types/models';

function lines(value: unknown): string[] {
  return Array.isArray(value) ? value.filter((item): item is string => typeof item === 'string') : [];
}

/**
 * Read the draft from an `objective-refinement-session.summary.created` WebSocket event. The Admiral sends
 * `{ sessionId, messageId, summary }` where `summary` is the draft object (camelCase), not the draft itself; list
 * fields are normalized to arrays so the summary panel can always render them. Returns null for a malformed event.
 */
export function refinementSummaryFromEvent(data: unknown): ObjectiveRefinementSummaryResponse | null {
  if (!data || typeof data !== 'object') return null;
  const event = data as { sessionId?: unknown; messageId?: unknown; summary?: unknown };
  if (!event.summary || typeof event.summary !== 'object') return null;
  const draft = event.summary as Partial<ObjectiveRefinementSummaryResponse>;
  const sessionId = typeof event.sessionId === 'string' ? event.sessionId : draft.sessionId;
  if (typeof sessionId !== 'string' || !sessionId) return null;
  const messageId = typeof event.messageId === 'string' ? event.messageId : (draft.messageId ?? null);
  return {
    sessionId,
    messageId,
    summary: typeof draft.summary === 'string' ? draft.summary : '',
    acceptanceCriteria: lines(draft.acceptanceCriteria),
    nonGoals: lines(draft.nonGoals),
    rolloutConstraints: lines(draft.rolloutConstraints),
    suggestedPipelineId: draft.suggestedPipelineId ?? null,
    method: typeof draft.method === 'string' ? draft.method : '',
  };
}
