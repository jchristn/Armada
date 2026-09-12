import { useEffect, useMemo, useState } from 'react';
import { Link, useLocation, useNavigate, useParams, useSearchParams } from 'react-router-dom';
import {
  createRunbook,
  deleteRunbook,
  getRunbook,
  listEnvironments,
  listRunbookExecutions,
  listWorkflowProfiles,
  startRunbookExecution,
  updateRunbook,
  updateRunbookExecution,
} from '../api/client';
import type {
  CheckRunType,
  DeploymentEnvironment,
  Runbook,
  RunbookExecution,
  RunbookExecutionStartRequest,
  RunbookExecutionStatus,
  RunbookExecutionUpdateRequest,
  RunbookParameter,
  RunbookStep,
  RunbookUpsertRequest,
  WorkflowProfile,
} from '../types/models';
import { useAuth } from '../context/AuthContext';
import { useLocale } from '../context/LocaleContext';
import { useNotifications } from '../context/NotificationContext';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import PageHeader from '../components/shared/PageHeader';
import CopyButton from '../components/shared/CopyButton';
import ErrorModal from '../components/shared/ErrorModal';
import JsonViewer from '../components/shared/JsonViewer';
import StatusBadge from '../components/shared/StatusBadge';
import { canEdit as canEditScoped, type ScopeViewer } from '../lib/scoping';
import { buildRunbookDuplicatePayload } from '../lib/duplicates';

const RUNBOOK_EXECUTION_STATUSES: RunbookExecutionStatus[] = ['Running', 'Completed', 'Cancelled'];
const RUNBOOK_CHECK_TYPES: CheckRunType[] = [
  'Build',
  'UnitTest',
  'IntegrationTest',
  'E2ETest',
  'Migration',
  'SecurityScan',
  'Performance',
  'Deploy',
  'Rollback',
  'SmokeTest',
  'HealthCheck',
  'DeploymentVerification',
  'RollbackVerification',
  'Custom',
];

interface RunbookPageState {
  prefillExecution?: Partial<RunbookExecutionStartRequest>;
}

function createDefaultParameter(): RunbookParameter {
  return {
    name: 'parameter',
    label: '',
    description: '',
    defaultValue: '',
    required: false,
  };
}

function createDefaultStep(): RunbookStep {
  return {
    id: `rbs_${Date.now()}_${Math.random().toString(36).slice(2, 8)}`,
    title: 'Step',
    instructions: '',
  };
}

export default function RunbookDetail() {
  const { id } = useParams<{ id: string }>();
  const location = useLocation();
  const navigate = useNavigate();
  const [searchParams, setSearchParams] = useSearchParams();
  const { isAdmin, isTenantAdmin, user } = useAuth();
  const viewer: ScopeViewer = { isAdmin, isTenantAdmin, tenantId: user?.user?.tenantId, userId: user?.user?.id };
  const { t, formatDateTime, formatRelativeTime } = useLocale();
  const { pushToast } = useNotifications();

  const createMode = id === 'new';
  const pageState = (location.state as RunbookPageState | null) || null;

  const [runbook, setRunbook] = useState<Runbook | null>(null);
  const canManage = createMode ? true : (runbook ? canEditScoped(viewer, runbook) : (isAdmin || isTenantAdmin));
  const [profiles, setProfiles] = useState<WorkflowProfile[]>([]);
  const [environments, setEnvironments] = useState<DeploymentEnvironment[]>([]);
  const [executions, setExecutions] = useState<RunbookExecution[]>([]);
  const [fileName, setFileName] = useState('RUNBOOK.md');
  const [title, setTitle] = useState('Runbook');
  const [description, setDescription] = useState('');
  const [workflowProfileId, setWorkflowProfileId] = useState('');
  const [environmentId, setEnvironmentId] = useState('');
  const [environmentName, setEnvironmentName] = useState('');
  const [defaultCheckType, setDefaultCheckType] = useState<CheckRunType | ''>('');
  const [active, setActive] = useState(true);
  const [overviewMarkdown, setOverviewMarkdown] = useState('');
  const [parameters, setParameters] = useState<RunbookParameter[]>([]);
  const [steps, setSteps] = useState<RunbookStep[]>([]);
  const [showStartPanel, setShowStartPanel] = useState(false);
  const [executionTitle, setExecutionTitle] = useState('');
  const [executionWorkflowProfileId, setExecutionWorkflowProfileId] = useState('');
  const [executionEnvironmentId, setExecutionEnvironmentId] = useState('');
  const [executionEnvironmentName, setExecutionEnvironmentName] = useState('');
  const [executionCheckType, setExecutionCheckType] = useState<CheckRunType | ''>('');
  const [executionNotes, setExecutionNotes] = useState('');
  const [executionParameterValues, setExecutionParameterValues] = useState<Record<string, string>>({});
  const [selectedExecutionId, setSelectedExecutionId] = useState('');
  const [executionDraft, setExecutionDraft] = useState<RunbookExecution | null>(null);
  const [loading, setLoading] = useState(!createMode);
  const [saving, setSaving] = useState(false);
  const [startingExecution, setStartingExecution] = useState(false);
  const [savingExecution, setSavingExecution] = useState(false);
  const [error, setError] = useState('');
  const [jsonData, setJsonData] = useState<{ open: boolean; title: string; data: unknown }>({ open: false, title: '', data: null });
  const [confirm, setConfirm] = useState<{ open: boolean; title: string; message: string; onConfirm: () => void }>({
    open: false,
    title: '',
    message: '',
    onConfirm: () => {},
  });

  useEffect(() => {
    let cancelled = false;
    Promise.all([
      listWorkflowProfiles({ pageSize: 9999 }),
      listEnvironments({ pageSize: 9999 }),
    ]).then(([profileResult, environmentResult]) => {
      if (cancelled) return;
      setProfiles(profileResult.objects || []);
      setEnvironments(environmentResult.objects || []);
    }).catch((err: unknown) => {
      if (!cancelled) setError(err instanceof Error ? err.message : t('Failed to load runbook reference data.'));
    });

    return () => { cancelled = true; };
  }, [t]);

  useEffect(() => {
    if (createMode) {
      if (pageState?.prefillExecution) {
        setExecutionWorkflowProfileId(pageState.prefillExecution.workflowProfileId || '');
        setExecutionEnvironmentId(pageState.prefillExecution.environmentId || '');
        setExecutionEnvironmentName(pageState.prefillExecution.environmentName || '');
        setExecutionCheckType((pageState.prefillExecution.checkType as CheckRunType | undefined) || '');
        setExecutionNotes(pageState.prefillExecution.notes || '');
      }
      return;
    }

    if (!id) return;
    let mounted = true;
    const runbookId = id;

    async function loadRunbook() {
      try {
        setLoading(true);
        const [runbookResult, executionResult] = await Promise.all([
          getRunbook(runbookId),
          listRunbookExecutions({ runbookId, pageSize: 9999 }),
        ]);
        if (!mounted) return;
        setRunbook(runbookResult);
        setFileName(runbookResult.fileName);
        setTitle(runbookResult.title);
        setDescription(runbookResult.description || '');
        setWorkflowProfileId(runbookResult.workflowProfileId || '');
        setEnvironmentId(runbookResult.environmentId || '');
        setEnvironmentName(runbookResult.environmentName || '');
        setDefaultCheckType(runbookResult.defaultCheckType || '');
        setActive(runbookResult.active);
        setOverviewMarkdown(runbookResult.overviewMarkdown || '');
        setParameters(runbookResult.parameters || []);
        setSteps(runbookResult.steps || []);
        setExecutions(executionResult.objects || []);

        const requestedExecutionId = searchParams.get('executionId');
        const initialExecution = (executionResult.objects || []).find((execution) => execution.id === requestedExecutionId)
          || (executionResult.objects || [])[0]
          || null;
        setSelectedExecutionId(initialExecution?.id || '');
        setExecutionDraft(initialExecution ? cloneExecution(initialExecution) : null);
        setError('');
      } catch (err: unknown) {
        if (mounted) setError(err instanceof Error ? err.message : t('Failed to load runbook.'));
      } finally {
        if (mounted) setLoading(false);
      }
    }

    void loadRunbook();
    return () => { mounted = false; };
  }, [createMode, id, pageState?.prefillExecution, searchParams, t]);

  useEffect(() => {
    if (!executionEnvironmentId) return;
    const selected = environments.find((environment) => environment.id === executionEnvironmentId);
    if (selected) {
      setExecutionEnvironmentName(selected.name);
    }
  }, [environments, executionEnvironmentId]);

  useEffect(() => {
    if (!environmentId) return;
    const selected = environments.find((environment) => environment.id === environmentId);
    if (selected) {
      setEnvironmentName(selected.name);
    }
  }, [environmentId, environments]);

  useEffect(() => {
    const nextExecution = executions.find((execution) => execution.id === selectedExecutionId) || null;
    setExecutionDraft(nextExecution ? cloneExecution(nextExecution) : null);
    if (selectedExecutionId) {
      setSearchParams((current) => {
        current.set('executionId', selectedExecutionId);
        return current;
      }, { replace: true });
    }
  }, [executions, selectedExecutionId, setSearchParams]);

  const environmentMap = useMemo(() => new Map(environments.map((environment) => [environment.id, environment])), [environments]);
  const selectedEnvironment = useMemo(
    () => (executionDraft?.environmentId ? environmentMap.get(executionDraft.environmentId) : null) || (environmentId ? environmentMap.get(environmentId) : null) || null,
    [environmentId, environmentMap, executionDraft?.environmentId],
  );

  function buildPayload(): RunbookUpsertRequest {
    return {
      fileName: fileName.trim() || null,
      title: title.trim() || null,
      description: description.trim() || null,
      workflowProfileId: workflowProfileId || null,
      environmentId: environmentId || null,
      environmentName: environmentName.trim() || null,
      defaultCheckType: defaultCheckType || null,
      parameters,
      steps,
      overviewMarkdown,
      active,
    };
  }

  async function handleSave() {
    if (!canManage) return;
    try {
      setSaving(true);
      const payload = buildPayload();
      if (createMode) {
        const created = await createRunbook(payload);
        pushToast('success', t('Runbook "{{title}}" created.', { title: created.title }));
        navigate(`/runbooks/${created.id}`, { state: pageState });
        return;
      }

      if (!id) return;
      const updated = await updateRunbook(id, payload);
      setRunbook(updated);
      pushToast('success', t('Runbook "{{title}}" saved.', { title: updated.title }));
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Save failed.'));
    } finally {
      setSaving(false);
    }
  }

  function handleDelete() {
    if (!runbook || !canManage) return;
    setConfirm({
      open: true,
      title: t('Delete Runbook'),
      message: t('Delete "{{title}}"? This removes the runbook definition and keeps existing executions only in the event log history.', { title: runbook.title }),
      onConfirm: async () => {
        setConfirm((current) => ({ ...current, open: false }));
        try {
          await deleteRunbook(runbook.id);
          pushToast('warning', t('Runbook "{{title}}" deleted.', { title: runbook.title }));
          navigate('/runbooks');
        } catch (err: unknown) {
          setError(err instanceof Error ? err.message : t('Delete failed.'));
        }
      },
    });
  }

  async function handleDuplicate() {
    if (!runbook || !canManage) return;
    try {
      setSaving(true);
      const created = await createRunbook(buildRunbookDuplicatePayload(runbook));
      pushToast('success', t('Runbook "{{title}}" duplicated.', { title: created.title }));
      navigate(`/runbooks/${created.id}`, { state: pageState });
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Duplicate failed.'));
    } finally {
      setSaving(false);
    }
  }

  function resetExecutionForm() {
    setExecutionTitle('');
    setExecutionWorkflowProfileId(pageState?.prefillExecution?.workflowProfileId || workflowProfileId);
    setExecutionEnvironmentId(pageState?.prefillExecution?.environmentId || environmentId);
    setExecutionEnvironmentName(pageState?.prefillExecution?.environmentName || environmentName);
    setExecutionCheckType((pageState?.prefillExecution?.checkType as CheckRunType | undefined) || defaultCheckType || '');
    setExecutionNotes(pageState?.prefillExecution?.notes || '');
    const nextValues: Record<string, string> = {};
    for (const parameter of parameters) {
      nextValues[parameter.name] = pageState?.prefillExecution?.parameterValues?.[parameter.name]
        || parameter.defaultValue
        || '';
    }
    setExecutionParameterValues(nextValues);
  }

  async function handleStartExecution() {
    if (!runbook) return;
    try {
      setStartingExecution(true);
      const payload: RunbookExecutionStartRequest = {
        title: executionTitle.trim() || null,
        workflowProfileId: executionWorkflowProfileId || null,
        environmentId: executionEnvironmentId || null,
        environmentName: executionEnvironmentName.trim() || null,
        checkType: executionCheckType || null,
        parameterValues: executionParameterValues,
        deploymentId: pageState?.prefillExecution?.deploymentId || null,
        incidentId: pageState?.prefillExecution?.incidentId || null,
        notes: executionNotes.trim() || null,
      };
      const execution = await startRunbookExecution(runbook.id, payload);
      const refreshed = await listRunbookExecutions({ runbookId: runbook.id, pageSize: 9999 });
      setExecutions(refreshed.objects || []);
      setSelectedExecutionId(execution.id);
      setShowStartPanel(false);
      pushToast('success', t('Execution "{{title}}" started.', { title: execution.title }));
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Failed to start execution.'));
    } finally {
      setStartingExecution(false);
    }
  }

  async function handleSaveExecution(statusOverride?: RunbookExecutionStatus) {
    if (!executionDraft) return;
    try {
      setSavingExecution(true);
      const payload: RunbookExecutionUpdateRequest = {
        status: statusOverride || executionDraft.status,
        completedStepIds: executionDraft.completedStepIds,
        stepNotes: executionDraft.stepNotes,
        notes: executionDraft.notes || null,
      };
      const updated = await updateRunbookExecution(executionDraft.id, payload);
      const refreshed = executions.map((execution) => execution.id === updated.id ? updated : execution);
      setExecutions(refreshed);
      setExecutionDraft(cloneExecution(updated));
      pushToast('success', t('Execution "{{title}}" updated.', { title: updated.title }));
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Failed to update execution.'));
    } finally {
      setSavingExecution(false);
    }
  }

  function updateParameter(index: number, field: keyof RunbookParameter, value: string | boolean) {
    setParameters((current) => current.map((parameter, parameterIndex) => {
      if (parameterIndex !== index) return parameter;
      return {
        ...parameter,
        [field]: value,
      };
    }));
  }

  function updateStep(index: number, field: keyof RunbookStep, value: string) {
    setSteps((current) => current.map((step, stepIndex) => {
      if (stepIndex !== index) return step;
      return {
        ...step,
        [field]: value,
      };
    }));
  }

  function toggleExecutionStep(stepId: string, checked: boolean) {
    setExecutionDraft((current) => {
      if (!current) return current;
      const nextCompleted = checked
        ? Array.from(new Set([...current.completedStepIds, stepId]))
        : current.completedStepIds.filter((value) => value !== stepId);
      return {
        ...current,
        completedStepIds: nextCompleted,
      };
    });
  }

  function updateExecutionStepNote(stepId: string, note: string) {
    setExecutionDraft((current) => {
      if (!current) return current;
      return {
        ...current,
        stepNotes: {
          ...current.stepNotes,
          [stepId]: note,
        },
      };
    });
  }

  function canLaunchCheck() {
    if (!executionDraft && !defaultCheckType) return false;
    return !!(selectedEnvironment?.vesselId && (executionDraft?.checkType || defaultCheckType));
  }

  function handleLaunchCheck() {
    if (!selectedEnvironment?.vesselId) return;
    const linkedExecution = executionDraft;
    const effectiveCheckType = linkedExecution?.checkType || defaultCheckType;
    if (!effectiveCheckType) return;

    navigate('/checks', {
      state: {
        prefill: {
          vesselId: selectedEnvironment.vesselId,
          workflowProfileId: linkedExecution?.workflowProfileId || workflowProfileId || null,
          deploymentId: linkedExecution?.deploymentId || pageState?.prefillExecution?.deploymentId || null,
          type: effectiveCheckType,
          environmentName: linkedExecution?.environmentName || environmentName || selectedEnvironment.name,
          label: linkedExecution?.title || title,
        },
      },
    });
  }

  if (loading) return <p className="text-dim">{t('Loading...')}</p>;

  return (
    <div>
      <PageHeader
        breadcrumb={
          <>
            <Link to="/runbooks">{t('Runbooks')}</Link> <span className="breadcrumb-sep">&gt;</span> <span>{createMode ? t('New Runbook') : title}</span>
          </>
        }
        title={createMode ? t('Create Runbook') : title}
        actions={
          <>
            {!createMode && runbook && <StatusBadge status={runbook.active ? 'Active' : 'Inactive'} />}
            {!createMode && (
              <button className="btn btn-sm" onClick={() => { resetExecutionForm(); setShowStartPanel((current) => !current); }}>
                {showStartPanel ? t('Hide Start Panel') : t('Start Execution')}
              </button>
            )}
            {!createMode && executionDraft && (
              <>
                <button className="btn btn-sm" onClick={() => void handleSaveExecution()}>{t('Save Execution')}</button>
                <button className="btn btn-sm" onClick={() => void handleSaveExecution('Completed')}>{t('Mark Completed')}</button>
                <button className="btn btn-sm" onClick={() => void handleSaveExecution('Cancelled')}>{t('Cancel Execution')}</button>
              </>
            )}
            {!createMode && canLaunchCheck() && (
              <button className="btn btn-sm" onClick={handleLaunchCheck}>{t('Run Check')}</button>
            )}
            {!createMode && runbook && (
              <button className="btn btn-sm" onClick={() => setJsonData({ open: true, title, data: runbook })}>{t('View JSON')}</button>
            )}
            {!createMode && canManage && (
              <button className="btn btn-sm" disabled={saving} onClick={() => void handleDuplicate()}>{t('Duplicate')}</button>
            )}
            {!createMode && canManage && (
              <button className="btn btn-sm btn-danger" onClick={handleDelete}>{t('Delete')}</button>
            )}
          </>
        }
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

      {showStartPanel && !createMode && runbook && (
        <section className="card detail-panel" style={{ marginBottom: '1rem' }}>
          <h3>{t('Start Execution')}</h3>
          <div className="detail-form-grid">
            <div className="detail-field">
              <span className="detail-label">{t('Title')}</span>
              <input value={executionTitle} onChange={(event) => setExecutionTitle(event.target.value)} placeholder={runbook.title} />
            </div>
            <div className="detail-field">
              <span className="detail-label">{t('Workflow Profile')}</span>
              <select value={executionWorkflowProfileId} onChange={(event) => setExecutionWorkflowProfileId(event.target.value)}>
                <option value="">{t('Use runbook binding')}</option>
                {profiles.map((profile) => (
                  <option key={profile.id} value={profile.id}>{profile.name}</option>
                ))}
              </select>
            </div>
            <div className="detail-field">
              <span className="detail-label">{t('Environment')}</span>
              <select value={executionEnvironmentId} onChange={(event) => setExecutionEnvironmentId(event.target.value)}>
                <option value="">{t('Use runbook binding')}</option>
                {environments.map((environment) => (
                  <option key={environment.id} value={environment.id}>{environment.name}</option>
                ))}
              </select>
            </div>
            <div className="detail-field">
              <span className="detail-label">{t('Environment Name')}</span>
              <input value={executionEnvironmentName} onChange={(event) => setExecutionEnvironmentName(event.target.value)} />
            </div>
            <div className="detail-field">
              <span className="detail-label">{t('Check Type')}</span>
              <select value={executionCheckType} onChange={(event) => setExecutionCheckType(event.target.value as CheckRunType | '')}>
                <option value="">{t('No default check')}</option>
                {RUNBOOK_CHECK_TYPES.map((checkType) => (
                  <option key={checkType} value={checkType}>{checkType}</option>
                ))}
              </select>
            </div>
            <div className="detail-field detail-field-full">
              <span className="detail-label">{t('Execution Notes')}</span>
              <textarea rows={3} value={executionNotes} onChange={(event) => setExecutionNotes(event.target.value)} />
            </div>
          </div>

          {parameters.length > 0 && (
            <>
              <div className="detail-divider" />
              <h4>{t('Parameters')}</h4>
              <div style={{ display: 'grid', gap: '0.85rem' }}>
                {parameters.map((parameter) => (
                  <div key={parameter.name} className="detail-field">
                    <span className="detail-label">{parameter.label || parameter.name}{parameter.required ? ' *' : ''}</span>
                    <input
                      value={executionParameterValues[parameter.name] || ''}
                      onChange={(event) => setExecutionParameterValues((current) => ({ ...current, [parameter.name]: event.target.value }))}
                      placeholder={parameter.description || parameter.defaultValue || ''}
                    />
                  </div>
                ))}
              </div>
            </>
          )}

          <div className="inline-actions" style={{ marginTop: '1rem' }}>
            <button className="btn btn-primary" disabled={startingExecution} onClick={() => void handleStartExecution()}>
              {startingExecution ? t('Starting...') : t('Start Execution')}
            </button>
          </div>
        </section>
      )}

      <div className="detail-grid">
        <section className="card detail-panel">
          <h3>{t('Runbook')}</h3>
          <div className="detail-form-grid">
            <div className="detail-field">
              <span className="detail-label">{t('File Name')}</span>
              <input value={fileName} onChange={(event) => setFileName(event.target.value)} disabled={!canManage} />
            </div>
            <div className="detail-field">
              <span className="detail-label">{t('Title')}</span>
              <input value={title} onChange={(event) => setTitle(event.target.value)} disabled={!canManage} />
            </div>
            <div className="detail-field detail-field-full">
              <span className="detail-label">{t('Description')}</span>
              <textarea rows={2} value={description} onChange={(event) => setDescription(event.target.value)} disabled={!canManage} />
            </div>
            <div className="detail-field">
              <span className="detail-label">{t('Workflow Profile')}</span>
              <select value={workflowProfileId} onChange={(event) => setWorkflowProfileId(event.target.value)} disabled={!canManage}>
                <option value="">{t('No workflow profile')}</option>
                {profiles.map((profile) => (
                  <option key={profile.id} value={profile.id}>{profile.name}</option>
                ))}
              </select>
            </div>
            <div className="detail-field">
              <span className="detail-label">{t('Environment')}</span>
              <select value={environmentId} onChange={(event) => setEnvironmentId(event.target.value)} disabled={!canManage}>
                <option value="">{t('No environment')}</option>
                {environments.map((environment) => (
                  <option key={environment.id} value={environment.id}>{environment.name}</option>
                ))}
              </select>
            </div>
            <div className="detail-field">
              <span className="detail-label">{t('Environment Name')}</span>
              <input value={environmentName} onChange={(event) => setEnvironmentName(event.target.value)} disabled={!canManage} />
            </div>
            <div className="detail-field">
              <span className="detail-label">{t('Default Check Type')}</span>
              <select value={defaultCheckType} onChange={(event) => setDefaultCheckType(event.target.value as CheckRunType | '')} disabled={!canManage}>
                <option value="">{t('No default check')}</option>
                {RUNBOOK_CHECK_TYPES.map((checkType) => (
                  <option key={checkType} value={checkType}>{checkType}</option>
                ))}
              </select>
            </div>
            <div className="detail-field detail-field-full">
              <span className="detail-label">{t('Overview Markdown')}</span>
              <textarea rows={10} value={overviewMarkdown} onChange={(event) => setOverviewMarkdown(event.target.value)} disabled={!canManage} />
            </div>
          </div>

          <label style={{ display: 'inline-flex', alignItems: 'center', gap: '0.45rem', marginTop: '1rem' }}>
            <input type="checkbox" checked={active} onChange={(event) => setActive(event.target.checked)} disabled={!canManage} />
            <span>{t('Active')}</span>
          </label>

          {canManage && (
            <div className="inline-actions" style={{ marginTop: '1rem' }}>
              <button className="btn btn-primary" disabled={saving} onClick={() => void handleSave()}>
                {saving ? t('Saving...') : createMode ? t('Create Runbook') : t('Save Runbook')}
              </button>
            </div>
          )}
        </section>

        <section className="card detail-panel">
          <h3>{t('Overview')}</h3>
          <div className="detail-meta-grid">
            <div className="detail-field"><span className="detail-label">{t('Workflow Profile')}</span><span>{workflowProfileId || '-'}</span></div>
            <div className="detail-field"><span className="detail-label">{t('Environment')}</span><span>{environmentId || environmentName || '-'}</span></div>
            <div className="detail-field"><span className="detail-label">{t('Default Check')}</span><span>{defaultCheckType || '-'}</span></div>
            <div className="detail-field"><span className="detail-label">{t('Parameters')}</span><span>{parameters.length}</span></div>
            <div className="detail-field"><span className="detail-label">{t('Steps')}</span><span>{steps.length}</span></div>
            <div className="detail-field"><span className="detail-label">{t('Executions')}</span><span>{executions.length}</span></div>
            {!createMode && runbook && (
              <>
                <div className="detail-field"><span className="detail-label">{t('Created')}</span><span>{formatDateTime(runbook.createdUtc)}</span></div>
                <div className="detail-field"><span className="detail-label">{t('Last Updated')}</span><span>{formatRelativeTime(runbook.lastUpdateUtc)}</span></div>
              </>
            )}
          </div>

          {!createMode && runbook && (
            <>
              <div className="detail-divider" />
              <h4>{t('Identifiers')}</h4>
              <div className="detail-key-list">
                <div className="detail-key-row">
                  <span>{t('Runbook ID')}</span>
                  <div className="detail-key-actions">
                    <code>{runbook.id}</code>
                    <CopyButton text={runbook.id} title={t('Copy runbook ID')} />
                  </div>
                </div>
                <div className="detail-key-row">
                  <span>{t('Playbook ID')}</span>
                  <div className="detail-key-actions">
                    <code>{runbook.playbookId}</code>
                    <CopyButton text={runbook.playbookId} title={t('Copy playbook ID')} />
                  </div>
                </div>
              </div>

              <div className="detail-divider" />
              <h4>{t('Executions')}</h4>
              {executions.length === 0 ? (
                <p className="text-dim">{t('No executions recorded yet.')}</p>
              ) : (
                <div style={{ display: 'grid', gap: '0.65rem' }}>
                  {executions.map((execution) => (
                    <button
                      key={execution.id}
                      className={`card${selectedExecutionId === execution.id ? ' selected-card' : ''}`}
                      style={{ padding: '0.85rem', textAlign: 'left', border: selectedExecutionId === execution.id ? '1px solid var(--accent-color)' : undefined }}
                      onClick={() => setSelectedExecutionId(execution.id)}
                    >
                      <div style={{ display: 'flex', justifyContent: 'space-between', gap: '0.75rem', alignItems: 'center' }}>
                        <strong>{execution.title}</strong>
                        <StatusBadge status={execution.status} />
                      </div>
                      <div className="text-dim" style={{ marginTop: '0.3rem' }}>
                        {execution.environmentName || t('No environment')} • {execution.checkType || t('No check type')} • {execution.completedStepIds.length}/{steps.length} {t('steps')}
                      </div>
                      <div className="mono text-dim" style={{ fontSize: '0.78rem', marginTop: '0.2rem' }}>{execution.id}</div>
                    </button>
                  ))}
                </div>
              )}
            </>
          )}
        </section>
      </div>

      <div className="detail-grid" style={{ marginTop: '1rem' }}>
        <section className="card detail-panel">
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: '0.75rem' }}>
            <h3>{t('Parameters')}</h3>
            {canManage && (
              <button className="btn btn-sm" onClick={() => setParameters((current) => [...current, createDefaultParameter()])}>{t('Add Parameter')}</button>
            )}
          </div>
          {parameters.length === 0 ? (
            <p className="text-dim">{t('No parameters defined.')}</p>
          ) : (
            <div style={{ display: 'grid', gap: '0.85rem' }}>
              {parameters.map((parameter, index) => (
                <div key={`${parameter.name}-${index}`} className="card" style={{ padding: '0.85rem' }}>
                  <div className="detail-form-grid">
                    <div className="detail-field">
                      <span className="detail-label">{t('Name')}</span>
                      <input value={parameter.name} onChange={(event) => updateParameter(index, 'name', event.target.value)} disabled={!canManage} />
                    </div>
                    <div className="detail-field">
                      <span className="detail-label">{t('Label')}</span>
                      <input value={parameter.label || ''} onChange={(event) => updateParameter(index, 'label', event.target.value)} disabled={!canManage} />
                    </div>
                    <div className="detail-field">
                      <span className="detail-label">{t('Default Value')}</span>
                      <input value={parameter.defaultValue || ''} onChange={(event) => updateParameter(index, 'defaultValue', event.target.value)} disabled={!canManage} />
                    </div>
                    <div className="detail-field detail-field-full">
                      <span className="detail-label">{t('Description')}</span>
                      <textarea rows={2} value={parameter.description || ''} onChange={(event) => updateParameter(index, 'description', event.target.value)} disabled={!canManage} />
                    </div>
                  </div>
                  <div className="inline-actions" style={{ marginTop: '0.75rem' }}>
                    <label style={{ display: 'inline-flex', alignItems: 'center', gap: '0.45rem' }}>
                      <input type="checkbox" checked={parameter.required} onChange={(event) => updateParameter(index, 'required', event.target.checked)} disabled={!canManage} />
                      <span>{t('Required')}</span>
                    </label>
                    {canManage && (
                      <button className="btn btn-sm btn-danger" onClick={() => setParameters((current) => current.filter((_, parameterIndex) => parameterIndex !== index))}>{t('Remove')}</button>
                    )}
                  </div>
                </div>
              ))}
            </div>
          )}
        </section>

        <section className="card detail-panel">
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: '0.75rem' }}>
            <h3>{t('Steps')}</h3>
            {canManage && (
              <button className="btn btn-sm" onClick={() => setSteps((current) => [...current, createDefaultStep()])}>{t('Add Step')}</button>
            )}
          </div>
          {steps.length === 0 ? (
            <p className="text-dim">{t('No steps defined yet.')}</p>
          ) : (
            <div style={{ display: 'grid', gap: '0.85rem' }}>
              {steps.map((step, index) => (
                <div key={step.id} className="card" style={{ padding: '0.85rem' }}>
                  <div className="detail-form-grid">
                    <div className="detail-field">
                      <span className="detail-label">{t('Title')}</span>
                      <input value={step.title} onChange={(event) => updateStep(index, 'title', event.target.value)} disabled={!canManage} />
                    </div>
                    <div className="detail-field detail-field-full">
                      <span className="detail-label">{t('Instructions')}</span>
                      <textarea rows={4} value={step.instructions} onChange={(event) => updateStep(index, 'instructions', event.target.value)} disabled={!canManage} />
                    </div>
                  </div>
                  {canManage && (
                    <div className="inline-actions" style={{ marginTop: '0.75rem' }}>
                      <button className="btn btn-sm btn-danger" onClick={() => setSteps((current) => current.filter((_, stepIndex) => stepIndex !== index))}>{t('Remove')}</button>
                    </div>
                  )}
                </div>
              ))}
            </div>
          )}
        </section>
      </div>

      {!createMode && executionDraft && (
        <section className="card detail-panel" style={{ marginTop: '1rem' }}>
          <h3>{t('Execution Progress')}</h3>
          <div className="detail-meta-grid">
            <div className="detail-field"><span className="detail-label">{t('Status')}</span><span><StatusBadge status={executionDraft.status} /></span></div>
            <div className="detail-field"><span className="detail-label">{t('Environment')}</span><span>{executionDraft.environmentName || '-'}</span></div>
            <div className="detail-field"><span className="detail-label">{t('Check Type')}</span><span>{executionDraft.checkType || '-'}</span></div>
            <div className="detail-field"><span className="detail-label">{t('Deployment')}</span><span>{executionDraft.deploymentId ? <Link to={`/deployments/${executionDraft.deploymentId}`}>{executionDraft.deploymentId}</Link> : '-'}</span></div>
            <div className="detail-field"><span className="detail-label">{t('Incident')}</span><span>{executionDraft.incidentId ? <Link to={`/incidents/${executionDraft.incidentId}`}>{executionDraft.incidentId}</Link> : '-'}</span></div>
            <div className="detail-field"><span className="detail-label">{t('Started')}</span><span>{formatDateTime(executionDraft.startedUtc)}</span></div>
            <div className="detail-field"><span className="detail-label">{t('Completed')}</span><span>{executionDraft.completedUtc ? formatDateTime(executionDraft.completedUtc) : '-'}</span></div>
            <div className="detail-field"><span className="detail-label">{t('Last Updated')}</span><span>{formatRelativeTime(executionDraft.lastUpdateUtc)}</span></div>
          </div>

          <div className="detail-field detail-field-full" style={{ marginTop: '1rem' }}>
            <span className="detail-label">{t('Execution Notes')}</span>
            <textarea
              rows={3}
              value={executionDraft.notes || ''}
              onChange={(event) => setExecutionDraft((current) => current ? { ...current, notes: event.target.value } : current)}
            />
          </div>

          <div style={{ display: 'grid', gap: '0.85rem', marginTop: '1rem' }}>
            {steps.map((step) => (
              <div key={step.id} className="card" style={{ padding: '0.85rem' }}>
                <label style={{ display: 'flex', gap: '0.65rem', alignItems: 'flex-start' }}>
                  <input
                    type="checkbox"
                    checked={executionDraft.completedStepIds.includes(step.id)}
                    onChange={(event) => toggleExecutionStep(step.id, event.target.checked)}
                  />
                  <div style={{ flex: 1 }}>
                    <strong>{step.title}</strong>
                    <div className="text-dim" style={{ marginTop: '0.25rem', whiteSpace: 'pre-wrap' }}>{step.instructions}</div>
                  </div>
                </label>
                <div className="detail-field detail-field-full" style={{ marginTop: '0.75rem' }}>
                  <span className="detail-label">{t('Step Notes')}</span>
                  <textarea
                    rows={2}
                    value={executionDraft.stepNotes[step.id] || ''}
                    onChange={(event) => updateExecutionStepNote(step.id, event.target.value)}
                  />
                </div>
              </div>
            ))}
          </div>

          <div className="inline-actions" style={{ marginTop: '1rem' }}>
            <button className="btn btn-primary" disabled={savingExecution} onClick={() => void handleSaveExecution()}>
              {savingExecution ? t('Saving...') : t('Save Progress')}
            </button>
            <button className="btn" disabled={savingExecution} onClick={() => void handleSaveExecution('Completed')}>{t('Mark Completed')}</button>
            <button className="btn" disabled={savingExecution} onClick={() => void handleSaveExecution('Cancelled')}>{t('Cancel Execution')}</button>
          </div>
        </section>
      )}
    </div>
  );
}

function cloneExecution(execution: RunbookExecution): RunbookExecution {
  return {
    ...execution,
    parameterValues: { ...execution.parameterValues },
    completedStepIds: [...execution.completedStepIds],
    stepNotes: { ...execution.stepNotes },
  };
}
