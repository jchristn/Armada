import { getClientBaseUrl } from '@dashboard/api/client';
import {
  buildExplorerOperations,
  findOperationForReplay,
  generateCodeSnippets,
  type ExplorerOperation,
  type ExplorerRequestPreview,
  type ExplorerResponse,
  type OpenApiSpec,
  type ReplayRequest,
} from '@dashboard/lib/apiExplorer';

/**
 * The mobile API Explorer's network side: loading the live OpenAPI document and sending a built request. The
 * operation model, schema examples, and code snippets come from the dashboard's lib/apiExplorer.ts. URLs are built
 * by hand because React Native's URL has no searchParams.
 */

/** Auth and proxy headers the explorer sends: the session token (as the dashboard does) plus the host's headers. */
export function explorerAuthHeaders(sessionToken: string | null, hostHeaders: Record<string, string> | null): Record<string, string> {
  const headers: Record<string, string> = { ...(hostHeaders ?? {}) };
  if (sessionToken) headers['X-Token'] = sessionToken;
  return headers;
}

/** Absolute URL of a server path (the client's base URL plus the path). */
export function serverUrl(path: string): string {
  return `${getClientBaseUrl()}${path}`;
}

/** Loads /openapi.json and lists its operations. */
export async function loadExplorerSpec(headers: Record<string, string>): Promise<{ spec: OpenApiSpec; operations: ExplorerOperation[] }> {
  const result = await fetch(serverUrl('/openapi.json'), { headers });
  if (!result.ok) throw new Error(`Failed to load OpenAPI document (${result.status})`);
  const spec = (await result.json()) as OpenApiSpec;
  return { spec, operations: buildExplorerOperations(spec) };
}

/**
 * The request the builder will send: path parameters substituted (an empty one stays as {name}, as the dashboard
 * shows it), non-empty query parameters appended, the session token and non-empty custom headers, and Content-Type
 * when a body is sent.
 */
export function buildExplorerRequest(
  operation: ExplorerOperation,
  values: ExplorerValues,
  baseUrl: string,
  sessionToken: string | null,
): ExplorerRequestPreview {
  const path = Object.entries(values.path).reduce(
    (current, [key, value]) => current.replace(`{${key}}`, encodeURIComponent(value || `{${key}}`)),
    operation.path,
  );
  const query = Object.entries(values.query)
    .filter(([, value]) => value !== '')
    .map(([key, value]) => `${encodeURIComponent(key)}=${encodeURIComponent(value)}`)
    .join('&');
  const headers: Record<string, string> = {};
  if (sessionToken) headers['X-Token'] = sessionToken;
  Object.entries(values.header).forEach(([key, value]) => {
    if (value !== '' && key.toLowerCase() !== 'x-token') headers[key] = value;
  });
  if (operation.requestBody && values.body.trim()) headers['Content-Type'] = operation.requestBodyContentType || 'application/json';
  return {
    method: operation.method,
    url: `${baseUrl}${path}${query ? `?${query}` : ''}`,
    headers,
    body: values.body.trim() || '',
    contentType: operation.requestBodyContentType || 'application/json',
  };
}

function byteLength(text: string): number {
  return typeof TextEncoder !== 'undefined' ? new TextEncoder().encode(text).length : text.length;
}

/** Sends a built request and captures the response the way the dashboard's explorer does. */
export async function sendExplorerRequest(
  request: ExplorerRequestPreview,
  hostHeaders: Record<string, string> | null,
  signal?: AbortSignal,
  now: () => number = () => Date.now(),
): Promise<ExplorerResponse> {
  const startedAt = now();
  const result = await fetch(request.url, {
    method: request.method.toUpperCase(),
    headers: { ...(hostHeaders ?? {}), ...request.headers },
    body: request.body || undefined,
    signal,
  });
  const durationMs = now() - startedAt;
  const headers: Record<string, string> = {};
  result.headers.forEach((value, key) => { headers[key] = value; });
  const contentType = result.headers.get('content-type') || '';
  let body = '';
  let sizeBytes = 0;
  let preview: unknown = null;
  if (contentType.includes('application/json') || contentType.startsWith('text/')) {
    body = await result.text();
    sizeBytes = byteLength(body);
    if (contentType.includes('application/json')) {
      try { preview = JSON.parse(body) as unknown; } catch { preview = body; }
    } else {
      preview = body;
    }
  } else {
    const blob = await result.blob();
    sizeBytes = blob.size;
    body = `Binary response (${blob.type || 'application/octet-stream'}, ${blob.size} bytes)`;
    preview = body;
  }
  return {
    ok: result.ok,
    status: result.status,
    statusText: result.statusText,
    durationMs,
    headers,
    contentType,
    body,
    preview,
    sizeBytes,
    code: generateCodeSnippets(request),
  };
}

/** Request builder values for one operation. */
export interface ExplorerValues {
  path: Record<string, string>;
  query: Record<string, string>;
  header: Record<string, string>;
  body: string;
}

function strings(values: Record<string, string | null> | null | undefined, skip: string[] = []): Record<string, string> {
  return Object.fromEntries(Object.entries(values ?? {})
    .filter(([key]) => !skip.includes(key.toLowerCase()))
    .map(([key, value]) => [key, value || '']));
}

/**
 * Matches a captured request (Request History Replay) to an operation and returns its prefilled builder values, as
 * the dashboard's explorer does with its router state: path values from the route, query, headers (without the
 * session token or Authorization), and the body. Null when no operation matches.
 */
export function replayToValues(operations: ExplorerOperation[], replay: ReplayRequest): { operationId: string; values: ExplorerValues } | null {
  const match = findOperationForReplay(operations, replay);
  if (!match) return null;
  return {
    operationId: match.operationId,
    values: {
      path: strings({ ...replay.pathValues, ...match.pathValues }),
      query: strings(replay.queryValues),
      header: strings(replay.headerValues, ['x-token', 'authorization']),
      body: replay.bodyValue || '',
    },
  };
}
