/*
 * Dashboard page tabs: the dashboard's own pages (Overview with its charts, Needs You, Dispatch, Activity, Request
 * History, Token Usage, every hub and detail page) in VS Code editor tabs. The page runs in a webview (webview/
 * dashboard) and every request it makes goes through here, sent to the connected Admiral with the extension's
 * credential; live events come from the extension's event bus. One tab per area is reused by default
 * (armada.pages.tabReuse), and open tabs come back after a reload.
 */

import * as crypto from 'crypto';
import * as vscode from 'vscode';
import { ConnectionManager } from '../../connection/connectionManager';
import { credentialHeaders, dashboardPageUrl, normalizeBaseUrl } from '../../transport/hostClient';
import { ArmadaEventBus } from '../../transport/eventBus';
import { BRIDGE_TOKEN, DashboardInit, HeaderPair, HostMessage, WebviewMessage, isWebviewMessage } from './protocol';
import { routeArea } from './routes';

/** The webview panel type (also the serializer's). */
export const DASHBOARD_PAGE_VIEW_TYPE = 'armada.dashboardPage';

/** Opens a dashboard route in a tab: (route, { newTab }). */
export const OPEN_PAGE_COMMAND = 'armada.page.open';

/** How page tabs are reused. */
export type TabReuse = 'area' | 'single' | 'never';

interface Page {
  panel: vscode.WebviewPanel;
  path: string;
  inflight: Map<number, AbortController>;
  /** Router state for the first render only. */
  state?: unknown;
  /** True once the page's script runs; messages sent before then are lost, so navigation waits for it. */
  ready: boolean;
  /** Where to go once the page is ready. */
  pendingNavigate?: { path: string; state?: unknown };
}

/** Headers the host sets itself, never copied from the page. */
const DROPPED_HEADERS = new Set(['host', 'origin', 'referer', 'content-length', 'connection', 'cookie', 'x-token', 'authorization', 'x-api-key']);

function readReuse(): TabReuse {
  const value = vscode.workspace.getConfiguration('armada').get<string>('pages.tabReuse', 'area');
  return value === 'single' || value === 'never' ? value : 'area';
}

function fallbackTitle(path: string): string {
  const area = routeArea(path);
  const names: Record<string, string> = {
    overview: 'Overview', inbox: 'Needs You', dispatch: 'Dispatch', 'fleet-actions': 'Fleet Actions', missions: 'Missions',
    voyages: 'Voyage', activity: 'Activity', vessels: 'Vessels', fleets: 'Fleet', captains: 'Captains', delivery: 'Delivery',
    configuration: 'Configuration', 'api-explorer': 'API Explorer', server: 'Settings', evaluations: 'Evaluations',
  };
  return names[area] ?? area.replace(/-/g, ' ').replace(/^./, (c) => c.toUpperCase());
}

/** What the page tabs have done, for diagnostics and the integration tests. */
export interface DashboardPageStats {
  /** Requests served, by HTTP status (0 for a failed request). */
  statuses: Record<string, number>;
  /** Requests that did not succeed ("GET /api/v1/x 404"), most recent last. */
  failures: string[];
  /** Routes the pages reported, with their titles, most recent last. */
  locations: Array<{ path: string; title: string }>;
  /** Errors the pages reported. */
  errors: string[];
  /** What each page rendered a few seconds after it opened. */
  snapshots: Array<Extract<WebviewMessage, { type: 'snapshot' }>>;
}

/** Opens and serves dashboard page tabs. */
export class DashboardPages implements vscode.Disposable {
  private readonly _ExtensionUri: vscode.Uri;
  private readonly _Connection: ConnectionManager;
  private readonly _Pages = new Set<Page>();
  private readonly _Disposables: vscode.Disposable[] = [];
  private _LastBase: string | undefined;
  private readonly _Log: ((line: string) => void) | undefined;
  private readonly _Stats: DashboardPageStats = { statuses: {}, failures: [], locations: [], errors: [], snapshots: [] };

  /**
   * @throws Error when an argument is missing.
   */
  public constructor(extensionUri: vscode.Uri, connection: ConnectionManager, bus: ArmadaEventBus, log?: (line: string) => void) {
    if (!extensionUri || !connection || !bus) throw new Error('extensionUri, connection and bus are required');
    this._ExtensionUri = extensionUri;
    this._Log = log;
    this._Connection = connection;
    this._LastBase = connection.active?.baseUrl;
    const subscription = bus.subscribe((event) => this.broadcast({ type: 'event', data: event }));
    this._Disposables.push(
      { dispose: () => subscription.dispose() },
      connection.onDidChange(() => {
        const base = connection.active?.baseUrl;
        if (base && base !== this._LastBase) {
          // Another Admiral: every page starts over against it.
          this._LastBase = base;
          for (const page of this._Pages) this.render(page);
          return;
        }
        this.broadcast({ type: 'socket', connected: connection.connected });
        if (connection.connected) this.broadcast({ type: 'reload' });
      }),
      vscode.window.registerWebviewPanelSerializer(DASHBOARD_PAGE_VIEW_TYPE, {
        deserializeWebviewPanel: async (panel, state) => {
          const path = state && typeof state === 'object' && typeof (state as { path?: unknown }).path === 'string' ? (state as { path: string }).path : '/';
          this.adopt(panel, path);
        },
      }),
    );
  }

  /** The number of open page tabs (tests and diagnostics). */
  public get count(): number {
    return this._Pages.size;
  }

  /** Sends an automation step (scroll or click) to the active page tab; false when there is none. */
  public automate(step: { action: 'scroll' | 'click' | 'select'; selector?: string; text?: string; y?: number; value?: string }): boolean {
    const page = [...this._Pages].find((p) => p.panel.active) ?? [...this._Pages].pop();
    if (!page) return false;
    void page.panel.webview.postMessage({ type: 'automation', ...step } satisfies HostMessage);
    return true;
  }

  /** Reloads every open page (its data, not the tab). */
  public reload(): void {
    this.broadcast({ type: 'reload' });
  }

  /** What the pages have done so far. */
  public get stats(): DashboardPageStats {
    return { statuses: { ...this._Stats.statuses }, failures: [...this._Stats.failures], locations: [...this._Stats.locations], errors: [...this._Stats.errors], snapshots: [...this._Stats.snapshots] };
  }

  /**
   * Opens a dashboard route ("/activity?source=tokens") in a tab, reusing the tab of the same area unless newTab.
   * @throws Error for an empty route.
   */
  public open(route: string, options: { newTab?: boolean; preserveFocus?: boolean; state?: unknown } = {}): void {
    if (!route) throw new Error('route is required');
    const path = route.startsWith('/') ? route : `/${route}`;
    const reuse = readReuse();
    if (!options.newTab && reuse !== 'never') {
      const existing = [...this._Pages].find((page) => reuse === 'single' || page.path === path || routeArea(page.path) === routeArea(path));
      if (existing) {
        existing.panel.reveal(existing.panel.viewColumn, options.preserveFocus ?? false);
        if (existing.path !== path || options.state !== undefined) {
          existing.path = path;
          if (existing.ready) void existing.panel.webview.postMessage({ type: 'navigate', path, state: options.state } satisfies HostMessage);
          else existing.pendingNavigate = { path, state: options.state };
        }
        return;
      }
    }
    const panel = vscode.window.createWebviewPanel(DASHBOARD_PAGE_VIEW_TYPE, fallbackTitle(path), { viewColumn: vscode.ViewColumn.Active, preserveFocus: options.preserveFocus ?? false }, {
      enableScripts: true,
      enableFindWidget: true,
      retainContextWhenHidden: true,
      localResourceRoots: [vscode.Uri.joinPath(this._ExtensionUri, 'dist', 'webview'), vscode.Uri.joinPath(this._ExtensionUri, 'media')],
    });
    this.adopt(panel, path, options.state);
  }

  public dispose(): void {
    for (const d of this._Disposables) d.dispose();
    for (const page of [...this._Pages]) page.panel.dispose();
  }

  private adopt(panel: vscode.WebviewPanel, path: string, state?: unknown): void {
    const page: Page = { panel, path, inflight: new Map(), state, ready: false };
    this._Pages.add(page);
    panel.iconPath = vscode.Uri.joinPath(this._ExtensionUri, 'media', 'icon.png');
    panel.webview.options = {
      enableScripts: true,
      localResourceRoots: [vscode.Uri.joinPath(this._ExtensionUri, 'dist', 'webview'), vscode.Uri.joinPath(this._ExtensionUri, 'media')],
    };
    panel.title = fallbackTitle(path);
    panel.webview.onDidReceiveMessage((message: unknown) => {
      if (isWebviewMessage(message)) void this.handle(page, message);
    });
    panel.onDidDispose(() => {
      for (const controller of page.inflight.values()) controller.abort();
      this._Pages.delete(page);
    });
    this.render(page);
  }

  private render(page: Page): void {
    page.ready = false;
    const webview = page.panel.webview;
    const nonce = crypto.randomBytes(16).toString('base64');
    const script = webview.asWebviewUri(vscode.Uri.joinPath(this._ExtensionUri, 'dist', 'webview', 'dashboard.js'));
    const style = webview.asWebviewUri(vscode.Uri.joinPath(this._ExtensionUri, 'dist', 'webview', 'dashboard.css'));
    const init: DashboardInit = { path: page.path, state: page.state, dashboardUrl: this._Connection.active?.dashboardUrl ?? '', connected: this._Connection.connected };
    const csp = [
      "default-src 'none'",
      `script-src 'nonce-${nonce}' ${webview.cspSource}`,
      `style-src ${webview.cspSource} 'unsafe-inline'`,
      `img-src ${webview.cspSource} data: blob: https: http:`,
      `font-src ${webview.cspSource} data:`,
      `connect-src ${webview.cspSource} data: blob:`,
    ].join('; ');
    const initJson = JSON.stringify(init).replace(/&/g, '&amp;').replace(/"/g, '&quot;').replace(/</g, '&lt;');
    webview.html = `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<meta http-equiv="Content-Security-Policy" content="${csp}">
<meta name="viewport" content="width=device-width, initial-scale=1.0">
<link rel="stylesheet" href="${style}">
<title>Armada</title>
</head>
<body>
<div id="root" data-armada-init="${initJson}"></div>
<script type="module" nonce="${nonce}" src="${script}"></script>
</body>
</html>`;
    page.state = undefined;
  }

  private broadcast(message: HostMessage): void {
    for (const page of this._Pages) void page.panel.webview.postMessage(message);
  }

  private async handle(page: Page, message: WebviewMessage): Promise<void> {
    switch (message.type) {
      case 'ready':
        page.ready = true;
        if (page.pendingNavigate) {
          const next = page.pendingNavigate;
          page.pendingNavigate = undefined;
          void page.panel.webview.postMessage({ type: 'navigate', path: next.path, state: next.state } satisfies HostMessage);
        }
        return;
      case 'fetch':
        await this.fetch(page, message);
        return;
      case 'abort':
        page.inflight.get(message.id)?.abort();
        page.inflight.delete(message.id);
        return;
      case 'location':
        page.path = message.path;
        page.panel.title = message.title || fallbackTitle(message.path);
        this._Stats.locations.push({ path: message.path, title: message.title });
        if (this._Stats.locations.length > 200) this._Stats.locations.shift();
        return;
      case 'snapshot':
        this._Stats.snapshots.push(message);
        if (this._Stats.snapshots.length > 200) this._Stats.snapshots.shift();
        return;
      case 'error':
        this._Stats.errors.push(message.message);
        if (this._Stats.errors.length > 200) this._Stats.errors.shift();
        this._Log?.(`Dashboard page ${page.path}: ${message.message}`);
        return;
      case 'openPage':
        this.open(message.path, { newTab: true });
        return;
      case 'openInBrowser': {
        const base = this._Connection.active?.dashboardUrl;
        if (base) await vscode.env.openExternal(vscode.Uri.parse(dashboardPageUrl(base, message.path)));
        return;
      }
      case 'openExternal':
        if (/^(https?|mailto):/i.test(message.url)) await vscode.env.openExternal(vscode.Uri.parse(message.url));
        return;
      case 'saveFile': {
        const target = await vscode.window.showSaveDialog({ defaultUri: vscode.Uri.file(message.filename), saveLabel: 'Save' });
        if (target) {
          await vscode.workspace.fs.writeFile(target, Buffer.from(message.base64, 'base64'));
          void vscode.window.showInformationMessage(`Armada: saved ${target.fsPath}.`);
        }
        return;
      }
      case 'command':
        if (message.command.startsWith('armada.')) await vscode.commands.executeCommand(message.command, ...(message.args ?? []));
        return;
      default:
        return;
    }
  }

  private fail_(line: string): void {
    this._Stats.failures.push(line);
    if (this._Stats.failures.length > 200) this._Stats.failures.shift();
  }

  private count_(status: number): void {
    const key = String(status);
    this._Stats.statuses[key] = (this._Stats.statuses[key] ?? 0) + 1;
  }

  private async fetch(page: Page, message: Extract<WebviewMessage, { type: 'fetch' }>): Promise<void> {
    const post = (reply: HostMessage) => void page.panel.webview.postMessage(reply);
    const active = this._Connection.active;
    if (!active) {
      post({ type: 'fetchError', id: message.id, message: 'Not connected to an Admiral. Run "Armada: Connect".' });
      return;
    }
    const headers = new Headers();
    for (const [name, value] of message.headers) {
      const lower = name.toLowerCase();
      if (DROPPED_HEADERS.has(lower)) {
        // A real session token the page signed in with (not the placeholder) is kept.
        if (lower === 'x-token' && value && value !== BRIDGE_TOKEN && !active.credential) headers.set(name, value);
        continue;
      }
      headers.set(name, value);
    }
    for (const [name, value] of Object.entries(credentialHeaders(active.credential))) headers.set(name, value);
    const controller = new AbortController();
    page.inflight.set(message.id, controller);
    try {
      const response = await fetch(`${normalizeBaseUrl(active.baseUrl)}${message.path}`, {
        method: message.method,
        headers,
        body: message.body ? Buffer.from(message.body, 'base64') : undefined,
        signal: controller.signal,
      });
      const body = Buffer.from(await response.arrayBuffer()).toString('base64');
      const replyHeaders: HeaderPair[] = [];
      response.headers.forEach((value, name) => {
        if (name !== 'content-encoding' && name !== 'transfer-encoding' && name !== 'content-length') replyHeaders.push([name, value]);
      });
      this.count_(response.status);
      if (response.status >= 400) this.fail_(`${message.method} ${message.path.split('?')[0]} ${response.status}`);
      if (response.status >= 500) this._Log?.(`Dashboard page ${page.path}: ${message.method} ${message.path.split('?')[0]} answered ${response.status}`);
      post({ type: 'fetchResult', id: message.id, status: response.status, statusText: response.statusText, headers: replyHeaders, body });
    } catch (err) {
      if (controller.signal.aborted) return;
      this.count_(0);
      this.fail_(`${message.method} ${message.path.split('?')[0]} failed`);
      this._Log?.(`Dashboard page ${page.path}: ${message.method} ${message.path.split('?')[0]} failed: ${err instanceof Error ? err.message : String(err)}`);
      post({ type: 'fetchError', id: message.id, message: err instanceof Error ? err.message : String(err) });
    } finally {
      page.inflight.delete(message.id);
    }
  }
}
