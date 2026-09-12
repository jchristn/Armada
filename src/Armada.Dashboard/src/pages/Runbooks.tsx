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
import RefreshButton from '../components/shared/RefreshButton';
import StatusBadge from '../components/shared/StatusBadge';
import AutoRefreshSelect from '../components/shared/AutoRefreshSelect';
import { useAutoRefresh } from '../lib/useAutoRefresh';
import { buildRunbookDuplicatePayload } from '../lib/duplicates';

const RUNBOOK_CHECK_TYPES: CheckRunType[] = [
  'Build',
  'UnitTest',
  'IntegrationTest',
  'E2ETest',
  'Migration',
  'SecurityScan',
  'Performance',
  'Deploy',
  'Rollback',
  'SmokeTest',
  'HealthCheck',
  'DeploymentVerification',
  'RollbackVerification',
  'Custom',
];

interface RunbookPageState {
  prefillExecution?: Partial<RunbookExecutionStartRequest>;
}

export default function Runbooks() {
  const navigate = useNavigate();
  const location = useLocation();
  const { isAdmin, isTenantAdmin, user } = useAuth();
  const viewer: ScopeViewer = { isAdmin, isTenantAdmin, tenantId: user?.user?.tenantId, userId: user?.user?.id };
  const { t, formatDateTime, formatRelativeTime } = useLocale();
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
        listRunbooks({ pageSize: 9999 }),
        listRunbookExecutions({ pageSize: 9999 }),
        listWorkflowProfiles({ pageSize: 9999 }),
        listEnvironments({ pageSize: 9999 }),
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
        try {
          await deleteRunbook(runbook.id);
          pushToast('warning', t('Runbook "{{title}}" deleted.', { title: runbook.title }));
          await load();
        } catch (err: unknown) {
          setError(err instanceof Error ? err.message : t('Delete failed.'));
        }
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

  return (
    <div>
      <PageHeader
        title={t('Runbooks')}
        subtitle={t('Playbook-backed operational runbooks with bound workflow profiles, environments, parameters, step tracking, and execution history.')}
        actions={(
          <>
            <AutoRefreshSelect seconds={refreshSeconds} onChange={setRefreshSeconds} />
            <RefreshButton onRefresh={load} title={t('Refresh runbooks')} />
            <button className="btn btn-primary" onClick={openCreate}>
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
            <label>{t('File Name')}
              <input type="text" value={createForm.fileName} onChange={(event) => setCreateForm({ ...createForm, fileName: event.target.value })} required />
            </label>
            <label>{t('Title')}
              <input type="text" value={createForm.title} onChange={(event) => setCreateForm({ ...createForm, title: event.target.value })} required />
            </label>
            <label>{t('Description')}
              <textarea rows={2} value={createForm.description} onChange={(event) => setCreateForm({ ...createForm, description: event.target.value })} />
            </label>
            <label>{t('Workflow Profile')}
              <select value={createForm.workflowProfileId} onChange={(event) => setCreateForm({ ...createForm, workflowProfileId: event.target.value })}>
                <option value="">{t('No workflow profile')}</option>
                {profiles.map((profile) => (
                  <option key={profile.id} value={profile.id}>{profile.name}</option>
                ))}
              </select>
            </label>
            <label>{t('Environment')}
              <select value={createForm.environmentId} onChange={(event) => setCreateForm({ ...createForm, environmentId: event.target.value })}>
                <option value="">{t('No environment')}</option>
                {environments.map((environment) => (
                  <option key={environment.id} value={environment.id}>{environment.name}</option>
                ))}
              </select>
            </label>
            <label>{t('Default Check Type')}
              <select value={createForm.defaultCheckType} onChange={(event) => setCreateForm({ ...createForm, defaultCheckType: event.target.value as CheckRunType | '' })}>
                <option value="">{t('No default check')}</option>
                {RUNBOOK_CHECK_TYPES.map((checkType) => (
                  <option key={checkType} value={checkType}>{checkType}</option>
                ))}
              </select>
            </label>
            <ScopeSelect viewer={viewer} value={createForm.scope} onChange={(scope) => setCreateForm({ ...createForm, scope })} />
            <label className="checkbox-row">
              <input type="checkbox" checked={createForm.active} onChange={(event) => setCreateForm({ ...createForm, active: event.target.checked })} />
              <span>{t('Active')}</span>
            </label>
            <div className="modal-actions">
              <button type="submit" className="btn btn-primary" disabled={saving}>{saving ? t('Saving...') : t('Create Runbook')}</button>
              <button type="button" className="btn" onClick={() => setShowCreate(false)} disabled={saving}>{t('Cancel')}</button>
            </div>
          </form>
        </div>
      )}

      <div className="playbook-overview-grid">
        <div className="card playbook-overview-card">
          <span>{t('Total Runbooks')}</span>
          <strong>{runbooks.length}</strong>
        </div>
        <div className="card playbook-overview-card">
          <span>{t('Active')}</span>
          <strong>{runbooks.filter((runbook) => runbook.active).length}</strong>
        </div>
        <div className="card playbook-overview-card">
          <span>{t('Executions')}</span>
          <strong>{executions.length}</strong>
        </div>
        <div className="card playbook-overview-card">
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
          />
          <select value={activeFilter} onChange={(event) => setActiveFilter(event.target.value as typeof activeFilter)}>
            <option value="all">{t('All states')}</option>
            <option value="active">{t('Active')}</option>
            <option value="inactive">{t('Inactive')}</option>
          </select>
        </div>
      </div>

      {loading && runbooks.length === 0 ? (
        <p className="text-dim">{t('Loading...')}</p>
      ) : filtered.length === 0 ? (
        <div className="playbook-empty-state">
          <strong>{t('No runbooks match the current filters.')}</strong>
          <span>{canManage ? t('Create a runbook to guide release, deploy, rollback, migration, or incident work step by step.') : t('Ask a tenant administrator to create and manage runbooks.')}</span>
        </div>
      ) : (
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th>{t('Runbook')}</th>
                <th>{t('Binding')}</th>
                <th>{t('Steps')}</th>
                <th>{t('Executions')}</th>
                <th>{t('Visibility')}</th>
                <th>{t('Last Updated')}</th>
                <th className="text-right">{t('Actions')}</th>
              </tr>
              <tr className="column-filter-row">
                <td><input type="text" className="col-filter" value={colFilters.title} onChange={e => setColFilters(f => ({ ...f, title: e.target.value }))} placeholder={t('Filter...')} /></td>
                <td></td>
                <td></td>
                <td></td>
                <td></td>
                <td></td>
                <td></td>
              </tr>
            </thead>
            <tbody>
              {filtered.map((runbook) => {
                const counts = executionCounts.get(runbook.id) || { total: 0, running: 0 };
                return (
                  <tr key={runbook.id} className="clickable" onClick={() => setViewRecord(runbook as unknown as Record<string, unknown>)}>
                    <td>
                      <strong>{runbook.title}</strong>
                      <div className="text-dim" style={{ marginTop: '0.2rem' }}>
                        {runbook.fileName} {runbook.active ? `• ${t('Active')}` : `• ${t('Inactive')}`}
                      </div>
                      <div className="mono text-dim" style={{ fontSize: '0.78rem' }}>{runbook.id}</div>
                      {runbook.description && (
                        <div className="text-dim" style={{ marginTop: '0.2rem' }}>{runbook.description}</div>
                      )}
                    </td>
                    <td className="text-dim">
                      <div>{runbook.workflowProfileId ? (profileMap.get(runbook.workflowProfileId) || runbook.workflowProfileId) : t('No workflow profile')}</div>
                      <div>{runbook.environmentId ? (environmentMap.get(runbook.environmentId) || runbook.environmentName || runbook.environmentId) : (runbook.environmentName || t('No environment'))}</div>
                      <div>{runbook.defaultCheckType || t('No default check')}</div>
                    </td>
                    <td className="text-dim">{runbook.steps.length} {t('steps')} • {runbook.parameters.length} {t('parameters')}</td>
                    <td className="text-dim">
                      <div>{counts.total} {t('total')}</div>
                      <div>{counts.running} {t('running')}</div>
                    </td>
                    <td><ScopeBadge scope={runbook.scope} /></td>
                    <td className="text-dim" title={formatDateTime(runbook.lastUpdateUtc)}>{formatRelativeTime(runbook.lastUpdateUtc)}</td>
                    <td className="text-right" onClick={(event) => event.stopPropagation()}>
                      <ActionMenu
                        id={`runbook-${runbook.id}`}
                        items={[
                        { label: 'Open', onClick: () => navigate(`/runbooks/${runbook.id}`, { state: carryState }) },
                        { label: 'Duplicate', onClick: () => void handleDuplicate(runbook) },
                        { label: 'View JSON', onClick: () => setJsonData({ open: true, title: runbook.title, data: runbook }) },
                        ...(counts.running > 0 ? [{ label: `Running: ${counts.running}`, onClick: () => navigate(`/runbooks/${runbook.id}`, { state: carryState }) }] : []),
                        ...(canEditScoped(viewer, runbook) ? [{ label: 'Delete', danger: true as const, onClick: () => handleDelete(runbook) }] : []),
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
