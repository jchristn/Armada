/*
 * The Armada sidebar as one tree, laid out like the dashboard's navigation: Armada Overview, then the hubs of its
 * Operations, Build, Configuration and System sections (Needs You, Dispatch, Fleet Actions, Missions, Activity,
 * Vessels, Captains, Delivery, Configuration, API Explorer, Settings). A hub holds the trees that used to be views of
 * their own: inline (a hub with one source shows that tree's rows directly) or as a tab node (Missions holds Missions,
 * Voyages, Merge Queue and Black Box). Rows below a hub come from the source trees unchanged, so their commands,
 * context menus and live updates keep working.
 */

import * as vscode from 'vscode';
import { t } from '../../i18n';

/** The view id (package.json contributes.views.armada). */
export const NAV_VIEW_ID = 'armadaNav';

/** Focuses the Armada tree and expands a hub (argument: the hub key, e.g. "needsYou"). */
export const NAV_REVEAL_COMMAND = 'armada.nav.reveal';

/** A tree that is shown inside the Armada tree. */
export interface NavSource {
  /** Stable id ("armadaMissions"); menus match `armadaSource.<id>` on the node that stands for this source. */
  id: string;
  provider: vscode.TreeDataProvider<unknown>;
  /** A count for the node's description (open items, entries waiting). */
  count?: () => number | undefined;
}

/** One child of a hub. */
export type NavChild =
  | { kind: 'source'; source: NavSource; label?: string; icon?: string; tooltip?: string }
  | { kind: 'command'; label: string; icon: string; command: string; args?: unknown[]; tooltip?: string };

/** A dashboard hub, or a leaf (Armada Overview, API Explorer) when it has a command and no children. */
export interface NavHub {
  key: string;
  label: string;
  icon: string;
  tooltip: string;
  /** The dashboard section the hub belongs to (shown in its tooltip). */
  section?: string;
  children: NavChild[];
  command?: { command: string; args?: unknown[] };
}

/** A node the Armada tree adds itself. */
export type NavNode =
  | { navKind: 'hub'; hub: NavHub }
  | { navKind: 'tab'; hub: NavHub; child: Extract<NavChild, { kind: 'source' }> }
  | { navKind: 'command'; hub: NavHub; child: Extract<NavChild, { kind: 'command' }> };

/** Whether an element is one of the Armada tree's own nodes. */
export function isNavNode(element: unknown): element is NavNode {
  return !!element && typeof element === 'object' && typeof (element as { navKind?: unknown }).navKind === 'string';
}

/** The view-tab id of a hub ("armadaHub.missions"). */
export function hubTabId(hub: NavHub): string {
  return `armadaHub.${hub.key}`;
}

/** The Armada tree. */
export class NavTreeProvider implements vscode.TreeDataProvider<unknown>, vscode.Disposable {
  private readonly _Hubs: NavHub[];
  private readonly _IsConnected: () => boolean;
  private readonly _HubNodes: Map<string, NavNode>;
  private readonly _TabNodes = new Map<string, NavNode>();
  private readonly _Owners = new WeakMap<object, NavSource>();
  private readonly _OnDidChange = new vscode.EventEmitter<unknown>();
  private readonly _Subscriptions: vscode.Disposable[] = [];

  public readonly onDidChangeTreeData: vscode.Event<unknown> = this._OnDidChange.event;

  /**
   * @throws Error when hubs is missing.
   */
  public constructor(hubs: NavHub[], isConnected: () => boolean) {
    if (!hubs) throw new Error('hubs is required');
    this._Hubs = hubs;
    this._IsConnected = isConnected;
    this._HubNodes = new Map(hubs.map((hub) => [hub.key, { navKind: 'hub', hub } as NavNode]));
    const seen = new Set<NavSource>();
    for (const hub of hubs) {
      for (const child of hub.children) {
        if (child.kind !== 'source' || seen.has(child.source)) continue;
        seen.add(child.source);
        const event = child.source.provider.onDidChangeTreeData;
        if (!event) continue;
        this._Subscriptions.push(event((changed) => {
          // A whole-tree change of a source also changes the counts on the hubs: rebuild from the top.
          const element = Array.isArray(changed) ? undefined : changed;
          this._OnDidChange.fire(element && typeof element === 'object' && this._Owners.has(element) ? element : undefined);
        }) as vscode.Disposable);
      }
    }
  }

  /** The hubs, in order. */
  public get hubs(): NavHub[] {
    return this._Hubs;
  }

  /** The hub node for a key. */
  public hubNode(key: string): NavNode | undefined {
    return this._HubNodes.get(key);
  }

  /** Rebuilds the tree. */
  public refresh(): void {
    this._OnDidChange.fire(undefined);
  }

  public getChildren(element?: unknown): unknown[] | Thenable<unknown[]> {
    if (!element) return this._IsConnected() ? this._Hubs.map((hub) => this._HubNodes.get(hub.key) as NavNode) : [];
    if (isNavNode(element)) {
      if (element.navKind === 'hub') return this.hubChildren(element.hub);
      if (element.navKind === 'tab') return this.sourceChildren(element.child.source, undefined);
      return [];
    }
    const owner = typeof element === 'object' ? this._Owners.get(element as object) : undefined;
    return owner ? this.sourceChildren(owner, element) : [];
  }

  /** Parents of the tree's own nodes, so a hub can be revealed. Rows from the source trees are not revealed. */
  public getParent(element: unknown): unknown {
    if (isNavNode(element) && element.navKind !== 'hub') return this._HubNodes.get(element.hub.key);
    return undefined;
  }

  public getTreeItem(element: unknown): vscode.TreeItem | Thenable<vscode.TreeItem> {
    if (isNavNode(element)) return this.navItem(element);
    const owner = typeof element === 'object' ? this._Owners.get(element as object) : undefined;
    if (!owner) return new vscode.TreeItem('');
    return Promise.resolve(owner.provider.getTreeItem(element)).then((item) => {
      // Ids only have to be unique per view; prefix them with the source so two sources never collide here.
      if (item.id) item.id = `${owner.id}:${item.id}`;
      return item;
    });
  }

  public dispose(): void {
    for (const s of this._Subscriptions) s.dispose();
    this._OnDidChange.dispose();
  }

  private async hubChildren(hub: NavHub): Promise<unknown[]> {
    const rows: unknown[] = [];
    for (const child of hub.children) {
      if (child.kind === 'command') rows.push({ navKind: 'command', hub, child } as NavNode);
      else if (child.label) rows.push(this.tabNode(hub, child));
      else rows.push(...(await this.sourceChildren(child.source, undefined)));
    }
    return rows;
  }

  private tabNode(hub: NavHub, child: Extract<NavChild, { kind: 'source' }>): NavNode {
    let node = this._TabNodes.get(child.source.id);
    if (!node) {
      node = { navKind: 'tab', hub, child };
      this._TabNodes.set(child.source.id, node);
    }
    return node;
  }

  private async sourceChildren(source: NavSource, element: unknown): Promise<unknown[]> {
    const children = (await source.provider.getChildren(element)) ?? [];
    for (const child of children) if (child && typeof child === 'object') this._Owners.set(child as object, source);
    return children;
  }

  private navItem(node: NavNode): vscode.TreeItem {
    if (node.navKind === 'command') {
      const item = new vscode.TreeItem(t(node.child.label), vscode.TreeItemCollapsibleState.None);
      item.id = `nav:command:${node.hub.key}:${node.child.command}`;
      item.iconPath = new vscode.ThemeIcon(node.child.icon);
      item.tooltip = node.child.tooltip ? t(node.child.tooltip) : undefined;
      item.command = { command: node.child.command, title: node.child.label, arguments: node.child.args ?? [] };
      item.contextValue = 'armadaNavCommand';
      return item;
    }
    if (node.navKind === 'tab') {
      const source = node.child.source;
      const item = new vscode.TreeItem(t(node.child.label ?? source.id), vscode.TreeItemCollapsibleState.Collapsed);
      item.id = `nav:tab:${source.id}`;
      item.iconPath = new vscode.ThemeIcon(node.child.icon ?? 'list-tree');
      item.tooltip = node.child.tooltip ? t(node.child.tooltip) : undefined;
      item.description = countText(source.count?.());
      item.contextValue = `armadaNav armadaNav.tabbable armadaSource.${source.id}`;
      return item;
    }
    const hub = node.hub;
    const leaf = !hub.children.length;
    const item = new vscode.TreeItem(t(hub.label), leaf ? vscode.TreeItemCollapsibleState.None : vscode.TreeItemCollapsibleState.Collapsed);
    item.id = `nav:hub:${hub.key}`;
    item.iconPath = new vscode.ThemeIcon(hub.icon);
    item.tooltip = hub.section ? `${t(hub.label)} (${t(hub.section)}): ${t(hub.tooltip)}` : t(hub.tooltip);
    if (hub.command) item.command = { command: hub.command.command, title: hub.label, arguments: hub.command.args ?? [] };
    // A hub's count is the sum over the sources shown directly in it (Needs You's open items).
    const inline = hub.children.filter((c): c is Extract<NavChild, { kind: 'source' }> => c.kind === 'source' && !c.label);
    const total = inline.reduce((sum, c) => sum + (c.source.count?.() ?? 0), 0);
    item.description = countText(total);
    item.contextValue = ['armadaNav', `armadaNav.hub.${hub.key}`, leaf ? '' : 'armadaNav.tabbable', ...inline.map((c) => `armadaSource.${c.source.id}`)].filter(Boolean).join(' ');
    return item;
  }
}

/** A tree for one hub, for its editor tab. */
export class NavHubProvider implements vscode.TreeDataProvider<unknown> {
  private readonly _Nav: NavTreeProvider;
  private readonly _Key: string;

  public readonly onDidChangeTreeData: vscode.Event<unknown>;

  public constructor(nav: NavTreeProvider, key: string) {
    this._Nav = nav;
    this._Key = key;
    this.onDidChangeTreeData = nav.onDidChangeTreeData;
  }

  public getChildren(element?: unknown): unknown[] | Thenable<unknown[]> {
    return this._Nav.getChildren(element ?? this._Nav.hubNode(this._Key));
  }

  public getTreeItem(element: unknown): vscode.TreeItem | Thenable<vscode.TreeItem> {
    return this._Nav.getTreeItem(element);
  }
}

function countText(count: number | undefined): string | undefined {
  return count ? String(count) : undefined;
}
