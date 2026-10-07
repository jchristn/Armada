import type {
  AskActionProposal,
  AskMessage,
  AskThread,
  AskThreadDetail,
  AskTrackedWork,
  AskWorkSnapshot,
  CliPermissionRequest,
} from '../types/models';
import { applyToolEvent, type ToolEvent } from './toolEvents';
import type { AskEvent } from './askEvents';
import { applySnapshotToWork } from './askWork';
import { mergeCliRequest } from './cliPermissions';

/** The assistant reply being streamed for the current captain turn. */
export interface StreamingTurn {
  turnId: string;
  text: string;
  thinking: string;
  tools: ToolEvent[];
  /** Set once `ask.turn` reports a terminal state; the persisted message then replaces the stream. */
  finished: boolean;
  finalMessageId: string | null;
}

export interface ConversationState {
  threadId: string | null;
  thread: AskThread | null;
  /** Ordered by sequence ascending. Optimistic user messages carry `isLocal: true`. */
  messages: AskMessage[];
  hasMore: boolean;
  trackedWork: AskTrackedWork[];
  snapshots: Record<string, AskWorkSnapshot>;
  /** Latest known state of every proposal, by id (events can arrive before or after the message). */
  proposals: Record<string, AskActionProposal>;
  /** Latest known state of every CLI permission request of this thread, by id (cards and socket events). */
  cliPermissions: Record<string, CliPermissionRequest>;
  streaming: StreamingTurn | null;
  turnActive: boolean;
  /** Last turn failure reported by the server, shown inline until the next turn. */
  turnError: string | null;
}

export type ConversationAction =
  | { type: 'reset'; threadId: string | null }
  | { type: 'loaded'; threadId: string; detail: AskThreadDetail; messages: AskMessage[]; hasMore: boolean }
  | { type: 'older'; threadId: string; messages: AskMessage[]; hasMore: boolean }
  | { type: 'latest'; threadId: string; messages: AskMessage[] }
  | { type: 'detail'; threadId: string; detail: AskThreadDetail }
  | { type: 'event'; event: AskEvent }
  | { type: 'optimisticUser'; message: AskMessage }
  | { type: 'confirmUser'; localId: string; messageId: string | null; turnId: string | null }
  | { type: 'dropOptimistic'; localId: string }
  | { type: 'proposal'; proposal: AskActionProposal }
  | { type: 'cliPermission'; request: CliPermissionRequest }
  | { type: 'snapshot'; trackedWorkId: string; snapshot: AskWorkSnapshot }
  | { type: 'thread'; thread: AskThread }
  | { type: 'turnEnded' };

export function initialConversation(threadId: string | null = null): ConversationState {
  return {
    threadId,
    thread: null,
    messages: [],
    hasMore: false,
    trackedWork: [],
    snapshots: {},
    proposals: {},
    cliPermissions: {},
    streaming: null,
    turnActive: false,
    turnError: null,
  };
}

export function isLocalMessage(message: AskMessage): boolean {
  return message.isLocal === true;
}

/** Merge incoming messages by id (incoming wins), drop optimistic copies the server has now persisted, sort. */
export function mergeMessages(existing: AskMessage[], incoming: AskMessage[]): AskMessage[] {
  const byId = new Map<string, AskMessage>();
  for (const message of existing) byId.set(message.id, message);
  for (const message of incoming) {
    if (!message || !message.id) continue;
    const next: AskMessage = { ...byId.get(message.id), ...message };
    // Only an optimistic copy is local; a server copy of the same id replaces that state.
    if (message.isLocal !== true) delete next.isLocal;
    byId.set(message.id, next);
  }
  let merged = [...byId.values()];
  // An optimistic user message is superseded by a persisted user message with the same text at or after its
  // position (an earlier identical question does not count).
  const persistedUsers = incoming.filter((m) => m && m.id && !isLocalMessage(m) && m.role === 'User');
  merged = merged.filter((m) => !(isLocalMessage(m) && persistedUsers.some((p) => (p.contentText ?? '').trim() === (m.contentText ?? '').trim() && p.sequence >= m.sequence)));
  merged.sort((a, b) => (a.sequence - b.sequence) || (isLocalMessage(a) ? 1 : 0) - (isLocalMessage(b) ? 1 : 0));
  return merged;
}

const TERMINAL_PROPOSAL = new Set(['executed', 'failed', 'rejected', 'expired']);

/** Merge two copies of a proposal; a decided proposal never goes back to Pending/Approved from a stale copy. */
export function mergeProposal(prev: AskActionProposal | undefined, next: AskActionProposal): AskActionProposal {
  if (!prev) return next;
  const prevDone = TERMINAL_PROPOSAL.has(String(prev.status).toLowerCase());
  const nextDone = TERMINAL_PROPOSAL.has(String(next.status).toLowerCase());
  if (prevDone && !nextDone) return { ...next, ...prev };
  return { ...prev, ...next };
}

function indexProposals(state: Record<string, AskActionProposal>, messages: AskMessage[], extra?: AskActionProposal[] | null): Record<string, AskActionProposal> {
  const next = { ...state };
  for (const message of messages) {
    if (message.proposal?.id) next[message.proposal.id] = mergeProposal(next[message.proposal.id], message.proposal);
  }
  for (const proposal of extra ?? []) if (proposal?.id) next[proposal.id] = mergeProposal(next[proposal.id], proposal);
  return next;
}

function indexCliPermissions(state: Record<string, CliPermissionRequest>, messages: AskMessage[], extra?: CliPermissionRequest[] | null): Record<string, CliPermissionRequest> {
  let next: Record<string, CliPermissionRequest> | null = null;
  const add = (request: CliPermissionRequest | null | undefined) => {
    if (!request?.id) return;
    next = next ?? { ...state };
    next[request.id] = mergeCliRequest(next[request.id], request);
  };
  for (const message of messages) add(message.cliPermissionRequest);
  for (const request of extra ?? []) add(request);
  return next ?? state;
}

function upsertWork(list: AskTrackedWork[], work: AskTrackedWork): AskTrackedWork[] {
  const idx = list.findIndex((w) => w.id === work.id);
  if (idx === -1) return [...list, work];
  const copy = [...list];
  copy[idx] = { ...copy[idx], ...work };
  return copy;
}

function indexWork(list: AskTrackedWork[], snapshots: Record<string, AskWorkSnapshot>, messages: AskMessage[]) {
  let work = list;
  const snaps = { ...snapshots };
  for (const message of messages) {
    if (message.trackedWork?.id) work = upsertWork(work, message.trackedWork);
  }
  for (const item of work) if (item.snapshot && !snaps[item.id]) snaps[item.id] = item.snapshot;
  return { work, snaps };
}

/** Whether a newly arrived message is the persisted form of the stream on screen. */
function closesStream(streaming: StreamingTurn | null, message: AskMessage): boolean {
  if (!streaming) return false;
  if (streaming.finalMessageId && message.id === streaming.finalMessageId) return true;
  return streaming.finished && message.role === 'Assistant' && (message.kind === 'Text' || message.kind === 'Error');
}

function emptyStream(turnId: string): StreamingTurn {
  return { turnId, text: '', thinking: '', tools: [], finished: false, finalMessageId: null };
}

/** Pure reducer for the open conversation. Events for other threads are ignored. */
export function conversationReducer(state: ConversationState, action: ConversationAction): ConversationState {
  switch (action.type) {
    case 'reset':
      return initialConversation(action.threadId);

    case 'loaded': {
      if (action.threadId !== state.threadId) return state;
      // Keep optimistic messages that the server has not persisted yet (a send racing the initial load).
      const messages = mergeMessages(state.messages.filter(isLocalMessage), action.messages);
      const { work, snaps } = indexWork(action.detail.trackedWork ?? [], state.snapshots, messages);
      const thread = action.detail.thread;
      // `activeTurnId` is optional in the contract: when the server reports it, it is authoritative; when it is
      // absent, keep what the socket already told us.
      const reportsTurn = !!thread && Object.prototype.hasOwnProperty.call(thread, 'activeTurnId');
      const activeTurn = thread?.activeTurnId ?? null;
      let streaming = state.streaming && !state.streaming.finished ? state.streaming : null;
      let turnActive = state.turnActive;
      if (reportsTurn) {
        turnActive = !!activeTurn;
        streaming = activeTurn ? (streaming?.turnId === activeTurn ? streaming : emptyStream(activeTurn)) : null;
      }
      return {
        ...state,
        thread,
        messages,
        hasMore: action.hasMore,
        trackedWork: work,
        snapshots: snaps,
        proposals: indexProposals(state.proposals, messages, action.detail.pendingProposals),
        cliPermissions: indexCliPermissions(state.cliPermissions, messages, action.detail.pendingCliPermissions),
        streaming,
        turnActive,
      };
    }

    case 'older': {
      if (action.threadId !== state.threadId) return state;
      const messages = mergeMessages(state.messages, action.messages);
      const { work, snaps } = indexWork(state.trackedWork, state.snapshots, action.messages);
      return { ...state, messages, hasMore: action.hasMore, trackedWork: work, snapshots: snaps, proposals: indexProposals(state.proposals, action.messages), cliPermissions: indexCliPermissions(state.cliPermissions, action.messages) };
    }

    case 'latest': {
      if (action.threadId !== state.threadId) return state;
      const messages = mergeMessages(state.messages, action.messages);
      const { work, snaps } = indexWork(state.trackedWork, state.snapshots, action.messages);
      let streaming = state.streaming;
      if (streaming?.finished) streaming = null;
      return { ...state, messages, trackedWork: work, snapshots: snaps, proposals: indexProposals(state.proposals, action.messages), cliPermissions: indexCliPermissions(state.cliPermissions, action.messages), streaming };
    }

    case 'detail': {
      if (action.threadId !== state.threadId) return state;
      let work = state.trackedWork;
      for (const item of action.detail.trackedWork ?? []) work = upsertWork(work, item);
      const snaps = { ...state.snapshots };
      for (const item of work) if (item.snapshot) snaps[item.id] = item.snapshot;
      return {
        ...state,
        thread: action.detail.thread ?? state.thread,
        trackedWork: work,
        snapshots: snaps,
        proposals: indexProposals(state.proposals, [], action.detail.pendingProposals),
        cliPermissions: indexCliPermissions(state.cliPermissions, [], action.detail.pendingCliPermissions),
      };
    }

    case 'optimisticUser':
      return { ...state, messages: mergeMessages(state.messages, [action.message]), turnActive: true, turnError: null };

    case 'confirmUser': {
      const messages = state.messages.map((m) => (m.id === action.localId && action.messageId ? { ...m, id: action.messageId, isLocal: undefined } : m));
      const deduped = mergeMessages([], messages);
      const streaming = action.turnId && (!state.streaming || state.streaming.turnId !== action.turnId)
        ? (state.streaming && !state.streaming.finished && state.streaming.text === '' ? { ...state.streaming, turnId: action.turnId } : emptyStream(action.turnId))
        : state.streaming;
      return { ...state, messages: deduped, streaming, turnActive: !!action.turnId || state.turnActive };
    }

    case 'dropOptimistic':
      return { ...state, messages: state.messages.filter((m) => m.id !== action.localId), turnActive: false };

    case 'proposal':
      return { ...state, proposals: { ...state.proposals, [action.proposal.id]: mergeProposal(state.proposals[action.proposal.id], action.proposal) } };

    case 'cliPermission':
      if (action.request.threadId && action.request.threadId !== state.threadId) return state;
      return { ...state, cliPermissions: { ...state.cliPermissions, [action.request.id]: mergeCliRequest(state.cliPermissions[action.request.id], action.request) } };

    case 'snapshot': {
      const work = state.trackedWork.map((w) => (w.id === action.trackedWorkId ? applySnapshotToWork(w, action.snapshot) : w));
      return { ...state, snapshots: { ...state.snapshots, [action.trackedWorkId]: action.snapshot }, trackedWork: work };
    }

    case 'thread':
      if (action.thread.id !== state.threadId) return state;
      return { ...state, thread: { ...state.thread, ...action.thread } };

    case 'turnEnded':
      return { ...state, turnActive: false, streaming: state.streaming && !state.streaming.text && state.streaming.tools.length === 0 ? null : state.streaming };

    case 'event':
      return applyEvent(state, action.event);

    default:
      return state;
  }
}

function applyEvent(state: ConversationState, event: AskEvent): ConversationState {
  if (!state.threadId || event.threadId !== state.threadId) return state;
  switch (event.type) {
    case 'ask.chunk':
    case 'ask.thinking':
    case 'ask.tool': {
      let stream = state.streaming && state.streaming.turnId === event.turnId ? state.streaming : null;
      if (!stream) {
        // A late event for a turn that already closed is ignored; otherwise start following the new turn.
        if (state.streaming && state.streaming.turnId !== event.turnId && !state.streaming.finished) {
          stream = emptyStream(event.turnId);
        } else if (state.streaming?.finished && state.streaming.turnId === event.turnId) {
          return state;
        } else {
          stream = emptyStream(event.turnId);
        }
      }
      if (event.type === 'ask.chunk') stream = { ...stream, text: stream.text + event.delta };
      else if (event.type === 'ask.thinking') stream = { ...stream, thinking: stream.thinking + event.delta };
      else stream = { ...stream, tools: applyToolEvent(stream.tools, event.tool) };
      return { ...state, streaming: stream, turnActive: true };
    }

    case 'ask.turn': {
      if (event.state === 'started') {
        const streaming = state.streaming && state.streaming.turnId === event.turnId ? state.streaming : emptyStream(event.turnId);
        return { ...state, streaming, turnActive: true, turnError: null };
      }
      const failed = event.state === 'failed';
      const alreadyPersisted = event.messageId ? state.messages.some((m) => m.id === event.messageId) : false;
      let streaming: StreamingTurn | null = state.streaming && state.streaming.turnId === event.turnId
        ? { ...state.streaming, finished: true, finalMessageId: event.messageId }
        : state.streaming;
      if (alreadyPersisted || (streaming && !streaming.text && streaming.tools.length === 0 && streaming.finished)) streaming = null;
      return { ...state, streaming, turnActive: false, turnError: failed ? (event.error ?? 'failed') : null };
    }

    case 'ask.message': {
      const message = event.message;
      const messages = mergeMessages(state.messages, [message]);
      const { work, snaps } = indexWork(state.trackedWork, state.snapshots, [message]);
      const proposals = indexProposals(state.proposals, [message]);
      const cliPermissions = indexCliPermissions(state.cliPermissions, [message]);
      const streaming = closesStream(state.streaming, message) ? null : state.streaming;
      return { ...state, messages, trackedWork: work, snapshots: snaps, proposals, cliPermissions, streaming };
    }

    case 'ask.proposal':
      return conversationReducer(state, { type: 'proposal', proposal: event.proposal });

    case 'ask.work': {
      let work = state.trackedWork;
      if (event.trackedWork) work = upsertWork(work, event.trackedWork);
      if (event.snapshot) {
        if (!work.some((w) => w.id === event.trackedWorkId)) {
          work = [...work, {
            id: event.trackedWorkId,
            threadId: event.threadId,
            entityType: event.snapshot.entityType,
            entityId: event.snapshot.entityId,
            title: event.snapshot.title,
            status: event.snapshot.status,
            state: event.snapshot.state ?? 'Active',
          }];
        }
        work = work.map((w) => (w.id === event.trackedWorkId ? applySnapshotToWork(w, event.snapshot as AskWorkSnapshot) : w));
        return { ...state, trackedWork: work, snapshots: { ...state.snapshots, [event.trackedWorkId]: event.snapshot } };
      }
      return { ...state, trackedWork: work };
    }

    case 'ask.thread':
      return { ...state, thread: { ...state.thread, ...event.thread } };

    default:
      return state;
  }
}

/** The proposal to render for a message: the latest event-driven copy wins over the embedded one. */
export function proposalForMessage(state: Pick<ConversationState, 'proposals'>, message: AskMessage): AskActionProposal | null {
  const id = message.proposalId ?? message.proposal?.id ?? null;
  if (id && state.proposals[id]) return message.proposal ? mergeProposal(message.proposal, state.proposals[id]) : state.proposals[id];
  return message.proposal ?? null;
}

/** The CLI permission request a CliPermission card renders: the latest event-driven copy merged over the embedded one. */
export function cliRequestForMessage(state: Pick<ConversationState, 'cliPermissions'>, message: AskMessage): CliPermissionRequest | null {
  const embedded = message.cliPermissionRequest ?? null;
  const id = embedded?.id ?? null;
  if (id && state.cliPermissions[id]) return mergeCliRequest(embedded ?? undefined, state.cliPermissions[id]);
  if (embedded) return embedded;
  // A card whose request was not hydrated: find the request that points at this message.
  return Object.values(state.cliPermissions).find((r) => r.messageId === message.id) ?? null;
}

/**
 * Which message hosts the full live card for each tracked item: the first non-milestone message that references
 * it (usually the ActionResult). Milestone (WorkUpdate) messages render compactly and link to that card.
 */
export function workCardHosts(messages: AskMessage[]): Record<string, string> {
  const hosts: Record<string, string> = {};
  for (const message of messages) {
    const workId = message.trackedWorkId ?? message.trackedWork?.id ?? null;
    if (!workId || hosts[workId]) continue;
    if (message.kind === 'WorkUpdate') continue;
    hosts[workId] = message.id;
  }
  // Items only referenced by milestones get their card on the first milestone.
  for (const message of messages) {
    const workId = message.trackedWorkId ?? message.trackedWork?.id ?? null;
    if (workId && !hosts[workId]) hosts[workId] = message.id;
  }
  return hosts;
}
