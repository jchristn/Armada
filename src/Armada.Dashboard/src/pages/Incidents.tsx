import { duplicateDisplayName } from '../lib/duplicates';
import { useEffect, useMemo, useState } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import PageHeader from '../components/shared/PageHeader';
import {
  createIncident,
  deleteIncident,
  listDeployments,
  listEnvironments,
  listIncidents,
  listReleases,
  listVessels,
} from '../api/client';
import { listAll } from '../api/paging';
import type {
  Deployment,
  DeploymentEnvironment,
  Incident,
  IncidentSeverity,
  IncidentStatus,
  IncidentUpsertRequest,
  Release,
  Vessel,
} from '../types/models';
import { useAuth } from '../context/AuthContext';
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
import { INCIDENT_SEVERITIES, INCIDENT_STATUSES } from '../lib/deliveryForms';
import { useDeleteProgress } from '../lib/useDeleteProgress';


interface IncidentPageState {
  prefill?: Record<string, unknown>;
}

export default function Incidents() {
  const navigate = useNavigate();
  const location = useLocation();
  const { isAdmin, isTenantAdmin } = useAuth();
  const { t, formatDateTime, formatRelativeTime } = useLocale();
  const { pushToast } = useNotifications();
  const deletion = useDeleteProgress();

  const [incidents, setIncidents] = useState<Incident[]>([]);
  const [vessels, setVessels] = useState<Vessel[]>([]);
  const [environments, setEnvironments] = useState<DeploymentEnvironment[]>([]);
  const [deployments, setDeployments] = useState<Deployment[]>([]);
  const [releases, setReleases] = useState<Release[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [search, setSearch] = useState('');
  const [statusFilter, setStatusFilter] = useState<'all' | IncidentStatus>('all');
  const [severityFilter, setSeverityFilter] = useState<'all' | IncidentSeverity>('all');
  const [colFilters, setColFilters] = useState({ title: '', environmentName: '' });
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
    title: string;
    status: IncidentStatus;
    severity: IncidentSeverity;
    vesselId: string;
    environmentId: string;
    deploymentId: string;
    releaseId: string;
    summary: string;
    impact: string;
  }>({
    title: 'Incident',
    status: 'Open',
    severity: 'High',
    vesselId: '',
    environmentId: '',
    deploymentId: '',
    releaseId: '',
    summary: '',
    impact: '',
  });

  const canManage = isAdmin || isTenantAdmin;

  const tips = {
    title: t('Short name for what broke, such as Checkout errors after v1.4.0. Required.'),
    status: t('Lifecycle state: Open, Monitoring after a fix, Mitigated, RolledBack, or Closed. Changing it does not run anything.'),
    severity: t('How bad the impact is: Critical, High, Medium, or Low. Used for sorting and filtering only.'),
    vessel: t('Repository affected by the incident. Optional, used for context and filtering.'),
    environment: t('Environment where the incident happened. Optional; links the incident to that environment\'s record.'),
    deployment: t('Deployment that caused or relates to the incident. Optional; open the deployment to roll it back.'),
    release: t('Release involved in the incident. Optional, for context.'),
    summary: t('What happened, in a sentence or two. Shown in the list and searchable.'),
    impact: t('Who or what was affected and how, such as 5% of checkout requests failing for 20 minutes.'),
  };
  const carryState = (location.state as IncidentPageState | null) || null;

  function openCreate() {
    const prefill = (carryState?.prefill || {}) as Record<string, unknown>;
    setCreateForm({
      title: typeof prefill.title === 'string' ? prefill.title : 'Incident',
      status: (typeof prefill.status === 'string' ? prefill.status : 'Open') as IncidentStatus,
      severity: (typeof prefill.severity === 'string' ? prefill.severity : 'High') as IncidentSeverity,
      vesselId: typeof prefill.vesselId === 'string' ? prefill.vesselId : '',
      environmentId: typeof prefill.environmentId === 'string' ? prefill.environmentId : '',
      deploymentId: typeof prefill.deploymentId === 'string' ? prefill.deploymentId : '',
      releaseId: typeof prefill.releaseId === 'string' ? prefill.releaseId : '',
      summary: typeof prefill.summary === 'string' ? prefill.summary : '',
      impact: typeof prefill.impact === 'string' ? prefill.impact : '',
    });
    setShowCreate(true);
  }

  function openDuplicate(incident: Incident) {
    setCreateForm({ title: duplicateDisplayName(incident.title), status: 'Open', severity: incident.severity,
      vesselId: incident.vesselId || '', environmentId: incident.environmentId || '', deploymentId: incident.deploymentId || '',
      releaseId: incident.releaseId || '', summary: incident.summary || '', impact: incident.impact || '' });
    setShowCreate(true);
  }

  async function handleCreate(event: React.FormEvent) {
    event.preventDefault();
    if (saving) return;
    setSaving(true);
    try {
      const payload: IncidentUpsertRequest = {
        title: createForm.title.trim() || null,
        summary: createForm.summary.trim() || null,
        status: createForm.status,
        severity: createForm.severity,
        vesselId: createForm.vesselId || null,
        environmentId: createForm.environmentId || null,
        environmentName: createForm.environmentId ? (environmentMap.get(createForm.environmentId) || null) : null,
        deploymentId: createForm.deploymentId || null,
        releaseId: createForm.releaseId || null,
        impact: createForm.impact.trim() || null,
      };
      const created = await createIncident(payload);
      setShowCreate(false);
      pushToast('success', t('Incident "{{title}}" created.', { title: created.title }));
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
      const [incidentResult, vesselResult, environmentResult, deploymentResult, releaseResult] = await Promise.all([
        listAll(listIncidents),
        listAll(listVessels),
        listAll(listEnvironments),
        listAll(listDeployments),
        listAll(listReleases),
      ]);
      setIncidents(incidentResult.objects || []);
      setVessels(vesselResult.objects || []);
      setEnvironments(environmentResult.objects || []);
      setDeployments(deploymentResult.objects || []);
      setReleases(releaseResult.objects || []);
      setError('');
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Failed to load incidents.'));
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void load();
  }, []);

  const { seconds: refreshSeconds, setSeconds: setRefreshSeconds } = useAutoRefresh('incidents', load);

  const vesselMap = useMemo(() => new Map(vessels.map((vessel) => [vessel.id, vessel.name])), [vessels]);
  const environmentMap = useMemo(() => new Map(environments.map((environment) => [environment.id, environment.name])), [environments]);
  const deploymentMap = useMemo(() => new Map(deployments.map((deployment) => [deployment.id, deployment.title])), [deployments]);
  const releaseMap = useMemo(() => new Map(releases.map((release) => [release.id, release.title])), [releases]);

  const filtered = useMemo(() => incidents.filter((incident) => {
    const normalizedSearch = search.trim().toLowerCase();
    const matchesSearch = normalizedSearch.length === 0
      || incident.title.toLowerCase().includes(normalizedSearch)
      || (incident.summary || '').toLowerCase().includes(normalizedSearch)
      || (incident.environmentName || '').toLowerCase().includes(normalizedSearch)
      || (incident.impact || '').toLowerCase().includes(normalizedSearch)
      || incident.id.toLowerCase().includes(normalizedSearch);
    const matchesStatus = statusFilter === 'all' || incident.status === statusFilter;
    const matchesSeverity = severityFilter === 'all' || incident.severity === severityFilter;
    const matchesColFilters = (!colFilters.title || incident.title.toLowerCase().includes(colFilters.title.toLowerCase()))
      && (!colFilters.environmentName || (incident.environmentName ?? '').toLowerCase().includes(colFilters.environmentName.toLowerCase()));
    return matchesSearch && matchesStatus && matchesSeverity && matchesColFilters;
  }), [colFilters, incidents, search, severityFilter, statusFilter]);

  const openCount = incidents.filter((incident) => incident.status === 'Open').length;
  const monitoringCount = incidents.filter((incident) => incident.status === 'Monitoring').length;
  const mitigatedCount = incidents.filter((incident) => incident.status === 'Mitigated').length;
  const closedCount = incidents.filter((incident) => incident.status === 'Closed' || incident.status === 'RolledBack').length;

  function handleDelete(incident: Incident) {
    setConfirm({
      open: true,
      title: t('Delete Incident'),
      message: t('Delete "{{title}}"? This removes the incident snapshot chain but does not affect deployments, checks, or releases.', { title: incident.title }),
      onConfirm: async () => {
        setConfirm((current) => ({ ...current, open: false }));
        if (!(await deletion.run({ kind: 'incident', action: () => deleteIncident(incident.id), errorMessage: (err) => (err instanceof Error ? err.message : t('Delete failed.')) }))) return;
        pushToast('warning', t('Incident "{{title}}" deleted.', { title: incident.title }));
        await load();
      },
    });
  }

  const columns: DataTableColumn<Incident>[] = [
    {
      key: 'id', label: t('ID'), required: true, headerTitle: t('Incident ID (inc_ prefix), used by the REST API, MCP tools, and links.'), cellClassName: 'mono text-dim table-id-cell',
      render: (incident) => (
        <span className="id-display">
          <span className="id-value" title={incident.id}>{incident.id}</span>
          <CopyButton text={incident.id} onClick={e => e.stopPropagation()} />
        </span>
      ),
    },
    {
      key: 'title', label: t('Incident'), required: true, headerTitle: t('Incident title. Hover a row for its summary or impact.'),
      filter: <input type="text" className="col-filter" aria-label={t('Filter by title')} title={t('Show only incidents whose title contains this text.')} value={colFilters.title} onChange={e => setColFilters(f => ({ ...f, title: e.target.value }))} placeholder={t('Filter...')} />,
      // One line: the summary (or impact) is in the tooltip and in the optional Summary column.
      cellTitle: (incident) => [incident.title, incident.summary || incident.impact].filter(Boolean).join('\n'),
      render: (incident) => <strong className="cell-one-line">{incident.title}</strong>,
    },
    { key: 'status', label: t('Status'), headerTitle: t('Lifecycle state: Open, Monitoring, Mitigated, RolledBack, or Closed.'), cellClassName: 'cell-nowrap', render: (incident) => <StatusBadge status={incident.status} /> },
    { key: 'severity', label: t('Severity'), headerTitle: t('Impact level: Critical, High, Medium, or Low.'), cellClassName: 'cell-nowrap', render: (incident) => <StatusBadge status={incident.severity} /> },
    {
      key: 'environment', label: t('Environment'), headerTitle: t('Environment where the incident happened.'), cellClassName: 'text-dim',
      clearFilter: () => setColFilters(f => ({ ...f, environmentName: '' })),
      filter: <input type="text" className="col-filter" aria-label={t('Filter by environment')} title={t('Show only incidents whose environment name contains this text.')} value={colFilters.environmentName} onChange={e => setColFilters(f => ({ ...f, environmentName: e.target.value }))} placeholder={t('Filter...')} />,
      render: (incident) => (incident.environmentId ? (environmentMap.get(incident.environmentId) || incident.environmentName || incident.environmentId) : (incident.environmentName || '-')),
    },
    { key: 'deployment', label: t('Deployment'), headerTitle: t('Deployment linked to the incident.'), cellClassName: 'text-dim', render: (incident) => (incident.deploymentId ? (deploymentMap.get(incident.deploymentId) || incident.deploymentId) : '-') },
    { key: 'release', label: t('Release'), headerTitle: t('Release linked to the incident.'), cellClassName: 'text-dim', render: (incident) => (incident.releaseId ? (releaseMap.get(incident.releaseId) || incident.releaseId) : '-') },
    {
      key: 'summary', label: t('Summary'), headerTitle: t('Incident summary, or its impact when no summary is set.'), defaultHidden: true, cellClassName: 'text-dim',
      render: (incident) => {
        const text = incident.summary || incident.impact || t('No summary provided');
        return <span className="cell-one-line" title={text}>{text}</span>;
      },
    },
    {
      key: 'lastUpdated', label: t('Last Updated'), headerTitle: t('When the incident was last saved. Hover a cell for the exact time.'), cellClassName: 'text-dim cell-nowrap',
      cellTitle: (incident) => formatDateTime(incident.lastUpdateUtc),
      render: (incident) => formatRelativeTime(incident.lastUpdateUtc),
    },
    {
      key: 'actions', label: t('Actions'), fixed: true, interactive: true, className: 'text-right',
      render: (incident) => (
        <ActionMenu
          id={`incident-${incident.id}`}
          items={[
            ...(canManage ? [{ label: 'Duplicate', title: 'Edit a fresh incident with the same context. Timeline and resolution are not copied.', onClick: () => openDuplicate(incident) }] : []),
            { label: 'Open', title: 'Open the incident detail page to edit root cause, recovery, and postmortem notes or start a hotfix.', onClick: () => navigate(`/incidents/${incident.id}`, { state: carryState }) },
            { label: 'View JSON', title: 'Show the raw incident record as returned by the API.', onClick: () => setJsonData({ open: true, title: incident.title, data: incident }) },
            ...(canManage ? [{ label: 'Delete', title: 'Delete this incident and its saved history. Deployments, checks, and releases are not affected. Cannot be undone.', danger: true as const, onClick: () => handleDelete(incident) }] : []),
          ]}
        />
      ),
    },
  ];

  return (
    <div>
      <PageHeader
        title={t('Incidents')}
        subtitle={t('Track incidents, hotfix context, rollback history, recovery notes, and postmortems alongside deployments and releases.')}
        actions={(
          <>
            {canManage && (
              <button className="btn btn-primary" onClick={openCreate} title={t('Record a new incident. Creating it from a deployment or environment instead keeps more context linked.')}>
                + {t('Incident')}
              </button>
            )}
          </>
        )}
      />

      <ErrorModal error={error} onClose={() => setError('')} />
      <JsonViewer open={jsonData.open} title={jsonData.title} data={jsonData.data} onClose={() => setJsonData({ open: false, title: '', data: null })} />
      <RecordDetailModal
        open={!!viewRecord}
        title={viewRecord ? String(viewRecord.title || viewRecord.id || '') : ''}
        subtitle={t('Incident')}
        record={viewRecord}
        onClose={() => setViewRecord(null)}
        onEdit={() => {
          const r = viewRecord;
          setViewRecord(null);
          if (r) navigate(`/incidents/${(r as { id: string }).id}`, { state: carryState });
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
      {deletion.dialog}

      {showCreate && (
        <div className="modal-overlay" onClick={() => setShowCreate(false)}>
          <form className="modal modal-large" onClick={(event) => event.stopPropagation()} onSubmit={handleCreate}>
            <h3>{t('Create Incident')}</h3>
            <label title={tips.title}>{t('Title')}
              <input title={tips.title} type="text" value={createForm.title} onChange={(event) => setCreateForm({ ...createForm, title: event.target.value })} required />
            </label>
            <label title={tips.status}>{t('Status')}
              <select title={tips.status} value={createForm.status} onChange={(event) => setCreateForm({ ...createForm, status: event.target.value as IncidentStatus })}>
                {INCIDENT_STATUSES.map((status) => (
                  <option key={status} value={status}>{status}</option>
                ))}
              </select>
            </label>
            <label title={tips.severity}>{t('Severity')}
              <select title={tips.severity} value={createForm.severity} onChange={(event) => setCreateForm({ ...createForm, severity: event.target.value as IncidentSeverity })}>
                {INCIDENT_SEVERITIES.map((severity) => (
                  <option key={severity} value={severity}>{severity}</option>
                ))}
              </select>
            </label>
            <label title={tips.vessel}>{t('Vessel')}
              <select title={tips.vessel} value={createForm.vesselId} onChange={(event) => setCreateForm({ ...createForm, vesselId: event.target.value })}>
                <option value="">{t('Select a vessel')}</option>
                {vessels.map((vessel) => (
                  <option key={vessel.id} value={vessel.id}>{vessel.name}</option>
                ))}
              </select>
            </label>
            <label title={tips.environment}>{t('Environment')}
              <select title={tips.environment} value={createForm.environmentId} onChange={(event) => setCreateForm({ ...createForm, environmentId: event.target.value })}>
                <option value="">{t('Select an environment')}</option>
                {environments.map((environment) => (
                  <option key={environment.id} value={environment.id}>{environment.name}</option>
                ))}
              </select>
            </label>
            <label title={tips.deployment}>{t('Deployment')}
              <select title={tips.deployment} value={createForm.deploymentId} onChange={(event) => setCreateForm({ ...createForm, deploymentId: event.target.value })}>
                <option value="">{t('No linked deployment')}</option>
                {deployments.map((deployment) => (
                  <option key={deployment.id} value={deployment.id}>{deployment.title}</option>
                ))}
              </select>
            </label>
            <label title={tips.release}>{t('Release')}
              <select title={tips.release} value={createForm.releaseId} onChange={(event) => setCreateForm({ ...createForm, releaseId: event.target.value })}>
                <option value="">{t('No linked release')}</option>
                {releases.map((release) => (
                  <option key={release.id} value={release.id}>{release.title}</option>
                ))}
              </select>
            </label>
            <label title={tips.summary}>{t('Summary')}
              <textarea title={tips.summary} rows={3} value={createForm.summary} onChange={(event) => setCreateForm({ ...createForm, summary: event.target.value })} />
            </label>
            <label title={tips.impact}>{t('Impact')}
              <textarea title={tips.impact} rows={3} value={createForm.impact} onChange={(event) => setCreateForm({ ...createForm, impact: event.target.value })} />
            </label>
            <div className="modal-actions">
              <button type="submit" className="btn btn-primary" disabled={saving} title={t('Create the incident record. You can add root cause, recovery, and postmortem notes on its detail page.')}>{saving ? t('Saving...') : t('Create Incident')}</button>
              <button type="button" className="btn" onClick={() => setShowCreate(false)} disabled={saving} title={t('Close without saving.')}>{t('Cancel')}</button>
            </div>
          </form>
        </div>
      )}

      <div className="playbook-overview-grid">
        <div className="card playbook-overview-card" title={t('All incident records you can see, before filters.')}>
          <span>{t('Total Incidents')}</span>
          <strong>{incidents.length}</strong>
        </div>
        <div className="card playbook-overview-card" title={t('Incidents still active and needing attention.')}>
          <span>{t('Open')}</span>
          <strong>{openCount}</strong>
        </div>
        <div className="card playbook-overview-card" title={t('Incidents being watched after mitigation work.')}>
          <span>{t('Monitoring')}</span>
          <strong>{monitoringCount}</strong>
        </div>
        <div className="card playbook-overview-card" title={t('Incidents whose impact is stopped but may still have follow-up work.')}>
          <span>{t('Mitigated')}</span>
          <strong>{mitigatedCount}</strong>
        </div>
        <div className="card playbook-overview-card" title={t('Incidents fully closed or resolved by a rollback.')}>
          <span>{t('Closed / Rolled Back')}</span>
          <strong>{closedCount}</strong>
        </div>
      </div>

      <div className="card" style={{ padding: '1rem', marginBottom: '1rem' }}>
        <div className="playbook-filter-row">
          <input
            type="text"
            value={search}
            onChange={(event) => setSearch(event.target.value)}
            placeholder={t('Search by title, summary, impact, environment, or ID...')}
            title={t('Filters the list to incidents whose title, summary, impact, environment, or ID contains this text.')}
          />
          <select aria-label={t('All statuses')} title={t('Show only incidents in this state.')} value={statusFilter} onChange={(event) => setStatusFilter(event.target.value as typeof statusFilter)}>
            <option value="all">{t('All statuses')}</option>
            {INCIDENT_STATUSES.map((status) => (
              <option key={status} value={status}>{status}</option>
            ))}
          </select>
          <select aria-label={t('All severities')} title={t('Show only incidents of this severity.')} value={severityFilter} onChange={(event) => setSeverityFilter(event.target.value as typeof severityFilter)}>
            <option value="all">{t('All severities')}</option>
            {INCIDENT_SEVERITIES.map((severity) => (
              <option key={severity} value={severity}>{severity}</option>
            ))}
          </select>
        </div>
      </div>

      <DataTable
        tableKey="incidents"
        columns={columns}
        rows={filtered}
        rowKey={(incident) => incident.id}
        onRowClick={(incident) => setViewRecord(incident as unknown as Record<string, unknown>)}
        autoRefresh={{ seconds: refreshSeconds, onChange: setRefreshSeconds }}
        onRefresh={load}
        refreshTitle={t('Refresh incidents')}
        placeholder={loading && incidents.length === 0 ? <p className="text-dim">{t('Loading...')}</p> : filtered.length === 0 ? (
          <div className="playbook-empty-state">
            <strong>{t('No incidents match the current filters.')}</strong>
            <span>{canManage ? t('Create incidents from deployments or environments to preserve hotfix and rollback context.') : t('Ask a tenant administrator to create and manage incident records.')}</span>
          </div>
        ) : undefined}
      />
    </div>
  );
}
