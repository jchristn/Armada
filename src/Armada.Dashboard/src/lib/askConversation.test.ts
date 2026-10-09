import { parseAskEvent, parseTurnState, type AskEvent } from './askEvents';
import { CLOSED_TURN_LIMIT, conversationReducer, initialConversation, isLocalMessage, mergeMessages, proposalForMessage, workCardHosts, type ConversationState } from './askConversation';
import { applyActivityEvent, applyThreadUpdate, isThreadWorking, sortThreads } from './askThreads';
import { isWorkActive, statusCounts, workProgress, workRoute } from './askWork';
import type { AskMessage, AskThread, AskThreadDetail, AskWorkSnapshot } from '../types/models';

const thread = (id: string, extra: Partial<AskThread> = {}): AskThread => ({
  id, title: id, captainId: 'cpt_1', autoApprove: false, pinned: false, archived: false, lastMessageUtc: '2026-10-04T10:00:00Z', unreadCount: 0, ...extra,
});

const msg = (id: string, sequence: number, extra: Partial<AskMessage> = {}): AskMessage => ({
  id, threadId: 'ath_1', sequence, role: 'Assistant', kind: 'Text', contentText: id, ...extra,
});

function loaded(messages: AskMessage[] = [], detail: Partial<AskThreadDetail> = {}): ConversationState {
  return conversationReducer(initialConversation('ath_1'), {
    type: 'loaded', threadId: 'ath_1', detail: { thread: thread('ath_1'), trackedWork: [], pendingProposals: [], ...detail }, messages, hasMore: false,
  });
}

function ev(type: string, data: unknown): AskEvent {
  const parsed = parseAskEvent({ type, data });
  if (!parsed) throw new Error(`unparsed ${type}`);
  return parsed;
}

describe('parseAskEvent', () => {
  it('requires a thread id and normalizes PascalCase payloads', () => {
    expect(parseAskEvent({ type: 'ask.chunk', data: { turnId: 't', delta: 'x' } })).toBeNull();
    expect(parseAskEvent({ type: 'mission.changed', data: { threadId: 'ath_1' } })).toBeNull();
    const e = parseAskEvent({ type: 'ask.message', data: { ThreadId: 'ath_1', Message: { Id: 'amg_1', Sequence: 3, Role: 'System', Kind: 'WorkUpdate', ContentText: 'hi' } } });
    expect(e).toMatchObject({ type: 'ask.message', threadId: 'ath_1', message: { id: 'amg_1', sequence: 3, kind: 'WorkUpdate' } });
  });

  it('falls back to the nested entity for the thread id', () => {
    expect(parseAskEvent({ type: 'ask.thread', data: { thread: { id: 'ath_9', title: 'x' } } })).toMatchObject({ threadId: 'ath_9' });
    expect(parseAskEvent({ type: 'ask.proposal', data: { proposal: { id: 'aap_1', threadId: 'ath_2', status: 'Pending' } } })).toMatchObject({ threadId: 'ath_2' });
  });

  it('lowercases turn states', () => {
    expect(parseAskEvent({ type: 'ask.turn', data: { threadId: 'a', turnId: 't', state: 'Completed', messageId: 'm' } })).toMatchObject({ state: 'completed', messageId: 'm' });
  });
});

describe('conversationReducer', () => {
  it('routes events by thread: another thread’s events do not touch the open conversation', () => {
    const state = loaded([msg('amg_1', 1)]);
    const other = conversationReducer(state, { type: 'event', event: ev('ask.message', { threadId: 'ath_2', message: msg('amg_x', 2, { threadId: 'ath_2' }) }) });
    expect(other).toBe(state);
    const chunk = conversationReducer(state, { type: 'event', event: ev('ask.chunk', { threadId: 'ath_2', turnId: 't', delta: 'hi' }) });
    expect(chunk.streaming).toBeNull();
  });

  it('streams chunks, thinking, and tools for a turn, then hands over to the persisted message', () => {
    let state = loaded([msg('amg_1', 1, { role: 'User' })]);
    state = conversationReducer(state, { type: 'event', event: ev('ask.turn', { threadId: 'ath_1', turnId: 't1', state: 'started' }) });
    expect(state.turnActive).toBe(true);
    state = conversationReducer(state, { type: 'event', event: ev('ask.chunk', { threadId: 'ath_1', turnId: 't1', delta: 'Hel' }) });
    state = conversationReducer(state, { type: 'event', event: ev('ask.chunk', { threadId: 'ath_1', turnId: 't1', delta: 'lo' }) });
    state = conversationReducer(state, { type: 'event', event: ev('ask.thinking', { threadId: 'ath_1', turnId: 't1', delta: 'hmm' }) });
    state = conversationReducer(state, { type: 'event', event: ev('ask.tool', { threadId: 'ath_1', turnId: 't1', phase: 'started', id: 'c1', name: 'enumerate' }) });
    expect(state.streaming).toMatchObject({ text: 'Hello', thinking: 'hmm' });
    expect(state.streaming?.tools[0]).toMatchObject({ id: 'c1', status: 'running' });

    state = conversationReducer(state, { type: 'event', event: ev('ask.turn', { threadId: 'ath_1', turnId: 't1', state: 'completed', messageId: 'amg_2' }) });
    expect(state.turnActive).toBe(false);
    expect(state.streaming?.finished).toBe(true);
    state = conversationReducer(state, { type: 'event', event: ev('ask.message', { threadId: 'ath_1', message: msg('amg_2', 2, { contentText: 'Hello' }) }) });
    expect(state.streaming).toBeNull();
    expect(state.messages.map((m) => m.id)).toEqual(['amg_1', 'amg_2']);
  });

  it('keeps streaming when a milestone arrives mid-turn', () => {
    let state = loaded();
    state = conversationReducer(state, { type: 'event', event: ev('ask.chunk', { threadId: 'ath_1', turnId: 't1', delta: 'x' }) });
    state = conversationReducer(state, { type: 'event', event: ev('ask.message', { threadId: 'ath_1', message: msg('amg_5', 5, { role: 'System', kind: 'WorkUpdate' }) }) });
    expect(state.streaming?.text).toBe('x');
  });

  it('replaces an optimistic user message with the persisted one', () => {
    let state = loaded([msg('amg_1', 1, { role: 'User', contentText: 'same' })]);
    state = conversationReducer(state, { type: 'optimisticUser', message: msg('local-1', 2, { role: 'User', contentText: 'same', isLocal: true }) });
    expect(state.messages).toHaveLength(2);
    state = conversationReducer(state, { type: 'confirmUser', localId: 'local-1', messageId: 'amg_2', turnId: 't9' });
    expect(state.messages.map((m) => m.id)).toEqual(['amg_1', 'amg_2']);
    expect(state.streaming?.turnId).toBe('t9');
    state = conversationReducer(state, { type: 'event', event: ev('ask.message', { threadId: 'ath_1', message: msg('amg_2', 2, { role: 'User', contentText: 'same' }) }) });
    expect(state.messages).toHaveLength(2);
  });

  it('updates a confirm card from ask.proposal and the work card from ask.work', () => {
    const proposalMsg = msg('amg_3', 3, { kind: 'ActionProposal', proposalId: 'aap_1', proposal: { id: 'aap_1', threadId: 'ath_1', toolName: 'dispatch', source: 'Captain', status: 'Pending' } });
    let state = loaded([proposalMsg], { trackedWork: [{ id: 'atw_1', threadId: 'ath_1', entityType: 'Voyage', entityId: 'vyg_1', title: 'V', status: 'Open', state: 'Active' }] });
    expect(proposalForMessage(state, state.messages[0])?.status).toBe('Pending');
    state = conversationReducer(state, { type: 'event', event: ev('ask.proposal', { threadId: 'ath_1', proposal: { id: 'aap_1', threadId: 'ath_1', toolName: 'dispatch', source: 'Captain', status: 'Executed' } }) });
    expect(proposalForMessage(state, state.messages[0])?.status).toBe('Executed');

    const snapshot: AskWorkSnapshot = { entityType: 'Voyage', entityId: 'vyg_1', status: 'Complete', state: 'Succeeded', missions: [{ id: 'msn_1', status: 'Complete' }] };
    state = conversationReducer(state, { type: 'event', event: ev('ask.work', { threadId: 'ath_1', trackedWorkId: 'atw_1', snapshot }) });
    expect(state.snapshots.atw_1.status).toBe('Complete');
    expect(state.trackedWork[0]).toMatchObject({ status: 'Complete', state: 'Succeeded' });
  });

  it('adds tracked work it has not seen from an ask.work event', () => {
    const state = conversationReducer(loaded(), { type: 'event', event: ev('ask.work', { threadId: 'ath_1', trackedWorkId: 'atw_9', snapshot: { entityType: 'Job', entityId: 'job_1', status: 'Running' } }) });
    expect(state.trackedWork).toHaveLength(1);
    expect(state.trackedWork[0]).toMatchObject({ id: 'atw_9', entityType: 'Job', state: 'Active' });
  });

  it('prepends older pages in sequence order', () => {
    let state = loaded([msg('amg_5', 5), msg('amg_6', 6)]);
    state = conversationReducer(state, { type: 'older', threadId: 'ath_1', messages: [msg('amg_3', 3), msg('amg_4', 4)], hasMore: false });
    expect(state.messages.map((m) => m.sequence)).toEqual([3, 4, 5, 6]);
    expect(state.hasMore).toBe(false);
  });

  it('respects activeTurnId when the server reports it', () => {
    let state = conversationReducer(initialConversation('ath_1'), { type: 'event', event: ev('ask.chunk', { threadId: 'ath_1', turnId: 't', delta: 'x' }) });
    expect(state.turnActive).toBe(true);
    state = conversationReducer(state, { type: 'loaded', threadId: 'ath_1', detail: { thread: thread('ath_1', { activeTurnId: null }) }, messages: [], hasMore: false });
    expect(state.turnActive).toBe(false);
    expect(state.streaming).toBeNull();
  });
});

describe('message helpers', () => {
  it('mergeMessages sorts and dedupes by id', () => {
    expect(mergeMessages([msg('b', 2), msg('a', 1)], [msg('b', 2, { contentText: 'new' })]).map((m) => `${m.id}:${m.contentText}`)).toEqual(['a:a', 'b:new']);
  });

  it('workCardHosts puts the card on the first non-milestone message', () => {
    const hosts = workCardHosts([
      msg('m1', 1, { kind: 'WorkUpdate', trackedWorkId: 'atw_1' }),
      msg('m2', 2, { kind: 'ActionResult', trackedWorkId: 'atw_1' }),
      msg('m3', 3, { kind: 'WorkUpdate', trackedWorkId: 'atw_2' }),
    ]);
    expect(hosts).toEqual({ atw_1: 'm2', atw_2: 'm3' });
  });

  it('workCardHosts never puts the card on a WorkReport while another message references the work', () => {
    expect(workCardHosts([
      msg('m1', 1, { kind: 'ActionResult', trackedWorkId: 'atw_1' }),
      msg('m2', 2, { kind: 'WorkReport', trackedWorkId: 'atw_1' }),
    ])).toEqual({ atw_1: 'm1' });
    // A report that arrives before the result in the page still does not take the card.
    expect(workCardHosts([
      msg('r', 1, { kind: 'WorkReport', trackedWorkId: 'atw_1' }),
      msg('u', 2, { kind: 'WorkUpdate', trackedWorkId: 'atw_1' }),
    ])).toEqual({ atw_1: 'u' });
    // Only when nothing else is loaded does the report carry the card, so the work stays reachable.
    expect(workCardHosts([msg('r', 1, { kind: 'WorkReport', trackedWorkId: 'atw_2' })])).toEqual({ atw_2: 'r' });
  });
});

describe('thread list helpers', () => {
  it('orders pinned first, then by most recent message', () => {
    const sorted = sortThreads([
      thread('old', { lastMessageUtc: '2026-10-01T00:00:00Z' }),
      thread('pinned-old', { pinned: true, lastMessageUtc: '2026-09-01T00:00:00Z' }),
      thread('new', { lastMessageUtc: '2026-10-04T00:00:00Z' }),
    ]);
    expect(sorted.map((t) => t.id)).toEqual(['pinned-old', 'new', 'old']);
  });

  it('applies ask.thread updates: unread on other threads, zero on the open one, removal when archived', () => {
    const list = [thread('a'), thread('b')];
    const filter = { search: '', includeArchived: false };
    expect(applyThreadUpdate(list, thread('b', { unreadCount: 2 }), filter, 'a').find((t) => t.id === 'b')?.unreadCount).toBe(2);
    expect(applyThreadUpdate(list, thread('a', { unreadCount: 2 }), filter, 'a').find((t) => t.id === 'a')?.unreadCount).toBe(0);
    expect(applyThreadUpdate(list, thread('b', { archived: true }), filter, 'a').map((t) => t.id)).toEqual(['a']);
    expect(applyThreadUpdate(list, thread('c', { lastMessageUtc: '2026-10-05T00:00:00Z' }), filter, 'a')[0].id).toBe('c');
    expect(applyThreadUpdate(list, thread('d', { title: 'zzz' }), { search: 'abc', includeArchived: false }, 'a')).toHaveLength(2);
  });

  it('tracks live working state from ask.work', () => {
    const t = thread('a', { activeWorkCount: 0 });
    let activity = applyActivityEvent({}, ev('ask.work', { threadId: 'a', trackedWorkId: 'w1', snapshot: { entityType: 'Voyage', entityId: 'v', status: 'InProgress', state: 'Active' } }));
    expect(isThreadWorking(t, activity)).toBe(true);
    activity = applyActivityEvent(activity, ev('ask.work', { threadId: 'a', trackedWorkId: 'w1', snapshot: { entityType: 'Voyage', entityId: 'v', status: 'Complete', state: 'Succeeded' } }));
    expect(isThreadWorking(t, activity)).toBe(false);
    expect(isThreadWorking(thread('b', { activeWorkCount: 2 }), activity)).toBe(true);
  });
});

describe('work helpers', () => {
  it('computes progress from mission rows, counts, or counters', () => {
    expect(workProgress({ entityType: 'Voyage', entityId: 'v', status: 'InProgress', missions: [
      { id: '1', status: 'Complete' }, { id: '2', status: 'Failed' }, { id: '3', status: 'InProgress' }, { id: '4', status: 'Pending' },
    ] })).toEqual({ total: 4, done: 2, failed: 1, percent: 50 });
    expect(workProgress({ entityType: 'Voyage', entityId: 'v', status: 'x', counts: { complete: 3, inProgress: 1 } })).toMatchObject({ total: 4, done: 3 });
    expect(workProgress({ entityType: 'Job', entityId: 'j', status: 'Running', totalCount: 10, completedCount: 4 })).toMatchObject({ percent: 40 });
    expect(workProgress({ entityType: 'Job', entityId: 'j', status: 'Running' })).toBeNull();
  });

  it('counts statuses, decides activity, and links to the detail page', () => {
    expect(statusCounts({ entityType: 'Voyage', entityId: 'v', status: 'x', missions: [{ id: '1', status: 'Failed' }, { id: '2', status: 'Failed' }] })).toEqual([{ status: 'Failed', count: 2 }]);
    expect(isWorkActive({ state: 'Active' })).toBe(true);
    expect(isWorkActive({ status: 'Complete' })).toBe(false);
    expect(isWorkActive({ status: 'SomethingNew' })).toBe(true);
    expect(workRoute('Voyage', 'vyg_1')).toBe('/voyages/vyg_1');
    expect(workRoute('FleetActionRun', 'far_1')).toBe('/fleet-actions/runs/far_1');
    expect(workRoute('VesselImportBatch', 'vib_1')).toBe('/vessels/import?batch=vib_1');
  });
});

describe('optimistic messages (isLocal flag, not an id prefix)', () => {
  it('identifies optimistic messages by the flag only', () => {
    expect(isLocalMessage(msg('local-1', 1, { role: 'User' }))).toBe(false);
    expect(isLocalMessage(msg('tmp_9', 1, { role: 'User', isLocal: true }))).toBe(true);
  });

  it('replaces an optimistic message whose id has no special prefix', () => {
    let state = loaded([msg('amg_1', 1)]);
    state = conversationReducer(state, { type: 'optimisticUser', message: msg('pending-a', 2, { role: 'User', contentText: 'hi', isLocal: true }) });
    state = conversationReducer(state, { type: 'event', event: ev('ask.message', { threadId: 'ath_1', message: msg('amg_2', 2, { role: 'User', contentText: 'hi' }) }) });
    expect(state.messages.map((m) => m.id)).toEqual(['amg_1', 'amg_2']);
    expect(state.messages.some(isLocalMessage)).toBe(false);
  });

  it('a persisted message with a local-looking id is not dropped as optimistic', () => {
    let state = loaded([msg('local-7', 1, { role: 'User', contentText: 'same' })]);
    state = conversationReducer(state, { type: 'event', event: ev('ask.message', { threadId: 'ath_1', message: msg('amg_2', 2, { role: 'User', contentText: 'same' }) }) });
    expect(state.messages.map((m) => m.id)).toEqual(['local-7', 'amg_2']);
  });

  it('clears the flag when the server confirms the message id', () => {
    let state = loaded();
    state = conversationReducer(state, { type: 'optimisticUser', message: msg('pending-b', 1, { role: 'User', isLocal: true }) });
    state = conversationReducer(state, { type: 'confirmUser', localId: 'pending-b', messageId: 'amg_1', turnId: null });
    expect(state.messages[0].id).toBe('amg_1');
    expect(isLocalMessage(state.messages[0])).toBe(false);
  });
});

describe('a turn that finishes before the send returns', () => {
  /** Optimistic send, then the whole turn arrives on the socket before the send's HTTP response. */
  function fastTurn(): ConversationState {
    let state = loaded();
    state = conversationReducer(state, { type: 'optimisticUser', message: msg('local-1', 1, { role: 'User', contentText: 'hi', isLocal: true }) });
    expect(state.turnActive).toBe(true);
    state = conversationReducer(state, { type: 'event', event: ev('ask.turn', { threadId: 'ath_1', turnId: 't1', state: 'started' }) });
    state = conversationReducer(state, { type: 'event', event: ev('ask.message', { threadId: 'ath_1', message: msg('amg_1', 1, { role: 'User', contentText: 'hi' }) }) });
    state = conversationReducer(state, { type: 'event', event: ev('ask.chunk', { threadId: 'ath_1', turnId: 't1', delta: 'Hello' }) });
    state = conversationReducer(state, { type: 'event', event: ev('ask.message', { threadId: 'ath_1', message: msg('amg_2', 2, { contentText: 'Hello' }) }) });
    state = conversationReducer(state, { type: 'event', event: ev('ask.turn', { threadId: 'ath_1', turnId: 't1', state: 'completed', messageId: 'amg_2' }) });
    return state;
  }

  it('stays ended and shows the reply when the turn id arrives after the completion', () => {
    let state = fastTurn();
    state = conversationReducer(state, { type: 'confirmUser', localId: 'local-1', messageId: 'amg_1', turnId: 't1' });
    expect(state.turnActive).toBe(false);
    expect(state.streaming).toBeNull();
    expect(state.messages.map((m) => m.id)).toEqual(['amg_1', 'amg_2']);
  });

  it('ignores late stream events for the finished turn', () => {
    let state = fastTurn();
    state = conversationReducer(state, { type: 'event', event: ev('ask.chunk', { threadId: 'ath_1', turnId: 't1', delta: 'late' }) });
    expect(state.turnActive).toBe(false);
    expect(state.streaming).toBeNull();
  });

  it('a snapshot loaded before the completion does not revive the turn', () => {
    let state = fastTurn();
    state = conversationReducer(state, {
      type: 'loaded', threadId: 'ath_1', detail: { thread: thread('ath_1', { activeTurnId: 't1' }), trackedWork: [] }, messages: [], hasMore: false,
    });
    expect(state.turnActive).toBe(false);
    expect(state.streaming).toBeNull();
  });

  it('a later turn still becomes active from the send response', () => {
    let state = fastTurn();
    state = conversationReducer(state, { type: 'confirmUser', localId: 'local-1', messageId: 'amg_1', turnId: 't1' });
    state = conversationReducer(state, { type: 'optimisticUser', message: msg('local-2', 3, { role: 'User', contentText: 'again', isLocal: true }) });
    state = conversationReducer(state, { type: 'confirmUser', localId: 'local-2', messageId: 'amg_3', turnId: 't2' });
    expect(state.turnActive).toBe(true);
    expect(state.streaming?.turnId).toBe('t2');
  });

  it('remembers a bounded number of finished turns', () => {
    let state = loaded();
    for (let i = 0; i < CLOSED_TURN_LIMIT + 5; i++) {
      state = conversationReducer(state, { type: 'event', event: ev('ask.turn', { threadId: 'ath_1', turnId: `t${i}`, state: 'completed' }) });
    }
    expect(state.closedTurnIds).toHaveLength(CLOSED_TURN_LIMIT);
    expect(state.closedTurnIds[CLOSED_TURN_LIMIT - 1]).toBe(`t${CLOSED_TURN_LIMIT + 4}`);
  });
});

describe('ask.turn state parsing', () => {
  it('maps wire states to the typed union', () => {
    expect(parseTurnState(null)).toBe('started');
    expect(parseTurnState('Completed')).toBe('completed');
    expect(parseTurnState('failed')).toBe('failed');
    expect(parseTurnState('cancelled')).toBe('cancelled');
    expect(parseTurnState('something-new')).toBe('completed');
  });
});
