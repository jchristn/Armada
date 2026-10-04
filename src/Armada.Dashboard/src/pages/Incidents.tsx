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
import RefreshButton from '../components/shared/RefreshButton';
import StatusBadge from '../components/shared/StatusBadge';
import AutoRefreshSelect from '../components/shared/AutoRefreshSelect';
import { useAutoRefresh } from '../lib/useAutoRefresh';

const INCIDENT_STATUSES: IncidentStatus[] = ['Open', 'Monitoring', 'Mitigated', 'RolledBack', 'Closed'];
const INCIDENT_SEVERITIES: IncidentSeverity[] = ['Critical', 'High', 'Medium', 'Low'];

interface IncidentPageState {
  prefill?: Record<string, unknown>;
}

export default function Incidents() {
  const navigate = useNavigate();
  const location = useLocation();
  const { isAdmin, isTenantAdmin } = useAuth();
  const { t, formatDateTime, formatRelativeTime } = useLocale();
  const { pushToast } = useNotifications();

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
        listIncidents({ pageSize: 9999 }),
        listVessels({ pageSize: 9999 }),
        listEnvironments({ pageSize: 9999 }),
        listDeployments({ pageSize: 9999 }),
        listReleases({ pageSize: 9999 }),
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
        try {
          await deleteIncident(incident.id);
          pushToast('warning', t('Incident "{{title}}" deleted.', { title: incident.title }));
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
        title={t('Incidents')}
        subtitle={t('Track incidents, hotfix context, rollback history, recovery notes, and postmortems alongside deployments and releases.')}
        actions={(
          <>
            <AutoRefreshSelect seconds={refreshSeconds} onChange={setRefreshSeconds} />
            <RefreshButton onRefresh={load} title={t('Refresh incidents')} />
            {canManage && (
              <button className="btn btn-primary" onClick={openCreate}>
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

      {showCreate && (
        <div className="modal-overlay" onClick={() => setShowCreate(false)}>
          <form className="modal modal-large" onClick={(event) => event.stopPropagation()} onSubmit={handleCreate}>
            <h3>{t('Create Incident')}</h3>
            <label>{t('Title')}
              <input type="text" value={createForm.title} onChange={(event) => setCreateForm({ ...createForm, title: event.target.value })} required />
            </label>
            <label>{t('Status')}
              <select value={createForm.status} onChange={(event) => setCreateForm({ ...createForm, status: event.target.value as IncidentStatus })}>
                {INCIDENT_STATUSES.map((status) => (
                  <option key={status} value={status}>{status}</option>
                ))}
              </select>
            </label>
            <label>{t('Severity')}
              <select value={createForm.severity} onChange={(event) => setCreateForm({ ...createForm, severity: event.target.value as IncidentSeverity })}>
                {INCIDENT_SEVERITIES.map((severity) => (
                  <option key={severity} value={severity}>{severity}</option>
                ))}
              </select>
            </label>
            <label>{t('Vessel')}
              <select value={createForm.vesselId} onChange={(event) => setCreateForm({ ...createForm, vesselId: event.target.value })}>
                <option value="">{t('Select a vessel')}</option>
                {vessels.map((vessel) => (
                  <option key={vessel.id} value={vessel.id}>{vessel.name}</option>
                ))}
              </select>
            </label>
            <label>{t('Environment')}
              <select value={createForm.environmentId} onChange={(event) => setCreateForm({ ...createForm, environmentId: event.target.value })}>
                <option value="">{t('Select an environment')}</option>
                {environments.map((environment) => (
                  <option key={environment.id} value={environment.id}>{environment.name}</option>
                ))}
              </select>
            </label>
            <label>{t('Deployment')}
              <select value={createForm.deploymentId} onChange={(event) => setCreateForm({ ...createForm, deploymentId: event.target.value })}>
                <option value="">{t('No linked deployment')}</option>
                {deployments.map((deployment) => (
                  <option key={deployment.id} value={deployment.id}>{deployment.title}</option>
                ))}
              </select>
            </label>
            <label>{t('Release')}
              <select value={createForm.releaseId} onChange={(event) => setCreateForm({ ...createForm, releaseId: event.target.value })}>
                <option value="">{t('No linked release')}</option>
                {releases.map((release) => (
                  <option key={release.id} value={release.id}>{release.title}</option>
                ))}
              </select>
            </label>
            <label>{t('Summary')}
              <textarea rows={3} value={createForm.summary} onChange={(event) => setCreateForm({ ...createForm, summary: event.target.value })} />
            </label>
            <label>{t('Impact')}
              <textarea rows={3} value={createForm.impact} onChange={(event) => setCreateForm({ ...createForm, impact: event.target.value })} />
            </label>
            <div className="modal-actions">
              <button type="submit" className="btn btn-primary" disabled={saving}>{saving ? t('Saving...') : t('Create Incident')}</button>
              <button type="button" className="btn" onClick={() => setShowCreate(false)} disabled={saving}>{t('Cancel')}</button>
            </div>
          </form>
        </div>
      )}

      <div className="playbook-overview-grid">
        <div className="card playbook-overview-card">
          <span>{t('Total Incidents')}</span>
          <strong>{incidents.length}</strong>
        </div>
        <div className="card playbook-overview-card">
          <span>{t('Open')}</span>
          <strong>{openCount}</strong>
        </div>
        <div className="card playbook-overview-card">
          <span>{t('Monitoring')}</span>
          <strong>{monitoringCount}</strong>
        </div>
        <div className="card playbook-overview-card">
          <span>{t('Mitigated')}</span>
          <strong>{mitigatedCount}</strong>
        </div>
        <div className="card playbook-overview-card">
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
          />
          <select aria-label={t('All statuses')} value={statusFilter} onChange={(event) => setStatusFilter(event.target.value as typeof statusFilter)}>
            <option value="all">{t('All statuses')}</option>
            {INCIDENT_STATUSES.map((status) => (
              <option key={status} value={status}>{status}</option>
            ))}
          </select>
          <select aria-label={t('All severities')} value={severityFilter} onChange={(event) => setSeverityFilter(event.target.value as typeof severityFilter)}>
            <option value="all">{t('All severities')}</option>
            {INCIDENT_SEVERITIES.map((severity) => (
              <option key={severity} value={severity}>{severity}</option>
            ))}
          </select>
        </div>
      </div>

      {loading && incidents.length === 0 ? (
        <p className="text-dim">{t('Loading...')}</p>
      ) : filtered.length === 0 ? (
        <div className="playbook-empty-state">
          <strong>{t('No incidents match the current filters.')}</strong>
          <span>{canManage ? t('Create incidents from deployments or environments to preserve hotfix and rollback context.') : t('Ask a tenant administrator to create and manage incident records.')}</span>
        </div>
      ) : (
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th>{t('Incident')}</th>
                <th>{t('Status')}</th>
                <th>{t('Severity')}</th>
                <th>{t('Environment')}</th>
                <th>{t('Deployment')}</th>
                <th>{t('Release')}</th>
                <th>{t('Last Updated')}</th>
                <th className="text-right">{t('Actions')}</th>
              </tr>
              <tr className="column-filter-row">
                <td><input type="text" className="col-filter" value={colFilters.title} onChange={e => setColFilters(f => ({ ...f, title: e.target.value }))} placeholder={t('Filter...')} /></td>
                <td></td>
                <td></td>
                <td><input type="text" className="col-filter" value={colFilters.environmentName} onChange={e => setColFilters(f => ({ ...f, environmentName: e.target.value }))} placeholder={t('Filter...')} /></td>
                <td></td>
                <td></td>
                <td></td>
                <td></td>
              </tr>
            </thead>
            <tbody>
              {filtered.map((incident) => (
                <tr key={incident.id} className="clickable" onClick={() => setViewRecord(incident as unknown as Record<string, unknown>)}>
                  <td>
                    <strong>{incident.title}</strong>
                    <div className="text-dim" style={{ marginTop: '0.2rem' }}>
                      {incident.summary || incident.impact || t('No summary provided')}
                    </div>
                    <div className="mono text-dim" style={{ fontSize: '0.78rem' }}>{incident.id}</div>
                  </td>
                  <td><StatusBadge status={incident.status} /></td>
                  <td><StatusBadge status={incident.severity} /></td>
                  <td className="text-dim">{incident.environmentId ? (environmentMap.get(incident.environmentId) || incident.environmentName || incident.environmentId) : (incident.environmentName || '-')}</td>
                  <td className="text-dim">{incident.deploymentId ? (deploymentMap.get(incident.deploymentId) || incident.deploymentId) : '-'}</td>
                  <td className="text-dim">{incident.releaseId ? (releaseMap.get(incident.releaseId) || incident.releaseId) : '-'}</td>
                  <td className="text-dim" title={formatDateTime(incident.lastUpdateUtc)}>{formatRelativeTime(incident.lastUpdateUtc)}</td>
                  <td className="text-right" onClick={(event) => event.stopPropagation()}>
                    <ActionMenu
                      id={`incident-${incident.id}`}
                      items={[
                        { label: 'Open', onClick: () => navigate(`/incidents/${incident.id}`, { state: carryState }) },
                        { label: 'View JSON', onClick: () => setJsonData({ open: true, title: incident.title, data: incident }) },
                        ...(canManage ? [{ label: 'Delete', danger: true as const, onClick: () => handleDelete(incident) }] : []),
                      ]}
                    />
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
