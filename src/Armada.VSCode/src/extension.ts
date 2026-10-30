import * as vscode from 'vscode';
import { BlackBoxEvents } from './api/blackbox';
import { BlackBoxSocketEventData } from './api/types';
import { BlackBoxCommands } from './commands/blackBoxCommands';
import { InsightCommands } from './commands/insightCommands';
import { readSettings } from './config';
import { ArmadaConnection } from './connection';
import { BlackBoxDecorationProvider } from './decorations/blackBoxDecorationProvider';
import { LiveMode, LiveUpdates } from './live/liveUpdates';
import { BlackBoxStore } from './state/blackBoxStore';
import { FAILURES_VIEW_ID, FailuresTreeProvider } from './tree/failuresTreeProvider';
import { EntryViewManager } from './views/entryView';

/** Objects created on activation, returned for the integration tests. */
export interface ArmadaExtensionApi {
  connection: ArmadaConnection;
  store: BlackBoxStore;
  commands: BlackBoxCommands;
  liveMode(): LiveMode;
}

let _Live: LiveUpdates | undefined;

/** Extension entry point. */
export async function activate(context: vscode.ExtensionContext): Promise<ArmadaExtensionApi> {
  const output = vscode.window.createOutputChannel('Armada');
  context.subscriptions.push(output);

  const connection = new ArmadaConnection(context.secrets);
  context.subscriptions.push(connection);

  const store = new BlackBoxStore({
    getClient: () => connection.client,
    getWorkspaceFolderPaths: () => (vscode.workspace.workspaceFolders ?? []).filter((f) => f.uri.scheme === 'file').map((f) => f.uri.fsPath),
    getSettings: () => readSettings(),
  });

  const tree = new FailuresTreeProvider(store);
  const view = vscode.window.createTreeView(FAILURES_VIEW_ID, { treeDataProvider: tree, showCollapseAll: true });
  context.subscriptions.push(tree, view);
  const updateView = (): void => {
    view.badge = tree.badge();
    view.description = tree.viewDescription();
  };
  context.subscriptions.push(store.onDidChange(updateView));

  const decorations = new BlackBoxDecorationProvider(store);
  context.subscriptions.push(decorations, vscode.window.registerFileDecorationProvider(decorations));

  const entryViews = new EntryViewManager(() => connection.requireClient(), () => readSettings().dashboardUrl);
  context.subscriptions.push(entryViews);

  const commands = new BlackBoxCommands({
    getClient: () => connection.requireClient(),
    store,
    getSettings: () => readSettings(),
    storageUri: context.globalStorageUri,
    openEntryView: (entry) => entryViews.open(entry),
  });
  const insights = new InsightCommands({
    getClient: () => connection.requireClient(),
    store,
    pickEntry: (arg, title) => commands.pickEntry(arg, title),
  });

  let refreshTimer: ReturnType<typeof setTimeout> | undefined;
  const scheduleRefresh = (): void => {
    if (refreshTimer) clearTimeout(refreshTimer);
    refreshTimer = setTimeout(() => {
      refreshTimer = undefined;
      void store.refresh();
    }, 750);
  };
  context.subscriptions.push({ dispose: () => refreshTimer && clearTimeout(refreshTimer) });

  const onEvent = (type: string, data: BlackBoxSocketEventData): void => {
    output.appendLine(`[live] ${type} ${data.entryId ?? ''} ${data.missionId ?? ''}`);
    scheduleRefresh();
    const settings = readSettings();
    if (type !== BlackBoxEvents.captured || !settings.notifyOnCapture) return;
    const matched = store.matchedVesselIds();
    if (matched.length && data.vesselId && !matched.includes(data.vesselId)) return;
    const subject = data.missionId ?? data.voyageId ?? data.entryId ?? 'a mission';
    const where = data.step ? ` (${data.phase ? `${data.phase} / ` : ''}${data.step})` : '';
    void vscode.window.showWarningMessage(`Armada: black box captured for ${subject}${where}.`, 'Open', 'Copy FIX THIS').then((choice) => {
      const id = data.entryId ?? data.missionId ?? undefined;
      if (!id) return;
      if (choice === 'Open') void commands.openBlackBox(id);
      if (choice === 'Copy FIX THIS') void commands.copyFixThis(id);
    });
  };

  const restartLive = (): void => {
    _Live?.stop();
    _Live = undefined;
    const client = connection.client;
    if (!client) return;
    const settings = readSettings();
    _Live = new LiveUpdates({
      url: client.webSocketUrl(),
      useWebSocket: settings.useWebSocket,
      pollIntervalMs: settings.pollIntervalSeconds * 1000,
      onEvent,
      onPoll: () => void store.refresh(),
      onModeChange: (mode, detail) => output.appendLine(`[live] mode ${mode}${detail ? `: ${detail}` : ''}`),
    });
    _Live.start();
  };
  context.subscriptions.push({ dispose: () => _Live?.stop() });

  context.subscriptions.push(connection.onDidChange(() => {
    store.invalidateVessels();
    void store.refresh();
    restartLive();
  }));

  const register = (id: string, fn: (...args: unknown[]) => unknown): void => {
    context.subscriptions.push(vscode.commands.registerCommand(id, fn));
  };
  register('armada.connect', () => connection.connect());
  register('armada.disconnect', () => connection.disconnect());
  register('armada.refreshFailures', () => {
    store.invalidateVessels();
    return store.refresh();
  });
  register('armada.toggleWorkspaceFilter', async () => {
    const config = vscode.workspace.getConfiguration('armada');
    const current = config.get<boolean>('failures.filterToWorkspaceVessel', true);
    await config.update('failures.filterToWorkspaceVessel', !current, vscode.ConfigurationTarget.Global);
  });
  register('armada.openEntry', (arg) => commands.openBlackBox(arg));
  register('armada.openBlackBox', (arg) => commands.openBlackBox(arg));
  register('armada.copyFixThis', (arg) => commands.copyFixThis(arg));
  register('armada.openFixThisInEditor', (arg) => commands.openFixThisInEditor(arg));
  register('armada.sendFixThisToChat', (arg) => commands.sendFixThisToChat(arg));
  register('armada.runRecoveryPath', (arg, pathId) => commands.runRecoveryPath(arg, typeof pathId === 'string' ? pathId : undefined));
  register('armada.resolveEntry', (arg) => commands.resolveEntry(arg));
  register('armada.addNote', (arg) => commands.addNote(arg));
  register('armada.askArmada', (arg) => commands.askArmada(arg));
  register('armada.showBlackBoxTimeline', (arg) => insights.showTimeline(arg));
  register('armada.compareBlackBoxAttempts', (arg, otherId) => insights.compareAttempts(arg, typeof otherId === 'string' ? otherId : undefined));
  register('armada.previewMissionOverlap', (arg) => insights.previewOverlap(arg));

  context.subscriptions.push(vscode.workspace.onDidChangeWorkspaceFolders(() => void store.refresh()));
  context.subscriptions.push(vscode.workspace.onDidChangeConfiguration((e) => {
    if (!e.affectsConfiguration('armada')) return;
    if (e.affectsConfiguration('armada.url') || e.affectsConfiguration('armada.tenant') || e.affectsConfiguration('armada.authHeader')) {
      void connection.restore();
      return;
    }
    if (e.affectsConfiguration('armada.liveUpdates')) restartLive();
    void store.refresh();
  }));

  try {
    await connection.restore();
  } catch (err) {
    output.appendLine(`[connect] restore failed: ${err instanceof Error ? err.message : String(err)}`);
  }
  updateView();

  return { connection, store, commands, liveMode: () => _Live?.mode ?? 'stopped' };
}

/** Extension shutdown. */
export function deactivate(): void {
  _Live?.stop();
  _Live = undefined;
}
