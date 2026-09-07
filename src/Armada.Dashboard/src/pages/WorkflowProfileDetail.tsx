import { useEffect, useMemo, useState } from 'react';
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom';
import {
  createWorkflowProfile,
  deleteWorkflowProfile,
  getWorkflowProfile,
  listFleets,
  listVessels,
  updateWorkflowProfile,
  validateWorkflowProfile,
} from '../api/client';
import type { Fleet, Vessel, WorkflowEnvironmentProfile, WorkflowInputReference, WorkflowProfile, WorkflowProfileValidationResult } from '../types/models';
import { useAuth } from '../context/AuthContext';
import { useLocale } from '../context/LocaleContext';
import { useNotifications } from '../context/NotificationContext';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import CopyButton from '../components/shared/CopyButton';
import ErrorModal from '../components/shared/ErrorModal';
import JsonViewer from '../components/shared/JsonViewer';
import PageHeader from '../components/shared/PageHeader';
import StatusBadge from '../components/shared/StatusBadge';
import { canEdit as canEditScoped, type ScopeViewer } from '../lib/scoping';
import WorkflowCommandPreview from '../components/shared/WorkflowCommandPreview';
import { buildWorkflowProfileDuplicatePayload } from '../lib/duplicates';

function splitList(value: string): string[] {
  return value
    .split(/\r?\n|,/)
    .map((item) => item.trim())
    .filter(Boolean);
}

function joinList(values: string[] | null | undefined): string {
  return (values || []).join('\n');
}

function blankEnvironment(): WorkflowEnvironmentProfile {
  return {
    environmentName: 'dev',
    deployCommand: null,
    rollbackCommand: null,
    smokeTestCommand: null,
    healthCheckCommand: null,
    deploymentVerificationCommand: null,
    rollbackVerificationCommand: null,
  };
}

function blankInputReference(): WorkflowInputReference {
  return {
    provider: 'EnvironmentVariable',
    key: '',
    environmentName: null,
    description: null,
  };
}

function inputReferencePlaceholder(input: WorkflowInputReference): string {
  switch (input.provider) {
    case 'EnvironmentVariable':
      return 'AWS_PROFILE';
    case 'FilePath':
      return '/path/to/config.json';
    case 'DirectoryPath':
      return '/path/to/config-directory';
    case 'AwsSecretsManager':
      return 'prod/app/database-password';
    case 'AzureKeyVaultSecret':
      return 'kv://armada-prod/database-password';
    case 'HashiCorpVault':
      return 'secret/data/armada/prod/database';
    case 'OnePassword':
      return 'op://Engineering/Armada Prod/database-password';
    default:
      return 'Input reference';
  }
}

export default function WorkflowProfileDetail() {
  const { id } = useParams<{ id: string }>();
  const [searchParams] = useSearchParams();
  const navigate = useNavigate();
  const { isAdmin, isTenantAdmin, user } = useAuth();
  const viewer: ScopeViewer = { isAdmin, isTenantAdmin, tenantId: user?.user?.tenantId, userId: user?.user?.id };
  const { t, formatDateTime } = useLocale();
  const { pushToast } = useNotifications();

  const createMode = id === 'new';

  const [profile, setProfile] = useState<WorkflowProfile | null>(null);
  const canManage = createMode ? true : (profile ? canEditScoped(viewer, { scope: profile.ownershipScope, tenantId: profile.tenantId, userId: profile.userId }) : (isAdmin || isTenantAdmin));
  const [fleets, setFleets] = useState<Fleet[]>([]);
  const [vessels, setVessels] = useState<Vessel[]>([]);
  const [name, setName] = useState('Default Workflow');
  const [description, setDescription] = useState('');
  const [scope, setScope] = useState<'Global' | 'Fleet' | 'Vessel'>('Global');
  const [fleetId, setFleetId] = useState('');
  const [vesselId, setVesselId] = useState('');
  const [isDefault, setIsDefault] = useState(false);
  const [active, setActive] = useState(true);
  const [languageHints, setLanguageHints] = useState('');
  const [requiredInputs, setRequiredInputs] = useState<WorkflowInputReference[]>([]);
  const [expectedArtifacts, setExpectedArtifacts] = useState('');
  const [lintCommand, setLintCommand] = useState('');
  const [buildCommand, setBuildCommand] = useState('');
  const [unitTestCommand, setUnitTestCommand] = useState('');
  const [integrationTestCommand, setIntegrationTestCommand] = useState('');
  const [e2eTestCommand, setE2ETestCommand] = useState('');
  const [migrationCommand, setMigrationCommand] = useState('');
  const [securityScanCommand, setSecurityScanCommand] = useState('');
  const [performanceCommand, setPerformanceCommand] = useState('');
  const [packageCommand, setPackageCommand] = useState('');
  const [deploymentVerificationCommand, setDeploymentVerificationCommand] = useState('');
  const [rollbackVerificationCommand, setRollbackVerificationCommand] = useState('');
  const [publishArtifactCommand, setPublishArtifactCommand] = useState('');
  const [releaseVersioningCommand, setReleaseVersioningCommand] = useState('');
  const [changelogGenerationCommand, setChangelogGenerationCommand] = useState('');
  const [environments, setEnvironments] = useState<WorkflowEnvironmentProfile[]>([]);
  const [validation, setValidation] = useState<WorkflowProfileValidationResult | null>(null);
  const [loading, setLoading] = useState(!createMode);
  const [saving, setSaving] = useState(false);
  const [validating, setValidating] = useState(false);
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
    Promise.all([listFleets({ pageSize: 9999 }), listVessels({ pageSize: 9999 })])
      .then(([fleetResult, vesselResult]) => {
        if (cancelled) return;
        setFleets(fleetResult.objects || []);
        setVessels(vesselResult.objects || []);
      })
      .catch(() => {
        if (!cancelled) setError(t('Failed to load fleets and vessels.'));
      });

    return () => { cancelled = true; };
  }, [t]);

  useEffect(() => {
    if (createMode || !id) return;
    let mounted = true;
    const profileId = id;

    async function load() {
      try {
        setLoading(true);
        const result = await getWorkflowProfile(profileId);
        if (!mounted) return;
        setProfile(result);
        setName(result.name);
        setDescription(result.description || '');
        setScope(result.scope);
        setFleetId(result.fleetId || '');
        setVesselId(result.vesselId || '');
        setIsDefault(result.isDefault);
        setActive(result.active);
        setLanguageHints(joinList(result.languageHints));
        setRequiredInputs(result.requiredInputs || []);
        setExpectedArtifacts(joinList(result.expectedArtifacts));
        setLintCommand(result.lintCommand || '');
        setBuildCommand(result.buildCommand || '');
        setUnitTestCommand(result.unitTestCommand || '');
        setIntegrationTestCommand(result.integrationTestCommand || '');
        setE2ETestCommand(result.e2eTestCommand || '');
        setMigrationCommand(result.migrationCommand || '');
        setSecurityScanCommand(result.securityScanCommand || '');
        setPerformanceCommand(result.performanceCommand || '');
        setPackageCommand(result.packageCommand || '');
        setDeploymentVerificationCommand(result.deploymentVerificationCommand || '');
        setRollbackVerificationCommand(result.rollbackVerificationCommand || '');
        setPublishArtifactCommand(result.publishArtifactCommand || '');
        setReleaseVersioningCommand(result.releaseVersioningCommand || '');
        setChangelogGenerationCommand(result.changelogGenerationCommand || '');
        setEnvironments(result.environments || []);
        setError('');
      } catch (err: unknown) {
        if (mounted) setError(err instanceof Error ? err.message : t('Failed to load workflow profile.'));
      } finally {
        if (mounted) setLoading(false);
      }
    }

    load();
    return () => { mounted = false; };
  }, [createMode, id, t]);

  useEffect(() => {
    if (!createMode) return;

    const requestedScope = searchParams.get('scope');
    if (requestedScope === 'Global' || requestedScope === 'Fleet' || requestedScope === 'Vessel') {
      setScope(requestedScope);
    }

    const requestedFleetId = searchParams.get('fleetId');
    const requestedVesselId = searchParams.get('vesselId');
    if (requestedFleetId) setFleetId(requestedFleetId);
    if (requestedVesselId) setVesselId(requestedVesselId);
  }, [createMode, searchParams]);

  const fleetOptions = useMemo(() => fleets.filter((fleet) => fleet.active !== false), [fleets]);
  const vesselOptions = useMemo(() => vessels.filter((vessel) => vessel.active !== false), [vessels]);

  function buildPayload(): Partial<WorkflowProfile> {
    return {
      name: name.trim(),
      description: description.trim() || null,
      scope,
      fleetId: scope === 'Fleet' ? fleetId || null : null,
      vesselId: scope === 'Vessel' ? vesselId || null : null,
      isDefault,
      active,
      languageHints: splitList(languageHints),
      lintCommand: lintCommand.trim() || null,
      buildCommand: buildCommand.trim() || null,
      unitTestCommand: unitTestCommand.trim() || null,
      integrationTestCommand: integrationTestCommand.trim() || null,
      e2eTestCommand: e2eTestCommand.trim() || null,
      migrationCommand: migrationCommand.trim() || null,
      securityScanCommand: securityScanCommand.trim() || null,
      performanceCommand: performanceCommand.trim() || null,
      packageCommand: packageCommand.trim() || null,
      deploymentVerificationCommand: deploymentVerificationCommand.trim() || null,
      rollbackVerificationCommand: rollbackVerificationCommand.trim() || null,
      publishArtifactCommand: publishArtifactCommand.trim() || null,
      releaseVersioningCommand: releaseVersioningCommand.trim() || null,
      changelogGenerationCommand: changelogGenerationCommand.trim() || null,
      requiredInputs: requiredInputs
        .map((item) => ({
          provider: item.provider,
          key: item.key.trim(),
          environmentName: item.environmentName?.trim() || null,
          description: item.description?.trim() || null,
        }))
        .filter((item) => item.key.length > 0),
      expectedArtifacts: splitList(expectedArtifacts),
      environments: environments.map((environment) => ({
        environmentName: environment.environmentName.trim(),
        deployCommand: environment.deployCommand?.trim() || null,
        rollbackCommand: environment.rollbackCommand?.trim() || null,
        smokeTestCommand: environment.smokeTestCommand?.trim() || null,
        healthCheckCommand: environment.healthCheckCommand?.trim() || null,
        deploymentVerificationCommand: environment.deploymentVerificationCommand?.trim() || null,
        rollbackVerificationCommand: environment.rollbackVerificationCommand?.trim() || null,
      })).filter((environment) => environment.environmentName),
    };
  }

  async function handleValidate() {
    try {
      setValidating(true);
      const result = await validateWorkflowProfile(buildPayload());
      setValidation(result);
      if (result.isValid) {
        pushToast('success', t('Workflow profile is valid.'));
      } else {
        pushToast('warning', t('Workflow profile has validation errors.'));
      }
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Validation failed.'));
    } finally {
      setValidating(false);
    }
  }

  async function handleSave() {
    if (!canManage) return;
    try {
      setSaving(true);
      const payload = buildPayload();
      if (createMode) {
        const created = await createWorkflowProfile(payload);
        pushToast('success', t('Workflow profile "{{name}}" created.', { name: created.name }));
        navigate(`/workflow-profiles/${created.id}`);
        return;
      }

      if (!id) return;
      const updated = await updateWorkflowProfile(id, payload);
      setProfile(updated);
      pushToast('success', t('Workflow profile "{{name}}" saved.', { name: updated.name }));
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Save failed.'));
    } finally {
      setSaving(false);
    }
  }

  function handleDelete() {
    if (!profile || !canManage) return;
    setConfirm({
      open: true,
      title: t('Delete Workflow Profile'),
      message: t('Delete "{{name}}"? Existing check runs remain, but future runs will not be able to resolve this profile.', { name: profile.name }),
      onConfirm: async () => {
        setConfirm((current) => ({ ...current, open: false }));
        try {
          await deleteWorkflowProfile(profile.id);
          pushToast('warning', t('Workflow profile "{{name}}" deleted.', { name: profile.name }));
          navigate('/workflow-profiles');
        } catch (err: unknown) {
          setError(err instanceof Error ? err.message : t('Delete failed.'));
        }
      },
    });
  }

  async function handleDuplicate() {
    if (!profile || !canManage) return;
    try {
      setSaving(true);
      const created = await createWorkflowProfile(buildWorkflowProfileDuplicatePayload(profile));
      pushToast('success', t('Workflow profile "{{name}}" duplicated.', { name: created.name }));
      navigate(`/workflow-profiles/${created.id}`);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Duplicate failed.'));
    } finally {
      setSaving(false);
    }
  }

  function updateEnvironment(index: number, key: keyof WorkflowEnvironmentProfile, value: string) {
    setEnvironments((current) => current.map((environment, currentIndex) => (
      currentIndex === index ? { ...environment, [key]: value || null } : environment
    )));
  }

  function updateInputReference(index: number, patch: Partial<WorkflowInputReference>) {
    setRequiredInputs((current) => current.map((item, currentIndex) => (
      currentIndex === index ? { ...item, ...patch } : item
    )));
  }

  if (loading) return <p className="text-dim">{t('Loading...')}</p>;

  return (
    <div>
      <PageHeader
        breadcrumb={
          <>
            <Link to="/workflow-profiles">{t('Workflow Profiles')}</Link> <span className="breadcrumb-sep">&gt;</span> <span>{createMode ? t('New Workflow Profile') : name}</span>
          </>
        }
        title={createMode ? t('Create Workflow Profile') : name}
        actions={
          <>
            {!createMode && <StatusBadge status={active ? 'Active' : 'Inactive'} />}
            {!createMode && (
              <button className="btn btn-sm" onClick={() => setJsonData({ open: true, title: name, data: profile })}>
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

      {!canManage && (
        <div className="alert alert-error" style={{ marginBottom: '1rem' }}>
          {t('You can view this workflow profile, but only its owner or a tenant administrator can change it.')}
        </div>
      )}

      {!createMode && profile && (
        <div className="detail-grid" style={{ marginBottom: '1rem' }}>
          <div className="detail-field">
            <span className="detail-label">{t('ID')}</span>
            <span className="id-display">
              <span className="mono">{profile.id}</span>
              <CopyButton text={profile.id} />
            </span>
          </div>
          <div className="detail-field">
            <span className="detail-label">{t('Created')}</span>
            <span>{formatDateTime(profile.createdUtc)}</span>
          </div>
          <div className="detail-field">
            <span className="detail-label">{t('Last Updated')}</span>
            <span>{formatDateTime(profile.lastUpdateUtc)}</span>
          </div>
          <div className="detail-field">
            <span className="detail-label">{t('Scope')}</span>
            <StatusBadge status={profile.scope} />
          </div>
        </div>
      )}

      {validation && (
        <div className="card" style={{ marginBottom: '1rem' }}>
          <div className="detail-header" style={{ marginBottom: '0.75rem' }}>
            <h3>{t('Validation')}</h3>
            <StatusBadge status={validation.isValid ? 'Valid' : 'Needs Attention'} />
          </div>
          <div className="detail-grid">
            <div className="detail-field">
              <span className="detail-label">{t('Available Check Types')}</span>
              <span>{validation.availableCheckTypes.length > 0 ? validation.availableCheckTypes.join(', ') : t('None')}</span>
            </div>
          </div>
          <div style={{ marginTop: '0.9rem' }}>
            <strong>{t('Resolved Commands')}</strong>
            <div style={{ marginTop: '0.5rem' }}>
              <WorkflowCommandPreview
                commands={validation.commandPreviews}
                emptyMessage={t('No resolved commands are available for this workflow profile.')}
              />
            </div>
          </div>
          {validation.errors.length > 0 && (
            <div style={{ marginTop: '0.75rem' }}>
              <strong>{t('Errors')}</strong>
              <ul style={{ marginTop: '0.4rem' }}>
                {validation.errors.map((item) => <li key={item}>{item}</li>)}
              </ul>
            </div>
          )}
          {validation.warnings.length > 0 && (
            <div style={{ marginTop: '0.75rem' }}>
              <strong>{t('Warnings')}</strong>
              <ul style={{ marginTop: '0.4rem' }}>
                {validation.warnings.map((item) => <li key={item}>{item}</li>)}
              </ul>
            </div>
          )}
        </div>
      )}

      <div className="card playbook-editor-card">
        <div className="detail-grid" style={{ marginBottom: '1rem' }}>
          <label className="playbook-editor-field">
            <span>{t('Name')}</span>
            <input type="text" value={name} disabled={!canManage} onChange={(event) => setName(event.target.value)} />
          </label>
          <label className="playbook-editor-field">
            <span>{t('Scope')}</span>
            <select value={scope} disabled={!canManage} onChange={(event) => setScope(event.target.value as 'Global' | 'Fleet' | 'Vessel')}>
              <option value="Global">{t('Global')}</option>
              <option value="Fleet">{t('Fleet')}</option>
              <option value="Vessel">{t('Vessel')}</option>
            </select>
          </label>
          {scope === 'Fleet' && (
            <label className="playbook-editor-field">
              <span>{t('Fleet')}</span>
              <select value={fleetId} disabled={!canManage} onChange={(event) => setFleetId(event.target.value)}>
                <option value="">{t('Select a fleet...')}</option>
                {fleetOptions.map((fleet) => (
                  <option key={fleet.id} value={fleet.id}>{fleet.name}</option>
                ))}
              </select>
            </label>
          )}
          {scope === 'Vessel' && (
            <label className="playbook-editor-field">
              <span>{t('Vessel')}</span>
              <select value={vesselId} disabled={!canManage} onChange={(event) => setVesselId(event.target.value)}>
                <option value="">{t('Select a vessel...')}</option>
                {vesselOptions.map((vessel) => (
                  <option key={vessel.id} value={vessel.id}>{vessel.name}</option>
                ))}
              </select>
            </label>
          )}
        </div>

        <label className="playbook-editor-field">
          <span>{t('Description')}</span>
          <input type="text" value={description} disabled={!canManage} onChange={(event) => setDescription(event.target.value)} />
        </label>

        <div className="detail-grid" style={{ marginBottom: '1rem' }}>
          <label className="playbook-toggle">
            <input type="checkbox" checked={isDefault} disabled={!canManage} onChange={(event) => setIsDefault(event.target.checked)} />
            <span>{t('Default for this scope')}</span>
          </label>
          <label className="playbook-toggle">
            <input type="checkbox" checked={active} disabled={!canManage} onChange={(event) => setActive(event.target.checked)} />
            <span>{t('Active')}</span>
          </label>
        </div>

        <div className="detail-grid" style={{ marginBottom: '1rem' }}>
          <label className="playbook-editor-field">
            <span>{t('Language / Runtime Hints')}</span>
            <textarea value={languageHints} disabled={!canManage} onChange={(event) => setLanguageHints(event.target.value)} rows={3} placeholder={t('dotnet\nreact\npostgres')} />
          </label>
          <label className="playbook-editor-field">
            <span>{t('Expected Artifacts')}</span>
            <textarea value={expectedArtifacts} disabled={!canManage} onChange={(event) => setExpectedArtifacts(event.target.value)} rows={3} placeholder={t('bin/Release/app.zip\ncoverage/summary.xml')} />
          </label>
        </div>

        <div className="card" style={{ marginBottom: '1rem', padding: '1rem' }}>
          <div className="detail-header" style={{ marginBottom: '0.75rem' }}>
            <h3>{t('Required Inputs')}</h3>
            {canManage && (
              <button type="button" className="btn btn-sm" onClick={() => setRequiredInputs((current) => [...current, blankInputReference()])}>
                + {t('Input')}
              </button>
            )}
          </div>
          <p className="text-dim" style={{ marginBottom: '0.85rem' }}>
            {t('Store provider/key references here so Armada can warn before checks run. No secret values are stored in the workflow profile itself.')}
          </p>
          {requiredInputs.length === 0 ? (
            <div className="text-dim">{t('No required inputs configured.')}</div>
          ) : (
            <div style={{ display: 'grid', gap: '0.75rem' }}>
              {requiredInputs.map((item, index) => (
                <div key={`input-${index}`} className="detail-grid">
                  <label className="playbook-editor-field">
                    <span>{t('Provider')}</span>
                    <select value={item.provider} disabled={!canManage} onChange={(event) => updateInputReference(index, { provider: event.target.value as WorkflowInputReference['provider'] })}>
                      <option value="EnvironmentVariable">{t('Environment Variable')}</option>
                      <option value="FilePath">{t('File Path')}</option>
                      <option value="DirectoryPath">{t('Directory Path')}</option>
                      <option value="AwsSecretsManager">{t('AWS Secrets Manager')}</option>
                      <option value="AzureKeyVaultSecret">{t('Azure Key Vault')}</option>
                      <option value="HashiCorpVault">{t('HashiCorp Vault')}</option>
                      <option value="OnePassword">{t('1Password')}</option>
                    </select>
                  </label>
                  <label className="playbook-editor-field">
                    <span>{t('Environment Scope')}</span>
                    <select value={item.environmentName || ''} disabled={!canManage} onChange={(event) => updateInputReference(index, { environmentName: event.target.value || null })}>
                      <option value="">{t('All Environments')}</option>
                      {environments.map((environment) => (
                        <option key={`input-env-${environment.environmentName}-${index}`} value={environment.environmentName}>
                          {environment.environmentName}
                        </option>
                      ))}
                    </select>
                  </label>
                  <label className="playbook-editor-field">
                    <span>{t('Key / Path')}</span>
                    <input
                      type="text"
                      value={item.key}
                      disabled={!canManage}
                      onChange={(event) => updateInputReference(index, { key: event.target.value })}
                      placeholder={t(inputReferencePlaceholder(item))}
                    />
                  </label>
                  <label className="playbook-editor-field">
                    <span>{t('Description')}</span>
                    <input
                      type="text"
                      value={item.description || ''}
                      disabled={!canManage}
                      onChange={(event) => updateInputReference(index, { description: event.target.value || null })}
                      placeholder={t('Optional operator note or secret purpose')}
                    />
                  </label>
                  {canManage && (
                    <div className="playbook-editor-field">
                      <span>{t('Actions')}</span>
                      <button type="button" className="btn btn-sm btn-danger" onClick={() => setRequiredInputs((current) => current.filter((_, currentIndex) => currentIndex !== index))}>
                        {t('Remove')}
                      </button>
                    </div>
                  )}
                </div>
              ))}
            </div>
          )}
        </div>

        <div className="detail-grid" style={{ marginBottom: '1rem' }}>
          <label className="playbook-editor-field">
            <span>{t('Lint Command')}</span>
            <textarea value={lintCommand} disabled={!canManage} onChange={(event) => setLintCommand(event.target.value)} rows={3} />
          </label>
          <label className="playbook-editor-field">
            <span>{t('Build Command')}</span>
            <textarea value={buildCommand} disabled={!canManage} onChange={(event) => setBuildCommand(event.target.value)} rows={3} />
          </label>
          <label className="playbook-editor-field">
            <span>{t('Unit Test Command')}</span>
            <textarea value={unitTestCommand} disabled={!canManage} onChange={(event) => setUnitTestCommand(event.target.value)} rows={3} />
          </label>
          <label className="playbook-editor-field">
            <span>{t('Integration Test Command')}</span>
            <textarea value={integrationTestCommand} disabled={!canManage} onChange={(event) => setIntegrationTestCommand(event.target.value)} rows={3} />
          </label>
          <label className="playbook-editor-field">
            <span>{t('E2E Test Command')}</span>
            <textarea value={e2eTestCommand} disabled={!canManage} onChange={(event) => setE2ETestCommand(event.target.value)} rows={3} />
          </label>
          <label className="playbook-editor-field">
            <span>{t('Migration Command')}</span>
            <textarea value={migrationCommand} disabled={!canManage} onChange={(event) => setMigrationCommand(event.target.value)} rows={3} />
          </label>
          <label className="playbook-editor-field">
            <span>{t('Security Scan Command')}</span>
            <textarea value={securityScanCommand} disabled={!canManage} onChange={(event) => setSecurityScanCommand(event.target.value)} rows={3} />
          </label>
          <label className="playbook-editor-field">
            <span>{t('Performance Command')}</span>
            <textarea value={performanceCommand} disabled={!canManage} onChange={(event) => setPerformanceCommand(event.target.value)} rows={3} />
          </label>
          <label className="playbook-editor-field">
            <span>{t('Package Command')}</span>
            <textarea value={packageCommand} disabled={!canManage} onChange={(event) => setPackageCommand(event.target.value)} rows={3} />
          </label>
          <label className="playbook-editor-field">
            <span>{t('Deployment Verification Command')}</span>
            <textarea value={deploymentVerificationCommand} disabled={!canManage} onChange={(event) => setDeploymentVerificationCommand(event.target.value)} rows={3} />
          </label>
          <label className="playbook-editor-field">
            <span>{t('Rollback Verification Command')}</span>
            <textarea value={rollbackVerificationCommand} disabled={!canManage} onChange={(event) => setRollbackVerificationCommand(event.target.value)} rows={3} />
          </label>
          <label className="playbook-editor-field">
            <span>{t('Publish Artifact Command')}</span>
            <textarea value={publishArtifactCommand} disabled={!canManage} onChange={(event) => setPublishArtifactCommand(event.target.value)} rows={3} />
          </label>
          <label className="playbook-editor-field">
            <span>{t('Release Versioning Command')}</span>
            <textarea value={releaseVersioningCommand} disabled={!canManage} onChange={(event) => setReleaseVersioningCommand(event.target.value)} rows={3} />
          </label>
          <label className="playbook-editor-field">
            <span>{t('Changelog Generation Command')}</span>
            <textarea value={changelogGenerationCommand} disabled={!canManage} onChange={(event) => setChangelogGenerationCommand(event.target.value)} rows={3} />
          </label>
        </div>

        <div className="card" style={{ marginBottom: '1rem', padding: '1rem' }}>
          <div className="detail-header" style={{ marginBottom: '0.75rem' }}>
            <h3>{t('Environment Commands')}</h3>
            {canManage && (
              <button className="btn btn-sm" type="button" onClick={() => setEnvironments((current) => [...current, blankEnvironment()])}>
                + {t('Environment')}
              </button>
            )}
          </div>

          {environments.length === 0 ? (
            <p className="text-dim">{t('No environment-specific commands configured yet.')}</p>
          ) : (
            <div style={{ display: 'grid', gap: '1rem' }}>
              {environments.map((environment, index) => (
                <div key={`${environment.environmentName}-${index}`} className="card" style={{ padding: '1rem' }}>
                  <div className="detail-header" style={{ marginBottom: '0.75rem' }}>
                    <h4>{environment.environmentName || t('Environment')}</h4>
                    {canManage && (
                      <button className="btn btn-sm btn-danger" type="button" onClick={() => setEnvironments((current) => current.filter((_, currentIndex) => currentIndex !== index))}>
                        {t('Remove')}
                      </button>
                    )}
                  </div>
                  <div className="detail-grid">
                    <label className="playbook-editor-field">
                      <span>{t('Name')}</span>
                      <input type="text" value={environment.environmentName} disabled={!canManage} onChange={(event) => updateEnvironment(index, 'environmentName', event.target.value)} />
                    </label>
                    <label className="playbook-editor-field">
                      <span>{t('Deploy Command')}</span>
                      <textarea value={environment.deployCommand || ''} disabled={!canManage} onChange={(event) => updateEnvironment(index, 'deployCommand', event.target.value)} rows={3} />
                    </label>
                    <label className="playbook-editor-field">
                      <span>{t('Rollback Command')}</span>
                      <textarea value={environment.rollbackCommand || ''} disabled={!canManage} onChange={(event) => updateEnvironment(index, 'rollbackCommand', event.target.value)} rows={3} />
                    </label>
                    <label className="playbook-editor-field">
                      <span>{t('Smoke Test Command')}</span>
                      <textarea value={environment.smokeTestCommand || ''} disabled={!canManage} onChange={(event) => updateEnvironment(index, 'smokeTestCommand', event.target.value)} rows={3} />
                    </label>
                    <label className="playbook-editor-field">
                      <span>{t('Health Check Command')}</span>
                      <textarea value={environment.healthCheckCommand || ''} disabled={!canManage} onChange={(event) => updateEnvironment(index, 'healthCheckCommand', event.target.value)} rows={3} />
                    </label>
                    <label className="playbook-editor-field">
                      <span>{t('Deployment Verification Command')}</span>
                      <textarea value={environment.deploymentVerificationCommand || ''} disabled={!canManage} onChange={(event) => updateEnvironment(index, 'deploymentVerificationCommand', event.target.value)} rows={3} />
                    </label>
                    <label className="playbook-editor-field">
                      <span>{t('Rollback Verification Command')}</span>
                      <textarea value={environment.rollbackVerificationCommand || ''} disabled={!canManage} onChange={(event) => updateEnvironment(index, 'rollbackVerificationCommand', event.target.value)} rows={3} />
                    </label>
                  </div>
                </div>
              ))}
            </div>
          )}
        </div>

        <div className="playbook-editor-actions">
          <button className="btn" disabled={validating} onClick={handleValidate}>
            {validating ? t('Validating...') : t('Validate')}
          </button>
          <button className="btn btn-primary" disabled={!canManage || saving} onClick={handleSave}>
            {saving ? t('Saving...') : createMode ? t('Create Workflow Profile') : t('Save Changes')}
          </button>
          <button className="btn" onClick={() => navigate('/workflow-profiles')}>
            {t('Back')}
          </button>
        </div>
      </div>
    </div>
  );
}
