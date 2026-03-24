import { useEffect, useState, useCallback } from 'react';
import { useParams, useNavigate, Link } from 'react-router-dom';
import { getMergeEntry, deleteMergeEntry, processMergeEntry, cancelMergeEntry, listVessels, getMissionDiff, getMissionLog } from '../api/client';
import type { MergeEntry, Vessel } from '../types/models';
import ActionMenu from '../components/shared/ActionMenu';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import JsonViewer from '../components/shared/JsonViewer';
import DiffViewer from '../components/shared/DiffViewer';
import LogViewer from '../components/shared/LogViewer';
import StatusBadge from '../components/shared/StatusBadge';
import CopyButton from '../components/shared/CopyButton';
import ErrorModal from '../components/shared/ErrorModal';

function formatTimeAbsolute(utc: string | null): string {
  if (!utc) return '-';
  return new Date(utc).toLocaleString();
}

function formatTimeRelative(utc: string | null): string {
  if (!utc) return '';
  const d = new Date(utc);
  const diff = Date.now() - d.getTime();
  if (diff < 60000) return 'just now';
  if (diff < 3600000) return Math.floor(diff / 60000) + 'm ago';
  if (diff < 86400000) return Math.floor(diff / 3600000) + 'h ago';
  return Math.floor(diff / 86400000) + 'd ago';
}

export default function MergeQueueDetail() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const [entry, setEntry] = useState<MergeEntry | null>(null);
  const [vessels, setVessels] = useState<Vessel[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  // JSON viewer
  const [jsonData, setJsonData] = useState<{ open: boolean; title: string; data: unknown }>({ open: false, title: '', data: null });

  // Confirm
  const [confirm, setConfirm] = useState<{ open: boolean; title: string; message: string; onConfirm: () => void }>({ open: false, title: '', message: '', onConfirm: () => {} });

  // Diff viewer
  const [diffModal, setDiffModal] = useState<{ open: boolean; title: string; rawDiff: string; loading: boolean }>({ open: false, title: '', rawDiff: '', loading: false });

  // Log viewer
  const [logModal, setLogModal] = useState<{ open: boolean; title: string; missionId: string; content: string; totalLines: number; lineCount: number }>({ open: false, title: '', missionId: '', content: '', totalLines: 0, lineCount: 200 });

  const vesselName = useCallback((vesselId: string | null) => {
    if (!vesselId) return '-';
    return vessels.find(v => v.id === vesselId)?.name ?? vesselId;
  }, [vessels]);

  const load = useCallback(async () => {
    if (!id) return;
    try {
      setLoading(true);
      const [e, vResult] = await Promise.all([getMergeEntry(id), listVessels({ pageSize: 1000 })]);
      setEntry(e);
      setVessels(vResult.objects);
      setError('');
    } catch {
      setError('Failed to load merge entry.');
    } finally {
      setLoading(false);
    }
  }, [id]);

  useEffect(() => { load(); }, [load]);

  function handleProcess() {
    if (!entry) return;
    setConfirm({
      open: true,
      title: 'Process Entry',
      message: `Process merge entry for branch "${entry.branchName}"?`,
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        try {
          await processMergeEntry(entry.id);
          load();
        } catch { setError('Process failed.'); }
      },
    });
  }

  function handleCancel() {
    if (!entry) return;
    setConfirm({
      open: true,
      title: 'Cancel Entry',
      message: `Cancel merge entry for branch "${entry.branchName}"?`,
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        try {
          await cancelMergeEntry(entry.id);
          load();
        } catch { setError('Cancel failed.'); }
      },
    });
  }

  function handleDelete() {
    if (!entry) return;
    setConfirm({
      open: true,
      title: 'Delete Entry',
      message: `Delete merge entry ${entry.id}? This cannot be undone.`,
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        try {
          await deleteMergeEntry(entry.id);
          navigate('/merge-queue');
        } catch { setError('Delete failed.'); }
      },
    });
  }

  // Mission diff/log shortcuts
  async function handleMissionDiff() {
    if (!entry?.missionId) return;
    setDiffModal({ open: true, title: `Diff: Mission ${entry.missionId.substring(0, 8)}...`, rawDiff: '', loading: true });
    try {
      const result = await getMissionDiff(entry.missionId);
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

  function handleMissionLog() {
    if (!entry?.missionId) return;
    setLogModal({ open: true, title: `Log: Mission ${entry.missionId.substring(0, 8)}...`, missionId: entry.missionId, content: 'Loading...', totalLines: 0, lineCount: 200 });
    fetchLog(entry.missionId, 200);
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

  if (loading) return <p className="text-dim">Loading...</p>;
  if (error && !entry) return <ErrorModal error={error} onClose={() => setError('')} />;
  if (!entry) return <p className="text-dim">Merge entry not found.</p>;

  const actionItems = [
    { label: 'View JSON', onClick: () => setJsonData({ open: true, title: `Merge Entry: ${entry.id}`, data: entry }) },
    { label: 'Process', onClick: handleProcess },
    { label: 'Cancel', onClick: handleCancel },
    ...(entry.missionId ? [
      { label: 'Mission Diff', onClick: handleMissionDiff },
      { label: 'Mission Log', onClick: handleMissionLog },
    ] : []),
    { label: 'Delete', danger: true as const, onClick: handleDelete },
  ];

  return (
    <div>
      <div className="breadcrumb">
        <Link to="/merge-queue">Merge Queue</Link> <span className="breadcrumb-sep">&gt;</span> <span className="mono">{entry.id}</span>
      </div>

      <div className="detail-header">
        <h2>Merge Entry</h2>
        <div className="inline-actions">
          {entry.missionId && (
            <>
              <button className="btn btn-sm" onClick={handleMissionDiff} title="View mission diff">Diff</button>
              <button className="btn btn-sm" onClick={handleMissionLog} title="View mission log">Log</button>
            </>
          )}
          <ActionMenu id={`merge-entry-action-${entry.id}`} items={actionItems} />
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

      {/* Entry Info */}
      <div className="detail-grid">
        <div className="detail-field">
          <span className="detail-label">ID</span>
          <span className="id-display">
            <span className="mono">{entry.id}</span>
            <CopyButton text={entry.id} />
          </span>
        </div>
        <div className="detail-field"><span className="detail-label">Status</span><StatusBadge status={entry.status} /></div>
        <div className="detail-field"><span className="detail-label">Branch</span><span className="mono">{entry.branchName}</span></div>
        <div className="detail-field"><span className="detail-label">Target Branch</span><span className="mono">{entry.targetBranch}</span></div>
        <div className="detail-field"><span className="detail-label">Priority</span><span>{entry.priority}</span></div>
        <div className="detail-field">
          <span className="detail-label">Vessel</span>
          {entry.vesselId
            ? <Link to={`/vessels/${entry.vesselId}`}>{vesselName(entry.vesselId)}</Link>
            : <span>-</span>}
        </div>
        <div className="detail-field">
          <span className="detail-label">Mission</span>
          {entry.missionId
            ? <Link to={`/missions/${entry.missionId}`} className="mono">{entry.missionId}</Link>
            : <span>-</span>}
        </div>
        <div className="detail-field"><span className="detail-label">Batch ID</span><span className="mono">{entry.batchId || '-'}</span></div>
        <div className="detail-field"><span className="detail-label">Test Exit Code</span><span>{entry.testExitCode ?? '-'}</span></div>
        <div className="detail-field"><span className="detail-label">Tenant ID</span><span className="mono">{entry.tenantId || '-'}</span></div>
        <div className="detail-field"><span className="detail-label">Test Command</span><span className="mono">{entry.testCommand || '-'}</span></div>
        <div className="detail-field">
          <span className="detail-label">Created</span>
          <span>{formatTimeRelative(entry.createdUtc)} <span className="text-dim">({formatTimeAbsolute(entry.createdUtc)})</span></span>
        </div>
        <div className="detail-field">
          <span className="detail-label">Test Started</span>
          <span>{entry.testStartedUtc ? <>{formatTimeRelative(entry.testStartedUtc)} <span className="text-dim">({formatTimeAbsolute(entry.testStartedUtc)})</span></> : '-'}</span>
        </div>
        <div className="detail-field">
          <span className="detail-label">Completed</span>
          <span>{entry.completedUtc ? <>{formatTimeRelative(entry.completedUtc)} <span className="text-dim">({formatTimeAbsolute(entry.completedUtc)})</span></> : '-'}</span>
        </div>
        <div className="detail-field">
          <span className="detail-label">Last Updated</span>
          <span>{formatTimeRelative(entry.lastUpdateUtc)} <span className="text-dim">({formatTimeAbsolute(entry.lastUpdateUtc)})</span></span>
        </div>
      </div>

      {/* Test Command */}
      {entry.testCommand && (
        <div className="detail-context-section">
          <h4>Test Command</h4>
          <pre className="detail-context-block">{entry.testCommand}</pre>
        </div>
      )}

      {/* Test Output */}
      {entry.testOutput && (
        <div className="detail-context-section">
          <h4>Test Output</h4>
          <pre className="detail-context-block">{entry.testOutput}</pre>
        </div>
      )}
    </div>
  );
}
