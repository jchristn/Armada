import { useEffect, useMemo, useState } from 'react';
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom';
import {
  createEnvironment,
  deleteEnvironment,
  getEnvironment,
  listVessels,
  updateEnvironment,
} from '../api/client';
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

const ENVIRONMENT_KINDS: EnvironmentKind[] = ['Development', 'Test', 'Staging', 'Production', 'CustomerHosted', 'Custom'];

function parseHeaderLines(value: string) {
  const headers: Record<string, string> = {};
  for (const rawLine of value.split(/\r?\n/)) {
    const line = rawLine.trim();
    if (!line) continue;
    const separatorIndex = line.indexOf(':');
    if (separatorIndex < 1) continue;
    const key = line.substring(0, separatorIndex).trim();
    const headerValue = line.substring(separatorIndex + 1).trim();
    if (!key) continue;
    headers[key] = headerValue;
  }
  return headers;
}

function serializeHeaderLines(value: Record<string, string> | null | undefined) {
  return Object.entries(value || {}).map(([key, headerValue]) => `${key}: ${headerValue}`).join('\n');
}

function createVerificationDefinition(): DeploymentVerificationDefinition {
  return {
    id: `dvd_${Date.now()}_${Math.random().toString(36).slice(2, 8)}`,
    name: 'Verification',
    method: 'GET',
    path: '/health',
    requestBody: null,
    headers: {},
    expectedStatusCode: 200,
    mustContainText: null,
    active: true,
  };
}

export default function EnvironmentDetail() {
  const { id } = useParams<{ id: string }>();
  const [searchParams] = useSearchParams();
  const navigate = useNavigate();
  const { isAdmin, isTenantAdmin } = useAuth();
  const { t, formatDateTime, formatRelativeTime } = useLocale();
  const { pushToast } = useNotifications();

  const createMode = id === 'new';
  const canManage = isAdmin || isTenantAdmin;

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

    listVessels({ pageSize: 9999 }).then((result) => {
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
        try {
          await deleteEnvironment(environment.id);
          pushToast('warning', t('Environment "{{name}}" deleted.', { name: environment.name }));
          navigate('/environments');
        } catch (err: unknown) {
          setError(err instanceof Error ? err.message : t('Delete failed.'));
        }
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
        breadcrumb={
          <>
            <Link to="/environments">{t('Environments')}</Link> <span className="breadcrumb-sep">&gt;</span> <span>{createMode ? t('New Environment') : name}</span>
          </>
        }
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
              >
                {t('Runbook')}
              </button>
            )}
            {!createMode && selectedVessel && (
              <button className="btn btn-sm" onClick={() => navigate(`/workspace/${selectedVessel.id}`)}>
                {t('Open Workspace')}
              </button>
            )}
            {!createMode && (
              <button className="btn btn-sm" onClick={() => setJsonData({ open: true, title: name, data: environment })}>
                {t('View JSON')}
              </button>
            )}
            {!createMode && canManage && (
              <button className="btn btn-sm" disabled={saving} onClick={() => void handleDuplicate()}>
                {t('Duplicate')}
              </button>
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
          <h3>{t('Environment')}</h3>
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
              <span className="detail-label">{t('Name')}</span>
              <input aria-label={t('Name')} value={name} onChange={(event) => setName(event.target.value)} disabled={!canManage} />
            </div>

            <div className="detail-field">
              <span className="detail-label">{t('Kind')}</span>
              <select aria-label={t('Kind')} value={kind} onChange={(event) => setKind(event.target.value as EnvironmentKind)} disabled={!canManage}>
                {ENVIRONMENT_KINDS.map((value) => (
                  <option key={value} value={value}>{value}</option>
                ))}
              </select>
            </div>

            <div className="detail-field">
              <span className="detail-label">{t('Configuration Source')}</span>
              <input aria-label={t('Configuration Source')} value={configurationSource} onChange={(event) => setConfigurationSource(event.target.value)} disabled={!canManage} placeholder={t('e.g. Helm values, appsettings.Production.json, Azure slot config')} />
            </div>

            <div className="detail-field">
              <span className="detail-label">{t('Base URL')}</span>
              <input aria-label={t('Base URL')} value={baseUrl} onChange={(event) => setBaseUrl(event.target.value)} disabled={!canManage} placeholder="https://service.example.com" />
            </div>

            <div className="detail-field">
              <span className="detail-label">{t('Health Endpoint')}</span>
              <input aria-label={t('Health Endpoint')} value={healthEndpoint} onChange={(event) => setHealthEndpoint(event.target.value)} disabled={!canManage} placeholder="/health or https://service.example.com/health" />
            </div>

            <div className="detail-field detail-field-full">
              <span className="detail-label">{t('Description')}</span>
              <textarea aria-label={t('Description')} rows={3} value={description} onChange={(event) => setDescription(event.target.value)} disabled={!canManage} />
            </div>

            <div className="detail-field detail-field-full">
              <span className="detail-label">{t('Access Notes')}</span>
              <textarea aria-label={t('Access Notes')} rows={4} value={accessNotes} onChange={(event) => setAccessNotes(event.target.value)} disabled={!canManage} placeholder={t('How do operators reach or authenticate to this environment?')} />
            </div>

            <div className="detail-field detail-field-full">
              <span className="detail-label">{t('Deployment Rules')}</span>
              <textarea aria-label={t('Deployment Rules')} rows={4} value={deploymentRules} onChange={(event) => setDeploymentRules(event.target.value)} disabled={!canManage} placeholder={t('Document freeze windows, approval policy, maintenance constraints, or rollout notes.')} />
            </div>
          </div>

          <div className="detail-divider" />
          <h4>{t('Verification & Monitoring')}</h4>
          <div className="detail-form-grid">
            <div className="detail-field">
              <span className="detail-label">{t('Rollout Monitoring Window (minutes)')}</span>
              <input aria-label={t('Rollout Monitoring Window (minutes)')} type="number" min={0} value={rolloutMonitoringWindowMinutes} onChange={(event) => setRolloutMonitoringWindowMinutes(Math.max(0, parseInt(event.target.value || '0', 10) || 0))} disabled={!canManage} />
            </div>
            <div className="detail-field">
              <span className="detail-label">{t('Monitoring Interval (seconds)')}</span>
              <input aria-label={t('Monitoring Interval (seconds)')} type="number" min={30} value={rolloutMonitoringIntervalSeconds} onChange={(event) => setRolloutMonitoringIntervalSeconds(Math.max(30, parseInt(event.target.value || '30', 10) || 30))} disabled={!canManage} />
            </div>
          </div>

          <label style={{ display: 'inline-flex', alignItems: 'center', gap: '0.45rem', marginTop: '1rem' }}>
            <input type="checkbox" checked={alertOnRegression} onChange={(event) => setAlertOnRegression(event.target.checked)} disabled={!canManage} />
            <span>{t('Record regression alerts during rollout monitoring')}</span>
          </label>

          <div className="detail-divider" />
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: '0.75rem' }}>
            <h4>{t('Reusable Verification Definitions')}</h4>
            {canManage && (
              <button className="btn btn-sm" onClick={() => setVerificationDefinitions((current) => [...current, createVerificationDefinition()])}>
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
                      <span className="detail-label">{t('Name')}</span>
                      <input aria-label={t('Name')} value={definition.name} onChange={(event) => setVerificationDefinitions((current) => current.map((item, itemIndex) => itemIndex === index ? { ...item, name: event.target.value } : item))} disabled={!canManage} />
                    </div>
                    <div className="detail-field">
                      <span className="detail-label">{t('Method')}</span>
                      <input aria-label={t('Method')} value={definition.method} onChange={(event) => setVerificationDefinitions((current) => current.map((item, itemIndex) => itemIndex === index ? { ...item, method: event.target.value.toUpperCase() } : item))} disabled={!canManage} />
                    </div>
                    <div className="detail-field detail-field-full">
                      <span className="detail-label">{t('Path')}</span>
                      <input aria-label={t('Path')} value={definition.path} onChange={(event) => setVerificationDefinitions((current) => current.map((item, itemIndex) => itemIndex === index ? { ...item, path: event.target.value } : item))} disabled={!canManage} placeholder="/health or /api/status" />
                    </div>
                    <div className="detail-field">
                      <span className="detail-label">{t('Expected Status')}</span>
                      <input aria-label={t('Expected Status')} type="number" min={100} max={599} value={definition.expectedStatusCode ?? 200} onChange={(event) => setVerificationDefinitions((current) => current.map((item, itemIndex) => itemIndex === index ? { ...item, expectedStatusCode: parseInt(event.target.value || '0', 10) || null } : item))} disabled={!canManage} />
                    </div>
                    <div className="detail-field">
                      <span className="detail-label">{t('Must Contain Text')}</span>
                      <input aria-label={t('Must Contain Text')} value={definition.mustContainText || ''} onChange={(event) => setVerificationDefinitions((current) => current.map((item, itemIndex) => itemIndex === index ? { ...item, mustContainText: event.target.value || null } : item))} disabled={!canManage} />
                    </div>
                    <div className="detail-field detail-field-full">
                      <span className="detail-label">{t('Headers')}</span>
                      <textarea aria-label={t('Headers')} rows={3} value={serializeHeaderLines(definition.headers)} onChange={(event) => setVerificationDefinitions((current) => current.map((item, itemIndex) => itemIndex === index ? { ...item, headers: parseHeaderLines(event.target.value) } : item))} disabled={!canManage} placeholder={t('Header-Name: value')} />
                    </div>
                    <div className="detail-field detail-field-full">
                      <span className="detail-label">{t('Request Body')}</span>
                      <textarea aria-label={t('Request Body')} rows={4} value={definition.requestBody || ''} onChange={(event) => setVerificationDefinitions((current) => current.map((item, itemIndex) => itemIndex === index ? { ...item, requestBody: event.target.value || null } : item))} disabled={!canManage} />
                    </div>
                  </div>

                  <div className="inline-actions" style={{ marginTop: '0.75rem' }}>
                    <label style={{ display: 'inline-flex', alignItems: 'center', gap: '0.45rem' }}>
                      <input type="checkbox" checked={definition.active} onChange={(event) => setVerificationDefinitions((current) => current.map((item, itemIndex) => itemIndex === index ? { ...item, active: event.target.checked } : item))} disabled={!canManage} />
                      <span>{t('Active')}</span>
                    </label>
                    {canManage && (
                      <button className="btn btn-sm btn-danger" onClick={() => setVerificationDefinitions((current) => current.filter((_, itemIndex) => itemIndex !== index))}>
                        {t('Remove')}
                      </button>
                    )}
                  </div>
                </div>
              ))}
            </div>
          )}

          <div style={{ display: 'flex', gap: '1rem', flexWrap: 'wrap', marginTop: '1rem' }}>
            <label style={{ display: 'inline-flex', alignItems: 'center', gap: '0.45rem' }}>
              <input type="checkbox" checked={requiresApproval} onChange={(event) => setRequiresApproval(event.target.checked)} disabled={!canManage} />
              <span>{t('Requires approval')}</span>
            </label>
            <label style={{ display: 'inline-flex', alignItems: 'center', gap: '0.45rem' }}>
              <input type="checkbox" checked={isDefault} onChange={(event) => setIsDefault(event.target.checked)} disabled={!canManage} />
              <span>{t('Default environment for vessel')}</span>
            </label>
            <label style={{ display: 'inline-flex', alignItems: 'center', gap: '0.45rem' }}>
              <input type="checkbox" checked={active} onChange={(event) => setActive(event.target.checked)} disabled={!canManage} />
              <span>{t('Active')}</span>
            </label>
          </div>

          {canManage && (
            <div className="inline-actions" style={{ marginTop: '1rem' }}>
              <button className="btn btn-primary" disabled={saving} onClick={handleSave}>
                {saving ? t('Saving...') : t('Save Environment')}
              </button>
            </div>
          )}
        </section>

        <section className="card detail-panel">
          <h3>{t('Overview')}</h3>
          <div className="detail-meta-grid">
            <div className="detail-field"><span className="detail-label">{t('Vessel')}</span><span>{selectedVessel?.name || vesselId || '-'}</span></div>
            <div className="detail-field"><span className="detail-label">{t('Kind')}</span><span>{kind}</span></div>
            <div className="detail-field"><span className="detail-label">{t('Default')}</span><span>{isDefault ? t('Yes') : t('No')}</span></div>
            <div className="detail-field"><span className="detail-label">{t('Approval')}</span><span>{requiresApproval ? t('Required') : t('Not required')}</span></div>
            <div className="detail-field"><span className="detail-label">{t('Status')}</span><span>{active ? t('Active') : t('Inactive')}</span></div>
            <div className="detail-field"><span className="detail-label">{t('Health')}</span><span>{healthEndpoint || '-'}</span></div>
            <div className="detail-field"><span className="detail-label">{t('Verification Definitions')}</span><span>{verificationDefinitions.length}</span></div>
            <div className="detail-field"><span className="detail-label">{t('Monitoring Window')}</span><span>{rolloutMonitoringWindowMinutes} {t('minutes')}</span></div>
            <div className="detail-field"><span className="detail-label">{t('Monitoring Interval')}</span><span>{rolloutMonitoringIntervalSeconds} {t('seconds')}</span></div>
            <div className="detail-field"><span className="detail-label">{t('Regression Alerts')}</span><span>{alertOnRegression ? t('Enabled') : t('Disabled')}</span></div>
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
