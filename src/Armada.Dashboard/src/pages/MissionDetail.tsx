import { useEffect, useState, useMemo, useCallback, useRef } from 'react';
import { useNotifications } from '../context/NotificationContext';
import { useParams, useNavigate, Link } from 'react-router-dom';
import {
  getMission,
  updateMission,
  deleteMission,
  purgeMission,
  getMissionDiff,
  getMissionGitHubPullRequest,
  getMissionLog,
  getMissionInstructions,
  getMissionLandingPreview,
  restartMission,
  retryMissionLanding,
  transitionMission,
  approveMissionReview,
  denyMissionReview,
  listCheckRuns,
  listVessels,
  listCaptains,
  listDeployments,
} from '../api/client';
import type { Captain, CheckRun, Deployment, GitHubPullRequestDetail, LandingPreviewResult, Mission, Vessel } from '../types/models';
import ErrorModal from '../components/shared/ErrorModal';
import StatusBadge from '../components/shared/StatusBadge';
import ActionMenu from '../components/shared/ActionMenu';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import JsonViewer from '../components/shared/JsonViewer';
import DiffViewer from '../components/shared/DiffViewer';
import LogViewer from '../components/shared/LogViewer';
import PageHeader from '../components/shared/PageHeader';
import CopyButton from '../components/shared/CopyButton';
import Markdown from '../components/shared/Markdown';
import Button from '../components/shared/Button';
import CaptainRef from '../components/shared/CaptainRef';
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
  const [landingPreview, setLandingPreview] = useState<LandingPreviewResult | null>(null);
  const [linkedCheckRuns, setLinkedCheckRuns] = useState<CheckRun[]>([]);
  const [linkedDeployments, setLinkedDeployments] = useState<Deployment[]>([]);
  const [gitHubPullRequest, setGitHubPullRequest] = useState<GitHubPullRequestDetail | null>(null);
  const [loadingGitHubPullRequest, setLoadingGitHubPullRequest] = useState(false);
  const [loadingLandingPreview, setLoadingLandingPreview] = useState(false);
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
  const [reviewDecision, setReviewDecision] = useState<{ comment: string } | null>(null);

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

  const loadMission = useCallback(async () => {
    if (!id) return;
    // Only show loading spinner on initial load, not background refreshes
    const isInitialLoad = !missionLoadedRef.current;
    if (isInitialLoad) setLoading(true);
    try {
      const m = await getMission(id);
      setMission(m);
      setLoadingLandingPreview(true);
      getMissionLandingPreview(id)
        .then((result) => setLandingPreview(result))
        .catch(() => setLandingPreview(null))
        .finally(() => setLoadingLandingPreview(false));
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

  useEffect(() => {
    if (!id) {
      setLinkedCheckRuns([]);
      setLinkedDeployments([]);
      return;
    }

    let cancelled = false;
    Promise.all([
      listCheckRuns({ pageSize: 1000, filters: { missionId: id } }).catch(() => null),
      listDeployments({ pageSize: 1000, missionId: id }).catch(() => null),
    ]).then(([checkResult, deploymentResult]) => {
      if (cancelled) return;
      setLinkedCheckRuns(checkResult?.objects || []);
      setLinkedDeployments(deploymentResult?.objects || []);
    }).catch(() => {
      if (!cancelled) {
        setLinkedCheckRuns([]);
        setLinkedDeployments([]);
      }
    });

    return () => {
      cancelled = true;
    };
  }, [id]);

  useEffect(() => {
    if (!mission?.id || !mission.prUrl) {
      setGitHubPullRequest(null);
      setLoadingGitHubPullRequest(false);
      return;
    }

    let cancelled = false;
    setLoadingGitHubPullRequest(true);
    getMissionGitHubPullRequest(mission.id).then((result) => {
      if (!cancelled) setGitHubPullRequest(result);
    }).catch(() => {
      if (!cancelled) setGitHubPullRequest(null);
    }).finally(() => {
      if (!cancelled) setLoadingGitHubPullRequest(false);
    });

    return () => { cancelled = true; };
  }, [mission?.id, mission?.prUrl]);

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

  function handleMarkComplete() {
    if (!mission) return;
    setConfirm({
      open: true,
      title: t('Mark Complete'),
      message: t('Mark mission "{{title}}" as Complete? Use this when the work has already landed and the mission just needs to graduate out of Review.', { title: mission.title }),
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        try {
          await transitionMission(mission.id, { status: 'Complete' });
          pushToast('success', t('Mission "{{title}}" marked Complete.', { title: mission.title }));
          loadMission();
        } catch (e: unknown) {
          setError(t('Mark Complete failed: {{message}}', { message: e instanceof Error ? e.message : String(e) }));
        }
      },
    });
  }

  async function submitReview(verdict: 'approve' | 'conditional' | 'morework' | 'deny') {
    if (!mission || !reviewDecision) return;

    const comment = reviewDecision.comment.trim();
    try {
      if (verdict === 'approve') {
        await approveMissionReview(mission.id, { comment: comment || undefined });
        pushToast('success', t('Review approved for "{{title}}".', { title: mission.title }));
      } else if (verdict === 'conditional') {
        await approveMissionReview(mission.id, { comment, conditional: true });
        pushToast('success', t('Conditionally approved "{{title}}". The next step will consider your feedback.', { title: mission.title }));
      } else if (verdict === 'morework') {
        await denyMissionReview(mission.id, { comment, action: 'RetryStage' });
        pushToast('warning', t('Sent "{{title}}" back for more work with your feedback.', { title: mission.title }));
      } else {
        await denyMissionReview(mission.id, { comment: comment || undefined, action: 'FailPipeline' });
        pushToast('warning', t('Review denied for "{{title}}".', { title: mission.title }));
      }

      setReviewDecision(null);
      loadMission();
    } catch (e: unknown) {
      setError(t('Review decision failed: {{message}}', { message: e instanceof Error ? e.message : String(e) }));
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
  const canResolveReview = mission.status === 'Review' && mission.requiresReview;
  const canMarkComplete = mission.status === 'Review' && !mission.requiresReview;
  // A mission is landable when work is produced, a prior landing failed, or it sits in Review with no
  // explicit review gate to resolve (requiresReview=false) -- in which case landing is how it graduates
  // out of Review.
  const canLand = mission.status === 'WorkProduced' || mission.status === 'LandingFailed'
    || (mission.status === 'Review' && !mission.requiresReview);
  const landLabel = mission.status === 'LandingFailed' ? t('Retry Landing') : t('Land');

  return (
    <div>
      <PageHeader
        breadcrumb={
          <>
            <Link to="/missions">{t('Missions')}</Link> <span className="breadcrumb-sep">&gt;</span> <span>{mission.title}</span>
          </>
        }
        title={mission.title}
        actions={
          <>
            {canResolveReview && (
              <button className="btn btn-sm btn-primary" onClick={() => setReviewDecision({ comment: mission.reviewComment || '' })}>{t('Resolve Review')}</button>
            )}
            {canMarkComplete && (
              <button className="btn btn-sm btn-primary" onClick={handleMarkComplete} title={t('Graduate this mission out of Review to Complete')}>{t('Mark Complete')}</button>
            )}
            <button className="btn btn-sm" onClick={handleViewDiff} title={t('View mission diff')}>{t('Diff')}</button>
            <button className="btn btn-sm" onClick={handleViewLog} title={t('View mission log')}>{t('Log')}</button>
            <button className="btn btn-sm" onClick={handleViewInstructions} title={t('View mission instructions')}>{t('Instructions')}</button>
            {mission.vesselId && (
              <button className="btn btn-sm" onClick={() => navigate('/checks', { state: { prefill: { vesselId: mission.vesselId, missionId: mission.id, voyageId: mission.voyageId || null, branchName: mission.branchName || null, commitHash: mission.commitHash || null, label: mission.title } } })}>
                {t('Run Check')}
              </button>
            )}
            {canLand && (
              <Button className="btn btn-sm btn-primary" onClick={async () => { try { await retryMissionLanding(mission.id); pushToast('success', t('Landing succeeded! Mission status updated.')); loadMission(); } catch (e) { setError(e instanceof Error ? e.message : t('Landing failed.')); } }} title={t('Rebase the mission branch and merge it into the target branch, then complete the mission')}>{landLabel}</Button>
            )}
            <ActionMenu id={`mission-action-${mission.id}`} items={[
              { label: 'Edit', onClick: openEdit },
              ...(canResolveReview ? [
                { label: 'Resolve Review', onClick: () => setReviewDecision({ comment: mission.reviewComment || '' }) },
              ] : []),
              ...(canMarkComplete ? [
                { label: 'Mark Complete', onClick: handleMarkComplete },
              ] : []),
              { label: 'View Diff', onClick: handleViewDiff },
              { label: 'View Log', onClick: handleViewLog },
              { label: 'View Instructions', onClick: handleViewInstructions },
              ...(mission.vesselId ? [{ label: 'Run Check', onClick: () => navigate('/checks', { state: { prefill: { vesselId: mission.vesselId, missionId: mission.id, voyageId: mission.voyageId || null, branchName: mission.branchName || null, commitHash: mission.commitHash || null, label: mission.title } } }) }] : []),
              { label: 'Transition Status', onClick: () => setShowTransition(true) },
              { label: 'View JSON', onClick: () => setJsonData({ open: true, title: t('Mission: {{title}}', { title: mission.title }), data: mission }) },
              { label: 'Restart', onClick: handleRestart },
              ...(canLand ? [{ label: mission.status === 'LandingFailed' ? 'Retry Landing' : 'Land', onClick: async () => { try { await retryMissionLanding(mission.id); pushToast('success', t('Landing succeeded! Mission status updated.')); loadMission(); } catch (e) { setError(e instanceof Error ? e.message : t('Landing failed.')); } } }] : []),
              { label: 'Purge', danger: true, onClick: handlePurge },
              { label: 'Delete', danger: true, onClick: handleDelete },
            ]} />
          </>
        }
      />

      <ErrorModal error={error} onClose={() => setError('')} />

      <JsonViewer open={jsonData.open} title={jsonData.title} data={jsonData.data} onClose={() => setJsonData({ open: false, title: '', data: null })} />
      <ConfirmDialog open={confirm.open} title={confirm.title} message={confirm.message}
        onConfirm={confirm.onConfirm} onCancel={() => setConfirm(c => ({ ...c, open: false }))} />

      <div className="card landing-preview-card">
        <div className="readiness-panel-header">
          <div>
            <h3>{t('Landing Preview')}</h3>
            <div className="readiness-panel-meta">
              {landingPreview?.sourceBranch ? `${landingPreview.sourceBranch} -> ${landingPreview.targetBranch}` : mission.branchName || t('No branch selected')}
            </div>
          </div>
          <span className={`readiness-pill ${landingPreview?.isReadyToLand ? 'ready' : 'warning'}`}>
            {landingPreview?.isReadyToLand ? t('Ready To Land') : t('Needs Review')}
          </span>
        </div>
        {loadingLandingPreview ? (
          <div className="text-dim">{t('Calculating landing preview...')}</div>
        ) : !landingPreview ? (
          <div className="text-dim">{t('Landing preview is not available for this mission yet.')}</div>
        ) : (
          <>
            <div className="readiness-summary-row">
              <span>{t('Branch category')}: {landingPreview.branchCategory}</span>
              <span>{t('Landing mode')}: {landingPreview.landingMode || t('Inherited')}</span>
              <span>{t('Cleanup')}: {landingPreview.branchCleanupPolicy || t('Inherited')}</span>
              {landingPreview.expectedLandingAction && <span>{t('Action')}: {landingPreview.expectedLandingAction}</span>}
              <span>{landingPreview.requirePassingChecksToLand ? t('Passing checks required') : t('Passing checks optional')}</span>
            </div>
            <div className="readiness-summary-row">
              <span>{landingPreview.targetBranchProtected ? t('Protected target branch') : t('Target branch not protected')}</span>
              {landingPreview.protectedBranchMatch && <span>{t('Policy')}: <span className="mono">{landingPreview.protectedBranchMatch}</span></span>}
              {landingPreview.requirePullRequestForProtectedBranches && <span>{t('PR required for protected branches')}</span>}
              {landingPreview.requireMergeQueueForReleaseBranches && <span>{t('Merge queue required for release branches')}</span>}
            </div>
            {landingPreview.latestCheckSummary && (
              <div className="landing-preview-latest-check">
                <strong>{t('Latest check')}</strong>
                <div className="text-dim">{landingPreview.latestCheckSummary}</div>
              </div>
            )}
            {landingPreview.issues.length > 0 ? (
              <div className="readiness-issues">
                {landingPreview.issues.map((issue, index) => (
                  <div key={`${issue.code}-${index}`} className={`readiness-issue ${issue.severity.toLowerCase()}`}>
                    <div className="readiness-issue-title-row">
                      <strong>{issue.title}</strong>
                      <span className={`readiness-issue-severity ${issue.severity.toLowerCase()}`}>{issue.severity}</span>
                    </div>
                    <div className="text-dim">{issue.message}</div>
                  </div>
                ))}
              </div>
            ) : (
              <div className="readiness-success-copy">{t('No landing blockers are currently predicted for this mission.')}</div>
            )}
          </>
        )}
      </div>

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
        markdown
        totalLines={logModal.totalLines}
        completed={mission != null && ['Complete', 'Failed', 'Cancelled', 'WorkProduced', 'LandingFailed', 'Review'].includes(mission.status)}
        onClose={() => setLogModal({ open: false, title: '', missionId: '', content: '', totalLines: 0, lineCount: 200 })}
        onRefresh={handleLogRefresh}
        onLineCountChange={handleLogLineCountChange}
      />
      <LogViewer
        open={instructionsModal.open}
        title={instructionsModal.title}
        content={instructionsModal.content}
        markdown
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

      {reviewDecision && (
        <div className="modal-overlay" onClick={() => setReviewDecision(null)}>
          <div className="modal review-verdict-modal" onClick={e => e.stopPropagation()}>
            <h3>{t('Resolve Review')}</h3>
            <p className="text-dim">
              {t('Choose how to resolve this review gate. Your feedback is carried into the next step or the re-run.')}
            </p>
            <label style={{ marginTop: 12 }}>
              {t('Feedback')}
              <textarea
                value={reviewDecision.comment}
                rows={8}
                onChange={event => setReviewDecision(current => current ? { ...current, comment: event.target.value } : current)}
                placeholder={t('Required for Conditionally Approve and More Work Required; optional for Approve/Deny.')}
              />
            </label>
            <ul className="review-verdict-help text-dim">
              <li><strong>{t('Approve')}</strong> {t('- accept this stage and continue.')}</li>
              <li><strong>{t('Conditionally Approve')}</strong> {t('- continue, but the next step must consider your feedback.')}</li>
              <li><strong>{t('More Work Required')}</strong> {t('- redo this same step with your feedback.')}</li>
              <li><strong>{t('Deny')}</strong> {t('- reject this stage and fail the pipeline.')}</li>
            </ul>
            <div className="modal-actions review-verdict-actions">
              <Button className="btn btn-primary" onClick={() => submitReview('approve')}>{t('Approve')}</Button>
              <Button className="btn" onClick={() => submitReview('conditional')} disabled={!reviewDecision.comment.trim()} title={!reviewDecision.comment.trim() ? t('Add feedback first') : undefined}>{t('Conditionally Approve')}</Button>
              <Button className="btn" onClick={() => submitReview('morework')} disabled={!reviewDecision.comment.trim()} title={!reviewDecision.comment.trim() ? t('Add feedback first') : undefined}>{t('More Work Required')}</Button>
              <Button className="btn btn-danger" onClick={() => submitReview('deny')}>{t('Deny')}</Button>
              <button className="btn" onClick={() => setReviewDecision(null)}>{t('Cancel')}</button>
            </div>
          </div>
        </div>
      )}

      {mission.prUrl && (
        <div className="card" style={{ marginTop: '1rem', marginBottom: '1rem' }}>
          <div className="detail-header" style={{ marginBottom: '0.75rem' }}>
            <h3>{t('GitHub Pull Request')}</h3>
            <div className="inline-actions">
              <a className="btn btn-sm" href={mission.prUrl} target="_blank" rel="noreferrer">{t('Open GitHub')}</a>
              <button className="btn btn-sm" disabled={loadingGitHubPullRequest} onClick={() => mission.id && void getMissionGitHubPullRequest(mission.id).then(setGitHubPullRequest).catch(() => setGitHubPullRequest(null))}>
                {loadingGitHubPullRequest ? t('Refreshing...') : t('Refresh')}
              </button>
            </div>
          </div>
          {loadingGitHubPullRequest ? (
            <div className="text-dim">{t('Loading GitHub pull-request evidence...')}</div>
          ) : !gitHubPullRequest ? (
            <div className="text-dim">{t('GitHub pull-request evidence is unavailable for this mission.')}</div>
          ) : (
            <>
              <div className="detail-grid" style={{ marginBottom: '0.75rem' }}>
                <div className="detail-field"><span className="detail-label">{t('Repository')}</span><span>{gitHubPullRequest.repository}</span></div>
                <div className="detail-field"><span className="detail-label">{t('Review Status')}</span><span>{gitHubPullRequest.reviewStatus}</span></div>
                <div className="detail-field"><span className="detail-label">{t('State')}</span><span>{gitHubPullRequest.state}</span></div>
                <div className="detail-field"><span className="detail-label">{t('Mergeability')}</span><span>{gitHubPullRequest.mergeableState || '-'}</span></div>
              </div>
              <div className="text-dim" style={{ marginBottom: '0.75rem' }}>
                {gitHubPullRequest.title}
              </div>
              <div className="detail-grid">
                <div className="card">
                  <h4>{t('Reviews')}</h4>
                  {gitHubPullRequest.reviews.length === 0 ? <div className="text-dim">{t('No reviews')}</div> : (
                    <div style={{ display: 'grid', gap: '0.45rem' }}>
                      {gitHubPullRequest.reviews.map((review, index) => (
                        <div key={`review-${index}`}>
                          <strong>{review.reviewerLogin || t('Unknown')}</strong> <span className="text-dim">{review.state}</span>
                        </div>
                      ))}
                    </div>
                  )}
                </div>
                <div className="card">
                  <h4>{t('Checks')}</h4>
                  {gitHubPullRequest.checks.length === 0 ? <div className="text-dim">{t('No provider checks')}</div> : (
                    <div style={{ display: 'grid', gap: '0.45rem' }}>
                      {gitHubPullRequest.checks.map((check, index) => (
                        <div key={`check-${index}`}>
                          <strong>{check.name}</strong> <span className="text-dim">{check.status}{check.conclusion ? ` / ${check.conclusion}` : ''}</span>
                        </div>
                      ))}
                    </div>
                  )}
                </div>
              </div>
            </>
          )}
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
        <div className="detail-field">
          <span className="detail-label">{t('Mode')}</span>
          <span>{t(mission.mode || 'Implementation')}</span>
        </div>
        <div className="detail-field">
          <span className="detail-label">{t('Review Gate')}</span>
          <span>
            {mission.requiresReview
              ? <StatusBadge status={mission.status === 'Review' ? 'Waiting Review' : 'Required'} />
              : <span className="text-dim">{t('None')}</span>}
          </span>
        </div>
        <div className="detail-field">
          <span className="detail-label">{t('On Deny')}</span>
          <span>{mission.requiresReview ? (mission.reviewDenyAction === 'FailPipeline' ? t('Fail pipeline') : t('Retry stage')) : <span className="text-dim">-</span>}</span>
        </div>
        <div className="detail-field">
          <span className="detail-label">{t('Review Requested')}</span>
          <span>{mission.reviewRequestedUtc ? formatDateTime(mission.reviewRequestedUtc) : '-'}</span>
        </div>
        <div className="detail-field">
          <span className="detail-label">{t('Reviewed')}</span>
          <span>{mission.reviewedUtc ? formatDateTime(mission.reviewedUtc) : '-'}</span>
        </div>
        <div className="detail-field">
          <span className="detail-label">{t('Reviewed By')}</span>
          <span className="mono">{mission.reviewedByUserId || '-'}</span>
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
        {mission.reviewComment && (
          <div className="detail-field" style={{ gridColumn: '1 / -1' }}>
            <span className="detail-label">{t('Review Comment')}</span>
            <pre style={{
              margin: 0,
              padding: '0.75rem',
              background: 'rgba(255, 184, 77, 0.08)',
              border: '1px solid rgba(255, 184, 77, 0.24)',
              borderRadius: '4px',
              color: 'var(--text)',
              fontSize: '0.85em',
              whiteSpace: 'pre-wrap',
              wordBreak: 'break-word',
              fontFamily: 'monospace'
            }}>{mission.reviewComment}</pre>
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
          <span className="detail-label">{t('Preferred Captain')}</span>
          <span style={{ display: 'inline-flex', alignItems: 'center', gap: '0.4rem', flexWrap: 'wrap' }}>
            <CaptainRef captainId={mission.requestedCaptainId} captains={captains} showTier={false} />
            {mission.requestedCaptainId && mission.captainId && mission.captainId !== mission.requestedCaptainId
              ? <span className="text-dim" title={t('The preferred captain was busy, so this step fell back by capability tier.')}>{t('(fell back to tier)')}</span>
              : null}
          </span>
        </div>
        <div className="detail-field">
          <span className="detail-label">{t('Actual Captain')}</span>
          <CaptainRef captainId={mission.captainId} captains={captains} autoLabel="-" showTier={false} />
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
        <div className="detail-field">
          <span className="detail-label">{t('Linked Checks')}</span>
          <span>{linkedCheckRuns.length}</span>
        </div>
        <div className="detail-field">
          <span className="detail-label">{t('Linked Deployments')}</span>
          <span>{linkedDeployments.length}</span>
        </div>
      </div>

      {(linkedCheckRuns.length > 0 || linkedDeployments.length > 0) && (
        <div className="detail-grid" style={{ marginTop: '1rem' }}>
          <div className="card">
            <h3>{t('Linked Checks')}</h3>
            {linkedCheckRuns.length === 0 ? (
              <p className="text-dim">{t('No checks are linked to this mission yet.')}</p>
            ) : (
              <div style={{ display: 'grid', gap: '0.65rem' }}>
                {linkedCheckRuns.map((checkRun) => (
                  <div key={checkRun.id}>
                    <Link to={`/checks/${checkRun.id}`}>{checkRun.label || checkRun.type}</Link>
                    <span className="mono text-dim" style={{ marginLeft: '0.45rem', fontSize: '0.78rem' }}>{checkRun.id}</span>
                  </div>
                ))}
              </div>
            )}
          </div>
          <div className="card">
            <h3>{t('Linked Deployments')}</h3>
            {linkedDeployments.length === 0 ? (
              <p className="text-dim">{t('No deployments are linked to this mission yet.')}</p>
            ) : (
              <div style={{ display: 'grid', gap: '0.75rem' }}>
                {linkedDeployments.map((deployment) => (
                  <div key={deployment.id}>
                    <Link to={`/deployments/${deployment.id}`}>{deployment.title}</Link>
                    <div className="text-dim" style={{ marginTop: '0.2rem' }}>
                      {deployment.environmentName || t('No environment')} • {deployment.status} • {deployment.verificationStatus}
                    </div>
                  </div>
                ))}
              </div>
            )}
          </div>
        </div>
      )}

      {/* Description */}
      {mission.description && (
        <div style={{ marginTop: '1rem' }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
            <h3 style={{ margin: 0 }}>{t('Description')}</h3>
            <CopyButton text={mission.description} title={t('Copy raw markdown')} />
          </div>
          <div className="card markdown" style={{ padding: '1rem', marginTop: '0.5rem' }}>
            <Markdown>{mission.description}</Markdown>
          </div>
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
