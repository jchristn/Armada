import { useEffect, useState, useMemo, useCallback, useRef } from 'react';
import { useNotifications } from '../context/NotificationContext';
import { useParams, useNavigate, Link } from 'react-router-dom';
import {
  getMission,
  updateMission,
  deleteMission,
  purgeMission,
  getMissionDiff,
  getMissionLog,
  getMissionInstructions,
  restartMission,
  retryMissionLanding,
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
import CopyButton from '../components/shared/CopyButton';
import { useLocale } from '../context/LocaleContext';

const MISSION_STATUSES = [
  'Pending', 'Assigned', 'InProgress', 'WorkProduced', 'Testing', 'Review', 'Complete', 'Failed', 'LandingFailed', 'Cancelled',
];

export default function MissionDetail() {
  const { t, formatDateTime, formatRelativeTime } = useLocale();
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();

  const [mission, setMission] = useState<Mission | null>(null);
  const [vessels, setVessels] = useState<Vessel[]>([]);
  const [captains, setCaptains] = useState<Captain[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const { pushToast } = useNotifications();

  // Diff viewer (shared modal)
  const [diffModal, setDiffModal] = useState<{ open: boolean; title: string; rawDiff: string; loading: boolean }>({ open: false, title: '', rawDiff: '', loading: false });

  // Log viewer (shared modal)
  const [logModal, setLogModal] = useState<{ open: boolean; title: string; missionId: string; content: string; totalLines: number; lineCount: number }>({ open: false, title: '', missionId: '', content: '', totalLines: 0, lineCount: 200 });
  const [instructionsModal, setInstructionsModal] = useState<{ open: boolean; title: string; content: string }>({ open: false, title: '', content: '' });
  const missionLoadedRef = useRef(false);

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

  const formatDuration = useCallback((totalRuntimeMs: number | null | undefined): string => {
    if (totalRuntimeMs == null || totalRuntimeMs < 0) return t('N/A');
    const totalSeconds = totalRuntimeMs / 1000;
    if (totalSeconds < 60) return t('{{seconds}}s', { seconds: totalSeconds.toFixed(1) });

    const hours = Math.floor(totalSeconds / 3600);
    const minutes = Math.floor((totalSeconds % 3600) / 60);
    const seconds = Math.floor(totalSeconds % 60);

    if (hours > 0) return t('{{hours}}h {{minutes}}m', { hours, minutes });
    if (minutes > 0 && seconds > 0) return t('{{minutes}}m {{seconds}}s', { minutes, seconds });
    return t('{{minutes}}m', { minutes });
  }, [t]);

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
    // Only show loading spinner on initial load, not background refreshes
    const isInitialLoad = !missionLoadedRef.current;
    if (isInitialLoad) setLoading(true);
    try {
      const m = await getMission(id);
      setMission(m);
      missionLoadedRef.current = true;
      // Only clear error on initial load -- don't dismiss user-facing errors from actions
      if (isInitialLoad) setError('');
    } catch (e: unknown) {
      if (isInitialLoad) setError(t('Failed to load mission: {{message}}', { message: e instanceof Error ? e.message : String(e) }));
    } finally {
      setLoading(false);
    }
  }, [id, t]);

  useEffect(() => {
    loadMission();
  }, [loadMission]);

  useEffect(() => {
    listVessels({ pageSize: 1000 }).then(r => setVessels(r.objects || [])).catch(() => {});
    listCaptains({ pageSize: 1000 }).then(r => setCaptains(r.objects || [])).catch(() => {});
  }, []);

  async function handleViewDiff() {
    if (!id) return;
    setDiffModal({ open: true, title: t('Diff: {{title}}', { title: mission?.title || id }), rawDiff: '', loading: true });
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
      setLogModal(l => ({ ...l, content: result.log || t('No log output'), totalLines: result.totalLines || 0 }));
    } catch (e: unknown) {
      // Don't replace existing log content on transient fetch failure
      setLogModal(l => ({
        ...l,
        content: l.content && l.content !== t('Loading...')
          ? l.content
          : t('Log unavailable: {{message}}', { message: e instanceof Error ? e.message : String(e) })
      }));
    }
  }, [t]);

  function handleViewLog() {
    if (!id) return;
    setLogModal({ open: true, title: t('Log: {{title}}', { title: mission?.title || id }), missionId: id, content: t('Loading...'), totalLines: 0, lineCount: 200 });
    fetchLog(id, 200);
  }

  async function handleViewInstructions() {
    if (!id) return;
    setInstructionsModal({ open: true, title: t('Mission Instructions'), content: t('Loading...') });
    try {
      const result = await getMissionInstructions(id);
      setInstructionsModal({
        open: true,
        title: t('Instructions: {{fileName}}', { fileName: result.fileName || t('Mission Instructions') }),
        content: result.content || t('No mission instructions found.'),
      });
    } catch (e: unknown) {
      setInstructionsModal({
        open: true,
        title: t('Mission Instructions'),
        content: t('Instructions unavailable: {{message}}', { message: e instanceof Error ? e.message : String(e) }),
      });
    }
  }

  // Use a ref for loadMission so the log refresh callback identity stays stable
  const loadMissionRef = useRef(loadMission);
  loadMissionRef.current = loadMission;
  const logRefreshCountRef = useRef(0);
  const handleLogRefresh = useCallback(() => {
    if (logModal.missionId) fetchLog(logModal.missionId, logModal.lineCount);
    // Refresh mission status every 5th poll (~5 seconds) to detect completion
    logRefreshCountRef.current++;
    if (logRefreshCountRef.current % 5 === 0) loadMissionRef.current();
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
      pushToast('success', t('Mission "{{title}}" saved.', { title: editTitle }));
      loadMission();
    } catch (e: unknown) {
      setError(t('Save failed: {{message}}', { message: e instanceof Error ? e.message : String(e) }));
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
      pushToast('success', t('Mission "{{title}}" moved to {{status}}.', { title: mission.title, status: transitionStatus }));
      loadMission();
    } catch (e: unknown) {
      setError(t('Transition failed: {{message}}', { message: e instanceof Error ? e.message : String(e) }));
    }
  }

  function handleRestart() {
    if (!mission) return;
    setConfirm({
      open: true,
      title: t('Restart Mission'),
      message: t('Restart mission "{{title}}"? This will reset the mission to Pending status.', { title: mission.title }),
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        try {
          await restartMission(mission.id);
          pushToast('success', t('Mission "{{title}}" restarted.', { title: mission.title }));
          loadMission();
        } catch (e: unknown) {
          setError(t('Restart failed: {{message}}', { message: e instanceof Error ? e.message : String(e) }));
        }
      },
    });
  }

  function handlePurge() {
    if (!mission) return;
    setConfirm({
      open: true,
      title: t('Purge Mission'),
      message: t('Purge mission "{{title}}"? This will clean up all associated resources (branches, worktrees, etc.) and cannot be undone.', { title: mission.title }),
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        try {
          await purgeMission(mission.id);
          pushToast('warning', t('Mission "{{title}}" purged.', { title: mission.title }));
          loadMission();
        } catch (e: unknown) {
          setError(t('Purge failed: {{message}}', { message: e instanceof Error ? e.message : String(e) }));
        }
      },
    });
  }

  function handleDelete() {
    if (!mission) return;
    setConfirm({
      open: true,
      title: t('Delete Mission'),
      message: t('Permanently delete mission "{{title}}"? This cannot be undone.', { title: mission.title }),
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        try {
          await deleteMission(mission.id);
          pushToast('warning', t('Mission "{{title}}" deleted.', { title: mission.title }));
          navigate('/missions');
        } catch (e: unknown) {
          setError(t('Delete failed: {{message}}', { message: e instanceof Error ? e.message : String(e) }));
        }
      },
    });
  }

  if (loading) return <p className="text-dim">{t('Loading...')}</p>;
  if (!mission) return <ErrorModal error={error || t('Mission not found.')} onClose={() => navigate('/missions')} />;

  return (
    <div>
      {/* Breadcrumb */}
      <div className="breadcrumb">
        <Link to="/missions">{t('Missions')}</Link> <span className="breadcrumb-sep">&gt;</span> <span>{mission.title}</span>
      </div>

      <div className="detail-header">
        <h2>{mission.title}</h2>
        <div className="inline-actions">
          <button className="btn btn-sm" onClick={handleViewDiff} title={t('View mission diff')}>{t('Diff')}</button>
          <button className="btn btn-sm" onClick={handleViewLog} title={t('View mission log')}>{t('Log')}</button>
          <button className="btn btn-sm" onClick={handleViewInstructions} title={t('View mission instructions')}>{t('Instructions')}</button>
          {(mission.status === 'WorkProduced' || mission.status === 'LandingFailed') && (
            <button className="btn btn-sm btn-primary" onClick={async () => { try { await retryMissionLanding(mission.id); pushToast('success', t('Landing succeeded! Mission status updated.')); loadMission(); } catch (e) { setError(e instanceof Error ? e.message : t('Retry landing failed.')); } }} title={t('Rebase the mission branch and re-attempt merge into the target branch')}>{t('Retry Landing')}</button>
          )}
          <ActionMenu id={`mission-action-${mission.id}`} items={[
            { label: 'Edit', onClick: openEdit },
            { label: 'View Diff', onClick: handleViewDiff },
            { label: 'View Log', onClick: handleViewLog },
            { label: 'View Instructions', onClick: handleViewInstructions },
            { label: 'Transition Status', onClick: () => setShowTransition(true) },
            { label: 'View JSON', onClick: () => setJsonData({ open: true, title: t('Mission: {{title}}', { title: mission.title }), data: mission }) },
            { label: 'Restart', onClick: handleRestart },
            ...((mission.status === 'WorkProduced' || mission.status === 'LandingFailed') ? [{ label: 'Retry Landing', onClick: async () => { try { await retryMissionLanding(mission.id); pushToast('success', t('Landing succeeded! Mission status updated.')); loadMission(); } catch (e) { setError(e instanceof Error ? e.message : t('Retry landing failed.')); } } }] : []),
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
        completed={mission != null && ['Complete', 'Failed', 'Cancelled', 'WorkProduced', 'LandingFailed'].includes(mission.status)}
        onClose={() => setLogModal({ open: false, title: '', missionId: '', content: '', totalLines: 0, lineCount: 200 })}
        onRefresh={handleLogRefresh}
        onLineCountChange={handleLogLineCountChange}
      />
      <LogViewer
        open={instructionsModal.open}
        title={instructionsModal.title}
        content={instructionsModal.content}
        completed={true}
        onClose={() => setInstructionsModal({ open: false, title: '', content: '' })}
      />

      {/* Edit Modal */}
      {editModal && (
        <div className="modal-overlay" onClick={() => setEditModal(false)}>
          <form className="modal" onClick={e => e.stopPropagation()} onSubmit={handleSaveEdit}>
            <h3>{t('Edit Mission')}</h3>
            <label>{t('Title')}<input value={editTitle} onChange={e => setEditTitle(e.target.value)} required /></label>
            <label>{t('Description')}<textarea value={editDescription} onChange={e => setEditDescription(e.target.value)} rows={3} /></label>
            <label>{t('Priority')}<input type="number" value={editPriority} onChange={e => setEditPriority(Number(e.target.value))} min={0} max={1000} /></label>
            <div className="modal-actions">
              <button type="submit" className="btn btn-primary" disabled={editSaving}>{editSaving ? t('Saving...') : t('Save')}</button>
              <button type="button" className="btn" onClick={() => setEditModal(false)}>{t('Cancel')}</button>
            </div>
          </form>
        </div>
      )}

      {/* Transition Modal */}
      {showTransition && (
        <div className="modal-overlay" onClick={() => setShowTransition(false)}>
          <div className="modal" onClick={e => e.stopPropagation()}>
            <h3>{t('Transition Mission Status')}</h3>
            <p className="text-dim">{t('Current status')}: <StatusBadge status={mission.status} /></p>
            <label style={{ marginTop: 12 }}>
              {t('New Status')}
              <select value={transitionStatus} onChange={e => setTransitionStatus(e.target.value)}>
                <option value="">{t('Select status...')}</option>
                {MISSION_STATUSES.map(s => (
                  <option key={s} value={s}>{t(s)}</option>
                ))}
              </select>
            </label>
            <div className="modal-actions">
              <button className="btn btn-primary" onClick={handleTransition} disabled={!transitionStatus}>{t('Transition')}</button>
              <button className="btn" onClick={() => setShowTransition(false)}>{t('Cancel')}</button>
            </div>
          </div>
        </div>
      )}

      {/* Mission Info */}
      <div className="detail-grid" style={{ gridTemplateColumns: '1fr 1fr 1fr 1fr' }}>
        <div className="detail-field">
          <span className="detail-label">{t('ID')}</span>
          <span className="id-display">
            <span className="mono">{mission.id}</span>
            <CopyButton text={mission.id} />
          </span>
        </div>
        <div className="detail-field"><span className="detail-label">{t('Tenant ID')}</span><span className="mono">{mission.tenantId || '-'}</span></div>
        <div className="detail-field">
          <span className="detail-label">{t('Status')}</span>
          <StatusBadge status={mission.status} />
        </div>
        {mission.failureReason && (
          <div className="detail-field" style={{ gridColumn: '1 / -1' }}>
            <span className="detail-label">{t('Failure Reason')}</span>
            <pre style={{
              margin: 0,
              padding: '0.75rem',
              background: 'rgba(255, 80, 80, 0.08)',
              border: '1px solid rgba(255, 80, 80, 0.2)',
              borderRadius: '4px',
              color: 'var(--text)',
              fontSize: '0.85em',
              whiteSpace: 'pre-wrap',
              wordBreak: 'break-word',
              fontFamily: 'monospace'
            }}>{mission.failureReason}</pre>
          </div>
        )}
        <div className="detail-field"><span className="detail-label">{t('Priority')}</span><span>{mission.priority}</span></div>
        <div className="detail-field">
          <span className="detail-label">{t('Voyage')}</span>
          {mission.voyageId
            ? <Link to={`/voyages/${mission.voyageId}`} className="mono">{mission.voyageId}</Link>
            : <span>-</span>}
        </div>
        <div className="detail-field">
          <span className="detail-label">{t('Vessel')}</span>
          {mission.vesselId
            ? <Link to={`/vessels/${mission.vesselId}`}>{vesselName(mission.vesselId)}</Link>
            : <span>-</span>}
        </div>
        <div className="detail-field">
          <span className="detail-label">{t('Captain')}</span>
          {mission.captainId
            ? <Link to={`/captains/${mission.captainId}`}>{captainName(mission.captainId)}</Link>
            : <span>-</span>}
        </div>
        <div className="detail-field">
          <span className="detail-label">{t('Parent Mission')}</span>
          {mission.parentMissionId
            ? <Link to={`/missions/${mission.parentMissionId}`} className="mono">{mission.parentMissionId}</Link>
            : <span>-</span>}
        </div>
        <div className="detail-field">
          <span className="detail-label">{t('Persona')}</span>
          <span>{mission.persona || <span className="text-dim">{t('Worker')}</span>}</span>
        </div>
        {mission.dependsOnMissionId && (
          <div className="detail-field">
            <span className="detail-label">{t('Depends On')}</span>
            <Link to={`/missions/${mission.dependsOnMissionId}`} className="mono">{mission.dependsOnMissionId}</Link>
          </div>
        )}
        {mission.status === 'WorkProduced' && mission.dependsOnMissionId === null && mission.persona && mission.persona !== 'Worker' && (
          <div className="detail-field" style={{ gridColumn: '1 / -1' }}>
            <span className="detail-label">{t('Pipeline Status')}</span>
            <span className="text-dim">{t('Work complete -- handed off to the next pipeline stage')}</span>
          </div>
        )}
        <div className="detail-field"><span className="detail-label">{t('Branch Name')}</span><span className="mono">{mission.branchName || '-'}</span></div>
        <div className="detail-field">
          <span className="detail-label">{t('Dock')}</span>
          {mission.dockId
            ? <Link to={`/docks/${mission.dockId}`} className="mono">{mission.dockId}</Link>
            : <span>-</span>}
        </div>
        <div className="detail-field"><span className="detail-label">{t('Process ID')}</span><span>{mission.processId ?? '-'}</span></div>
        <div className="detail-field">
          <span className="detail-label">{t('PR URL')}</span>
          {mission.prUrl
            ? <a href={mission.prUrl} target="_blank" rel="noopener noreferrer">{mission.prUrl}</a>
            : <span>-</span>}
        </div>
        <div className="detail-field"><span className="detail-label">{t('Commit Hash')}</span><span className="mono">{mission.commitHash || '-'}</span></div>
        <div className="detail-field">
          <span className="detail-label">{t('Created')}</span>
          <span title={mission.createdUtc}>
            {formatRelativeTime(mission.createdUtc)}
            <span className="text-dim"> ({formatDateTime(mission.createdUtc)})</span>
          </span>
        </div>
        <div className="detail-field">
          <span className="detail-label">{t('Started')}</span>
          <span title={formatDateTime(mission.startedUtc)}>
            {formatRelativeTime(mission.startedUtc) || '-'}
            {mission.startedUtc && <span className="text-dim"> ({formatDateTime(mission.startedUtc)})</span>}
          </span>
        </div>
        <div className="detail-field">
          <span className="detail-label">{t('Completed')}</span>
          <span title={formatDateTime(mission.completedUtc)}>
            {formatRelativeTime(mission.completedUtc) || '-'}
            {mission.completedUtc && <span className="text-dim"> ({formatDateTime(mission.completedUtc)})</span>}
          </span>
        </div>
        <div className="detail-field">
          <span className="detail-label">{t('Total Runtime')}</span>
          <span>{formatDuration(mission.totalRuntimeMs)}</span>
        </div>
        <div className="detail-field">
          <span className="detail-label">{t('Last Updated')}</span>
          <span title={mission.lastUpdateUtc}>
            {formatRelativeTime(mission.lastUpdateUtc)}
            <span className="text-dim"> ({formatDateTime(mission.lastUpdateUtc)})</span>
          </span>
        </div>
      </div>

      {/* Description */}
      {mission.description && (
        <div style={{ marginTop: '1rem' }}>
          <h3>{t('Description')}</h3>
          <div className="card" style={{ padding: '1rem', whiteSpace: 'pre-wrap' }}>{mission.description}</div>
        </div>
      )}

      {mission.playbookSnapshots && mission.playbookSnapshots.length > 0 && (
        <div style={{ marginTop: '1rem' }}>
          <h3>{t('Playbooks')}</h3>
          <div className="playbook-snapshot-list">
            {mission.playbookSnapshots.map((snapshot, index) => (
              <div key={`${snapshot.fileName}-${index}`} className="playbook-snapshot-card">
                <div className="playbook-snapshot-header">
                  <div>
                    <strong>{snapshot.fileName}</strong>
                    <div className="text-dim">{snapshot.description || t('No description')}</div>
                  </div>
                  <StatusBadge status={snapshot.deliveryMode.replace(/([a-z])([A-Z])/g, '$1 $2')} />
                </div>

                <div className="playbook-snapshot-meta">
                  <span><strong>{t('Resolved Path')}:</strong> <span className="mono">{snapshot.resolvedPath || '-'}</span></span>
                  <span><strong>{t('Worktree Path')}:</strong> <span className="mono">{snapshot.worktreeRelativePath || '-'}</span></span>
                  <span><strong>{t('Source Updated')}:</strong> {snapshot.sourceLastUpdateUtc ? formatDateTime(snapshot.sourceLastUpdateUtc) : '-'}</span>
                </div>

                <pre className="playbook-snapshot-content">{snapshot.content}</pre>
              </div>
            ))}
          </div>
        </div>
      )}
    </div>
  );
}
