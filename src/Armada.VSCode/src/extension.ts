import * as fs from 'fs';
import * as os from 'os';
import * as path from 'path';
import * as vscode from 'vscode';
import { readSettings } from './config';
import { ConnectionManager } from './connection/connectionManager';
import { harborDirectory, tuiPreferencesPath } from './connection/endpoints';
import { discoverLocalAdmiral } from './connection/localDiscovery';
import { ConnectionStatusBar, CONNECTION_ACTIONS_COMMAND, showConnectionActions } from './connection/statusBar';
import { BlackBoxCommands, CommandConnection } from './surfaces/blackbox/blackBoxCommands';
import { BlackBoxDecorationProvider } from './surfaces/blackbox/blackBoxDecorationProvider';
import { BlackBoxLive } from './surfaces/blackbox/blackBoxLive';
import { BlackBoxStore } from './surfaces/blackbox/blackBoxStore';
import { EntryViewManager } from './surfaces/blackbox/entryView';
import { FAILURES_EXPLORER_VIEW_ID, FAILURES_VIEW_ID, FailuresTreeProvider } from './surfaces/blackbox/failuresTreeProvider';
import { InsightCommands } from './surfaces/blackbox/insightCommands';
import { ASK_VIEW_ID, AskViewProvider } from './surfaces/ask/askView';
import { DetailPanelManager } from './surfaces/ops/detailPanels';
import { ARMADA_DOCUMENT_SCHEME, ArmadaDocumentProvider } from './surfaces/ops/opsDocuments';
import { registerOpsCommands } from './surfaces/ops/opsCommands';
import { OpsStatusBar } from './surfaces/ops/opsStatusBar';
import { OpsStore } from './surfaces/ops/opsStore';
import { CaptainsTreeProvider, FleetTreeProvider, MergeQueueTreeProvider, MissionsTreeProvider, NeedsYouTreeProvider, OPS_VIEW_IDS, OpsTreeProvider, VoyagesTreeProvider } from './surfaces/ops/opsTrees';
import { ArmadaEventBus, LiveMode } from './transport/eventBus';
import { dashboardPageUrl } from './transport/hostClient';

/** Objects created on activation, returned for the integration tests. */
export interface ArmadaExtensionApi {
  connection: ConnectionManager;
  events: ArmadaEventBus;
  store: BlackBoxStore;
  commands: BlackBoxCommands;
  ops: OpsStore;
  details: DetailPanelManager;
  ask: AskViewProvider;
  liveMode(): LiveMode;
}

let _Bus: ArmadaEventBus | undefined;

/** Extension entry point. Registers everything; the first connection attempt runs after activation returns. */
export async function activate(context: vscode.ExtensionContext): Promise<ArmadaExtensionApi> {
  const output = vscode.window.createOutputChannel('Armada', { log: true });
  context.subscriptions.push(output);

  const connection = new ConnectionManager({ secrets: context.secrets, globalState: context.globalState, log: output });
  context.subscriptions.push(connection);
  const requireConnection = (): CommandConnection => connection.requireActive();

  const initial = readSettings();
  const bus = new ArmadaEventBus({ useWebSocket: initial.useWebSocket, pollIntervalMs: initial.pollIntervalSeconds * 1000, log: (line) => output.info(line) });
  _Bus = bus;
  context.subscriptions.push({ dispose: () => bus.dispose() });

  const store = new BlackBoxStore({
    getConnection: () => connection.active,
    getWorkspaceFolderPaths: () => (vscode.workspace.workspaceFolders ?? []).filter((f) => f.uri.scheme === 'file').map((f) => f.uri.fsPath),
    getSettings: () => readSettings(),
  });

  // The native operational views (Needs You, Missions, Voyages, Captains, Fleet, Merge Queue) over one live store.
  const ops = new OpsStore({ isConnected: () => connection.connected, log: (line) => output.warn(line) });
  context.subscriptions.push({ dispose: () => ops.dispose() });
  const opsTrees: Array<[string, OpsTreeProvider]> = [
    [OPS_VIEW_IDS.needsYou, new NeedsYouTreeProvider(ops)],
    [OPS_VIEW_IDS.missions, new MissionsTreeProvider(ops)],
    [OPS_VIEW_IDS.voyages, new VoyagesTreeProvider(ops)],
    [OPS_VIEW_IDS.captains, new CaptainsTreeProvider(ops)],
    [OPS_VIEW_IDS.fleet, new FleetTreeProvider(ops)],
    [OPS_VIEW_IDS.merge, new MergeQueueTreeProvider(ops)],
  ];
  for (const [id, provider] of opsTrees) {
    const view = vscode.window.createTreeView(id, { treeDataProvider: provider, showCollapseAll: id !== OPS_VIEW_IDS.needsYou });
    context.subscriptions.push(provider, view, ops.onDidChange(() => {
      view.badge = provider.badge();
    }));
  }
  const details = new DetailPanelManager(ops);
  const documents = new ArmadaDocumentProvider(ops);
  context.subscriptions.push(details, documents, vscode.workspace.registerTextDocumentContentProvider(ARMADA_DOCUMENT_SCHEME, documents), new OpsStatusBar(ops));
  context.subscriptions.push(...registerOpsCommands({ store: ops, details, connection }));
  context.subscriptions.push(bus.subscribe((event) => ops.onEvent(event.type)), bus.onPoll(() => void ops.refreshAll()));

  // Ask Armada: the chat view at the top of the Armada sidebar.
  const askView = new AskViewProvider({ extensionUri: context.extensionUri, connection, bus, globalState: context.globalState });
  context.subscriptions.push(askView, vscode.window.registerWebviewViewProvider(ASK_VIEW_ID, askView, { webviewOptions: { retainContextWhenHidden: true } }));

  // The black box tree, in the Armada container, and in the Explorer when armada.failures.showInExplorer is on (the
  // view's `when` clause reads the setting).

  const tree = new FailuresTreeProvider(store);
  const failureViews = [FAILURES_VIEW_ID, FAILURES_EXPLORER_VIEW_ID].map((id) => vscode.window.createTreeView(id, { treeDataProvider: tree, showCollapseAll: true }));
  context.subscriptions.push(tree, ...failureViews);
  const updateViews = (): void => {
    for (const view of failureViews) {
      view.badge = tree.badge();
      view.description = tree.viewDescription();
    }
  };
  context.subscriptions.push(store.onDidChange(updateViews));

  const decorations = new BlackBoxDecorationProvider(store);
  context.subscriptions.push(decorations, vscode.window.registerFileDecorationProvider(decorations));

  const entryViews = new EntryViewManager(requireConnection);
  context.subscriptions.push(entryViews);

  const commands = new BlackBoxCommands({
    requireConnection,
    store,
    getSettings: () => readSettings(),
    storageUri: context.globalStorageUri,
    openEntryView: (entry) => entryViews.open(entry),
    askInChat: (prompt) => askView.ask(prompt),
  });
  const insights = new InsightCommands({
    requireConnection,
    store,
    pickEntry: (arg, title) => commands.pickEntry(arg, title),
  });

  const statusBar = new ConnectionStatusBar(connection);
  context.subscriptions.push(statusBar, bus.onModeChange((mode) => statusBar.setLiveMode(mode)));

  const live = new BlackBoxLive(bus, {
    refresh: () => void store.refresh(),
    matchedVesselIds: () => store.matchedVesselIds(),
    log: (line) => output.debug(line),
    onCaptured: (data) => {
      if (!readSettings().notifyOnCapture) return;
      const subject = data.missionId ?? data.voyageId ?? data.entryId ?? 'a mission';
      const where = data.step ? ` (${data.phase ? `${data.phase} / ` : ''}${data.step})` : '';
      void vscode.window.showWarningMessage(`Armada: black box captured for ${subject}${where}.`, 'Open', 'Copy FIX THIS').then((choice) => {
        const id = data.entryId ?? data.missionId ?? undefined;
        if (!id) return;
        if (choice === 'Open') void commands.openBlackBox(id);
        if (choice === 'Copy FIX THIS') void commands.copyFixThis(id);
      });
    },
  });
  context.subscriptions.push(live);

  const restartBus = (): void => {
    const active = connection.active;
    if (!active) {
      bus.stop();
      return;
    }
    const settings = readSettings();
    bus.setOptions(settings.useWebSocket, settings.pollIntervalSeconds * 1000);
    bus.start({ baseUrl: active.baseUrl, token: active.socketToken });
  };

  context.subscriptions.push(connection.onDidChange((state) => {
    if (state.phase === 'connected') {
      store.invalidateVessels();
      void store.refresh();
      void ops.refreshAll();
      restartBus();
      return;
    }
    bus.stop();
    if (state.phase !== 'connecting') {
      void store.refresh();
      void ops.refreshAll();
    }
  }));

  const register = (id: string, fn: (...args: unknown[]) => unknown): void => {
    context.subscriptions.push(vscode.commands.registerCommand(id, fn));
  };
  register('armada.connect', () => connection.connect({ interactive: true }));
  register('armada.reconnect', () => connection.reconnect());
  register('armada.disconnect', () => connection.disconnect());
  register(CONNECTION_ACTIONS_COMMAND, () => showConnectionActions(connection));
  register('armada.showOutput', () => output.show(true));
  register('armada.openSettings', () => openSettings(context.extension.id));
  register('armada.openDashboard', () => openDashboard(connection, '/'));
  register('armada.ask.open', () => vscode.commands.executeCommand(`${ASK_VIEW_ID}.focus`));
  register('armada.ask.new', () => askView.newConversation());
  register('armada.ask.history', () => askView.pickConversation());
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
  register('armada.draftArmadaFixMission', (arg) => commands.draftArmadaFixMission(arg));
  register('armada.showResilienceGaps', () => insights.showResilienceGaps());

  context.subscriptions.push(vscode.workspace.onDidChangeWorkspaceFolders(() => void store.refresh()));
  context.subscriptions.push(vscode.workspace.onDidChangeConfiguration((e) => {
    if (!e.affectsConfiguration('armada')) return;
    if (e.affectsConfiguration('armada.tenant')) store.invalidateVessels();
    if (e.affectsConfiguration('armada.liveUpdates')) restartBus();
    void store.refresh();
  }));

  updateViews();

  // No network work during activate: the first connection attempt (local discovery, health, whoami) runs afterwards.
  const startup = setTimeout(() => void connection.restore(), 0);
  context.subscriptions.push({ dispose: () => clearTimeout(startup) });

  return { connection, events: bus, store, commands, ops, details, ask: askView, liveMode: () => bus.mode };
}

/** Extension shutdown. */
export function deactivate(): void {
  _Bus?.dispose();
  _Bus = undefined;
}

/**
 * "Armada: Open Settings": the Armada settings files this machine has (~/.armada/settings.json, Harbor's
 * settings.json, the TUI's tui.json), which is where the connection comes from, plus the extension's own preferences.
 */
async function openSettings(extensionId: string): Promise<void> {
  const local = discoverLocalAdmiral();
  const files: Array<{ label: string; file: string }> = [
    { label: 'Armada settings', file: local.settingsPath },
    { label: 'Harbor settings', file: path.join(harborDirectory(), 'settings.json') },
    { label: 'TUI profiles', file: tuiPreferencesPath() },
  ].filter((f) => fs.existsSync(f.file));
  const items: Array<vscode.QuickPickItem & { file?: string }> = files.map((f) => ({ label: `$(file) ${f.label}`, description: displayHomePath(f.file), file: f.file }));
  items.push({ label: '$(settings-gear) Extension Preferences', description: 'views, live updates, decorations' });
  const picked = items.length === 1 ? items[0] : await vscode.window.showQuickPick(items, { title: 'Armada: Open Settings', placeHolder: 'The Armada settings the extension connects with, or its own preferences' });
  if (!picked) return;
  if (picked.file) {
    await vscode.window.showTextDocument(vscode.Uri.file(picked.file));
    return;
  }
  await vscode.commands.executeCommand('workbench.action.openSettings', `@ext:${extensionId}`);
}

function displayHomePath(file: string): string {
  const home = os.homedir();
  return file.startsWith(home + path.sep) ? `~${file.slice(home.length)}` : file;
}

async function openDashboard(connection: ConnectionManager, route: string): Promise<boolean> {
  const base = connection.dashboardUrl();
  if (!base) {
    void vscode.window.showErrorMessage('Armada: no Admiral is configured, so there is no dashboard to open. Run "Armada: Connect".', 'Connect').then((choice) => {
      if (choice === 'Connect') void vscode.commands.executeCommand('armada.connect');
    });
    return false;
  }
  return vscode.env.openExternal(vscode.Uri.parse(dashboardPageUrl(base, route)));
}
