import { useEffect, useState, useCallback } from 'react';
import { useParams, useNavigate, Link } from 'react-router-dom';
import { listFleets, listVessels, listPipelines, updateFleet, deleteFleet } from '../api/client';
import type { Fleet, Vessel, Pipeline } from '../types/models';
import ActionMenu from '../components/shared/ActionMenu';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import JsonViewer from '../components/shared/JsonViewer';
import CopyButton from '../components/shared/CopyButton';
import ErrorModal from '../components/shared/ErrorModal';
import { useLocale } from '../context/LocaleContext';
import { useNotifications } from '../context/NotificationContext';

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

  if (loading) return <p className="text-dim">{t('Loading...')}</p>;
  if (error && !fleet) return <ErrorModal error={error} onClose={() => setError('')} />;
  if (!fleet) return <p className="text-dim">{t('Fleet not found.')}</p>;

  return (
    <div>
      {/* Breadcrumb */}
      <div className="breadcrumb">
        <Link to="/fleets">{t('Fleets')}</Link> <span className="breadcrumb-sep">&gt;</span> <span>{fleet.name}</span>
      </div>

      <div className="detail-header">
        <h2>{fleet.name}</h2>
        <div className="inline-actions">
          <ActionMenu id={`fleet-${fleet.id}`} items={[
            { label: 'Edit', onClick: openEdit },
            { label: 'View JSON', onClick: () => setJsonData({ open: true, title: t('Fleet: {{name}}', { name: fleet.name }), data: fleet }) },
            { label: 'Delete', danger: true, onClick: handleDelete },
          ]} />
        </div>
      </div>

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
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th title={t('Vessel name and unique identifier')}>{t('Name')}</th>
                  <th title={t('Git repository URL')}>{t('Repo URL')}</th>
                  <th title={t('Default branch for merging')}>{t('Branch')}</th>
                </tr>
              </thead>
              <tbody>
                {vessels.map(v => (
                  <tr key={v.id} className="clickable" onClick={() => navigate(`/vessels/${v.id}`)}>
                    <td>
                      <strong>{v.name}</strong>
                      <div className="text-dim id-display">
                        <span className="mono">{v.id}</span>
                        <CopyButton text={v.id} />
                      </div>
                    </td>
                    <td className="text-dim vessel-repo-cell table-url-cell">
                      {v.repoUrl ? (
                        <span className="id-display">
                          <span className="url-value" title={v.repoUrl}>{v.repoUrl}</span>
                          <CopyButton text={v.repoUrl} onClick={e => e.stopPropagation()} title={t('Copy URL')} />
                        </span>
                      ) : '-'}
                    </td>
                    <td>{v.defaultBranch || 'main'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}
      {vessels.length === 0 && <p className="text-dim" style={{ marginTop: '1rem' }}>{t('No vessels in this fleet.')}</p>}
    </div>
  );
}
