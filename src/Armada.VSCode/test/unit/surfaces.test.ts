import * as assert from 'assert';
import * as fs from 'fs';
import * as os from 'os';
import * as path from 'path';
import { beforeEach, describe, it } from 'node:test';
import * as vscode from 'vscode';
import { askArmadaModelItem, dashboardModelItem, navModelSections, navShortcuts } from '@dashboard/lib/navModel';
import { ConnectionState } from '../../src/connection/connectionManager';
import { connectionActions, describeStatus } from '../../src/connection/statusBar';
import { activate, ArmadaExtensionApi } from '../../src/extension';
import { FAILURES_EXPLORER_VIEW_ID, FAILURES_VIEW_ID } from '../../src/surfaces/blackbox/failuresTreeProvider';
import { NAV_ICONS, NAVIGATION_VIEW_ID, NavigationTreeProvider, NavNode, NavPageNode, NavSectionNode, navigationRoots, OPEN_DASHBOARD_PAGE_COMMAND, routeFromArg } from '../../src/surfaces/nav/navigationTree';
import { fake, FakeMemento, FakeSecretStorage, ThemeIcon, TreeItemCollapsibleState, Uri } from '../fakes/vscode';

/** The extension manifest; the extension ID is read from it, never written in a test. */
interface Manifest {
  name: string;
  publisher: string;
  version: string;
  activationEvents: string[];
  contributes: {
    viewsContainers: { activitybar: Array<{ id: string; icon: string }> };
    views: Record<string, Array<{ id: string; when?: string }>>;
    viewsWelcome: Array<{ view: string; contents: string; when: string }>;
    commands: Array<{ command: string; title: string }>;
    configuration: { properties: Record<string, { default: unknown }> };
    menus: Record<string, Array<{ command: string; when?: string }>>;
  };
}

const ROOT = path.resolve(__dirname, '..', '..', '..');
const MANIFEST = JSON.parse(fs.readFileSync(path.join(ROOT, 'package.json'), 'utf8')) as Manifest;
const EXTENSION_ID = `${MANIFEST.publisher}.${MANIFEST.name}`;

const CONNECTED: ConnectionState = {
  phase: 'connected',
  target: { kind: 'local', baseUrl: 'http://127.0.0.1:7890', label: 'local', source: '/h/.armada/settings.json' },
  serverVersion: '1.3.0',
  contractVersion: '1.0',
  identity: { email: 'dev@example.com', tenantId: 'ten_1', tenantName: 'Default', globalAdmin: true, tenantAdmin: true },
};

async function activateWithFakes(): Promise<{ api: ArmadaExtensionApi; context: { subscriptions: Array<{ dispose(): void }> } }> {
  const context = {
    subscriptions: [] as Array<{ dispose(): void }>,
    secrets: new FakeSecretStorage(),
    globalState: new FakeMemento(),
    globalStorageUri: Uri.file(path.join(os.tmpdir(), 'armada-vscode-test-storage')),
    extension: { id: EXTENSION_ID },
  };
  const api = await activate(context as unknown as vscode.ExtensionContext);
  return { api, context };
}

beforeEach(() => fake.reset());

describe('navigation tree', () => {
  it('mirrors the dashboard navigation model: Dashboard, Ask Armada, then OPERATIONS, BUILD, CONFIGURATION and SYSTEM', () => {
    const roots = navigationRoots();
    const pages = roots.filter((n): n is NavPageNode => n.kind === 'page');
    const sections = roots.filter((n): n is NavSectionNode => n.kind === 'section');
    assert.deepStrictEqual(pages.map((p) => p.route), [dashboardModelItem.to, askArmadaModelItem.to]);
    assert.deepStrictEqual(sections.map((s) => s.label), ['OPERATIONS', 'BUILD', 'CONFIGURATION', 'SYSTEM']);
    assert.deepStrictEqual(sections.map((s) => s.label), navModelSections.map((s) => s.label));
    for (const section of navModelSections) {
      const node = sections.find((s) => s.key === section.key);
      assert.ok(node, section.key);
      const sidebar = section.items.filter((i) => !i.hidden).map((i) => i.to);
      assert.deepStrictEqual(node.pages.filter((p) => !p.shortcut).map((p) => p.route), sidebar, `${section.label} items`);
      const shortcuts = navShortcuts.filter((s) => s.section === section.label).map((s) => s.to);
      assert.deepStrictEqual(node.pages.filter((p) => p.shortcut).map((p) => p.route), shortcuts, `${section.label} shortcuts`);
    }
  });

  it('has a codicon for every icon key the model uses', () => {
    const keys = new Set([dashboardModelItem.icon, askArmadaModelItem.icon, ...navModelSections.flatMap((s) => s.items.map((i) => i.icon)), ...navShortcuts.map((s) => s.icon)]);
    for (const key of keys) assert.ok(NAV_ICONS[key], `icon for ${key}`);
  });

  it('is empty while disconnected, and each page opens its dashboard route', () => {
    let connected = false;
    const tree = new NavigationTreeProvider(() => connected);
    assert.deepStrictEqual(tree.getChildren(), []);
    connected = true;
    const roots = tree.getChildren();
    const operations = roots.find((n): n is NavSectionNode => n.kind === 'section' && n.label === 'OPERATIONS');
    assert.ok(operations);
    const sectionItem = tree.getTreeItem(operations) as unknown as { collapsibleState: TreeItemCollapsibleState };
    assert.strictEqual(sectionItem.collapsibleState, TreeItemCollapsibleState.Expanded);
    const missions = tree.getChildren(operations).find((n: NavNode) => n.kind === 'page' && n.route === '/missions') as NavPageNode;
    const item = tree.getTreeItem(missions) as unknown as { command: { command: string; arguments: unknown[] }; iconPath: ThemeIcon };
    assert.strictEqual(item.command.command, OPEN_DASHBOARD_PAGE_COMMAND);
    assert.deepStrictEqual(item.command.arguments, ['/missions']);
    assert.strictEqual(item.iconPath.id, 'checklist');
    assert.strictEqual(routeFromArg(missions), '/missions');
    assert.strictEqual(routeFromArg('/server?tab=storage'), '/server?tab=storage');
    assert.strictEqual(routeFromArg('missions'), undefined);
  });
});

describe('status bar', () => {
  it('shows the state and target', () => {
    const connected = describeStatus(CONNECTED, 'websocket');
    assert.strictEqual(connected.text, '$(plug) Armada: local');
    assert.match(connected.tooltip, /connected to http:\/\/127\.0\.0\.1:7890 \(local Admiral\)/);
    assert.match(connected.tooltip, /Armada 1\.3\.0, contract 1\.0/);
    assert.match(connected.tooltip, /dev@example\.com \(Default\), global admin/);
    assert.match(connected.tooltip, /Live updates: WebSocket/);
    const remote = describeStatus({ ...CONNECTED, target: { kind: 'remote', baseUrl: 'https://a.example.com', label: 'a.example.com', source: 'armada.url' } }, 'polling');
    assert.strictEqual(remote.text, '$(plug) Armada: a.example.com');
    assert.strictEqual(describeStatus({ phase: 'connecting', target: CONNECTED.target }, 'stopped').text, '$(sync~spin) Armada: local');
    const failed = describeStatus({ phase: 'failed', failure: { kind: 'incompatible', summary: 'server too old', message: 'Update Armada.' } }, 'stopped');
    assert.strictEqual(failed.text, '$(warning) Armada: server too old');
    assert.strictEqual(failed.warning, true);
    assert.strictEqual(describeStatus({ phase: 'disconnected' }, 'stopped').text, '$(debug-disconnect) Armada');
  });

  it('offers connection actions for the state', () => {
    const commands = (state: ConnectionState) => connectionActions(state).map((a) => a.command);
    assert.deepStrictEqual(commands(CONNECTED), ['armada.openDashboard', 'armada.connect', 'armada.reconnect', 'armada.disconnect', 'armada.showOutput', 'armada.openSettings']);
    assert.deepStrictEqual(commands({ phase: 'disconnected' }), ['armada.connect', 'armada.showOutput', 'armada.openSettings']);
    assert.deepStrictEqual(commands({ phase: 'failed', target: CONNECTED.target, failure: { kind: 'unreachable', summary: 'unreachable', message: 'x' } }), ['armada.connect', 'armada.reconnect', 'armada.disconnect', 'armada.showOutput', 'armada.openSettings']);
  });
});

describe('extension manifest and activation', () => {
  it('reads its identity from package.json and keeps the product version', () => {
    assert.match(EXTENSION_ID, /^[a-z0-9-]+\.[a-z0-9-]+$/);
    assert.match(MANIFEST.version, /^\d+\.\d+\.\d+$/);
    assert.deepStrictEqual(MANIFEST.activationEvents, ['onStartupFinished']);
  });

  it('contributes the Armada container with the navigation and failures views, and the Explorer copy behind its setting', () => {
    const container = MANIFEST.contributes.viewsContainers.activitybar.find((c) => c.id === 'armada');
    assert.ok(container);
    assert.ok(fs.existsSync(path.join(ROOT, container.icon)), 'container icon exists');
    assert.deepStrictEqual(MANIFEST.contributes.views.armada.map((v) => v.id), [NAVIGATION_VIEW_ID, FAILURES_VIEW_ID]);
    const explorer = MANIFEST.contributes.views.explorer.find((v) => v.id === FAILURES_EXPLORER_VIEW_ID);
    assert.strictEqual(explorer?.when, 'config.armada.failures.showInExplorer');
    assert.strictEqual(MANIFEST.contributes.configuration.properties['armada.failures.showInExplorer'].default, false);
    for (const view of [NAVIGATION_VIEW_ID, FAILURES_VIEW_ID, FAILURES_EXPLORER_VIEW_ID]) {
      const welcome = MANIFEST.contributes.viewsWelcome.filter((w) => w.view === view);
      assert.ok(welcome.some((w) => w.when.includes('!armada.connected') && w.contents.includes('command:armada.connect')), `disconnected welcome for ${view}`);
    }
  });

  it('registers exactly the contributed commands, and every menu and welcome link names one', async () => {
    const tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'armada-vscode-unit-'));
    const previous = process.env.ARMADA_DATA_DIR;
    process.env.ARMADA_DATA_DIR = tmp;
    try {
      const { api, context } = await activateWithFakes();
      const contributed = MANIFEST.contributes.commands.map((c) => c.command).sort();
      assert.deepStrictEqual(Array.from(fake.registered.keys()).sort(), contributed);
      for (const [menu, entries] of Object.entries(MANIFEST.contributes.menus)) {
        for (const entry of entries) assert.ok(contributed.includes(entry.command), `${menu}: ${entry.command}`);
      }
      for (const welcome of MANIFEST.contributes.viewsWelcome) {
        for (const match of welcome.contents.matchAll(/command:([A-Za-z.]+)/g)) assert.ok(contributed.includes(match[1]), `welcome link ${match[1]}`);
      }
      assert.deepStrictEqual(fake.treeViews, [NAVIGATION_VIEW_ID, FAILURES_VIEW_ID, FAILURES_EXPLORER_VIEW_ID]);
      assert.strictEqual(fake.statusBarItems.length, 1);
      assert.strictEqual(fake.statusBarItems[0].command, 'armada.showConnectionActions');
      assert.strictEqual(api.connection.state.phase, 'disconnected', 'no network work during activate');
      assert.strictEqual(api.liveMode(), 'stopped');
      for (const s of context.subscriptions) s.dispose();
    } finally {
      if (previous === undefined) delete process.env.ARMADA_DATA_DIR;
      else process.env.ARMADA_DATA_DIR = previous;
      fs.rmSync(tmp, { recursive: true, force: true });
    }
  });
});
