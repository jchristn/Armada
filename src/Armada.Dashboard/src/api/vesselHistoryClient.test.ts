import { afterEach, describe, expect, it, vi } from 'vitest';
import { getVesselCommitActivity, getVesselCommits } from './client';
import { onlyCallArgs } from '../test/mockCalls';

function jsonResponse(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
}

describe('vessel history client', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('activity sends camelCase query params and camelizes the response', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(200, {
      VesselId: 'vsl_1', Branch: 'dev', From: '2026-01-01', To: '2026-01-02', UtcOffsetMinutes: -420,
      Days: [{ Date: '2026-01-01', Count: 2 }, { Date: '2026-01-02', Count: 0 }],
      TotalCommits: 2, MaxDayCount: 2, FirstCommitUtc: '2025-03-01T00:00:00Z', LastCommitUtc: '2026-01-01T10:00:00Z', Error: null,
    }));
    vi.stubGlobal('fetch', fetchMock);
    const result = await getVesselCommitActivity('vsl_1', { branch: 'dev', from: '2026-01-01', to: '2026-01-02', utcOffsetMinutes: -420 });
    expect(result.days[0]).toEqual({ date: '2026-01-01', count: 2 });
    expect(result.maxDayCount).toBe(2);
    expect(result.firstCommitUtc).toBe('2025-03-01T00:00:00Z');
    const url = new URL(onlyCallArgs(fetchMock)[0], 'http://localhost');
    expect(url.pathname).toBe('/api/v1/vessels/vsl_1/history/activity');
    expect(Object.fromEntries(url.searchParams)).toEqual({ branch: 'dev', from: '2026-01-01', to: '2026-01-02', utcOffsetMinutes: '-420' });
  });

  it('commits sends cursor and limit, and omits unset params', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(200, {
      VesselId: 'vsl_1', Branch: 'main', NextCursor: null, Error: null,
      Commits: [{ Sha: 'abc', ShortSha: 'abc', Files: [{ Kind: 'Renamed', Path: 'b', OldPath: 'a', AddedLines: 1, DeletedLines: 0, IsBinary: false }] }],
    }));
    vi.stubGlobal('fetch', fetchMock);
    const result = await getVesselCommits('vsl_1', { cursor: 'cur_1', limit: 50 });
    expect(result.commits[0].files[0]).toEqual({ kind: 'Renamed', path: 'b', oldPath: 'a', addedLines: 1, deletedLines: 0, isBinary: false });
    expect(result.nextCursor).toBeNull();
    const url = new URL(onlyCallArgs(fetchMock)[0], 'http://localhost');
    expect(url.pathname).toBe('/api/v1/vessels/vsl_1/history/commits');
    expect(Object.fromEntries(url.searchParams)).toEqual({ cursor: 'cur_1', limit: '50' });
  });

  it('commits surfaces a 400 message', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse(400, { Message: 'Invalid cursor.' })));
    await expect(getVesselCommits('vsl_1', { cursor: 'bad' })).rejects.toThrow('Invalid cursor.');
  });
});
