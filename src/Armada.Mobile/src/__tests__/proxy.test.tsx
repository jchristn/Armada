import AsyncStorage from '@react-native-async-storage/async-storage';
import * as SecureStore from 'expo-secure-store';
import { act, renderHook, waitFor } from '@testing-library/react-native';
import { createHash } from 'crypto';
import type { ReactNode } from 'react';
import * as client from '@dashboard/api/client';
import type { WhoAmIResult } from '@dashboard/types/models';
import { AuthProvider, useAuth, type AuthHooks } from '../auth/AuthContext';
import {
  ProxyError,
  createProxyClient,
  hasUsableInstance,
  isSelectableInstance,
  proxyLoginProof,
  type FetchLike,
  type ProxyClient,
} from '../proxy/proxyApi';
import { createProxySocketFactory, proxySubprotocol, type NativeWebSocketCtor } from '../proxy/proxySocket';
import { base64UrlUtf8, sha256Hex } from '../proxy/sha256';
import type { SocketLike } from '@dashboard/lib/armadaSocket';
import { proxyTokenKey, tokenKey } from '../storage/secure';

jest.mock('@dashboard/api/client', () => require('../test/mockClient').clientMockFactory());

const api = client as jest.Mocked<typeof client> & { __fireUnauthorized: () => void };
const secureStore = (SecureStore as unknown as { __store: Map<string, string> }).__store;

const nodeSha = (s: string) => createHash('sha256').update(s, 'utf8').digest('hex');

describe('sha256 and base64url', () => {
  it('matches the FIPS test vectors', () => {
    expect(sha256Hex('')).toBe('e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855');
    expect(sha256Hex('abc')).toBe('ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad');
    expect(sha256Hex('abcdbcdecdefdefgefghfghighijhijkijkljklmklmnlmnomnopnopq'))
      .toBe('248d6a61d20638b8e5c026930c3e6039a33ce45964ff2167f6ecedd419db06c1');
  });

  it('matches Node for block boundaries and non-ASCII text', () => {
    for (const s of ['a'.repeat(55), 'a'.repeat(56), 'a'.repeat(64), 'a'.repeat(119), 'p\u00e4ssw\u00f6rd \u4f60\u597d \u{1f680}']) {
      expect(sha256Hex(s)).toBe(nodeSha(s));
    }
  });

  it('base64url has no padding and uses the URL alphabet', () => {
    for (const s of ['', 'f', 'fo', 'foo', 'foob', '0f34455311b54e719f50927df5ecdfd7', '\u00ff\u00fe>>??']) {
      expect(base64UrlUtf8(s)).toBe(Buffer.from(s, 'utf8').toString('base64url'));
    }
  });

  it('the login proof is the documented formula (password trimmed, nonce lowercased)', () => {
    const nonce = '4F3A0C7A8F6C49D9B6711D2C1A7B5E90';
    const expected = nodeSha(`proxy-browser-login:proxy:${nonce.toLowerCase()}:${nodeSha('s3cret')}`);
    expect(proxyLoginProof('  s3cret ', nonce)).toBe(expected);
  });
});

interface Recorded { url: string; method: string; headers: Record<string, string>; body?: string; credentials?: string }

function fakeFetch(routes: Record<string, { status: number; body?: unknown; headers?: Record<string, string> } | (() => never)>) {
  const calls: Recorded[] = [];
  const fn: FetchLike = async (url, init) => {
    calls.push({ url, method: init?.method ?? 'GET', headers: init?.headers ?? {}, body: init?.body, credentials: init?.credentials });
    const key = `${init?.method ?? 'GET'} ${url.replace(/^https?:\/\/[^/]+/, '')}`;
    const route = routes[key];
    if (!route) throw new TypeError(`Network request failed (${key})`);
    if (typeof route === 'function') route();
    const r = route as { status: number; body?: unknown; headers?: Record<string, string> };
    const text = r.body === undefined ? '' : typeof r.body === 'string' ? r.body : JSON.stringify(r.body);
    return {
      status: r.status,
      ok: r.status >= 200 && r.status < 300,
      headers: { get: (name: string) => r.headers?.[name] ?? null },
      text: async () => text,
    };
  };
  return { fn, calls };
}

describe('proxy API client', () => {
  it('logs in with a challenge and proof, never sending the password', async () => {
    const { fn, calls } = fakeFetch({
      'GET /proxy-api/v1/auth/challenge': { status: 200, body: { nonce: 'abc123', expiresUtc: 'x' } },
      'POST /proxy-api/v1/auth/login': { status: 200, body: { token: 'P1', expiresUtc: '2026-10-08T00:00:00Z', selectedInstanceId: null } },
    });
    const proxy = createProxyClient('https://proxy.example/', { fetch: fn });
    const result = await proxy.login('hunter2');
    expect(result).toEqual({ token: 'P1', expiresUtc: '2026-10-08T00:00:00Z', selectedInstanceId: null });
    expect(calls[1].url).toBe('https://proxy.example/proxy-api/v1/auth/login');
    expect(JSON.parse(calls[1].body ?? '{}')).toEqual({ nonce: 'abc123', proofSha256: proxyLoginProof('hunter2', 'abc123'), setCookie: false });
    expect(calls.map((c) => c.body ?? '').join('')).not.toContain('hunter2');
  });

  it('never uses cookies: the login asks for none and every request omits credentials', async () => {
    const { fn, calls } = fakeFetch({
      'GET /proxy-api/v1/auth/challenge': { status: 200, body: { nonce: 'abc123', expiresUtc: 'x' } },
      'POST /proxy-api/v1/auth/login': { status: 200, body: { token: 'P1', expiresUtc: null, selectedInstanceId: null } },
      'GET /proxy-api/v1/instances': { status: 200, body: { instances: [] } },
      'POST /proxy-api/v1/auth/logout': { status: 200, body: { success: true } },
    });
    const proxy = createProxyClient('https://proxy.example', { fetch: fn });
    await proxy.login('hunter2');
    await proxy.listInstances('P1');
    await proxy.logout('P1');
    expect(JSON.parse(calls[1].body ?? '{}').setCookie).toBe(false);
    expect(calls.map((c) => c.credentials)).toEqual(['omit', 'omit', 'omit', 'omit']);
  });

  it('sends the session as a bearer token on /proxy-api routes', async () => {
    const { fn, calls } = fakeFetch({
      'GET /proxy-api/v1/instances': { status: 200, body: { count: 1, instances: [{ instanceId: 'armada-1', state: 'connected', capabilities: ['dashboard.http.relay'] }] } },
      'POST /proxy-api/v1/session/instance': { status: 200, body: { isAuthenticated: true, selectedInstanceId: 'armada-1', selectedInstance: { instanceId: 'armada-1', state: 'connected' }, relay: { api: true } } },
    });
    const proxy = createProxyClient('https://proxy.example', { fetch: fn });
    const list = await proxy.listInstances('P1');
    expect(list.map((i) => i.instanceId)).toEqual(['armada-1']);
    const ctx = await proxy.selectInstance('P1', 'armada-1');
    expect(hasUsableInstance(ctx)).toBe(true);
    expect(calls.every((c) => c.headers.Authorization === 'Bearer P1')).toBe(true);
    expect(calls.some((c) => 'X-Token' in c.headers)).toBe(false);
  });

  it('maps statuses to error kinds (401, 429 with Retry-After, 409) and a non-proxy 200 to notFound', async () => {
    const { fn } = fakeFetch({
      'GET /proxy-api/v1/session/context': { status: 401, body: { error: 'Proxy authentication required. Sign in again.' } },
      'GET /proxy-api/v1/auth/challenge': { status: 200, body: { nonce: 'n' } },
      'POST /proxy-api/v1/auth/login': { status: 429, body: { error: 'locked' }, headers: { 'Retry-After': '120' } },
      'POST /proxy-api/v1/session/instance': { status: 409, body: { error: 'not connected' } },
      'GET /proxy-api/v1/instances': { status: 200, body: '<html>dashboard</html>' },
    });
    const proxy = createProxyClient('https://proxy.example', { fetch: fn });
    await expect(proxy.sessionContext('P')).rejects.toMatchObject({ kind: 'unauthorized', status: 401 });
    await expect(proxy.login('x')).rejects.toMatchObject({ kind: 'lockedOut', retryAfterSeconds: 120 });
    await expect(proxy.selectInstance('P', 'armada-1')).rejects.toMatchObject({ kind: 'conflict' });
    await expect(proxy.listInstances('P')).rejects.toMatchObject({ kind: 'notFound' });
  });

  it('an unreachable proxy is a network error', async () => {
    const { fn } = fakeFetch({});
    await expect(createProxyClient('https://proxy.example', { fetch: fn }).sessionContext('P')).rejects.toMatchObject({ kind: 'network' });
  });

  it('instance selection rules: connected (or stale) with the HTTP relay; context needs a selected live instance', () => {
    expect(isSelectableInstance({ instanceId: 'a', state: 'connected', capabilities: ['dashboard.http.relay'] })).toBe(true);
    expect(isSelectableInstance({ instanceId: 'a', state: 'offline' })).toBe(false);
    expect(isSelectableInstance({ instanceId: 'a', state: 'connected', capabilities: [] })).toBe(false);
    expect(hasUsableInstance({ selectedInstanceId: null })).toBe(false);
    expect(hasUsableInstance({ selectedInstanceId: 'a', selectedInstance: { state: 'offline' } })).toBe(false);
    expect(hasUsableInstance({ selectedInstanceId: 'a', selectedInstance: { state: 'connected' }, relay: { api: true } })).toBe(true);
  });
});

class FakeNativeSocket implements SocketLike {
  static created: FakeNativeSocket[] = [];
  readyState = 0;
  sent: string[] = [];
  closed = false;
  onopen: ((ev: unknown) => void) | null = null;
  onmessage: ((ev: { data: unknown }) => void) | null = null;
  onclose: ((ev: unknown) => void) | null = null;
  onerror: ((ev: unknown) => void) | null = null;
  constructor(public url: string, public protocols?: string | string[] | null, public options?: { headers: Record<string, string> } | null) {
    FakeNativeSocket.created.push(this);
  }
  send(data: string) { this.sent.push(data); }
  close() { this.closed = true; this.readyState = 3; this.onclose?.({}); }
  open() { this.readyState = 1; this.onopen?.({}); }
  fail() { this.readyState = 3; this.onclose?.({ code: 1006 }); }
}

describe('proxy WebSocket factory', () => {
  beforeEach(() => { FakeNativeSocket.created = []; });
  const ctor = FakeNativeSocket as unknown as NativeWebSocketCtor;

  it('sends the proxy session as an armada-proxy-session subprotocol first', () => {
    const { factory, mode } = createProxySocketFactory({ proxyToken: () => 'P1', ctor });
    factory('wss://proxy.example/ws?token=A1');
    const socket = FakeNativeSocket.created[0];
    expect(socket.url).toBe('wss://proxy.example/ws?token=A1');
    expect(socket.protocols).toEqual(['armada', proxySubprotocol('P1')]);
    expect(proxySubprotocol('P1')).toBe(`armada-proxy-session.${Buffer.from('P1').toString('base64url')}`);
    expect(socket.options).toBeUndefined();
    expect(mode()).toBe('subprotocol');
  });

  it('falls back to the X-Armada-Proxy-Session header when the subprotocol attempt never opens', () => {
    const modes: string[] = [];
    const { factory, mode } = createProxySocketFactory({ proxyToken: () => 'P1', ctor, onModeChange: (m) => modes.push(m) });
    factory('wss://p/ws');
    FakeNativeSocket.created[0].fail();
    expect(mode()).toBe('header');
    factory('wss://p/ws');
    const second = FakeNativeSocket.created[1];
    expect(second.protocols).toBeNull();
    expect(second.options).toEqual({ headers: { 'X-Armada-Proxy-Session': 'P1' } });
    second.open();
    second.fail();
    expect(mode()).toBe('header');
    expect(modes).toEqual(['header']);
  });

  it('keeps a mode that opened once, and a client-side close is not a failure', () => {
    const { factory, mode } = createProxySocketFactory({ proxyToken: () => 'P1', ctor });
    const wrapped = factory('wss://p/ws');
    wrapped.close();
    expect(mode()).toBe('subprotocol');
    factory('wss://p/ws');
    FakeNativeSocket.created[1].open();
    FakeNativeSocket.created[1].fail();
    factory('wss://p/ws');
    FakeNativeSocket.created[2].fail();
    expect(mode()).toBe('subprotocol');
  });

  it('forwards socket events to what ArmadaSocket attached', () => {
    const { factory } = createProxySocketFactory({ proxyToken: () => 'P1', ctor });
    const wrapped = factory('wss://p/ws');
    const events: string[] = [];
    wrapped.onopen = () => events.push('open');
    wrapped.onmessage = (ev) => events.push(`msg:${String(ev.data)}`);
    wrapped.onclose = () => events.push('close');
    const raw = FakeNativeSocket.created[0];
    raw.open();
    expect(wrapped.readyState).toBe(1);
    raw.onmessage?.({ data: 'x' });
    wrapped.send('hello');
    raw.fail();
    expect(events).toEqual(['open', 'msg:x', 'close']);
    expect(raw.sent).toEqual(['hello']);
  });
});

// ------------------------------------------------------------------------------------------------------------------
// Proxy profiles in the session (AuthContext)

const ME = { tenant: { id: 'ten_1', name: 'Default' }, user: { id: 'usr_1', email: 'admin@armada', isAdmin: true, isTenantAdmin: true }, passwordChangeRequired: false } as unknown as WhoAmIResult;
const PROXY_DRAFT = { name: 'Remote', url: 'https://proxy.example', kind: 'Proxy' as const, biometricUnlock: false };
const CONNECTED = { isAuthenticated: true, selectedInstanceId: 'armada-1', selectedInstance: { instanceId: 'armada-1', state: 'connected' }, relay: { api: true, websocket: true, dashboard: true } };
const NONE_SELECTED = { isAuthenticated: true, selectedInstanceId: null, selectedInstance: null, relay: null };

function mockProxy(): jest.Mocked<ProxyClient> {
  return {
    baseUrl: 'https://proxy.example',
    login: jest.fn(async () => ({ token: 'P1', expiresUtc: null, selectedInstanceId: null })),
    listInstances: jest.fn(async () => [{ instanceId: 'armada-1', state: 'connected', capabilities: ['dashboard.http.relay'] }]),
    selectInstance: jest.fn(async () => CONNECTED),
    sessionContext: jest.fn(async () => CONNECTED),
    logoutInstance: jest.fn(async () => NONE_SELECTED),
    logout: jest.fn(async () => undefined),
  } as unknown as jest.Mocked<ProxyClient>;
}

let proxy: jest.Mocked<ProxyClient>;
let hooks: AuthHooks;

function wrapper({ children }: { children: ReactNode }) {
  return <AuthProvider hooks={hooks} proxyClientFactory={() => proxy}>{children}</AuthProvider>;
}

async function mount() {
  const hook = await renderHook(() => useAuth(), { wrapper });
  await waitFor(() => expect(hook.result.current.status).not.toBe('loading'));
  return hook;
}

/** Sign in all the way: proxy portal, instance, Admiral. */
async function signedInThroughProxy() {
  const hook = await mount();
  await act(async () => { await hook.result.current.saveProfile(PROXY_DRAFT); });
  await act(async () => { await hook.result.current.proxySignIn('pw'); });
  await act(async () => { await hook.result.current.proxySelectInstance('armada-1'); });
  await act(async () => { await hook.result.current.login('A1', { method: 'token' }); });
  return hook;
}

describe('Proxy profiles', () => {
  beforeEach(async () => {
    await AsyncStorage.clear();
    secureStore.clear();
    jest.clearAllMocks();
    proxy = mockProxy();
    hooks = { onSessionEnding: jest.fn(async () => undefined) };
    api.whoami.mockResolvedValue(ME);
  });

  it('a new Proxy profile starts at the portal step', async () => {
    const { result } = await mount();
    await act(async () => { await result.current.saveProfile(PROXY_DRAFT); });
    expect(result.current.status).toBe('signedOut');
    expect(result.current.proxyStage).toBe('portal');
  });

  it('portal sign-in keeps the proxy token in secure storage and sends it on relayed requests', async () => {
    const { result } = await mount();
    await act(async () => { await result.current.saveProfile(PROXY_DRAFT); });
    await act(async () => { await result.current.proxySignIn('pw'); });
    const id = result.current.activeProfile!.id;
    expect(secureStore.get(proxyTokenKey(id))).toBe('P1');
    const values = await Promise.all((await AsyncStorage.getAllKeys()).map((k) => AsyncStorage.getItem(k)));
    expect(values.join('|')).not.toContain('P1');
    expect(api.configureClient).toHaveBeenCalledWith({ headers: { 'X-Armada-Proxy-Session': 'P1' } });
    expect(result.current.requestHeaders).toEqual({ 'X-Armada-Proxy-Session': 'P1' });
    expect(result.current.proxyStage).toBe('instance');
  });

  it('a wrong proxy password surfaces the ProxyError and stores nothing', async () => {
    proxy.login.mockRejectedValueOnce(new ProxyError('unauthorized', 401, 'Proxy password is invalid.'));
    const { result } = await mount();
    await act(async () => { await result.current.saveProfile(PROXY_DRAFT); });
    await act(async () => {
      await expect(result.current.proxySignIn('wrong')).rejects.toMatchObject({ kind: 'unauthorized' });
    });
    expect(secureStore.size).toBe(0);
    expect(result.current.proxyStage).toBe('portal');
  });

  it('picking an instance remembers it, then the Admiral sign-in goes through the relay', async () => {
    const { result } = await signedInThroughProxy();
    expect(proxy.selectInstance).toHaveBeenCalledWith('P1', 'armada-1');
    expect(result.current.activeProfile?.proxyInstanceId).toBe('armada-1');
    expect(result.current.status).toBe('signedIn');
    expect(result.current.proxyStage).toBeNull();
    expect(result.current.sessionToken).toBe('A1');
    expect(result.current.proxyToken).toBe('P1');
    const id = result.current.activeProfile!.id;
    expect(secureStore.get(tokenKey(id))).toBe('A1');
  });

  it('restores a proxy session on launch: proxy context, then whoami through the relay', async () => {
    const first = await signedInThroughProxy();
    await first.unmount();
    jest.clearAllMocks();
    const second = await mount();
    await waitFor(() => expect(second.result.current.status).toBe('signedIn'));
    expect(proxy.sessionContext).toHaveBeenCalledWith('P1');
    expect(api.configureClient).toHaveBeenCalledWith({ baseUrl: 'https://proxy.example' });
    expect(api.configureClient).toHaveBeenCalledWith({ headers: { 'X-Armada-Proxy-Session': 'P1' } });
    expect(api.setAuthToken).toHaveBeenLastCalledWith('A1');
  });

  it('an expired proxy session on launch goes back to the portal and keeps the Admiral token', async () => {
    const first = await signedInThroughProxy();
    const id = first.result.current.activeProfile!.id;
    await first.unmount();
    proxy.sessionContext.mockRejectedValueOnce(new ProxyError('unauthorized', 401, 'expired'));
    const second = await mount();
    expect(second.result.current.status).toBe('signedOut');
    expect(second.result.current.proxyStage).toBe('portal');
    expect(second.result.current.proxyExpired).toBe(true);
    expect(secureStore.has(proxyTokenKey(id))).toBe(false);
    expect(secureStore.get(tokenKey(id))).toBe('A1');
  });

  it('a 401 while signed in, with the proxy session gone, needs only the proxy password to resume', async () => {
    const { result } = await signedInThroughProxy();
    const id = result.current.activeProfile!.id;
    proxy.sessionContext.mockRejectedValueOnce(new ProxyError('unauthorized', 401, 'expired'));
    await act(async () => { api.__fireUnauthorized(); });
    await waitFor(() => expect(result.current.proxyStage).toBe('portal'));
    expect(result.current.proxyExpired).toBe(true);
    expect(secureStore.get(tokenKey(id))).toBe('A1');
    expect(hooks.onSessionEnding).not.toHaveBeenCalled();

    proxy.login.mockResolvedValueOnce({ token: 'P2', expiresUtc: null, selectedInstanceId: null });
    await act(async () => { await result.current.proxySignIn('pw'); });
    expect(proxy.selectInstance).toHaveBeenLastCalledWith('P2', 'armada-1');
    expect(result.current.status).toBe('signedIn');
    expect(result.current.proxyToken).toBe('P2');
    expect(result.current.sessionToken).toBe('A1');
    expect(result.current.proxyExpired).toBe(false);
  });

  it('a 401 while signed in, with the proxy session valid, drops only the Admiral token', async () => {
    const { result } = await signedInThroughProxy();
    const id = result.current.activeProfile!.id;
    await act(async () => { api.__fireUnauthorized(); });
    await waitFor(() => expect(result.current.status).toBe('signedOut'));
    expect(result.current.proxyStage).toBe('admiral');
    expect(secureStore.has(tokenKey(id))).toBe(false);
    expect(secureStore.get(proxyTokenKey(id))).toBe('P1');
    expect(hooks.onSessionEnding).toHaveBeenCalledWith(expect.objectContaining({ id }), null);
  });

  it('a remembered instance that is no longer connected leads to the picker', async () => {
    const first = await signedInThroughProxy();
    await first.unmount();
    proxy.sessionContext.mockResolvedValueOnce(NONE_SELECTED);
    proxy.selectInstance.mockRejectedValueOnce(new ProxyError('conflict', 409, 'not connected'));
    const second = await mount();
    expect(second.result.current.status).toBe('signedOut');
    expect(second.result.current.proxyStage).toBe('instance');
  });

  it('a relay 409 from whoami (instance disconnected) leads to the picker', async () => {
    const first = await signedInThroughProxy();
    await first.unmount();
    api.whoami.mockRejectedValueOnce(new client.ApiError('Select a connected deployment', 409, null));
    const second = await mount();
    expect(second.result.current.proxyStage).toBe('instance');
  });

  it('sign-out ends the Admiral session first (push cleanup gets a session with the proxy header)', async () => {
    const { result } = await signedInThroughProxy();
    const id = result.current.activeProfile!.id;
    await act(async () => { await result.current.logout(); });
    expect(hooks.onSessionEnding).toHaveBeenCalledWith(
      expect.objectContaining({ id }),
      { baseUrl: 'https://proxy.example', token: 'A1', headers: { 'X-Armada-Proxy-Session': 'P1' } },
    );
    expect(result.current.status).toBe('signedOut');
    expect(result.current.proxyStage).toBe('admiral');
    expect(secureStore.has(tokenKey(id))).toBe(false);
    expect(secureStore.get(proxyTokenKey(id))).toBe('P1');
  });

  it('signing out of the proxy logs out there and forgets both tokens', async () => {
    const { result } = await signedInThroughProxy();
    await act(async () => { await result.current.proxySignOut(); });
    expect(proxy.logout).toHaveBeenCalledWith('P1');
    expect(secureStore.size).toBe(0);
    expect(result.current.proxyStage).toBe('portal');
    expect(api.configureClient).toHaveBeenLastCalledWith({ headers: null });
  });

  it('choosing a different instance discards the Admiral token of the previous one', async () => {
    const { result } = await signedInThroughProxy();
    const id = result.current.activeProfile!.id;
    await act(async () => { await result.current.proxySignOut(); });
    await act(async () => { await result.current.proxySignIn('pw'); });
    secureStore.set(tokenKey(id), 'A-old');
    await act(async () => { await result.current.proxySelectInstance('armada-2'); });
    expect(secureStore.has(tokenKey(id))).toBe(false);
    expect(result.current.activeProfile?.proxyInstanceId).toBe('armada-2');
    expect(result.current.proxyStage).toBe('admiral');
  });

  it('deleting a Proxy profile logs out of the proxy and removes both tokens', async () => {
    const { result } = await signedInThroughProxy();
    const id = result.current.activeProfile!.id;
    await act(async () => { await result.current.deleteProfile(id); });
    expect(hooks.onSessionEnding).toHaveBeenCalledWith(expect.objectContaining({ id }), expect.objectContaining({ token: 'A1' }));
    expect(proxy.logout).toHaveBeenCalledWith('P1');
    expect(secureStore.size).toBe(0);
  });
});
