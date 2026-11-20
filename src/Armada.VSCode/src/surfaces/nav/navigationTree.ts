/*
 * The Armada navigation tree: the dashboard's navigation model (@dashboard/lib/navModel, shared with the dashboard
 * sidebar and the mobile app) as a tree with the sections OPERATIONS, BUILD, CONFIGURATION and SYSTEM. Until a domain
 * has a native view or panel, a page opens in the external browser (VSCODE_EXTENSION_PLAN.md 6, interim fallback).
 */

import * as vscode from 'vscode';
import { askArmadaModelItem, dashboardModelItem, DEFAULT_EXPANDED_SECTIONS, navModelSections, navShortcuts, NavModelItem } from '@dashboard/lib/navModel';

/** Tree view id (package.json contributes.views.armada). */
export const NAVIGATION_VIEW_ID = 'armadaNavigation';

/** Command that opens a dashboard route. */
export const OPEN_DASHBOARD_PAGE_COMMAND = 'armada.openDashboardPage';

/** Codicons for the navigation model's icon keys (each client maps the keys to its own glyphs). */
export const NAV_ICONS: Record<string, string> = {
  dashboard: 'dashboard',
  ask: 'comment-discussion',
  needsYou: 'bell',
  dispatch: 'rocket',
  fleetActions: 'run-all',
  missions: 'checklist',
  activity: 'pulse',
  vessels: 'repo',
  captains: 'person',
  delivery: 'package',
  configuration: 'settings-gear',
  apiExplorer: 'symbol-interface',
  server: 'server',
  planning: 'lightbulb',
  blackbox: 'archive',
  cliPermissions: 'shield',
};

/** A section of the tree. */
export interface NavSectionNode {
  kind: 'section';
  key: string;
  label: string;
  pages: NavPageNode[];
}

/** A dashboard page. */
export interface NavPageNode {
  kind: 'page';
  route: string;
  label: string;
  tooltip: string;
  icon: string;
  /** A hub tab reached through a command-palette shortcut rather than a sidebar item. */
  shortcut: boolean;
}

export type NavNode = NavSectionNode | NavPageNode;

/** The tree's top level: Dashboard, Ask Armada, then one node per section. Pure, for tests. */
export function navigationRoots(): NavNode[] {
  const roots: NavNode[] = [page(dashboardModelItem, false), page(askArmadaModelItem, false)];
  for (const section of navModelSections) {
    const pages = section.items.filter((item) => !item.hidden).map((item) => page(item, false));
    for (const shortcut of navShortcuts) {
      if (shortcut.section === section.label) pages.push(page(shortcut, true));
    }
    roots.push({ kind: 'section', key: section.key, label: section.label, pages });
  }
  return roots;
}

/** Tree data for the navigation view. Empty while disconnected, so the welcome view shows. */
export class NavigationTreeProvider implements vscode.TreeDataProvider<NavNode>, vscode.Disposable {
  private readonly _IsConnected: () => boolean;
  private readonly _OnDidChange = new vscode.EventEmitter<NavNode | undefined>();

  public readonly onDidChangeTreeData: vscode.Event<NavNode | undefined> = this._OnDidChange.event;

  /**
   * @throws Error when isConnected is missing.
   */
  public constructor(isConnected: () => boolean) {
    if (!isConnected) throw new Error('isConnected is required');
    this._IsConnected = isConnected;
  }

  /** Re-reads the connection state. */
  public refresh(): void {
    this._OnDidChange.fire(undefined);
  }

  public getChildren(element?: NavNode): NavNode[] {
    if (!element) return this._IsConnected() ? navigationRoots() : [];
    return element.kind === 'section' ? element.pages : [];
  }

  public getTreeItem(element: NavNode): vscode.TreeItem {
    if (element.kind === 'section') {
      const expanded = DEFAULT_EXPANDED_SECTIONS[element.key] !== false;
      const item = new vscode.TreeItem(element.label, expanded ? vscode.TreeItemCollapsibleState.Expanded : vscode.TreeItemCollapsibleState.Collapsed);
      item.id = `nav:section:${element.key}`;
      item.contextValue = 'armadaNavSection';
      return item;
    }
    const item = new vscode.TreeItem(element.label, vscode.TreeItemCollapsibleState.None);
    item.id = `nav:page:${element.route}`;
    item.iconPath = new vscode.ThemeIcon(NAV_ICONS[element.icon] ?? 'link-external');
    item.description = element.shortcut ? 'tab' : undefined;
    item.tooltip = `${element.tooltip}\n\nOpens ${element.route} in the dashboard (browser) until the extension has a view for it.`;
    item.contextValue = 'armadaNavPage';
    item.command = { command: OPEN_DASHBOARD_PAGE_COMMAND, title: `Open ${element.label}`, arguments: [element.route] };
    return item;
  }

  public dispose(): void {
    this._OnDidChange.dispose();
  }
}

/** The route a command argument names: a route string or a page node. */
export function routeFromArg(arg: unknown): string | undefined {
  if (typeof arg === 'string' && arg.startsWith('/')) return arg;
  if (arg && typeof arg === 'object' && (arg as NavPageNode).kind === 'page' && typeof (arg as NavPageNode).route === 'string') return (arg as NavPageNode).route;
  return undefined;
}

function page(item: NavModelItem, shortcut: boolean): NavPageNode {
  return { kind: 'page', route: item.to, label: item.label, tooltip: item.tooltip ?? item.label, icon: item.icon, shortcut };
}
