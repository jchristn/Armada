import * as vscode from 'vscode';
import { LiveMode } from '../transport/eventBus';
import { ConnectionManager, ConnectionState } from './connectionManager';

/** Command the status bar item runs: a quick pick of connection actions. */
export const CONNECTION_ACTIONS_COMMAND = 'armada.showConnectionActions';

/** Text, icon and tooltip of the status bar item for a state. Pure, for tests. */
export function describeStatus(state: ConnectionState, live: LiveMode): { text: string; tooltip: string; warning: boolean } {
  const target = state.target;
  const where = target ? (target.kind === 'local' ? 'local' : target.label) : '';
  switch (state.phase) {
    case 'connected': {
      const lines = [
        `Armada: connected to ${target?.baseUrl ?? ''} (${target?.kind === 'local' ? 'local Admiral' : 'remote Admiral'})`,
        target?.source ? `Found in ${target.source}` : '',
        state.credentialOrigin ? `Credential: ${state.credentialOrigin}` : '',
        state.serverVersion ? `Armada ${state.serverVersion}, contract ${state.contractVersion ?? 'unknown'}` : '',
        state.identity?.email ? `Signed in as ${state.identity.email}${state.identity.tenantName ? ` (${state.identity.tenantName})` : ''}${state.identity.globalAdmin ? ', global admin' : state.identity.tenantAdmin ? ', tenant admin' : ''}` : '',
        `Live updates: ${live === 'websocket' ? 'WebSocket' : live}`,
        'Click for connection actions.',
      ];
      return { text: `$(plug) Armada: ${where}`, tooltip: lines.filter(Boolean).join('\n'), warning: false };
    }
    case 'connecting':
      return { text: `$(sync~spin) Armada: ${where || 'connecting'}`, tooltip: `Armada: connecting to ${target?.baseUrl ?? 'the Admiral'}...`, warning: false };
    case 'failed':
      return {
        text: `$(warning) Armada: ${state.failure?.summary ?? 'not connected'}`,
        tooltip: `Armada: ${state.failure?.message ?? 'not connected'}\nClick for connection actions.`,
        warning: true,
      };
    default:
      return { text: '$(debug-disconnect) Armada', tooltip: 'Armada: not connected. Click for connection actions.', warning: false };
  }
}

/** The status bar item that shows the connection state and target. */
export class ConnectionStatusBar implements vscode.Disposable {
  private readonly _Item: vscode.StatusBarItem;
  private readonly _Connection: ConnectionManager;
  private readonly _Subscriptions: vscode.Disposable[] = [];
  private _Live: LiveMode = 'stopped';

  /**
   * @throws Error when connection is missing.
   */
  public constructor(connection: ConnectionManager) {
    if (!connection) throw new Error('connection is required');
    this._Connection = connection;
    this._Item = vscode.window.createStatusBarItem('armada.connection', vscode.StatusBarAlignment.Left, 50);
    this._Item.name = 'Armada Connection';
    this._Item.command = CONNECTION_ACTIONS_COMMAND;
    this._Subscriptions.push(connection.onDidChange(() => this.update()));
    this.update();
    this._Item.show();
  }

  /** Called when the live mode changes. */
  public setLiveMode(mode: LiveMode): void {
    this._Live = mode;
    this.update();
  }

  public dispose(): void {
    for (const s of this._Subscriptions) s.dispose();
    this._Item.dispose();
  }

  private update(): void {
    const view = describeStatus(this._Connection.state, this._Live);
    this._Item.text = view.text;
    this._Item.tooltip = view.tooltip;
    this._Item.backgroundColor = view.warning ? new vscode.ThemeColor('statusBarItem.warningBackground') : undefined;
  }
}

/** One entry of the connection actions quick pick. */
export interface ConnectionAction {
  label: string;
  description?: string;
  command: string;
}

/** The actions offered for a state, in order. Pure, for tests. */
export function connectionActions(state: ConnectionState): ConnectionAction[] {
  const actions: ConnectionAction[] = [];
  if (state.phase === 'connected') {
    actions.push({ label: '$(link-external) Open Dashboard', description: state.target?.baseUrl, command: 'armada.openDashboard' });
    actions.push({ label: '$(plug) Connect to Another Admiral...', command: 'armada.connect' });
    actions.push({ label: '$(refresh) Reconnect', command: 'armada.reconnect' });
  } else {
    actions.push({ label: '$(plug) Connect...', description: 'pick an Admiral found on this machine, or enter a URL', command: 'armada.connect' });
    if (state.phase === 'failed' && state.target) actions.push({ label: '$(refresh) Retry', description: state.target.baseUrl, command: 'armada.reconnect' });
  }
  if (state.phase === 'connected' || state.phase === 'failed') actions.push({ label: '$(debug-disconnect) Disconnect', command: 'armada.disconnect' });
  actions.push({ label: '$(output) Show Output', command: 'armada.showOutput' });
  actions.push({ label: '$(gear) Open Settings', command: 'armada.openSettings' });
  return actions;
}

/** Shows the connection actions and runs the one picked. */
export async function showConnectionActions(connection: ConnectionManager): Promise<string | undefined> {
  const state = connection.state;
  const title = state.phase === 'connected'
    ? `Armada: connected to ${state.target?.baseUrl ?? ''}`
    : state.phase === 'failed' ? `Armada: ${state.failure?.message ?? 'not connected'}` : 'Armada: not connected';
  const picked = await vscode.window.showQuickPick(connectionActions(state), { title, placeHolder: 'Connection actions' });
  if (!picked) return undefined;
  await vscode.commands.executeCommand(picked.command);
  return picked.command;
}
