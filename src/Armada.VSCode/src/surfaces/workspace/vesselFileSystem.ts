/*
 * A vessel's checkout as a VS Code file system (scheme armada-vessel, authority the vessel id), backed by the Admiral's
 * workspace API: the Explorer lists it, the editor opens and saves its files (with the content hash, so a file changed
 * on the server since it was opened is not overwritten), and files and folders can be created, renamed and deleted.
 * This is the dashboard's Workspace page, natively; it works for vessels on a remote Admiral or Harbor too.
 */

import * as vscode from 'vscode';
import * as api from '@dashboard/api/client';
import type { WorkspaceTreeEntry } from '@dashboard/types/models';
import { ApiError } from '@dashboard/api/client';
import { describeError } from '../../transport/errors';

/** URI scheme of vessel workspaces. */
export const VESSEL_SCHEME = 'armada-vessel';

/** The URI of a path in a vessel's workspace. */
export function vesselUri(vesselId: string, relativePath = ''): vscode.Uri {
  return vscode.Uri.from({ scheme: VESSEL_SCHEME, authority: vesselId, path: `/${relativePath.replace(/^\/+/, '')}` });
}

/** The vessel id and relative path of a vessel URI. */
export function vesselPath(uri: vscode.Uri): { vesselId: string; path: string } {
  return { vesselId: uri.authority, path: uri.path.replace(/^\/+/, '').replace(/\/+$/, '') };
}

interface Listing {
  at: number;
  entries: WorkspaceTreeEntry[];
}

/** The file system provider. */
export class VesselFileSystem implements vscode.FileSystemProvider {
  private readonly _OnDidChange = new vscode.EventEmitter<vscode.FileChangeEvent[]>();
  private readonly _Listings = new Map<string, Listing>();
  private readonly _Hashes = new Map<string, string>();

  public readonly onDidChangeFile: vscode.Event<vscode.FileChangeEvent[]> = this._OnDidChange.event;

  public watch(): vscode.Disposable {
    return new vscode.Disposable(() => undefined);
  }

  public async stat(uri: vscode.Uri): Promise<vscode.FileStat> {
    const { vesselId, path } = vesselPath(uri);
    if (!path) return { type: vscode.FileType.Directory, ctime: 0, mtime: 0, size: 0 };
    const parent = path.includes('/') ? path.slice(0, path.lastIndexOf('/')) : '';
    const name = path.slice(path.lastIndexOf('/') + 1);
    const entry = (await this.list(vesselId, parent)).find((e) => e.name === name);
    if (!entry) throw vscode.FileSystemError.FileNotFound(uri);
    const mtime = Date.parse(entry.lastWriteUtc) || 0;
    return {
      type: entry.isDirectory ? vscode.FileType.Directory : vscode.FileType.File,
      ctime: mtime,
      mtime,
      size: entry.sizeBytes ?? 0,
      permissions: entry.isDirectory || entry.isEditable ? undefined : vscode.FilePermission.Readonly,
    };
  }

  public async readDirectory(uri: vscode.Uri): Promise<Array<[string, vscode.FileType]>> {
    const { vesselId, path } = vesselPath(uri);
    const entries = await this.list(vesselId, path, true);
    return entries.map((e) => [e.name, e.isDirectory ? vscode.FileType.Directory : vscode.FileType.File]);
  }

  public async readFile(uri: vscode.Uri): Promise<Uint8Array> {
    const { vesselId, path } = vesselPath(uri);
    const file = await this.call(uri, () => api.getWorkspaceFile(vesselId, path));
    if (file.isBinary) throw vscode.FileSystemError.Unavailable(`${path} is a binary file; the Armada workspace serves text files only.`);
    if (file.previewTruncated || file.isLarge) throw vscode.FileSystemError.Unavailable(`${path} is too large to open from the Armada workspace.`);
    this._Hashes.set(uri.toString(), file.contentHash);
    return new TextEncoder().encode(file.content);
  }

  public async writeFile(uri: vscode.Uri, content: Uint8Array, options: { create: boolean; overwrite: boolean }): Promise<void> {
    const { vesselId, path } = vesselPath(uri);
    let exists = true;
    try {
      await this.stat(uri);
    } catch {
      exists = false;
    }
    if (!exists && !options.create) throw vscode.FileSystemError.FileNotFound(uri);
    if (exists && !options.overwrite) throw vscode.FileSystemError.FileExists(uri);
    const saved = await this.call(uri, () => api.saveWorkspaceFile(vesselId, { path, content: new TextDecoder().decode(content), expectedHash: exists ? this._Hashes.get(uri.toString()) ?? null : null }));
    this._Hashes.set(uri.toString(), saved.contentHash);
    this.invalidate(vesselId, path);
    this._OnDidChange.fire([{ type: exists ? vscode.FileChangeType.Changed : vscode.FileChangeType.Created, uri }]);
  }

  public async createDirectory(uri: vscode.Uri): Promise<void> {
    const { vesselId, path } = vesselPath(uri);
    await this.call(uri, () => api.createWorkspaceDirectory(vesselId, { path }));
    this.invalidate(vesselId, path);
    this._OnDidChange.fire([{ type: vscode.FileChangeType.Created, uri }]);
  }

  public async delete(uri: vscode.Uri): Promise<void> {
    const { vesselId, path } = vesselPath(uri);
    await this.call(uri, () => api.deleteWorkspaceEntry(vesselId, path));
    this.invalidate(vesselId, path);
    this._OnDidChange.fire([{ type: vscode.FileChangeType.Deleted, uri }]);
  }

  public async rename(oldUri: vscode.Uri, newUri: vscode.Uri): Promise<void> {
    const from = vesselPath(oldUri);
    const to = vesselPath(newUri);
    if (from.vesselId !== to.vesselId) throw vscode.FileSystemError.NoPermissions('Files move only within one vessel.');
    await this.call(oldUri, () => api.renameWorkspaceEntry(from.vesselId, { path: from.path, newPath: to.path }));
    this.invalidate(from.vesselId, from.path);
    this.invalidate(to.vesselId, to.path);
    this._OnDidChange.fire([{ type: vscode.FileChangeType.Deleted, uri: oldUri }, { type: vscode.FileChangeType.Created, uri: newUri }]);
  }

  private async list(vesselId: string, path: string, fresh = false): Promise<WorkspaceTreeEntry[]> {
    const key = `${vesselId}:${path}`;
    const cached = this._Listings.get(key);
    if (cached && !fresh && Date.now() - cached.at < 5000) return cached.entries;
    const result = await this.call(vesselUri(vesselId, path), () => api.listWorkspaceDirectory(vesselId, path || undefined));
    this._Listings.set(key, { at: Date.now(), entries: result.entries ?? [] });
    return result.entries ?? [];
  }

  private invalidate(vesselId: string, path: string): void {
    const parent = path.includes('/') ? path.slice(0, path.lastIndexOf('/')) : '';
    this._Listings.delete(`${vesselId}:${parent}`);
    this._Listings.delete(`${vesselId}:${path}`);
  }

  private async call<T>(uri: vscode.Uri, fn: () => Promise<T>): Promise<T> {
    try {
      return await fn();
    } catch (err) {
      if (err instanceof ApiError && err.status === 404) throw vscode.FileSystemError.FileNotFound(uri);
      if (err instanceof ApiError && (err.status === 401 || err.status === 403)) throw vscode.FileSystemError.NoPermissions(describeError(err));
      if (err instanceof ApiError && err.status === 409) throw vscode.FileSystemError.Unavailable(`${describeError(err)} (the file changed on the server; reopen it to see the latest)`);
      throw vscode.FileSystemError.Unavailable(describeError(err));
    }
  }
}

/** Registers the file system and the vessel workspace commands. */
export function registerVesselWorkspace(getVesselName: (vesselId: string) => string): vscode.Disposable[] {
  const fsProvider = new VesselFileSystem();
  const disposables: vscode.Disposable[] = [vscode.workspace.registerFileSystemProvider(VESSEL_SCHEME, fsProvider, { isCaseSensitive: true })];
  const register = (id: string, fn: (...args: unknown[]) => Promise<unknown>): void => {
    disposables.push(vscode.commands.registerCommand(id, async (...args: unknown[]) => {
      try {
        return await fn(...args);
      } catch (err) {
        void vscode.window.showErrorMessage(`Armada: ${describeError(err)}`);
        return undefined;
      }
    }));
  };
  const vesselIdOf = async (arg: unknown, title: string): Promise<string | undefined> => {
    if (typeof arg === 'string') return arg;
    if (arg && typeof arg === 'object' && 'vessel' in arg) return (arg as { vessel: { id: string } }).vessel.id;
    if (arg && typeof arg === 'object' && 'record' in arg) return String((arg as { record: { id?: unknown } }).record.id ?? '');
    const folder = vscode.workspace.workspaceFolders?.find((f) => f.uri.scheme === VESSEL_SCHEME);
    if (folder) return folder.uri.authority;
    void vscode.window.showInformationMessage(`Armada: ${title} works on a vessel; run it from a vessel in the Fleet view.`);
    return undefined;
  };

  register('armada.vessel.openWorkspace', async (arg) => {
    const vesselId = await vesselIdOf(arg, 'Open Vessel Workspace');
    if (!vesselId) return;
    const uri = vesselUri(vesselId);
    if (vscode.workspace.workspaceFolders?.some((f) => f.uri.toString() === uri.toString())) {
      await vscode.commands.executeCommand('workbench.view.explorer');
      return;
    }
    vscode.workspace.updateWorkspaceFolders(vscode.workspace.workspaceFolders?.length ?? 0, 0, { uri, name: `${getVesselName(vesselId)} (Armada)` });
    await vscode.commands.executeCommand('workbench.view.explorer');
  });
  register('armada.vessel.search', async (arg) => {
    const vesselId = await vesselIdOf(arg, 'Search Vessel');
    if (!vesselId) return;
    const query = await vscode.window.showInputBox({ title: `Search ${getVesselName(vesselId)}`, prompt: 'Text to find in the vessel\'s files', ignoreFocusOut: true });
    if (!query) return;
    const result = await api.searchWorkspace(vesselId, query, 200);
    const matches = result.matches ?? [];
    if (!matches.length) {
      void vscode.window.showInformationMessage(`Armada: no matches for "${query}".`);
      return;
    }
    const picked = await vscode.window.showQuickPick(matches.map((m) => ({ label: m.path, description: `line ${m.lineNumber}`, detail: m.preview.trim(), match: m })), { title: `${result.totalMatches} matches for "${query}"${result.truncated ? ' (first 200)' : ''}`, matchOnDetail: true });
    if (!picked) return;
    const editor = await vscode.window.showTextDocument(vesselUri(vesselId, picked.match.path), { preview: true });
    if (picked.match.lineNumber) {
      const line = Math.max(0, picked.match.lineNumber - 1);
      editor.selection = new vscode.Selection(line, 0, line, 0);
      editor.revealRange(new vscode.Range(line, 0, line, 0), vscode.TextEditorRevealType.InCenter);
    }
  });
  register('armada.vessel.exec', async (arg) => {
    const vesselId = await vesselIdOf(arg, 'Run Command in Vessel');
    if (!vesselId) return;
    const command = await vscode.window.showInputBox({ title: `Run a command in ${getVesselName(vesselId)}`, prompt: 'Runs in the vessel\'s checkout on the machine that holds it', placeHolder: 'git log --oneline -5', ignoreFocusOut: true });
    if (!command) return;
    const result = await vscode.window.withProgress({ location: vscode.ProgressLocation.Notification, title: `Armada: running "${command}"...` }, () => api.execWorkspaceCommand(vesselId, command, 120));
    const text = [`$ ${result.command}`, `# in ${result.workingDirectory}, exit code ${result.exitCode}${result.timedOut ? ' (timed out)' : ''}`, '', result.stdout, result.stderr ? `\n# stderr\n${result.stderr}` : ''].join('\n');
    await vscode.window.showTextDocument(await vscode.workspace.openTextDocument({ language: 'shellscript', content: text }), { preview: false });
  });
  register('armada.vessel.changes', async (arg) => {
    const vesselId = await vesselIdOf(arg, 'Show Vessel Changes');
    if (!vesselId) return;
    const diff = await api.getWorkspaceDiff(vesselId);
    const text = diff.diff || (diff.error ? `# ${diff.error}` : '# The vessel\'s checkout has no uncommitted changes.');
    await vscode.window.showTextDocument(await vscode.workspace.openTextDocument({ language: 'diff', content: text }), { preview: false });
  });
  register('armada.vessel.openTerminal', async (arg) => {
    const vesselId = await vesselIdOf(arg, 'Open Terminal on Host');
    if (!vesselId) return;
    await api.openWorkspaceTerminal(vesselId);
    void vscode.window.showInformationMessage(`Armada: opened a terminal at ${getVesselName(vesselId)} on the machine that holds it.`);
  });
  return disposables;
}
