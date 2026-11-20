/*
 * Mission logs, captain logs and mission changes as read-only VS Code documents (scheme `armada`), so they open in the
 * editor with search, folding, the minimap and diff syntax highlighting. Logs of running missions and working
 * captains refresh every few seconds while their document is open.
 *
 *   armada:/missions/<id>/<title>.log      the mission log
 *   armada:/missions/<id>/<title>.diff     the mission's changes (unified diff)
 *   armada:/captains/<id>/<name>.log       the captain log
 */

import * as vscode from 'vscode';
import { getCaptainLog, getMissionDiff, getMissionLog } from '@dashboard/api/client';
import { describeError } from '../../transport/errors';
import { ACTIVE_MISSION_STATUSES, OpsStore } from './opsStore';

/** URI scheme of Armada documents. */
export const ARMADA_DOCUMENT_SCHEME = 'armada';

/** Lines of log loaded per document. */
const LOG_LINES = 5000;

/** A parsed Armada document URI. */
export interface ArmadaDocumentRef {
  kind: 'missionLog' | 'missionDiff' | 'captainLog';
  id: string;
}

/** The document URI for a mission log, mission diff or captain log. */
export function armadaDocumentUri(ref: ArmadaDocumentRef, title: string): vscode.Uri {
  const name = (title || ref.id).replace(/[\\/:*?"<>|#%]/g, ' ').replace(/\s+/g, ' ').trim().slice(0, 80) || ref.id;
  const folder = ref.kind === 'captainLog' ? 'captains' : 'missions';
  const extension = ref.kind === 'missionDiff' ? 'diff' : 'log';
  return vscode.Uri.from({ scheme: ARMADA_DOCUMENT_SCHEME, path: `/${folder}/${encodeURIComponent(ref.id)}/${name}.${extension}` });
}

/** Parses an Armada document URI path. */
export function parseArmadaDocumentPath(path: string): ArmadaDocumentRef | undefined {
  const match = /^\/(missions|captains)\/([^/]+)\/[^/]*\.(log|diff)$/.exec(path);
  if (!match) return undefined;
  const id = decodeURIComponent(match[2]);
  if (match[1] === 'captains') return match[3] === 'log' ? { kind: 'captainLog', id } : undefined;
  return { kind: match[3] === 'diff' ? 'missionDiff' : 'missionLog', id };
}

/** Serves Armada documents and keeps open logs of live work current. */
export class ArmadaDocumentProvider implements vscode.TextDocumentContentProvider, vscode.Disposable {
  private readonly _Store: OpsStore;
  private readonly _OnDidChange = new vscode.EventEmitter<vscode.Uri>();
  private readonly _Timer: ReturnType<typeof setInterval>;
  private readonly _Last = new Map<string, string>();

  public readonly onDidChange: vscode.Event<vscode.Uri> = this._OnDidChange.event;

  /**
   * @throws Error when store is missing.
   */
  public constructor(store: OpsStore, refreshMs = 3000) {
    if (!store) throw new Error('store is required');
    this._Store = store;
    this._Timer = setInterval(() => this.refreshLive(), refreshMs);
  }

  public async provideTextDocumentContent(uri: vscode.Uri): Promise<string> {
    const ref = parseArmadaDocumentPath(uri.path);
    if (!ref) return `Not an Armada document: ${uri.toString()}`;
    let text: string;
    try {
      if (ref.kind === 'missionLog') text = (await getMissionLog(ref.id, LOG_LINES)).log || '(the mission log is empty)';
      else if (ref.kind === 'captainLog') text = (await getCaptainLog(ref.id, LOG_LINES)).log || '(the captain log is empty)';
      else {
        const diff = await getMissionDiff(ref.id);
        text = diff.diff || (diff.error ? `# ${diff.error}` : '# This mission has no changes.');
      }
    } catch (err) {
      text = `Could not load from Armada: ${describeError(err)}`;
    }
    this._Last.set(uri.toString(), text);
    return text;
  }

  /** Reloads one document now. */
  public reload(uri: vscode.Uri): void {
    this._OnDidChange.fire(uri);
  }

  public dispose(): void {
    clearInterval(this._Timer);
    this._OnDidChange.dispose();
  }

  private refreshLive(): void {
    for (const doc of vscode.workspace.textDocuments) {
      if (doc.uri.scheme !== ARMADA_DOCUMENT_SCHEME || doc.isClosed) continue;
      const ref = parseArmadaDocumentPath(doc.uri.path);
      if (!ref || ref.kind === 'missionDiff') continue;
      const live = ref.kind === 'missionLog'
        ? ACTIVE_MISSION_STATUSES.includes(this._Store.missions.find((m) => m.id === ref.id)?.status ?? '')
        : this._Store.captains.find((c) => c.id === ref.id)?.state === 'Working';
      if (live) this._OnDidChange.fire(doc.uri);
    }
  }
}

/** Opens a mission log, mission diff or captain log in the editor, scrolled to the end for logs. */
export async function openArmadaDocument(ref: ArmadaDocumentRef, title: string): Promise<void> {
  const uri = armadaDocumentUri(ref, title);
  const document = await vscode.workspace.openTextDocument(uri);
  if (ref.kind === 'missionDiff' && document.languageId !== 'diff') await vscode.languages.setTextDocumentLanguage(document, 'diff');
  const editor = await vscode.window.showTextDocument(document, { preview: false });
  if (ref.kind !== 'missionDiff') {
    const end = document.lineAt(Math.max(0, document.lineCount - 1)).range.end;
    editor.selection = new vscode.Selection(end, end);
    editor.revealRange(new vscode.Range(end, end), vscode.TextEditorRevealType.Default);
  }
}
