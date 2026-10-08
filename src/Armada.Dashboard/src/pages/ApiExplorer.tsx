import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useLocation, useNavigate, useParams } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';
import { useLocale } from '../context/LocaleContext';
import { useNotifications } from '../context/NotificationContext';
import CopyButton from '../components/shared/CopyButton';
import PageHeader from '../components/shared/PageHeader';
import { formatBytes, parseJsonString, methodClass } from '../lib/format';
import { getClientBaseUrl } from '../api/client';
import {
  buildExplorerOperations,
  CODE_TABS,
  explorerTags,
  filterExplorerOperations,
  findOperationForReplay,
  generateCodeSnippets,
  getResponseText,
  parameterInitialValue,
  RESPONSE_TABS,
  schemaExample,
  type CodeTab,
  type ExplorerOperation,
  type ExplorerRequestPreview,
  type ExplorerResponse,
  type OpenApiParameter,
  type OpenApiSpec,
  type ReplayRequest,
  type ResponseTab,
} from '../lib/apiExplorer';

function buildApiBaseForDisplay() {
  return getClientBaseUrl() || window.location.origin;
}

function buildRequestUrl(path: string) {
  const baseUrl = getClientBaseUrl();
  return baseUrl ? `${baseUrl}${path}` : path;
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

  const operations = useMemo<ExplorerOperation[]>(() => buildExplorerOperations(spec), [spec]);

  const tags = useMemo(() => explorerTags(operations), [operations]);

  const filteredOperations = useMemo(
    () => filterExplorerOperations(operations, selectedTag, operationFilter),
    [operationFilter, operations, selectedTag],
  );

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
                      <textarea aria-label={t('Request Body')} value={bodyValue} onChange={(event) => setBodyValue(event.target.value)} spellCheck={false} rows={14} />
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
