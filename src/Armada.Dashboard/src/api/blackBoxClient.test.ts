import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  ApiError,
  apiErrorCode,
  configureClient,
  deleteBlackBoxEntries,
  enumerateBlackBox,
  getBlackBox,
  getBlackBoxSection,
  getMissionBlackBox,
  getMissionBlackBoxBrief,
  readBlackBoxObject,
  resolveBlackBox,
  runBlackBoxRecovery,
  searchBlackBox,
  setAuthToken,
} from './client';
import { onlyCallArgs } from '../test/mockCalls';

function jsonResponse(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
}

function stub(response: Response) {
  const fetchMock = vi.fn().mockResolvedValue(response);
  vi.stubGlobal('fetch', fetchMock);
  return fetchMock;
}

const ENTRY = {
  Id: 'bbx_1', SubjectType: 'Mission', MissionId: 'msn_1', Phase: 'Landing', Step: 'LocalMerge', Triage: 'UserConfig',
  Resolution: 'Open', State: 'Analyzed', CaptureMode: 'Inline', Attempt: 1, IsCurrent: true, Pinned: false, TotalBytes: 10, ObjectCount: 3,
  CreatedUtc: '2026-10-28T10:00:00Z',
  Record: {
    EntryId: 'bbx_1', SchemaVersion: 1,
    Analysis: { RecoveryPaths: [{ Id: 'rp1', Rank: 1, Title: 'Retry', Action: { Tool: 'retry_landing', Arguments: { MissionId: 'msn_1' } } }] },
  },
};

describe('black box client', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    setAuthToken(null);
  });

  it('reads a mission entry, camelizes it and keeps action arguments as written', async () => {
    setAuthToken('tok_1');
    const fetchMock = stub(jsonResponse(200, ENTRY));
    const entry = await getMissionBlackBox('msn_1');
    const [url, init] = onlyCallArgs(fetchMock);
    expect(url).toMatch(/\/api\/v1\/missions\/msn_1\/blackbox$/);
    expect(init.headers['X-Token']).toBe('tok_1');
    expect(entry.id).toBe('bbx_1');
    expect(entry.record?.analysis?.recoveryPaths?.[0].action?.arguments).toEqual({ MissionId: 'msn_1' });
  });

  it('reads an entry without its record and one section', async () => {
    const fetchMock = stub(jsonResponse(200, { ...ENTRY, Record: undefined }));
    await getBlackBox('bbx_1', false);
    expect(onlyCallArgs(fetchMock)[0]).toMatch(/\/api\/v1\/blackbox\/bbx_1\?includeRecord=false$/);
    const sectionMock = stub(jsonResponse(200, { EntryId: 'bbx_1', Section: 'Conflict', Record: { EntryId: 'bbx_1', SchemaVersion: 1, Conflict: { IsConflict: true, ConflictedFiles: ['a.ts'] } } }));
    const section = await getBlackBoxSection('bbx_1', 'Conflict');
    expect(onlyCallArgs(sectionMock)[0]).toMatch(/\/sections\/Conflict$/);
    expect(section.record.conflict?.conflictedFiles).toEqual(['a.ts']);
  });

  it('returns the brief as Markdown text with the audience on the querystring', async () => {
    const fetchMock = stub(new Response('# FIX THIS', { status: 200, headers: { 'Content-Type': 'text/markdown' } }));
    const brief = await getMissionBlackBoxBrief('msn_1', { audience: 'maintainer', redactHosts: true });
    expect(brief).toBe('# FIX THIS');
    expect(onlyCallArgs(fetchMock)[0]).toMatch(/\/api\/v1\/missions\/msn_1\/blackbox\/brief\?audience=maintainer&redactHosts=true$/);
  });

  it('sends PascalCase filters for enumerate and search', async () => {
    const page = { Success: true, PageNumber: 1, PageSize: 10, TotalPages: 0, TotalRecords: 0, Objects: [] };
    const fetchMock = stub(jsonResponse(200, page));
    await enumerateBlackBox({ vesselId: 'vsl_1', resolution: 'Open', pinned: false, pageSize: 25 });
    expect(JSON.parse(onlyCallArgs(fetchMock)[1].body)).toEqual({ VesselId: 'vsl_1', Resolution: 'Open', Pinned: false, PageSize: 25 });
    const searchMock = stub(jsonResponse(200, page));
    await searchBlackBox({ query: 'merge conflict', fingerprint: 'fp_1' });
    const [url, init] = onlyCallArgs(searchMock);
    expect(url).toMatch(/\/api\/v1\/blackbox\/search$/);
    expect(JSON.parse(init.body)).toEqual({ Fingerprint: 'fp_1', Query: 'merge conflict' });
  });

  it('pages an object with the name as one encoded segment and reads the paging headers', async () => {
    const fetchMock = stub(new Response('M a.ts', {
      status: 200,
      headers: { 'Content-Type': 'text/plain', ETag: '"abc"', 'X-BlackBox-Offset': '0', 'X-BlackBox-Total-Bytes': '3000000', 'X-BlackBox-Next-Offset': '1048576' },
    }));
    const read = await readBlackBoxObject('bbx_1', 'evidence/git-status.txt', { offset: 0, length: 1048576 });
    expect(onlyCallArgs(fetchMock)[0]).toMatch(/\/api\/v1\/blackbox\/bbx_1\/objects\/evidence%2Fgit-status\.txt\?offset=0&length=1048576$/);
    expect(read).toMatchObject({ contentType: 'text/plain', etag: 'abc', offset: 0, totalBytes: 3000000, nextOffset: 1048576, text: 'M a.ts' });
  });

  it('surfaces the black box code of an object read error', async () => {
    stub(jsonResponse(410, { Error: 'NotFound', Message: 'gone', Data: { Code: 'BlackBoxObjectMissing', Field: null } }));
    const err = await readBlackBoxObject('bbx_1', 'evidence/diff.patch').catch((e: unknown) => e);
    expect(err).toBeInstanceOf(ApiError);
    expect((err as ApiError).status).toBe(410);
    expect(apiErrorCode(err)).toBe('BlackBoxObjectMissing');
  });

  it('dry runs recovery by default and sends the token and acknowledgement for the real run', async () => {
    const fetchMock = stub(jsonResponse(200, { EntryId: 'bbx_1', PathId: 'rp1', DryRun: true, ConfirmToken: 'tok', Succeeded: true }));
    const dry = await runBlackBoxRecovery('bbx_1', 'rp1');
    expect(dry.confirmToken).toBe('tok');
    expect(JSON.parse(onlyCallArgs(fetchMock)[1].body)).toEqual({ DryRun: true, AuthorKind: 'Operator' });
    const realMock = stub(jsonResponse(200, { EntryId: 'bbx_1', PathId: 'rp1', DryRun: false, Succeeded: true, StepResults: [{ Order: 1, Tool: 'retry_landing', Succeeded: true }] }));
    const real = await runBlackBoxRecovery('bbx_1', 'rp1', { dryRun: false, confirmToken: 'tok', acknowledgeDataLoss: true });
    const [url, init] = onlyCallArgs(realMock);
    expect(url).toMatch(/\/api\/v1\/blackbox\/bbx_1\/recovery\/rp1$/);
    expect(JSON.parse(init.body)).toEqual({ DryRun: false, ConfirmToken: 'tok', AcknowledgeDataLoss: true, AuthorKind: 'Operator' });
    expect(real.stepResults?.[0].succeeded).toBe(true);
  });

  it('reports a 501 from a server without the recovery runner', async () => {
    stub(jsonResponse(501, { Error: 'NotImplemented', StatusCode: 501, Message: 'not yet', Data: { Code: 'NotImplemented' } }));
    const err = await runBlackBoxRecovery('bbx_1', 'rp1').catch((e: unknown) => e);
    expect((err as ApiError).status).toBe(501);
    expect(apiErrorCode(err)).toBe('NotImplemented');
  });

  it('resolves and deletes by id', async () => {
    const fetchMock = stub(jsonResponse(200, { ...ENTRY, Resolution: 'Resolved' }));
    const resolved = await resolveBlackBox('bbx_1', { resolution: 'Resolved', note: 'landed' });
    expect(JSON.parse(onlyCallArgs(fetchMock)[1].body)).toEqual({ Resolution: 'Resolved', Note: 'landed', Link: null });
    expect(resolved.resolution).toBe('Resolved');
    const deleteMock = stub(jsonResponse(200, { Requested: 2, Deleted: 1, Pending: 0, BytesFreed: 5, Skipped: ['bbx_2'] }));
    const result = await deleteBlackBoxEntries(['bbx_1', 'bbx_2']);
    expect(JSON.parse(onlyCallArgs(deleteMock)[1].body)).toEqual({ Ids: ['bbx_1', 'bbx_2'] });
    expect(result.skipped).toEqual(['bbx_2']);
  });

  it('uses the configured base URL', async () => {
    configureClient({ baseUrl: 'https://armada.example' });
    try {
      const fetchMock = stub(jsonResponse(200, ENTRY));
      await getBlackBox('bbx_1');
      expect(onlyCallArgs(fetchMock)[0]).toBe('https://armada.example/api/v1/blackbox/bbx_1?includeRecord=true');
    } finally {
      configureClient({ baseUrl: '' });
    }
  });
});
