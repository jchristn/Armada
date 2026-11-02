import { duplicateModelEndpoint } from '../api/client';
import { duplicateDisplayName } from '../lib/duplicates';
import { defaultInferenceOptions, parseAllowedTools, validateInferenceOptions } from '../lib/inferenceOptions';
import type { InferenceOptions } from '../types/models';
import { useEffect, useMemo, useState } from 'react';
import {
  enumerateModelEndpoints,
  createModelEndpoint,
  createCaptain,
  updateModelEndpoint,
  deleteModelEndpoint,
  validateModelEndpoint,
  healthCheckModelEndpoints,
} from '../api/client';
import type { ModelEndpoint, ModelEndpointAuthType, ModelEndpointKind, ModelProvider, ModelEndpointProbeResult, ScopeEnum } from '../types/models';
import { canEdit as canEditScoped, resolveCreateScope, type ScopeViewer } from '../lib/scoping';
import ScopeBadge from '../components/shared/ScopeBadge';
import ScopeSelect from '../components/shared/ScopeSelect';
import { useAuth } from '../context/AuthContext';
import { useLocale } from '../context/LocaleContext';
import { useNotifications } from '../context/NotificationContext';
import ActionMenu from '../components/shared/ActionMenu';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import ErrorModal from '../components/shared/ErrorModal';
import DialogShell from '../components/shared/DialogShell';
import LoadingIndicator from '../components/shared/LoadingIndicator';
import JsonViewer from '../components/shared/JsonViewer';
import DataTable, { type DataTableColumn } from '../components/shared/DataTable';
import { useAutoRefresh } from '../lib/useAutoRefresh';
import PageHeader from '../components/shared/PageHeader';
import ModelProbeEvaluate from '../components/ModelProbeEvaluate';
import StatusBadge from '../components/shared/StatusBadge';
import HealthHistogram from '../components/shared/HealthHistogram';
import CopyButton from '../components/shared/CopyButton';
import { formatHealthSpan, MODEL_ENDPOINT_KINDS, MODEL_PROVIDERS, unsupportedEndpointReason } from '../lib/configuration';
import {
  allowedAuthTypes,
  authFormFromEndpoint,
  authSecretLabel,
  authSecretPlaceholder,
  authSentAs,
  authSummary,
  authTypeLabel,
  buildEndpointAuthPayload,
  effectiveAuthType,
  headerKeepsStoredValue,
  newHeaderRow,
  providerSignsRequests,
  validateEndpointAuth,
  type EndpointAuthForm,
  type EndpointHeaderRow,
  type LocalizedText,
} from '../lib/endpointAuth';
import { useDeleteProgress } from '../lib/useDeleteProgress';
import TruncationNote from '../components/shared/TruncationNote';

const PROVIDERS = MODEL_PROVIDERS;
const KINDS = MODEL_ENDPOINT_KINDS;
const formatSpan = (firstUtc: string | null) => formatHealthSpan(firstUtc);
const unsupportedReason = unsupportedEndpointReason;

interface EndpointForm extends EndpointAuthForm {
  inferenceOptions: InferenceOptions;
  allowedToolsText: string;
  name: string;
  kind: ModelEndpointKind;
  provider: ModelProvider;
  baseUrl: string;
  model: string;
  apiKey: string;
  apiKeyTouched: boolean;
  region: string;
  project: string;
  apiVersion: string;
  accessKeyId: string;
  dimensionality: string;
  timeoutMs: string;
  enabled: boolean;
  scope: ScopeEnum;
}

const EMPTY_FORM: EndpointForm = {
  inferenceOptions: { ...defaultInferenceOptions },
  allowedToolsText: '',
  name: 'New Endpoint',
  kind: 'Inference',
  provider: 'OpenAI',
  baseUrl: '',
  model: '',
  apiKey: '',
  apiKeyTouched: false,
  region: '',
  project: '',
  apiVersion: '',
  accessKeyId: '',
  dimensionality: '0',
  timeoutMs: '120000',
  enabled: true,
  scope: 'TenantWide',
  ...authFormFromEndpoint(null),
};



const EP_NAME_TIP = 'Name for this endpoint, shown when choosing one for a captain, for example openai-prod or local-ollama. Required.';
const EP_KIND_TIP = 'Inference answers prompts and can drive API Endpoint captains. Embedding turns text into vectors. It also decides how Validate tests the endpoint.';
const EP_PROVIDER_TIP = 'The API this endpoint speaks. It sets the request format and default authentication. Use OpenAICompatible for servers like vLLM, LM Studio or LiteLLM.';
const EP_REGION_TIP = 'Cloud region the model runs in, for example us-east-1 on Bedrock or us-central1 on Vertex AI. Required.';
const EP_PROJECT_TIP = 'Your Google Cloud project ID, for example my-gcp-project. Required for Vertex AI.';
const EP_ACCESS_KEY_TIP = 'AWS access key ID, for example AKIA... Put the matching secret access key in the secret field below. Required for Bedrock.';
const EP_API_VERSION_TIP = 'Azure OpenAI API version, for example 2024-10-21. Leave blank to use the current default version.';
const EP_AUTH_TYPE_TIP = 'How the secret is attached to each request: as a bearer token, basic auth, a named header or a query parameter. Provider default sends it the way this provider expects.';
const EP_USERNAME_TIP = 'User name for basic authentication. The password goes in the secret field below. Required.';
const EP_HEADER_NAME_TIP = 'Name of the HTTP header that carries the secret, for example X-API-Key. Required.';
const EP_QUERY_TIP = 'Name of the URL query parameter that carries the secret, for example key. The secret then appears in request URLs, so prefer a header when the server allows it.';
const EP_DIM_TIP = 'Embedding endpoints only: an optional hint of the vector length the model returns, for example 1536. 0 means no hint.';
const EP_TIMEOUT_TIP = 'How long to wait for one request before giving up, in milliseconds, from 1000 to 600000. The default is 120000 (2 minutes).';
const EP_ENABLED_TIP = 'On: the endpoint is available for use and is health-checked. Off: it is kept, but marked not for use and skipped by health checks.';

export default function Endpoints() {
  const { isAdmin, isTenantAdmin, user } = useAuth();
  const viewer: ScopeViewer = { isAdmin, isTenantAdmin, tenantId: user?.user?.tenantId, userId: user?.user?.id };
  const { t, formatRelativeTime, formatDateTime } = useLocale();
  const { pushToast } = useNotifications();
  const deletion = useDeleteProgress();
  const [endpoints, setEndpoints] = useState<ModelEndpoint[]>([]);
  const [endpointTotal, setEndpointTotal] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [search, setSearch] = useState('');
  const [kindFilter, setKindFilter] = useState('all');
  const [jsonData, setJsonData] = useState<{ open: boolean; title: string; data: unknown }>({ open: false, title: '', data: null });
  const [confirm, setConfirm] = useState<{ open: boolean; title: string; message: string; onConfirm: () => void }>({
    open: false, title: '', message: '', onConfirm: () => {},
  });

  const [showForm, setShowForm] = useState(false);
  const [editing, setEditing] = useState<ModelEndpoint | null>(null);
  const [duplicateSource, setDuplicateSource] = useState<ModelEndpoint | null>(null);
  const [createLinkedCaptain, setCreateLinkedCaptain] = useState(false);
  const [captainName, setCaptainName] = useState('');
  const [saving, setSaving] = useState(false);
  const [form, setForm] = useState<EndpointForm>(EMPTY_FORM);

  // Validate + health modals
  const [validating, setValidating] = useState<string | null>(null);
  const [probe, setProbe] = useState<{ open: boolean; endpoint: ModelEndpoint | null; result: ModelEndpointProbeResult | null; error: string | null }>({ open: false, endpoint: null, result: null, error: null });
  const [health, setHealth] = useState<{ open: boolean; endpoint: ModelEndpoint | null }>({ open: false, endpoint: null });
  const [sweeping, setSweeping] = useState(false);

  const canManage = isAdmin || isTenantAdmin;
  const formReason = unsupportedReason(form.provider, form.kind);
  const isAzure = form.provider === 'AzureOpenAI';
  const isVertex = form.provider === 'VertexAI';
  const isBedrock = form.provider === 'Bedrock';
  // Regional and hosted providers can supply their own default URL.
  const isCloud = form.provider === 'OllamaCloud';
  const baseUrlRequired = !isVertex && !isBedrock && !isCloud && form.provider !== 'XAI';
  const signsRequests = providerSignsRequests(form.provider);
  const formAuthType = effectiveAuthType(form.provider, form.authType);
  const secretLabelKey = authSecretLabel(form.provider, formAuthType);
  const hasStoredSecret = !!(editing ?? duplicateSource)?.hasApiKey;
  const sentAs = authSentAs(form.provider, formAuthType, form);
  const lt = (text: LocalizedText) => t(text.key, text.params);
  const baseUrlTip = isCloud ? 'Fixed URL: https://ollama.com:443. Requires an Ollama API key and a cloud model.' : form.provider === 'XAI' ? 'Defaults to https://api.x.ai. Uses the native xAI Responses API; requires an API key.' : isVertex || isBedrock
    ? 'Optional. Leave blank to use the provider\'s regional address; set it only to send requests through a proxy or private endpoint.'
    : isAzure
      ? 'Your Azure OpenAI resource address, for example https://my-resource.openai.azure.com. Required.'
      : 'Server address without the API path, for example https://api.openai.com or http://localhost:11434 for Ollama. Required.';
  const modelTip = isAzure
    ? 'The Azure deployment name to call, for example gpt-4o. Required.'
    : isBedrock
      ? 'The Bedrock model id, for example anthropic.claude-3-5-sonnet-20240620-v1:0.'
      : 'Model name to request, for example gpt-4o-mini or text-embedding-3-small. Captains using this endpoint can override it.';
  const secretTip = isVertex
    ? 'The service-account JSON key for your Google Cloud project. Stored on the Admiral and never shown again. Leave blank when editing to keep the stored one.'
    : 'The secret sent with each request, such as an API key, token or password. Stored on the Admiral and never shown again. Leave blank when editing to keep the stored one.';

  function updateHeader(id: string, patch: Partial<EndpointHeaderRow>) {
    setForm((current) => ({ ...current, headers: current.headers.map((row) => (row.id === id ? { ...row, ...patch } : row)) }));
  }

  function removeHeader(id: string) {
    setForm((current) => ({ ...current, headers: current.headers.filter((row) => row.id !== id) }));
  }

  function addHeader() {
    setForm((current) => ({ ...current, headers: [...current.headers, newHeaderRow()] }));
  }

  function openCreate() {
    setCreateLinkedCaptain(false);
    setCaptainName('');
    setEditing(null);
    setDuplicateSource(null);
    setForm({ ...EMPTY_FORM, scope: resolveCreateScope(viewer) });
    setShowForm(true);
  }

  function openEdit(endpoint: ModelEndpoint) {
    setCreateLinkedCaptain(false);
    setCaptainName('');
    setDuplicateSource(null);
    setEditing(endpoint);
    setForm({
      inferenceOptions: { ...defaultInferenceOptions, ...endpoint.inferenceOptions },
      allowedToolsText: endpoint.inferenceOptions?.allowedTools?.join('\n') ?? '',
      name: endpoint.name,
      kind: endpoint.kind,
      provider: endpoint.provider,
      baseUrl: endpoint.baseUrl,
      model: endpoint.model || '',
      apiKey: '',
      apiKeyTouched: false,
      region: endpoint.region || '',
      project: endpoint.project || '',
      apiVersion: endpoint.apiVersion || '',
      accessKeyId: endpoint.accessKeyId || '',
      dimensionality: String(endpoint.dimensionality ?? 0),
      timeoutMs: String(endpoint.timeoutMs ?? 120000),
      enabled: endpoint.enabled,
      scope: endpoint.scope,
      ...authFormFromEndpoint(endpoint),
    });
    setShowForm(true);
  }

  function openDuplicate(endpoint: ModelEndpoint) {
    openEdit(endpoint);
    setEditing(null);
    setDuplicateSource(endpoint);
    setForm(current => ({ ...current, name: duplicateDisplayName(endpoint.name), scope: resolveCreateScope(viewer) }));
  }

  async function handleSave(e: React.FormEvent) {
    e.preventDefault();
    if (saving) return;
    if (formReason) { setError(formReason); return; }
    const inferenceProblem = validateInferenceOptions(form.inferenceOptions);
    if (inferenceProblem) { setError(t(inferenceProblem)); return; }
    const authProblem = validateEndpointAuth(form.provider, form);
    if (authProblem) { setError(lt(authProblem)); return; }
    setSaving(true);
    try {
      const payload: Partial<ModelEndpoint> & { apiKey?: string | null } = {
        inferenceOptions: form.inferenceOptions,
        name: form.name,
        kind: form.kind,
        provider: form.provider,
        baseUrl: isCloud ? 'https://ollama.com:443' : form.baseUrl,
        model: form.model.trim() || null,
        region: form.region.trim() || null,
        project: form.project.trim() || null,
        apiVersion: form.apiVersion.trim() || null,
        accessKeyId: form.accessKeyId.trim() || null,
        dimensionality: Number.parseInt(form.dimensionality, 10) || 0,
        timeoutMs: Number.parseInt(form.timeoutMs, 10) || 120000,
        enabled: form.enabled,
        scope: form.scope,
        ...buildEndpointAuthPayload(form.provider, form),
      };
      // Only send apiKey when the operator actually typed one, so an unchanged edit keeps the stored key.
      if (form.apiKeyTouched && formAuthType !== 'None') payload.apiKey = form.apiKey;

      const saved = editing
        ? await updateModelEndpoint(editing.id, payload)
        : duplicateSource ? await duplicateModelEndpoint(duplicateSource.id, payload) : await createModelEndpoint(payload);
      if (form.kind === 'Inference' && createLinkedCaptain) {
        // Keep the saved identity if the second request fails, so retrying cannot duplicate the endpoint.
        setEditing(saved);
        setDuplicateSource(null);
        try {
          await createCaptain({ name: captainName.trim() || saved.name, runtime: 'ApiEndpoint', modelEndpointId: saved.id });
        } catch (err: unknown) {
          await load();
          setError(t('Endpoint saved, but captain creation failed: {{error}}. Save again to retry, or uncheck captain creation to finish.', { error: err instanceof Error ? err.message : t('Unknown error') }));
          return;
        }
      }
      setShowForm(false);
      pushToast('success', createLinkedCaptain && form.kind === 'Inference'
        ? t('Endpoint "{{name}}" saved and captain created.', { name: saved.name })
        : t('Endpoint "{{name}}" saved.', { name: saved.name }));
      await load();
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Save failed.'));
    } finally {
      setSaving(false);
    }
  }

  async function load() {
    try {
      setLoading(true);
      // One bounded page of the largest size; the note above the table says when more endpoints exist.
      const result = await enumerateModelEndpoints();
      setEndpoints(result?.objects ?? []);
      setEndpointTotal(result?.totalRecords ?? (result?.objects?.length ?? 0));
      setError('');
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Failed to load endpoints.'));
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => { load(); }, []);

  const { seconds: refreshSeconds, setSeconds: setRefreshSeconds } = useAutoRefresh('model-endpoints', load);

  const filtered = useMemo(() => endpoints.filter((endpoint) => {
    const matchesSearch = search.trim().length === 0
      || endpoint.name.toLowerCase().includes(search.toLowerCase())
      || (endpoint.baseUrl || '').toLowerCase().includes(search.toLowerCase())
      || (endpoint.model || '').toLowerCase().includes(search.toLowerCase())
      || endpoint.id.toLowerCase().includes(search.toLowerCase());
    const matchesKind = kindFilter === 'all' || endpoint.kind === kindFilter;
    return matchesSearch && matchesKind;
  }), [endpoints, search, kindFilter]);

  async function handleValidate(endpoint: ModelEndpoint) {
    if (validating) return;
    setValidating(endpoint.id);
    setProbe({ open: true, endpoint, result: null, error: null });
    try {
      const result = await validateModelEndpoint(endpoint.id);
      setProbe(current => current.endpoint?.id === endpoint.id ? { ...current, result } : current);
      if (result.success) pushToast('success', t('Endpoint "{{name}}" validated.', { name: endpoint.name }));
      else pushToast('error', t('Endpoint "{{name}}" validation failed.', { name: endpoint.name }));
      await load();
    } catch (err: unknown) {
      const message = err instanceof Error ? err.message : t('Validation failed.');
      setProbe(current => current.endpoint?.id === endpoint.id ? { ...current, error: message } : current);
    } finally {
      setValidating(null);
    }
  }

  async function handleSweep() {
    if (sweeping) return;
    setSweeping(true);
    try {
      const result = await healthCheckModelEndpoints();
      pushToast('success', t('Health sweep probed {{count}} base URL(s).', { count: result.distinctBaseUrlsProbed }));
      await load();
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Health sweep failed.'));
    } finally {
      setSweeping(false);
    }
  }

  function handleDelete(endpoint: ModelEndpoint) {
    setConfirm({
      open: true,
      title: t('Delete Endpoint'),
      message: t('Delete "{{name}}"? This cannot be undone.', { name: endpoint.name }),
      onConfirm: async () => {
        setConfirm((current) => ({ ...current, open: false }));
        if (!(await deletion.run({ kind: 'modelEndpoint', action: () => deleteModelEndpoint(endpoint.id), errorMessage: (err) => (err instanceof Error ? err.message : t('Delete failed.')) }))) return;
        pushToast('warning', t('Endpoint "{{name}}" deleted.', { name: endpoint.name }));
        await load();
      },
    });
  }

  /** "Bearer token, key stored, 2 extra headers": what the endpoint authenticates with, without any secret. */
  function credentialSummary(endpoint: ModelEndpoint): string {
    const parts = [lt(authSummary(endpoint))];
    const type = effectiveAuthType(endpoint.provider, endpoint.authType);
    if (type !== 'None') parts.push(endpoint.hasApiKey ? t('secret stored') : t('no secret stored'));
    const headerCount = endpoint.headers?.length ?? 0;
    if (headerCount > 0) parts.push(t('{count, plural, one {# extra header} other {# extra headers}}', { count: headerCount }));
    return parts.join(', ');
  }

  const columns: DataTableColumn<ModelEndpoint>[] = [
    {
      key: 'id', label: t('ID'), required: true, headerTitle: t('Unique endpoint ID (mep_ prefix), used by captains and the API to refer to this endpoint.'), cellClassName: 'mono text-dim table-id-cell', interactive: true,
      render: (endpoint) => (
        <span className="id-display">
          <span className="id-value" title={endpoint.id}>{endpoint.id}</span>
          <CopyButton text={endpoint.id} title={t('Copy endpoint ID')} />
        </span>
      ),
    },
    {
      key: 'name', label: t('Endpoint'), required: true, headerTitle: t('Endpoint name, shown when choosing an endpoint for a captain. (disabled) means it is not used or health-checked.'), cellClassName: 'cell-nowrap',
      render: (endpoint) => (
        <>
          <strong>{endpoint.name}</strong>
          {!endpoint.enabled && <span className="text-dim"> ({t('disabled')})</span>}
        </>
      ),
    },
    {
      // Was a third line under the name; now its own one-line column.
      key: 'baseUrl', label: t('Base URL'), headerTitle: t('Server address requests go to. Endpoints that share a base URL and credentials are health-checked once per sweep.'), cellClassName: 'mono text-dim',
      cellTitle: (endpoint) => endpoint.baseUrl,
      render: (endpoint) => <span className="cell-clip"><span>{endpoint.baseUrl}</span></span>,
    },
    { key: 'kind', label: t('Kind'), headerTitle: t('Inference endpoints answer prompts (chat or completion) and can drive API Endpoint captains. Embedding endpoints turn text into vectors.'), cellClassName: 'text-dim', render: (endpoint) => endpoint.kind },
    { key: 'provider', label: t('Provider'), headerTitle: t('The API the endpoint speaks, such as OpenAI, Anthropic, Ollama or Bedrock. It decides the request format and default authentication.'), cellClassName: 'text-dim', render: (endpoint) => endpoint.provider },
    {
      key: 'auth', label: t('Authentication'), headerTitle: t('How requests are authenticated, whether a secret is stored, and how many extra headers are sent. Secrets are never shown.'), cellClassName: 'text-dim cell-nowrap',
      cellTitle: (endpoint) => credentialSummary(endpoint),
      render: (endpoint) => <span className="cell-one-line">{credentialSummary(endpoint)}</span>,
    },
    {
      key: 'model', label: t('Model'), headerTitle: t('Model requested from the provider (the deployment name on Azure, the model id on Bedrock). A captain can override it.'), cellClassName: 'text-dim',
      render: (endpoint) => <span className="cell-one-line" title={endpoint.model || undefined}>{endpoint.model || '-'}</span>,
    },
    { key: 'visibility', label: t('Visibility'), headerTitle: t('Tenant-wide endpoints are usable by everyone in the tenant and edited by admins; personal ones only by their owner.'), cellClassName: 'cell-nowrap', render: (endpoint) => <ScopeBadge scope={endpoint.scope} /> },
    {
      key: 'health', label: t('Health'), headerTitle: t('Result of the latest health check and a bar of recent checks (green passed, red failed). Click a cell for details.'), interactive: true, cellClassName: 'cell-nowrap',
      render: (endpoint) => (
        <span
          className="health-cell"
          role="button"
          tabIndex={0}
          title={t('Open this endpoint\'s health details: uptime, recent check history, latency and the last error.')}
          onClick={() => setHealth({ open: true, endpoint })}
          onKeyDown={(e) => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); setHealth({ open: true, endpoint }); } }}
        >
          <StatusBadge status={endpoint.healthStatus} />
          {endpoint.healthHistory.length > 0 && <HealthHistogram history={endpoint.healthHistory} width={96} height={18} />}
        </span>
      ),
    },
    {
      key: 'lastChecked', label: t('Last Checked'), headerTitle: t('When the endpoint was last health-checked. Never means no check has run yet, or the endpoint is disabled.'), cellClassName: 'text-dim cell-nowrap',
      cellTitle: (endpoint) => (endpoint.lastHealthCheckUtc ? formatDateTime(endpoint.lastHealthCheckUtc) : ''),
      render: (endpoint) => (endpoint.lastHealthCheckUtc ? formatRelativeTime(endpoint.lastHealthCheckUtc) : t('Never')),
    },
    {
      key: 'actions', label: t('Actions'), headerTitle: t('Open the menu in a row to view health, validate, edit or delete that endpoint.'), fixed: true, interactive: true, className: 'text-right',
      render: (endpoint) => {
        const canEditRow = canEditScoped(viewer, endpoint);
        return (
          <ActionMenu
            id={`endpoint-${endpoint.id}`}
            items={[
              { label: 'Health', title: 'Show this endpoint\'s uptime, recent check history, latency and last error.', onClick: () => setHealth({ open: true, endpoint }) },
              ...(canEditRow && endpoint.kind === 'Inference' ? [{ label: 'Evaluate', title: 'Open this endpoint\'s details to run the Quick probes against it as an evaluation.', onClick: () => setHealth({ open: true, endpoint }) }] : []),
              ...(canEditRow ? [{ label: 'Validate', disabled: validating !== null, title: 'Send a real request to the configured model with the stored credentials. The provider may bill for it.', onClick: () => handleValidate(endpoint) }] : []),
              ...(canEditRow ? [{ label: 'Duplicate', title: 'Edit a new copy before saving. Stored credentials are copied securely on save.', onClick: () => openDuplicate(endpoint) }] : []),
              ...(canEditRow ? [{ label: 'Edit', title: 'Change this endpoint\'s address, model, credentials, headers and limits.', onClick: () => openEdit(endpoint) }] : []),
              { label: 'View JSON', title: 'Show this endpoint\'s record as JSON. Stored secrets are never included.', onClick: () => setJsonData({ open: true, title: endpoint.name, data: endpoint }) },
              ...(canEditRow ? [{ label: 'Delete', danger: true as const, title: 'Permanently delete this endpoint, its stored secrets and its health history. Anything configured to use it needs another endpoint.', onClick: () => handleDelete(endpoint) }] : []),
            ]}
          />
        );
      },
    },
  ];

  return (
    <div>
      <PageHeader
        title={t('Endpoints')}
        subtitle={t('Managed embedding and inference model endpoints. Health checks are deduplicated by base URL; Validate sends a real request to the configured model.')}
        actions={(
          <>
            {canManage && (
              <button className="btn" onClick={handleSweep} disabled={sweeping} title={t('Health-check every enabled endpoint now instead of waiting for the next automatic sweep. Endpoints sharing a base URL and credentials are checked once.')}>
                {sweeping ? t('Sweeping...') : t('Run Health Sweep')}
              </button>
            )}
            {(
              <button className="btn btn-primary" onClick={openCreate} title={t('Add a model endpoint: a provider URL, model and credentials Armada can call, for example to drive an API Endpoint captain.')}>+ {t('Endpoint')}</button>
            )}
          </>
        )}
      />

      <ErrorModal error={error} onClose={() => setError('')} />
      <JsonViewer open={jsonData.open} title={jsonData.title} data={jsonData.data} onClose={() => setJsonData({ open: false, title: '', data: null })} />
      <ConfirmDialog
        open={confirm.open}
        title={confirm.title}
        message={confirm.message}
        onConfirm={confirm.onConfirm}
        onCancel={() => setConfirm((current) => ({ ...current, open: false }))}
      />
      {deletion.dialog}

      {showForm && (
        <div className="modal-overlay" onClick={() => setShowForm(false)}>
          <form className="modal modal-large" onClick={(e) => e.stopPropagation()} onSubmit={handleSave}>
            <h3>{editing ? t('Edit Endpoint') : duplicateSource ? t('Duplicate Endpoint') : t('Create Endpoint')}</h3>
            <label title={t(EP_NAME_TIP)}>{t('Name')}
              <input type="text" title={t(EP_NAME_TIP)} value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} required />
            </label>
            <div className="detail-form-grid">
              <label title={t(EP_KIND_TIP)}>{t('Kind')}
                <select title={t(EP_KIND_TIP)} value={form.kind} onChange={(e) => setForm({ ...form, kind: e.target.value as ModelEndpointKind })}>
                  {KINDS.map((k) => <option key={k} value={k}>{k}</option>)}
                </select>
              </label>
              <label title={t(EP_PROVIDER_TIP)}>{t('Provider')}
                <select title={t(EP_PROVIDER_TIP)} value={form.provider} onChange={(e) => setForm({ ...form, provider: e.target.value as ModelProvider })}>
                  {PROVIDERS.map((p) => <option key={p} value={p}>{p}</option>)}
                </select>
              </label>
            </div>
            {formReason && <p className="text-danger" style={{ margin: '0 0 0.5rem' }}>{formReason}</p>}
            {(isVertex || isBedrock) && (
              <div className="detail-form-grid">
                <label title={t(EP_REGION_TIP)}>{t('Region')}
                  <input type="text" title={t(EP_REGION_TIP)} value={form.region} onChange={(e) => setForm({ ...form, region: e.target.value })} placeholder={isBedrock ? 'us-east-1' : 'us-central1'} required />
                </label>
                {isVertex && (
                  <label title={t(EP_PROJECT_TIP)}>{t('Project')}
                    <input type="text" title={t(EP_PROJECT_TIP)} value={form.project} onChange={(e) => setForm({ ...form, project: e.target.value })} placeholder="my-gcp-project" required />
                  </label>
                )}
                {isBedrock && (
                  <label title={t(EP_ACCESS_KEY_TIP)}>{t('Access Key ID')}
                    <input type="text" title={t(EP_ACCESS_KEY_TIP)} value={form.accessKeyId} onChange={(e) => setForm({ ...form, accessKeyId: e.target.value })} placeholder="AKIA..." required autoComplete="off" />
                  </label>
                )}
              </div>
            )}
            <label title={t(baseUrlTip)}>{isVertex || isBedrock ? t('Base URL (optional override)') : isAzure ? t('Base URL (resource endpoint)') : t('Base URL')}
              <input
                type="text"
                title={t(baseUrlTip)}
                value={isCloud ? 'https://ollama.com:443' : form.baseUrl}
                disabled={isCloud}
                onChange={(e) => setForm({ ...form, baseUrl: e.target.value })}
                placeholder={isAzure ? 'https://my-resource.openai.azure.com' : 'https://api.openai.com'}
                required={baseUrlRequired}
              />
            </label>
            <div className="detail-form-grid">
              <label title={t(modelTip)}>{isAzure ? t('Deployment name') : isBedrock ? t('Bedrock model id') : t('Model')}
                <input type="text" title={t(modelTip)} value={form.model} onChange={(e) => setForm({ ...form, model: e.target.value })} placeholder={isAzure ? 'gpt-4o' : isBedrock ? 'anthropic.claude-3-5-sonnet-20240620-v1:0' : form.kind === 'Embedding' ? 'text-embedding-3-small' : 'gpt-4o-mini'} required={isAzure} />
              </label>
              {isAzure && (
                <label title={t(EP_API_VERSION_TIP)}>{t('API Version')}
                  <input type="text" title={t(EP_API_VERSION_TIP)} value={form.apiVersion} onChange={(e) => setForm({ ...form, apiVersion: e.target.value })} placeholder="2024-10-21 (default)" />
                </label>
              )}
            </div>
            <fieldset className="endpoint-auth-section">
              <legend title={t('How Armada proves its identity to the provider. Secrets are stored on the Admiral and never shown again after saving.')}>{t('Authentication')}</legend>
              {!signsRequests && (
                <label title={t(EP_AUTH_TYPE_TIP)}>{t('Authentication type')}
                  <select
                    title={t(EP_AUTH_TYPE_TIP)}
                    value={formAuthType}
                    onChange={(e) => setForm({ ...form, authType: e.target.value as ModelEndpointAuthType })}
                  >
                    {allowedAuthTypes(form.provider).map((type) => <option key={type} value={type}>{t(authTypeLabel(type))}</option>)}
                  </select>
                </label>
              )}
              {formAuthType === 'Basic' && (
                <label title={t(EP_USERNAME_TIP)}>{t('Username')}
                  <input type="text" title={t(EP_USERNAME_TIP)} value={form.authUsername} onChange={(e) => setForm({ ...form, authUsername: e.target.value })} required autoComplete="off" />
                </label>
              )}
              {formAuthType === 'Header' && (
                <label title={t(EP_HEADER_NAME_TIP)}>{t('Header name')}
                  <input type="text" title={t(EP_HEADER_NAME_TIP)} value={form.authHeaderName} onChange={(e) => setForm({ ...form, authHeaderName: e.target.value })} placeholder="X-API-Key" required autoComplete="off" />
                </label>
              )}
              {formAuthType === 'QueryParameter' && (
                <label title={t(EP_QUERY_TIP)}>{t('Query parameter name')}
                  <input type="text" title={t(EP_QUERY_TIP)} value={form.authQueryParameter} onChange={(e) => setForm({ ...form, authQueryParameter: e.target.value })} placeholder="key" required autoComplete="off" />
                </label>
              )}
              {secretLabelKey && (
                <label title={t(secretTip)}>{t(secretLabelKey)}
                  {isVertex ? (
                    <textarea
                      title={t(secretTip)}
                      value={form.apiKey}
                      onChange={(e) => setForm({ ...form, apiKey: e.target.value, apiKeyTouched: true })}
                      placeholder={t(authSecretPlaceholder(form.provider, formAuthType, hasStoredSecret))}
                      rows={4}
                      autoComplete="off"
                      style={{ fontFamily: 'monospace' }}
                    />
                  ) : (
                    <input
                      type="password"
                      title={t(secretTip)}
                      required={(isCloud || form.provider === 'XAI') && !hasStoredSecret}
                      value={form.apiKey}
                      onChange={(e) => setForm({ ...form, apiKey: e.target.value, apiKeyTouched: true })}
                      placeholder={t(authSecretPlaceholder(form.provider, formAuthType, hasStoredSecret))}
                      autoComplete="new-password"
                    />
                  )}
                </label>
              )}
              <p className="form-help endpoint-auth-sent-as" data-i18n-skip="true">{lt(sentAs)}</p>
              <div className="endpoint-headers">
                <div className="endpoint-headers-title">
                  <span className="form-label" title={t('Additional HTTP headers sent on every request to this endpoint, whatever the authentication type. Values are stored as secrets.')}>{t('Extra headers')}</span>
                  <span className="form-help">{t('Sent on every request, for example the two Cloudflare Access service token headers. Values are stored as secrets.')}</span>
                </div>
                {form.headers.map((row, index) => (
                  <div key={row.id} className="endpoint-header-row">
                    <input
                      type="text"
                      aria-label={t('Header {{index}} name', { index: index + 1 })}
                      title={t('HTTP header name, for example CF-Access-Client-Id. Header names are not case sensitive.')}
                      value={row.name}
                      onChange={(e) => updateHeader(row.id, { name: e.target.value })}
                      placeholder="CF-Access-Client-Id"
                      autoComplete="off"
                    />
                    <input
                      type="password"
                      aria-label={t('Header {{index}} value', { index: index + 1 })}
                      title={headerKeepsStoredValue(row) ? t('Header value, stored as a secret. Leave blank to keep the value already stored for this header.') : t('Header value, stored as a secret and never shown again after saving.')}
                      value={row.value}
                      onChange={(e) => updateHeader(row.id, { value: e.target.value })}
                      placeholder={headerKeepsStoredValue(row) ? t('(stored - leave blank to keep stored value)') : t('Value')}
                      autoComplete="new-password"
                    />
                    <button
                      type="button"
                      className="btn btn-sm"
                      onClick={() => removeHeader(row.id)}
                      title={t('Remove this header. It stops being sent once you save.')}
                    >
                      {t('Remove')}
                    </button>
                  </div>
                ))}
                <button type="button" className="btn btn-sm endpoint-header-add" onClick={addHeader} title={t('Add a header that is sent on every request to this endpoint.')}>+ {t('Add header')}</button>
              </div>
            </fieldset>
            <div className="detail-form-grid">
              <label title={t(EP_DIM_TIP)}>{t('Dimensionality')}
                <input type="number" min={0} title={t(EP_DIM_TIP)} value={form.dimensionality} onChange={(e) => setForm({ ...form, dimensionality: e.target.value })} />
              </label>
              <label title={t(EP_TIMEOUT_TIP)}>{t('Timeout (ms)')}
                <input type="number" min={1000} max={600000} title={t(EP_TIMEOUT_TIP)} value={form.timeoutMs} onChange={(e) => setForm({ ...form, timeoutMs: e.target.value })} />
              </label>
            </div>
            {form.kind === 'Inference' && <fieldset className="endpoint-auth-section endpoint-inference-section">
              <legend>{t('Inference and captain execution')}</legend>
              <div className="endpoint-inference-toggles">
              {([
                ['toolsEnabled', 'Enable tool calls'], ['readOnlyTools', 'Read-only built-in tools'],
                ['mcpToolsEnabled', 'Enable scoped MCP tools'], ['requireHarbor', 'Require Harbor execution'],
                ['streaming', 'Stream responses'],
              ] as const).map(([key, label]) => <label key={key} className="checkbox-row">
                <input type="checkbox" checked={form.inferenceOptions[key] ?? false} onChange={e => setForm({ ...form, inferenceOptions: { ...form.inferenceOptions, [key]: e.target.checked } })} /><span>{t(label)}</span>
              </label>)}
              </div>
              <p className="text-dim">{t('Read-only excludes shell, file changes and MCP tools. Require Harbor prevents fallback to Admiral execution.')}</p>
              <label>{t('Allowed tool names (one per line; blank allows all)')}
                <textarea rows={3} value={form.allowedToolsText} onChange={e => setForm({ ...form, allowedToolsText: e.target.value, inferenceOptions: { ...form.inferenceOptions, allowedTools: parseAllowedTools(e.target.value) } })} />
              </label>
              <div className="detail-form-grid">
              {([
                ['maxIterations', 'Maximum model turns', 1, 1000, 1], ['maxTokens', 'Maximum output tokens', 1, 10000000, 1],
                ['temperature', 'Temperature', 0, 2, 0.01], ['topP', 'Top P', 0, 1, 0.01],
                ['contextLength', 'Context window (Ollama num_ctx)', 512, 2097152, 1], ['maxConcurrentMissions', 'Max concurrent missions (per base URL)', 1, 64, 1],
                ['firstResponseTimeoutMs', 'First-response timeout (ms)', 1000, 1800000, 1],
              ] as const).map(([key, label, min, max, step]) => <label key={key}>{t(label)}
                <input type="number" min={min} max={max} step={step} required={key === 'maxIterations'}
                  placeholder={t(key === 'contextLength' ? 'Model default' : key === 'maxConcurrentMissions' ? 'Unlimited' : key === 'firstResponseTimeoutMs' ? 'Request timeout' : 'Provider default')}
                  value={form.inferenceOptions[key] ?? ''} onChange={e => setForm({ ...form, inferenceOptions: { ...form.inferenceOptions, [key]: e.target.value === '' ? null : Number(e.target.value) } })} />
              </label>)}
              </div>
              <p className="text-dim">{t('A model turn can contain several tool calls. Output tokens include reasoning and the visible answer; PolyPrompt defaults to 4,096 when no limit is set. Reasoning models may need a larger explicit budget.')}</p>
              <p className="text-dim">{t('The context window applies to Ollama and Ollama Cloud; a run that fills it stops with a context-limit error instead of continuing on truncated history. Max concurrent missions limits how many missions run at once against this base URL across all captains.')}</p>
              <p className="text-dim">{t('Stream responses when a proxy or load balancer cuts long requests: the request timeout then limits only the silence between chunks, not the whole response. The first-response timeout covers loading a cold model and evaluating a long prompt before the first chunk. Both need an updated Harbor.')}</p>
            </fieldset>}
            {form.kind === 'Inference' && <fieldset className="endpoint-auth-section endpoint-inference-section">
              <legend>{t('Captain')}</legend>
              <label className="checkbox-row" style={{ display: 'flex' }}>
                <input type="checkbox" checked={createLinkedCaptain} onChange={e => setCreateLinkedCaptain(e.target.checked)} />
                <span>{t('Create a captain for this endpoint')}</span>
              </label>
              <p className="text-dim">{t('Create an API Endpoint captain when saving, ready to select in Ask Armada and persona priorities.')}</p>
              {createLinkedCaptain && <label>{t('Captain name')}
                <input value={captainName} placeholder={form.name} onChange={e => setCaptainName(e.target.value)} />
              </label>}
            </fieldset>}
            <ScopeSelect viewer={viewer} value={form.scope} onChange={(scope) => setForm({ ...form, scope })} />
            <label className="checkbox-row" style={{ display: 'flex' }} title={t(EP_ENABLED_TIP)}>
              <input type="checkbox" title={t(EP_ENABLED_TIP)} checked={form.enabled} onChange={(e) => setForm({ ...form, enabled: e.target.checked })} />
              <span>{t('Enabled')}</span>
            </label>
            <div className="modal-actions">
              <button type="submit" className="btn btn-primary" disabled={saving || !!formReason} title={editing ? t('Save changes to this endpoint. Use Validate afterward to check it with a real request.') : t('Create this endpoint. Use Validate afterward to check it with a real request.')}>{saving ? t('Saving...') : editing ? t('Save Changes') : duplicateSource ? t('Save Copy') : t('Create Endpoint')}</button>
              <button type="button" className="btn" onClick={() => setShowForm(false)} disabled={saving}>{t('Cancel')}</button>
            </div>
          </form>
        </div>
      )}

      {probe.open && probe.endpoint && (
        <DialogShell
          open={probe.open}
          title={t('Validation Result')}
          onClose={() => setProbe({ open: false, endpoint: null, result: null, error: null })}
          footer={<button type="button" className="btn" onClick={() => setProbe({ open: false, endpoint: null, result: null, error: null })}>{t('Close')}</button>}
        >
            <p><strong>{probe.endpoint.name}</strong> <span className="text-dim mono" style={{ fontSize: '0.78rem' }}>{probe.endpoint.id}</span></p>
            {!probe.result && !probe.error && <LoadingIndicator label={t('Validating...')} />}
            {probe.error && <p role="alert" className="text-danger">{probe.error}</p>}
            {probe.result && <dl className="detail-list">
              <dt title={t('Whether the validation request succeeded.')}>{t('Result')}</dt>
              <dd><StatusBadge status={probe.result?.success ? 'Healthy' : 'Unhealthy'} /></dd>
              <dt title={t('How long the validation request took, in milliseconds.')}>{t('Latency')}</dt>
              <dd>{probe.result?.latencyMs ?? '-'} ms</dd>
              {probe.result?.statusCode != null && (<><dt title={t('HTTP status code the provider returned.')}>{t('Status Code')}</dt><dd>{probe.result.statusCode}</dd></>)}
              {probe.result?.embeddingDimensions != null && (<><dt title={t('Length of the vector the embedding endpoint returned.')}>{t('Dimensions')}</dt><dd>{probe.result.embeddingDimensions}</dd></>)}
              {probe.result?.sampleText && (<><dt title={t('The start of the text the model returned.')}>{t('Sample')}</dt><dd className="mono" style={{ whiteSpace: 'pre-wrap' }}>{probe.result.sampleText}</dd></>)}
              {probe.result?.error && (<><dt>{t('Error')}</dt><dd className="text-danger">{probe.result.error}</dd></>)}
              {probe.result?.note && (<><dt title={t('Where the check ran and what it could not cover.')}>{t('Note')}</dt><dd>{probe.result.note}</dd></>)}
            </dl>}
        </DialogShell>
      )}

      {health.open && health.endpoint && (
        <div className="modal-overlay" onClick={() => setHealth({ open: false, endpoint: null })}>
          <div className="modal modal-large" onClick={(e) => e.stopPropagation()}>
            <h3>{t('Health')}: {health.endpoint.name}</h3>
            <div className="health-modal">
              <div className="health-meta">
                <span><strong>{t('Base URL')}:</strong> <code>{health.endpoint.baseUrl}</code></span>
                {health.endpoint.model && <span><strong>{t('Model')}:</strong> {health.endpoint.model}</span>}
                <span><strong>{t('Authentication')}:</strong> {credentialSummary(health.endpoint)}</span>
                {(health.endpoint.headers?.length ?? 0) > 0 && (
                  <span><strong>{t('Extra headers')}:</strong> <code>{health.endpoint.headers.map((h) => h.name).join(', ')}</code></span>
                )}
                {health.endpoint.lastLatencyMs != null && <span><strong>{t('Latency')}:</strong> {health.endpoint.lastLatencyMs} ms</span>}
              </div>

              <div className="health-stats-row">
                <div className="health-stat-card" title={t('Result of the most recent health check.')}>
                  <div className="health-stat-label">{t('Status')}</div>
                  <div className="health-stat-value"><StatusBadge status={health.endpoint.healthStatus} /></div>
                </div>
                <div className="health-stat-card" title={t('Share of the retained health checks that passed.')}>
                  <div className="health-stat-label">{t('Uptime')}</div>
                  <div className="health-stat-value">{health.endpoint.healthHistory.length > 0 ? `${health.endpoint.uptimePercentage.toFixed(2)}%` : '-'}</div>
                </div>
                <div className="health-stat-card" title={t('How far back the retained health check history goes.')}>
                  <div className="health-stat-label">{t('History Span')}</div>
                  <div className="health-stat-value">{formatSpan(health.endpoint.firstHealthCheckUtc)}</div>
                </div>
                <div className="health-stat-card" title={t('Number of health checks in a row that have passed, counting back from the latest.')}>
                  <div className="health-stat-label">{t('Consecutive OK')}</div>
                  <div className="health-stat-value health-ok">{health.endpoint.consecutiveSuccesses}</div>
                </div>
                <div className="health-stat-card" title={t('Number of health checks in a row that have failed, counting back from the latest.')}>
                  <div className="health-stat-label">{t('Consecutive Fail')}</div>
                  <div className="health-stat-value health-fail">{health.endpoint.consecutiveFailures}</div>
                </div>
              </div>

              {health.endpoint.lastHealthError && (
                <div className="health-error-box">
                  <div className="health-error-label">{t('Last Error')}</div>
                  <div className="health-error-message">{health.endpoint.lastHealthError}</div>
                </div>
              )}

              <div>
                <div className="health-section-label" title={t('Recent health checks, oldest on the left. Green passed, red failed.')}>{t('Health History')}</div>
                <div className="health-histogram-container">
                  <HealthHistogram history={health.endpoint.healthHistory} height={36} fill />
                </div>
              </div>

              <div className="health-timestamps">
                <div><span>{t('First check')}</span><strong>{health.endpoint.firstHealthCheckUtc ? formatDateTime(health.endpoint.firstHealthCheckUtc) : '-'}</strong></div>
                <div><span>{t('Last check')}</span><strong>{health.endpoint.lastHealthCheckUtc ? formatDateTime(health.endpoint.lastHealthCheckUtc) : '-'}</strong></div>
                <div><span>{t('Last healthy')}</span><strong>{health.endpoint.lastHealthyUtc ? formatDateTime(health.endpoint.lastHealthyUtc) : '-'}</strong></div>
                <div><span>{t('Last unhealthy')}</span><strong>{health.endpoint.lastUnhealthyUtc ? formatDateTime(health.endpoint.lastUnhealthyUtc) : '-'}</strong></div>
              </div>

              <p className="text-dim" style={{ fontSize: '0.82rem', margin: 0 }}>{t('Health checks are deduplicated by base URL: endpoints sharing a base URL are probed once per sweep.')}</p>

              {health.endpoint.kind === 'Inference' && canEditScoped(viewer, health.endpoint) && (
                <div>
                  <div className="health-section-label" title={t('Probe this model endpoint: served context, output contract, ASCII and format, tool calling, speed and determinism.')}>{t('Evaluation')}</div>
                  <ModelProbeEvaluate key={health.endpoint.id} target="modelEndpoint" targetId={health.endpoint.id} />
                </div>
              )}
            </div>
            <div className="modal-actions">
              {canManage && (
                <button type="button" className="btn btn-primary" disabled={validating === health.endpoint.id} title={t('Send a real request to the configured model with the stored credentials. The provider may bill for it.')} onClick={() => { const ep = health.endpoint!; setHealth({ open: false, endpoint: null }); handleValidate(ep); }}>
                  {validating === health.endpoint.id ? t('Validating...') : t('Validate Now')}
                </button>
              )}
              <button type="button" className="btn" onClick={() => setHealth({ open: false, endpoint: null })}>{t('Close')}</button>
            </div>
          </div>
        </div>
      )}

      <div className="playbook-overview-grid">
        <div className="card playbook-overview-card" title={t('All endpoints you can see, enabled and disabled.')}>
          <span>{t('Total Endpoints')}</span>
          <strong>{Math.max(endpointTotal, endpoints.length)}</strong>
        </div>
        <div className="card playbook-overview-card" title={t('Endpoints whose latest health check passed.')}>
          <span>{t('Healthy')}</span>
          <strong>{endpoints.filter((e) => e.healthStatus === 'Healthy').length}</strong>
        </div>
        <div className="card playbook-overview-card" title={t('Endpoints whose latest health check failed. Open one to see the error.')}>
          <span>{t('Unhealthy')}</span>
          <strong>{endpoints.filter((e) => e.healthStatus === 'Unhealthy').length}</strong>
        </div>
        <div className="card playbook-overview-card" title={t('Endpoints that can be used and are included in health checks.')}>
          <span>{t('Enabled')}</span>
          <strong>{endpoints.filter((e) => e.enabled).length}</strong>
        </div>
      </div>

      <div className="card" style={{ padding: '1rem', marginBottom: '1rem' }}>
        <div className="playbook-filter-row">
          <input type="text" value={search} onChange={(e) => setSearch(e.target.value)} title={t('Show only endpoints whose name, base URL, model or ID contains this text.')} placeholder={t('Search by name, base URL, model, or ID...')} />
          <select aria-label={t('All kinds')} title={t('Show all endpoints, or only inference or only embedding ones.')} value={kindFilter} onChange={(e) => setKindFilter(e.target.value)}>
            <option value="all">{t('All kinds')}</option>
            {KINDS.map((k) => <option key={k} value={k}>{k}</option>)}
          </select>
        </div>
      </div>

      <TruncationNote shown={endpoints.length} total={endpointTotal} />
      <DataTable
        tableKey="model-endpoints"
        columns={columns}
        rows={filtered}
        rowKey={(endpoint) => endpoint.id}
        onRowClick={(endpoint) => (canEditScoped(viewer, endpoint) ? openEdit(endpoint) : setHealth({ open: true, endpoint }))}
        autoRefresh={{ seconds: refreshSeconds, onChange: setRefreshSeconds }}
        onRefresh={load}
        refreshTitle={t('Refresh endpoints')}
        placeholder={loading && endpoints.length === 0 ? <p className="text-dim">{t('Loading...')}</p> : filtered.length === 0 ? (
          <div className="playbook-empty-state">
            <strong>{t('No endpoints match the current filters.')}</strong>
            <span>{canManage ? t('Add an embedding or inference endpoint to manage and monitor it.') : t('Ask a tenant administrator to configure model endpoints.')}</span>
          </div>
        ) : undefined}
      />
    </div>
  );
}
