import * as assert from 'assert';
import * as fs from 'fs';
import * as path from 'path';
import { describe, it } from 'node:test';
import { navModelSections } from '@dashboard/lib/navModel';
import type { AskThread } from '@dashboard/types/models';
import { conversationItem } from '../../src/surfaces/ask/conversationsTree';
import { dashboardHubs, HUB_RESOURCES, LEGACY_TAB_TARGETS, NavSources } from '../../src/surfaces/nav/navLayout';
import { NavHubProvider, NavSource, NavTreeProvider } from '../../src/surfaces/nav/navTree';
import { resourceCatalog } from '../../src/surfaces/resources/resourceCatalog';
import { extendCatalog, resourceCatalogExtras } from '../../src/surfaces/resources/resourceCatalogExtras';
import { EventEmitter, ThemeIcon, TreeItem, TreeItemCollapsibleState } from '../fakes/vscode';

/** A fake source tree: roots, children per root, change events. */
function source(id: string, roots: string[], children: Record<string, string[]> = {}, count?: number): FakeSource {
  const changed = new EventEmitter<unknown>();
  const nodes = new Map<string, { id: string }>();
  const node = (key: string) => {
    if (!nodes.has(key)) nodes.set(key, { id: key });
    return nodes.get(key) as { id: string };
  };
  return {
    id,
    count: () => count,
    fire: () => changed.fire(undefined),
    provider: {
      onDidChangeTreeData: changed.event as never,
      getChildren: (el?: unknown) => (el ? (children[(el as { id: string }).id] ?? []).map(node) : roots.map(node)),
      getTreeItem: (el: unknown) => {
        const key = (el as { id: string }).id;
        const item = new TreeItem(key, children[key] ? TreeItemCollapsibleState.Collapsed : TreeItemCollapsibleState.None);
        item.id = key;
        item.contextValue = 'armadaThing';
        return item as never;
      },
    },
  };
}

type FakeSource = NavSource & { fire(): void };

function sources(): NavSources & { missions: FakeSource; delivery: FakeSource } {
  const s = {
    needsYou: source('armadaNeedsYou', ['review-1', 'stall-1'], {}, 2),
    missions: source('armadaMissions', ['active'], { active: ['msn_1'] }),
    voyages: source('armadaVoyages', ['vyg_1']),
    merge: source('armadaMergeQueue', ['mrg_1']),
    failures: source('armadaFailures', ['bbx_1'], {}, 1),
    fleet: source('armadaFleet', ['fleet-a'], { 'fleet-a': ['vsl_1'] }),
    captains: source('armadaCaptains', ['cpt_1']),
    delivery: source('armadaResources.delivery', ['deployments']),
  };
  return { ...s, resources: (key: string) => (key === 'delivery' ? s.delivery : undefined) } as never;
}

async function items(nav: NavTreeProvider, parent?: unknown): Promise<Array<{ el: unknown; item: TreeItem }>> {
  const children = await nav.getChildren(parent);
  return Promise.all(children.map(async (el) => ({ el, item: (await nav.getTreeItem(el)) as unknown as TreeItem })));
}

describe('the Armada tree', () => {
  it('lists the dashboard navigation in its order: Overview, then the Operations, Build, Configuration and System hubs', () => {
    const hubs = dashboardHubs(sources());
    const dashboard = navModelSections.flatMap((section) => section.items.map((item) => item.label)).map((label) => (label === 'Settings' ? 'Settings' : label));
    assert.deepStrictEqual(hubs.slice(1).map((h) => h.label), dashboard);
    assert.strictEqual(hubs[0].label, 'Armada Overview');
    assert.deepStrictEqual(hubs.find((h) => h.key === 'missions')?.children.filter((c) => c.kind === 'source' && c.label).map((c) => (c as { label: string }).label), ['Missions', 'Voyages', 'Merge Queue', 'Black Box']);
  });

  it('puts every record kind under exactly one hub, and every hub key exists', () => {
    const noop = async () => undefined;
    const specs = resourceCatalog({ pickCaptain: noop, pickVessel: noop, openPlanning: noop, openRefinement: noop, pickVessels: noop } as never);
    specs.push(...resourceCatalogExtras());
    extendCatalog(specs);
    const placed = Object.values(HUB_RESOURCES).flat();
    assert.deepStrictEqual(placed.length, new Set(placed).size, 'no kind is placed twice');
    assert.deepStrictEqual(specs.filter((s) => !s.hidden).map((s) => s.key).sort(), [...placed].sort(), 'every shown kind is placed, and only those');
    const keys = dashboardHubs(sources()).map((h) => h.key);
    for (const hub of Object.keys(HUB_RESOURCES)) assert.ok(keys.includes(hub), hub);
  });

  it('shows one-source hubs inline, Missions as tabs, and passes rows, ids and menus through', async () => {
    const s = sources();
    const nav = new NavTreeProvider(dashboardHubs(s), () => true);
    const roots = await items(nav);
    assert.deepStrictEqual(roots.map((r) => r.item.label), ['Armada Overview', 'Needs You', 'Dispatch', 'Fleet Actions', 'Missions', 'Activity', 'Vessels', 'Captains', 'Delivery', 'Configuration', 'API Explorer', 'Settings']);
    const overview = roots[0].item;
    assert.strictEqual(overview.collapsibleState, TreeItemCollapsibleState.None);
    assert.strictEqual(overview.command?.command, 'armada.home');
    const needsYou = roots[1];
    assert.strictEqual(needsYou.item.description, '2', 'the count of the inline source');
    assert.match(String(needsYou.item.contextValue), /armadaNav\.hub\.needsYou\b/);
    assert.match(String(needsYou.item.contextValue), /armadaSource\.armadaNeedsYou\b/);
    assert.deepStrictEqual((await items(nav, needsYou.el)).map((r) => r.item.label), ['review-1', 'stall-1']);
    const dispatch = await items(nav, roots[2].el);
    assert.deepStrictEqual(dispatch.map((r) => r.item.command?.command), ['armada.dispatch', 'armada.dispatchVoyage'], 'commands, and no record kinds without a source');
    const missions = await items(nav, roots[4].el);
    assert.deepStrictEqual(missions.map((r) => r.item.label), ['Missions', 'Voyages', 'Merge Queue', 'Black Box']);
    assert.strictEqual(missions[3].item.description, '1');
    assert.match(String(missions[3].item.contextValue), /armadaSource\.armadaFailures\b/);
    assert.match(String(missions[0].item.contextValue), /armadaNav\.tabbable/);
    const active = await items(nav, missions[0].el);
    assert.deepStrictEqual(active.map((r) => [r.item.label, r.item.id, r.item.contextValue]), [['active', 'armadaMissions:active', 'armadaThing']]);
    assert.deepStrictEqual((await items(nav, active[0].el)).map((r) => r.item.label), ['msn_1'], 'deeper rows come from their own tree');
    const vessels = await items(nav, roots[6].el);
    assert.deepStrictEqual(vessels.map((r) => r.item.label), ['fleet-a']);
    assert.deepStrictEqual((await items(nav, roots[8].el)).map((r) => r.item.label), ['deployments']);
    assert.ok(roots[0].item.iconPath instanceof ThemeIcon);
    assert.strictEqual(nav.getParent(missions[0].el), roots[4].el, 'tabs reveal under their hub');
  });

  it('is empty while disconnected (the welcome shows), follows source changes, and builds a hub tab', async () => {
    let connected = false;
    const s = sources();
    const nav = new NavTreeProvider(dashboardHubs(s), () => connected);
    assert.deepStrictEqual(await nav.getChildren(), []);
    connected = true;
    let fired = 0;
    nav.onDidChangeTreeData(() => fired++);
    s.missions.fire();
    assert.strictEqual(fired, 1);
    const hub = new NavHubProvider(nav, 'missions');
    assert.deepStrictEqual((await Promise.all((await hub.getChildren()).map(async (el) => ((await hub.getTreeItem(el)) as unknown as TreeItem).label))), ['Missions', 'Voyages', 'Merge Queue', 'Black Box']);
  });

  it('maps every old view tab command to a hub or tab that opens', () => {
    const hubs = dashboardHubs({ ...sources(), resources: (key: string) => source(`armadaResources.${key}`, ['kind']) });
    const ids = new Set([...hubs.filter((h) => h.children.length).map((h) => `armadaHub.${h.key}`), 'armadaMissions', 'armadaVoyages', 'armadaMergeQueue', 'armadaFailures']);
    for (const [view, target] of Object.entries(LEGACY_TAB_TARGETS)) assert.ok(ids.has(target), `${view} -> ${target}`);
  });

  it('has a menu for every source the menus name', () => {
    const manifest = JSON.parse(fs.readFileSync(path.join(__dirname, '..', '..', '..', 'package.json'), 'utf8')) as { contributes: { menus: Record<string, Array<{ when?: string }>> } };
    const named = new Set<string>();
    for (const entries of Object.values(manifest.contributes.menus)) {
      for (const e of entries) for (const m of (e.when ?? '').matchAll(/armadaSource\\?\.(\w+)/g)) named.add(m[1]);
    }
    const known = new Set(['armadaNeedsYou', 'armadaMissions', 'armadaVoyages', 'armadaMergeQueue', 'armadaFailures', 'armadaFleet', 'armadaCaptains']);
    for (const id of named) assert.ok(known.has(id), id);
  });
});

describe('conversations', () => {
  const thread = (over: Partial<AskThread> = {}): AskThread => ({ id: 'ath_1', title: 'Release plan', pinned: false, archived: false, unreadCount: 0, messageCount: 4, lastMessageUtc: '2026-11-20T10:00:00Z', summaryText: 'Planning *the* release', ...over }) as AskThread;
  const NOW = new Date('2026-11-20T12:00:00Z');

  it('shows a conversation with its age, unread count and state for menus, and opens it on click', () => {
    const item = conversationItem(thread({ unreadCount: 2, pinned: true }), null, NOW) as unknown as TreeItem;
    assert.strictEqual(item.label, 'Release plan');
    assert.match(String(item.description), /^2 unread · /);
    assert.strictEqual((item.iconPath as ThemeIcon).id, 'pinned');
    assert.strictEqual(item.contextValue, 'armadaConversation armadaConversation.pinned armadaConversation.active armadaConversation.unread');
    assert.deepStrictEqual(item.command, { command: 'armada.conversations.open', title: 'Open', arguments: ['ath_1'] });
    assert.match(String((item.tooltip as { value: string }).value), /Planning \\\*the\\\* release/);
  });

  it('marks the conversation open in the chat, and archived ones', () => {
    const open = conversationItem(thread(), 'ath_1', NOW) as unknown as TreeItem;
    assert.match(String(open.description), /\bopen\b/);
    assert.strictEqual((open.iconPath as ThemeIcon).id, 'comment-draft');
    const archived = conversationItem(thread({ archived: true, title: '' }), null, NOW) as unknown as TreeItem;
    assert.strictEqual(archived.label, 'Conversation');
    assert.match(String(archived.contextValue), /armadaConversation\.archived/);
  });
});
