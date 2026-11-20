import * as crypto from 'crypto';
import * as vscode from 'vscode';
import { getBlackBox, getVesselBlackBoxDoctor, listBlackBoxNotes } from '@dashboard/api/client';
import type { BlackBoxDoctorPage, BlackBoxEntrySummary, BlackBoxNote } from '@dashboard/types/blackbox';
import { describeError } from '../../transport/errors';
import { entryLabel } from '../../util/format';
import { CommandConnection } from './blackBoxCommands';
import { isEntryViewMessage, renderEntryErrorHtml, renderEntryHtml } from './entryHtml';

/** Map from webview actions to extension commands. */
const ACTION_COMMANDS: Record<string, string> = {
  copyFixThis: 'armada.copyFixThis',
  openFixThis: 'armada.openFixThisInEditor',
  sendToChat: 'armada.sendFixThisToChat',
  runRecovery: 'armada.runRecoveryPath',
  resolve: 'armada.resolveEntry',
  addNote: 'armada.addNote',
  askArmada: 'armada.askArmada',
  timeline: 'armada.showBlackBoxTimeline',
  compareAttempts: 'armada.compareBlackBoxAttempts',
  draftFix: 'armada.draftArmadaFixMission',
};

/** Shows black box entries in webview panels, one panel per entry. */
export class EntryViewManager implements vscode.Disposable {
  private readonly _RequireConnection: () => CommandConnection;
  private readonly _Panels = new Map<string, vscode.WebviewPanel>();

  /**
   * @throws Error when requireConnection is missing.
   */
  public constructor(requireConnection: () => CommandConnection) {
    if (!requireConnection) throw new Error('requireConnection is required');
    this._RequireConnection = requireConnection;
  }

  /** Opens (or reveals) the panel for an entry and renders it. */
  public async open(entry: BlackBoxEntrySummary): Promise<void> {
    let panel = this._Panels.get(entry.id);
    if (panel) {
      panel.reveal();
    } else {
      panel = vscode.window.createWebviewPanel('armadaBlackBox', `Black Box: ${entry.missionId ?? entry.voyageId ?? entry.id}`, vscode.ViewColumn.Active, {
        enableScripts: true,
        retainContextWhenHidden: false,
        localResourceRoots: [],
      });
      const created = panel;
      this._Panels.set(entry.id, created);
      created.onDidDispose(() => this._Panels.delete(entry.id));
      created.webview.onDidReceiveMessage((message: unknown) => {
        if (!isEntryViewMessage(message)) return;
        if (message.action === 'refresh') {
          void this.render(created, entry);
          return;
        }
        if (message.action === 'openRoute' && message.route) {
          const [command, arg] = nativeCommandForRoute(message.route);
          void vscode.commands.executeCommand(command, arg);
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
      const connection = this._RequireConnection();
      const entry = await getBlackBox(summary.id);
      let notes: BlackBoxNote[] = [];
      try {
        notes = (await listBlackBoxNotes(summary.id)).objects;
      } catch {
        notes = [];
      }
      let doctor: BlackBoxDoctorPage | null = null;
      if (entry.vesselId) {
        try {
          doctor = await getVesselBlackBoxDoctor(entry.vesselId, { pageNumber: 1, pageSize: 5 });
        } catch {
          doctor = null;
        }
      }
      panel.title = `Black Box: ${entry.missionId ?? entry.voyageId ?? entry.id}`;
      panel.webview.html = renderEntryHtml(entry, notes, nonce, panel.webview.cspSource, { dashboardUrl: connection.dashboardUrl, doctor });
    } catch (err) {
      panel.webview.html = renderEntryErrorHtml(entryLabel(summary), describeError(err), nonce);
    }
  }
}

/**
 * The extension command that shows a dashboard route natively: missions, voyages, captains, merge entries and black
 * box entries open in VS Code; anything else opens in the browser.
 */
export function nativeCommandForRoute(route: string): [string, string] {
  const match = /^\/(missions|voyages|captains|merge-queue|blackbox)\/([^/?#]+)/.exec(route);
  if (match) {
    const id = decodeURIComponent(match[2]);
    switch (match[1]) {
      case 'missions': return ['armada.mission.open', id];
      case 'voyages': return ['armada.voyage.open', id];
      case 'captains': return ['armada.captain.open', id];
      case 'merge-queue': return ['armada.merge.open', id];
      case 'blackbox': return ['armada.openBlackBox', id];
    }
  }
  return ['armada.openInBrowser', route];
}
