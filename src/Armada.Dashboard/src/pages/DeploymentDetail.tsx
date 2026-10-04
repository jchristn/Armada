import { useEffect, useMemo, useState } from 'react';
import { Link, useLocation, useNavigate, useParams } from 'react-router-dom';
import {
  approveDeployment,
  createDeployment,
  deleteDeployment,
  denyDeployment,
  getDeployment,
  listRunbookExecutions,
  listEnvironments,
  listReleases,
  listVessels,
  listWorkflowProfiles,
  rollbackDeployment,
  syncGitHubActions,
  updateDeployment,
  verifyDeployment,
} from '../api/client';
import type {
  Deployment,
  DeploymentEnvironment,
  DeploymentStatus,
  DeploymentUpsertRequest,
  Release,
  RunbookExecution,
  Vessel,
  WorkflowProfile,
} from '../types/models';
import { useAuth } from '../context/AuthContext';
import { useLocale } from '../context/LocaleContext';
import { useNotifications } from '../context/NotificationContext';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import CopyButton from '../components/shared/CopyButton';
import ErrorModal from '../components/shared/ErrorModal';
import JsonViewer from '../components/shared/JsonViewer';
import PageHeader from '../components/shared/PageHeader';
import StatusBadge from '../components/shared/StatusBadge';

interface DeploymentPrefillState {
  prefill?: DeploymentUpsertRequest;
}

function joinList(values: string[] | null | undefined): string {
  return (values || []).join('\n');
}

export default function DeploymentDetail() {
  const { id } = useParams<{ id: string }>();
  const location = useLocation();
  const navigate = useNavigate();
  const { isAdmin, isTenantAdmin } = useAuth();
  const { t, formatDateTime, formatRelativeTime } = useLocale();
  const { pushToast } = useNotifications();

  const createMode = id === 'new';
  const canManage = isAdmin || isTenantAdmin;

  const [deployment, setDeployment] = useState<Deployment | null>(null);
  const [vessels, setVessels] = useState<Vessel[]>([]);
  const [profiles, setProfiles] = useState<WorkflowProfile[]>([]);
  const [environments, setEnvironments] = useState<DeploymentEnvironment[]>([]);
  const [releases, setReleases] = useState<Release[]>([]);
  const [runbookExecutions, setRunbookExecutions] = useState<RunbookExecution[]>([]);
  const [vesselId, setVesselId] = useState('');
  const [workflowProfileId, setWorkflowProfileId] = useState('');
  const [environmentId, setEnvironmentId] = useState('');
  const [environmentName, setEnvironmentName] = useState('');
  const [releaseId, setReleaseId] = useState('');
  const [missionId, setMissionId] = useState('');
  const [voyageId, setVoyageId] = useState('');
  const [title, setTitle] = useState('Deployment');
  const [sourceRef, setSourceRef] = useState('');
  const [summary, setSummary] = useState('');
  const [notes, setNotes] = useState('');
  const [autoExecute, setAutoExecute] = useState(true);
  const [loading, setLoading] = useState(!createMode);
  const [saving, setSaving] = useState(false);
  const [syncingGitHub, setSyncingGitHub] = useState(false);
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
      listVessels({ pageSize: 9999 }),
      listWorkflowProfiles({ pageSize: 9999 }),
      listEnvironments({ pageSize: 9999 }),
      listReleases({ pageSize: 9999 }),
    ]).then(([vesselResult, profileResult, environmentResult, releaseResult]) => {
      if (cancelled) return;
      setVessels(vesselResult.objects || []);
      setProfiles(profileResult.objects || []);
      setEnvironments(environmentResult.objects || []);
      setReleases(releaseResult.objects || []);
    }).catch((err: unknown) => {
      if (!cancelled) setError(err instanceof Error ? err.message : t('Failed to load deployment reference data.'));
    });

    return () => { cancelled = true; };
  }, [t]);

  useEffect(() => {
    if (!createMode || !location.state) return;
    const state = location.state as DeploymentPrefillState;
    if (!state.prefill) return;

    setVesselId(state.prefill.vesselId || '');
    setWorkflowProfileId(state.prefill.workflowProfileId || '');
    setEnvironmentId(state.prefill.environmentId || '');
    setEnvironmentName(state.prefill.environmentName || '');
    setReleaseId(state.prefill.releaseId || '');
    setMissionId(state.prefill.missionId || '');
    setVoyageId(state.prefill.voyageId || '');
    setTitle(state.prefill.title || 'Deployment');
    setSourceRef(state.prefill.sourceRef || '');
    setSummary(state.prefill.summary || '');
    setNotes(state.prefill.notes || '');
    setAutoExecute(state.prefill.autoExecute ?? true);
  }, [createMode, location.state]);

  useEffect(() => {
    if (createMode || !id) return;
    let mounted = true;
    const deploymentId = id;

    async function loadDeployment() {
      try {
        setLoading(true);
        const result = await getDeployment(deploymentId);
        if (!mounted) return;
        setDeployment(result);
        setVesselId(result.vesselId || '');
        setWorkflowProfileId(result.workflowProfileId || '');
        setEnvironmentId(result.environmentId || '');
        setEnvironmentName(result.environmentName || '');
        setReleaseId(result.releaseId || '');
        setMissionId(result.missionId || '');
        setVoyageId(result.voyageId || '');
        setTitle(result.title);
        setSourceRef(result.sourceRef || '');
        setSummary(result.summary || '');
        setNotes(result.notes || '');
        setError('');
      } catch (err: unknown) {
        if (mounted) setError(err instanceof Error ? err.message : t('Failed to load deployment.'));
      } finally {
        if (mounted) setLoading(false);
      }
    }

    loadDeployment();
    return () => { mounted = false; };
  }, [createMode, id, t]);

  useEffect(() => {
    if (createMode || !id) {
      setRunbookExecutions([]);
      return;
    }

    let cancelled = false;
    listRunbookExecutions({ deploymentId: id, pageSize: 9999 }).then((result) => {
      if (!cancelled) setRunbookExecutions(result.objects || []);
    }).catch(() => {
      if (!cancelled) setRunbookExecutions([]);
    });

    return () => { cancelled = true; };
  }, [createMode, id]);

  const selectedVessel = useMemo(() => vessels.find((candidate) => candidate.id === vesselId) || null, [vesselId, vessels]);
  const selectedEnvironment = useMemo(() => environments.find((candidate) => candidate.id === environmentId) || null, [environmentId, environments]);
  const filteredEnvironments = useMemo(() => environments.filter((environment) => !vesselId || environment.vesselId === vesselId), [environments, vesselId]);
  const filteredReleases = useMemo(() => releases.filter((release) => !vesselId || release.vesselId === vesselId), [releases, vesselId]);
  const profileMap = useMemo(() => new Map(profiles.map((profile) => [profile.id, profile.name])), [profiles]);

  useEffect(() => {
    if (!environmentId) return;
    const selected = environments.find((environment) => environment.id === environmentId);
    if (selected) {
      setEnvironmentName(selected.name);
      if (!vesselId && selected.vesselId) setVesselId(selected.vesselId);
    }
  }, [environmentId, environments, vesselId]);

  useEffect(() => {
    if (!releaseId) return;
    const selected = releases.find((current) => current.id === releaseId);
    if (selected && !vesselId && selected.vesselId) {
      setVesselId(selected.vesselId);
    }
  }, [releaseId, releases, vesselId]);

  function buildPayload(): DeploymentUpsertRequest {
    return {
      vesselId: vesselId || null,
      workflowProfileId: workflowProfileId || null,
      environmentId: environmentId || null,
      environmentName: environmentName.trim() || null,
      releaseId: releaseId || null,
      missionId: missionId.trim() || null,
      voyageId: voyageId.trim() || null,
      title: title.trim() || null,
      sourceRef: sourceRef.trim() || null,
      summary: summary.trim() || null,
      notes: notes.trim() || null,
      autoExecute,
    };
  }

  async function handleSave() {
    if (!canManage) return;
    try {
      setSaving(true);
      const payload = buildPayload();
      if (createMode) {
        const created = await createDeployment(payload);
        pushToast('success', t('Deployment "{{title}}" created.', { title: created.title }));
        navigate(`/deployments/${created.id}`);
        return;
      }

      if (!id) return;
      const updated = await updateDeployment(id, payload);
      setDeployment(updated);
      setVesselId(updated.vesselId || '');
      setWorkflowProfileId(updated.workflowProfileId || '');
      setEnvironmentId(updated.environmentId || '');
      setEnvironmentName(updated.environmentName || '');
      setReleaseId(updated.releaseId || '');
      setMissionId(updated.missionId || '');
      setVoyageId(updated.voyageId || '');
      setTitle(updated.title);
      setSourceRef(updated.sourceRef || '');
      setSummary(updated.summary || '');
      setNotes(updated.notes || '');
      pushToast('success', t('Deployment "{{title}}" saved.', { title: updated.title }));
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Save failed.'));
    } finally {
      setSaving(false);
    }
  }

  function handleDelete() {
    if (!deployment || !canManage) return;
    setConfirm({
      open: true,
      title: t('Delete Deployment'),
      message: t('Delete "{{title}}"? This removes only the deployment record.', { title: deployment.title }),
      onConfirm: async () => {
        setConfirm((current) => ({ ...current, open: false }));
        try {
          await deleteDeployment(deployment.id);
          pushToast('warning', t('Deployment "{{title}}" deleted.', { title: deployment.title }));
          navigate('/deployments');
        } catch (err: unknown) {
          setError(err instanceof Error ? err.message : t('Delete failed.'));
        }
      },
    });
  }

  function handleAction(
    action: 'approve' | 'deny' | 'verify' | 'rollback',
    currentDeployment: Deployment,
  ) {
    const titles: Record<typeof action, string> = {
      approve: t('Approve Deployment'),
      deny: t('Deny Deployment'),
      verify: t('Run Verification'),
      rollback: t('Rollback Deployment'),
    };

    const messages: Record<typeof action, string> = {
      approve: t('Approve and execute "{{title}}"?', { title: currentDeployment.title }),
      deny: t('Deny "{{title}}" without executing it?', { title: currentDeployment.title }),
      verify: t('Re-run post-deploy verification for "{{title}}"?', { title: currentDeployment.title }),
      rollback: t('Run rollback for "{{title}}"?', { title: currentDeployment.title }),
    };

    setConfirm({
      open: true,
      title: titles[action],
      message: messages[action],
      onConfirm: async () => {
        setConfirm((current) => ({ ...current, open: false }));
        try {
          let updated: Deployment;
          if (action === 'approve') updated = await approveDeployment(currentDeployment.id);
          else if (action === 'deny') updated = await denyDeployment(currentDeployment.id);
          else if (action === 'verify') updated = await verifyDeployment(currentDeployment.id);
          else updated = await rollbackDeployment(currentDeployment.id);

          setDeployment(updated);
          setVesselId(updated.vesselId || '');
          setWorkflowProfileId(updated.workflowProfileId || '');
          setEnvironmentId(updated.environmentId || '');
          setEnvironmentName(updated.environmentName || '');
          setReleaseId(updated.releaseId || '');
          setMissionId(updated.missionId || '');
          setVoyageId(updated.voyageId || '');
          setTitle(updated.title);
          setSourceRef(updated.sourceRef || '');
          setSummary(updated.summary || '');
          setNotes(updated.notes || '');
          pushToast('success', t('Deployment "{{title}}" updated.', { title: updated.title }));
        } catch (err: unknown) {
          setError(err instanceof Error ? err.message : t('Action failed.'));
        }
      },
    });
  }

  function renderLinkedChecks(currentDeployment: Deployment) {
    if (currentDeployment.checkRunIds.length === 0)
      return <span className="text-dim">{t('No linked checks')}</span>;

    return (
      <div style={{ display: 'grid', gap: '0.35rem' }}>
        {currentDeployment.checkRunIds.map((checkRunId) => (
          <div key={checkRunId}>
            <Link to={`/checks/${checkRunId}`}>{checkRunId}</Link>
          </div>
        ))}
      </div>
    );
  }

  function openRunCheck(currentDeployment: Deployment) {
    navigate('/checks', {
      state: {
        prefill: {
          vesselId: currentDeployment.vesselId || null,
          workflowProfileId: currentDeployment.workflowProfileId || null,
          deploymentId: currentDeployment.id,
          missionId: currentDeployment.missionId || null,
          voyageId: currentDeployment.voyageId || null,
          environmentName: currentDeployment.environmentName || null,
          type: 'DeploymentVerification',
          label: `${currentDeployment.title} verification`,
        },
      },
    });
  }

  function openIncidentCreate(currentDeployment: Deployment) {
    navigate('/incidents/new', {
      state: {
        prefill: {
          title: `${currentDeployment.title} Incident`,
          summary: currentDeployment.latestMonitoringSummary || currentDeployment.summary || null,
          vesselId: currentDeployment.vesselId || null,
          environmentId: currentDeployment.environmentId || null,
          environmentName: currentDeployment.environmentName || null,
          deploymentId: currentDeployment.id,
          releaseId: currentDeployment.releaseId || null,
          missionId: currentDeployment.missionId || null,
          voyageId: currentDeployment.voyageId || null,
          severity: currentDeployment.status === 'VerificationFailed' || currentDeployment.status === 'Failed' ? 'High' : 'Medium',
        },
      },
    });
  }

  function openRunbooks(currentDeployment: Deployment) {
    navigate('/runbooks', {
      state: {
        prefillExecution: {
          title: `${currentDeployment.title} Runbook`,
          workflowProfileId: currentDeployment.workflowProfileId || null,
          environmentId: currentDeployment.environmentId || null,
          environmentName: currentDeployment.environmentName || null,
          deploymentId: currentDeployment.id,
          checkType: currentDeployment.status === 'RolledBack' ? 'RollbackVerification' : 'DeploymentVerification',
          notes: currentDeployment.summary || currentDeployment.notes || null,
        },
      },
    });
  }

  async function handleSyncGitHubActions(currentDeployment: Deployment) {
    if (!currentDeployment.vesselId) return;

    try {
      setSyncingGitHub(true);
      const syncResult = await syncGitHubActions({
        vesselId: currentDeployment.vesselId,
        workflowProfileId: currentDeployment.workflowProfileId || null,
        deploymentId: currentDeployment.id,
        environmentName: currentDeployment.environmentName || null,
        branchName: currentDeployment.sourceRef || null,
        runCount: 20,
      });
      const refreshed = await getDeployment(currentDeployment.id);
      setDeployment(refreshed);
      pushToast('success', t('GitHub Actions sync complete: {{created}} created, {{updated}} updated.', {
        created: syncResult.createdCount,
        updated: syncResult.updatedCount,
      }));
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('GitHub Actions sync failed.'));
    } finally {
      setSyncingGitHub(false);
    }
  }

  if (loading) return <p className="text-dim">{t('Loading...')}</p>;

  return (
    <div>
      <PageHeader
        breadcrumb={
          <>
            <Link to="/deployments">{t('Deployments')}</Link> <span className="breadcrumb-sep">&gt;</span> <span>{createMode ? t('New Deployment') : title}</span>
          </>
        }
        title={createMode ? t('Create Deployment') : title}
        actions={
          <>
            {!createMode && deployment && <StatusBadge status={deployment.status} />}
            {!createMode && deployment && <StatusBadge status={deployment.verificationStatus} />}
            {!createMode && selectedVessel && (
              <button className="btn btn-sm" onClick={() => navigate(`/workspace/${selectedVessel.id}`)}>
                {t('Open Workspace')}
              </button>
            )}
            {!createMode && deployment && (
              <button className="btn btn-sm" onClick={() => openRunCheck(deployment)}>
                {t('Run Check')}
              </button>
            )}
            {!createMode && deployment?.vesselId && (
              <button className="btn btn-sm" disabled={syncingGitHub} onClick={() => void handleSyncGitHubActions(deployment)}>
                {syncingGitHub ? t('Syncing GitHub...') : t('Sync GitHub Actions')}
              </button>
            )}
            {!createMode && deployment && (
              <button className="btn btn-sm" onClick={() => openRunbooks(deployment)}>
                {t('Runbook')}
              </button>
            )}
            {!createMode && deployment && (
              <button className="btn btn-sm" onClick={() => openIncidentCreate(deployment)}>
                {t('Create Incident')}
              </button>
            )}
            {!createMode && deployment?.releaseId && (
              <button className="btn btn-sm" onClick={() => navigate(`/releases/${deployment.releaseId}`)}>
                {t('Open Release')}
              </button>
            )}
            {!createMode && deployment && (
              <button className="btn btn-sm" onClick={() => setJsonData({ open: true, title, data: deployment })}>
                {t('View JSON')}
              </button>
            )}
            {!createMode && deployment?.status === 'PendingApproval' && canManage && (
              <>
                <button className="btn btn-sm btn-primary" onClick={() => handleAction('approve', deployment)}>
                  {t('Approve')}
                </button>
                <button className="btn btn-sm" onClick={() => handleAction('deny', deployment)}>
                  {t('Deny')}
                </button>
              </>
            )}
            {!createMode && deployment && deployment.status !== 'PendingApproval' && canManage && (
              <>
                <button className="btn btn-sm" onClick={() => handleAction('verify', deployment)}>
                  {t('Verify')}
                </button>
                <button className="btn btn-sm" onClick={() => handleAction('rollback', deployment)}>
                  {t('Rollback')}
                </button>
              </>
            )}
            {!createMode && canManage && (
              <button className="btn btn-sm btn-danger" onClick={handleDelete}>
                {t('Delete')}
              </button>
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

      <div className="detail-grid">
        <section className="card detail-panel">
          <h3>{t('Deployment')}</h3>
          <div className="detail-form-grid">
            <div className="detail-field">
              <span className="detail-label">{t('Vessel')}</span>
              <select aria-label={t('Vessel')} value={vesselId} onChange={(event) => setVesselId(event.target.value)} disabled={!canManage}>
                <option value="">{t('Select a vessel')}</option>
                {vessels.map((vessel) => (
                  <option key={vessel.id} value={vessel.id}>{vessel.name}</option>
                ))}
              </select>
            </div>

            <div className="detail-field">
              <span className="detail-label">{t('Workflow Profile')}</span>
              <select aria-label={t('Workflow Profile')} value={workflowProfileId} onChange={(event) => setWorkflowProfileId(event.target.value)} disabled={!canManage}>
                <option value="">{t('Resolved default')}</option>
                {profiles.map((profile) => (
                  <option key={profile.id} value={profile.id}>{profile.name}</option>
                ))}
              </select>
            </div>

            <div className="detail-field">
              <span className="detail-label">{t('Environment')}</span>
              <select aria-label={t('Environment')} value={environmentId} onChange={(event) => setEnvironmentId(event.target.value)} disabled={!canManage}>
                <option value="">{t('Resolve by environment name')}</option>
                {filteredEnvironments.map((environment) => (
                  <option key={environment.id} value={environment.id}>{environment.name}</option>
                ))}
              </select>
            </div>

            <div className="detail-field">
              <span className="detail-label">{t('Environment Name')}</span>
              <input aria-label={t('Environment Name')} value={environmentName} onChange={(event) => setEnvironmentName(event.target.value)} disabled={!canManage} placeholder={t('staging, production, customer-a')} />
            </div>

            <div className="detail-field">
              <span className="detail-label">{t('Release')}</span>
              <select aria-label={t('Release')} value={releaseId} onChange={(event) => setReleaseId(event.target.value)} disabled={!canManage}>
                <option value="">{t('No linked release')}</option>
                {filteredReleases.map((release) => (
                  <option key={release.id} value={release.id}>{release.title}</option>
                ))}
              </select>
            </div>

            <div className="detail-field">
              <span className="detail-label">{t('Source Ref')}</span>
              <input aria-label={t('Source Ref')} value={sourceRef} onChange={(event) => setSourceRef(event.target.value)} disabled={!canManage} placeholder={t('branch, tag, or commit')} />
            </div>

            <div className="detail-field">
              <span className="detail-label">{t('Mission ID')}</span>
              <input aria-label={t('Mission ID')} value={missionId} onChange={(event) => setMissionId(event.target.value)} disabled={!canManage} placeholder="mis_..." />
            </div>

            <div className="detail-field">
              <span className="detail-label">{t('Voyage ID')}</span>
              <input aria-label={t('Voyage ID')} value={voyageId} onChange={(event) => setVoyageId(event.target.value)} disabled={!canManage} placeholder="voy_..." />
            </div>

            <div className="detail-field detail-field-full">
              <span className="detail-label">{t('Title')}</span>
              <input aria-label={t('Title')} value={title} onChange={(event) => setTitle(event.target.value)} disabled={!canManage} />
            </div>

            <div className="detail-field detail-field-full">
              <span className="detail-label">{t('Summary')}</span>
              <textarea aria-label={t('Summary')} rows={3} value={summary} onChange={(event) => setSummary(event.target.value)} disabled={!canManage} />
            </div>

            <div className="detail-field detail-field-full">
              <span className="detail-label">{t('Notes')}</span>
              <textarea aria-label={t('Notes')} rows={4} value={notes} onChange={(event) => setNotes(event.target.value)} disabled={!canManage} />
            </div>
          </div>

          <label style={{ display: 'inline-flex', alignItems: 'center', gap: '0.45rem', marginTop: '1rem' }}>
            <input type="checkbox" checked={autoExecute} onChange={(event) => setAutoExecute(event.target.checked)} disabled={!canManage} />
            <span>{t('Execute immediately when approval is not required')}</span>
          </label>

          {canManage && (
            <div className="inline-actions" style={{ marginTop: '1rem' }}>
              <button className="btn btn-primary" disabled={saving} onClick={handleSave}>
                {saving ? t('Saving...') : createMode ? t('Create Deployment') : t('Save Deployment')}
              </button>
            </div>
          )}
        </section>

        <section className="card detail-panel">
          <h3>{t('Overview')}</h3>
          <div className="detail-meta-grid">
            <div className="detail-meta-field">
              <span>{t('Status')}</span>
              <strong>{deployment ? <StatusBadge status={deployment.status} /> : t('Draft')}</strong>
            </div>
            <div className="detail-meta-field">
              <span>{t('Verification')}</span>
              <strong>{deployment ? <StatusBadge status={deployment.verificationStatus} /> : t('NotRun')}</strong>
            </div>
            <div className="detail-meta-field">
              <span>{t('Workflow Profile')}</span>
              <strong>{workflowProfileId ? (profileMap.get(workflowProfileId) || workflowProfileId) : t('Resolved default')}</strong>
            </div>
            <div className="detail-meta-field">
              <span>{t('Environment')}</span>
              <strong>{selectedEnvironment?.name || environmentName || '-'}</strong>
            </div>
            <div className="detail-meta-field">
              <span>{t('Check Runs')}</span>
              <strong>{deployment?.checkRunIds.length || 0}</strong>
            </div>
            <div className="detail-meta-field">
              <span>{t('Created')}</span>
              <strong>{deployment ? formatDateTime(deployment.createdUtc) : '-'}</strong>
            </div>
            <div className="detail-meta-field">
              <span>{t('Started')}</span>
              <strong>{deployment?.startedUtc ? formatRelativeTime(deployment.startedUtc) : '-'}</strong>
            </div>
            <div className="detail-meta-field">
              <span>{t('Completed')}</span>
              <strong>{deployment?.completedUtc ? formatRelativeTime(deployment.completedUtc) : '-'}</strong>
            </div>
            <div className="detail-meta-field">
              <span>{t('Approved By')}</span>
              <strong>{deployment?.approvedByUserId || '-'}</strong>
            </div>
            <div className="detail-meta-field">
              <span>{t('Monitoring Window')}</span>
              <strong>{deployment?.monitoringWindowEndsUtc ? formatRelativeTime(deployment.monitoringWindowEndsUtc) : '-'}</strong>
            </div>
            <div className="detail-meta-field">
              <span>{t('Last Monitored')}</span>
              <strong>{deployment?.lastMonitoredUtc ? formatRelativeTime(deployment.lastMonitoredUtc) : '-'}</strong>
            </div>
            <div className="detail-meta-field">
              <span>{t('Regression Alerts')}</span>
              <strong>{deployment?.monitoringFailureCount ?? 0}</strong>
            </div>
            <div className="detail-meta-field">
              <span>{t('Last Alert')}</span>
              <strong>{deployment?.lastRegressionAlertUtc ? formatRelativeTime(deployment.lastRegressionAlertUtc) : '-'}</strong>
            </div>
          </div>

          {!createMode && deployment && (
            <>
              <div className="detail-divider" />
              <h4>{t('Verification Monitoring')}</h4>
              {deployment.latestMonitoringSummary ? (
                <div className="card" style={{ padding: '0.85rem', marginBottom: '1rem' }}>
                  <div className="text-dim" style={{ marginBottom: '0.35rem' }}>{t('Latest monitoring summary')}</div>
                  <div>{deployment.latestMonitoringSummary}</div>
                </div>
              ) : (
                <p className="text-dim">{t('No rollout monitoring summary has been recorded yet for this deployment.')}</p>
              )}

              <div className="detail-divider" />
              <h4>{t('Linked Checks')}</h4>
              {renderLinkedChecks(deployment)}

              <div className="detail-divider" />
              <h4>{t('Runbook Executions')}</h4>
              {runbookExecutions.length === 0 ? (
                <p className="text-dim">{t('No runbook executions are linked to this deployment yet.')}</p>
              ) : (
                <div style={{ display: 'grid', gap: '0.75rem' }}>
                  {runbookExecutions.map((execution) => (
                    <div key={execution.id} className="card" style={{ padding: '0.85rem' }}>
                      <div style={{ display: 'flex', justifyContent: 'space-between', gap: '0.75rem', alignItems: 'center' }}>
                        <div>
                          <strong>{execution.title}</strong>
                          <div className="mono text-dim" style={{ fontSize: '0.78rem' }}>{execution.id}</div>
                        </div>
                        <StatusBadge status={execution.status} />
                      </div>
                      <div className="text-dim" style={{ marginTop: '0.3rem' }}>
                        {execution.environmentName || t('No environment')} • {execution.checkType || t('No check type')}
                      </div>
                      <div className="inline-actions" style={{ marginTop: '0.65rem' }}>
                        <button className="btn btn-sm" onClick={() => navigate(`/runbooks/${execution.runbookId}?executionId=${encodeURIComponent(execution.id)}`)}>
                          {t('Open Runbook')}
                        </button>
                      </div>
                    </div>
                  ))}
                </div>
              )}

              <div className="detail-divider" />
              <h4>{t('Request Summary')}</h4>
              {deployment.requestHistorySummary ? (
                <div className="detail-meta-grid">
                  <div className="detail-meta-field">
                    <span>{t('Total Requests')}</span>
                    <strong>{deployment.requestHistorySummary.totalCount}</strong>
                  </div>
                  <div className="detail-meta-field">
                    <span>{t('Success Rate')}</span>
                    <strong>{deployment.requestHistorySummary.successRate}%</strong>
                  </div>
                  <div className="detail-meta-field">
                    <span>{t('Average Duration')}</span>
                    <strong>{deployment.requestHistorySummary.averageDurationMs} ms</strong>
                  </div>
                  <div className="detail-meta-field">
                    <span>{t('Buckets')}</span>
                    <strong>{deployment.requestHistorySummary.buckets.length}</strong>
                  </div>
                </div>
              ) : (
                <p className="text-dim">{t('No request-history evidence is recorded yet for this deployment.')}</p>
              )}

              <div className="detail-divider" />
              <h4>{t('Identifiers')}</h4>
              <div className="detail-key-list">
                <div className="detail-key-row">
                  <span>{t('Deployment ID')}</span>
                  <div className="detail-key-actions">
                    <code>{deployment.id}</code>
                    <CopyButton text={deployment.id} title={t('Copy deployment ID')} />
                  </div>
                </div>
                {deployment.releaseId && (
                  <div className="detail-key-row">
                    <span>{t('Release')}</span>
                    <div className="detail-key-actions">
                      <Link to={`/releases/${deployment.releaseId}`}>{deployment.releaseId}</Link>
                    </div>
                  </div>
                )}
                {deployment.missionId && (
                  <div className="detail-key-row">
                    <span>{t('Mission')}</span>
                    <div className="detail-key-actions">
                      <Link to={`/missions/${deployment.missionId}`}>{deployment.missionId}</Link>
                    </div>
                  </div>
                )}
                {deployment.voyageId && (
                  <div className="detail-key-row">
                    <span>{t('Voyage')}</span>
                    <div className="detail-key-actions">
                      <Link to={`/voyages/${deployment.voyageId}`}>{deployment.voyageId}</Link>
                    </div>
                  </div>
                )}
              </div>
            </>
          )}
        </section>
      </div>
    </div>
  );
}
