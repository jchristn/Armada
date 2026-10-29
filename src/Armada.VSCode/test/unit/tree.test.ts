import * as assert from 'assert';
import { describe, it } from 'node:test';
import { EntryNode, FailureNode, FailuresTreeProvider, MessageNode, VesselNode } from '../../src/tree/failuresTreeProvider';
import { MockFetch, page } from '../fakes/fetch';
import { summary, VESSEL_A } from '../fakes/fixtures';
import { makeStore, standardMock } from '../fakes/storeHelpers';
import { MarkdownString, ThemeIcon, TreeItemCollapsibleState } from '../fakes/vscode';

const NOW = () => new Date('2026-10-28T13:00:00Z');

function messages(nodes: FailureNode[]): MessageNode[] {
  return nodes.filter((n): n is MessageNode => n.kind === 'message');
}

describe('FailuresTreeProvider', () => {
  it('shows a connect row when disconnected, with no badge', async () => {
    const store = makeStore(undefined);
    await store.refresh();
    const tree = new FailuresTreeProvider(store, NOW);
    const roots = tree.getChildren();
    assert.strictEqual(roots.length, 1);
    const row = roots[0] as MessageNode;
    assert.strictEqual(row.command, 'armada.connect');
    assert.strictEqual(tree.getTreeItem(row).command?.command, 'armada.connect');
    assert.strictEqual(tree.badge(), undefined);
  });

  it('shows the error with a refresh action when the server fails', async () => {
    const store = makeStore(new MockFetch().otherwise(() => ({ status: 403, json: { Error: 'Forbidden' } })));
    await store.refresh();
    const tree = new FailuresTreeProvider(store, NOW);
    const [row] = messages(tree.getChildren());
    assert.match(row.text, /403/);
    assert.strictEqual(row.command, 'armada.refreshFailures');
  });

  it('groups entries by vessel with kind, step and age, and badges the open count', async () => {
    const store = makeStore(standardMock());
    await store.refresh();
    const tree = new FailuresTreeProvider(store, NOW);
    const roots = tree.getChildren();
    const vessels = roots.filter((n): n is VesselNode => n.kind === 'vessel');
    assert.strictEqual(vessels.length, 1);
    assert.strictEqual(vessels[0].vesselName, 'alpha');
    const vesselItem = tree.getTreeItem(vessels[0]);
    assert.strictEqual(vesselItem.collapsibleState, TreeItemCollapsibleState.Expanded);
    assert.strictEqual(vesselItem.description, '2 open');

    const children = tree.getChildren(vessels[0]) as EntryNode[];
    assert.deepStrictEqual(children.map((c) => c.entry.Id), ['bbx_1', 'bbx_2']);
    const item = tree.getTreeItem(children[0]);
    assert.strictEqual(item.label, 'Add login (msn_x)');
    assert.strictEqual(item.description, 'LandingConflict, LocalMerge, 3h ago');
    assert.strictEqual(item.contextValue, 'armadaEntryOpen');
    assert.strictEqual(item.command?.command, 'armada.openEntry');
    assert.strictEqual((item.iconPath as unknown as ThemeIcon).id, 'error');
    const tip = (item.tooltip as unknown as MarkdownString).value;
    assert.match(tip, /Entry: `bbx_1`/);
    assert.match(tip, /Captured: 2026-10-28T10:00:00Z/);
    assert.match(tip, /\*\*Add login \(msn\\_x\)\*\*/);

    assert.deepStrictEqual(tree.badge(), { value: 12, tooltip: '12 open Armada black box entries' });
    assert.strictEqual(tree.viewDescription(), 'alpha');
    assert.deepStrictEqual(tree.getChildren(children[0]), []);
  });

  it('marks resolved entries and falls back to the kind when no failure kind is recorded', async () => {
    const mock = new MockFetch()
      .on('GET', '/api/v1/vessels', { json: page([VESSEL_A]) })
      .on('POST', '/api/v1/blackbox/enumerate', { json: page([summary({ Id: 'r', Resolution: 'Resolved', FailureKind: null, Phase: 'Execution', Step: undefined, MissionTitle: null })]) });
    const store = makeStore(mock, { settings: { includeResolved: true, decorationsEnabled: false } });
    await store.refresh();
    const tree = new FailuresTreeProvider(store, NOW);
    const vessel = tree.getChildren().find((n): n is VesselNode => n.kind === 'vessel');
    assert.ok(vessel);
    const item = tree.getTreeItem(tree.getChildren(vessel)[0]);
    assert.strictEqual(item.label, 'msn_x');
    assert.strictEqual(item.description, 'Execution, 3h ago');
    assert.strictEqual(item.contextValue, 'armadaEntryResolved');
    assert.strictEqual(tree.badge(), undefined);
  });

  it('says when no vessel matches the workspace and shows every vessel', async () => {
    const store = makeStore(standardMock(), { remotes: {} });
    await store.refresh();
    const tree = new FailuresTreeProvider(store, NOW);
    const roots = tree.getChildren();
    assert.match(messages(roots)[0].text, /No vessel matches this workspace/);
    assert.strictEqual(roots.filter((n) => n.kind === 'vessel').length, 2);
    assert.strictEqual(tree.viewDescription(), 'all vessels');
  });

  it('shows an empty state when there are no open failures', async () => {
    const mock = new MockFetch()
      .on('GET', '/api/v1/vessels', { json: page([VESSEL_A]) })
      .on('POST', '/api/v1/blackbox/enumerate', { json: page([]) });
    const store = makeStore(mock);
    await store.refresh();
    const tree = new FailuresTreeProvider(store, NOW);
    assert.deepStrictEqual(messages(tree.getChildren()).map((m) => m.text), ['No open failures for this repository.']);
  });

  it('fires onDidChangeTreeData when the store changes', async () => {
    const store = makeStore(undefined);
    const tree = new FailuresTreeProvider(store, NOW);
    let fired = 0;
    tree.onDidChangeTreeData(() => fired++);
    await store.refresh();
    assert.ok(fired >= 1);
    tree.dispose();
  });
});
