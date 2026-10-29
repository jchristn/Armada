/*
 * Runs inside the VS Code extension host (see ../runTest.ts). Uses plain assertions so no test framework is needed.
 * There is no Armada server here, so these checks cover activation, registration and the disconnected state.
 */
import * as assert from 'assert';
import * as vscode from 'vscode';

const EXPECTED_COMMANDS = [
  'armada.connect',
  'armada.disconnect',
  'armada.refreshFailures',
  'armada.openBlackBox',
  'armada.copyFixThis',
  'armada.openFixThisInEditor',
  'armada.sendFixThisToChat',
  'armada.runRecoveryPath',
  'armada.resolveEntry',
  'armada.addNote',
  'armada.askArmada',
  'armada.showBlackBoxTimeline',
  'armada.compareBlackBoxAttempts',
  'armada.previewMissionOverlap',
];

export async function run(): Promise<void> {
  const extension = vscode.extensions.getExtension('jchristn.armada-vscode');
  assert.ok(extension, 'the extension is installed in development mode');
  const api = (await extension.activate()) as { store: { status: string }; liveMode(): string };

  const registered = await vscode.commands.getCommands(true);
  for (const command of EXPECTED_COMMANDS) assert.ok(registered.includes(command), `${command} is registered`);

  assert.strictEqual(api.store.status, 'disconnected', 'no stored key means disconnected');
  assert.strictEqual(api.liveMode(), 'stopped');

  // Without a connection, commands show an error and resolve instead of throwing.
  const result = await vscode.commands.executeCommand('armada.copyFixThis', { kind: 'entry', entry: { Id: 'bbx_none', CreatedUtc: new Date().toISOString() } });
  assert.strictEqual(result, undefined);

  await vscode.commands.executeCommand('armada.refreshFailures');
  assert.strictEqual(api.store.status, 'disconnected');
  console.log('[integration] all checks passed');
}
