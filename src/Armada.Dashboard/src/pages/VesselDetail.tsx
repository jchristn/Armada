import { useEffect, useState, useCallback, useMemo } from 'react';
import { useParams, useNavigate, Link, useSearchParams } from 'react-router-dom';
import { listVessels, listFleets, listMissionSummaries, listPipelines, createVessel, updateVessel, deleteVessel, getVesselReadiness, getVesselLandingPreview } from '../api/client';
import type { Fleet, Vessel, MissionSummary, Pipeline, VesselReadinessResult, LandingPreviewResult } from '../types/models';
import ActionMenu from '../components/shared/ActionMenu';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import JsonViewer from '../components/shared/JsonViewer';
import PageHeader from '../components/shared/PageHeader';
import StatusBadge from '../components/shared/StatusBadge';
import CopyButton from '../components/shared/CopyButton';
import ErrorModal from '../components/shared/ErrorModal';
import ReadinessPanel from '../components/shared/ReadinessPanel';
import { useLocale } from '../context/LocaleContext';
import { useNotifications } from '../context/NotificationContext';
import { buildVesselDuplicatePayload } from '../lib/duplicates';

interface VesselForm {
  name: string;
  fleetId: string;
  repoUrl: string;
  defaultBranch: string;
  localPath: string;
  workingDirectory: string;
  projectContext: string;
  styleGuide: string;
  enableModelContext: boolean;
  modelContext: string;
  gitHubTokenOverride: string;
  clearGitHubTokenOverride: boolean;
  requirePassingChecksToLand: boolean;
  protectedBranchPatterns: string;
  secretScanEnabled: boolean;
  protectedPathPatterns: string;
  privateIdentifierDenylist: string;
  releaseBranchPrefix: string;
  hotfixBranchPrefix: string;
  requirePullRequestForProtectedBranches: boolean;
  requireMergeQueueForReleaseBranches: boolean;
  defaultPipelineId: string;
}

export default function VesselDetail() {
  const { t, formatDateTime, formatRelativeTime } = useLocale();
  const { pushToast } = useNotifications();
  const { id } = useParams<{ id: string }>();
  const [searchParams, setSearchParams] = useSearchParams();
  const navigate = useNavigate();
  const [vessel, setVessel] = useState<Vessel | null>(null);
  const [fleets, setFleets] = useState<Fleet[]>([]);
  const [missions, setMissions] = useState<MissionSummary[]>([]);
  const [pipelines, setPipelines] = useState<Pipeline[]>([]);
  const [readiness, setReadiness] = useState<VesselReadinessResult | null>(null);
  const [landingPreview, setLandingPreview] = useState<LandingPreviewResult | null>(null);
  const [loadingReadiness, setLoadingReadiness] = useState(false);
  const [loadingLandingPreview, setLoadingLandingPreview] = useState(false);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  // Edit modal
  const [showForm, setShowForm] = useState(false);
  const [form, setForm] = useState<VesselForm>({ name: '', fleetId: '', repoUrl: '', defaultBranch: 'main', localPath: '', workingDirectory: '', projectContext: '', styleGuide: '', enableModelContext: true, modelContext: '', gitHubTokenOverride: '', clearGitHubTokenOverride: false, requirePassingChecksToLand: false, protectedBranchPatterns: '', secretScanEnabled: false, protectedPathPatterns: '', privateIdentifierDenylist: '', releaseBranchPrefix: 'release/', hotfixBranchPrefix: 'hotfix/', requirePullRequestForProtectedBranches: false, requireMergeQueueForReleaseBranches: false, defaultPipelineId: '' });

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
      const [vResult, fResult, mResult, pResult] = await Promise.all([
        listVessels({ pageSize: 9999 }),
        listFleets({ pageSize: 9999 }),
        listMissionSummaries({ pageSize: 1000, filters: { vesselId: id } }),
        listPipelines({ pageSize: 9999 }),
      ]);
      const found = vResult.objects.find(v => v.id === id);
      if (!found) { setError(t('Vessel not found.')); setLoading(false); return; }
      setVessel(found);
      setFleets(fResult.objects);
      setMissions(mResult.objects || []);
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

  function openEdit() {
    if (!vessel) return;
    setForm({
      name: vessel.name,
      fleetId: vessel.fleetId ?? '',
      repoUrl: vessel.repoUrl ?? '',
      defaultBranch: vessel.defaultBranch || 'main',
      localPath: vessel.localPath ?? '',
      workingDirectory: vessel.workingDirectory ?? '',
      projectContext: vessel.projectContext ?? '',
      styleGuide: vessel.styleGuide ?? '',
      enableModelContext: vessel.enableModelContext,
      modelContext: vessel.modelContext ?? '',
      gitHubTokenOverride: '',
      clearGitHubTokenOverride: false,
      requirePassingChecksToLand: vessel.requirePassingChecksToLand,
      protectedBranchPatterns: (vessel.protectedBranchPatterns || []).join('\n'),
      secretScanEnabled: vessel.secretScanEnabled ?? false,
      protectedPathPatterns: (vessel.protectedPathPatterns || []).join('\n'),
      privateIdentifierDenylist: (vessel.privateIdentifierDenylist || []).join('\n'),
      releaseBranchPrefix: vessel.releaseBranchPrefix || 'release/',
      hotfixBranchPrefix: vessel.hotfixBranchPrefix || 'hotfix/',
      requirePullRequestForProtectedBranches: vessel.requirePullRequestForProtectedBranches,
      requireMergeQueueForReleaseBranches: vessel.requireMergeQueueForReleaseBranches,
      defaultPipelineId: vessel.defaultPipelineId ?? '',
    });
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

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!vessel) return;
    try {
      const payload: Record<string, unknown> = { ...form };
      if (!payload.localPath) delete payload.localPath;
      if (!payload.workingDirectory) delete payload.workingDirectory;
      if (!payload.projectContext) delete payload.projectContext;
      if (!payload.styleGuide) delete payload.styleGuide;
      if (!payload.modelContext) delete payload.modelContext;
      if (!payload.defaultPipelineId) delete payload.defaultPipelineId;
      delete payload.clearGitHubTokenOverride;
      if (form.clearGitHubTokenOverride)
        payload.gitHubTokenOverride = '';
      else if (!form.gitHubTokenOverride.trim())
        delete payload.gitHubTokenOverride;
      else
        payload.gitHubTokenOverride = form.gitHubTokenOverride.trim();
      payload.protectedBranchPatterns = form.protectedBranchPatterns
        .split(/\r?\n/)
        .map((item) => item.trim())
        .filter((item) => item.length > 0);
      payload.protectedPathPatterns = form.protectedPathPatterns
        .split(/\r?\n/)
        .map((item) => item.trim())
        .filter((item) => item.length > 0);
      payload.privateIdentifierDenylist = form.privateIdentifierDenylist
        .split(/\r?\n/)
        .map((item) => item.trim())
        .filter((item) => item.length > 0);
      await updateVessel(vessel.id, payload);
      setShowForm(false);
      pushToast('success', t('Vessel "{{name}}" saved.', { name: form.name }));
      load();
    } catch { setError(t('Save failed.')); }
  }

  function handleDelete() {
    if (!vessel) return;
    setConfirm({
      open: true,
      title: t('Delete Vessel'),
      message: t('Delete vessel "{{name}}"? This cannot be undone.', { name: vessel.name }),
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        try {
          await deleteVessel(vessel.id);
          pushToast('warning', t('Vessel "{{name}}" deleted.', { name: vessel.name }));
          navigate('/vessels');
        } catch { setError(t('Delete failed.')); }
      },
    });
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
            <Link to="/vessels">{t('Vessels')}</Link> <span className="breadcrumb-sep">&gt;</span> <span>{vessel.name}</span>
          </>
        }
        title={vessel.name}
        actions={
          <>
            <button type="button" className="btn btn-sm" onClick={handleManageObjectives}>
              {t('Manage Objectives')}
            </button>
            {vessel.fleetId && (
              <button type="button" className="btn btn-sm" onClick={() => navigate(`/fleets/${vessel.fleetId}`)}>
                {t('Manage Fleet')}
              </button>
            )}
            <button type="button" className="btn btn-sm" onClick={() => navigate(`/vessels/${vessel.id}/onboarding`)}>
              {t('Onboarding')}
            </button>
            <button type="button" className="btn btn-sm" onClick={() => navigate('/checks', { state: { prefill: { vesselId: vessel.id, branchName: vessel.defaultBranch || '' } } })}>
              {t('Run Check')}
            </button>
            <button type="button" className="btn btn-sm" onClick={() => navigate(`/workspace/${vessel.id}`)}>
              {t('Open Workspace')}
            </button>
            <ActionMenu id={`vessel-${vessel.id}`} items={[
              { label: 'Manage Objectives', onClick: handleManageObjectives },
              { label: 'Manage Fleet', onClick: () => navigate(`/fleets/${vessel.fleetId}`), disabled: !vessel.fleetId },
              { label: 'Run Check', onClick: () => navigate('/checks', { state: { prefill: { vesselId: vessel.id, branchName: vessel.defaultBranch || '' } } }) },
              { label: 'Open Workspace', onClick: () => navigate(`/workspace/${vessel.id}`) },
              { label: 'Edit', onClick: openEdit },
              { label: 'Duplicate', onClick: () => void handleDuplicate() },
              { label: 'View JSON', onClick: () => setJsonData({ open: true, title: t('Vessel: {{name}}', { name: vessel.name }), data: vessel }) },
              { label: 'Delete', danger: true, onClick: handleDelete },
            ]} />
          </>
        }
      />

      <ErrorModal error={error} onClose={() => setError('')} />

      {/* Edit Modal */}
      {showForm && (
        <div className="modal-overlay" onClick={() => setShowForm(false)}>
          <form className="modal modal-lg" onClick={e => e.stopPropagation()} onSubmit={handleSubmit}>
            <h3>{t('Edit Vessel')}</h3>
            <label>{t('Name')}<input value={form.name} onChange={e => setForm({ ...form, name: e.target.value })} required /></label>
            <label>{t('Fleet')}
              <select value={form.fleetId} onChange={e => setForm({ ...form, fleetId: e.target.value })} required>
                <option value="">{t('Select a fleet...')}</option>
                {fleets.map(f => <option key={f.id} value={f.id}>{f.name}</option>)}
              </select>
            </label>
            <label>{t('Repository URL')}<input value={form.repoUrl} onChange={e => setForm({ ...form, repoUrl: e.target.value })} required placeholder={t('https://github.com/org/repo.git')} /></label>
            <label>{t('Default Branch')}<input value={form.defaultBranch} onChange={e => setForm({ ...form, defaultBranch: e.target.value })} /></label>
            <label>{t('Release Branch Prefix')}<input value={form.releaseBranchPrefix} onChange={e => setForm({ ...form, releaseBranchPrefix: e.target.value })} /></label>
            <label>{t('Hotfix Branch Prefix')}<input value={form.hotfixBranchPrefix} onChange={e => setForm({ ...form, hotfixBranchPrefix: e.target.value })} /></label>
            <label>{t('Local Path')}<input value={form.localPath} onChange={e => setForm({ ...form, localPath: e.target.value })} /></label>
            <label>{t('Working Directory')}<input value={form.workingDirectory} onChange={e => setForm({ ...form, workingDirectory: e.target.value })} /></label>
            <label>
              GitHub Token Override
              <input
                type="password"
                value={form.gitHubTokenOverride}
                onChange={e => setForm({ ...form, gitHubTokenOverride: e.target.value, clearGitHubTokenOverride: false })}
                placeholder={vessel.hasGitHubTokenOverride ? 'Leave blank to keep existing override' : 'Optional per-vessel GitHub token'}
                autoComplete="new-password"
              />
              <span className="text-dim" style={{ fontSize: '0.8em' }}>
                {vessel.hasGitHubTokenOverride
                  ? 'This vessel already has an override. Leave blank to keep it, enter a new token to replace it, or clear it below.'
                  : 'No vessel override is stored. Armada will use the global GitHub token if one is configured.'}
              </span>
            </label>
            {vessel.hasGitHubTokenOverride && (
              <label style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                <input
                  type="checkbox"
                  checked={form.clearGitHubTokenOverride}
                  onChange={e => setForm({ ...form, clearGitHubTokenOverride: e.target.checked, gitHubTokenOverride: e.target.checked ? '' : form.gitHubTokenOverride })}
                  style={{ width: 'auto' }}
                />
                Clear existing GitHub token override
              </label>
            )}
            <label>
              {t('Protected Branch Patterns')}
              <textarea value={form.protectedBranchPatterns} onChange={e => setForm({ ...form, protectedBranchPatterns: e.target.value })} rows={4} placeholder={t('One pattern per line, e.g. main or release/*')} />
            </label>
            <label style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
              <input type="checkbox" checked={form.secretScanEnabled} onChange={e => setForm({ ...form, secretScanEnabled: e.target.checked })} style={{ width: 'auto' }} />
              {t('Scan Mission Diffs for Secrets')}
            </label>
            <label>
              {t('Protected Path Patterns')}
              <textarea value={form.protectedPathPatterns} onChange={e => setForm({ ...form, protectedPathPatterns: e.target.value })} rows={3} placeholder={t('One glob per line, e.g. .env* or infra/**')} />
              <span className="text-dim" style={{ fontSize: '0.72rem' }}>
                {t('A mission that adds or modifies a matching path is flagged as a boundary violation.')}
              </span>
            </label>
            <label>
              {t('Private Identifier Denylist')}
              <textarea value={form.privateIdentifierDenylist} onChange={e => setForm({ ...form, privateIdentifierDenylist: e.target.value })} rows={3} placeholder={t('One value per line, e.g. an internal hostname or account ID')} />
              <span className="text-dim" style={{ fontSize: '0.72rem' }}>
                {t('Added diff lines containing any of these literals are flagged. Do not list actual secrets here.')}
              </span>
            </label>
            <label>
              {t('Project Context')}
              <textarea value={form.projectContext} onChange={e => setForm({ ...form, projectContext: e.target.value })} rows={4} />
              <span className="text-dim" style={{ fontSize: '0.8em' }}>{form.projectContext.length} {t('characters')}</span>
            </label>
            <label>
              {t('Style Guide')}
              <textarea value={form.styleGuide} onChange={e => setForm({ ...form, styleGuide: e.target.value })} rows={4} />
              <span className="text-dim" style={{ fontSize: '0.8em' }}>{form.styleGuide.length} {t('characters')}</span>
            </label>
            <label>{t('Default Pipeline')}
              <select value={form.defaultPipelineId} onChange={e => setForm({ ...form, defaultPipelineId: e.target.value })}>
                <option value="">{t('None (WorkerOnly)')}</option>
                {pipelines.map(p => (
                  <option key={p.id} value={p.id}>{p.name}</option>
                ))}
              </select>
            </label>
            <label style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
              <input type="checkbox" checked={form.enableModelContext} onChange={e => setForm({ ...form, enableModelContext: e.target.checked })} style={{ width: 'auto' }} />
              {t('Enable Model Context')}
            </label>
            <label style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
              <input type="checkbox" checked={form.requirePassingChecksToLand} onChange={e => setForm({ ...form, requirePassingChecksToLand: e.target.checked })} style={{ width: 'auto' }} />
              {t('Require Passing Checks To Land')}
            </label>
            <label style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
              <input type="checkbox" checked={form.requirePullRequestForProtectedBranches} onChange={e => setForm({ ...form, requirePullRequestForProtectedBranches: e.target.checked })} style={{ width: 'auto' }} />
              {t('Require PR For Protected Branches')}
            </label>
            <label style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
              <input type="checkbox" checked={form.requireMergeQueueForReleaseBranches} onChange={e => setForm({ ...form, requireMergeQueueForReleaseBranches: e.target.checked })} style={{ width: 'auto' }} />
              {t('Require Merge Queue For Release Branches')}
            </label>
            {form.enableModelContext && (
              <label>
                {t('Model Context')}
                <textarea value={form.modelContext} onChange={e => setForm({ ...form, modelContext: e.target.value })} rows={4} placeholder={t('Agent-accumulated context will appear here after missions run with model context enabled...')} />
                <span className="text-dim" style={{ fontSize: '0.8em' }}>{form.modelContext.length} {t('characters')}</span>
              </label>
            )}
            <div className="modal-actions">
              <button type="submit" className="btn btn-primary">{t('Save')}</button>
              <button type="button" className="btn" onClick={() => setShowForm(false)}>{t('Cancel')}</button>
            </div>
          </form>
        </div>
      )}

      <JsonViewer open={jsonData.open} title={jsonData.title} data={jsonData.data} onClose={() => setJsonData({ open: false, title: '', data: null })} />
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
            <h3>{t('Landing Preview')}</h3>
            <div className="readiness-panel-meta">
              {landingPreview?.sourceBranch ? `${landingPreview.sourceBranch} -> ${landingPreview.targetBranch}` : landingPreview?.targetBranch || t('No branch selected')}
            </div>
          </div>
          <span className={`readiness-pill ${landingPreview?.isReadyToLand ? 'ready' : 'warning'}`}>
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
              <div className="readiness-success-copy">{t('No landing blockers are currently predicted for this vessel.')}</div>
            )}
          </>
        )}
      </div>

      {/* Vessel Info */}
      <div className="detail-grid">
        <div className="detail-field">
          <span className="detail-label">{t('ID')}</span>
          <span className="id-display">
            <span className="mono">{vessel.id}</span>
            <CopyButton text={vessel.id} />
          </span>
        </div>
        <div className="detail-field"><span className="detail-label">{t('Name')}</span><span>{vessel.name}</span></div>
        <div className="detail-field">
          <span className="detail-label">{t('Fleet')}</span>
          {vessel.fleetId ? (
            <Link to={`/fleets/${vessel.fleetId}`}>{fleetMap.get(vessel.fleetId) ?? vessel.fleetId}</Link>
          ) : <span>-</span>}
        </div>
        <div className="detail-field">
          <span className="detail-label">{t('Repo URL')}</span>
          {vessel.repoUrl
            ? <a href={vessel.repoUrl} target="_blank" rel="noopener noreferrer" className="mono">{vessel.repoUrl}</a>
            : <span>-</span>}
        </div>
        <div className="detail-field"><span className="detail-label">{t('Default Branch')}</span><span>{vessel.defaultBranch || 'main'}</span></div>
        <div className="detail-field"><span className="detail-label">{t('Local Path')}</span><span className="mono" title={t('Path to the bare git repository clone used by Armada')}>{vessel.localPath || '-'}</span></div>
        <div className="detail-field"><span className="detail-label">{t('Working Directory')}</span><span className="mono" title={t('Your local checkout where completed missions are merged')}>{vessel.workingDirectory || '-'}</span></div>
        <div className="detail-field"><span className="detail-label">{t('Landing Mode')}</span><span>{vessel.landingMode || '-'}</span></div>
        <div className="detail-field"><span className="detail-label">{t('Branch Cleanup Policy')}</span><span>{vessel.branchCleanupPolicy || '-'}</span></div>
        <div className="detail-field"><span className="detail-label">{t('Release Branch Prefix')}</span><span className="mono">{vessel.releaseBranchPrefix || 'release/'}</span></div>
        <div className="detail-field"><span className="detail-label">{t('Hotfix Branch Prefix')}</span><span className="mono">{vessel.hotfixBranchPrefix || 'hotfix/'}</span></div>
        <div className="detail-field"><span className="detail-label">{t('Require Passing Checks To Land')}</span><span>{vessel.requirePassingChecksToLand ? t('Yes') : t('No')}</span></div>
        <div className="detail-field"><span className="detail-label">{t('Require PR For Protected Branches')}</span><span>{vessel.requirePullRequestForProtectedBranches ? t('Yes') : t('No')}</span></div>
        <div className="detail-field"><span className="detail-label">{t('Require Merge Queue For Release Branches')}</span><span>{vessel.requireMergeQueueForReleaseBranches ? t('Yes') : t('No')}</span></div>
        <div className="detail-field"><span className="detail-label">{t('Allow Concurrent Missions')}</span><span>{vessel.allowConcurrentMissions ? t('Yes') : t('No')}</span></div>
        <div className="detail-field"><span className="detail-label">{t('Auto-Land Gate')}</span><span>{vessel.autoLandEnabled ? t('Enabled') : t('Disabled')}</span></div>
        {vessel.autoLandEnabled && (
          <>
            <div className="detail-field"><span className="detail-label">{t('Auto-Land Max Files')}</span><span>{vessel.autoLandMaxFiles ?? 0}</span></div>
            <div className="detail-field"><span className="detail-label">{t('Auto-Land Max Lines')}</span><span>{vessel.autoLandMaxLines ?? 0}</span></div>
            <div className="detail-field"><span className="detail-label">{t('Auto-Land Allowed Paths')}</span><span className="mono">{(vessel.autoLandPathAllowGlobs && vessel.autoLandPathAllowGlobs.length) ? vessel.autoLandPathAllowGlobs.join(', ') : '-'}</span></div>
            <div className="detail-field"><span className="detail-label">{t('Auto-Land Denied Paths')}</span><span className="mono">{(vessel.autoLandPathDenyGlobs && vessel.autoLandPathDenyGlobs.length) ? vessel.autoLandPathDenyGlobs.join(', ') : '-'}</span></div>
          </>
        )}
        <div className="detail-field"><span className="detail-label">{t('Definition-of-Done Gate')}</span><span>{vessel.definitionOfDoneEnabled ? t('Enabled') : t('Disabled')}</span></div>
        {vessel.definitionOfDoneEnabled && (
          <>
            <div className="detail-field"><span className="detail-label">{t('DoD Build Command')}</span><span className="mono">{vessel.definitionOfDoneBuildCommand || '-'}</span></div>
            <div className="detail-field"><span className="detail-label">{t('DoD Test Command')}</span><span className="mono">{vessel.definitionOfDoneTestCommand || '-'}</span></div>
            <div className="detail-field"><span className="detail-label">{t('DoD Timeout (s)')}</span><span>{vessel.definitionOfDoneTimeoutSeconds ?? 1800}</span></div>
          </>
        )}
        <div className="detail-field"><span className="detail-label">GitHub Token Override</span><span>{vessel.hasGitHubTokenOverride ? 'Configured' : 'Inherited / None'}</span></div>
        <div className="detail-field">
          <span className="detail-label">{t('Default Pipeline')}</span>
          <span>{pipelines.find(p => p.id === vessel.defaultPipelineId)?.name || vessel.defaultPipelineId || <span className="text-dim">{t('None (WorkerOnly)')}</span>}</span>
        </div>
        <div className="detail-field"><span className="detail-label">{t('Active')}</span><span>{vessel.active !== false ? t('Yes') : t('No')}</span></div>
        <div className="detail-field">
          <span className="detail-label">{t('Created')}</span>
          <span title={vessel.createdUtc}>
            {formatRelativeTime(vessel.createdUtc)}
            <span className="text-dim"> ({formatDateTime(vessel.createdUtc)})</span>
          </span>
        </div>
        <div className="detail-field">
          <span className="detail-label">{t('Last Updated')}</span>
          <span title={vessel.lastUpdateUtc}>
            {formatRelativeTime(vessel.lastUpdateUtc)}
            <span className="text-dim"> ({formatDateTime(vessel.lastUpdateUtc)})</span>
          </span>
        </div>
      </div>

      {/* Project Context */}
      {vessel.projectContext && (
        <div className="detail-context-section">
          <h4>{t('Project Context')}</h4>
          <pre className="detail-context-block">{vessel.projectContext}</pre>
        </div>
      )}

      {/* Style Guide */}
      {vessel.styleGuide && (
        <div className="detail-context-section">
          <h4>{t('Style Guide')}</h4>
          <pre className="detail-context-block">{vessel.styleGuide}</pre>
        </div>
      )}

      {vessel.protectedBranchPatterns && vessel.protectedBranchPatterns.length > 0 && (
        <div className="detail-context-section">
          <h4>{t('Protected Branch Patterns')}</h4>
          <pre className="detail-context-block">{vessel.protectedBranchPatterns.join('\n')}</pre>
        </div>
      )}

      {(vessel.secretScanEnabled || (vessel.protectedPathPatterns && vessel.protectedPathPatterns.length > 0) || (vessel.privateIdentifierDenylist && vessel.privateIdentifierDenylist.length > 0)) && (
        <div className="detail-context-section">
          <h4>{t('Dock Boundary')}</h4>
          <div className="detail-field"><span className="detail-label">{t('Secret Scan')}</span><span>{vessel.secretScanEnabled ? t('Enabled') : t('Disabled')}</span></div>
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
          <h4>{t('Model Context')}</h4>
          <pre className="detail-context-block">{vessel.modelContext}</pre>
        </div>
      )}

      {/* Recent Missions */}
      <div style={{ marginTop: '1rem' }}>
        <h3>{t('Missions')}</h3>
        {missions.length > 0 ? (
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th title={t('Mission name and unique identifier')}>{t('Mission')}</th>
                  <th title={t('Current mission lifecycle state')}>{t('Status')}</th>
                  <th title={t('AI captain assigned to this mission')}>{t('Captain')}</th>
                  <th title={t('Git branch for this mission\'s work')}>{t('Branch')}</th>
                </tr>
              </thead>
              <tbody>
                {missions.map(m => (
                  <tr key={m.id} className="clickable" onClick={() => navigate(`/missions/${m.id}`)}>
                    <td>
                      <strong>{m.title}</strong>
                      <div className="text-dim id-display">
                        <span className="mono">{m.id}</span>
                        <CopyButton text={m.id} />
                      </div>
                    </td>
                    <td><StatusBadge status={m.status} /></td>
                    <td>{m.captainId || '-'}</td>
                    <td className="text-dim">{m.branchName || '-'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : (
          <p className="text-dim" style={{ marginTop: '0.5rem' }}>{t('No missions yet')}</p>
        )}
      </div>
    </div>
  );
}
