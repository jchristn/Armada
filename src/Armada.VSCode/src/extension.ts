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
import { NAVIGATION_VIEW_ID, NavigationTreeProvider, OPEN_DASHBOARD_PAGE_COMMAND, routeFromArg } from './surfaces/nav/navigationTree';
import { ArmadaEventBus, LiveMode } from './transport/eventBus';
import { dashboardPageUrl } from './transport/hostClient';

/** Objects created on activation, returned for the integration tests. */
export interface ArmadaExtensionApi {
  connection: ConnectionManager;
  events: ArmadaEventBus;
  store: BlackBoxStore;
  commands: BlackBoxCommands;
  navigation: NavigationTreeProvider;
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

  // Navigation (the dashboard's nav model) and the black box tree, in the Armada container; the black box tree also
  // in the Explorer when armada.failures.showInExplorer is on (the view's `when` clause reads the setting).
  const navigation = new NavigationTreeProvider(() => connection.connected);
  const navigationView = vscode.window.createTreeView(NAVIGATION_VIEW_ID, { treeDataProvider: navigation });
  context.subscriptions.push(navigation, navigationView);

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
    navigation.refresh();
    if (state.phase === 'connected') {
      store.invalidateVessels();
      void store.refresh();
      restartBus();
      return;
    }
    bus.stop();
    if (state.phase !== 'connecting') void store.refresh();
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
  register(OPEN_DASHBOARD_PAGE_COMMAND, (arg) => openDashboard(connection, routeFromArg(arg) ?? '/'));
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

  return { connection, events: bus, store, commands, navigation, liveMode: () => bus.mode };
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
