/*
 * The webview's side of the host bridge (src/surfaces/dashboard/protocol.ts): fetch and WebSocket are replaced so the
 * dashboard's API client and live updates run through the extension's connection, and file downloads and external
 * links go to VS Code.
 */

import { BRIDGE_ORIGIN, type HeaderPair, type HostMessage, type WebviewMessage } from '../../src/surfaces/dashboard/protocol';

interface VsCodeApi {
  postMessage(message: unknown): void;
  getState(): unknown;
  setState(state: unknown): void;
}

declare function acquireVsCodeApi(): VsCodeApi;

const vscode = acquireVsCodeApi();

/** Sends a message to the host. */
export function post(message: WebviewMessage): void {
  vscode.postMessage(message);
}

/** The webview's saved state (survives the tab being hidden and VS Code restarting). */
export function savedState<T>(): T | undefined {
  return (vscode.getState() as T | undefined) ?? undefined;
}

/** Saves the webview's state. */
export function saveState(state: unknown): void {
  vscode.setState(state);
}

type Pending = { resolve: (r: Response) => void; reject: (e: unknown) => void };
const pending = new Map<number, Pending>();
let nextId = 1;
const hostListeners = new Set<(m: HostMessage) => void>();
/** Navigate and reload messages that arrived before the page subscribed (it subscribes once React has mounted). */
const undelivered: HostMessage[] = [];

/** Subscribes to host messages other than fetch results; messages that arrived before anyone listened go first. */
export function onHostMessage(listener: (m: HostMessage) => void): () => void {
  hostListeners.add(listener);
  for (const message of undelivered.splice(0)) listener(message);
  return () => hostListeners.delete(listener);
}

function toBase64(bytes: Uint8Array): string {
  let binary = '';
  const chunk = 0x8000;
  for (let i = 0; i < bytes.length; i += chunk) binary += String.fromCharCode(...bytes.subarray(i, i + chunk));
  return btoa(binary);
}

function fromBase64(text: string): Uint8Array<ArrayBuffer> {
  const binary = atob(text);
  const bytes = new Uint8Array(new ArrayBuffer(binary.length));
  for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
  return bytes;
}

/** Base64 of a blob, for file downloads. */
export async function blobToBase64(blob: Blob): Promise<string> {
  return toBase64(new Uint8Array(await blob.arrayBuffer()));
}

function abortError(): Error {
  return new DOMException('Aborted', 'AbortError');
}

const nativeFetch = window.fetch.bind(window);

async function bridgedFetch(input: RequestInfo | URL, init?: RequestInit): Promise<Response> {
  let url = typeof input === 'string' ? input : input instanceof URL ? input.href : input.url;
  if (url.startsWith('/')) url = BRIDGE_ORIGIN + url;
  if (!url.startsWith(BRIDGE_ORIGIN)) return nativeFetch(input, init);
  const request = new Request(url, init ?? (typeof input === 'string' || input instanceof URL ? undefined : input));
  const signal = init?.signal ?? (input instanceof Request ? input.signal : undefined);
  if (signal?.aborted) throw abortError();
  const headers: HeaderPair[] = [];
  request.headers.forEach((value, name) => headers.push([name, value]));
  let body: string | undefined;
  if (request.method !== 'GET' && request.method !== 'HEAD') {
    const bytes = new Uint8Array(await request.arrayBuffer());
    if (bytes.length) body = toBase64(bytes);
  }
  const id = nextId++;
  return new Promise<Response>((resolve, reject) => {
    pending.set(id, { resolve, reject });
    signal?.addEventListener('abort', () => {
      if (!pending.has(id)) return;
      pending.delete(id);
      post({ type: 'abort', id });
      reject(abortError());
    }, { once: true });
    post({ type: 'fetch', id, path: url.slice(BRIDGE_ORIGIN.length) || '/', method: request.method, headers, body });
  });
}

/** A WebSocket stand-in fed by the host's event stream. */
class BridgeSocket {
  public static readonly CONNECTING = 0;
  public static readonly OPEN = 1;
  public static readonly CLOSING = 2;
  public static readonly CLOSED = 3;
  public readyState = 0;
  public onopen: ((ev: unknown) => void) | null = null;
  public onmessage: ((ev: { data: unknown }) => void) | null = null;
  public onclose: ((ev: unknown) => void) | null = null;
  public onerror: ((ev: unknown) => void) | null = null;

  public constructor() {
    sockets.add(this);
    setTimeout(() => {
      if (this.readyState !== 0) return;
      if (hostConnected) this.open();
      else this.fail();
    }, 0);
  }

  public open(): void {
    this.readyState = 1;
    this.onopen?.({});
  }

  public fail(): void {
    if (this.readyState === 3) return;
    this.readyState = 3;
    sockets.delete(this);
    this.onclose?.({});
  }

  public send(): void {
    // The host's event stream is one-way; nothing the dashboard sends is needed.
  }

  public close(): void {
    this.fail();
  }
}

const sockets = new Set<BridgeSocket>();
let hostConnected = true;

/** Installs the bridge. Call before the dashboard modules run. */
export function installBridge(connected: boolean): void {
  hostConnected = connected;
  // Page errors go to the host's log (the "Armada" output channel), where a broken page can be diagnosed.
  window.addEventListener('error', (event) => post({ type: 'error', message: String(event.message || event.error || 'error') }));
  window.addEventListener('unhandledrejection', (event) => {
    const reason = event.reason as { name?: string; message?: string } | undefined;
    if (reason?.name === 'AbortError') return;
    post({ type: 'error', message: `Unhandled rejection: ${reason?.message ?? String(event.reason)}` });
  });
  window.fetch = bridgedFetch as typeof window.fetch;
  (window as unknown as { WebSocket: unknown }).WebSocket = BridgeSocket;
  window.addEventListener('message', (event: MessageEvent) => {
    const message = event.data as HostMessage;
    if (!message || typeof message !== 'object') return;
    if (message.type === 'fetchResult') {
      const entry = pending.get(message.id);
      if (!entry) return;
      pending.delete(message.id);
      const nullBody = message.status === 204 || message.status === 304;
      entry.resolve(new Response(nullBody ? null : fromBase64(message.body), { status: message.status, statusText: message.statusText, headers: message.headers }));
      return;
    }
    if (message.type === 'fetchError') {
      const entry = pending.get(message.id);
      if (!entry) return;
      pending.delete(message.id);
      entry.reject(new TypeError(message.message));
      return;
    }
    if (message.type === 'event') {
      const data = JSON.stringify(message.data);
      for (const socket of sockets) if (socket.readyState === 1) socket.onmessage?.({ data });
      return;
    }
    if (message.type === 'socket') {
      hostConnected = message.connected;
      if (!message.connected) for (const socket of [...sockets]) socket.fail();
    }
    if (!hostListeners.size) {
      if (message.type === 'navigate' || message.type === 'reload') undelivered.push(message);
      return;
    }
    for (const listener of hostListeners) listener(message);
  });
  // Links that leave the dashboard open in the browser; links to dashboard pages outside the router open in a tab.
  document.addEventListener('click', (event) => {
    const anchor = (event.target as Element | null)?.closest?.('a');
    if (!anchor || event.defaultPrevented) return;
    const href = anchor.getAttribute('href') ?? '';
    if (/^(https?:|mailto:)/i.test(href)) {
      event.preventDefault();
      post({ type: 'openExternal', url: href });
    } else if (href.startsWith('/dashboard/')) {
      event.preventDefault();
      post({ type: 'openPage', path: href.slice('/dashboard'.length) });
    } else if (href.startsWith('/') && (event.metaKey || event.ctrlKey || anchor.target === '_blank')) {
      // Cmd/Ctrl+click (or a link meant for a new tab) opens the page in a tab of its own.
      event.preventDefault();
      event.stopPropagation();
      post({ type: 'openPage', path: href });
    }
  }, true);
  window.open = ((url?: string | URL) => {
    const href = url ? String(url) : '';
    if (href.startsWith('/dashboard/')) post({ type: 'openPage', path: href.slice('/dashboard'.length) });
    else if (href.startsWith('/')) post({ type: 'openPage', path: href });
    else if (href) post({ type: 'openExternal', url: href });
    return null;
  }) as typeof window.open;
}
