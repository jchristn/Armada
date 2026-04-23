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
import { useLocale } from '../context/LocaleContext';
import { useNotifications } from '../context/NotificationContext';

export default function MergeQueueDetail() {
  const { t, formatDateTime, formatRelativeTime } = useLocale();
  const { pushToast } = useNotifications();
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
      const isInitialLoad = !entry;
      const [e, vResult] = await Promise.all([getMergeEntry(id), listVessels({ pageSize: 1000 })]);
      setEntry(e);
      setVessels(vResult.objects);
      if (isInitialLoad) setError('');
    } catch {
      setError(t('Failed to load merge entry.'));
    } finally {
      setLoading(false);
    }
  }, [id, t]);

  useEffect(() => { load(); }, [load]);

  function handleProcess() {
    if (!entry) return;
    setConfirm({
      open: true,
      title: t('Process Entry'),
      message: t('Process merge entry for branch "{{branchName}}"?', { branchName: entry.branchName }),
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        try {
          await processMergeEntry(entry.id);
          pushToast('success', t('Merge entry {{id}} processing started.', { id: entry.id }));
          load();
        } catch { setError(t('Process failed.')); }
      },
    });
  }

  function handleCancel() {
    if (!entry) return;
    setConfirm({
      open: true,
      title: t('Cancel Entry'),
      message: t('Cancel merge entry for branch "{{branchName}}"?', { branchName: entry.branchName }),
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        try {
          await cancelMergeEntry(entry.id);
          pushToast('warning', t('Merge entry {{id}} cancelled.', { id: entry.id }));
          load();
        } catch { setError(t('Cancel failed.')); }
      },
    });
  }

  function handleDelete() {
    if (!entry) return;
    setConfirm({
      open: true,
      title: t('Delete Entry'),
      message: t('Delete merge entry {{id}}? This cannot be undone.', { id: entry.id }),
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        try {
          await deleteMergeEntry(entry.id);
          pushToast('warning', t('Merge entry {{id}} deleted.', { id: entry.id }));
          navigate('/merge-queue');
        } catch { setError(t('Delete failed.')); }
      },
    });
  }

  // Mission diff/log shortcuts
  async function handleMissionDiff() {
    if (!entry?.missionId) return;
    setDiffModal({ open: true, title: t('Diff: Mission {{id}}...', { id: entry.missionId.substring(0, 8) }), rawDiff: '', loading: true });
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
      setLogModal(l => ({ ...l, content: result.log || t('No log output'), totalLines: result.totalLines || 0 }));
    } catch (e: unknown) {
      setLogModal(l => ({ ...l, content: t('Log unavailable: {{message}}', { message: e instanceof Error ? e.message : String(e) }) }));
    }
  }, [t]);

  function handleMissionLog() {
    if (!entry?.missionId) return;
    setLogModal({ open: true, title: t('Log: Mission {{id}}...', { id: entry.missionId.substring(0, 8) }), missionId: entry.missionId, content: t('Loading...'), totalLines: 0, lineCount: 200 });
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

  if (loading) return <p className="text-dim">{t('Loading...')}</p>;
  if (error && !entry) return <ErrorModal error={error} onClose={() => setError('')} />;
  if (!entry) return <p className="text-dim">{t('Merge entry not found.')}</p>;

  const actionItems = [
    { label: 'View JSON', onClick: () => setJsonData({ open: true, title: t('Merge Entry: {{id}}', { id: entry.id }), data: entry }) },
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
        <Link to="/merge-queue">{t('Merge Queue')}</Link> <span className="breadcrumb-sep">&gt;</span> <span className="mono">{entry.id}</span>
      </div>

      <div className="detail-header">
        <h2>{t('Merge Entry')}</h2>
        <div className="inline-actions">
          {entry.missionId && (
            <>
              <button className="btn btn-sm" onClick={handleMissionDiff} title={t('View mission diff')}>{t('Diff')}</button>
              <button className="btn btn-sm" onClick={handleMissionLog} title={t('View mission log')}>{t('Log')}</button>
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
          <span className="detail-label">{t('ID')}</span>
          <span className="id-display">
            <span className="mono">{entry.id}</span>
            <CopyButton text={entry.id} />
          </span>
        </div>
        <div className="detail-field"><span className="detail-label">{t('Status')}</span><StatusBadge status={entry.status} /></div>
        <div className="detail-field"><span className="detail-label">{t('Branch')}</span><span className="mono">{entry.branchName}</span></div>
        <div className="detail-field"><span className="detail-label">{t('Target Branch')}</span><span className="mono">{entry.targetBranch}</span></div>
        <div className="detail-field"><span className="detail-label">{t('Priority')}</span><span>{entry.priority}</span></div>
        <div className="detail-field">
          <span className="detail-label">{t('Vessel')}</span>
          {entry.vesselId
            ? <Link to={`/vessels/${entry.vesselId}`}>{vesselName(entry.vesselId)}</Link>
            : <span>-</span>}
        </div>
        <div className="detail-field">
          <span className="detail-label">{t('Mission')}</span>
          {entry.missionId
            ? <Link to={`/missions/${entry.missionId}`} className="mono">{entry.missionId}</Link>
            : <span>-</span>}
        </div>
        <div className="detail-field"><span className="detail-label">{t('Batch ID')}</span><span className="mono">{entry.batchId || '-'}</span></div>
        <div className="detail-field"><span className="detail-label">{t('Test Exit Code')}</span><span>{entry.testExitCode ?? '-'}</span></div>
        <div className="detail-field"><span className="detail-label">{t('Tenant ID')}</span><span className="mono">{entry.tenantId || '-'}</span></div>
        <div className="detail-field"><span className="detail-label">{t('Test Command')}</span><span className="mono">{entry.testCommand || '-'}</span></div>
        <div className="detail-field">
          <span className="detail-label">{t('Created')}</span>
          <span>{formatRelativeTime(entry.createdUtc)} <span className="text-dim">({formatDateTime(entry.createdUtc)})</span></span>
        </div>
        <div className="detail-field">
          <span className="detail-label">{t('Test Started')}</span>
          <span>{entry.testStartedUtc ? <>{formatRelativeTime(entry.testStartedUtc)} <span className="text-dim">({formatDateTime(entry.testStartedUtc)})</span></> : '-'}</span>
        </div>
        <div className="detail-field">
          <span className="detail-label">{t('Completed')}</span>
          <span>{entry.completedUtc ? <>{formatRelativeTime(entry.completedUtc)} <span className="text-dim">({formatDateTime(entry.completedUtc)})</span></> : '-'}</span>
        </div>
        <div className="detail-field">
          <span className="detail-label">{t('Last Updated')}</span>
          <span>{formatRelativeTime(entry.lastUpdateUtc)} <span className="text-dim">({formatDateTime(entry.lastUpdateUtc)})</span></span>
        </div>
      </div>

      {/* Test Command */}
      {entry.testCommand && (
        <div className="detail-context-section">
          <h4>{t('Test Command')}</h4>
          <pre className="detail-context-block">{entry.testCommand}</pre>
        </div>
      )}

      {/* Test Output */}
      {entry.testOutput && (
        <div className="detail-context-section">
          <h4>{t('Test Output')}</h4>
          <pre className="detail-context-block">{entry.testOutput}</pre>
        </div>
      )}
    </div>
  );
}
