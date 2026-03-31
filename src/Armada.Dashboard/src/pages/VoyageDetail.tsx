import { useEffect, useState, useMemo, useCallback } from 'react';
import { useParams, useNavigate, Link } from 'react-router-dom';
import {
  getVoyage,
  purgeVoyage,
  cancelVoyage,
  listMissions,
  getMissionDiff,
  getMissionLog,
  createMission,
  listVessels,
  listCaptains,
} from '../api/client';
import type { Voyage, Mission, Vessel, Captain } from '../types/models';
import StatusBadge from '../components/shared/StatusBadge';
import ErrorModal from '../components/shared/ErrorModal';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import JsonViewer from '../components/shared/JsonViewer';
import DiffViewer from '../components/shared/DiffViewer';
import LogViewer from '../components/shared/LogViewer';
import CopyButton from '../components/shared/CopyButton';

// ── Helper utilities ──

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

// ── Main component ──

export default function VoyageDetail() {
  const { id } = useParams<{ id: string }>();
  const nav = useNavigate();

  const [voyage, setVoyage] = useState<Voyage | null>(null);
  const [missions, setMissions] = useState<Mission[]>([]);
  const [vessels, setVessels] = useState<Vessel[]>([]);
  const [captains, setCaptains] = useState<Captain[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  // Modals
  const [diffModal, setDiffModal] = useState<{ open: boolean; title: string; rawDiff: string; loading: boolean }>({ open: false, title: '', rawDiff: '', loading: false });
  const [logModal, setLogModal] = useState<{ open: boolean; title: string; missionId: string; content: string; totalLines: number; lineCount: number }>({ open: false, title: '', missionId: '', content: '', totalLines: 0, lineCount: 200 });
  const [jsonData, setJsonData] = useState<{ open: boolean; title: string; data: unknown }>({ open: false, title: '', data: null });
  const [confirm, setConfirm] = useState<{ open: boolean; title: string; message: string; danger: boolean; onConfirm: () => void }>({ open: false, title: '', message: '', danger: false, onConfirm: () => {} });

  // Lookup maps
  const vesselMap = useMemo(() => {
    const m = new Map<string, string>();
    vessels.forEach(v => m.set(v.id, v.name));
    return m;
  }, [vessels]);

  const captainMap = useMemo(() => {
    const m = new Map<string, string>();
    captains.forEach(c => m.set(c.id, c.name));
    return m;
  }, [captains]);

  const vesselName = useCallback((vid: string | null | undefined) => vid ? vesselMap.get(vid) || vid.slice(0, 8) : '-', [vesselMap]);
  const captainName = useCallback((cid: string | null | undefined) => cid ? captainMap.get(cid) || cid.slice(0, 8) : '-', [captainMap]);

  const loadVoyage = useCallback(async () => {
    if (!id) return;
    setLoading(true);
    try {
      const v = await getVoyage(id);
      // The API may return { voyage, missions } or just the voyage object
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const raw = v as any;
      if (raw.voyage) {
        setVoyage(raw.voyage);
        setMissions(raw.missions || []);
      } else {
        setVoyage(raw);
        // Load missions separately
        try {
          const mResult = await listMissions({ pageSize: 1000, filters: { voyageId: id } });
          setMissions(mResult.objects || []);
        } catch {
          setMissions([]);
        }
      }
    } catch (e: unknown) {
      setError('Failed to load voyage: ' + (e instanceof Error ? e.message : String(e)));
    } finally {
      setLoading(false);
    }
  }, [id]);

  useEffect(() => {
    loadVoyage();
    listVessels({ pageSize: 1000 }).then(r => setVessels(r.objects || [])).catch(() => {});
    listCaptains({ pageSize: 1000 }).then(r => setCaptains(r.objects || [])).catch(() => {});
  }, [loadVoyage]);

  // Progress
  const completedCount = missions.filter(m => m.status === 'Complete').length;
  const failedCount = missions.filter(m => m.status === 'Failed').length;
  const progressPct = missions.length > 0 ? Math.round((completedCount / missions.length) * 100) : 0;

  // Actions
  function handleCancel() {
    if (!voyage) return;
    setConfirm({
      open: true,
      title: 'Cancel Voyage',
      message: 'Cancel this voyage? All pending missions will be cancelled.',
      danger: true,
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        try {
          await cancelVoyage(voyage.id);
          loadVoyage();
        } catch (e: unknown) {
          setError('Cancel failed: ' + (e instanceof Error ? e.message : String(e)));
        }
      },
    });
  }

  function handleDelete() {
    if (!voyage) return;
    setConfirm({
      open: true,
      title: 'Delete Voyage',
      message: 'Permanently delete this voyage and all its missions? This cannot be undone.',
      danger: true,
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        try {
          await purgeVoyage(voyage.id);
          nav('/voyages');
        } catch (e: unknown) {
          setError('Delete failed: ' + (e instanceof Error ? e.message : String(e)));
        }
      },
    });
  }

  function handleRetryFailed() {
    if (!voyage) return;
    const failed = missions.filter(m => m.status === 'Failed');
    if (failed.length === 0) return;
    setConfirm({
      open: true,
      title: 'Retry Failed Missions',
      message: `Retry ${failed.length} failed mission(s)? New missions will be created with the same parameters.`,
      danger: false,
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        try {
          for (const m of failed) {
            await createMission({
              title: m.title,
              description: m.description || undefined,
              vesselId: m.vesselId || undefined,
              voyageId: m.voyageId || undefined,
              priority: m.priority,
            });
          }
          loadVoyage();
        } catch (e: unknown) {
          setError('Retry failed: ' + (e instanceof Error ? e.message : String(e)));
        }
      },
    });
  }

  function handleViewJson() {
    if (!voyage) return;
    setJsonData({ open: true, title: 'Voyage: ' + (voyage.title || voyage.id), data: voyage });
  }

  async function handleMissionDiff(missionId: string, title: string) {
    setDiffModal({ open: true, title: 'Diff: ' + title, rawDiff: '', loading: true });
    try {
      const result = await getMissionDiff(missionId);
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

  function handleMissionLog(missionId: string, title: string) {
    setLogModal({ open: true, title: 'Log: ' + title, missionId, content: 'Loading...', totalLines: 0, lineCount: 200 });
    fetchLog(missionId, 200);
  }

  const handleLogRefresh = useCallback(() => {
    if (logModal.missionId) {
      fetchLog(logModal.missionId, logModal.lineCount);
    }
  }, [logModal.missionId, logModal.lineCount, fetchLog]);

  const handleLogLineCountChange = useCallback((lines: number) => {
    setLogModal(l => ({ ...l, lineCount: lines }));
    if (logModal.missionId) fetchLog(logModal.missionId, lines);
  }, [logModal.missionId, fetchLog]);

  if (loading) return <p className="text-muted">Loading...</p>;
  if (!voyage) return <ErrorModal error={error || 'Voyage not found.'} onClose={() => nav('/voyages')} />;

  return (
    <div>
      {/* Breadcrumb */}
      <div style={{ display: 'flex', gap: 8, alignItems: 'center', marginBottom: 16, fontSize: 13 }}>
        <Link to="/voyages">Voyages</Link>
        <span style={{ color: 'var(--text-muted)' }}>/</span>
        <span>{voyage.title || voyage.id}</span>
      </div>

      <ErrorModal error={error} onClose={() => setError('')} />

      {/* Header */}
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', marginBottom: 20 }}>
        <h2>{voyage.title || voyage.id}</h2>
      </div>

      {/* Action buttons */}
      <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', marginBottom: 20 }}>
        {(voyage.status === 'Open' || voyage.status === 'InProgress') && (
          <button className="btn-sm btn-danger" onClick={handleCancel}>Cancel Voyage</button>
        )}
        {failedCount > 0 && (
          <button className="btn-sm" onClick={handleRetryFailed}>Retry Failed ({failedCount})</button>
        )}
        <button className="btn-sm" onClick={handleViewJson}>View JSON</button>
        {voyage.status !== 'Open' && voyage.status !== 'InProgress' && (
          <button className="btn-sm btn-danger" onClick={handleDelete}>Delete</button>
        )}
      </div>

      {/* Detail grid */}
      <div className="card-grid" style={{ marginBottom: 20 }}>
        <div className="card">
          <h3>Status</h3>
          <StatusBadge status={voyage.status} />
        </div>

        <div className="card">
          <h3>Details</h3>
          <div style={{ display: 'grid', gap: 8 }}>
            <div><span className="text-muted" style={{ fontSize: 12 }}>ID</span>
              <div className="mono" style={{ fontSize: 12, display: 'flex', alignItems: 'center', gap: 4 }}>
                {voyage.id}
                <CopyButton text={voyage.id} />
              </div>
            </div>
            <div><span className="text-muted" style={{ fontSize: 12 }}>Description</span><div>{voyage.description || '-'}</div></div>
          </div>
        </div>

        <div className="card">
          <h3>Configuration</h3>
          <div style={{ display: 'grid', gap: 8 }}>
            <div><span className="text-muted" style={{ fontSize: 12 }}>Auto-Push</span><div>{voyage.autoPush != null ? (voyage.autoPush ? 'Yes' : 'No') : '-'}</div></div>
            <div><span className="text-muted" style={{ fontSize: 12 }}>Auto-Create PRs</span><div>{voyage.autoCreatePullRequests != null ? (voyage.autoCreatePullRequests ? 'Yes' : 'No') : '-'}</div></div>
            <div><span className="text-muted" style={{ fontSize: 12 }}>Auto-Merge PRs</span><div>{voyage.autoMergePullRequests != null ? (voyage.autoMergePullRequests ? 'Yes' : 'No') : '-'}</div></div>
            <div><span className="text-muted" style={{ fontSize: 12 }}>Landing Mode</span><div>{voyage.landingMode || '-'}</div></div>
          </div>
        </div>

        <div className="card">
          <h3>Timestamps</h3>
          <div style={{ display: 'grid', gap: 8 }}>
            <div>
              <span className="text-muted" style={{ fontSize: 12 }}>Created</span>
              <div>{formatTimeAbsolute(voyage.createdUtc)} <span className="text-muted" style={{ fontSize: 11 }}>({formatTimeRelative(voyage.createdUtc)})</span></div>
            </div>
            {voyage.completedUtc && (
              <div>
                <span className="text-muted" style={{ fontSize: 12 }}>Completed</span>
                <div>{formatTimeAbsolute(voyage.completedUtc)} <span className="text-muted" style={{ fontSize: 11 }}>({formatTimeRelative(voyage.completedUtc)})</span></div>
              </div>
            )}
          </div>
        </div>
      </div>

      {/* Progress bar */}
      {missions.length > 0 && (
        <div style={{ marginBottom: 20 }}>
          <div style={{ height: 10, background: '#e0e0e8', borderRadius: 5, overflow: 'hidden' }}>
            <div style={{ height: '100%', width: `${progressPct}%`, background: 'var(--primary)', transition: 'width 0.3s' }} />
          </div>
          <span className="text-muted" style={{ fontSize: 12, marginTop: 4, display: 'inline-block' }}>
            {completedCount}/{missions.length} complete, {failedCount} failed
          </span>
        </div>
      )}

      {/* Missions table */}
      <h3 style={{ marginBottom: 12 }}>Missions</h3>
      {missions.length > 0 ? (
        <table className="table">
          <thead>
            <tr>
              <th>Mission</th>
              <th>Status</th>
              <th>Vessel</th>
              <th>Captain</th>
              <th>Branch</th>
              <th style={{ width: 120 }}>Actions</th>
            </tr>
          </thead>
          <tbody>
            {missions.map(m => (
              <tr key={m.id} style={{ cursor: 'pointer' }} onClick={() => nav(`/missions/${m.id}`)}>
                <td>
                  <strong>{m.title}</strong>
                  <div className="text-muted mono" style={{ fontSize: 11, display: 'flex', alignItems: 'center', gap: 4 }}>
                    {m.id}
                    <CopyButton text={m.id} onClick={e => e.stopPropagation()} />
                  </div>
                </td>
                <td>
                  <StatusBadge status={m.status} />
                </td>
                <td>{m.vesselId ? <Link to={`/vessels/${m.vesselId}`} onClick={e => e.stopPropagation()}>{vesselName(m.vesselId)}</Link> : '-'}</td>
                <td>{m.captainId ? <Link to={`/captains/${m.captainId}`} onClick={e => e.stopPropagation()}>{captainName(m.captainId)}</Link> : '-'}</td>
                <td className="mono text-muted" style={{ fontSize: 11 }}>{m.branchName || '-'}</td>
                <td onClick={e => e.stopPropagation()}>
                  <div style={{ display: 'flex', gap: 4 }}>
                    <button className="btn-sm" onClick={() => handleMissionDiff(m.id, m.title)} title="View Diff">Diff</button>
                    <button className="btn-sm" onClick={() => handleMissionLog(m.id, m.title)} title="View Log">Log</button>
                    <button className="btn-sm" onClick={() => nav(`/missions/${m.id}`)} title="View Detail">Detail</button>
                  </div>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      ) : (
        <p className="text-muted">No missions in this voyage.</p>
      )}

      {/* Shared modals */}
      <ConfirmDialog
        open={confirm.open}
        title={confirm.title}
        message={confirm.message}
        danger={confirm.danger}
        onConfirm={confirm.onConfirm}
        onCancel={() => setConfirm(c => ({ ...c, open: false }))}
      />
      <JsonViewer
        open={jsonData.open}
        title={jsonData.title}
        data={jsonData.data}
        onClose={() => setJsonData({ open: false, title: '', data: null })}
      />
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
    </div>
  );
}
