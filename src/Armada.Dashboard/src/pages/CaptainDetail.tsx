import { useEffect, useState, useCallback, useRef } from 'react';
import { useParams, useNavigate, Link } from 'react-router-dom';
import {
  apiErrorMessage,
  createCaptain,
  getCaptain,
  getCaptainTools,
  getCaptainLog,
  stopCaptain,
  unquarantineCaptain,
  recallCaptain,
  getMission,
  listMissionSummaries,
  updateCaptain,
  deleteCaptain,
  setCaptainCliPermissionPolicy,
  enumerateModelEndpoints,
} from '../api/client';
import { listAll } from '../api/paging';
import type { CliPermissionPolicy, ModelEndpoint } from '../types/models';
import type { Captain, Mission, MissionSummary, LogResult, FormattedLogEntry, CaptainToolAccessResult } from '../types/models';
import ActionMenu from '../components/shared/ActionMenu';
import MuxRuntimeFields from '../components/captains/MuxRuntimeFields';
import CaptainToolViewer from '../components/captains/CaptainToolViewer';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import ErrorModal from '../components/shared/ErrorModal';
import JsonViewer from '../components/shared/JsonViewer';
import PageHeader from '../components/shared/PageHeader';
import StatusBadge from '../components/shared/StatusBadge';
import CopyButton from '../components/shared/CopyButton';
import DataTable from '../components/shared/DataTable';
import { useLocale } from '../context/LocaleContext';
import { useNotifications } from '../context/NotificationContext';
import { useAuth } from '../context/AuthContext';
import CliPermissionPolicySelect from '../components/cliPermissions/CliPermissionPolicySelect';
import { policyLabel } from '../lib/cliPermissions';
import { EMPTY_MUX_CAPTAIN_FORM, MUX_APPROVAL_ASK_WARNING, isMuxRuntime, isUnsupportedMuxApprovalPolicy, parseMuxCaptainOptions, type MuxCaptainFormFields } from '../lib/mux';
import { supportsAutoApproveSwitch } from '../lib/captainApproval';
import { CAPTAIN_AUTO_APPROVE_TOOLTIP, CAPTAIN_CLI_POLICY_TOOLTIP, CAPTAIN_INFERENCE_ENDPOINT_TOOLTIP, CAPTAIN_NAME_TOOLTIP, CAPTAIN_REASONING_EFFORT_TOOLTIP, CAPTAIN_RUNTIME_TOOLTIP, CAPTAIN_SYSTEM_INSTRUCTIONS_TOOLTIP, CAPTAIN_TIER_TOOLTIP, buildCaptainPayload, captainDetailActions, captainFormError, captainFormErrorMessage, captainFormFromCaptain, captainModelPlaceholder, captainModelTooltip, cliPolicyChanged } from '../lib/captainForm';
import { buildCaptainDuplicatePayload } from '../lib/duplicates';
import ModelProbeEvaluate from '../components/ModelProbeEvaluate';
import { useDeleteProgress } from '../lib/useDeleteProgress';

const ALLOWED_PERSONAS_TOOLTIP = 'Personas this captain may take on, as a JSON array of persona names, for example ["Worker", "Judge"]. Blank lets it fill any persona.';
const PREFERRED_PERSONA_TOOLTIP = 'A persona name, for example Worker. When several captains could take a mission, dispatch prefers this captain for work of this persona. Blank means no preference.';
const LOG_READABLE_TOOLTIP = 'On: show the log as entries with tool calls named, secrets redacted and noise removed. Off: show the raw log text.';
const RUNTIMES = ['ClaudeCode', 'Codex', 'Gemini', 'Cursor', 'Mux', 'OpenCode', 'Pi', 'ApiEndpoint', 'Custom'];
type CaptainDetailFormState = {
  name: string;
  runtime: string;
  systemInstructions: string;
  model: string;
  modelEndpointId: string;
  reasoningEffort: string;
  tier: string;
  allowedPersonas: string;
  preferredPersona: string;
  autoApprove: boolean;
  /** CLI tool permission policy; null inherits. Saved through its own endpoint. */
  cliPermissionPolicy: CliPermissionPolicy | null;
} & MuxCaptainFormFields;

export default function CaptainDetail() {
  const { t, formatDateTime, formatRelativeTime } = useLocale();
  const { pushToast } = useNotifications();
  const deletion = useDeleteProgress();
  const { isAdmin, isTenantAdmin } = useAuth();
  const canManageCliPolicy = !!isAdmin || !!isTenantAdmin;
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const [captain, setCaptain] = useState<Captain | null>(null);
  const [currentMission, setCurrentMission] = useState<Mission | null>(null);
  const [missions, setMissions] = useState<MissionSummary[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const requestGenerationRef = useRef(0);
  const loadedCaptainIdRef = useRef<string | null>(null);

  // Edit
  const [showForm, setShowForm] = useState(false);
  const [inferenceEndpoints, setInferenceEndpoints] = useState<ModelEndpoint[]>([]);
  const [form, setForm] = useState<CaptainDetailFormState>({ name: '', runtime: 'ClaudeCode', systemInstructions: '', model: '', modelEndpointId: '', reasoningEffort: '', tier: '', allowedPersonas: '', preferredPersona: '', autoApprove: true, cliPermissionPolicy: null, ...EMPTY_MUX_CAPTAIN_FORM });

  // Log viewer
  const [logText, setLogText] = useState<string | null>(null);
  const [logEntries, setLogEntries] = useState<FormattedLogEntry[] | null>(null);
  const [logLoading, setLogLoading] = useState(false);
  const [showLog, setShowLog] = useState(false);
  const [logReadable, setLogReadable] = useState(true);
  const [logInfo, setLogInfo] = useState('');

  // JSON viewer
  const [jsonData, setJsonData] = useState<{ open: boolean; title: string; data: unknown }>({ open: false, title: '', data: null });
  const [toolViewer, setToolViewer] = useState<{ open: boolean; loading: boolean; error: string; data: CaptainToolAccessResult | null }>({
    open: false,
    loading: false,
    error: '',
    data: null,
  });

  // Confirm
  const [confirm, setConfirm] = useState<{ open: boolean; title: string; message: string; onConfirm: () => void }>({ open: false, title: '', message: '', onConfirm: () => {} });

  const load = useCallback(async () => {
    if (!id) return;
    const generation = ++requestGenerationRef.current;
    const routeId = id;
    const isNewRoute = loadedCaptainIdRef.current !== routeId;
    const current = () => generation === requestGenerationRef.current;
    if (isNewRoute) {
      setCaptain(null);
      setCurrentMission(null);
      setMissions([]);
      setError('');
    }
    setLoading(true);
    try {
      const cap = await getCaptain(routeId);
      if (!current()) return;
      setCaptain(cap);
      if (cap.currentMissionId) {
        try {
          const m = await getMission(cap.currentMissionId);
          if (!current()) return;
          setCurrentMission(m);
        } catch {
          if (!current()) return;
          setCurrentMission(null);
        }
      } else {
        setCurrentMission(null);
      }
      try {
        const mResult = await listMissionSummaries({ pageSize: 100, filters: { captainId: routeId } });
        if (!current()) return;
        setMissions(mResult.objects || []);
      } catch {
        if (!current()) return;
        setMissions([]);
      }
      loadedCaptainIdRef.current = routeId;
    } catch {
      if (!current()) return;
      setError(t('Failed to load captain.'));
    } finally {
      if (current()) setLoading(false);
    }
  }, [id, t]);

  useEffect(() => { load(); }, [load]);

  function openEdit() {
    if (!captain) return;
    // withEndpoint keeps an API-endpoint captain's inference endpoint (the form edits it below).
    setForm(captainFormFromCaptain(captain, { withEndpoint: true, withPersonas: true, defaultRuntime: 'ClaudeCode' }) as CaptainDetailFormState);
    setShowForm(true);
    // A picker needs every endpoint: read the pages explicitly (bounded by listAll's safety stop).
    listAll(enumerateModelEndpoints)
      .then(result => setInferenceEndpoints((result?.objects ?? []).filter(e => e.kind === 'Inference')))
      .catch(() => setInferenceEndpoints([]));
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!captain) return;
    try {
      const formError = captainFormError(form);
      if (formError) {
        setError(t(captainFormErrorMessage(formError)));
        return;
      }

      await updateCaptain(captain.id, buildCaptainPayload(form));
      if (cliPolicyChanged(captain, form)) {
        await setCaptainCliPermissionPolicy(captain.id, form.cliPermissionPolicy);
      }
      setShowForm(false);
      pushToast('success', t('Captain "{{name}}" saved.', { name: form.name }));
      load();
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : t('Save failed.'));
    }
  }

  async function handleViewLog(formatted = logReadable) {
    if (!id) return;
    setLogLoading(true);
    setShowLog(true);
    try {
      const result: LogResult = await getCaptainLog(id, 500, formatted);
      setLogText(result.log || t('(empty log)'));
      setLogEntries(formatted && result.entries ? result.entries : null);
      setLogInfo(t('({{lines}} of {{totalLines}} lines)', { lines: result.lines || 0, totalLines: result.totalLines || 0 }));
    } catch {
      setLogText(t('Failed to load log.'));
      setLogEntries(null);
      setLogInfo('');
    } finally {
      setLogLoading(false);
    }
  }

  function handleStop() {
    if (!captain) return;
    setConfirm({
      open: true,
      title: t('Stop Captain'),
      message: t('Stop captain "{{name}}"? This will halt the current mission.', { name: captain.name }),
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        try {
          await stopCaptain(captain.id);
          pushToast('warning', t('Captain "{{name}}" stopped.', { name: captain.name }));
          load();
        } catch { setError(t('Failed to stop captain.')); }
      },
    });
  }

  async function handleUnquarantine() {
    if (!captain) return;
    try {
      await unquarantineCaptain(captain.id);
      pushToast('success', t('Quarantine lifted for "{{name}}".', { name: captain.name }));
      load();
    } catch { setError(t('Failed to lift quarantine.')); }
  }

  function handleRecall() {
    if (!captain) return;
    setConfirm({
      open: true,
      title: t('Recall Captain'),
      message: t('Recall captain "{{name}}"? The captain will finish current work and return to idle.', { name: captain.name }),
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        try {
          await recallCaptain(captain.id);
          pushToast('warning', t('Captain "{{name}}" recalled.', { name: captain.name }));
          load();
        } catch { setError(t('Failed to recall captain.')); }
      },
    });
  }

  function handleRemove() {
    if (!captain) return;
    setConfirm({
      open: true,
      title: t('Remove Captain'),
      message: t('Remove captain "{{name}}"? This cannot be undone.', { name: captain.name }),
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        if (!(await deletion.run({ kind: 'captain', action: () => deleteCaptain(captain.id), errorMessage: (err) => apiErrorMessage(err, t('Remove failed.')) }))) return;
        pushToast('warning', t('Captain "{{name}}" removed.', { name: captain.name }));
        navigate('/captains');
      },
    });
  }

  async function handleDuplicate() {
    if (!captain) return;
    try {
      const created = await createCaptain(buildCaptainDuplicatePayload(captain));
      pushToast('success', t('Captain "{{name}}" duplicated.', { name: created.name }));
      navigate(`/captains/${created.id}`);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Duplicate failed.'));
    }
  }

  async function handleViewTools() {
    if (!captain) return;

    setToolViewer({
      open: true,
      loading: true,
      error: '',
      data: null,
    });

    try {
      // Probe once (refresh) for the summary and sources; the viewer then reads each tool section a page at a time.
      const result = await getCaptainTools(captain.id, { refresh: true, pageSize: 1 });
      setToolViewer({
        open: true,
        loading: false,
        error: '',
        data: result,
      });
    } catch {
      setToolViewer({
        open: true,
        loading: false,
        error: t('Failed to load captain tools.'),
        data: null,
      });
    }
  }

  function getActionItems() {
    if (!captain) return [];
    const items: { label: string; danger?: boolean; title?: string; onClick: () => void }[] = [
      { label: 'Edit', title: 'Change this captain\'s name, runtime, model, tier, permissions, personas and instructions.', onClick: openEdit },
      { label: 'Duplicate', title: 'Create a new idle captain with a copy of this one\'s name, runtime, model, instructions, personas and runtime options, then open it.', onClick: () => void handleDuplicate() },
      { label: 'View Tools', title: 'Show the MCP servers and tools this captain\'s runtime can reach, checked live where Armada can.', onClick: () => void handleViewTools() },
      { label: 'View Log', title: 'Show this captain\'s agent output log below the details.', onClick: handleViewLog },
      { label: 'View JSON', title: 'Show this captain\'s full record as JSON, as the API returns it.', onClick: () => setJsonData({ open: true, title: t('Captain: {{name}}', { name: captain.name }), data: captain }) },
    ];
    const lifecycle = captainDetailActions(captain.state);
    if (lifecycle.recall) items.push({ label: 'Recall Captain', title: 'Pull the captain off its current mission and return it to Idle. The mission is marked Failed. You confirm first.', onClick: handleRecall });
    if (lifecycle.stop) items.push({ label: 'Stop Captain', danger: true, title: 'Kill the captain\'s process (or end its planning session) and return it to Idle. A mission it is running is marked Failed. You confirm first.', onClick: handleStop });
    items.push({ label: 'Remove', danger: true, title: 'Permanently delete this captain. You confirm first; this cannot be undone.', onClick: handleRemove });
    return items;
  }

  if (loading) return <p className="text-dim">{t('Loading...')}</p>;
  if (error && !captain) return <ErrorModal error={error} onClose={() => setError('')} />;
  if (!captain) return <p className="text-dim">{t('Captain not found.')}</p>;

  const muxOptions = parseMuxCaptainOptions(captain.runtimeOptionsJson);

  return (
    <div>
      <PageHeader
        breadcrumb={
          <>
            <Link to="/captains">{t('Captains')}</Link> <span className="breadcrumb-sep">&gt;</span> <span>{captain.name}</span>
          </>
        }
        title={captain.name}
        actions={
          <>
            <ActionMenu id={`captain-${captain.id}`} items={getActionItems()} />
          </>
        }
      />

      <ErrorModal error={error} onClose={() => setError('')} />

      {/* Edit Modal */}
      {showForm && (
        <div className="modal-overlay" onClick={() => setShowForm(false)}>
          <form className="modal" onClick={e => e.stopPropagation()} onSubmit={handleSubmit}>
            <h3>{t('Edit Captain')}</h3>
            <label title={t(CAPTAIN_NAME_TOOLTIP)}>{t('Name')}<input value={form.name} onChange={e => setForm({ ...form, name: e.target.value })} title={t(CAPTAIN_NAME_TOOLTIP)} required /></label>
            <label title={t(CAPTAIN_RUNTIME_TOOLTIP)}>{t('Runtime')}
              <select value={form.runtime} onChange={e => setForm({ ...form, runtime: e.target.value })} title={t(CAPTAIN_RUNTIME_TOOLTIP)} required>
                {RUNTIMES.map(r => <option key={r} value={r}>{r}</option>)}
              </select>
            </label>
            <label title={t(CAPTAIN_SYSTEM_INSTRUCTIONS_TOOLTIP)}>
              {t('System Instructions')}
              <textarea value={form.systemInstructions} onChange={e => setForm({ ...form, systemInstructions: e.target.value })} title={t(CAPTAIN_SYSTEM_INSTRUCTIONS_TOOLTIP)} rows={4} placeholder={t('e.g., You are a testing specialist. Always run tests before committing...')} />
            </label>
            <label title={t(captainModelTooltip(form.runtime))}>
              {t('Model')}
              <input value={form.model} onChange={e => setForm({ ...form, model: e.target.value })} title={t(captainModelTooltip(form.runtime))} aria-label={t('Model')} placeholder={t(captainModelPlaceholder(form.runtime))} />
            </label>
            {form.runtime === 'ApiEndpoint' && (
              <label title={t(CAPTAIN_INFERENCE_ENDPOINT_TOOLTIP)}>
                {t('Inference Endpoint')}
                <select value={form.modelEndpointId} onChange={e => setForm({ ...form, modelEndpointId: e.target.value })} title={t(CAPTAIN_INFERENCE_ENDPOINT_TOOLTIP)} required>
                  <option value="">{t('Select an inference endpoint...')}</option>
                  {form.modelEndpointId && !inferenceEndpoints.some(ep => ep.id === form.modelEndpointId) && (
                    <option value={form.modelEndpointId}>{form.modelEndpointId}</option>
                  )}
                  {inferenceEndpoints.map(ep => (
                    <option key={ep.id} value={ep.id}>{ep.name} ({ep.provider}{ep.model ? ' / ' + ep.model : ''})</option>
                  ))}
                </select>
              </label>
            )}
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
              <span className="text-dim" style={{ fontSize: '0.72rem' }}>
                {t('Missions requiring a tier route to captains at or above it. Leave on Auto to classify from the model name.')}
              </span>
            </label>
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
            <label title={t(ALLOWED_PERSONAS_TOOLTIP)}>
              {t('Allowed Personas (JSON array)')}
              <textarea value={form.allowedPersonas} onChange={e => setForm({ ...form, allowedPersonas: e.target.value })} title={t(ALLOWED_PERSONAS_TOOLTIP)} rows={2} placeholder={t('["Worker", "Judge"]')} />
            </label>
            <label title={t(PREFERRED_PERSONA_TOOLTIP)}>
              {t('Preferred Persona')}
              <input value={form.preferredPersona} onChange={e => setForm({ ...form, preferredPersona: e.target.value })} title={t(PREFERRED_PERSONA_TOOLTIP)} placeholder={t('e.g., Worker')} />
            </label>
            <div className="modal-actions">
              <button type="submit" className="btn btn-primary" title={t('Save changes to this captain. They apply to its next agent launch, not to a mission it is already running.')}>{t('Save')}</button>
              <button type="button" className="btn" onClick={() => setShowForm(false)}>{t('Cancel')}</button>
            </div>
          </form>
        </div>
      )}

      <JsonViewer open={jsonData.open} title={jsonData.title} data={jsonData.data} onClose={() => setJsonData({ open: false, title: '', data: null })} />
      <CaptainToolViewer
        open={toolViewer.open}
        captainName={captain.name}
        captainId={toolViewer.data ? captain.id : undefined}
        loading={toolViewer.loading}
        error={toolViewer.error}
        data={toolViewer.data}
        onClose={() => setToolViewer({ open: false, loading: false, error: '', data: null })}
      />
      <ConfirmDialog open={confirm.open} title={confirm.title} message={confirm.message}
        onConfirm={confirm.onConfirm} onCancel={() => setConfirm(c => ({ ...c, open: false }))} />
      {deletion.dialog}

      {/* Captain Info */}
      <div className="detail-grid">
        <div className="detail-field">
          <span className="detail-label" title={t('Unique captain ID (cpt_ prefix), used by the API, CLI, MCP tools and logs.')}>{t('ID')}</span>
          <span className="id-display">
            <span className="mono">{captain.id}</span>
            <CopyButton text={captain.id} />
          </span>
        </div>
        <div className="detail-field"><span className="detail-label">{t('Name')}</span><span>{captain.name}</span></div>
        <div className="detail-field"><span className="detail-label" title={t('The tenant (organization) that owns this captain. A dash means it has no tenant.')}>{t('Tenant ID')}</span><span className="mono">{captain.tenantId || '-'}</span></div>
        <div className="detail-field"><span className="detail-label" title={t('The agent program this captain runs, such as Claude Code, Codex or Mux.')}>{t('Runtime')}</span><span>{captain.runtime || 'ClaudeCode'}</span></div>
        <div className="detail-field">
          <span className="detail-label" title={t(CAPTAIN_CLI_POLICY_TOOLTIP)}>{t('CLI tool permissions')}</span>
          <span data-testid="captain-cli-policy">{captain.cliPermissionPolicy ? policyLabel(t, captain.cliPermissionPolicy) : t('Inherit (auto-approve option, then server default)')}</span>
        </div>
      </div>
      {isMuxRuntime(captain.runtime) && (
        <div className="detail-grid">
          <div className="detail-field">
            <span className="detail-label" title={t('The saved mux endpoint this captain launches with (passed as --endpoint).')}>{t('Mux Endpoint')}</span>
            <span>{muxOptions?.endpoint || <span className="text-dim">{t('Not configured')}</span>}</span>
          </div>
          <div className="detail-field">
            <span className="detail-label" title={t('The mux config directory used at launch (--config-dir). Mux default means MUX_CONFIG_DIR, then ~/.mux.')}>{t('Mux Config Directory')}</span>
            <span className="mono">{muxOptions?.configDirectory || <span className="text-dim">{t('Mux default')}</span>}</span>
          </div>
          <div className="detail-field">
            <span className="detail-label" title={t('Adapter type override (--adapter-type). Endpoint default means the saved endpoint\'s adapter is used.')}>{t('Mux Adapter')}</span>
            <span>{muxOptions?.adapterType || <span className="text-dim">{t('Endpoint default')}</span>}</span>
          </div>
          <div className="detail-field">
            <span className="detail-label" title={t('Server URL override (--base-url). Endpoint default means the saved endpoint\'s URL is used.')}>{t('Mux Base URL')}</span>
            <span className="mono">{muxOptions?.baseUrl || <span className="text-dim">{t('Endpoint default')}</span>}</span>
          </div>
          <div className="detail-field">
            <span className="detail-label" title={t('How mux handles tool calls that need approval. auto and autoapprove run with --yolo (no prompts); deny refuses them. Default is auto, or deny when auto-approve is off.')}>{t('Mux Approval Policy')}</span>
            <span>
              {muxOptions?.approvalPolicy || <span className="text-dim">{t('Default (auto)')}</span>}
              {isUnsupportedMuxApprovalPolicy(muxOptions?.approvalPolicy) && (
                <span className="text-danger" style={{ display: 'block', fontSize: '0.75rem' }}>{t(MUX_APPROVAL_ASK_WARNING)}</span>
              )}
            </span>
          </div>
          <div className="detail-field">
            <span className="detail-label" title={t('System prompt file on the machine that runs mux, passed as --system-prompt. Mux default means mux uses its own prompt.')}>{t('Mux System Prompt Path')}</span>
            <span className="mono">{muxOptions?.systemPromptPath || <span className="text-dim">{t('Mux default')}</span>}</span>
          </div>
        </div>
      )}
      {captain.systemInstructions && (
        <div className="detail-context-section">
          <h4 title={t('Text added to every mission prompt this captain gets, before the vessel context and mission details.')}>{t('System Instructions')}</h4>
          <pre className="detail-context-block">{captain.systemInstructions}</pre>
        </div>
      )}
      <div className="detail-grid">
        <div className="detail-field">
          <span className="detail-label" title={t('Personas (roles such as Worker or Judge) this captain may take on. Any means it can fill every persona.')}>{t('Allowed Personas')}</span>
          <span>{captain.allowedPersonas || <span className="text-dim">{t('Any (no restriction)')}</span>}</span>
        </div>
        <div className="detail-field">
          <span className="detail-label" title={t('Model override passed to the runtime. Runtime default means the runtime picks its own model.')}>{t('Model')}</span>
          <span>{captain.model || <span className="text-dim">{t('Runtime default')}</span>}</span>
        </div>
        <div className="detail-field">
          <span className="detail-label" title={t('The persona dispatch prefers to give this captain when several captains could take the work. None means no preference.')}>{t('Preferred Persona')}</span>
          <span>{captain.preferredPersona || <span className="text-dim">{t('None')}</span>}</span>
        </div>
        <div className="detail-field">
          <span className="detail-label" title={t('Idle is free for work, Working is on a mission, Planning, Refining and Analyzing are reserved, Stalled has gone silent, Quarantined is skipped by dispatch for a while.')}>{t('State')}</span>
          <StatusBadge status={captain.state} />
        </div>
        {captain.state === 'Quarantined' && (
          <div className="detail-field">
            <span className="detail-label" title={t('Why the captain was quarantined (for example a usage limit, auth failure or crash loop) and when it ends. Dispatch skips it until then.')}>{t('Quarantine')}</span>
            <span style={{ display: 'flex', alignItems: 'center', gap: '0.6rem', flexWrap: 'wrap' }}>
              <span className="text-dim">
                {captain.quarantineReason || t('quarantined')}
                {captain.quarantineUntilUtc ? ` (${t('until')} ${formatDateTime(captain.quarantineUntilUtc)})` : ''}
              </span>
              <button type="button" className="btn btn-sm" onClick={handleUnquarantine} title={t('Lift the quarantine now so this captain can be assigned missions again before the quarantine expires.')}>{t('Lift Quarantine')}</button>
            </span>
          </div>
        )}
        <div className="detail-field">
          <span className="detail-label" title={t('The mission this captain is running now. A dash means it has no mission.')}>{t('Current Mission')}</span>
          {captain.currentMissionId ? (
            <span className="id-display">
              <Link className="mono" to={`/missions/${captain.currentMissionId}`}>{captain.currentMissionId}</Link>
              <CopyButton text={captain.currentMissionId!} />
            </span>
          ) : <span>-</span>}
        </div>
        <div className="detail-field">
          <span className="detail-label" title={t('The dock (git worktree) the captain works in for its current mission. A dash means none.')}>{t('Current Dock')}</span>
          {captain.currentDockId ? (
            <span className="id-display">
              <Link className="mono" to={`/docks/${captain.currentDockId}`}>{captain.currentDockId}</Link>
              <CopyButton text={captain.currentDockId!} />
            </span>
          ) : <span>-</span>}
        </div>
        <div className="detail-field"><span className="detail-label" title={t('Operating system process ID of the running agent, on the Admiral host or Harbor. A dash means no process is running.')}>{t('Process ID')}</span><span>{captain.processId || '-'}</span></div>
        <div className="detail-field"><span className="detail-label" title={t('How many times Armada has automatically tried to recover the agent on its current mission, for example after a crash.')}>{t('Recovery Attempts')}</span><span>{captain.recoveryAttempts ?? 0}</span></div>
        <div className="detail-field">
          <span className="detail-label" title={t('When the captain last produced agent output. If it stops advancing while the captain is Working, the captain may be stalled.')}>{t('Last Heartbeat')}</span>
          <span title={formatDateTime(captain.lastHeartbeatUtc)}>
            {formatRelativeTime(captain.lastHeartbeatUtc) || '-'}
            {captain.lastHeartbeatUtc && <span className="text-dim"> ({formatDateTime(captain.lastHeartbeatUtc)})</span>}
          </span>
        </div>
        <div className="detail-field">
          <span className="detail-label">{t('Created')}</span>
          <span title={captain.createdUtc}>
            {formatRelativeTime(captain.createdUtc)}
            <span className="text-dim"> ({formatDateTime(captain.createdUtc)})</span>
          </span>
        </div>
        <div className="detail-field">
          <span className="detail-label">{t('Last Updated')}</span>
          <span title={captain.lastUpdateUtc}>
            {formatRelativeTime(captain.lastUpdateUtc)}
            <span className="text-dim"> ({formatDateTime(captain.lastUpdateUtc)})</span>
          </span>
        </div>
      </div>

      {/* Evaluation (Quick mechanical probes) */}
      <div style={{ marginTop: '1rem' }}>
        <h3 title={t('Probe this captain through its own runtime: served context, output contract, ASCII and format, tool calling, speed and determinism.')}>{t('Evaluation')}</h3>
        <ModelProbeEvaluate target="captain" targetId={captain.id} onError={setError} />
      </div>

      {/* Current Mission Details */}
      {currentMission && (
        <div style={{ marginTop: '1rem' }}>
          <h3>{t('Current Mission')}</h3>
          <div className="card" style={{ padding: '1rem' }}>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 8 }}>
              <strong>{currentMission.title}</strong>
              <StatusBadge status={currentMission.status} />
            </div>
            {currentMission.description && <p className="text-dim" style={{ marginBottom: 8 }}>{currentMission.description}</p>}
            <div style={{ display: 'flex', gap: 16, fontSize: 13 }}>
              <span>{t('Branch')}: <span className="mono">{currentMission.branchName || '-'}</span></span>
              <span>{t('Priority')}: {currentMission.priority}</span>
            </div>
            <div style={{ marginTop: 8 }}>
              <Link to={`/missions/${currentMission.id}`} className="btn btn-sm" title={t('Open this mission\'s page with its instructions, diff, log and status.')}>{t('View Mission')}</Link>
            </div>
          </div>
        </div>
      )}

      {/* Log Viewer */}
      {showLog && (
        <div style={{ marginTop: '1rem' }}>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
            <h3>{t('Captain Log')} {logInfo && <span className="text-dim">{logInfo}</span>}</h3>
            <span style={{ display: 'flex', alignItems: 'center', gap: '0.6rem' }}>
              <label className="ask-stream-toggle" title={t(LOG_READABLE_TOOLTIP)}>
                <input type="checkbox" title={t(LOG_READABLE_TOOLTIP)} checked={logReadable} onChange={(e) => { setLogReadable(e.target.checked); void handleViewLog(e.target.checked); }} disabled={logLoading} />
                {t('Readable')}
              </label>
              <button className="btn" onClick={() => setShowLog(false)} title={t('Close the log panel. The log itself is kept.')}>{t('Hide Log')}</button>
            </span>
          </div>
          {logLoading ? (
            <p className="text-dim">{t('Loading log...')}</p>
          ) : logEntries && logEntries.length > 0 ? (
            <div style={{
              background: '#1a1a2e',
              color: '#e0e0e0',
              padding: 16,
              borderRadius: 'var(--radius)',
              overflow: 'auto',
              fontSize: 12,
              fontFamily: 'var(--mono)',
              maxHeight: '60vh',
            }}>
              {logEntries.map((entry, i) => (
                <div key={i} style={{ whiteSpace: 'pre-wrap', wordBreak: 'break-word', padding: '1px 0' }}>
                  {entry.isToolCall && entry.toolName ? (
                    <span className="tag" style={{ background: '#2a2a4a', color: '#9ad', marginRight: 6 }}>{t('tool')}: {entry.toolName}</span>
                  ) : null}
                  <span>{entry.text}</span>
                  {entry.redacted ? <span className="tag stalled" style={{ marginLeft: 6 }}>{t('redacted')}</span> : null}
                </div>
              ))}
            </div>
          ) : (
            <pre style={{
              background: '#1a1a2e',
              color: '#e0e0e0',
              padding: 16,
              borderRadius: 'var(--radius)',
              overflow: 'auto',
              fontSize: 12,
              fontFamily: 'var(--mono)',
              maxHeight: '60vh',
              whiteSpace: 'pre-wrap',
              wordBreak: 'break-word',
            }}>
              {logText}
            </pre>
          )}
        </div>
      )}

      {/* Recent Missions */}
      <div style={{ marginTop: '1rem' }}>
        <h3 title={t('Up to 100 missions this captain has been assigned, newest first. Click a row to open one.')}>{t('Recent Missions')}</h3>
        {missions.length > 0 ? (
          <DataTable
            tableKey="captain-detail-missions"
            rows={missions}
            rowKey={(m) => m.id}
            recordCount={null}
            onRowClick={(m) => navigate(`/missions/${m.id}`)}
            columns={[
              { key: 'actions', label: t('Actions'), fixed: true, interactive: true, render: m => <ActionMenu id={`captain-mission-${m.id}`} items={[
                { label: 'Open', onClick: () => navigate(`/missions/${m.id}`) },
                { label: 'Duplicate', onClick: () => navigate(`/missions?duplicate=${encodeURIComponent(m.id)}`) },
              ]} /> },
              {
                key: 'id', label: t('ID'), required: true, headerTitle: t('Unique mission ID (msn_ prefix). Use the copy button to copy it.'), cellClassName: 'mono text-dim table-id-cell',
                render: (m) => (
                  <span className="id-display">
                    <span className="id-value" title={m.id}>{m.id}</span>
                    <CopyButton text={m.id} onClick={(e) => e.stopPropagation()} />
                  </span>
                ),
              },
              {
                key: 'mission', label: t('Mission'), required: true, headerTitle: t('Mission title. Click a row to open the mission.'),
                render: (m) => <strong className="cell-one-line" title={m.title}>{m.title}</strong>,
              },
              { key: 'status', label: t('Status'), headerTitle: t('Where the mission is in its lifecycle, for example Pending, InProgress, Review, Complete or Failed.'), cellClassName: 'cell-nowrap', render: (m) => <StatusBadge status={m.status} /> },
              {
                key: 'branch', label: t('Branch'), headerTitle: t('The git branch the captain committed this mission\'s work to. A dash means no branch was created.'), cellClassName: 'mono text-dim',
                render: (m) => <span className="cell-one-line" title={m.branchName || undefined}>{m.branchName || '-'}</span>,
              },
              {
                key: 'date', label: t('Date'), headerTitle: t('When the mission finished, or when it was created if it has not finished. Hover a value for the exact time.'), cellClassName: 'text-dim cell-nowrap',
                cellTitle: (m) => formatDateTime(m.completedUtc || m.createdUtc),
                render: (m) => formatRelativeTime(m.completedUtc || m.createdUtc),
              },
            ]}
          />
        ) : (
          <p className="text-dim" style={{ marginTop: '0.5rem' }}>{t('No missions yet')}</p>
        )}
      </div>
    </div>
  );
}
