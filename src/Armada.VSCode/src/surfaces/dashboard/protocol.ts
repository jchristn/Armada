/*
 * Messages between a dashboard page tab (the dashboard's own React pages running in a webview, webview/dashboard) and
 * the extension host. The webview cannot reach the Admiral itself (no credential, and the Admiral does not answer
 * cross-origin requests), so every HTTP request and the live event stream go through the host, which sends them
 * with the extension's connection. Pure, shared by both sides.
 */

/** The origin the webview's API client is pointed at; the host replaces it with the connected Admiral. */
export const BRIDGE_ORIGIN = 'https://armada.vscode.invalid';

/** The session token the webview's AuthProvider is given. The host drops it and adds the real credential. */
export const BRIDGE_TOKEN = 'vscode-bridge';

/** What the host tells a new page through the HTML (data-armada-init). */
export interface DashboardInit {
  /** The dashboard route to show first ("/activity?source=requests"). */
  path: string;
  /** Router state for the first route (a dispatch prefill, for example). */
  state?: unknown;
  /** The browser dashboard's base URL, for Open in Browser. */
  dashboardUrl: string;
  /** Whether the host is connected (an unconnected page shows a prompt instead). */
  connected: boolean;
}

/** One HTTP header. */
export type HeaderPair = [string, string];

/** Webview to host. */
export type WebviewMessage =
  | { type: 'ready' }
  | { type: 'fetch'; id: number; path: string; method: string; headers: HeaderPair[]; body?: string }
  | { type: 'abort'; id: number }
  | { type: 'location'; path: string; title: string }
  | { type: 'openExternal'; url: string }
  | { type: 'openPage'; path: string }
  | { type: 'openInBrowser'; path: string }
  | { type: 'saveFile'; filename: string; base64: string }
  | { type: 'command'; command: string; args?: unknown[] }
  | { type: 'error'; message: string }
  | { type: 'snapshot'; path: string; charts: number; tables: number; cards: number; text: number; message?: string };

/** Host to webview. */
export type HostMessage =
  | { type: 'fetchResult'; id: number; status: number; statusText: string; headers: HeaderPair[]; body: string }
  | { type: 'fetchError'; id: number; message: string }
  | { type: 'event'; data: unknown }
  | { type: 'socket'; connected: boolean }
  | { type: 'navigate'; path: string; state?: unknown }
  | { type: 'reload' };

/** Routes the host handles itself instead of the page (Ask Armada has its own view in VS Code). */
export function hostHandledRoute(path: string): string | undefined {
  if (/^\/ask(\/|\?|$)/.test(path)) return 'armada.ask.open';
  return undefined;
}

/** Narrows an unknown webview message. */
export function isWebviewMessage(value: unknown): value is WebviewMessage {
  return !!value && typeof value === 'object' && typeof (value as { type?: unknown }).type === 'string';
}
