import { useEffect, useState, useCallback } from 'react';
import { useParams, useNavigate, Link } from 'react-router-dom';
import { listFleets, listVessels, updateFleet, deleteFleet } from '../api/client';
import type { Fleet, Vessel } from '../types/models';
import ActionMenu from '../components/shared/ActionMenu';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import JsonViewer from '../components/shared/JsonViewer';
import CopyButton, { copyToClipboard } from '../components/shared/CopyButton';
import ErrorModal from '../components/shared/ErrorModal';

function formatTimeAbsolute(utc: string | null): string {
  if (!utc) return '-';
  return new Date(utc).toLocaleString();
}

export default function FleetDetail() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const [fleet, setFleet] = useState<Fleet | null>(null);
  const [vessels, setVessels] = useState<Vessel[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  // Edit modal
  const [showForm, setShowForm] = useState(false);
  const [form, setForm] = useState({ name: '', description: '' });

  // JSON viewer
  const [jsonData, setJsonData] = useState<{ open: boolean; title: string; data: unknown }>({ open: false, title: '', data: null });

  // Confirm
  const [confirm, setConfirm] = useState<{ open: boolean; title: string; message: string; onConfirm: () => void }>({ open: false, title: '', message: '', onConfirm: () => {} });

  const load = useCallback(async () => {
    if (!id) return;
    try {
      setLoading(true);
      const [fResult, vResult] = await Promise.all([listFleets({ pageSize: 9999 }), listVessels({ pageSize: 9999 })]);
      const found = fResult.objects.find(f => f.id === id);
      if (!found) { setError('Fleet not found.'); setLoading(false); return; }
      setFleet(found);
      setVessels(vResult.objects.filter(v => v.fleetId === id));
      setError('');
    } catch {
      setError('Failed to load fleet.');
    } finally {
      setLoading(false);
    }
  }, [id]);

  useEffect(() => { load(); }, [load]);

  function openEdit() {
    if (!fleet) return;
    setForm({ name: fleet.name, description: fleet.description ?? '' });
    setShowForm(true);
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!fleet) return;
    try {
      await updateFleet(fleet.id, form);
      setShowForm(false);
      load();
    } catch { setError('Save failed.'); }
  }

  function handleDelete() {
    if (!fleet) return;
    setConfirm({
      open: true,
      title: 'Delete Fleet',
      message: `Delete fleet "${fleet.name}"? This cannot be undone.`,
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        try {
          await deleteFleet(fleet.id);
          navigate('/fleets');
        } catch { setError('Delete failed.'); }
      },
    });
  }

  if (loading) return <p className="text-dim">Loading...</p>;
  if (error && !fleet) return <ErrorModal error={error} onClose={() => setError('')} />;
  if (!fleet) return <p className="text-dim">Fleet not found.</p>;

  return (
    <div>
      {/* Breadcrumb */}
      <div className="breadcrumb">
        <Link to="/fleets">Fleets</Link> <span className="breadcrumb-sep">&gt;</span> <span>{fleet.name}</span>
      </div>

      <div className="detail-header">
        <h2>{fleet.name}</h2>
        <div className="inline-actions">
          <ActionMenu id={`fleet-${fleet.id}`} items={[
            { label: 'Edit', onClick: openEdit },
            { label: 'View JSON', onClick: () => setJsonData({ open: true, title: `Fleet: ${fleet.name}`, data: fleet }) },
            { label: 'Delete', danger: true, onClick: handleDelete },
          ]} />
        </div>
      </div>

      <ErrorModal error={error} onClose={() => setError('')} />

      {/* Edit Modal */}
      {showForm && (
        <div className="modal-overlay" onClick={() => setShowForm(false)}>
          <form className="modal" onClick={e => e.stopPropagation()} onSubmit={handleSubmit}>
            <h3>Edit Fleet</h3>
            <label>Name<input value={form.name} onChange={e => setForm({ ...form, name: e.target.value })} required /></label>
            <label>Description<input value={form.description} onChange={e => setForm({ ...form, description: e.target.value })} /></label>
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

      {/* Fleet Info */}
      <div className="detail-grid">
        <div className="detail-field">
          <span className="detail-label">ID</span>
          <span className="id-display">
            <span className="mono">{fleet.id}</span>
            <button className="copy-btn" onClick={() => copyToClipboard(fleet.id)} title="Copy ID" />
          </span>
        </div>
        <div className="detail-field"><span className="detail-label">Name</span><span>{fleet.name}</span></div>
        <div className="detail-field"><span className="detail-label">Description</span><span>{fleet.description || '-'}</span></div>
        <div className="detail-field"><span className="detail-label">Active</span><span>{fleet.active !== false ? 'Yes' : 'No'}</span></div>
        <div className="detail-field"><span className="detail-label">Created</span><span title={fleet.createdUtc}>{formatTimeAbsolute(fleet.createdUtc)}</span></div>
        <div className="detail-field"><span className="detail-label">Last Updated</span><span>{formatTimeAbsolute(fleet.lastUpdateUtc)}</span></div>
      </div>

      {/* Linked Vessels */}
      {vessels.length > 0 && (
        <div>
          <h3>Vessels</h3>
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th title="Vessel name and unique identifier">Name</th>
                  <th title="Git repository URL">Repo URL</th>
                  <th title="Default branch for merging">Branch</th>
                </tr>
              </thead>
              <tbody>
                {vessels.map(v => (
                  <tr key={v.id} className="clickable" onClick={() => navigate(`/vessels/${v.id}`)}>
                    <td>
                      <strong>{v.name}</strong>
                      <div className="text-dim id-display">
                        <span className="mono">{v.id}</span>
                        <button className="copy-btn" onClick={e => { e.stopPropagation(); copyToClipboard(v.id); }} title="Copy ID" />
                      </div>
                    </td>
                    <td className="text-dim vessel-repo-cell table-url-cell">
                      {v.repoUrl ? (
                        <span className="id-display">
                          <span className="url-value" title={v.repoUrl}>{v.repoUrl}</span>
                          <CopyButton text={v.repoUrl} onClick={e => e.stopPropagation()} title="Copy URL" />
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
      {vessels.length === 0 && <p className="text-dim" style={{ marginTop: '1rem' }}>No vessels in this fleet.</p>}
    </div>
  );
}
