import * as assert from 'assert';
import * as path from 'path';
import { beforeEach, describe, it } from 'node:test';
import * as vscode from 'vscode';
import { readSettings } from '../../src/config';
import {
  API_KEY_SECRET,
  CONNECTED_CONTEXT_KEY,
  ConnectionManager,
  GLOBAL_ADMIN_CONTEXT_KEY,
  MANUAL_DISCONNECT_KEY,
  STATE_CONTEXT_KEY,
  TENANT_ADMIN_CONTEXT_KEY,
} from '../../src/connection/connectionManager';
import { checkContract, SUPPORTED_CONTRACT_MAJOR } from '../../src/connection/contract';
import { armadaDataDirectory, discoverLocalAdmiral, isLocalAdmiral, isLoopbackUrl, LocalAdmiral, LocalAdmiralMissing } from '../../src/connection/localDiscovery';
import { MockFetch } from '../fakes/fetch';
import { fake, FakeMemento, FakeSecretStorage } from '../fakes/vscode';

const LOCAL: LocalAdmiral = { settingsPath: '/home/dev/.armada/settings.json', port: 7890, apiKey: 'ak_local', baseUrl: 'http://127.0.0.1:7890' };
const MISSING: LocalAdmiralMissing = { settingsPath: '/home/dev/.armada/settings.json', reason: 'No Armada settings at /home/dev/.armada/settings.json.' };

const HEALTH = { Status: 'healthy', Version: '1.3.0', ContractVersion: '1.0', Ports: { Admiral: 7890, Mcp: 7891 } };

function whoami(isAdmin: boolean, isTenantAdmin: boolean): Record<string, unknown> {
  return {
    Tenant: { Id: 'ten_1', Name: 'Default', Active: true },
    User: { Id: 'usr_1', TenantId: 'ten_1', Email: 'dev@example.com', IsAdmin: isAdmin, IsTenantAdmin: isTenantAdmin },
  };
}

function admiral(options: { health?: unknown; healthStatus?: number; whoami?: unknown; whoamiStatus?: number; down?: boolean } = {}): MockFetch {
  const mock = new MockFetch().install();
  if (options.down) return mock.otherwise(() => ({ networkError: new TypeError('fetch failed') }));
  return mock
    .on('GET', '/api/v1/status/health', { status: options.healthStatus ?? 200, json: options.health ?? HEALTH })
    .on('GET', '/api/v1/whoami', (req) => {
      if (options.whoamiStatus) return { status: options.whoamiStatus, json: { Error: 'NotAuthorized' } };
      const authorized = req.headers['X-Api-Key'] === 'ak_local' || req.headers.Authorization === 'Bearer tok-remote' || req.headers.Authorization === 'Bearer tok-new';
      return authorized ? { json: options.whoami ?? whoami(true, false) } : { status: 401, json: { Error: 'NotAuthorized' } };
    });
}

function manager(discovery: LocalAdmiral | LocalAdmiralMissing = LOCAL, secrets = new FakeSecretStorage(), memento = new FakeMemento()): { conn: ConnectionManager; secrets: FakeSecretStorage; memento: FakeMemento; logs: string[] } {
  const logs: string[] = [];
  const log = { info: (l: string) => logs.push(l), warn: (l: string) => logs.push(l), error: (l: string) => logs.push(l) };
  const conn = new ConnectionManager({
    secrets: secrets as unknown as vscode.SecretStorage,
    globalState: memento as unknown as vscode.Memento,
    log,
    discover: () => discovery,
  });
  return { conn, secrets, memento, logs };
}

beforeEach(() => fake.reset());

describe('local discovery', () => {
  it('reads the port and API key from <data dir>/settings.json, honoring ARMADA_DATA_DIR', () => {
    const files: Record<string, string> = {
      [path.join('/data', 'settings.json')]: JSON.stringify({ admiralPort: 17890, apiKey: ' ak_x ', rest: { hostname: 'localhost' } }),
    };
    const found = discoverLocalAdmiral({ env: { ARMADA_DATA_DIR: '/data' }, readFile: (f) => files[f] ?? (() => { throw new Error('ENOENT'); })() });
    assert.ok(isLocalAdmiral(found));
    assert.deepStrictEqual(found, { settingsPath: path.join('/data', 'settings.json'), port: 17890, apiKey: 'ak_x', baseUrl: 'http://127.0.0.1:17890' });
    assert.strictEqual(armadaDataDirectory({ env: {}, homeDir: '/home/dev' }), path.join('/home/dev', '.armada'));
  });

  it('defaults the port, reads PascalCase keys, and reports a missing or broken file', () => {
    const read = (text: string) => discoverLocalAdmiral({ env: {}, homeDir: '/h', readFile: () => text });
    assert.deepStrictEqual(read('{"ApiKey":"k"}'), { settingsPath: path.join('/h', '.armada', 'settings.json'), port: 7890, apiKey: 'k', baseUrl: 'http://127.0.0.1:7890' });
    assert.strictEqual((read('{"admiralPort":99999}') as LocalAdmiral).port, 7890);
    assert.strictEqual((read('{}') as LocalAdmiral).apiKey, null);
    assert.match((read('not json') as LocalAdmiralMissing).reason, /not valid JSON/);
    const missing = discoverLocalAdmiral({ env: {}, homeDir: '/h', readFile: () => { throw new Error('ENOENT'); } });
    assert.ok(!isLocalAdmiral(missing));
    assert.match(missing.reason, /No Armada settings at/);
  });

  it('recognizes loopback URLs on a port', () => {
    assert.ok(isLoopbackUrl('http://localhost:7890', 7890));
    assert.ok(isLoopbackUrl('http://127.0.0.1:7890/', 7890));
    assert.ok(isLoopbackUrl('http://[::1]:7890', 7890));
    assert.ok(!isLoopbackUrl('http://localhost:7891', 7890));
    assert.ok(!isLoopbackUrl('http://10.0.0.5:7890', 7890));
    assert.ok(!isLoopbackUrl('nonsense'));
  });
});

describe('contract check', () => {
  it('accepts the supported major and explains either side being too old', () => {
    assert.strictEqual(SUPPORTED_CONTRACT_MAJOR, 1);
    assert.strictEqual(checkContract('1.0').compatible, true);
    assert.strictEqual(checkContract('1.7').compatible, true);
    const older = checkContract('0.9', '0.9.0');
    assert.deepStrictEqual([older.compatible, older.problem, older.summary], [false, 'serverTooOld', 'server too old']);
    assert.match(older.reason, /Admiral 0\.9\.0 is too old/);
    const newer = checkContract('2.0');
    assert.deepStrictEqual([newer.compatible, newer.problem, newer.summary], [false, 'extensionTooOld', 'extension too old']);
    assert.match(newer.reason, /Update the Armada extension/);
    assert.strictEqual(checkContract(undefined).problem, 'serverTooOld');
    assert.strictEqual(checkContract('abc').problem, 'unknown');
  });
});

describe('ConnectionManager', () => {
  it('connects to the local Admiral with no prompt: health, contract, whoami as X-Api-Key, role keys', async () => {
    const mock = admiral();
    const { conn } = manager();
    assert.strictEqual(await conn.connect(), true);
    assert.strictEqual(fake.messages.length, 0, 'no prompt and no message');
    assert.deepStrictEqual(mock.requests.map((r) => r.path), ['/api/v1/status/health', '/api/v1/whoami']);
    assert.strictEqual(mock.requests[1].headers['X-Api-Key'], 'ak_local');
    assert.strictEqual(mock.requests[1].url, 'http://127.0.0.1:7890/api/v1/whoami');
    assert.strictEqual(conn.state.phase, 'connected');
    assert.deepStrictEqual(conn.state.target, { kind: 'local', baseUrl: 'http://127.0.0.1:7890', label: 'local', source: LOCAL.settingsPath });
    assert.strictEqual(conn.state.contractVersion, '1.0');
    assert.strictEqual(conn.state.identity?.email, 'dev@example.com');
    const keys = fake.contextKeys();
    assert.strictEqual(keys.get(CONNECTED_CONTEXT_KEY), true);
    assert.strictEqual(keys.get(STATE_CONTEXT_KEY), 'connected');
    assert.strictEqual(keys.get(GLOBAL_ADMIN_CONTEXT_KEY), true);
    assert.strictEqual(keys.get(TENANT_ADMIN_CONTEXT_KEY), true, 'a global admin passes tenant admin checks, as on the server');
    assert.strictEqual(conn.active?.socketToken, 'ak_local');
    assert.strictEqual(conn.active?.dashboardUrl, 'http://127.0.0.1:7890/dashboard');
  });

  it('sets only the tenant admin key for a tenant admin, and neither for a user', async () => {
    admiral({ whoami: whoami(false, true) });
    const tenantAdmin = manager();
    await tenantAdmin.conn.connect();
    assert.deepStrictEqual([fake.contextKeys().get(GLOBAL_ADMIN_CONTEXT_KEY), fake.contextKeys().get(TENANT_ADMIN_CONTEXT_KEY)], [false, true]);
    fake.reset();
    admiral({ whoami: whoami(false, false) });
    const user = manager();
    await user.conn.connect();
    assert.deepStrictEqual([fake.contextKeys().get(GLOBAL_ADMIN_CONTEXT_KEY), fake.contextKeys().get(TENANT_ADMIN_CONTEXT_KEY)], [false, false]);
  });

  it('shares one in-flight attempt between concurrent callers', async () => {
    const mock = admiral();
    const { conn } = manager();
    const results = await Promise.all([conn.connect(), conn.connect(), conn.connect()]);
    assert.deepStrictEqual(results, [true, true, true]);
    assert.strictEqual(mock.calls('GET', '/api/v1/status/health').length, 1);
    assert.strictEqual(mock.calls('GET', '/api/v1/whoami').length, 1);
  });

  it('refuses a server that is too old or too new, before sending the credential', async () => {
    const mock = admiral({ health: { ...HEALTH, ContractVersion: undefined, Version: '1.2.0' } });
    const { conn } = manager();
    assert.strictEqual(await conn.connect(), false);
    assert.strictEqual(conn.state.phase, 'failed');
    assert.strictEqual(conn.state.failure?.kind, 'incompatible');
    assert.strictEqual(conn.state.failure?.summary, 'server too old');
    assert.strictEqual(mock.calls('GET', '/api/v1/whoami').length, 0);
    fake.reset();
    admiral({ health: { ...HEALTH, ContractVersion: '2.0' } });
    const newer = manager();
    await newer.conn.connect({ interactive: true });
    assert.strictEqual(newer.conn.state.failure?.summary, 'extension too old');
    assert.ok(fake.messages.some((m) => m.level === 'error' && /extension is too old/.test(m.text)));
    assert.strictEqual(fake.contextKeys().get(CONNECTED_CONTEXT_KEY), false);
  });

  it('explains an unreachable Admiral and a missing local install, with follow-up actions when interactive', async () => {
    admiral({ down: true });
    const down = manager();
    assert.strictEqual(await down.conn.connect({ interactive: true }), false);
    assert.strictEqual(down.conn.state.failure?.kind, 'unreachable');
    const message = fake.messages.find((m) => m.level === 'error');
    assert.match(message?.text ?? '', /Cannot reach the Armada server/);
    assert.deepStrictEqual(message?.items, ['Retry', 'Connect to a Remote Admiral', 'Show Output']);
    fake.reset();
    const none = manager(MISSING);
    assert.strictEqual(await none.conn.connect(), false);
    assert.strictEqual(none.conn.state.failure?.kind, 'noLocalAdmiral');
    assert.strictEqual(fake.messages.length, 0, 'a background attempt only logs');
    assert.ok(none.logs.some((l) => /No Armada settings/.test(l)));
  });

  it('reports a local Admiral that has not written its API key yet, and a rejected key', async () => {
    admiral();
    const noKey = manager({ ...LOCAL, apiKey: null });
    assert.strictEqual(await noKey.conn.connect(), false);
    assert.strictEqual(noKey.conn.state.failure?.kind, 'noCredential');
    admiral();
    const wrong = manager({ ...LOCAL, apiKey: 'ak_wrong' });
    assert.strictEqual(await wrong.conn.connect(), false);
    assert.strictEqual(wrong.conn.state.failure?.kind, 'unauthorized');
    assert.strictEqual(wrong.conn.state.failure?.summary, 'credential rejected');
  });

  it('uses armada.url with the key from secret storage, sent as a bearer token', async () => {
    fake.config.set('armada.url', 'https://armada.example.com/dashboard');
    const mock = admiral();
    const secrets = new FakeSecretStorage();
    secrets.values.set(API_KEY_SECRET, 'tok-remote');
    const { conn } = manager(LOCAL, secrets);
    assert.strictEqual(await conn.connect(), true);
    assert.strictEqual(mock.requests[1].url, 'https://armada.example.com/api/v1/whoami');
    assert.strictEqual(mock.requests[1].headers.Authorization, 'Bearer tok-remote');
    assert.deepStrictEqual(conn.state.target, { kind: 'remote', baseUrl: 'https://armada.example.com', label: 'armada.example.com', source: 'armada.url' });
  });

  it('uses the local key for an armada.url that is the local Admiral, and fails a remote URL with no key', async () => {
    fake.config.set('armada.url', 'http://localhost:7890');
    const mock = admiral();
    const local = manager();
    assert.strictEqual(await local.conn.connect(), true);
    assert.strictEqual(mock.requests[1].headers['X-Api-Key'], 'ak_local');
    assert.strictEqual(local.conn.state.target?.kind, 'local');
    fake.config.set('armada.url', 'https://armada.example.com');
    admiral();
    const remote = manager();
    assert.strictEqual(await remote.conn.connect(), false);
    assert.strictEqual(remote.conn.state.failure?.kind, 'noCredential');
  });

  it('prompts for a remote URL and key, verifies them, then stores the key and the URL', async () => {
    const mock = admiral();
    const { conn, secrets } = manager();
    fake.inputBox.push('https://armada.example.com/dashboard', 'tok-new');
    assert.strictEqual(await conn.connectRemote(), true);
    assert.strictEqual(secrets.values.get(API_KEY_SECRET), 'tok-new');
    assert.deepStrictEqual(fake.configUpdates, [{ key: 'armada.url', value: 'https://armada.example.com' }]);
    assert.strictEqual(mock.calls('GET', '/api/v1/whoami')[0].headers.Authorization, 'Bearer tok-new');
    assert.ok(fake.messages.some((m) => m.level === 'info' && /connected to https:\/\/armada\.example\.com/.test(m.text)));
  });

  it('does not store a key the Admiral rejects, and stops when a prompt is cancelled', async () => {
    admiral();
    const { conn, secrets } = manager();
    fake.inputBox.push('https://armada.example.com', 'bad');
    assert.strictEqual(await conn.connectRemote(), false);
    assert.strictEqual(secrets.values.size, 0);
    assert.deepStrictEqual(fake.configUpdates, []);
    assert.ok(fake.messages.some((m) => m.level === 'error' && /rejected the credential/.test(m.text)));
    fake.reset();
    fake.inputBox.push(undefined);
    assert.strictEqual(await conn.connectRemote(), false);
    fake.inputBox.push('https://a', '');
    assert.strictEqual(await conn.connectRemote(), false);
    assert.ok(fake.messages.some((m) => /is required/.test(m.text)));
  });

  it('disconnects, removes a stored key, and does not reconnect at startup until Connect', async () => {
    const mock = admiral();
    const secrets = new FakeSecretStorage();
    secrets.values.set(API_KEY_SECRET, 'old');
    const { conn, memento } = manager(LOCAL, secrets);
    await conn.connect();
    await conn.disconnect();
    assert.strictEqual(secrets.values.size, 0);
    assert.strictEqual(memento.get(MANUAL_DISCONNECT_KEY), true);
    assert.strictEqual(conn.state.phase, 'disconnected');
    assert.strictEqual(fake.contextKeys().get(CONNECTED_CONTEXT_KEY), false);
    assert.ok(fake.messages.some((m) => /API key was removed/.test(m.text)));
    const before = mock.requests.length;
    assert.strictEqual(await conn.restore(), false);
    assert.strictEqual(mock.requests.length, before, 'restore skips the network after a manual disconnect');
    assert.strictEqual(await conn.connect({ interactive: true }), true);
    assert.strictEqual(memento.get(MANUAL_DISCONNECT_KEY), undefined);
  });

  it('reconnects only when the connection settings point somewhere else', async () => {
    const mock = admiral();
    const { conn } = manager();
    await conn.connect();
    await conn.onSettingsChanged();
    assert.strictEqual(mock.calls('GET', '/api/v1/status/health').length, 1);
    fake.config.set('armada.url', 'https://armada.example.com');
    await conn.onSettingsChanged();
    assert.strictEqual(mock.calls('GET', '/api/v1/status/health').length, 2);
  });

  it('throws NotConnectedError from requireActive while disconnected', () => {
    const { conn } = manager();
    assert.throws(() => conn.requireActive(), /not connected/);
    assert.strictEqual(conn.dashboardUrl(), 'http://127.0.0.1:7890/dashboard', 'the configured target still has a dashboard');
    assert.strictEqual(manager(MISSING).conn.dashboardUrl(), '');
  });
});

describe('readSettings', () => {
  it('uses defaults: the local Admiral and no Explorer copy of the failures view', () => {
    const s = readSettings();
    assert.strictEqual(s.url, '');
    assert.strictEqual(s.authHeader, 'Bearer');
    assert.strictEqual(s.dashboardUrl, '');
    assert.strictEqual(s.showFailuresInExplorer, false);
    assert.strictEqual(s.maxEntries, 50);
  });

  it('clamps numbers and rejects unknown enum values', () => {
    fake.config.set('armada.failures.maxEntries', 100000);
    fake.config.set('armada.liveUpdates.pollIntervalSeconds', 1);
    fake.config.set('armada.authHeader', 'Cookie');
    fake.config.set('armada.dashboardUrl', 'https://a/dashboard/');
    fake.config.set('armada.failures.showInExplorer', true);
    const s = readSettings();
    assert.strictEqual(s.maxEntries, 500);
    assert.strictEqual(s.pollIntervalSeconds, 10);
    assert.strictEqual(s.authHeader, 'Bearer');
    assert.strictEqual(s.dashboardUrl, 'https://a/dashboard');
    assert.strictEqual(s.showFailuresInExplorer, true);
  });
});
