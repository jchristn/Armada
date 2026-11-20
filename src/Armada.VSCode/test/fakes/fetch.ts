/*
 * A fetch mock for the shared dashboard client (@dashboard/api/client), which calls the global fetch. Install it with
 * useMock(): it replaces globalThis.fetch and points the shared client at BASE with a test credential. Responses are
 * real Response objects; mocked server bodies are written in the server's PascalCase, as the Admiral sends them.
 */
import { HostCredential, useAdmiral } from '../../src/transport/hostClient';

/** Base URL every test client talks to. */
export const BASE = 'http://armada.test:7890';

/** The credential tests connect with. */
export const TEST_CREDENTIAL: HostCredential = { secret: 'secret-key', header: 'Bearer' };

/** One recorded request. */
export interface RecordedRequest {
  method: string;
  url: string;
  path: string;
  query: URLSearchParams;
  headers: Record<string, string>;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  body: any;
}

/** What a handler returns. */
export interface MockResponse {
  status?: number;
  json?: unknown;
  text?: string;
  contentType?: string;
  /** Reject the fetch (simulates a network failure). */
  networkError?: Error;
  /** Never resolve until aborted (simulates a timeout). */
  hang?: boolean;
}

export type Handler = (req: RecordedRequest) => MockResponse | undefined;

/** A fetch mock routed by `METHOD /path` (query string excluded) or a catch-all handler. */
export class MockFetch {
  public readonly requests: RecordedRequest[] = [];
  private readonly _Routes = new Map<string, Handler>();
  private _Fallback: Handler | undefined;

  public on(method: string, path: string, response: MockResponse | Handler): this {
    this._Routes.set(`${method.toUpperCase()} ${path}`, typeof response === 'function' ? response : () => response);
    return this;
  }

  public otherwise(handler: Handler): this {
    this._Fallback = handler;
    return this;
  }

  /** Requests to a path (query excluded). */
  public calls(method: string, path: string): RecordedRequest[] {
    return this.requests.filter((r) => r.method === method && r.path === path);
  }

  public readonly fetch = (input: string | URL | Request, init: RequestInit = {}): Promise<Response> => {
    const url = typeof input === 'string' ? input : input instanceof URL ? input.toString() : input.url;
    const parsed = new URL(url);
    const method = (init.method ?? 'GET').toUpperCase();
    const headers: Record<string, string> = {};
    if (init.headers) for (const [k, v] of Object.entries(init.headers as Record<string, string>)) headers[k] = v;
    const req: RecordedRequest = {
      method,
      url,
      path: parsed.pathname,
      query: parsed.searchParams,
      headers,
      body: typeof init.body === 'string' && init.body ? JSON.parse(init.body) : undefined,
    };
    this.requests.push(req);
    const handler = this._Routes.get(`${method} ${parsed.pathname}`) ?? this._Fallback;
    const response = handler ? handler(req) : { status: 404, json: { Error: 'NotFound', Message: 'no mock route' } };
    if (!response) return Promise.resolve(make({ status: 404, json: { Error: 'NotFound' } }));
    if (response.networkError) return Promise.reject(response.networkError);
    if (response.hang) {
      return new Promise((_resolve, reject) => {
        init.signal?.addEventListener('abort', () => {
          const err = new Error('aborted');
          err.name = 'AbortError';
          reject(err);
        });
      });
    }
    return Promise.resolve(make(response));
  };

  /** Replaces the global fetch with this mock. */
  public install(): this {
    (globalThis as { fetch: unknown }).fetch = this.fetch;
    return this;
  }
}

/** Installs the mock and points the shared client at BASE. */
export function useMock(mock: MockFetch, credential: HostCredential = TEST_CREDENTIAL): MockFetch {
  mock.install();
  useAdmiral(BASE, credential);
  return mock;
}

function make(response: MockResponse): Response {
  const status = response.status ?? 200;
  if (status === 204) return new Response(null, { status });
  const text = response.text ?? (response.json !== undefined ? JSON.stringify(response.json) : '');
  const contentType = response.contentType ?? (response.text !== undefined ? 'text/markdown; charset=utf-8' : 'application/json; charset=utf-8');
  return new Response(text, { status, headers: { 'Content-Type': contentType } });
}

/** Converts camelCase keys to the server's PascalCase (action arguments are kept as written). */
// eslint-disable-next-line @typescript-eslint/no-explicit-any
export function pascalize(value: any, parentKey = ''): any {
  if (Array.isArray(value)) return value.map((v) => pascalize(v, parentKey));
  if (value && typeof value === 'object') {
    if (parentKey === 'arguments' || parentKey === 'Arguments') return value;
    return Object.fromEntries(Object.entries(value).map(([k, v]) => [k.charAt(0).toUpperCase() + k.slice(1), pascalize(v, k)]));
  }
  return value;
}

/** An EnumerationResult with one page, in the server's shape. */
export function page<T>(objects: T[], totalRecords?: number, extra: Record<string, unknown> = {}): Record<string, unknown> {
  return { Success: true, PageNumber: 1, PageSize: objects.length || 10, TotalPages: 1, TotalRecords: totalRecords ?? objects.length, Objects: pascalize(objects), ...extra };
}
