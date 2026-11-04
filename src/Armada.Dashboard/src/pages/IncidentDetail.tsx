import { useEffect, useMemo, useState } from 'react';
import { Link, useLocation, useNavigate, useParams } from 'react-router-dom';
import {
  createIncident,
  deleteIncident,
  getIncident,
  listDeployments,
  listEnvironments,
  listReleases,
  listRunbookExecutions,
  listVessels,
  rollbackDeployment,
  updateIncident,
} from '../api/client';
import { listAll } from '../api/paging';
import type {
  Deployment,
  DeploymentEnvironment,
  Incident,
  IncidentSeverity,
  IncidentStatus,
  IncidentUpsertRequest,
  Release,
  RunbookExecution,
  Vessel,
} from '../types/models';
import { useAuth } from '../context/AuthContext';
import { useLocale } from '../context/LocaleContext';
import { useNotifications } from '../context/NotificationContext';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import CopyButton from '../components/shared/CopyButton';
import ErrorModal from '../components/shared/ErrorModal';
import JsonViewer from '../components/shared/JsonViewer';
import PageHeader from '../components/shared/PageHeader';
import { ErrorState } from '../components/shared/StateBlocks';
import StatusBadge from '../components/shared/StatusBadge';
import { buildIncidentPrompt, INCIDENT_SEVERITIES, INCIDENT_STATUSES, toInputDateTime, toUtcValue } from '../lib/deliveryForms';
import { PLANNING_PATH } from '../lib/navTargets';
import { useDeleteProgress } from '../lib/useDeleteProgress';


interface IncidentPrefillState {
  prefill?: IncidentUpsertRequest;
}

export default function IncidentDetail() {
  const { id } = useParams<{ id: string }>();
  const location = useLocation();
  const navigate = useNavigate();
  const { isAdmin, isTenantAdmin } = useAuth();
  const { t, formatDateTime, formatRelativeTime } = useLocale();
  const { pushToast } = useNotifications();
  const deletion = useDeleteProgress();

  const createMode = id === 'new';
  const canManage = isAdmin || isTenantAdmin;

  const tips = {
    title: t('Short name for what broke, such as Checkout errors after v1.4.0. Required.'),
    status: t('Lifecycle state: Open, Monitoring after a fix, Mitigated, RolledBack, or Closed. Changing it does not run anything.'),
    severity: t('How bad the impact is: Critical, High, Medium, or Low. Used for sorting and filtering only.'),
    vessel: t('Repository affected by the incident. Also the vessel Plan Hotfix and Dispatch Hotfix target.'),
    environment: t('Environment where the incident happened. Optional; links the incident to that environment\'s record.'),
    environmentName: t('Free-text environment name kept on the incident, such as production. Useful when no environment record is picked.'),
    deployment: t('Deployment that caused or relates to the incident. Linking one enables Rollback Deployment here.'),
    release: t('Release involved in the incident. Optional, for context.'),
    missionId: t('Mission linked to the incident, such as msn_abc123. Optional.'),
    voyageId: t('Voyage linked to the incident, such as vyg_abc123. Optional.'),
    detected: t('When the problem was first noticed, in your local time. Defaults to when the incident was created.'),
    mitigated: t('When the impact was stopped, in your local time. Set it yourself; changing the status does not fill it.'),
    closed: t('When the incident was fully closed, in your local time. Set it yourself; changing the status does not fill it.'),
    rollbackDeploymentId: t('Deployment that was rolled back to recover, such as dpl_abc123. Filled in when you use Rollback Deployment.'),
    summary: t('What happened, in a sentence or two. Shown in the list and searchable.'),
    impact: t('Who or what was affected and how, such as 5% of checkout requests failing for 20 minutes.'),
    rootCause: t('Why it happened, once known, such as a missing database index after a migration.'),
    recoveryNotes: t('What was done to recover, such as rollback steps or config changes.'),
    postmortem: t('Lessons learned and follow-up actions after the incident is resolved.'),
  };

  const [incident, setIncident] = useState<Incident | null>(null);
  const [vessels, setVessels] = useState<Vessel[]>([]);
  const [environments, setEnvironments] = useState<DeploymentEnvironment[]>([]);
  const [deployments, setDeployments] = useState<Deployment[]>([]);
  const [releases, setReleases] = useState<Release[]>([]);
  const [executions, setExecutions] = useState<RunbookExecution[]>([]);
  const [vesselId, setVesselId] = useState('');
  const [environmentId, setEnvironmentId] = useState('');
  const [environmentName, setEnvironmentName] = useState('');
  const [deploymentId, setDeploymentId] = useState('');
  const [releaseId, setReleaseId] = useState('');
  const [missionId, setMissionId] = useState('');
  const [voyageId, setVoyageId] = useState('');
  const [rollbackDeploymentId, setRollbackDeploymentId] = useState('');
  const [title, setTitle] = useState('Incident');
  const [summary, setSummary] = useState('');
  const [status, setStatus] = useState<IncidentStatus>('Open');
  const [severity, setSeverity] = useState<IncidentSeverity>('High');
  const [impact, setImpact] = useState('');
  const [rootCause, setRootCause] = useState('');
  const [recoveryNotes, setRecoveryNotes] = useState('');
  const [postmortem, setPostmortem] = useState('');
  const [detectedUtc, setDetectedUtc] = useState('');
  const [mitigatedUtc, setMitigatedUtc] = useState('');
  const [closedUtc, setClosedUtc] = useState('');
  const [loading, setLoading] = useState(!createMode);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');
  // A failed entity load (for example a stale link) renders an error state instead of an empty editor.
  const [loadFailure, setLoadFailure] = useState<{ id: string | undefined; message: string } | null>(null);
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
      listAll(listVessels),
      listAll(listEnvironments),
      listAll(listDeployments),
      listAll(listReleases),
    ]).then(([vesselResult, environmentResult, deploymentResult, releaseResult]) => {
      if (cancelled) return;
      setVessels(vesselResult.objects || []);
      setEnvironments(environmentResult.objects || []);
      setDeployments(deploymentResult.objects || []);
      setReleases(releaseResult.objects || []);
    }).catch((err: unknown) => {
      if (!cancelled) setError(err instanceof Error ? err.message : t('Failed to load incident reference data.'));
    });

    return () => { cancelled = true; };
  }, [t]);

  useEffect(() => {
    if (!createMode || !location.state) return;
    const state = location.state as IncidentPrefillState;
    if (!state.prefill) return;

    setVesselId(state.prefill.vesselId || '');
    setEnvironmentId(state.prefill.environmentId || '');
    setEnvironmentName(state.prefill.environmentName || '');
    setDeploymentId(state.prefill.deploymentId || '');
    setReleaseId(state.prefill.releaseId || '');
    setMissionId(state.prefill.missionId || '');
    setVoyageId(state.prefill.voyageId || '');
    setRollbackDeploymentId(state.prefill.rollbackDeploymentId || '');
    setTitle(state.prefill.title || 'Incident');
    setSummary(state.prefill.summary || '');
    setStatus(state.prefill.status || 'Open');
    setSeverity(state.prefill.severity || 'High');
    setImpact(state.prefill.impact || '');
    setRootCause(state.prefill.rootCause || '');
    setRecoveryNotes(state.prefill.recoveryNotes || '');
    setPostmortem(state.prefill.postmortem || '');
    setDetectedUtc(toInputDateTime(state.prefill.detectedUtc || null));
    setMitigatedUtc(toInputDateTime(state.prefill.mitigatedUtc || null));
    setClosedUtc(toInputDateTime(state.prefill.closedUtc || null));
  }, [createMode, location.state]);

  useEffect(() => {
    if (createMode || !id) return;
    let mounted = true;
    const incidentId = id;

    async function loadIncident() {
      try {
        setLoading(true);
        const result = await getIncident(incidentId);
        if (!mounted) return;
        setIncident(result);
        setVesselId(result.vesselId || '');
        setEnvironmentId(result.environmentId || '');
        setEnvironmentName(result.environmentName || '');
        setDeploymentId(result.deploymentId || '');
        setReleaseId(result.releaseId || '');
        setMissionId(result.missionId || '');
        setVoyageId(result.voyageId || '');
        setRollbackDeploymentId(result.rollbackDeploymentId || '');
        setTitle(result.title);
        setSummary(result.summary || '');
        setStatus(result.status);
        setSeverity(result.severity);
        setImpact(result.impact || '');
        setRootCause(result.rootCause || '');
        setRecoveryNotes(result.recoveryNotes || '');
        setPostmortem(result.postmortem || '');
        setDetectedUtc(toInputDateTime(result.detectedUtc));
        setMitigatedUtc(toInputDateTime(result.mitigatedUtc));
        setClosedUtc(toInputDateTime(result.closedUtc));
        setError('');
      } catch (err: unknown) {
        if (mounted) setLoadFailure({ id, message: err instanceof Error ? err.message : t('Failed to load incident.') });
      } finally {
        if (mounted) setLoading(false);
      }
    }

    void loadIncident();
    return () => { mounted = false; };
  }, [createMode, id, t]);

  useEffect(() => {
    if (createMode || !id) {
      setExecutions([]);
      return;
    }

    let cancelled = false;
    listAll(listRunbookExecutions, { incidentId: id }).then((result) => {
      if (!cancelled) setExecutions(result.objects || []);
    }).catch(() => {
      if (!cancelled) setExecutions([]);
    });

    return () => { cancelled = true; };
  }, [createMode, id]);

  const selectedVessel = useMemo(() => vessels.find((candidate) => candidate.id === vesselId) || null, [vesselId, vessels]);
  const selectedEnvironment = useMemo(() => environments.find((candidate) => candidate.id === environmentId) || null, [environmentId, environments]);
  const selectedDeployment = useMemo(() => deployments.find((candidate) => candidate.id === deploymentId) || null, [deploymentId, deployments]);

  useEffect(() => {
    if (!environmentId) return;
    const selected = environments.find((candidate) => candidate.id === environmentId);
    if (selected) {
      setEnvironmentName(selected.name);
      if (!vesselId && selected.vesselId) setVesselId(selected.vesselId);
    }
  }, [environmentId, environments, vesselId]);

  useEffect(() => {
    if (!deploymentId) return;
    const selected = deployments.find((candidate) => candidate.id === deploymentId);
    if (selected) {
      if (!environmentId && selected.environmentId) setEnvironmentId(selected.environmentId);
      if (!environmentName && selected.environmentName) setEnvironmentName(selected.environmentName);
      if (!vesselId && selected.vesselId) setVesselId(selected.vesselId);
      if (!releaseId && selected.releaseId) setReleaseId(selected.releaseId);
      if (!missionId && selected.missionId) setMissionId(selected.missionId);
      if (!voyageId && selected.voyageId) setVoyageId(selected.voyageId);
    }
  }, [deploymentId, deployments, environmentId, environmentName, missionId, releaseId, vesselId, voyageId]);

  function buildPayload(): IncidentUpsertRequest {
    return {
      title: title.trim() || null,
      summary: summary.trim() || null,
      status,
      severity,
      vesselId: vesselId || null,
      environmentId: environmentId || null,
      environmentName: environmentName.trim() || null,
      deploymentId: deploymentId.trim() || null,
      releaseId: releaseId.trim() || null,
      missionId: missionId.trim() || null,
      voyageId: voyageId.trim() || null,
      rollbackDeploymentId: rollbackDeploymentId.trim() || null,
      impact: impact.trim() || null,
      rootCause: rootCause.trim() || null,
      recoveryNotes: recoveryNotes.trim() || null,
      postmortem: postmortem.trim() || null,
      detectedUtc: toUtcValue(detectedUtc),
      mitigatedUtc: toUtcValue(mitigatedUtc),
      closedUtc: toUtcValue(closedUtc),
    };
  }

  async function handleSave() {
    if (!canManage) return;
    try {
      setSaving(true);
      const payload = buildPayload();
      if (createMode) {
        const created = await createIncident(payload);
        pushToast('success', t('Incident "{{title}}" created.', { title: created.title }));
        navigate(`/incidents/${created.id}`);
        return;
      }

      if (!id) return;
      const updated = await updateIncident(id, payload);
      setIncident(updated);
      pushToast('success', t('Incident "{{title}}" saved.', { title: updated.title }));
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Save failed.'));
    } finally {
      setSaving(false);
    }
  }

  function handleDelete() {
    if (!incident || !canManage) return;
    setConfirm({
      open: true,
      title: t('Delete Incident'),
      message: t('Delete "{{title}}"? This removes only the incident record.', { title: incident.title }),
      onConfirm: async () => {
        setConfirm((current) => ({ ...current, open: false }));
        if (!(await deletion.run({ kind: 'incident', action: () => deleteIncident(incident.id), errorMessage: (err) => (err instanceof Error ? err.message : t('Delete failed.')) }))) return;
        pushToast('warning', t('Incident "{{title}}" deleted.', { title: incident.title }));
        navigate('/incidents');
      },
    });
  }

  function handlePlanHotfix() {
    const prompt = buildIncidentPrompt(title, summary, impact, environmentName, deploymentId, releaseId);
    navigate(PLANNING_PATH, {
      state: {
        fromIncident: true,
        vesselId: vesselId || selectedEnvironment?.vesselId || null,
        title: `Hotfix: ${title}`,
        initialPrompt: prompt,
      },
    });
  }

  function handleDispatchHotfix() {
    const prompt = buildIncidentPrompt(title, summary, impact, environmentName, deploymentId, releaseId);
    navigate('/dispatch', {
      state: {
        fromIncident: true,
        vesselId: vesselId || selectedEnvironment?.vesselId || null,
        voyageTitle: `Hotfix: ${title}`,
        prompt,
      },
    });
  }

  function handleRollback() {
    if (!incident?.deploymentId) return;
    setConfirm({
      open: true,
      title: t('Rollback Deployment'),
      message: t('Rollback deployment "{{deploymentId}}" and attach the result to this incident?', { deploymentId: incident.deploymentId }),
      onConfirm: async () => {
        setConfirm((current) => ({ ...current, open: false }));
        try {
          await rollbackDeployment(incident.deploymentId!);
          const updated = await updateIncident(incident.id, {
            rollbackDeploymentId: incident.deploymentId,
            status: 'RolledBack',
          });
          setIncident(updated);
          setRollbackDeploymentId(updated.rollbackDeploymentId || '');
          setStatus(updated.status);
          pushToast('warning', t('Rollback started for "{{deploymentId}}".', { deploymentId: incident.deploymentId! }));
        } catch (err: unknown) {
          setError(err instanceof Error ? err.message : t('Rollback failed.'));
        }
      },
    });
  }

  function handleRunbook() {
    navigate('/runbooks', {
      state: {
        prefillExecution: {
          title: `${title} Response`,
          workflowProfileId: selectedDeployment?.workflowProfileId || null,
          environmentId: environmentId || selectedDeployment?.environmentId || null,
          environmentName: environmentName || selectedDeployment?.environmentName || null,
          deploymentId: deploymentId || null,
          incidentId: incident?.id || null,
          checkType: deploymentId ? 'DeploymentVerification' : 'Custom',
          notes: buildIncidentPrompt(title, summary, impact, environmentName, deploymentId, releaseId),
        },
      },
    });
  }

  if (loading) return <p className="text-dim">{t('Loading...')}</p>;

  if (!createMode && loadFailure && loadFailure.id === id) {
    return (
      <div>
        <PageHeader breadcrumb={[{ label: t('Incidents'), to: '/incidents' }, { label: id || '' }]} title={id || ''} />
        <ErrorState message={loadFailure.message} />
      </div>
    );
  }

  return (
    <div>
      <PageHeader
        breadcrumb={[
          { label: t('Incidents'), to: '/incidents' },
          { label: createMode ? t('New Incident') : title },
        ]}
        title={createMode ? t('Create Incident') : title}
        actions={
          <>
            {!createMode && <StatusBadge status={status} />}
            {!createMode && <StatusBadge status={severity} />}
            {!!(vesselId || selectedEnvironment?.vesselId) && (
              <>
                <button className="btn btn-sm" onClick={handlePlanHotfix} title={t('Open Planning with a hotfix session prefilled from this incident\'s title, summary, impact, and links. Nothing is dispatched yet.')}>{t('Plan Hotfix')}</button>
                <button className="btn btn-sm" onClick={handleDispatchHotfix} title={t('Open Dispatch with a hotfix voyage prefilled from this incident for its vessel. You review it before captains start.')}>{t('Dispatch Hotfix')}</button>
              </>
            )}
            {!createMode && (
              <button className="btn btn-sm" onClick={handleRunbook} title={t('Go to Runbooks to start a response runbook prefilled with this incident, its deployment, and environment.')}>{t('Runbook')}</button>
            )}
            {!createMode && incident?.deploymentId && (
              <button className="btn btn-sm" onClick={handleRollback} title={t('Run the linked deployment\'s rollback command now and mark this incident RolledBack. Asks for confirmation first.')}>{t('Rollback Deployment')}</button>
            )}
            {!createMode && incident?.deploymentId && (
              <button className="btn btn-sm" onClick={() => navigate(`/deployments/${incident.deploymentId}`)} title={t('Open the deployment linked to this incident.')}>{t('Open Deployment')}</button>
            )}
            {!createMode && incident?.environmentId && (
              <button className="btn btn-sm" onClick={() => navigate(`/environments/${incident.environmentId}`)} title={t('Open the environment linked to this incident.')}>{t('Open Environment')}</button>
            )}
            {!createMode && incident && (
              <button className="btn btn-sm" onClick={() => setJsonData({ open: true, title, data: incident })} title={t('Show the raw incident record as returned by the API.')}>{t('View JSON')}</button>
            )}
            {!createMode && canManage && (
              <button className="btn btn-sm btn-danger" onClick={handleDelete} title={t('Delete this incident and its saved history. Deployments, checks, and releases are not affected. Cannot be undone.')}>{t('Delete')}</button>
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
      {deletion.dialog}

      <div className="detail-grid">
        <section className="card detail-panel">
          <h3>{t('Incident')}</h3>
          <div className="detail-form-grid">
            <div className="detail-field detail-field-full">
              <span className="detail-label" title={tips.title}>{t('Title')}</span>
              <input aria-label={t('Title')} title={tips.title} value={title} onChange={(event) => setTitle(event.target.value)} disabled={!canManage} />
            </div>

            <div className="detail-field">
              <span className="detail-label" title={tips.status}>{t('Status')}</span>
              <select aria-label={t('Status')} title={tips.status} value={status} onChange={(event) => setStatus(event.target.value as IncidentStatus)} disabled={!canManage}>
                {INCIDENT_STATUSES.map((value) => (
                  <option key={value} value={value}>{value}</option>
                ))}
              </select>
            </div>

            <div className="detail-field">
              <span className="detail-label" title={tips.severity}>{t('Severity')}</span>
              <select aria-label={t('Severity')} title={tips.severity} value={severity} onChange={(event) => setSeverity(event.target.value as IncidentSeverity)} disabled={!canManage}>
                {INCIDENT_SEVERITIES.map((value) => (
                  <option key={value} value={value}>{value}</option>
                ))}
              </select>
            </div>

            <div className="detail-field">
              <span className="detail-label" title={tips.vessel}>{t('Vessel')}</span>
              <select aria-label={t('Vessel')} title={tips.vessel} value={vesselId} onChange={(event) => setVesselId(event.target.value)} disabled={!canManage}>
                <option value="">{t('Select a vessel')}</option>
                {vessels.map((vessel) => (
                  <option key={vessel.id} value={vessel.id}>{vessel.name}</option>
                ))}
              </select>
            </div>

            <div className="detail-field">
              <span className="detail-label" title={tips.environment}>{t('Environment')}</span>
              <select aria-label={t('Environment')} title={tips.environment} value={environmentId} onChange={(event) => setEnvironmentId(event.target.value)} disabled={!canManage}>
                <option value="">{t('Select an environment')}</option>
                {environments.map((environment) => (
                  <option key={environment.id} value={environment.id}>{environment.name}</option>
                ))}
              </select>
            </div>

            <div className="detail-field">
              <span className="detail-label" title={tips.environmentName}>{t('Environment Name')}</span>
              <input aria-label={t('Environment Name')} title={tips.environmentName} value={environmentName} onChange={(event) => setEnvironmentName(event.target.value)} disabled={!canManage} />
            </div>

            <div className="detail-field">
              <span className="detail-label" title={tips.deployment}>{t('Deployment')}</span>
              <select aria-label={t('Deployment')} title={tips.deployment} value={deploymentId} onChange={(event) => setDeploymentId(event.target.value)} disabled={!canManage}>
                <option value="">{t('No linked deployment')}</option>
                {deployments.map((deployment) => (
                  <option key={deployment.id} value={deployment.id}>{deployment.title}</option>
                ))}
              </select>
            </div>

            <div className="detail-field">
              <span className="detail-label" title={tips.release}>{t('Release')}</span>
              <select aria-label={t('Release')} title={tips.release} value={releaseId} onChange={(event) => setReleaseId(event.target.value)} disabled={!canManage}>
                <option value="">{t('No linked release')}</option>
                {releases.map((release) => (
                  <option key={release.id} value={release.id}>{release.title}</option>
                ))}
              </select>
            </div>

            <div className="detail-field">
              <span className="detail-label" title={tips.missionId}>{t('Mission ID')}</span>
              <input aria-label={t('Mission ID')} title={tips.missionId} value={missionId} onChange={(event) => setMissionId(event.target.value)} disabled={!canManage} placeholder="msn_..." />
            </div>

            <div className="detail-field">
              <span className="detail-label" title={tips.voyageId}>{t('Voyage ID')}</span>
              <input aria-label={t('Voyage ID')} title={tips.voyageId} value={voyageId} onChange={(event) => setVoyageId(event.target.value)} disabled={!canManage} placeholder="vyg_..." />
            </div>

            <div className="detail-field">
              <span className="detail-label" title={tips.detected}>{t('Detected')}</span>
              <input aria-label={t('Detected')} title={tips.detected} type="datetime-local" value={detectedUtc} onChange={(event) => setDetectedUtc(event.target.value)} disabled={!canManage} />
            </div>

            <div className="detail-field">
              <span className="detail-label" title={tips.mitigated}>{t('Mitigated')}</span>
              <input aria-label={t('Mitigated')} title={tips.mitigated} type="datetime-local" value={mitigatedUtc} onChange={(event) => setMitigatedUtc(event.target.value)} disabled={!canManage} />
            </div>

            <div className="detail-field">
              <span className="detail-label" title={tips.closed}>{t('Closed')}</span>
              <input aria-label={t('Closed')} title={tips.closed} type="datetime-local" value={closedUtc} onChange={(event) => setClosedUtc(event.target.value)} disabled={!canManage} />
            </div>

            <div className="detail-field">
              <span className="detail-label" title={tips.rollbackDeploymentId}>{t('Rollback Deployment')}</span>
              <input aria-label={t('Rollback Deployment')} title={tips.rollbackDeploymentId} value={rollbackDeploymentId} onChange={(event) => setRollbackDeploymentId(event.target.value)} disabled={!canManage} placeholder="dpl_..." />
            </div>

            <div className="detail-field detail-field-full">
              <span className="detail-label" title={tips.summary}>{t('Summary')}</span>
              <textarea aria-label={t('Summary')} title={tips.summary} rows={3} value={summary} onChange={(event) => setSummary(event.target.value)} disabled={!canManage} />
            </div>

            <div className="detail-field detail-field-full">
              <span className="detail-label" title={tips.impact}>{t('Impact')}</span>
              <textarea aria-label={t('Impact')} title={tips.impact} rows={3} value={impact} onChange={(event) => setImpact(event.target.value)} disabled={!canManage} />
            </div>

            <div className="detail-field detail-field-full">
              <span className="detail-label" title={tips.rootCause}>{t('Root Cause')}</span>
              <textarea aria-label={t('Root Cause')} title={tips.rootCause} rows={3} value={rootCause} onChange={(event) => setRootCause(event.target.value)} disabled={!canManage} />
            </div>

            <div className="detail-field detail-field-full">
              <span className="detail-label" title={tips.recoveryNotes}>{t('Recovery Notes')}</span>
              <textarea aria-label={t('Recovery Notes')} title={tips.recoveryNotes} rows={4} value={recoveryNotes} onChange={(event) => setRecoveryNotes(event.target.value)} disabled={!canManage} />
            </div>

            <div className="detail-field detail-field-full">
              <span className="detail-label" title={tips.postmortem}>{t('Postmortem')}</span>
              <textarea aria-label={t('Postmortem')} title={tips.postmortem} rows={5} value={postmortem} onChange={(event) => setPostmortem(event.target.value)} disabled={!canManage} />
            </div>
          </div>

          {canManage && (
            <div className="inline-actions" style={{ marginTop: '1rem' }}>
              <button className="btn btn-primary" disabled={saving} onClick={handleSave} title={createMode ? t('Create the incident record. You can add root cause, recovery, and postmortem notes on its detail page.') : t('Save this incident. Each save keeps a snapshot, so earlier versions stay in the activity history.')}>
                {saving ? t('Saving...') : createMode ? t('Create Incident') : t('Save Incident')}
              </button>
            </div>
          )}
        </section>

        <section className="card detail-panel">
          <h3>{t('Overview')}</h3>
          <div className="detail-meta-grid">
            <div className="detail-field" title={tips.vessel}><span className="detail-label">{t('Vessel')}</span><span>{selectedVessel?.name || vesselId || '-'}</span></div>
            <div className="detail-field" title={tips.environment}><span className="detail-label">{t('Environment')}</span><span>{selectedEnvironment?.name || environmentName || '-'}</span></div>
            <div className="detail-field" title={tips.deployment}><span className="detail-label">{t('Deployment')}</span><span>{deploymentId || '-'}</span></div>
            <div className="detail-field" title={tips.release}><span className="detail-label">{t('Release')}</span><span>{releaseId || '-'}</span></div>
            <div className="detail-field" title={tips.detected}><span className="detail-label">{t('Detected')}</span><span>{detectedUtc ? formatDateTime(toUtcValue(detectedUtc) || detectedUtc) : '-'}</span></div>
            <div className="detail-field" title={t('When the incident was last saved.')}><span className="detail-label">{t('Last Updated')}</span><span>{incident?.lastUpdateUtc ? formatRelativeTime(incident.lastUpdateUtc) : '-'}</span></div>
            {incident?.failureKind && (
              <>
                <div className="detail-field" title={t('Classified cause of the failed mission that opened this incident automatically, such as Compile, TestFail, or Timeout.')}><span className="detail-label">{t('Failure Kind')}</span><span>{incident.failureKind}</span></div>
                <div className="detail-field" title={t('Rescue missions Armada has dispatched for this incident. When the cap is reached, the incident stays Open for a human.')}><span className="detail-label">{t('Rescue Attempts')}</span><span>{incident.recoveryAttempts ?? 0}</span></div>
                <div className="detail-field" title={t('Number of rescue missions linked to this incident. A landed rescue moves it to Mitigated, then Closed once the fix holds.')}><span className="detail-label">{t('Rescue Missions')}</span><span>{(incident.rescueMissionIds && incident.rescueMissionIds.length) ? incident.rescueMissionIds.length : 0}</span></div>
              </>
            )}
          </div>

          {!createMode && incident && (
            <>
              <div className="detail-divider" />
              <h4>{t('Identifiers')}</h4>
              <div className="detail-key-list">
                <div className="detail-key-row">
                  <span>{t('Incident ID')}</span>
                  <div className="detail-key-actions">
                    <code>{incident.id}</code>
                    <CopyButton text={incident.id} title={t('Copy incident ID')} />
                  </div>
                </div>
                {incident.deploymentId && (
                  <div className="detail-key-row">
                    <span>{t('Deployment')}</span>
                    <div className="detail-key-actions">
                      <Link to={`/deployments/${incident.deploymentId}`}>{incident.deploymentId}</Link>
                    </div>
                  </div>
                )}
                {incident.environmentId && (
                  <div className="detail-key-row">
                    <span>{t('Environment')}</span>
                    <div className="detail-key-actions">
                      <Link to={`/environments/${incident.environmentId}`}>{incident.environmentId}</Link>
                    </div>
                  </div>
                )}
                {incident.releaseId && (
                  <div className="detail-key-row">
                    <span>{t('Release')}</span>
                    <div className="detail-key-actions">
                      <Link to={`/releases/${incident.releaseId}`}>{incident.releaseId}</Link>
                    </div>
                  </div>
                )}
              </div>

              <div className="detail-divider" />
              <h4 title={t('Runbook executions started for this incident.')}>{t('Runbook Executions')}</h4>
              {executions.length === 0 ? (
                <p className="text-dim">{t('No runbook executions are linked to this incident yet.')}</p>
              ) : (
                <div style={{ display: 'grid', gap: '0.75rem' }}>
                  {executions.map((execution) => (
                    <div key={execution.id} className="card" style={{ padding: '0.85rem' }}>
                      <div style={{ display: 'flex', justifyContent: 'space-between', gap: '0.75rem', alignItems: 'center' }}>
                        <div>
                          <strong>{execution.title}</strong>
                          <div className="mono text-dim" style={{ fontSize: '0.78rem' }}>{execution.id}</div>
                        </div>
                        <StatusBadge status={execution.status} />
                      </div>
                      <div className="text-dim" style={{ marginTop: '0.4rem' }}>
                        {execution.environmentName || t('No environment')} • {execution.checkType || t('No check type')} • {execution.completedStepIds.length} {t('steps complete')}
                      </div>
                      <div className="text-dim" style={{ marginTop: '0.2rem' }}>
                        {t('Last updated')} {formatRelativeTime(execution.lastUpdateUtc)}
                      </div>
                      <div className="inline-actions" style={{ marginTop: '0.65rem' }}>
                        <button className="btn btn-sm" onClick={() => navigate(`/runbooks/${execution.runbookId}`)} title={t('Open the runbook this execution follows.')}>
                          {t('Open Runbook')}
                        </button>
                      </div>
                    </div>
                  ))}
                </div>
              )}
            </>
          )}
        </section>
      </div>
    </div>
  );
}
