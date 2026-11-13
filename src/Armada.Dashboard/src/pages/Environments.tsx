import { useEffect, useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { createEnvironment, deleteEnvironment, listEnvironments, listVessels, updateEnvironment } from '../api/client';
import { listAll } from '../api/paging';
import type { DeploymentEnvironment, DeploymentEnvironmentUpsertRequest, EnvironmentKind, Vessel } from '../types/models';
import { useAuth } from '../context/AuthContext';
import { useLocale } from '../context/LocaleContext';
import { useNotifications } from '../context/NotificationContext';
import ActionMenu from '../components/shared/ActionMenu';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import ErrorModal from '../components/shared/ErrorModal';
import JsonViewer from '../components/shared/JsonViewer';
import CopyButton from '../components/shared/CopyButton';
import DataTable, { type DataTableColumn } from '../components/shared/DataTable';
import PageHeader from '../components/shared/PageHeader';
import { useAutoRefresh } from '../lib/useAutoRefresh';
import { buildEnvironmentDuplicatePayload } from '../lib/duplicates';
import { ENVIRONMENT_KINDS } from '../lib/environmentForm';
import { useDeleteProgress } from '../lib/useDeleteProgress';

export default function Environments() {
  const navigate = useNavigate();
  const { isAdmin, isTenantAdmin } = useAuth();
  const { t, formatDateTime, formatRelativeTime } = useLocale();
  const { pushToast } = useNotifications();
  const deletion = useDeleteProgress();

  const [environments, setEnvironments] = useState<DeploymentEnvironment[]>([]);
  const [vessels, setVessels] = useState<Vessel[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [search, setSearch] = useState('');
  const [kindFilter, setKindFilter] = useState<'all' | EnvironmentKind>('all');
  const [vesselFilter, setVesselFilter] = useState('all');
  const [activeFilter, setActiveFilter] = useState<'all' | 'active' | 'inactive'>('all');
  const [colFilters, setColFilters] = useState({ name: '', baseUrl: '', health: '' });
  const [jsonData, setJsonData] = useState<{ open: boolean; title: string; data: unknown }>({ open: false, title: '', data: null });
  const [confirm, setConfirm] = useState<{ open: boolean; title: string; message: string; onConfirm: () => void }>({
    open: false,
    title: '',
    message: '',
    onConfirm: () => {},
  });

  // Create/Edit modal
  const [showCreate, setShowCreate] = useState(false);
  const [editing, setEditing] = useState<DeploymentEnvironment | null>(null);
  const [saving, setSaving] = useState(false);
  const [createForm, setCreateForm] = useState<{
    vesselId: string;
    name: string;
    kind: EnvironmentKind;
    configurationSource: string;
    baseUrl: string;
    healthEndpoint: string;
    description: string;
    accessNotes: string;
    deploymentRules: string;
    requiresApproval: boolean;
    isDefault: boolean;
    active: boolean;
  }>({
    vesselId: '',
    name: 'Environment',
    kind: 'Development',
    configurationSource: '',
    baseUrl: '',
    healthEndpoint: '',
    description: '',
    accessNotes: '',
    deploymentRules: '',
    requiresApproval: false,
    isDefault: false,
    active: true,
  });

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
  };

  function openCreate() {
    setEditing(null);
    setCreateForm({
      vesselId: '',
      name: 'Environment',
      kind: 'Development',
      configurationSource: '',
      baseUrl: '',
      healthEndpoint: '',
      description: '',
      accessNotes: '',
      deploymentRules: '',
      requiresApproval: false,
      isDefault: false,
      active: true,
    });
    setShowCreate(true);
  }

  function openEdit(environment: DeploymentEnvironment) {
    setEditing(environment);
    setCreateForm({
      vesselId: environment.vesselId || '',
      name: environment.name,
      kind: environment.kind,
      configurationSource: environment.configurationSource || '',
      baseUrl: environment.baseUrl || '',
      healthEndpoint: environment.healthEndpoint || '',
      description: environment.description || '',
      accessNotes: environment.accessNotes || '',
      deploymentRules: environment.deploymentRules || '',
      requiresApproval: environment.requiresApproval,
      isDefault: environment.isDefault,
      active: environment.active,
    });
    setShowCreate(true);
  }

  async function handleCreate(event: React.FormEvent) {
    event.preventDefault();
    if (saving) return;
    setSaving(true);
    try {
      const payload: DeploymentEnvironmentUpsertRequest = {
        vesselId: createForm.vesselId || null,
        name: createForm.name.trim() || null,
        description: createForm.description.trim() || null,
        kind: createForm.kind,
        configurationSource: createForm.configurationSource.trim() || null,
        baseUrl: createForm.baseUrl.trim() || null,
        healthEndpoint: createForm.healthEndpoint.trim() || null,
        accessNotes: createForm.accessNotes.trim() || null,
        deploymentRules: createForm.deploymentRules.trim() || null,
        verificationDefinitions: editing ? editing.verificationDefinitions : [],
        rolloutMonitoringWindowMinutes: editing ? editing.rolloutMonitoringWindowMinutes : 60,
        rolloutMonitoringIntervalSeconds: editing ? editing.rolloutMonitoringIntervalSeconds : 300,
        alertOnRegression: editing ? editing.alertOnRegression : true,
        requiresApproval: createForm.requiresApproval,
        isDefault: createForm.isDefault,
        active: createForm.active,
      };
      if (editing) {
        const updated = await updateEnvironment(editing.id, payload);
        setShowCreate(false);
        pushToast('success', t('Environment "{{name}}" saved.', { name: updated.name }));
      } else {
        const created = await createEnvironment(payload);
        setShowCreate(false);
        pushToast('success', t('Environment "{{name}}" created.', { name: created.name }));
      }
      await load();
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Save failed.'));
    } finally {
      setSaving(false);
    }
  }

  async function load() {
    try {
      setLoading(true);
      const [environmentResult, vesselResult] = await Promise.all([
        listAll(listEnvironments),
        listAll(listVessels),
      ]);
      setEnvironments(environmentResult.objects || []);
      setVessels(vesselResult.objects || []);
      setError('');
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Failed to load environments.'));
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    load();
  }, []);

  const { seconds: refreshSeconds, setSeconds: setRefreshSeconds } = useAutoRefresh('environments', load);

  const vesselMap = useMemo(() => new Map(vessels.map((vessel) => [vessel.id, vessel.name])), [vessels]);

  const filtered = useMemo(() => environments.filter((environment) => {
    const normalizedSearch = search.trim().toLowerCase();
    const matchesSearch = normalizedSearch.length === 0
      || environment.name.toLowerCase().includes(normalizedSearch)
      || (environment.description || '').toLowerCase().includes(normalizedSearch)
      || (environment.baseUrl || '').toLowerCase().includes(normalizedSearch)
      || (environment.configurationSource || '').toLowerCase().includes(normalizedSearch)
      || environment.id.toLowerCase().includes(normalizedSearch);

    const matchesKind = kindFilter === 'all' || environment.kind === kindFilter;
    const matchesVessel = vesselFilter === 'all' || environment.vesselId === vesselFilter;
    const matchesActive = activeFilter === 'all'
      || (activeFilter === 'active' && environment.active)
      || (activeFilter === 'inactive' && !environment.active);

    const matchesColFilters = (!colFilters.name || environment.name.toLowerCase().includes(colFilters.name.toLowerCase()))
      && (!colFilters.baseUrl || (environment.baseUrl ?? '').toLowerCase().includes(colFilters.baseUrl.toLowerCase()))
      && (!colFilters.health || (environment.healthEndpoint ?? '').toLowerCase().includes(colFilters.health.toLowerCase()));

    return matchesSearch && matchesKind && matchesVessel && matchesActive && matchesColFilters;
  }), [activeFilter, colFilters, environments, kindFilter, search, vesselFilter]);

  const activeCount = environments.filter((environment) => environment.active).length;
  const defaultCount = environments.filter((environment) => environment.isDefault).length;
  const approvalCount = environments.filter((environment) => environment.requiresApproval).length;

  function handleDelete(environment: DeploymentEnvironment) {
    setConfirm({
      open: true,
      title: t('Delete Environment'),
      message: t('Delete "{{name}}"? This removes only the environment record.', { name: environment.name }),
      onConfirm: async () => {
        setConfirm((current) => ({ ...current, open: false }));
        if (!(await deletion.run({ kind: 'environment', action: () => deleteEnvironment(environment.id), errorMessage: (err) => (err instanceof Error ? err.message : t('Delete failed.')) }))) return;
        pushToast('warning', t('Environment "{{name}}" deleted.', { name: environment.name }));
        await load();
      },
    });
  }

  async function handleDuplicate(environment: DeploymentEnvironment) {
    try {
      const created = await createEnvironment(buildEnvironmentDuplicatePayload(environment));
      pushToast('success', t('Environment "{{name}}" duplicated.', { name: created.name }));
      navigate(`/environments/${created.id}`);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Duplicate failed.'));
    }
  }

  const columns: DataTableColumn<DeploymentEnvironment>[] = [
    {
      key: 'id', label: t('ID'), required: true, headerTitle: t('Environment ID (env_ prefix), used by the REST API, MCP tools, and links.'), cellClassName: 'mono text-dim table-id-cell',
      render: (environment) => (
        <span className="id-display">
          <span className="id-value" title={environment.id}>{environment.id}</span>
          <CopyButton text={environment.id} onClick={e => e.stopPropagation()} />
        </span>
      ),
    },
    {
      key: 'name', label: t('Environment'), required: true, headerTitle: t('Environment name. Hover a row for its description.'),
      filter: <input type="text" className="col-filter" aria-label={t('Filter by name')} title={t('Show only environments whose name contains this text.')} value={colFilters.name} onChange={e => setColFilters(f => ({ ...f, name: e.target.value }))} placeholder={t('Filter...')} />,
      // One line: default/active state and description have their own columns; the description is also in the tooltip.
      cellTitle: (environment) => [environment.name, environment.description].filter(Boolean).join('\n'),
      render: (environment) => <strong className="cell-one-line">{environment.name}</strong>,
    },
    { key: 'kind', label: t('Kind'), headerTitle: t('Classification: Development, Test, Staging, Production, or Custom.'), cellClassName: 'text-dim', render: (environment) => environment.kind },
    { key: 'vessel', label: t('Vessel'), headerTitle: t('Repository this environment belongs to.'), cellClassName: 'text-dim', render: (environment) => (environment.vesselId ? (vesselMap.get(environment.vesselId) || environment.vesselId) : '-') },
    {
      key: 'state', label: t('State'), headerTitle: t('Active or inactive, and whether it is the vessel\'s default target when a deployment names no environment.'), cellClassName: 'text-dim cell-nowrap',
      render: (environment) => `${environment.active ? t('Active') : t('Inactive')}${environment.isDefault ? ` \u2022 ${t('Default target')}` : ''}`,
    },
    {
      key: 'baseUrl', label: t('Base URL'), headerTitle: t('Root URL of the deployed service; health checks and verification paths resolve against it.'), cellClassName: 'text-dim',
      cellTitle: (environment) => environment.baseUrl || undefined,
      clearFilter: () => setColFilters(f => ({ ...f, baseUrl: '' })),
      filter: <input type="text" className="col-filter" aria-label={t('Filter by base URL')} title={t('Show only environments whose base URL contains this text.')} value={colFilters.baseUrl} onChange={e => setColFilters(f => ({ ...f, baseUrl: e.target.value }))} placeholder={t('Filter...')} />,
      render: (environment) => <span className="cell-clip"><span>{environment.baseUrl || '-'}</span></span>,
    },
    {
      key: 'health', label: t('Health'), headerTitle: t('Health endpoint requested after each deploy, when a Base URL is also set.'), cellClassName: 'text-dim',
      cellTitle: (environment) => environment.healthEndpoint || undefined,
      clearFilter: () => setColFilters(f => ({ ...f, health: '' })),
      filter: <input type="text" className="col-filter" aria-label={t('Filter by health endpoint')} title={t('Show only environments whose health endpoint contains this text.')} value={colFilters.health} onChange={e => setColFilters(f => ({ ...f, health: e.target.value }))} placeholder={t('Filter...')} />,
      render: (environment) => <span className="cell-clip"><span>{environment.healthEndpoint || '-'}</span></span>,
    },
    {
      key: 'policy', label: t('Policy'), headerTitle: t('Approval required means deployments wait for someone to approve them; self-service deployments run right away.'), cellClassName: 'text-dim cell-nowrap',
      render: (environment) => (environment.requiresApproval ? t('Approval required') : t('Self-service')),
    },
    {
      key: 'description', label: t('Description'), headerTitle: t('What the environment is for.'), defaultHidden: true, cellClassName: 'text-dim',
      render: (environment) => <span className="cell-one-line" title={environment.description || undefined}>{environment.description || '-'}</span>,
    },
    {
      key: 'lastUpdated', label: t('Last Updated'), headerTitle: t('When the environment record last changed. Hover a cell for the exact time.'), cellClassName: 'text-dim cell-nowrap',
      cellTitle: (environment) => formatDateTime(environment.lastUpdateUtc),
      render: (environment) => formatRelativeTime(environment.lastUpdateUtc),
    },
    {
      key: 'actions', label: t('Actions'), fixed: true, interactive: true, className: 'text-right',
      render: (environment) => (
        <ActionMenu
          id={`environment-${environment.id}`}
          items={[
            { label: 'Open', title: 'Open the environment detail page with its verification checks, monitoring settings, deployments, and incidents.', onClick: () => navigate(`/environments/${environment.id}`) },
            ...(canManage ? [{ label: 'Edit', title: 'Change this environment\'s URLs, notes, approval rule, and default or active state.', onClick: () => openEdit(environment) }] : []),
            ...(canManage ? [{ label: 'Duplicate', title: 'Create a copy with the same settings and verification checks, not set as default, and open it.', onClick: () => void handleDuplicate(environment) }] : []),
            { label: 'View JSON', title: 'Show the raw environment record as returned by the API.', onClick: () => setJsonData({ open: true, title: environment.name, data: environment }) },
            ...(canManage ? [{ label: 'Delete', title: 'Delete this environment record. Existing deployments keep their environment name. Cannot be undone.', danger: true as const, onClick: () => handleDelete(environment) }] : []),
          ]}
        />
      ),
    },
  ];

  return (
    <div>
      <PageHeader
        title={t('Environments')}
        subtitle={t('Named deployment targets for vessels, with URLs, configuration sources, approval requirements, and operator notes.')}
        actions={(
          <>
            {canManage && (
              <button className="btn btn-primary" onClick={openCreate} title={t('Create a named deployment target for a vessel, such as staging or production.')}>
                + {t('Environment')}
              </button>
            )}
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
      {deletion.dialog}

      {showCreate && (
        <div className="modal-overlay" onClick={() => setShowCreate(false)}>
          <form className="modal modal-large" onClick={(event) => event.stopPropagation()} onSubmit={handleCreate}>
            <h3>{editing ? t('Edit Environment') : t('Create Environment')}</h3>
            <label title={tips.vessel}>{t('Vessel')}
              <select title={tips.vessel} value={createForm.vesselId} onChange={(event) => setCreateForm({ ...createForm, vesselId: event.target.value })}>
                <option value="">{t('Select a vessel')}</option>
                {vessels.map((vessel) => (
                  <option key={vessel.id} value={vessel.id}>{vessel.name}</option>
                ))}
              </select>
            </label>
            <label title={tips.name}>{t('Name')}
              <input type="text" title={tips.name} value={createForm.name} onChange={(event) => setCreateForm({ ...createForm, name: event.target.value })} required />
            </label>
            <label title={tips.kind}>{t('Kind')}
              <select title={tips.kind} value={createForm.kind} onChange={(event) => setCreateForm({ ...createForm, kind: event.target.value as EnvironmentKind })}>
                {ENVIRONMENT_KINDS.map((value) => (
                  <option key={value} value={value}>{value}</option>
                ))}
              </select>
            </label>
            <label title={tips.configurationSource}>{t('Configuration Source')}
              <input type="text" title={tips.configurationSource} value={createForm.configurationSource} onChange={(event) => setCreateForm({ ...createForm, configurationSource: event.target.value })} placeholder={t('e.g. Helm values, appsettings.Production.json, Azure slot config')} />
            </label>
            <label title={tips.baseUrl}>{t('Base URL')}
              <input type="text" title={tips.baseUrl} value={createForm.baseUrl} onChange={(event) => setCreateForm({ ...createForm, baseUrl: event.target.value })} placeholder="https://service.example.com" />
            </label>
            <label title={tips.healthEndpoint}>{t('Health Endpoint')}
              <input type="text" title={tips.healthEndpoint} value={createForm.healthEndpoint} onChange={(event) => setCreateForm({ ...createForm, healthEndpoint: event.target.value })} placeholder="/health or https://service.example.com/health" />
            </label>
            <label title={tips.description}>{t('Description')}
              <textarea title={tips.description} rows={3} value={createForm.description} onChange={(event) => setCreateForm({ ...createForm, description: event.target.value })} />
            </label>
            <label title={tips.accessNotes}>{t('Access Notes')}
              <textarea title={tips.accessNotes} rows={3} value={createForm.accessNotes} onChange={(event) => setCreateForm({ ...createForm, accessNotes: event.target.value })} placeholder={t('How do operators reach or authenticate to this environment?')} />
            </label>
            <label title={tips.deploymentRules}>{t('Deployment Rules')}
              <textarea title={tips.deploymentRules} rows={3} value={createForm.deploymentRules} onChange={(event) => setCreateForm({ ...createForm, deploymentRules: event.target.value })} placeholder={t('Document freeze windows, approval policy, maintenance constraints, or rollout notes.')} />
            </label>
            <div style={{ display: 'flex', gap: '1rem', flexWrap: 'wrap' }}>
              <label title={tips.requiresApproval} style={{ display: 'inline-flex', alignItems: 'center', gap: '0.45rem' }}>
                <input type="checkbox" title={tips.requiresApproval} checked={createForm.requiresApproval} onChange={(event) => setCreateForm({ ...createForm, requiresApproval: event.target.checked })} />
                <span>{t('Requires approval')}</span>
              </label>
              <label title={tips.isDefault} style={{ display: 'inline-flex', alignItems: 'center', gap: '0.45rem' }}>
                <input type="checkbox" title={tips.isDefault} checked={createForm.isDefault} onChange={(event) => setCreateForm({ ...createForm, isDefault: event.target.checked })} />
                <span>{t('Default environment for vessel')}</span>
              </label>
              <label title={tips.active} style={{ display: 'inline-flex', alignItems: 'center', gap: '0.45rem' }}>
                <input type="checkbox" title={tips.active} checked={createForm.active} onChange={(event) => setCreateForm({ ...createForm, active: event.target.checked })} />
                <span>{t('Active')}</span>
              </label>
            </div>
            <div className="modal-actions">
              <button type="submit" className="btn btn-primary" disabled={saving} title={editing ? t('Save changes to this environment. Existing deployments are not re-run.') : t('Create this environment for the selected vessel.')}>{saving ? t('Saving...') : editing ? t('Save Changes') : t('Create Environment')}</button>
              <button type="button" className="btn" onClick={() => setShowCreate(false)} disabled={saving} title={t('Close without saving.')}>{t('Cancel')}</button>
            </div>
          </form>
        </div>
      )}

      <div className="playbook-overview-grid">
        <div className="card playbook-overview-card" title={t('All environment records you can see, before filters.')}>
          <span>{t('Total Environments')}</span>
          <strong>{environments.length}</strong>
        </div>
        <div className="card playbook-overview-card" title={t('Environments that are active and can be picked as a default target or monitored.')}>
          <span>{t('Active')}</span>
          <strong>{activeCount}</strong>
        </div>
        <div className="card playbook-overview-card" title={t('Environments marked as their vessel\'s default target. At most one per vessel.')}>
          <span>{t('Default Targets')}</span>
          <strong>{defaultCount}</strong>
        </div>
        <div className="card playbook-overview-card" title={t('Environments where deployments wait for someone to approve them before running.')}>
          <span>{t('Require Approval')}</span>
          <strong>{approvalCount}</strong>
        </div>
      </div>

      <div className="card" style={{ padding: '1rem', marginBottom: '1rem' }}>
        <div className="playbook-filter-row">
          <input
            type="text"
            value={search}
            onChange={(event) => setSearch(event.target.value)}
            placeholder={t('Search by name, description, base URL, configuration source, or ID...')}
            title={t('Filters the list to environments whose name, description, base URL, configuration source, or ID contains this text.')}
          />
          <select aria-label={t('All kinds')} title={t('Show only environments of this kind.')} value={kindFilter} onChange={(event) => setKindFilter(event.target.value as typeof kindFilter)}>
            <option value="all">{t('All kinds')}</option>
            {ENVIRONMENT_KINDS.map((kind) => (
              <option key={kind} value={kind}>{kind}</option>
            ))}
          </select>
          <select aria-label={t('All vessels')} title={t('Show only environments for this vessel.')} value={vesselFilter} onChange={(event) => setVesselFilter(event.target.value)}>
            <option value="all">{t('All vessels')}</option>
            {vessels.map((vessel) => (
              <option key={vessel.id} value={vessel.id}>{vessel.name}</option>
            ))}
          </select>
          <select aria-label={t('All states')} title={t('Show only active or only inactive environments.')} value={activeFilter} onChange={(event) => setActiveFilter(event.target.value as typeof activeFilter)}>
            <option value="all">{t('All states')}</option>
            <option value="active">{t('Active')}</option>
            <option value="inactive">{t('Inactive')}</option>
          </select>
        </div>
      </div>

      <DataTable
        tableKey="environments"
        columns={columns}
        rows={filtered}
        rowKey={(environment) => environment.id}
        onRowClick={(environment) => (canManage ? openEdit(environment) : navigate(`/environments/${environment.id}`))}
        autoRefresh={{ seconds: refreshSeconds, onChange: setRefreshSeconds }}
        onRefresh={load}
        refreshTitle={t('Refresh environments')}
        placeholder={loading && environments.length === 0 ? <p className="text-dim">{t('Loading...')}</p> : filtered.length === 0 ? (
          <div className="playbook-empty-state">
            <strong>{t('No environments match the current filters.')}</strong>
            <span>{canManage ? t('Create an environment to capture deployment metadata, URLs, and approval rules for a vessel.') : t('Ask a tenant administrator to create and manage environment records.')}</span>
          </div>
        ) : undefined}
      />
    </div>
  );
}
