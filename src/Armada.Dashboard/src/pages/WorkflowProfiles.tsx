import { useEffect, useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { createWorkflowProfile, deleteWorkflowProfile, listFleets, listVessels, listWorkflowProfiles, updateWorkflowProfile } from '../api/client';
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
import RefreshButton from '../components/shared/RefreshButton';
import AutoRefreshSelect from '../components/shared/AutoRefreshSelect';
import { useAutoRefresh } from '../lib/useAutoRefresh';
import PageHeader from '../components/shared/PageHeader';
import StatusBadge from '../components/shared/StatusBadge';
import { buildWorkflowProfileDuplicatePayload } from '../lib/duplicates';

function splitList(value: string): string[] {
  return value
    .split(/\r?\n|,/)
    .map((item) => item.trim())
    .filter(Boolean);
}

function countProfileCapabilities(profile: WorkflowProfile): number {
  const commands = [
    profile.lintCommand,
    profile.buildCommand,
    profile.unitTestCommand,
    profile.integrationTestCommand,
    profile.e2eTestCommand,
    profile.packageCommand,
    profile.publishArtifactCommand,
    profile.releaseVersioningCommand,
    profile.changelogGenerationCommand,
  ].filter((value) => !!value).length;

  const environmentCommands = profile.environments.reduce((total, environment) => total
    + (environment.deployCommand ? 1 : 0)
    + (environment.rollbackCommand ? 1 : 0)
    + (environment.smokeTestCommand ? 1 : 0)
    + (environment.healthCheckCommand ? 1 : 0), 0);

  return commands + environmentCommands;
}

export default function WorkflowProfiles() {
  const navigate = useNavigate();
  const { isAdmin, isTenantAdmin, user } = useAuth();
  const viewer: ScopeViewer = { isAdmin, isTenantAdmin, tenantId: user?.user?.tenantId, userId: user?.user?.id };
  const canEditProfile = (p: WorkflowProfile) => canEditScoped(viewer, { scope: p.ownershipScope, tenantId: p.tenantId, userId: p.userId });
  const { t, formatDateTime, formatRelativeTime } = useLocale();
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
      const result = await listWorkflowProfiles({ pageSize: 9999 });
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
    void listFleets({ pageSize: 9999 }).then((r) => setFleets(r.objects || [])).catch(() => {});
    void listVessels({ pageSize: 9999 }).then((r) => setVessels(r.objects || [])).catch(() => {});
  }, []);

  const { seconds: refreshSeconds, setSeconds: setRefreshSeconds } = useAutoRefresh('workflowprofiles', load);

  const fleetOptions = useMemo(() => fleets.filter((fleet) => fleet.active !== false), [fleets]);
  const vesselOptions = useMemo(() => vessels.filter((vessel) => vessel.active !== false), [vessels]);

  function openCreate() {
    setEditing(null);
    setCreateForm(EMPTY_CREATE_FORM);
    setShowCreate(true);
  }

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
      const payload: Partial<WorkflowProfile> = {
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
        try {
          await deleteWorkflowProfile(profile.id);
          pushToast('warning', t('Workflow profile "{{name}}" deleted.', { name: profile.name }));
          await load();
        } catch (err: unknown) {
          setError(err instanceof Error ? err.message : t('Delete failed.'));
        }
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

  return (
    <div>
      <PageHeader
        title={t('Workflow Profiles')}
        subtitle={t('Tenant-scoped command profiles that tell Armada how each project builds, tests, packages, releases, deploys, and verifies itself.')}
        actions={(
          <>
            <AutoRefreshSelect seconds={refreshSeconds} onChange={setRefreshSeconds} />
            <RefreshButton onRefresh={load} title={t('Refresh workflow profiles')} />
            <button className="btn btn-primary" onClick={openCreate}>
              + {t('Workflow Profile')}
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
          <form className="modal modal-large" onClick={(event) => event.stopPropagation()} onSubmit={handleCreate}>
            <h3>{editing ? t('Edit Workflow Profile') : t('Create Workflow Profile')}</h3>
            <p className="text-dim" style={{ marginTop: 0 }}>
              {t('Set the core details here. Required inputs, environment commands, and the remaining commands can be configured after creation.')}
            </p>
            {editing && (
              <p className="text-dim" style={{ marginTop: 0 }}>
                <a href={`/workflow-profiles/${editing.id}`} onClick={(event) => { event.preventDefault(); navigate(`/workflow-profiles/${editing.id}`); }}>
                  {t('Open full editor')}
                </a>
              </p>
            )}
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
                  {fleetOptions.map((fleet) => <option key={fleet.id} value={fleet.id}>{fleet.name}</option>)}
                </select>
              </label>
            )}
            {createForm.scope === 'Vessel' && (
              <label>{t('Vessel')}
                <select value={createForm.vesselId} onChange={(event) => setCreateForm((current) => ({ ...current, vesselId: event.target.value }))}>
                  <option value="">{t('Select a vessel...')}</option>
                  {vesselOptions.map((vessel) => <option key={vessel.id} value={vessel.id}>{vessel.name}</option>)}
                </select>
              </label>
            )}
            <label>{t('Language / Runtime Hints')}
              <textarea rows={3} value={createForm.languageHints} onChange={(event) => setCreateForm((current) => ({ ...current, languageHints: event.target.value }))} placeholder={t('dotnet\nreact\npostgres')} />
            </label>
            <label>{t('Expected Artifacts')}
              <textarea rows={3} value={createForm.expectedArtifacts} onChange={(event) => setCreateForm((current) => ({ ...current, expectedArtifacts: event.target.value }))} placeholder={t('bin/Release/app.zip\ncoverage/summary.xml')} />
            </label>
            <label>{t('Lint Command')}
              <textarea rows={2} value={createForm.lintCommand} onChange={(event) => setCreateForm((current) => ({ ...current, lintCommand: event.target.value }))} />
            </label>
            <label>{t('Build Command')}
              <textarea rows={2} value={createForm.buildCommand} onChange={(event) => setCreateForm((current) => ({ ...current, buildCommand: event.target.value }))} />
            </label>
            <label>{t('Unit Test Command')}
              <textarea rows={2} value={createForm.unitTestCommand} onChange={(event) => setCreateForm((current) => ({ ...current, unitTestCommand: event.target.value }))} />
            </label>
            <label>{t('Integration Test Command')}
              <textarea rows={2} value={createForm.integrationTestCommand} onChange={(event) => setCreateForm((current) => ({ ...current, integrationTestCommand: event.target.value }))} />
            </label>
            <label>{t('E2E Test Command')}
              <textarea rows={2} value={createForm.e2eTestCommand} onChange={(event) => setCreateForm((current) => ({ ...current, e2eTestCommand: event.target.value }))} />
            </label>
            <label>{t('Package Command')}
              <textarea rows={2} value={createForm.packageCommand} onChange={(event) => setCreateForm((current) => ({ ...current, packageCommand: event.target.value }))} />
            </label>
            <label style={{ display: 'inline-flex', alignItems: 'center', gap: '0.45rem' }}>
              <input type="checkbox" checked={createForm.isDefault} onChange={(event) => setCreateForm((current) => ({ ...current, isDefault: event.target.checked }))} />
              <span>{t('Default for this scope')}</span>
            </label>
            <label style={{ display: 'inline-flex', alignItems: 'center', gap: '0.45rem' }}>
              <input type="checkbox" checked={createForm.active} onChange={(event) => setCreateForm((current) => ({ ...current, active: event.target.checked }))} />
              <span>{t('Active')}</span>
            </label>
            <div className="modal-actions">
              <button type="submit" className="btn btn-primary" disabled={saving}>{saving ? t('Saving...') : editing ? t('Save Changes') : t('Create Workflow Profile')}</button>
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
          <strong>{t('No workflow profiles match the current filters.')}</strong>
          <span>{canManage ? t('Create a workflow profile to teach Armada how a project actually builds and ships.') : t('Ask a tenant administrator to define workflow profiles for shared build and deploy actions.')}</span>
        </div>
      ) : (
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th>{t('Profile')}</th>
                <th>{t('Scope')}</th>
                <th>{t('Visibility')}</th>
                <th>{t('Capabilities')}</th>
                <th>{t('Targets')}</th>
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
                <tr key={profile.id} className="clickable" onClick={() => canEditRow ? openEdit(profile) : navigate(`/workflow-profiles/${profile.id}`)}>
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
                  <td className="text-dim">{countProfileCapabilities(profile)} {t('commands')}</td>
                  <td className="text-dim">{profile.environments.length} {t('environments')}</td>
                  <td><StatusBadge status={profile.active ? 'Active' : 'Inactive'} /></td>
                  <td className="text-dim" title={formatDateTime(profile.lastUpdateUtc)}>{formatRelativeTime(profile.lastUpdateUtc)}</td>
                  <td className="text-right" onClick={(event) => event.stopPropagation()}>
                    <ActionMenu
                      id={`workflow-profile-${profile.id}`}
                      items={[
                        { label: 'Open', onClick: () => navigate(`/workflow-profiles/${profile.id}`) },
                        ...(canEditRow ? [{ label: 'Edit', onClick: () => openEdit(profile) }] : []),
                        { label: 'Duplicate', onClick: () => void handleDuplicate(profile) },
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
