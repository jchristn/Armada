import { useEffect, useMemo, useState } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import PageHeader from '../components/shared/PageHeader';
import {
  createRunbook,
  deleteRunbook,
  listEnvironments,
  listRunbookExecutions,
  listRunbooks,
  listWorkflowProfiles,
} from '../api/client';
import { listAll } from '../api/paging';
import type {
  CheckRunType,
  DeploymentEnvironment,
  Runbook,
  RunbookExecution,
  RunbookExecutionStartRequest,
  RunbookUpsertRequest,
  WorkflowProfile,
  ScopeEnum,
} from '../types/models';
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
import RecordDetailModal from '../components/shared/RecordDetailModal';
import CopyButton from '../components/shared/CopyButton';
import DataTable, { type DataTableColumn } from '../components/shared/DataTable';
import StatusBadge from '../components/shared/StatusBadge';
import { useAutoRefresh } from '../lib/useAutoRefresh';
import { buildRunbookDuplicatePayload } from '../lib/duplicates';
import { RUNBOOK_CHECK_TYPES } from '../lib/deliveryForms';
import { useDeleteProgress } from '../lib/useDeleteProgress';

interface RunbookPageState {
  prefillExecution?: Partial<RunbookExecutionStartRequest>;
}

export default function Runbooks() {
  const navigate = useNavigate();
  const location = useLocation();
  const { isAdmin, isTenantAdmin, user } = useAuth();
  const viewer: ScopeViewer = { isAdmin, isTenantAdmin, tenantId: user?.user?.tenantId, userId: user?.user?.id };
  const { t, formatDateTime, formatRelativeTime } = useLocale();
  const deletion = useDeleteProgress();
  const { pushToast } = useNotifications();

  const [runbooks, setRunbooks] = useState<Runbook[]>([]);
  const [executions, setExecutions] = useState<RunbookExecution[]>([]);
  const [profiles, setProfiles] = useState<WorkflowProfile[]>([]);
  const [environments, setEnvironments] = useState<DeploymentEnvironment[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [search, setSearch] = useState('');
  const [activeFilter, setActiveFilter] = useState<'all' | 'active' | 'inactive'>('all');
  const [colFilters, setColFilters] = useState({ title: '' });
  const [jsonData, setJsonData] = useState<{ open: boolean; title: string; data: unknown }>({ open: false, title: '', data: null });
  const [viewRecord, setViewRecord] = useState<Record<string, unknown> | null>(null);
  const [confirm, setConfirm] = useState<{ open: boolean; title: string; message: string; onConfirm: () => void }>({
    open: false,
    title: '',
    message: '',
    onConfirm: () => {},
  });

  // Create modal
  const [showCreate, setShowCreate] = useState(false);
  const [saving, setSaving] = useState(false);
  const [createForm, setCreateForm] = useState<{
    fileName: string;
    title: string;
    description: string;
    workflowProfileId: string;
    environmentId: string;
    defaultCheckType: CheckRunType | '';
    active: boolean;
    scope: ScopeEnum;
  }>({
    fileName: 'RUNBOOK.md',
    title: 'Runbook',
    description: '',
    workflowProfileId: '',
    environmentId: '',
    defaultCheckType: '',
    active: true,
    scope: resolveCreateScope(viewer),
  });

  const canManage = isAdmin || isTenantAdmin;

  const tips = {
    fileName: t('Name of the backing playbook file, ending in .md, such as deploy-checklist.md. Must be unique in your tenant.'),
    title: t('Name shown in lists and on executions, such as Production deploy checklist.'),
    description: t('Short description of when to use this runbook. Shown in the list tooltip and searchable.'),
    workflowProfile: t('Profile whose commands this runbook\'s checks run by default. Blank leaves executions to pick one.'),
    environment: t('Environment new executions of this runbook target by default. Blank leaves it to each execution.'),
    defaultCheckType: t('Check type suggested when running a check from an execution of this runbook, such as DeploymentVerification.'),
    active: t('Uncheck to retire the runbook without deleting it. It stays listed, but its backing playbook can no longer be attached to missions.'),
  };
  const carryState = (location.state as RunbookPageState | null) || null;

  function openCreate() {
    setCreateForm({
      fileName: 'RUNBOOK.md',
      title: 'Runbook',
      description: '',
      workflowProfileId: '',
      environmentId: '',
      defaultCheckType: '',
      active: true,
      scope: resolveCreateScope(viewer),
    });
    setShowCreate(true);
  }

  async function handleCreate(event: React.FormEvent) {
    event.preventDefault();
    if (saving) return;
    setSaving(true);
    try {
      const payload: RunbookUpsertRequest = {
        fileName: createForm.fileName.trim() || null,
        title: createForm.title.trim() || null,
        description: createForm.description.trim() || null,
        workflowProfileId: createForm.workflowProfileId || null,
        environmentId: createForm.environmentId || null,
        environmentName: createForm.environmentId ? (environmentMap.get(createForm.environmentId) || null) : null,
        defaultCheckType: createForm.defaultCheckType || null,
        parameters: [],
        steps: [],
        overviewMarkdown: '',
        active: createForm.active,
        scope: createForm.scope,
      };
      const created = await createRunbook(payload);
      setShowCreate(false);
      pushToast('success', t('Runbook "{{title}}" created.', { title: created.title }));
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
      const [runbookResult, executionResult, profileResult, environmentResult] = await Promise.all([
        listAll(listRunbooks),
        listAll(listRunbookExecutions),
        listAll(listWorkflowProfiles),
        listAll(listEnvironments),
      ]);
      setRunbooks(runbookResult.objects || []);
      setExecutions(executionResult.objects || []);
      setProfiles(profileResult.objects || []);
      setEnvironments(environmentResult.objects || []);
      setError('');
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Failed to load runbooks.'));
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void load();
  }, []);

  const { seconds: refreshSeconds, setSeconds: setRefreshSeconds } = useAutoRefresh('runbooks', load);

  const profileMap = useMemo(() => new Map(profiles.map((profile) => [profile.id, profile.name])), [profiles]);
  const environmentMap = useMemo(() => new Map(environments.map((environment) => [environment.id, environment.name])), [environments]);
  const executionCounts = useMemo(() => {
    const counts = new Map<string, { total: number; running: number }>();
    for (const execution of executions) {
      const current = counts.get(execution.runbookId) || { total: 0, running: 0 };
      current.total += 1;
      if (execution.status === 'Running') current.running += 1;
      counts.set(execution.runbookId, current);
    }
    return counts;
  }, [executions]);

  const filtered = useMemo(() => runbooks.filter((runbook) => {
    const normalizedSearch = search.trim().toLowerCase();
    const matchesSearch = normalizedSearch.length === 0
      || runbook.title.toLowerCase().includes(normalizedSearch)
      || runbook.fileName.toLowerCase().includes(normalizedSearch)
      || (runbook.description || '').toLowerCase().includes(normalizedSearch)
      || (runbook.environmentName || '').toLowerCase().includes(normalizedSearch)
      || runbook.id.toLowerCase().includes(normalizedSearch);
    const matchesActive = activeFilter === 'all'
      || (activeFilter === 'active' && runbook.active)
      || (activeFilter === 'inactive' && !runbook.active);
    const matchesColFilters = (!colFilters.title || runbook.title.toLowerCase().includes(colFilters.title.toLowerCase()));
    return matchesSearch && matchesActive && matchesColFilters;
  }), [activeFilter, colFilters, runbooks, search]);

  function handleDelete(runbook: Runbook) {
    setConfirm({
      open: true,
      title: t('Delete Runbook'),
      message: t('Delete "{{title}}"? This removes the runbook definition but does not touch deployments, incidents, or completed check runs.', { title: runbook.title }),
      onConfirm: async () => {
        setConfirm((current) => ({ ...current, open: false }));
        if (!(await deletion.run({ kind: 'runbook', action: () => deleteRunbook(runbook.id), errorMessage: (err) => err instanceof Error ? err.message : t('Delete failed.') }))) return;
        pushToast('warning', t('Runbook "{{title}}" deleted.', { title: runbook.title }));
        await load();
      },
    });
  }

  async function handleDuplicate(runbook: Runbook) {
    try {
      const created = await createRunbook(buildRunbookDuplicatePayload(runbook));
      pushToast('success', t('Runbook "{{title}}" duplicated.', { title: created.title }));
      navigate(`/runbooks/${created.id}`, { state: carryState });
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Duplicate failed.'));
    }
  }

  const emptyState = (
    <div className="playbook-empty-state">
      <strong>{t('No runbooks match the current filters.')}</strong>
      <span>{canManage ? t('Create a runbook to guide release, deploy, rollback, migration, or incident work step by step.') : t('Ask a tenant administrator to create and manage runbooks.')}</span>
    </div>
  );

  // The runbook and binding cells used to stack four and three lines; each value is its own one-line column now,
  // with the less essential ones hidden by default (the column chooser turns them on).
  const columns: DataTableColumn<Runbook>[] = [
    {
      key: 'id', label: t('ID'), required: true, headerTitle: t('Runbook ID. It is also the ID of the backing playbook.'), cellClassName: 'mono text-dim table-id-cell',
      render: (runbook) => (
        <span className="id-display">
          <span className="id-value" title={runbook.id}>{runbook.id}</span>
          <CopyButton text={runbook.id} onClick={e => e.stopPropagation()} />
        </span>
      ),
    },
    {
      key: 'title', label: t('Runbook'), required: true, headerTitle: t('Runbook title. Hover a row for its description.'),
      clearFilter: () => setColFilters(f => ({ ...f, title: '' })),
      filter: <input type="text" className="col-filter" aria-label={t('Runbook')} title={t('Show only runbooks whose title contains this text.')} value={colFilters.title} onChange={e => setColFilters(f => ({ ...f, title: e.target.value }))} placeholder={t('Filter...')} />,
      cellTitle: (runbook) => runbook.description || undefined,
      render: (runbook) => <strong>{runbook.title}</strong>,
    },
    {
      key: 'fileName', label: t('File Name'), headerTitle: t('File name of the backing playbook that holds the runbook\'s markdown.'), defaultHidden: true, cellClassName: 'mono text-dim',
      render: (runbook) => <span className="cell-one-line" title={runbook.fileName}>{runbook.fileName}</span>,
    },
    {
      key: 'description', label: t('Description'), headerTitle: t('When to use this runbook.'), defaultHidden: true, cellClassName: 'text-dim truncate-cell',
      render: (runbook) => runbook.description ? <span className="truncate-text" title={runbook.description}>{runbook.description}</span> : '-',
    },
    { key: 'status', label: t('Status'), headerTitle: t('Whether the runbook is active or retired.'), cellClassName: 'cell-nowrap', render: (runbook) => <StatusBadge status={runbook.active ? 'Active' : 'Inactive'} /> },
    {
      key: 'workflowProfile', label: t('Workflow Profile'), headerTitle: t('Profile bound to the runbook, used for checks run from its executions.'), cellClassName: 'text-dim',
      render: (runbook) => {
        const text = runbook.workflowProfileId ? (profileMap.get(runbook.workflowProfileId) || runbook.workflowProfileId) : t('No workflow profile');
        return <span className="cell-one-line" title={text}>{text}</span>;
      },
    },
    {
      key: 'environment', label: t('Environment'), headerTitle: t('Environment new executions target by default.'), cellClassName: 'text-dim',
      render: (runbook) => {
        const text = runbook.environmentId ? (environmentMap.get(runbook.environmentId) || runbook.environmentName || runbook.environmentId) : (runbook.environmentName || t('No environment'));
        return <span className="cell-one-line" title={text}>{text}</span>;
      },
    },
    {
      key: 'defaultCheck', label: t('Default Check Type'), headerTitle: t('Check type suggested when running a check from an execution.'), defaultHidden: true, cellClassName: 'text-dim cell-nowrap',
      render: (runbook) => runbook.defaultCheckType || t('No default check'),
    },
    {
      key: 'steps', label: t('Steps'), headerTitle: t('Number of checklist steps and fill-in parameters the runbook defines.'), cellClassName: 'text-dim cell-nowrap',
      render: (runbook) => <>{runbook.steps.length} {t('steps')} {'\u2022'} {runbook.parameters.length} {t('parameters')}</>,
    },
    {
      key: 'executions', label: t('Executions'), headerTitle: t('How many times the runbook has been started, and how many of those are still running.'), cellClassName: 'text-dim cell-nowrap',
      render: (runbook) => {
        const counts = executionCounts.get(runbook.id) || { total: 0, running: 0 };
        return <>{counts.total} {t('total')} {'\u2022'} {counts.running} {t('running')}</>;
      },
    },
    { key: 'visibility', label: t('Visibility'), headerTitle: t('Tenant-wide runbooks are visible to everyone in the tenant and editable by admins; personal ones belong to their owner.'), cellClassName: 'cell-nowrap', render: (runbook) => <ScopeBadge scope={runbook.scope} /> },
    {
      key: 'lastUpdated', label: t('Last Updated'), headerTitle: t('When the runbook was last changed. Hover a cell for the exact time.'), cellClassName: 'text-dim cell-nowrap',
      cellTitle: (runbook) => formatDateTime(runbook.lastUpdateUtc),
      render: (runbook) => formatRelativeTime(runbook.lastUpdateUtc),
    },
    {
      key: 'actions', label: t('Actions'), fixed: true, interactive: true, className: 'text-right',
      render: (runbook) => {
        const counts = executionCounts.get(runbook.id) || { total: 0, running: 0 };
        return (
          <ActionMenu
            id={`runbook-${runbook.id}`}
            items={[
              { label: 'Open', title: 'Open the runbook to edit its steps or start and track an execution.', onClick: () => navigate(`/runbooks/${runbook.id}`, { state: carryState }) },
              { label: 'Duplicate', title: 'Create a copy of this runbook under a new file name and open it.', onClick: () => void handleDuplicate(runbook) },
              { label: 'View JSON', title: 'Show the raw runbook record as returned by the API.', onClick: () => setJsonData({ open: true, title: runbook.title, data: runbook }) },
              ...(counts.running > 0 ? [{ label: `Running: ${counts.running}`, title: 'Open the runbook to see its executions that are still running.', onClick: () => navigate(`/runbooks/${runbook.id}`, { state: carryState }) }] : []),
              ...(canEditScoped(viewer, runbook) ? [{ label: 'Delete', title: 'Delete this runbook and its backing playbook. Past executions, deployments, and incidents are kept. Cannot be undone.', danger: true as const, onClick: () => handleDelete(runbook) }] : []),
            ]}
          />
        );
      },
    },
  ];

  return (
    <div>
      <PageHeader
        title={t('Runbooks')}
        subtitle={t('Playbook-backed operational runbooks with bound workflow profiles, environments, parameters, step tracking, and execution history.')}
        actions={(
          <>
            <button className="btn btn-primary" onClick={openCreate} title={t('Create a step-by-step runbook for release, deploy, rollback, or incident work. It is stored as a playbook.')}>
              + {t('Runbook')}
            </button>
          </>
        )}
      />

      {carryState?.prefillExecution && (
        <div className="alert" style={{ marginBottom: '1rem' }}>
          {t('An incident or deployment handed off a prefilled runbook execution context. Open a runbook to start the execution with those defaults.')}
        </div>
      )}

      <ErrorModal error={error} onClose={() => setError('')} />
      <JsonViewer open={jsonData.open} title={jsonData.title} data={jsonData.data} onClose={() => setJsonData({ open: false, title: '', data: null })} />
      <RecordDetailModal
        open={!!viewRecord}
        title={viewRecord ? String(viewRecord.title || viewRecord.id || '') : ''}
        subtitle={viewRecord ? String(viewRecord.fileName || '') : undefined}
        record={viewRecord}
        onClose={() => setViewRecord(null)}
        onEdit={() => {
          const id = viewRecord?.id;
          setViewRecord(null);
          if (id) navigate(`/runbooks/${String(id)}`, { state: carryState });
        }}
        editLabel={t('Open Details')}
      />
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
            <h3>{t('Create Runbook')}</h3>
            <label title={tips.fileName}>{t('File Name')}
              <input title={tips.fileName} type="text" value={createForm.fileName} onChange={(event) => setCreateForm({ ...createForm, fileName: event.target.value })} required />
            </label>
            <label title={tips.title}>{t('Title')}
              <input title={tips.title} type="text" value={createForm.title} onChange={(event) => setCreateForm({ ...createForm, title: event.target.value })} required />
            </label>
            <label title={tips.description}>{t('Description')}
              <textarea title={tips.description} rows={2} value={createForm.description} onChange={(event) => setCreateForm({ ...createForm, description: event.target.value })} />
            </label>
            <label title={tips.workflowProfile}>{t('Workflow Profile')}
              <select title={tips.workflowProfile} value={createForm.workflowProfileId} onChange={(event) => setCreateForm({ ...createForm, workflowProfileId: event.target.value })}>
                <option value="">{t('No workflow profile')}</option>
                {profiles.map((profile) => (
                  <option key={profile.id} value={profile.id}>{profile.name}</option>
                ))}
              </select>
            </label>
            <label title={tips.environment}>{t('Environment')}
              <select title={tips.environment} value={createForm.environmentId} onChange={(event) => setCreateForm({ ...createForm, environmentId: event.target.value })}>
                <option value="">{t('No environment')}</option>
                {environments.map((environment) => (
                  <option key={environment.id} value={environment.id}>{environment.name}</option>
                ))}
              </select>
            </label>
            <label title={tips.defaultCheckType}>{t('Default Check Type')}
              <select title={tips.defaultCheckType} value={createForm.defaultCheckType} onChange={(event) => setCreateForm({ ...createForm, defaultCheckType: event.target.value as CheckRunType | '' })}>
                <option value="">{t('No default check')}</option>
                {RUNBOOK_CHECK_TYPES.map((checkType) => (
                  <option key={checkType} value={checkType}>{checkType}</option>
                ))}
              </select>
            </label>
            <ScopeSelect viewer={viewer} value={createForm.scope} onChange={(scope) => setCreateForm({ ...createForm, scope })} />
            <label className="checkbox-row" title={tips.active}>
              <input type="checkbox" title={tips.active} checked={createForm.active} onChange={(event) => setCreateForm({ ...createForm, active: event.target.checked })} />
              <span>{t('Active')}</span>
            </label>
            <div className="modal-actions">
              <button type="submit" className="btn btn-primary" disabled={saving} title={t('Create the runbook. Add its steps and parameters on the next page.')}>{saving ? t('Saving...') : t('Create Runbook')}</button>
              <button type="button" className="btn" onClick={() => setShowCreate(false)} disabled={saving} title={t('Close without saving.')}>{t('Cancel')}</button>
            </div>
          </form>
        </div>
      )}

      <div className="playbook-overview-grid">
        <div className="card playbook-overview-card" title={t('All runbooks you can see, before filters.')}>
          <span>{t('Total Runbooks')}</span>
          <strong>{runbooks.length}</strong>
        </div>
        <div className="card playbook-overview-card" title={t('Runbooks currently in use, not retired.')}>
          <span>{t('Active')}</span>
          <strong>{runbooks.filter((runbook) => runbook.active).length}</strong>
        </div>
        <div className="card playbook-overview-card" title={t('Times any runbook has been started.')}>
          <span>{t('Executions')}</span>
          <strong>{executions.length}</strong>
        </div>
        <div className="card playbook-overview-card" title={t('Runbook executions still in progress.')}>
          <span>{t('Running')}</span>
          <strong>{executions.filter((execution) => execution.status === 'Running').length}</strong>
        </div>
      </div>

      <div className="card" style={{ padding: '1rem', marginBottom: '1rem' }}>
        <div className="playbook-filter-row">
          <input
            type="text"
            value={search}
            onChange={(event) => setSearch(event.target.value)}
            placeholder={t('Search by title, file name, description, environment, or ID...')}
            title={t('Filters the list to runbooks whose title, file name, description, environment, or ID contains this text.')}
          />
          <select aria-label={t('All states')} title={t('Show only active or only retired runbooks.')} value={activeFilter} onChange={(event) => setActiveFilter(event.target.value as typeof activeFilter)}>
            <option value="all">{t('All states')}</option>
            <option value="active">{t('Active')}</option>
            <option value="inactive">{t('Inactive')}</option>
          </select>
        </div>
      </div>

      <DataTable
        tableKey="runbooks"
        columns={columns}
        rows={filtered}
        rowKey={(runbook) => runbook.id}
        onRowClick={(runbook) => setViewRecord(runbook as unknown as Record<string, unknown>)}
        autoRefresh={{ seconds: refreshSeconds, onChange: setRefreshSeconds }}
        onRefresh={load}
        refreshTitle={t('Refresh runbooks')}
        emptyMessage={emptyState}
        placeholder={runbooks.length > 0 ? undefined : loading ? <p className="text-dim">{t('Loading...')}</p> : emptyState}
      />
    </div>
  );
}
