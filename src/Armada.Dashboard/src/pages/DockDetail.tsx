import { useEffect, useState, useCallback, useRef } from 'react';
import { useParams, useNavigate, Link } from 'react-router-dom';
import {
  apiErrorMessage,
  getDock,
  deleteDock,
  listCaptains,
  listVessels,
} from '../api/client';
import { listAll } from '../api/paging';
import type { Dock, Captain, Vessel } from '../types/models';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import JsonViewer from '../components/shared/JsonViewer';
import CopyButton from '../components/shared/CopyButton';
import ErrorModal from '../components/shared/ErrorModal';
import { useLocale } from '../context/LocaleContext';
import { useNotifications } from '../context/NotificationContext';
import { parseDockGitAnchors } from '../lib/dockAnchors';
import { useDeleteProgress } from '../lib/useDeleteProgress';

export default function DockDetail() {
  const { t, formatDateTime, formatRelativeTime } = useLocale();
  const { pushToast } = useNotifications();
  const deletion = useDeleteProgress();
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();

  const [dock, setDock] = useState<Dock | null>(null);
  const [captains, setCaptains] = useState<Captain[]>([]);
  const [vessels, setVessels] = useState<Vessel[]>([]);
  const [error, setError] = useState('');
  const requestGenerationRef = useRef(0);
  const loadedDockIdRef = useRef<string | null>(null);
  const [jsonView, setJsonView] = useState<{ title: string; data: unknown } | null>(null);
  const [confirmAction, setConfirmAction] = useState<{ message: string; action: () => void } | null>(null);

  const captainName = useCallback((captainId: string | null) => {
    if (!captainId) return t('-');
    const c = captains.find(c => c.id === captainId);
    return c?.name || captainId;
  }, [captains, t]);

  const vesselName = useCallback((vesselId: string | null) => {
    if (!vesselId) return t('-');
    const v = vessels.find(v => v.id === vesselId);
    return v?.name || vesselId;
  }, [vessels, t]);

  useEffect(() => {
    if (!id) return;
    const generation = ++requestGenerationRef.current;
    const routeId = id;
    const current = () => generation === requestGenerationRef.current;
    if (loadedDockIdRef.current !== routeId) {
      setDock(null);
      setError('');
    }
    getDock(routeId).then((dock) => {
      if (!current()) return;
      loadedDockIdRef.current = routeId;
      setDock(dock);
    }).catch(() => {
      if (!current()) return;
      setError(t('Failed to load dock.'));
    });
    listAll(listCaptains).then(r => setCaptains(r.objects || [])).catch(() => {});
    listAll(listVessels).then(r => setVessels(r.objects || [])).catch(() => {});
  }, [id, t]);

  function handleDelete() {
    setConfirmAction({
      message: t('Delete dock {{name}}? This will clean up the git worktree and cannot be undone.', { name: id ?? '' }),
      action: async () => {
        setConfirmAction(null);
        if (!(await deletion.run({ kind: 'dock', action: () => deleteDock(id!), errorMessage: (err) => apiErrorMessage(err, t('Delete failed.')) }))) return;
        pushToast('warning', t('Dock {{id}} deleted.', { id: id ?? '' }));
        navigate('/docks');
      }
    });
  }

  if (error && !dock) {
    return (
      <div>
        <ErrorModal error={error} onClose={() => setError('')} />
        <button className="btn-sm" onClick={() => navigate('/docks')}>&larr; {t('Back to Docks')}</button>
      </div>
    );
  }

  if (!dock) return <p className="text-muted">{t('Loading...')}</p>;

  return (
    <div>
      {/* Breadcrumb */}
      <div className="breadcrumb" style={{ marginBottom: 16, fontSize: 13 }}>
        <Link to="/docks" title={t('Back to the list of all docks.')}>{t('Docks')}</Link>
        <span className="text-muted"> / </span>
        <span className="mono">{dock.id}</span>
      </div>

      <ErrorModal error={error} onClose={() => setError('')} />

      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 20 }}>
        <h2>{t('Dock Details')}</h2>
        <div style={{ display: 'flex', gap: 8 }}>
          <button className="btn-sm" onClick={() => setJsonView({ title: t('Dock: {{id}}', { id: dock.id }), data: dock })} title={t('Show the raw dock record as JSON.')}>{t('View JSON')}</button>
          <button className="btn-sm btn-danger" onClick={handleDelete} title={t('Remove this dock and its worktree folder after you confirm. Commits on its branch stay in the repository; uncommitted files in the worktree are lost. Refused while a captain is using it.')}>{t('Delete')}</button>
        </div>
      </div>

      {/* Detail grid */}
      <div className="card">
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(250px, 1fr))', gap: 16 }}>
          <div>
            <div className="text-muted" style={{ fontSize: 11, fontWeight: 600, textTransform: 'uppercase', marginBottom: 4 }} title={t('Dock ID (dck_...), used by the CLI, API and MCP tools.')}>{t('ID')}</div>
            <div className="mono" style={{ display: 'flex', alignItems: 'center', gap: 4 }}>
              {dock.id}
              <CopyButton text={dock.id} />
            </div>
          </div>
          <div>
            <div className="text-muted" style={{ fontSize: 11, fontWeight: 600, textTransform: 'uppercase', marginBottom: 4 }} title={t('Tenant that owns this dock.')}>{t('Tenant ID')}</div>
            <div className="mono">{dock.tenantId || '-'}</div>
          </div>
          <div>
            <div className="text-muted" style={{ fontSize: 11, fontWeight: 600, textTransform: 'uppercase', marginBottom: 4 }} title={t('Whether the dock is active and usable by a captain.')}>{t('Status')}</div>
            <span className={`status ${dock.active ? 'status-active' : 'status-inactive'}`} title={dock.active ? t('The dock is active and usable by a captain.') : t('The dock is not active. Deleting it removes its worktree folder.')}>
              {dock.active ? t('Active') : t('Inactive')}
            </span>
          </div>
          <div>
            <div className="text-muted" style={{ fontSize: 11, fontWeight: 600, textTransform: 'uppercase', marginBottom: 4 }} title={t('Repository this worktree was created from. Click to open the vessel.')}>{t('Vessel')}</div>
            {dock.vesselId
              ? <Link to={`/vessels/${dock.vesselId}`}>{vesselName(dock.vesselId)}</Link>
              : <span>-</span>}
          </div>
          <div>
            <div className="text-muted" style={{ fontSize: 11, fontWeight: 600, textTransform: 'uppercase', marginBottom: 4 }} title={t('Captain currently using this dock. A dash means it is not assigned. Click to open the captain.')}>{t('Captain')}</div>
            {dock.captainId
              ? <Link to={`/captains/${dock.captainId}`}>{captainName(dock.captainId)}</Link>
              : <span>-</span>}
          </div>
          <div>
            <div className="text-muted" style={{ fontSize: 11, fontWeight: 600, textTransform: 'uppercase', marginBottom: 4 }} title={t('Branch checked out in the worktree, usually the mission branch.')}>{t('Branch Name')}</div>
            <div className="mono">{dock.branchName || '-'}</div>
          </div>
          <div>
            <div className="text-muted" style={{ fontSize: 11, fontWeight: 600, textTransform: 'uppercase', marginBottom: 4 }} title={t('Folder holding the git worktree, on the Admiral host or on the Harbor that owns the dock.')}>{t('Worktree Path')}</div>
            <div className="mono" style={{ wordBreak: 'break-all' }}>{dock.worktreePath || '-'}</div>
          </div>
          <div>
            <div className="text-muted" style={{ fontSize: 11, fontWeight: 600, textTransform: 'uppercase', marginBottom: 4 }} title={t('When the dock was provisioned.')}>{t('Created')}</div>
            <div>{formatRelativeTime(dock.createdUtc)} <span className="text-muted">({formatDateTime(dock.createdUtc)})</span></div>
          </div>
          <div>
            <div className="text-muted" style={{ fontSize: 11, fontWeight: 600, textTransform: 'uppercase', marginBottom: 4 }} title={t('When the dock record last changed, for example when a captain took or released it.')}>{t('Last Updated')}</div>
            <div>{dock.lastUpdateUtc ? (<>{formatRelativeTime(dock.lastUpdateUtc)} <span className="text-muted">({formatDateTime(dock.lastUpdateUtc)})</span></>) : '-'}</div>
          </div>
        </div>
      </div>

      {/* Git anchors resolved at provisioning */}
      {(() => {
        const anchors = parseDockGitAnchors(dock.gitAnchorsJson);
        if (!anchors) return null;
        return (
          <div className="detail-card" style={{ marginTop: 16 }}>
            <h3 title={t('Git facts recorded when the dock was created, so a captain resuming the mission knows where the work began.')}>{t('Starting Point')}</h3>
            <div className="detail-grid">
              <div>
                <div className="text-muted" style={{ fontSize: 11, fontWeight: 600, textTransform: 'uppercase', marginBottom: 4 }} title={t('Commit the working branch was created from.')}>{t('Start Commit')}</div>
                <div className="mono">{anchors.startCommit || '-'}</div>
              </div>
              <div>
                <div className="text-muted" style={{ fontSize: 11, fontWeight: 600, textTransform: 'uppercase', marginBottom: 4 }} title={t('Branch the mission work is meant to land into, usually the vessel default branch.')}>{t('Target Branch')}</div>
                <div className="mono">{anchors.targetBranch || '-'}</div>
              </div>
              <div>
                <div className="text-muted" style={{ fontSize: 11, fontWeight: 600, textTransform: 'uppercase', marginBottom: 4 }} title={t('Branch the captain commits the mission work to.')}>{t('Working Branch')}</div>
                <div className="mono">{anchors.workingBranch || '-'}</div>
              </div>
            </div>
            {anchors.recentPathCommits && anchors.recentPathCommits.length > 0 && (
              <div style={{ marginTop: 8 }}>
                <div className="text-muted" style={{ fontSize: 11, fontWeight: 600, textTransform: 'uppercase', marginBottom: 4 }} title={t('Recent commits that touched the files the mission names, as path: hash subject.')}>{t('Recent Commits On Relevant Paths')}</div>
                <ul className="mono" style={{ margin: 0, paddingLeft: 18 }}>
                  {anchors.recentPathCommits.map((c, i) => <li key={i}>{c}</li>)}
                </ul>
              </div>
            )}
            {anchors.subjectTermsPresent && anchors.subjectTermsPresent.length > 0 && (
              <div style={{ marginTop: 8 }}>
                <div className="text-muted" style={{ fontSize: 11, fontWeight: 600, textTransform: 'uppercase', marginBottom: 4 }} title={t('Words from the mission description that already appear in the repository, hinting that some of the work may exist.')}>{t('Subject Terms Already In Tree')}</div>
                <div>{anchors.subjectTermsPresent.map((term, i) => <span key={i} className="tag" style={{ marginRight: 4 }}>{term}</span>)}</div>
              </div>
            )}
          </div>
        );
      })()}

      {/* JSON Viewer */}
      <JsonViewer open={jsonView !== null} title={jsonView?.title ?? ''} data={jsonView?.data ?? null} onClose={() => setJsonView(null)} />

      {/* Confirm Dialog */}
      <ConfirmDialog open={confirmAction !== null} message={confirmAction?.message ?? ''} onConfirm={() => confirmAction?.action()} onCancel={() => setConfirmAction(null)} />
      {deletion.dialog}
    </div>
  );
}
