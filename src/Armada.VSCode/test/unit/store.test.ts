import * as assert from 'assert';
import { describe, it } from 'node:test';
import { MockFetch, page } from '../fakes/fetch';
import { summary, VESSEL_A } from '../fakes/fixtures';
import { makeStore, standardMock } from '../fakes/storeHelpers';

describe('BlackBoxStore', () => {
  it('filters to the workspace vessel, matched by remote URL', async () => {
    const mock = standardMock();
    const store = makeStore(mock);
    let changes = 0;
    store.onDidChange(() => changes++);
    await store.refresh();
    assert.strictEqual(store.status, 'ready');
    assert.strictEqual(store.filtered, true);
    assert.deepStrictEqual(store.matchedVesselIds(), ['vsl_a']);
    assert.deepStrictEqual(store.entries.map((e) => e.Id), ['bbx_1', 'bbx_2']);
    assert.strictEqual(store.openCount, 12, 'open count uses TotalRecords of the Open query');
    const body = mock.calls('POST', '/api/v1/blackbox/enumerate')[0].body as Record<string, unknown>;
    assert.strictEqual(body.VesselId, 'vsl_a');
    assert.strictEqual(body.Resolution, 'Open');
    assert.strictEqual(body.IsCurrent, true);
    assert.ok(changes >= 2, 'loading and ready both notify');
  });

  it('collects conflicted files (newest entry wins, counted) and hotspot directories', async () => {
    const store = makeStore(standardMock());
    await store.refresh();
    const files = store.conflictedFiles.get('/work/alpha');
    assert.ok(files);
    assert.deepStrictEqual(Array.from(files.keys()).sort(), ['src/a.ts', 'src/b.ts']);
    assert.strictEqual(files.get('src/a.ts')?.missionId, 'msn_x');
    assert.strictEqual(files.get('src/a.ts')?.count, 2);
    assert.strictEqual(store.hotspots.get('/work/alpha')?.get('src')?.EntryCount, 3);
  });

  it('shows every vessel when no workspace folder matches, and flags it', async () => {
    const store = makeStore(standardMock(), { remotes: { '/work/alpha': ['https://github.com/someone/else.git'] } });
    await store.refresh();
    assert.strictEqual(store.filtered, false);
    assert.strictEqual(store.unmatchedWorkspace, true);
    assert.deepStrictEqual(store.entries.map((e) => e.Id), ['bbx_3', 'bbx_1', 'bbx_2']);
    assert.strictEqual(store.conflictedFiles.size, 0);
  });

  it('shows every vessel when the filter is off, without the unmatched flag', async () => {
    const store = makeStore(standardMock(), { settings: { filterToWorkspaceVessel: false } });
    await store.refresh();
    assert.strictEqual(store.filtered, false);
    assert.strictEqual(store.unmatchedWorkspace, false);
    assert.strictEqual(store.entries.length, 3);
  });

  it('counts open entries from the list when resolved entries are included', async () => {
    const mock = new MockFetch()
      .on('GET', '/api/v1/vessels', { json: page([VESSEL_A]) })
      .on('POST', '/api/v1/blackbox/enumerate', { json: page([summary({ Id: 'a' }), summary({ Id: 'b', Resolution: 'Resolved' })], 99) });
    const store = makeStore(mock, { settings: { includeResolved: true, decorationsEnabled: false } });
    await store.refresh();
    assert.strictEqual(store.openCount, 1);
    assert.strictEqual((mock.calls('POST', '/api/v1/blackbox/enumerate')[0].body as Record<string, unknown>).Resolution, undefined);
  });

  it('is disconnected without a client', async () => {
    const store = makeStore(undefined);
    await store.refresh();
    assert.strictEqual(store.status, 'disconnected');
    assert.strictEqual(store.entries.length, 0);
  });

  it('reports a clear error and never throws when the server rejects the key', async () => {
    const mock = new MockFetch().otherwise(() => ({ status: 401, json: { Error: 'NotAuthorized' } }));
    const store = makeStore(mock);
    await store.refresh();
    assert.strictEqual(store.status, 'error');
    assert.match(store.error ?? '', /401/);
    assert.strictEqual(store.openCount, 0);
  });

  it('reports 503 store unavailable from the enumerate route', async () => {
    const mock = standardMock().on('POST', '/api/v1/blackbox/enumerate', { status: 503, json: { Error: 'BlackBoxStoreUnavailable', Message: 'disk offline' } });
    const store = makeStore(mock);
    await store.refresh();
    assert.strictEqual(store.status, 'error');
    assert.match(store.error ?? '', /store is unavailable.*disk offline/);
  });

  it('ignores a missing Conflict section (410) and a missing hotspots route (404)', async () => {
    const mock = standardMock()
      .on('GET', '/api/v1/blackbox/bbx_1/sections/Conflict', { status: 410, json: { Error: 'NotFound', Data: { Code: 'BlackBoxObjectMissing' } } })
      .on('POST', '/api/v1/blackbox/hotspots/enumerate', { status: 404, text: 'Not Found' });
    const store = makeStore(mock);
    await store.refresh();
    assert.strictEqual(store.status, 'ready');
    assert.deepStrictEqual(Array.from(store.conflictedFiles.get('/work/alpha')?.keys() ?? []), ['src/a.ts']);
    assert.strictEqual(store.hotspots.get('/work/alpha')?.size, 0);
  });

  it('skips decorations when disabled', async () => {
    const mock = standardMock();
    const store = makeStore(mock, { settings: { decorationsEnabled: false } });
    await store.refresh();
    assert.strictEqual(mock.calls('GET', '/api/v1/blackbox/bbx_1/sections/Conflict').length, 0);
    assert.strictEqual(mock.calls('POST', '/api/v1/blackbox/hotspots/enumerate').length, 0);
  });

  it('loads vessels once until invalidated', async () => {
    const mock = standardMock();
    const store = makeStore(mock);
    await store.refresh();
    await store.refresh();
    assert.strictEqual(mock.calls('GET', '/api/v1/vessels').length, 1);
    store.invalidateVessels();
    await store.refresh();
    assert.strictEqual(mock.calls('GET', '/api/v1/vessels').length, 2);
  });

  it('keeps notifying other listeners when one throws', async () => {
    const store = makeStore(undefined);
    let called = false;
    store.onDidChange(() => {
      throw new Error('bad listener');
    });
    store.onDidChange(() => {
      called = true;
    });
    await store.refresh();
    assert.ok(called);
  });
});
