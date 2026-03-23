import { useEffect, useState, useMemo, useCallback } from 'react';
import { useParams, useNavigate, Link } from 'react-router-dom';
import {
  getMission,
  updateMission,
  deleteMission,
  purgeMission,
  getMissionDiff,
  getMissionLog,
  restartMission,
  transitionMission,
  listVessels,
  listCaptains,
} from '../api/client';
import type { Mission, Vessel, Captain } from '../types/models';
import ErrorModal from '../components/shared/ErrorModal';
import StatusBadge from '../components/shared/StatusBadge';
import ActionMenu from '../components/shared/ActionMenu';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import JsonViewer from '../components/shared/JsonViewer';
import DiffViewer from '../components/shared/DiffViewer';
import LogViewer from '../components/shared/LogViewer';
import { copyToClipboard } from '../components/shared/CopyButton';

const MISSION_STATUSES = [
  'Pending', 'Assigned', 'InProgress', 'WorkProduced', 'Testing', 'Review', 'Complete', 'Failed', 'LandingFailed', 'Cancelled',
];

function formatTimeAbsolute(utc: string | null | undefined): string {
  if (!utc) return '-';
  return new Date(utc).toLocaleString();
}

function formatTimeRelative(utc: string | null | undefined): string {
  if (!utc) return '';
  const d = new Date(utc);
  const diff = Date.now() - d.getTime();
  if (diff < 60000) return 'just now';
  if (diff < 3600000) return Math.floor(diff / 60000) + 'm ago';
  if (diff < 86400000) return Math.floor(diff / 3600000) + 'h ago';
  return Math.floor(diff / 86400000) + 'd ago';
}

export default function MissionDetail() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();

  const [mission, setMission] = useState<Mission | null>(null);
  const [vessels, setVessels] = useState<Vessel[]>([]);
  const [captains, setCaptains] = useState<Captain[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  // Diff viewer (shared modal)
  const [diffModal, setDiffModal] = useState<{ open: boolean; title: string; rawDiff: string; loading: boolean }>({ open: false, title: '', rawDiff: '', loading: false });

  // Log viewer (shared modal)
  const [logModal, setLogModal] = useState<{ open: boolean; title: string; missionId: string; content: string; totalLines: number; lineCount: number }>({ open: false, title: '', missionId: '', content: '', totalLines: 0, lineCount: 200 });

  // Transition
  const [showTransition, setShowTransition] = useState(false);
  const [transitionStatus, setTransitionStatus] = useState('');

  // Edit form
  const [editModal, setEditModal] = useState(false);
  const [editTitle, setEditTitle] = useState('');
  const [editDescription, setEditDescription] = useState('');
  const [editPriority, setEditPriority] = useState(100);
  const [editSaving, setEditSaving] = useState(false);

  // JSON viewer
  const [jsonData, setJsonData] = useState<{ open: boolean; title: string; data: unknown }>({ open: false, title: '', data: null });

  // Confirm
  const [confirm, setConfirm] = useState<{ open: boolean; title: string; message: string; onConfirm: () => void }>({ open: false, title: '', message: '', onConfirm: () => {} });

  // Lookup maps
  const vesselName = useMemo(() => {
    const m = new Map<string, string>();
    vessels.forEach(v => m.set(v.id, v.name));
    return (vid: string | null | undefined) => vid ? m.get(vid) || vid : '-';
  }, [vessels]);

  const captainName = useMemo(() => {
    const m = new Map<string, string>();
    captains.forEach(c => m.set(c.id, c.name));
    return (cid: string | null | undefined) => cid ? m.get(cid) || cid : '-';
  }, [captains]);

  const loadMission = useCallback(async () => {
    if (!id) return;
    setLoading(true);
    try {
      const m = await getMission(id);
      setMission(m);
      setError('');
    } catch (e: unknown) {
      setError('Failed to load mission: ' + (e instanceof Error ? e.message : String(e)));
    } finally {
      setLoading(false);
    }
  }, [id]);

  useEffect(() => {
    loadMission();
    listVessels({ pageSize: 1000 }).then(r => setVessels(r.objects || [])).catch(() => {});
    listCaptains({ pageSize: 1000 }).then(r => setCaptains(r.objects || [])).catch(() => {});
  }, [loadMission]);

  async function handleViewDiff() {
    if (!id) return;
    setDiffModal({ open: true, title: `Diff: ${mission?.title || id}`, rawDiff: '', loading: true });
    try {
      const result = await getMissionDiff(id);
      setDiffModal(d => ({ ...d, rawDiff: result?.diff || '', loading: false }));
    } catch {
      setDiffModal(d => ({ ...d, rawDiff: '', loading: false }));
    }
  }

  const fetchLog = useCallback(async (missionId: string, lines: number) => {
    try {
      const result = await getMissionLog(missionId, lines);
      setLogModal(l => ({ ...l, content: result.log || 'No log output', totalLines: result.totalLines || 0 }));
    } catch (e: unknown) {
      setLogModal(l => ({ ...l, content: 'Log unavailable: ' + (e instanceof Error ? e.message : String(e)) }));
    }
  }, []);

  function handleViewLog() {
    if (!id) return;
    setLogModal({ open: true, title: `Log: ${mission?.title || id}`, missionId: id, content: 'Loading...', totalLines: 0, lineCount: 200 });
    fetchLog(id, 200);
  }

  const handleLogRefresh = useCallback(() => {
    if (logModal.missionId) fetchLog(logModal.missionId, logModal.lineCount);
  }, [logModal.missionId, logModal.lineCount, fetchLog]);

  const handleLogLineCountChange = useCallback((lines: number) => {
    setLogModal(l => ({ ...l, lineCount: lines }));
    if (logModal.missionId) fetchLog(logModal.missionId, lines);
  }, [logModal.missionId, fetchLog]);

  function openEdit() {
    if (!mission) return;
    setEditTitle(mission.title);
    setEditDescription(mission.description || '');
    setEditPriority(mission.priority);
    setEditModal(true);
  }

  async function handleSaveEdit(e: React.FormEvent) {
    e.preventDefault();
    if (!mission) return;
    setEditSaving(true);
    try {
      await updateMission(mission.id, { title: editTitle, description: editDescription, priority: editPriority });
      setEditModal(false);
      loadMission();
    } catch (e: unknown) {
      setError('Save failed: ' + (e instanceof Error ? e.message : String(e)));
    } finally {
      setEditSaving(false);
    }
  }

  async function handleTransition() {
    if (!mission || !transitionStatus) return;
    try {
      await transitionMission(mission.id, { status: transitionStatus });
      setShowTransition(false);
      setTransitionStatus('');
      loadMission();
    } catch (e: unknown) {
      setError('Transition failed: ' + (e instanceof Error ? e.message : String(e)));
    }
  }

  function handleRestart() {
    if (!mission) return;
    setConfirm({
      open: true,
      title: 'Restart Mission',
      message: `Restart mission "${mission.title}"? This will reset the mission to Pending status.`,
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        try {
          await restartMission(mission.id);
          loadMission();
        } catch (e: unknown) {
          setError('Restart failed: ' + (e instanceof Error ? e.message : String(e)));
        }
      },
    });
  }

  function handlePurge() {
    if (!mission) return;
    setConfirm({
      open: true,
      title: 'Purge Mission',
      message: `Purge mission "${mission.title}"? This will clean up all associated resources (branches, worktrees, etc.) and cannot be undone.`,
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        try {
          await purgeMission(mission.id);
          loadMission();
        } catch (e: unknown) {
          setError('Purge failed: ' + (e instanceof Error ? e.message : String(e)));
        }
      },
    });
  }

  function handleDelete() {
    if (!mission) return;
    setConfirm({
      open: true,
      title: 'Delete Mission',
      message: `Permanently delete mission "${mission.title}"? This cannot be undone.`,
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        try {
          await deleteMission(mission.id);
          navigate('/missions');
        } catch (e: unknown) {
          setError('Delete failed: ' + (e instanceof Error ? e.message : String(e)));
        }
      },
    });
  }

  if (loading) return <p className="text-dim">Loading...</p>;
  if (!mission) return <ErrorModal error={error || 'Mission not found.'} onClose={() => setError('')} />;

  return (
    <div>
      {/* Breadcrumb */}
      <div className="breadcrumb">
        <Link to="/missions">Missions</Link> <span className="breadcrumb-sep">&gt;</span> <span>{mission.title}</span>
      </div>

      <div className="detail-header">
        <h2>{mission.title}</h2>
        <div className="inline-actions">
          <button className="btn btn-sm" onClick={handleViewDiff} title="View mission diff">Diff</button>
          <button className="btn btn-sm" onClick={handleViewLog} title="View mission log">Log</button>
          <ActionMenu id={`mission-action-${mission.id}`} items={[
            { label: 'Edit', onClick: openEdit },
            { label: 'View Diff', onClick: handleViewDiff },
            { label: 'View Log', onClick: handleViewLog },
            { label: 'Transition Status', onClick: () => setShowTransition(true) },
            { label: 'View JSON', onClick: () => setJsonData({ open: true, title: `Mission: ${mission.title}`, data: mission }) },
            { label: 'Restart', onClick: handleRestart },
            { label: 'Purge', danger: true, onClick: handlePurge },
            { label: 'Delete', danger: true, onClick: handleDelete },
          ]} />
        </div>
      </div>

      <ErrorModal error={error} onClose={() => setError('')} />

      <JsonViewer open={jsonData.open} title={jsonData.title} data={jsonData.data} onClose={() => setJsonData({ open: false, title: '', data: null })} />
      <ConfirmDialog open={confirm.open} title={confirm.title} message={confirm.message}
        onConfirm={confirm.onConfirm} onCancel={() => setConfirm(c => ({ ...c, open: false }))} />
      <DiffViewer
        open={diffModal.open}
        title={diffModal.title}
        rawDiff={diffModal.rawDiff}
        loading={diffModal.loading}
        onClose={() => setDiffModal({ open: false, title: '', rawDiff: '', loading: false })}
      />
      <LogViewer
        open={logModal.open}
        title={logModal.title}
        content={logModal.content}
        totalLines={logModal.totalLines}
        onClose={() => setLogModal({ open: false, title: '', missionId: '', content: '', totalLines: 0, lineCount: 200 })}
        onRefresh={handleLogRefresh}
        onLineCountChange={handleLogLineCountChange}
      />

      {/* Edit Modal */}
      {editModal && (
        <div className="modal-overlay" onClick={() => setEditModal(false)}>
          <form className="modal" onClick={e => e.stopPropagation()} onSubmit={handleSaveEdit}>
            <h3>Edit Mission</h3>
            <label>Title<input value={editTitle} onChange={e => setEditTitle(e.target.value)} required /></label>
            <label>Description<textarea value={editDescription} onChange={e => setEditDescription(e.target.value)} rows={3} /></label>
            <label>Priority<input type="number" value={editPriority} onChange={e => setEditPriority(Number(e.target.value))} min={0} max={1000} /></label>
            <div className="modal-actions">
              <button type="submit" className="btn btn-primary" disabled={editSaving}>{editSaving ? 'Saving...' : 'Save'}</button>
              <button type="button" className="btn" onClick={() => setEditModal(false)}>Cancel</button>
            </div>
          </form>
        </div>
      )}

      {/* Transition Modal */}
      {showTransition && (
        <div className="modal-overlay" onClick={() => setShowTransition(false)}>
          <div className="modal" onClick={e => e.stopPropagation()}>
            <h3>Transition Mission Status</h3>
            <p className="text-dim">Current status: <StatusBadge status={mission.status} /></p>
            <label style={{ marginTop: 12 }}>
              New Status
              <select value={transitionStatus} onChange={e => setTransitionStatus(e.target.value)}>
                <option value="">Select status...</option>
                {MISSION_STATUSES.map(s => (
                  <option key={s} value={s}>{s}</option>
                ))}
              </select>
            </label>
            <div className="modal-actions">
              <button className="btn btn-primary" onClick={handleTransition} disabled={!transitionStatus}>Transition</button>
              <button className="btn" onClick={() => setShowTransition(false)}>Cancel</button>
            </div>
          </div>
        </div>
      )}

      {/* Mission Info */}
      <div className="detail-grid">
        <div className="detail-field">
          <span className="detail-label">ID</span>
          <span className="id-display">
            <span className="mono">{mission.id}</span>
            <button className="copy-btn" onClick={() => copyToClipboard(mission.id)} title="Copy ID" />
          </span>
        </div>
        <div className="detail-field"><span className="detail-label">Tenant ID</span><span className="mono">{mission.tenantId || '-'}</span></div>
        <div className="detail-field">
          <span className="detail-label">Status</span>
          <StatusBadge status={mission.status} />
        </div>
        <div className="detail-field"><span className="detail-label">Priority</span><span>{mission.priority}</span></div>
        <div className="detail-field">
          <span className="detail-label">Voyage</span>
          {mission.voyageId
            ? <Link to={`/voyages/${mission.voyageId}`} className="mono">{mission.voyageId}</Link>
            : <span>-</span>}
        </div>
        <div className="detail-field">
          <span className="detail-label">Vessel</span>
          {mission.vesselId
            ? <Link to={`/vessels/${mission.vesselId}`}>{vesselName(mission.vesselId)}</Link>
            : <span>-</span>}
        </div>
        <div className="detail-field">
          <span className="detail-label">Captain</span>
          {mission.captainId
            ? <Link to={`/captains/${mission.captainId}`}>{captainName(mission.captainId)}</Link>
            : <span>-</span>}
        </div>
        <div className="detail-field">
          <span className="detail-label">Parent Mission</span>
          {mission.parentMissionId
            ? <Link to={`/missions/${mission.parentMissionId}`} className="mono">{mission.parentMissionId}</Link>
            : <span>-</span>}
        </div>
        <div className="detail-field"><span className="detail-label">Branch Name</span><span className="mono">{mission.branchName || '-'}</span></div>
        <div className="detail-field">
          <span className="detail-label">Dock</span>
          {mission.dockId
            ? <Link to={`/docks/${mission.dockId}`} className="mono">{mission.dockId}</Link>
            : <span>-</span>}
        </div>
        <div className="detail-field"><span className="detail-label">Process ID</span><span>{mission.processId ?? '-'}</span></div>
        <div className="detail-field">
          <span className="detail-label">PR URL</span>
          {mission.prUrl
            ? <a href={mission.prUrl} target="_blank" rel="noopener noreferrer">{mission.prUrl}</a>
            : <span>-</span>}
        </div>
        <div className="detail-field"><span className="detail-label">Commit Hash</span><span className="mono">{mission.commitHash || '-'}</span></div>
        <div className="detail-field">
          <span className="detail-label">Created</span>
          <span title={mission.createdUtc}>
            {formatTimeRelative(mission.createdUtc)}
            <span className="text-dim"> ({formatTimeAbsolute(mission.createdUtc)})</span>
          </span>
        </div>
        <div className="detail-field">
          <span className="detail-label">Started</span>
          <span title={formatTimeAbsolute(mission.startedUtc)}>
            {formatTimeRelative(mission.startedUtc) || '-'}
            {mission.startedUtc && <span className="text-dim"> ({formatTimeAbsolute(mission.startedUtc)})</span>}
          </span>
        </div>
        <div className="detail-field">
          <span className="detail-label">Completed</span>
          <span title={formatTimeAbsolute(mission.completedUtc)}>
            {formatTimeRelative(mission.completedUtc) || '-'}
            {mission.completedUtc && <span className="text-dim"> ({formatTimeAbsolute(mission.completedUtc)})</span>}
          </span>
        </div>
        <div className="detail-field">
          <span className="detail-label">Last Updated</span>
          <span title={mission.lastUpdateUtc}>
            {formatTimeRelative(mission.lastUpdateUtc)}
            <span className="text-dim"> ({formatTimeAbsolute(mission.lastUpdateUtc)})</span>
          </span>
        </div>
      </div>

      {/* Description */}
      {mission.description && (
        <div style={{ marginTop: '1rem' }}>
          <h3>Description</h3>
          <div className="card" style={{ padding: '1rem', whiteSpace: 'pre-wrap' }}>{mission.description}</div>
        </div>
      )}
    </div>
  );
}
