import * as path from 'path';
import * as vscode from 'vscode';
import { BlackBoxStore } from '../state/blackBoxStore';
import { formatDate } from '../util/format';

/** Badge shown on a file that conflicted in an open entry's landing. */
export const CONFLICT_BADGE = 'BX';

/** Badge shown on a hotspot directory. */
export const HOTSPOT_BADGE = 'H';

/** File decorations for conflicted files and hotspot directories of the workspace's vessel. */
export class BlackBoxDecorationProvider implements vscode.FileDecorationProvider, vscode.Disposable {
  private readonly _Store: BlackBoxStore;
  private readonly _OnDidChange = new vscode.EventEmitter<vscode.Uri | vscode.Uri[] | undefined>();
  private readonly _Subscription: { dispose(): void };

  public readonly onDidChangeFileDecorations: vscode.Event<vscode.Uri | vscode.Uri[] | undefined> = this._OnDidChange.event;

  /**
   * @throws Error when store is missing.
   */
  public constructor(store: BlackBoxStore) {
    if (!store) throw new Error('store is required');
    this._Store = store;
    this._Subscription = store.onDidChange(() => this._OnDidChange.fire(undefined));
  }

  public provideFileDecoration(uri: vscode.Uri): vscode.FileDecoration | undefined {
    if (uri.scheme !== 'file') return undefined;
    const target = uri.fsPath;
    for (const [folder, files] of this._Store.conflictedFiles) {
      const rel = relativeInside(folder, target);
      if (rel === null) continue;
      const conflict = files.get(rel);
      if (conflict) {
        const more = conflict.count > 1 ? ` (and ${conflict.count - 1} more open ${conflict.count - 1 === 1 ? 'entry' : 'entries'})` : '';
        const decoration = new vscode.FileDecoration(
          CONFLICT_BADGE,
          `conflicted in ${conflict.missionId} landing, ${formatDate(conflict.createdUtc)}${more}`,
          new vscode.ThemeColor('gitDecoration.conflictingResourceForeground'),
        );
        decoration.propagate = false;
        return decoration;
      }
    }
    for (const [folder, dirs] of this._Store.hotspots) {
      const rel = relativeInside(folder, target);
      if (rel === null) continue;
      const hotspot = dirs.get(rel);
      if (hotspot) {
        const count = hotspot.ConflictCount ?? hotspot.EntryCount;
        const detail = count !== undefined ? `${count} conflict${count === 1 ? '' : 's'} in black box entries` : 'frequent conflicts in black box entries';
        const last = hotspot.LastSeenUtc ? `, last ${formatDate(hotspot.LastSeenUtc)}` : '';
        const decoration = new vscode.FileDecoration(HOTSPOT_BADGE, `Armada hotspot: ${detail}${last}`, new vscode.ThemeColor('list.warningForeground'));
        decoration.propagate = false;
        return decoration;
      }
    }
    return undefined;
  }

  public dispose(): void {
    this._Subscription.dispose();
    this._OnDidChange.dispose();
  }
}

/** Repository-relative forward-slash path of target inside folder, or null when it is outside (or the folder itself). */
export function relativeInside(folder: string, target: string): string | null {
  const rel = path.relative(folder, target);
  if (!rel || rel.startsWith('..') || path.isAbsolute(rel)) return null;
  return rel.split(path.sep).join('/');
}
