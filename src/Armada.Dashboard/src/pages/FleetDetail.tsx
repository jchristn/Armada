import { useEffect, useState, useCallback } from 'react';
import { useParams, useNavigate, Link } from 'react-router-dom';
import { listFleets, listVessels, listPipelines, createFleet, updateFleet, deleteFleet } from '../api/client';
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

export default function FleetDetail() {
  const { t, formatDateTime } = useLocale();
  const { pushToast } = useNotifications();
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const [fleet, setFleet] = useState<Fleet | null>(null);
  const [vessels, setVessels] = useState<Vessel[]>([]);
  const [pipelines, setPipelines] = useState<Pipeline[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  // Edit modal
  const [showForm, setShowForm] = useState(false);
  const [form, setForm] = useState({ name: '', description: '', defaultPipelineId: '' });

  // JSON viewer
  const [jsonData, setJsonData] = useState<{ open: boolean; title: string; data: unknown }>({ open: false, title: '', data: null });

  // Confirm
  const [confirm, setConfirm] = useState<{ open: boolean; title: string; message: string; onConfirm: () => void }>({ open: false, title: '', message: '', onConfirm: () => {} });

  const load = useCallback(async () => {
    if (!id) return;
    try {
      setLoading(true);
      const isInitialLoad = !fleet;
      const [fResult, vResult, pResult] = await Promise.all([listFleets({ pageSize: 9999 }), listVessels({ pageSize: 9999 }), listPipelines({ pageSize: 9999 })]);
      const found = fResult.objects.find((fleetItem) => fleetItem.id === id);
      if (!found) { setError(t('Fleet not found.')); setLoading(false); return; }
      setFleet(found);
      setVessels(vResult.objects.filter(v => v.fleetId === id));
      setPipelines(pResult.objects);
      if (isInitialLoad) setError('');
    } catch {
      setError(t('Failed to load fleet.'));
    } finally {
      setLoading(false);
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
    } catch { setError(t('Save failed.')); }
  }

  function handleDelete() {
    if (!fleet) return;
    setConfirm({
      open: true,
      title: t('Delete Fleet'),
      message: t('Delete fleet "{{name}}"? This cannot be undone.', { name: fleet.name }),
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        try {
          await deleteFleet(fleet.id);
          pushToast('warning', t('Fleet "{{name}}" deleted.', { name: fleet.name }));
          navigate('/fleets');
        } catch { setError(t('Delete failed.')); }
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
            <Link to="/fleets">{t('Fleets')}</Link> <span className="breadcrumb-sep">&gt;</span> <span>{fleet.name}</span>
          </>
        }
        title={fleet.name}
        actions={
          <>
            <ActionMenu id={`fleet-${fleet.id}`} items={[
              { label: 'Edit', onClick: openEdit },
              { label: 'Duplicate', onClick: () => void handleDuplicate() },
              { label: 'View JSON', onClick: () => setJsonData({ open: true, title: t('Fleet: {{name}}', { name: fleet.name }), data: fleet }) },
              { label: 'Delete', danger: true, onClick: handleDelete },
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
            <label>{t('Name')}<input value={form.name} onChange={e => setForm({ ...form, name: e.target.value })} required /></label>
            <label>{t('Description')}<input value={form.description} onChange={e => setForm({ ...form, description: e.target.value })} /></label>
            <label>{t('Default Pipeline')}
              <select value={form.defaultPipelineId} onChange={e => setForm({ ...form, defaultPipelineId: e.target.value })}>
                <option value="">{t('None (WorkerOnly)')}</option>
                {pipelines.map(p => (
                  <option key={p.id} value={p.id}>{p.name} ({p.stages.map(s => s.personaName).join(' -> ')})</option>
                ))}
              </select>
            </label>
            <div className="modal-actions">
              <button type="submit" className="btn btn-primary">{t('Save')}</button>
              <button type="button" className="btn" onClick={() => setShowForm(false)}>{t('Cancel')}</button>
            </div>
          </form>
        </div>
      )}

      <JsonViewer open={jsonData.open} title={jsonData.title} data={jsonData.data} onClose={() => setJsonData({ open: false, title: '', data: null })} />
      <ConfirmDialog open={confirm.open} title={confirm.title} message={confirm.message}
        onConfirm={confirm.onConfirm} onCancel={() => setConfirm(c => ({ ...c, open: false }))} />

      {/* Fleet Info */}
      <div className="detail-grid">
        <div className="detail-field">
          <span className="detail-label">{t('ID')}</span>
          <span className="id-display">
            <span className="mono">{fleet.id}</span>
            <CopyButton text={fleet.id} />
          </span>
        </div>
        <div className="detail-field"><span className="detail-label">{t('Name')}</span><span>{fleet.name}</span></div>
        <div className="detail-field"><span className="detail-label">{t('Description')}</span><span>{fleet.description || '-'}</span></div>
        <div className="detail-field"><span className="detail-label">{t('Default Pipeline')}</span><span>{pipelines.find(p => p.id === fleet.defaultPipelineId)?.name || fleet.defaultPipelineId || <span className="text-dim">{t('None (WorkerOnly)')}</span>}</span></div>
        <div className="detail-field"><span className="detail-label">{t('Active')}</span><span>{fleet.active !== false ? t('Yes') : t('No')}</span></div>
        <div className="detail-field"><span className="detail-label">{t('Created')}</span><span title={fleet.createdUtc}>{formatDateTime(fleet.createdUtc)}</span></div>
        <div className="detail-field"><span className="detail-label">{t('Last Updated')}</span><span>{formatDateTime(fleet.lastUpdateUtc)}</span></div>
      </div>

      {/* Linked Vessels */}
      {vessels.length > 0 && (
        <div>
          <h3>{t('Vessels')}</h3>
          <DataTable
            tableKey="fleet-detail-vessels"
            rows={vessels}
            rowKey={(v) => v.id}
            recordCount={null}
            onRowClick={(v) => navigate(`/vessels/${v.id}`)}
            columns={[
              { key: 'name', label: t('Name'), required: true, headerTitle: t('Vessel name and unique identifier'), render: (v) => <strong>{v.name}</strong> },
              {
                key: 'id', label: t('ID'), required: true, cellClassName: 'mono text-dim table-id-cell',
                render: (v) => (
                  <span className="id-display">
                    <span className="id-value" title={v.id}>{v.id}</span>
                    <CopyButton text={v.id} />
                  </span>
                ),
              },
              {
                key: 'repoUrl', label: t('Repo URL'), headerTitle: t('Git repository URL'), cellClassName: 'text-dim vessel-repo-cell table-url-cell',
                render: (v) => v.repoUrl ? (
                  <span className="id-display">
                    <span className="url-value" title={v.repoUrl}>{v.repoUrl}</span>
                    <CopyButton text={v.repoUrl} onClick={e => e.stopPropagation()} title={t('Copy URL')} />
                  </span>
                ) : '-',
              },
              {
                key: 'branch', label: t('Branch'), headerTitle: t('Default branch for merging'), cellClassName: 'mono',
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
