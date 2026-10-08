import { camelizeKeys, type ProxySessionContext } from '@dashboard/api/client';
import { sha256Hex } from './sha256';

/**
 * Armada.Proxy's own API (`/proxy-api/v1/*`) for a native client: no cookies (the login asks for none, and requests
 * omit credentials); the proxy session token travels as
 * `Authorization: Bearer` (docs/PROXY_API.md, Native Clients). These routes are never relayed to an Admiral, so
 * they do not go through the shared dashboard client (which carries the Admiral's credentials).
 */

export type { ProxySessionContext };

/** One connected Admiral as the proxy lists it. */
export interface ProxyInstance {
  instanceId: string;
  state: 'connected' | 'stale' | 'offline' | string;
  armadaVersion?: string | null;
  capabilities?: string[];
  remoteAddress?: string | null;
  lastSeenUtc?: string | null;
}

export interface ProxyLoginResult {
  token: string;
  expiresUtc: string | null;
  selectedInstanceId: string | null;
}

/** Why a proxy call failed; callers branch on `kind`, never on message text. */
export type ProxyErrorKind =
  | 'unauthorized' // 401: wrong password, or the proxy session expired or was logged out
  | 'lockedOut' // 429: too many failed logins from this address
  | 'conflict' // 409: the instance is not connected or not relay-capable
  | 'notFound' // 404: unknown instance (or not an Armada.Proxy at this address)
  | 'badRequest'
  | 'network'
  | 'server';

export class ProxyError extends Error {
  kind: ProxyErrorKind;
  status: number;
  /** Seconds from Retry-After on a lockout. */
  retryAfterSeconds: number | null;

  constructor(kind: ProxyErrorKind, status: number, message: string, retryAfterSeconds: number | null = null) {
    super(message);
    this.name = 'ProxyError';
    this.kind = kind;
    this.status = status;
    this.retryAfterSeconds = retryAfterSeconds;
  }
}

export function isProxyError(err: unknown, kind: ProxyErrorKind): boolean {
  return err instanceof ProxyError && err.kind === kind;
}

/** The login proof: sha256hex('proxy-browser-login:proxy:' + nonce + ':' + sha256hex(trim(password))). */
export function proxyLoginProof(password: string, nonce: string): string {
  const passwordHash = sha256Hex(password.trim());
  return sha256Hex(`proxy-browser-login:proxy:${nonce.trim().toLowerCase()}:${passwordHash}`);
}

/** Header that carries the proxy session on relayed routes (`/api/v1/*`, `/ws` upgrade). */
export const PROXY_SESSION_HEADER = 'X-Armada-Proxy-Session';

export function proxySessionHeaders(proxyToken: string | null): Record<string, string> | null {
  return proxyToken ? { [PROXY_SESSION_HEADER]: proxyToken } : null;
}

export type FetchLike = (url: string, init?: { method?: string; headers?: Record<string, string>; body?: string; signal?: AbortSignal; credentials?: 'omit' }) => Promise<{
  status: number;
  ok: boolean;
  headers: { get: (name: string) => string | null };
  text: () => Promise<string>;
}>;

export interface ProxyClientOptions {
  fetch?: FetchLike;
  timeoutMs?: number;
}

function kindForStatus(status: number): ProxyErrorKind {
  if (status === 401) return 'unauthorized';
  if (status === 429) return 'lockedOut';
  if (status === 409) return 'conflict';
  if (status === 404) return 'notFound';
  if (status === 400 || status === 413) return 'badRequest';
  return 'server';
}

/** A client for one proxy origin (`https://proxy.example`, no trailing slash). */
export function createProxyClient(baseUrl: string, options: ProxyClientOptions = {}) {
  const base = baseUrl.replace(/\/+$/, '');
  const doFetch: FetchLike = options.fetch ?? ((url, init) => fetch(url, init) as unknown as ReturnType<FetchLike>);
  const timeoutMs = options.timeoutMs ?? 20000;

  async function call<T>(method: string, path: string, token: string | null, body?: unknown): Promise<T> {
    const headers: Record<string, string> = { Accept: 'application/json' };
    if (body !== undefined) headers['Content-Type'] = 'application/json';
    if (token) headers.Authorization = `Bearer ${token}`;
    const controller = new AbortController();
    const timer = setTimeout(() => controller.abort(), timeoutMs);
    let res: Awaited<ReturnType<FetchLike>>;
    try {
      res = await doFetch(`${base}${path}`, {
        method,
        headers,
        body: body !== undefined ? JSON.stringify(body) : undefined,
        signal: controller.signal,
        // The session travels only as a header: never store or send a cookie (React Native's fetch would keep one
        // in the platform cookie jar, outside the Keychain / Keystore, and send it automatically).
        credentials: 'omit',
      });
    } catch {
      throw new ProxyError('network', 0, 'The proxy could not be reached.');
    } finally {
      clearTimeout(timer);
    }
    const text = await res.text().catch(() => '');
    if (!res.ok) {
      let message = text || String(res.status);
      try {
        const parsed = JSON.parse(text) as { error?: unknown; message?: unknown };
        if (typeof parsed.error === 'string') message = parsed.error;
        else if (typeof parsed.message === 'string') message = parsed.message;
      } catch {
        // Not JSON; keep the raw text.
      }
      const retryAfter = res.status === 429 ? Number(res.headers.get('Retry-After')) : NaN;
      throw new ProxyError(kindForStatus(res.status), res.status, message, Number.isFinite(retryAfter) ? retryAfter : null);
    }
    if (!text) return undefined as T;
    try {
      return camelizeKeys(JSON.parse(text)) as T;
    } catch {
      // A 200 that is not JSON is not an Armada.Proxy (for example a web server or an Admiral at this address).
      throw new ProxyError('notFound', res.status, 'The address did not answer like an Armada.Proxy.');
    }
  }

  return {
    baseUrl: base,

    /** Challenge, proof, and login. Resolves the session token (keep it in secure storage). */
    async login(password: string): Promise<ProxyLoginResult> {
      const challenge = await call<{ nonce?: string }>('GET', '/proxy-api/v1/auth/challenge', null);
      if (!challenge?.nonce) throw new ProxyError('notFound', 200, 'The address did not answer like an Armada.Proxy.');
      const result = await call<Partial<ProxyLoginResult>>('POST', '/proxy-api/v1/auth/login', null, {
        nonce: challenge.nonce,
        proofSha256: proxyLoginProof(password, challenge.nonce),
        // A native client keeps the token itself; the proxy then sets no session cookie (docs/PROXY_API.md).
        setCookie: false,
      });
      if (!result?.token) throw new ProxyError('server', 200, 'The proxy did not return a session token.');
      return { token: result.token, expiresUtc: result.expiresUtc ?? null, selectedInstanceId: result.selectedInstanceId ?? null };
    },

    async listInstances(token: string): Promise<ProxyInstance[]> {
      const result = await call<{ instances?: ProxyInstance[] }>('GET', '/proxy-api/v1/instances', token);
      return Array.isArray(result?.instances) ? result.instances : [];
    },

    selectInstance(token: string, instanceId: string): Promise<ProxySessionContext> {
      return call<ProxySessionContext>('POST', '/proxy-api/v1/session/instance', token, { instanceId });
    },

    sessionContext(token: string): Promise<ProxySessionContext> {
      return call<ProxySessionContext>('GET', '/proxy-api/v1/session/context', token);
    },

    logoutInstance(token: string): Promise<ProxySessionContext> {
      return call<ProxySessionContext>('POST', '/proxy-api/v1/session/logout-instance', token, {});
    },

    async logout(token: string): Promise<void> {
      await call<unknown>('POST', '/proxy-api/v1/auth/logout', token, {});
    },
  };
}

export type ProxyClient = ReturnType<typeof createProxyClient>;

/** True when the session context names a selected instance the relay can reach. */
export function hasUsableInstance(ctx: ProxySessionContext | null | undefined): boolean {
  if (!ctx?.selectedInstanceId) return false;
  const state = ctx.selectedInstance?.state;
  if (state && state !== 'connected' && state !== 'stale') return false;
  return ctx.relay?.api !== false;
}

/** True when the instance can be picked (connected and advertises the HTTP relay). */
export function isSelectableInstance(instance: ProxyInstance): boolean {
  if (instance.state !== 'connected' && instance.state !== 'stale') return false;
  return !instance.capabilities || instance.capabilities.includes('dashboard.http.relay');
}
