import type { Captain, ObjectiveRefinementMessage, ObjectiveRefinementSession } from '../../types/models';

export function upsertRefinementSession(
  sessions: ObjectiveRefinementSession[],
  session: ObjectiveRefinementSession,
): ObjectiveRefinementSession[] {
  const next = [...sessions];
  const index = next.findIndex((item) => item.id === session.id);
  if (index >= 0) next[index] = session;
  else next.unshift(session);
  return next.sort((a, b) => new Date(b.lastUpdateUtc).getTime() - new Date(a.lastUpdateUtc).getTime());
}

export function removeRefinementSession(
  sessions: ObjectiveRefinementSession[],
  sessionId: string,
): ObjectiveRefinementSession[] {
  return sessions.filter((session) => session.id !== sessionId);
}

export function upsertRefinementMessage(
  messages: ObjectiveRefinementMessage[],
  message: ObjectiveRefinementMessage,
): ObjectiveRefinementMessage[] {
  const next = [...messages];
  const index = next.findIndex((item) => item.id === message.id);
  if (index >= 0) next[index] = message;
  else next.push(message);
  return next.sort((a, b) => a.sequence - b.sequence);
}

export function mergeCaptainState(
  captains: Captain[],
  update: { id: string; state: string; name?: string | null },
): Captain[] {
  return captains.map((captain) => (
    captain.id === update.id
      ? {
          ...captain,
          state: update.state,
          name: update.name ?? captain.name,
        }
      : captain
  ));
}

export function getLatestAssistantRefinementMessage(
  messages: ObjectiveRefinementMessage[],
): ObjectiveRefinementMessage | null {
  return [...messages]
    .reverse()
    .find((message) => message.role.toLowerCase() === 'assistant' && message.content.trim().length > 0) || null;
}
