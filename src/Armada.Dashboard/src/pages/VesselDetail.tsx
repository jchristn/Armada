import { useEffect, useState, useCallback, useMemo } from 'react';
import { useParams, useNavigate, Link } from 'react-router-dom';
import { listVessels, listFleets, listMissions, updateVessel, deleteVessel } from '../api/client';
import type { Fleet, Vessel, Mission } from '../types/models';
import ActionMenu from '../components/shared/ActionMenu';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import JsonViewer from '../components/shared/JsonViewer';
import StatusBadge from '../components/shared/StatusBadge';
import { copyToClipboard } from '../components/shared/CopyButton';
import ErrorModal from '../components/shared/ErrorModal';

function formatTimeAbsolute(utc: string | null): string {
  if (!utc) return '-';
  return new Date(utc).toLocaleString();
}

function formatTime(utc: string | null): string {
  if (!utc) return '-';
  const d = new Date(utc);
  const now = new Date();
  const diff = now.getTime() - d.getTime();
  if (diff < 60000) return 'just now';
  if (diff < 3600000) return `${Math.floor(diff / 60000)}m ago`;
  if (diff < 86400000) return `${Math.floor(diff / 3600000)}h ago`;
  return d.toLocaleDateString();
}

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
}

export default function VesselDetail() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const [vessel, setVessel] = useState<Vessel | null>(null);
  const [fleets, setFleets] = useState<Fleet[]>([]);
  const [missions, setMissions] = useState<Mission[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  // Edit modal
  const [showForm, setShowForm] = useState(false);
  const [form, setForm] = useState<VesselForm>({ name: '', fleetId: '', repoUrl: '', defaultBranch: 'main', localPath: '', workingDirectory: '', projectContext: '', styleGuide: '', enableModelContext: true, modelContext: '' });

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
      const [vResult, fResult, mResult] = await Promise.all([listVessels({ pageSize: 9999 }), listFleets({ pageSize: 9999 }), listMissions({ pageSize: 9999 })]);
      const found = vResult.objects.find(v => v.id === id);
      if (!found) { setError('Vessel not found.'); setLoading(false); return; }
      setVessel(found);
      setFleets(fResult.objects);
      setMissions(mResult.objects.filter(m => m.vesselId === id));
      setError('');
    } catch {
      setError('Failed to load vessel.');
    } finally {
      setLoading(false);
    }
  }, [id]);

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
      await updateVessel(vessel.id, payload);
      setShowForm(false);
      load();
    } catch { setError('Save failed.'); }
  }

  function handleDelete() {
    if (!vessel) return;
    setConfirm({
      open: true,
      title: 'Delete Vessel',
      message: `Delete vessel "${vessel.name}"? This cannot be undone.`,
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        try {
          await deleteVessel(vessel.id);
          navigate('/vessels');
        } catch { setError('Delete failed.'); }
      },
    });
  }

  if (loading) return <p className="text-dim">Loading...</p>;
  if (error && !vessel) return <ErrorModal error={error} onClose={() => setError('')} />;
  if (!vessel) return <p className="text-dim">Vessel not found.</p>;

  return (
    <div>
      {/* Breadcrumb */}
      <div className="breadcrumb">
        <Link to="/vessels">Vessels</Link> <span className="breadcrumb-sep">&gt;</span> <span>{vessel.name}</span>
      </div>

      <div className="detail-header">
        <h2>{vessel.name}</h2>
        <div className="inline-actions">
          <ActionMenu id={`vessel-${vessel.id}`} items={[
            { label: 'Edit', onClick: openEdit },
            { label: 'View JSON', onClick: () => setJsonData({ open: true, title: `Vessel: ${vessel.name}`, data: vessel }) },
            { label: 'Delete', danger: true, onClick: handleDelete },
          ]} />
        </div>
      </div>

      <ErrorModal error={error} onClose={() => setError('')} />

      {/* Edit Modal */}
      {showForm && (
        <div className="modal-overlay" onClick={() => setShowForm(false)}>
          <form className="modal modal-lg" onClick={e => e.stopPropagation()} onSubmit={handleSubmit}>
            <h3>Edit Vessel</h3>
            <label>Name<input value={form.name} onChange={e => setForm({ ...form, name: e.target.value })} required /></label>
            <label>Fleet
              <select value={form.fleetId} onChange={e => setForm({ ...form, fleetId: e.target.value })} required>
                <option value="">Select a fleet...</option>
                {fleets.map(f => <option key={f.id} value={f.id}>{f.name}</option>)}
              </select>
            </label>
            <label>Repo URL<input value={form.repoUrl} onChange={e => setForm({ ...form, repoUrl: e.target.value })} /></label>
            <label>Default Branch<input value={form.defaultBranch} onChange={e => setForm({ ...form, defaultBranch: e.target.value })} /></label>
            <label>Local Path<input value={form.localPath} onChange={e => setForm({ ...form, localPath: e.target.value })} /></label>
            <label>Working Directory<input value={form.workingDirectory} onChange={e => setForm({ ...form, workingDirectory: e.target.value })} /></label>
            <label>
              Project Context
              <textarea value={form.projectContext} onChange={e => setForm({ ...form, projectContext: e.target.value })} rows={4} />
              <span className="text-dim" style={{ fontSize: '0.8em' }}>{form.projectContext.length} characters</span>
            </label>
            <label>
              Style Guide
              <textarea value={form.styleGuide} onChange={e => setForm({ ...form, styleGuide: e.target.value })} rows={4} />
              <span className="text-dim" style={{ fontSize: '0.8em' }}>{form.styleGuide.length} characters</span>
            </label>
            <label style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
              <input type="checkbox" checked={form.enableModelContext} onChange={e => setForm({ ...form, enableModelContext: e.target.checked })} style={{ width: 'auto' }} />
              Enable Model Context
            </label>
            {form.enableModelContext && (
              <label>
                Model Context
                <textarea value={form.modelContext} onChange={e => setForm({ ...form, modelContext: e.target.value })} rows={4} placeholder="Agent-accumulated context will appear here after missions run with model context enabled..." />
                <span className="text-dim" style={{ fontSize: '0.8em' }}>{form.modelContext.length} characters</span>
              </label>
            )}
            <div className="modal-actions">
              <button type="submit" className="btn btn-primary">Save</button>
              <button type="button" className="btn" onClick={() => setShowForm(false)}>Cancel</button>
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
          <span className="detail-label">ID</span>
          <span className="id-display">
            <span className="mono">{vessel.id}</span>
            <button className="copy-btn" onClick={() => copyToClipboard(vessel.id)} title="Copy ID" />
          </span>
        </div>
        <div className="detail-field"><span className="detail-label">Name</span><span>{vessel.name}</span></div>
        <div className="detail-field">
          <span className="detail-label">Fleet</span>
          {vessel.fleetId ? (
            <Link to={`/fleets/${vessel.fleetId}`}>{fleetMap.get(vessel.fleetId) ?? vessel.fleetId}</Link>
          ) : <span>-</span>}
        </div>
        <div className="detail-field">
          <span className="detail-label">Repo URL</span>
          {vessel.repoUrl
            ? <a href={vessel.repoUrl} target="_blank" rel="noopener noreferrer" className="mono">{vessel.repoUrl}</a>
            : <span>-</span>}
        </div>
        <div className="detail-field"><span className="detail-label">Default Branch</span><span>{vessel.defaultBranch || 'main'}</span></div>
        <div className="detail-field"><span className="detail-label">Local Path</span><span className="mono" title="Path to the bare git repository clone used by Armada">{vessel.localPath || '-'}</span></div>
        <div className="detail-field"><span className="detail-label">Working Directory</span><span className="mono" title="Your local checkout where completed missions are merged">{vessel.workingDirectory || '-'}</span></div>
        <div className="detail-field"><span className="detail-label">Landing Mode</span><span>{vessel.landingMode || '-'}</span></div>
        <div className="detail-field"><span className="detail-label">Branch Cleanup Policy</span><span>{vessel.branchCleanupPolicy || '-'}</span></div>
        <div className="detail-field"><span className="detail-label">Allow Concurrent Missions</span><span>{vessel.allowConcurrentMissions ? 'Yes' : 'No'}</span></div>
        <div className="detail-field"><span className="detail-label">Active</span><span>{vessel.active !== false ? 'Yes' : 'No'}</span></div>
        <div className="detail-field">
          <span className="detail-label">Created</span>
          <span title={vessel.createdUtc}>
            {formatTime(vessel.createdUtc)}
            <span className="text-dim"> ({formatTimeAbsolute(vessel.createdUtc)})</span>
          </span>
        </div>
        <div className="detail-field">
          <span className="detail-label">Last Updated</span>
          <span title={vessel.lastUpdateUtc}>
            {formatTime(vessel.lastUpdateUtc)}
            <span className="text-dim"> ({formatTimeAbsolute(vessel.lastUpdateUtc)})</span>
          </span>
        </div>
      </div>

      {/* Project Context */}
      {vessel.projectContext && (
        <div className="detail-context-section">
          <h4>Project Context</h4>
          <pre className="detail-context-block">{vessel.projectContext}</pre>
        </div>
      )}

      {/* Style Guide */}
      {vessel.styleGuide && (
        <div className="detail-context-section">
          <h4>Style Guide</h4>
          <pre className="detail-context-block">{vessel.styleGuide}</pre>
        </div>
      )}

      {/* Model Context */}
      {vessel.enableModelContext && vessel.modelContext && (
        <div className="detail-context-section">
          <h4>Model Context</h4>
          <pre className="detail-context-block">{vessel.modelContext}</pre>
        </div>
      )}

      {/* Recent Missions */}
      <div style={{ marginTop: '1rem' }}>
        <h3>Missions</h3>
        {missions.length > 0 ? (
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th title="Mission name and unique identifier">Mission</th>
                  <th title="Current mission lifecycle state">Status</th>
                  <th title="AI captain assigned to this mission">Captain</th>
                  <th title="Git branch for this mission's work">Branch</th>
                </tr>
              </thead>
              <tbody>
                {missions.map(m => (
                  <tr key={m.id} className="clickable" onClick={() => navigate(`/missions/${m.id}`)}>
                    <td>
                      <strong>{m.title}</strong>
                      <div className="text-dim id-display">
                        <span className="mono">{m.id}</span>
                        <button className="copy-btn" onClick={e => { e.stopPropagation(); copyToClipboard(m.id); }} title="Copy ID" />
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
          <p className="text-dim" style={{ marginTop: '0.5rem' }}>No missions yet</p>
        )}
      </div>
    </div>
  );
}
