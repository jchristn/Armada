import * as vscode from 'vscode';
import { ArmadaApiError, ArmadaBlackBoxClient, ArmadaClientOptions, describeError, normalizeBaseUrl } from './api/blackbox';
import { readSettings } from './config';

/** SecretStorage key of the API key. */
export const API_KEY_SECRET = 'armada.apiKey';

/** Context key that drives the welcome view and menus. */
export const CONNECTED_CONTEXT_KEY = 'armada.connected';

/** Creates a client; replaced in tests. */
export type ClientFactory = (options: ArmadaClientOptions) => ArmadaBlackBoxClient;

/** Owns the connection settings, the SecretStorage API key and the current client. */
export class ArmadaConnection implements vscode.Disposable {
  private readonly _Secrets: vscode.SecretStorage;
  private readonly _Factory: ClientFactory;
  private readonly _OnDidChange = new vscode.EventEmitter<ArmadaBlackBoxClient | undefined>();
  private _Client: ArmadaBlackBoxClient | undefined;

  /** Fires with the new client (or undefined) whenever the connection changes. */
  public readonly onDidChange: vscode.Event<ArmadaBlackBoxClient | undefined> = this._OnDidChange.event;

  /**
   * @throws Error when secrets is missing.
   */
  public constructor(secrets: vscode.SecretStorage, factory?: ClientFactory) {
    if (!secrets) throw new Error('secrets is required');
    this._Secrets = secrets;
    this._Factory = factory ?? ((options) => new ArmadaBlackBoxClient(options));
  }

  /** The current client, if connected. */
  public get client(): ArmadaBlackBoxClient | undefined {
    return this._Client;
  }

  /**
   * The current client.
   * @throws ArmadaApiError (notConfigured) when not connected.
   */
  public requireClient(): ArmadaBlackBoxClient {
    if (!this._Client) throw new ArmadaApiError('notConfigured', 'Armada is not connected.');
    return this._Client;
  }

  /** Builds a client from the stored key and settings, without prompting. Returns whether a client exists. */
  public async restore(): Promise<boolean> {
    const key = await this._Secrets.get(API_KEY_SECRET);
    const settings = readSettings();
    if (!key || !settings.url) {
      await this.setClient(undefined);
      return false;
    }
    try {
      await this.setClient(this._Factory({ baseUrl: settings.url, apiKey: key, tenant: settings.tenant, authHeader: settings.authHeader }));
      return true;
    } catch {
      await this.setClient(undefined);
      return false;
    }
  }

  /**
   * Prompts for the URL and API key, verifies them against the server, stores the key in SecretStorage and the URL
   * in settings. Returns true on success. Never throws; failures are shown to the user.
   */
  public async connect(): Promise<boolean> {
    const settings = readSettings();
    const url = await vscode.window.showInputBox({
      title: 'Armada: Connect (1/2)',
      prompt: 'Armada Admiral URL',
      value: settings.url || 'http://localhost:7890',
      ignoreFocusOut: true,
      validateInput: (value) => (/^\s*(https?:\/\/)?[^\s/]+/i.test(value) ? undefined : 'Enter a URL such as http://localhost:7890'),
    });
    if (url === undefined) return false;
    const existing = await this._Secrets.get(API_KEY_SECRET);
    const key = await vscode.window.showInputBox({
      title: 'Armada: Connect (2/2)',
      prompt: existing ? 'Armada API key or bearer token (leave empty to keep the stored key)' : 'Armada API key or bearer token',
      password: true,
      ignoreFocusOut: true,
    });
    if (key === undefined) return false;
    const apiKey = key.trim() || existing || '';
    if (!apiKey) {
      void vscode.window.showErrorMessage('Armada: an API key is required.');
      return false;
    }

    let client: ArmadaBlackBoxClient;
    try {
      client = this._Factory({ baseUrl: url.trim(), apiKey, tenant: settings.tenant, authHeader: settings.authHeader });
      await client.listVessels(1, 1);
    } catch (err) {
      void vscode.window.showErrorMessage(`Armada: could not connect. ${describeError(err)}`);
      return false;
    }

    await this._Secrets.store(API_KEY_SECRET, apiKey);
    const normalized = normalizeBaseUrl(url);
    if (normalized !== settings.url) {
      await vscode.workspace.getConfiguration('armada').update('url', normalized, vscode.ConfigurationTarget.Global);
    }
    await this.setClient(client);
    void vscode.window.showInformationMessage(`Armada: connected to ${normalized}.`);
    return true;
  }

  /** Removes the stored key and drops the client. */
  public async disconnect(): Promise<void> {
    await this._Secrets.delete(API_KEY_SECRET);
    await this.setClient(undefined);
    void vscode.window.showInformationMessage('Armada: disconnected. The API key was removed from secret storage.');
  }

  public dispose(): void {
    this._OnDidChange.dispose();
  }

  private async setClient(client: ArmadaBlackBoxClient | undefined): Promise<void> {
    this._Client = client;
    await vscode.commands.executeCommand('setContext', CONNECTED_CONTEXT_KEY, client !== undefined);
    this._OnDidChange.fire(client);
  }
}
