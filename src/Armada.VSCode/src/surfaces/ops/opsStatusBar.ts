import * as vscode from 'vscode';
import { ACTIVE_MISSION_STATUSES, OpsStore } from './opsStore';

/** Status bar summary of live work: active missions and what needs you (a spinner while they load). Clicking it opens Missions. */
export class OpsStatusBar implements vscode.Disposable {
  private readonly _Item: vscode.StatusBarItem;
  private readonly _Store: OpsStore;
  private readonly _Subscription: { dispose(): void };

  /**
   * @throws Error when store is missing.
   */
  public constructor(store: OpsStore) {
    if (!store) throw new Error('store is required');
    this._Store = store;
    this._Item = vscode.window.createStatusBarItem('armada.ops', vscode.StatusBarAlignment.Left, 49);
    this._Item.name = 'Armada Work';
    this._Item.command = { command: 'armada.page.open', title: 'Open Missions', arguments: ['/missions'] };
    this._Subscription = store.onDidChange(() => this.update());
    this.update();
  }

  public dispose(): void {
    this._Subscription.dispose();
    this._Item.dispose();
  }

  private update(): void {
    const state = this._Store.state('missions');
    if (state === 'loading') {
      this._Item.text = '$(loading~spin) Armada';
      this._Item.tooltip = 'Armada: loading missions...';
      this._Item.show();
      return;
    }
    if (state !== 'ready') {
      this._Item.hide();
      return;
    }
    const active = this._Store.missionsIn(ACTIVE_MISSION_STATUSES).length;
    const needs = this._Store.inbox.length;
    this._Item.text = `$(pulse) ${active}${needs ? `  $(bell-dot) ${needs}` : ''}`;
    this._Item.tooltip = `Armada: ${active} active ${active === 1 ? 'mission' : 'missions'}${needs ? `, ${needs} ${needs === 1 ? 'item needs' : 'items need'} you` : ''}. Click to open Missions.`;
    this._Item.show();
  }
}
