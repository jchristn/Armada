import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  ApiError,
  NetworkError,
  TimeoutError,
  getProxySessionContext,
  getWorkspaceTree,
  isApiStatus,
  logoutProxy,
  setOnUnauthorized,
} from './client';

function jsonResponse(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
}

describe('client error classification by status and class', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    vi.useRealTimers();
  });

  it('a missing workspace path is detected by HTTP 404 whatever the message says', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse(404, { Message: 'Ruta no encontrada' })));
    const error = await getWorkspaceTree('vsl_1', 'src/missing').catch((e: unknown) => e);
    expect(error).toBeInstanceOf(ApiError);
    expect(isApiStatus(error, 404)).toBe(true);
  });

  it('the old message text on another status is not a 404', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse(400, { Message: 'Workspace path not found.' })));
    const error = await getWorkspaceTree('vsl_1', '../x').catch((e: unknown) => e);
    expect(isApiStatus(error, 404)).toBe(false);
    expect(isApiStatus(error, 400)).toBe(true);
  });

  it('a 401 is an ApiError with status 401 and calls the unauthorized hook; message text kept', async () => {
    const onUnauthorized = vi.fn();
    setOnUnauthorized(onUnauthorized);
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse(401, {})));
    const error = await getWorkspaceTree('vsl_1').catch((e: unknown) => e);
    expect(isApiStatus(error, 401)).toBe(true);
    expect((error as Error).message).toBe('Unauthorized');
    expect(onUnauthorized).toHaveBeenCalledTimes(1);
  });

  it('a client-side timeout is a TimeoutError (detected by class, message text kept)', async () => {
    vi.useFakeTimers();
    vi.stubGlobal('fetch', vi.fn((_url: string, init: RequestInit) => new Promise((_resolve, reject) => {
      init.signal?.addEventListener('abort', () => reject(new DOMException('The operation was aborted.', 'AbortError')));
    })));
    const pending = getWorkspaceTree('vsl_1').catch((e: unknown) => e);
    await vi.advanceTimersByTimeAsync(30000);
    const error = await pending;
    expect(error).toBeInstanceOf(TimeoutError);
    expect(error).not.toBeInstanceOf(ApiError);
    expect((error as Error).message).toBe('Request timed out');
  });

  it('a fetch that gets no response is a NetworkError whatever class the host rejects with', async () => {
    class HostFetchError extends Error {}
    for (const rejection of [new TypeError('Failed to fetch'), new HostFetchError('fetch failed: The resource could not be loaded')]) {
      vi.stubGlobal('fetch', vi.fn().mockRejectedValue(rejection));
      const error = await getWorkspaceTree('vsl_1').catch((e: unknown) => e);
      expect(error).toBeInstanceOf(NetworkError);
      expect(error).not.toBeInstanceOf(ApiError);
      expect((error as NetworkError).cause).toBe(rejection);
    }
  });

  it('a timeout is a TimeoutError even when the host rejects the aborted fetch with its own error class', async () => {
    vi.useFakeTimers();
    class HostFetchError extends Error {}
    vi.stubGlobal('fetch', vi.fn((_url: string, init: RequestInit) => new Promise((_resolve, reject) => {
      init.signal?.addEventListener('abort', () => reject(new HostFetchError('fetch failed: cancelled')));
    })));
    const pending = getWorkspaceTree('vsl_1').catch((e: unknown) => e);
    await vi.advanceTimersByTimeAsync(30000);
    const error = await pending;
    expect(error).toBeInstanceOf(TimeoutError);
    expect(error).not.toBeInstanceOf(NetworkError);
  });

  it('proxy 404/401 are typed: session context is null and logout tolerates 404', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('', { status: 404 })));
    await expect(getProxySessionContext()).resolves.toBeNull();
    await expect(logoutProxy()).resolves.toBeUndefined();
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('', { status: 401 })));
    await expect(getProxySessionContext()).resolves.toBeNull();
    await expect(logoutProxy()).rejects.toSatisfy((e: unknown) => isApiStatus(e, 401));
  });
});
