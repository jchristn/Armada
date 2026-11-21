/*
 * The Manage view: a table of contents laid out like the dashboard's navigation (navLayout.ts). A hub opens its
 * dashboard page and expands to its tabs; each tab opens its page. Pages open in editor tabs (dashboard/
 * dashboardPages.ts). The tree reads nothing from the Admiral itself, so it is instant; Needs You shows its count.
 */

import * as vscode from 'vscode';
import { t } from '../../i18n';
import { NavHub, NavPage } from './navLayout';

/** The view id (package.json contributes.views.armada). */
export const NAV_VIEW_ID = 'armadaNav';

/** Focuses the Manage view and expands a hub (argument: the hub key, e.g. "activity"). */
export const NAV_REVEAL_COMMAND = 'armada.nav.reveal';

/** Opens a dashboard page tab (dashboard/dashboardPages.ts). */
const OPEN_PAGE = 'armada.page.open';

/** A node of the tree. */
export type NavNode =
  | { navKind: 'hub'; hub: NavHub }
  | { navKind: 'page'; hub: NavHub; page: NavPage };

/** Whether an element is one of the tree's nodes. */
export function isNavNode(element: unknown): element is NavNode {
  return !!element && typeof element === 'object' && typeof (element as { navKind?: unknown }).navKind === 'string';
}

/** Options for the tree. */
export interface NavTreeOptions {
  /** A count shown beside a hub (Needs You's open items), by hub key. */
  counts?: Record<string, () => number | undefined>;
}

/** The Manage tree. */
export class NavTreeProvider implements vscode.TreeDataProvider<NavNode>, vscode.Disposable {
  private readonly _Hubs: NavHub[];
  private readonly _IsConnected: () => boolean;
  private readonly _Counts: Record<string, () => number | undefined>;
  private readonly _HubNodes: Map<string, NavNode>;
  private readonly _PageNodes = new Map<string, NavNode[]>();
  private readonly _OnDidChange = new vscode.EventEmitter<NavNode | undefined>();

  public readonly onDidChangeTreeData: vscode.Event<NavNode | undefined> = this._OnDidChange.event;

  /**
   * @throws Error when hubs is missing.
   */
  public constructor(hubs: NavHub[], isConnected: () => boolean, options: NavTreeOptions = {}) {
    if (!hubs) throw new Error('hubs is required');
    this._Hubs = hubs;
    this._IsConnected = isConnected;
    this._Counts = options.counts ?? {};
    this._HubNodes = new Map(hubs.map((hub) => [hub.key, { navKind: 'hub', hub } as NavNode]));
    for (const hub of hubs) this._PageNodes.set(hub.key, hub.pages.map((page) => ({ navKind: 'page', hub, page }) as NavNode));
  }

  /** The hubs, in order. */
  public get hubs(): NavHub[] {
    return this._Hubs;
  }

  /** The node of a hub. */
  public hubNode(key: string): NavNode | undefined {
    return this._HubNodes.get(key);
  }

  /** Rebuilds the tree (connection and counts). */
  public refresh(): void {
    this._OnDidChange.fire(undefined);
  }

  public getChildren(element?: NavNode): NavNode[] {
    if (!element) return this._IsConnected() ? this._Hubs.map((hub) => this._HubNodes.get(hub.key) as NavNode) : [];
    if (element.navKind === 'hub') return this._PageNodes.get(element.hub.key) ?? [];
    return [];
  }

  public getParent(element: NavNode): NavNode | undefined {
    return element.navKind === 'page' ? this._HubNodes.get(element.hub.key) : undefined;
  }

  public getTreeItem(node: NavNode): vscode.TreeItem {
    if (node.navKind === 'page') {
      const page = node.page;
      const item = new vscode.TreeItem(t(page.label), vscode.TreeItemCollapsibleState.None);
      item.id = `nav:page:${page.route}`;
      item.iconPath = new vscode.ThemeIcon(page.icon);
      item.tooltip = page.tooltip ? `${t(page.label)}: ${t(page.tooltip)}` : t(page.label);
      item.command = { command: OPEN_PAGE, title: t('Open'), arguments: [page.route] };
      item.contextValue = 'armadaNavPage';
      return item;
    }
    const hub = node.hub;
    const leaf = !hub.pages.length;
    const item = new vscode.TreeItem(t(hub.label), leaf ? vscode.TreeItemCollapsibleState.None : vscode.TreeItemCollapsibleState.Collapsed);
    item.id = `nav:hub:${hub.key}`;
    item.iconPath = new vscode.ThemeIcon(hub.icon);
    item.tooltip = hub.section ? `${t(hub.label)} (${t(hub.section)}): ${t(hub.tooltip)}` : t(hub.tooltip);
    // Clicking a hub opens its dashboard page; the arrow lists its tabs.
    item.command = { command: OPEN_PAGE, title: t('Open'), arguments: [hub.route] };
    const count = this._Counts[hub.key]?.();
    item.description = count ? String(count) : undefined;
    item.contextValue = `armadaNav armadaNav.hub.${hub.key}`;
    return item;
  }

  public dispose(): void {
    this._OnDidChange.dispose();
  }
}

/** Every page of the tree, for "Armada: Open Page...": hubs and their tabs, in order. */
export function navPages(hubs: NavHub[]): Array<{ label: string; route: string }> {
  const pages: Array<{ label: string; route: string }> = [];
  for (const hub of hubs) {
    pages.push({ label: hub.label, route: hub.route });
    for (const page of hub.pages) if (page.label !== hub.label) pages.push({ label: `${hub.label}: ${page.label}`, route: page.route });
  }
  return pages;
}
