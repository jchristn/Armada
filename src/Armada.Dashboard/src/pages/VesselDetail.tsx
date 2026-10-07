import { useEffect, useState, useCallback, useMemo } from 'react';
import { useParams, useNavigate, Link, useSearchParams } from 'react-router-dom';
import { listVessels, listFleets, listMissionSummaries, listPipelines, createVessel, deleteVessel, getVesselReadiness, getVesselLandingPreview } from '../api/client';
import type { Fleet, Vessel, MissionSummary, Pipeline, VesselReadinessResult, LandingPreviewResult } from '../types/models';
import ActionMenu from '../components/shared/ActionMenu';
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
import { useNotifications } from '../context/NotificationContext';
import { buildVesselDuplicatePayload } from '../lib/duplicates';
import VesselFormModal from '../components/vessels/VesselFormModal';

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
        try {
          await deleteVessel(vessel.id);
          pushToast('warning', t('Vessel "{{name}}" deleted.', { name: vessel.name }));
          navigate('/vessels');
        } catch { setError(t('Delete failed.')); }
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
            <Link to="/vessels">{t('Vessels')}</Link> <span className="breadcrumb-sep">&gt;</span> <span>{vessel.name}</span>
          </>
        }
        title={vessel.name}
        actions={
          <>
            <button type="button" className="btn btn-sm btn-primary" onClick={handleDispatch}>
              {t('Dispatch')}
            </button>
            <button type="button" className="btn btn-sm" onClick={handleManageObjectives}>
              {t('Manage Objectives')}
            </button>
            {vessel.fleetId && (
              <button type="button" className="btn btn-sm" onClick={() => navigate(`/fleets/${vessel.fleetId}`)}>
                {t('Manage Fleet')}
              </button>
            )}
            <button type="button" className="btn btn-sm" onClick={handleViewHistory}>
              {t('View History')}
            </button>
            <button type="button" className="btn btn-sm" onClick={() => navigate(`/vessels/${vessel.id}/onboarding`)}>
              {t('Onboarding')}
            </button>
            <button type="button" className="btn btn-sm" onClick={() => navigate('/checks', { state: { prefill: { vesselId: vessel.id, branchName: vessel.defaultBranch || '' } } })}>
              {t('Run Check')}
            </button>
            <button type="button" className="btn btn-sm" onClick={() => navigate(`/workspace/${vessel.id}`)}>
              {t('Open Workspace')}
            </button>
            <VesselHealthButton vesselId={vessel.id} vesselName={vessel.name} defaultBranch={vessel.defaultBranch} />
            <ActionMenu id={`vessel-${vessel.id}`} items={[
              { label: 'Dispatch', onClick: handleDispatch },
              { label: 'Manage Objectives', onClick: handleManageObjectives },
              { label: 'View History', onClick: handleViewHistory },
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
        <div className="detail-field"><span className="detail-label">{t('Agent Auto-Approve')}</span><span>{vessel.autoApprove === true ? t('On for this vessel') : vessel.autoApprove === false ? t('Off for this vessel') : t('Use captain setting')}</span></div>
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
          <DataTable
            tableKey="vessel-detail-missions"
            rows={missions}
            rowKey={(m) => m.id}
            recordCount={null}
            onRowClick={(m) => navigate(`/missions/${m.id}`)}
            columns={[
              {
                key: 'mission', label: t('Mission'), required: true, headerTitle: t('Mission name and unique identifier'),
                render: (m) => <strong className="cell-one-line" title={m.title}>{m.title}</strong>,
              },
              {
                key: 'id', label: t('ID'), required: true, cellClassName: 'mono text-dim table-id-cell',
                render: (m) => (
                  <span className="id-display">
                    <span className="id-value" title={m.id}>{m.id}</span>
                    <CopyButton text={m.id} />
                  </span>
                ),
              },
              { key: 'status', label: t('Status'), headerTitle: t('Current mission lifecycle state'), cellClassName: 'cell-nowrap', render: (m) => <StatusBadge status={m.status} /> },
              {
                key: 'captain', label: t('Captain'), headerTitle: t('AI captain assigned to this mission'), cellClassName: 'mono',
                render: (m) => <span className="cell-one-line" title={m.captainId || undefined}>{m.captainId || '-'}</span>,
              },
              {
                key: 'branch', label: t('Branch'), headerTitle: t('Git branch for this mission\'s work'), cellClassName: 'mono text-dim',
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
