import ActionMenu from '../components/shared/ActionMenu';
import { useEffect, useState, useMemo, useCallback, useRef } from 'react';
import { useParams, useNavigate, Link } from 'react-router-dom';
import {
  getVoyage,
  getMission,
  purgeVoyage,
  cancelVoyage,
  listMissions,
  getMissionDiff,
  getMissionLog,
  createMission,
  listVessels,
  listCaptains,
} from '../api/client';
import { listAll } from '../api/paging';
import type { Voyage, VoyageProgress, Mission, Vessel, Captain, MissionPlaybookSnapshot, SelectedPlaybook, CaptainAssignmentOverride } from '../types/models';
import CaptainRef from '../components/shared/CaptainRef';
import StatusBadge from '../components/shared/StatusBadge';
import EvaluationBadge from '../components/shared/EvaluationBadge';
import ErrorModal from '../components/shared/ErrorModal';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import JsonViewer from '../components/shared/JsonViewer';
import DiffViewer from '../components/shared/DiffViewer';
import LogViewer from '../components/shared/LogViewer';
import CopyButton from '../components/shared/CopyButton';
import DataTable from '../components/shared/DataTable';
import { useLocale } from '../context/LocaleContext';
import { useNotifications } from '../context/NotificationContext';
import { useLiveRefresh } from '../lib/useLiveRefresh';
import { findLandingMode, getVoyageLandingModes } from '../lib/vesselForm';
import { formatDeliveryMode, isVoyageActive, parseCaptainOverrides, retryMissionPayload, splitVoyageResponse, voyageProgressFromDetail, VOYAGE_MISSION_PAGE_SIZE } from '../lib/voyageDetail';
import { useDeleteProgress } from '../lib/useDeleteProgress';

// ── Main component ──

export default function VoyageDetail() {
  const { t, formatDateTime, formatRelativeTime } = useLocale();
  const deletion = useDeleteProgress();
  const { pushToast } = useNotifications();
  const { id } = useParams<{ id: string }>();
  const nav = useNavigate();

  const [voyage, setVoyage] = useState<Voyage | null>(null);
  const [detail, setDetail] = useState<VoyageProgress | null>(null);
  // One server page of the voyage's missions (oldest first); the voyage detail carries the counts.
  const [missions, setMissions] = useState<Mission[]>([]);
  const [missionPage, setMissionPage] = useState({ pageNumber: 1, pageSize: VOYAGE_MISSION_PAGE_SIZE, totalPages: 0, totalRecords: 0 });
  const [voyageSnapshots, setVoyageSnapshots] = useState<MissionPlaybookSnapshot[]>([]);
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

  const loadedVoyageIdRef = useRef<string | null>(null);
  const requestGenerationRef = useRef(0);
  const loadVoyage = useCallback(async () => {
    if (!id) return;
    const generation = ++requestGenerationRef.current;
    const routeId = id;
    const isNewRoute = loadedVoyageIdRef.current !== routeId;
    const current = () => generation === requestGenerationRef.current;
    // Show the loading state only when the route changes. A live refresh updates the page in place.
    if (isNewRoute) {
      setLoading(true);
      setVoyage(null);
      setDetail(null);
      setMissions([]);
      setVoyageSnapshots([]);
      setError('');
    }
    try {
      // The voyage detail carries counts only; the missions table reads one server page at a time.
      const parts = splitVoyageResponse(await getVoyage(routeId));
      if (!current()) return;
      setVoyage(parts.voyage);
      setDetail(parts.detail);
      try {
        const page = await listMissions({ pageNumber: missionPage.pageNumber, pageSize: missionPage.pageSize, filters: { voyageId: routeId, order: 'CreatedAscending' } });
        if (!current()) return;
        setMissions(page.objects || []);
        setMissionPage(p => ({ ...p, totalPages: page.totalPages, totalRecords: page.totalRecords }));
        // List reads carry no playbook snapshots; any mission's detail does (each gets the voyage's selections).
        const firstMission = (page.objects || [])[0];
        if (missionPage.pageNumber === 1) {
          const snapshots = firstMission ? ((await getMission(firstMission.id).catch(() => null))?.playbookSnapshots || []) : [];
          if (!current()) return;
          setVoyageSnapshots(snapshots);
        }
      } catch {
        if (!current()) return;
        setMissions([]);
      }
      loadedVoyageIdRef.current = routeId;
    } catch (e: unknown) {
      if (!current()) return;
      setError(t('Failed to load voyage: {{message}}', { message: e instanceof Error ? e.message : String(e) }));
    } finally {
      if (current()) setLoading(false);
    }
  }, [id, t, missionPage.pageNumber, missionPage.pageSize]);

  // Follow the voyage and its missions as they run and land.
  useLiveRefresh(['mission.', 'voyage.'], loadVoyage);

  useEffect(() => {
    loadVoyage();
    listAll(listVessels).then(r => setVessels(r.objects || [])).catch(() => {});
    listAll(listCaptains).then(r => setCaptains(r.objects || [])).catch(() => {});
  }, [loadVoyage]);

  // Progress
  const progress = voyageProgressFromDetail(detail);
  const completedCount = progress.completed;
  const failedCount = progress.failed;
  const progressPct = progress.percent;
  const totalMissions = progress.total;
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
        if (!(await deletion.run({ kind: 'voyagePurge', action: () => purgeVoyage(voyage.id), errorMessage: (err) => t('Delete failed: {{message}}', { message: err instanceof Error ? err.message : String(err) }) }))) return;
        pushToast('warning', t('Voyage "{{title}}" deleted.', { title: voyage.title || voyage.id }));
        nav('/voyages');
      },
    });
  }

  function handleRetryFailed() {
    if (!voyage) return;
    if (failedCount === 0) return;
    setConfirm({
      open: true,
      title: t('Retry Failed Missions'),
      message: t('Retry {{count}} failed mission(s)? New missions will be created with the same parameters.', { count: failedCount }),
      danger: false,
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        try {
          // Every failed mission, read explicitly in bounded pages (not only the page the table shows).
          const failed = (await listAll(listMissions, { filters: { voyageId: voyage.id, status: 'Failed' } })).objects || [];
          for (const m of failed) {
            await createMission(retryMissionPayload(m));
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
    setJsonData({ open: true, title: t('Voyage: {{title}}', { title: voyage.title || voyage.id }), data: detail ?? voyage });
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
        <h2>{voyage.title || voyage.id} <EvaluationBadge isEvaluation={voyage.isEvaluation} /></h2>
      </div>

      {/* Action buttons */}
      <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', marginBottom: 20 }}>
        <button className="btn-sm" onClick={() => nav('/checks', { state: { prefill: { vesselId: detail?.vesselId || missions[0]?.vesselId || '', voyageId: voyage.id, branchName: missions[0]?.branchName || null, label: voyage.title } } })} title={t('Open Checks with this voyage and its first mission\'s vessel and branch filled in, to run a build, test or other check.')}>{t('Run Check')}</button>
        <button
          className="btn-sm"
          onClick={() => nav('/releases/new', {
            state: {
              prefill: {
                vesselId: detail?.vesselId || missions[0]?.vesselId || null,
                // The release links the voyage; its missions follow from it.
                voyageIds: [voyage.id],
                title: voyage.title ? `${voyage.title} Release` : 'Voyage Release',
              },
            },
          })}
          title={t('Start a new release draft that includes this voyage and all of its missions.')}
        >
          {t('Draft Release')}
        </button>
        {isVoyageActive(voyage.status) && (
          <button className="btn-sm btn-danger" onClick={handleCancel} title={t('Cancel the voyage and its pending and assigned missions. Missions already running are not stopped. The records are kept.')}>{t('Cancel Voyage')}</button>
        )}
        {failedCount > 0 && (
          <button className="btn-sm" onClick={handleRetryFailed} title={t('Create a new mission for each failed mission, with the same title, description and settings. The failed ones are left as they are.')}>{t('Retry Failed')} ({failedCount})</button>
        )}
        <button className="btn-sm" onClick={handleViewJson} title={t('Show the raw voyage record and its missions as the API returns them.')}>{t('View JSON')}</button>
        {!isVoyageActive(voyage.status) && (
          <button className="btn-sm btn-danger" onClick={handleDelete} title={t('Permanently delete this voyage and all of its missions. Cannot be undone.')}>{t('Delete')}</button>
        )}
      </div>

      {/* Detail grid */}
      <div className="card-grid" style={{ marginBottom: 20 }}>
        <div className="card">
          <h3 title={t('Open while missions are being set up, InProgress while any run, then Complete, Failed (some missions failed) or Cancelled.')}>{t('Status')}</h3>
          <StatusBadge status={voyage.status} />
        </div>

        <div className="card">
          <h3>{t('Details')}</h3>
          <div style={{ display: 'grid', gap: 8 }}>
            <div><span className="text-muted" style={{ fontSize: 12 }} title={t('Unique voyage ID (vyg_ prefix). Use it with the REST API, MCP tools and the armada CLI.')}>{t('ID')}</span>
              <div className="mono" style={{ fontSize: 12, display: 'flex', alignItems: 'center', gap: 4 }}>
                {voyage.id}
                <CopyButton text={voyage.id} />
              </div>
            </div>
            <div><span className="text-muted" style={{ fontSize: 12 }} title={t('Notes about the voyage as a whole. Captains do not see this; each mission has its own instructions.')}>{t('Description')}</span><div>{voyage.description || '-'}</div></div>
          </div>
        </div>

        <div className="card">
          <h3 title={t('Settings this voyage overrides for its missions. A dash or Default means the vessel or server setting applies.')}>{t('Configuration')}</h3>
          <div style={{ display: 'grid', gap: 8 }}>
            <div><span className="text-muted" style={{ fontSize: 12 }} title={t('Whether pull requests opened for this voyage\'s missions are merged automatically once they can be. A dash means the server setting applies.')}>{t('Auto-Merge PRs')}</span><div>{voyage.autoMergePullRequests != null ? (voyage.autoMergePullRequests ? t('Yes') : t('No')) : '-'}</div></div>
            <div><span className="text-muted" style={{ fontSize: 12 }} title={t('How this voyage\'s missions land. Default means each vessel\'s own mode, then the server default. Hover the value for details.')}>{t('Landing Mode')}</span><div title={findLandingMode(getVoyageLandingModes(t), voyage.landingMode).description}>{voyage.landingMode || t('Default')} <span className="text-dim">({findLandingMode(getVoyageLandingModes(t), voyage.landingMode).short})</span></div></div>
          </div>
        </div>

        {(() => {
          const overrides: CaptainAssignmentOverride[] = parseCaptainOverrides(voyage.captainOverridesJson);
          if (!overrides.length) return null;
          return (
            <div className="card">
              <h3 title={t('Captains this voyage asked for by persona. If one is busy, dispatch falls back to an idle captain at the listed tier or above.')}>{t('Captain Assignments')}</h3>
              <div style={{ display: 'grid', gap: 8 }}>
                {overrides.map((override, index) => (
                  <div key={`${override.persona}-${index}`}>
                    <span className="text-muted" style={{ fontSize: 12 }}>{override.persona}</span>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '0.4rem', flexWrap: 'wrap' }}>
                      <CaptainRef captainId={override.captainId} captains={captains} />
                      {override.strictCaptain ? <span className="text-muted">{t('Wait for selected captain')}</span> : override.fallbackTier ? <span className="text-muted" style={{ fontSize: 11 }} title={t('Lowest capability tier dispatch may fall back to when this captain is busy.')}>{t('fallback: {{tier}}', { tier: t(override.fallbackTier) })}</span> : null}
                    </div>
                  </div>
                ))}
              </div>
            </div>
          );
        })()}

        <div className="card">
          <h3>{t('Timestamps')}</h3>
          <div style={{ display: 'grid', gap: 8 }}>
            <div>
              <span className="text-muted" style={{ fontSize: 12 }} title={t('When the voyage was created.')}>{t('Created')}</span>
              <div>{formatDateTime(voyage.createdUtc)} <span className="text-muted" style={{ fontSize: 11 }}>({formatRelativeTime(voyage.createdUtc)})</span></div>
            </div>
            {voyage.completedUtc && (
              <div>
                <span className="text-muted" style={{ fontSize: 12 }} title={t('When every mission in the voyage reached a final state.')}>{t('Completed')}</span>
                <div>{formatDateTime(voyage.completedUtc)} <span className="text-muted" style={{ fontSize: 11 }}>({formatRelativeTime(voyage.completedUtc)})</span></div>
              </div>
            )}
          </div>
        </div>
      </div>

      {/* Progress bar */}
      {totalMissions > 0 && (
        <div style={{ marginBottom: 20 }}>
          <div style={{ height: 10, background: '#e0e0e8', borderRadius: 5, overflow: 'hidden' }}>
            <div style={{ height: '100%', width: `${progressPct}%`, background: 'var(--primary)', transition: 'width 0.3s' }} />
          </div>
          <span className="text-muted" style={{ fontSize: 12, marginTop: 4, display: 'inline-block' }} title={t('The bar fills as missions finish successfully. Failed missions count toward the total but not toward complete.')}>
            {t('{{completed}}/{{total}} complete, {{failed}} failed', { completed: completedCount, total: totalMissions, failed: failedCount })}
          </span>
        </div>
      )}

      {(voyageSnapshots.length > 0 || voyageSelections.length > 0) && (
        <div className="card playbook-voyage-card" style={{ marginBottom: 20 }}>
          <div className="playbook-voyage-header">
            <div>
              <h3 title={t('Playbooks (reusable instruction files) given to this voyage\'s captains, and how each was delivered.')}>{t('Playbooks')}</h3>
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
                  <span title={t('How the playbook reached the captain: inlined in the instructions, referenced by path, or copied into the worktree.')}><StatusBadge status={formatDeliveryMode(snapshot.deliveryMode)} /></span>
                  <span className="mono text-dim" title={t('Where the playbook was placed in the worktree, or the file it was read from.')}>{snapshot.worktreeRelativePath || snapshot.resolvedPath || '-'}</span>
                </div>
              </div>
            )) : voyageSelections.map((selection, index) => (
              <div key={`${selection.playbookId}-${index}`} className="playbook-voyage-item">
                <div>
                  <strong>{selection.playbookId}</strong>
                  <div className="text-dim">{t('Playbook details are available after mission snapshots are created.')}</div>
                </div>
                <div className="playbook-voyage-item-meta">
                  <span title={t('How the playbook reached the captain: inlined in the instructions, referenced by path, or copied into the worktree.')}><StatusBadge status={formatDeliveryMode(selection.deliveryMode)} /></span>
                </div>
              </div>
            ))}
          </div>
        </div>
      )}

      {/* Missions table */}
      <h3 style={{ marginBottom: 12 }}>{t('Missions')}</h3>
      {missions.length > 0 || missionPage.totalRecords > 0 ? (
        <DataTable
          tableKey="voyage-detail-missions"
          className="table"
          rows={missions}
          rowKey={(m) => m.id}
          pagination={{
            pageNumber: missionPage.pageNumber,
            pageSize: missionPage.pageSize,
            totalPages: missionPage.totalPages,
            totalRecords: missionPage.totalRecords,
            onPageChange: (p) => setMissionPage(s => ({ ...s, pageNumber: p })),
            onPageSizeChange: (size) => setMissionPage(s => ({ ...s, pageSize: size, pageNumber: 1 })),
          }}
          onRowClick={(m) => nav(`/missions/${m.id}`)}
          columns={[
            {
              key: 'id', label: t('ID'), required: true, headerTitle: t('Unique mission ID (msn_ prefix).'), cellClassName: 'mono text-dim table-id-cell',
              render: (m) => (
                <span className="id-display">
                  <span className="id-value" title={m.id}>{m.id}</span>
                  <CopyButton text={m.id} onClick={e => e.stopPropagation()} />
                </span>
              ),
            },
            {
              key: 'mission', label: t('Mission'), required: true, headerTitle: t('Mission title. Click a row to open the mission.'), cellClassName: 'cell-title',
              cellTitle: (m) => m.title,
              render: (m) => <><strong className="line-clamp-2">{m.title}</strong>{m.isEvaluation && <EvaluationBadge isEvaluation />}</>,
            },
            { key: 'status', label: t('Status'), headerTitle: t('Lifecycle state: Pending waits for a captain, InProgress is running, WorkProduced awaits landing, then Complete, Failed or Cancelled.'), cellClassName: 'cell-nowrap', render: (m) => <StatusBadge status={m.status} /> },
            {
              key: 'vessel', label: t('Vessel'), headerTitle: t('Repository the mission works in. Click a name to open that vessel.'),
              render: (m) => m.vesselId ? <Link to={`/vessels/${m.vesselId}`} onClick={e => e.stopPropagation()}>{vesselName(m.vesselId)}</Link> : '-',
            },
            {
              key: 'captain', label: t('Captain'), headerTitle: t('Agent that ran or is running the mission. Shows - until dispatch assigns one.'),
              render: (m) => m.captainId ? <Link to={`/captains/${m.captainId}`} onClick={e => e.stopPropagation()}>{captainName(m.captainId)}</Link> : '-',
            },
            {
              key: 'branch', label: t('Branch'), headerTitle: t('Git branch that holds the mission\'s commits. Its work lands from this branch by merge, pull request or push.'), cellClassName: 'mono text-muted',
              cellTitle: (m) => m.branchName || undefined,
              render: (m) => <span className="cell-clip"><span>{m.branchName || '-'}</span></span>,
            },
            {
              key: 'actions', label: t('Actions'), headerTitle: t('View the mission\'s diff or log, or open the mission.'), fixed: true, interactive: true, cellClassName: 'cell-nowrap',
              render: (m) => (
                <ActionMenu id={`voyage-mission-${m.id}`} items={[
                  { label: 'Duplicate', onClick: () => nav(`/missions?duplicate=${encodeURIComponent(m.id)}`) },
                  { label: 'Diff', onClick: () => handleMissionDiff(m.id, m.title) },
                  { label: 'Log', onClick: () => handleMissionLog(m.id, m.title) },
                  { label: 'Detail', onClick: () => nav(`/missions/${m.id}`) },
                ]} />
              ),
            },
          ]}
        />
      ) : (
        <p className="text-muted">{t('No missions in this voyage.')}</p>
      )}

      {/* Shared modals */}
      {deletion.dialog}
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
