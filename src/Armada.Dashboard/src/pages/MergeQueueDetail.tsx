import { useEffect, useState, useCallback } from 'react';
import { useParams, useNavigate, Link } from 'react-router-dom';
import { apiErrorMessage, getMergeEntry, deleteMergeEntry, processMergeEntry, cancelMergeEntry, listVessels, getMissionDiff, getMissionLog, getVesselLandingPreview } from '../api/client';
import { listAll } from '../api/paging';
import type { MergeEntry, Vessel, LandingPreviewResult } from '../types/models';
import ActionMenu from '../components/shared/ActionMenu';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import JsonViewer from '../components/shared/JsonViewer';
import DiffViewer from '../components/shared/DiffViewer';
import LogViewer from '../components/shared/LogViewer';
import PageHeader from '../components/shared/PageHeader';
import StatusBadge from '../components/shared/StatusBadge';
import CopyButton from '../components/shared/CopyButton';
import ErrorModal from '../components/shared/ErrorModal';
import { useLocale } from '../context/LocaleContext';
import { useNotifications } from '../context/NotificationContext';
import { useDeleteProgress } from '../lib/useDeleteProgress';

export default function MergeQueueDetail() {
  const { t, formatDateTime, formatRelativeTime } = useLocale();
  const { pushToast } = useNotifications();
  const deletion = useDeleteProgress();
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const [entry, setEntry] = useState<MergeEntry | null>(null);
  const [vessels, setVessels] = useState<Vessel[]>([]);
  const [landingPreview, setLandingPreview] = useState<LandingPreviewResult | null>(null);
  const [loadingLandingPreview, setLoadingLandingPreview] = useState(false);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  // JSON viewer
  const [jsonData, setJsonData] = useState<{ open: boolean; title: string; data: unknown }>({ open: false, title: '', data: null });

  // Confirm
  const [confirm, setConfirm] = useState<{ open: boolean; title: string; message: string; onConfirm: () => void }>({ open: false, title: '', message: '', onConfirm: () => {} });

  // Diff viewer
  const [diffModal, setDiffModal] = useState<{ open: boolean; title: string; rawDiff: string; loading: boolean; error?: string }>({ open: false, title: '', rawDiff: '', loading: false });

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
      const [e, vResult] = await Promise.all([getMergeEntry(id), listAll(listVessels)]);
      setEntry(e);
      setVessels(vResult.objects);
      if (e.vesselId) {
        setLoadingLandingPreview(true);
        getVesselLandingPreview(e.vesselId, e.branchName)
          .then((result) => setLandingPreview(result))
          .catch(() => setLandingPreview(null))
          .finally(() => setLoadingLandingPreview(false));
      } else {
        setLandingPreview(null);
      }
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
        if (!(await deletion.run({ kind: 'mergeEntry', action: () => deleteMergeEntry(entry.id), errorMessage: (err) => apiErrorMessage(err, t('Delete failed.')) }))) return;
        pushToast('warning', t('Merge entry {{id}} deleted.', { id: entry.id }));
        navigate('/merge-queue');
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
    } catch (e) {
      setDiffModal(d => ({ ...d, rawDiff: '', loading: false, error: e instanceof Error ? e.message : String(e) }));
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
    { label: 'View JSON', title: 'Show the raw merge entry record as the API returns it.', onClick: () => setJsonData({ open: true, title: t('Merge Entry: {{id}}', { id: entry.id }), data: entry }) },
    { label: 'Process', title: 'Merge, test and land this entry now instead of waiting for the next automatic pass.', onClick: handleProcess },
    { label: 'Cancel', title: 'Take this entry out of the queue without merging it. The record is kept.', onClick: handleCancel },
    ...(entry.missionId ? [
      { label: 'Mission Diff', title: 'Show the code changes made by the mission this entry merges.', onClick: handleMissionDiff },
      { label: 'Mission Log', title: 'Show the captain\'s session log for the mission this entry merges.', onClick: handleMissionLog },
    ] : []),
    { label: 'Delete', danger: true as const, title: 'Permanently delete a finished entry (Landed, Failed or Cancelled), or cancel one that is still active. You confirm first.', onClick: handleDelete },
  ];

  return (
    <div>
      <PageHeader
        breadcrumb={[
          { label: t('Merge Queue'), to: '/merge-queue' },
          { label: entry.id },
        ]}
        title={t('Merge Entry')}
        actions={
          <>
            {entry.missionId && (
              <>
                <button className="btn btn-sm" onClick={handleMissionDiff} title={t('Show the code changes made by the mission this entry merges.')}>{t('Diff')}</button>
                <button className="btn btn-sm" onClick={handleMissionLog} title={t('Show the captain\'s session log for the mission this entry merges.')}>{t('Log')}</button>
              </>
            )}
            <ActionMenu id={`merge-entry-action-${entry.id}`} items={actionItems} />
          </>
        }
      />

      <ErrorModal error={error} onClose={() => setError('')} />

      <JsonViewer open={jsonData.open} title={jsonData.title} data={jsonData.data} onClose={() => setJsonData({ open: false, title: '', data: null })} />
      <ConfirmDialog open={confirm.open} title={confirm.title} message={confirm.message}
        onConfirm={confirm.onConfirm} onCancel={() => setConfirm(c => ({ ...c, open: false }))} />
      {deletion.dialog}
      <DiffViewer
        open={diffModal.open}
        title={diffModal.title}
        rawDiff={diffModal.rawDiff}
        error={diffModal.error}
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

      <div className="card landing-preview-card" style={{ marginBottom: '1rem' }}>
        <div className="readiness-panel-header">
          <div>
            <h3 title={t('What would happen if this branch landed now, based on the vessel\'s landing mode, branch protection and check rules. Nothing is merged by viewing it.')}>{t('Landing Preview')}</h3>
            <div className="readiness-panel-meta" title={t('Branch being merged, then the target branch it merges into.')}>{`${entry.branchName} -> ${entry.targetBranch}`}</div>
          </div>
          <span className={`readiness-pill ${landingPreview?.isReadyToLand ? 'ready' : 'warning'}`} title={landingPreview?.isReadyToLand ? t('No blocking issues were found; the entry can land when the queue processes it.') : t('Something may block landing. See the issues listed in this card.')}>
            {landingPreview?.isReadyToLand ? t('Ready To Land') : t('Needs Review')}
          </span>
        </div>
        {loadingLandingPreview ? (
          <div className="text-dim">{t('Calculating landing preview...')}</div>
        ) : !landingPreview ? (
          <div className="text-dim">{t('Landing preview is not available for this merge entry yet.')}</div>
        ) : (
          <>
            <div className="readiness-summary-row">
              <span title={t('Kind of target branch (for example Default or Release), which decides which landing rules apply.')}>{t('Branch category')}: {landingPreview.branchCategory}</span>
              <span title={t('How the work lands: LocalMerge, MergeAndPush, PullRequest, MergeQueue or None (merge by hand). Inherited means the voyage, vessel or server default applies.')}>{t('Landing mode')}: {landingPreview.landingMode || t('Inherited')}</span>
              <span title={t('What happens to the mission branch after it lands, such as deleting it locally or on the remote. Inherited means the default applies.')}>{t('Cleanup')}: {landingPreview.branchCleanupPolicy || t('Inherited')}</span>
              {landingPreview.expectedLandingAction && <span title={t('The landing step Armada expects to take once all policies are applied.')}>{t('Action')}: {landingPreview.expectedLandingAction}</span>}
            </div>
            <div className="readiness-summary-row">
              <span title={t('Whether the target branch matches one of the vessel\'s protected-branch patterns, which can force pull-request landing.')}>{landingPreview.targetBranchProtected ? t('Protected target branch') : t('Target branch not protected')}</span>
              {landingPreview.protectedBranchMatch && <span title={t('Protected-branch pattern that matched the target branch.')}>{t('Policy')}: <span className="mono">{landingPreview.protectedBranchMatch}</span></span>}
              {landingPreview.requirePullRequestForProtectedBranches && <span title={t('This vessel lands into protected branches only through a pull request, never a direct merge.')}>{t('PR required for protected branches')}</span>}
              {landingPreview.requireMergeQueueForReleaseBranches && <span title={t('This vessel lands into release branches only through the merge queue, which tests each change before merging.')}>{t('Merge queue required for release branches')}</span>}
            </div>
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
              <div className="readiness-success-copy">{t('No landing blockers are currently predicted for this merge entry.')}</div>
            )}
          </>
        )}
      </div>

      {/* Entry Info */}
      <div className="detail-grid">
        <div className="detail-field">
          <span className="detail-label" title={t('Unique merge entry ID (mrg_ prefix).')}>{t('ID')}</span>
          <span className="id-display">
            <span className="mono">{entry.id}</span>
            <CopyButton text={entry.id} />
          </span>
        </div>
        <div className="detail-field"><span className="detail-label" title={t('Queued waits its turn, Testing is merging and running tests, Landed is merged, Failed hit a conflict or failing test, Cancelled was removed.')}>{t('Status')}</span><StatusBadge status={entry.status} /></div>
        <div className="detail-field"><span className="detail-label" title={t('Branch being merged, usually a mission branch.')}>{t('Branch')}</span><span className="mono">{entry.branchName}</span></div>
        <div className="detail-field"><span className="detail-label" title={t('Branch this entry merges into.')}>{t('Target Branch')}</span><span className="mono">{entry.targetBranch}</span></div>
        <div className="detail-field"><span className="detail-label" title={t('Order within this vessel and target branch. Lower numbers are processed first.')}>{t('Priority')}</span><span>{entry.priority}</span></div>
        <div className="detail-field">
          <span className="detail-label" title={t('Repository the branch is merged in.')}>{t('Vessel')}</span>
          {entry.vesselId
            ? <Link to={`/vessels/${entry.vesselId}`}>{vesselName(entry.vesselId)}</Link>
            : <span>-</span>}
        </div>
        <div className="detail-field">
          <span className="detail-label" title={t('Mission whose work this entry merges. Shows - for a branch enqueued by hand.')}>{t('Mission')}</span>
          {entry.missionId
            ? <Link to={`/missions/${entry.missionId}`} className="mono">{entry.missionId}</Link>
            : <span>-</span>}
        </div>
        <div className="detail-field"><span className="detail-label" title={t('Processing batch this entry was tested in, if any.')}>{t('Batch ID')}</span><span className="mono">{entry.batchId || '-'}</span></div>
        <div className="detail-field"><span className="detail-label" title={t('Exit code of the test command. 0 means the tests passed; anything else marks the entry Failed.')}>{t('Test Exit Code')}</span><span>{entry.testExitCode ?? '-'}</span></div>
        <div className="detail-field"><span className="detail-label" title={t('Tenant (organization) that owns this entry.')}>{t('Tenant ID')}</span><span className="mono">{entry.tenantId || '-'}</span></div>
        <div className="detail-field"><span className="detail-label" title={t('Shell command run in the merged result before landing. - means no tests are run.')}>{t('Test Command')}</span><span className="mono">{entry.testCommand || '-'}</span></div>
        <div className="detail-field">
          <span className="detail-label" title={t('When the entry was added to the queue.')}>{t('Created')}</span>
          <span>{formatRelativeTime(entry.createdUtc)} <span className="text-dim">({formatDateTime(entry.createdUtc)})</span></span>
        </div>
        <div className="detail-field">
          <span className="detail-label" title={t('When the queue started merging and testing this entry.')}>{t('Test Started')}</span>
          <span>{entry.testStartedUtc ? <>{formatRelativeTime(entry.testStartedUtc)} <span className="text-dim">({formatDateTime(entry.testStartedUtc)})</span></> : '-'}</span>
        </div>
        <div className="detail-field">
          <span className="detail-label" title={t('When the entry landed, failed or was cancelled.')}>{t('Completed')}</span>
          <span>{entry.completedUtc ? <>{formatRelativeTime(entry.completedUtc)} <span className="text-dim">({formatDateTime(entry.completedUtc)})</span></> : '-'}</span>
        </div>
        <div className="detail-field">
          <span className="detail-label" title={t('When anything on this entry last changed.')}>{t('Last Updated')}</span>
          <span>{formatRelativeTime(entry.lastUpdateUtc)} <span className="text-dim">({formatDateTime(entry.lastUpdateUtc)})</span></span>
        </div>
      </div>

      {/* Test Command */}
      {entry.testCommand && (
        <div className="detail-context-section">
          <h4 title={t('Shell command run in the merged result before landing.')}>{t('Test Command')}</h4>
          <pre className="detail-context-block">{entry.testCommand}</pre>
        </div>
      )}

      {/* Test Output */}
      {entry.testOutput && (
        <div className="detail-context-section">
          <h4 title={t('Output of the test command from the last run, useful when the entry Failed.')}>{t('Test Output')}</h4>
          <pre className="detail-context-block">{entry.testOutput}</pre>
        </div>
      )}
    </div>
  );
}
