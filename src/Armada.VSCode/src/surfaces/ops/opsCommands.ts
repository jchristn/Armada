/*
 * Commands of the native Armada views: open detail pages, logs and changes; act on missions, voyages, captains and
 * the merge queue (with confirmations for anything destructive); answer Needs You items; dispatch missions and
 * voyages through a form. Every command takes an id or the tree node it was invoked on.
 */

import * as vscode from 'vscode';
import {
  approveMissionReview,
  cancelMergeEntry,
  cancelVoyage,
  createVoyage,
  decideCliPermissionRequest,
  denyMissionReview,
  dispatchMission,
  processAllMergeQueue,
  processMergeEntry,
  restartCaptain,
  restartMission,
  stopCaptain,
  transitionMission,
} from '@dashboard/api/client';
import type { InboxItem, Vessel } from '@dashboard/types/models';
import { ConnectionManager } from '../../connection/connectionManager';
import { describeError } from '../../transport/errors';
import { dashboardPageUrl } from '../../transport/hostClient';
import { readGitRemoteUrls, sameRepository } from '../../util/repoUrl';
import { DetailKind, DetailPanelManager } from './detailPanels';
import { showForm } from './formPanel';
import { openArmadaDocument } from './opsDocuments';
import { OpsStore } from './opsStore';
import { OpsNode } from './opsTrees';

/** Host dependencies. */
export interface OpsCommandDeps {
  store: OpsStore;
  details: DetailPanelManager;
  connection: ConnectionManager;
}

/** Registers the commands; returns their disposables. */
export function registerOpsCommands(deps: OpsCommandDeps): vscode.Disposable[] {
  const { store, details, connection } = deps;
  const disposables: vscode.Disposable[] = [];
  const register = (id: string, fn: (...args: unknown[]) => unknown): void => {
    disposables.push(vscode.commands.registerCommand(id, async (...args: unknown[]) => {
      try {
        return await fn(...args);
      } catch (err) {
        void vscode.window.showErrorMessage(`Armada: ${describeError(err)}`);
        return undefined;
      }
    }));
  };
  const requireConnected = async (): Promise<boolean> => connection.connected || connection.connect({ interactive: true });
  const after = (kind: DetailKind, id: string, domains: Array<Parameters<OpsStore['refresh']>[0]>): void => {
    for (const d of domains) void store.refresh(d);
    details.reload(kind, id);
  };

  register('armada.ops.refresh', () => store.refreshAll());

  // Missions
  register('armada.mission.open', async (arg) => {
    const id = missionId(arg);
    if (id && (await requireConnected())) await details.open('mission', id);
  });
  register('armada.mission.viewLog', async (arg) => {
    const id = missionId(arg);
    if (id) await openArmadaDocument({ kind: 'missionLog', id }, store.missions.find((m) => m.id === id)?.title ?? id);
  });
  register('armada.mission.viewDiff', async (arg) => {
    const id = missionId(arg);
    if (id) await openArmadaDocument({ kind: 'missionDiff', id }, store.missions.find((m) => m.id === id)?.title ?? id);
  });
  register('armada.mission.cancel', async (arg) => {
    const id = missionId(arg);
    if (!id || !(await confirm(`Cancel the mission "${missionTitle(store, id)}"? Its captain stops working on it.`, 'Cancel Mission'))) return;
    await transitionMission(id, { status: 'Cancelled' });
    void vscode.window.showInformationMessage(`Armada: cancelled "${missionTitle(store, id)}".`);
    after('mission', id, ['missions', 'captains', 'voyages']);
  });
  register('armada.mission.restart', async (arg) => {
    const id = missionId(arg);
    if (!id || !(await confirm(`Restart the mission "${missionTitle(store, id)}"?`, 'Restart'))) return;
    const restarted = await restartMission(id);
    void vscode.window.showInformationMessage(`Armada: restarted "${missionTitle(store, id)}".`);
    after('mission', id, ['missions', 'voyages']);
    if (restarted?.id && restarted.id !== id) await details.open('mission', restarted.id);
  });
  register('armada.mission.approve', async (arg) => {
    const id = missionId(arg);
    if (!id) return;
    const comment = await vscode.window.showInputBox({ title: `Approve "${missionTitle(store, id)}"`, prompt: 'Comment (optional)', ignoreFocusOut: true });
    if (comment === undefined) return;
    await approveMissionReview(id, { comment: comment.trim() || undefined });
    void vscode.window.showInformationMessage(`Armada: approved "${missionTitle(store, id)}".`);
    after('mission', id, ['missions', 'inbox']);
  });
  register('armada.mission.deny', async (arg) => {
    const id = missionId(arg);
    if (!id) return;
    const action = await vscode.window.showQuickPick([
      { label: 'Retry the stage', description: 'send it back to the captain with your comment', value: 'RetryStage' as const },
      { label: 'Fail the pipeline', description: 'stop this work', value: 'FailPipeline' as const },
    ], { title: `Deny "${missionTitle(store, id)}"`, placeHolder: 'What should happen next?' });
    if (!action) return;
    const comment = await vscode.window.showInputBox({ title: `Deny "${missionTitle(store, id)}"`, prompt: 'What needs to change?', ignoreFocusOut: true });
    if (comment === undefined) return;
    await denyMissionReview(id, { comment: comment.trim() || undefined, action: action.value });
    void vscode.window.showInformationMessage(`Armada: denied "${missionTitle(store, id)}".`);
    after('mission', id, ['missions', 'inbox']);
  });
  register('armada.mission.copyId', async (arg) => {
    const id = missionId(arg);
    if (id) await vscode.env.clipboard.writeText(id);
  });

  // Voyages
  register('armada.voyage.open', async (arg) => {
    const id = voyageId(arg);
    if (id && (await requireConnected())) await details.open('voyage', id);
  });
  register('armada.voyage.cancel', async (arg) => {
    const id = voyageId(arg);
    const title = store.voyages.find((v) => v.voyage.id === id)?.voyage.title ?? id;
    if (!id || !(await confirm(`Cancel the voyage "${title}" and its unfinished missions?`, 'Cancel Voyage'))) return;
    await cancelVoyage(id);
    void vscode.window.showInformationMessage(`Armada: cancelled the voyage "${title}".`);
    after('voyage', id, ['voyages', 'missions', 'captains']);
  });

  // Captains
  register('armada.captain.open', async (arg) => {
    const id = captainId(arg);
    if (id && (await requireConnected())) await details.open('captain', id);
  });
  register('armada.captain.viewLog', async (arg) => {
    const id = captainId(arg);
    if (id) await openArmadaDocument({ kind: 'captainLog', id }, store.captainName(id));
  });
  register('armada.captain.stop', async (arg) => {
    const id = captainId(arg);
    if (!id || !(await confirm(`Stop the captain "${store.captainName(id)}"? Its current mission stops.`, 'Stop Captain'))) return;
    await stopCaptain(id);
    void vscode.window.showInformationMessage(`Armada: stopping "${store.captainName(id)}".`);
    after('captain', id, ['captains', 'missions']);
  });
  register('armada.captain.restart', async (arg) => {
    const id = captainId(arg);
    if (!id || !(await confirm(`Restart the captain "${store.captainName(id)}"?`, 'Restart Captain'))) return;
    await restartCaptain(id);
    void vscode.window.showInformationMessage(`Armada: restarting "${store.captainName(id)}".`);
    after('captain', id, ['captains']);
  });

  // Merge queue
  register('armada.merge.open', async (arg) => {
    const id = mergeId(arg);
    if (id && (await requireConnected())) await details.open('merge', id);
  });
  register('armada.merge.process', async (arg) => {
    const id = mergeId(arg);
    if (!id) return;
    await processMergeEntry(id);
    void vscode.window.showInformationMessage('Armada: processing the merge entry.');
    after('merge', id, ['merge', 'missions']);
  });
  register('armada.merge.cancel', async (arg) => {
    const id = mergeId(arg);
    if (!id || !(await confirm('Remove this entry from the merge queue?', 'Remove'))) return;
    await cancelMergeEntry(id);
    after('merge', id, ['merge']);
  });
  register('armada.merge.processAll', async () => {
    if (!(await confirm('Process the whole merge queue now?', 'Process Queue'))) return;
    await processAllMergeQueue();
    void vscode.window.showInformationMessage('Armada: processing the merge queue.');
    void store.refresh('merge');
  });

  // Needs You
  register('armada.inbox.open', async (arg) => {
    const item = inboxItemFrom(arg);
    if (!item) return;
    if (item.kind === 'cli_permission' && item.cliPermission) {
      await decideCliPermission(item);
      void store.refresh('inbox');
      return;
    }
    const id = item.entityId ?? '';
    const kind: DetailKind | undefined = id.startsWith('msn_') ? 'mission' : id.startsWith('vyg_') ? 'voyage' : id.startsWith('cpt_') ? 'captain' : id.startsWith('mrg_') ? 'merge' : undefined;
    if (kind) {
      await details.open(kind, id);
      return;
    }
    const choice = await vscode.window.showInformationMessage(`${item.title}\n\n${item.detail}`, { modal: true }, 'Open in Browser');
    if (choice === 'Open in Browser') await openInBrowser(connection, item.href);
  });

  // Dispatch
  register('armada.dispatch', async (arg) => {
    if (!(await requireConnected())) return;
    if (store.state('fleet') !== 'ready') await store.refresh('fleet');
    const vesselId = arg && typeof arg === 'object' && (arg as OpsNode).kind === 'vessel' ? (arg as Extract<OpsNode, { kind: 'vessel' }>).vessel.id : undefined;
    await dispatchForm(store, details, vesselId);
  });
  register('armada.dispatchVoyage', async () => {
    if (!(await requireConnected())) return;
    if (store.state('fleet') !== 'ready') await store.refresh('fleet');
    await voyageForm(store, details);
  });

  // Vessels
  register('armada.vessel.openFolder', async (arg) => {
    const vessel = arg && typeof arg === 'object' && (arg as OpsNode).kind === 'vessel' ? (arg as Extract<OpsNode, { kind: 'vessel' }>).vessel : undefined;
    if (vessel?.localPath) await vscode.commands.executeCommand('vscode.openFolder', vscode.Uri.file(vessel.localPath), { forceNewWindow: true });
  });

  // Shared
  register('armada.copyText', async (text) => {
    if (typeof text === 'string') await vscode.env.clipboard.writeText(text);
  });
  register('armada.openUrl', async (url) => {
    if (typeof url === 'string' && /^https?:\/\//i.test(url)) await vscode.env.openExternal(vscode.Uri.parse(url));
  });
  register('armada.openInBrowser', async (route) => openInBrowser(connection, typeof route === 'string' ? route : '/'));

  return disposables;
}

async function openInBrowser(connection: ConnectionManager, route: string): Promise<void> {
  const base = connection.dashboardUrl();
  if (!base) return;
  const path = route.startsWith('/dashboard') ? route.slice('/dashboard'.length) || '/' : route;
  await vscode.env.openExternal(vscode.Uri.parse(dashboardPageUrl(base, path)));
}

async function confirm(message: string, action: string): Promise<boolean> {
  return (await vscode.window.showWarningMessage(message, { modal: true }, action)) === action;
}

function missionTitle(store: OpsStore, id: string): string {
  return store.missions.find((m) => m.id === id)?.title ?? id;
}

function idFrom(arg: unknown, fromNode: (node: OpsNode) => string | undefined): string | undefined {
  if (typeof arg === 'string') return arg.trim() || undefined;
  if (arg && typeof arg === 'object' && 'kind' in arg) return fromNode(arg as OpsNode);
  return undefined;
}

function missionId(arg: unknown): string | undefined {
  return idFrom(arg, (n) => (n.kind === 'mission' ? n.mission.id : n.kind === 'inbox' && n.item.entityId?.startsWith('msn_') ? n.item.entityId : undefined));
}

function voyageId(arg: unknown): string | undefined {
  return idFrom(arg, (n) => (n.kind === 'voyage' ? n.progress.voyage.id : n.kind === 'mission' ? n.mission.voyageId ?? undefined : undefined));
}

function captainId(arg: unknown): string | undefined {
  return idFrom(arg, (n) => (n.kind === 'captain' ? n.captain.id : n.kind === 'mission' ? n.mission.captainId ?? undefined : undefined));
}

function mergeId(arg: unknown): string | undefined {
  return idFrom(arg, (n) => (n.kind === 'merge' ? n.entry.id : undefined));
}

function inboxItemFrom(arg: unknown): InboxItem | undefined {
  if (!arg || typeof arg !== 'object') return undefined;
  if ((arg as OpsNode).kind === 'inbox') return (arg as Extract<OpsNode, { kind: 'inbox' }>).item;
  if (typeof (arg as InboxItem).title === 'string' && typeof (arg as InboxItem).kind === 'string') return arg as InboxItem;
  return undefined;
}

async function decideCliPermission(item: InboxItem): Promise<void> {
  const request = item.cliPermission;
  if (!request) return;
  const subject = request.summaryText ? `${request.toolName}: ${request.summaryText}` : request.toolName;
  const choice = await vscode.window.showQuickPick([
    { label: '$(check) Allow once', value: 'AllowOnce' as const },
    { label: '$(check-all) Allow and remember', value: 'AllowAndRemember' as const },
    { label: '$(comment-discussion) Allow for this conversation', value: 'AllowForConversation' as const },
    { label: '$(close) Deny', value: 'Deny' as const },
  ], { title: item.title, placeHolder: subject });
  if (!choice) return;
  await decideCliPermissionRequest(request.id, { decision: choice.value });
  void vscode.window.showInformationMessage(`Armada: ${choice.value === 'Deny' ? 'denied' : 'allowed'} ${request.toolName}.`);
}

/** The vessel whose repository is open in the workspace, if any. */
function workspaceVessel(vessels: Vessel[]): Vessel | undefined {
  for (const folder of vscode.workspace.workspaceFolders ?? []) {
    const remotes = readGitRemoteUrls(folder.uri.fsPath);
    const match = vessels.find((v) => remotes.some((r) => sameRepository(r, v.repoUrl)) || (v.localPath && folder.uri.fsPath === v.localPath));
    if (match) return match;
  }
  return undefined;
}

function vesselOptions(store: OpsStore): Array<{ value: string; label: string }> {
  return store.vessels.filter((v) => v.active !== false).map((v) => ({ value: v.id, label: v.repoUrl ? `${v.name} (${v.repoUrl})` : v.name }));
}

async function dispatchForm(store: OpsStore, details: DetailPanelManager, vesselId?: string): Promise<void> {
  const preferred = vesselId ?? workspaceVessel(store.vessels)?.id ?? store.vessels[0]?.id ?? '';
  const missionIdCreated = await showForm<string>({
    title: 'Dispatch a Mission',
    intro: 'A captain picks the mission up, works it in its own dock, and lands the result the way the vessel is configured to.',
    submitLabel: 'Dispatch',
    fields: [
      { key: 'vesselId', label: 'Vessel', type: 'select', value: preferred, options: vesselOptions(store), required: true, hint: 'The repository the captain works in. The open workspace\'s repository is preselected.' },
      { key: 'title', label: 'Title', type: 'text', required: true, placeholder: 'Fix the flaky login test' },
      { key: 'description', label: 'Brief', type: 'textarea', placeholder: 'What to do, where, and how to know it is done.', hint: 'The captain works from this. Name files, expected behavior and how to verify.' },
      { key: 'priority', label: 'Priority', type: 'number', value: '100', hint: 'Lower runs sooner.' },
    ],
  }, async (values) => {
    try {
      const priority = Number.parseInt(values.priority ?? '', 10);
      const mission = await dispatchMission({
        vesselId: values.vesselId,
        title: values.title.trim(),
        description: values.description?.trim() || undefined,
        priority: Number.isFinite(priority) ? priority : undefined,
      });
      return { result: mission.id };
    } catch (err) {
      return { error: describeError(err) };
    }
  });
  if (!missionIdCreated) return;
  void vscode.window.showInformationMessage('Armada: mission dispatched.');
  void store.refresh('missions');
  await details.open('mission', missionIdCreated);
}

async function voyageForm(store: OpsStore, details: DetailPanelManager): Promise<void> {
  const preferred = workspaceVessel(store.vessels)?.id ?? store.vessels[0]?.id ?? '';
  const voyageIdCreated = await showForm<string>({
    title: 'Dispatch a Voyage',
    intro: 'A voyage is a batch of related missions on one vessel. Each line of Missions becomes a mission; write "Title: brief" to give one a brief.',
    submitLabel: 'Dispatch Voyage',
    fields: [
      { key: 'vesselId', label: 'Vessel', type: 'select', value: preferred, options: vesselOptions(store), required: true },
      { key: 'title', label: 'Voyage title', type: 'text', required: true, placeholder: 'Harden the login flow' },
      { key: 'description', label: 'Description', type: 'textarea', placeholder: 'The goal of the voyage, shared by its missions.' },
      { key: 'missions', label: 'Missions', type: 'textarea', required: true, placeholder: 'Add rate limiting to /login: limit to 5 attempts a minute per IP\nCover the lockout with tests' },
    ],
  }, async (values) => {
    const missions = (values.missions ?? '').split(/\r?\n/).map((line) => line.trim()).filter(Boolean).map((line) => {
      const at = line.indexOf(':');
      return at > 0 ? { vesselId: values.vesselId, title: line.slice(0, at).trim(), description: line.slice(at + 1).trim() || undefined } : { vesselId: values.vesselId, title: line };
    });
    if (!missions.length) return { error: 'Add at least one mission, one per line.' };
    try {
      const voyage = await createVoyage({ title: values.title.trim(), description: values.description?.trim() || undefined, vesselId: values.vesselId, missions });
      return { result: voyage.id };
    } catch (err) {
      return { error: describeError(err) };
    }
  });
  if (!voyageIdCreated) return;
  void vscode.window.showInformationMessage('Armada: voyage dispatched.');
  void store.refresh('voyages');
  void store.refresh('missions');
  await details.open('voyage', voyageIdCreated);
}
