import { enumerateCliPermissionRules, enumerateHarbors, enumerateModelEndpoints, enumerateMuxEndpoints, getCaptainTools, listAllMuxEndpoints, setAuthToken } from './client';
import { onlyCallArgs } from '../test/mockCalls';

function json(body: Record<string, unknown>) {
  return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } });
}

function muxPage(pageNumber: number, totalPages: number, names: string[], totalRecords: number, success = true) {
  return json({
    Success: success, PageNumber: pageNumber, PageSize: 1000, TotalPages: totalPages, TotalRecords: totalRecords,
    Objects: names.map((Name) => ({ Name })), ContractVersion: 1, ConfigDirectory: '/cfg', ErrorCode: success ? '' : 'endpoint_list_error', ErrorMessage: success ? '' : 'boom',
  });
}

describe('Group D paged helpers', () => {
  beforeEach(() => setAuthToken('tok'));
  afterEach(() => vi.unstubAllGlobals());

  it('enumerateMuxEndpoints reads one page with the paging and directory in the querystring', async () => {
    const fetchMock = vi.fn().mockResolvedValue(muxPage(2, 3, ['b'], 21));
    vi.stubGlobal('fetch', fetchMock);
    const page = await enumerateMuxEndpoints({ configDirectory: '/my cfg', pageNumber: 2, pageSize: 10 });
    const [url] = onlyCallArgs(fetchMock) as [string, RequestInit];
    expect(url).toContain('/api/v1/runtimes/mux/endpoints?pageNumber=2&pageSize=10&configDirectory=%2Fmy+cfg');
    expect(page.totalRecords).toBe(21);
    expect(page.objects.map((e) => e.name)).toEqual(['b']);
  });

  it('listAllMuxEndpoints walks the pages explicitly until totalPages and keeps the server total', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(muxPage(1, 3, ['a'], 3))
      .mockResolvedValueOnce(muxPage(2, 3, ['b'], 3))
      .mockResolvedValueOnce(muxPage(3, 3, ['c'], 3));
    vi.stubGlobal('fetch', fetchMock);
    const all = await listAllMuxEndpoints('/cfg');
    expect(fetchMock).toHaveBeenCalledTimes(3);
    for (const [i, call] of fetchMock.mock.calls.entries()) {
      expect(String(call[0])).toContain(`pageNumber=${i + 1}&pageSize=1000`);
    }
    expect(all.objects.map((e) => e.name)).toEqual(['a', 'b', 'c']);
    expect(all.totalRecords).toBe(3);
  });

  it('listAllMuxEndpoints returns the failing page instead of a partial list', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(muxPage(1, 2, ['a'], 2))
      .mockResolvedValueOnce(muxPage(2, 2, [], 2, false));
    vi.stubGlobal('fetch', fetchMock);
    const result = await listAllMuxEndpoints();
    expect(result.success).toBe(false);
    expect(result.errorMessage).toBe('boom');
  });

  it('getCaptainTools sends tools paging, the source kind, and refresh in the querystring', async () => {
    const fetchMock = vi.fn().mockResolvedValue(json({ CaptainId: 'cpt_1', Tools: { PageNumber: 2, PageSize: 25, TotalPages: 3, TotalRecords: 60, Objects: [] } }));
    vi.stubGlobal('fetch', fetchMock);
    const result = await getCaptainTools('cpt_1', { sourceKind: 'McpServer', pageNumber: 2, pageSize: 25, refresh: true });
    const [url] = onlyCallArgs(fetchMock) as [string, RequestInit];
    expect(url).toContain('/api/v1/captains/cpt_1/tools?pageNumber=2&pageSize=25&sourceKind=McpServer&refresh=true');
    expect(result.tools.totalRecords).toBe(60);
  });

  it('getCaptainTools with no query asks for the default first page', async () => {
    const fetchMock = vi.fn().mockResolvedValue(json({ CaptainId: 'cpt_1' }));
    vi.stubGlobal('fetch', fetchMock);
    await getCaptainTools('cpt_1');
    const [url] = onlyCallArgs(fetchMock) as [string, RequestInit];
    expect(url.endsWith('/api/v1/captains/cpt_1/tools')).toBe(true);
  });

  it('enumerateHarbors, enumerateModelEndpoints and enumerateCliPermissionRules return one page with its true total', async () => {
    const calls: Array<[() => Promise<{ totalRecords: number; objects: unknown[] }>, string]> = [
      [() => enumerateHarbors(), '/api/v1/harbors/enumerate'],
      [() => enumerateModelEndpoints({ pageNumber: 2, pageSize: 50 }), '/api/v1/model-endpoints/enumerate'],
      [() => enumerateCliPermissionRules({ scope: 'Global' }), '/api/v1/cli-permissions/rules/enumerate'],
    ];
    for (const [call, path] of calls) {
      const fetchMock = vi.fn().mockResolvedValue(json({ Success: true, PageNumber: 1, PageSize: 1000, TotalPages: 2, TotalRecords: 1500, Objects: [{ Id: 'x' }] }));
      vi.stubGlobal('fetch', fetchMock);
      const page = await call();
      expect(fetchMock).toHaveBeenCalledTimes(1);
      const [url, init] = onlyCallArgs(fetchMock) as [string, RequestInit];
      expect(url).toContain(path);
      expect(init.method).toBe('POST');
      expect(page.totalRecords).toBe(1500);
      expect(page.objects).toHaveLength(1);
    }
    const fetchMock = vi.fn().mockResolvedValue(json({ Success: true, Objects: [] }));
    vi.stubGlobal('fetch', fetchMock);
    await enumerateModelEndpoints({ pageNumber: 2, pageSize: 50 });
    const [, init] = onlyCallArgs(fetchMock) as [string, RequestInit];
    expect(JSON.parse(String(init.body))).toMatchObject({ PageNumber: 2, PageSize: 50 });
  });
});
