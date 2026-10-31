import { useEffect, useState, useMemo, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import { apiErrorMessage, listCaptains, createCaptain, updateCaptain, deleteCaptain, stopCaptain, recallCaptain, stopAllCaptains, restartCaptain, getCaptainTools, enumerateModelEndpoints, evaluateCaptain, setCaptainCliPermissionPolicy } from '../api/client';
import { listAll } from '../api/paging';
import type { ModelEndpoint, ModelEvaluationOptions, ModelProbeDepth } from '../types/models';
import type { Captain, CaptainToolAccessResult } from '../types/models';
import DataTable, { type DataTableColumn } from '../components/shared/DataTable';
import ActionMenu from '../components/shared/ActionMenu';
import StatusBadge from '../components/shared/StatusBadge';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import MuxRuntimeFields from '../components/captains/MuxRuntimeFields';
import CaptainTierBadge from '../components/shared/CaptainTierBadge';
import CaptainToolViewer from '../components/captains/CaptainToolViewer';
import ModelEvaluateDialog from '../components/ModelEvaluateDialog';
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
import { CAPTAIN_AUTO_APPROVE_TOOLTIP, CAPTAIN_CLI_POLICY_TOOLTIP, CAPTAIN_INFERENCE_ENDPOINT_TOOLTIP, CAPTAIN_NAME_TOOLTIP, CAPTAIN_REASONING_EFFORT_TOOLTIP, CAPTAIN_RUNTIME_TOOLTIP, CAPTAIN_SYSTEM_INSTRUCTIONS_TOOLTIP, CAPTAIN_TIER_TOOLTIP, buildCaptainCreatePayload, buildCaptainPayload, captainFormError, captainFormErrorMessage, captainFormFromCaptain, captainModelPlaceholder, captainModelTooltip, cliPolicyChanged, emptyCaptainForm, type CaptainFormState } from '../lib/captainForm';
import { buildCaptainDuplicatePayload } from '../lib/duplicates';
import { usePersistedPageSize } from '../lib/usePersistedPageSize';
import { PLANNING_PATH } from '../lib/navTargets';
import { useDeleteProgress } from '../lib/useDeleteProgress';

type SortDir = 'asc' | 'desc';
type SortField = 'name' | 'runtime' | 'state' | 'createdUtc';

export default function Captains() {
  const navigate = useNavigate();
  const { t, formatRelativeTime, formatDateTime } = useLocale();
  const { pushToast } = useNotifications();
  const deletion = useDeleteProgress();
  const { isAdmin, isTenantAdmin } = useAuth();
  const canManageCliPolicy = !!isAdmin || !!isTenantAdmin;
  // POST /captains/{id}/evaluate needs a tenant admin (or global admin), so only they see Evaluate...
  const canEvaluate = !!isAdmin || !!isTenantAdmin;
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
  const [evaluateTarget, setEvaluateTarget] = useState<Captain | null>(null);
  const [evaluating, setEvaluating] = useState(false);
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
  const [pageSize, setPageSize] = usePersistedPageSize('captains', 25);

  const load = useCallback(async () => {
    try {
      setLoading(true);
      const result = await listAll(listCaptains, { filters: userScope ? { userId: userScope } : undefined });
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
    // A picker needs every endpoint: read the pages explicitly (bounded by listAll's safety stop).
    listAll(enumerateModelEndpoints)
      .then(result => setInferenceEndpoints((result?.objects ?? []).filter(e => e.kind === 'Inference')))
      .catch(() => setInferenceEndpoints([]));
  }, []);

  // CRUD
  function openCreate() {
    setForm(emptyCaptainForm());
    setEditing(null);
    setShowForm(true);
  }

  function openEdit(c: Captain) {
    // Carry the persona fields even though this modal does not show them, so saving keeps them unchanged.
    setForm(captainFormFromCaptain(c, { withEndpoint: true, withPersonas: true }));
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
        if (!(await deletion.run({ kind: 'captain', action: () => deleteCaptain(id), errorMessage: (err) => apiErrorMessage(err, t('Delete failed.')) }))) return;
        pushToast('warning', t('Captain "{{name}}" deleted.', { name }));
        load();
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
        if (!(await deletion.run({
          kind: 'captain',
          count: ids.length,
          action: async () => {
            failed = 0;
            for (const id of ids) {
              try { await deleteCaptain(id); } catch { failed++; }
            }
          },
        }))) return;
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

  async function handleEvaluate(captain: Captain, depth: ModelProbeDepth, options: ModelEvaluationOptions) {
    setEvaluating(true);
    try {
      const run = await evaluateCaptain(captain.id, depth, options);
      setEvaluateTarget(null);
      pushToast('success', t('Evaluation {{runId}} started for captain "{{name}}".', { runId: run.id, name: captain.name }));
      navigate(`/captains/${encodeURIComponent(captain.id)}?evaluationRun=${encodeURIComponent(run.id)}`);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Evaluate failed.'));
    } finally {
      setEvaluating(false);
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
      // Probe once (refresh) for the summary and sources; the viewer then reads each tool section a page at a time.
      const result = await getCaptainTools(captain.id, { refresh: true, pageSize: 1 });
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
    navigate(PLANNING_PATH, {
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
      key: 'id', label: t('ID'), required: true, headerTitle: t('Unique captain ID (cpt_ prefix), used by the API, CLI, MCP tools and logs. Use the copy button to copy it.'), cellClassName: 'mono text-dim table-id-cell',
      render: (c) => (
        <span className="id-display">
          <span className="id-value" title={c.id}>{c.id}</span>
          <CopyButton text={c.id} onClick={e => e.stopPropagation()} />
        </span>
      ),
    },
    {
      key: 'name', label: t('Name'), required: true, sortKey: 'name', headerTitle: t('Captain name, shown in lists, logs and mission assignments. Click to sort.'), cellClassName: 'cell-nowrap',
      filter: <input type="text" className="col-filter" aria-label={t('Name')} title={t('Show only captains whose name contains this text (not case sensitive).')} value={colFilters.name} onChange={e => setColFilter('name', e.target.value)} placeholder={t('Filter...')} />,
      render: (c) => <><strong>{c.name}</strong>{c.tier ? <> <CaptainTierBadge tier={c.tier} /></> : null}</>,
    },
    {
      key: 'runtime', label: t('Runtime'), sortKey: 'runtime', headerTitle: t('Agent program the captain runs, such as ClaudeCode, Codex, Mux or ApiEndpoint. Click to sort.'), cellClassName: 'text-dim',
      clearFilter: () => setColFilter('runtime', ''),
      filter: <input type="text" className="col-filter" aria-label={t('Runtime')} title={t('Show only captains whose runtime contains this text, for example codex.')} value={colFilters.runtime} onChange={e => setColFilter('runtime', e.target.value)} placeholder={t('Filter...')} />,
      render: (c) => c.runtime,
    },
    {
      key: 'state', label: t('State'), sortKey: 'state', headerTitle: t('Idle is free for work, Working is on a mission, Planning, Refining and Analyzing are reserved, Stalled has gone silent, Quarantined is skipped by dispatch for a while. Click to sort.'), cellClassName: 'cell-nowrap',
      clearFilter: () => setColFilter('state', ''),
      filter: <input type="text" className="col-filter" aria-label={t('State')} title={t('Show only captains whose state contains this text, for example idle or working.')} value={colFilters.state} onChange={e => setColFilter('state', e.target.value)} placeholder={t('Filter...')} />,
      render: (c) => (
        <>
          <StatusBadge status={c.state} />
          {c.state === 'Quarantined' && (
            <span className="tag stalled" title={c.quarantineReason ? t('Quarantined: dispatch skips this captain until the time shown. Reason: {{reason}}', { reason: c.quarantineReason }) : t('Quarantined: dispatch skips this captain until the time shown, then it returns to Idle.')} style={{ marginLeft: '0.35rem' }}>
              {c.quarantineUntilUtc ? t('until {{time}}', { time: formatRelativeTime(c.quarantineUntilUtc) }) : t('quarantined')}
            </span>
          )}
        </>
      ),
    },
    {
      key: 'currentMission', label: t('Current Mission'), headerTitle: t('The mission this captain is running now. Click the ID to open it; a dash means it has no mission.'), cellClassName: 'mono text-dim cell-nowrap', interactive: true,
      cellTitle: (c) => c.currentMissionId ?? undefined,
      render: (c) => c.currentMissionId ? (
        <a href="#" onClick={e => { e.preventDefault(); navigate(`/missions/${c.currentMissionId}`); }}>
          {c.currentMissionId.substring(0, 8)}...
        </a>
      ) : '-',
    },
    {
      key: 'heartbeat', label: t('Heartbeat'), headerTitle: t('When the captain last produced agent output. If it stops advancing while the captain is Working, the captain may be stalled. Hover a value for the exact time.'), cellClassName: 'text-dim cell-nowrap', cellTitle: (c) => formatDateTime(c.lastHeartbeatUtc),
      render: (c) => formatRelativeTime(c.lastHeartbeatUtc),
    },
    {
      key: 'created', label: t('Created'), sortKey: 'createdUtc', headerTitle: t('When this captain was created. Hover a value for the exact time; click to sort.'),
      cellClassName: 'text-dim cell-nowrap', cellTitle: (c) => formatDateTime(c.createdUtc),
      render: (c) => formatRelativeTime(c.createdUtc),
    },
    {
      key: 'actions', label: t('Actions'), headerTitle: t('Open the menu in a row to view, edit, duplicate, stop, restart or delete that captain.'), fixed: true, interactive: true, className: 'text-right',
      render: (c) => (
        <ActionMenu id={`captain-${c.id}`} items={[
          { label: 'View Detail', title: 'Open this captain\'s page with its state, settings, missions and log.', onClick: () => navigate(`/captains/${c.id}`) },
          ...(canCaptainStartPlanning(c) ? [{ label: 'Start Planning', title: 'Open a planning session with this captain to break work into missions before dispatching. Reserves the captain while the session runs.', onClick: () => handleStartPlanning(c) }] : []),
          { label: 'Edit', title: 'Change this captain\'s name, runtime, model, tier, permissions and instructions.', onClick: () => openEdit(c) },
          { label: 'Duplicate', title: 'Create a new idle captain with a copy of this one\'s name, runtime, model, instructions, personas and runtime options, then open it.', onClick: () => void handleDuplicate(c) },
          { label: 'View Tools', title: 'Show the MCP servers and tools this captain\'s runtime can reach, checked live where Armada can.', onClick: () => void handleViewTools(c) },
          ...(canEvaluate ? [{ label: 'Evaluate...', title: 'Evaluate this captain through its own runtime: Quick probes, or Full with the fixture battery and an evaluator captain\'s grading. Opens the captain\'s page to follow the run. Advisory only.', onClick: () => setEvaluateTarget(c) }] : []),
          { label: 'View JSON', title: 'Show this captain\'s full record as JSON, as the API returns it.', onClick: () => setJsonData({ open: true, title: `${t('Captain')}: ${c.name}`, data: c }) },
          { label: 'View Notifications', title: 'Open the inbox of items waiting on a person, such as reviews, failures and stalled captains.', onClick: () => navigate('/inbox') },
          { label: 'Stop', title: 'Kill the captain\'s process and return it to Idle. A mission it is running is marked Failed and its dock is reclaimed. You confirm first.', onClick: () => handleStop(c.id, c.name) },
          { label: 'Recall', title: 'Pull the captain off its current mission and return it to Idle. The mission is marked Failed. You confirm first.', onClick: () => handleRecall(c.id, c.name) },
          { label: 'Restart', title: 'Delete this captain and create a new one with the same name, runtime, model, instructions and personas. The new captain gets a new ID.', onClick: () => handleRestart(c.id, c.name) },
          { label: 'Delete', danger: true, title: 'Permanently remove this captain. You confirm first; this cannot be undone.', onClick: () => handleDelete(c.id, c.name) },
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
          <div className="captains-toolbar">
            <UserScopeFilter value={userScope} onChange={(id) => { setUserScope(id); setPageNumber(1); }} />
            {selected.length > 0 && (
              <button className="btn btn-sm btn-danger" onClick={handleBulkDelete} title={t('Permanently remove the selected captains. You confirm first; this cannot be undone.')}>
                {t('Delete Selected')} ({selected.length})
              </button>
            )}
            <button className="btn btn-sm btn-danger" onClick={handleStopAll} title={t('Stop every working captain now: kill its process, mark its mission Failed and return it to Idle. You confirm first.')}>{t('Stop All')}</button>
            <button className="btn btn-primary btn-sm" onClick={openCreate} title={t('Create a captain: an AI agent worker that picks up and runs missions.')}>+ {t('Captain')}</button>
          </div>
        )}
      />

      <ErrorModal error={error} onClose={() => setError('')} />

      {/* Create/Edit Modal */}
      {showForm && (
        <div className="modal-overlay" onClick={() => setShowForm(false)}>
          <form className={`modal modal-captain${isMuxRuntime(form.runtime) ? ' modal-mux' : ''}`} onClick={e => e.stopPropagation()} onSubmit={handleSubmit}>
            <h3>{editing ? t('Edit Captain') : t('Create Captain')}</h3>
            <label title={t(CAPTAIN_NAME_TOOLTIP)}>{t('Name')}<input value={form.name} onChange={e => setForm({ ...form, name: e.target.value })} title={t(CAPTAIN_NAME_TOOLTIP)} required /></label>
            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '0 1rem' }}>
              <label title={t(CAPTAIN_RUNTIME_TOOLTIP)}>{t('Runtime')}
                <select value={form.runtime} onChange={e => setForm({ ...form, runtime: e.target.value })} title={t(CAPTAIN_RUNTIME_TOOLTIP)} required>
                  <option value="">{t('Select runtime...')}</option>
                  <option value="ClaudeCode">Claude Code</option>
                  <option value="Codex">Codex</option>
                  <option value="Gemini">Gemini</option>
                  <option value="Cursor">Cursor</option>
                  <option value="Mux">Mux</option>
                  <option value="OpenCode">OpenCode</option>
                  <option value="Pi">pi</option>
                  <option value="ApiEndpoint">API Endpoint</option>
                </select>
              </label>
              <label title={t(captainModelTooltip(form.runtime))}>
                {t('Model')}
                <input value={form.model} onChange={e => setForm({ ...form, model: e.target.value })} title={t(captainModelTooltip(form.runtime))} aria-label={t('Model')} placeholder={t(captainModelPlaceholder(form.runtime))} />
              </label>
            </div>
            {form.runtime === 'ApiEndpoint' && (
              <label title={t(CAPTAIN_INFERENCE_ENDPOINT_TOOLTIP)}>
                {t('Inference Endpoint')}
                <select value={form.modelEndpointId} onChange={e => setForm({ ...form, modelEndpointId: e.target.value })} title={t(CAPTAIN_INFERENCE_ENDPOINT_TOOLTIP)} required>
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
              <label title={t(CAPTAIN_REASONING_EFFORT_TOOLTIP)}>
                {t('Reasoning effort')}
                <select value={form.reasoningEffort} onChange={e => setForm({ ...form, reasoningEffort: e.target.value })} title={t(CAPTAIN_REASONING_EFFORT_TOOLTIP)}>
                  <option value="">{t('Runtime default')}</option>
                  <option value="Off">{t('Off')}</option>
                  <option value="Minimal">{t('Minimal')}</option>
                  <option value="Low">{t('Low')}</option>
                  <option value="Medium">{t('Medium')}</option>
                  <option value="High">{t('High')}</option>
                </select>
              </label>
              <label title={t(CAPTAIN_TIER_TOOLTIP)}>
                {t('Capability tier')}
                <select value={form.tier} onChange={e => setForm({ ...form, tier: e.target.value })} title={t(CAPTAIN_TIER_TOOLTIP)}>
                  <option value="">{t('Auto (classify from model)')}</option>
                  <option value="Economy">{t('Economy')}</option>
                  <option value="Standard">{t('Standard')}</option>
                  <option value="Premium">{t('Premium')}</option>
                </select>
              </label>
            </div>
            {supportsAutoApproveSwitch(form.runtime) && (
              <label className="captain-auto-approve-field" title={t(CAPTAIN_AUTO_APPROVE_TOOLTIP)}>
                <input type="checkbox" checked={form.autoApprove} onChange={e => setForm({ ...form, autoApprove: e.target.checked })} title={t(CAPTAIN_AUTO_APPROVE_TOOLTIP)} />
                {' '}{t('Auto-approve agent tool use (runs the CLI with its permission-bypass flag)')}
              </label>
            )}
            <div className="captain-cli-policy-field">
              <label htmlFor="captain-cli-policy" title={t(CAPTAIN_CLI_POLICY_TOOLTIP)}>{t('CLI tool permissions')}</label>
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
            <label className="captain-instructions-field" title={t(CAPTAIN_SYSTEM_INSTRUCTIONS_TOOLTIP)}>
              {t('System Instructions')}
              <textarea value={form.systemInstructions} onChange={e => setForm({ ...form, systemInstructions: e.target.value })} title={t(CAPTAIN_SYSTEM_INSTRUCTIONS_TOOLTIP)} rows={4} placeholder={t('e.g., You are a testing specialist. Always run tests before committing...')} />
            </label>
            <div className="modal-actions">
              <button type="submit" className="btn btn-primary" disabled={saving} title={editing ? t('Save changes to this captain. They apply to its next agent launch, not to a mission it is already running.') : t('Create this captain. It starts Idle and can be picked for missions right away.')}>{saving ? t('Saving...') : t('Save')}</button>
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
        captainId={toolViewer.data?.captainId || undefined}
        loading={toolViewer.loading}
        error={toolViewer.error}
        data={toolViewer.data}
        onClose={() => setToolViewer({ open: false, captainName: '', loading: false, error: '', data: null })}
      />

      {/* Confirm Dialog */}
      <ConfirmDialog open={confirm.open} title={confirm.title} message={confirm.message}
        onConfirm={confirm.onConfirm} onCancel={() => setConfirm(c => ({ ...c, open: false }))} />
      {deletion.dialog}
      {evaluateTarget && (
        <ModelEvaluateDialog
          open
          captainId={evaluateTarget.id}
          submitting={evaluating}
          onClose={() => setEvaluateTarget(null)}
          onSubmit={(depth, options) => void handleEvaluate(evaluateTarget, depth, options)}
        />
      )}

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
