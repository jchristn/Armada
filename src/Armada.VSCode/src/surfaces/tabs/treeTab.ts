/*
 * Any Armada sidebar view as an editor tab: the same tree (its groups, records, descriptions and what clicking each
 * does) laid out as a full-width page, with the view's main actions on top. The page follows the tree as it changes,
 * so a tab of Missions or Needs You stays live. For views whose records load on expand (Delivery, Configuration, ...),
 * opening the tab loads every kind in it.
 */

import * as crypto from 'crypto';
import * as vscode from 'vscode';
import { t } from '../../i18n';
import { DetailAction, DetailListItem, DetailPage, DetailSection, isDetailMessage, renderDetailContent, renderDetailHtml } from '../ops/detailHtml';

/** A view that can open as a tab. */
export interface TabbableView<T> {
  viewId: string;
  title: string;
  provider: vscode.TreeDataProvider<T>;
  /** The view's main actions, shown as buttons. */
  actions?: DetailAction[];
}

function labelOf(item: vscode.TreeItem): string {
  const label = item.label;
  if (typeof label === 'string') return label;
  return label?.label ?? '';
}

function descriptionOf(item: vscode.TreeItem): string {
  return typeof item.description === 'string' ? item.description : '';
}

function linkOf(item: vscode.TreeItem): DetailListItem['link'] {
  const command = item.command;
  if (!command || !command.command.startsWith('armada.')) return undefined;
  return { command: command.command, args: (command.arguments ?? []).filter((a) => a === null || ['string', 'number', 'boolean', 'object'].includes(typeof a)) };
}

/** Builds the page for a view by walking its tree two levels deep. */
export async function treePage<T>(view: TabbableView<T>): Promise<DetailPage> {
  const roots = (await view.provider.getChildren()) ?? [];
  const sections: DetailSection[] = [];
  const loose: DetailListItem[] = [];
  for (const root of roots) {
    const item = await view.provider.getTreeItem(root);
    const collapsible = item.collapsibleState !== undefined && item.collapsibleState !== vscode.TreeItemCollapsibleState.None;
    if (!collapsible) {
      loose.push({ label: labelOf(item), description: descriptionOf(item), link: linkOf(item) });
      continue;
    }
    const children = (await view.provider.getChildren(root)) ?? [];
    const items: DetailListItem[] = [];
    for (const child of children) {
      const childItem = await view.provider.getTreeItem(child);
      items.push({ label: labelOf(childItem), description: descriptionOf(childItem), link: linkOf(childItem) });
    }
    const heading = [labelOf(item), descriptionOf(item)].filter(Boolean).join(' · ');
    const link = linkOf(item);
    if (link) items.unshift({ label: t('Open'), description: labelOf(item), link });
    sections.push({ kind: 'list', title: heading, items, empty: t('Nothing here.') });
  }
  if (loose.length) sections.unshift({ kind: 'list', title: view.title, items: loose });
  if (!sections.length) sections.push({ kind: 'list', title: view.title, items: [], empty: t('Nothing here. Connect to an Admiral, or check back when there is work.') });
  return { title: t(view.title), actions: [...(view.actions ?? []), { label: 'Refresh', command: 'armada.tab.refresh', args: [view.viewId] }], sections };
}

/** Opens and maintains view tabs. */
export class TreeTabManager implements vscode.Disposable {
  private readonly _Views = new Map<string, TabbableView<unknown>>();
  private readonly _Tabs = new Map<string, { panel: vscode.WebviewPanel; html: string; timer?: ReturnType<typeof setTimeout> }>();
  private readonly _Disposables: vscode.Disposable[] = [];

  /** Registers a view so it can open as a tab, and follows its changes. */
  public register<T>(view: TabbableView<T>): void {
    this._Views.set(view.viewId, view as TabbableView<unknown>);
    if (view.provider.onDidChangeTreeData) {
      this._Disposables.push(view.provider.onDidChangeTreeData(() => this.schedule(view.viewId)));
    }
  }

  /** Opens (or reveals) a view's tab. */
  public async open(viewId: string): Promise<void> {
    const view = this._Views.get(viewId);
    if (!view) return;
    const existing = this._Tabs.get(viewId);
    if (existing) {
      existing.panel.reveal(undefined, false);
      await this.render(viewId);
      return;
    }
    const panel = vscode.window.createWebviewPanel('armada.viewTab', `Armada: ${t(view.title)}`, vscode.ViewColumn.Active, { enableScripts: true, enableFindWidget: true, retainContextWhenHidden: true });
    panel.webview.onDidReceiveMessage((message: unknown) => {
      if (isDetailMessage(message)) void vscode.commands.executeCommand(message.command, ...message.args);
    });
    panel.onDidDispose(() => {
      const tab = this._Tabs.get(viewId);
      if (tab?.timer) clearTimeout(tab.timer);
      this._Tabs.delete(viewId);
    });
    this._Tabs.set(viewId, { panel, html: '' });
    await this.render(viewId);
  }

  /** Re-renders a view's tab now. */
  public async render(viewId: string): Promise<void> {
    const view = this._Views.get(viewId);
    const tab = this._Tabs.get(viewId);
    if (!view || !tab) return;
    const page = await treePage(view);
    const body = renderDetailContent(page);
    if (!tab.html) tab.panel.webview.html = renderDetailHtml(page, crypto.randomBytes(16).toString('base64'));
    else if (body !== tab.html) void tab.panel.webview.postMessage({ type: 'update', html: body });
    tab.html = body;
  }

  public dispose(): void {
    for (const d of this._Disposables) d.dispose();
    for (const tab of this._Tabs.values()) tab.panel.dispose();
  }

  private schedule(viewId: string): void {
    const tab = this._Tabs.get(viewId);
    if (!tab || tab.timer) return;
    tab.timer = setTimeout(() => {
      tab.timer = undefined;
      void this.render(viewId);
    }, 300);
  }
}
