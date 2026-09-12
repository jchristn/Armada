import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useLocation, useNavigate, useParams } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';
import { useLocale } from '../context/LocaleContext';
import { useNotifications } from '../context/NotificationContext';
import CopyButton from '../components/shared/CopyButton';
import PageHeader from '../components/shared/PageHeader';
import { formatBytes, parseJsonString, methodClass } from '../lib/format';
const RESPONSE_TABS = ['preview', 'body', 'headers', 'code'] as const;
const CODE_TABS = ['curl', 'fetch', 'csharp'] as const;
const BASE_URL = import.meta.env.VITE_ARMADA_SERVER_URL || '';

type ResponseTab = typeof RESPONSE_TABS[number];
type CodeTab = typeof CODE_TABS[number];

type JsonValue = string | number | boolean | null | JsonValue[] | { [key: string]: JsonValue };

interface OpenApiSpec {
  paths?: Record<string, OpenApiPathItem>;
  components?: {
    schemas?: Record<string, OpenApiSchema>;
  };
}

interface OpenApiPathItem extends Record<string, OpenApiOperationSource | OpenApiParameter[] | undefined> {
  parameters?: OpenApiParameter[];
}

type OpenApiPathItemValue = OpenApiOperationSource | OpenApiParameter[] | undefined;

interface OpenApiOperationSource {
  operationId?: string;
  summary?: string;
  description?: string;
  tags?: string[];
  parameters?: OpenApiParameter[];
  requestBody?: {
    content?: Record<string, { schema?: OpenApiSchema }>;
  };
  responses?: Record<string, unknown>;
}

interface OpenApiParameter {
  name: string;
  in: 'path' | 'query' | 'header' | string;
  required?: boolean;
  description?: string;
  example?: unknown;
  schema?: OpenApiSchema;
}

interface OpenApiSchema {
  $ref?: string;
  type?: string;
  format?: string;
  example?: JsonValue;
  default?: JsonValue;
  enum?: JsonValue[];
  properties?: Record<string, OpenApiSchema>;
  items?: OpenApiSchema;
  allOf?: OpenApiSchema[];
  oneOf?: OpenApiSchema[];
  anyOf?: OpenApiSchema[];
}

interface ExplorerOperation {
  id: string;
  method: string;
  path: string;
  summary: string;
  description: string;
  tag: string;
  parameters: OpenApiParameter[];
  requestBody: { schema?: OpenApiSchema } | null;
  requestBodyContentType: string;
}

interface ExplorerRequestPreview {
  method: string;
  url: string;
  headers: Record<string, string>;
  body: string;
  contentType: string;
}

interface ExplorerResponse {
  ok: boolean;
  status: number;
  statusText: string;
  durationMs: number;
  headers: Record<string, string>;
  contentType: string;
  body: string;
  preview: unknown;
  sizeBytes: number;
  code: Record<CodeTab, string>;
}

interface ReplayRequest {
  operationId?: string;
  method: string;
  route: string;
  routeTemplate?: string | null;
  pathValues: Record<string, string | null>;
  queryValues: Record<string, string | null>;
  headerValues: Record<string, string | null>;
  bodyValue: string;
}

function isOperationSource(value: OpenApiPathItemValue): value is OpenApiOperationSource {
  if (!value || Array.isArray(value)) return false;
  return true;
}

function resolveSchema(schema: OpenApiSchema | undefined | null, spec: OpenApiSpec | null, seen = new Set<string>()): OpenApiSchema | null {
  if (!schema) return null;
  if (schema.$ref) {
    const refName = schema.$ref.split('/').pop();
    if (!refName || seen.has(refName)) return null;
    seen.add(refName);
    return resolveSchema(spec?.components?.schemas?.[refName], spec, seen);
  }
  if (schema.allOf?.length) {
    return schema.allOf.reduce<OpenApiSchema>((merged, item) => {
      const resolved = resolveSchema(item, spec, new Set(seen));
      return {
        ...merged,
        ...resolved,
        properties: { ...(merged.properties || {}), ...(resolved?.properties || {}) },
      };
    }, {});
  }
  if (schema.oneOf?.length) return resolveSchema(schema.oneOf[0], spec, seen);
  if (schema.anyOf?.length) return resolveSchema(schema.anyOf[0], spec, seen);
  return schema;
}

function schemaExample(schema: OpenApiSchema | undefined | null, spec: OpenApiSpec | null): JsonValue {
  const resolved = resolveSchema(schema, spec);
  if (!resolved) return {};
  if (resolved.example !== undefined) return resolved.example;
  if (resolved.default !== undefined) return resolved.default;

  switch (resolved.type) {
    case 'object': {
      const value: Record<string, JsonValue> = {};
      Object.entries(resolved.properties || {}).forEach(([key, childSchema]) => {
        value[key] = schemaExample(childSchema, spec);
      });
      return value;
    }
    case 'array':
      return resolved.items ? [schemaExample(resolved.items, spec)] : [];
    case 'integer':
    case 'number':
      return 0;
    case 'boolean':
      return false;
    case 'string':
      if (resolved.enum?.length) return resolved.enum[0];
      if (resolved.format === 'date-time') return new Date().toISOString();
      return '';
    default:
      return {};
  }
}

function parameterInitialValue(parameter: OpenApiParameter, spec: OpenApiSpec | null) {
  if (parameter.example !== undefined) return String(parameter.example);
  const resolved = resolveSchema(parameter.schema, spec);
  if (resolved?.default !== undefined) return String(resolved.default);
  if (resolved?.enum?.length) return String(resolved.enum[0]);
  return '';
}

function prettifyContent(body: string, contentType: string) {
  if (!body) return '(empty)';
  if (contentType.includes('application/json')) {
    try {
      return JSON.stringify(JSON.parse(body), null, 2);
    } catch {
      return body;
    }
  }
  return body;
}

function generateCodeSnippets(request: ExplorerRequestPreview): Record<CodeTab, string> {
  const headerLines = Object.entries(request.headers || {});
  const curlHeaders = headerLines.map(([key, value]) => `-H "${key}: ${value}"`).join(' \\\n  ');
  const fetchHeaders = headerLines.length > 0
    ? `,\n  headers: ${JSON.stringify(request.headers, null, 2).replace(/\n/g, '\n  ')}`
    : '';
  const csharpHeaders = headerLines
    .map(([key, value]) => `request.Headers.TryAddWithoutValidation("${key}", "${value}");`)
    .join('\n');
  const body = request.body ? prettifyContent(request.body, request.contentType || 'application/json') : '';
  const curlBody = request.body ? ` \\\n  --data '${body.replace(/'/g, "'\\''")}'` : '';
  const fetchBody = request.body ? `,\n  body: ${JSON.stringify(body)}` : '';
  const csharpBody = request.body
    ? `request.Content = new StringContent(${JSON.stringify(body)}, Encoding.UTF8, "${request.contentType || 'application/json'}");`
    : '';

  return {
    curl: `curl -X ${request.method.toUpperCase()} "${request.url}"${curlHeaders ? ` \\\n  ${curlHeaders}` : ''}${curlBody}`,
    fetch: `const response = await fetch(${JSON.stringify(request.url)}, {\n  method: ${JSON.stringify(request.method.toUpperCase())}${fetchHeaders}${fetchBody}\n});\n\nconst data = await response.text();`,
    csharp: `using System.Net.Http;\nusing System.Text;\n\nusing var client = new HttpClient();\nusing var request = new HttpRequestMessage(HttpMethod.${request.method.charAt(0).toUpperCase() + request.method.slice(1).toLowerCase()}, ${JSON.stringify(request.url)});\n${csharpHeaders}${csharpBody ? `\n${csharpBody}` : ''}\nusing var response = await client.SendAsync(request);\nvar body = await response.Content.ReadAsStringAsync();`,
  };
}

function getOperationSubtext(operation: ExplorerOperation) {
  const summary = operation.summary?.trim();
  const description = operation.description?.trim();
  const methodPath = `${operation.method.toUpperCase()} ${operation.path}`;
  if (description && description !== summary && description !== methodPath && description !== operation.path) return description;
  if (summary && summary !== methodPath && summary !== operation.path) return summary;
  return '';
}

function getResponseText(response: ExplorerResponse | null, responseTab: ResponseTab, codeTab: CodeTab) {
  if (!response) return '';
  if (responseTab === 'headers') return JSON.stringify(response.headers, null, 2);
  if (responseTab === 'code') return response.code[codeTab];
  if (responseTab === 'preview') {
    return typeof response.preview === 'string' ? response.preview : JSON.stringify(response.preview, null, 2);
  }
  return prettifyContent(response.body, response.contentType);
}

function buildApiBaseForDisplay() {
  return BASE_URL || window.location.origin;
}

function buildRequestUrl(path: string) {
  return BASE_URL ? `${BASE_URL}${path}` : path;
}

function parseQueryString(queryString: string | null | undefined) {
  const result: Record<string, string | null> = {};
  if (!queryString) return result;
  const search = queryString.startsWith('?') ? queryString.substring(1) : queryString;
  const params = new URLSearchParams(search);
  params.forEach((value, key) => {
    result[key] = value;
  });
  return result;
}

function buildPathRegex(template: string) {
  const keys: string[] = [];
  const pattern = template.replace(/[.*+?^${}()|[\]\\]/g, '\\$&').replace(/\\\{([^}]+)\\\}/g, (_match, key: string) => {
    keys.push(key);
    return '([^/]+)';
  });
  return { regex: new RegExp(`^${pattern}$`), keys };
}

function resolveReplayPathValues(template: string, route: string, initialValues?: Record<string, string | null>) {
  const { regex, keys } = buildPathRegex(template);
  const match = regex.exec(route);
  const pathValues: Record<string, string | null> = { ...(initialValues || {}) };
  if (!match) return pathValues;

  keys.forEach((key, index) => {
    if (!(key in pathValues) || pathValues[key] == null || pathValues[key] === '') {
      pathValues[key] = decodeURIComponent(match[index + 1]);
    }
  });

  return pathValues;
}

function findOperationForReplay(operations: ExplorerOperation[], replay: ReplayRequest) {
  for (const operation of operations) {
    if (operation.method.toLowerCase() !== replay.method.toLowerCase()) continue;
    if (replay.routeTemplate && replay.routeTemplate === operation.path) {
      return {
        operationId: operation.id,
        pathValues: resolveReplayPathValues(operation.path, replay.route, replay.pathValues),
      };
    }

    const pathValues = resolveReplayPathValues(operation.path, replay.route, replay.pathValues);
    if (Object.keys(pathValues).length === 0 && operation.path !== replay.route) continue;

    return {
      operationId: operation.id,
      pathValues,
    };
  }
  return null;
}

function ParameterSection({
  title,
  parameters,
  values,
  onChange,
}: {
  title: string;
  parameters: OpenApiParameter[];
  values: Record<string, string>;
  onChange: React.Dispatch<React.SetStateAction<Record<string, string>>>;
}) {
  return (
    <div className="api-parameter-section">
      <div className="api-section-heading">
        <h4>{title}</h4>
        <span>{parameters.length}</span>
      </div>
      {parameters.length === 0 ? (
        <div className="api-empty-copy">No parameters</div>
      ) : (
        <div className="api-input-stack">
          {parameters.map((parameter) => (
            <label key={`${parameter.in}-${parameter.name}`} className="api-input-field">
              <span>{parameter.name}</span>
              <input
                type="text"
                value={values[parameter.name] || ''}
                onChange={(event) => onChange((current) => ({ ...current, [parameter.name]: event.target.value }))}
                placeholder={parameter.description || parameter.name}
              />
            </label>
          ))}
        </div>
      )}
    </div>
  );
}

function ResponsePanel({
  response,
  responseTab,
  codeTab,
  onCodeTabChange,
}: {
  response: ExplorerResponse;
  responseTab: ResponseTab;
  codeTab: CodeTab;
  onCodeTabChange: (value: CodeTab) => void;
}) {
  if (responseTab === 'headers') {
    return (
      <div className="api-response-table">
        {Object.entries(response.headers).map(([key, value]) => (
          <div key={key} className="api-response-row">
            <span>{key}</span>
            <code>{value}</code>
          </div>
        ))}
      </div>
    );
  }

  if (responseTab === 'code') {
    return (
      <>
        <div className="api-tab-row nested">
          {CODE_TABS.map((tab) => (
            <button key={tab} type="button" className={`api-tab ${codeTab === tab ? 'active' : ''}`} onClick={() => onCodeTabChange(tab)}>
              {tab}
            </button>
          ))}
        </div>
        <pre className="api-code-block">{response.code[codeTab]}</pre>
      </>
    );
  }

  const value = getResponseText(response, responseTab, codeTab);
  return <pre className="api-code-block">{value || '(empty)'}</pre>;
}

export default function ApiExplorer() {
  const { operationId } = useParams();
  const location = useLocation();
  const navigate = useNavigate();
  const { sessionToken } = useAuth();
  const { t } = useLocale();
  const { pushToast } = useNotifications();

  const [spec, setSpec] = useState<OpenApiSpec | null>(null);
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(true);
  const [selectedOperationId, setSelectedOperationId] = useState(operationId || '');
  const [operationFilter, setOperationFilter] = useState('');
  const [selectedTag, setSelectedTag] = useState('All');
  const [pathValues, setPathValues] = useState<Record<string, string>>({});
  const [queryValues, setQueryValues] = useState<Record<string, string>>({});
  const [headerValues, setHeaderValues] = useState<Record<string, string>>({});
  const [bodyValue, setBodyValue] = useState('');
  const [response, setResponse] = useState<ExplorerResponse | null>(null);
  const [responseTab, setResponseTab] = useState<ResponseTab>('preview');
  const [codeTab, setCodeTab] = useState<CodeTab>('curl');
  const [abortController, setAbortController] = useState<AbortController | null>(null);
  const [pendingReplay, setPendingReplay] = useState<ReplayRequest | null>(null);
  const skipDefaultInitializationForOperationId = useRef<string | null>(null);

  const operations = useMemo<ExplorerOperation[]>(() => {
    if (!spec?.paths) return [];
    return Object.entries(spec.paths).flatMap(([path, pathItem]) =>
      Object.entries(pathItem)
        .flatMap(([method, operation]) => {
          if (!['get', 'post', 'put', 'delete', 'patch', 'head'].includes(method)) return [];
          if (!isOperationSource(operation)) return [];

          const pathLevelParameters = Array.isArray(pathItem.parameters) ? pathItem.parameters : [];
          const mergedParameters = [...pathLevelParameters, ...(operation.parameters || [])];
          const parameters = mergedParameters.filter((parameter, index) => {
            const current = `${parameter.in}:${parameter.name}`;
            return mergedParameters.findIndex((item) => `${item.in}:${item.name}` === current) === index;
          });
          const requestBodyContent = operation.requestBody?.content || {};
          const preferredContentType = requestBodyContent['application/json']
            ? 'application/json'
            : Object.keys(requestBodyContent)[0] || '';

          return [{
            id: operation.operationId || `${method}:${path}`,
            method,
            path,
            summary: operation.summary || `${method.toUpperCase()} ${path}`,
            description: operation.description || '',
            tag: operation.tags?.[0] || 'General',
            parameters,
            requestBody: requestBodyContent[preferredContentType] || null,
            requestBodyContentType: preferredContentType,
          }];
        }),
    );
  }, [spec]);

  const tags = useMemo(() => ['All', ...new Set(operations.map((operation) => operation.tag))], [operations]);

  const filteredOperations = useMemo(() => {
    return operations.filter((operation) => {
      const matchesTag = selectedTag === 'All' || operation.tag === selectedTag;
      const haystack = `${operation.summary} ${operation.path} ${operation.tag}`.toLowerCase();
      const matchesFilter = !operationFilter || haystack.includes(operationFilter.toLowerCase());
      return matchesTag && matchesFilter;
    });
  }, [operationFilter, operations, selectedTag]);

  const groupedOperations = useMemo(() => {
    const groups = new Map<string, ExplorerOperation[]>();
    filteredOperations.forEach((operation) => {
      const existing = groups.get(operation.tag) || [];
      existing.push(operation);
      groups.set(operation.tag, existing);
    });
    return Array.from(groups.entries()).sort((left, right) => left[0].localeCompare(right[0]));
  }, [filteredOperations]);

  const selectedOperation = useMemo(
    () => operations.find((operation) => operation.id === selectedOperationId) || filteredOperations[0] || null,
    [filteredOperations, operations, selectedOperationId],
  );

  const requestPreview = useMemo<ExplorerRequestPreview | null>(() => {
    if (!selectedOperation) return null;
    const path = Object.entries(pathValues).reduce(
      (currentPath, [key, value]) => currentPath.replace(`{${key}}`, encodeURIComponent(value || `{${key}}`)),
      selectedOperation.path,
    );
    const url = new URL(path, buildApiBaseForDisplay());
    Object.entries(queryValues).forEach(([key, value]) => {
      if (value !== '') url.searchParams.set(key, value);
    });

    const headers: Record<string, string> = {};
    if (sessionToken) headers['X-Token'] = sessionToken;
    Object.entries(headerValues).forEach(([key, value]) => {
      if (value !== '' && !['x-token'].includes(key.toLowerCase())) headers[key] = value;
    });
    if (selectedOperation.requestBody && bodyValue.trim()) {
      headers['Content-Type'] = selectedOperation.requestBodyContentType || 'application/json';
    }

    return {
      method: selectedOperation.method,
      url: url.toString(),
      headers,
      body: bodyValue.trim() || '',
      contentType: selectedOperation.requestBodyContentType || 'application/json',
    };
  }, [bodyValue, headerValues, pathValues, queryValues, selectedOperation, sessionToken]);

  const responseCopyText = useMemo(
    () => getResponseText(response, responseTab, codeTab),
    [codeTab, response, responseTab],
  );

  useEffect(() => {
    const state = location.state as { replayRequest?: ReplayRequest } | null;
    if (state?.replayRequest) {
      setPendingReplay(state.replayRequest);
      navigate(location.pathname, { replace: true, state: null });
    }
  }, [location.pathname, location.state, navigate]);

  useEffect(() => {
    let cancelled = false;
    const controller = new AbortController();

    async function loadSpec() {
      setLoading(true);
      setError('');
      try {
        const headers: Record<string, string> = {};
        if (sessionToken) headers['X-Token'] = sessionToken;
        const result = await fetch(buildRequestUrl('/openapi.json'), { headers, signal: controller.signal });
        if (!result.ok) throw new Error(`Failed to load OpenAPI document (${result.status})`);
        const data = (await result.json()) as OpenApiSpec;
        if (!cancelled) setSpec(data);
      } catch (err) {
        if (!cancelled && !(err instanceof DOMException && err.name === 'AbortError')) {
          setError(err instanceof Error ? err.message : t('Failed to load OpenAPI document.'));
        }
      } finally {
        if (!cancelled) setLoading(false);
      }
    }

    void loadSpec();
    return () => {
      cancelled = true;
      controller.abort();
    };
  }, [sessionToken, t]);

  useEffect(() => {
    if (!selectedOperation && filteredOperations.length > 0) {
      setSelectedOperationId(operationId || filteredOperations[0].id);
    }
  }, [filteredOperations, operationId, selectedOperation]);

  useEffect(() => {
    if (!pendingReplay || !operations.length || pendingReplay.operationId) return;
    const match = findOperationForReplay(operations, pendingReplay);
    if (!match) {
      setError(t('No matching OpenAPI operation was found for the replay request.'));
      setPendingReplay(null);
      return;
    }

    setPendingReplay({
      ...pendingReplay,
      operationId: match.operationId,
      pathValues: { ...pendingReplay.pathValues, ...match.pathValues },
    });
    setSelectedOperationId(match.operationId);
  }, [operations, pendingReplay, t]);

  useEffect(() => {
    if (!selectedOperation) return;

    if (pendingReplay?.operationId === selectedOperation.id) {
      setPathValues(Object.fromEntries(Object.entries(pendingReplay.pathValues || {}).map(([key, value]) => [key, value || ''])));
      setQueryValues(Object.fromEntries(Object.entries(pendingReplay.queryValues || {}).map(([key, value]) => [key, value || ''])));
      const nextHeaders = Object.fromEntries(
        Object.entries(pendingReplay.headerValues || {})
          .filter(([key]) => !['x-token', 'authorization'].includes(key.toLowerCase()))
          .map(([key, value]) => [key, value || '']),
      );
      setHeaderValues(nextHeaders);
      setBodyValue(pendingReplay.bodyValue || '');
      skipDefaultInitializationForOperationId.current = selectedOperation.id;
      setPendingReplay(null);
    } else {
      if (skipDefaultInitializationForOperationId.current === selectedOperation.id) {
        skipDefaultInitializationForOperationId.current = null;
        return;
      }

      const nextPath: Record<string, string> = {};
      const nextQuery: Record<string, string> = {};
      const nextHeaders: Record<string, string> = {};
      selectedOperation.parameters.forEach((parameter) => {
        if (parameter.in === 'path') nextPath[parameter.name] = parameterInitialValue(parameter, spec);
        if (parameter.in === 'query') nextQuery[parameter.name] = parameterInitialValue(parameter, spec);
        if (parameter.in === 'header' && !['x-token', 'authorization', 'content-type'].includes(parameter.name.toLowerCase())) {
          nextHeaders[parameter.name] = parameterInitialValue(parameter, spec);
        }
      });
      setPathValues(nextPath);
      setQueryValues(nextQuery);
      setHeaderValues(nextHeaders);
      setBodyValue(selectedOperation.requestBody
        ? JSON.stringify(schemaExample(selectedOperation.requestBody.schema, spec), null, 2)
        : '');
    }

    setResponse(null);
    setResponseTab('preview');
    setCodeTab('curl');
  }, [pendingReplay, selectedOperation, spec]);

  useEffect(() => {
    if (!operationId) return;
    setSelectedOperationId(operationId);
  }, [operationId]);

  const handleSend = useCallback(async () => {
    if (!requestPreview) return;
    const controller = new AbortController();
    setAbortController(controller);
    setError('');
    const startedAt = performance.now();

    try {
      const headers = new Headers(requestPreview.headers);
      const result = await fetch(requestPreview.url, {
        method: requestPreview.method.toUpperCase(),
        headers,
        body: requestPreview.body || undefined,
        signal: controller.signal,
      });
      const durationMs = performance.now() - startedAt;
      const responseHeaders = Object.fromEntries(result.headers.entries());
      const contentType = result.headers.get('content-type') || '';
      let rawBody = '';
      let sizeBytes = 0;
      let preview: unknown = null;

      if (contentType.includes('application/json') || contentType.startsWith('text/')) {
        rawBody = await result.text();
        sizeBytes = new TextEncoder().encode(rawBody).length;
        if (contentType.includes('application/json')) {
          try {
            preview = JSON.parse(rawBody) as unknown;
          } catch {
            preview = rawBody;
          }
        } else {
          preview = rawBody;
        }
      } else {
        const blob = await result.blob();
        sizeBytes = blob.size;
        rawBody = `Binary response (${blob.type || 'application/octet-stream'}, ${blob.size} bytes)`;
        preview = rawBody;
      }

      const nextResponse: ExplorerResponse = {
        ok: result.ok,
        status: result.status,
        statusText: result.statusText,
        durationMs,
        headers: responseHeaders,
        contentType,
        body: rawBody,
        preview,
        sizeBytes,
        code: generateCodeSnippets(requestPreview),
      };

      setResponse(nextResponse);
      pushToast('success', t('Request completed with status {{status}}.', { status: result.status }));
    } catch (err) {
      if (!(err instanceof DOMException && err.name === 'AbortError')) {
        setError(err instanceof Error ? err.message : t('Request failed.'));
      }
    } finally {
      setAbortController(null);
    }
  }, [bodyValue, headerValues, pathValues, pushToast, queryValues, requestPreview, selectedOperation, t]);

  return (
    <div className="api-explorer-page">
      <PageHeader
        title={t('API Explorer')}
        subtitle={t('Browse the live OpenAPI document, execute authenticated requests, inspect responses, and replay captured traffic.')}
        actions={(
          <>
            <a className="btn btn-sm" href={buildRequestUrl('/openapi.json')} target="_blank" rel="noreferrer">
              {t('OpenAPI JSON')}
            </a>
            <a className="btn btn-sm" href={buildRequestUrl('/swagger')} target="_blank" rel="noreferrer">
              {t('Swagger')}
            </a>
            <button className="btn btn-primary btn-sm" onClick={() => void handleSend()} disabled={!selectedOperation || !!abortController || loading}>
              {abortController ? t('Running...') : t('Send Request')}
            </button>
            {abortController && (
              <button className="btn btn-sm" onClick={() => abortController.abort()}>
                {t('Abort')}
              </button>
            )}
          </>
        )}
      />

      {error && <div className="api-tool-error">{error}</div>}

      <div className="api-explorer-stack">
          <div className="card api-card">
            <div className="request-card-header">
              <div>
                <h3>{t('Operations')}</h3>
                <p className="text-dim">{t('Filter the live Armada API surface by category or text, then choose an operation to build a request.')}</p>
              </div>
            </div>
            <div className="request-card-body api-card-body">
              {loading ? (
                <div className="request-history-empty">{t('Loading OpenAPI document...')}</div>
              ) : (
                <div className="api-operation-picker">
                  <label className="api-operation-picker-field">
                    <span>{t('Category')}</span>
                    <select value={selectedTag} onChange={(event) => setSelectedTag(event.target.value)}>
                      {tags.map((tag) => (
                        <option key={tag} value={tag}>{tag}</option>
                      ))}
                    </select>
                  </label>
                  <label className="api-operation-picker-field api-operation-picker-filter">
                    <span>{t('Filter')}</span>
                    <input value={operationFilter} onChange={(event) => setOperationFilter(event.target.value)} placeholder={t('Filter by path or summary')} />
                  </label>
                  <label className="api-operation-picker-field api-operation-picker-operation">
                    <span>{t('Operation')}</span>
                    <select
                      value={selectedOperation?.id || ''}
                      onChange={(event) => {
                        setSelectedOperationId(event.target.value);
                        navigate(`/api-explorer/${encodeURIComponent(event.target.value)}`);
                      }}
                    >
                      {filteredOperations.length === 0 && <option value="">{t('No operations match the current filter')}</option>}
                      {filteredOperations.map((operation) => (
                        <option key={operation.id} value={operation.id}>
                          {operation.method.toUpperCase()} {operation.path}{operation.summary ? ` -- ${operation.summary}` : ''}
                        </option>
                      ))}
                    </select>
                  </label>
                </div>
              )}
            </div>
          </div>

          <div className="card api-card">
            <div className="request-card-header">
              <div>
                <h3>{selectedOperation?.summary || t('Request Builder')}</h3>
                {selectedOperation && <p className="text-dim">{selectedOperation.description || selectedOperation.path}</p>}
              </div>
              {selectedOperation && <span className="api-tag-badge">{selectedOperation.tag}</span>}
            </div>
            <div className="request-card-body api-card-body">
              {selectedOperation ? (
                <>
                  <div className="api-request-overview">
                    <span className={methodClass(selectedOperation.method)}>{selectedOperation.method.toUpperCase()}</span>
                    <code>{selectedOperation.path}</code>
                  </div>

                  <div className="api-parameter-grid">
                    <ParameterSection title={t('Path Parameters')} parameters={selectedOperation.parameters.filter((parameter) => parameter.in === 'path')} values={pathValues} onChange={setPathValues} />
                    <ParameterSection title={t('Query Parameters')} parameters={selectedOperation.parameters.filter((parameter) => parameter.in === 'query')} values={queryValues} onChange={setQueryValues} />
                    <ParameterSection title={t('Headers')} parameters={selectedOperation.parameters.filter((parameter) => parameter.in === 'header' && !['x-token', 'authorization', 'content-type'].includes(parameter.name.toLowerCase()))} values={headerValues} onChange={setHeaderValues} />
                  </div>

                  {selectedOperation.requestBody && (
                    <div className="api-body-section">
                      <div className="api-section-heading">
                        <h4>{t('Request Body')}</h4>
                        <span>{selectedOperation.requestBodyContentType || 'application/json'}</span>
                      </div>
                      <textarea value={bodyValue} onChange={(event) => setBodyValue(event.target.value)} spellCheck={false} rows={14} />
                    </div>
                  )}

                  {requestPreview && (
                    <div className="api-request-preview">
                      <div className="api-section-heading">
                        <h4>{t('Request Preview')}</h4>
                        <span>{requestPreview.method.toUpperCase()}</span>
                      </div>
                      <pre>{requestPreview.url}</pre>
                    </div>
                  )}
                </>
              ) : (
                <div className="request-history-empty">{t('No operations are available in the current OpenAPI document.')}</div>
              )}
            </div>
          </div>

          <div className="card api-card">
            <div className="request-card-header">
              <div>
                <h3>{t('Response')}</h3>
                {response && (
                  <p className="text-dim api-response-meta">
                    <span className={`request-status-pill ${response.ok ? 'success' : 'error'}`}>{response.status} {response.statusText}</span>
                    <span className="api-response-badge">{response.contentType || 'n/a'}</span>
                    <span>{response.durationMs.toFixed(2)} ms</span>
                    <span>{formatBytes(response.sizeBytes)}</span>
                  </p>
                )}
              </div>
              {response && (
                <CopyButton text={responseCopyText} title="Copy the current response view" />
              )}
            </div>
            <div className="request-card-body api-card-body">
              <div className="api-tab-row">
                {RESPONSE_TABS.map((tab) => (
                  <button
                    key={tab}
                    type="button"
                    className={`api-tab ${responseTab === tab ? 'active' : ''}`}
                    onClick={() => setResponseTab(tab)}
                    disabled={!response}
                  >
                    {t(tab.charAt(0).toUpperCase() + tab.slice(1))}
                  </button>
                ))}
              </div>
              {!response ? (
                <div className="request-history-empty">{t('Send a request to inspect the live response here.')}</div>
              ) : (
                <ResponsePanel response={response} responseTab={responseTab} codeTab={codeTab} onCodeTabChange={setCodeTab} />
              )}
            </div>
          </div>
      </div>
    </div>
  );
}
