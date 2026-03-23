import { useEffect, useState, useCallback } from 'react';
import { useParams, useNavigate, Link } from 'react-router-dom';
import {
  getCaptain,
  getCaptainLog,
  stopCaptain,
  recallCaptain,
  getMission,
  listMissions,
  updateCaptain,
  deleteCaptain,
} from '../api/client';
import type { Captain, Mission, LogResult } from '../types/models';
import ActionMenu from '../components/shared/ActionMenu';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import ErrorModal from '../components/shared/ErrorModal';
import JsonViewer from '../components/shared/JsonViewer';
import StatusBadge from '../components/shared/StatusBadge';
import { copyToClipboard } from '../components/shared/CopyButton';

const RUNTIMES = ['ClaudeCode', 'Codex', 'Gemini', 'Cursor', 'Custom'];

function formatTimeAbsolute(utc: string | null): string {
  if (!utc) return '-';
  return new Date(utc).toLocaleString();
}

function formatTimeRelative(utc: string | null): string {
  if (!utc) return '';
  const d = new Date(utc);
  const now = new Date();
  const diff = now.getTime() - d.getTime();
  if (diff < 0) return 'just now';
  if (diff < 60000) return 'just now';
  if (diff < 3600000) return `${Math.floor(diff / 60000)}m ago`;
  if (diff < 86400000) return `${Math.floor(diff / 3600000)}h ago`;
  if (diff < 2592000000) return `${Math.floor(diff / 86400000)}d ago`;
  return d.toLocaleDateString();
}

export default function CaptainDetail() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const [captain, setCaptain] = useState<Captain | null>(null);
  const [currentMission, setCurrentMission] = useState<Mission | null>(null);
  const [missions, setMissions] = useState<Mission[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  // Edit
  const [showForm, setShowForm] = useState(false);
  const [form, setForm] = useState({ name: '', runtime: 'ClaudeCode', systemInstructions: '' });

  // Log viewer
  const [logText, setLogText] = useState<string | null>(null);
  const [logLoading, setLogLoading] = useState(false);
  const [showLog, setShowLog] = useState(false);
  const [logInfo, setLogInfo] = useState('');

  // JSON viewer
  const [jsonData, setJsonData] = useState<{ open: boolean; title: string; data: unknown }>({ open: false, title: '', data: null });

  // Confirm
  const [confirm, setConfirm] = useState<{ open: boolean; title: string; message: string; onConfirm: () => void }>({ open: false, title: '', message: '', onConfirm: () => {} });

  const load = useCallback(async () => {
    if (!id) return;
    try {
      setLoading(true);
      const cap = await getCaptain(id);
      setCaptain(cap);
      // Load current mission if set
      if (cap.currentMissionId) {
        try {
          const m = await getMission(cap.currentMissionId);
          setCurrentMission(m);
        } catch { setCurrentMission(null); }
      } else {
        setCurrentMission(null);
      }
      // Load missions assigned to this captain
      try {
        const mResult = await listMissions({ pageSize: 100, filters: { captainId: id } });
        setMissions(mResult.objects || []);
      } catch { setMissions([]); }
      setError('');
    } catch {
      setError('Failed to load captain.');
    } finally {
      setLoading(false);
    }
  }, [id]);

  useEffect(() => { load(); }, [load]);

  function openEdit() {
    if (!captain) return;
    setForm({ name: captain.name, runtime: captain.runtime || 'ClaudeCode', systemInstructions: captain.systemInstructions ?? '' });
    setShowForm(true);
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!captain) return;
    try {
      const payload = { ...form } as Record<string, unknown>;
      if (!payload.systemInstructions) delete payload.systemInstructions;
      await updateCaptain(captain.id, payload);
      setShowForm(false);
      load();
    } catch { setError('Save failed.'); }
  }

  async function handleViewLog() {
    if (!id) return;
    setLogLoading(true);
    setShowLog(true);
    try {
      const result: LogResult = await getCaptainLog(id);
      setLogText(result.log || '(empty log)');
      setLogInfo(`(${result.lines || 0} of ${result.totalLines || 0} lines)`);
    } catch {
      setLogText('Failed to load log.');
      setLogInfo('');
    } finally {
      setLogLoading(false);
    }
  }

  function handleStop() {
    if (!captain) return;
    setConfirm({
      open: true,
      title: 'Stop Captain',
      message: `Stop captain "${captain.name}"? This will halt the current mission.`,
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        try {
          await stopCaptain(captain.id);
          load();
        } catch { setError('Failed to stop captain.'); }
      },
    });
  }

  function handleRecall() {
    if (!captain) return;
    setConfirm({
      open: true,
      title: 'Recall Captain',
      message: `Recall captain "${captain.name}"? The captain will finish current work and return to idle.`,
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        try {
          await recallCaptain(captain.id);
          load();
        } catch { setError('Failed to recall captain.'); }
      },
    });
  }

  function handleRemove() {
    if (!captain) return;
    setConfirm({
      open: true,
      title: 'Remove Captain',
      message: `Remove captain "${captain.name}"? This cannot be undone.`,
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        try {
          await deleteCaptain(captain.id);
          navigate('/captains');
        } catch { setError('Remove failed.'); }
      },
    });
  }

  function getActionItems() {
    if (!captain) return [];
    const items: { label: string; danger?: boolean; onClick: () => void }[] = [
      { label: 'Edit', onClick: openEdit },
      { label: 'View Log', onClick: handleViewLog },
      { label: 'View JSON', onClick: () => setJsonData({ open: true, title: `Captain: ${captain.name}`, data: captain }) },
    ];
    if (captain.state === 'Working' || captain.state === 'Stalled') {
      items.push({ label: 'Recall Captain', onClick: handleRecall });
      items.push({ label: 'Stop Captain', danger: true, onClick: handleStop });
    }
    items.push({ label: 'Remove', danger: true, onClick: handleRemove });
    return items;
  }

  if (loading) return <p className="text-dim">Loading...</p>;
  if (error && !captain) return <ErrorModal error={error} onClose={() => setError('')} />;
  if (!captain) return <p className="text-dim">Captain not found.</p>;

  return (
    <div>
      {/* Breadcrumb */}
      <div className="breadcrumb">
        <Link to="/captains">Captains</Link> <span className="breadcrumb-sep">&gt;</span> <span>{captain.name}</span>
      </div>

      <div className="detail-header">
        <h2>{captain.name}</h2>
        <div className="inline-actions">
          <ActionMenu id={`captain-${captain.id}`} items={getActionItems()} />
        </div>
      </div>

      <ErrorModal error={error} onClose={() => setError('')} />

      {/* Edit Modal */}
      {showForm && (
        <div className="modal-overlay" onClick={() => setShowForm(false)}>
          <form className="modal" onClick={e => e.stopPropagation()} onSubmit={handleSubmit}>
            <h3>Edit Captain</h3>
            <label>Name<input value={form.name} onChange={e => setForm({ ...form, name: e.target.value })} required /></label>
            <label>Runtime
              <select value={form.runtime} onChange={e => setForm({ ...form, runtime: e.target.value })} required>
                {RUNTIMES.map(r => <option key={r} value={r}>{r}</option>)}
              </select>
            </label>
            <label title="Optional instructions injected into every mission prompt for this captain. Use this to specialize behavior, add guardrails, or provide persistent context.">
              System Instructions
              <textarea value={form.systemInstructions} onChange={e => setForm({ ...form, systemInstructions: e.target.value })} rows={4} placeholder="e.g., You are a testing specialist. Always run tests before committing..." />
            </label>
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

      {/* Captain Info */}
      <div className="detail-grid">
        <div className="detail-field">
          <span className="detail-label">ID</span>
          <span className="id-display">
            <span className="mono">{captain.id}</span>
            <button className="copy-btn" onClick={() => copyToClipboard(captain.id)} title="Copy ID" />
          </span>
        </div>
        <div className="detail-field"><span className="detail-label">Name</span><span>{captain.name}</span></div>
        <div className="detail-field"><span className="detail-label">Tenant ID</span><span className="mono">{captain.tenantId || '-'}</span></div>
        <div className="detail-field"><span className="detail-label">Runtime</span><span>{captain.runtime || 'ClaudeCode'}</span></div>
      </div>
      {captain.systemInstructions && (
        <div className="detail-context-section">
          <h4>System Instructions</h4>
          <pre className="detail-context-block">{captain.systemInstructions}</pre>
        </div>
      )}
      <div className="detail-grid">
        <div className="detail-field">
          <span className="detail-label">State</span>
          <StatusBadge status={captain.state} />
        </div>
        <div className="detail-field">
          <span className="detail-label">Current Mission</span>
          {captain.currentMissionId ? (
            <span className="id-display">
              <Link className="mono" to={`/missions/${captain.currentMissionId}`}>{captain.currentMissionId}</Link>
              <button className="copy-btn" onClick={() => copyToClipboard(captain.currentMissionId!)} title="Copy ID" />
            </span>
          ) : <span>-</span>}
        </div>
        <div className="detail-field">
          <span className="detail-label">Current Dock</span>
          {captain.currentDockId ? (
            <span className="id-display">
              <Link className="mono" to={`/docks/${captain.currentDockId}`}>{captain.currentDockId}</Link>
              <button className="copy-btn" onClick={() => copyToClipboard(captain.currentDockId!)} title="Copy ID" />
            </span>
          ) : <span>-</span>}
        </div>
        <div className="detail-field"><span className="detail-label">Process ID</span><span>{captain.processId || '-'}</span></div>
        <div className="detail-field"><span className="detail-label">Recovery Attempts</span><span>{captain.recoveryAttempts ?? 0}</span></div>
        <div className="detail-field">
          <span className="detail-label">Last Heartbeat</span>
          <span title={formatTimeAbsolute(captain.lastHeartbeatUtc)}>
            {formatTimeRelative(captain.lastHeartbeatUtc) || '-'}
            {captain.lastHeartbeatUtc && <span className="text-dim"> ({formatTimeAbsolute(captain.lastHeartbeatUtc)})</span>}
          </span>
        </div>
        <div className="detail-field">
          <span className="detail-label">Created</span>
          <span title={captain.createdUtc}>
            {formatTimeRelative(captain.createdUtc)}
            <span className="text-dim"> ({formatTimeAbsolute(captain.createdUtc)})</span>
          </span>
        </div>
        <div className="detail-field">
          <span className="detail-label">Last Updated</span>
          <span title={captain.lastUpdateUtc}>
            {formatTimeRelative(captain.lastUpdateUtc)}
            <span className="text-dim"> ({formatTimeAbsolute(captain.lastUpdateUtc)})</span>
          </span>
        </div>
      </div>

      {/* Current Mission Details */}
      {currentMission && (
        <div style={{ marginTop: '1rem' }}>
          <h3>Current Mission</h3>
          <div className="card" style={{ padding: '1rem' }}>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 8 }}>
              <strong>{currentMission.title}</strong>
              <StatusBadge status={currentMission.status} />
            </div>
            {currentMission.description && <p className="text-dim" style={{ marginBottom: 8 }}>{currentMission.description}</p>}
            <div style={{ display: 'flex', gap: 16, fontSize: 13 }}>
              <span>Branch: <span className="mono">{currentMission.branchName || '-'}</span></span>
              <span>Priority: {currentMission.priority}</span>
            </div>
            <div style={{ marginTop: 8 }}>
              <Link to={`/missions/${currentMission.id}`} className="btn btn-sm">View Mission</Link>
            </div>
          </div>
        </div>
      )}

      {/* Log Viewer */}
      {showLog && (
        <div style={{ marginTop: '1rem' }}>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
            <h3>Captain Log {logInfo && <span className="text-dim">{logInfo}</span>}</h3>
            <button className="btn" onClick={() => setShowLog(false)}>Hide Log</button>
          </div>
          {logLoading ? (
            <p className="text-dim">Loading log...</p>
          ) : (
            <pre style={{
              background: '#1a1a2e',
              color: '#e0e0e0',
              padding: 16,
              borderRadius: 'var(--radius)',
              overflow: 'auto',
              fontSize: 12,
              fontFamily: 'var(--mono)',
              maxHeight: '60vh',
              whiteSpace: 'pre-wrap',
              wordBreak: 'break-word',
            }}>
              {logText}
            </pre>
          )}
        </div>
      )}

      {/* Recent Missions */}
      <div style={{ marginTop: '1rem' }}>
        <h3>Recent Missions</h3>
        {missions.length > 0 ? (
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th title="Mission name and unique identifier">Mission</th>
                  <th title="Current mission lifecycle state">Status</th>
                  <th title="Git branch for this mission's work">Branch</th>
                  <th title="When the mission was completed or created">Date</th>
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
                    <td className="text-dim">{m.branchName || '-'}</td>
                    <td className="text-dim" title={formatTimeAbsolute(m.completedUtc || m.createdUtc)}>
                      {formatTimeRelative(m.completedUtc || m.createdUtc)}
                    </td>
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
