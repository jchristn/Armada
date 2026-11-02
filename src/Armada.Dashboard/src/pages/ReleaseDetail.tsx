import { useEffect, useMemo, useState } from 'react';
import { Link, useLocation, useNavigate, useParams } from 'react-router-dom';
import {
  createRelease,
  deleteRelease,
  getRelease,
  getReleaseGitHubPullRequests,
  listCheckRuns,
  listDeployments,
  listObjectives,
  listVessels,
  listVoyages,
  listWorkflowProfiles,
  refreshRelease,
  updateRelease,
} from '../api/client';
import { listAll } from '../api/paging';
import type { CheckRun, Deployment, GitHubPullRequestDetail, Objective, Release, ReleaseStatus, ReleaseUpsertRequest, Vessel, Voyage, WorkflowProfile } from '../types/models';
import { useAuth } from '../context/AuthContext';
import { useLocale } from '../context/LocaleContext';
import { useNotifications } from '../context/NotificationContext';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import CopyButton from '../components/shared/CopyButton';
import DataTable from '../components/shared/DataTable';
import ErrorModal from '../components/shared/ErrorModal';
import JsonViewer from '../components/shared/JsonViewer';
import PageHeader from '../components/shared/PageHeader';
import StatusBadge from '../components/shared/StatusBadge';
import { joinList, RELEASE_STATUSES, splitList } from '../lib/deliveryForms';
import { useDeleteProgress } from '../lib/useDeleteProgress';

interface ReleasePrefillState {
  prefill?: ReleaseUpsertRequest;
  objectiveIds?: string[];
}

/** Pull requests read per page; each one is a GitHub read, so pages stay small. */
const GITHUB_PULL_REQUEST_PAGE_SIZE = 25;

export default function ReleaseDetail() {
  const { id } = useParams<{ id: string }>();
  const location = useLocation();
  const navigate = useNavigate();
  const { isAdmin, isTenantAdmin } = useAuth();
  const { t, formatDateTime, formatRelativeTime } = useLocale();
  const deletion = useDeleteProgress();
  const { pushToast } = useNotifications();

  // The static route releases/new has no :id parameter; releases/:id with id "new" is the same page.
  const createMode = !id || id === 'new';
  const canManage = isAdmin || isTenantAdmin;

  const tips = {
    title: t('Release name shown in lists and on deployments. Blank uses the vessel name and version, such as api 1.4.0.'),
    status: t('Lifecycle stage: Draft, Candidate, Shipped, Failed, or RolledBack. Setting Shipped records the published time once.'),
    vessel: t('Repository the release belongs to. Blank infers it from the linked missions or check runs; all linked work must share one vessel.'),
    workflowProfile: t('Profile used for this release and the default for its deployments. Blank uses the vessel\'s profile, then its fleet\'s, then the global default.'),
    version: t('Release version, such as 1.4.0. Blank uses a version found in a linked release-versioning check, else the vessel\'s highest release version with the patch bumped.'),
    tagName: t('Git tag for the release, such as v1.4.0. Blank uses v plus the version. Deployments of this release default to this ref.'),
    summary: t('One-paragraph description of what ships. Blank generates one from the linked voyages, missions, and checks.'),
    notes: t('Release notes as plain text. Blank generates notes from the linked work and artifacts; edit them before marking the release Shipped.'),
    voyageIds: t('Voyages included in this release, one ID per line or comma-separated, such as vyg_abc123. Their missions are linked automatically.'),
    missionIds: t('Missions included in this release, one ID per line or comma-separated, such as msn_abc123. Each must belong to the release\'s vessel.'),
    checkRunIds: t('Check runs that prove readiness, one ID per line or comma-separated, such as chk_abc123. Their artifacts become the release artifacts.'),
  };

  const [release, setRelease] = useState<Release | null>(null);
  const [vessels, setVessels] = useState<Vessel[]>([]);
  const [profiles, setProfiles] = useState<WorkflowProfile[]>([]);
  const [voyages, setVoyages] = useState<Voyage[]>([]);
  const [checkRuns, setCheckRuns] = useState<CheckRun[]>([]);
  const [deployments, setDeployments] = useState<Deployment[]>([]);
  const [objectives, setObjectives] = useState<Objective[]>([]);
  const [gitHubPullRequests, setGitHubPullRequests] = useState<GitHubPullRequestDetail[]>([]);
  const [vesselId, setVesselId] = useState('');
  const [workflowProfileId, setWorkflowProfileId] = useState('');
  const [title, setTitle] = useState('Draft Release');
  const [version, setVersion] = useState('');
  const [tagName, setTagName] = useState('');
  const [summary, setSummary] = useState('');
  const [notes, setNotes] = useState('');
  const [status, setStatus] = useState<ReleaseStatus>('Draft');
  const [voyageIds, setVoyageIds] = useState('');
  const [missionIds, setMissionIds] = useState('');
  const [checkRunIds, setCheckRunIds] = useState('');
  const [prefillObjectiveIds, setPrefillObjectiveIds] = useState<string[]>([]);
  const [loading, setLoading] = useState(!createMode);
  const [loadingGitHubPullRequests, setLoadingGitHubPullRequests] = useState(false);
  const [gitHubPullRequestTotal, setGitHubPullRequestTotal] = useState(0);
  const [gitHubPullRequestPage, setGitHubPullRequestPage] = useState(1);
  const [loadingMoreGitHubPullRequests, setLoadingMoreGitHubPullRequests] = useState(false);
  const [saving, setSaving] = useState(false);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState('');
  const [jsonData, setJsonData] = useState<{ open: boolean; title: string; data: unknown }>({ open: false, title: '', data: null });
  const [confirm, setConfirm] = useState<{ open: boolean; title: string; message: string; onConfirm: () => void }>({
    open: false,
    title: '',
    message: '',
    onConfirm: () => {},
  });

  useEffect(() => {
    let cancelled = false;

    Promise.all([
      listAll(listVessels),
      listAll(listWorkflowProfiles),
      listAll(listVoyages),
      listAll(listCheckRuns),
    ]).then(([vesselResult, profileResult, voyageResult, checkRunResult]) => {
      if (cancelled) return;
      setVessels(vesselResult.objects || []);
      setProfiles(profileResult.objects || []);
      setVoyages(voyageResult.objects || []);
      setCheckRuns(checkRunResult.objects || []);
    }).catch(() => {
      if (!cancelled) setError(t('Failed to load release reference data.'));
    });

    return () => { cancelled = true; };
  }, [t]);

  useEffect(() => {
    if (!createMode || !location.state) return;
    const state = location.state as ReleasePrefillState;
    if (!state.prefill) return;

    setVesselId(state.prefill.vesselId || '');
    setWorkflowProfileId(state.prefill.workflowProfileId || '');
    setTitle(state.prefill.title || 'Draft Release');
    setVersion(state.prefill.version || '');
    setTagName(state.prefill.tagName || '');
    setSummary(state.prefill.summary || '');
    setNotes(state.prefill.notes || '');
    setStatus(state.prefill.status || 'Draft');
    setVoyageIds(joinList(state.prefill.voyageIds));
    setMissionIds(joinList(state.prefill.missionIds));
    setCheckRunIds(joinList(state.prefill.checkRunIds));
    setPrefillObjectiveIds(state.objectiveIds || []);
  }, [createMode, location.state]);

  useEffect(() => {
    if (createMode || !id) return;
    let mounted = true;
    const releaseId = id;

    async function load() {
      try {
        setLoading(true);
        const result = await getRelease(releaseId);
        if (!mounted) return;
        setRelease(result);
        setVesselId(result.vesselId || '');
        setWorkflowProfileId(result.workflowProfileId || '');
        setTitle(result.title);
        setVersion(result.version || '');
        setTagName(result.tagName || '');
        setSummary(result.summary || '');
        setNotes(result.notes || '');
        setStatus(result.status);
        setVoyageIds(joinList(result.voyageIds));
        setMissionIds(joinList(result.missionIds));
        setCheckRunIds(joinList(result.checkRunIds));
        setError('');
      } catch (err: unknown) {
        if (mounted) setError(err instanceof Error ? err.message : t('Failed to load release.'));
      } finally {
        if (mounted) setLoading(false);
      }
    }

    load();
    return () => { mounted = false; };
  }, [createMode, id, t]);

  const vesselMap = useMemo(() => new Map(vessels.map((vessel) => [vessel.id, vessel.name])), [vessels]);
  const workflowProfileMap = useMemo(() => new Map(profiles.map((profile) => [profile.id, profile.name])), [profiles]);
  const voyageMap = useMemo(() => new Map(voyages.map((voyage) => [voyage.id, voyage.title || voyage.id])), [voyages]);
  const checkRunMap = useMemo(() => new Map(checkRuns.map((run) => [run.id, run.label || run.type])), [checkRuns]);
  const relatedDeployments = useMemo(() => deployments.filter((deployment) => deployment.releaseId === release?.id), [deployments, release?.id]);
  const relatedObjectives = useMemo(() => objectives.filter((objective) => objective.releaseIds.includes(release?.id || '')), [objectives, release?.id]);

  useEffect(() => {
    if (createMode || !id) {
      setDeployments([]);
      return;
    }

    let cancelled = false;

    // Ask the server for this release's deployments (every page) instead of filtering one capped page.
    void listAll(listDeployments, { releaseId: id }).then((result) => {
      if (!cancelled) setDeployments(result.objects || []);
    }).catch(() => {
      if (!cancelled) setError(t('Failed to load release reference data.'));
    });

    return () => { cancelled = true; };
  }, [createMode, id, t]);

  useEffect(() => {
    if (createMode || !id) {
      setObjectives([]);
      return;
    }

    let cancelled = false;

    void listAll(listObjectives, { releaseId: id }).then((result) => {
      if (!cancelled) setObjectives(result.objects || []);
    }).catch(() => {
      if (!cancelled) setError(t('Failed to load linked objectives.'));
    });

    return () => { cancelled = true; };
  }, [createMode, id, t]);

  useEffect(() => {
    if (createMode || !id) {
      setGitHubPullRequests([]);
      setGitHubPullRequestTotal(0);
      setLoadingGitHubPullRequests(false);
      return;
    }

    let cancelled = false;
    setLoadingGitHubPullRequests(true);
    // One page at a time: each pull request on the page is read from GitHub; the total drives "Load more".
    getReleaseGitHubPullRequests(id, { pageNumber: 1, pageSize: GITHUB_PULL_REQUEST_PAGE_SIZE }).then((result) => {
      if (cancelled) return;
      setGitHubPullRequests(result?.objects || []);
      setGitHubPullRequestTotal(result?.totalRecords ?? 0);
      setGitHubPullRequestPage(1);
    }).catch(() => {
      if (!cancelled) { setGitHubPullRequests([]); setGitHubPullRequestTotal(0); }
    }).finally(() => {
      if (!cancelled) setLoadingGitHubPullRequests(false);
    });

    return () => { cancelled = true; };
  }, [createMode, id]);

  const loadMoreGitHubPullRequests = () => {
    if (!id || loadingMoreGitHubPullRequests) return;
    const next = gitHubPullRequestPage + 1;
    setLoadingMoreGitHubPullRequests(true);
    getReleaseGitHubPullRequests(id, { pageNumber: next, pageSize: GITHUB_PULL_REQUEST_PAGE_SIZE }).then((result) => {
      setGitHubPullRequests(prev => [...prev, ...(result?.objects || [])]);
      setGitHubPullRequestTotal(result?.totalRecords ?? 0);
      setGitHubPullRequestPage(next);
    }).catch(() => {
      setError(t('Failed to load more linked pull requests.'));
    }).finally(() => setLoadingMoreGitHubPullRequests(false));
  };

  function buildPayload(): ReleaseUpsertRequest {
    return {
      vesselId: vesselId || null,
      workflowProfileId: workflowProfileId || null,
      title: title.trim() || null,
      version: version.trim() || null,
      tagName: tagName.trim() || null,
      summary: summary.trim() || null,
      notes: notes.trim() || null,
      status,
      voyageIds: splitList(voyageIds),
      missionIds: splitList(missionIds),
      checkRunIds: splitList(checkRunIds),
      objectiveIds: prefillObjectiveIds,
    };
  }

  async function handleSave() {
    if (!canManage) return;
    try {
      setSaving(true);
      const payload = buildPayload();
      if (createMode) {
        const created = await createRelease(payload);
        pushToast('success', t('Release "{{title}}" created.', { title: created.title }));
        navigate(`/releases/${created.id}`);
        return;
      }

      if (!id) return;
      const updated = await updateRelease(id, payload);
      setRelease(updated);
      setTitle(updated.title);
      setVersion(updated.version || '');
      setTagName(updated.tagName || '');
      setSummary(updated.summary || '');
      setNotes(updated.notes || '');
      setStatus(updated.status);
      setVoyageIds(joinList(updated.voyageIds));
      setMissionIds(joinList(updated.missionIds));
      setCheckRunIds(joinList(updated.checkRunIds));
      pushToast('success', t('Release "{{title}}" saved.', { title: updated.title }));
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Save failed.'));
    } finally {
      setSaving(false);
    }
  }

  async function handleRefresh() {
    if (!id || createMode) return;
    try {
      setRefreshing(true);
      const refreshed = await refreshRelease(id);
      setRelease(refreshed);
      setVesselId(refreshed.vesselId || '');
      setWorkflowProfileId(refreshed.workflowProfileId || '');
      setTitle(refreshed.title);
      setVersion(refreshed.version || '');
      setTagName(refreshed.tagName || '');
      setSummary(refreshed.summary || '');
      setNotes(refreshed.notes || '');
      setStatus(refreshed.status);
      setVoyageIds(joinList(refreshed.voyageIds));
      setMissionIds(joinList(refreshed.missionIds));
      setCheckRunIds(joinList(refreshed.checkRunIds));
      pushToast('success', t('Release "{{title}}" refreshed from linked work.', { title: refreshed.title }));
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Refresh failed.'));
    } finally {
      setRefreshing(false);
    }
  }

  function handleDelete() {
    if (!release || !canManage) return;
    setConfirm({
      open: true,
      title: t('Delete Release'),
      message: t('Delete "{{title}}"? This removes only the release record.', { title: release.title }),
      onConfirm: async () => {
        setConfirm((current) => ({ ...current, open: false }));
        if (!(await deletion.run({ kind: 'release', action: () => deleteRelease(release.id), errorMessage: (err) => err instanceof Error ? err.message : t('Delete failed.') }))) return;
        pushToast('warning', t('Release "{{title}}" deleted.', { title: release.title }));
        navigate('/releases');
      },
    });
  }

  function renderLinkedList(items: string[], kind: 'voyage' | 'mission' | 'check') {
    if (items.length === 0) {
      return <span className="text-dim">{t('None')}</span>;
    }

    return (
      <div style={{ display: 'grid', gap: '0.35rem' }}>
        {items.map((item) => (
          <div key={`${kind}-${item}`} style={{ overflowWrap: 'anywhere', minWidth: 0 }}>
            {kind === 'voyage' && <Link to={`/voyages/${item}`}>{voyageMap.get(item) || item}</Link>}
            {kind === 'mission' && <Link to={`/missions/${item}`}>{item}</Link>}
            {kind === 'check' && <Link to={`/checks/${item}`}>{checkRunMap.get(item) || item}</Link>}
            <span className="mono text-dim" style={{ marginLeft: '0.45rem', fontSize: '0.78rem' }}>{item}</span>
          </div>
        ))}
      </div>
    );
  }

  if (loading) return <p className="text-dim">{t('Loading...')}</p>;

  return (
    <div>
      <PageHeader
        breadcrumb={[
          { label: t('Releases'), to: '/releases' },
          { label: createMode ? t('New Release') : title },
        ]}
        title={createMode ? t('Create Release') : title}
        actions={
          <>
            {!createMode && <StatusBadge status={status} />}
            {!createMode && (
              <button
                className="btn btn-sm"
                onClick={() => navigate('/deployments/new', {
                  state: {
                    prefill: {
                      vesselId: vesselId || null,
                      workflowProfileId: workflowProfileId || null,
                      releaseId: release?.id || null,
                      voyageId: release?.voyageIds[0] || null,
                      missionId: release?.missionIds[0] || null,
                      title: `${title} Deploy`,
                      sourceRef: tagName || version || null,
                      summary: summary || null,
                    },
                  },
                })}
                title={t('Open a new deployment prefilled with this release, its vessel, profile, and tag as the source ref. Nothing runs until you create it.')}
              >
                {t('Deploy')}
              </button>
            )}
            {!createMode && (
              <button
                className="btn btn-sm"
                onClick={() => navigate('/checks', {
                  state: {
                    prefill: {
                      vesselId: vesselId || null,
                      workflowProfileId: workflowProfileId || null,
                      voyageId: release?.voyageIds[0] || null,
                      missionId: release?.missionIds[0] || null,
                      label: title,
                    },
                  },
                })}
                title={t('Go to Checks with this release\'s vessel, profile, and first voyage and mission prefilled. Link the resulting check here to add evidence.')}
              >
                {t('Run Check')}
              </button>
            )}
            {!createMode && (
              <button className="btn btn-sm" onClick={() => setJsonData({ open: true, title, data: release })} title={t('Show the raw release record as returned by the API.')}>
                {t('View JSON')}
              </button>
            )}
            {!createMode && canManage && (
              <button className="btn btn-sm" disabled={refreshing} onClick={handleRefresh} title={t('Re-read the linked voyages, missions, and check runs to pick up new missions and rebuild the artifact list. Saved title, version, and notes are kept.')}>
                {refreshing ? t('Refreshing...') : t('Refresh Derived Fields')}
              </button>
            )}
            {!createMode && canManage && (
              <button className="btn btn-sm btn-danger" onClick={handleDelete} title={t('Delete this release record. Linked work, checks, and artifacts on disk are kept. Cannot be undone.')}>
                {t('Delete')}
              </button>
            )}
          </>
        }
      />

      <ErrorModal error={error} onClose={() => setError('')} />
      <JsonViewer open={jsonData.open} title={jsonData.title} data={jsonData.data} onClose={() => setJsonData({ open: false, title: '', data: null })} />
      {deletion.dialog}
      <ConfirmDialog
        open={confirm.open}
        title={confirm.title}
        message={confirm.message}
        onConfirm={confirm.onConfirm}
        onCancel={() => setConfirm((current) => ({ ...current, open: false }))}
      />

      {!canManage && (
        <div className="alert alert-error" style={{ marginBottom: '1rem' }}>
          {t('You can view releases, but only tenant administrators can create or change them.')}
        </div>
      )}

      {createMode && prefillObjectiveIds.length > 0 && (
        <div className="alert" style={{ marginBottom: '1rem', display: 'flex', justifyContent: 'space-between', gap: '0.75rem', alignItems: 'center', flexWrap: 'wrap' }}>
          <span>{t('Prefilled from {{count}} backlog item(s). Armada will link this release back to those scoped work records when you create it.', { count: prefillObjectiveIds.length })}</span>
          <button type="button" className="btn btn-sm" onClick={() => navigate(`/backlog/${prefillObjectiveIds[0]}`)} title={t('Open the backlog item this release draft was started from.')}>
            {t('Open Backlog Item')}
          </button>
        </div>
      )}

      {!createMode && release && (
        <div className="detail-grid" style={{ marginBottom: '1rem' }}>
          <div className="detail-field">
            <span className="detail-label" title={t('Release ID (rel_ prefix), used by the REST API, MCP tools, and links.')}>{t('ID')}</span>
            <span className="id-display">
              <span className="mono">{release.id}</span>
              <CopyButton text={release.id} />
            </span>
          </div>
          <div className="detail-field">
            <span className="detail-label">{t('Created')}</span>
            <span>{formatDateTime(release.createdUtc)}</span>
          </div>
          <div className="detail-field">
            <span className="detail-label">{t('Last Updated')}</span>
            <span title={formatDateTime(release.lastUpdateUtc)}>{formatRelativeTime(release.lastUpdateUtc)}</span>
          </div>
          <div className="detail-field">
            <span className="detail-label" title={t('When the release was first marked Shipped. Blank until then.')}>{t('Published')}</span>
            <span>{release.publishedUtc ? formatDateTime(release.publishedUtc) : '-'}</span>
          </div>
        </div>
      )}

      <div className="card playbook-editor-card">
        <div className="detail-grid" style={{ marginBottom: '1rem' }}>
          <label className="playbook-editor-field" title={tips.title}>
            <span>{t('Title')}</span>
            <input title={tips.title} type="text" value={title} disabled={!canManage} onChange={(event) => setTitle(event.target.value)} />
          </label>
          <label className="playbook-editor-field" title={tips.status}>
            <span>{t('Status')}</span>
            <select title={tips.status} value={status} disabled={!canManage} onChange={(event) => setStatus(event.target.value as ReleaseStatus)}>
              {RELEASE_STATUSES.map((value) => (
                <option key={value} value={value}>{value}</option>
              ))}
            </select>
          </label>
          <label className="playbook-editor-field" title={tips.vessel}>
            <span>{t('Vessel')}</span>
            <select title={tips.vessel} value={vesselId} disabled={!canManage} onChange={(event) => setVesselId(event.target.value)}>
              <option value="">{t('Resolve from linked work or select a vessel...')}</option>
              {vessels.map((vessel) => (
                <option key={vessel.id} value={vessel.id}>{vessel.name}</option>
              ))}
            </select>
          </label>
          <label className="playbook-editor-field" title={tips.workflowProfile}>
            <span>{t('Workflow Profile')}</span>
            <select title={tips.workflowProfile} value={workflowProfileId} disabled={!canManage} onChange={(event) => setWorkflowProfileId(event.target.value)}>
              <option value="">{t('Resolved default')}</option>
              {profiles.map((profile) => (
                <option key={profile.id} value={profile.id}>{profile.name}</option>
              ))}
            </select>
          </label>
        </div>

        <div className="detail-grid" style={{ marginBottom: '1rem' }}>
          <label className="playbook-editor-field" title={tips.version}>
            <span>{t('Version')}</span>
            <input title={tips.version} type="text" value={version} disabled={!canManage} onChange={(event) => setVersion(event.target.value)} placeholder="1.2.3" />
          </label>
          <label className="playbook-editor-field" title={tips.tagName}>
            <span>{t('Tag Name')}</span>
            <input title={tips.tagName} type="text" value={tagName} disabled={!canManage} onChange={(event) => setTagName(event.target.value)} placeholder="v1.2.3" />
          </label>
        </div>

        <label className="playbook-editor-field" title={tips.summary} style={{ marginBottom: '1rem' }}>
          <span>{t('Summary')}</span>
          <textarea title={tips.summary} value={summary} disabled={!canManage} onChange={(event) => setSummary(event.target.value)} rows={3} />
        </label>

        <label className="playbook-editor-field" title={tips.notes} style={{ marginBottom: '1rem' }}>
          <span>{t('Notes')}</span>
          <textarea title={tips.notes} value={notes} disabled={!canManage} onChange={(event) => setNotes(event.target.value)} rows={10} />
        </label>

        <div className="detail-grid" style={{ marginBottom: '1rem' }}>
          <label className="playbook-editor-field" title={tips.voyageIds}>
            <span>{t('Voyage IDs')}</span>
            <textarea title={tips.voyageIds} value={voyageIds} disabled={!canManage} onChange={(event) => setVoyageIds(event.target.value)} rows={4} placeholder="vyg_..." />
          </label>
          <label className="playbook-editor-field" title={tips.missionIds}>
            <span>{t('Mission IDs')}</span>
            <textarea title={tips.missionIds} value={missionIds} disabled={!canManage} onChange={(event) => setMissionIds(event.target.value)} rows={4} placeholder="msn_..." />
          </label>
          <label className="playbook-editor-field" title={tips.checkRunIds}>
            <span>{t('Check Run IDs')}</span>
            <textarea title={tips.checkRunIds} value={checkRunIds} disabled={!canManage} onChange={(event) => setCheckRunIds(event.target.value)} rows={4} placeholder="chk_..." />
          </label>
        </div>

        <div className="playbook-editor-actions">
          <button className="btn btn-primary" disabled={!canManage || saving} onClick={handleSave} title={createMode ? t('Create the release. Blank fields are generated from the linked work.') : t('Save changes to this release. Artifacts are rebuilt from the linked check runs.')}>
            {saving ? t('Saving...') : createMode ? t('Create Release') : t('Save Changes')}
          </button>
          <button className="btn" onClick={() => navigate('/releases')} title={t('Return to the release list. Unsaved changes are lost.')}>
            {t('Back')}
          </button>
        </div>
      </div>

      {!createMode && release && (
        <>
          <div className="detail-grid" style={{ marginTop: '1rem', marginBottom: '1rem' }}>
            <div className="card">
              <h3 title={t('Voyages included in this release. Edit the Voyage IDs field above to change them.')}>{t('Linked Voyages')}</h3>
              {renderLinkedList(release.voyageIds, 'voyage')}
            </div>
            <div className="card">
              <h3 title={t('Missions included in this release, listed directly or through a linked voyage.')}>{t('Linked Missions')}</h3>
              {renderLinkedList(release.missionIds, 'mission')}
            </div>
            <div className="card">
              <h3 title={t('Check runs offered as evidence that this release is ready. Their artifacts are listed under Artifacts.')}>{t('Linked Checks')}</h3>
              {renderLinkedList(release.checkRunIds, 'check')}
            </div>
          </div>

          <div className="card" style={{ marginBottom: '1rem' }}>
            <div className="detail-header" style={{ marginBottom: '0.75rem' }}>
              <h3 title={t('Backlog items that reference this release.')}>{t('Linked Backlog Items')}</h3>
              <div className="text-dim">
                {relatedObjectives.length} {t('linked backlog items')}
              </div>
            </div>

            {relatedObjectives.length === 0 ? (
              <p className="text-dim">{t('No backlog items currently reference this release.')}</p>
            ) : (
              <div style={{ display: 'grid', gap: '0.75rem' }}>
                {relatedObjectives.map((objective) => (
                  <div key={objective.id} className="card" style={{ padding: '0.85rem' }}>
                    <div style={{ display: 'flex', justifyContent: 'space-between', gap: '0.75rem', alignItems: 'center' }}>
                      <div>
                        <strong><Link to={`/backlog/${objective.id}`}>{objective.title}</Link></strong>
                        <div className="mono text-dim" style={{ fontSize: '0.78rem' }}>{objective.id}</div>
                      </div>
                      <StatusBadge status={objective.status} />
                    </div>
                    {objective.description && (
                      <div className="text-dim" style={{ marginTop: '0.35rem' }}>{objective.description}</div>
                    )}
                    <div style={{ marginTop: '0.45rem', display: 'flex', gap: '0.5rem', flexWrap: 'wrap' }}>
                      <button className="btn btn-sm" onClick={() => navigate(`/history?objectiveId=${encodeURIComponent(objective.id)}`)} title={t('Open All Activity filtered to this backlog item.')}>
                        {t('View History')}
                      </button>
                      <button className="btn btn-sm" onClick={() => navigate(`/backlog/${objective.id}`)} title={t('Open this backlog item.')}>
                        {t('Open Backlog Item')}
                      </button>
                    </div>
                  </div>
                ))}
              </div>
            )}
          </div>

          <div className="card" style={{ marginBottom: '1rem' }}>
            <div className="detail-header" style={{ marginBottom: '0.75rem' }}>
              <h3 title={t('Deployments that ship this release, with their rollout and verification status, check count, and API request count.')}>{t('Deployment Evidence')}</h3>
              <div className="text-dim">
                {relatedDeployments.length} {t('linked deployments')}
              </div>
            </div>

            {relatedDeployments.length === 0 ? (
              <p className="text-dim">{t('No deployments are linked to this release yet.')}</p>
            ) : (
              <div style={{ display: 'grid', gap: '0.75rem' }}>
                {relatedDeployments.map((deployment) => (
                  <div key={deployment.id} className="card" style={{ padding: '0.85rem' }}>
                    <div style={{ display: 'flex', justifyContent: 'space-between', gap: '0.75rem', alignItems: 'center' }}>
                      <div>
                        <strong><Link to={`/deployments/${deployment.id}`}>{deployment.title}</Link></strong>
                        <div className="mono text-dim" style={{ fontSize: '0.78rem' }}>{deployment.id}</div>
                      </div>
                      <div style={{ display: 'flex', gap: '0.5rem', alignItems: 'center', flexWrap: 'wrap' }}>
                        <StatusBadge status={deployment.status} />
                        <StatusBadge status={deployment.verificationStatus} />
                      </div>
                    </div>
                    <div className="text-dim" style={{ marginTop: '0.35rem' }}>
                      {(deployment.environmentName || t('No environment'))} • {deployment.checkRunIds.length} {t('checks')} • {deployment.requestHistorySummary?.totalCount || 0} {t('requests')}
                    </div>
                    {deployment.latestMonitoringSummary && (
                      <div style={{ marginTop: '0.45rem' }}>{deployment.latestMonitoringSummary}</div>
                    )}
                  </div>
                ))}
              </div>
            )}
          </div>

          <div className="card">
            <div className="detail-header" style={{ marginBottom: '0.75rem' }}>
              <h3 title={t('Files produced by the linked check runs, such as build outputs and test reports. Rebuilt when the release is saved or refreshed.')}>{t('Artifacts')}</h3>
              <div className="text-dim">
                {release.workflowProfileId ? (workflowProfileMap.get(release.workflowProfileId) || release.workflowProfileId) : t('Resolved default workflow profile')}
                {release.vesselId ? ` • ${vesselMap.get(release.vesselId) || release.vesselId}` : ''}
              </div>
            </div>

            {release.artifacts.length === 0 ? (
              <p className="text-dim">{t('No artifacts are currently linked to this release. Refresh the release after relevant check runs complete to rebuild derived artifact metadata.')}</p>
            ) : (
              <DataTable
                tableKey="release-detail-artifacts"
                rows={release.artifacts}
                rowKey={(artifact) => `${artifact.sourceId || 'source'}-${artifact.path}`}
                recordCount={null}
                columns={[
                  {
                    key: 'source', label: t('Source'), headerTitle: t('Record that produced the artifact, usually a check run.'), cellClassName: 'text-dim cell-nowrap',
                    render: (artifact) => (
                      <>
                        {artifact.sourceType}
                        {artifact.sourceId && (
                          <>
                            {' '}
                            <Link className="mono" to={`/checks/${artifact.sourceId}`}>{artifact.sourceId}</Link>
                          </>
                        )}
                      </>
                    ),
                  },
                  {
                    key: 'path', label: t('Path'), headerTitle: t('Artifact path, relative to the directory the check ran in.'), required: true, cellClassName: 'mono',
                    render: (artifact) => <span className="cell-one-line" style={{ ['--cell-max' as string]: '40rem' }} title={artifact.path}>{artifact.path}</span>,
                  },
                  { key: 'size', label: t('Size'), headerTitle: t('File size in bytes when the artifact was recorded.'), cellClassName: 'cell-nowrap', render: (artifact) => `${artifact.sizeBytes.toLocaleString()} ${t('bytes')}` },
                  { key: 'lastWrite', label: t('Last Write'), headerTitle: t('When the artifact file was last written.'), cellClassName: 'cell-nowrap', render: (artifact) => (artifact.lastWriteUtc ? formatDateTime(artifact.lastWriteUtc) : '-') },
                ]}
              />
            )}
          </div>

          <div className="card" style={{ marginBottom: '1rem' }}>
            <div className="detail-header" style={{ marginBottom: '0.75rem' }}>
              <h3 title={t('GitHub pull requests found from the pull request URLs on the linked missions, with their state, reviews, and checks.')}>{t('GitHub Pull Requests')}</h3>
              <div className="text-dim">
                {gitHubPullRequests.length < gitHubPullRequestTotal
                  ? t('{{shown}} of {{total}} linked pull requests', { shown: gitHubPullRequests.length, total: gitHubPullRequestTotal })
                  : `${gitHubPullRequestTotal} ${t('linked pull requests')}`}
              </div>
            </div>

            {loadingGitHubPullRequests ? (
              <p className="text-dim">{t('Loading GitHub pull-request evidence...')}</p>
            ) : gitHubPullRequests.length === 0 ? (
              <p className="text-dim">{t('No linked GitHub pull requests were found for this release yet.')}</p>
            ) : (
              <div style={{ display: 'grid', gap: '0.75rem' }}>
                {gitHubPullRequests.map((pullRequest) => (
                  <div key={`${pullRequest.repository}-${pullRequest.number}`} className="card" style={{ padding: '0.85rem' }}>
                    <div style={{ display: 'flex', justifyContent: 'space-between', gap: '0.75rem', alignItems: 'center', flexWrap: 'wrap' }}>
                      <div>
                        <strong><a href={pullRequest.url} target="_blank" rel="noreferrer">{pullRequest.title}</a></strong>
                        <div className="text-dim">{pullRequest.repository} #{pullRequest.number}</div>
                      </div>
                      <div style={{ display: 'flex', gap: '0.5rem', flexWrap: 'wrap' }}>
                        <StatusBadge status={pullRequest.state} />
                        <StatusBadge status={pullRequest.reviewStatus} />
                      </div>
                    </div>
                    <div className="detail-grid" style={{ marginTop: '0.75rem' }}>
                      <div className="detail-field" title={t('Number of GitHub status checks on the pull request.')}><span className="detail-label">{t('Checks')}</span><span>{pullRequest.checkCount}</span></div>
                      <div className="detail-field" title={t('Number of reviews submitted on the pull request.')}><span className="detail-label">{t('Reviews')}</span><span>{pullRequest.reviewCount}</span></div>
                      <div className="detail-field" title={t('Reviewers whose review is still requested.')}><span className="detail-label">{t('Reviewers')}</span><span>{pullRequest.requestedReviewers.length > 0 ? pullRequest.requestedReviewers.join(', ') : '-'}</span></div>
                      <div className="detail-field" title={t('When the pull request last changed on GitHub.')}><span className="detail-label">{t('Updated')}</span><span>{pullRequest.updatedUtc ? formatRelativeTime(pullRequest.updatedUtc) : '-'}</span></div>
                    </div>
                  </div>
                ))}
                {gitHubPullRequests.length < gitHubPullRequestTotal && (
                  <div>
                    <button type="button" className="btn btn-sm" disabled={loadingMoreGitHubPullRequests} onClick={loadMoreGitHubPullRequests} title={t('Read the next page of linked pull requests from GitHub.')}>
                      {loadingMoreGitHubPullRequests ? t('Loading...') : t('Load more')}
                    </button>
                  </div>
                )}
              </div>
            )}
          </div>
        </>
      )}
    </div>
  );
}
