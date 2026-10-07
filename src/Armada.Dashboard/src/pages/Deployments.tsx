import { useEffect, useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import PageHeader from '../components/shared/PageHeader';
import {
  createDeployment,
  deleteDeployment,
  listDeployments,
  listEnvironments,
  listReleases,
  listVessels,
  listWorkflowProfiles,
  updateDeployment,
} from '../api/client';
import type {
  Deployment,
  DeploymentEnvironment,
  DeploymentStatus,
  DeploymentUpsertRequest,
  DeploymentVerificationStatus,
  Release,
  Vessel,
  WorkflowProfile,
} from '../types/models';
import { useAuth } from '../context/AuthContext';
import { useLocale } from '../context/LocaleContext';
import { useNotifications } from '../context/NotificationContext';
import ActionMenu from '../components/shared/ActionMenu';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import ErrorModal from '../components/shared/ErrorModal';
import JsonViewer from '../components/shared/JsonViewer';
import CopyButton from '../components/shared/CopyButton';
import DataTable, { type DataTableColumn } from '../components/shared/DataTable';
import { useAutoRefresh } from '../lib/useAutoRefresh';
import StatusBadge from '../components/shared/StatusBadge';
import { buildEnvironmentOptions } from '../lib/deploymentEnvironments';
import { sortByName } from '../lib/sortByName';

const DEPLOYMENT_STATUSES: DeploymentStatus[] = [
  'PendingApproval',
  'Running',
  'Succeeded',
  'VerificationFailed',
  'Failed',
  'Denied',
  'RollingBack',
  'RolledBack',
];

const VERIFICATION_STATUSES: DeploymentVerificationStatus[] = [
  'NotRun',
  'Running',
  'Passed',
  'Failed',
  'Partial',
  'Skipped',
];

export default function Deployments() {
  const navigate = useNavigate();
  const { isAdmin, isTenantAdmin } = useAuth();
  const { t, formatDateTime, formatRelativeTime } = useLocale();
  const { pushToast } = useNotifications();

  const [deployments, setDeployments] = useState<Deployment[]>([]);
  const [vessels, setVessels] = useState<Vessel[]>([]);
  const [environments, setEnvironments] = useState<DeploymentEnvironment[]>([]);
  const [releases, setReleases] = useState<Release[]>([]);
  const [profiles, setProfiles] = useState<WorkflowProfile[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [search, setSearch] = useState('');
  const [statusFilter, setStatusFilter] = useState<'all' | DeploymentStatus>('all');
  const [verificationFilter, setVerificationFilter] = useState<'all' | DeploymentVerificationStatus>('all');
  const [vesselFilter, setVesselFilter] = useState('all');
  const [colFilters, setColFilters] = useState({ title: '', environmentName: '' });
  const [jsonData, setJsonData] = useState<{ open: boolean; title: string; data: unknown }>({ open: false, title: '', data: null });
  const [confirm, setConfirm] = useState<{ open: boolean; title: string; message: string; onConfirm: () => void }>({
    open: false,
    title: '',
    message: '',
    onConfirm: () => {},
  });

  const canManage = isAdmin || isTenantAdmin;

  const EMPTY_CREATE_FORM = {
    vesselId: '',
    workflowProfileId: '',
    environmentId: '',
    environmentName: '',
    releaseId: '',
    sourceRef: '',
    missionId: '',
    voyageId: '',
    title: 'Deployment',
    summary: '',
    notes: '',
    autoExecute: true,
  };

  const [showCreate, setShowCreate] = useState(false);
  const [editing, setEditing] = useState<Deployment | null>(null);
  const [saving, setSaving] = useState(false);
  const [createForm, setCreateForm] = useState(EMPTY_CREATE_FORM);

  async function load() {
    try {
      setLoading(true);
      const [deploymentResult, vesselResult, environmentResult, releaseResult, profileResult] = await Promise.all([
        listDeployments({ pageSize: 9999 }),
        listVessels({ pageSize: 9999 }),
        listEnvironments({ pageSize: 9999 }),
        listReleases({ pageSize: 9999 }),
        listWorkflowProfiles({ pageSize: 9999 }),
      ]);
      setDeployments(deploymentResult.objects || []);
      setVessels(sortByName(vesselResult.objects));
      setEnvironments(environmentResult.objects || []);
      setReleases(releaseResult.objects || []);
      setProfiles(profileResult.objects || []);
      setError('');
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Failed to load deployments.'));
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    load();
  }, []);
  const { seconds: refreshSeconds, setSeconds: setRefreshSeconds } = useAutoRefresh('deployments', load);

  function openCreate() {
    setEditing(null);
    setCreateForm(EMPTY_CREATE_FORM);
    setShowCreate(true);
  }

  function openEdit(deployment: Deployment) {
    setEditing(deployment);
    setCreateForm({
      vesselId: deployment.vesselId || '',
      workflowProfileId: deployment.workflowProfileId || '',
      environmentId: deployment.environmentId || '',
      environmentName: deployment.environmentName || '',
      releaseId: deployment.releaseId || '',
      sourceRef: deployment.sourceRef || '',
      missionId: deployment.missionId || '',
      voyageId: deployment.voyageId || '',
      title: deployment.title || 'Deployment',
      summary: deployment.summary || '',
      notes: deployment.notes || '',
      autoExecute: true,
    });
    setShowCreate(true);
  }

  function handleEnvironmentChange(environmentId: string) {
    const selected = environments.find((environment) => environment.id === environmentId);
    setCreateForm((current) => ({
      ...current,
      environmentId,
      environmentName: selected ? selected.name : current.environmentName,
      vesselId: !current.vesselId && selected?.vesselId ? selected.vesselId : current.vesselId,
    }));
  }

  function handleVesselChange(vesselId: string) {
    setCreateForm((current) => {
      const environment = environments.find((item) => item.id === current.environmentId);
      const keepEnvironment = !vesselId || !environment || environment.vesselId === vesselId;
      return {
        ...current,
        vesselId,
        environmentId: keepEnvironment ? current.environmentId : '',
      };
    });
  }

  function handleReleaseChange(releaseId: string) {
    const selected = releases.find((release) => release.id === releaseId);
    setCreateForm((current) => ({
      ...current,
      releaseId,
      vesselId: !current.vesselId && selected?.vesselId ? selected.vesselId : current.vesselId,
    }));
  }

  async function handleCreate(event: React.FormEvent) {
    event.preventDefault();
    if (saving) return;
    try {
      setSaving(true);
      const payload: DeploymentUpsertRequest = {
        vesselId: createForm.vesselId || null,
        workflowProfileId: createForm.workflowProfileId || null,
        environmentId: createForm.environmentId || null,
        environmentName: createForm.environmentName.trim() || null,
        releaseId: createForm.releaseId || null,
        missionId: createForm.missionId.trim() || null,
        voyageId: createForm.voyageId.trim() || null,
        title: createForm.title.trim() || null,
        sourceRef: createForm.sourceRef.trim() || null,
        summary: createForm.summary.trim() || null,
        notes: createForm.notes.trim() || null,
        autoExecute: createForm.autoExecute,
      };
      if (editing) {
        const updated = await updateDeployment(editing.id, payload);
        setShowCreate(false);
        pushToast('success', t('Deployment "{{title}}" saved.', { title: updated.title }));
      } else {
        const created = await createDeployment(payload);
        setShowCreate(false);
        pushToast('success', t('Deployment "{{title}}" created.', { title: created.title }));
      }
      await load();
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Save failed.'));
    } finally {
      setSaving(false);
    }
  }

  const filteredEnvironments = useMemo(
    () => environments.filter((environment) => !createForm.vesselId || environment.vesselId === createForm.vesselId),
    [environments, createForm.vesselId],
  );
  const filteredReleases = useMemo(
    () => releases.filter((release) => !createForm.vesselId || release.vesselId === createForm.vesselId),
    [releases, createForm.vesselId],
  );

  const vesselMap = useMemo(() => new Map(vessels.map((vessel) => [vessel.id, vessel.name])), [vessels]);
  const environmentMap = useMemo(() => new Map(environments.map((environment) => [environment.id, environment.name])), [environments]);
  // Every vessel gets a default "Development" environment, so without a chosen vessel the names alone are
  // indistinguishable; label them "vessel / environment" and sort by that label.
  const environmentOptions = useMemo(
    () => buildEnvironmentOptions(filteredEnvironments, vesselMap, !!createForm.vesselId, t('No vessel')),
    [filteredEnvironments, vesselMap, createForm.vesselId, t],
  );
  const releaseMap = useMemo(() => new Map(releases.map((release) => [release.id, release.title])), [releases]);

  const filtered = useMemo(() => deployments.filter((deployment) => {
    const normalizedSearch = search.trim().toLowerCase();
    const matchesSearch = normalizedSearch.length === 0
      || deployment.title.toLowerCase().includes(normalizedSearch)
      || (deployment.environmentName || '').toLowerCase().includes(normalizedSearch)
      || (deployment.summary || '').toLowerCase().includes(normalizedSearch)
      || (deployment.sourceRef || '').toLowerCase().includes(normalizedSearch)
      || deployment.id.toLowerCase().includes(normalizedSearch);

    const matchesStatus = statusFilter === 'all' || deployment.status === statusFilter;
    const matchesVerification = verificationFilter === 'all' || deployment.verificationStatus === verificationFilter;
    const matchesVessel = vesselFilter === 'all' || deployment.vesselId === vesselFilter;
    const matchesColFilters = (!colFilters.title || deployment.title.toLowerCase().includes(colFilters.title.toLowerCase()))
      && (!colFilters.environmentName || (deployment.environmentName ?? '').toLowerCase().includes(colFilters.environmentName.toLowerCase()));
    return matchesSearch && matchesStatus && matchesVerification && matchesVessel && matchesColFilters;
  }), [colFilters, deployments, search, statusFilter, verificationFilter, vesselFilter]);

  const pendingApprovalCount = deployments.filter((deployment) => deployment.status === 'PendingApproval').length;
  const runningCount = deployments.filter((deployment) => deployment.status === 'Running' || deployment.status === 'RollingBack').length;
  const successfulCount = deployments.filter((deployment) => deployment.status === 'Succeeded').length;
  const failedCount = deployments.filter((deployment) => deployment.status === 'Failed' || deployment.status === 'VerificationFailed').length;

  function handleDelete(deployment: Deployment) {
    setConfirm({
      open: true,
      title: t('Delete Deployment'),
      message: t('Delete "{{title}}"? This removes only the deployment record and leaves linked checks, releases, and environments intact.', { title: deployment.title }),
      onConfirm: async () => {
        setConfirm((current) => ({ ...current, open: false }));
        try {
          await deleteDeployment(deployment.id);
          pushToast('warning', t('Deployment "{{title}}" deleted.', { title: deployment.title }));
          await load();
        } catch (err: unknown) {
          setError(err instanceof Error ? err.message : t('Delete failed.'));
        }
      },
    });
  }

  const columns: DataTableColumn<Deployment>[] = [
    {
      key: 'title', label: t('Deployment'), required: true,
      filter: <input type="text" className="col-filter" aria-label={t('Filter by title')} value={colFilters.title} onChange={e => setColFilters(f => ({ ...f, title: e.target.value }))} placeholder={t('Filter...')} />,
      // One line: the summary and approval requirement are in the tooltip and in their own (optional) columns.
      cellTitle: (deployment) => [deployment.title, deployment.summary, deployment.approvalRequired ? t('Approval required') : ''].filter(Boolean).join('\n'),
      render: (deployment) => <strong className="cell-one-line">{deployment.title}</strong>,
    },
    {
      key: 'id', label: t('ID'), required: true, cellClassName: 'mono text-dim table-id-cell',
      render: (deployment) => (
        <span className="id-display">
          <span className="id-value" title={deployment.id}>{deployment.id}</span>
          <CopyButton text={deployment.id} onClick={e => e.stopPropagation()} />
        </span>
      ),
    },
    { key: 'status', label: t('Status'), cellClassName: 'cell-nowrap', render: (deployment) => <StatusBadge status={deployment.status} /> },
    { key: 'verification', label: t('Verification'), cellClassName: 'cell-nowrap', render: (deployment) => <StatusBadge status={deployment.verificationStatus} /> },
    {
      key: 'sourceRef', label: t('Source Ref'), cellClassName: 'mono text-dim',
      render: (deployment) => <span className="cell-one-line" title={deployment.sourceRef || undefined}>{deployment.sourceRef || '-'}</span>,
    },
    { key: 'vessel', label: t('Vessel'), cellClassName: 'text-dim', render: (deployment) => (deployment.vesselId ? (vesselMap.get(deployment.vesselId) || deployment.vesselId) : '-') },
    {
      key: 'environment', label: t('Environment'), cellClassName: 'text-dim',
      clearFilter: () => setColFilters(f => ({ ...f, environmentName: '' })),
      filter: <input type="text" className="col-filter" aria-label={t('Filter by environment')} value={colFilters.environmentName} onChange={e => setColFilters(f => ({ ...f, environmentName: e.target.value }))} placeholder={t('Filter...')} />,
      render: (deployment) => (deployment.environmentId ? (environmentMap.get(deployment.environmentId) || deployment.environmentName || deployment.environmentId) : (deployment.environmentName || '-')),
    },
    { key: 'release', label: t('Release'), cellClassName: 'text-dim', render: (deployment) => (deployment.releaseId ? (releaseMap.get(deployment.releaseId) || deployment.releaseId) : '-') },
    { key: 'checks', label: t('Checks'), cellClassName: 'text-dim', render: (deployment) => deployment.checkRunIds.length },
    {
      key: 'approval', label: t('Approval'), defaultHidden: true, cellClassName: 'text-dim cell-nowrap',
      render: (deployment) => (deployment.approvalRequired ? t('Approval required') : '-'),
    },
    {
      key: 'summary', label: t('Summary'), defaultHidden: true, cellClassName: 'text-dim',
      render: (deployment) => <span className="cell-one-line" title={deployment.summary || undefined}>{deployment.summary || '-'}</span>,
    },
    {
      key: 'lastUpdated', label: t('Last Updated'), cellClassName: 'text-dim cell-nowrap',
      cellTitle: (deployment) => formatDateTime(deployment.lastUpdateUtc),
      render: (deployment) => formatRelativeTime(deployment.lastUpdateUtc),
    },
    {
      key: 'actions', label: t('Actions'), fixed: true, interactive: true, className: 'text-right',
      render: (deployment) => (
        <ActionMenu
          id={`deployment-${deployment.id}`}
          items={[
            { label: 'Open', onClick: () => navigate(`/deployments/${deployment.id}`) },
            ...(canManage ? [{ label: 'Edit', onClick: () => openEdit(deployment) }] : []),
            { label: 'View JSON', onClick: () => setJsonData({ open: true, title: deployment.title, data: deployment }) },
            ...(canManage ? [{ label: 'Delete', danger: true as const, onClick: () => handleDelete(deployment) }] : []),
          ]}
        />
      ),
    },
  ];

  return (
    <div>
      <PageHeader
        title={t('Deployments')}
        subtitle={t('First-class deployment records linking releases, environments, checks, approval, verification, rollback, and request-history evidence.')}
        actions={(
          <>
            {canManage && (
              <button className="btn btn-primary" onClick={openCreate}>
                + {t('Deployment')}
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

      {showCreate && (
        <div className="modal-overlay" onClick={() => setShowCreate(false)}>
          <form className="modal modal-large" onClick={(event) => event.stopPropagation()} onSubmit={handleCreate}>
            <h3>{editing ? t('Edit Deployment') : t('Create Deployment')}</h3>
            <label>{t('Vessel')}
              <select value={createForm.vesselId} onChange={(event) => handleVesselChange(event.target.value)}>
                <option value="">{t('Select a vessel')}</option>
                {vessels.map((vessel) => (
                  <option key={vessel.id} value={vessel.id}>{vessel.name}</option>
                ))}
              </select>
            </label>
            <label>{t('Workflow Profile')}
              <select value={createForm.workflowProfileId} onChange={(event) => setCreateForm((current) => ({ ...current, workflowProfileId: event.target.value }))}>
                <option value="">{t('Resolved default')}</option>
                {profiles.map((profile) => (
                  <option key={profile.id} value={profile.id}>{profile.name}</option>
                ))}
              </select>
            </label>
            <label>{t('Environment')}
              <select value={createForm.environmentId} onChange={(event) => handleEnvironmentChange(event.target.value)}>
                <option value="">{t('Resolve by environment name')}</option>
                {environmentOptions.map((option) => (
                  <option key={option.id} value={option.id}>{option.label}</option>
                ))}
              </select>
            </label>
            <label>{t('Environment Name')}
              <input value={createForm.environmentName} onChange={(event) => setCreateForm((current) => ({ ...current, environmentName: event.target.value }))} placeholder={t('staging, production, customer-a')} />
            </label>
            <label>{t('Release')}
              <select value={createForm.releaseId} onChange={(event) => handleReleaseChange(event.target.value)}>
                <option value="">{t('No linked release')}</option>
                {filteredReleases.map((release) => (
                  <option key={release.id} value={release.id}>{release.title}</option>
                ))}
              </select>
            </label>
            <label>{t('Source Ref')}
              <input value={createForm.sourceRef} onChange={(event) => setCreateForm((current) => ({ ...current, sourceRef: event.target.value }))} placeholder={t('branch, tag, or commit')} />
            </label>
            <label>{t('Mission ID')}
              <input value={createForm.missionId} onChange={(event) => setCreateForm((current) => ({ ...current, missionId: event.target.value }))} placeholder="mis_..." />
            </label>
            <label>{t('Voyage ID')}
              <input value={createForm.voyageId} onChange={(event) => setCreateForm((current) => ({ ...current, voyageId: event.target.value }))} placeholder="voy_..." />
            </label>
            <label>{t('Title')}
              <input value={createForm.title} onChange={(event) => setCreateForm((current) => ({ ...current, title: event.target.value }))} />
            </label>
            <label>{t('Summary')}
              <textarea rows={3} value={createForm.summary} onChange={(event) => setCreateForm((current) => ({ ...current, summary: event.target.value }))} />
            </label>
            <label>{t('Notes')}
              <textarea rows={4} value={createForm.notes} onChange={(event) => setCreateForm((current) => ({ ...current, notes: event.target.value }))} />
            </label>
            <label style={{ display: 'inline-flex', alignItems: 'center', gap: '0.45rem' }}>
              <input type="checkbox" checked={createForm.autoExecute} onChange={(event) => setCreateForm((current) => ({ ...current, autoExecute: event.target.checked }))} />
              <span>{t('Execute immediately when approval is not required')}</span>
            </label>
            <div className="modal-actions">
              <button type="submit" className="btn btn-primary" disabled={saving}>{saving ? t('Saving...') : editing ? t('Save Changes') : t('Create Deployment')}</button>
              <button type="button" className="btn" onClick={() => setShowCreate(false)} disabled={saving}>{t('Cancel')}</button>
            </div>
          </form>
        </div>
      )}

      <div className="playbook-overview-grid">
        <div className="card playbook-overview-card">
          <span>{t('Total Deployments')}</span>
          <strong>{deployments.length}</strong>
        </div>
        <div className="card playbook-overview-card">
          <span>{t('Pending Approval')}</span>
          <strong>{pendingApprovalCount}</strong>
        </div>
        <div className="card playbook-overview-card">
          <span>{t('Running')}</span>
          <strong>{runningCount}</strong>
        </div>
        <div className="card playbook-overview-card">
          <span>{t('Succeeded')}</span>
          <strong>{successfulCount}</strong>
        </div>
        <div className="card playbook-overview-card">
          <span>{t('Failed / Verification Failed')}</span>
          <strong>{failedCount}</strong>
        </div>
      </div>

      <div className="card" style={{ padding: '1rem', marginBottom: '1rem' }}>
        <div className="playbook-filter-row">
          <input
            type="text"
            value={search}
            onChange={(event) => setSearch(event.target.value)}
            placeholder={t('Search by title, environment, source ref, summary, or ID...')}
          />
          <select aria-label={t('All statuses')} value={statusFilter} onChange={(event) => setStatusFilter(event.target.value as typeof statusFilter)}>
            <option value="all">{t('All statuses')}</option>
            {DEPLOYMENT_STATUSES.map((status) => (
              <option key={status} value={status}>{status}</option>
            ))}
          </select>
          <select aria-label={t('All verification states')} value={verificationFilter} onChange={(event) => setVerificationFilter(event.target.value as typeof verificationFilter)}>
            <option value="all">{t('All verification states')}</option>
            {VERIFICATION_STATUSES.map((status) => (
              <option key={status} value={status}>{status}</option>
            ))}
          </select>
          <select aria-label={t('All vessels')} value={vesselFilter} onChange={(event) => setVesselFilter(event.target.value)}>
            <option value="all">{t('All vessels')}</option>
            {vessels.map((vessel) => (
              <option key={vessel.id} value={vessel.id}>{vessel.name}</option>
            ))}
          </select>
        </div>
      </div>

      <DataTable
        tableKey="deployments"
        columns={columns}
        rows={filtered}
        rowKey={(deployment) => deployment.id}
        onRowClick={(deployment) => (canManage ? openEdit(deployment) : navigate(`/deployments/${deployment.id}`))}
        autoRefresh={{ seconds: refreshSeconds, onChange: setRefreshSeconds }}
        onRefresh={load}
        refreshTitle={t('Refresh deployments')}
        placeholder={loading && deployments.length === 0 ? <p className="text-dim">{t('Loading...')}</p> : filtered.length === 0 ? (
          <div className="playbook-empty-state">
            <strong>{t('No deployments match the current filters.')}</strong>
            <span>{canManage ? t('Create a deployment from an environment or release to track approval, execution, verification, and rollback in one record.') : t('Ask a tenant administrator to create and manage deployment records.')}</span>
          </div>
        ) : undefined}
      />
    </div>
  );
}
