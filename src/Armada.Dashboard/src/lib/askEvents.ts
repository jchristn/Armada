import { camelizeKeys } from '../api/client';
import type {
  AskActionProposal,
  AskMessage,
  AskThread,
  AskTurnState,
  AskTrackedWork,
  AskWorkSnapshot,
  WebSocketMessage,
} from '../types/models';
import type { ToolEventMessage } from '../components/shared/ChatToolChips';

/**
 * Normalized Ask Armada WebSocket events. Every event carries the `threadId` it belongs to, so the page can route
 * it to the open conversation or to the thread list (unread badge, working dot) without guessing.
 */
export type AskEvent =
  | { type: 'ask.chunk'; threadId: string; turnId: string; delta: string }
  | { type: 'ask.thinking'; threadId: string; turnId: string; delta: string }
  | { type: 'ask.tool'; threadId: string; turnId: string; tool: ToolEventMessage }
  | { type: 'ask.turn'; threadId: string; turnId: string; state: AskTurnState; messageId: string | null; error: string | null }
  | { type: 'ask.message'; threadId: string; message: AskMessage }
  | { type: 'ask.proposal'; threadId: string; proposal: AskActionProposal }
  | { type: 'ask.work'; threadId: string; trackedWorkId: string; snapshot: AskWorkSnapshot | null; trackedWork: AskTrackedWork | null }
  | { type: 'ask.thread'; threadId: string; thread: AskThread };

type Payload = Record<string, unknown>;

const TURN_STATES: readonly AskTurnState[] = ['started', 'completed', 'failed', 'cancelled'];

/**
 * Parse the wire state of an ask.turn event. A missing state means 'started' (the server's first emit); a value
 * outside the known set ends the turn without an error, as 'completed' does.
 */
export function parseTurnState(value: string | null): AskTurnState {
  if (value === null) return 'started';
  const lower = value.toLowerCase();
  const known = TURN_STATES.find((s) => s === lower);
  return known ?? 'completed';
}

function str(value: unknown): string | null {
  return typeof value === 'string' && value.length > 0 ? value : null;
}

function obj<T>(value: unknown): T | null {
  return value && typeof value === 'object' && !Array.isArray(value) ? (value as T) : null;
}

/**
 * Turn a raw socket message into an AskEvent, or null when it is not an Ask event or lacks a thread id. Payload
 * keys are camelized defensively (the hub already sends camelCase), and the thread id falls back to the nested
 * entity's own `threadId` / `id` when the top-level field is missing.
 */
export function parseAskEvent(msg: WebSocketMessage | null | undefined): AskEvent | null {
  if (!msg || typeof msg.type !== 'string' || !msg.type.startsWith('ask.')) return null;
  const data = obj<Payload>(camelizeKeys(msg.data ?? null));
  if (!data) return null;

  const message = obj<AskMessage>(data.message);
  const proposal = obj<AskActionProposal>(data.proposal);
  const thread = obj<AskThread>(data.thread);
  const snapshot = obj<AskWorkSnapshot>(data.snapshot);
  const trackedWork = obj<AskTrackedWork>(data.trackedWork);
  const threadId = str(data.threadId)
    ?? str(message?.threadId)
    ?? str(proposal?.threadId)
    ?? str(trackedWork?.threadId)
    ?? (msg.type === 'ask.thread' ? str(thread?.id) : null);
  if (!threadId) return null;

  switch (msg.type) {
    case 'ask.chunk':
    case 'ask.thinking': {
      const turnId = str(data.turnId);
      const delta = typeof data.delta === 'string' ? data.delta : '';
      if (!turnId || !delta) return null;
      return { type: msg.type, threadId, turnId, delta };
    }
    case 'ask.tool': {
      const turnId = str(data.turnId);
      if (!turnId) return null;
      return { type: 'ask.tool', threadId, turnId, tool: data as ToolEventMessage };
    }
    case 'ask.turn': {
      const turnId = str(data.turnId);
      if (!turnId) return null;
      return {
        type: 'ask.turn',
        threadId,
        turnId,
        state: parseTurnState(str(data.state)),
        messageId: str(data.messageId),
        error: str(data.error) ?? str(data.errorText),
      };
    }
    case 'ask.message':
      return message ? { type: 'ask.message', threadId, message: { ...message, threadId: message.threadId || threadId } } : null;
    case 'ask.proposal':
      return proposal ? { type: 'ask.proposal', threadId, proposal: { ...proposal, threadId: proposal.threadId || threadId } } : null;
    case 'ask.work': {
      const trackedWorkId = str(data.trackedWorkId) ?? str(snapshot?.trackedWorkId) ?? str(trackedWork?.id);
      if (!trackedWorkId) return null;
      return { type: 'ask.work', threadId, trackedWorkId, snapshot, trackedWork };
    }
    case 'ask.thread':
      return thread ? { type: 'ask.thread', threadId, thread: { ...thread, id: thread.id || threadId } } : null;
    default:
      return null;
  }
}
