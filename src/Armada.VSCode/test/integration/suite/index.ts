/*
 * Runs inside the VS Code extension host (see ../runTest.ts). Uses plain assertions so no test framework is needed.
 *
 * Always: the extension (its ID read from package.json) activates, every contributed command is registered, and the
 * Armada view container and its views exist and open. Disconnected run (default): the local discovery finds no
 * Admiral and every surface shows the disconnected state. Connected run (ARMADA_VSCODE_EXPECT_ADMIRAL=1, against a
 * throwaway Admiral): connect, whoami, role keys, the navigation tree, the black box store and the event bus
 * subscription all work.
 */
import * as assert from 'assert';
import { execFile } from 'child_process';
import * as fs from 'fs';
import * as path from 'path';
import * as vscode from 'vscode';
import type { ArmadaExtensionApi } from '../../../src/extension';

interface Manifest {
  name: string;
  publisher: string;
  contributes: { commands: Array<{ command: string }>; views: { armada: Array<{ id: string }> } & Record<string, Array<{ id: string }>>; viewsContainers: { activitybar: Array<{ id: string }> } };
}

const ROOT = path.resolve(__dirname, '..', '..', '..', '..');
const MANIFEST = JSON.parse(fs.readFileSync(path.join(ROOT, 'package.json'), 'utf8')) as Manifest;

async function waitFor(condition: () => boolean, timeoutMs: number, label: string): Promise<void> {
  const deadline = Date.now() + timeoutMs;
  while (!condition()) {
    if (Date.now() > deadline) throw new Error(`timed out waiting for ${label}`);
    await new Promise((r) => setTimeout(r, 100));
  }
}

function log(line: string): void {
  console.log(`[integration] ${line}`);
}

async function capture(dir: string, name: string): Promise<void> {
  if (process.platform !== 'darwin') return;
  fs.mkdirSync(dir, { recursive: true });
  await new Promise((r) => setTimeout(r, 2500));
  const file = path.join(dir, `${name}.png`);
  await new Promise<void>((resolve) => execFile('screencapture', ['-x', file], (err) => {
    log(err ? `screenshot ${name} failed: ${err.message}` : `screenshot ${file}`);
    resolve();
  }));
}

async function screenshots(dir: string): Promise<void> {
  if (process.platform !== 'darwin') {
    log('screenshots skipped: only macOS (screencapture) is supported');
    return;
  }
  fs.mkdirSync(dir, { recursive: true });
  const config = vscode.workspace.getConfiguration('workbench');
  for (const [theme, name] of [['Default Light Modern', 'light'], ['Default Dark Modern', 'dark']]) {
    await config.update('colorTheme', theme, vscode.ConfigurationTarget.Global);
    await vscode.commands.executeCommand('workbench.view.extension.armada');
    await new Promise((r) => setTimeout(r, 2500));
    const file = path.join(dir, `armada-container-${name}.png`);
    await new Promise<void>((resolve) => execFile('screencapture', ['-x', file], (err) => {
      log(err ? `screenshot ${name} failed: ${err.message}` : `screenshot ${file}`);
      resolve();
    }));
  }
}

export async function run(): Promise<void> {
  const extensionId = `${MANIFEST.publisher}.${MANIFEST.name}`;
  const extension = vscode.extensions.getExtension(extensionId);
  assert.ok(extension, `the extension ${extensionId} is installed in development mode`);
  const api = (await extension.activate()) as ArmadaExtensionApi;

  const registered = await vscode.commands.getCommands(true);
  for (const { command } of MANIFEST.contributes.commands) assert.ok(registered.includes(command), `${command} is registered`);

  const container = MANIFEST.contributes.viewsContainers.activitybar[0].id;
  assert.strictEqual(container, 'armada');
  assert.ok(registered.includes(`workbench.view.extension.${container}`), 'the Armada view container exists');
  for (const view of MANIFEST.contributes.views.armada) assert.ok(registered.includes(`${view.id}.focus`), `view ${view.id} exists`);
  await vscode.commands.executeCommand(`workbench.view.extension.${container}`);
  for (const view of MANIFEST.contributes.views.armada) await vscode.commands.executeCommand(`${view.id}.focus`);
  assert.strictEqual(vscode.workspace.getConfiguration('armada').get('failures.showInExplorer'), false, 'the Explorer copy is off by default');

  // The first connection attempt runs after activation.
  await waitFor(() => api.connection.state.phase === 'connected' || api.connection.state.phase === 'failed', 30000, 'the startup connection attempt');
  const state = api.connection.state;
  log(`connection: ${state.phase} ${state.target?.baseUrl ?? ''} ${state.failure?.message ?? ''}`);

  if (process.env.ARMADA_VSCODE_EXPECT_ADMIRAL === '1') {
    assert.strictEqual(state.phase, 'connected', `connected to the throwaway Admiral (${state.failure?.message ?? ''})`);
    assert.strictEqual(state.target?.kind, 'local', 'found through ARMADA_DATA_DIR/settings.json with no prompt');
    assert.strictEqual(state.contractVersion, '1.0');
    assert.ok(state.identity, 'whoami returned an identity');
    assert.strictEqual(state.identity.globalAdmin, true, 'the local API key is a global admin (armada.role.globalAdmin)');
    assert.strictEqual(state.identity.tenantAdmin, true, 'armada.role.tenantAdmin');
    await waitFor(() => api.events.mode === 'websocket', 15000, 'the /ws event bus');
    await waitFor(() => api.events.received > 0, 15000, 'the status.snapshot reply to subscribe');
    await waitFor(() => api.store.status === 'ready', 15000, 'the black box store');
    log(`whoami ${state.identity.email ?? ''}; events received ${api.events.received}; store ${api.store.status} with ${api.store.entries.length} entries`);

    // The native views load live data, and a mission opens as a native detail page and its log as a document.
    for (const domain of ['missions', 'voyages', 'captains', 'fleet', 'merge', 'inbox'] as const) {
      await waitFor(() => api.ops.state(domain) === 'ready', 20000, `the ${domain} view`);
    }
    log(`ops: ${api.ops.missions.length} missions, ${api.ops.voyages.length} voyages, ${api.ops.captains.length} captains, ${api.ops.vessels.length} vessels, ${api.ops.mergeQueue.length} merge entries, ${api.ops.inbox.length} need you`);
    for (const view of MANIFEST.contributes.views.armada) await vscode.commands.executeCommand(`${view.id}.focus`);
    // Every resource kind loads from the Admiral, and a record opens as a page.
    const failed: string[] = [];
    for (const spec of api.resources.specs.filter((s) => s.key !== 'blackBoxEntry')) {
      await api.resources.store.load(spec);
      const loaded = api.resources.store.get(spec);
      if (loaded?.state !== 'ready') failed.push(`${spec.key}: ${loaded?.error ?? loaded?.state}`);
    }
    log(`resources: ${api.resources.specs.map((s) => `${s.key} ${api.resources.store.get(s)?.records.length ?? '?'}`).join(', ')}`);
    assert.deepStrictEqual(failed, [], 'every resource kind loads');
    const withRecords = api.resources.specs.find((s) => (api.resources.store.get(s)?.records.length ?? 0) > 0 && s.get);
    if (withRecords) {
      const record = api.resources.store.get(withRecords)?.records[0] ?? {};
      await vscode.commands.executeCommand('armada.resource.open', withRecords.key, String(record[withRecords.idKey ?? 'id'] ?? ''));
      log(`resource page: ${withRecords.label} opened`);
    }

    // A vessel's checkout is a VS Code file system: list its root and read a file.
    const local = api.ops.vessels.find((v) => v.localPath && fs.existsSync(v.localPath));
    if (local) {
      const rootUri = vscode.Uri.from({ scheme: 'armada-vessel', authority: local.id, path: '/' });
      const entries = await vscode.workspace.fs.readDirectory(rootUri);
      assert.ok(entries.length > 0, `the workspace of ${local.name} lists files`);
      const file = entries.find(([name, type]) => type === vscode.FileType.File && /\.(md|json|txt|ts|cs|yml)$/i.test(name));
      if (file) {
        const bytes = await vscode.workspace.fs.readFile(vscode.Uri.from({ scheme: 'armada-vessel', authority: local.id, path: `/${file[0]}` }));
        assert.ok(bytes.length > 0, `${file[0]} reads`);
      }
      log(`vessel workspace: ${local.name} lists ${entries.length} entries${file ? `, read ${file[0]}` : ''}`);
    } else {
      log('vessel workspace: no vessel with a local checkout on this machine');
    }

    // The Armada tree: the dashboard's hubs, with live rows under them.
    const label = async (el: unknown): Promise<string> => {
      const item = await api.nav.getTreeItem(el);
      return typeof item.label === 'string' ? item.label : item.label?.label ?? '';
    };
    const hubs = await api.nav.getChildren();
    const hubLabels = await Promise.all(hubs.map(label));
    assert.deepStrictEqual(hubLabels, ['Armada Overview', 'Needs You', 'Dispatch', 'Fleet Actions', 'Missions', 'Activity', 'Vessels', 'Captains', 'Delivery', 'Configuration', 'API Explorer', 'Settings']);
    const missionTabs = await Promise.all((await api.nav.getChildren(hubs[hubLabels.indexOf('Missions')])).map(label));
    assert.deepStrictEqual(missionTabs.slice(0, 4), ['Missions', 'Voyages', 'Merge Queue', 'Black Box']);
    const missionGroups = await Promise.all((await api.nav.getChildren((await api.nav.getChildren(hubs[hubLabels.indexOf('Missions')]))[0])).map(label));
    assert.ok(missionGroups.length > 0, 'the Missions tab lists its groups');
    const configuration = await Promise.all((await api.nav.getChildren(hubs[hubLabels.indexOf('Configuration')])).map(label));
    assert.ok(configuration.includes('Personas') && configuration.includes('Harbors'), `Configuration kinds: ${configuration.join(', ')}`);
    const vessels = await api.nav.getChildren(hubs[hubLabels.indexOf('Vessels')]);
    assert.ok(vessels.length > 0, 'the Vessels hub lists fleets');
    log(`armada tree: ${hubLabels.length} hubs; Missions groups ${missionGroups.join(', ')}; Configuration ${configuration.length} kinds; Vessels ${vessels.length} rows`);
    await vscode.commands.executeCommand('armadaNav.focus');
    await vscode.commands.executeCommand('armadaConversations.focus');

    await vscode.commands.executeCommand('armada.home');
    log('home overview opened');
    await vscode.commands.executeCommand('armada.charts');
    log('activity charts opened');
    await vscode.commands.executeCommand('armada.tab.armadaMissions');
    await vscode.commands.executeCommand('armada.tab.armadaDelivery');
    await vscode.commands.executeCommand('armada.ask.openInEditor');
    log('Missions, Delivery and Ask Armada opened as editor tabs');

    // Ask Armada: the chat view opens and shows an earlier conversation from the Admiral.
    const latest = await api.ask.latestConversationId();
    if (latest) {
      await api.ask.openConversation(latest);
      await waitFor(() => api.ask.shown.threadId === latest && api.ask.shown.messages > 0, 15000, 'the Ask conversation to load');
      log(`ask: conversation ${latest} with ${api.ask.shown.messages} messages`);
      const shots = process.env.ARMADA_VSCODE_SCREENSHOT_DIR;
      if (shots) await capture(shots, 'armada-ask');
    } else {
      await vscode.commands.executeCommand('armadaAsk.focus');
      log('ask: no earlier conversations on this Admiral; the chat view opened empty');
    }
    const mission = api.ops.missions[0];
    if (mission) {
      await vscode.commands.executeCommand('armada.mission.open', mission.id);
      await waitFor(() => api.details.openCount === 1, 10000, 'the mission page');
      await vscode.commands.executeCommand('armada.mission.viewLog', mission.id);
      await waitFor(() => vscode.window.activeTextEditor?.document.uri.scheme === 'armada', 10000, 'the mission log document');
      log(`mission ${mission.id}: page open, log document ${vscode.window.activeTextEditor?.document.uri.path} (${vscode.window.activeTextEditor?.document.lineCount} lines)`);
      const shots = process.env.ARMADA_VSCODE_SCREENSHOT_DIR;
      if (shots) {
        await vscode.commands.executeCommand('workbench.action.closeActiveEditor');
        await vscode.commands.executeCommand(`${MANIFEST.contributes.views.armada[1].id}.focus`);
        await capture(shots, 'armada-native-mission');
      }
    }
  } else {
    assert.strictEqual(state.phase, 'failed');
    assert.strictEqual(state.failure?.kind, 'noAdmiral', 'no Admiral, Harbor or TUI profile in the empty data directory');
    assert.strictEqual(api.store.status, 'disconnected');
    assert.strictEqual(api.liveMode(), 'stopped');
    assert.strictEqual(api.ops.state('missions'), 'disconnected', 'the welcome view shows instead of the trees');
    // Without a connection, commands show an error and resolve instead of throwing.
    const result = await vscode.commands.executeCommand('armada.copyFixThis', { kind: 'entry', entry: { id: 'bbx_none', createdUtc: new Date().toISOString() } });
    assert.strictEqual(result, undefined);
  }

  const shots = process.env.ARMADA_VSCODE_SCREENSHOT_DIR;
  if (shots) await screenshots(shots);
  log('all checks passed');
}
