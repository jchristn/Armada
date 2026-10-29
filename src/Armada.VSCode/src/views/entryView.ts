import * as crypto from 'crypto';
import * as vscode from 'vscode';
import { ArmadaBlackBoxClient, describeError } from '../api/blackbox';
import { BlackBoxEntrySummary, BlackBoxNote } from '../api/types';
import { entryLabel } from '../util/format';
import { escapeHtml, isEntryViewMessage, renderEntryHtml } from './entryHtml';

/** Map from webview actions to extension commands. */
const ACTION_COMMANDS: Record<string, string> = {
  copyFixThis: 'armada.copyFixThis',
  openFixThis: 'armada.openFixThisInEditor',
  sendToChat: 'armada.sendFixThisToChat',
  runRecovery: 'armada.runRecoveryPath',
  resolve: 'armada.resolveEntry',
  addNote: 'armada.addNote',
  askArmada: 'armada.askArmada',
};

/** Shows black box entries in webview panels, one panel per entry. */
export class EntryViewManager implements vscode.Disposable {
  private readonly _GetClient: () => ArmadaBlackBoxClient;
  private readonly _Panels = new Map<string, vscode.WebviewPanel>();

  /**
   * @throws Error when getClient is missing.
   */
  public constructor(getClient: () => ArmadaBlackBoxClient) {
    if (!getClient) throw new Error('getClient is required');
    this._GetClient = getClient;
  }

  /** Opens (or reveals) the panel for an entry and renders it. */
  public async open(entry: BlackBoxEntrySummary): Promise<void> {
    let panel = this._Panels.get(entry.Id);
    if (panel) {
      panel.reveal();
    } else {
      panel = vscode.window.createWebviewPanel('armadaBlackBox', `Black Box: ${entry.MissionId ?? entry.VoyageId ?? entry.Id}`, vscode.ViewColumn.Active, {
        enableScripts: true,
        retainContextWhenHidden: false,
        localResourceRoots: [],
      });
      const created = panel;
      this._Panels.set(entry.Id, created);
      created.onDidDispose(() => this._Panels.delete(entry.Id));
      created.webview.onDidReceiveMessage((message: unknown) => {
        if (!isEntryViewMessage(message)) return;
        if (message.action === 'refresh') {
          void this.render(created, entry);
          return;
        }
        const command = ACTION_COMMANDS[message.action];
        if (!command) return;
        if (message.action === 'runRecovery') void vscode.commands.executeCommand(command, entry, message.pathId ?? undefined);
        else void vscode.commands.executeCommand(command, entry);
      });
    }
    await this.render(panel, entry);
  }

  public dispose(): void {
    for (const panel of this._Panels.values()) panel.dispose();
    this._Panels.clear();
  }

  private async render(panel: vscode.WebviewPanel, summary: BlackBoxEntrySummary): Promise<void> {
    const nonce = crypto.randomBytes(16).toString('base64');
    try {
      const client = this._GetClient();
      const entry = await client.getEntry(summary.Id);
      let notes: BlackBoxNote[] = [];
      try {
        notes = (await client.listNotes(summary.Id)).Objects;
      } catch {
        notes = [];
      }
      panel.title = `Black Box: ${entry.MissionId ?? entry.VoyageId ?? entry.Id}`;
      panel.webview.html = renderEntryHtml(entry, notes, nonce, panel.webview.cspSource);
    } catch (err) {
      panel.webview.html = `<!DOCTYPE html><html><head><meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline';"></head><body style="font-family: var(--vscode-font-family); padding: 16px;"><h2>${escapeHtml(entryLabel(summary))}</h2><p>${escapeHtml(describeError(err))}</p></body></html>`;
    }
  }
}
