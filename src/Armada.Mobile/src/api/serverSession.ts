import { ApiError, camelizeKeys } from '@dashboard/api/client';
import { proxySessionHeaders } from '../proxy/proxyApi';
import type { ServerProfile } from '../profiles/types';
import { readProxyToken, readToken } from '../storage/secure';

/**
 * Everything needed to call one Admiral as one user, independent of the shared client's global configuration:
 * the base URL (the Admiral, or the Armada.Proxy origin that relays to it), the Admiral token, and extra headers
 * (the proxy session). Used for calls that must reach a profile other than the active one (for example removing
 * this device's push registration when a background profile is deleted).
 */
export interface ServerSession {
  baseUrl: string;
  token: string;
  headers: Record<string, string> | null;
}

export function sessionFor(profile: Pick<ServerProfile, 'url' | 'kind'>, token: string, proxyToken: string | null): ServerSession {
  return {
    baseUrl: profile.url.replace(/\/+$/, ''),
    token,
    headers: profile.kind === 'Proxy' ? proxySessionHeaders(proxyToken) : null,
  };
}

/** The stored session of a profile, or null when it has no Admiral token (or a Proxy profile has no proxy session). */
export async function storedSessionFor(profile: ServerProfile): Promise<ServerSession | null> {
  const token = await readToken(profile.id);
  if (!token) return null;
  const proxyToken = profile.kind === 'Proxy' ? await readProxyToken(profile.id) : null;
  if (profile.kind === 'Proxy' && !proxyToken) return null;
  return sessionFor(profile, token, proxyToken);
}

export type SessionFetch = (url: string, init: { method: string; headers: Record<string, string>; body?: string; signal?: AbortSignal }) => Promise<{
  status: number;
  ok: boolean;
  text: () => Promise<string>;
}>;

/** A JSON request with the session's credentials; non-2xx responses throw the shared client's ApiError. */
export async function sessionRequest<T>(
  session: ServerSession,
  method: string,
  path: string,
  body?: unknown,
  fetchImpl: SessionFetch = (url, init) => fetch(url, init),
  timeoutMs = 20000,
): Promise<T> {
  const headers: Record<string, string> = { ...(session.headers ?? {}), 'Content-Type': 'application/json', 'X-Token': session.token };
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), timeoutMs);
  try {
    const res = await fetchImpl(`${session.baseUrl}${path}`, {
      method,
      headers,
      body: body === undefined ? undefined : JSON.stringify(body),
      signal: controller.signal,
    });
    const text = await res.text().catch(() => '');
    if (!res.ok) {
      let message = `${res.status}`;
      let data: unknown = null;
      try {
        const parsed = JSON.parse(text) as Record<string, unknown>;
        const m = parsed.Message ?? parsed.message ?? parsed.Error ?? parsed.error;
        if (typeof m === 'string') message = m;
        data = camelizeKeys(parsed.Data ?? parsed.data ?? null);
      } catch {
        if (text) message = text;
      }
      throw new ApiError(message, res.status, data);
    }
    if (res.status === 204 || !text) return undefined as T;
    return camelizeKeys(JSON.parse(text)) as T;
  } finally {
    clearTimeout(timer);
  }
}
