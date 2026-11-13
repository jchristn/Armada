import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { EnumerationResult } from '../types/models';

vi.mock('../api/client', () => ({
  getAskThread: vi.fn(),
  enumerateAskWork: vi.fn(),
  enumerateAskProposals: vi.fn(),
  enumerateAskToolCalls: vi.fn(),
  enumerateAskTurnToolCalls: vi.fn(),
  enumerateAskWorkMissions: vi.fn(),
  enumerateAskWorkTargets: vi.fn(),
  listCliPermissionRequests: vi.fn(),
}));

const client = await import('../api/client');
const { loadAskThreadView, readAllPages, withToolCalls, withWorkRows, ASK_PAGE_SIZE } = await import('./askPaging');

function page<T>(objects: T[], pageNumber: number, totalRecords: number, pageSize = ASK_PAGE_SIZE): EnumerationResult<T> {
  return { success: true, pageNumber, pageSize, totalPages: Math.ceil(totalRecords / pageSize), totalRecords, objects, totalMs: 0 };
}

beforeEach(() => vi.clearAllMocks());

describe('askPaging', () => {
  it('reads every page and stops at the last one, not at the first', async () => {
    const reads: number[] = [];
    const all = await readAllPages(async (pageNumber) => {
      reads.push(pageNumber);
      return page(pageNumber < 3 ? [pageNumber * 10, pageNumber * 10 + 1] : [30], pageNumber, 5, 2);
    });
    expect(all).toEqual([10, 11, 20, 21, 30]);
    expect(reads).toEqual([1, 2, 3]);
  });

  it('stops on an empty page even when the server reports more pages', async () => {
    const all = await readAllPages(async (pageNumber) => page<number>(pageNumber === 1 ? [1] : [], pageNumber, 50, 1));
    expect(all).toEqual([1]);
  });

  it('composes the thread view from the counted collections and skips the reads it does not need', async () => {
    vi.mocked(client.getAskThread).mockResolvedValue({ thread: { id: 'ath_1' } as never, trackedWorkCount: 2, pendingProposalCount: 0, pendingCliPermissionCount: 0, activeTurn: { turnId: 'atn_1', toolCallCount: 1 } });
    vi.mocked(client.enumerateAskWork).mockResolvedValue(page([{ id: 'atw_1' }, { id: 'atw_2' }] as never[], 1, 2));
    vi.mocked(client.enumerateAskTurnToolCalls).mockResolvedValue(page([{ callId: 'c1', toolName: 'status' }] as never[], 1, 1));
    const view = await loadAskThreadView('ath_1');
    expect(view.trackedWork?.map((w) => w.id)).toEqual(['atw_1', 'atw_2']);
    expect(view.pendingProposals).toEqual([]);
    expect(view.pendingCliPermissions).toEqual([]);
    expect(view.activeTurn?.toolCalls?.map((c) => c.callId)).toEqual(['c1']);
    expect(client.enumerateAskProposals).not.toHaveBeenCalled();
    expect(client.listCliPermissionRequests).not.toHaveBeenCalled();
  });

  it('attaches tool calls only to messages that have them', async () => {
    vi.mocked(client.enumerateAskToolCalls).mockResolvedValue(page([{ callId: 'c1', toolName: 'enumerate' }] as never[], 1, 1));
    const messages = await withToolCalls('ath_1', [
      { id: 'amg_1', threadId: 'ath_1', sequence: 1, role: 'User', kind: 'Text', toolCallCount: 0 },
      { id: 'amg_2', threadId: 'ath_1', sequence: 2, role: 'Assistant', kind: 'Text', toolCallCount: 1 },
    ]);
    expect(messages[0].toolCalls).toBeUndefined();
    expect(messages[1].toolCalls?.[0].toolName).toBe('enumerate');
    expect(client.enumerateAskToolCalls).toHaveBeenCalledTimes(1);
  });

  it('loads the first page of work rows with the total, so a card can say it shows part of them', async () => {
    vi.mocked(client.enumerateAskWorkMissions).mockResolvedValue(page([{ id: 'msn_1', title: 'a', status: 'Complete' }] as never[], 1, 250));
    const snapshot = await withWorkRows('ath_1', 'atw_1', { entityType: 'Voyage', entityId: 'vyg_1', status: 'InProgress', missionCount: 250 });
    expect(snapshot.missions?.length).toBe(1);
    expect(snapshot.rowTotal).toBe(250);
    const run = await withWorkRows('ath_1', 'atw_2', { entityType: 'Job', entityId: 'job_1', status: 'Running' });
    expect(run.missions).toBeUndefined();
    expect(client.enumerateAskWorkTargets).not.toHaveBeenCalled();
  });
});
