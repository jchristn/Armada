import { ApiError, captureCaptainUsage, getUsageLatest, getUsageMissions, getUsageSeries, setAuthToken } from './client';
import { onlyCallArgs } from '../test/mockCalls';

function jsonResponse(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
}
function stub(response: Response) {
  const fetchMock = vi.fn().mockResolvedValue(response);
  vi.stubGlobal('fetch', fetchMock);
  return fetchMock;
}
const EMPTY = { Success: true, PageNumber: 1, PageSize: 100, TotalPages: 0, TotalRecords: 0, TotalMs: 1, Objects: [] };

describe('usage client', () => {
  afterEach(() => { vi.unstubAllGlobals(); setAuthToken(null); });

  it('sends the batch latest read with encoded comma-separated ids, bounded page and the session token', async () => {
    setAuthToken('tok_1');
    const fetchMock = stub(jsonResponse(200, { ...EMPTY, Objects: [{ Id: 'uss_1', CaptainIds: ['cpt_a'], Status: 'AuthMissing', Windows: [], HostId: 'hbr_1' }] }));
    const result = await getUsageLatest({ captainIds: 'cpt_a,cpt_b', pageNumber: 2, pageSize: 100 });
    const [url, init] = onlyCallArgs(fetchMock) as [string, RequestInit];
    expect(url).toBe('/api/v1/usage/latest?pageNumber=2&pageSize=100&captainIds=cpt_a%2Ccpt_b');
    expect((init.headers as Record<string, string>)['X-Token']).toBe('tok_1');
    expect(result.objects[0]).toMatchObject({ id: 'uss_1', captainIds: ['cpt_a'], status: 'AuthMissing', hostId: 'hbr_1' });
  });

  it('omits empty filters and keeps the exact series and drill-down filters', async () => {
    const fetchMock = stub(jsonResponse(200, { FromUtc: 'a', ToUtc: 'b', BucketMinutes: 60, Resolution: 'raw', GroupBy: 'account', Series: [], Coverage: {} }));
    await getUsageSeries({ from: '2026-11-01T00:00:00.000Z', to: '2026-11-02T00:00:00.000Z', metric: 'limits', groupBy: 'account', bucketMinutes: 60, provider: '', accountKey: undefined });
    expect(onlyCallArgs(fetchMock)[0]).toBe('/api/v1/usage/series?from=2026-11-01T00%3A00%3A00.000Z&to=2026-11-02T00%3A00%3A00.000Z&metric=limits&groupBy=account&bucketMinutes=60');
    const missions = stub(jsonResponse(200, { ...EMPTY, Coverage: { ExcludedUnattributedRecords: 2 } }));
    const page = await getUsageMissions({ from: 'f', to: 't', provider: 'anthropic', accountKey: 'acct_1', hostId: 'hbr_1' });
    expect(onlyCallArgs(missions)[0]).toBe('/api/v1/usage/missions?from=f&to=t&provider=anthropic&accountKey=acct_1&hostId=hbr_1');
    expect(page.coverage.excludedUnattributedRecords).toBe(2);
  });

  it('posts an empty capture body and surfaces cooldown as ApiError 429 with the next eligible time', async () => {
    const ok = stub(jsonResponse(202, { JobId: 'job_1', Status: 'Queued', JoinedExisting: false, NextEligibleUtc: null }));
    const receipt = await captureCaptainUsage('cpt_a');
    const [url, init] = onlyCallArgs(ok) as [string, RequestInit];
    expect(url).toBe('/api/v1/captains/cpt_a/usage/capture');
    expect(init.method).toBe('POST');
    expect(init.body).toBe('{}');
    expect(receipt).toMatchObject({ jobId: 'job_1', status: 'Queued' });
    stub(jsonResponse(429, { Error: 'SlowDown', StatusCode: 429, Message: 'Cooldown', Data: { Code: 'Cooldown', NextEligibleUtc: '2026-11-01T11:00:00Z' } }));
    const err = await captureCaptainUsage('cpt_a').catch((e: unknown) => e);
    expect(err).toBeInstanceOf(ApiError);
    expect((err as ApiError).status).toBe(429);
    expect((err as ApiError).data).toMatchObject({ nextEligibleUtc: '2026-11-01T11:00:00Z' });
  });
});
