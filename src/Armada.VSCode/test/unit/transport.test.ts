import * as assert from 'assert';
import { describe, it } from 'node:test';
import { enumerateBlackBox } from '@dashboard/api/client';
import { describeError, hasStatus, needsConnection, NotConnectedError } from '../../src/transport/errors';
import {
  credentialHeaders,
  dashboardAskUrl,
  dashboardPageUrl,
  getEntryForAnyId,
  listAllVessels,
  normalizeBaseUrl,
  readHealth,
  useAdmiral,
} from '../../src/transport/hostClient';
import { BASE, MockFetch, page, useMock } from '../fakes/fetch';
import { serverEntry } from '../fakes/fixtures';

async function errorFrom(mock: MockFetch): Promise<unknown> {
  useMock(mock);
  try {
    await enumerateBlackBox({ pageNumber: 1, pageSize: 1 });
  } catch (err) {
    return err;
  }
  throw new Error('expected a failure');
}

describe('host client', () => {
  it('sends a bearer token, or the settings API key as X-Api-Key, with every shared-client request', async () => {
    const mock = new MockFetch().on('POST', '/api/v1/blackbox/enumerate', { json: page([]) });
    useMock(mock, { secret: 'tok-1', header: 'Bearer' });
    await enumerateBlackBox({});
    useMock(mock, { secret: 'ak_local', header: 'X-Api-Key' });
    await enumerateBlackBox({});
    assert.strictEqual(mock.requests[0].headers.Authorization, 'Bearer tok-1');
    assert.strictEqual(mock.requests[0].headers['X-Api-Key'], undefined);
    assert.strictEqual(mock.requests[1].headers['X-Api-Key'], 'ak_local');
    assert.strictEqual(mock.requests[1].headers.Authorization, undefined);
    assert.ok(mock.requests[0].url.startsWith(`${BASE}/api/v1/`));
    assert.deepStrictEqual(credentialHeaders(undefined), {});
  });

  it('points the shared client at a normalized base URL', async () => {
    const mock = new MockFetch().install().on('GET', '/api/v1/status/health', { json: { Status: 'healthy' } });
    useAdmiral('http://armada.test:7890/dashboard/', undefined);
    await readHealth();
    assert.strictEqual(mock.requests[0].url, 'http://armada.test:7890/api/v1/status/health');
    assert.deepStrictEqual(mock.requests[0].headers['X-Api-Key'], undefined);
  });

  it('narrows the health response, including the contract version', async () => {
    const mock = new MockFetch().on('GET', '/api/v1/status/health', { json: { Status: 'healthy', Version: '1.3.0', ContractVersion: '1.0', Ports: { Admiral: 7890, Mcp: 7891 } } });
    useMock(mock);
    assert.deepStrictEqual(await readHealth(), { status: 'healthy', version: '1.3.0', contractVersion: '1.0', admiralPort: 7890, mcpPort: 7891 });
    mock.on('GET', '/api/v1/status/health', { json: { Status: 'healthy' } });
    assert.deepStrictEqual(await readHealth(), { status: 'healthy', version: null, contractVersion: null, admiralPort: null, mcpPort: null });
  });

  it('routes mission, voyage and entry ids to their black box routes', async () => {
    const mock = new MockFetch().otherwise(() => ({ json: serverEntry() }));
    useMock(mock);
    await getEntryForAnyId(' msn_1 ');
    await getEntryForAnyId('vyg_1');
    await getEntryForAnyId('bbx_1');
    assert.deepStrictEqual(mock.requests.map((r) => r.path), ['/api/v1/missions/msn_1/blackbox', '/api/v1/voyages/vyg_1/blackbox', '/api/v1/blackbox/bbx_1']);
    await assert.rejects(getEntryForAnyId('  '), /An id is required/);
  });

  it('lists every vessel page by page with the tenant filter', async () => {
    const mock = new MockFetch().on('GET', '/api/v1/vessels', (req) => {
      const pageNumber = Number(req.query.get('pageNumber'));
      return { json: { ...page([{ Id: `vsl_${pageNumber}`, Name: `v${pageNumber}` }]), PageNumber: pageNumber, TotalPages: 2, TotalRecords: 2 } };
    });
    useMock(mock);
    const vessels = await listAllVessels('ten_1');
    assert.deepStrictEqual(vessels.map((v) => v.id), ['vsl_1', 'vsl_2']);
    assert.strictEqual(mock.requests[0].query.get('tenantId'), 'ten_1');
    assert.strictEqual(mock.requests.length, 2);
  });

  it('builds dashboard and Ask URLs', () => {
    assert.strictEqual(normalizeBaseUrl(' armada.test:7890/dashboard/ '), 'http://armada.test:7890');
    assert.strictEqual(normalizeBaseUrl('https://armada.example.com/'), 'https://armada.example.com');
    assert.strictEqual(dashboardPageUrl('http://a/dashboard/', '/missions?tab=blackbox'), 'http://a/dashboard/missions?tab=blackbox');
    assert.strictEqual(dashboardPageUrl('http://a/dashboard', '/'), 'http://a/dashboard/');
    assert.strictEqual(dashboardAskUrl('http://a/dashboard', 'why & how?'), 'http://a/dashboard/ask?prompt=why%20%26%20how%3F');
  });
});

describe('error descriptions', () => {
  const cases: Array<{ status: number; json: unknown; expect: RegExp }> = [
    { status: 401, json: { Error: 'NotAuthorized' }, expect: /rejected the credential \(401\)/ },
    { status: 403, json: { Error: 'Forbidden', Message: 'admins only' }, expect: /not allowed to do this \(403\) \(admins only\)/ },
    { status: 404, json: { Error: 'NotFound', Message: 'gone', Data: { Code: 'BlackBoxNotFound' } }, expect: /No black box entry was found \(404\) \(gone\)/ },
    { status: 404, json: { Error: 'NotFound' }, expect: /Not found \(404\).*route/ },
    { status: 409, json: { Error: 'Conflict', Message: 'busy' }, expect: /conflicts with the current state \(409\) \(busy\)/ },
    { status: 410, json: { Error: 'NotFound', Data: { Code: 'BlackBoxObjectMissing' } }, expect: /missing from storage \(410 BlackBoxObjectMissing\)/ },
    { status: 400, json: { Error: 'BadRequest', Message: 'bad' }, expect: /rejected the request \(400\) \(bad\)/ },
    { status: 501, json: { Error: 'NotImplemented' }, expect: /does not support that operation \(501/ },
    { status: 503, json: { Error: 'ServiceUnavailable', Data: { Code: 'BlackBoxActionsUnavailable' } }, expect: /cannot run black box recovery actions/ },
    { status: 503, json: { Error: 'ServiceUnavailable', Data: { Code: 'BlackBoxStoreUnavailable' } }, expect: /black box store is unavailable/ },
    { status: 500, json: { Error: 'InternalError', Message: 'boom' }, expect: /server error \(500\) \(boom\)/ },
  ];
  for (const c of cases) {
    it(`explains ${c.status} ${JSON.stringify(c.json)}`, async () => {
      const err = await errorFrom(new MockFetch().otherwise(() => ({ status: c.status, json: c.json })));
      assert.match(describeError(err), c.expect);
      assert.strictEqual(hasStatus(err, c.status), true);
      assert.strictEqual(needsConnection(err), c.status === 401);
    });
  }

  it('explains a network failure, a timeout and a non-JSON error body', async () => {
    const network = await errorFrom(new MockFetch().otherwise(() => ({ networkError: Object.assign(new TypeError('fetch failed'), { cause: new Error('connect ECONNREFUSED 127.0.0.1:7890') }) })));
    assert.match(describeError(network), /Cannot reach the Armada server: fetch failed \(connect ECONNREFUSED 127\.0\.0\.1:7890\)/);
    const plain = await errorFrom(new MockFetch().otherwise(() => ({ status: 502, text: 'Bad Gateway' })));
    assert.strictEqual(describeError(plain), 'Armada server error (502).');
    assert.match(describeError(new NotConnectedError()), /not connected/);
    assert.ok(needsConnection(new NotConnectedError()));
    assert.strictEqual(describeError('text'), 'text');
  });
});
