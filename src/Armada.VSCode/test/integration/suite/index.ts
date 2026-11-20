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
