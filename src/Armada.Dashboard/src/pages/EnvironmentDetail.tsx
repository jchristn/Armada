import { useEffect, useMemo, useState } from 'react';
import { useNavigate, useParams, useSearchParams } from 'react-router-dom';
import {
  createEnvironment,
  deleteEnvironment,
  getEnvironment,
  listVessels,
  updateEnvironment,
} from '../api/client';
import { listAll } from '../api/paging';
import type {
  DeploymentEnvironment,
  DeploymentEnvironmentUpsertRequest,
  DeploymentVerificationDefinition,
  EnvironmentKind,
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
import { buildEnvironmentDuplicatePayload } from '../lib/duplicates';
import { createVerificationDefinition, ENVIRONMENT_KINDS, parseHeaderLines, serializeHeaderLines } from '../lib/environmentForm';
import { useDeleteProgress } from '../lib/useDeleteProgress';

export default function EnvironmentDetail() {
  const { id } = useParams<{ id: string }>();
  const [searchParams] = useSearchParams();
  const navigate = useNavigate();
  const { isAdmin, isTenantAdmin } = useAuth();
  const { t, formatDateTime, formatRelativeTime } = useLocale();
  const { pushToast } = useNotifications();
  const deletion = useDeleteProgress();

  const createMode = id === 'new';
  const canManage = isAdmin || isTenantAdmin;

  const tips = {
    vessel: t('Repository this environment belongs to. Deployments can only target environments of their own vessel.'),
    name: t('Environment name, such as staging or production. It must match the environment name in the workflow profile so the right deploy and rollback commands run.'),
    kind: t('Classification of the environment: Development, Test, Staging, Production, or Custom. Used for labeling and filtering.'),
    configurationSource: t('Where this environment\'s settings live, for operators to reference, such as Helm values or appsettings.Production.json. Not read by Armada.'),
    baseUrl: t('Root URL of the deployed service, such as https://staging.example.com. Health checks and relative verification paths are resolved against it.'),
    healthEndpoint: t('Path or URL requested after each deploy, such as /health. The health check runs only when both this and the Base URL are set.'),
    description: t('Short description of what this environment is for. Shown in the list tooltip and searchable.'),
    accessNotes: t('How operators reach or sign in to this environment, such as a VPN or bastion host. Do not paste secrets here.'),
    deploymentRules: t('Freeze windows, policies, or constraints operators should follow. Informational only; use Requires approval to enforce a gate.'),
    requiresApproval: t('When checked, new deployments to this environment wait in Pending Approval until someone approves or denies them.'),
    isDefault: t('Target used when a deployment names no environment. Only one per vessel; setting it clears the flag on the vessel\'s other environments.'),
    active: t('Inactive environments are skipped when Armada picks a default target and are not rollout-monitored.'),
    monitoringWindow: t('Minutes after a successful, verified deploy during which Armada keeps re-running the health and verification checks. 0 turns monitoring off; maximum 1440.'),
    monitoringInterval: t('Seconds between rollout-monitoring passes, from 30 to 3600. Default 300.'),
    alertOnRegression: t('Record the time of each new regression, shown as Last Alert on the deployment. A failed monitoring pass marks it Verification Failed either way.'),
    defName: t('Label for this check, shown on the check runs it produces, such as API status.'),
    defMethod: t('HTTP method to send, such as GET or POST. Blank means GET.'),
    defPath: t('Path appended to the Base URL, such as /api/status, or a full https:// URL.'),
    defExpectedStatus: t('HTTP status code the response must return, such as 200. Clear it to accept any status.'),
    defMustContain: t('Text the response body must contain, case-insensitive, such as "healthy". Blank skips the body check.'),
    defHeaders: t('Request headers, one per line as Name: value, such as Authorization: Bearer abc. Stored in plain text.'),
    defBody: t('Optional request body for POST or PUT checks, such as a JSON document.'),
    defActive: t('Only active definitions run during deploy verification and rollout monitoring.'),
  };

  const [environment, setEnvironment] = useState<DeploymentEnvironment | null>(null);
  const [vessels, setVessels] = useState<Vessel[]>([]);
  const [vesselId, setVesselId] = useState('');
  const [name, setName] = useState('Environment');
  const [description, setDescription] = useState('');
  const [kind, setKind] = useState<EnvironmentKind>('Development');
  const [configurationSource, setConfigurationSource] = useState('');
  const [baseUrl, setBaseUrl] = useState('');
  const [healthEndpoint, setHealthEndpoint] = useState('');
  const [accessNotes, setAccessNotes] = useState('');
  const [deploymentRules, setDeploymentRules] = useState('');
  const [verificationDefinitions, setVerificationDefinitions] = useState<DeploymentVerificationDefinition[]>([]);
  const [rolloutMonitoringWindowMinutes, setRolloutMonitoringWindowMinutes] = useState(60);
  const [rolloutMonitoringIntervalSeconds, setRolloutMonitoringIntervalSeconds] = useState(300);
  const [alertOnRegression, setAlertOnRegression] = useState(true);
  const [requiresApproval, setRequiresApproval] = useState(false);
  const [isDefault, setIsDefault] = useState(false);
  const [active, setActive] = useState(true);
  const [loading, setLoading] = useState(!createMode);
  const [saving, setSaving] = useState(false);
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

    listAll(listVessels).then((result) => {
      if (!cancelled) setVessels(result.objects || []);
    }).catch(() => {
      if (!cancelled) setError(t('Failed to load vessels.'));
    });

    return () => {
      cancelled = true;
    };
  }, [t]);

  useEffect(() => {
    if (createMode || !id) return;

    let mounted = true;
    const environmentId = id;
    async function load() {
      try {
        setLoading(true);
        const result = await getEnvironment(environmentId);
        if (!mounted) return;
        setEnvironment(result);
        setVesselId(result.vesselId || '');
        setName(result.name);
        setDescription(result.description || '');
        setKind(result.kind);
        setConfigurationSource(result.configurationSource || '');
        setBaseUrl(result.baseUrl || '');
        setHealthEndpoint(result.healthEndpoint || '');
        setAccessNotes(result.accessNotes || '');
        setDeploymentRules(result.deploymentRules || '');
        setVerificationDefinitions(result.verificationDefinitions || []);
        setRolloutMonitoringWindowMinutes(result.rolloutMonitoringWindowMinutes || 60);
        setRolloutMonitoringIntervalSeconds(result.rolloutMonitoringIntervalSeconds || 300);
        setAlertOnRegression(result.alertOnRegression);
        setRequiresApproval(result.requiresApproval);
        setIsDefault(result.isDefault);
        setActive(result.active);
        setError('');
      } catch (err: unknown) {
        if (mounted) setError(err instanceof Error ? err.message : t('Failed to load environment.'));
      } finally {
        if (mounted) setLoading(false);
      }
    }

    load();
    return () => {
      mounted = false;
    };
  }, [createMode, id, t]);

  useEffect(() => {
    if (!createMode) return;

    const requestedVesselId = searchParams.get('vesselId');
    const requestedKind = searchParams.get('kind');
    const requestedName = searchParams.get('name');

    if (requestedVesselId) setVesselId(requestedVesselId);
    if (requestedName) setName(requestedName);
    if (requestedKind && ENVIRONMENT_KINDS.includes(requestedKind as EnvironmentKind)) {
      setKind(requestedKind as EnvironmentKind);
    }
  }, [createMode, searchParams]);

  const selectedVessel = useMemo(() => vessels.find((candidate) => candidate.id === vesselId) || null, [vesselId, vessels]);

  function buildPayload(): DeploymentEnvironmentUpsertRequest {
    return {
      vesselId: vesselId || null,
      name: name.trim() || null,
      description: description.trim() || null,
      kind,
      configurationSource: configurationSource.trim() || null,
      baseUrl: baseUrl.trim() || null,
      healthEndpoint: healthEndpoint.trim() || null,
      accessNotes: accessNotes.trim() || null,
      deploymentRules: deploymentRules.trim() || null,
      verificationDefinitions,
      rolloutMonitoringWindowMinutes,
      rolloutMonitoringIntervalSeconds,
      alertOnRegression,
      requiresApproval,
      isDefault,
      active,
    };
  }

  async function handleSave() {
    if (!canManage) return;
    try {
      setSaving(true);
      const payload = buildPayload();
      if (createMode) {
        const created = await createEnvironment(payload);
        pushToast('success', t('Environment "{{name}}" created.', { name: created.name }));
        navigate(`/environments/${created.id}`);
        return;
      }

      if (!id) return;
      const updated = await updateEnvironment(id, payload);
      setEnvironment(updated);
      setVesselId(updated.vesselId || '');
      setName(updated.name);
      setDescription(updated.description || '');
      setKind(updated.kind);
      setConfigurationSource(updated.configurationSource || '');
      setBaseUrl(updated.baseUrl || '');
      setHealthEndpoint(updated.healthEndpoint || '');
      setAccessNotes(updated.accessNotes || '');
      setDeploymentRules(updated.deploymentRules || '');
      setVerificationDefinitions(updated.verificationDefinitions || []);
      setRolloutMonitoringWindowMinutes(updated.rolloutMonitoringWindowMinutes || 60);
      setRolloutMonitoringIntervalSeconds(updated.rolloutMonitoringIntervalSeconds || 300);
      setAlertOnRegression(updated.alertOnRegression);
      setRequiresApproval(updated.requiresApproval);
      setIsDefault(updated.isDefault);
      setActive(updated.active);
      pushToast('success', t('Environment "{{name}}" saved.', { name: updated.name }));
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Save failed.'));
    } finally {
      setSaving(false);
    }
  }

  function handleDelete() {
    if (!environment || !canManage) return;
    setConfirm({
      open: true,
      title: t('Delete Environment'),
      message: t('Delete "{{name}}"? This removes only the environment record.', { name: environment.name }),
      onConfirm: async () => {
        setConfirm((current) => ({ ...current, open: false }));
        if (!(await deletion.run({ kind: 'environment', action: () => deleteEnvironment(environment.id), errorMessage: (err) => (err instanceof Error ? err.message : t('Delete failed.')) }))) return;
        pushToast('warning', t('Environment "{{name}}" deleted.', { name: environment.name }));
        navigate('/environments');
      },
    });
  }

  async function handleDuplicate() {
    if (!environment || !canManage) return;
    try {
      setSaving(true);
      const created = await createEnvironment(buildEnvironmentDuplicatePayload(environment));
      pushToast('success', t('Environment "{{name}}" duplicated.', { name: created.name }));
      navigate(`/environments/${created.id}`);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Duplicate failed.'));
    } finally {
      setSaving(false);
    }
  }

  if (loading) return <p className="text-dim">{t('Loading...')}</p>;

  return (
    <div>
      <PageHeader
        breadcrumb={[
          { label: t('Environments'), to: '/environments' },
          { label: createMode ? t('New Environment') : name },
        ]}
        title={createMode ? t('Create Environment') : name}
        actions={
          <>
            {!createMode && (
              <button
                className="btn btn-sm"
                onClick={() => navigate('/deployments/new', {
                  state: {
                    prefill: {
                      vesselId: vesselId || null,
                      environmentId: environment?.id || null,
                      environmentName: environment?.name || null,
                      title: `${name} Deploy`,
                    },
                  },
                })}
                title={t('Open a new deployment prefilled with this environment and its vessel. Nothing runs until you create it.')}
              >
                {t('Deploy')}
              </button>
            )}
            {!createMode && (
              <button
                className="btn btn-sm"
                onClick={() => navigate('/checks', {
                  state: {
                    prefill: {
                      vesselId: vesselId || null,
                      label: name,
                      environmentName: name,
                    },
                  },
                })}
                title={t('Go to Checks with this vessel and environment name prefilled to run a check against it.')}
              >
                {t('Run Check')}
              </button>
            )}
            {!createMode && (
              <button
                className="btn btn-sm"
                onClick={() => navigate('/incidents/new', {
                  state: {
                    prefill: {
                      vesselId: vesselId || null,
                      environmentId: environment?.id || null,
                      environmentName: environment?.name || name,
                      title: `${name} Incident`,
                      severity: kind === 'Production' ? 'High' : 'Medium',
                    },
                  },
                })}
                title={t('Open a new incident prefilled with this environment and vessel. Nothing is saved until you create it.')}
              >
                {t('Create Incident')}
              </button>
            )}
            {!createMode && (
              <button
                className="btn btn-sm"
                onClick={() => navigate('/runbooks', {
                  state: {
                    prefillExecution: {
                      environmentId: environment?.id || null,
                      environmentName: environment?.name || name,
                      checkType: verificationDefinitions.length > 0 ? 'DeploymentVerification' : 'HealthCheck',
                    },
                  },
                })}
                title={t('Go to Runbooks to start a runbook execution prefilled with this environment.')}
              >
                {t('Runbook')}
              </button>
            )}
            {!createMode && selectedVessel && (
              <button className="btn btn-sm" onClick={() => navigate(`/workspace/${selectedVessel.id}`)} title={t('Open the vessel workspace to browse its files, branches, and readiness.')}>
                {t('Open Workspace')}
              </button>
            )}
            {!createMode && (
              <button className="btn btn-sm" onClick={() => setJsonData({ open: true, title: name, data: environment })} title={t('Show the raw environment record as returned by the API.')}>
                {t('View JSON')}
              </button>
            )}
            {!createMode && canManage && (
              <button className="btn btn-sm" disabled={saving} onClick={() => void handleDuplicate()} title={t('Create a copy with the same settings and verification checks, not set as default, and open it.')}>
                {t('Duplicate')}
              </button>
            )}
            {!createMode && canManage && (
              <button className="btn btn-sm btn-danger" onClick={handleDelete} title={t('Delete this environment record. Existing deployments keep their environment name. Cannot be undone.')}>
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
      {deletion.dialog}

      <div className="detail-grid">
        <section className="card detail-panel">
          <h3>{t('Environment')}</h3>
          <div className="detail-form-grid">
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
              <span className="detail-label" title={tips.name}>{t('Name')}</span>
              <input aria-label={t('Name')} title={tips.name} value={name} onChange={(event) => setName(event.target.value)} disabled={!canManage} />
            </div>

            <div className="detail-field">
              <span className="detail-label" title={tips.kind}>{t('Kind')}</span>
              <select aria-label={t('Kind')} title={tips.kind} value={kind} onChange={(event) => setKind(event.target.value as EnvironmentKind)} disabled={!canManage}>
                {ENVIRONMENT_KINDS.map((value) => (
                  <option key={value} value={value}>{value}</option>
                ))}
              </select>
            </div>

            <div className="detail-field">
              <span className="detail-label" title={tips.configurationSource}>{t('Configuration Source')}</span>
              <input aria-label={t('Configuration Source')} title={tips.configurationSource} value={configurationSource} onChange={(event) => setConfigurationSource(event.target.value)} disabled={!canManage} placeholder={t('e.g. Helm values, appsettings.Production.json, Azure slot config')} />
            </div>

            <div className="detail-field">
              <span className="detail-label" title={tips.baseUrl}>{t('Base URL')}</span>
              <input aria-label={t('Base URL')} title={tips.baseUrl} value={baseUrl} onChange={(event) => setBaseUrl(event.target.value)} disabled={!canManage} placeholder="https://service.example.com" />
            </div>

            <div className="detail-field">
              <span className="detail-label" title={tips.healthEndpoint}>{t('Health Endpoint')}</span>
              <input aria-label={t('Health Endpoint')} title={tips.healthEndpoint} value={healthEndpoint} onChange={(event) => setHealthEndpoint(event.target.value)} disabled={!canManage} placeholder="/health or https://service.example.com/health" />
            </div>

            <div className="detail-field detail-field-full">
              <span className="detail-label" title={tips.description}>{t('Description')}</span>
              <textarea aria-label={t('Description')} title={tips.description} rows={3} value={description} onChange={(event) => setDescription(event.target.value)} disabled={!canManage} />
            </div>

            <div className="detail-field detail-field-full">
              <span className="detail-label" title={tips.accessNotes}>{t('Access Notes')}</span>
              <textarea aria-label={t('Access Notes')} title={tips.accessNotes} rows={4} value={accessNotes} onChange={(event) => setAccessNotes(event.target.value)} disabled={!canManage} placeholder={t('How do operators reach or authenticate to this environment?')} />
            </div>

            <div className="detail-field detail-field-full">
              <span className="detail-label" title={tips.deploymentRules}>{t('Deployment Rules')}</span>
              <textarea aria-label={t('Deployment Rules')} title={tips.deploymentRules} rows={4} value={deploymentRules} onChange={(event) => setDeploymentRules(event.target.value)} disabled={!canManage} placeholder={t('Document freeze windows, approval policy, maintenance constraints, or rollout notes.')} />
            </div>
          </div>

          <div className="detail-divider" />
          <h4 title={t('How long and how often Armada keeps checking a deployment after it succeeds. Monitoring needs a health endpoint or an active verification definition.')}>{t('Verification & Monitoring')}</h4>
          <div className="detail-form-grid">
            <div className="detail-field">
              <span className="detail-label" title={tips.monitoringWindow}>{t('Rollout Monitoring Window (minutes)')}</span>
              <input aria-label={t('Rollout Monitoring Window (minutes)')} title={tips.monitoringWindow} type="number" min={0} value={rolloutMonitoringWindowMinutes} onChange={(event) => setRolloutMonitoringWindowMinutes(Math.max(0, parseInt(event.target.value || '0', 10) || 0))} disabled={!canManage} />
            </div>
            <div className="detail-field">
              <span className="detail-label" title={tips.monitoringInterval}>{t('Monitoring Interval (seconds)')}</span>
              <input aria-label={t('Monitoring Interval (seconds)')} title={tips.monitoringInterval} type="number" min={30} value={rolloutMonitoringIntervalSeconds} onChange={(event) => setRolloutMonitoringIntervalSeconds(Math.max(30, parseInt(event.target.value || '30', 10) || 30))} disabled={!canManage} />
            </div>
          </div>

          <label title={tips.alertOnRegression} style={{ display: 'inline-flex', alignItems: 'center', gap: '0.45rem', marginTop: '1rem' }}>
            <input type="checkbox" title={tips.alertOnRegression} checked={alertOnRegression} onChange={(event) => setAlertOnRegression(event.target.checked)} disabled={!canManage} />
            <span>{t('Record regression alerts during rollout monitoring')}</span>
          </label>

          <div className="detail-divider" />
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: '0.75rem' }}>
            <h4 title={t('HTTP checks run after every deploy to this environment and on each rollout-monitoring pass. A check passes when its status and body match.')}>{t('Reusable Verification Definitions')}</h4>
            {canManage && (
              <button className="btn btn-sm" onClick={() => setVerificationDefinitions((current) => [...current, createVerificationDefinition()])} title={t('Add an HTTP verification check. It is kept only after you save the environment.')}>
                {t('Add Verification')}
              </button>
            )}
          </div>
          {verificationDefinitions.length === 0 ? (
            <p className="text-dim">{t('No reusable verification definitions are configured for this environment yet.')}</p>
          ) : (
            <div style={{ display: 'grid', gap: '0.85rem' }}>
              {verificationDefinitions.map((definition, index) => (
                <div key={definition.id} className="card" style={{ padding: '0.85rem' }}>
                  <div className="detail-form-grid">
                    <div className="detail-field">
                      <span className="detail-label" title={tips.defName}>{t('Name')}</span>
                      <input aria-label={t('Name')} title={tips.defName} value={definition.name} onChange={(event) => setVerificationDefinitions((current) => current.map((item, itemIndex) => itemIndex === index ? { ...item, name: event.target.value } : item))} disabled={!canManage} />
                    </div>
                    <div className="detail-field">
                      <span className="detail-label" title={tips.defMethod}>{t('Method')}</span>
                      <input aria-label={t('Method')} title={tips.defMethod} value={definition.method} onChange={(event) => setVerificationDefinitions((current) => current.map((item, itemIndex) => itemIndex === index ? { ...item, method: event.target.value.toUpperCase() } : item))} disabled={!canManage} />
                    </div>
                    <div className="detail-field detail-field-full">
                      <span className="detail-label" title={tips.defPath}>{t('Path')}</span>
                      <input aria-label={t('Path')} title={tips.defPath} value={definition.path} onChange={(event) => setVerificationDefinitions((current) => current.map((item, itemIndex) => itemIndex === index ? { ...item, path: event.target.value } : item))} disabled={!canManage} placeholder="/health or /api/status" />
                    </div>
                    <div className="detail-field">
                      <span className="detail-label" title={tips.defExpectedStatus}>{t('Expected Status')}</span>
                      <input aria-label={t('Expected Status')} title={tips.defExpectedStatus} type="number" min={100} max={599} value={definition.expectedStatusCode ?? 200} onChange={(event) => setVerificationDefinitions((current) => current.map((item, itemIndex) => itemIndex === index ? { ...item, expectedStatusCode: parseInt(event.target.value || '0', 10) || null } : item))} disabled={!canManage} />
                    </div>
                    <div className="detail-field">
                      <span className="detail-label" title={tips.defMustContain}>{t('Must Contain Text')}</span>
                      <input aria-label={t('Must Contain Text')} title={tips.defMustContain} value={definition.mustContainText || ''} onChange={(event) => setVerificationDefinitions((current) => current.map((item, itemIndex) => itemIndex === index ? { ...item, mustContainText: event.target.value || null } : item))} disabled={!canManage} />
                    </div>
                    <div className="detail-field detail-field-full">
                      <span className="detail-label" title={tips.defHeaders}>{t('Headers')}</span>
                      <textarea aria-label={t('Headers')} title={tips.defHeaders} rows={3} value={serializeHeaderLines(definition.headers)} onChange={(event) => setVerificationDefinitions((current) => current.map((item, itemIndex) => itemIndex === index ? { ...item, headers: parseHeaderLines(event.target.value) } : item))} disabled={!canManage} placeholder={t('Header-Name: value')} />
                    </div>
                    <div className="detail-field detail-field-full">
                      <span className="detail-label" title={tips.defBody}>{t('Request Body')}</span>
                      <textarea aria-label={t('Request Body')} title={tips.defBody} rows={4} value={definition.requestBody || ''} onChange={(event) => setVerificationDefinitions((current) => current.map((item, itemIndex) => itemIndex === index ? { ...item, requestBody: event.target.value || null } : item))} disabled={!canManage} />
                    </div>
                  </div>

                  <div className="inline-actions" style={{ marginTop: '0.75rem' }}>
                    <label title={tips.defActive} style={{ display: 'inline-flex', alignItems: 'center', gap: '0.45rem' }}>
                      <input type="checkbox" title={tips.defActive} checked={definition.active} onChange={(event) => setVerificationDefinitions((current) => current.map((item, itemIndex) => itemIndex === index ? { ...item, active: event.target.checked } : item))} disabled={!canManage} />
                      <span>{t('Active')}</span>
                    </label>
                    {canManage && (
                      <button className="btn btn-sm btn-danger" onClick={() => setVerificationDefinitions((current) => current.filter((_, itemIndex) => itemIndex !== index))} title={t('Remove this verification check. Takes effect when you save the environment.')}>
                        {t('Remove')}
                      </button>
                    )}
                  </div>
                </div>
              ))}
            </div>
          )}

          <div style={{ display: 'flex', gap: '1rem', flexWrap: 'wrap', marginTop: '1rem' }}>
            <label title={tips.requiresApproval} style={{ display: 'inline-flex', alignItems: 'center', gap: '0.45rem' }}>
              <input type="checkbox" title={tips.requiresApproval} checked={requiresApproval} onChange={(event) => setRequiresApproval(event.target.checked)} disabled={!canManage} />
              <span>{t('Requires approval')}</span>
            </label>
            <label title={tips.isDefault} style={{ display: 'inline-flex', alignItems: 'center', gap: '0.45rem' }}>
              <input type="checkbox" title={tips.isDefault} checked={isDefault} onChange={(event) => setIsDefault(event.target.checked)} disabled={!canManage} />
              <span>{t('Default environment for vessel')}</span>
            </label>
            <label title={tips.active} style={{ display: 'inline-flex', alignItems: 'center', gap: '0.45rem' }}>
              <input type="checkbox" title={tips.active} checked={active} onChange={(event) => setActive(event.target.checked)} disabled={!canManage} />
              <span>{t('Active')}</span>
            </label>
          </div>

          {canManage && (
            <div className="inline-actions" style={{ marginTop: '1rem' }}>
              <button className="btn btn-primary" disabled={saving} onClick={handleSave} title={createMode ? t('Create this environment for the selected vessel.') : t('Save changes to this environment. Existing deployments are not re-run.')}>
                {saving ? t('Saving...') : t('Save Environment')}
              </button>
            </div>
          )}
        </section>

        <section className="card detail-panel">
          <h3>{t('Overview')}</h3>
          <div className="detail-meta-grid">
            <div className="detail-field" title={tips.vessel}><span className="detail-label">{t('Vessel')}</span><span>{selectedVessel?.name || vesselId || '-'}</span></div>
            <div className="detail-field" title={tips.kind}><span className="detail-label">{t('Kind')}</span><span>{kind}</span></div>
            <div className="detail-field" title={tips.isDefault}><span className="detail-label">{t('Default')}</span><span>{isDefault ? t('Yes') : t('No')}</span></div>
            <div className="detail-field" title={tips.requiresApproval}><span className="detail-label">{t('Approval')}</span><span>{requiresApproval ? t('Required') : t('Not required')}</span></div>
            <div className="detail-field" title={tips.active}><span className="detail-label">{t('Status')}</span><span>{active ? t('Active') : t('Inactive')}</span></div>
            <div className="detail-field" title={tips.healthEndpoint}><span className="detail-label">{t('Health')}</span><span>{healthEndpoint || '-'}</span></div>
            <div className="detail-field" title={t('Number of HTTP verification checks defined for this environment, active or not.')}><span className="detail-label">{t('Verification Definitions')}</span><span>{verificationDefinitions.length}</span></div>
            <div className="detail-field" title={tips.monitoringWindow}><span className="detail-label">{t('Monitoring Window')}</span><span>{rolloutMonitoringWindowMinutes} {t('minutes')}</span></div>
            <div className="detail-field" title={tips.monitoringInterval}><span className="detail-label">{t('Monitoring Interval')}</span><span>{rolloutMonitoringIntervalSeconds} {t('seconds')}</span></div>
            <div className="detail-field" title={tips.alertOnRegression}><span className="detail-label">{t('Regression Alerts')}</span><span>{alertOnRegression ? t('Enabled') : t('Disabled')}</span></div>
          </div>

          {!createMode && environment && (
            <>
              <div className="card-sep" />
              <div className="detail-meta-grid">
                <div className="detail-field">
                  <span className="detail-label">{t('Environment ID')}</span>
                  <span className="mono">
                    {environment.id}
                    <CopyButton text={environment.id} />
                  </span>
                </div>
                <div className="detail-field"><span className="detail-label">{t('Created')}</span><span title={formatDateTime(environment.createdUtc)}>{formatRelativeTime(environment.createdUtc)}</span></div>
                <div className="detail-field"><span className="detail-label">{t('Last Updated')}</span><span title={formatDateTime(environment.lastUpdateUtc)}>{formatRelativeTime(environment.lastUpdateUtc)}</span></div>
              </div>
            </>
          )}
        </section>
      </div>
    </div>
  );
}
