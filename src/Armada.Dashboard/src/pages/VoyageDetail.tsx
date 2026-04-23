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
import type { Voyage, Mission, Vessel, Captain, MissionPlaybookSnapshot, SelectedPlaybook } from '../types/models';
import StatusBadge from '../components/shared/StatusBadge';
import ErrorModal from '../components/shared/ErrorModal';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import JsonViewer from '../components/shared/JsonViewer';
import DiffViewer from '../components/shared/DiffViewer';
import LogViewer from '../components/shared/LogViewer';
import CopyButton from '../components/shared/CopyButton';
import { useLocale } from '../context/LocaleContext';
import { useNotifications } from '../context/NotificationContext';

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

function formatDeliveryMode(value: string): string {
  return value
    .replace(/([a-z])([A-Z])/g, '$1 $2')
    .replace('Into Worktree', 'Into Worktree')
    .trim();
}

// ── Main component ──

export default function VoyageDetail() {
  const { t, formatDateTime, formatRelativeTime } = useLocale();
  const { pushToast } = useNotifications();
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
      setError(t('Failed to load voyage: {{message}}', { message: e instanceof Error ? e.message : String(e) }));
    } finally {
      setLoading(false);
    }
  }, [id, t]);

  useEffect(() => {
    loadVoyage();
    listVessels({ pageSize: 1000 }).then(r => setVessels(r.objects || [])).catch(() => {});
    listCaptains({ pageSize: 1000 }).then(r => setCaptains(r.objects || [])).catch(() => {});
  }, [loadVoyage]);

  // Progress
  const completedCount = missions.filter(m => m.status === 'Complete').length;
  const failedCount = missions.filter(m => m.status === 'Failed').length;
  const progressPct = missions.length > 0 ? Math.round((completedCount / missions.length) * 100) : 0;
  const voyageSnapshots: MissionPlaybookSnapshot[] = missions[0]?.playbookSnapshots || [];
  const voyageSelections: SelectedPlaybook[] = voyage?.selectedPlaybooks || [];

  // Actions
  function handleCancel() {
    if (!voyage) return;
    setConfirm({
      open: true,
      title: t('Cancel Voyage'),
      message: t('Cancel this voyage? All pending missions will be cancelled.'),
      danger: true,
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        try {
          await cancelVoyage(voyage.id);
          pushToast('warning', t('Voyage "{{title}}" cancelled.', { title: voyage.title || voyage.id }));
          loadVoyage();
        } catch (e: unknown) {
          setError(t('Cancel failed: {{message}}', { message: e instanceof Error ? e.message : String(e) }));
        }
      },
    });
  }

  function handleDelete() {
    if (!voyage) return;
    setConfirm({
      open: true,
      title: t('Delete Voyage'),
      message: t('Permanently delete this voyage and all its missions? This cannot be undone.'),
      danger: true,
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        try {
          await purgeVoyage(voyage.id);
          pushToast('warning', t('Voyage "{{title}}" deleted.', { title: voyage.title || voyage.id }));
          nav('/voyages');
        } catch (e: unknown) {
          setError(t('Delete failed: {{message}}', { message: e instanceof Error ? e.message : String(e) }));
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
      title: t('Retry Failed Missions'),
      message: t('Retry {{count}} failed mission(s)? New missions will be created with the same parameters.', { count: failed.length }),
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
          pushToast('success', t('Retried {{count}} failed mission(s).', { count: failed.length }));
          loadVoyage();
        } catch (e: unknown) {
          setError(t('Retry failed: {{message}}', { message: e instanceof Error ? e.message : String(e) }));
        }
      },
    });
  }

  function handleViewJson() {
    if (!voyage) return;
    setJsonData({ open: true, title: t('Voyage: {{title}}', { title: voyage.title || voyage.id }), data: voyage });
  }

  async function handleMissionDiff(missionId: string, title: string) {
    setDiffModal({ open: true, title: t('Diff: {{title}}', { title }), rawDiff: '', loading: true });
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
      setLogModal(l => ({ ...l, content: result.log || t('No log output'), totalLines: result.totalLines || 0 }));
    } catch (e: unknown) {
      setLogModal(l => ({ ...l, content: t('Log unavailable: {{message}}', { message: e instanceof Error ? e.message : String(e) }) }));
    }
  }, [t]);

  function handleMissionLog(missionId: string, title: string) {
    setLogModal({ open: true, title: t('Log: {{title}}', { title }), missionId, content: t('Loading...'), totalLines: 0, lineCount: 200 });
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

  if (loading) return <p className="text-muted">{t('Loading...')}</p>;
  if (!voyage) return <ErrorModal error={error || t('Voyage not found.')} onClose={() => nav('/voyages')} />;

  return (
    <div>
      {/* Breadcrumb */}
      <div style={{ display: 'flex', gap: 8, alignItems: 'center', marginBottom: 16, fontSize: 13 }}>
        <Link to="/voyages">{t('Voyages')}</Link>
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
          <button className="btn-sm btn-danger" onClick={handleCancel}>{t('Cancel Voyage')}</button>
        )}
        {failedCount > 0 && (
          <button className="btn-sm" onClick={handleRetryFailed}>{t('Retry Failed')} ({failedCount})</button>
        )}
        <button className="btn-sm" onClick={handleViewJson}>{t('View JSON')}</button>
        {voyage.status !== 'Open' && voyage.status !== 'InProgress' && (
          <button className="btn-sm btn-danger" onClick={handleDelete}>{t('Delete')}</button>
        )}
      </div>

      {/* Detail grid */}
      <div className="card-grid" style={{ marginBottom: 20 }}>
        <div className="card">
          <h3>{t('Status')}</h3>
          <StatusBadge status={voyage.status} />
        </div>

        <div className="card">
          <h3>{t('Details')}</h3>
          <div style={{ display: 'grid', gap: 8 }}>
            <div><span className="text-muted" style={{ fontSize: 12 }}>{t('ID')}</span>
              <div className="mono" style={{ fontSize: 12, display: 'flex', alignItems: 'center', gap: 4 }}>
                {voyage.id}
                <CopyButton text={voyage.id} />
              </div>
            </div>
            <div><span className="text-muted" style={{ fontSize: 12 }}>{t('Description')}</span><div>{voyage.description || '-'}</div></div>
          </div>
        </div>

        <div className="card">
          <h3>{t('Configuration')}</h3>
          <div style={{ display: 'grid', gap: 8 }}>
            <div><span className="text-muted" style={{ fontSize: 12 }}>{t('Auto-Push')}</span><div>{voyage.autoPush != null ? (voyage.autoPush ? t('Yes') : t('No')) : '-'}</div></div>
            <div><span className="text-muted" style={{ fontSize: 12 }}>{t('Auto-Create PRs')}</span><div>{voyage.autoCreatePullRequests != null ? (voyage.autoCreatePullRequests ? t('Yes') : t('No')) : '-'}</div></div>
            <div><span className="text-muted" style={{ fontSize: 12 }}>{t('Auto-Merge PRs')}</span><div>{voyage.autoMergePullRequests != null ? (voyage.autoMergePullRequests ? t('Yes') : t('No')) : '-'}</div></div>
            <div><span className="text-muted" style={{ fontSize: 12 }}>{t('Landing Mode')}</span><div>{voyage.landingMode || '-'}</div></div>
          </div>
        </div>

        <div className="card">
          <h3>{t('Timestamps')}</h3>
          <div style={{ display: 'grid', gap: 8 }}>
            <div>
              <span className="text-muted" style={{ fontSize: 12 }}>{t('Created')}</span>
              <div>{formatDateTime(voyage.createdUtc)} <span className="text-muted" style={{ fontSize: 11 }}>({formatRelativeTime(voyage.createdUtc)})</span></div>
            </div>
            {voyage.completedUtc && (
              <div>
                <span className="text-muted" style={{ fontSize: 12 }}>{t('Completed')}</span>
                <div>{formatDateTime(voyage.completedUtc)} <span className="text-muted" style={{ fontSize: 11 }}>({formatRelativeTime(voyage.completedUtc)})</span></div>
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
            {t('{{completed}}/{{total}} complete, {{failed}} failed', { completed: completedCount, total: missions.length, failed: failedCount })}
          </span>
        </div>
      )}

      {(voyageSnapshots.length > 0 || voyageSelections.length > 0) && (
        <div className="card playbook-voyage-card" style={{ marginBottom: 20 }}>
          <div className="playbook-voyage-header">
            <div>
              <h3>{t('Playbooks')}</h3>
              <p className="text-dim">
                {voyageSnapshots.length > 0
                  ? t('These snapshots show the actual playbook content and delivery mode that were applied to the voyage missions.')
                  : t('This voyage has playbook selections recorded, but mission snapshots are not available yet.')}
              </p>
            </div>
          </div>

          <div className="playbook-voyage-list">
            {voyageSnapshots.length > 0 ? voyageSnapshots.map((snapshot, index) => (
              <div key={`${snapshot.fileName}-${index}`} className="playbook-voyage-item">
                <div>
                  <strong>{snapshot.fileName}</strong>
                  <div className="text-dim">{snapshot.description || t('No description')}</div>
                </div>
                <div className="playbook-voyage-item-meta">
                  <StatusBadge status={formatDeliveryMode(snapshot.deliveryMode)} />
                  <span className="mono text-dim">{snapshot.worktreeRelativePath || snapshot.resolvedPath || '-'}</span>
                </div>
              </div>
            )) : voyageSelections.map((selection, index) => (
              <div key={`${selection.playbookId}-${index}`} className="playbook-voyage-item">
                <div>
                  <strong>{selection.playbookId}</strong>
                  <div className="text-dim">{t('Playbook details are available after mission snapshots are created.')}</div>
                </div>
                <div className="playbook-voyage-item-meta">
                  <StatusBadge status={formatDeliveryMode(selection.deliveryMode)} />
                </div>
              </div>
            ))}
          </div>
        </div>
      )}

      {/* Missions table */}
      <h3 style={{ marginBottom: 12 }}>{t('Missions')}</h3>
      {missions.length > 0 ? (
        <table className="table">
          <thead>
            <tr>
              <th>{t('Mission')}</th>
              <th>{t('Status')}</th>
              <th>{t('Vessel')}</th>
              <th>{t('Captain')}</th>
              <th>{t('Branch')}</th>
              <th style={{ width: 120 }}>{t('Actions')}</th>
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
                    <button className="btn-sm" onClick={() => handleMissionDiff(m.id, m.title)} title={t('View Diff')}>{t('Diff')}</button>
                    <button className="btn-sm" onClick={() => handleMissionLog(m.id, m.title)} title={t('View Log')}>{t('Log')}</button>
                    <button className="btn-sm" onClick={() => nav(`/missions/${m.id}`)} title={t('View Detail')}>{t('Detail')}</button>
                  </div>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      ) : (
        <p className="text-muted">{t('No missions in this voyage.')}</p>
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
