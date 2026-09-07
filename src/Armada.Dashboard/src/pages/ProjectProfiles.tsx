import { useEffect, useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { createProjectProfile, deleteProjectProfile, listFleets, listProjectProfiles, listVessels, updateProjectProfile } from '../api/client';
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
import RefreshButton from '../components/shared/RefreshButton';
import AutoRefreshSelect from '../components/shared/AutoRefreshSelect';
import PageHeader from '../components/shared/PageHeader';
import StatusBadge from '../components/shared/StatusBadge';
import { useAutoRefresh } from '../lib/useAutoRefresh';

function splitList(value: string): string[] {
  return value.split(/\r?\n|,/).map((item) => item.trim()).filter(Boolean);
}

export default function ProjectProfiles() {
  const navigate = useNavigate();
  const { isAdmin, isTenantAdmin, user } = useAuth();
  const viewer: ScopeViewer = { isAdmin, isTenantAdmin, tenantId: user?.user?.tenantId, userId: user?.user?.id };
  const canEditProfile = (p: ProjectProfile) => canEditScoped(viewer, { scope: p.ownershipScope, tenantId: p.tenantId, userId: p.userId });
  const { t, formatDateTime, formatRelativeTime } = useLocale();
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
      const result = await listProjectProfiles({ pageSize: 9999 });
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
    void listFleets().then((r) => setFleets(r.objects || [])).catch(() => {});
    void listVessels().then((r) => setVessels(r.objects || [])).catch(() => {});
  }, []);

  function openCreate() {
    setEditing(null);
    setCreateForm(EMPTY_CREATE_FORM);
    setShowCreate(true);
  }

  function openEdit(profile: ProjectProfile) {
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
        personaOverrides: editing ? (editing.personaOverrides || []) : [],
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
        try {
          await deleteProjectProfile(profile.id);
          pushToast('warning', t('Project profile "{{name}}" deleted.', { name: profile.name }));
          await load();
        } catch (err: unknown) {
          setError(err instanceof Error ? err.message : t('Delete failed.'));
        }
      },
    });
  }

  return (
    <div>
      <PageHeader
        title={t('Project Profiles')}
        subtitle={t('Per-project customization that binds a pipeline, workflow profile, persona prompt overrides, and skills, resolved global to fleet to vessel.')}
        actions={(
          <>
            <AutoRefreshSelect seconds={refreshSeconds} onChange={setRefreshSeconds} />
            <RefreshButton onRefresh={load} title={t('Refresh project profiles')} />
            <button className="btn btn-primary" onClick={openCreate}>
              + {t('Project Profile')}
            </button>
          </>
        )}
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

      {showCreate && (
        <div className="modal-overlay" onClick={() => setShowCreate(false)}>
          <form className="modal" onClick={(event) => event.stopPropagation()} onSubmit={handleCreate}>
            <h3>{editing ? t('Edit Project Profile') : t('Create Project Profile')}</h3>
            <label>{t('Name')}
              <input value={createForm.name} onChange={(event) => setCreateForm((current) => ({ ...current, name: event.target.value }))} required />
            </label>
            <label>{t('Description')}
              <input value={createForm.description} onChange={(event) => setCreateForm((current) => ({ ...current, description: event.target.value }))} />
            </label>
            <label>{t('Scope')}
              <select value={createForm.scope} onChange={(event) => setCreateForm((current) => ({ ...current, scope: event.target.value as 'Global' | 'Fleet' | 'Vessel' }))}>
                <option value="Global">{t('Global')}</option>
                <option value="Fleet">{t('Fleet')}</option>
                <option value="Vessel">{t('Vessel')}</option>
              </select>
            </label>
            <ScopeSelect viewer={viewer} value={createForm.ownershipScope} onChange={(ownershipScope) => setCreateForm((current) => ({ ...current, ownershipScope }))} />
            {createForm.scope === 'Fleet' && (
              <label>{t('Fleet')}
                <select value={createForm.fleetId} onChange={(event) => setCreateForm((current) => ({ ...current, fleetId: event.target.value }))}>
                  <option value="">{t('Select a fleet...')}</option>
                  {fleets.map((f) => <option key={f.id} value={f.id}>{f.name}</option>)}
                </select>
              </label>
            )}
            {createForm.scope === 'Vessel' && (
              <label>{t('Vessel')}
                <select value={createForm.vesselId} onChange={(event) => setCreateForm((current) => ({ ...current, vesselId: event.target.value }))}>
                  <option value="">{t('Select a vessel...')}</option>
                  {vessels.map((v) => <option key={v.id} value={v.id}>{v.name}</option>)}
                </select>
              </label>
            )}
            <label>{t('Default Pipeline ID')}
              <input value={createForm.defaultPipelineId} onChange={(event) => setCreateForm((current) => ({ ...current, defaultPipelineId: event.target.value }))} placeholder="ppl_..." />
            </label>
            <label>{t('Workflow Profile ID')}
              <input value={createForm.workflowProfileId} onChange={(event) => setCreateForm((current) => ({ ...current, workflowProfileId: event.target.value }))} placeholder="wfp_..." />
            </label>
            <label>{t('Skills')}
              <textarea rows={4} value={createForm.skills} onChange={(event) => setCreateForm((current) => ({ ...current, skills: event.target.value }))} placeholder={'dotnet\ntdd'} />
            </label>
            <label style={{ display: 'inline-flex', alignItems: 'center', gap: '0.45rem' }}>
              <input type="checkbox" checked={createForm.isDefault} onChange={(event) => setCreateForm((current) => ({ ...current, isDefault: event.target.checked }))} />
              <span>{t('Default for scope')}</span>
            </label>
            <label style={{ display: 'inline-flex', alignItems: 'center', gap: '0.45rem' }}>
              <input type="checkbox" checked={createForm.active} onChange={(event) => setCreateForm((current) => ({ ...current, active: event.target.checked }))} />
              <span>{t('Active')}</span>
            </label>
            <div className="modal-actions">
              <button type="submit" className="btn btn-primary" disabled={saving}>{saving ? t('Saving...') : editing ? t('Save Changes') : t('Create Project Profile')}</button>
              <button type="button" className="btn" onClick={() => setShowCreate(false)} disabled={saving}>{t('Cancel')}</button>
            </div>
          </form>
        </div>
      )}

      <div className="playbook-overview-grid">
        <div className="card playbook-overview-card">
          <span>{t('Total Profiles')}</span>
          <strong>{profiles.length}</strong>
        </div>
        <div className="card playbook-overview-card">
          <span>{t('Active')}</span>
          <strong>{activeCount}</strong>
        </div>
        <div className="card playbook-overview-card">
          <span>{t('Defaults')}</span>
          <strong>{defaultCount}</strong>
        </div>
        <div className="card playbook-overview-card">
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
            placeholder={t('Search by name, description, or ID...')}
          />
          <select value={scopeFilter} onChange={(event) => setScopeFilter(event.target.value as typeof scopeFilter)}>
            <option value="all">{t('All scopes')}</option>
            <option value="Global">{t('Global')}</option>
            <option value="Fleet">{t('Fleet')}</option>
            <option value="Vessel">{t('Vessel')}</option>
          </select>
          <select value={statusFilter} onChange={(event) => setStatusFilter(event.target.value as typeof statusFilter)}>
            <option value="all">{t('All statuses')}</option>
            <option value="active">{t('Active only')}</option>
            <option value="inactive">{t('Inactive only')}</option>
          </select>
        </div>
      </div>

      {loading && profiles.length === 0 ? (
        <p className="text-dim">{t('Loading...')}</p>
      ) : filtered.length === 0 ? (
        <div className="playbook-empty-state">
          <strong>{t('No project profiles match the current filters.')}</strong>
          <span>{canManage ? t('Create a project profile to customize personas, pipeline, and skills for a project.') : t('Ask a tenant administrator to define project profiles.')}</span>
        </div>
      ) : (
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th>{t('Profile')}</th>
                <th>{t('Scope')}</th>
                <th>{t('Visibility')}</th>
                <th>{t('Overrides')}</th>
                <th>{t('Skills')}</th>
                <th>{t('Status')}</th>
                <th>{t('Last Updated')}</th>
                <th className="text-right">{t('Actions')}</th>
              </tr>
              <tr className="column-filter-row">
                <td><input type="text" className="col-filter" value={colFilters.name} onChange={e => setColFilters(f => ({ ...f, name: e.target.value }))} placeholder={t('Filter...')} /></td>
                <td></td>
                <td></td>
                <td></td>
                <td></td>
                <td></td>
                <td></td>
                <td></td>
              </tr>
            </thead>
            <tbody>
              {filtered.map((profile) => {
                const canEditRow = canEditProfile(profile);
                return (
                <tr key={profile.id} className="clickable" onClick={() => canEditRow ? openEdit(profile) : navigate(`/project-profiles/${profile.id}`)}>
                  <td>
                    <strong>{profile.name}</strong>
                    <div className="mono text-dim" style={{ fontSize: '0.78rem' }}>{profile.id}</div>
                    {profile.description && (
                      <div className="text-dim" style={{ marginTop: '0.2rem' }}>{profile.description}</div>
                    )}
                  </td>
                  <td>
                    <StatusBadge status={profile.scope} />
                    {profile.isDefault && <div className="text-dim" style={{ marginTop: '0.25rem' }}>{t('Default')}</div>}
                  </td>
                  <td><ScopeBadge scope={profile.ownershipScope} /></td>
                  <td className="text-dim">{profile.personaOverrides?.length || 0} {t('personas')}</td>
                  <td className="text-dim">{profile.skills?.length || 0} {t('skills')}</td>
                  <td><StatusBadge status={profile.active ? 'Active' : 'Inactive'} /></td>
                  <td className="text-dim" title={formatDateTime(profile.lastUpdateUtc)}>{formatRelativeTime(profile.lastUpdateUtc)}</td>
                  <td className="text-right" onClick={(event) => event.stopPropagation()}>
                    <ActionMenu
                      id={`project-profile-${profile.id}`}
                      items={[
                        { label: 'Open', onClick: () => navigate(`/project-profiles/${profile.id}`) },
                        ...(canEditRow ? [{ label: 'Edit', onClick: () => openEdit(profile) }] : []),
                        { label: 'View JSON', onClick: () => setJsonData({ open: true, title: profile.name, data: profile }) },
                        ...(canEditRow ? [{ label: 'Delete', danger: true as const, onClick: () => handleDelete(profile) }] : []),
                      ]}
                    />
                  </td>
                </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
