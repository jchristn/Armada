import * as assert from 'assert';
import * as path from 'path';
import { beforeEach, describe, it } from 'node:test';
import * as vscode from 'vscode';
import { readSettings } from '../../src/config';
import {
  CONNECTED_CONTEXT_KEY,
  ConnectionManager,
  credentialSecretKey,
  ENDPOINT_KEY,
  GLOBAL_ADMIN_CONTEXT_KEY,
  MANUAL_DISCONNECT_KEY,
  STATE_CONTEXT_KEY,
  TENANT_ADMIN_CONTEXT_KEY,
} from '../../src/connection/connectionManager';
import { checkContract, SUPPORTED_CONTRACT_MAJOR } from '../../src/connection/contract';
import {
  AdmiralEndpoint,
  canonicalBaseUrl,
  discoverEndpoints,
  discoverHarbor,
  discoverTuiProfiles,
  harborDirectory,
  restBaseFromLinkUrl,
  tuiCredentialKey,
} from '../../src/connection/endpoints';
import { armadaDataDirectory, discoverLocalAdmiral, isLocalAdmiral, isLoopbackUrl, LocalAdmiral, LocalAdmiralMissing } from '../../src/connection/localDiscovery';
import { MockFetch } from '../fakes/fetch';
import { fake, FakeMemento, FakeSecretStorage } from '../fakes/vscode';

const LOCAL: AdmiralEndpoint = {
  baseUrl: 'http://127.0.0.1:7890',
  kind: 'local',
  label: 'Local Admiral',
  sources: ['local'],
  detail: '~/.armada/settings.json',
  credentials: [{ secret: 'ak_local', header: 'X-Api-Key', origin: 'local Admiral API key' }],
};

const HARBOR: AdmiralEndpoint = {
  baseUrl: 'https://armada.example.com',
  kind: 'remote',
  label: "Harbor's Admiral (armada.example.com)",
  sources: ['harbor'],
  detail: 'Harbor (running), ~/.armada-harbor/settings.json',
  credentials: [
    { secret: 'tok-remote', header: 'Bearer', origin: 'Harbor access key' },
    { secret: 'tok-remote', header: 'X-Api-Key', origin: 'Harbor access key' },
  ],
  harborRunning: true,
  dashboardUrl: 'https://dash.example.com/dashboard/',
};

const HEALTH = { Status: 'healthy', Version: '1.3.0', ContractVersion: '1.0', Ports: { Admiral: 7890, Mcp: 7891 } };

function whoami(isAdmin: boolean, isTenantAdmin: boolean): Record<string, unknown> {
  return {
    Tenant: { Id: 'ten_1', Name: 'Default', Active: true },
    User: { Id: 'usr_1', TenantId: 'ten_1', Email: 'dev@example.com', IsAdmin: isAdmin, IsTenantAdmin: isTenantAdmin },
  };
}

function admiral(options: { health?: unknown; whoami?: unknown; whoamiStatus?: number; down?: boolean; downHosts?: string[] } = {}): MockFetch {
  const mock = new MockFetch().install();
  if (options.down) return mock.otherwise(() => ({ networkError: new TypeError('fetch failed') }));
  const isDown = (url: string) => (options.downHosts ?? []).some((h) => new URL(url).host === h);
  return mock
    .on('GET', '/api/v1/status/health', (req) => (isDown(req.url) ? { networkError: new TypeError('fetch failed') } : { json: options.health ?? HEALTH }))
    .on('GET', '/api/v1/whoami', (req) => {
      if (options.whoamiStatus) return { status: options.whoamiStatus, json: { Error: 'NotAuthorized' } };
      const authorized = req.headers['X-Api-Key'] === 'ak_local' || req.headers.Authorization === 'Bearer tok-remote' || req.headers['X-Api-Key'] === 'ak_settings';
      return authorized ? { json: options.whoami ?? whoami(true, false) } : { status: 401, json: { Error: 'NotAuthorized' } };
    });
}

function manager(endpoints: AdmiralEndpoint[] = [LOCAL], secrets = new FakeSecretStorage(), memento = new FakeMemento()): { conn: ConnectionManager; secrets: FakeSecretStorage; memento: FakeMemento; logs: string[] } {
  const logs: string[] = [];
  const log = { info: (l: string) => logs.push(l), warn: (l: string) => logs.push(l), error: (l: string) => logs.push(l) };
  const conn = new ConnectionManager({
    secrets: secrets as unknown as vscode.SecretStorage,
    globalState: memento as unknown as vscode.Memento,
    log,
    discover: async () => endpoints.map((e) => ({ ...e, credentials: [...e.credentials] })),
    probe: async (baseUrl) => baseUrl !== 'http://down.example.com',
  });
  return { conn, secrets, memento, logs };
}

/** Picks the quick pick item whose label contains text. */
function pickLabel(text: string): (items: Array<{ label: string }>) => unknown {
  return (items) => items.find((i) => i.label.includes(text));
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

describe('endpoint discovery', () => {
  const home = '/home/dev';
  function files(map: Record<string, unknown>): (f: string) => string {
    return (f) => {
      if (!(f in map)) throw new Error('ENOENT');
      const v = map[f];
      return typeof v === 'string' ? v : JSON.stringify(v);
    };
  }
  const harborSettings = path.join(home, '.armada-harbor', 'settings.json');
  const armadaSettings = path.join(home, '.armada', 'settings.json');
  const tui = path.join(home, '.armada', 'tui.json');
  const tuiCreds = path.join(home, '.armada', 'tui-credentials.json');

  it('maps a Harbor link URL to the Admiral REST base, as LocalAdmiralInfo.RestBaseUrl does', () => {
    assert.strictEqual(restBaseFromLinkUrl('ws://view.example.org:8710/v1.0/harbor/connect'), 'http://view.example.org:8710');
    assert.strictEqual(restBaseFromLinkUrl('wss://armada.example.com/v1.0/harbor/connect'), 'https://armada.example.com');
    assert.strictEqual(restBaseFromLinkUrl('ftp://x'), null);
    assert.strictEqual(restBaseFromLinkUrl('nonsense'), null);
    assert.strictEqual(harborDirectory({ env: { ARMADA_HARBOR_HOME: '/opt/harbor' } }), path.resolve('/opt/harbor'));
    assert.strictEqual(canonicalBaseUrl('http://localhost:7890'), canonicalBaseUrl('http://127.0.0.1:7890/'));
    assert.notStrictEqual(canonicalBaseUrl('http://a:80'), canonicalBaseUrl('https://a'));
  });

  it("reads Harbor's settings and the TUI's profiles", () => {
    const read = files({
      [harborSettings]: { ServerLinkUrl: 'ws://view.example.org:8710/v1.0/harbor/connect', DashboardUrl: 'http://view.example.org:9710/dashboard', AccessKey: 'hk', Secret: '' },
      [tui]: { Profiles: [{ Name: 'default', Url: 'http://127.0.0.1:7890', FollowsLocalAdmiral: true }, { Name: 'prod', Url: 'https://armada.example.com' }, { Name: '' }], ActiveProfile: 'prod' },
    });
    assert.deepStrictEqual(discoverHarbor({ env: {}, homeDir: home, readFile: read }), {
      settingsPath: harborSettings,
      baseUrl: 'http://view.example.org:8710',
      accessKey: 'hk',
      dashboardUrl: 'http://view.example.org:9710/dashboard',
    });
    assert.deepStrictEqual(discoverTuiProfiles({ env: {}, homeDir: home, readFile: read }), [
      { name: 'default', url: 'http://127.0.0.1:7890', active: false, followsLocalAdmiral: true },
      { name: 'prod', url: 'https://armada.example.com', active: true, followsLocalAdmiral: false },
    ]);
    assert.strictEqual(discoverHarbor({ env: {}, homeDir: home, readFile: files({}) }), null);
    assert.deepStrictEqual(discoverTuiProfiles({ env: {}, homeDir: home, readFile: files({ [tui]: 'not json' }) }), []);
  });

  it("orders a running Harbor's Admiral first, merges duplicates, and collects every stored credential", async () => {
    const osSecrets: Record<string, string> = { [tuiCredentialKey({ name: 'prod', url: 'https://armada.example.com', active: true, followsLocalAdmiral: false })]: 'tui-token' };
    const endpoints = await discoverEndpoints({
      env: {},
      homeDir: home,
      readFile: files({
        [harborSettings]: { ServerLinkUrl: 'wss://armada.example.com/v1.0/harbor/connect', DashboardUrl: 'https://dash.example.com/dashboard', AccessKey: 'hk' },
        [armadaSettings]: { admiralPort: 7890, apiKey: 'ak_local' },
        [tui]: { Profiles: [{ Name: 'default', Url: 'http://localhost:7890', FollowsLocalAdmiral: true }, { Name: 'prod', Url: 'https://armada.example.com' }, { Name: 'lab', Url: 'http://10.0.0.5:7890' }], ActiveProfile: 'prod' },
        [tuiCreds]: { 'profile:lab@http://10.0.0.5:7890': 'lab-token' },
      }),
      isHarborRunning: async () => true,
      readOsSecret: async (service, account) => (service === 'armada-tui' ? osSecrets[account] ?? null : null),
    });
    assert.deepStrictEqual(endpoints.map((e) => [e.baseUrl, e.kind, e.sources]), [
      ['https://armada.example.com', 'remote', ['harbor', 'tui']],
      ['http://127.0.0.1:7890', 'local', ['local', 'tui']],
      ['http://10.0.0.5:7890', 'remote', ['tui']],
    ]);
    const harbor = endpoints[0];
    assert.strictEqual(harbor.harborRunning, true);
    assert.strictEqual(harbor.dashboardUrl, 'https://dash.example.com/dashboard');
    assert.match(harbor.detail, /^Harbor \(running\), ~\/\.armada-harbor\/settings\.json; TUI profile "prod" \(active\)/);
    assert.deepStrictEqual(harbor.credentials.map((c) => [c.secret, c.header]), [['hk', 'Bearer'], ['hk', 'X-Api-Key'], ['tui-token', 'Bearer'], ['tui-token', 'X-Api-Key']]);
    assert.deepStrictEqual(endpoints[1].credentials.map((c) => c.secret), ['ak_local']);
    assert.deepStrictEqual(endpoints[2].credentials.map((c) => c.secret), ['lab-token', 'lab-token'], 'the TUI file store is the fallback');
  });

  it('treats a Harbor linked to the local Admiral as the local Admiral, and finds nothing on a bare machine', async () => {
    const endpoints = await discoverEndpoints({
      env: {},
      homeDir: home,
      readFile: files({
        [harborSettings]: { ServerLinkUrl: 'ws://127.0.0.1:7890/v1.0/harbor/connect', AccessKey: '' },
        [armadaSettings]: { admiralPort: 7890, apiKey: 'ak_local' },
      }),
      isHarborRunning: async () => false,
      readOsSecret: async () => null,
    });
    assert.strictEqual(endpoints.length, 1);
    assert.deepStrictEqual([endpoints[0].kind, endpoints[0].label, endpoints[0].sources], ['local', 'Local Admiral', ['harbor', 'local']]);
    assert.deepStrictEqual(endpoints[0].credentials.map((c) => c.secret), ['ak_local']);
    assert.deepStrictEqual(await discoverEndpoints({ env: {}, homeDir: home, readFile: files({}), readOsSecret: async () => null }), []);
  });
});

describe('contract check', () => {
  it('accepts the supported major, accepts an Admiral that predates contract versions, and explains either side being too old', () => {
    assert.strictEqual(SUPPORTED_CONTRACT_MAJOR, 1);
    assert.strictEqual(checkContract('1.0').compatible, true);
    assert.strictEqual(checkContract('1.7').compatible, true);
    const legacy = checkContract(undefined, '1.3.0');
    assert.strictEqual(legacy.compatible, true);
    assert.match(legacy.reason, /1\.3\.0 does not report a contract version/);
    const older = checkContract('0.9', '0.9.0');
    assert.deepStrictEqual([older.compatible, older.problem, older.summary], [false, 'serverTooOld', 'server too old']);
    assert.match(older.reason, /Admiral 0\.9\.0 is too old/);
    const newer = checkContract('2.0');
    assert.deepStrictEqual([newer.compatible, newer.problem, newer.summary], [false, 'extensionTooOld', 'extension too old']);
    assert.match(newer.reason, /Update the Armada extension/);
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
    assert.deepStrictEqual(conn.state.target, { kind: 'local', baseUrl: 'http://127.0.0.1:7890', label: 'local', source: '~/.armada/settings.json' });
    assert.strictEqual(conn.state.contractVersion, '1.0');
    assert.strictEqual(conn.state.credentialOrigin, 'local Admiral API key');
    assert.strictEqual(conn.state.identity?.email, 'dev@example.com');
    const keys = fake.contextKeys();
    assert.strictEqual(keys.get(CONNECTED_CONTEXT_KEY), true);
    assert.strictEqual(keys.get(STATE_CONTEXT_KEY), 'connected');
    assert.strictEqual(keys.get(GLOBAL_ADMIN_CONTEXT_KEY), true);
    assert.strictEqual(keys.get(TENANT_ADMIN_CONTEXT_KEY), true, 'a global admin passes tenant admin checks, as on the server');
    assert.strictEqual(conn.active?.socketToken, 'ak_local');
    assert.strictEqual(conn.active?.dashboardUrl, 'http://127.0.0.1:7890/dashboard');
  });

  it("connects at startup to a running Harbor's remote Admiral with Harbor's access key as a bearer token", async () => {
    const mock = admiral({ health: { ...HEALTH, ContractVersion: undefined } });
    const { conn } = manager([HARBOR, LOCAL]);
    assert.strictEqual(await conn.restore(), true);
    assert.strictEqual(fake.messages.length, 0);
    assert.strictEqual(conn.state.target?.baseUrl, 'https://armada.example.com');
    assert.strictEqual(conn.state.target?.kind, 'remote');
    assert.strictEqual(mock.calls('GET', '/api/v1/whoami')[0].headers.Authorization, 'Bearer tok-remote');
    assert.strictEqual(conn.state.credentialOrigin, 'Harbor access key');
    assert.strictEqual(conn.active?.dashboardUrl, 'https://dash.example.com/dashboard', "Harbor's configured dashboard");
    assert.strictEqual(mock.calls('GET', '/api/v1/status/health').length, 1, 'the local Admiral is not tried once Harbor works');
  });

  it('falls through to the next endpoint, and reports the first failure with what else was tried', async () => {
    admiral({ downHosts: ['armada.example.com'] });
    const fallback = manager([HARBOR, LOCAL]);
    assert.strictEqual(await fallback.conn.connect(), true);
    assert.strictEqual(fallback.conn.state.target?.kind, 'local');
    fake.reset();
    admiral({ down: true });
    const none = manager([HARBOR, LOCAL]);
    assert.strictEqual(await none.conn.connect(), false);
    assert.strictEqual(none.conn.state.failure?.kind, 'unreachable');
    assert.strictEqual(none.conn.state.target?.baseUrl, 'https://armada.example.com');
    assert.match(none.conn.state.failure?.message ?? '', /Also tried http:\/\/127\.0\.0\.1:7890/);
    assert.strictEqual(fake.messages.length, 0, 'a background attempt only logs');
  });

  it('tries every credential in order, and reports a rejected or missing one', async () => {
    const mock = admiral();
    const { conn } = manager([{ ...HARBOR, credentials: [{ secret: 'stale', header: 'Bearer', origin: 'TUI session' }, ...HARBOR.credentials] }]);
    assert.strictEqual(await conn.connect(), true);
    assert.deepStrictEqual(mock.calls('GET', '/api/v1/whoami').map((r) => r.headers.Authorization), ['Bearer stale', 'Bearer tok-remote']);
    admiral();
    const wrong = manager([{ ...LOCAL, credentials: [{ secret: 'ak_wrong', header: 'X-Api-Key', origin: 'local Admiral API key' }] }]);
    assert.strictEqual(await wrong.conn.connect(), false);
    assert.strictEqual(wrong.conn.state.failure?.kind, 'unauthorized');
    assert.strictEqual(wrong.conn.state.failure?.summary, 'credential rejected');
    admiral();
    const noKey = manager([{ ...LOCAL, credentials: [] }]);
    assert.strictEqual(await noKey.conn.connect(), false);
    assert.strictEqual(noKey.conn.state.failure?.kind, 'noCredential');
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
    const mock = admiral({ health: { ...HEALTH, ContractVersion: '0.9', Version: '0.9.0' } });
    const { conn } = manager();
    assert.strictEqual(await conn.connect(), false);
    assert.strictEqual(conn.state.failure?.kind, 'incompatible');
    assert.strictEqual(conn.state.failure?.summary, 'server too old');
    assert.strictEqual(mock.calls('GET', '/api/v1/whoami').length, 0);
    fake.reset();
    admiral({ health: { ...HEALTH, ContractVersion: '2.0' } });
    const newer = manager();
    fake.quickPick.push(pickLabel('Local Admiral'));
    await newer.conn.connect({ interactive: true });
    assert.strictEqual(newer.conn.state.failure?.summary, 'extension too old');
    const message = fake.messages.find((m) => m.level === 'error');
    assert.match(message?.text ?? '', /extension is too old/);
    assert.deepStrictEqual(message?.items, ['Connect...', 'Show Output']);
    assert.strictEqual(fake.contextKeys().get(CONNECTED_CONTEXT_KEY), false);
  });

  it('reports a machine with no Armada settings', async () => {
    admiral();
    const { conn } = manager([]);
    assert.strictEqual(await conn.connect(), false);
    assert.strictEqual(conn.state.failure?.kind, 'noAdmiral');
    assert.match(conn.state.failure?.message ?? '', /Run "Armada: Connect"/);
  });

  it('Connect lists the discovered Admirals with reachability and an "Enter an Admiral URL" item, and remembers the pick', async () => {
    admiral();
    const { conn, memento } = manager([HARBOR, LOCAL, { ...LOCAL, baseUrl: 'http://down.example.com', kind: 'remote', label: 'lab', credentials: [] }]);
    let shown: Array<{ label: string; description?: string; detail?: string }> = [];
    fake.quickPick.push((items: typeof shown) => {
      shown = items;
      return items[0];
    });
    assert.strictEqual(await conn.connect({ interactive: true }), true);
    assert.deepStrictEqual(shown.map((i) => i.label), ["$(remote) Harbor's Admiral (armada.example.com)", '$(home) Local Admiral', '$(remote) lab', '$(edit) Enter an Admiral URL...']);
    assert.match(shown[0].detail ?? '', /Harbor \(running\).*responding.*credential found/);
    assert.match(shown[2].detail ?? '', /not responding.*will ask for a key/);
    assert.strictEqual(memento.get(ENDPOINT_KEY), 'https://armada.example.com');
    assert.ok(fake.messages.some((m) => m.level === 'info' && /connected to https:\/\/armada\.example\.com/.test(m.text)));
  });

  it('startup reconnects to the picked Admiral instead of the first discovered one', async () => {
    const mock = admiral();
    const memento = new FakeMemento();
    await memento.update(ENDPOINT_KEY, 'http://localhost:7890');
    const { conn } = manager([HARBOR, LOCAL], new FakeSecretStorage(), memento);
    assert.strictEqual(await conn.restore(), true);
    assert.strictEqual(conn.state.target?.baseUrl, 'http://127.0.0.1:7890');
    assert.strictEqual(mock.calls('GET', '/api/v1/status/health').length, 1);
  });

  it('asks for a key only when no stored credential works, tries it as Bearer then X-Api-Key, and stores what worked', async () => {
    const mock = admiral();
    const { conn, secrets, memento } = manager([]);
    fake.quickPick.push(pickLabel('Enter an Admiral URL'));
    fake.inputBox.push('https://armada.example.com/dashboard', 'ak_settings');
    assert.strictEqual(await conn.connect({ interactive: true }), true);
    assert.deepStrictEqual(mock.calls('GET', '/api/v1/whoami').map((r) => [r.headers.Authorization, r.headers['X-Api-Key']]), [['Bearer ak_settings', undefined], [undefined, 'ak_settings']]);
    assert.deepStrictEqual(JSON.parse(secrets.values.get(credentialSecretKey('https://armada.example.com')) ?? '{}'), { secret: 'ak_settings', header: 'X-Api-Key' });
    assert.strictEqual(memento.get(ENDPOINT_KEY), 'https://armada.example.com');
    fake.reset();
    const again = admiral();
    const restored = manager([], secrets, memento);
    assert.strictEqual(await restored.conn.restore(), true, 'the stored key is used at startup');
    assert.strictEqual(again.calls('GET', '/api/v1/whoami')[0].headers['X-Api-Key'], 'ak_settings');
  });

  it('does not store a key the Admiral rejects, and stops when the picker or a prompt is cancelled', async () => {
    admiral();
    const { conn, secrets, memento } = manager([{ ...HARBOR, credentials: [] }]);
    fake.quickPick.push(pickLabel("Harbor's Admiral"));
    fake.inputBox.push('bad');
    assert.strictEqual(await conn.connect({ interactive: true }), false);
    assert.strictEqual(secrets.values.size, 0);
    assert.strictEqual(memento.get(ENDPOINT_KEY), undefined);
    assert.ok(fake.messages.some((m) => m.level === 'error' && /rejected/.test(m.text)));
    fake.reset();
    fake.quickPick.push(undefined);
    assert.strictEqual(await conn.connect({ interactive: true }), false);
    fake.quickPick.push(pickLabel("Harbor's Admiral"));
    fake.inputBox.push('');
    assert.strictEqual(await conn.connect({ interactive: true }), false);
    assert.ok(fake.messages.some((m) => /is required/.test(m.text)));
  });

  it('disconnects, removes a key entered for the Admiral, and does not reconnect at startup until Connect', async () => {
    const mock = admiral();
    const secrets = new FakeSecretStorage();
    secrets.values.set(credentialSecretKey(LOCAL.baseUrl), JSON.stringify({ secret: 'old', header: 'Bearer' }));
    const { conn, memento } = manager([LOCAL], secrets);
    await conn.connect();
    await conn.disconnect();
    assert.strictEqual(secrets.values.size, 0);
    assert.strictEqual(memento.get(MANUAL_DISCONNECT_KEY), true);
    assert.strictEqual(conn.state.phase, 'disconnected');
    assert.strictEqual(fake.contextKeys().get(CONNECTED_CONTEXT_KEY), false);
    assert.ok(fake.messages.some((m) => /removed from secret storage/.test(m.text)));
    const before = mock.requests.length;
    assert.strictEqual(await conn.restore(), false);
    assert.strictEqual(mock.requests.length, before, 'restore skips the network after a manual disconnect');
    fake.quickPick.push(pickLabel('Local Admiral'));
    assert.strictEqual(await conn.connect({ interactive: true }), true);
    assert.strictEqual(memento.get(MANUAL_DISCONNECT_KEY), undefined);
  });

  it('explains an unreachable Admiral with Retry, and Retry reconnects to the same Admiral', async () => {
    admiral({ down: true });
    const { conn } = manager();
    fake.quickPick.push(pickLabel('Local Admiral'));
    assert.strictEqual(await conn.connect({ interactive: true }), false);
    const message = fake.messages.find((m) => m.level === 'error');
    assert.match(message?.text ?? '', /Cannot reach the Armada server/);
    assert.deepStrictEqual(message?.items, ['Retry', 'Connect...', 'Show Output']);
    admiral();
    assert.strictEqual(await conn.reconnect(), true);
    assert.strictEqual(conn.state.target?.baseUrl, LOCAL.baseUrl);
  });

  it('throws NotConnectedError from requireActive while disconnected', () => {
    const { conn } = manager();
    assert.throws(() => conn.requireActive(), /not connected/);
    assert.strictEqual(conn.dashboardUrl(), '');
  });
});

describe('readSettings', () => {
  it('uses defaults: no Explorer copy of the failures view and no dashboard override', () => {
    const s = readSettings();
    assert.strictEqual(s.tenant, '');
    assert.strictEqual(s.dashboardUrl, '');
    assert.strictEqual(s.showFailuresInExplorer, false);
    assert.strictEqual(s.maxEntries, 50);
  });

  it('clamps numbers and trims the dashboard URL', () => {
    fake.config.set('armada.failures.maxEntries', 100000);
    fake.config.set('armada.liveUpdates.pollIntervalSeconds', 1);
    fake.config.set('armada.dashboardUrl', 'https://a/dashboard/');
    fake.config.set('armada.failures.showInExplorer', true);
    const s = readSettings();
    assert.strictEqual(s.maxEntries, 500);
    assert.strictEqual(s.pollIntervalSeconds, 10);
    assert.strictEqual(s.dashboardUrl, 'https://a/dashboard');
    assert.strictEqual(s.showFailuresInExplorer, true);
  });
});
