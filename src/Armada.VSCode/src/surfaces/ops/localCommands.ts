/*
 * What the Harbor tray app and the dashboard's chrome offer besides the data views: notifications when something new
 * needs you, the local Harbor's logs, folder, id and diagnostics, the Admiral's MCP URL, documentation and issue
 * reporting.
 */

import { execFile, spawn } from 'child_process';
import * as fs from 'fs';
import * as os from 'os';
import * as path from 'path';
import * as vscode from 'vscode';
import { changePassword, configureClient, downloadBackup, restoreBackup, searchBlackBox } from '@dashboard/api/client';
import type { InboxItem } from '@dashboard/types/models';
import { ConnectionManager } from '../../connection/connectionManager';
import { harborDirectory, isHarborRunning } from '../../connection/endpoints';
import { describeError } from '../../transport/errors';
import { readHealth } from '../../transport/hostClient';
import { NAV_REVEAL_COMMAND } from '../nav/navTree';
import { OpsStore } from './opsStore';

/** The project's home page and issue tracker. */
const REPOSITORY_URL = 'https://github.com/jchristn/armada';

/** A stable key for an inbox item. */
export function inboxKey(item: InboxItem): string {
  return `${item.kind}:${item.entityId ?? item.title}`;
}

/** Items in `now` that were not in `before` (by key). Pure, for tests. */
export function newInboxItems(before: Set<string> | undefined, now: InboxItem[]): InboxItem[] {
  if (!before) return [];
  return now.filter((item) => !before.has(inboxKey(item)));
}

/** Shows a notification for each new Needs You item (at most three; more are summarized). */
export function watchNeedsYou(store: OpsStore, enabled: () => boolean): vscode.Disposable {
  let seen: Set<string> | undefined;
  const subscription = store.onDidChange((domain) => {
    if (domain !== 'inbox') return;
    if (store.state('inbox') !== 'ready') {
      if (store.state('inbox') === 'disconnected') seen = undefined;
      return;
    }
    const fresh = newInboxItems(seen, store.inbox);
    seen = new Set(store.inbox.map(inboxKey));
    if (!fresh.length || !enabled()) return;
    if (fresh.length > 3) {
      void vscode.window.showWarningMessage(`Armada: ${fresh.length} new items need you.`, 'Show').then((choice) => {
        if (choice === 'Show') void vscode.commands.executeCommand(NAV_REVEAL_COMMAND, 'needsYou');
      });
      return;
    }
    for (const item of fresh) {
      void vscode.window.showWarningMessage(`Armada: ${item.title}`, 'Open').then((choice) => {
        if (choice === 'Open') void vscode.commands.executeCommand('armada.inbox.open', item);
      });
    }
  });
  return { dispose: () => subscription.dispose() };
}

interface HarborSettingsFile {
  HarborId?: string;
  Name?: string;
  ServerLinkUrl?: string;
}

function readHarborSettings(): HarborSettingsFile | undefined {
  try {
    const parsed = JSON.parse(fs.readFileSync(path.join(harborDirectory(), 'settings.json'), 'utf8').replace(/^﻿/, '')) as HarborSettingsFile;
    return parsed && typeof parsed === 'object' ? parsed : undefined;
  } catch {
    return undefined;
  }
}

/** Registers the commands; returns their disposables. */
export function registerLocalCommands(connection: ConnectionManager, extensionVersion: string): vscode.Disposable[] {
  const disposables: vscode.Disposable[] = [];
  const register = (id: string, fn: () => Promise<unknown>): void => {
    disposables.push(vscode.commands.registerCommand(id, async () => {
      try {
        return await fn();
      } catch (err) {
        void vscode.window.showErrorMessage(`Armada: ${describeError(err)}`);
        return undefined;
      }
    }));
  };

  register('armada.harbor.openLogs', async () => {
    const dir = path.join(harborDirectory(), 'logs');
    const logs = fs.existsSync(dir) ? fs.readdirSync(dir).filter((f) => f.startsWith('harbor.log')).map((f) => path.join(dir, f)).sort((a, b) => fs.statSync(b).mtimeMs - fs.statSync(a).mtimeMs) : [];
    if (!logs.length) {
      void vscode.window.showInformationMessage(`Armada: no Harbor log in ${dir}. Is Armada Harbor installed on this machine?`);
      return;
    }
    const editor = await vscode.window.showTextDocument(vscode.Uri.file(logs[0]), { preview: false });
    const end = editor.document.lineAt(Math.max(0, editor.document.lineCount - 1)).range.end;
    editor.revealRange(new vscode.Range(end, end));
  });
  register('armada.harbor.openFolder', async () => {
    const dir = harborDirectory();
    if (!fs.existsSync(dir)) {
      void vscode.window.showInformationMessage(`Armada: ${dir} does not exist. Is Armada Harbor installed on this machine?`);
      return;
    }
    await vscode.commands.executeCommand('revealFileInOS', vscode.Uri.file(path.join(dir, 'settings.json')));
  });
  register('armada.harbor.copyId', async () => {
    const id = readHarborSettings()?.HarborId;
    if (!id) {
      void vscode.window.showInformationMessage('Armada: no Harbor settings on this machine.');
      return;
    }
    await vscode.env.clipboard.writeText(id);
    void vscode.window.showInformationMessage(`Armada: copied the Harbor id ${id}.`);
  });
  register('armada.copyMcpUrl', async () => {
    const active = connection.requireActive();
    const health = await readHealth();
    const host = new URL(active.baseUrl).hostname;
    const url = `http://${host}:${health.mcpPort ?? 7891}/mcp`;
    await vscode.env.clipboard.writeText(url);
    void vscode.window.showInformationMessage(`Armada: copied the MCP URL ${url}.`);
  });
  register('armada.copyDiagnostics', async () => {
    const state = connection.state;
    const harbor = readHarborSettings();
    const lines = [
      `Armada for VS Code ${extensionVersion}`,
      `VS Code ${vscode.version} on ${process.platform} ${process.arch}`,
      `Connection: ${state.phase}${state.target ? ` to ${state.target.baseUrl} (${state.target.kind}, found in ${state.target.source})` : ''}`,
      state.serverVersion ? `Admiral: Armada ${state.serverVersion}, contract ${state.contractVersion ?? 'not reported'}` : '',
      state.failure ? `Last failure: ${state.failure.message}` : '',
      harbor ? `Harbor: ${harbor.Name ?? ''} ${harbor.HarborId ?? ''}, linked to ${harbor.ServerLinkUrl ?? 'nothing'}` : 'Harbor: not configured on this machine',
    ].filter(Boolean);
    await vscode.env.clipboard.writeText(lines.join('\n'));
    void vscode.window.showInformationMessage('Armada: diagnostics copied (no keys or tokens).');
  });
  // Files the API hands over (backups, black box bundles) are saved through a save dialog.
  configureClient({
    platform: {
      saveFile: async (file) => {
        const folder = vscode.workspace.workspaceFolders?.[0]?.uri;
        const target = await vscode.window.showSaveDialog({ title: 'Armada: Save File', defaultUri: folder ? vscode.Uri.joinPath(folder, file.filename) : undefined });
        if (!target) return;
        await vscode.workspace.fs.writeFile(target, new Uint8Array(await file.body.arrayBuffer()));
        void vscode.window.showInformationMessage(`Armada: saved ${target.fsPath}.`);
      },
    },
  });
  register('armada.backup.download', async () => {
    connection.requireActive();
    await vscode.window.withProgress({ location: vscode.ProgressLocation.Notification, title: 'Armada: preparing the backup...' }, () => downloadBackup());
  });
  register('armada.backup.restore', async () => {
    connection.requireActive();
    const picked = await vscode.window.showOpenDialog({ canSelectMany: false, filters: { 'Armada backup': ['zip'] }, title: 'Armada: Restore from Backup' });
    if (!picked?.[0]) return;
    const choice = await vscode.window.showWarningMessage(`Restore the Admiral from ${path.basename(picked[0].fsPath)}? This replaces the Admiral's current data and restarts it.`, { modal: true }, 'Restore');
    if (choice !== 'Restore') return;
    const bytes = await vscode.workspace.fs.readFile(picked[0]);
    await vscode.window.withProgress({ location: vscode.ProgressLocation.Notification, title: 'Armada: restoring...' }, () => restoreBackup({ name: path.basename(picked[0].fsPath), arrayBuffer: async () => bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength) as ArrayBuffer }));
    void vscode.window.showInformationMessage('Armada: restore started. The Admiral restarts when it finishes.');
  });
  register('armada.account.changePassword', async () => {
    connection.requireActive();
    const currentPassword = await vscode.window.showInputBox({ title: 'Change Password (1/2)', prompt: 'Current password', password: true, ignoreFocusOut: true });
    if (currentPassword === undefined) return;
    const newPassword = await vscode.window.showInputBox({ title: 'Change Password (2/2)', prompt: 'New password', password: true, ignoreFocusOut: true, validateInput: (v) => (v.length < 8 ? 'Use at least 8 characters' : undefined) });
    if (newPassword === undefined) return;
    await changePassword({ currentPassword, newPassword } as never);
    void vscode.window.showInformationMessage('Armada: password changed.');
  });
  register('armada.blackbox.search', async () => {
    connection.requireActive();
    const query = await vscode.window.showInputBox({ title: 'Search the black box', prompt: 'Words to find in failure records (messages, files, commands)', ignoreFocusOut: true });
    if (!query?.trim()) return;
    const result = await searchBlackBox({ query: query.trim(), pageNumber: 1, pageSize: 100 } as never);
    const entries = result.objects ?? [];
    if (!entries.length) {
      void vscode.window.showInformationMessage(`Armada: no black box entries match "${query.trim()}".`);
      return;
    }
    const picked = await vscode.window.showQuickPick(entries.map((e) => ({ label: e.missionTitle ?? e.missionId ?? e.voyageId ?? e.id, description: e.failureKind ?? e.phase ?? '', detail: e.id, id: e.id })), { title: `${result.totalRecords ?? entries.length} entries match "${query.trim()}"`, matchOnDetail: true });
    if (picked) await vscode.commands.executeCommand('armada.openBlackBox', picked.id);
  });
  // The local Harbor process (the tray app's link controls): start, stop, restart, show.
  register('armada.harbor.start', async () => {
    if (await isHarborRunning()) {
      void vscode.window.showInformationMessage('Armada: Harbor is already running on this machine.');
      return;
    }
    const app = harborApp();
    if (!app) {
      void vscode.window.showErrorMessage('Armada: Armada Harbor is not installed on this machine. Install it from the Armada release, or run "armada-harbor --headless".');
      return;
    }
    if (app.macApp) spawn('open', ['-a', app.macApp], { detached: true, stdio: 'ignore' }).unref();
    else spawn(app.executable, [], { detached: true, stdio: 'ignore' }).unref();
    void vscode.window.showInformationMessage('Armada: starting Harbor. It links to the Admiral in its settings.');
  });
  register('armada.harbor.stop', async () => {
    if (!(await isHarborRunning())) {
      void vscode.window.showInformationMessage('Armada: Harbor is not running on this machine.');
      return;
    }
    if ((await vscode.window.showWarningMessage('Stop Armada Harbor on this machine? Captains it is running stop with it.', { modal: true }, 'Stop Harbor')) !== 'Stop Harbor') return;
    await stopHarborProcess();
    void vscode.window.showInformationMessage('Armada: Harbor stopped.');
  });
  register('armada.harbor.restart', async () => {
    if ((await vscode.window.showWarningMessage('Restart Armada Harbor on this machine? It drops and re-establishes its link to the Admiral.', { modal: true }, 'Restart Harbor')) !== 'Restart Harbor') return;
    await stopHarborProcess();
    await new Promise((r) => setTimeout(r, 1500));
    await vscode.commands.executeCommand('armada.harbor.start');
  });
  register('armada.harbor.show', async () => {
    const app = harborApp();
    if (app?.macApp) {
      spawn('open', ['-a', app.macApp], { detached: true, stdio: 'ignore' }).unref();
      return;
    }
    await vscode.commands.executeCommand('armada.resource.refresh', 'harbor');
    await vscode.commands.executeCommand('armadaSystem.focus');
  });
  register('armada.mcp.install', async () => {
    const cli = armadaCli();
    if (!cli) {
      void vscode.window.showErrorMessage('Armada: the armada CLI is not installed ("dotnet tool install -g Armada.Helm", or an Armada installer).');
      return;
    }
    const terminal = vscode.window.createTerminal({ name: 'Armada MCP Install' });
    terminal.show();
    terminal.sendText(`"${cli}" mcp install`);
  });
  register('armada.about', async () => {
    const state = connection.state;
    const lines = [
      `Armada for VS Code ${extensionVersion}`,
      state.serverVersion ? `Admiral: Armada ${state.serverVersion} (contract ${state.contractVersion ?? 'not reported'}) at ${state.target?.baseUrl ?? ''}` : 'Not connected to an Admiral',
      state.identity?.email ? `Signed in as ${state.identity.email}${state.identity.tenantName ? ` (${state.identity.tenantName})` : ''}` : '',
      `Harbor on this machine: ${(await isHarborRunning()) ? 'running' : harborApp() ? 'installed, not running' : 'not installed'}`,
    ].filter(Boolean);
    const choice = await vscode.window.showInformationMessage(lines.join('\n'), { modal: true }, 'Copy Diagnostics', 'Documentation');
    if (choice === 'Copy Diagnostics') await vscode.commands.executeCommand('armada.copyDiagnostics');
    if (choice === 'Documentation') await vscode.commands.executeCommand('armada.docs');
  });
  register('armada.docs', async () => {
    await vscode.env.openExternal(vscode.Uri.parse(`${REPOSITORY_URL}#readme`));
  });
  register('armada.reportIssue', async () => {
    await vscode.env.openExternal(vscode.Uri.parse(`${REPOSITORY_URL}/issues/new`));
  });
  return disposables;
}

/** The installed Harbor: a macOS app bundle, or an executable on PATH. */
function harborApp(): { macApp?: string; executable: string } | undefined {
  if (process.platform === 'darwin') {
    for (const dir of ['/Applications', path.join(os.homedir(), 'Applications')]) {
      const bundle = path.join(dir, 'Armada Harbor.app');
      if (fs.existsSync(bundle)) return { macApp: bundle, executable: path.join(bundle, 'Contents', 'MacOS', 'Armada.Harbor') };
    }
  }
  const onPath = findOnPath(process.platform === 'win32' ? ['armada-harbor.exe', 'Armada.Harbor.exe'] : ['armada-harbor', 'Armada.Harbor']);
  return onPath ? { executable: onPath } : undefined;
}

/** The armada CLI on PATH or in the .NET tools folder. */
function armadaCli(): string | undefined {
  const name = process.platform === 'win32' ? 'armada.exe' : 'armada';
  return findOnPath([name]) ?? [path.join(os.homedir(), '.dotnet', 'tools', name)].find((p) => fs.existsSync(p));
}

function findOnPath(names: string[]): string | undefined {
  for (const dir of (process.env.PATH ?? '').split(path.delimiter)) {
    for (const name of names) {
      const candidate = path.join(dir, name);
      if (dir && fs.existsSync(candidate)) return candidate;
    }
  }
  return undefined;
}

function stopHarborProcess(): Promise<void> {
  return new Promise((resolve) => {
    const done = (): void => resolve();
    if (process.platform === 'win32') execFile('taskkill', ['/IM', 'Armada.Harbor.exe', '/T'], done);
    else execFile('pkill', ['-f', 'Armada.Harbor'], done);
  });
}

