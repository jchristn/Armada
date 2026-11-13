import * as assert from 'assert';
import { beforeEach, describe, it } from 'node:test';
import * as vscode from 'vscode';
import { ArmadaBlackBoxClient } from '../../src/api/blackbox';
import { readSettings } from '../../src/config';
import { API_KEY_SECRET, ArmadaConnection, CONNECTED_CONTEXT_KEY } from '../../src/connection';
import { MockFetch, page } from '../fakes/fetch';
import { fake, FakeSecretStorage } from '../fakes/vscode';

function connection(mock: MockFetch, secrets = new FakeSecretStorage()): { conn: ArmadaConnection; secrets: FakeSecretStorage } {
  const conn = new ArmadaConnection(secrets as unknown as vscode.SecretStorage, (options) => new ArmadaBlackBoxClient({ ...options, fetchImpl: mock.fetch }));
  return { conn, secrets };
}

function contextValue(): unknown {
  const calls = fake.executed.filter((e) => e.command === 'setContext' && e.args[0] === CONNECTED_CONTEXT_KEY);
  return calls.length ? calls[calls.length - 1].args[1] : undefined;
}

beforeEach(() => fake.reset());

describe('ArmadaConnection', () => {
  it('connects, verifies the key, stores it in secret storage and saves the URL', async () => {
    const mock = new MockFetch().on('GET', '/api/v1/vessels', { json: page([]) });
    const { conn, secrets } = connection(mock);
    fake.inputBox.push('http://armada.test:7890/dashboard', 'tok-123');
    assert.strictEqual(await conn.connect(), true);
    assert.strictEqual(secrets.values.get(API_KEY_SECRET), 'tok-123');
    assert.deepStrictEqual(fake.configUpdates, [{ key: 'armada.url', value: 'http://armada.test:7890' }]);
    assert.strictEqual(mock.requests[0].headers.Authorization, 'Bearer tok-123');
    assert.strictEqual(contextValue(), true);
    assert.ok(conn.client);
  });

  it('keeps the stored key when the key prompt is left empty', async () => {
    const mock = new MockFetch().on('GET', '/api/v1/vessels', { json: page([]) });
    const secrets = new FakeSecretStorage();
    secrets.values.set(API_KEY_SECRET, 'old-key');
    const { conn } = connection(mock, secrets);
    fake.inputBox.push('http://armada.test:7890', '');
    assert.strictEqual(await conn.connect(), true);
    assert.strictEqual(mock.requests[0].headers.Authorization, 'Bearer old-key');
  });

  it('does not store a key the server rejects (401) and explains why', async () => {
    const mock = new MockFetch().otherwise(() => ({ status: 401, json: { Error: 'NotAuthorized' } }));
    const { conn, secrets } = connection(mock);
    fake.inputBox.push('http://armada.test:7890', 'bad');
    assert.strictEqual(await conn.connect(), false);
    assert.strictEqual(secrets.values.size, 0);
    assert.ok(fake.messages.some((m) => m.level === 'error' && /rejected the API key/.test(m.text)));
    assert.strictEqual(conn.client, undefined);
  });

  it('explains an unreachable server', async () => {
    const mock = new MockFetch().otherwise(() => ({ networkError: new TypeError('fetch failed') }));
    const { conn } = connection(mock);
    fake.inputBox.push('http://nowhere:1', 'k');
    assert.strictEqual(await conn.connect(), false);
    assert.ok(fake.messages.some((m) => /Cannot reach the Armada server/.test(m.text)));
  });

  it('stops when a prompt is cancelled or no key is given', async () => {
    const { conn } = connection(new MockFetch());
    fake.inputBox.push(undefined);
    assert.strictEqual(await conn.connect(), false);
    fake.inputBox.push('http://armada.test:7890', '');
    assert.strictEqual(await conn.connect(), false);
    assert.ok(fake.messages.some((m) => /API key is required/.test(m.text)));
  });

  it('restores from secret storage without prompting, and disconnects', async () => {
    const secrets = new FakeSecretStorage();
    secrets.values.set(API_KEY_SECRET, 'k');
    fake.config.set('armada.url', 'http://armada.test:7890');
    const { conn } = connection(new MockFetch(), secrets);
    assert.strictEqual(await conn.restore(), true);
    assert.ok(conn.requireClient());
    await conn.disconnect();
    assert.strictEqual(secrets.values.size, 0);
    assert.strictEqual(conn.client, undefined);
    assert.strictEqual(contextValue(), false);
    assert.throws(() => conn.requireClient(), /not connected/);
  });

  it('restores to disconnected without a key', async () => {
    const { conn } = connection(new MockFetch());
    assert.strictEqual(await conn.restore(), false);
    assert.strictEqual(contextValue(), false);
  });
});

describe('readSettings', () => {
  beforeEach(() => fake.reset());

  it('uses defaults and derives the dashboard URL', () => {
    const s = readSettings();
    assert.strictEqual(s.url, 'http://localhost:7890');
    assert.strictEqual(s.dashboardUrl, 'http://localhost:7890/dashboard');
    assert.strictEqual(s.authHeader, 'Bearer');
    assert.strictEqual(s.pollIntervalSeconds, 60);
  });

  it('clamps numbers and rejects unknown enum values', () => {
    fake.config.set('armada.liveUpdates.pollIntervalSeconds', 1);
    fake.config.set('armada.failures.maxEntries', 100000);
    fake.config.set('armada.briefAudience', 'maintainer');
    fake.config.set('armada.authHeader', 'X-Api-Key');
    fake.config.set('armada.dashboardUrl', 'https://dash.example.com/dashboard/');
    const s = readSettings();
    assert.strictEqual(s.pollIntervalSeconds, 10);
    assert.strictEqual(s.maxEntries, 500);
    assert.strictEqual(s.authHeader, 'X-Api-Key');
    assert.strictEqual('briefAudience' in s, false, 'the removed briefAudience setting is ignored');
    assert.strictEqual(s.dashboardUrl, 'https://dash.example.com/dashboard');
  });
});
