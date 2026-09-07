import { useEffect, useMemo, useState } from 'react';
import {
  listModelEndpoints,
  createModelEndpoint,
  updateModelEndpoint,
  deleteModelEndpoint,
  validateModelEndpoint,
  healthCheckModelEndpoints,
} from '../api/client';
import type { ModelEndpoint, ModelEndpointKind, ModelProvider, ModelEndpointProbeResult, ScopeEnum } from '../types/models';
import { canEdit as canEditScoped, resolveCreateScope, type ScopeViewer } from '../lib/scoping';
import ScopeBadge from '../components/shared/ScopeBadge';
import ScopeSelect from '../components/shared/ScopeSelect';
import { useAuth } from '../context/AuthContext';
import { useLocale } from '../context/LocaleContext';
import { useNotifications } from '../context/NotificationContext';
import ActionMenu from '../components/shared/ActionMenu';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import ErrorModal from '../components/shared/ErrorModal';
import JsonViewer from '../components/shared/JsonViewer';
import RefreshButton from '../components/shared/RefreshButton';
import AutoRefreshSelect from '../components/shared/AutoRefreshSelect';
import { useAutoRefresh } from '../lib/useAutoRefresh';
import PageHeader from '../components/shared/PageHeader';
import StatusBadge from '../components/shared/StatusBadge';
import HealthHistogram from '../components/shared/HealthHistogram';
import CopyButton from '../components/shared/CopyButton';

const PROVIDERS: ModelProvider[] = ['Ollama', 'OpenAI', 'OpenAICompatible', 'Anthropic', 'Gemini', 'VoyageAI'];
const KINDS: ModelEndpointKind[] = ['Embedding', 'Inference'];

interface EndpointForm {
  name: string;
  kind: ModelEndpointKind;
  provider: ModelProvider;
  baseUrl: string;
  model: string;
  apiKey: string;
  apiKeyTouched: boolean;
  dimensionality: string;
  timeoutMs: string;
  enabled: boolean;
  scope: ScopeEnum;
}

const EMPTY_FORM: EndpointForm = {
  name: 'New Endpoint',
  kind: 'Inference',
  provider: 'OpenAI',
  baseUrl: '',
  model: '',
  apiKey: '',
  apiKeyTouched: false,
  dimensionality: '0',
  timeoutMs: '120000',
  enabled: true,
  scope: 'TenantWide',
};

/** Humanize the span between the earliest retained probe and now, for the health modal. */
function formatSpan(firstUtc: string | null): string {
  if (!firstUtc) return '-';
  const ms = Date.now() - new Date(firstUtc).getTime();
  if (ms < 0) return '-';
  const minutes = Math.floor(ms / 60000);
  if (minutes < 1) return '<1m';
  if (minutes < 60) return `${minutes}m`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `${hours}h ${minutes % 60}m`;
  const days = Math.floor(hours / 24);
  return `${days}d ${hours % 24}h`;
}

/** Reason a provider/kind combination is invalid, or null when valid. Mirrors the server-side guard. */
function unsupportedReason(provider: ModelProvider, kind: ModelEndpointKind): string | null {
  if (kind === 'Embedding' && provider === 'Anthropic') return 'Anthropic does not provide an embeddings API. Choose Inference or a different provider.';
  if (kind === 'Inference' && provider === 'VoyageAI') return 'Voyage AI provides embeddings only. Choose Embedding or a different provider.';
  return null;
}

export default function Endpoints() {
  const { isAdmin, isTenantAdmin, user } = useAuth();
  const viewer: ScopeViewer = { isAdmin, isTenantAdmin, tenantId: user?.user?.tenantId, userId: user?.user?.id };
  const { t, formatRelativeTime, formatDateTime } = useLocale();
  const { pushToast } = useNotifications();
  const [endpoints, setEndpoints] = useState<ModelEndpoint[]>([]);
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
  const [saving, setSaving] = useState(false);
  const [form, setForm] = useState<EndpointForm>(EMPTY_FORM);

  // Validate + health modals
  const [validating, setValidating] = useState<string | null>(null);
  const [probe, setProbe] = useState<{ open: boolean; endpoint: ModelEndpoint | null; result: ModelEndpointProbeResult | null }>({ open: false, endpoint: null, result: null });
  const [health, setHealth] = useState<{ open: boolean; endpoint: ModelEndpoint | null }>({ open: false, endpoint: null });
  const [sweeping, setSweeping] = useState(false);

  const canManage = isAdmin || isTenantAdmin;
  const formReason = unsupportedReason(form.provider, form.kind);

  function openCreate() {
    setEditing(null);
    setForm({ ...EMPTY_FORM, scope: resolveCreateScope(viewer) });
    setShowForm(true);
  }

  function openEdit(endpoint: ModelEndpoint) {
    setEditing(endpoint);
    setForm({
      name: endpoint.name,
      kind: endpoint.kind,
      provider: endpoint.provider,
      baseUrl: endpoint.baseUrl,
      model: endpoint.model || '',
      apiKey: '',
      apiKeyTouched: false,
      dimensionality: String(endpoint.dimensionality ?? 0),
      timeoutMs: String(endpoint.timeoutMs ?? 120000),
      enabled: endpoint.enabled,
      scope: endpoint.scope,
    });
    setShowForm(true);
  }

  async function handleSave(e: React.FormEvent) {
    e.preventDefault();
    if (saving) return;
    if (formReason) { setError(formReason); return; }
    setSaving(true);
    try {
      const payload: Partial<ModelEndpoint> & { apiKey?: string | null } = {
        name: form.name,
        kind: form.kind,
        provider: form.provider,
        baseUrl: form.baseUrl,
        model: form.model.trim() || null,
        dimensionality: Number.parseInt(form.dimensionality, 10) || 0,
        timeoutMs: Number.parseInt(form.timeoutMs, 10) || 120000,
        enabled: form.enabled,
        scope: form.scope,
      };
      // Only send apiKey when the operator actually typed one, so an unchanged edit keeps the stored key.
      if (form.apiKeyTouched) payload.apiKey = form.apiKey;

      if (editing) {
        const updated = await updateModelEndpoint(editing.id, payload);
        setShowForm(false);
        pushToast('success', t('Endpoint "{{name}}" saved.', { name: updated.name }));
      } else {
        const created = await createModelEndpoint(payload);
        setShowForm(false);
        pushToast('success', t('Endpoint "{{name}}" created.', { name: created.name }));
      }
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
      const result = await listModelEndpoints();
      setEndpoints(result || []);
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
    setValidating(endpoint.id);
    try {
      const result = await validateModelEndpoint(endpoint.id);
      setProbe({ open: true, endpoint, result });
      if (result.success) pushToast('success', t('Endpoint "{{name}}" validated.', { name: endpoint.name }));
      else pushToast('error', t('Endpoint "{{name}}" validation failed.', { name: endpoint.name }));
      await load();
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Validation failed.'));
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
        try {
          await deleteModelEndpoint(endpoint.id);
          pushToast('warning', t('Endpoint "{{name}}" deleted.', { name: endpoint.name }));
          await load();
        } catch (err: unknown) {
          setError(err instanceof Error ? err.message : t('Delete failed.'));
        }
      },
    });
  }

  return (
    <div>
      <PageHeader
        title={t('Endpoints')}
        subtitle={t('Managed embedding and inference model endpoints. Health checks are deduplicated by base URL; Validate sends a real request to the configured model.')}
        actions={(
          <>
            <AutoRefreshSelect seconds={refreshSeconds} onChange={setRefreshSeconds} />
            <RefreshButton onRefresh={load} title={t('Refresh endpoints')} />
            {canManage && (
              <button className="btn" onClick={handleSweep} disabled={sweeping} title={t('Probe all enabled endpoints, deduplicated by base URL.')}>
                {sweeping ? t('Sweeping...') : t('Run Health Sweep')}
              </button>
            )}
            {(
              <button className="btn btn-primary" onClick={openCreate}>+ {t('Endpoint')}</button>
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

      {showForm && (
        <div className="modal-overlay" onClick={() => setShowForm(false)}>
          <form className="modal modal-large" onClick={(e) => e.stopPropagation()} onSubmit={handleSave}>
            <h3>{editing ? t('Edit Endpoint') : t('Create Endpoint')}</h3>
            <label>{t('Name')}
              <input type="text" value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} required />
            </label>
            <div className="detail-form-grid">
              <label>{t('Kind')}
                <select value={form.kind} onChange={(e) => setForm({ ...form, kind: e.target.value as ModelEndpointKind })}>
                  {KINDS.map((k) => <option key={k} value={k}>{k}</option>)}
                </select>
              </label>
              <label>{t('Provider')}
                <select value={form.provider} onChange={(e) => setForm({ ...form, provider: e.target.value as ModelProvider })}>
                  {PROVIDERS.map((p) => <option key={p} value={p}>{p}</option>)}
                </select>
              </label>
            </div>
            {formReason && <p className="text-danger" style={{ margin: '0 0 0.5rem' }}>{formReason}</p>}
            <label>{t('Base URL')}
              <input type="text" value={form.baseUrl} onChange={(e) => setForm({ ...form, baseUrl: e.target.value })} placeholder="https://api.openai.com" required />
            </label>
            <label>{t('Model')}
              <input type="text" value={form.model} onChange={(e) => setForm({ ...form, model: e.target.value })} placeholder={form.kind === 'Embedding' ? 'text-embedding-3-small' : 'gpt-4o-mini'} />
            </label>
            <label>{t('API Key')}
              <input
                type="password"
                value={form.apiKey}
                onChange={(e) => setForm({ ...form, apiKey: e.target.value, apiKeyTouched: true })}
                placeholder={editing && editing.hasApiKey ? t('(unchanged - leave blank to keep stored key)') : t('Optional')}
                autoComplete="new-password"
              />
            </label>
            <div className="detail-form-grid">
              <label>{t('Dimensionality')}
                <input type="number" min={0} value={form.dimensionality} onChange={(e) => setForm({ ...form, dimensionality: e.target.value })} />
              </label>
              <label>{t('Timeout (ms)')}
                <input type="number" min={1000} max={600000} value={form.timeoutMs} onChange={(e) => setForm({ ...form, timeoutMs: e.target.value })} />
              </label>
            </div>
            <ScopeSelect viewer={viewer} value={form.scope} onChange={(scope) => setForm({ ...form, scope })} />
            <label className="checkbox-row">
              <input type="checkbox" checked={form.enabled} onChange={(e) => setForm({ ...form, enabled: e.target.checked })} />
              <span>{t('Enabled')}</span>
            </label>
            <div className="modal-actions">
              <button type="submit" className="btn btn-primary" disabled={saving || !!formReason}>{saving ? t('Saving...') : editing ? t('Save Changes') : t('Create Endpoint')}</button>
              <button type="button" className="btn" onClick={() => setShowForm(false)} disabled={saving}>{t('Cancel')}</button>
            </div>
          </form>
        </div>
      )}

      {probe.open && probe.endpoint && (
        <div className="modal-overlay" onClick={() => setProbe({ open: false, endpoint: null, result: null })}>
          <div className="modal" onClick={(e) => e.stopPropagation()}>
            <h3>{t('Validation Result')}</h3>
            <p><strong>{probe.endpoint.name}</strong> <span className="text-dim mono" style={{ fontSize: '0.78rem' }}>{probe.endpoint.id}</span></p>
            <dl className="detail-list">
              <dt>{t('Result')}</dt>
              <dd><StatusBadge status={probe.result?.success ? 'Healthy' : 'Unhealthy'} /></dd>
              <dt>{t('Latency')}</dt>
              <dd>{probe.result?.latencyMs ?? '-'} ms</dd>
              {probe.result?.statusCode != null && (<><dt>{t('Status Code')}</dt><dd>{probe.result.statusCode}</dd></>)}
              {probe.result?.embeddingDimensions != null && (<><dt>{t('Dimensions')}</dt><dd>{probe.result.embeddingDimensions}</dd></>)}
              {probe.result?.sampleText && (<><dt>{t('Sample')}</dt><dd className="mono" style={{ whiteSpace: 'pre-wrap' }}>{probe.result.sampleText}</dd></>)}
              {probe.result?.error && (<><dt>{t('Error')}</dt><dd className="text-danger">{probe.result.error}</dd></>)}
            </dl>
            <div className="modal-actions">
              <button type="button" className="btn" onClick={() => setProbe({ open: false, endpoint: null, result: null })}>{t('Close')}</button>
            </div>
          </div>
        </div>
      )}

      {health.open && health.endpoint && (
        <div className="modal-overlay" onClick={() => setHealth({ open: false, endpoint: null })}>
          <div className="modal modal-large" onClick={(e) => e.stopPropagation()}>
            <h3>{t('Health')}: {health.endpoint.name}</h3>
            <div className="health-modal">
              <div className="health-meta">
                <span><strong>{t('Base URL')}:</strong> <code>{health.endpoint.baseUrl}</code></span>
                {health.endpoint.model && <span><strong>{t('Model')}:</strong> {health.endpoint.model}</span>}
                {health.endpoint.lastLatencyMs != null && <span><strong>{t('Latency')}:</strong> {health.endpoint.lastLatencyMs} ms</span>}
              </div>

              <div className="health-stats-row">
                <div className="health-stat-card">
                  <div className="health-stat-label">{t('Status')}</div>
                  <div className="health-stat-value"><StatusBadge status={health.endpoint.healthStatus} /></div>
                </div>
                <div className="health-stat-card">
                  <div className="health-stat-label">{t('Uptime')}</div>
                  <div className="health-stat-value">{health.endpoint.healthHistory.length > 0 ? `${health.endpoint.uptimePercentage.toFixed(2)}%` : '-'}</div>
                </div>
                <div className="health-stat-card">
                  <div className="health-stat-label">{t('History Span')}</div>
                  <div className="health-stat-value">{formatSpan(health.endpoint.firstHealthCheckUtc)}</div>
                </div>
                <div className="health-stat-card">
                  <div className="health-stat-label">{t('Consecutive OK')}</div>
                  <div className="health-stat-value health-ok">{health.endpoint.consecutiveSuccesses}</div>
                </div>
                <div className="health-stat-card">
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
                <div className="health-section-label">{t('Health History')}</div>
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
            </div>
            <div className="modal-actions">
              {canManage && (
                <button type="button" className="btn btn-primary" disabled={validating === health.endpoint.id} onClick={() => { const ep = health.endpoint!; setHealth({ open: false, endpoint: null }); handleValidate(ep); }}>
                  {validating === health.endpoint.id ? t('Validating...') : t('Validate Now')}
                </button>
              )}
              <button type="button" className="btn" onClick={() => setHealth({ open: false, endpoint: null })}>{t('Close')}</button>
            </div>
          </div>
        </div>
      )}

      <div className="playbook-overview-grid">
        <div className="card playbook-overview-card">
          <span>{t('Total Endpoints')}</span>
          <strong>{endpoints.length}</strong>
        </div>
        <div className="card playbook-overview-card">
          <span>{t('Healthy')}</span>
          <strong>{endpoints.filter((e) => e.healthStatus === 'Healthy').length}</strong>
        </div>
        <div className="card playbook-overview-card">
          <span>{t('Unhealthy')}</span>
          <strong>{endpoints.filter((e) => e.healthStatus === 'Unhealthy').length}</strong>
        </div>
        <div className="card playbook-overview-card">
          <span>{t('Enabled')}</span>
          <strong>{endpoints.filter((e) => e.enabled).length}</strong>
        </div>
      </div>

      <div className="card" style={{ padding: '1rem', marginBottom: '1rem' }}>
        <div className="playbook-filter-row">
          <input type="text" value={search} onChange={(e) => setSearch(e.target.value)} placeholder={t('Search by name, base URL, model, or ID...')} />
          <select value={kindFilter} onChange={(e) => setKindFilter(e.target.value)}>
            <option value="all">{t('All kinds')}</option>
            {KINDS.map((k) => <option key={k} value={k}>{k}</option>)}
          </select>
        </div>
      </div>

      {loading && endpoints.length === 0 ? (
        <p className="text-dim">{t('Loading...')}</p>
      ) : filtered.length === 0 ? (
        <div className="playbook-empty-state">
          <strong>{t('No endpoints match the current filters.')}</strong>
          <span>{canManage ? t('Add an embedding or inference endpoint to manage and monitor it.') : t('Ask a tenant administrator to configure model endpoints.')}</span>
        </div>
      ) : (
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th>{t('Endpoint')}</th>
                <th>{t('Kind')}</th>
                <th>{t('Provider')}</th>
                <th>{t('Model')}</th>
                <th>{t('Visibility')}</th>
                <th>{t('Health')}</th>
                <th>{t('Last Checked')}</th>
                <th className="text-right">{t('Actions')}</th>
              </tr>
            </thead>
            <tbody>
              {filtered.map((endpoint) => {
                const canEditRow = canEditScoped(viewer, endpoint);
                return (
                <tr key={endpoint.id} className="clickable" onClick={() => canEditRow ? openEdit(endpoint) : setHealth({ open: true, endpoint })}>
                  <td>
                    <strong>{endpoint.name}</strong>
                    {!endpoint.enabled && <span className="text-dim"> ({t('disabled')})</span>}
                    <div className="mono text-dim" style={{ fontSize: '0.78rem', display: 'flex', alignItems: 'center', gap: '0.25rem' }} onClick={(e) => e.stopPropagation()}>
                      <span title={endpoint.id}>{endpoint.id}</span>
                      <CopyButton text={endpoint.id} title={t('Copy endpoint ID')} />
                    </div>
                    <div className="mono text-dim" style={{ fontSize: '0.78rem' }}>{endpoint.baseUrl}</div>
                  </td>
                  <td className="text-dim">{endpoint.kind}</td>
                  <td className="text-dim">{endpoint.provider}</td>
                  <td className="text-dim">{endpoint.model || '-'}</td>
                  <td><ScopeBadge scope={endpoint.scope} /></td>
                  <td onClick={(e) => e.stopPropagation()}>
                    <span
                      className="health-cell"
                      role="button"
                      tabIndex={0}
                      title={t('View health detail')}
                      onClick={() => setHealth({ open: true, endpoint })}
                      onKeyDown={(e) => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); setHealth({ open: true, endpoint }); } }}
                    >
                      <StatusBadge status={endpoint.healthStatus} />
                      {endpoint.healthHistory.length > 0 && <HealthHistogram history={endpoint.healthHistory} width={96} height={18} />}
                    </span>
                  </td>
                  <td className="text-dim" title={endpoint.lastHealthCheckUtc ? formatDateTime(endpoint.lastHealthCheckUtc) : ''}>
                    {endpoint.lastHealthCheckUtc ? formatRelativeTime(endpoint.lastHealthCheckUtc) : t('Never')}
                  </td>
                  <td className="text-right" onClick={(e) => e.stopPropagation()}>
                    <ActionMenu
                      id={`endpoint-${endpoint.id}`}
                      items={[
                        { label: 'Health', onClick: () => setHealth({ open: true, endpoint }) },
                        ...(canEditRow ? [{ label: validating === endpoint.id ? 'Validating...' : 'Validate', onClick: () => handleValidate(endpoint) }] : []),
                        ...(canEditRow ? [{ label: 'Edit', onClick: () => openEdit(endpoint) }] : []),
                        { label: 'View JSON', onClick: () => setJsonData({ open: true, title: endpoint.name, data: endpoint }) },
                        ...(canEditRow ? [{ label: 'Delete', danger: true as const, onClick: () => handleDelete(endpoint) }] : []),
                      ]}
                    />
                  </td>
                </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
