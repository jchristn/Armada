import * as fs from 'fs';
import * as os from 'os';
import * as path from 'path';
import * as vscode from 'vscode';
import { readSettings } from './config';
import { loadCatalog } from './i18n';
import { ConnectionManager } from './connection/connectionManager';
import { harborDirectory, tuiPreferencesPath } from './connection/endpoints';
import { discoverLocalAdmiral } from './connection/localDiscovery';
import { ConnectionStatusBar, CONNECTION_ACTIONS_COMMAND, showConnectionActions } from './connection/statusBar';
import { BlackBoxCommands, CommandConnection } from './surfaces/blackbox/blackBoxCommands';
import { BlackBoxDecorationProvider } from './surfaces/blackbox/blackBoxDecorationProvider';
import { BlackBoxLive } from './surfaces/blackbox/blackBoxLive';
import { BlackBoxStore } from './surfaces/blackbox/blackBoxStore';
import { EntryViewManager } from './surfaces/blackbox/entryView';
import { FAILURES_EXPLORER_VIEW_ID, FailuresTreeProvider } from './surfaces/blackbox/failuresTreeProvider';
import { InsightCommands } from './surfaces/blackbox/insightCommands';
import { ASK_VIEW_ID, AskViewProvider } from './surfaces/ask/askView';
import { resourceCatalog } from './surfaces/resources/resourceCatalog';
import { extendCatalog, resourceCatalogExtras } from './surfaces/resources/resourceCatalogExtras';
import { ResourceSpec } from './surfaces/resources/resourceSpec';
import { ResourceController, ResourceStore } from './surfaces/resources/resourceViews';
import { DetailPanelManager } from './surfaces/ops/detailPanels';
import { registerExtraCommands } from './surfaces/ops/extraCommands';
import { registerLocalCommands, watchNeedsYou } from './surfaces/ops/localCommands';
import { PLANNING, REFINEMENT, SessionChatManager } from './surfaces/sessions/sessionChat';
import { registerVesselWorkspace } from './surfaces/workspace/vesselFileSystem';
import { ARMADA_DOCUMENT_SCHEME, ArmadaDocumentProvider } from './surfaces/ops/opsDocuments';
import { registerOpsCommands } from './surfaces/ops/opsCommands';
import { OpsStatusBar } from './surfaces/ops/opsStatusBar';
import { OpsStore } from './surfaces/ops/opsStore';
import { CaptainsTreeProvider, FleetTreeProvider, MergeQueueTreeProvider, MissionsTreeProvider, NeedsYouTreeProvider, VoyagesTreeProvider } from './surfaces/ops/opsTrees';
import { dashboardHubs, LEGACY_TAB_TARGETS } from './surfaces/nav/navLayout';
import { isNavNode, NAV_REVEAL_COMMAND, NAV_VIEW_ID, navPages, NavTreeProvider } from './surfaces/nav/navTree';
import { registerConversations } from './surfaces/ask/conversationsTree';
import { parseAskEvent } from '@dashboard/lib/askEvents';
import type { WebSocketMessage } from '@dashboard/types/models';
import { ArmadaEventBus, LiveMode } from './transport/eventBus';
import { dashboardPageUrl } from './transport/hostClient';
import { DashboardPages, OPEN_PAGE_COMMAND } from './surfaces/dashboard/dashboardPages';
import { setPageOpener } from './surfaces/dashboard/pageRouting';
import { KIND_ROUTES } from './surfaces/dashboard/routes';

/** Objects created on activation, returned for the integration tests. */
export interface ArmadaExtensionApi {
  connection: ConnectionManager;
  events: ArmadaEventBus;
  store: BlackBoxStore;
  commands: BlackBoxCommands;
  ops: OpsStore;
  details: DetailPanelManager;
  ask: AskViewProvider;
  resources: { specs: ResourceSpec[]; store: ResourceStore };
  /** The Armada tree (the dashboard's navigation). */
  nav: NavTreeProvider;
  /** The dashboard page tabs. */
  pages: DashboardPages;
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

  // The dashboard's own pages (Overview, Needs You, Dispatch, Activity, every hub and detail page) in editor tabs.
  const pages = new DashboardPages(context.extensionUri, connection, bus, (line) => output.warn(line));
  context.subscriptions.push(pages, { dispose: () => setPageOpener(undefined) });
  setPageOpener((route, options) => pages.open(route, options));
  context.subscriptions.push(
    vscode.commands.registerCommand(OPEN_PAGE_COMMAND, (route?: unknown, options?: unknown) => {
      if (!connection.connected) {
        void vscode.window.showWarningMessage('Armada: connect to an Admiral first.', 'Connect').then((choice) => { if (choice) void vscode.commands.executeCommand('armada.connect'); });
        return;
      }
      const opts = options && typeof options === 'object' ? (options as { newTab?: boolean; state?: unknown }) : {};
      pages.open(typeof route === 'string' && route ? route : '/', opts);
    }),
    vscode.commands.registerCommand('armada.page.openList', (kind?: unknown) => {
      if (typeof kind === 'string' && KIND_ROUTES[kind]) void vscode.commands.executeCommand(OPEN_PAGE_COMMAND, KIND_ROUTES[kind]);
    }),
    vscode.commands.registerCommand('armada.page.pick', async () => {
      const picked = await vscode.window.showQuickPick(navPages(dashboardHubs()).map((p) => ({ label: p.label, description: p.route, route: p.route })), { title: 'Armada: Open Page', matchOnDescription: true });
      if (picked) void vscode.commands.executeCommand(OPEN_PAGE_COMMAND, picked.route);
    }),
  );

  const store = new BlackBoxStore({
    getConnection: () => connection.active,
    getWorkspaceFolderPaths: () => (vscode.workspace.workspaceFolders ?? []).filter((f) => f.uri.scheme === 'file').map((f) => f.uri.fsPath),
    getSettings: () => readSettings(),
  });

  // The native operational views (Needs You, Missions, Voyages, Captains, Fleet, Merge Queue) over one live store.
  const ops = new OpsStore({ isConnected: () => connection.connected, log: (line) => output.warn(line) });
  context.subscriptions.push({ dispose: () => ops.dispose() });
  const opsTrees = {
    needsYou: new NeedsYouTreeProvider(ops),
    missions: new MissionsTreeProvider(ops),
    voyages: new VoyagesTreeProvider(ops),
    captains: new CaptainsTreeProvider(ops),
    fleet: new FleetTreeProvider(ops),
    merge: new MergeQueueTreeProvider(ops),
  };
  context.subscriptions.push(...Object.values(opsTrees));
  const details = new DetailPanelManager(ops);
  const documents = new ArmadaDocumentProvider(ops);
  context.subscriptions.push(details, documents, vscode.workspace.registerTextDocumentContentProvider(ARMADA_DOCUMENT_SCHEME, documents), new OpsStatusBar(ops));
  context.subscriptions.push(...registerOpsCommands({ store: ops, details, connection }));
  context.subscriptions.push(bus.subscribe((event) => ops.onEvent(event.type)), bus.onPoll(() => void ops.refreshAll()));

  // Backlog, Fleet Actions, Delivery, Configuration, Activity and System: every other kind of record, natively.
  const sessions = new SessionChatManager(bus);
  context.subscriptions.push(sessions);
  const pickOne = async (title: string, items: Array<{ label: string; description?: string; id: string }>): Promise<string | undefined> => (await vscode.window.showQuickPick(items, { title, matchOnDescription: true }))?.id;
  const specs = resourceCatalog({
    pickCaptain: async (title) => {
      if (ops.state('captains') !== 'ready') await ops.refresh('captains');
      return pickOne(title, ops.captains.map((c) => ({ label: c.name, description: `${c.runtime}${c.model ? ` · ${c.model}` : ''} · ${c.state}`, id: c.id })));
    },
    pickVessel: async (title) => {
      if (ops.state('fleet') !== 'ready') await ops.refresh('fleet');
      return pickOne(title, ops.vessels.map((v) => ({ label: v.name, description: v.repoUrl ?? undefined, id: v.id })));
    },
    openPlanning: (id) => sessions.open(PLANNING, id),
    openRefinement: (id) => sessions.open(REFINEMENT, id),
    pickVessels: async (title) => {
      if (ops.state('fleet') !== 'ready') await ops.refresh('fleet');
      const picked = await vscode.window.showQuickPick(ops.vessels.map((v) => ({ label: v.name, description: v.repoUrl ?? undefined, id: v.id })), { title, canPickMany: true, matchOnDescription: true });
      return picked?.map((p) => p.id);
    },
  });
  context.subscriptions.push(vscode.commands.registerCommand('armada.pickVessels', async (title?: string) => {
    if (ops.state('fleet') !== 'ready') await ops.refresh('fleet');
    const picked = await vscode.window.showQuickPick(ops.vessels.map((v) => ({ label: v.name, description: v.repoUrl ?? undefined, id: v.id })), { title: title ?? 'Vessels', canPickMany: true, matchOnDescription: true });
    return picked?.map((p) => p.id);
  }));
  specs.push(...resourceCatalogExtras());
  extendCatalog(specs);
  const resources = new ResourceStore(() => connection.connected);
  const resourceController = new ResourceController(specs, resources, async () => connection.connected || connection.connect({ interactive: true }), (key) => {
    if (key === 'vessel' || key === 'fleet') void ops.refresh('fleet');
    if (key === 'captain') void ops.refresh('captains');
  });
  context.subscriptions.push(resourceController, bus.onPoll(() => void resourceController.refreshAll()));

  // The Black Box tree (the Explorer's Armada Failures view).
  const tree = new FailuresTreeProvider(store);
  context.subscriptions.push(tree);
  // The Manage view: the dashboard's navigation as a table of contents; every entry opens its page in a tab.
  const badgeCount = (badge: vscode.ViewBadge | undefined): number | undefined => badge?.value || undefined;
  const nav = new NavTreeProvider(dashboardHubs(), () => connection.connected, { counts: { needsYou: () => badgeCount(opsTrees.needsYou.badge()) } });
  const navView = vscode.window.createTreeView(NAV_VIEW_ID, { treeDataProvider: nav, showCollapseAll: true });
  let navCount = 0;
  context.subscriptions.push(nav, navView, connection.onDidChange(() => nav.refresh()), ops.onDidChange(() => {
    navView.badge = opsTrees.needsYou.badge();
    const count = badgeCount(navView.badge) ?? 0;
    if (count !== navCount) {
      navCount = count;
      nav.refresh();
    }
  }));
  context.subscriptions.push(vscode.commands.registerCommand(NAV_REVEAL_COMMAND, async (key?: unknown) => {
    await vscode.commands.executeCommand(`${NAV_VIEW_ID}.focus`);
    const node = typeof key === 'string' ? nav.hubNode(key) : undefined;
    if (node) await navView.reveal(node, { expand: true, focus: false, select: false });
  }));
  // Kinds whose records change through live events reload when they arrive (others on the poll tick).
  const liveKinds: Record<string, string> = { 'deployment.': 'deployment', 'incident.': 'incident', 'objective.': 'backlog', 'planning-session.': 'planningSession', 'captain-selection.': 'captain' };
  context.subscriptions.push(bus.subscribe((event) => {
    for (const [prefix, key] of Object.entries(liveKinds)) {
      const spec = specs.find((s) => s.key === key);
      if (spec && event.type.startsWith(prefix) && resources.get(spec)) void resources.load(spec);
    }
  }));

  context.subscriptions.push(...registerExtraCommands({ store: ops, connection, openPlanning: (id) => sessions.open(PLANNING, id) }));
  context.subscriptions.push(...registerLocalCommands(connection, String((context.extension.packageJSON as { version?: string } | undefined)?.version ?? '')));
  context.subscriptions.push(...registerVesselWorkspace((vesselId) => ops.vesselName(vesselId)));
  context.subscriptions.push(watchNeedsYou(ops, () => vscode.workspace.getConfiguration('armada').get<boolean>('notifications.needsYou', true) !== false));

  // Ask Armada: the chat view at the top of the Armada sidebar.
  const askView = new AskViewProvider({ extensionUri: context.extensionUri, connection, bus, globalState: context.globalState });
  context.subscriptions.push(askView, vscode.window.registerWebviewViewProvider(ASK_VIEW_ID, askView, { webviewOptions: { retainContextWhenHidden: true } }));
  // Conversations: Ask Armada's conversation list (the dashboard Ask page's list), under the chat.
  context.subscriptions.push(...registerConversations(askView, () => connection.connected, connection.onDidChange, (listener) => bus.subscribe((event) => {
    const parsed = parseAskEvent(event as unknown as WebSocketMessage);
    if (parsed?.type === 'ask.thread') listener(parsed.thread, parsed.threadId);
    else if (parsed?.type === 'ask.turn' && parsed.state !== 'started') listener(null, parsed.threadId);
  })));

  // The black box tree, in the Armada container, and in the Explorer when armada.failures.showInExplorer is on (the
  // view's `when` clause reads the setting).

  const failureViews = [FAILURES_EXPLORER_VIEW_ID].map((id) => vscode.window.createTreeView(id, { treeDataProvider: tree, showCollapseAll: true }));
  context.subscriptions.push(...failureViews);
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
      // Display strings in VS Code's language, from the Admiral's catalog (skipped in English).
      const baseUrl = connection.active?.baseUrl;
      if (baseUrl) {
        void loadCatalog(baseUrl, displayLanguage()).then((translated) => {
          if (!translated) return;
          void ops.refreshAll();
              });
      }
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
      resources.clear();
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
  register('armada.ask.openInEditor', () => askView.openInEditor());
  register('armada.tab.open', async (viewId) => {
    const route = typeof viewId === 'string' ? LEGACY_TAB_TARGETS[viewId] : undefined;
    if (viewId === ASK_VIEW_ID) askView.openInEditor();
    else if (route) await vscode.commands.executeCommand(OPEN_PAGE_COMMAND, route);
    else await vscode.commands.executeCommand('armada.page.pick');
  });
  register('armada.nav.openTab', async (node) => {
    if (!isNavNode(node)) return vscode.commands.executeCommand('armada.page.pick');
    return vscode.commands.executeCommand(OPEN_PAGE_COMMAND, node.navKind === 'page' ? node.page.route : node.hub.route, { newTab: true });
  });
  register('armada.nav.refresh', async () => {
    void ops.refreshAll();
    void store.refresh();
    nav.refresh();
    pages.reload();
  });
  register('armada.tab.refresh', async () => {
    void ops.refreshAll();
    pages.reload();
  });
  for (const [viewId, route] of Object.entries(LEGACY_TAB_TARGETS)) register(`armada.tab.${viewId}`, () => vscode.commands.executeCommand(OPEN_PAGE_COMMAND, route));
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
    if (e.affectsConfiguration('armada.language') && connection.active) {
      void loadCatalog(connection.active.baseUrl, displayLanguage()).then(() => {
        void ops.refreshAll();
          });
    }
    if (e.affectsConfiguration('armada.tenant')) store.invalidateVessels();
    if (e.affectsConfiguration('armada.liveUpdates')) restartBus();
    void store.refresh();
  }));

  updateViews();

  // No network work during activate: the first connection attempt (local discovery, health, whoami) runs afterwards.
  const startup = setTimeout(() => void connection.restore(), 0);
  context.subscriptions.push({ dispose: () => clearTimeout(startup) });

  return { connection, events: bus, store, commands, ops, details, ask: askView, resources: { specs, store: resources }, nav, pages, liveMode: () => bus.mode };
}

/** The language for Armada's labels: armada.language, or VS Code's display language when it is "auto". */
function displayLanguage(): string {
  const configured = String(vscode.workspace.getConfiguration('armada').get<string>('language', 'auto') ?? 'auto').trim();
  return !configured || configured === 'auto' ? vscode.env.language : configured;
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
