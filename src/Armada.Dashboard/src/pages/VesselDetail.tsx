import { useEffect, useState, useCallback, useMemo } from 'react';
import { useParams, useNavigate, Link, useSearchParams } from 'react-router-dom';
import { getVessel, listFleets, listMissionSummaries, listPipelines, createVessel, deleteVessel, getVesselReadiness, getVesselLandingPreview } from '../api/client';
import { listAll } from '../api/paging';
import type { Fleet, Vessel, MissionSummary, Pipeline, VesselReadinessResult, LandingPreviewResult } from '../types/models';
import ActionMenu from '../components/shared/ActionMenu';
import VesselCleanupModal from '../components/vessels/VesselCleanupModal';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import JsonViewer from '../components/shared/JsonViewer';
import PageHeader from '../components/shared/PageHeader';
import StatusBadge from '../components/shared/StatusBadge';
import CopyButton from '../components/shared/CopyButton';
import DataTable from '../components/shared/DataTable';
import ErrorModal from '../components/shared/ErrorModal';
import ReadinessPanel from '../components/shared/ReadinessPanel';
import VesselHealthButton from '../components/vessels/health/VesselHealthButton';
import { useLocale } from '../context/LocaleContext';
import { usePersistedPageSize } from '../lib/usePersistedPageSize';
import { useNotifications } from '../context/NotificationContext';
import { buildVesselDuplicatePayload } from '../lib/duplicates';
import VesselFormModal from '../components/vessels/VesselFormModal';
import VesselReservationsCard from '../components/blackbox/VesselReservationsCard';
import VesselBlackBoxDoctorCard from '../components/blackbox/VesselBlackBoxDoctorCard';
import { useDeleteProgress } from '../lib/useDeleteProgress';

export default function VesselDetail() {
  const { t, formatDateTime, formatRelativeTime } = useLocale();
  const deletion = useDeleteProgress();
  const { pushToast } = useNotifications();
  const { id } = useParams<{ id: string }>();
  const [searchParams, setSearchParams] = useSearchParams();
  const navigate = useNavigate();
  const [vessel, setVessel] = useState<Vessel | null>(null);
  const [fleets, setFleets] = useState<Fleet[]>([]);
  const [missions, setMissions] = useState<MissionSummary[]>([]);
  // Missions are paged on the server: a vessel can have far more missions than one page holds.
  const [missionPage, setMissionPage] = useState(1);
  const [missionPageSize, setMissionPageSize] = usePersistedPageSize('vessel-detail-missions', 25);
  const [missionTotals, setMissionTotals] = useState({ totalRecords: 0, totalPages: 1, totalMs: 0 });
  const [pipelines, setPipelines] = useState<Pipeline[]>([]);
  const [readiness, setReadiness] = useState<VesselReadinessResult | null>(null);
  const [landingPreview, setLandingPreview] = useState<LandingPreviewResult | null>(null);
  const [loadingReadiness, setLoadingReadiness] = useState(false);
  const [loadingLandingPreview, setLoadingLandingPreview] = useState(false);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  // Edit modal
  const [showForm, setShowForm] = useState(false);
  const [cleanupOpen, setCleanupOpen] = useState(false);

  // JSON viewer
  const [jsonData, setJsonData] = useState<{ open: boolean; title: string; data: unknown }>({ open: false, title: '', data: null });

  // Confirm
  const [confirm, setConfirm] = useState<{ open: boolean; title: string; message: string; onConfirm: () => void }>({ open: false, title: '', message: '', onConfirm: () => {} });

  const fleetMap = useMemo(() => {
    const m = new Map<string, string>();
    for (const f of fleets) m.set(f.id, f.name);
    return m;
  }, [fleets]);

  const load = useCallback(async () => {
    if (!id) return;
    try {
      setLoading(true);
      const isInitialLoad = !vessel;
      let found: Vessel | null = null;
      try {
        found = await getVessel(id);
      } catch {
        found = null;
      }
      if (!found) { setError(t('Vessel not found.')); setLoading(false); return; }
      const [fResult, pResult] = await Promise.all([
        listAll(listFleets),
        listAll(listPipelines),
      ]);
      setVessel(found);
      setFleets(fResult.objects);
      setPipelines(pResult.objects);
      setLoadingReadiness(true);
      getVesselReadiness(id)
        .then((result) => setReadiness(result))
        .catch(() => setReadiness(null))
        .finally(() => setLoadingReadiness(false));
      setLoadingLandingPreview(true);
      getVesselLandingPreview(id, found.defaultBranch || null)
        .then((result) => setLandingPreview(result))
        .catch(() => setLandingPreview(null))
        .finally(() => setLoadingLandingPreview(false));
      if (isInitialLoad) setError('');
    } catch {
      setError(t('Failed to load vessel.'));
    } finally {
      setLoading(false);
    }
  }, [id, t]);

  useEffect(() => { load(); }, [load]);

  useEffect(() => {
    if (!id) return;
    let cancelled = false;
    listMissionSummaries({ pageNumber: missionPage, pageSize: missionPageSize, filters: { vesselId: id } })
      .then((result) => {
        if (cancelled) return;
        setMissions(result.objects || []);
        setMissionTotals({
          totalRecords: result.totalRecords ?? (result.objects || []).length,
          totalPages: Math.max(1, result.totalPages || 1),
          totalMs: result.totalMs ?? 0,
        });
      })
      .catch(() => { if (!cancelled) setMissions([]); });
    return () => { cancelled = true; };
  }, [id, missionPage, missionPageSize]);

  function openEdit() {
    if (!vessel) return;
    setShowForm(true);
  }

  useEffect(() => {
    if (!vessel) return;
    if (searchParams.get('edit') !== '1') return;
    openEdit();
    setSearchParams((current) => {
      const next = new URLSearchParams(current);
      next.delete('edit');
      return next;
    }, { replace: true });
  }, [searchParams, setSearchParams, vessel]);

  function handleDelete() {
    if (!vessel) return;
    setConfirm({
      open: true,
      title: t('Delete Vessel'),
      message: t('Delete vessel "{{name}}"? This cannot be undone.', { name: vessel.name }),
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        if (!(await deletion.run({ kind: 'vessel', action: () => deleteVessel(vessel.id) }))) return;
        pushToast('warning', t('Vessel "{{name}}" deleted.', { name: vessel.name }));
        navigate('/vessels');
      },
    });
  }

  function handleDispatch() {
    if (!vessel) return;
    navigate('/dispatch', { state: { fromVessel: true, vesselId: vessel.id } });
  }

  function handleViewHistory() {
    if (!vessel) return;
    navigate(`/vessels/${vessel.id}/history`);
  }

  function handleManageObjectives() {
    if (!vessel) return;
    const params = new URLSearchParams({ vesselId: vessel.id });
    if (vessel.fleetId) {
      params.set('fleetId', vessel.fleetId);
    }

    navigate(`/backlog?${params.toString()}`);
  }

  async function handleDuplicate() {
    if (!vessel) return;
    try {
      const created = await createVessel(buildVesselDuplicatePayload(vessel));
      pushToast('success', t('Vessel "{{name}}" duplicated.', { name: created.name }));
      navigate(`/vessels/${created.id}?edit=1`);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Duplicate failed.'));
    }
  }

  if (loading) return <p className="text-dim">{t('Loading...')}</p>;
  if (error && !vessel) return <ErrorModal error={error} onClose={() => setError('')} />;
  if (!vessel) return <p className="text-dim">{t('Vessel not found.')}</p>;

  return (
    <div>
      {/* Breadcrumb */}
      <PageHeader
        breadcrumb={
          <>
            <Link to="/vessels" title={t('Back to the list of all vessels.')}>{t('Vessels')}</Link> <span className="breadcrumb-sep">&gt;</span> <span>{vessel.name}</span>
          </>
        }
        title={vessel.name}
        actions={
          <>
            <button type="button" className="btn btn-sm btn-primary" onClick={handleDispatch} title={t('Go to Dispatch with this vessel selected, to send a captain a new mission.')}>
              {t('Dispatch')}
            </button>
            <button type="button" className="btn btn-sm" onClick={handleManageObjectives} title={t('Open the objectives (backlog) filtered to this vessel and its fleet.')}>
              {t('Manage Objectives')}
            </button>
            {vessel.fleetId && (
              <button type="button" className="btn btn-sm" onClick={() => navigate(`/fleets/${vessel.fleetId}`)} title={t('Open the fleet this vessel belongs to.')}>
                {t('Manage Fleet')}
              </button>
            )}
            <button type="button" className="btn btn-sm" onClick={handleViewHistory} title={t('Open the commit history and activity heatmap for this vessel.')}>
              {t('View History')}
            </button>
            <button type="button" className="btn btn-sm" onClick={() => navigate(`/vessels/${vessel.id}/onboarding`)} title={t('Open the setup checklist that shows what this vessel still needs before missions can run and land smoothly.')}>
              {t('Onboarding')}
            </button>
            <button type="button" className="btn btn-sm" onClick={() => navigate('/checks', { state: { prefill: { vesselId: vessel.id, branchName: vessel.defaultBranch || '' } } })} title={t('Go to Checks with this vessel and its default branch filled in, to run a build, test or other check.')}>
              {t('Run Check')}
            </button>
            <button type="button" className="btn btn-sm" onClick={() => navigate(`/workspace/${vessel.id}`)} title={t('Browse, edit and run commands in this vessel working directory.')}>
              {t('Open Workspace')}
            </button>
            <VesselHealthButton vesselId={vessel.id} vesselName={vessel.name} defaultBranch={vessel.defaultBranch} />
            <ActionMenu id={`vessel-${vessel.id}`} items={[
              { label: 'Dispatch', title: 'Go to Dispatch with this vessel selected, to send a captain a new mission.', onClick: handleDispatch },
              { label: 'Manage Objectives', title: 'Open the objectives (backlog) filtered to this vessel and its fleet.', onClick: handleManageObjectives },
              { label: 'View History', title: 'Open the commit history and activity heatmap for this vessel.', onClick: handleViewHistory },
              { label: 'Manage Fleet', title: 'Open the fleet this vessel belongs to. Unavailable when the vessel has no fleet.', onClick: () => navigate(`/fleets/${vessel.fleetId}`), disabled: !vessel.fleetId },
              { label: 'Run Check', title: 'Go to Checks with this vessel and its default branch filled in, to run a build, test or other check.', onClick: () => navigate('/checks', { state: { prefill: { vesselId: vessel.id, branchName: vessel.defaultBranch || '' } } }) },
              { label: 'Open Workspace', title: 'Browse, edit and run commands in this vessel working directory.', onClick: () => navigate(`/workspace/${vessel.id}`) },
              { label: 'Cleanup', title: "Inspect this vessel's docks, worktrees and Armada branches, see which are safe, in use or unknown, and remove safe ones after you confirm. Opening it deletes nothing.", onClick: () => setCleanupOpen(true) },
              { label: 'Edit', title: 'Open the Edit Vessel form for this vessel.', onClick: openEdit },
              { label: 'Duplicate', title: 'Create a copy of this vessel with the same settings and paths, then open it for editing. Change the name and paths before using both.', onClick: () => void handleDuplicate() },
              { label: 'View JSON', title: 'Show the raw vessel record as JSON.', onClick: () => setJsonData({ open: true, title: t('Vessel: {{name}}', { name: vessel.name }), data: vessel }) },
              { label: 'Delete', title: 'Delete this vessel after you confirm, with its missions, docks and Armada-managed bare clone. Your working directory is never touched.', danger: true, onClick: handleDelete },
            ]} />
          </>
        }
      />

      <ErrorModal error={error} onClose={() => setError('')} />

      {/* Edit Modal (the same form as Vessels > Edit) */}
      {showForm && (
        <VesselFormModal
          vessel={vessel}
          fleets={fleets}
          pipelines={pipelines}
          onClose={() => setShowForm(false)}
          onError={setError}
          onSaved={(name) => {
            setShowForm(false);
            pushToast('success', t('Vessel "{{name}}" saved.', { name }));
            load();
          }}
        />
      )}

      <JsonViewer open={jsonData.open} title={jsonData.title} data={jsonData.data} onClose={() => setJsonData({ open: false, title: '', data: null })} />
      <VesselCleanupModal vesselId={vessel.id} vesselName={vessel.name} open={cleanupOpen} onClose={() => setCleanupOpen(false)} />
      {deletion.dialog}
      <ConfirmDialog open={confirm.open} title={confirm.title} message={confirm.message}
        onConfirm={confirm.onConfirm} onCancel={() => setConfirm(c => ({ ...c, open: false }))} />

      <ReadinessPanel
        title={t('Readiness')}
        readiness={readiness}
        loading={loadingReadiness}
        emptyMessage={t('Readiness data is not available for this vessel yet.')}
      />

      <div className="card landing-preview-card">
        <div className="readiness-panel-header">
          <div>
            <h3 title={t('What would happen if a mission landed on this vessel now: the landing mode, branch rules that apply, and anything predicted to block it.')}>{t('Landing Preview')}</h3>
            <div className="readiness-panel-meta">
              {landingPreview?.sourceBranch ? `${landingPreview.sourceBranch} -> ${landingPreview.targetBranch}` : landingPreview?.targetBranch || t('No branch selected')}
            </div>
          </div>
          <span className={`readiness-pill ${landingPreview?.isReadyToLand ? 'ready' : 'warning'}`} title={landingPreview?.isReadyToLand ? t('No blocking issues are predicted, so a finished mission can land with the current settings.') : t('At least one issue below would block or hold landing. Fix it, or expect missions to wait for review.')}>
            {landingPreview?.isReadyToLand ? t('Ready To Land') : t('Needs Review')}
          </span>
        </div>
        {loadingLandingPreview ? (
          <div className="text-dim">{t('Calculating landing preview...')}</div>
        ) : !landingPreview ? (
          <div className="text-dim">{t('Landing preview is not available for this vessel yet.')}</div>
        ) : (
          <>
            <div className="readiness-summary-row">
              <span title={t('How the branch is classified: Default, Release or Hotfix by the vessel branch prefixes, otherwise Feature. Release and hotfix rules depend on it.')}>{t('Branch category')}: {landingPreview.branchCategory}</span>
              <span title={t('Landing mode in effect. Inherited means the vessel does not set one and the global setting applies.')}>{t('Landing mode')}: {landingPreview.landingMode || t('Inherited')}</span>
              <span title={t('What happens to the mission branch after landing. Inherited means the global setting (Local Only by default).')}>{t('Cleanup')}: {landingPreview.branchCleanupPolicy || t('Inherited')}</span>
              {landingPreview.expectedLandingAction && <span title={t('What Armada will do with finished work under the current landing mode.')}>{t('Action')}: {landingPreview.expectedLandingAction}</span>}
              <span title={landingPreview.requirePassingChecksToLand ? t('This vessel blocks landing until at least one check has passed for the branch.') : t('Landing does not wait for a passing check. Turn on Require Passing Checks To Land to require one.')}>{landingPreview.requirePassingChecksToLand ? t('Passing checks required') : t('Passing checks optional')}</span>
            </div>
            <div className="readiness-summary-row">
              <span title={landingPreview.targetBranchProtected ? t('The target branch matches one of the vessel Protected Branch Patterns, so protected-branch rules apply.') : t('The target branch matches none of the vessel Protected Branch Patterns.')}>{landingPreview.targetBranchProtected ? t('Protected target branch') : t('Target branch not protected')}</span>
              {landingPreview.protectedBranchMatch && <span title={t('The Protected Branch Pattern the target branch matched.')}>{t('Policy')}: <span className="mono">{landingPreview.protectedBranchMatch}</span></span>}
              {landingPreview.requirePullRequestForProtectedBranches && <span title={t('Protected branches may only land through a pull request or the merge queue.')}>{t('PR required for protected branches')}</span>}
              {landingPreview.requireMergeQueueForReleaseBranches && <span title={t('Release branches may only land through the merge queue.')}>{t('Merge queue required for release branches')}</span>}
            </div>
            {landingPreview.latestCheckSummary && (
              <div className="landing-preview-latest-check">
                <strong title={t('Most recent check run for this vessel and branch.')}>{t('Latest check')}</strong>
                <div className="text-dim">{landingPreview.latestCheckSummary}</div>
              </div>
            )}
            {landingPreview.issues.length > 0 ? (
              <div className="readiness-issues">
                {landingPreview.issues.map((issue, index) => (
                  <div key={`${issue.code}-${index}`} className={`readiness-issue ${issue.severity.toLowerCase()}`}>
                    <div className="readiness-issue-title-row">
                      <strong>{issue.title}</strong>
                      <span className={`readiness-issue-severity ${issue.severity.toLowerCase()}`} title={issue.severity === 'Error' ? t('Error: this blocks landing until it is fixed.') : issue.severity === 'Warning' ? t('Warning: landing can proceed, but this is worth fixing.') : t('Information only; nothing is blocked.')}>{issue.severity}</span>
                    </div>
                    <div className="text-dim">{issue.message}</div>
                  </div>
                ))}
              </div>
            ) : (
              <div className="readiness-success-copy">{t('No landing blockers are currently predicted for this vessel.')}</div>
            )}
          </>
        )}
      </div>

      {/* Vessel Info */}
      <div className="detail-grid">
        <div className="detail-field">
          <span className="detail-label" title={t('Vessel ID (vsl_...), used by the CLI, API and MCP tools.')}>{t('ID')}</span>
          <span className="id-display">
            <span className="mono">{vessel.id}</span>
            <CopyButton text={vessel.id} />
          </span>
        </div>
        <div className="detail-field"><span className="detail-label">{t('Name')}</span><span>{vessel.name}</span></div>
        <div className="detail-field">
          <span className="detail-label" title={t('Fleet this vessel belongs to. Click to open the fleet.')}>{t('Fleet')}</span>
          {vessel.fleetId ? (
            <Link to={`/fleets/${vessel.fleetId}`}>{fleetMap.get(vessel.fleetId) ?? vessel.fleetId}</Link>
          ) : <span>-</span>}
        </div>
        <div className="detail-field">
          <span className="detail-label" title={t('Git remote Armada clones, fetches and pushes to. Click to open it in a new tab.')}>{t('Repo URL')}</span>
          {vessel.repoUrl
            ? <a href={vessel.repoUrl} target="_blank" rel="noopener noreferrer" className="mono">{vessel.repoUrl}</a>
            : <span>-</span>}
        </div>
        <div className="detail-field"><span className="detail-label" title={t('Branch missions start from and land into, e.g. main or develop. Defaults to main. Armada creates it in the bare clone if it is missing.')}>{t('Default Branch')}</span><span>{vessel.defaultBranch || 'main'}</span></div>
        <div className="detail-field"><span className="detail-label" title={t('Folder on the Admiral host where Armada keeps its bare clone of the repo; mission worktrees are made from it. Leave blank and Armada clones into its repos directory on first dispatch.')}>{t('Local Path')}</span><span className="mono" title={vessel.localPath || t('Not set yet. Armada clones the repository into its repos directory the first time a mission is dispatched.')}>{vessel.localPath || '-'}</span></div>
        <div className="detail-field"><span className="detail-label" title={t('Your own checkout of this repo on the Admiral host, e.g. /home/me/code/my-service. Local Merge lands here, and Workspace, checks, health and fleet actions run in it. Armada never deletes it.')}>{t('Working Directory')}</span><span className="mono" title={vessel.workingDirectory || t('Not set. Local Merge, Workspace, health and fleet actions need a working directory (or a Harbor with a checkout).')}>{vessel.workingDirectory || '-'}</span></div>
        <div className="detail-field"><span className="detail-label" title={t('How finished mission work reaches the repo: local merge, merge and push, pull request, merge queue, or manual. Default uses the voyage or global setting.')}>{t('Landing Mode')}</span><span>{vessel.landingMode || '-'}</span></div>
        <div className="detail-field"><span className="detail-label" title={t('What happens to a mission branch after it lands. Local Only deletes it from the bare clone; Local and Remote also deletes it on the remote; None keeps it. Default is the global setting (Local Only).')}>{t('Branch Cleanup Policy')}</span><span>{vessel.branchCleanupPolicy || '-'}</span></div>
        <div className="detail-field"><span className="detail-label" title={t('Branch name prefix that marks a release branch, e.g. release/ matches release/1.2. Used by the release merge queue rule below. Defaults to release/.')}>{t('Release Branch Prefix')}</span><span className="mono">{vessel.releaseBranchPrefix || 'release/'}</span></div>
        <div className="detail-field"><span className="detail-label" title={t('Branch name prefix that marks a hotfix branch, e.g. hotfix/ matches hotfix/login-fix. Landing readiness warns when a protected hotfix lands by local merge. Defaults to hotfix/.')}>{t('Hotfix Branch Prefix')}</span><span className="mono">{vessel.hotfixBranchPrefix || 'hotfix/'}</span></div>
        <div className="detail-field"><span className="detail-label" title={t('Block landing until at least one structured check (build, test, lint) has passed for the mission branch. Off by default.')}>{t('Require Passing Checks To Land')}</span><span>{vessel.requirePassingChecksToLand ? t('Yes') : t('No')}</span></div>
        <div className="detail-field"><span className="detail-label" title={t('When the target branch matches a Protected Branch Pattern, only Pull Request or Merge Queue landing is allowed; local merges are blocked. Off by default.')}>{t('Require PR For Protected Branches')}</span><span>{vessel.requirePullRequestForProtectedBranches ? t('Yes') : t('No')}</span></div>
        <div className="detail-field"><span className="detail-label" title={t('Branches starting with the Release Branch Prefix may only land through the merge queue. Off by default.')}>{t('Require Merge Queue For Release Branches')}</span><span>{vessel.requireMergeQueueForReleaseBranches ? t('Yes') : t('No')}</span></div>
        <div className="detail-field"><span className="detail-label" title={t('Let several missions be active on this vessel at once, each in its own worktree. Off (the default) runs one mission at a time, which avoids merge conflicts between missions.')}>{t('Allow Concurrent Missions')}</span><span>{vessel.allowConcurrentMissions ? t('Yes') : t('No')}</span></div>
        <div className="detail-field"><span className="detail-label" title={t('Whether CLI captains run here with their auto-approve (permission bypass) flags. Use captain setting keeps each captain as configured; On or Off overrides every captain on this vessel.')}>{t('Agent Auto-Approve')}</span><span>{vessel.autoApprove === true ? t('On for this vessel') : vessel.autoApprove === false ? t('Off for this vessel') : t('Use captain setting')}</span></div>
        <div className="detail-field"><span className="detail-label" title={t('When on, a passing mission lands unattended only if it meets the file, line and path limits below; anything larger holds for your review. Off uses the normal landing and review rules.')}>{t('Auto-Land Gate')}</span><span>{vessel.autoLandEnabled ? t('Enabled') : t('Disabled')}</span></div>
        {vessel.autoLandEnabled && (
          <>
            <div className="detail-field"><span className="detail-label" title={t('Largest number of changed files a mission may have and still auto-land, e.g. 10. 0 or blank means no file limit.')}>{t('Auto-Land Max Files')}</span><span>{vessel.autoLandMaxFiles ?? 0}</span></div>
            <div className="detail-field"><span className="detail-label" title={t('Largest number of changed lines (added plus removed) a mission may have and still auto-land, e.g. 200. 0 or blank means no line limit.')}>{t('Auto-Land Max Lines')}</span><span>{vessel.autoLandMaxLines ?? 0}</span></div>
            <div className="detail-field"><span className="detail-label" title={t('When set, a mission auto-lands only if every changed file matches one of these globs, one per line, e.g. src/** or docs/**. Blank allows any path.')}>{t('Auto-Land Allowed Paths')}</span><span className="mono">{(vessel.autoLandPathAllowGlobs && vessel.autoLandPathAllowGlobs.length) ? vessel.autoLandPathAllowGlobs.join(', ') : '-'}</span></div>
            <div className="detail-field"><span className="detail-label" title={t('A mission that changes any file matching these globs never auto-lands and always holds for review, one per line, e.g. infra/** or *.csproj.')}>{t('Auto-Land Denied Paths')}</span><span className="mono">{(vessel.autoLandPathDenyGlobs && vessel.autoLandPathDenyGlobs.length) ? vessel.autoLandPathDenyGlobs.join(', ') : '-'}</span></div>
          </>
        )}
        <div className="detail-field"><span className="detail-label" title={t('Run the build and test commands below inside the mission worktree before the mission is accepted. A failure, or a timeout, blocks acceptance and records the reason. Off by default.')}>{t('Definition-of-Done Gate')}</span><span>{vessel.definitionOfDoneEnabled ? t('Enabled') : t('Disabled')}</span></div>
        {vessel.definitionOfDoneEnabled && (
          <>
            <div className="detail-field"><span className="detail-label" title={t('Shell command that builds the project in the mission worktree, e.g. dotnet build or npm run build. A non-zero exit blocks acceptance as a compile failure. Blank skips the build step.')}>{t('DoD Build Command')}</span><span className="mono">{vessel.definitionOfDoneBuildCommand || '-'}</span></div>
            <div className="detail-field"><span className="detail-label" title={t('Shell command that runs the unit tests in the mission worktree, e.g. dotnet test or npm test. A non-zero exit blocks acceptance as a test failure. Blank skips the test step.')}>{t('DoD Test Command')}</span><span className="mono">{vessel.definitionOfDoneTestCommand || '-'}</span></div>
            <div className="detail-field"><span className="detail-label" title={t('Time limit in seconds for each of the build and test commands, from 30 to 7200. Blank uses 1800 (30 minutes). Going over counts as a timeout failure.')}>{t('DoD Timeout (s)')}</span><span>{vessel.definitionOfDoneTimeoutSeconds ?? 1800}</span></div>
          </>
        )}
        <div className="detail-field"><span className="detail-label" title={t('Whether this vessel has its own GitHub token. Configured means a vessel token is stored (never shown); otherwise the global token from Settings is used, if any.')}>{t('GitHub Token Override')}</span><span>{vessel.hasGitHubTokenOverride ? t('Configured') : t('Inherited / None')}</span></div>
        <div className="detail-field">
          <span className="detail-label" title={t('Persona pipeline used when a dispatch to this vessel does not name one, e.g. Architect then Worker then Judge. None falls back to the fleet default, then a single Worker stage.')}>{t('Default Pipeline')}</span>
          <span>{pipelines.find(p => p.id === vessel.defaultPipelineId)?.name || vessel.defaultPipelineId || <span className="text-dim">{t('None (WorkerOnly)')}</span>}</span>
        </div>
        <div className="detail-field"><span className="detail-label">{t('Active')}</span><span>{vessel.active !== false ? t('Yes') : t('No')}</span></div>
        <div className="detail-field">
          <span className="detail-label" title={t('When the vessel was registered with Armada.')}>{t('Created')}</span>
          <span title={vessel.createdUtc}>
            {formatRelativeTime(vessel.createdUtc)}
            <span className="text-dim"> ({formatDateTime(vessel.createdUtc)})</span>
          </span>
        </div>
        <div className="detail-field">
          <span className="detail-label" title={t('When the vessel settings were last changed.')}>{t('Last Updated')}</span>
          <span title={vessel.lastUpdateUtc}>
            {formatRelativeTime(vessel.lastUpdateUtc)}
            <span className="text-dim"> ({formatDateTime(vessel.lastUpdateUtc)})</span>
          </span>
        </div>
      </div>

      {/* Project Context */}
      {vessel.projectContext && (
        <div className="detail-context-section">
          <h4 title={t('What this project is: purpose, architecture, key files and dependencies. Added to every captain prompt for this vessel, so keep it accurate and concise.')}>{t('Project Context')}</h4>
          <pre className="detail-context-block">{vessel.projectContext}</pre>
        </div>
      )}

      {/* Style Guide */}
      {vessel.styleGuide && (
        <div className="detail-context-section">
          <h4 title={t('Coding rules captains must follow here: naming, patterns, language restrictions, preferred libraries. Added to every captain prompt for this vessel.')}>{t('Style Guide')}</h4>
          <pre className="detail-context-block">{vessel.styleGuide}</pre>
        </div>
      )}

      {vessel.protectedBranchPatterns && vessel.protectedBranchPatterns.length > 0 && (
        <div className="detail-context-section">
          <h4 title={t('Branches treated as protected, one exact name or glob per line, e.g. main or release/*. Used by the Require PR rule below and shown in landing readiness. Blank protects nothing.')}>{t('Protected Branch Patterns')}</h4>
          <pre className="detail-context-block">{vessel.protectedBranchPatterns.join('\n')}</pre>
        </div>
      )}

      {(vessel.secretScanEnabled || (vessel.protectedPathPatterns && vessel.protectedPathPatterns.length > 0) || (vessel.privateIdentifierDenylist && vessel.privateIdentifierDenylist.length > 0)) && (
        <div className="detail-context-section">
          <h4 title={t('Checks run on each mission diff before it lands: secret scanning, protected paths a mission may not touch, and private strings that must not leak.')}>{t('Dock Boundary')}</h4>
          <div className="detail-field"><span className="detail-label" title={t('Before landing, scan each mission diff for secrets such as API keys, and block changes to Protected Path Patterns or text from the Private Identifier Denylist. Off by default.')}>{t('Secret Scan')}</span><span>{vessel.secretScanEnabled ? t('Enabled') : t('Disabled')}</span></div>
          {vessel.protectedPathPatterns && vessel.protectedPathPatterns.length > 0 && (
            <pre className="detail-context-block">{t('Protected paths')}:{'\n'}{vessel.protectedPathPatterns.join('\n')}</pre>
          )}
          {vessel.privateIdentifierDenylist && vessel.privateIdentifierDenylist.length > 0 && (
            <pre className="detail-context-block">{t('Private identifiers')}:{'\n'}{vessel.privateIdentifierDenylist.join('\n')}</pre>
          )}
        </div>
      )}

      {/* Model Context */}
      {vessel.enableModelContext && vessel.modelContext && (
        <div className="detail-context-section">
          <h4 title={t('Notes captains have accumulated about this repo during missions, fed to future missions. You can edit or trim it. Turn on Enable Model Context to use it.')}>{t('Model Context')}</h4>
          <pre className="detail-context-block">{vessel.modelContext}</pre>
        </div>
      )}

      <VesselReservationsCard vesselId={vessel.id} serializeOverlappingMissions={vessel.serializeOverlappingMissions} />
      <VesselBlackBoxDoctorCard vesselId={vessel.id} onApplied={() => { void load(); }} />

      {/* Recent Missions */}
      <div style={{ marginTop: '1rem' }}>
        <h3 title={t('Missions dispatched to this vessel, newest first. Click a row to open the mission.')}>{t('Missions')}</h3>
        {missions.length > 0 || missionTotals.totalRecords > 0 ? (
          <DataTable
            tableKey="vessel-detail-missions"
            rows={missions}
            rowKey={(m) => m.id}
            pagination={{
              pageNumber: missionPage,
              pageSize: missionPageSize,
              totalPages: missionTotals.totalPages,
              totalRecords: missionTotals.totalRecords,
              totalMs: missionTotals.totalMs,
              onPageChange: setMissionPage,
              onPageSizeChange: (size) => { setMissionPageSize(size); setMissionPage(1); },
            }}
            onRowClick={(m) => navigate(`/missions/${m.id}`)}
            columns={[
              { key: 'actions', label: t('Actions'), fixed: true, interactive: true, render: m => <ActionMenu id={`vessel-mission-${m.id}`} items={[
                { label: 'Open', onClick: () => navigate(`/missions/${m.id}`) },
                { label: 'Duplicate', onClick: () => navigate(`/missions?duplicate=${encodeURIComponent(m.id)}`) },
              ]} /> },
              {
                key: 'id', label: t('ID'), headerTitle: t('Mission ID (msn_...), used by the CLI, API and MCP tools.'), required: true, cellClassName: 'mono text-dim table-id-cell',
                render: (m) => (
                  <span className="id-display">
                    <span className="id-value" title={m.id}>{m.id}</span>
                    <CopyButton text={m.id} />
                  </span>
                ),
              },
              {
                key: 'mission', label: t('Mission'), required: true, headerTitle: t('Mission title. Click a row to open the mission.'),
                render: (m) => <strong className="cell-one-line" title={m.title}>{m.title}</strong>,
              },
              { key: 'status', label: t('Status'), headerTitle: t('Where the mission is in its lifecycle, from Pending through InProgress and WorkProduced to Complete, Failed or Cancelled.'), cellClassName: 'cell-nowrap', render: (m) => <StatusBadge status={m.status} /> },
              {
                key: 'captain', label: t('Captain'), headerTitle: t('ID of the captain (AI agent) assigned to the mission. A dash means none is assigned yet.'), cellClassName: 'mono',
                render: (m) => <span className="cell-one-line" title={m.captainId || undefined}>{m.captainId || '-'}</span>,
              },
              {
                key: 'branch', label: t('Branch'), headerTitle: t('Git branch the captain commits the mission work to, usually armada/<captain>/<mission id>.'), cellClassName: 'mono text-dim',
                render: (m) => <span className="cell-one-line" title={m.branchName || undefined}>{m.branchName || '-'}</span>,
              },
            ]}
          />
        ) : (
          <p className="text-dim" style={{ marginTop: '0.5rem' }}>{t('No missions yet')}</p>
        )}
      </div>
    </div>
  );
}
