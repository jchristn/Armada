import { splitVoyageResponse } from '../lib/voyageDetail';
import CliPermissionCountdown from '../components/cliPermissions/CliPermissionCountdown';
import { useEffect, useState, useMemo, useCallback, useRef } from 'react';
import { useNotifications } from '../context/NotificationContext';
import { useLiveRefresh } from '../lib/useLiveRefresh';
import { useParams, useNavigate, Link } from 'react-router-dom';
import {
  getMission,
  getVoyage,
  updateMission,
  deleteMission,
  purgeMission,
  getMissionDiff,
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
  getHarbor,
} from '../api/client';
import { listAll } from '../api/paging';
import type { Captain, CheckRun, Deployment, Harbor, LandingPreviewResult, Mission, Vessel } from '../types/models';

import { loadPullRequestEvidence, type PullRequestEvidence } from '../lib/pullRequestEvidence';
import ErrorModal from '../components/shared/ErrorModal';
import StatusBadge from '../components/shared/StatusBadge';
import ActionMenu from '../components/shared/ActionMenu';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import JsonViewer from '../components/shared/JsonViewer';
import DiffViewer from '../components/shared/DiffViewer';
import LogViewer from '../components/shared/LogViewer';
import PageHeader from '../components/shared/PageHeader';
import EvaluationBadge from '../components/shared/EvaluationBadge';
import CopyButton from '../components/shared/CopyButton';
import Markdown from '../components/shared/Markdown';
import Button from '../components/shared/Button';
import CaptainRef from '../components/shared/CaptainRef';
import AssignmentBlockerCard from '../components/missions/AssignmentBlockerCard';
import BranchesModal from '../components/vessels/BranchesModal';
import { useLocale } from '../context/LocaleContext';
import { useDeleteProgress } from '../lib/useDeleteProgress';
import { MISSION_TRANSITION_STATUSES, formatMissionDuration, isMissionLogCompleted, missionLandingState } from '../lib/missionActions';

const MISSION_STATUSES = MISSION_TRANSITION_STATUSES;

export default function MissionDetail() {
  const { t, formatDateTime, formatRelativeTime } = useLocale();
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();

  const [mission, setMission] = useState<Mission | null>(null);
  const [vessels, setVessels] = useState<Vessel[]>([]);
  const [captains, setCaptains] = useState<Captain[]>([]);
  const [harbor, setHarbor] = useState<Harbor | null>(null);
  const [landingPreview, setLandingPreview] = useState<LandingPreviewResult | null>(null);
  const [linkedCheckRuns, setLinkedCheckRuns] = useState<CheckRun[]>([]);
  const [linkedDeployments, setLinkedDeployments] = useState<Deployment[]>([]);
  const [pullRequestEvidence, setPullRequestEvidence] = useState<PullRequestEvidence | null>(null);
  const gitHubPullRequest = pullRequestEvidence?.detail ?? null;
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
  const requestGenerationRef = useRef(0);
  const loadedMissionIdRef = useRef<string | null>(null);

  // Transition
  const [showTransition, setShowTransition] = useState(false);
  const [transitionStatus, setTransitionStatus] = useState('');
  const [reviewApproval, setReviewApproval] = useState<import('../types/models').RunApprovalOptions | null>(null);
  useEffect(() => {
    let cancelled = false;
    setReviewApproval(null);
    if (mission?.voyageId) void (async () => {
      try {
        const raw = await getVoyage(mission.voyageId!);
        const voyage = splitVoyageResponse(raw).voyage;
        if (!cancelled) setReviewApproval(voyage.approvalOptions ?? null);
      } catch { /* Mission details remain available if the voyage is unavailable. */ }
    })();
    return () => { cancelled = true; };
  }, [mission?.voyageId]);

  const [reviewDecision, setReviewDecision] = useState<{ comment: string } | null>(null);

  // Edit form
  const [editModal, setEditModal] = useState(false);
  const [editTitle, setEditTitle] = useState('');
  const [editDescription, setEditDescription] = useState('');
  const [editPriority, setEditPriority] = useState(100);
  const [editSaving, setEditSaving] = useState(false);

  // Manage Branches (manual landing: Landing Mode None)
  const [branchesOpen, setBranchesOpen] = useState(false);

  // JSON viewer
  const [jsonData, setJsonData] = useState<{ open: boolean; title: string; data: unknown }>({ open: false, title: '', data: null });

  // Confirm
  const [confirm, setConfirm] = useState<{ open: boolean; title: string; message: string; onConfirm: () => void }>({ open: false, title: '', message: '', onConfirm: () => {} });
  // Delete and purge run behind the "this may take some time" progress modal.
  const deletion = useDeleteProgress();

  const formatDuration = useCallback((totalRuntimeMs: number | null | undefined): string => formatMissionDuration(totalRuntimeMs, t), [t]);

  const missionCaptain = captains.find(captain => captain.id === mission?.captainId);

  // Lookup maps
  const vesselName = useMemo(() => {
    const m = new Map<string, string>();
    vessels.forEach(v => m.set(v.id, v.name));
    return (vid: string | null | undefined) => vid ? m.get(vid) || vid : '-';
  }, [vessels]);

  const loadMission = useCallback(async () => {
    if (!id) return;
    const generation = ++requestGenerationRef.current;
    const routeId = id;
    // Show the loading state only when the route changes. A refresh of the same mission keeps the record on screen.
    const isInitialLoad = loadedMissionIdRef.current !== routeId;
    if (isInitialLoad) {
      setLoading(true);
      setMission(null);
      setLandingPreview(null);
      setLoadingLandingPreview(false);
    }
    const current = () => generation === requestGenerationRef.current;
    try {
      const m = await getMission(routeId);
      if (!current()) return;
      setMission(m);
      setLoadingLandingPreview(true);
      getMissionLandingPreview(routeId)
        .then((result) => { if (current()) setLandingPreview(result); })
        .catch(() => { if (current()) setLandingPreview(null); })
        .finally(() => { if (current()) setLoadingLandingPreview(false); });
      loadedMissionIdRef.current = routeId;
      // Only clear error on the first load of this route -- don't dismiss user-facing errors from actions
      if (isInitialLoad) setError('');
    } catch (e: unknown) {
      if (!current()) return;
      if (isInitialLoad) setError(t('Failed to load mission: {{message}}', { message: e instanceof Error ? e.message : String(e) }));
    } finally {
      if (current()) setLoading(false);
    }
  }, [id, t]);

  useEffect(() => {
    loadMission();
  }, [loadMission]);

  // Follow the mission's status, review, and landing as they change on the server.
  useLiveRefresh(['mission.'], loadMission);

  useEffect(() => {
    listAll(listVessels).then(r => setVessels(r.objects || [])).catch(() => {});
    listAll(listCaptains).then(r => setCaptains(r.objects || [])).catch(() => {});
  }, []);

  // Resolve the Harbor the mission ran on to its name (the id is shown if it cannot be read).
  const assignedHarborId = mission?.assignedHarborId ?? null;
  useEffect(() => {
    if (!assignedHarborId) return undefined;
    let cancelled = false;
    getHarbor(assignedHarborId)
      .then(h => { if (!cancelled) setHarbor(h); })
      .catch(() => { if (!cancelled) setHarbor(null); });
    return () => { cancelled = true; };
  }, [assignedHarborId]);
  const assignedHarbor = harbor && harbor.id === assignedHarborId ? harbor : null;
  const actualCaptain = mission?.captainId ? captains.find(c => c.id === mission.captainId) ?? null : null;

  useEffect(() => {
    if (!id) {
      setLinkedCheckRuns([]);
      setLinkedDeployments([]);
      return;
    }

    let cancelled = false;
    Promise.all([
      listAll(listCheckRuns, { filters: { missionId: id } }).catch(() => null),
      listAll(listDeployments, { missionId: id }).catch(() => null),
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
      setPullRequestEvidence(null);
      setLoadingGitHubPullRequest(false);
      return;
    }

    let cancelled = false;
    setLoadingGitHubPullRequest(true);
    loadPullRequestEvidence(mission.id).then((result) => {
      if (!cancelled) setPullRequestEvidence(result);
    }).catch(() => {
      if (!cancelled) setPullRequestEvidence(null);
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
        const done = await deletion.run({
          kind: 'missionPurge',
          action: () => purgeMission(mission.id),
          errorMessage: (e) => t('Purge failed: {{message}}', { message: e instanceof Error ? e.message : String(e) }),
        });
        if (!done) return;
        pushToast('warning', t('Mission "{{title}}" purged.', { title: mission.title }));
        loadMission();
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
  const landing = missionLandingState(mission, landingPreview);
  const { canResolveReview, canMarkComplete, showManualMerge, showLand } = landing;
  const landLabel = landing.isRetry ? t('Retry Landing') : t('Land');
  const landingPill = { className: landing.pill.tone, label: t(landing.pill.label) };
  const handleLand = async () => { try { await retryMissionLanding(mission.id); pushToast('success', t('Landing succeeded! Mission status updated.')); loadMission(); } catch (e) { setError(e instanceof Error ? e.message : t('Landing failed.')); } };

  return (
    <div>
      <PageHeader
        breadcrumb={
          <>
            <Link to="/missions">{t('Missions')}</Link> <span className="breadcrumb-sep">&gt;</span> <span>{mission.title}</span>
          </>
        }
        title={<>{mission.title} <EvaluationBadge isEvaluation={mission.isEvaluation} /></>}
        actions={
          <>
            {canResolveReview && reviewApproval && <CliPermissionCountdown expiresUtc={mission.reviewDeadlineUtc} active={!!mission.reviewDeadlineUtc} timeoutDecision={reviewApproval.timeoutDecision} />}
            {canResolveReview && (
              <button className="btn btn-sm btn-primary" onClick={() => setReviewDecision({ comment: mission.reviewComment || '' })} title={t('Decide this review gate: approve, conditionally approve, request more work, or deny (which fails the pipeline).')}>{t('Resolve Review')}</button>
            )}
            {canMarkComplete && (
              <button className="btn btn-sm btn-primary" onClick={handleMarkComplete} title={t('Move this mission from Review to Complete without landing anything. Use it when the work has already been merged.')}>{t('Mark Complete')}</button>
            )}
            <button className="btn btn-sm" onClick={handleViewDiff} title={t('Show the code changes this mission made, from its worktree or the snapshot saved when it finished.')}>{t('Diff')}</button>
            <button className="btn btn-sm" onClick={handleViewLog} title={t('Show the captain\'s session log for this mission, newest 200 lines by default.')}>{t('Log')}</button>
            <button className="btn btn-sm" onClick={handleViewInstructions} title={t('Show the instruction file Armada generated for the captain, such as CLAUDE.md or AGENTS.md, including the mission description.')}>{t('Instructions')}</button>
            {mission.vesselId && (
              <button className="btn btn-sm" onClick={() => navigate('/checks', { state: { prefill: { vesselId: mission.vesselId, missionId: mission.id, voyageId: mission.voyageId || null, branchName: mission.branchName || null, commitHash: mission.commitHash || null, label: mission.title } } })} title={t('Open Checks with this mission\'s vessel, branch and commit filled in, to run a build, test or other check against its work.')}>
                {t('Run Check')}
              </button>
            )}
            {showLand && (
              <Button className="btn btn-sm btn-primary" onClick={handleLand} title={t('Rebase the mission branch onto its target branch and land it using the vessel\'s landing mode (merge, pull request or merge queue).')}>{landLabel}</Button>
            )}
            {showManualMerge && (
              <button className="btn btn-sm btn-primary" onClick={() => setBranchesOpen(true)} title={t('This vessel lands by hand (Landing Mode None). Merge the mission branch in Manage Branches; the mission completes once its branch is merged.')}>{t('Merge in Manage Branches')}</button>
            )}
            <ActionMenu id={`mission-action-${mission.id}`} items={[
              { label: 'Edit', title: 'Change the mission title, description (the captain\'s instructions) and priority.', onClick: openEdit },
              ...(canResolveReview ? [
                { label: 'Resolve Review', title: 'Decide this review gate: approve, conditionally approve, request more work, or deny (which fails the pipeline).', onClick: () => setReviewDecision({ comment: mission.reviewComment || '' }) },
              ] : []),
              ...(canMarkComplete ? [
                { label: 'Mark Complete', title: 'Move this mission from Review to Complete without landing anything. Use it when the work has already been merged.', onClick: handleMarkComplete },
              ] : []),
              { label: 'View Diff', title: 'Show the code changes this mission made, from its worktree or the snapshot saved when it finished.', onClick: handleViewDiff },
              { label: 'View Log', title: 'Show the captain\'s session log for this mission, newest 200 lines by default.', onClick: handleViewLog },
              { label: 'View Instructions', title: 'Show the instruction file Armada generated for the captain, such as CLAUDE.md or AGENTS.md, including the mission description.', onClick: handleViewInstructions },
              ...(mission.vesselId ? [{ label: 'Run Check', title: 'Open Checks with this mission\'s vessel, branch and commit filled in, to run a build, test or other check against its work.', onClick: () => navigate('/checks', { state: { prefill: { vesselId: mission.vesselId, missionId: mission.id, voyageId: mission.voyageId || null, branchName: mission.branchName || null, commitHash: mission.commitHash || null, label: mission.title } } }) }] : []),
              { label: 'Transition Status', title: 'Move the mission to another status by hand, such as InProgress to Failed. Only valid moves are accepted. Use it to correct a stuck mission.', onClick: () => setShowTransition(true) },
              { label: 'View JSON', title: 'Show the raw mission record as the API returns it.', onClick: () => setJsonData({ open: true, title: t('Mission: {{title}}', { title: mission.title }), data: mission }) },
              { label: 'Restart', title: 'Reset a Failed or Cancelled mission to Pending so it is dispatched again. Clears its captain, branch, pull request and timing.', onClick: handleRestart },
              ...(showLand ? [{ label: mission.status === 'LandingFailed' ? 'Retry Landing' : 'Land', title: 'Rebase the mission branch onto its target branch and land it using the vessel\'s landing mode (merge, pull request or merge queue).', onClick: handleLand }] : []),
              ...(showManualMerge ? [{ label: 'Merge in Manage Branches', title: 'Open Manage Branches with this mission\'s branch selected so you can merge it by hand. The vessel\'s landing mode is None.', onClick: () => setBranchesOpen(true) }] : []),
              { label: 'Purge', danger: true, title: 'Permanently delete the mission and clean up its branch, worktree and other resources. This cannot be undone.', onClick: handlePurge },
              { label: 'Delete', danger: true, title: 'Cancel the mission: its status becomes Cancelled and the record is kept. Use Purge to remove it for good.', onClick: handleDelete },
            ]} />
          </>
        }
      />

      <ErrorModal error={error} onClose={() => setError('')} />

      <JsonViewer open={jsonData.open} title={jsonData.title} data={jsonData.data} onClose={() => setJsonData({ open: false, title: '', data: null })} />
      <ConfirmDialog open={confirm.open} title={confirm.title} message={confirm.message}
        onConfirm={confirm.onConfirm} onCancel={() => setConfirm(c => ({ ...c, open: false }))} />
      {deletion.dialog}

      {mission.status === 'Pending' && mission.assignmentBlocker && (
        <AssignmentBlockerCard blocker={mission.assignmentBlocker} />
      )}

      {showManualMerge && mission.vesselId && (
        <BranchesModal
          vesselId={mission.vesselId}
          vesselName={vesselName(mission.vesselId)}
          open={branchesOpen}
          onClose={() => { setBranchesOpen(false); loadMission(); }}
          initialMergeSource={mission.branchName}
          onMerged={() => loadMission()}
        />
      )}

      <div className="card landing-preview-card">
        <div className="readiness-panel-header">
          <div>
            <h3 title={t('What would happen if this mission landed now, based on the vessel\'s landing mode, branch protection and check rules. Nothing is merged by viewing it.')}>{t('Landing Preview')}</h3>
            <div className="readiness-panel-meta" title={t('Mission branch, then the target branch it would land into.')}>
              {landingPreview?.sourceBranch ? `${landingPreview.sourceBranch} -> ${landingPreview.targetBranch}` : mission.branchName || t('No branch selected')}
            </div>
          </div>
          <span className={`readiness-pill ${landingPill.className}`} title={t('Overall landing outlook for this mission, from the preview and its current status.')}>
            {landingPill.label}
          </span>
        </div>
        {loadingLandingPreview ? (
          <div className="text-dim">{t('Calculating landing preview...')}</div>
        ) : !landingPreview ? (
          <div className="text-dim">{t('Landing preview is not available for this mission yet.')}</div>
        ) : (
          <>
            <div className="readiness-summary-row">
              <span title={t('Kind of target branch (for example Default or Release), which decides which landing rules apply.')}>{t('Branch category')}: {landingPreview.branchCategory}</span>
              <span title={t('How the work lands: LocalMerge, MergeAndPush, PullRequest, MergeQueue or None (merge by hand). Inherited means the voyage, vessel or server default applies.')}>{t('Landing mode')}: {landingPreview.landingMode || t('Inherited')}</span>
              <span title={t('What happens to the mission branch after it lands, such as deleting it locally or on the remote. Inherited means the default applies.')}>{t('Cleanup')}: {landingPreview.branchCleanupPolicy || t('Inherited')}</span>
              {landingPreview.expectedLandingAction && <span title={t('The landing step Armada expects to take once all policies are applied.')}>{t('Action')}: {landingPreview.expectedLandingAction}</span>}
              <span title={t('Whether the vessel blocks landing until a check run for this work has passed.')}>{landingPreview.requirePassingChecksToLand ? t('Passing checks required') : t('Passing checks optional')}</span>
            </div>
            <div className="readiness-summary-row">
              <span title={t('Whether the target branch matches one of the vessel\'s protected-branch patterns, which can force pull-request landing.')}>{landingPreview.targetBranchProtected ? t('Protected target branch') : t('Target branch not protected')}</span>
              {landingPreview.protectedBranchMatch && <span title={t('Protected-branch pattern that matched the target branch.')}>{t('Policy')}: <span className="mono">{landingPreview.protectedBranchMatch}</span></span>}
              {landingPreview.requirePullRequestForProtectedBranches && <span title={t('This vessel lands into protected branches only through a pull request, never a direct merge.')}>{t('PR required for protected branches')}</span>}
              {landingPreview.requireMergeQueueForReleaseBranches && <span title={t('This vessel lands into release branches only through the merge queue, which tests each change before merging.')}>{t('Merge queue required for release branches')}</span>}
            </div>
            {landingPreview.latestCheckSummary && (
              <div className="landing-preview-latest-check">
                <strong title={t('Most recent check run (build, test or similar) linked to this mission\'s work.')}>{t('Latest check')}</strong>
                <div className="text-dim">{landingPreview.latestCheckSummary}</div>
              </div>
            )}
            {landingPreview.issues.length > 0 ? (
              <div className="readiness-issues">
                {landingPreview.issues.map((issue, index) => (
                  <div key={`${issue.code}-${index}`} className={`readiness-issue ${issue.severity.toLowerCase()}`}>
                    <div className="readiness-issue-title-row">
                      <strong>{issue.title}</strong>
                      <span className={`readiness-issue-severity ${issue.severity.toLowerCase()}`} title={t('Error blocks landing; Warning is worth checking but does not block it.')}>{issue.severity}</span>
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
        completed={mission != null && isMissionLogCompleted(mission.status)}
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
            <label title={t('Short name for the mission, shown in lists and notifications. Required.')}>{t('Title')}<input value={editTitle} onChange={e => setEditTitle(e.target.value)} required title={t('Short name for the mission, shown in lists and notifications. Required.')} /></label>
            <label title={t('Instructions the captain works from. A captain already running keeps the instructions it started with; restart to apply changes.')}>{t('Description')}<textarea value={editDescription} onChange={e => setEditDescription(e.target.value)} rows={3} title={t('Instructions the captain works from. A captain already running keeps the instructions it started with; restart to apply changes.')} /></label>
            <label title={t('Dispatch order from 0 to 1000. Lower numbers are assigned to a captain first; the default is 100.')}>{t('Priority')}<input type="number" value={editPriority} onChange={e => setEditPriority(Number(e.target.value))} min={0} max={1000} title={t('Dispatch order from 0 to 1000. Lower numbers are assigned to a captain first; the default is 100.')} /></label>
            <div className="modal-actions">
              <button type="submit" className="btn btn-primary" disabled={editSaving} title={t('Save the title, description and priority changes.')}>{editSaving ? t('Saving...') : t('Save')}</button>
              <button type="button" className="btn" onClick={() => setEditModal(false)} title={t('Close without saving your changes.')}>{t('Cancel')}</button>
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
            <label style={{ marginTop: 12 }} title={t('Status to move the mission to. The server rejects moves that are not valid from the current status.')}>
              {t('New Status')}
              <select value={transitionStatus} onChange={e => setTransitionStatus(e.target.value)} title={t('Status to move the mission to. The server rejects moves that are not valid from the current status.')}>
                <option value="">{t('Select status...')}</option>
                {MISSION_STATUSES.map(s => (
                  <option key={s} value={s}>{t(s)}</option>
                ))}
              </select>
            </label>
            <div className="modal-actions">
              <button className="btn btn-primary" onClick={handleTransition} disabled={!transitionStatus} title={t('Move the mission to the selected status now. Use this only to correct a mission stuck in the wrong state.')}>{t('Transition')}</button>
              <button className="btn" onClick={() => setShowTransition(false)} title={t('Close without changing the mission status.')}>{t('Cancel')}</button>
            </div>
          </div>
        </div>
      )}

      {reviewDecision && (
        <div className="modal-overlay" onClick={() => setReviewDecision(null)}>
          <div className="modal review-verdict-modal" onClick={e => e.stopPropagation()}>
            <h3>{t('Resolve Review')}</h3>
            <CliPermissionCountdown expiresUtc={mission.reviewDeadlineUtc} active={!!reviewApproval && !!mission.reviewDeadlineUtc} timeoutDecision={reviewApproval?.timeoutDecision} />
            <p className="text-dim">
              {t('Choose how to resolve this review gate. Your feedback is carried into the next step or the re-run.')}
            </p>
            <label style={{ marginTop: 12 }} title={t('Your review notes. They are saved on the mission and passed to the next step or the re-run. Required for Conditionally Approve and More Work Required.')}>
              {t('Feedback')}
              <textarea
                title={t('Your review notes. They are saved on the mission and passed to the next step or the re-run. Required for Conditionally Approve and More Work Required.')}
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
              <Button className="btn btn-primary" onClick={() => submitReview('approve')} title={t('Accept this stage. The pipeline moves to its next stage, or to landing if this is the last one.')}>{t('Approve')}</Button>
              <Button className="btn" onClick={() => submitReview('conditional')} disabled={!reviewDecision.comment.trim()} title={!reviewDecision.comment.trim() ? t('Write feedback first. Conditionally Approve continues the pipeline and hands your feedback to the next step.') : t('Continue the pipeline; the next step receives your feedback and must take it into account.')}>{t('Conditionally Approve')}</Button>
              <Button className="btn" onClick={() => submitReview('morework')} disabled={!reviewDecision.comment.trim()} title={!reviewDecision.comment.trim() ? t('Write feedback first. More Work Required re-runs this same step with your feedback.') : t('Send this step back to Pending to be redone with your feedback.')}>{t('More Work Required')}</Button>
              <Button className="btn btn-danger" onClick={() => submitReview('deny')} title={t('Reject this stage and fail the pipeline. Later stages do not run.')}>{t('Deny')}</Button>
              <button className="btn" onClick={() => setReviewDecision(null)} title={t('Close without deciding. The mission stays in Review.')}>{t('Cancel')}</button>
            </div>
          </div>
        </div>
      )}

      {mission.prUrl && (
        <div className="card" style={{ marginTop: '1rem', marginBottom: '1rem' }}>
          <div className="detail-header" style={{ marginBottom: '0.75rem' }}>
            <h3 title={t('Review and check status Armada read from GitHub for this mission\'s pull request.')}>{t('GitHub Pull Request')}</h3>
            <div className="inline-actions">
              <a className="btn btn-sm" href={mission.prUrl} target="_blank" rel="noreferrer" title={t('Open the pull request on GitHub in a new tab.')}>{t('Open GitHub')}</a>
              <button className="btn btn-sm" disabled={loadingGitHubPullRequest} onClick={() => mission.id && void loadPullRequestEvidence(mission.id).then(setPullRequestEvidence).catch(() => setPullRequestEvidence(null))} title={t('Fetch the latest reviews, checks and mergeability for this pull request from GitHub.')}>
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
                <div className="detail-field"><span className="detail-label" title={t('Overall review decision on the pull request, such as Approved or Changes requested.')}>{t('Review Status')}</span><span>{gitHubPullRequest.reviewStatus}</span></div>
                <div className="detail-field"><span className="detail-label" title={t('Whether the pull request is open, closed or merged on GitHub.')}>{t('State')}</span><span>{gitHubPullRequest.state}</span></div>
                <div className="detail-field"><span className="detail-label" title={t('GitHub\'s view of whether the pull request can merge now, for example clean, blocked or dirty (has conflicts).')}>{t('Mergeability')}</span><span>{gitHubPullRequest.mergeableState || '-'}</span></div>
              </div>
              <div className="text-dim" style={{ marginBottom: '0.75rem' }}>
                {gitHubPullRequest.title}
              </div>
              <div className="detail-grid">
                <div className="card">
                  <h4 title={t('Each reviewer and the state of their latest review.')}>{t('Reviews')}{gitHubPullRequest.reviewCount > (pullRequestEvidence?.reviews.length ?? 0) ? ` (${pullRequestEvidence?.reviews.length ?? 0}/${gitHubPullRequest.reviewCount})` : ''}</h4>
                  {(pullRequestEvidence?.reviews.length ?? 0) === 0 ? <div className="text-dim">{t('No reviews')}</div> : (
                    <div style={{ display: 'grid', gap: '0.45rem' }}>
                      {(pullRequestEvidence?.reviews ?? []).map((review, index) => (
                        <div key={`review-${index}`}>
                          <strong>{review.reviewerLogin || t('Unknown')}</strong> <span className="text-dim">{review.state}</span>
                        </div>
                      ))}
                    </div>
                  )}
                </div>
                <div className="card">
                  <h4 title={t('CI checks GitHub reports for the pull request, with status and conclusion.')}>{t('Checks')}{gitHubPullRequest.checkCount > (pullRequestEvidence?.checks.length ?? 0) ? ` (${pullRequestEvidence?.checks.length ?? 0}/${gitHubPullRequest.checkCount})` : ''}</h4>
                  {(pullRequestEvidence?.checks.length ?? 0) === 0 ? <div className="text-dim">{t('No provider checks')}</div> : (
                    <div style={{ display: 'grid', gap: '0.45rem' }}>
                      {(pullRequestEvidence?.checks ?? []).map((check, index) => (
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
          <span className="detail-label" title={t('Unique mission ID (msn_ prefix). Use it with the REST API, MCP tools and the armada CLI.')}>{t('ID')}</span>
          <span className="id-display">
            <span className="mono">{mission.id}</span>
            <CopyButton text={mission.id} />
          </span>
        </div>
        <div className="detail-field"><span className="detail-label" title={t('Tenant (organization) that owns this mission. Only users in that tenant, and global admins, can see it.')}>{t('Tenant ID')}</span><span className="mono">{mission.tenantId || '-'}</span></div>
        <div className="detail-field">
          <span className="detail-label" title={t('Where the mission is in its lifecycle, from Pending (waiting for a captain) to Complete, Failed or Cancelled.')}>{t('Status')}</span>
          <StatusBadge status={mission.status} />
        </div>
        <div className="detail-field">
          <span className="detail-label" title={t('Implementation changes code and lands a diff. Audit and Research are read-only and produce a written report instead.')}>{t('Mode')}</span>
          <span>{t(mission.mode || 'Implementation')}</span>
        </div>
        <div className="detail-field">
          <span className="detail-label" title={t('Whether a person must approve this mission before its pipeline continues. Set by the pipeline stage that created it.')}>{t('Review Gate')}</span>
          <span>
            {mission.requiresReview
              ? <StatusBadge status={mission.status === 'Review' ? 'Waiting Review' : 'Required'} />
              : <span className="text-dim">{t('None')}</span>}
          </span>
        </div>
        <div className="detail-field">
          <span className="detail-label" title={t('What a denied review does: Retry stage sends this step back to be redone; Fail pipeline stops the whole pipeline.')}>{t('On Deny')}</span>
          <span>{mission.requiresReview ? (mission.reviewDenyAction === 'FailPipeline' ? t('Fail pipeline') : t('Retry stage')) : <span className="text-dim">-</span>}</span>
        </div>
        <div className="detail-field">
          <span className="detail-label" title={t('When the mission last entered Review and started waiting for a decision.')}>{t('Review Requested')}</span>
          <span>{mission.reviewRequestedUtc ? formatDateTime(mission.reviewRequestedUtc) : '-'}</span>
        </div>
        <div className="detail-field">
          <span className="detail-label" title={t('When the most recent review decision was made.')}>{t('Reviewed')}</span>
          <span>{mission.reviewedUtc ? formatDateTime(mission.reviewedUtc) : '-'}</span>
        </div>
        <div className="detail-field">
          <span className="detail-label" title={t('User ID of whoever made the most recent review decision.')}>{t('Reviewed By')}</span>
          <span className="mono">{mission.reviewedByUserId || '-'}</span>
        </div>
        {mission.failureReason && (
          <div className="detail-field" style={{ gridColumn: '1 / -1' }}>
            <span className="detail-label" title={t('Why the mission failed or could not land, as recorded when it failed.')}>{t('Failure Reason')}</span>
            <span className="text-dim">{t('The failure reason and available remediation are recorded here. Correct configuration problems before restarting; see the mission log for inference turns and tool calls.')}</span>
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
            }} className="mission-failure-reason" data-failure-kind={mission.failureKind ?? undefined}>{mission.failureReason}</pre>
            <div className="flex gap-2" style={{ flexWrap: 'wrap' }}>
              {/* A landing blocked by the host (FailureKind Infra: a dirty checkout, a held lock) is fixed outside Armada
                  and then retried; offer the retry next to the reason so it is one click away. */}
              {mission.status === 'LandingFailed' && mission.failureKind === 'Infra' && showLand && (
                <Button className="btn btn-sm btn-primary" onClick={handleLand} title={t('Rebase the mission branch onto its target branch and land it using the vessel\'s landing mode (merge, pull request or merge queue).')}>{t('Retry Landing')}</Button>
              )}
              {mission.captainId && <Link className="btn btn-sm" to={`/captains/${mission.captainId}`}>{t('Open captain configuration')}</Link>}
              {missionCaptain?.modelEndpointId && <Link className="btn btn-sm" to="/configuration?tab=endpoints">{t('Open inference endpoints')}</Link>}
              {mission.vesselId && <Link className="btn btn-sm" to={`/vessels/${mission.vesselId}`}>{t('Open vessel configuration')}</Link>}
            </div>
          </div>
        )}
        {mission.reviewComment && (
          <div className="detail-field" style={{ gridColumn: '1 / -1' }}>
            <span className="detail-label" title={t('Feedback from the most recent review decision.')}>{t('Review Comment')}</span>
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
        <div className="detail-field"><span className="detail-label" title={t('Dispatch order. Lower numbers are assigned to a captain first; the default is 100.')}>{t('Priority')}</span><span>{mission.priority}</span></div>
        <div className="detail-field">
          <span className="detail-label" title={t('Voyage (batch of related missions) this mission belongs to. Shows - for a standalone mission.')}>{t('Voyage')}</span>
          {mission.voyageId
            ? <Link to={`/voyages/${mission.voyageId}`} className="mono">{mission.voyageId}</Link>
            : <span>-</span>}
        </div>
        <div className="detail-field">
          <span className="detail-label" title={t('Repository the mission works in.')}>{t('Vessel')}</span>
          {mission.vesselId
            ? <Link to={`/vessels/${mission.vesselId}`}>{vesselName(mission.vesselId)}</Link>
            : <span>-</span>}
        </div>
        <div className="detail-field">
          <span className="detail-label" title={t('Captain this mission asked for. If that captain is busy, dispatch uses another idle captain of the same or higher tier.')}>{t('Preferred Captain')}</span>
          <span style={{ display: 'inline-flex', alignItems: 'center', gap: '0.4rem', flexWrap: 'wrap' }}>
            <CaptainRef captainId={mission.requestedCaptainId} captains={captains} showTier={false} />
            {mission.requestedCaptainId && mission.captainId && mission.captainId !== mission.requestedCaptainId
              ? <span className="text-dim" title={t('The preferred captain was busy, so this step fell back by capability tier.')}>{t('(fell back to tier)')}</span>
              : null}
          </span>
        </div>
        <div className="detail-field">
          <span className="detail-label" title={t('Captain that ran or is running the mission. Shows - until dispatch assigns one.')}>{t('Actual Captain')}</span>
          <span style={{ display: 'inline-flex', alignItems: 'center', gap: '0.4rem', flexWrap: 'wrap' }}>
            <CaptainRef captainId={mission.captainId} captains={captains} autoLabel="-" showTier={false} />
            {actualCaptain?.runtime
              ? <span className="text-dim" data-testid="mission-captain-runtime" title={t('Agent program this captain runs, such as ClaudeCode, Codex, Mux or ApiEndpoint.')}>({actualCaptain.runtime})</span>
              : null}
          </span>
        </div>
        <div className="detail-field">
          <span className="detail-label" title={t('Harbor (a developer machine running Armada Harbor) the mission was routed to. Admiral host means it ran on the Admiral itself, or has not been routed yet.')}>{t('Harbor')}</span>
          {mission.assignedHarborId
            ? (
              <Link
                to="/configuration?tab=harbors"
                data-testid="mission-harbor"
                title={t('Open Harbors to see this Harbor\'s connection, capacity and jobs. ID: {{id}}', { id: mission.assignedHarborId })}
                className={assignedHarbor ? undefined : 'mono'}
              >
                {assignedHarbor?.name || mission.assignedHarborId}
              </Link>
            )
            : <span className="text-dim" title={t('No Harbor took this mission: it ran on the Admiral host, or it has not been routed yet.')}>{t('Admiral host')}</span>}
        </div>
        <div className="detail-field">
          <span className="detail-label" title={t('Mission that created this one as a sub-task, for example an architect mission that planned it.')}>{t('Parent Mission')}</span>
          {mission.parentMissionId
            ? <Link to={`/missions/${mission.parentMissionId}`} className="mono">{mission.parentMissionId}</Link>
            : <span>-</span>}
        </div>
        <div className="detail-field">
          <span className="detail-label" title={t('Role the captain plays, such as Worker, Architect or Judge. It sets the prompt and which captains may take it. Blank means Worker.')}>{t('Persona')}</span>
          <span>{mission.persona || <span className="text-dim">{t('Worker')}</span>}</span>
        </div>
        {mission.dependsOnMissionId && (
          <div className="detail-field">
            <span className="detail-label" title={t('Mission that must finish successfully before this one can be assigned, usually the previous pipeline stage.')}>{t('Depends On')}</span>
            <Link to={`/missions/${mission.dependsOnMissionId}`} className="mono">{mission.dependsOnMissionId}</Link>
          </div>
        )}
        {mission.status === 'WorkProduced' && mission.dependsOnMissionId === null && mission.persona && mission.persona !== 'Worker' && (
          <div className="detail-field" style={{ gridColumn: '1 / -1' }}>
            <span className="detail-label" title={t('Where this stage stands in its pipeline.')}>{t('Pipeline Status')}</span>
            <span className="text-dim">{t('Work complete -- handed off to the next pipeline stage')}</span>
          </div>
        )}
        <div className="detail-field"><span className="detail-label" title={t('Git branch that holds this mission\'s commits. Its work lands from this branch.')}>{t('Branch Name')}</span><span className="mono">{mission.branchName || '-'}</span></div>
        <div className="detail-field">
          <span className="detail-label" title={t('Git worktree the captain works in for this mission. Released after the mission finishes.')}>{t('Dock')}</span>
          {mission.dockId
            ? <Link to={`/docks/${mission.dockId}`} className="mono">{mission.dockId}</Link>
            : <span>-</span>}
        </div>
        <div className="detail-field"><span className="detail-label" title={t('Operating system process ID of the agent working this mission, while it runs.')}>{t('Process ID')}</span><span>{mission.processId ?? '-'}</span></div>
        <div className="detail-field">
          <span className="detail-label" title={t('Pull request opened for this mission\'s work, when the vessel lands by pull request.')}>{t('PR URL')}</span>
          {mission.prUrl
            ? <a href={mission.prUrl} target="_blank" rel="noopener noreferrer">{mission.prUrl}</a>
            : <span>-</span>}
        </div>
        <div className="detail-field"><span className="detail-label" title={t('Commit at the head of the mission branch when the mission finished.')}>{t('Commit Hash')}</span><span className="mono">{mission.commitHash || '-'}</span></div>
        <div className="detail-field">
          <span className="detail-label" title={t('When the mission was created.')}>{t('Created')}</span>
          <span title={mission.createdUtc}>
            {formatRelativeTime(mission.createdUtc)}
            <span className="text-dim"> ({formatDateTime(mission.createdUtc)})</span>
          </span>
        </div>
        <div className="detail-field">
          <span className="detail-label" title={t('When a captain started working on the mission.')}>{t('Started')}</span>
          <span title={formatDateTime(mission.startedUtc)}>
            {formatRelativeTime(mission.startedUtc) || '-'}
            {mission.startedUtc && <span className="text-dim"> ({formatDateTime(mission.startedUtc)})</span>}
          </span>
        </div>
        <div className="detail-field">
          <span className="detail-label" title={t('When the mission reached a final state such as Complete, Failed or Cancelled.')}>{t('Completed')}</span>
          <span title={formatDateTime(mission.completedUtc)}>
            {formatRelativeTime(mission.completedUtc) || '-'}
            {mission.completedUtc && <span className="text-dim"> ({formatDateTime(mission.completedUtc)})</span>}
          </span>
        </div>
        <div className="detail-field">
          <span className="detail-label" title={t('Time from start to completion, or so far if it is still running.')}>{t('Total Runtime')}</span>
          <span>{formatDuration(mission.totalRuntimeMs)}</span>
        </div>
        <div className="detail-field">
          <span className="detail-label" title={t('When anything on this mission last changed.')}>{t('Last Updated')}</span>
          <span title={mission.lastUpdateUtc}>
            {formatRelativeTime(mission.lastUpdateUtc)}
            <span className="text-dim"> ({formatDateTime(mission.lastUpdateUtc)})</span>
          </span>
        </div>
        <div className="detail-field">
          <span className="detail-label" title={t('Number of check runs (builds, tests and similar) recorded against this mission.')}>{t('Linked Checks')}</span>
          <span>{linkedCheckRuns.length}</span>
        </div>
        <div className="detail-field">
          <span className="detail-label" title={t('Number of deployments that include this mission\'s work.')}>{t('Linked Deployments')}</span>
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
            <CopyButton text={mission.description} title={t('Copy the description to the clipboard as raw Markdown.')} />
          </div>
          <div className="card markdown" style={{ padding: '1rem', marginTop: '0.5rem' }}>
            <Markdown>{mission.description}</Markdown>
          </div>
        </div>
      )}

      {mission.playbookSnapshots && mission.playbookSnapshots.length > 0 && (
        <div style={{ marginTop: '1rem' }}>
          <h3 title={t('Playbooks (reusable instruction files) given to the captain, saved exactly as they were when the mission started.')}>{t('Playbooks')}</h3>
          <div className="playbook-snapshot-list">
            {mission.playbookSnapshots.map((snapshot, index) => (
              <div key={`${snapshot.fileName}-${index}`} className="playbook-snapshot-card">
                <div className="playbook-snapshot-header">
                  <div>
                    <strong>{snapshot.fileName}</strong>
                    <div className="text-dim">{snapshot.description || t('No description')}</div>
                  </div>
                  <span title={t('How the playbook reached the captain: inlined in the instructions, referenced by path, or copied into the worktree.')}><StatusBadge status={snapshot.deliveryMode.replace(/([a-z])([A-Z])/g, '$1 $2')} /></span>
                </div>

                <div className="playbook-snapshot-meta">
                  <span title={t('Where the playbook file was read from on the Admiral host.')}><strong>{t('Resolved Path')}:</strong> <span className="mono">{snapshot.resolvedPath || '-'}</span></span>
                  <span title={t('Where the playbook was placed inside the mission worktree, relative to its root.')}><strong>{t('Worktree Path')}:</strong> <span className="mono">{snapshot.worktreeRelativePath || '-'}</span></span>
                  <span title={t('When the playbook was last edited before this snapshot was taken.')}><strong>{t('Source Updated')}:</strong> {snapshot.sourceLastUpdateUtc ? formatDateTime(snapshot.sourceLastUpdateUtc) : '-'}</span>
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
