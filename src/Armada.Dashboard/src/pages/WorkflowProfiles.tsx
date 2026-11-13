import { useEffect, useMemo, useState } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { createWorkflowProfile, deleteWorkflowProfile, listFleets, listVessels, listWorkflowProfiles, updateWorkflowProfile } from '../api/client';
import { listAll } from '../api/paging';
import { countProfileCapabilities, splitList } from '../lib/configuration';
import type { Fleet, Vessel, WorkflowProfile, ScopeEnum } from '../types/models';
import { useAuth } from '../context/AuthContext';
import { canEdit as canEditScoped, resolveCreateScope, type ScopeViewer } from '../lib/scoping';
import ScopeBadge from '../components/shared/ScopeBadge';
import ScopeSelect from '../components/shared/ScopeSelect';
import { useLocale } from '../context/LocaleContext';
import { useNotifications } from '../context/NotificationContext';
import ActionMenu from '../components/shared/ActionMenu';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import ErrorModal from '../components/shared/ErrorModal';
import JsonViewer from '../components/shared/JsonViewer';
import CopyButton from '../components/shared/CopyButton';
import DataTable, { type DataTableColumn } from '../components/shared/DataTable';
import { useAutoRefresh } from '../lib/useAutoRefresh';
import PageHeader from '../components/shared/PageHeader';
import StatusBadge from '../components/shared/StatusBadge';
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
const LINTCOMMAND_TIP = 'Shell command Armada runs for a Lint check in the vessel\'s working directory, for example npm run lint. Leave blank if the project has no lint step.';
const BUILDCOMMAND_TIP = 'Shell command Armada runs for a Build check in the vessel\'s working directory, for example dotnet build. Leave blank if the project has no build step.';
const UNITTESTCOMMAND_TIP = 'Full unit-test command: the complete unit-test suite, run in the vessel\'s working directory, for example dotnet test. Used by Unit Test checks and by stage checks unless the abbreviated suite is requested. Leave blank if the project has no unit test step.';
const ABBREVIATEDUNITTESTCOMMAND_TIP = 'Abbreviated unit-test command: a fast subset of the unit tests, for example dotnet test --filter Category=Smoke. Used by Abbreviated Unit Test checks, and by stage checks when the mission description or the stage persona\'s prompt contains [ARMADA:UNIT-TESTS] abbreviated. When blank, those run the full command instead.';
const UNIT_TEST_SELECTION_HINT = 'Which unit-test command runs: you can choose directly by running an Abbreviated Unit Test check, or by putting [ARMADA:UNIT-TESTS] abbreviated (or full) in a mission description. Otherwise the stage persona\'s prompt decides, for example a Test Engineer prompt template containing that line. With neither, the full command runs.';
const INTEGRATIONTESTCOMMAND_TIP = 'Shell command Armada runs for an Integration Test check in the vessel\'s working directory, for example npm run test:integration. Leave blank if the project has no integration test step.';
const E2ETESTCOMMAND_TIP = 'Shell command Armada runs for an E2E Test check in the vessel\'s working directory, for example npx playwright test. Leave blank if the project has no end-to-end test step.';
const PACKAGECOMMAND_TIP = 'Shell command Armada runs for a Package check in the vessel\'s working directory, for example dotnet publish -c Release. Leave blank if the project has no package step.';

export default function WorkflowProfiles() {
  const navigate = useNavigate();
  const { isAdmin, isTenantAdmin, user } = useAuth();
  const viewer: ScopeViewer = { isAdmin, isTenantAdmin, tenantId: user?.user?.tenantId, userId: user?.user?.id };
  const canEditProfile = (p: WorkflowProfile) => canEditScoped(viewer, { scope: p.ownershipScope, tenantId: p.tenantId, userId: p.userId });
  const { t, formatDateTime, formatRelativeTime } = useLocale();
  const deletion = useDeleteProgress();
  const { pushToast } = useNotifications();
  const [profiles, setProfiles] = useState<WorkflowProfile[]>([]);
  const [fleets, setFleets] = useState<Fleet[]>([]);
  const [vessels, setVessels] = useState<Vessel[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [search, setSearch] = useState('');
  const [scopeFilter, setScopeFilter] = useState<'all' | 'Global' | 'Fleet' | 'Vessel'>('all');
  const [statusFilter, setStatusFilter] = useState<'all' | 'active' | 'inactive'>('all');
  const [colFilters, setColFilters] = useState({ name: '' });
  const [jsonData, setJsonData] = useState<{ open: boolean; title: string; data: unknown }>({ open: false, title: '', data: null });
  const [confirm, setConfirm] = useState<{ open: boolean; title: string; message: string; onConfirm: () => void }>({
    open: false,
    title: '',
    message: '',
    onConfirm: () => {},
  });

  const canManage = isAdmin || isTenantAdmin;

  const EMPTY_CREATE_FORM = {
    name: 'Default Workflow',
    description: '',
    scope: 'Global' as 'Global' | 'Fleet' | 'Vessel',
    ownershipScope: resolveCreateScope(viewer) as ScopeEnum,
    fleetId: '',
    vesselId: '',
    isDefault: false,
    active: true,
    languageHints: '',
    expectedArtifacts: '',
    lintCommand: '',
    buildCommand: '',
    unitTestCommand: '',
    abbreviatedUnitTestCommand: '',
    integrationTestCommand: '',
    e2eTestCommand: '',
    packageCommand: '',
  };

  const [showCreate, setShowCreate] = useState(false);
  const [editing, setEditing] = useState<WorkflowProfile | null>(null);
  const [saving, setSaving] = useState(false);
  const [createForm, setCreateForm] = useState(EMPTY_CREATE_FORM);

  async function load() {
    try {
      setLoading(true);
      const result = await listAll(listWorkflowProfiles);
      setProfiles(result.objects || []);
      setError('');
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Failed to load workflow profiles.'));
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    load();
  }, []);

  useEffect(() => {
    void listAll(listFleets).then((r) => setFleets(r.objects || [])).catch(() => {});
    void listAll(listVessels).then((r) => setVessels(r.objects || [])).catch(() => {});
  }, []);

  const { seconds: refreshSeconds, setSeconds: setRefreshSeconds } = useAutoRefresh('workflowprofiles', load);

  const fleetOptions = useMemo(() => fleets.filter((fleet) => fleet.active !== false), [fleets]);
  const vesselOptions = useMemo(() => vessels.filter((vessel) => vessel.active !== false), [vessels]);

  function openCreate() {
    setEditing(null);
    setCreateForm(EMPTY_CREATE_FORM);
    setShowCreate(true);
  }

  // A link such as a readiness issue's "Create a workflow profile" (?createProfile=vessel&vesselId=...) opens the
  // create form scoped to that vessel (or fleet); the parameters are cleared so a refresh does not reopen it.
  const [searchParams, setSearchParams] = useSearchParams();
  useEffect(() => {
    const requested = searchParams.get('createProfile');
    if (!requested) return;
    const vesselId = searchParams.get('vesselId') || '';
    const fleetId = searchParams.get('fleetId') || '';
    const scope: 'Global' | 'Fleet' | 'Vessel' = requested === 'vessel' && vesselId ? 'Vessel' : requested === 'fleet' && fleetId ? 'Fleet' : 'Global';
    setEditing(null);
    setCreateForm({ ...EMPTY_CREATE_FORM, scope, vesselId: scope === 'Vessel' ? vesselId : '', fleetId: scope === 'Fleet' ? fleetId : '' });
    setShowCreate(true);
    const next = new URLSearchParams(searchParams);
    next.delete('createProfile');
    next.delete('vesselId');
    next.delete('fleetId');
    setSearchParams(next, { replace: true });
    // EMPTY_CREATE_FORM is rebuilt each render from the viewer; only the link parameters should trigger this.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [searchParams, setSearchParams]);

  function openEdit(profile: WorkflowProfile) {
    setEditing(profile);
    setCreateForm({
      name: profile.name,
      description: profile.description || '',
      scope: profile.scope,
      ownershipScope: profile.ownershipScope,
      fleetId: profile.fleetId || '',
      vesselId: profile.vesselId || '',
      isDefault: profile.isDefault,
      active: profile.active,
      languageHints: (profile.languageHints || []).join('\n'),
      expectedArtifacts: (profile.expectedArtifacts || []).join('\n'),
      lintCommand: profile.lintCommand || '',
      buildCommand: profile.buildCommand || '',
      unitTestCommand: profile.unitTestCommand || '',
      abbreviatedUnitTestCommand: profile.abbreviatedUnitTestCommand || '',
      integrationTestCommand: profile.integrationTestCommand || '',
      e2eTestCommand: profile.e2eTestCommand || '',
      packageCommand: profile.packageCommand || '',
    });
    setShowCreate(true);
  }

  async function handleCreate(event: React.FormEvent) {
    event.preventDefault();
    if (saving) return;
    try {
      setSaving(true);
      // The server replaces every field on update, so an edit keeps the commands this modal does not show.
      const payload: Partial<WorkflowProfile> = {
        ...(editing ?? {}),
        name: createForm.name.trim(),
        description: createForm.description.trim() || null,
        scope: createForm.scope,
        ownershipScope: createForm.ownershipScope,
        fleetId: createForm.scope === 'Fleet' ? createForm.fleetId || null : null,
        vesselId: createForm.scope === 'Vessel' ? createForm.vesselId || null : null,
        isDefault: createForm.isDefault,
        active: createForm.active,
        languageHints: splitList(createForm.languageHints),
        expectedArtifacts: splitList(createForm.expectedArtifacts),
        lintCommand: createForm.lintCommand.trim() || null,
        buildCommand: createForm.buildCommand.trim() || null,
        unitTestCommand: createForm.unitTestCommand.trim() || null,
        abbreviatedUnitTestCommand: createForm.abbreviatedUnitTestCommand.trim() || null,
        integrationTestCommand: createForm.integrationTestCommand.trim() || null,
        e2eTestCommand: createForm.e2eTestCommand.trim() || null,
        packageCommand: createForm.packageCommand.trim() || null,
        requiredInputs: editing ? editing.requiredInputs : [],
        environments: editing ? editing.environments : [],
      };
      if (editing) {
        const updated = await updateWorkflowProfile(editing.id, payload);
        setShowCreate(false);
        pushToast('success', t('Workflow profile "{{name}}" saved.', { name: updated.name }));
        await load();
      } else {
        const created = await createWorkflowProfile(payload);
        setShowCreate(false);
        pushToast('success', t('Workflow profile "{{name}}" created.', { name: created.name }));
        navigate(`/workflow-profiles/${created.id}`);
      }
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Save failed.'));
    } finally {
      setSaving(false);
    }
  }

  const filtered = useMemo(() => profiles.filter((profile) => {
    const matchesSearch = search.trim().length === 0
      || profile.name.toLowerCase().includes(search.toLowerCase())
      || (profile.description || '').toLowerCase().includes(search.toLowerCase())
      || profile.id.toLowerCase().includes(search.toLowerCase());

    const matchesScope = scopeFilter === 'all' || profile.scope === scopeFilter;
    const matchesStatus = statusFilter === 'all'
      || (statusFilter === 'active' && profile.active)
      || (statusFilter === 'inactive' && !profile.active);

    const matchesColumns = !colFilters.name || (profile.name ?? '').toLowerCase().includes(colFilters.name.toLowerCase());

    return matchesSearch && matchesScope && matchesStatus && matchesColumns;
  }), [profiles, scopeFilter, search, statusFilter, colFilters]);

  const defaultCount = profiles.filter((profile) => profile.isDefault).length;
  const activeCount = profiles.filter((profile) => profile.active).length;

  function handleDelete(profile: WorkflowProfile) {
    setConfirm({
      open: true,
      title: t('Delete Workflow Profile'),
      message: t('Delete "{{name}}"? Existing check runs remain, but this profile will no longer be available.', { name: profile.name }),
      onConfirm: async () => {
        setConfirm((current) => ({ ...current, open: false }));
        if (!(await deletion.run({ kind: 'workflowProfile', action: () => deleteWorkflowProfile(profile.id), errorMessage: (err) => err instanceof Error ? err.message : t('Delete failed.') }))) return;
        pushToast('warning', t('Workflow profile "{{name}}" deleted.', { name: profile.name }));
        await load();
      },
    });
  }

  async function handleDuplicate(profile: WorkflowProfile) {
    try {
      const created = await createWorkflowProfile(buildWorkflowProfileDuplicatePayload(profile));
      pushToast('success', t('Workflow profile "{{name}}" duplicated.', { name: created.name }));
      navigate(`/workflow-profiles/${created.id}`);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Duplicate failed.'));
    }
  }

  const emptyState = (
    <div className="playbook-empty-state">
      <strong>{t('No workflow profiles match the current filters.')}</strong>
      <span>{canManage ? t('Create a workflow profile to teach Armada how a project actually builds and ships.') : t('Ask a tenant administrator to define workflow profiles for shared build and deploy actions.')}</span>
    </div>
  );

  // Name, ID and description used to stack in one cell, and "Default" sat under the scope badge; each is on one line now.
  const columns: DataTableColumn<WorkflowProfile>[] = [
    {
      key: 'id', label: t('ID'), required: true, headerTitle: t('Unique workflow profile ID (wfp_ prefix), used when a vessel or check run names a profile directly. Use the copy button to copy it.'), cellClassName: 'mono text-dim table-id-cell',
      render: (profile) => (
        <span className="id-display">
          <span className="id-value" title={profile.id}>{profile.id}</span>
          <CopyButton text={profile.id} onClick={e => e.stopPropagation()} />
        </span>
      ),
    },
    {
      key: 'name', label: t('Profile'), required: true, headerTitle: t('Workflow profile name. A profile holds the commands Armada runs to lint, build, test, package and deploy a project.'),
      clearFilter: () => setColFilters(f => ({ ...f, name: '' })),
      filter: <input type="text" className="col-filter" aria-label={t('Profile')} title={t('Show only profiles whose name contains this text.')} value={colFilters.name} onChange={e => setColFilters(f => ({ ...f, name: e.target.value }))} placeholder={t('Filter...')} />,
      render: (profile) => <strong>{profile.name}</strong>,
    },
    {
      key: 'description', label: t('Description'), headerTitle: t('What this profile is for. Hover a value for the full text.'), defaultHidden: true, cellClassName: 'text-dim truncate-cell',
      render: (profile) => profile.description ? <span className="truncate-text" title={profile.description}>{profile.description}</span> : '-',
    },
    {
      key: 'scope', label: t('Scope'), headerTitle: t(SCOPE_TIP), cellClassName: 'cell-nowrap',
      render: (profile) => (
        <>
          <StatusBadge status={profile.scope} />
          {profile.isDefault && <span className="text-dim" title={t('Default: chosen first among active profiles that match the same scope.')}> {t('Default')}</span>}
        </>
      ),
    },
    { key: 'visibility', label: t('Visibility'), headerTitle: t('Tenant-wide profiles are usable by everyone in the tenant and edited by admins; personal ones only by their owner.'), cellClassName: 'cell-nowrap', render: (profile) => <ScopeBadge scope={profile.ownershipScope} /> },
    { key: 'capabilities', label: t('Capabilities'), headerTitle: t('How many check commands (lint, build, test, package, release and so on) the profile defines. Checks without a command cannot run.'), cellClassName: 'text-dim cell-nowrap', render: (profile) => <>{countProfileCapabilities(profile)} {t('commands')}</> },
    { key: 'targets', label: t('Targets'), headerTitle: t('How many deployment environments (for example staging or production) have their own deploy, rollback and verify commands.'), cellClassName: 'text-dim cell-nowrap', render: (profile) => <>{profile.environments.length} {t('environments')}</> },
    { key: 'status', label: t('Status'), headerTitle: t('Only active profiles are used. An inactive profile is never chosen, even when a vessel names it.'), cellClassName: 'cell-nowrap', render: (profile) => <StatusBadge status={profile.active ? 'Active' : 'Inactive'} /> },
    {
      key: 'lastUpdated', label: t('Last Updated'), headerTitle: t('When this profile was last changed. Among equally matching profiles, the newest wins after the default.'), cellClassName: 'text-dim cell-nowrap',
      cellTitle: (profile) => formatDateTime(profile.lastUpdateUtc),
      render: (profile) => formatRelativeTime(profile.lastUpdateUtc),
    },
    {
      key: 'actions', label: t('Actions'), headerTitle: t('Open the menu in a row to open, edit, duplicate or delete that profile.'), fixed: true, interactive: true, className: 'text-right',
      render: (profile) => {
        const canEditRow = canEditProfile(profile);
        return (
          <ActionMenu
            id={`workflow-profile-${profile.id}`}
            items={[
              { label: 'Open', title: 'Open the full editor with every command, environment, required input and readiness check.', onClick: () => navigate(`/workflow-profiles/${profile.id}`) },
              ...(canEditRow ? [{ label: 'Edit', title: 'Quick edit of the name, scope and main commands. Use Open for environments and the remaining commands.', onClick: () => openEdit(profile) }] : []),
              { label: 'Duplicate', title: 'Create a copy of this profile under a new name, then open it.', onClick: () => void handleDuplicate(profile) },
              { label: 'View JSON', title: 'Show this profile\'s full record as JSON, as the API returns it.', onClick: () => setJsonData({ open: true, title: profile.name, data: profile }) },
              ...(canEditRow ? [{ label: 'Delete', danger: true as const, title: 'Permanently delete this profile. Past check runs are kept; vessels fall back to the next matching profile. You confirm first.', onClick: () => handleDelete(profile) }] : []),
            ]}
          />
        );
      },
    },
  ];

  return (
    <div>
      <PageHeader
        title={t('Workflow Profiles')}
        subtitle={t('Tenant-scoped command profiles that tell Armada how each project builds, tests, packages, releases, deploys, and verifies itself.')}
        actions={(
          <>
            <button className="btn btn-primary" onClick={openCreate} title={t('Create a workflow profile: the commands Armada runs to lint, build, test, package and deploy a project.')}>
              + {t('Workflow Profile')}
            </button>
          </>
        )}
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

      {showCreate && (
        <div className="modal-overlay" onClick={() => setShowCreate(false)}>
          <form className="modal modal-large" onClick={(event) => event.stopPropagation()} onSubmit={handleCreate}>
            <h3>{editing ? t('Edit Workflow Profile') : t('Create Workflow Profile')}</h3>
            <p className="text-dim" style={{ marginTop: 0 }}>
              {t('Set the core details here. Required inputs, environment commands, and the remaining commands can be configured after creation.')}
            </p>
            {editing && (
              <p className="text-dim" style={{ marginTop: 0 }}>
                <a href={`/workflow-profiles/${editing.id}`} onClick={(event) => { event.preventDefault(); navigate(`/workflow-profiles/${editing.id}`); }} title={t('Open this profile\'s full page to edit environments, required inputs and every command. Unsaved changes here are lost.')}>
                  {t('Open full editor')}
                </a>
              </p>
            )}
            <label title={t(NAME_TIP)}>{t('Name')}
              <input title={t(NAME_TIP)} value={createForm.name} onChange={(event) => setCreateForm((current) => ({ ...current, name: event.target.value }))} required />
            </label>
            <label title={t(DESCRIPTION_TIP)}>{t('Description')}
              <input title={t(DESCRIPTION_TIP)} value={createForm.description} onChange={(event) => setCreateForm((current) => ({ ...current, description: event.target.value }))} />
            </label>
            <label title={t(SCOPE_TIP)}>{t('Scope')}
              <select title={t(SCOPE_TIP)} value={createForm.scope} onChange={(event) => setCreateForm((current) => ({ ...current, scope: event.target.value as 'Global' | 'Fleet' | 'Vessel' }))}>
                <option value="Global">{t('Global')}</option>
                <option value="Fleet">{t('Fleet')}</option>
                <option value="Vessel">{t('Vessel')}</option>
              </select>
            </label>
            <ScopeSelect viewer={viewer} value={createForm.ownershipScope} onChange={(ownershipScope) => setCreateForm((current) => ({ ...current, ownershipScope }))} />
            {createForm.scope === 'Fleet' && (
              <label title={t(FLEET_TIP)}>{t('Fleet')}
                <select title={t(FLEET_TIP)} value={createForm.fleetId} onChange={(event) => setCreateForm((current) => ({ ...current, fleetId: event.target.value }))}>
                  <option value="">{t('Select a fleet...')}</option>
                  {fleetOptions.map((fleet) => <option key={fleet.id} value={fleet.id}>{fleet.name}</option>)}
                </select>
              </label>
            )}
            {createForm.scope === 'Vessel' && (
              <label title={t(VESSEL_TIP)}>{t('Vessel')}
                <select title={t(VESSEL_TIP)} value={createForm.vesselId} onChange={(event) => setCreateForm((current) => ({ ...current, vesselId: event.target.value }))}>
                  <option value="">{t('Select a vessel...')}</option>
                  {vesselOptions.map((vessel) => <option key={vessel.id} value={vessel.id}>{vessel.name}</option>)}
                </select>
              </label>
            )}
            <label title={t(HINTS_TIP)}>{t('Language / Runtime Hints')}
              <textarea rows={3} title={t(HINTS_TIP)} value={createForm.languageHints} onChange={(event) => setCreateForm((current) => ({ ...current, languageHints: event.target.value }))} placeholder={t('dotnet\nreact\npostgres')} />
            </label>
            <label title={t(ARTIFACTS_TIP)}>{t('Expected Artifacts')}
              <textarea rows={3} title={t(ARTIFACTS_TIP)} value={createForm.expectedArtifacts} onChange={(event) => setCreateForm((current) => ({ ...current, expectedArtifacts: event.target.value }))} placeholder={t('bin/Release/app.zip\ncoverage/summary.xml')} />
            </label>
            <label title={t(LINTCOMMAND_TIP)}>{t('Lint Command')}
              <textarea rows={2} title={t(LINTCOMMAND_TIP)} value={createForm.lintCommand} onChange={(event) => setCreateForm((current) => ({ ...current, lintCommand: event.target.value }))} />
            </label>
            <label title={t(BUILDCOMMAND_TIP)}>{t('Build Command')}
              <textarea rows={2} title={t(BUILDCOMMAND_TIP)} value={createForm.buildCommand} onChange={(event) => setCreateForm((current) => ({ ...current, buildCommand: event.target.value }))} />
            </label>
            <label title={t(UNITTESTCOMMAND_TIP)}>{t('Unit Test Command (Full)')}
              <textarea rows={2} title={t(UNITTESTCOMMAND_TIP)} value={createForm.unitTestCommand} onChange={(event) => setCreateForm((current) => ({ ...current, unitTestCommand: event.target.value }))} placeholder={t('dotnet test')} />
            </label>
            <label title={t(ABBREVIATEDUNITTESTCOMMAND_TIP)}>{t('Unit Test Command (Abbreviated)')}
              <textarea rows={2} title={t(ABBREVIATEDUNITTESTCOMMAND_TIP)} value={createForm.abbreviatedUnitTestCommand} onChange={(event) => setCreateForm((current) => ({ ...current, abbreviatedUnitTestCommand: event.target.value }))} placeholder={t('dotnet test --filter Category=Smoke')} />
            </label>
            <p className="text-dim" style={{ marginTop: 0 }} title={t(UNIT_TEST_SELECTION_HINT)}>{t(UNIT_TEST_SELECTION_HINT)}</p>
            <label title={t(INTEGRATIONTESTCOMMAND_TIP)}>{t('Integration Test Command')}
              <textarea rows={2} title={t(INTEGRATIONTESTCOMMAND_TIP)} value={createForm.integrationTestCommand} onChange={(event) => setCreateForm((current) => ({ ...current, integrationTestCommand: event.target.value }))} />
            </label>
            <label title={t(E2ETESTCOMMAND_TIP)}>{t('E2E Test Command')}
              <textarea rows={2} title={t(E2ETESTCOMMAND_TIP)} value={createForm.e2eTestCommand} onChange={(event) => setCreateForm((current) => ({ ...current, e2eTestCommand: event.target.value }))} />
            </label>
            <label title={t(PACKAGECOMMAND_TIP)}>{t('Package Command')}
              <textarea rows={2} title={t(PACKAGECOMMAND_TIP)} value={createForm.packageCommand} onChange={(event) => setCreateForm((current) => ({ ...current, packageCommand: event.target.value }))} />
            </label>
            <label className="checkbox-row" title={t(DEFAULT_TIP)}>
              <input type="checkbox" title={t(DEFAULT_TIP)} checked={createForm.isDefault} onChange={(event) => setCreateForm((current) => ({ ...current, isDefault: event.target.checked }))} />
              <span>{t('Default for this scope')}</span>
            </label>
            <label className="checkbox-row" title={t(ACTIVE_TIP)}>
              <input type="checkbox" title={t(ACTIVE_TIP)} checked={createForm.active} onChange={(event) => setCreateForm((current) => ({ ...current, active: event.target.checked }))} />
              <span>{t('Active')}</span>
            </label>
            <div className="modal-actions">
              <button type="submit" className="btn btn-primary" disabled={saving} title={editing ? t('Save changes. Check runs started after saving use the new commands.') : t('Create this profile. You can add environments and the remaining commands on its page afterward.')}>{saving ? t('Saving...') : editing ? t('Save Changes') : t('Create Workflow Profile')}</button>
              <button type="button" className="btn" onClick={() => setShowCreate(false)} disabled={saving} title={t('Close without saving. Nothing you entered here is kept.')}>{t('Cancel')}</button>
            </div>
          </form>
        </div>
      )}

      <div className="playbook-overview-grid">
        <div className="card playbook-overview-card" title={t('All workflow profiles you can see, active and inactive.')}>
          <span>{t('Total Profiles')}</span>
          <strong>{profiles.length}</strong>
        </div>
        <div className="card playbook-overview-card" title={t('Profiles Armada can choose for vessels and check runs.')}>
          <span>{t('Active')}</span>
          <strong>{activeCount}</strong>
        </div>
        <div className="card playbook-overview-card" title={t('Profiles marked default, which win over other profiles that match the same scope.')}>
          <span>{t('Defaults')}</span>
          <strong>{defaultCount}</strong>
        </div>
        <div className="card playbook-overview-card" title={t('Total deployment environments defined across all profiles.')}>
          <span>{t('Environment Targets')}</span>
          <strong>{profiles.reduce((total, profile) => total + profile.environments.length, 0)}</strong>
        </div>
      </div>

      <div className="card" style={{ padding: '1rem', marginBottom: '1rem' }}>
        <div className="playbook-filter-row">
          <input
            type="text"
            value={search}
            onChange={(event) => setSearch(event.target.value)}
            title={t('Show only profiles whose name, description or ID contains this text.')}
            placeholder={t('Search by name, description, or ID...')}
          />
          <select aria-label={t('All scopes')} title={t('Show profiles of every scope, or only Global, Fleet or Vessel ones.')} value={scopeFilter} onChange={(event) => setScopeFilter(event.target.value as typeof scopeFilter)}>
            <option value="all">{t('All scopes')}</option>
            <option value="Global">{t('Global')}</option>
            <option value="Fleet">{t('Fleet')}</option>
            <option value="Vessel">{t('Vessel')}</option>
          </select>
          <select aria-label={t('All statuses')} title={t('Show all profiles, or only active or only inactive ones.')} value={statusFilter} onChange={(event) => setStatusFilter(event.target.value as typeof statusFilter)}>
            <option value="all">{t('All statuses')}</option>
            <option value="active">{t('Active only')}</option>
            <option value="inactive">{t('Inactive only')}</option>
          </select>
        </div>
      </div>

      <DataTable
        tableKey="workflowprofiles"
        columns={columns}
        rows={filtered}
        rowKey={(profile) => profile.id}
        onRowClick={(profile) => (canEditProfile(profile) ? openEdit(profile) : navigate(`/workflow-profiles/${profile.id}`))}
        autoRefresh={{ seconds: refreshSeconds, onChange: setRefreshSeconds }}
        onRefresh={load}
        refreshTitle={t('Refresh workflow profiles')}
        emptyMessage={emptyState}
        placeholder={profiles.length > 0 ? undefined : loading ? <p className="text-dim">{t('Loading...')}</p> : emptyState}
      />
    </div>
  );
}
