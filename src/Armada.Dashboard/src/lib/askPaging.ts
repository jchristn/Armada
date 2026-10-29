/**
 * Reads the Ask Armada collections the conversation view needs from their paged enumerations. The server never embeds
 * them in a thread, message, or snapshot (docs/ENUMERATION.md): a thread carries counts, a message its toolCallCount,
 * and a snapshot its missionCount and targetCount. Collections the view must show completely (pending proposals and
 * CLI permission requests, tracked work, a message's tool calls) are read page by page until the server's total is
 * reached; work-card rows load their first page and keep the total so the card can say how many it shows.
 */
import {
  enumerateAskProposals,
  enumerateAskToolCalls,
  enumerateAskTurnToolCalls,
  enumerateAskWork,
  enumerateAskWorkMissions,
  enumerateAskWorkTargets,
  getAskThread,
  listCliPermissionRequests,
} from '../api/client';
import type {
  AskMessage,
  AskThreadView,
  AskToolCall,
  AskWorkSnapshot,
  CliPermissionRequest,
  EnumerationResult,
} from '../types/models';

/** Rows requested per page. */
export const ASK_PAGE_SIZE = 100;
/** Work-card rows loaded with a snapshot (the first page). */
export const WORK_ROW_PAGE_SIZE = 100;
/** Pages read at most for one collection; a guard against a server that never reports its last page. */
const MAX_PAGES = 1000;

/** Every item of a paged enumeration, read one bounded page at a time until the last page. */
export async function readAllPages<T>(readPage: (pageNumber: number) => Promise<EnumerationResult<T> | null | undefined>): Promise<T[]> {
  const all: T[] = [];
  for (let pageNumber = 1; pageNumber <= MAX_PAGES; pageNumber++) {
    const page = await readPage(pageNumber);
    const objects = page?.objects ?? [];
    all.push(...objects);
    if (objects.length === 0 || pageNumber >= (page?.totalPages ?? 0)) break;
  }
  return all;
}

/** A thread's detail with its tracked work, pending proposals, pending CLI permission requests, and the running
 * turn's tool calls, each read from its paged enumeration. */
export async function loadAskThreadView(threadId: string): Promise<AskThreadView> {
  const detail = await getAskThread(threadId);
  const view: AskThreadView = { ...detail };
  const tasks: Promise<void>[] = [];
  if ((detail.trackedWorkCount ?? 0) > 0) {
    tasks.push(readAllPages((pageNumber) => enumerateAskWork(threadId, { pageNumber, pageSize: ASK_PAGE_SIZE }))
      .then((work) => { view.trackedWork = work; }));
  } else {
    view.trackedWork = [];
  }
  if ((detail.pendingProposalCount ?? 0) > 0) {
    tasks.push(readAllPages((pageNumber) => enumerateAskProposals(threadId, { pageNumber, pageSize: ASK_PAGE_SIZE, order: 'CreatedAscending', status: 'Pending' }))
      .then((proposals) => { view.pendingProposals = proposals; }));
  } else {
    view.pendingProposals = [];
  }
  if ((detail.pendingCliPermissionCount ?? 0) > 0) {
    tasks.push(readAllPages<CliPermissionRequest>(async (pageNumber) => {
      const objects = await listCliPermissionRequests({ threadId, status: 'Pending', pageNumber, limit: ASK_PAGE_SIZE });
      const totalPages = Math.ceil((detail.pendingCliPermissionCount ?? 0) / ASK_PAGE_SIZE);
      return { success: true, pageNumber, pageSize: ASK_PAGE_SIZE, totalPages, totalRecords: detail.pendingCliPermissionCount ?? 0, objects, totalMs: 0 };
    }).then((requests) => { view.pendingCliPermissions = requests; }));
  } else {
    view.pendingCliPermissions = [];
  }
  if (detail.activeTurn && (detail.activeTurn.toolCallCount ?? 0) > 0) {
    const turn = detail.activeTurn;
    tasks.push(readAllPages((pageNumber) => enumerateAskTurnToolCalls(threadId, { pageNumber, pageSize: ASK_PAGE_SIZE, order: 'CreatedAscending' }))
      .then((calls) => { view.activeTurn = { ...turn, toolCalls: calls }; }));
  }
  await Promise.all(tasks);
  return view;
}

/** Every tool call of one message, in the order they were made. */
export function loadToolCalls(threadId: string, messageId: string): Promise<AskToolCall[]> {
  return readAllPages((pageNumber) => enumerateAskToolCalls(threadId, messageId, { pageNumber, pageSize: ASK_PAGE_SIZE, order: 'CreatedAscending' }));
}

/** The messages with their tool calls attached (read only for messages whose toolCallCount is not zero). */
export async function withToolCalls(threadId: string, messages: AskMessage[]): Promise<AskMessage[]> {
  return Promise.all(messages.map(async (message) => {
    if (!message.toolCallCount || message.isLocal) return message;
    try {
      return { ...message, toolCalls: await loadToolCalls(threadId, message.id) };
    } catch {
      return message;
    }
  }));
}

/** The snapshot with the first page of its mission or target rows attached and the total row count. */
export async function withWorkRows(threadId: string, workId: string, snapshot: AskWorkSnapshot): Promise<AskWorkSnapshot> {
  const type = String(snapshot.entityType);
  try {
    if (type === 'FleetActionRun') {
      const page = await enumerateAskWorkTargets(threadId, workId, { pageNumber: 1, pageSize: WORK_ROW_PAGE_SIZE });
      return { ...snapshot, targets: page?.objects ?? [], rowTotal: page?.totalRecords ?? 0 };
    }
    // A black box card has no rows; empty lists mark it loaded so it is not fetched again.
    if (type === 'BlackBox') return { ...snapshot, missions: [], targets: [] };
    if (type === 'Voyage' || type === 'Mission') {
      const page = await enumerateAskWorkMissions(threadId, workId, { pageNumber: 1, pageSize: WORK_ROW_PAGE_SIZE, order: 'CreatedAscending' });
      return { ...snapshot, missions: page?.objects ?? [], rowTotal: page?.totalRecords ?? 0 };
    }
  } catch {
    // The card shows its counts until the rows can be read.
  }
  return snapshot;
}
