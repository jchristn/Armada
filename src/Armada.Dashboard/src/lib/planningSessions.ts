import type { Captain, PlanningSession, PlanningSessionMessage } from '../../types/models';

export type DispatchSeedSource = 'auto' | 'summary';

export interface DispatchSeedState {
  key: string;
  title: string;
  description: string;
  source: DispatchSeedSource;
}

export function upsertSession(sessions: PlanningSession[], session: PlanningSession): PlanningSession[] {
  const next = [...sessions];
  const index = next.findIndex((item) => item.id === session.id);
  if (index >= 0) next[index] = session;
  else next.unshift(session);
  return next.sort((a, b) => new Date(b.lastUpdateUtc).getTime() - new Date(a.lastUpdateUtc).getTime());
}

export function removeSession(sessions: PlanningSession[], sessionId: string): PlanningSession[] {
  return sessions.filter((session) => session.id !== sessionId);
}

export function upsertMessage(messages: PlanningSessionMessage[], message: PlanningSessionMessage): PlanningSessionMessage[] {
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

export function getLatestAssistantMessage(messages: PlanningSessionMessage[]): PlanningSessionMessage | null {
  return [...messages]
    .reverse()
    .find((message) => message.role.toLowerCase() === 'assistant' && message.content.trim().length > 0) || null;
}

export function buildDispatchSeed(sessionTitle: string | undefined, message: PlanningSessionMessage | null): { title: string; description: string } {
  const trimmedContent = message?.content.trim() || '';
  return {
    title: sessionTitle?.trim() || trimmedContent.substring(0, 80),
    description: trimmedContent,
  };
}

interface ResolveDispatchSeedOptions {
  sessionId: string;
  sessionTitle?: string;
  message: PlanningSessionMessage | null;
  currentTitle: string;
  currentDescription: string;
  previousSeed: DispatchSeedState | null;
}

export function resolveDispatchSeedUpdate(options: ResolveDispatchSeedOptions): DispatchSeedState | null {
  const {
    sessionId,
    sessionTitle,
    message,
    currentTitle,
    currentDescription,
    previousSeed,
  } = options;

  if (!message || !message.content.trim()) return null;

  const key = `${sessionId}:${message.id}`;
  if (previousSeed?.source === 'summary' && previousSeed.key === key) return null;

  const seed = buildDispatchSeed(sessionTitle, message);
  if (!previousSeed || previousSeed.key !== key) {
    return {
      key,
      title: seed.title,
      description: seed.description,
      source: 'auto',
    };
  }

  return {
    key,
    title: !currentTitle.trim() || currentTitle === previousSeed.title ? seed.title : currentTitle,
    description: !currentDescription.trim() || currentDescription === previousSeed.description ? seed.description : currentDescription,
    source: 'auto',
  };
}
