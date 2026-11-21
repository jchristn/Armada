/*
 * The resource views: one tree per area (Backlog, Fleet Actions, Delivery, Configuration, Activity, System), each
 * listing its kinds of record; expanding a kind loads its records. A record opens as a page drawn with the VS Code
 * theme (its fields, long text and structured values, and its actions), and can be created, edited and deleted
 * through native forms with confirmations.
 */

import * as crypto from 'crypto';
import * as vscode from 'vscode';
import { t } from '../../i18n';
import { describeError } from '../../transport/errors';
import { DetailAction, DetailPage, DetailSection, escapeHtml, isDetailMessage, renderDetailContent, renderDetailHtml } from '../ops/detailHtml';
import { statusTone } from '../ops/detailPages';
import { showForm } from '../ops/formPanel';
import { humanize } from '../ops/presentation';
import { applyEdits, createValues, editFields, fieldLabel, isLongText, recordId, recordStatus, recordTitle, ResourceRecord, ResourceSpec, rowsOf } from './resourceSpec';

/** A node in a resource tree. */
export type ResourceNode =
  | { kind: 'type'; spec: ResourceSpec }
  | { kind: 'record'; spec: ResourceSpec; record: ResourceRecord }
  | { kind: 'message'; spec: ResourceSpec; label: string; icon: string };

interface Loaded {
  state: 'loading' | 'ready' | 'error';
  records: ResourceRecord[];
  error?: string;
}

/** Records per kind, loaded on demand and kept until refreshed. */
export class ResourceStore {
  private readonly _Loaded = new Map<string, Loaded>();
  private readonly _Listeners = new Set<(key: string) => void>();
  private readonly _IsConnected: () => boolean;

  /**
   * @throws Error when isConnected is missing.
   */
  public constructor(isConnected: () => boolean) {
    if (!isConnected) throw new Error('isConnected is required');
    this._IsConnected = isConnected;
  }

  public onDidChange(listener: (key: string) => void): { dispose(): void } {
    this._Listeners.add(listener);
    return { dispose: () => this._Listeners.delete(listener) };
  }

  /** The loaded state of a kind, if it was ever loaded. */
  public get(spec: ResourceSpec): Loaded | undefined {
    return this._Loaded.get(spec.key);
  }

  /** Every kind that was loaded. */
  public loadedKeys(): string[] {
    return Array.from(this._Loaded.keys());
  }

  /** Loads (or reloads) a kind. Never throws. */
  public async load(spec: ResourceSpec): Promise<void> {
    if (!this._IsConnected()) {
      this._Loaded.delete(spec.key);
      this.fire(spec.key);
      return;
    }
    const previous = this._Loaded.get(spec.key);
    if (!previous) {
      this._Loaded.set(spec.key, { state: 'loading', records: [] });
      this.fire(spec.key);
    }
    try {
      const records = await spec.list();
      this._Loaded.set(spec.key, { state: 'ready', records });
    } catch (err) {
      this._Loaded.set(spec.key, { state: 'error', records: previous?.records ?? [], error: describeError(err) });
    }
    this.fire(spec.key);
  }

  /** Forgets everything (on disconnect). */
  public clear(): void {
    const keys = this.loadedKeys();
    this._Loaded.clear();
    for (const key of keys) this.fire(key);
  }

  private fire(key: string): void {
    for (const listener of this._Listeners) listener(key);
  }
}

/** One resource tree. */
export class ResourceTreeProvider implements vscode.TreeDataProvider<ResourceNode>, vscode.Disposable {
  private readonly _Specs: ResourceSpec[];
  private readonly _Store: ResourceStore;
  private readonly _IsConnected: () => boolean;
  private readonly _OnDidChange = new vscode.EventEmitter<ResourceNode | undefined>();
  private readonly _Subscription: { dispose(): void };
  private readonly _TypeNodes: Map<string, ResourceNode>;

  public readonly onDidChangeTreeData: vscode.Event<ResourceNode | undefined> = this._OnDidChange.event;

  /**
   * @throws Error when specs or store is missing.
   */
  public constructor(specs: ResourceSpec[], store: ResourceStore, isConnected: () => boolean) {
    if (!specs || !store) throw new Error('specs and store are required');
    this._Specs = specs;
    this._Store = store;
    this._IsConnected = isConnected;
    this._TypeNodes = new Map(specs.map((spec) => [spec.key, { kind: 'type', spec } as ResourceNode]));
    this._Subscription = store.onDidChange((key) => {
      const node = this._TypeNodes.get(key);
      if (node) this._OnDidChange.fire(node);
    });
  }

  public refresh(): void {
    this._OnDidChange.fire(undefined);
  }

  public getChildren(element?: ResourceNode): ResourceNode[] {
    if (!this._IsConnected()) return [];
    if (!element) return this._Specs.map((spec) => this._TypeNodes.get(spec.key) as ResourceNode);
    if (element.kind !== 'type') return [];
    const loaded = this._Store.get(element.spec);
    if (!loaded) {
      void this._Store.load(element.spec);
      return [{ kind: 'message', spec: element.spec, label: 'Loading...', icon: 'loading~spin' }];
    }
    if (loaded.state === 'loading') return [{ kind: 'message', spec: element.spec, label: 'Loading...', icon: 'loading~spin' }];
    const rows: ResourceNode[] = loaded.records.map((record) => ({ kind: 'record', spec: element.spec, record }));
    if (loaded.state === 'error') rows.unshift({ kind: 'message', spec: element.spec, label: `Could not load: ${loaded.error}`, icon: 'error' });
    return rows.length ? rows : [{ kind: 'message', spec: element.spec, label: `No ${element.spec.plural.toLowerCase()}`, icon: 'info' }];
  }

  public getTreeItem(node: ResourceNode): vscode.TreeItem {
    if (node.kind === 'type') {
      const loaded = this._Store.get(node.spec);
      const item = new vscode.TreeItem(t(node.spec.plural), vscode.TreeItemCollapsibleState.Collapsed);
      item.id = `resource:type:${node.spec.key}`;
      item.iconPath = new vscode.ThemeIcon(node.spec.icon);
      item.description = loaded?.state === 'ready' ? String(loaded.records.length) : undefined;
      item.contextValue = `armadaResourceType${(node.spec.create && node.spec.createFields) || node.spec.createCommand ? ' armadaResourceType.creatable' : ''}`;
      return item;
    }
    if (node.kind === 'message') {
      const item = new vscode.TreeItem(node.label, vscode.TreeItemCollapsibleState.None);
      item.id = `resource:message:${node.spec.key}:${node.label}`;
      item.iconPath = new vscode.ThemeIcon(node.icon);
      return item;
    }
    const spec = node.spec;
    const id = recordId(spec, node.record);
    const item = new vscode.TreeItem(recordTitle(node.record), vscode.TreeItemCollapsibleState.None);
    item.id = `resource:${spec.key}:${id}`;
    const status = recordStatus(node.record);
    const tone = status ? statusTone(status) : 'muted';
    item.iconPath = new vscode.ThemeIcon(spec.icon, tone === 'error' ? new vscode.ThemeColor('testing.iconFailed') : tone === 'ok' ? new vscode.ThemeColor('testing.iconPassed') : tone === 'warn' ? new vscode.ThemeColor('list.warningForeground') : undefined);
    item.description = spec.describe?.(node.record) || humanize(status);
    const tooltip = new vscode.MarkdownString(`**${recordTitle(node.record).replace(/[*_`[\]]/g, '\\$&')}**\n\n${spec.label} · \`${id}\``);
    item.tooltip = tooltip;
    const flags = [spec.update ? 'editable' : '', spec.remove ? 'deletable' : '', spec.actions?.length ? 'actionable' : ''].filter(Boolean).map((f) => ` armadaResource.${f}`).join('');
    item.contextValue = `armadaResource armadaResource.${spec.key}${flags}`;
    item.command = { command: 'armada.resource.open', title: 'Open', arguments: [spec.key, id] };
    return item;
  }

  public dispose(): void {
    this._Subscription.dispose();
    this._OnDidChange.dispose();
  }
}

/** Builds a record's page. Pure, for tests. */
export function resourcePage(spec: ResourceSpec, record: ResourceRecord): DetailPage {
  const id = recordId(spec, record);
  const actions: DetailAction[] = [];
  if (spec.update) actions.push({ label: 'Edit', command: 'armada.resource.edit', args: [spec.key, id], primary: true });
  (spec.actions ?? []).forEach((action, index) => {
    if (!action.when || action.when(record)) actions.push({ label: action.label, command: 'armada.resource.action', args: [spec.key, id, index] });
  });
  if (spec.remove) actions.push({ label: 'Delete', command: 'armada.resource.delete', args: [spec.key, id] });
  if (spec.route) actions.push({ label: 'Open in Browser', command: 'armada.openInBrowser', args: [spec.route(record)] });

  const rows: Array<{ label: string; value: string; code?: boolean }> = [];
  const sections: DetailSection[] = [];
  for (const [key, value] of Object.entries(record)) {
    if (value === null || value === undefined || value === '') continue;
    if (typeof value === 'string') {
      if (isLongText(key, value)) sections.push({ kind: 'pre', title: fieldLabel(key), text: value });
      else rows.push({ label: fieldLabel(key), value: /Utc$/.test(key) ? new Date(value).toLocaleString() : value, code: /Id$|^id$|Hash$|Branch|Name$/.test(key) && !/displayName/i.test(key) });
    } else if (typeof value === 'number' || typeof value === 'boolean') {
      rows.push({ label: fieldLabel(key), value: typeof value === 'boolean' ? (value ? 'Yes' : 'No') : String(value) });
    } else if (Array.isArray(value) && value.every((v) => typeof v === 'string' || typeof v === 'number')) {
      if (value.length) rows.push({ label: fieldLabel(key), value: value.join(', ') });
    } else if (typeof value === 'object' && (!Array.isArray(value) || value.length)) {
      sections.push({ kind: 'pre', title: fieldLabel(key), text: JSON.stringify(value, null, 2) });
    }
  }
  const status = recordStatus(record);
  return {
    title: recordTitle(record),
    status: status ? { label: humanize(status), tone: statusTone(status) } : undefined,
    subtitle: id,
    actions,
    sections: [{ kind: 'fields', rows }, ...sections],
  };
}

/** Record pages, commands and forms. */
export class ResourceController implements vscode.Disposable {
  private readonly _Specs: Map<string, ResourceSpec>;
  private readonly _Store: ResourceStore;
  private readonly _Pages = new Map<string, { panel: vscode.WebviewPanel; html: string }>();
  private readonly _Disposables: vscode.Disposable[] = [];
  private readonly _OnChanged: ((key: string) => void) | undefined;

  /**
   * @throws Error when specs or store is missing.
   */
  public constructor(specs: ResourceSpec[], store: ResourceStore, requireConnected: () => Promise<boolean>, onChanged?: (key: string) => void) {
    if (!specs || !store) throw new Error('specs and store are required');
    this._Specs = new Map(specs.map((s) => [s.key, s]));
    this._Store = store;
    this._OnChanged = onChanged;
    const register = (command: string, fn: (...args: unknown[]) => Promise<unknown>): void => {
      this._Disposables.push(vscode.commands.registerCommand(command, async (...args: unknown[]) => {
        try {
          return await fn(...args);
        } catch (err) {
          void vscode.window.showErrorMessage(`Armada: ${describeError(err)}`);
          return undefined;
        }
      }));
    };
    register('armada.resource.open', async (a, b) => {
      const target = this.target(a, b);
      if (target && (await requireConnected())) await this.open(target.spec, target.id);
    });
    register('armada.showResult', async (title, data) => {
      if (typeof title === 'string') await this.showResult(title, data);
    });
    register('armada.resource.refresh', async (a) => {
      const spec = this.specOf(a);
      if (spec) await store.load(spec);
      else for (const key of store.loadedKeys()) await store.load(this._Specs.get(key) as ResourceSpec);
    });
    register('armada.resource.create', async (a) => {
      const spec = this.specOf(a);
      if (spec) await this.create(spec);
    });
    register('armada.resource.edit', async (a, b) => {
      const target = this.target(a, b);
      if (target) await this.edit(target.spec, target.id);
    });
    register('armada.resource.delete', async (a, b) => {
      // From a tree with several records selected, VS Code passes the clicked node and the whole selection.
      if (Array.isArray(b) && b.length > 1) {
        const nodes = (b as ResourceNode[]).filter((n): n is Extract<ResourceNode, { kind: 'record' }> => n?.kind === 'record' && !!n.spec.remove);
        if (!nodes.length) return;
        if ((await vscode.window.showWarningMessage(`Delete ${nodes.length} records? This cannot be undone.`, { modal: true }, 'Delete')) !== 'Delete') return;
        const failures: string[] = [];
        for (const node of nodes) {
          try {
            await node.spec.remove?.(recordId(node.spec, node.record));
          } catch (err) {
            failures.push(`${recordTitle(node.record)}: ${describeError(err)}`);
          }
        }
        for (const spec of new Set(nodes.map((n) => n.spec))) await this._Store.load(spec);
        if (failures.length) void vscode.window.showErrorMessage(`Armada: ${failures.length} could not be deleted. ${failures.slice(0, 3).join('; ')}`);
        else void vscode.window.showInformationMessage(`Armada: deleted ${nodes.length} records.`);
        return;
      }
      const target = this.target(a, b);
      if (target) await this.remove(target.spec, target.id);
    });
    register('armada.resource.action', async (a, b, c) => {
      const target = this.target(a, b);
      if (!target) return;
      const record = await this.read(target.spec, target.id);
      const available = (target.spec.actions ?? []).map((action, index) => ({ action, index })).filter(({ action }) => !action.when || action.when(record));
      let index = typeof c === 'number' ? c : undefined;
      if (index === undefined) {
        const picked = await vscode.window.showQuickPick(available.map(({ action, index: i }) => ({ label: action.label, index: i })), { title: `${target.spec.label}: ${recordTitle(record)}` });
        if (!picked) return;
        index = picked.index;
      }
      const action = target.spec.actions?.[index];
      if (!action) return;
      if (action.confirm && (await vscode.window.showWarningMessage(action.confirm(record), { modal: true }, action.label)) !== action.label) return;
      const result = await action.run(record);
      if (action.showResult && result !== undefined) {
        await this.showResult(`${action.showResult}: ${recordTitle(record)}`, result);
        return;
      }
      await this.afterChange(target.spec, target.id);
    });
  }

  /** Reloads every open page and every loaded kind (connection changes, poll ticks). */
  public async refreshAll(): Promise<void> {
    for (const key of this._Store.loadedKeys()) {
      const spec = this._Specs.get(key);
      if (spec) await this._Store.load(spec);
    }
  }

  public dispose(): void {
    for (const d of this._Disposables) d.dispose();
    for (const { panel } of this._Pages.values()) panel.dispose();
  }

  private specOf(arg: unknown): ResourceSpec | undefined {
    if (typeof arg === 'string') return this._Specs.get(arg);
    if (arg && typeof arg === 'object' && 'spec' in arg) return (arg as { spec: ResourceSpec }).spec;
    return undefined;
  }

  private target(a: unknown, b: unknown): { spec: ResourceSpec; id: string } | undefined {
    if (typeof a === 'string' && typeof b === 'string') {
      const spec = this._Specs.get(a);
      return spec ? { spec, id: b } : undefined;
    }
    if (a && typeof a === 'object' && (a as ResourceNode).kind === 'record') {
      const node = a as Extract<ResourceNode, { kind: 'record' }>;
      return { spec: node.spec, id: recordId(node.spec, node.record) };
    }
    return undefined;
  }

  private async read(spec: ResourceSpec, id: string): Promise<ResourceRecord> {
    if (spec.get) return spec.get(id);
    let found = this._Store.get(spec)?.records.find((r) => recordId(spec, r) === id);
    if (!found) {
      await this._Store.load(spec);
      found = this._Store.get(spec)?.records.find((r) => recordId(spec, r) === id);
    }
    if (!found) throw new Error(`${spec.label} ${id} was not found.`);
    return found;
  }

  private async open(spec: ResourceSpec, id: string): Promise<void> {
    if (spec.open) {
      await spec.open(await this.read(spec, id));
      return;
    }
    const key = `${spec.key}:${id}`;
    let entry = this._Pages.get(key);
    if (entry) entry.panel.reveal(undefined, false);
    else {
      const panel = vscode.window.createWebviewPanel('armada.resource', `${spec.label}`, vscode.ViewColumn.Active, { enableScripts: true, enableFindWidget: true });
      panel.webview.onDidReceiveMessage((message: unknown) => {
        if (isDetailMessage(message)) void vscode.commands.executeCommand(message.command, ...message.args);
      });
      panel.onDidDispose(() => this._Pages.delete(key));
      entry = { panel, html: '' };
      this._Pages.set(key, entry);
    }
    await this.render(spec, id);
  }

  private async render(spec: ResourceSpec, id: string): Promise<void> {
    const entry = this._Pages.get(`${spec.key}:${id}`);
    if (!entry) return;
    try {
      const page = resourcePage(spec, await this.read(spec, id));
      entry.panel.title = page.title.length > 40 ? `${page.title.slice(0, 39)}…` : page.title;
      const body = renderDetailContent(page);
      if (!entry.html) entry.panel.webview.html = renderDetailHtml(page, crypto.randomBytes(16).toString('base64'));
      else if (body !== entry.html) void entry.panel.webview.postMessage({ type: 'update', html: body });
      entry.html = body;
    } catch (err) {
      entry.panel.webview.html = `<!DOCTYPE html><html><body style="font-family:var(--vscode-font-family);color:var(--vscode-errorForeground);padding:16px">Could not load this ${escapeHtml(spec.label.toLowerCase())}. ${escapeHtml(describeError(err))}</body></html>`;
    }
  }

  /** Opens an action's result (an object, or a list of records) as a read-only page. */
  private async showResult(title: string, result: unknown): Promise<void> {
    const rows = Array.isArray(result) ? result : rowsOf(result).length ? rowsOf(result) : undefined;
    const record: ResourceRecord = rows ? { title, count: rows.length, items: rows } : { title, ...(result && typeof result === 'object' ? (result as ResourceRecord) : { value: String(result) }) };
    const page = resourcePage({ key: 'result', label: title, plural: title, icon: 'info', view: 'system', list: async () => [] }, record);
    page.title = title;
    page.subtitle = undefined;
    page.actions = [];
    const panel = vscode.window.createWebviewPanel('armada.result', title.length > 40 ? `${title.slice(0, 39)}…` : title, vscode.ViewColumn.Active, { enableScripts: true, enableFindWidget: true });
    panel.webview.onDidReceiveMessage((message: unknown) => {
      if (isDetailMessage(message)) void vscode.commands.executeCommand(message.command, ...message.args);
    });
    panel.webview.html = renderDetailHtml(page, crypto.randomBytes(16).toString('base64'));
  }

  private async afterChange(spec: ResourceSpec, id: string | undefined): Promise<void> {
    this._OnChanged?.(spec.key);
    await this._Store.load(spec);
    if (id) await this.render(spec, id);
  }

  private async create(spec: ResourceSpec): Promise<void> {
    if (spec.createCommand) {
      await vscode.commands.executeCommand(spec.createCommand);
      await this._Store.load(spec);
      return;
    }
    if (!spec.create || !spec.createFields) return;
    const created = await showForm<unknown>({ title: `New ${spec.label}`, submitLabel: 'Create', fields: spec.createFields }, async (values) => {
      try {
        return { result: await spec.create?.(createValues(spec.createFields ?? [], values)) };
      } catch (err) {
        return { error: describeError(err) };
      }
    });
    if (created === undefined) return;
    void vscode.window.showInformationMessage(`Armada: created the ${spec.label.toLowerCase()}.`);
    this._OnChanged?.(spec.key);
    await this._Store.load(spec);
    const id = created && typeof created === 'object' ? recordId(spec, created as ResourceRecord) : '';
    if (id) await this.open(spec, id);
  }

  private async edit(spec: ResourceSpec, id: string): Promise<void> {
    if (!spec.update) return;
    const record = await this.read(spec, id);
    const fields = editFields(spec, record);
    const saved = await showForm<boolean>({ title: `Edit ${spec.label}: ${recordTitle(record)}`, submitLabel: 'Save', fields }, async (values) => {
      try {
        await spec.update?.(id, applyEdits(record, fields, values));
        return { result: true };
      } catch (err) {
        return { error: describeError(err) };
      }
    });
    if (!saved) return;
    void vscode.window.showInformationMessage(`Armada: saved the ${spec.label.toLowerCase()}.`);
    await this.afterChange(spec, id);
  }

  private async remove(spec: ResourceSpec, id: string): Promise<void> {
    if (!spec.remove) return;
    const record = await this.read(spec, id).catch(() => ({} as ResourceRecord));
    const title = recordTitle(record) || id;
    if ((await vscode.window.showWarningMessage(`Delete the ${spec.label.toLowerCase()} "${title}"? This cannot be undone.`, { modal: true }, 'Delete')) !== 'Delete') return;
    await spec.remove(id);
    void vscode.window.showInformationMessage(`Armada: deleted "${title}".`);
    this._Pages.get(`${spec.key}:${id}`)?.panel.dispose();
    this._OnChanged?.(spec.key);
    await this._Store.load(spec);
  }
}

