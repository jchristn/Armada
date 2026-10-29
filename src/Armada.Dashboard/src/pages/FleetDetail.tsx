import { useEffect, useState, useCallback, useRef } from 'react';
import { useParams, useNavigate, Link } from 'react-router-dom';
import { getFleet, listVessels, listPipelines, createFleet, updateFleet, deleteFleet, apiErrorMessage } from '../api/client';
import { listAll } from '../api/paging';
import type { Fleet, Vessel, Pipeline } from '../types/models';
import ActionMenu from '../components/shared/ActionMenu';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import JsonViewer from '../components/shared/JsonViewer';
import PageHeader from '../components/shared/PageHeader';
import CopyButton from '../components/shared/CopyButton';
import DataTable from '../components/shared/DataTable';
import ErrorModal from '../components/shared/ErrorModal';
import { useLocale } from '../context/LocaleContext';
import { useNotifications } from '../context/NotificationContext';
import { buildFleetDuplicatePayload } from '../lib/duplicates';
import { useDeleteProgress } from '../lib/useDeleteProgress';

export default function FleetDetail() {
  const { t, formatDateTime } = useLocale();
  const { pushToast } = useNotifications();
  const deletion = useDeleteProgress();
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const [fleet, setFleet] = useState<Fleet | null>(null);
  const [vessels, setVessels] = useState<Vessel[]>([]);
  const [pipelines, setPipelines] = useState<Pipeline[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const requestGenerationRef = useRef(0);
  const loadedFleetIdRef = useRef<string | null>(null);

  // Edit modal
  const [showForm, setShowForm] = useState(false);
  const [form, setForm] = useState({ name: '', description: '', defaultPipelineId: '' });

  // JSON viewer
  const [jsonData, setJsonData] = useState<{ open: boolean; title: string; data: unknown }>({ open: false, title: '', data: null });

  // Confirm
  const [confirm, setConfirm] = useState<{ open: boolean; title: string; message: string; onConfirm: () => void }>({ open: false, title: '', message: '', onConfirm: () => {} });

  const load = useCallback(async () => {
    if (!id) return;
    const generation = ++requestGenerationRef.current;
    const routeId = id;
    const isNewRoute = loadedFleetIdRef.current !== routeId;
    const current = () => generation === requestGenerationRef.current;
    if (isNewRoute) {
      setFleet(null);
      setVessels([]);
      setError('');
    }
    setLoading(true);
    try {
      let found: Fleet | null = null;
      try {
        found = await getFleet(routeId);
      } catch {
        found = null;
      }
      if (!current()) return;
      if (!found) { setError(t('Fleet not found.')); setLoading(false); return; }
      const [vResult, pResult] = await Promise.all([listAll(listVessels, { filters: { fleetId: routeId } }), listAll(listPipelines)]);
      if (!current()) return;
      setFleet(found);
      // The server filters by fleet; keep the client check so a route that ignores the filter cannot leak others.
      setVessels(vResult.objects.filter(v => v.fleetId === routeId));
      setPipelines(pResult.objects);
      loadedFleetIdRef.current = routeId;
    } catch {
      if (!current()) return;
      setError(t('Failed to load fleet.'));
    } finally {
      if (current()) setLoading(false);
    }
  }, [id, t]);

  useEffect(() => { load(); }, [load]);

  function openEdit() {
    if (!fleet) return;
    setForm({ name: fleet.name, description: fleet.description ?? '', defaultPipelineId: fleet.defaultPipelineId ?? '' });
    setShowForm(true);
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!fleet) return;
    try {
      await updateFleet(fleet.id, form);
      setShowForm(false);
      pushToast('success', t('Fleet "{{name}}" saved.', { name: form.name }));
      load();
    } catch (err: unknown) { setError(apiErrorMessage(err, t('Save failed.'))); }
  }

  function handleDelete() {
    if (!fleet) return;
    setConfirm({
      open: true,
      title: t('Delete Fleet'),
      message: t('Delete fleet "{{name}}"? This cannot be undone.', { name: fleet.name }),
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        if (!(await deletion.run({ kind: 'fleet', action: () => deleteFleet(fleet.id), errorMessage: (err) => apiErrorMessage(err, t('Delete failed.')) }))) return;
        pushToast('warning', t('Fleet "{{name}}" deleted.', { name: fleet.name }));
        navigate('/fleets');
      },
    });
  }

  async function handleDuplicate() {
    if (!fleet) return;
    try {
      const created = await createFleet(buildFleetDuplicatePayload(fleet));
      pushToast('success', t('Fleet "{{name}}" duplicated.', { name: created.name }));
      navigate(`/fleets/${created.id}`);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Duplicate failed.'));
    }
  }

  if (loading) return <p className="text-dim">{t('Loading...')}</p>;
  if (error && !fleet) return <ErrorModal error={error} onClose={() => setError('')} />;
  if (!fleet) return <p className="text-dim">{t('Fleet not found.')}</p>;

  return (
    <div>
      <PageHeader
        breadcrumb={
          <>
            <Link to="/fleets" title={t('Back to the list of all fleets.')}>{t('Fleets')}</Link> <span className="breadcrumb-sep">&gt;</span> <span>{fleet.name}</span>
          </>
        }
        title={fleet.name}
        actions={
          <>
            <ActionMenu id={`fleet-${fleet.id}`} items={[
              { label: 'Edit', title: 'Change the fleet name, description or default pipeline.', onClick: openEdit },
              { label: 'Duplicate', title: 'Create a new fleet with the same description and default pipeline. Vessels are not copied.', onClick: () => void handleDuplicate() },
              { label: 'View JSON', title: 'Show the raw fleet record as JSON.', onClick: () => setJsonData({ open: true, title: t('Fleet: {{name}}', { name: fleet.name }), data: fleet }) },
              { label: 'Delete', title: 'Delete the fleet after you confirm. Its vessels are kept and left without a fleet.', danger: true, onClick: handleDelete },
            ]} />
          </>
        }
      />

      <ErrorModal error={error} onClose={() => setError('')} />

      {/* Edit Modal */}
      {showForm && (
        <div className="modal-overlay" onClick={() => setShowForm(false)}>
          <form className="modal" onClick={e => e.stopPropagation()} onSubmit={handleSubmit}>
            <h3>{t('Edit Fleet')}</h3>
            <label title={t('Fleet name shown across Armada and used to pick the fleet in dispatch, e.g. Payments or Web Frontends. Required.')}>{t('Name')}<input title={t('Fleet name shown across Armada and used to pick the fleet in dispatch, e.g. Payments or Web Frontends. Required.')} value={form.name} onChange={e => setForm({ ...form, name: e.target.value })} required /></label>
            <label title={t('Optional note about what the repositories in this fleet have in common. Shown in the fleet list.')}>{t('Description')}<input title={t('Optional note about what the repositories in this fleet have in common. Shown in the fleet list.')} value={form.description} onChange={e => setForm({ ...form, description: e.target.value })} /></label>
            <label title={t('Pipeline used for dispatches to vessels in this fleet when the vessel itself has no default pipeline. None means a single Worker stage.')}>{t('Default Pipeline')}
              <select title={t('Pipeline used for dispatches to vessels in this fleet when the vessel itself has no default pipeline. None means a single Worker stage.')} value={form.defaultPipelineId} onChange={e => setForm({ ...form, defaultPipelineId: e.target.value })}>
                <option value="">{t('None (WorkerOnly)')}</option>
                {pipelines.map(p => (
                  <option key={p.id} value={p.id}>{p.name} ({p.stages.map(s => s.personaName).join(' -> ')})</option>
                ))}
              </select>
            </label>
            <div className="modal-actions">
              <button type="submit" className="btn btn-primary" title={t('Save your changes to this fleet.')}>{t('Save')}</button>
              <button type="button" className="btn" onClick={() => setShowForm(false)} title={t('Close without saving.')}>{t('Cancel')}</button>
            </div>
          </form>
        </div>
      )}

      <JsonViewer open={jsonData.open} title={jsonData.title} data={jsonData.data} onClose={() => setJsonData({ open: false, title: '', data: null })} />
      <ConfirmDialog open={confirm.open} title={confirm.title} message={confirm.message}
        onConfirm={confirm.onConfirm} onCancel={() => setConfirm(c => ({ ...c, open: false }))} />
      {deletion.dialog}

      {/* Fleet Info */}
      <div className="detail-grid">
        <div className="detail-field">
          <span className="detail-label" title={t('Fleet ID (flt_...), used by the CLI, API and MCP tools.')}>{t('ID')}</span>
          <span className="id-display">
            <span className="mono">{fleet.id}</span>
            <CopyButton text={fleet.id} />
          </span>
        </div>
        <div className="detail-field"><span className="detail-label">{t('Name')}</span><span>{fleet.name}</span></div>
        <div className="detail-field"><span className="detail-label">{t('Description')}</span><span>{fleet.description || '-'}</span></div>
        <div className="detail-field"><span className="detail-label" title={t('Pipeline used for dispatches to vessels in this fleet when the vessel itself has no default pipeline. None means a single Worker stage.')}>{t('Default Pipeline')}</span><span>{pipelines.find(p => p.id === fleet.defaultPipelineId)?.name || fleet.defaultPipelineId || <span className="text-dim">{t('None (WorkerOnly)')}</span>}</span></div>
        <div className="detail-field"><span className="detail-label" title={t('Whether the fleet is marked active. This is a label only; it does not stop dispatches to its vessels.')}>{t('Active')}</span><span>{fleet.active !== false ? t('Yes') : t('No')}</span></div>
        <div className="detail-field"><span className="detail-label">{t('Created')}</span><span title={fleet.createdUtc}>{formatDateTime(fleet.createdUtc)}</span></div>
        <div className="detail-field"><span className="detail-label" title={t('When the fleet settings were last changed.')}>{t('Last Updated')}</span><span>{formatDateTime(fleet.lastUpdateUtc)}</span></div>
      </div>

      {/* Linked Vessels */}
      {vessels.length > 0 && (
        <div>
          <h3 title={t('Repositories in this fleet. Click a row to open the vessel. Assign a vessel to a fleet from its Edit Vessel form.')}>{t('Vessels')}</h3>
          <DataTable
            tableKey="fleet-detail-vessels"
            rows={vessels}
            rowKey={(v) => v.id}
            recordCount={null}
            onRowClick={(v) => navigate(`/vessels/${v.id}`)}
            columns={[
              { key: 'actions', label: t('Actions'), fixed: true, interactive: true, render: v => <ActionMenu id={`fleet-vessel-${v.id}`} items={[
                { label: 'Open', onClick: () => navigate(`/vessels/${v.id}`) },
                { label: 'Duplicate', onClick: () => navigate(`/vessels?duplicate=${encodeURIComponent(v.id)}`) },
              ]} /> },
              {
                key: 'id', label: t('ID'), headerTitle: t('Vessel ID (vsl_...), used by the CLI, API and MCP tools.'), required: true, cellClassName: 'mono text-dim table-id-cell',
                render: (v) => (
                  <span className="id-display">
                    <span className="id-value" title={v.id}>{v.id}</span>
                    <CopyButton text={v.id} />
                  </span>
                ),
              },
              { key: 'name', label: t('Name'), required: true, headerTitle: t('Vessel name. Click a row to open the vessel.'), render: (v) => <strong>{v.name}</strong> },
              {
                key: 'repoUrl', label: t('Repo URL'), headerTitle: t('Git remote Armada clones from and pushes to for this vessel.'), cellClassName: 'text-dim vessel-repo-cell table-url-cell',
                render: (v) => v.repoUrl ? (
                  <span className="id-display">
                    <span className="url-value" title={v.repoUrl}>{v.repoUrl}</span>
                    <CopyButton text={v.repoUrl} onClick={e => e.stopPropagation()} />
                  </span>
                ) : '-',
              },
              {
                key: 'branch', label: t('Branch'), headerTitle: t('Branch missions start from and land into by default.'), cellClassName: 'mono',
                render: (v) => <span className="cell-one-line" title={v.defaultBranch || 'main'}>{v.defaultBranch || 'main'}</span>,
              },
            ]}
          />
        </div>
      )}
      {vessels.length === 0 && <p className="text-dim" style={{ marginTop: '1rem' }}>{t('No vessels in this fleet.')}</p>}
    </div>
  );
}
