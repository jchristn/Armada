import * as assert from 'assert';
import * as fs from 'fs';
import * as path from 'path';
import { beforeEach, describe, it } from 'node:test';
import { navModelSections } from '@dashboard/lib/navModel';
import type { AskThread } from '@dashboard/types/models';
import { conversationItem } from '../../src/surfaces/ask/conversationsTree';
import { dashboardHubs, LEGACY_TAB_TARGETS } from '../../src/surfaces/nav/navLayout';
import { NavNode, navPages, NavTreeProvider } from '../../src/surfaces/nav/navTree';
import { resourceCatalog } from '../../src/surfaces/resources/resourceCatalog';
import { extendCatalog, resourceCatalogExtras } from '../../src/surfaces/resources/resourceCatalogExtras';
import { createRoute, entityRoute, HUB_ROUTES, isDashboardRoute, KIND_ROUTES, recordRoute, routeArea, routeForId, SOURCE_ROUTES } from '../../src/surfaces/dashboard/routes';
import { hostHandledRoute } from '../../src/surfaces/dashboard/protocol';
import { fake, ThemeIcon, TreeItem, TreeItemCollapsibleState } from '../fakes/vscode';

function items(nav: NavTreeProvider, parent?: NavNode): Array<{ el: NavNode; item: TreeItem }> {
  return nav.getChildren(parent).map((el) => ({ el, item: nav.getTreeItem(el) as unknown as TreeItem }));
}

/** The dashboard's hub registry (lib/dashboardNavigation.ts dashboardHubs), read from its source: path, param, tab keys. */
function dashboardRegistry(): Array<{ path: string; param: string; tabs: string[] }> {
  const src = fs.readFileSync(path.join(__dirname, '..', '..', '..', '..', 'Armada.Dashboard', 'src', 'lib', 'dashboardNavigation.ts'), 'utf8');
  const block = src.slice(src.indexOf('export const dashboardHubs'), src.indexOf('export const dashboardCollections'));
  return [...block.matchAll(/path: '([^']+)', label: '[^']+', param: '(\w+)'[\s\S]*?tabs: \[([\s\S]*?)\]/g)].map((m) => ({
    path: m[1], param: m[2], tabs: [...m[3].matchAll(/key: '([^']+)'/g)].map((k) => k[1]),
  }));
}

describe('the Manage tree', () => {
  beforeEach(() => fake.reset());

  it('lists the dashboard navigation in its order: Overview, then the Operations, Build, Configuration and System hubs', () => {
    const hubs = dashboardHubs();
    const dashboard = navModelSections.flatMap((section) => section.items.map((item) => item.label));
    assert.deepStrictEqual(hubs.slice(1).map((h) => h.label), dashboard);
    assert.strictEqual(hubs[0].label, 'Armada Overview');
  });

  it('lists each hub\'s tabs exactly as the dashboard\'s hub registry does, in its order', () => {
    const hubs = dashboardHubs();
    for (const entry of dashboardRegistry()) {
      const hub = hubs.find((h) => h.route === entry.path);
      assert.ok(hub, `a hub for ${entry.path}`);
      const tabRoutes = hub.pages.map((p) => p.route).filter((r) => r.startsWith(`${entry.path}?${entry.param}=`));
      assert.deepStrictEqual(tabRoutes, entry.tabs.map((k) => `${entry.path}?${entry.param}=${k}`), entry.path);
    }
    assert.ok(dashboardRegistry().length >= 9, 'the registry was read');
  });

  it('gives every entry an icon and a page the dashboard has', () => {
    for (const hub of dashboardHubs()) {
      assert.ok(hub.icon && isDashboardRoute(hub.route), hub.key);
      for (const page of hub.pages) assert.ok(page.icon && isDashboardRoute(page.route), `${hub.key}: ${page.label} ${page.route}`);
    }
  });

  it('opens a hub\'s page on click and lists its tabs, which open theirs; nothing expands into records', () => {
    const nav = new NavTreeProvider(dashboardHubs(), () => true, { counts: { needsYou: () => 3 } });
    const roots = items(nav);
    assert.deepStrictEqual(roots.map((r) => r.item.label), ['Armada Overview', 'Needs You', 'Dispatch', 'Fleet Actions', 'Missions', 'Activity', 'Vessels', 'Captains', 'Delivery', 'Configuration', 'API Explorer', 'Settings']);
    assert.deepStrictEqual(roots.map((r) => r.item.command?.arguments?.[0]), ['/', '/inbox', '/dispatch', '/fleet-actions', '/missions', '/activity', '/vessels', '/captains', '/delivery', '/configuration', '/api-explorer', '/server']);
    assert.ok(roots.every((r) => r.item.command?.command === 'armada.page.open'));
    const needsYou = roots[1].item;
    assert.strictEqual(needsYou.collapsibleState, TreeItemCollapsibleState.None, 'Needs You opens its page; it does not expand');
    assert.strictEqual(needsYou.description, '3');
    assert.match(String(needsYou.contextValue), /armadaNav\.hub\.needsYou\b/);
    const activity = items(nav, roots[5].el);
    assert.deepStrictEqual(activity.map((r) => r.item.label), ['All Activity', 'API Requests', 'Events', 'Signals', 'Token Usage', 'Captain Usage', 'Concurrency']);
    assert.deepStrictEqual(activity.map((r) => r.item.command?.arguments?.[0]), ['/activity?source=history', '/activity?source=requests', '/activity?source=events', '/activity?source=signals', '/activity?source=tokens', '/activity?source=usage-overview', '/activity?source=concurrency']);
    for (const row of activity) {
      assert.strictEqual(row.item.collapsibleState, TreeItemCollapsibleState.None, 'a tab is a leaf');
      assert.ok(row.item.iconPath instanceof ThemeIcon);
      assert.deepStrictEqual(items(nav, row.el), []);
    }
    const configuration = items(nav, roots[9].el);
    assert.strictEqual(configuration[0].item.label, 'Workflow Profiles');
    assert.ok(configuration.every((r) => r.item.iconPath instanceof ThemeIcon), 'every Configuration entry has an icon');
    assert.strictEqual(nav.getParent(activity[0].el), roots[5].el, 'tabs reveal under their hub');
  });

  it('is empty while disconnected (the welcome shows) and lists every page for Open Page', () => {
    let connected = false;
    const nav = new NavTreeProvider(dashboardHubs(), () => connected);
    assert.deepStrictEqual(nav.getChildren(), []);
    connected = true;
    let fired = 0;
    nav.onDidChangeTreeData(() => fired++);
    nav.refresh();
    assert.strictEqual(fired, 1);
    const pages = navPages(dashboardHubs());
    assert.ok(pages.some((p) => p.label === 'Activity: Token Usage' && p.route === '/activity?source=tokens'));
    assert.ok(pages.some((p) => p.label === 'Dispatch: Create Voyage'));
    assert.ok(!pages.some((p) => p.label === 'Missions: Missions'), 'a tab named like its hub is listed once');
  });

  it('maps every old view tab command to a page the dashboard has', () => {
    for (const [view, route] of Object.entries(LEGACY_TAB_TARGETS)) assert.ok(isDashboardRoute(route), `${view} -> ${route}`);
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

describe('dashboard page routes', () => {
  beforeEach(() => fake.reset());

  it('opens every hub, tab and record kind on a page the dashboard has', () => {
    assert.strictEqual(routeForId('mdl_1'), '/model-profiles/mdl_1');
    assert.ok(isDashboardRoute('/model-profiles/mdl_1'));
    assert.strictEqual(KIND_ROUTES.modelProfile, '/captains?tab=profiles');
    for (const [key, route] of Object.entries({ ...HUB_ROUTES, ...SOURCE_ROUTES, ...KIND_ROUTES })) assert.ok(isDashboardRoute(route), `${key} -> ${route}`);
    for (const hub of dashboardHubs()) assert.strictEqual(HUB_ROUTES[hub.key], hub.route, `hub ${hub.key}`);
    const specs = resourceCatalog({} as never);
    specs.push(...resourceCatalogExtras());
    extendCatalog(specs);
    for (const spec of specs.filter((s) => !s.hidden)) assert.ok(KIND_ROUTES[spec.key], `kind ${spec.key} has a list page`);
  });

  it('opens each record on its own page when the dashboard has one, else on its kind\'s list', () => {
    const specs = resourceCatalog({} as never);
    specs.push(...resourceCatalogExtras());
    extendCatalog(specs);
    for (const spec of specs) {
      const route = recordRoute(spec.key, spec.route?.({ id: 'x_1', name: 'n', runbookId: 'rbk_1', vesselId: 'vsl_1' }));
      assert.ok(isDashboardRoute(route), `${spec.key} -> ${route}`);
    }
    assert.strictEqual(recordRoute('modelEndpoint', '/model-endpoints/mep_1'), '/configuration?tab=endpoints', 'no detail page: the list');
    assert.strictEqual(recordRoute('pipeline', '/pipelines/ppl_1'), '/pipelines/ppl_1');
    assert.strictEqual(createRoute('backlog'), '/backlog/new');
    assert.strictEqual(createRoute('persona'), '/configuration?tab=personas');
  });

  it('maps entity ids and routes to pages, areas and host-handled views', () => {
    assert.strictEqual(entityRoute('mission', 'msn_1'), '/missions/msn_1');
    assert.strictEqual(entityRoute('merge', 'mrg_1'), '/merge-queue/mrg_1');
    assert.strictEqual(routeForId('vyg_9'), '/voyages/vyg_9');
    assert.strictEqual(routeForId('xyz'), undefined);
    assert.strictEqual(routeArea('/activity?source=tokens'), 'activity');
    assert.strictEqual(routeArea('/'), 'overview');
    assert.strictEqual(hostHandledRoute('/ask/ath_1'), 'armada.ask.open');
    assert.strictEqual(hostHandledRoute('/askew'), undefined);
    assert.ok(!isDashboardRoute('/nowhere'));
    assert.ok(isDashboardRoute('/missions/msn_1?x=1'));
  });
});

