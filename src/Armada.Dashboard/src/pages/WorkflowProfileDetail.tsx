import { useEffect, useMemo, useState } from 'react';
import { useNavigate, useParams, useSearchParams } from 'react-router-dom';
import {
  createWorkflowProfile,
  deleteWorkflowProfile,
  getWorkflowProfile,
  listFleets,
  listVessels,
  updateWorkflowProfile,
  validateWorkflowProfile,
} from '../api/client';
import { listAll } from '../api/paging';
import { blankInputReference, blankWorkflowEnvironment, inputReferencePlaceholder, joinList, splitList } from '../lib/configuration';
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
import { useDeleteProgress } from '../lib/useDeleteProgress';






const NAME_TIP = 'Name for this profile, for example dotnet-service or web-frontend. Required.';
const DESCRIPTION_TIP = 'Optional note on what kind of project this profile is for.';
const SCOPE_TIP = 'Which vessels the profile applies to: Global for all, Fleet for one fleet\'s vessels, Vessel for one repository. A vessel uses its Vessel profile first, then Fleet, then Global.';
const FLEET_TIP = 'The fleet whose vessels this profile applies to. Required when Scope is Fleet.';
const VESSEL_TIP = 'The single repository this profile applies to. Required when Scope is Vessel.';
const HINTS_TIP = 'Optional languages and tools the project uses, one per line, for example dotnet or react. For reference only; Armada does not act on them.';
const ARTIFACTS_TIP = 'Files the checks produce, one path per line relative to the vessel\'s working directory, for example coverage/summary.xml. Armada collects them onto each check run.';
const DEFAULT_TIP = 'When several active profiles match the same vessel at the same scope, the default one is used. Otherwise the most recently updated wins.';
const ACTIVE_TIP = 'On: Armada can use this profile. Off: it is never chosen, even when a vessel names it.';
const INPUT_PROVIDER_TIP = 'Where the input lives: an environment variable, a file or folder on the machine that runs checks, or a secret manager such as AWS Secrets Manager, Azure Key Vault, HashiCorp Vault or 1Password.';
const INPUT_ENV_TIP = 'Limit this input to one environment, for example prod. All Environments means it applies everywhere.';
const INPUT_KEY_TIP = 'The variable name, path or secret reference to look for, for example NUGET_API_KEY or ~/.aws/credentials. Only the reference is stored, never the secret value.';
const INPUT_DESCRIPTION_TIP = 'Optional note on what the input is for, shown with readiness warnings.';
const ENV_NAME_TIP = 'Environment name, for example dev, staging or prod. Deployments and check runs for an environment with this name use the commands below. Required.';
const CMD_LINTCOMMAND_TIP = 'Shell command Armada runs for a Lint check in the vessel\'s working directory, for example npm run lint. Leave blank if the project has no lint step.';
const CMD_BUILDCOMMAND_TIP = 'Shell command Armada runs for a Build check in the vessel\'s working directory, for example dotnet build. Leave blank if the project has no build step.';
const CMD_UNITTESTCOMMAND_TIP = 'Full unit-test command: the complete unit-test suite, run in the vessel\'s working directory, for example dotnet test. Used by Unit Test checks and by stage checks unless the abbreviated suite is requested. Leave blank if the project has no unit test step.';
const CMD_ABBREVIATEDUNITTESTCOMMAND_TIP = 'Abbreviated unit-test command: a fast subset of the unit tests, for example dotnet test --filter Category=Smoke. Used by Abbreviated Unit Test checks, and by stage checks when the mission description or the stage persona\'s prompt contains [ARMADA:UNIT-TESTS] abbreviated. When blank, those run the full command instead.';
const UNIT_TEST_SELECTION_HINT = 'Which unit-test command runs: you can choose directly by running an Abbreviated Unit Test check, or by putting [ARMADA:UNIT-TESTS] abbreviated (or full) in a mission description. Otherwise the stage persona\'s prompt decides, for example a Test Engineer prompt template containing that line. With neither, the full command runs.';
const CMD_INTEGRATIONTESTCOMMAND_TIP = 'Shell command Armada runs for an Integration Test check in the vessel\'s working directory, for example npm run test:integration. Leave blank if the project has no integration test step.';
const CMD_E2ETESTCOMMAND_TIP = 'Shell command Armada runs for an E2E Test check in the vessel\'s working directory, for example npx playwright test. Leave blank if the project has no end-to-end test step.';
const CMD_MIGRATIONCOMMAND_TIP = 'Shell command Armada runs for a Migration check in the vessel\'s working directory, for example dotnet ef database update. Leave blank if the project has no database migration step.';
const CMD_SECURITYSCANCOMMAND_TIP = 'Shell command Armada runs for a Security Scan check in the vessel\'s working directory, for example npm audit --audit-level=high. Leave blank if the project has no security scan step.';
const CMD_PERFORMANCECOMMAND_TIP = 'Shell command Armada runs for a Performance check in the vessel\'s working directory, for example k6 run perf.js. Leave blank if the project has no performance step.';
const CMD_PACKAGECOMMAND_TIP = 'Shell command Armada runs for a Package check in the vessel\'s working directory, for example dotnet publish -c Release. Leave blank if the project has no package step.';
const CMD_DEPLOYMENTVERIFICATIONCOMMAND_TIP = 'Default command that checks a deployment worked, for example curl -f https://example.com/health. An environment below can set its own. Leave blank if not needed.';
const CMD_ROLLBACKVERIFICATIONCOMMAND_TIP = 'Default command that checks a rollback worked. An environment below can set its own. Leave blank if not needed.';
const CMD_PUBLISHARTIFACTCOMMAND_TIP = 'Shell command Armada runs for a Publish Artifact check in the vessel\'s working directory, for example dotnet nuget push *.nupkg. Leave blank if the project has no publish step.';
const CMD_RELEASEVERSIONINGCOMMAND_TIP = 'Shell command Armada runs for a Release Versioning check in the vessel\'s working directory, for example npm version patch. Leave blank if the project has no versioning step.';
const CMD_CHANGELOGGENERATIONCOMMAND_TIP = 'Shell command Armada runs for a Changelog check in the vessel\'s working directory, for example npx conventional-changelog -p angular. Leave blank if the project has no changelog step.';
const ENV_DEPLOY_TIP = 'Command that deploys to this environment, for example ./deploy.sh staging. Runs when a deployment to this environment starts.';
const ENV_ROLLBACK_TIP = 'Command that returns this environment to the previous release. Runs when a deployment here is rolled back.';
const ENV_SMOKE_TIP = 'Quick test run against this environment after a deploy to confirm the basics work, for example npm run smoke -- --env staging.';
const ENV_HEALTH_TIP = 'Command that checks this environment is up, for example curl -f https://staging.example.com/health. A non-zero exit means unhealthy.';
const ENV_DEPLOY_VERIFY_TIP = 'Command that confirms a deployment to this environment worked. Overrides the profile-wide verification command for this environment.';
const ENV_ROLLBACK_VERIFY_TIP = 'Command that confirms a rollback in this environment worked. Overrides the profile-wide rollback verification command here.';

export default function WorkflowProfileDetail() {
  const { id } = useParams<{ id: string }>();
  const [searchParams] = useSearchParams();
  const navigate = useNavigate();
  const { isAdmin, isTenantAdmin, user } = useAuth();
  const viewer: ScopeViewer = { isAdmin, isTenantAdmin, tenantId: user?.user?.tenantId, userId: user?.user?.id };
  const { t, formatDateTime } = useLocale();
  const deletion = useDeleteProgress();
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
  const [abbreviatedUnitTestCommand, setAbbreviatedUnitTestCommand] = useState('');
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
    Promise.all([listAll(listFleets), listAll(listVessels)])
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
        setAbbreviatedUnitTestCommand(result.abbreviatedUnitTestCommand || '');
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
      abbreviatedUnitTestCommand: abbreviatedUnitTestCommand.trim() || null,
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
        if (!(await deletion.run({ kind: 'workflowProfile', action: () => deleteWorkflowProfile(profile.id), errorMessage: (err) => err instanceof Error ? err.message : t('Delete failed.') }))) return;
        pushToast('warning', t('Workflow profile "{{name}}" deleted.', { name: profile.name }));
        navigate('/workflow-profiles');
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
        breadcrumb={[
          { label: t('Workflow Profiles'), to: '/workflow-profiles' },
          { label: createMode ? t('New Workflow Profile') : name },
        ]}
        title={createMode ? t('Create Workflow Profile') : name}
        actions={
          <>
            {!createMode && <StatusBadge status={active ? 'Active' : 'Inactive'} />}
            {!createMode && (
              <button className="btn btn-sm" onClick={() => setJsonData({ open: true, title: name, data: profile })} title={t('Show this profile\'s saved record as JSON, as the API returns it. Unsaved edits are not included.')}>
                {t('View JSON')}
              </button>
            )}
            {!createMode && canManage && (
              <button className="btn btn-sm" disabled={saving} onClick={() => void handleDuplicate()} title={t('Create a copy of this profile under a new name, then open it.')}>
                {t('Duplicate')}
              </button>
            )}
            {!createMode && canManage && (
              <button className="btn btn-sm btn-danger" onClick={handleDelete} title={t('Permanently delete this profile. Past check runs are kept; vessels fall back to the next matching profile. You confirm first.')}>
                {t('Delete')}
              </button>
            )}
          </>
        }
      />

      <ErrorModal error={error} onClose={() => setError('')} />
      <JsonViewer open={jsonData.open} title={jsonData.title} data={jsonData.data} onClose={() => setJsonData({ open: false, title: '', data: null })} />
      {deletion.dialog}
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
            <span className="detail-label" title={t('Unique workflow profile ID (wfp_ prefix), used when a vessel or check run names a profile directly.')}>{t('ID')}</span>
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
            <span className="detail-label" title={t(SCOPE_TIP)}>{t('Scope')}</span>
            <StatusBadge status={profile.scope} />
          </div>
        </div>
      )}

      {validation && (
        <div className="card" style={{ marginBottom: '1rem' }}>
          <div className="detail-header" style={{ marginBottom: '0.75rem' }}>
            <h3 title={t('Result of the last Validate click, checked against the form as it was then (saved or not).')}>{t('Validation')}</h3>
            <span className={`tag ${validation.isValid ? 'valid' : 'needs attention'}`} title={validation.isValid ? t('No errors found. Warnings, if any, are listed below.') : t('The profile has errors to fix. They are listed below.')}>{t(validation.isValid ? 'Valid' : 'Needs Attention')}</span>
          </div>
          <div className="detail-grid">
            <div className="detail-field">
              <span className="detail-label" title={t('Check types this profile has a command for, so check runs of these types can run. Types missing here cannot run.')}>{t('Available Check Types')}</span>
              <span>{validation.availableCheckTypes.length > 0 ? validation.availableCheckTypes.join(', ') : t('None')}</span>
            </div>
          </div>
          <div style={{ marginTop: '0.9rem' }}>
            <strong title={t('Every command Armada would run, per check type and environment, after environment overrides are applied.')}>{t('Resolved Commands')}</strong>
            <div style={{ marginTop: '0.5rem' }}>
              <WorkflowCommandPreview
                commands={validation.commandPreviews}
                emptyMessage={t('No resolved commands are available for this workflow profile.')}
              />
            </div>
          </div>
          {validation.errors.length > 0 && (
            <div style={{ marginTop: '0.75rem' }}>
              <strong title={t('Problems that must be fixed before the profile can be relied on.')}>{t('Errors')}</strong>
              <ul style={{ marginTop: '0.4rem' }}>
                {validation.errors.map((item) => <li key={item}>{item}</li>)}
              </ul>
            </div>
          )}
          {validation.warnings.length > 0 && (
            <div style={{ marginTop: '0.75rem' }}>
              <strong title={t('Things worth checking that do not stop the profile from working.')}>{t('Warnings')}</strong>
              <ul style={{ marginTop: '0.4rem' }}>
                {validation.warnings.map((item) => <li key={item}>{item}</li>)}
              </ul>
            </div>
          )}
        </div>
      )}

      <div className="card playbook-editor-card">
        <div className="detail-grid" style={{ marginBottom: '1rem' }}>
          <label className="playbook-editor-field" title={t(NAME_TIP)}>
            <span>{t('Name')}</span>
            <input type="text" title={t(NAME_TIP)} value={name} disabled={!canManage} onChange={(event) => setName(event.target.value)} />
          </label>
          <label className="playbook-editor-field" title={t(SCOPE_TIP)}>
            <span>{t('Scope')}</span>
            <select title={t(SCOPE_TIP)} value={scope} disabled={!canManage} onChange={(event) => setScope(event.target.value as 'Global' | 'Fleet' | 'Vessel')}>
              <option value="Global">{t('Global')}</option>
              <option value="Fleet">{t('Fleet')}</option>
              <option value="Vessel">{t('Vessel')}</option>
            </select>
          </label>
          {scope === 'Fleet' && (
            <label className="playbook-editor-field" title={t(FLEET_TIP)}>
              <span>{t('Fleet')}</span>
              <select title={t(FLEET_TIP)} value={fleetId} disabled={!canManage} onChange={(event) => setFleetId(event.target.value)}>
                <option value="">{t('Select a fleet...')}</option>
                {fleetOptions.map((fleet) => (
                  <option key={fleet.id} value={fleet.id}>{fleet.name}</option>
                ))}
              </select>
            </label>
          )}
          {scope === 'Vessel' && (
            <label className="playbook-editor-field" title={t(VESSEL_TIP)}>
              <span>{t('Vessel')}</span>
              <select title={t(VESSEL_TIP)} value={vesselId} disabled={!canManage} onChange={(event) => setVesselId(event.target.value)}>
                <option value="">{t('Select a vessel...')}</option>
                {vesselOptions.map((vessel) => (
                  <option key={vessel.id} value={vessel.id}>{vessel.name}</option>
                ))}
              </select>
            </label>
          )}
        </div>

        <label className="playbook-editor-field" title={t(DESCRIPTION_TIP)}>
          <span>{t('Description')}</span>
          <input type="text" title={t(DESCRIPTION_TIP)} value={description} disabled={!canManage} onChange={(event) => setDescription(event.target.value)} />
        </label>

        <div className="detail-grid" style={{ marginBottom: '1rem' }}>
          <label className="playbook-toggle" title={t(DEFAULT_TIP)}>
            <input type="checkbox" title={t(DEFAULT_TIP)} checked={isDefault} disabled={!canManage} onChange={(event) => setIsDefault(event.target.checked)} />
            <span>{t('Default for this scope')}</span>
          </label>
          <label className="playbook-toggle" title={t(ACTIVE_TIP)}>
            <input type="checkbox" title={t(ACTIVE_TIP)} checked={active} disabled={!canManage} onChange={(event) => setActive(event.target.checked)} />
            <span>{t('Active')}</span>
          </label>
        </div>

        <div className="detail-grid" style={{ marginBottom: '1rem' }}>
          <label className="playbook-editor-field" title={t(HINTS_TIP)}>
            <span>{t('Language / Runtime Hints')}</span>
            <textarea title={t(HINTS_TIP)} value={languageHints} disabled={!canManage} onChange={(event) => setLanguageHints(event.target.value)} rows={3} placeholder={t('dotnet\nreact\npostgres')} />
          </label>
          <label className="playbook-editor-field" title={t(ARTIFACTS_TIP)}>
            <span>{t('Expected Artifacts')}</span>
            <textarea title={t(ARTIFACTS_TIP)} value={expectedArtifacts} disabled={!canManage} onChange={(event) => setExpectedArtifacts(event.target.value)} rows={3} placeholder={t('bin/Release/app.zip\ncoverage/summary.xml')} />
          </label>
        </div>

        <div className="card" style={{ marginBottom: '1rem', padding: '1rem' }}>
          <div className="detail-header" style={{ marginBottom: '0.75rem' }}>
            <h3 title={t('Secrets, files or folders the commands need, listed by reference only. Armada uses them to warn before checks run when something is missing.')}>{t('Required Inputs')}</h3>
            {canManage && (
              <button type="button" className="btn btn-sm" onClick={() => setRequiredInputs((current) => [...current, blankInputReference()])} title={t('Add a required input row.')}>
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
                  <label className="playbook-editor-field" title={t(INPUT_PROVIDER_TIP)}>
                    <span>{t('Provider')}</span>
                    <select title={t(INPUT_PROVIDER_TIP)} value={item.provider} disabled={!canManage} onChange={(event) => updateInputReference(index, { provider: event.target.value as WorkflowInputReference['provider'] })}>
                      <option value="EnvironmentVariable">{t('Environment Variable')}</option>
                      <option value="FilePath">{t('File Path')}</option>
                      <option value="DirectoryPath">{t('Directory Path')}</option>
                      <option value="AwsSecretsManager">{t('AWS Secrets Manager')}</option>
                      <option value="AzureKeyVaultSecret">{t('Azure Key Vault')}</option>
                      <option value="HashiCorpVault">{t('HashiCorp Vault')}</option>
                      <option value="OnePassword">{t('1Password')}</option>
                    </select>
                  </label>
                  <label className="playbook-editor-field" title={t(INPUT_ENV_TIP)}>
                    <span>{t('Environment Scope')}</span>
                    <select title={t(INPUT_ENV_TIP)} value={item.environmentName || ''} disabled={!canManage} onChange={(event) => updateInputReference(index, { environmentName: event.target.value || null })}>
                      <option value="">{t('All Environments')}</option>
                      {environments.map((environment) => (
                        <option key={`input-env-${environment.environmentName}-${index}`} value={environment.environmentName}>
                          {environment.environmentName}
                        </option>
                      ))}
                    </select>
                  </label>
                  <label className="playbook-editor-field" title={t(INPUT_KEY_TIP)}>
                    <span>{t('Key / Path')}</span>
                    <input
                      type="text"
                      title={t(INPUT_KEY_TIP)}
                      value={item.key}
                      disabled={!canManage}
                      onChange={(event) => updateInputReference(index, { key: event.target.value })}
                      placeholder={t(inputReferencePlaceholder(item))}
                    />
                  </label>
                  <label className="playbook-editor-field" title={t(INPUT_DESCRIPTION_TIP)}>
                    <span>{t('Description')}</span>
                    <input
                      type="text"
                      title={t(INPUT_DESCRIPTION_TIP)}
                      value={item.description || ''}
                      disabled={!canManage}
                      onChange={(event) => updateInputReference(index, { description: event.target.value || null })}
                      placeholder={t('Optional operator note or secret purpose')}
                    />
                  </label>
                  {canManage && (
                    <div className="playbook-editor-field">
                      <span>{t('Actions')}</span>
                      <button type="button" className="btn btn-sm btn-danger" onClick={() => setRequiredInputs((current) => current.filter((_, currentIndex) => currentIndex !== index))} title={t('Remove this required input. Nothing changes until you save.')}>
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
          <label className="playbook-editor-field" title={t(CMD_LINTCOMMAND_TIP)}>
            <span>{t('Lint Command')}</span>
            <textarea title={t(CMD_LINTCOMMAND_TIP)} value={lintCommand} disabled={!canManage} onChange={(event) => setLintCommand(event.target.value)} rows={3} />
          </label>
          <label className="playbook-editor-field" title={t(CMD_BUILDCOMMAND_TIP)}>
            <span>{t('Build Command')}</span>
            <textarea title={t(CMD_BUILDCOMMAND_TIP)} value={buildCommand} disabled={!canManage} onChange={(event) => setBuildCommand(event.target.value)} rows={3} />
          </label>
          <label className="playbook-editor-field" title={t(CMD_UNITTESTCOMMAND_TIP)}>
            <span>{t('Unit Test Command (Full)')}</span>
            <textarea title={t(CMD_UNITTESTCOMMAND_TIP)} value={unitTestCommand} disabled={!canManage} onChange={(event) => setUnitTestCommand(event.target.value)} rows={3} placeholder={t('dotnet test')} />
          </label>
          <label className="playbook-editor-field" title={t(CMD_ABBREVIATEDUNITTESTCOMMAND_TIP)}>
            <span>{t('Unit Test Command (Abbreviated)')}</span>
            <textarea title={t(CMD_ABBREVIATEDUNITTESTCOMMAND_TIP)} value={abbreviatedUnitTestCommand} disabled={!canManage} onChange={(event) => setAbbreviatedUnitTestCommand(event.target.value)} rows={3} placeholder={t('dotnet test --filter Category=Smoke')} />
            <span className="text-dim" title={t(UNIT_TEST_SELECTION_HINT)}>{t(UNIT_TEST_SELECTION_HINT)}</span>
          </label>
          <label className="playbook-editor-field" title={t(CMD_INTEGRATIONTESTCOMMAND_TIP)}>
            <span>{t('Integration Test Command')}</span>
            <textarea title={t(CMD_INTEGRATIONTESTCOMMAND_TIP)} value={integrationTestCommand} disabled={!canManage} onChange={(event) => setIntegrationTestCommand(event.target.value)} rows={3} />
          </label>
          <label className="playbook-editor-field" title={t(CMD_E2ETESTCOMMAND_TIP)}>
            <span>{t('E2E Test Command')}</span>
            <textarea title={t(CMD_E2ETESTCOMMAND_TIP)} value={e2eTestCommand} disabled={!canManage} onChange={(event) => setE2ETestCommand(event.target.value)} rows={3} />
          </label>
          <label className="playbook-editor-field" title={t(CMD_MIGRATIONCOMMAND_TIP)}>
            <span>{t('Migration Command')}</span>
            <textarea title={t(CMD_MIGRATIONCOMMAND_TIP)} value={migrationCommand} disabled={!canManage} onChange={(event) => setMigrationCommand(event.target.value)} rows={3} />
          </label>
          <label className="playbook-editor-field" title={t(CMD_SECURITYSCANCOMMAND_TIP)}>
            <span>{t('Security Scan Command')}</span>
            <textarea title={t(CMD_SECURITYSCANCOMMAND_TIP)} value={securityScanCommand} disabled={!canManage} onChange={(event) => setSecurityScanCommand(event.target.value)} rows={3} />
          </label>
          <label className="playbook-editor-field" title={t(CMD_PERFORMANCECOMMAND_TIP)}>
            <span>{t('Performance Command')}</span>
            <textarea title={t(CMD_PERFORMANCECOMMAND_TIP)} value={performanceCommand} disabled={!canManage} onChange={(event) => setPerformanceCommand(event.target.value)} rows={3} />
          </label>
          <label className="playbook-editor-field" title={t(CMD_PACKAGECOMMAND_TIP)}>
            <span>{t('Package Command')}</span>
            <textarea title={t(CMD_PACKAGECOMMAND_TIP)} value={packageCommand} disabled={!canManage} onChange={(event) => setPackageCommand(event.target.value)} rows={3} />
          </label>
          <label className="playbook-editor-field" title={t(CMD_DEPLOYMENTVERIFICATIONCOMMAND_TIP)}>
            <span>{t('Deployment Verification Command')}</span>
            <textarea title={t(CMD_DEPLOYMENTVERIFICATIONCOMMAND_TIP)} value={deploymentVerificationCommand} disabled={!canManage} onChange={(event) => setDeploymentVerificationCommand(event.target.value)} rows={3} />
          </label>
          <label className="playbook-editor-field" title={t(CMD_ROLLBACKVERIFICATIONCOMMAND_TIP)}>
            <span>{t('Rollback Verification Command')}</span>
            <textarea title={t(CMD_ROLLBACKVERIFICATIONCOMMAND_TIP)} value={rollbackVerificationCommand} disabled={!canManage} onChange={(event) => setRollbackVerificationCommand(event.target.value)} rows={3} />
          </label>
          <label className="playbook-editor-field" title={t(CMD_PUBLISHARTIFACTCOMMAND_TIP)}>
            <span>{t('Publish Artifact Command')}</span>
            <textarea title={t(CMD_PUBLISHARTIFACTCOMMAND_TIP)} value={publishArtifactCommand} disabled={!canManage} onChange={(event) => setPublishArtifactCommand(event.target.value)} rows={3} />
          </label>
          <label className="playbook-editor-field" title={t(CMD_RELEASEVERSIONINGCOMMAND_TIP)}>
            <span>{t('Release Versioning Command')}</span>
            <textarea title={t(CMD_RELEASEVERSIONINGCOMMAND_TIP)} value={releaseVersioningCommand} disabled={!canManage} onChange={(event) => setReleaseVersioningCommand(event.target.value)} rows={3} />
          </label>
          <label className="playbook-editor-field" title={t(CMD_CHANGELOGGENERATIONCOMMAND_TIP)}>
            <span>{t('Changelog Generation Command')}</span>
            <textarea title={t(CMD_CHANGELOGGENERATIONCOMMAND_TIP)} value={changelogGenerationCommand} disabled={!canManage} onChange={(event) => setChangelogGenerationCommand(event.target.value)} rows={3} />
          </label>
        </div>

        <div className="card" style={{ marginBottom: '1rem', padding: '1rem' }}>
          <div className="detail-header" style={{ marginBottom: '0.75rem' }}>
            <h3 title={t('Deploy, rollback and verification commands per environment, such as staging or prod. Deployments to an environment use the commands with its name.')}>{t('Environment Commands')}</h3>
            {canManage && (
              <button className="btn btn-sm" type="button" onClick={() => setEnvironments((current) => [...current, blankWorkflowEnvironment()])} title={t('Add an environment with its own deploy and verification commands.')}>
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
                      <button className="btn btn-sm btn-danger" type="button" onClick={() => setEnvironments((current) => current.filter((_, currentIndex) => currentIndex !== index))} title={t('Remove this environment and its commands. Nothing changes until you save.')}>
                        {t('Remove')}
                      </button>
                    )}
                  </div>
                  <div className="detail-grid">
                    <label className="playbook-editor-field" title={t(ENV_NAME_TIP)}>
                      <span>{t('Name')}</span>
                      <input type="text" title={t(ENV_NAME_TIP)} value={environment.environmentName} disabled={!canManage} onChange={(event) => updateEnvironment(index, 'environmentName', event.target.value)} />
                    </label>
                    <label className="playbook-editor-field" title={t(ENV_DEPLOY_TIP)}>
                      <span>{t('Deploy Command')}</span>
                      <textarea title={t(ENV_DEPLOY_TIP)} value={environment.deployCommand || ''} disabled={!canManage} onChange={(event) => updateEnvironment(index, 'deployCommand', event.target.value)} rows={3} />
                    </label>
                    <label className="playbook-editor-field" title={t(ENV_ROLLBACK_TIP)}>
                      <span>{t('Rollback Command')}</span>
                      <textarea title={t(ENV_ROLLBACK_TIP)} value={environment.rollbackCommand || ''} disabled={!canManage} onChange={(event) => updateEnvironment(index, 'rollbackCommand', event.target.value)} rows={3} />
                    </label>
                    <label className="playbook-editor-field" title={t(ENV_SMOKE_TIP)}>
                      <span>{t('Smoke Test Command')}</span>
                      <textarea title={t(ENV_SMOKE_TIP)} value={environment.smokeTestCommand || ''} disabled={!canManage} onChange={(event) => updateEnvironment(index, 'smokeTestCommand', event.target.value)} rows={3} />
                    </label>
                    <label className="playbook-editor-field" title={t(ENV_HEALTH_TIP)}>
                      <span>{t('Health Check Command')}</span>
                      <textarea title={t(ENV_HEALTH_TIP)} value={environment.healthCheckCommand || ''} disabled={!canManage} onChange={(event) => updateEnvironment(index, 'healthCheckCommand', event.target.value)} rows={3} />
                    </label>
                    <label className="playbook-editor-field" title={t(ENV_DEPLOY_VERIFY_TIP)}>
                      <span>{t('Deployment Verification Command')}</span>
                      <textarea title={t(ENV_DEPLOY_VERIFY_TIP)} value={environment.deploymentVerificationCommand || ''} disabled={!canManage} onChange={(event) => updateEnvironment(index, 'deploymentVerificationCommand', event.target.value)} rows={3} />
                    </label>
                    <label className="playbook-editor-field" title={t(ENV_ROLLBACK_VERIFY_TIP)}>
                      <span>{t('Rollback Verification Command')}</span>
                      <textarea title={t(ENV_ROLLBACK_VERIFY_TIP)} value={environment.rollbackVerificationCommand || ''} disabled={!canManage} onChange={(event) => updateEnvironment(index, 'rollbackVerificationCommand', event.target.value)} rows={3} />
                    </label>
                  </div>
                </div>
              ))}
            </div>
          )}
        </div>

        <div className="playbook-editor-actions">
          <button className="btn" disabled={validating} onClick={handleValidate} title={t('Check the profile as currently filled in, without saving, and list errors, warnings and the commands that would run.')}>
            {validating ? t('Validating...') : t('Validate')}
          </button>
          <button className="btn btn-primary" disabled={!canManage || saving} onClick={handleSave} title={createMode ? t('Create this workflow profile.') : t('Save changes. Check runs started after saving use the new commands.')}>
            {saving ? t('Saving...') : createMode ? t('Create Workflow Profile') : t('Save Changes')}
          </button>
          <button className="btn" onClick={() => navigate('/workflow-profiles')} title={t('Return to the workflow profile list. Unsaved changes are discarded.')}>
            {t('Back')}
          </button>
        </div>
      </div>
    </div>
  );
}
