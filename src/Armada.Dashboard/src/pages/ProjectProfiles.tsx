import { duplicateDisplayName } from '../lib/duplicates';
import { useEffect, useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { createProjectProfile, deleteProjectProfile, listFleets, listProjectProfiles, listVessels, updateProjectProfile } from '../api/client';
import { listAll } from '../api/paging';
import { splitList } from '../lib/configuration';
import type { Fleet, ProjectProfile, Vessel, ScopeEnum } from '../types/models';
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
import PageHeader from '../components/shared/PageHeader';
import StatusBadge from '../components/shared/StatusBadge';
import { useAutoRefresh } from '../lib/useAutoRefresh';
import { useDeleteProgress } from '../lib/useDeleteProgress';


const NAME_TIP = 'Name for this project profile, for example web-app or payments-service. Required.';
const DESCRIPTION_TIP = 'Optional note on what this profile customizes.';
const SCOPE_TIP = 'Which vessels the profile applies to: Global for all, Fleet for one fleet\'s vessels, Vessel for one repository. A vessel uses its Vessel profile first, then Fleet, then Global.';
const FLEET_TIP = 'The fleet whose vessels this profile applies to. Required when Scope is Fleet.';
const VESSEL_TIP = 'The single repository this profile applies to. Required when Scope is Vessel.';
const PIPELINE_TIP = 'ID of the pipeline (ppl_ prefix) missions for these vessels run through, copied from the Pipelines tab. Blank keeps the vessel or fleet default pipeline.';
const WORKFLOW_TIP = 'ID of the workflow profile (wfp_ prefix) whose commands these vessels use for checks, copied from the Workflow Profiles tab. Blank uses normal workflow profile matching.';
const SKILLS_TIP = 'Skills to add to every mission prompt for these vessels, one name or ID per line, for example tdd. Unknown or inactive skills are skipped.';
const DEFAULT_TIP = 'When several active profiles match the same vessel at the same scope, the default one is used. Otherwise the most recently updated wins.';
const ACTIVE_TIP = 'On: Armada can use this profile. Off: it is never chosen, even when a vessel names it.';

export default function ProjectProfiles() {
  const navigate = useNavigate();
  const { isAdmin, isTenantAdmin, user } = useAuth();
  const viewer: ScopeViewer = { isAdmin, isTenantAdmin, tenantId: user?.user?.tenantId, userId: user?.user?.id };
  const canEditProfile = (p: ProjectProfile) => canEditScoped(viewer, { scope: p.ownershipScope, tenantId: p.tenantId, userId: p.userId });
  const { t, formatDateTime, formatRelativeTime } = useLocale();
  const deletion = useDeleteProgress();
  const { pushToast } = useNotifications();
  const [profiles, setProfiles] = useState<ProjectProfile[]>([]);
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
    name: 'Default Project Profile',
    description: '',
    scope: 'Global' as 'Global' | 'Fleet' | 'Vessel',
    ownershipScope: resolveCreateScope(viewer) as ScopeEnum,
    fleetId: '',
    vesselId: '',
    isDefault: false,
    active: true,
    defaultPipelineId: '',
    workflowProfileId: '',
    skills: '',
  };

  const [showCreate, setShowCreate] = useState(false);
  const [editing, setEditing] = useState<ProjectProfile | null>(null);
  const [saving, setSaving] = useState(false);
  const [createForm, setCreateForm] = useState(EMPTY_CREATE_FORM);

  async function load() {
    try {
      setLoading(true);
      const result = await listAll(listProjectProfiles);
      setProfiles(result.objects || []);
      setError('');
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Failed to load project profiles.'));
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    load();
  }, []);

  const { seconds: refreshSeconds, setSeconds: setRefreshSeconds } = useAutoRefresh('projectprofiles', load);

  useEffect(() => {
    void listAll(listFleets).then((r) => setFleets(r.objects || [])).catch(() => {});
    void listAll(listVessels).then((r) => setVessels(r.objects || [])).catch(() => {});
  }, []);

  const [duplicateSource, setDuplicateSource] = useState<ProjectProfile | null>(null);

  function openCreate() {
    setDuplicateSource(null);
    setEditing(null);
    setCreateForm(EMPTY_CREATE_FORM);
    setShowCreate(true);
  }

  function openEdit(profile: ProjectProfile) {
    setDuplicateSource(null);
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
      defaultPipelineId: profile.defaultPipelineId || '',
      workflowProfileId: profile.workflowProfileId || '',
      skills: (profile.skills || []).join('\n'),
    });
    setShowCreate(true);
  }

  function openDuplicate(profile: ProjectProfile) {
    openEdit(profile);
    setEditing(null);
    setDuplicateSource(profile);
    setCreateForm(current => ({ ...current, name: duplicateDisplayName(profile.name), isDefault: false, ownershipScope: resolveCreateScope(viewer) }));
  }

  async function handleCreate(event: React.FormEvent) {
    event.preventDefault();
    if (saving) return;
    try {
      setSaving(true);
      const payload: Partial<ProjectProfile> = {
        name: createForm.name,
        description: createForm.description || null,
        scope: createForm.scope,
        ownershipScope: createForm.ownershipScope,
        fleetId: createForm.scope === 'Fleet' ? (createForm.fleetId || null) : null,
        vesselId: createForm.scope === 'Vessel' ? (createForm.vesselId || null) : null,
        isDefault: createForm.isDefault,
        active: createForm.active,
        defaultPipelineId: createForm.defaultPipelineId || null,
        workflowProfileId: createForm.workflowProfileId || null,
        personaOverrides: (editing ?? duplicateSource)?.personaOverrides || [],
        skills: splitList(createForm.skills),
      };
      if (editing) {
        const updated = await updateProjectProfile(editing.id, payload);
        setShowCreate(false);
        pushToast('success', t('Project profile "{{name}}" saved.', { name: updated.name }));
      } else {
        const created = await createProjectProfile(payload);
        setShowCreate(false);
        pushToast('success', t('Project profile "{{name}}" created.', { name: created.name }));
      }
      await load();
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
  const overrideCount = profiles.reduce((total, profile) => total + (profile.personaOverrides?.length || 0), 0);

  function handleDelete(profile: ProjectProfile) {
    setConfirm({
      open: true,
      title: t('Delete Project Profile'),
      message: t('Delete "{{name}}"? Projects using it will fall back to their fleet or global profile.', { name: profile.name }),
      onConfirm: async () => {
        setConfirm((current) => ({ ...current, open: false }));
        if (!(await deletion.run({ kind: 'projectProfile', action: () => deleteProjectProfile(profile.id), errorMessage: (err) => err instanceof Error ? err.message : t('Delete failed.') }))) return;
        pushToast('warning', t('Project profile "{{name}}" deleted.', { name: profile.name }));
        await load();
      },
    });
  }

  const emptyState = (
    <div className="playbook-empty-state">
      <strong>{t('No project profiles match the current filters.')}</strong>
      <span>{canManage ? t('Create a project profile to customize personas, pipeline, and skills for a project.') : t('Ask a tenant administrator to define project profiles.')}</span>
    </div>
  );

  // Name, ID and description used to stack in one cell, and "Default" sat under the scope badge; each is on one line now.
  const columns: DataTableColumn<ProjectProfile>[] = [
    {
      key: 'id', label: t('ID'), required: true, headerTitle: t('Unique project profile ID (ppf_ prefix). Use the copy button to copy it.'), cellClassName: 'mono text-dim table-id-cell',
      render: (profile) => (
        <span className="id-display">
          <span className="id-value" title={profile.id}>{profile.id}</span>
          <CopyButton text={profile.id} onClick={e => e.stopPropagation()} />
        </span>
      ),
    },
    {
      key: 'name', label: t('Profile'), required: true, headerTitle: t('Project profile name. A project profile picks the pipeline, workflow profile, persona overrides and skills for the vessels it applies to.'),
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
    { key: 'overrides', label: t('Overrides'), headerTitle: t('How many personas this profile customizes, with a different prompt template or extra instructions for this project.'), cellClassName: 'text-dim cell-nowrap', render: (profile) => <>{profile.personaOverrides?.length || 0} {t('personas')}</> },
    { key: 'skills', label: t('Skills'), headerTitle: t('How many skills are added to mission prompts for vessels using this profile.'), cellClassName: 'text-dim cell-nowrap', render: (profile) => <>{profile.skills?.length || 0} {t('skills')}</> },
    { key: 'status', label: t('Status'), headerTitle: t('Only active profiles are used. An inactive profile is never chosen, even when a vessel names it.'), cellClassName: 'cell-nowrap', render: (profile) => <StatusBadge status={profile.active ? 'Active' : 'Inactive'} /> },
    {
      key: 'lastUpdated', label: t('Last Updated'), headerTitle: t('When this profile was last changed. Among equally matching profiles, the newest wins after the default.'), cellClassName: 'text-dim cell-nowrap',
      cellTitle: (profile) => formatDateTime(profile.lastUpdateUtc),
      render: (profile) => formatRelativeTime(profile.lastUpdateUtc),
    },
    {
      key: 'actions', label: t('Actions'), headerTitle: t('Open the menu in a row to open, edit or delete that profile.'), fixed: true, interactive: true, className: 'text-right',
      render: (profile) => {
        const canEditRow = canEditProfile(profile);
        return (
          <ActionMenu
            id={`project-profile-${profile.id}`}
            items={[
              { label: 'Open', title: 'Open the full editor, including persona overrides.', onClick: () => navigate(`/project-profiles/${profile.id}`) },
              { label: 'Duplicate', title: 'Edit a new copy before saving, including its persona overrides.', onClick: () => openDuplicate(profile) },
              ...(canEditRow ? [{ label: 'Edit', title: 'Quick edit of the name, scope, pipeline, workflow profile and skills. Use Open for persona overrides.', onClick: () => openEdit(profile) }] : []),
              { label: 'View JSON', title: 'Show this profile\'s full record as JSON, as the API returns it.', onClick: () => setJsonData({ open: true, title: profile.name, data: profile }) },
              ...(canEditRow ? [{ label: 'Delete', danger: true as const, title: 'Permanently delete this profile. Vessels using it fall back to their fleet or global profile. You confirm first.', onClick: () => handleDelete(profile) }] : []),
            ]}
          />
        );
      },
    },
  ];

  return (
    <div>
      <PageHeader
        title={t('Project Profiles')}
        subtitle={t('Per-project customization that binds a pipeline, workflow profile, persona prompt overrides, and skills, resolved global to fleet to vessel.')}
        actions={(
          <>
            <button className="btn btn-primary" onClick={openCreate} title={t('Create a project profile to choose the pipeline, workflow profile, persona overrides and skills for some vessels.')}>
              + {t('Project Profile')}
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
          <form className="modal" onClick={(event) => event.stopPropagation()} onSubmit={handleCreate}>
            <h3>{editing ? t('Edit Project Profile') : t('Create Project Profile')}</h3>
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
                  {fleets.map((f) => <option key={f.id} value={f.id}>{f.name}</option>)}
                </select>
              </label>
            )}
            {createForm.scope === 'Vessel' && (
              <label title={t(VESSEL_TIP)}>{t('Vessel')}
                <select title={t(VESSEL_TIP)} value={createForm.vesselId} onChange={(event) => setCreateForm((current) => ({ ...current, vesselId: event.target.value }))}>
                  <option value="">{t('Select a vessel...')}</option>
                  {vessels.map((v) => <option key={v.id} value={v.id}>{v.name}</option>)}
                </select>
              </label>
            )}
            <label title={t(PIPELINE_TIP)}>{t('Default Pipeline ID')}
              <input title={t(PIPELINE_TIP)} value={createForm.defaultPipelineId} onChange={(event) => setCreateForm((current) => ({ ...current, defaultPipelineId: event.target.value }))} placeholder="ppl_..." />
            </label>
            <label title={t(WORKFLOW_TIP)}>{t('Workflow Profile ID')}
              <input title={t(WORKFLOW_TIP)} value={createForm.workflowProfileId} onChange={(event) => setCreateForm((current) => ({ ...current, workflowProfileId: event.target.value }))} placeholder="wfp_..." />
            </label>
            <label title={t(SKILLS_TIP)}>{t('Skills')}
              <textarea rows={4} title={t(SKILLS_TIP)} value={createForm.skills} onChange={(event) => setCreateForm((current) => ({ ...current, skills: event.target.value }))} placeholder={'dotnet\ntdd'} />
            </label>
            <label style={{ display: 'inline-flex', alignItems: 'center', gap: '0.45rem' }} title={t(DEFAULT_TIP)}>
              <input type="checkbox" title={t(DEFAULT_TIP)} checked={createForm.isDefault} onChange={(event) => setCreateForm((current) => ({ ...current, isDefault: event.target.checked }))} />
              <span>{t('Default for scope')}</span>
            </label>
            <label style={{ display: 'inline-flex', alignItems: 'center', gap: '0.45rem' }} title={t(ACTIVE_TIP)}>
              <input type="checkbox" title={t(ACTIVE_TIP)} checked={createForm.active} onChange={(event) => setCreateForm((current) => ({ ...current, active: event.target.checked }))} />
              <span>{t('Active')}</span>
            </label>
            <div className="modal-actions">
              <button type="submit" className="btn btn-primary" disabled={saving} title={editing ? t('Save changes. Missions dispatched after saving use the new settings.') : t('Create this project profile.')}>{saving ? t('Saving...') : editing ? t('Save Changes') : t('Create Project Profile')}</button>
              <button type="button" className="btn" onClick={() => setShowCreate(false)} disabled={saving}>{t('Cancel')}</button>
            </div>
          </form>
        </div>
      )}

      <div className="playbook-overview-grid">
        <div className="card playbook-overview-card" title={t('All project profiles you can see, active and inactive.')}>
          <span>{t('Total Profiles')}</span>
          <strong>{profiles.length}</strong>
        </div>
        <div className="card playbook-overview-card" title={t('Profiles Armada can choose for vessels.')}>
          <span>{t('Active')}</span>
          <strong>{activeCount}</strong>
        </div>
        <div className="card playbook-overview-card" title={t('Profiles marked default, which win over other profiles that match the same scope.')}>
          <span>{t('Defaults')}</span>
          <strong>{defaultCount}</strong>
        </div>
        <div className="card playbook-overview-card" title={t('Total persona customizations across all project profiles.')}>
          <span>{t('Persona Overrides')}</span>
          <strong>{overrideCount}</strong>
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
        tableKey="projectprofiles"
        columns={columns}
        rows={filtered}
        rowKey={(profile) => profile.id}
        onRowClick={(profile) => (canEditProfile(profile) ? openEdit(profile) : navigate(`/project-profiles/${profile.id}`))}
        autoRefresh={{ seconds: refreshSeconds, onChange: setRefreshSeconds }}
        onRefresh={load}
        refreshTitle={t('Refresh project profiles')}
        emptyMessage={emptyState}
        placeholder={profiles.length > 0 ? undefined : loading ? <p className="text-dim">{t('Loading...')}</p> : emptyState}
      />
    </div>
  );
}
