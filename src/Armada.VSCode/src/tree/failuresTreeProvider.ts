import * as vscode from 'vscode';
import { BlackBoxEntrySummary } from '../api/types';
import { BlackBoxStore } from '../state/blackBoxStore';
import { entryKind, entryLabel, formatAge, isOpen } from '../util/format';

/** Tree view id (package.json contributes.views.explorer). */
export const FAILURES_VIEW_ID = 'armadaFailures';

/** A vessel group. */
export interface VesselNode {
  kind: 'vessel';
  vesselId: string;
  vesselName: string;
  entries: BlackBoxEntrySummary[];
}

/** One black box entry. */
export interface EntryNode {
  kind: 'entry';
  entry: BlackBoxEntrySummary;
}

/** An informational row (errors, empty states). */
export interface MessageNode {
  kind: 'message';
  text: string;
  detail?: string;
  icon: string;
  command?: string;
}

export type FailureNode = VesselNode | EntryNode | MessageNode;

/** Tree data for "Armada Failures": recent entries grouped by vessel. */
export class FailuresTreeProvider implements vscode.TreeDataProvider<FailureNode>, vscode.Disposable {
  private readonly _Store: BlackBoxStore;
  private readonly _OnDidChange = new vscode.EventEmitter<FailureNode | undefined>();
  private readonly _Subscription: { dispose(): void };
  private readonly _Now: () => Date;

  public readonly onDidChangeTreeData: vscode.Event<FailureNode | undefined> = this._OnDidChange.event;

  /**
   * @throws Error when store is missing.
   */
  public constructor(store: BlackBoxStore, now?: () => Date) {
    if (!store) throw new Error('store is required');
    this._Store = store;
    this._Now = now ?? (() => new Date());
    this._Subscription = store.onDidChange(() => this._OnDidChange.fire(undefined));
  }

  /** Badge for the view: open entry count, or undefined when zero. */
  public badge(): vscode.ViewBadge | undefined {
    const count = this._Store.openCount;
    if (!count) return undefined;
    return { value: count, tooltip: `${count} open Armada black box ${count === 1 ? 'entry' : 'entries'}` };
  }

  /** Short description for the view header. */
  public viewDescription(): string | undefined {
    if (this._Store.status !== 'ready') return undefined;
    if (this._Store.filtered) return this._Store.matches.map((m) => m.vessel.Name || m.vessel.Id).filter((v, i, a) => a.indexOf(v) === i).join(', ');
    return 'all vessels';
  }

  public getChildren(element?: FailureNode): FailureNode[] {
    if (!element) return this.rootNodes();
    if (element.kind === 'vessel') return element.entries.map((entry) => ({ kind: 'entry', entry }));
    return [];
  }

  public getTreeItem(element: FailureNode): vscode.TreeItem {
    if (element.kind === 'message') {
      const item = new vscode.TreeItem(element.text, vscode.TreeItemCollapsibleState.None);
      item.iconPath = new vscode.ThemeIcon(element.icon);
      item.tooltip = element.detail ?? element.text;
      if (element.command) item.command = { command: element.command, title: element.text };
      item.contextValue = 'armadaMessage';
      return item;
    }
    if (element.kind === 'vessel') {
      const open = element.entries.filter(isOpen).length;
      const item = new vscode.TreeItem(element.vesselName, vscode.TreeItemCollapsibleState.Expanded);
      item.id = `vessel:${element.vesselId}`;
      item.iconPath = new vscode.ThemeIcon('repo');
      item.description = `${open} open`;
      item.tooltip = `${element.vesselName} (${element.vesselId}): ${element.entries.length} ${element.entries.length === 1 ? 'entry' : 'entries'}, ${open} open`;
      item.contextValue = 'armadaVessel';
      return item;
    }
    const entry = element.entry;
    const open = isOpen(entry);
    const item = new vscode.TreeItem(entryLabel(entry), vscode.TreeItemCollapsibleState.None);
    item.id = `entry:${entry.Id}`;
    const parts = [entryKind(entry), entry.Step, formatAge(entry.CreatedUtc, this._Now())].filter((p) => !!p);
    item.description = parts.join(', ');
    const tooltip = new vscode.MarkdownString(undefined, false);
    tooltip.appendMarkdown(`**${escapeMd(entryLabel(entry))}**\n\n`);
    tooltip.appendMarkdown(`- Entry: \`${entry.Id}\`\n`);
    if (entry.Phase || entry.Step) tooltip.appendMarkdown(`- Where: ${escapeMd([entry.Phase, entry.Step].filter(Boolean).join(' / '))}\n`);
    if (entry.StatusBefore || entry.StatusAfter) tooltip.appendMarkdown(`- Status: ${escapeMd(`${entry.StatusBefore ?? '?'} to ${entry.StatusAfter ?? '?'}`)}\n`);
    if (entry.Triage) tooltip.appendMarkdown(`- Triage: ${escapeMd(entry.Triage)}\n`);
    tooltip.appendMarkdown(`- Resolution: ${escapeMd(entry.Resolution ?? 'Open')}\n`);
    tooltip.appendMarkdown(`- Captured: ${escapeMd(entry.CreatedUtc)}\n`);
    item.tooltip = tooltip;
    item.iconPath = new vscode.ThemeIcon(open ? 'error' : 'pass', new vscode.ThemeColor(open ? 'problemsErrorIcon.foreground' : 'testing.iconPassed'));
    item.contextValue = open ? 'armadaEntryOpen' : 'armadaEntryResolved';
    item.command = { command: 'armada.openEntry', title: 'Open Black Box', arguments: [element] };
    return item;
  }

  public dispose(): void {
    this._Subscription.dispose();
    this._OnDidChange.dispose();
  }

  private rootNodes(): FailureNode[] {
    const store = this._Store;
    if (store.status === 'disconnected') {
      return [{ kind: 'message', text: 'Not connected. Click to connect to Armada.', icon: 'plug', command: 'armada.connect' }];
    }
    if (store.status === 'error') {
      return [{ kind: 'message', text: store.error ?? 'Could not load black box entries.', detail: store.error, icon: 'warning', command: 'armada.refreshFailures' }];
    }
    if (store.status === 'loading' && store.entries.length === 0) {
      return [{ kind: 'message', text: 'Loading black box entries...', icon: 'loading~spin' }];
    }

    const groups = new Map<string, VesselNode>();
    for (const entry of store.entries) {
      const vesselId = entry.VesselId || '(none)';
      let group = groups.get(vesselId);
      if (!group) {
        const vessel = store.vessels.find((v) => v.Id === entry.VesselId);
        group = { kind: 'vessel', vesselId, vesselName: entry.VesselName || vessel?.Name || (entry.VesselId ? entry.VesselId : 'No vessel'), entries: [] };
        groups.set(vesselId, group);
      }
      group.entries.push(entry);
    }

    const nodes: FailureNode[] = [];
    if (store.unmatchedWorkspace) {
      nodes.push({ kind: 'message', text: 'No vessel matches this workspace; showing all vessels.', icon: 'info' });
    }
    if (groups.size === 0) {
      nodes.push({ kind: 'message', text: store.filtered ? 'No open failures for this repository.' : 'No open black box entries.', icon: 'check' });
      return nodes;
    }
    nodes.push(...Array.from(groups.values()).sort((a, b) => a.vesselName.localeCompare(b.vesselName)));
    return nodes;
  }
}

function escapeMd(value: string): string {
  return value.replace(/[\\`*_[\]<>|]/g, (c) => `\\${c}`);
}
