import { FetchLike } from '../../src/api/blackbox';

/** One recorded request. */
export interface RecordedRequest {
  method: string;
  url: string;
  path: string;
  headers: Record<string, string>;
  body: unknown;
}

/** What a handler returns. */
export interface MockResponse {
  status?: number;
  json?: unknown;
  text?: string;
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

  public readonly fetch: FetchLike = (url, init) => {
    const parsed = new URL(url);
    const req: RecordedRequest = {
      method: init.method,
      url,
      path: parsed.pathname,
      headers: init.headers,
      body: init.body ? JSON.parse(init.body) : undefined,
    };
    this.requests.push(req);
    const handler = this._Routes.get(`${init.method} ${parsed.pathname}`) ?? this._Fallback;
    const response = handler ? handler(req) : { status: 404, json: { Error: 'NotFound', Message: 'no mock route' } };
    if (!response) return Promise.resolve(make(404, JSON.stringify({ Error: 'NotFound' })));
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
    const text = response.text ?? (response.json !== undefined ? JSON.stringify(response.json) : '');
    return Promise.resolve(make(response.status ?? 200, text));
  };
}

function make(status: number, text: string): Awaited<ReturnType<FetchLike>> {
  return {
    status,
    ok: status >= 200 && status < 300,
    headers: { get: () => null },
    text: async () => text,
  };
}

/** An EnumerationResult with one page. */
export function page<T>(objects: T[], totalRecords?: number): { Success: boolean; PageNumber: number; PageSize: number; TotalPages: number; TotalRecords: number; Objects: T[] } {
  return { Success: true, PageNumber: 1, PageSize: objects.length || 10, TotalPages: 1, TotalRecords: totalRecords ?? objects.length, Objects: objects };
}
