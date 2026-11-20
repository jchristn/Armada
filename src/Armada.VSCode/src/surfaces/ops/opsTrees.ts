/*
 * The native Armada sidebar views over OpsStore: Needs You, Missions, Voyages, Captains, Fleet and Merge Queue. Each
 * is a VS Code tree with status icons, rich tooltips, inline actions and context menus (package.json menus match on
 * the contextValue set here), refreshed live as the store reloads.
 */

import * as vscode from 'vscode';
import type { Captain, Fleet, InboxItem, MergeEntry, MissionSummary, Vessel, VoyageProgress } from '@dashboard/types/models';
import { t } from '../../i18n';
import { formatAge } from '../../util/format';
import { ACTIVE_MISSION_STATUSES, FINISHED_MISSION_STATUSES, OpsDomain, OpsStore, QUEUED_MISSION_STATUSES, REVIEW_MISSION_STATUSES } from './opsStore';
import { captainLook, elapsed, humanize, inboxLook, mergeLook, missionLook, StatusLook, voyageLook } from './presentation';

/** View ids (package.json contributes.views.armada). */
export const OPS_VIEW_IDS = {
  needsYou: 'armadaNeedsYou',
  missions: 'armadaMissions',
  voyages: 'armadaVoyages',
  captains: 'armadaCaptains',
  fleet: 'armadaFleet',
  merge: 'armadaMergeQueue',
} as const;

/** Commands the tree items run on click. */
export const OPEN_MISSION_COMMAND = 'armada.mission.open';
export const OPEN_VOYAGE_COMMAND = 'armada.voyage.open';
export const OPEN_CAPTAIN_COMMAND = 'armada.captain.open';
export const OPEN_INBOX_ITEM_COMMAND = 'armada.inbox.open';
export const OPEN_MERGE_ENTRY_COMMAND = 'armada.merge.open';

/** How many finished missions and voyages the Recent groups show. */
const RECENT_LIMIT = 40;

/** A node in any of the operational trees. */
export type OpsNode =
  | { kind: 'group'; key: string; label: string; icon?: string; children: OpsNode[]; expanded: boolean; contextValue?: string }
  | { kind: 'mission'; mission: MissionSummary; parentKey: string }
  | { kind: 'voyage'; progress: VoyageProgress }
  | { kind: 'captain'; captain: Captain }
  | { kind: 'fleet'; fleet: Fleet | null; vessels: Vessel[] }
  | { kind: 'vessel'; vessel: Vessel }
  | { kind: 'merge'; entry: MergeEntry }
  | { kind: 'inbox'; item: InboxItem; index: number }
  | { kind: 'message'; key: string; label: string; icon: string; tooltip?: string };

/** One operational tree. */
export abstract class OpsTreeProvider implements vscode.TreeDataProvider<OpsNode>, vscode.Disposable {
  protected readonly store: OpsStore;
  private readonly _Domains: OpsDomain[];
  private readonly _OnDidChange = new vscode.EventEmitter<OpsNode | undefined>();
  private readonly _Subscription: { dispose(): void };
  protected readonly now: () => Date;

  public readonly onDidChangeTreeData: vscode.Event<OpsNode | undefined> = this._OnDidChange.event;

  /**
   * @throws Error when store is missing.
   */
  protected constructor(store: OpsStore, domains: OpsDomain[], now?: () => Date) {
    if (!store) throw new Error('store is required');
    this.store = store;
    this._Domains = domains;
    this.now = now ?? (() => new Date());
    this._Subscription = store.onDidChange((domain) => {
      if (this._Domains.includes(domain)) this._OnDidChange.fire(undefined);
    });
  }

  /** Rebuilds the tree. */
  public refresh(): void {
    this._OnDidChange.fire(undefined);
  }

  /** The view badge, or undefined. */
  public badge(): vscode.ViewBadge | undefined {
    return undefined;
  }

  public getChildren(element?: OpsNode): OpsNode[] {
    if (element) return this.childrenOf(element);
    const primary = this._Domains[0];
    const state = this.store.state(primary);
    if (state === 'disconnected') return [];
    if (state === 'loading') return [{ kind: 'message', key: 'loading', label: 'Loading...', icon: 'loading~spin' }];
    if (state === 'error') return [{ kind: 'message', key: 'error', label: `Could not load: ${this.store.error(primary) ?? 'error'}`, icon: 'error', tooltip: this.store.error(primary) }];
    const roots = this.roots();
    return roots.length ? roots : [this.empty()];
  }

  public getTreeItem(node: OpsNode): vscode.TreeItem {
    switch (node.kind) {
      case 'group': {
        const item = new vscode.TreeItem(node.label, node.expanded ? vscode.TreeItemCollapsibleState.Expanded : vscode.TreeItemCollapsibleState.Collapsed);
        item.id = `${this.idPrefix()}:group:${node.key}`;
        if (node.icon) item.iconPath = new vscode.ThemeIcon(node.icon);
        item.contextValue = node.contextValue ?? 'armadaGroup';
        return item;
      }
      case 'mission': return missionItem(node.mission, `${this.idPrefix()}:${node.parentKey}`, this.store, this.now());
      case 'voyage': return voyageItem(node.progress, this.idPrefix(), this.now());
      case 'captain': return captainItem(node.captain, this.idPrefix(), this.store);
      case 'fleet': {
        const item = new vscode.TreeItem(node.fleet?.name ?? 'No Fleet', vscode.TreeItemCollapsibleState.Expanded);
        item.id = `${this.idPrefix()}:fleet:${node.fleet?.id ?? 'none'}`;
        item.iconPath = new vscode.ThemeIcon('folder-library');
        item.description = `${node.vessels.length} ${node.vessels.length === 1 ? 'vessel' : 'vessels'}`;
        item.tooltip = node.fleet?.description ?? undefined;
        item.contextValue = node.fleet ? 'armadaFleet armadaFleet.real' : 'armadaFleet';
        return item;
      }
      case 'vessel': return vesselItem(node.vessel, this.idPrefix(), this.store);
      case 'merge': return mergeItem(node.entry, this.idPrefix(), this.store, this.now());
      case 'inbox': return inboxItem(node.item, node.index, this.idPrefix(), this.now());
      case 'message': {
        const item = new vscode.TreeItem(t(node.label), vscode.TreeItemCollapsibleState.None);
        item.id = `${this.idPrefix()}:message:${node.key}`;
        item.iconPath = new vscode.ThemeIcon(node.icon);
        item.tooltip = node.tooltip;
        return item;
      }
    }
  }

  public dispose(): void {
    this._Subscription.dispose();
    this._OnDidChange.dispose();
  }

  protected abstract idPrefix(): string;
  protected abstract roots(): OpsNode[];
  protected abstract empty(): OpsNode;

  protected childrenOf(node: OpsNode): OpsNode[] {
    if (node.kind === 'group') return node.children;
    return [];
  }
}

/** Needs You: everything waiting on a person (reviews, failures, permission requests, alerts). */
export class NeedsYouTreeProvider extends OpsTreeProvider {
  public constructor(store: OpsStore, now?: () => Date) {
    super(store, ['inbox'], now);
  }

  public badge(): vscode.ViewBadge | undefined {
    const count = this.store.inbox.length;
    return count ? { value: count, tooltip: `${count} ${count === 1 ? 'item needs' : 'items need'} you` } : undefined;
  }

  protected idPrefix(): string {
    return 'needs';
  }

  protected roots(): OpsNode[] {
    return this.store.inbox.map((item, index) => ({ kind: 'inbox', item, index }));
  }

  protected empty(): OpsNode {
    return { kind: 'message', key: 'empty', label: 'Nothing needs you right now', icon: 'check-all' };
  }
}

/** Missions: Needs Review, Active, Queued and Recent. */
export class MissionsTreeProvider extends OpsTreeProvider {
  public constructor(store: OpsStore, now?: () => Date) {
    super(store, ['missions', 'fleet', 'captains'], now);
  }

  public badge(): vscode.ViewBadge | undefined {
    const count = this.store.missionsIn(ACTIVE_MISSION_STATUSES).length;
    return count ? { value: count, tooltip: `${count} active ${count === 1 ? 'mission' : 'missions'}` } : undefined;
  }

  protected idPrefix(): string {
    return 'missions';
  }

  protected roots(): OpsNode[] {
    const group = (key: string, label: string, icon: string, missions: MissionSummary[], expanded: boolean): OpsNode | undefined =>
      missions.length ? { kind: 'group', key, label: `${t(label)} (${missions.length})`, icon, expanded, children: missions.map((mission) => ({ kind: 'mission', mission, parentKey: key })) } : undefined;
    return [
      group('review', 'Needs Review', 'eye', this.store.missionsIn(REVIEW_MISSION_STATUSES), true),
      group('active', 'Active', 'pulse', this.store.missionsIn(ACTIVE_MISSION_STATUSES), true),
      group('queued', 'Queued', 'list-ordered', this.store.missionsIn(QUEUED_MISSION_STATUSES), true),
      group('recent', 'Recent', 'history', this.store.missionsIn(FINISHED_MISSION_STATUSES).slice(0, RECENT_LIMIT), false),
    ].filter((g): g is OpsNode => g !== undefined);
  }

  protected empty(): OpsNode {
    return { kind: 'message', key: 'empty', label: 'No missions yet. Dispatch one with the + button.', icon: 'rocket' };
  }
}

/** Voyages: active and recent, each with its missions. */
export class VoyagesTreeProvider extends OpsTreeProvider {
  public constructor(store: OpsStore, now?: () => Date) {
    super(store, ['voyages', 'missions'], now);
  }

  protected idPrefix(): string {
    return 'voyages';
  }

  protected roots(): OpsNode[] {
    const active = this.store.voyages.filter((v) => v.voyage.status === 'Open' || v.voyage.status === 'InProgress');
    const recent = this.store.voyages.filter((v) => v.voyage.status !== 'Open' && v.voyage.status !== 'InProgress').slice(0, RECENT_LIMIT);
    const roots: OpsNode[] = active.map((progress) => ({ kind: 'voyage', progress }));
    if (recent.length) roots.push({ kind: 'group', key: 'recent', label: `Recent (${recent.length})`, icon: 'history', expanded: active.length === 0, children: recent.map((progress) => ({ kind: 'voyage', progress })) });
    return roots;
  }

  protected empty(): OpsNode {
    return { kind: 'message', key: 'empty', label: 'No voyages yet', icon: 'rocket' };
  }

  protected childrenOf(node: OpsNode): OpsNode[] {
    if (node.kind !== 'voyage') return super.childrenOf(node);
    const missions = this.store.missionsOfVoyage(node.progress.voyage.id);
    if (!missions.length) return [{ kind: 'message', key: `none:${node.progress.voyage.id}`, label: `${node.progress.totalMissions} missions (open the voyage for details)`, icon: 'info' }];
    return missions.map((mission) => ({ kind: 'mission', mission, parentKey: `voyage:${node.progress.voyage.id}` }));
  }
}

/** Captains, with the missions each is working. */
export class CaptainsTreeProvider extends OpsTreeProvider {
  public constructor(store: OpsStore, now?: () => Date) {
    super(store, ['captains', 'missions'], now);
  }

  protected idPrefix(): string {
    return 'captains';
  }

  protected roots(): OpsNode[] {
    const order = (c: Captain): number => (c.state === 'Working' ? 0 : c.state === 'Idle' ? 2 : 1);
    return [...this.store.captains].sort((a, b) => order(a) - order(b) || a.name.localeCompare(b.name)).map((captain) => ({ kind: 'captain', captain }));
  }

  protected empty(): OpsNode {
    return { kind: 'message', key: 'empty', label: 'No captains configured', icon: 'person-add' };
  }

  protected childrenOf(node: OpsNode): OpsNode[] {
    if (node.kind !== 'captain') return super.childrenOf(node);
    const ids = node.captain.activeMissionIds ?? [];
    const missions = this.store.missions.filter((m) => ids.includes(m.id) || (m.captainId === node.captain.id && ACTIVE_MISSION_STATUSES.includes(m.status)));
    return missions.map((mission) => ({ kind: 'mission', mission, parentKey: `captain:${node.captain.id}` }));
  }
}

/** Fleets and their vessels. */
export class FleetTreeProvider extends OpsTreeProvider {
  public constructor(store: OpsStore, now?: () => Date) {
    super(store, ['fleet'], now);
  }

  protected idPrefix(): string {
    return 'fleet';
  }

  protected roots(): OpsNode[] {
    const nodes: OpsNode[] = this.store.fleets.map((fleet) => ({ kind: 'fleet', fleet, vessels: this.store.vessels.filter((v) => v.fleetId === fleet.id) }));
    const loose = this.store.vessels.filter((v) => !v.fleetId || !this.store.fleets.some((f) => f.id === v.fleetId));
    if (loose.length) nodes.push({ kind: 'fleet', fleet: null, vessels: loose });
    return nodes.filter((n) => n.kind !== 'fleet' || n.vessels.length > 0 || this.store.fleets.length < 10);
  }

  protected empty(): OpsNode {
    return { kind: 'message', key: 'empty', label: 'No vessels yet', icon: 'repo' };
  }

  protected childrenOf(node: OpsNode): OpsNode[] {
    if (node.kind !== 'fleet') return super.childrenOf(node);
    return node.vessels.map((vessel) => ({ kind: 'vessel', vessel }));
  }
}

/** The merge queue. */
export class MergeQueueTreeProvider extends OpsTreeProvider {
  public constructor(store: OpsStore, now?: () => Date) {
    super(store, ['merge', 'fleet'], now);
  }

  public badge(): vscode.ViewBadge | undefined {
    const count = this.store.mergeQueue.filter((e) => e.status === 'Queued' || e.status === 'Testing').length;
    return count ? { value: count, tooltip: `${count} queued for landing` } : undefined;
  }

  protected idPrefix(): string {
    return 'merge';
  }

  protected roots(): OpsNode[] {
    const pending = this.store.mergeQueue.filter((e) => e.status === 'Queued' || e.status === 'Testing' || e.status === 'Passed');
    const done = this.store.mergeQueue.filter((e) => !pending.includes(e)).slice(0, RECENT_LIMIT);
    const roots: OpsNode[] = pending.map((entry) => ({ kind: 'merge', entry }));
    if (done.length) roots.push({ kind: 'group', key: 'done', label: `Recent (${done.length})`, icon: 'history', expanded: pending.length === 0, children: done.map((entry) => ({ kind: 'merge', entry })) });
    return roots;
  }

  protected empty(): OpsNode {
    return { kind: 'message', key: 'empty', label: 'The merge queue is empty', icon: 'git-merge' };
  }
}

function themeIcon(look: StatusLook): vscode.ThemeIcon {
  return look.color ? new vscode.ThemeIcon(look.icon, new vscode.ThemeColor(look.color)) : new vscode.ThemeIcon(look.icon);
}

function md(lines: Array<string | undefined | false | null>): vscode.MarkdownString {
  const text = new vscode.MarkdownString(lines.filter(Boolean).join('\n\n'));
  text.supportThemeIcons = true;
  return text;
}

function code(value: string | null | undefined): string {
  return value ? `\`${value.replace(/`/g, '')}\`` : '';
}

/** The tree item for a mission. contextValue is "armadaMission" plus the status, for menu `when` clauses. */
export function missionItem(mission: MissionSummary, idPrefix: string, store: OpsStore, now: Date): vscode.TreeItem {
  const item = new vscode.TreeItem(mission.title || mission.id, vscode.TreeItemCollapsibleState.None);
  item.id = `${idPrefix}:mission:${mission.id}`;
  item.iconPath = themeIcon(missionLook(mission.status));
  const running = ACTIVE_MISSION_STATUSES.includes(mission.status);
  const when = running ? elapsed(mission.startedUtc ?? mission.assignedUtc ?? mission.createdUtc, null, now) : formatAge(mission.completedUtc ?? mission.lastUpdateUtc, now);
  item.description = [humanize(mission.status), store.vesselName(mission.vesselId), when].filter(Boolean).join(' · ');
  item.tooltip = md([
    `$(${missionLook(mission.status).icon}) **${escapeMd(mission.title)}**`,
    `${humanize(mission.status)} · ${code(mission.id)}`,
    mission.vesselId && `Vessel: ${escapeMd(store.vesselName(mission.vesselId))}`,
    mission.captainId && `Captain: ${escapeMd(store.captainName(mission.captainId))}`,
    mission.branchName && `Branch: ${code(mission.branchName)}`,
    mission.failureReason && `$(error) ${escapeMd(mission.failureReason)}`,
  ]);
  item.contextValue = `armadaMission armadaMission.${mission.status}${mission.prUrl ? ' armadaMission.hasPr' : ''}`;
  item.command = { command: OPEN_MISSION_COMMAND, title: 'Open Mission', arguments: [mission.id] };
  return item;
}

function voyageItem(progress: VoyageProgress, idPrefix: string, now: Date): vscode.TreeItem {
  const voyage = progress.voyage;
  const item = new vscode.TreeItem(voyage.title || voyage.id, vscode.TreeItemCollapsibleState.Collapsed);
  item.id = `${idPrefix}:voyage:${voyage.id}`;
  item.iconPath = themeIcon(voyageLook(voyage.status));
  const done = progress.completedMissions + progress.failedMissions;
  item.description = `${done}/${progress.totalMissions} · ${humanize(voyage.status)}${progress.failedMissions ? ` · ${progress.failedMissions} failed` : ''} · ${formatAge(voyage.lastUpdateUtc ?? voyage.createdUtc, now)}`;
  item.tooltip = md([
    `$(${voyageLook(voyage.status).icon}) **${escapeMd(voyage.title)}**`,
    `${humanize(voyage.status)} · ${code(voyage.id)}`,
    `${progress.completedMissions} complete, ${progress.inProgressMissions} in progress, ${progress.failedMissions} failed, ${progress.totalMissions} total`,
  ]);
  item.contextValue = `armadaVoyage armadaVoyage.${voyage.status}`;
  item.command = { command: OPEN_VOYAGE_COMMAND, title: 'Open Voyage', arguments: [voyage.id] };
  return item;
}

function captainItem(captain: Captain, idPrefix: string, store: OpsStore): vscode.TreeItem {
  const active = captain.activeMissionIds?.length ?? captain.activeMissionCount ?? 0;
  const hasMissions = active > 0 || store.missions.some((m) => m.captainId === captain.id && ACTIVE_MISSION_STATUSES.includes(m.status));
  const item = new vscode.TreeItem(captain.name, hasMissions ? vscode.TreeItemCollapsibleState.Expanded : vscode.TreeItemCollapsibleState.None);
  item.id = `${idPrefix}:captain:${captain.id}`;
  item.iconPath = themeIcon(captainLook(captain.state));
  item.description = [humanize(captain.state), captain.runtime, captain.model ?? undefined].filter(Boolean).join(' · ');
  item.tooltip = md([
    `$(${captainLook(captain.state).icon}) **${escapeMd(captain.name)}**`,
    `${humanize(captain.state)} · ${code(captain.id)}`,
    `Runtime: ${escapeMd(captain.runtime)}${captain.model ? ` (${escapeMd(captain.model)})` : ''}`,
    captain.maxConcurrency ? `Concurrency: ${active}/${captain.maxConcurrency}` : undefined,
    captain.quarantineReason && `$(error) Quarantined: ${escapeMd(captain.quarantineReason)}`,
  ]);
  item.contextValue = `armadaCaptain armadaCaptain.${captain.state}`;
  item.command = { command: OPEN_CAPTAIN_COMMAND, title: 'Open Captain', arguments: [captain.id] };
  return item;
}

function vesselItem(vessel: Vessel, idPrefix: string, store: OpsStore): vscode.TreeItem {
  const item = new vscode.TreeItem(vessel.name, vscode.TreeItemCollapsibleState.None);
  item.id = `${idPrefix}:vessel:${vessel.id}`;
  item.iconPath = new vscode.ThemeIcon(vessel.active === false ? 'repo-forked' : 'repo');
  const activeMissions = store.missions.filter((m) => m.vesselId === vessel.id && ACTIVE_MISSION_STATUSES.includes(m.status)).length;
  item.description = [vessel.defaultBranch, activeMissions ? `${activeMissions} active` : undefined].filter(Boolean).join(' · ');
  item.tooltip = md([
    `$(repo) **${escapeMd(vessel.name)}**`,
    code(vessel.id),
    vessel.repoUrl && `Repository: ${escapeMd(vessel.repoUrl)}`,
    vessel.localPath && `Local path: ${code(vessel.localPath)}`,
    `Default branch: ${code(vessel.defaultBranch)}`,
  ]);
  item.contextValue = `armadaVessel${vessel.localPath ? ' armadaVessel.local' : ''}`;
  item.command = { command: 'armada.resource.open', title: 'Open Vessel', arguments: ['vessel', vessel.id] };
  return item;
}

function mergeItem(entry: MergeEntry, idPrefix: string, store: OpsStore, now: Date): vscode.TreeItem {
  const item = new vscode.TreeItem(`${entry.branchName} → ${entry.targetBranch}`, vscode.TreeItemCollapsibleState.None);
  item.id = `${idPrefix}:merge:${entry.id}`;
  item.iconPath = themeIcon(mergeLook(entry.status));
  item.description = [humanize(entry.status), store.vesselName(entry.vesselId), formatAge(entry.lastUpdateUtc ?? entry.createdUtc, now)].filter(Boolean).join(' · ');
  item.tooltip = md([
    `$(${mergeLook(entry.status).icon}) **${escapeMd(entry.branchName)}** into ${code(entry.targetBranch)}`,
    `${humanize(entry.status)} · ${code(entry.id)}`,
    entry.missionId && `Mission: ${code(entry.missionId)}`,
    entry.testCommand && `Test: ${code(entry.testCommand)}${entry.testExitCode !== null ? ` (exit ${entry.testExitCode})` : ''}`,
  ]);
  item.contextValue = `armadaMerge armadaMerge.${entry.status}`;
  item.command = { command: OPEN_MERGE_ENTRY_COMMAND, title: 'Open Merge Entry', arguments: [entry.id] };
  return item;
}

function inboxItem(entry: InboxItem, index: number, idPrefix: string, now: Date): vscode.TreeItem {
  const item = new vscode.TreeItem(entry.title, vscode.TreeItemCollapsibleState.None);
  item.id = `${idPrefix}:inbox:${entry.kind}:${entry.entityId ?? index}`;
  item.iconPath = themeIcon(inboxLook(entry.severity));
  item.description = [entry.entityName ?? undefined, entry.expiresUtc ? `expires ${formatAge(entry.expiresUtc, now)}` : undefined].filter(Boolean).join(' · ') || humanize(entry.kind);
  item.tooltip = md([`$(${inboxLook(entry.severity).icon}) **${escapeMd(entry.title)}**`, escapeMd(entry.detail), entry.entityId && code(entry.entityId)]);
  item.contextValue = `armadaInbox armadaInbox.${entry.kind}${entry.entityType ? ` armadaInbox.entity.${entry.entityType}` : ''}`;
  item.command = { command: OPEN_INBOX_ITEM_COMMAND, title: 'Open', arguments: [entry] };
  return item;
}

function escapeMd(value: string | null | undefined): string {
  return (value ?? '').replace(/([\\`*_{}[\]<>#|])/g, '\\$1');
}
