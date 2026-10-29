import * as assert from 'assert';
import { describe, it } from 'node:test';
import {
  ArmadaApiError,
  ArmadaBlackBoxClient,
  ASK_PREFILL_QUERY_PARAM,
  askArmadaPrompt,
  BLACKBOX_EVENT_TYPES,
  dashboardAskUrl,
  describeError,
  kindForStatus,
  normalizeBaseUrl,
  Routes,
} from '../../src/api/blackbox';
import { MockFetch, page } from '../fakes/fetch';
import { BASE, client, fullEntry, summary, VESSEL_A } from '../fakes/fixtures';

async function rejectsWith(promise: Promise<unknown>, kind: string, check?: (err: ArmadaApiError) => void): Promise<void> {
  await assert.rejects(promise, (err: unknown) => {
    assert.ok(err instanceof ArmadaApiError, `expected ArmadaApiError, got ${String(err)}`);
    assert.strictEqual(err.kind, kind);
    check?.(err);
    return true;
  });
}

describe('ArmadaBlackBoxClient construction', () => {
  it('rejects an empty URL or key as notConfigured', () => {
    assert.throws(() => new ArmadaBlackBoxClient({ baseUrl: '', apiKey: 'k' }), (e: unknown) => e instanceof ArmadaApiError && e.kind === 'notConfigured');
    assert.throws(() => new ArmadaBlackBoxClient({ baseUrl: BASE, apiKey: '' }), (e: unknown) => e instanceof ArmadaApiError && e.kind === 'notConfigured');
  });

  it('normalizes the base URL', () => {
    assert.strictEqual(normalizeBaseUrl('http://h:7890/'), 'http://h:7890');
    assert.strictEqual(normalizeBaseUrl('http://h:7890/dashboard/'), 'http://h:7890');
    assert.strictEqual(normalizeBaseUrl('localhost:7890'), 'http://localhost:7890');
    assert.strictEqual(normalizeBaseUrl(' https://a.example.com '), 'https://a.example.com');
  });

  it('builds ws and wss URLs with the token query parameter', () => {
    const mock = new MockFetch();
    const ws = new URL(client(mock).webSocketUrl());
    assert.strictEqual(ws.protocol, 'ws:');
    assert.strictEqual(ws.pathname, '/ws');
    assert.strictEqual(ws.searchParams.get('token'), 'secret-key');
    const secure = new ArmadaBlackBoxClient({ baseUrl: 'https://x.example.com', apiKey: 'a+b/c=', fetchImpl: mock.fetch });
    const wss = new URL(secure.webSocketUrl());
    assert.strictEqual(wss.protocol, 'wss:');
    assert.strictEqual(wss.searchParams.get('token'), 'a+b/c=');
  });
});

describe('ArmadaBlackBoxClient requests', () => {
  it('sends a bearer token by default and X-Api-Key when configured', async () => {
    const mock = new MockFetch().on('GET', '/api/v1/blackbox/bbx_1', { json: fullEntry() });
    await client(mock).getEntry('bbx_1');
    assert.strictEqual(mock.requests[0].headers.Authorization, 'Bearer secret-key');
    assert.strictEqual(mock.requests[0].headers['X-Api-Key'], undefined);
    await client(mock, { authHeader: 'X-Api-Key' }).getEntry('bbx_1');
    assert.strictEqual(mock.requests[1].headers['X-Api-Key'], 'secret-key');
    assert.strictEqual(mock.requests[1].headers.Authorization, undefined);
  });

  it('routes mission, voyage and entry ids to their endpoints', async () => {
    const mock = new MockFetch()
      .on('GET', '/api/v1/missions/msn_x/blackbox', { json: fullEntry() })
      .on('GET', '/api/v1/voyages/vyg_x/blackbox', { json: fullEntry({ Id: 'bbx_v' }) })
      .on('GET', '/api/v1/blackbox/bbx_9', { json: fullEntry({ Id: 'bbx_9' }) });
    const c = client(mock);
    assert.strictEqual((await c.getEntryForAnyId(' msn_x ')).Id, 'bbx_1');
    assert.strictEqual((await c.getEntryForAnyId('vyg_x')).Id, 'bbx_v');
    assert.strictEqual((await c.getEntryForAnyId('bbx_9')).Id, 'bbx_9');
    await rejectsWith(c.getEntryForAnyId('  '), 'badRequest');
  });

  it('enumerates entries with a PascalCase body, defaults and the tenant filter', async () => {
    const mock = new MockFetch().on('POST', '/api/v1/blackbox/enumerate', { json: page([summary()], 7) });
    const result = await client(mock, { tenant: 'ten_1' }).enumerateEntries({ VesselId: 'vsl_a', Resolution: 'Open' });
    assert.strictEqual(result.TotalRecords, 7);
    assert.deepStrictEqual(mock.requests[0].body, { PageNumber: 1, PageSize: 50, Order: 'CreatedDescending', VesselId: 'vsl_a', Resolution: 'Open', TenantId: 'ten_1' });
    assert.strictEqual(mock.requests[0].headers['Content-Type'], 'application/json');
  });

  it('lists vessels with paging and the tenant query, stopping at the last page', async () => {
    let calls = 0;
    const mock = new MockFetch().on('GET', '/api/v1/vessels', () => {
      calls++;
      return { json: { Success: true, PageNumber: calls, PageSize: 1000, TotalPages: 2, TotalRecords: 2, Objects: [{ ...VESSEL_A, Id: `vsl_${calls}` }] } };
    });
    const vessels = await client(mock, { tenant: 'ten_1' }).listAllVessels();
    assert.deepStrictEqual(vessels.map((v) => v.Id), ['vsl_1', 'vsl_2']);
    assert.ok(mock.requests[0].url.includes('pageNumber=1&pageSize=1000&tenantId=ten_1'));
    assert.ok(mock.requests[1].url.includes('pageNumber=2'));
  });

  it('returns the brief as Markdown text with the audience', async () => {
    const mock = new MockFetch().on('GET', '/api/v1/blackbox/bbx_1/brief', { text: '# FIX THIS\n\nbody' });
    const brief = await client(mock).getBrief('bbx_1', 'maintainer');
    assert.strictEqual(brief, '# FIX THIS\n\nbody');
    assert.ok(mock.requests[0].url.endsWith('?audience=maintainer'));
    assert.match(mock.requests[0].headers.Accept, /text\/markdown/);
  });

  it('reads sections, objects and notes', async () => {
    const mock = new MockFetch()
      .on('GET', '/api/v1/blackbox/bbx_1/sections/Conflict', { json: { EntryId: 'bbx_1', Section: 'Conflict', Record: { Conflict: { IsConflict: true, ConflictedFiles: ['a.ts'] } } } })
      .on('GET', '/api/v1/blackbox/bbx_1/objects', { json: page([{ Name: 'brief.md' }]) })
      .on('GET', '/api/v1/blackbox/bbx_1/objects/evidence%2Fgit-status.txt', { text: 'M a.ts' })
      .on('GET', '/api/v1/blackbox/bbx_1/notes', { json: page([{ Body: 'n' }]) });
    const c = client(mock);
    assert.deepStrictEqual((await c.getSection('bbx_1', 'Conflict')).Record?.Conflict, { IsConflict: true, ConflictedFiles: ['a.ts'] });
    assert.strictEqual((await c.listObjects('bbx_1')).Objects[0].Name, 'brief.md');
    assert.strictEqual(await c.readObjectText('bbx_1', 'evidence/git-status.txt', 0, 100), 'M a.ts');
    assert.ok(mock.requests[2].url.endsWith('/objects/evidence%2Fgit-status.txt?offset=0&length=100'));
    assert.strictEqual((await c.listNotes('bbx_1')).Objects[0].Body, 'n');
  });

  it('adds notes and validates them before sending', async () => {
    const mock = new MockFetch().on('POST', '/api/v1/blackbox/bbx_1/notes', { json: { Body: 'hi' } });
    const c = client(mock);
    await c.addNote('bbx_1', '  hi  ');
    assert.deepStrictEqual(mock.requests[0].body, { Body: 'hi' });
    await rejectsWith(c.addNote('bbx_1', '   '), 'badRequest');
    await rejectsWith(c.addNote('bbx_1', 'x'.repeat(8001)), 'badRequest');
    assert.strictEqual(mock.requests.length, 1);
  });

  it('resolves with trimmed note and link and omits empty ones', async () => {
    const mock = new MockFetch().on('POST', '/api/v1/blackbox/bbx_1/resolve', { json: fullEntry({ Resolution: 'Resolved' }) });
    const c = client(mock);
    await c.resolve('bbx_1', { Resolution: 'Resolved', Note: ' fixed ', Link: ' msn_z ' });
    await c.resolve('bbx_1', { Resolution: 'WontFix', Note: '', Link: '  ' });
    assert.deepStrictEqual(mock.requests[0].body, { Resolution: 'Resolved', Note: 'fixed', Link: 'msn_z' });
    assert.deepStrictEqual(mock.requests[1].body, { Resolution: 'WontFix' });
  });

  it('dry runs and runs recovery paths with the confirm token', async () => {
    const mock = new MockFetch().on('POST', '/api/v1/blackbox/bbx_1/recovery/rp1', (req) => ({
      json: (req.body as { DryRun: boolean }).DryRun ? { DryRun: true, ConfirmToken: 'tok' } : { DryRun: false, Success: true },
    }));
    const c = client(mock);
    assert.strictEqual((await c.dryRunRecovery('bbx_1', 'rp1')).ConfirmToken, 'tok');
    await c.runRecovery('bbx_1', 'rp1', 'tok', false);
    await c.runRecovery('bbx_1', 'rp1', 'tok', true);
    assert.deepStrictEqual(mock.requests[0].body, { DryRun: true, AuthorKind: 'Operator' });
    assert.deepStrictEqual(mock.requests[1].body, { DryRun: false, ConfirmToken: 'tok', AuthorKind: 'Operator' });
    assert.deepStrictEqual(mock.requests[2].body, { DryRun: false, ConfirmToken: 'tok', AcknowledgeDataLoss: true, AuthorKind: 'Operator' });
  });

  it('refuses a real recovery run without a confirm token, without calling the server', async () => {
    const mock = new MockFetch();
    await rejectsWith(client(mock).runRecovery('bbx_1', 'rp1', '', false), 'badRequest');
    assert.strictEqual(mock.requests.length, 0);
  });

  it('enumerates hotspots for a vessel', async () => {
    const mock = new MockFetch().on('POST', '/api/v1/blackbox/hotspots/enumerate', { json: page([{ VesselId: 'vsl_a', Directory: 'src/auth', FileCount: 2, EntryCount: 4, LastSeenUtc: '2026-10-28T10:00:00Z' }]) });
    const result = await client(mock).listHotspots('vsl_a', 5);
    assert.strictEqual(result.Objects[0].Directory, 'src/auth');
    assert.deepStrictEqual(mock.requests[0].body, { VesselId: 'vsl_a', PageNumber: 1, PageSize: 5 });
  });

  it('encodes ids in route paths', () => {
    assert.strictEqual(Routes.entry('a/b c'), '/api/v1/blackbox/a%2Fb%20c');
    assert.strictEqual(Routes.object('bbx_1', 'evidence/x y.txt'), '/api/v1/blackbox/bbx_1/objects/evidence%2Fx%20y.txt');
  });
});

describe('ArmadaBlackBoxClient errors', () => {
  const cases: Array<{ status: number; error: string; kind: string; text: RegExp }> = [
    { status: 401, error: 'NotAuthorized', kind: 'unauthorized', text: /rejected the API key/ },
    { status: 403, error: 'Forbidden', kind: 'forbidden', text: /not allowed/ },
    { status: 404, error: 'BlackBoxNotFound', kind: 'notFound', text: /No black box entry/ },
    { status: 409, error: 'BlackBoxDeleting', kind: 'conflict', text: /conflicts/ },
    { status: 410, error: 'BlackBoxObjectMissing', kind: 'gone', text: /missing from storage/ },
    { status: 503, error: 'BlackBoxStoreUnavailable', kind: 'unavailable', text: /store is unavailable/ },
    { status: 501, error: 'NotImplemented', kind: 'notImplemented', text: /recovery runner may not be merged/ },
    { status: 500, error: 'InternalError', kind: 'server', text: /server error/ },
    { status: 400, error: 'BadRequest', kind: 'badRequest', text: /rejected the request/ },
  ];
  for (const c of cases) {
    it(`maps ${c.status} ${c.error} to ${c.kind} with a clear message`, async () => {
      const mock = new MockFetch().otherwise(() => ({ status: c.status, json: { Error: c.error, StatusCode: c.status, Message: 'detail text' } }));
      await rejectsWith(client(mock).getEntry('bbx_1'), c.kind, (err) => {
        assert.strictEqual(err.status, c.status);
        assert.strictEqual(err.code, c.error);
        assert.strictEqual(err.serverMessage, 'detail text');
        assert.match(describeError(err), c.text);
      });
    });
  }

  it('takes the code from Data.Code over Error (the server sends Error NotFound with Data.Code BlackBoxNotFound)', async () => {
    const mock = new MockFetch().otherwise(() => ({ status: 404, json: { Error: 'NotFound', StatusCode: 404, Message: 'Black box entry bbx_1 not found.', Data: { Code: 'BlackBoxNotFound' } } }));
    await rejectsWith(client(mock).getEntry('bbx_1'), 'notFound', (err) => {
      assert.strictEqual(err.code, 'BlackBoxNotFound');
      assert.match(describeError(err), /No black box entry/);
    });
    const field = new MockFetch().otherwise(() => ({ status: 400, json: { Error: 'BadRequest', Message: 'audience must be agent, user or maintainer.', Data: { Code: 'InvalidArgument', Field: 'audience' } } }));
    await rejectsWith(client(field).getBrief('bbx_1', 'agent'), 'badRequest', (err) => assert.strictEqual(err.code, 'InvalidArgument'));
  });

  it('maps a 404 without a black box code to a route hint', async () => {
    const mock = new MockFetch().otherwise(() => ({ status: 404, text: 'Not Found' }));
    await rejectsWith(client(mock).listHotspots('vsl_a'), 'notFound', (err) => {
      assert.strictEqual(err.code, null);
      assert.match(describeError(err), /Not found \(404\)/);
    });
  });

  it('reports a network failure', async () => {
    const mock = new MockFetch().otherwise(() => ({ networkError: Object.assign(new TypeError('fetch failed'), { cause: new Error('ECONNREFUSED') }) }));
    await rejectsWith(client(mock).getEntry('bbx_1'), 'network', (err) => {
      assert.match(err.message, /ECONNREFUSED/);
      assert.match(describeError(err), /Cannot reach the Armada server/);
    });
  });

  it('reports a timeout', async () => {
    const mock = new MockFetch().otherwise(() => ({ hang: true }));
    await rejectsWith(client(mock, { timeoutMs: 20 }).getEntry('bbx_1'), 'timeout');
  });

  it('reports invalid JSON and a non-enumeration body', async () => {
    const bad = new MockFetch().otherwise(() => ({ text: '<html>' }));
    await rejectsWith(client(bad).getEntry('bbx_1'), 'invalidResponse');
    const shape = new MockFetch().otherwise(() => ({ json: { Id: 'x' } }));
    await rejectsWith(client(shape).enumerateEntries(), 'invalidResponse');
    const empty = new MockFetch().otherwise(() => ({ text: '' }));
    await rejectsWith(client(empty).getEntry('bbx_1'), 'invalidResponse');
  });

  it('maps statuses and describes non-API errors', () => {
    assert.strictEqual(kindForStatus(422), 'badRequest');
    assert.strictEqual(kindForStatus(502), 'server');
    assert.strictEqual(describeError(new Error('boom')), 'boom');
    assert.strictEqual(describeError('plain'), 'plain');
    assert.match(describeError(new ArmadaApiError('notConfigured', 'x')), /Armada: Connect/);
  });
});

describe('Ask Armada helpers and events', () => {
  it('uses the canonical prompt', () => {
    assert.strictEqual(
      askArmadaPrompt('msn_abc'),
      'Show me the pertinent details from the black box for mission msn_abc and give me your opinion on why it failed and what could have been done differently',
    );
  });

  it('builds the Ask URL without a prefill parameter while the dashboard has none', () => {
    assert.strictEqual(ASK_PREFILL_QUERY_PARAM, null);
    assert.deepStrictEqual(dashboardAskUrl('http://h:7890/dashboard/', 'p'), { url: 'http://h:7890/dashboard/ask', prefilled: false });
  });

  it('lists the black box WebSocket events', () => {
    assert.deepStrictEqual([...BLACKBOX_EVENT_TYPES].sort(), ['blackbox.analyzed', 'blackbox.captured', 'blackbox.deleted', 'blackbox.recovery_run', 'blackbox.resolved']);
  });
});
