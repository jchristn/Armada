import { useEffect, useState, useCallback, useMemo } from 'react';
import { useParams, useNavigate, Link } from 'react-router-dom';
import { listVessels, listFleets, listMissions, listPipelines, updateVessel, deleteVessel } from '../api/client';
import type { Fleet, Vessel, Mission, Pipeline } from '../types/models';
import ActionMenu from '../components/shared/ActionMenu';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import JsonViewer from '../components/shared/JsonViewer';
import StatusBadge from '../components/shared/StatusBadge';
import CopyButton from '../components/shared/CopyButton';
import ErrorModal from '../components/shared/ErrorModal';
import { useLocale } from '../context/LocaleContext';
import { useNotifications } from '../context/NotificationContext';

interface VesselForm {
  name: string;
  fleetId: string;
  repoUrl: string;
  defaultBranch: string;
  localPath: string;
  workingDirectory: string;
  projectContext: string;
  styleGuide: string;
  enableModelContext: boolean;
  modelContext: string;
  defaultPipelineId: string;
}

export default function VesselDetail() {
  const { t, formatDateTime, formatRelativeTime } = useLocale();
  const { pushToast } = useNotifications();
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const [vessel, setVessel] = useState<Vessel | null>(null);
  const [fleets, setFleets] = useState<Fleet[]>([]);
  const [missions, setMissions] = useState<Mission[]>([]);
  const [pipelines, setPipelines] = useState<Pipeline[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  // Edit modal
  const [showForm, setShowForm] = useState(false);
  const [form, setForm] = useState<VesselForm>({ name: '', fleetId: '', repoUrl: '', defaultBranch: 'main', localPath: '', workingDirectory: '', projectContext: '', styleGuide: '', enableModelContext: true, modelContext: '', defaultPipelineId: '' });

  // JSON viewer
  const [jsonData, setJsonData] = useState<{ open: boolean; title: string; data: unknown }>({ open: false, title: '', data: null });

  // Confirm
  const [confirm, setConfirm] = useState<{ open: boolean; title: string; message: string; onConfirm: () => void }>({ open: false, title: '', message: '', onConfirm: () => {} });

  const fleetMap = useMemo(() => {
    const m = new Map<string, string>();
    for (const f of fleets) m.set(f.id, f.name);
    return m;
  }, [fleets]);

  const load = useCallback(async () => {
    if (!id) return;
    try {
      setLoading(true);
      const isInitialLoad = !vessel;
      const [vResult, fResult, mResult, pResult] = await Promise.all([listVessels({ pageSize: 9999 }), listFleets({ pageSize: 9999 }), listMissions({ pageSize: 9999 }), listPipelines({ pageSize: 9999 })]);
      const found = vResult.objects.find(v => v.id === id);
      if (!found) { setError(t('Vessel not found.')); setLoading(false); return; }
      setVessel(found);
      setFleets(fResult.objects);
      setMissions(mResult.objects.filter(m => m.vesselId === id));
      setPipelines(pResult.objects);
      if (isInitialLoad) setError('');
    } catch {
      setError(t('Failed to load vessel.'));
    } finally {
      setLoading(false);
    }
  }, [id, t]);

  useEffect(() => { load(); }, [load]);

  function openEdit() {
    if (!vessel) return;
    setForm({
      name: vessel.name,
      fleetId: vessel.fleetId ?? '',
      repoUrl: vessel.repoUrl ?? '',
      defaultBranch: vessel.defaultBranch || 'main',
      localPath: vessel.localPath ?? '',
      workingDirectory: vessel.workingDirectory ?? '',
      projectContext: vessel.projectContext ?? '',
      styleGuide: vessel.styleGuide ?? '',
      enableModelContext: vessel.enableModelContext,
      modelContext: vessel.modelContext ?? '',
      defaultPipelineId: vessel.defaultPipelineId ?? '',
    });
    setShowForm(true);
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!vessel) return;
    try {
      const payload: Record<string, unknown> = { ...form };
      if (!payload.localPath) delete payload.localPath;
      if (!payload.workingDirectory) delete payload.workingDirectory;
      if (!payload.projectContext) delete payload.projectContext;
      if (!payload.styleGuide) delete payload.styleGuide;
      if (!payload.modelContext) delete payload.modelContext;
      if (!payload.defaultPipelineId) delete payload.defaultPipelineId;
      await updateVessel(vessel.id, payload);
      setShowForm(false);
      pushToast('success', t('Vessel "{{name}}" saved.', { name: form.name }));
      load();
    } catch { setError(t('Save failed.')); }
  }

  function handleDelete() {
    if (!vessel) return;
    setConfirm({
      open: true,
      title: t('Delete Vessel'),
      message: t('Delete vessel "{{name}}"? This cannot be undone.', { name: vessel.name }),
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        try {
          await deleteVessel(vessel.id);
          pushToast('warning', t('Vessel "{{name}}" deleted.', { name: vessel.name }));
          navigate('/vessels');
        } catch { setError(t('Delete failed.')); }
      },
    });
  }

  if (loading) return <p className="text-dim">{t('Loading...')}</p>;
  if (error && !vessel) return <ErrorModal error={error} onClose={() => setError('')} />;
  if (!vessel) return <p className="text-dim">{t('Vessel not found.')}</p>;

  return (
    <div>
      {/* Breadcrumb */}
      <div className="breadcrumb">
        <Link to="/vessels">{t('Vessels')}</Link> <span className="breadcrumb-sep">&gt;</span> <span>{vessel.name}</span>
      </div>

      <div className="detail-header">
        <h2>{vessel.name}</h2>
        <div className="inline-actions">
          <ActionMenu id={`vessel-${vessel.id}`} items={[
            { label: 'Edit', onClick: openEdit },
            { label: 'View JSON', onClick: () => setJsonData({ open: true, title: t('Vessel: {{name}}', { name: vessel.name }), data: vessel }) },
            { label: 'Delete', danger: true, onClick: handleDelete },
          ]} />
        </div>
      </div>

      <ErrorModal error={error} onClose={() => setError('')} />

      {/* Edit Modal */}
      {showForm && (
        <div className="modal-overlay" onClick={() => setShowForm(false)}>
          <form className="modal modal-lg" onClick={e => e.stopPropagation()} onSubmit={handleSubmit}>
            <h3>{t('Edit Vessel')}</h3>
            <label>{t('Name')}<input value={form.name} onChange={e => setForm({ ...form, name: e.target.value })} required /></label>
            <label>{t('Fleet')}
              <select value={form.fleetId} onChange={e => setForm({ ...form, fleetId: e.target.value })} required>
                <option value="">{t('Select a fleet...')}</option>
                {fleets.map(f => <option key={f.id} value={f.id}>{f.name}</option>)}
              </select>
            </label>
            <label>{t('Repository URL')}<input value={form.repoUrl} onChange={e => setForm({ ...form, repoUrl: e.target.value })} required placeholder={t('https://github.com/org/repo.git')} /></label>
            <label>{t('Default Branch')}<input value={form.defaultBranch} onChange={e => setForm({ ...form, defaultBranch: e.target.value })} /></label>
            <label>{t('Local Path')}<input value={form.localPath} onChange={e => setForm({ ...form, localPath: e.target.value })} /></label>
            <label>{t('Working Directory')}<input value={form.workingDirectory} onChange={e => setForm({ ...form, workingDirectory: e.target.value })} /></label>
            <label>
              {t('Project Context')}
              <textarea value={form.projectContext} onChange={e => setForm({ ...form, projectContext: e.target.value })} rows={4} />
              <span className="text-dim" style={{ fontSize: '0.8em' }}>{form.projectContext.length} {t('characters')}</span>
            </label>
            <label>
              {t('Style Guide')}
              <textarea value={form.styleGuide} onChange={e => setForm({ ...form, styleGuide: e.target.value })} rows={4} />
              <span className="text-dim" style={{ fontSize: '0.8em' }}>{form.styleGuide.length} {t('characters')}</span>
            </label>
            <label>{t('Default Pipeline')}
              <select value={form.defaultPipelineId} onChange={e => setForm({ ...form, defaultPipelineId: e.target.value })}>
                <option value="">{t('None (WorkerOnly)')}</option>
                {pipelines.map(p => (
                  <option key={p.id} value={p.id}>{p.name}</option>
                ))}
              </select>
            </label>
            <label style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
              <input type="checkbox" checked={form.enableModelContext} onChange={e => setForm({ ...form, enableModelContext: e.target.checked })} style={{ width: 'auto' }} />
              {t('Enable Model Context')}
            </label>
            {form.enableModelContext && (
              <label>
                {t('Model Context')}
                <textarea value={form.modelContext} onChange={e => setForm({ ...form, modelContext: e.target.value })} rows={4} placeholder={t('Agent-accumulated context will appear here after missions run with model context enabled...')} />
                <span className="text-dim" style={{ fontSize: '0.8em' }}>{form.modelContext.length} {t('characters')}</span>
              </label>
            )}
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

      {/* Vessel Info */}
      <div className="detail-grid">
        <div className="detail-field">
          <span className="detail-label">{t('ID')}</span>
          <span className="id-display">
            <span className="mono">{vessel.id}</span>
            <CopyButton text={vessel.id} />
          </span>
        </div>
        <div className="detail-field"><span className="detail-label">{t('Name')}</span><span>{vessel.name}</span></div>
        <div className="detail-field">
          <span className="detail-label">{t('Fleet')}</span>
          {vessel.fleetId ? (
            <Link to={`/fleets/${vessel.fleetId}`}>{fleetMap.get(vessel.fleetId) ?? vessel.fleetId}</Link>
          ) : <span>-</span>}
        </div>
        <div className="detail-field">
          <span className="detail-label">{t('Repo URL')}</span>
          {vessel.repoUrl
            ? <a href={vessel.repoUrl} target="_blank" rel="noopener noreferrer" className="mono">{vessel.repoUrl}</a>
            : <span>-</span>}
        </div>
        <div className="detail-field"><span className="detail-label">{t('Default Branch')}</span><span>{vessel.defaultBranch || 'main'}</span></div>
        <div className="detail-field"><span className="detail-label">{t('Local Path')}</span><span className="mono" title={t('Path to the bare git repository clone used by Armada')}>{vessel.localPath || '-'}</span></div>
        <div className="detail-field"><span className="detail-label">{t('Working Directory')}</span><span className="mono" title={t('Your local checkout where completed missions are merged')}>{vessel.workingDirectory || '-'}</span></div>
        <div className="detail-field"><span className="detail-label">{t('Landing Mode')}</span><span>{vessel.landingMode || '-'}</span></div>
        <div className="detail-field"><span className="detail-label">{t('Branch Cleanup Policy')}</span><span>{vessel.branchCleanupPolicy || '-'}</span></div>
        <div className="detail-field"><span className="detail-label">{t('Allow Concurrent Missions')}</span><span>{vessel.allowConcurrentMissions ? t('Yes') : t('No')}</span></div>
        <div className="detail-field">
          <span className="detail-label">{t('Default Pipeline')}</span>
          <span>{pipelines.find(p => p.id === vessel.defaultPipelineId)?.name || vessel.defaultPipelineId || <span className="text-dim">{t('None (WorkerOnly)')}</span>}</span>
        </div>
        <div className="detail-field"><span className="detail-label">{t('Active')}</span><span>{vessel.active !== false ? t('Yes') : t('No')}</span></div>
        <div className="detail-field">
          <span className="detail-label">{t('Created')}</span>
          <span title={vessel.createdUtc}>
            {formatRelativeTime(vessel.createdUtc)}
            <span className="text-dim"> ({formatDateTime(vessel.createdUtc)})</span>
          </span>
        </div>
        <div className="detail-field">
          <span className="detail-label">{t('Last Updated')}</span>
          <span title={vessel.lastUpdateUtc}>
            {formatRelativeTime(vessel.lastUpdateUtc)}
            <span className="text-dim"> ({formatDateTime(vessel.lastUpdateUtc)})</span>
          </span>
        </div>
      </div>

      {/* Project Context */}
      {vessel.projectContext && (
        <div className="detail-context-section">
          <h4>{t('Project Context')}</h4>
          <pre className="detail-context-block">{vessel.projectContext}</pre>
        </div>
      )}

      {/* Style Guide */}
      {vessel.styleGuide && (
        <div className="detail-context-section">
          <h4>{t('Style Guide')}</h4>
          <pre className="detail-context-block">{vessel.styleGuide}</pre>
        </div>
      )}

      {/* Model Context */}
      {vessel.enableModelContext && vessel.modelContext && (
        <div className="detail-context-section">
          <h4>{t('Model Context')}</h4>
          <pre className="detail-context-block">{vessel.modelContext}</pre>
        </div>
      )}

      {/* Recent Missions */}
      <div style={{ marginTop: '1rem' }}>
        <h3>{t('Missions')}</h3>
        {missions.length > 0 ? (
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th title={t('Mission name and unique identifier')}>{t('Mission')}</th>
                  <th title={t('Current mission lifecycle state')}>{t('Status')}</th>
                  <th title={t('AI captain assigned to this mission')}>{t('Captain')}</th>
                  <th title={t('Git branch for this mission\'s work')}>{t('Branch')}</th>
                </tr>
              </thead>
              <tbody>
                {missions.map(m => (
                  <tr key={m.id} className="clickable" onClick={() => navigate(`/missions/${m.id}`)}>
                    <td>
                      <strong>{m.title}</strong>
                      <div className="text-dim id-display">
                        <span className="mono">{m.id}</span>
                        <CopyButton text={m.id} />
                      </div>
                    </td>
                    <td><StatusBadge status={m.status} /></td>
                    <td>{m.captainId || '-'}</td>
                    <td className="text-dim">{m.branchName || '-'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : (
          <p className="text-dim" style={{ marginTop: '0.5rem' }}>{t('No missions yet')}</p>
        )}
      </div>
    </div>
  );
}
