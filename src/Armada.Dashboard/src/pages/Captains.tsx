import { useEffect, useState, useMemo, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import { listCaptains, createCaptain, updateCaptain, deleteCaptain, stopCaptain, recallCaptain, stopAllCaptains, restartCaptain, getCaptainTools, listModelEndpoints, setCaptainCliPermissionPolicy } from '../api/client';
import type { ModelEndpoint } from '../types/models';
import type { Captain, CaptainToolAccessResult } from '../types/models';
import DataTable, { type DataTableColumn } from '../components/shared/DataTable';
import ActionMenu from '../components/shared/ActionMenu';
import StatusBadge from '../components/shared/StatusBadge';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import MuxRuntimeFields from '../components/captains/MuxRuntimeFields';
import CaptainTierBadge from '../components/shared/CaptainTierBadge';
import CaptainToolViewer from '../components/captains/CaptainToolViewer';
import JsonViewer from '../components/shared/JsonViewer';
import CopyButton from '../components/shared/CopyButton';
import UserScopeFilter from '../components/shared/UserScopeFilter';
import { useAutoRefresh } from '../lib/useAutoRefresh';
import PageHeader from '../components/shared/PageHeader';
import ErrorModal from '../components/shared/ErrorModal';
import { useLocale } from '../context/LocaleContext';
import { useNotifications } from '../context/NotificationContext';
import { useAuth } from '../context/AuthContext';
import CliPermissionPolicySelect from '../components/cliPermissions/CliPermissionPolicySelect';
import { canCaptainStartPlanning } from '../lib/captains';
import { isMuxRuntime } from '../lib/mux';
import { supportsAutoApproveSwitch } from '../lib/captainApproval';
import { buildCaptainCreatePayload, buildCaptainPayload, captainFormError, captainFormErrorMessage, captainFormFromCaptain, cliPolicyChanged, emptyCaptainForm, type CaptainFormState } from '../lib/captainForm';
import { buildCaptainDuplicatePayload } from '../lib/duplicates';

type SortDir = 'asc' | 'desc';
type SortField = 'name' | 'runtime' | 'state' | 'createdUtc';

export default function Captains() {
  const navigate = useNavigate();
  const { t, formatRelativeTime, formatDateTime } = useLocale();
  const { pushToast } = useNotifications();
  const { isAdmin, isTenantAdmin } = useAuth();
  const canManageCliPolicy = !!isAdmin || !!isTenantAdmin;
  const [captains, setCaptains] = useState<Captain[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  // Modal state
  const [showForm, setShowForm] = useState(false);
  const [editing, setEditing] = useState<Captain | null>(null);
  const [form, setForm] = useState<CaptainFormState>(emptyCaptainForm());
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
  const [userScope, setUserScope] = useState('');
  const [pageSize, setPageSize] = useState(25);

  const load = useCallback(async () => {
    try {
      setLoading(true);
      const result = await listCaptains({ pageSize: 9999, filters: userScope ? { userId: userScope } : undefined });
      setCaptains(result.objects);
      setError('');
    } catch {
      setError(t('Failed to load captains.'));
    } finally {
      setLoading(false);
    }
  }, [userScope, t]);

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
    setForm(emptyCaptainForm());
    setEditing(null);
    setShowForm(true);
  }

  function openEdit(c: Captain) {
    setForm(captainFormFromCaptain(c, { withEndpoint: true }));
    setEditing(c);
    setShowForm(true);
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (saving) return;
    try {
      const formError = captainFormError(form);
      if (formError) {
        setError(t(captainFormErrorMessage(formError)));
        return;
      }

      setSaving(true);
      if (editing) {
        await updateCaptain(editing.id, buildCaptainPayload(form));
        if (cliPolicyChanged(editing, form)) {
          await setCaptainCliPermissionPolicy(editing.id, form.cliPermissionPolicy);
        }
      } else {
        await createCaptain(buildCaptainCreatePayload(form));
      }
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

  function setColFilter(key: 'name' | 'runtime' | 'state', value: string) {
    setColFilters(f => ({ ...f, [key]: value }));
    setPageNumber(1);
  }

  const columns: DataTableColumn<Captain>[] = [
    {
      key: 'name', label: t('Name'), required: true, sortKey: 'name', headerTitle: t('Captain name -- click to sort'), cellClassName: 'cell-nowrap',
      filter: <input type="text" className="col-filter" aria-label={t('Name')} value={colFilters.name} onChange={e => setColFilter('name', e.target.value)} placeholder={t('Filter...')} />,
      render: (c) => <><strong>{c.name}</strong>{c.tier ? <> <CaptainTierBadge tier={c.tier} /></> : null}</>,
    },
    {
      key: 'id', label: t('ID'), required: true, cellClassName: 'mono text-dim table-id-cell',
      render: (c) => (
        <span className="id-display">
          <span className="id-value" title={c.id}>{c.id}</span>
          <CopyButton text={c.id} onClick={e => e.stopPropagation()} />
        </span>
      ),
    },
    {
      key: 'runtime', label: t('Runtime'), sortKey: 'runtime', headerTitle: t('Runtime -- click to sort'), cellClassName: 'text-dim',
      clearFilter: () => setColFilter('runtime', ''),
      filter: <input type="text" className="col-filter" aria-label={t('Runtime')} value={colFilters.runtime} onChange={e => setColFilter('runtime', e.target.value)} placeholder={t('Filter...')} />,
      render: (c) => c.runtime,
    },
    {
      key: 'state', label: t('State'), sortKey: 'state', headerTitle: t('State -- click to sort'), cellClassName: 'cell-nowrap',
      clearFilter: () => setColFilter('state', ''),
      filter: <input type="text" className="col-filter" aria-label={t('State')} value={colFilters.state} onChange={e => setColFilter('state', e.target.value)} placeholder={t('Filter...')} />,
      render: (c) => (
        <>
          <StatusBadge status={c.state} />
          {c.state === 'Quarantined' && (
            <span className="tag stalled" title={c.quarantineReason || undefined} style={{ marginLeft: '0.35rem' }}>
              {c.quarantineUntilUtc ? t('until {{time}}', { time: formatRelativeTime(c.quarantineUntilUtc) }) : t('quarantined')}
            </span>
          )}
        </>
      ),
    },
    {
      key: 'currentMission', label: t('Current Mission'), cellClassName: 'mono text-dim cell-nowrap', interactive: true,
      cellTitle: (c) => c.currentMissionId ?? undefined,
      render: (c) => c.currentMissionId ? (
        <a href="#" onClick={e => { e.preventDefault(); navigate(`/missions/${c.currentMissionId}`); }}>
          {c.currentMissionId.substring(0, 8)}...
        </a>
      ) : '-',
    },
    {
      key: 'heartbeat', label: t('Heartbeat'), cellClassName: 'text-dim cell-nowrap', cellTitle: (c) => formatDateTime(c.lastHeartbeatUtc),
      render: (c) => formatRelativeTime(c.lastHeartbeatUtc),
    },
    {
      key: 'created', label: t('Created'), sortKey: 'createdUtc', headerTitle: t('Created date -- click to sort'),
      cellClassName: 'text-dim cell-nowrap', cellTitle: (c) => formatDateTime(c.createdUtc),
      render: (c) => formatRelativeTime(c.createdUtc),
    },
    {
      key: 'actions', label: t('Actions'), fixed: true, interactive: true, className: 'text-right',
      render: (c) => (
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
      ),
    },
  ];

  return (
    <div>
      <PageHeader
        title={t('Captains')}
        subtitle={t('AI agent harness processes that execute missions. Monitor state, current mission, and captain lifecycle.')}
        actions={(
          <>
            <UserScopeFilter value={userScope} onChange={(id) => { setUserScope(id); setPageNumber(1); }} />
            {selected.length > 0 && (
              <button className="btn btn-sm btn-danger" onClick={handleBulkDelete}>
                {t('Delete Selected')} ({selected.length})
              </button>
            )}
            <button className="btn btn-sm btn-danger" onClick={handleStopAll} title={t('Stop all captain processes')}>{t('Stop All')}</button>
            <button className="btn btn-primary btn-sm" onClick={openCreate}>+ {t('Captain')}</button>
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
            {supportsAutoApproveSwitch(form.runtime) && (
              <label className="captain-auto-approve-field" title={t('When off, the captain runs without its permission-bypass flag (Claude Code acceptEdits, Codex workspace-write sandbox, Gemini auto_edit, Cursor without --force, OpenCode without --auto, Mux deny). Shell commands the agent wants to run are then refused unless the runtime is configured to allow them.')}>
                <input type="checkbox" checked={form.autoApprove} onChange={e => setForm({ ...form, autoApprove: e.target.checked })} />
                {' '}{t('Auto-approve agent tool use (runs the CLI with its permission-bypass flag)')}
              </label>
            )}
            <div className="captain-cli-policy-field">
              <label htmlFor="captain-cli-policy">{t('CLI tool permissions')}</label>
              <CliPermissionPolicySelect
                id="captain-cli-policy"
                value={form.cliPermissionPolicy}
                onChange={(value) => setForm((current) => ({ ...current, cliPermissionPolicy: value }))}
                allowBypass={canManageCliPolicy}
                disabled={!canManageCliPolicy}
                ariaDescribedBy="captain-cli-policy-help"
              />
              <small id="captain-cli-policy-help" className="text-dim">
                {t('How this captain handles shell commands, file edits, and fetches that need permission. Inherit: missions follow the auto-approve option when it is set, then the server default; Ask conversations use the server default (Settings > CLI Tool Permissions). A conversation can override it.')}
                {!canManageCliPolicy && <> {t('Only admins can change this.')}</>}
              </small>
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

      <DataTable
        tableKey="captains"
        columns={columns}
        rows={paginated}
        rowKey={(c) => c.id}
        onRowClick={openEdit}
        sort={{ field: sortField, dir: sortDir, onSort: (field) => handleSort(field as SortField) }}
        pagination={{
          pageNumber: currentPage, pageSize, totalPages, totalRecords: sorted.length,
          onPageChange: (p) => setPageNumber(p),
          onPageSizeChange: (size) => { setPageSize(size); setPageNumber(1); },
        }}
        autoRefresh={{ seconds: refreshSeconds, onChange: setRefreshSeconds }}
        onRefresh={load}
        refreshTitle={t('Refresh captain data')}
        selection={{
          isSelected: (c) => selected.includes(c.id),
          onToggle: (c) => toggleSelect(c.id),
          allSelected,
          onToggleAll: (checked) => (checked ? selectAll() : clearSelection()),
          selectAllLabel: t('Select all captains'),
          rowLabel: () => t('Select this captain'),
        }}
        emptyMessage={t('No captains match the current filters.')}
        placeholder={captains.length > 0 ? undefined : <p className="text-dim">{loading ? t('Loading...') : t('No captains configured.')}</p>}
      />
    </div>
  );
}
