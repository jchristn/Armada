import { useEffect, useState, useMemo, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import { listCaptains, createCaptain, updateCaptain, deleteCaptain, stopCaptain, recallCaptain, stopAllCaptains, restartCaptain, getCaptainTools, listModelEndpoints } from '../api/client';
import type { ModelEndpoint } from '../types/models';
import type { Captain, CaptainToolAccessResult } from '../types/models';
import Pagination from '../components/shared/Pagination';
import ActionMenu from '../components/shared/ActionMenu';
import StatusBadge from '../components/shared/StatusBadge';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import MuxRuntimeFields from '../components/captains/MuxRuntimeFields';
import CaptainTierBadge from '../components/shared/CaptainTierBadge';
import CaptainToolViewer from '../components/captains/CaptainToolViewer';
import JsonViewer from '../components/shared/JsonViewer';
import CopyButton from '../components/shared/CopyButton';
import RefreshButton from '../components/shared/RefreshButton';
import AutoRefreshSelect from '../components/shared/AutoRefreshSelect';
import { useAutoRefresh } from '../lib/useAutoRefresh';
import PageHeader from '../components/shared/PageHeader';
import ErrorModal from '../components/shared/ErrorModal';
import { useLocale } from '../context/LocaleContext';
import { useNotifications } from '../context/NotificationContext';
import { canCaptainStartPlanning } from '../lib/captains';
import { buildMuxRuntimeOptionsJson, EMPTY_MUX_CAPTAIN_FORM, isMuxRuntime, muxFormFromCaptain, type MuxCaptainFormFields } from '../lib/mux';
import { buildCaptainDuplicatePayload } from '../lib/duplicates';

type SortDir = 'asc' | 'desc';
type SortField = 'name' | 'runtime' | 'state' | 'createdUtc';
type CaptainFormState = {
  name: string;
  runtime: string;
  systemInstructions: string;
  model: string;
  modelEndpointId: string;
  reasoningEffort: string;
  tier: string;
} & MuxCaptainFormFields;

export default function Captains() {
  const navigate = useNavigate();
  const { t, formatRelativeTime, formatDateTime } = useLocale();
  const { pushToast } = useNotifications();
  const [captains, setCaptains] = useState<Captain[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  // Modal state
  const [showForm, setShowForm] = useState(false);
  const [editing, setEditing] = useState<Captain | null>(null);
  const [form, setForm] = useState<CaptainFormState>({ name: '', runtime: '', systemInstructions: '', model: '', modelEndpointId: '', reasoningEffort: '', tier: '', ...EMPTY_MUX_CAPTAIN_FORM });
  const [saving, setSaving] = useState(false);
  const [inferenceEndpoints, setInferenceEndpoints] = useState<ModelEndpoint[]>([]);

  // JSON viewer
  const [jsonData, setJsonData] = useState<{ open: boolean; title: string; data: unknown }>({ open: false, title: '', data: null });
  const [toolViewer, setToolViewer] = useState<{ open: boolean; captainName: string; loading: boolean; error: string; data: CaptainToolAccessResult | null }>({
    open: false,
    captainName: '',
    loading: false,
    error: '',
    data: null,
  });

  // Confirm dialog
  const [confirm, setConfirm] = useState<{ open: boolean; title: string; message: string; onConfirm: () => void }>({ open: false, title: '', message: '', onConfirm: () => {} });

  // Selection
  const [selected, setSelected] = useState<string[]>([]);

  // Sorting
  const [sortField, setSortField] = useState<SortField>('name');
  const [sortDir, setSortDir] = useState<SortDir>('asc');

  // Column filters
  const [colFilters, setColFilters] = useState({ name: '', runtime: '', state: '' });

  // Pagination
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(25);

  const load = useCallback(async () => {
    try {
      setLoading(true);
      const result = await listCaptains({ pageSize: 9999 });
      setCaptains(result.objects);
      setError('');
    } catch {
      setError(t('Failed to load captains.'));
    } finally {
      setLoading(false);
    }
  }, [t]);

  useEffect(() => { load(); }, [load]);
  const { seconds: refreshSeconds, setSeconds: setRefreshSeconds } = useAutoRefresh('captains', load);

  // Filtered rows
  const filtered = useMemo(() => {
    return captains.filter(c =>
      (!colFilters.name || c.name.toLowerCase().includes(colFilters.name.toLowerCase())) &&
      (!colFilters.runtime || c.runtime.toLowerCase().includes(colFilters.runtime.toLowerCase())) &&
      (!colFilters.state || (c.state ?? '').toLowerCase().includes(colFilters.state.toLowerCase()))
    );
  }, [captains, colFilters]);

  // Sorted rows
  const sorted = useMemo(() => {
    const arr = [...filtered];
    arr.sort((a, b) => {
      let va: string = '';
      let vb: string = '';
      switch (sortField) {
        case 'runtime': va = a.runtime.toLowerCase(); vb = b.runtime.toLowerCase(); break;
        case 'state': va = (a.state ?? '').toLowerCase(); vb = (b.state ?? '').toLowerCase(); break;
        case 'createdUtc': va = a.createdUtc; vb = b.createdUtc; break;
        default: va = a.name.toLowerCase(); vb = b.name.toLowerCase();
      }
      if (va < vb) return sortDir === 'asc' ? -1 : 1;
      if (va > vb) return sortDir === 'asc' ? 1 : -1;
      return 0;
    });
    return arr;
  }, [filtered, sortField, sortDir]);

  // Paginated
  const totalPages = Math.max(1, Math.ceil(sorted.length / pageSize));
  const currentPage = Math.min(pageNumber, totalPages);
  const paginated = useMemo(() => {
    const start = (currentPage - 1) * pageSize;
    return sorted.slice(start, start + pageSize);
  }, [sorted, currentPage, pageSize]);

  function handleSort(field: SortField) {
    if (sortField === field) setSortDir(d => d === 'asc' ? 'desc' : 'asc');
    else { setSortField(field); setSortDir('asc'); }
  }

  function sortIcon(field: SortField) {
    if (sortField !== field) return '';
    return sortDir === 'asc' ? ' \u25B2' : ' \u25BC';
  }

  // Selection
  const allSelected = selected.length > 0 && selected.length === filtered.length;
  function toggleSelect(id: string) {
    setSelected(s => s.includes(id) ? s.filter(x => x !== id) : [...s, id]);
  }
  function selectAll() { setSelected(filtered.map(c => c.id)); }
  function clearSelection() { setSelected([]); }

  // Load the configured inference endpoints so an API-endpoint captain can be pointed at one.
  useEffect(() => {
    listModelEndpoints()
      .then(result => setInferenceEndpoints((result ?? []).filter(e => e.kind === 'Inference')))
      .catch(() => setInferenceEndpoints([]));
  }, []);

  // CRUD
  function openCreate() {
    setForm({ name: '', runtime: '', systemInstructions: '', model: '', modelEndpointId: '', reasoningEffort: '', tier: '', ...EMPTY_MUX_CAPTAIN_FORM });
    setEditing(null);
    setShowForm(true);
  }

  function openEdit(c: Captain) {
    setForm({
      name: c.name,
      runtime: c.runtime,
      systemInstructions: c.systemInstructions ?? '',
      model: c.model ?? '',
      modelEndpointId: c.modelEndpointId ?? '',
      reasoningEffort: c.reasoningEffort ?? '',
      tier: c.tier ?? '',
      ...muxFormFromCaptain(c),
    });
    setEditing(c);
    setShowForm(true);
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (saving) return;
    try {
      if (isMuxRuntime(form.runtime) && !form.muxEndpoint.trim()) {
        setError(t('Mux captains require a named Mux endpoint.'));
        return;
      }

      if (form.runtime === 'ApiEndpoint' && !form.modelEndpointId) {
        setError(t('API-endpoint captains require an inference endpoint. Select one, or add it under Configuration > Endpoints.'));
        return;
      }

      setSaving(true);
      const payload = { ...form } as Record<string, unknown>;
      if (!payload.systemInstructions) delete payload.systemInstructions;
      payload.model = form.model.trim() ? form.model.trim() : null;
      payload.modelEndpointId = form.runtime === 'ApiEndpoint' ? (form.modelEndpointId || null) : null;
      payload.reasoningEffort = form.reasoningEffort ? form.reasoningEffort : null;
      payload.tier = form.tier ? form.tier : null;
      payload.runtimeOptionsJson = buildMuxRuntimeOptionsJson(form.runtime, form);
      delete payload.muxConfigDirectory;
      delete payload.muxEndpoint;
      delete payload.muxBaseUrl;
      delete payload.muxAdapterType;
      delete payload.muxTemperature;
      delete payload.muxMaxTokens;
      delete payload.muxSystemPromptPath;
      delete payload.muxApprovalPolicy;
      if (editing) await updateCaptain(editing.id, payload);
      else await createCaptain(payload);
      setShowForm(false);
      pushToast('success', editing
        ? t('Captain "{{name}}" saved.', { name: form.name })
        : t('Captain "{{name}}" created.', { name: form.name }));
      load();
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : t('Save failed.'));
    } finally {
      setSaving(false);
    }
  }

  function handleDelete(id: string, name: string) {
    setConfirm({
      open: true,
      title: t('Delete Captain'),
      message: t('Delete captain "{{name}}"? This cannot be undone.', { name }),
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        try {
          await deleteCaptain(id);
          pushToast('warning', t('Captain "{{name}}" deleted.', { name }));
          load();
        } catch { setError(t('Delete failed.')); }
      },
    });
  }

  function handleBulkDelete() {
    setConfirm({
      open: true,
      title: t('Delete Selected Captains'),
      message: t('Delete {{count}} selected captain(s)? This cannot be undone.', { count: selected.length }),
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        const ids = [...selected];
        setSelected([]);
        let failed = 0;
        for (const id of ids) {
          try { await deleteCaptain(id); } catch { failed++; }
        }
        const deleted = ids.length - failed;
        if (deleted > 0) {
          pushToast(failed > 0 ? 'warning' : 'success', failed > 0
            ? t('Deleted {{deleted}} captains. {{failed}} failed.', { deleted, failed })
            : t('Deleted {{deleted}} captains.', { deleted }));
        }
        if (failed > 0) setError(t('Deleted {{deleted}} captains, {{failed}} failed.', { deleted: ids.length - failed, failed }));
        load();
      },
    });
  }

  function handleStop(id: string, name: string) {
    setConfirm({
      open: true,
      title: t('Stop Captain'),
      message: t('Stop captain "{{name}}"? The captain process will be terminated.', { name }),
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        try {
          await stopCaptain(id);
          pushToast('warning', t('Captain "{{name}}" stopped.', { name }));
          load();
        } catch { setError(t('Stop failed.')); }
      },
    });
  }

  function handleRecall(id: string, name: string) {
    setConfirm({
      open: true,
      title: t('Recall Captain'),
      message: t('Recall captain "{{name}}"? The captain will be recalled from its current mission.', { name }),
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        try {
          await recallCaptain(id);
          pushToast('warning', t('Captain "{{name}}" recalled.', { name }));
          load();
        } catch { setError(t('Recall failed.')); }
      },
    });
  }

  function handleRestart(id: string, name: string) {
    setConfirm({
      open: true,
      title: t('Restart Captain'),
      message: t('Restart captain "{{name}}"? The captain will be deleted and recreated with the same saved configuration.', { name }),
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        try {
          await restartCaptain(id);
          pushToast('success', t('Captain "{{name}}" restarted.', { name }));
          load();
        } catch { setError(t('Restart failed.')); }
      },
    });
  }

  function handleStopAll() {
    setConfirm({
      open: true,
      title: t('Stop All Captains'),
      message: t('Stop ALL captains? All captain processes will be terminated. This cannot be undone.'),
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        try {
          await stopAllCaptains();
          pushToast('warning', t('All captains stopped.'));
          load();
        } catch { setError(t('Stop all failed.')); }
      },
    });
  }

  async function handleDuplicate(captain: Captain) {
    try {
      const created = await createCaptain(buildCaptainDuplicatePayload(captain));
      pushToast('success', t('Captain "{{name}}" duplicated.', { name: created.name }));
      navigate(`/captains/${created.id}`);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Duplicate failed.'));
    }
  }

  async function handleViewTools(captain: Captain) {
    setToolViewer({
      open: true,
      captainName: captain.name,
      loading: true,
      error: '',
      data: null,
    });

    try {
      const result = await getCaptainTools(captain.id);
      setToolViewer({
        open: true,
        captainName: captain.name,
        loading: false,
        error: '',
        data: result,
      });
    } catch {
      setToolViewer({
        open: true,
        captainName: captain.name,
        loading: false,
        error: t('Failed to load captain tools.'),
        data: null,
      });
    }
  }

  function handleStartPlanning(captain: Captain) {
    navigate('/planning', {
      state: {
        captainId: captain.id,
      },
    });
  }

  return (
    <div>
      <PageHeader
        title={t('Captains')}
        subtitle={t('AI agent harness processes that execute missions. Monitor state, current mission, and captain lifecycle.')}
        actions={(
          <>
            {selected.length > 0 && (
              <button className="btn btn-sm btn-danger" onClick={handleBulkDelete}>
                {t('Delete Selected')} ({selected.length})
              </button>
            )}
            <button className="btn btn-sm btn-danger" onClick={handleStopAll} title={t('Stop all captain processes')}>{t('Stop All')}</button>
            <button className="btn btn-primary btn-sm" onClick={openCreate}>+ {t('Captain')}</button>
            <AutoRefreshSelect seconds={refreshSeconds} onChange={setRefreshSeconds} />
            <RefreshButton onRefresh={load} title={t('Refresh captain data')} />
          </>
        )}
      />

      <ErrorModal error={error} onClose={() => setError('')} />

      {/* Create/Edit Modal */}
      {showForm && (
        <div className="modal-overlay" onClick={() => setShowForm(false)}>
          <form className={`modal modal-captain${isMuxRuntime(form.runtime) ? ' modal-mux' : ''}`} onClick={e => e.stopPropagation()} onSubmit={handleSubmit}>
            <h3>{editing ? t('Edit Captain') : t('Create Captain')}</h3>
            <label>{t('Name')}<input value={form.name} onChange={e => setForm({ ...form, name: e.target.value })} required /></label>
            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '0 1rem' }}>
              <label title={t('The AI agent runtime this captain will use')}>{t('Runtime')}
                <select value={form.runtime} onChange={e => setForm({ ...form, runtime: e.target.value })} required>
                  <option value="">{t('Select runtime...')}</option>
                  <option value="ClaudeCode">Claude Code</option>
                  <option value="Codex">Codex</option>
                  <option value="Gemini">Gemini</option>
                  <option value="Cursor">Cursor</option>
                  <option value="Mux">Mux</option>
                  <option value="OpenCode">OpenCode</option>
                  <option value="ApiEndpoint">API Endpoint</option>
                </select>
              </label>
              <label title={t('Optional AI model identifier. Leave blank to let the runtime choose its default model.')}>
                {t('Model')}
                <input value={form.model} onChange={e => setForm({ ...form, model: e.target.value })} placeholder={form.runtime === 'ApiEndpoint' ? t('Optional; overrides the endpoint model') : t('e.g., gpt-5.4-mini')} />
              </label>
            </div>
            {form.runtime === 'ApiEndpoint' && (
              <label title={t('The configured inference endpoint this captain drives. Manage endpoints under Configuration > Endpoints.')}>
                {t('Inference Endpoint')}
                <select value={form.modelEndpointId} onChange={e => setForm({ ...form, modelEndpointId: e.target.value })} required>
                  <option value="">{t('Select an inference endpoint...')}</option>
                  {inferenceEndpoints.map(ep => (
                    <option key={ep.id} value={ep.id}>{ep.name} ({ep.provider}{ep.model ? ' / ' + ep.model : ''})</option>
                  ))}
                </select>
                {inferenceEndpoints.length === 0 && (
                  <small className="text-dim" style={{ display: 'block', marginTop: '0.25rem' }}>
                    {t('No inference endpoints configured. Add one under Configuration > Endpoints first.')}
                  </small>
                )}
              </label>
            )}
            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '0 1rem' }}>
              <label>
                {t('Reasoning effort')}
                <select value={form.reasoningEffort} onChange={e => setForm({ ...form, reasoningEffort: e.target.value })}>
                  <option value="">{t('Runtime default')}</option>
                  <option value="Off">{t('Off')}</option>
                  <option value="Minimal">{t('Minimal')}</option>
                  <option value="Low">{t('Low')}</option>
                  <option value="Medium">{t('Medium')}</option>
                  <option value="High">{t('High')}</option>
                </select>
              </label>
              <label>
                {t('Capability tier')}
                <select value={form.tier} onChange={e => setForm({ ...form, tier: e.target.value })}>
                  <option value="">{t('Auto (classify from model)')}</option>
                  <option value="Economy">{t('Economy')}</option>
                  <option value="Standard">{t('Standard')}</option>
                  <option value="Premium">{t('Premium')}</option>
                </select>
              </label>
            </div>
            <MuxRuntimeFields
              runtime={form.runtime}
              form={form}
              onChange={(patch) => setForm((current) => ({ ...current, ...patch }))}
              t={t}
            />
            <label className="captain-instructions-field" title={t('Optional instructions injected into every mission prompt for this captain. Use this to specialize behavior, add guardrails, or provide persistent context.')}>
              {t('System Instructions')}
              <textarea value={form.systemInstructions} onChange={e => setForm({ ...form, systemInstructions: e.target.value })} rows={4} placeholder={t('e.g., You are a testing specialist. Always run tests before committing...')} />
            </label>
            <div className="modal-actions">
              <button type="submit" className="btn btn-primary" disabled={saving}>{saving ? t('Saving...') : t('Save')}</button>
              <button type="button" className="btn" onClick={() => setShowForm(false)} disabled={saving}>{t('Cancel')}</button>
            </div>
          </form>
        </div>
      )}

      {/* JSON Viewer */}
      <JsonViewer open={jsonData.open} title={jsonData.title} data={jsonData.data} onClose={() => setJsonData({ open: false, title: '', data: null })} />
      <CaptainToolViewer
        open={toolViewer.open}
        captainName={toolViewer.captainName}
        loading={toolViewer.loading}
        error={toolViewer.error}
        data={toolViewer.data}
        onClose={() => setToolViewer({ open: false, captainName: '', loading: false, error: '', data: null })}
      />

      {/* Confirm Dialog */}
      <ConfirmDialog open={confirm.open} title={confirm.title} message={confirm.message}
        onConfirm={confirm.onConfirm} onCancel={() => setConfirm(c => ({ ...c, open: false }))} />

      {loading && captains.length === 0 && <p className="text-dim">{t('Loading...')}</p>}
      {!loading && captains.length === 0 && <p className="text-dim">{t('No captains configured.')}</p>}

      {captains.length > 0 && (
        <>
          <Pagination pageNumber={currentPage} pageSize={pageSize} totalPages={totalPages}
            totalRecords={sorted.length}
            onPageChange={p => setPageNumber(p)} onPageSizeChange={s => { setPageSize(s); setPageNumber(1); }} />

          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th className="col-checkbox">
                    <input type="checkbox" checked={allSelected} onChange={e => e.target.checked ? selectAll() : clearSelection()} title={t('Select all captains')} />
                  </th>
                  <th className="sortable" onClick={() => handleSort('name')} title={t('Captain name -- click to sort')}>
                    {t('Name')}{sortIcon('name')}
                  </th>
                  <th>{t('ID')}</th>
                  <th className="sortable" onClick={() => handleSort('runtime')} title={t('Runtime -- click to sort')}>
                    {t('Runtime')}{sortIcon('runtime')}
                  </th>
                  <th className="sortable" onClick={() => handleSort('state')} title={t('State -- click to sort')}>
                    {t('State')}{sortIcon('state')}
                  </th>
                  <th>{t('Current Mission')}</th>
                  <th>{t('Heartbeat')}</th>
                  <th className="sortable" onClick={() => handleSort('createdUtc')} title={t('Created date -- click to sort')}>
                    {t('Created')}{sortIcon('createdUtc')}
                  </th>
                  <th className="text-right">{t('Actions')}</th>
                </tr>
                <tr className="column-filter-row">
                  <td></td>
                  <td><input type="text" className="col-filter" value={colFilters.name} onChange={e => { setColFilters(f => ({ ...f, name: e.target.value })); setPageNumber(1); }} placeholder={t('Filter...')} /></td>
                  <td></td>
                  <td><input type="text" className="col-filter" value={colFilters.runtime} onChange={e => { setColFilters(f => ({ ...f, runtime: e.target.value })); setPageNumber(1); }} placeholder={t('Filter...')} /></td>
                  <td><input type="text" className="col-filter" value={colFilters.state} onChange={e => { setColFilters(f => ({ ...f, state: e.target.value })); setPageNumber(1); }} placeholder={t('Filter...')} /></td>
                  <td></td>
                  <td></td>
                  <td></td>
                  <td></td>
                </tr>
              </thead>
              <tbody>
                {paginated.map(c => (
                  <tr key={c.id} className="clickable" onClick={() => openEdit(c)}>
                    <td className="col-checkbox" onClick={e => e.stopPropagation()}>
                      <input type="checkbox" checked={selected.includes(c.id)} onChange={() => toggleSelect(c.id)} title={t('Select this captain')} />
                    </td>
                    <td><strong>{c.name}</strong>{c.tier ? <> <CaptainTierBadge tier={c.tier} /></> : null}</td>
                    <td className="mono text-dim table-id-cell">
                      <span className="id-display">
                        <span className="id-value" title={c.id}>{c.id}</span>
                        <CopyButton text={c.id} onClick={e => e.stopPropagation()} />
                      </span>
                    </td>
                    <td className="text-dim">{c.runtime}</td>
                    <td>
                      <StatusBadge status={c.state} />
                      {c.state === 'Quarantined' && (
                        <span className="tag stalled" title={c.quarantineReason || undefined} style={{ marginLeft: '0.35rem' }}>
                          {c.quarantineUntilUtc ? t('until {{time}}', { time: formatRelativeTime(c.quarantineUntilUtc) }) : t('quarantined')}
                        </span>
                      )}
                    </td>
                    <td className="mono text-dim" onClick={e => e.stopPropagation()}>
                      {c.currentMissionId ? (
                        <a href="#" onClick={e => { e.preventDefault(); navigate(`/missions/${c.currentMissionId}`); }}>
                          {c.currentMissionId.substring(0, 8)}...
                        </a>
                      ) : '-'}
                    </td>
                    <td className="text-dim" title={formatDateTime(c.lastHeartbeatUtc)}>{formatRelativeTime(c.lastHeartbeatUtc)}</td>
                    <td className="text-dim" title={formatDateTime(c.createdUtc)}>{formatRelativeTime(c.createdUtc)}</td>
                    <td className="text-right" onClick={e => e.stopPropagation()}>
                      <ActionMenu id={`captain-${c.id}`} items={[
                        { label: 'View Detail', onClick: () => navigate(`/captains/${c.id}`) },
                        ...(canCaptainStartPlanning(c) ? [{ label: 'Start Planning', onClick: () => handleStartPlanning(c) }] : []),
                        { label: 'Edit', onClick: () => openEdit(c) },
                        { label: 'Duplicate', onClick: () => void handleDuplicate(c) },
                        { label: 'View Tools', onClick: () => void handleViewTools(c) },
                        { label: 'View JSON', onClick: () => setJsonData({ open: true, title: `${t('Captain')}: ${c.name}`, data: c }) },
                        { label: 'View Notifications', onClick: () => navigate('/inbox') },
                        { label: 'Stop', onClick: () => handleStop(c.id, c.name) },
                        { label: 'Recall', onClick: () => handleRecall(c.id, c.name) },
                        { label: 'Restart', onClick: () => handleRestart(c.id, c.name) },
                        { label: 'Delete', danger: true, onClick: () => handleDelete(c.id, c.name) },
                      ]} />
                    </td>
                  </tr>
                ))}
                {paginated.length === 0 && (
                  <tr><td colSpan={9} className="text-dim">{t('No captains match the current filters.')}</td></tr>
                )}
              </tbody>
            </table>
          </div>
        </>
      )}
    </div>
  );
}
