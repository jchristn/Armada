import { defaultInferenceOptions, parseAllowedTools, streamingModeOf, validateInferenceOptions } from '@dashboard/lib/inferenceOptions';
import type { InferenceStreamingMode, ModelEndpoint, ModelEndpointKind, ModelProvider } from '@dashboard/types/models';
import { MODEL_ENDPOINT_KINDS, MODEL_PROVIDERS, unsupportedEndpointReason } from '@dashboard/lib/configuration';
import { resolveCreateScope, type ScopeViewer } from '@dashboard/lib/scoping';
import { bool, str, type FormField, type FormValues } from '../../components/resource/FormSheet';
import type { Translate } from '../../i18n/LocaleContext';
import { scopeField, scopeValue } from './common';

/** How the endpoint's secret is sent (the server's ModelEndpoint.authType). */
export type EndpointAuthType = 'ProviderDefault' | 'None' | 'Bearer' | 'Basic' | 'Header' | 'QueryParameter';

export const ENDPOINT_AUTH_TYPES: EndpointAuthType[] = ['ProviderDefault', 'None', 'Bearer', 'Basic', 'Header', 'QueryParameter'];

/** An extra header sent on every request. Reads return value null and hasValue; a null value on save keeps the stored one. */
export interface EndpointHeader {
  name: string;
  value?: string | null;
  hasValue?: boolean;
}

/** The authentication fields of a model endpoint (optional so older servers without them still type-check). */
export interface EndpointAuthFields {
  authType?: EndpointAuthType | null;
  authUsername?: string | null;
  authHeaderName?: string | null;
  authQueryParameter?: string | null;
  headers?: EndpointHeader[] | null;
}

/** A model endpoint as read, including the authentication fields. */
export type AuthModelEndpoint = ModelEndpoint & EndpointAuthFields;

/** The create / update body. */
export type EndpointPayload = Partial<ModelEndpoint> & EndpointAuthFields & { apiKey?: string | null };

const HEADER_COUNT = 'headerCount';
const headerNameKey = (i: number) => `headerName${i}`;
const headerValueKey = (i: number) => `headerValue${i}`;

/** Providers whose credential has a fixed shape (service-account JSON, AWS secret): only ProviderDefault applies. */
export function providerAllowsAuthChoice(provider: string): boolean {
  return provider !== 'VertexAI' && provider !== 'Bedrock' && provider !== 'OllamaCloud' && provider !== 'XAI';
}

/** The effective auth type for the current values (ProviderDefault when the provider has no choice). */
export function endpointAuthType(v: FormValues): EndpointAuthType {
  if (!providerAllowsAuthChoice(str(v, 'provider'))) return 'ProviderDefault';
  const raw = str(v, 'authType');
  return (ENDPOINT_AUTH_TYPES as string[]).includes(raw) ? (raw as EndpointAuthType) : 'ProviderDefault';
}

/** The auth type's display label (also used in the endpoint list and detail). */
export function authTypeLabel(t: Translate, type: EndpointAuthType | null | undefined): string {
  switch (type) {
    case 'None': return t('No authentication');
    case 'Bearer': return t('Bearer token');
    case 'Basic': return t('Basic');
    case 'Header': return t('Custom header');
    case 'QueryParameter': return t('Query parameter');
    default: return t('Provider default');
  }
}

/** The secret field's label for the auth type ("Bearer token", "Password", "Header value", or the provider's key). */
export function endpointSecretLabel(t: Translate, type: EndpointAuthType | null | undefined, provider: string): string {
  switch (type) {
    case 'Bearer': return t('Bearer token');
    case 'Basic': return t('Password');
    case 'Header': return t('Header value');
    default: return provider === 'VertexAI' ? t('Service Account JSON') : provider === 'Bedrock' ? t('AWS Secret Access Key') : t('API Key');
  }
}

/** The provider's usual way of sending the secret (ProviderDefault). */
function providerDefaultSentAs(t: Translate, provider: string): string {
  switch (provider) {
    case 'Anthropic': return 'x-api-key: <key>';
    case 'Gemini': return 'x-goog-api-key: <key>';
    case 'AzureOpenAI': return 'api-key: <key>';
    case 'VertexAI': return t('an OAuth token from the service-account JSON');
    case 'Bedrock': return t('an AWS signature made with the secret access key');
    default: return 'Authorization: Bearer <key>';
  }
}

/** A short "Sent as: ..." description of how the secret reaches the provider. */
export function endpointSentAs(t: Translate, v: FormValues): string {
  const type = endpointAuthType(v);
  if (type === 'None') return t('No credential is sent.');
  let how: string;
  switch (type) {
    case 'Bearer': how = 'Authorization: Bearer <token>'; break;
    case 'Basic': how = 'Authorization: Basic base64(<username>:<password>)'; break;
    case 'Header': how = `${str(v, 'authHeaderName').trim() || '<header>'}: <value>`; break;
    case 'QueryParameter': how = `?${str(v, 'authQueryParameter').trim() || '<parameter>'}=<key>`; break;
    default: how = providerDefaultSentAs(t, str(v, 'provider'));
  }
  return t('Sent as: {{how}}', { how });
}

function inferenceValues(endpoint: ModelEndpoint | null): FormValues {
  const options = { ...defaultInferenceOptions, ...endpoint?.inferenceOptions };
  return { toolsEnabled: options.allowedTools?.length === 0 ? false : options.toolsEnabled, readOnlyTools: options.readOnlyTools, mcpToolsEnabled: options.mcpToolsEnabled, requireHarbor: options.requireHarbor,
    allowedTools: options.allowedTools?.join('\n') ?? '', maxIterations: String(options.maxIterations), maxTokens: options.maxTokens == null ? '' : String(options.maxTokens),
    temperature: options.temperature == null ? '' : String(options.temperature), topP: options.topP == null ? '' : String(options.topP),
    streamingMode: streamingModeOf(options),
    contextLength: options.contextLength == null ? '' : String(options.contextLength),
    maxConcurrentMissions: options.maxConcurrentMissions == null ? '' : String(options.maxConcurrentMissions),
    firstResponseTimeoutMs: options.firstResponseTimeoutMs == null ? '' : String(options.firstResponseTimeoutMs),
    toolOutputWindowPercent: options.toolOutputWindowPercent == null ? '' : String(options.toolOutputWindowPercent),
    repeatedToolCallLimit: options.repeatedToolCallLimit == null ? '' : String(options.repeatedToolCallLimit) };
}

function inferencePayload(v: FormValues) {
  return {
      toolsEnabled: bool(v, 'toolsEnabled'), readOnlyTools: bool(v, 'readOnlyTools'), mcpToolsEnabled: bool(v, 'mcpToolsEnabled'), requireHarbor: bool(v, 'requireHarbor'),
      allowedTools: parseAllowedTools(str(v, 'allowedTools')), maxIterations: Number(str(v, 'maxIterations')),
      maxTokens: str(v, 'maxTokens').trim() ? Number(str(v, 'maxTokens')) : null,
      temperature: str(v, 'temperature').trim() ? Number(str(v, 'temperature')) : null,
      topP: str(v, 'topP').trim() ? Number(str(v, 'topP')) : null,
      // Carried on every save, so editing an endpoint on a phone never changes streaming or drops the window.
      streamingMode: (str(v, 'streamingMode') || 'Automatic') as InferenceStreamingMode,
      streaming: str(v, 'streamingMode') === 'On',
      contextLength: str(v, 'contextLength').trim() ? Number(str(v, 'contextLength')) : null,
      maxConcurrentMissions: str(v, 'maxConcurrentMissions').trim() ? Number(str(v, 'maxConcurrentMissions')) : null,
      firstResponseTimeoutMs: str(v, 'firstResponseTimeoutMs').trim() ? Number(str(v, 'firstResponseTimeoutMs')) : null,
      toolOutputWindowPercent: str(v, 'toolOutputWindowPercent').trim() ? Number(str(v, 'toolOutputWindowPercent')) : null,
      repeatedToolCallLimit: str(v, 'repeatedToolCallLimit').trim() ? Number(str(v, 'repeatedToolCallLimit')) : null,
    };
}

export function endpointValues(viewer: ScopeViewer, endpoint: ModelEndpoint | null): FormValues {
  const e = endpoint as AuthModelEndpoint | null;
  if (!e) {
    return {
      ...inferenceValues(null),
      name: 'New Endpoint', kind: 'Inference', provider: 'OpenAI', baseUrl: '', model: '', apiKey: '', region: '', project: '', apiVersion: '', accessKeyId: '',
      authType: 'ProviderDefault', authUsername: '', authHeaderName: '', authQueryParameter: '', [HEADER_COUNT]: '0',
      dimensionality: '0', timeoutMs: '120000', enabled: true, scope: resolveCreateScope(viewer),
    };
  }
  const headers = e.headers ?? [];
  const values: FormValues = {
    ...inferenceValues(e),
    name: e.name, kind: e.kind, provider: e.provider, baseUrl: e.baseUrl, model: e.model || '', apiKey: '', region: e.region || '', project: e.project || '',
    apiVersion: e.apiVersion || '', accessKeyId: e.accessKeyId || '',
    authType: e.authType || 'ProviderDefault', authUsername: e.authUsername || '', authHeaderName: e.authHeaderName || '', authQueryParameter: e.authQueryParameter || '',
    [HEADER_COUNT]: String(headers.length),
    dimensionality: String(e.dimensionality ?? 0), timeoutMs: String(e.timeoutMs ?? 120000), enabled: e.enabled, scope: e.scope,
  };
  // Stored header values are write-only: the row starts blank and a blank value keeps the stored one.
  headers.forEach((h, i) => {
    values[headerNameKey(i)] = h.name;
    values[headerValueKey(i)] = '';
  });
  return values;
}

function headerCount(v: FormValues): number {
  const n = Number.parseInt(str(v, HEADER_COUNT), 10);
  return Number.isNaN(n) || n < 0 ? 0 : n;
}

/** The header rows as typed (blank rows included). */
export function headerRows(v: FormValues): { name: string; value: string }[] {
  const rows: { name: string; value: string }[] = [];
  for (let i = 0; i < headerCount(v); i++) rows.push({ name: str(v, headerNameKey(i)).trim(), value: str(v, headerValueKey(i)) });
  return rows;
}

/** Adds a blank header row. */
export function addHeaderRow(v: FormValues): FormValues {
  const n = headerCount(v);
  return { ...v, [HEADER_COUNT]: String(n + 1), [headerNameKey(n)]: '', [headerValueKey(n)]: '' };
}

/** Removes header row `index`, shifting the later rows up. */
export function removeHeaderRow(v: FormValues, index: number): FormValues {
  const n = headerCount(v);
  if (index < 0 || index >= n) return v;
  const next: FormValues = { ...v, [HEADER_COUNT]: String(n - 1) };
  for (let i = index; i < n - 1; i++) {
    next[headerNameKey(i)] = str(v, headerNameKey(i + 1));
    next[headerValueKey(i)] = str(v, headerValueKey(i + 1));
  }
  delete next[headerNameKey(n - 1)];
  delete next[headerValueKey(n - 1)];
  return next;
}

function storedHeaderHasValue(existing: AuthModelEndpoint | null, name: string): boolean {
  const lower = name.toLowerCase();
  return !!existing?.headers?.some((h) => h.name.toLowerCase() === lower && h.hasValue);
}

/** The provider/kind guard message (translated), or null when the combination is valid. */
export function endpointReason(t: Translate, v: FormValues): string | null {
  const reason = unsupportedEndpointReason(str(v, 'provider') as ModelProvider, str(v, 'kind') as ModelEndpointKind);
  return reason ? t(reason) : null;
}

/** Everything that blocks saving beyond required fields: the provider guard, auth fields, and header rows. */
export function endpointValidate(t: Translate, v: FormValues, existing: ModelEndpoint | null): string | null {
  const reason = endpointReason(t, v);
  if (reason) return reason;
  const inferenceError = validateInferenceOptions(inferencePayload(v));
  if (inferenceError) return t(inferenceError);
  const type = endpointAuthType(v);
  if (['OllamaCloud', 'XAI'].includes(str(v, 'provider')) && !str(v, 'apiKey').trim() && !existing?.hasApiKey) return t('API key is required.');
  if (type === 'Basic' && !str(v, 'authUsername').trim()) return t('Basic authentication needs a username.');
  if (type === 'Header' && !str(v, 'authHeaderName').trim()) return t('Header authentication needs a header name.');
  if (type === 'QueryParameter' && !str(v, 'authQueryParameter').trim()) return t('Query parameter authentication needs a parameter name.');
  const seen = new Set<string>();
  for (const row of headerRows(v)) {
    if (!row.name) {
      if (row.value) return t('Each extra header needs a name.');
      continue;
    }
    if (/[\s:]/.test(row.name)) return t('Header name "{{name}}" cannot contain spaces or colons.', { name: row.name });
    const lower = row.name.toLowerCase();
    if (seen.has(lower)) return t('Header "{{name}}" is listed more than once.', { name: row.name });
    seen.add(lower);
    if (!row.value && !storedHeaderHasValue(existing as AuthModelEndpoint | null, row.name)) return t('Header "{{name}}" needs a value.', { name: row.name });
  }
  return null;
}

/**
 * The payload the dashboard saves. The credential is a secret: it is never shown, and it is sent only when typed, so
 * an unchanged edit keeps the stored key (the dashboard's apiKeyTouched). Header values work the same way: a blank
 * value is sent as null, which keeps the stored value for that name.
 */
export function endpointPayload(viewer: ScopeViewer, v: FormValues, existing: ModelEndpoint | null): EndpointPayload {
  const type = endpointAuthType(v);
  const payload: EndpointPayload = {
    inferenceOptions: inferencePayload(v),
    name: str(v, 'name'),
    kind: str(v, 'kind') as ModelEndpointKind,
    provider: str(v, 'provider') as ModelProvider,
    baseUrl: str(v, 'provider') === 'OllamaCloud' ? 'https://ollama.com:443' : str(v, 'baseUrl'),
    model: str(v, 'model').trim() || null,
    region: str(v, 'region').trim() || null,
    project: str(v, 'project').trim() || null,
    apiVersion: str(v, 'apiVersion').trim() || null,
    accessKeyId: str(v, 'accessKeyId').trim() || null,
    authType: type,
    authUsername: type === 'Basic' ? str(v, 'authUsername').trim() || null : null,
    authHeaderName: type === 'Header' ? str(v, 'authHeaderName').trim() || null : null,
    authQueryParameter: type === 'QueryParameter' ? str(v, 'authQueryParameter').trim() || null : null,
    dimensionality: Number.parseInt(str(v, 'dimensionality'), 10) || 0,
    timeoutMs: Number.parseInt(str(v, 'timeoutMs'), 10) || 120000,
    enabled: bool(v, 'enabled'),
    scope: scopeValue(viewer, v, existing?.scope ?? null),
  };
  if (type !== 'None' && str(v, 'apiKey')) payload.apiKey = str(v, 'apiKey');
  const rows = headerRows(v).filter((r) => r.name);
  // Sending headers replaces the list; omitting them keeps it, so an edit of a record without headers sends none.
  const stored = (existing as AuthModelEndpoint | null)?.headers;
  if (!existing || rows.length > 0 || (stored != null && stored.length > 0)) {
    payload.headers = rows.map((r) => ({ name: r.name, value: r.value || null }));
  }
  return payload;
}

export function endpointFields(t: Translate, viewer: ScopeViewer, v: FormValues, endpoint: ModelEndpoint | null): FormField[] {
  const existing = endpoint as AuthModelEndpoint | null;
  const provider = str(v, 'provider');
  const isAzure = provider === 'AzureOpenAI';
  const isVertex = provider === 'VertexAI';
  const isBedrock = provider === 'Bedrock';
  const authChoice = providerAllowsAuthChoice(provider);
  const type = endpointAuthType(v);
  const reason = endpointReason(t, v);
  const keepNote = existing?.hasApiKey ? (isVertex ? t('(unchanged - leave blank to keep stored credential)') : t('(unchanged - leave blank to keep stored key)')) : null;
  const secretLabel = endpointSecretLabel(t, type, provider);
  const sentAs = endpointSentAs(t, v);
  const headerFields: FormField[] = [];
  headerRows(v).forEach((row, i) => {
    const n = i + 1;
    const keep = row.name && storedHeaderHasValue(existing, row.name) ? t('(unchanged - leave blank to keep stored value)') : null;
    headerFields.push(
      { kind: 'text', key: headerNameKey(i), label: t('Header {{n}} name', { n }), placeholder: 'X-Organization' },
      { kind: 'secret', key: headerValueKey(i), label: t('Header {{n}} value', { n }), placeholder: keep ?? undefined, hint: keep },
      {
        kind: 'action', key: `headerRemove${i}`, label: t('Remove header {{n}}', { n }), variant: 'ghost', icon: 'trash-outline',
        accessibilityHint: row.name ? t('Removes the {{name}} header.', { name: row.name }) : undefined,
        apply: (values) => removeHeaderRow(values, i),
      },
    );
  });
  return [
    { kind: 'text', key: 'name', label: t('Name'), required: true },
    { kind: 'select', key: 'kind', label: t('Kind'), options: MODEL_ENDPOINT_KINDS.map((k) => ({ value: k, label: k })) },
    { kind: 'select', key: 'provider', label: t('Provider'), options: MODEL_PROVIDERS.map((p) => ({ value: p, label: p })), hint: reason },
    ...(isVertex || isBedrock ? [{ kind: 'text' as const, key: 'region', label: t('Region'), required: true, placeholder: isBedrock ? 'us-east-1' : 'us-central1' }] : []),
    ...(isVertex ? [{ kind: 'text' as const, key: 'project', label: t('Project') }] : []),
    ...(isBedrock ? [{ kind: 'text' as const, key: 'accessKeyId', label: t('Access Key ID') }] : []),
    ...(provider === 'OllamaCloud' ? [{ kind: 'note' as const, key: 'cloudUrl', label: 'https://ollama.com:443' }] : [{
      kind: 'text' as const, key: 'baseUrl', required: !isVertex && !isBedrock && provider !== 'OllamaCloud' && provider !== 'XAI',
      label: isVertex || isBedrock ? t('Base URL (optional override)') : isAzure ? t('Base URL (resource endpoint)') : t('Base URL'),
      placeholder: provider === 'OllamaCloud' ? 'https://ollama.com:443 (fixed)' : provider === 'XAI' ? 'https://api.x.ai (default)' : isAzure ? 'https://my-resource.openai.azure.com' : 'https://api.openai.com',
    }]),
    {
      kind: 'text', key: 'model', required: isAzure,
      label: isAzure ? t('Deployment name') : isBedrock ? t('Bedrock model id') : t('Model'),
      placeholder: isAzure ? 'gpt-4o' : isBedrock ? 'anthropic.claude-3-5-sonnet-20240620-v1:0' : str(v, 'kind') === 'Embedding' ? 'text-embedding-3-small' : 'gpt-4o-mini',
    },
    ...(isAzure ? [{ kind: 'text' as const, key: 'apiVersion', label: t('API Version'), placeholder: '2024-10-21 (default)' }] : []),
    ...(authChoice
      ? [{ kind: 'select' as const, key: 'authType', label: t('Authentication'), options: ENDPOINT_AUTH_TYPES.map((a) => ({ value: a, label: authTypeLabel(t, a) })), hint: sentAs }]
      : [{ kind: 'note' as const, key: 'authNote', label: sentAs }]),
    ...(type === 'Basic' ? [{ kind: 'text' as const, key: 'authUsername', label: t('Username'), required: true }] : []),
    ...(type === 'Header' ? [{ kind: 'text' as const, key: 'authHeaderName', label: t('Header name'), required: true, placeholder: 'X-API-Key' }] : []),
    ...(type === 'QueryParameter' ? [{ kind: 'text' as const, key: 'authQueryParameter', label: t('Query parameter name'), required: true, placeholder: 'key' }] : []),
    ...(type === 'None' ? [] : [{
      kind: isVertex ? 'multiline' as const : 'secret' as const, key: 'apiKey',
      label: secretLabel,
      placeholder: keepNote ?? (isVertex ? t('Paste the service-account JSON') : t('Optional')),
      hint: keepNote,
    }]),
    { kind: 'note', key: 'headersNote', label: t('Extra headers are sent on every request.') },
    ...headerFields,
    { kind: 'action', key: 'headerAdd', label: t('Add header'), icon: 'add', apply: addHeaderRow },
    { kind: 'integer', key: 'dimensionality', label: t('Dimensionality') },
    { kind: 'integer', key: 'timeoutMs', label: t('Timeout (ms)') },
    ...(str(v, 'kind') === 'Inference' ? [
      { kind: 'switch' as const, key: 'toolsEnabled', label: t('Enable tool calls') },
      { kind: 'switch' as const, key: 'readOnlyTools', label: t('Read-only built-in tools'), hint: t('Excludes shell, file changes and MCP tools.') },
      { kind: 'switch' as const, key: 'mcpToolsEnabled', label: t('Enable scoped MCP tools') },
      { kind: 'switch' as const, key: 'requireHarbor', label: t('Require Harbor execution') },
      { kind: 'multiline' as const, key: 'allowedTools', label: t('Allowed tool names (one per line; blank allows all)') },
      { kind: 'integer' as const, key: 'maxIterations', label: t('Maximum model turns'), required: true },
      { kind: 'integer' as const, key: 'maxTokens', label: t('Maximum output tokens') },
      { kind: 'text' as const, key: 'temperature', label: t('Temperature (0–2, optional)') },
      { kind: 'text' as const, key: 'topP', label: t('Top P (0–1, optional)') },
      { kind: 'select' as const, key: 'streamingMode', label: t('Stream responses'), options: (['Automatic', 'On', 'Off'] as const).map((m) => ({ value: m, label: t(m) })), hint: t('Automatic streams on OpenAI-compatible, Ollama and Ollama Cloud endpoints, and on a Harbor only when it supports streaming. Streaming makes the request timeout limit only the silence between chunks.') },
      { kind: 'integer' as const, key: 'firstResponseTimeoutMs', label: t('First-response timeout (ms)') },
      { kind: 'integer' as const, key: 'contextLength', label: t('Context window (Ollama num_ctx)') },
      { kind: 'integer' as const, key: 'maxConcurrentMissions', label: t('Max concurrent missions (per base URL)') },
      { kind: 'integer' as const, key: 'toolOutputWindowPercent', label: t('Tool output window (% of context)'), hint: t('Share of a known context window one file read or command output may fill; blank uses 12.') },
      { kind: 'integer' as const, key: 'repeatedToolCallLimit', label: t('Repeated tool call limit'), hint: t('Identical tool calls in a row (2-20) before a corrective note; twice as many stops the run. Blank uses 3.') },
    ] : []),
    scopeField(t, viewer),
    { kind: 'switch', key: 'enabled', label: t('Enabled') },
  ];
}
