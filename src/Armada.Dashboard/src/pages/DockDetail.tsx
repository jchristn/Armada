import { useEffect, useState, useCallback } from 'react';
import { useParams, useNavigate, Link } from 'react-router-dom';
import {
  getDock,
  deleteDock,
  listCaptains,
  listVessels,
} from '../api/client';
import type { Dock, Captain, Vessel } from '../types/models';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import JsonViewer from '../components/shared/JsonViewer';
import CopyButton from '../components/shared/CopyButton';
import ErrorModal from '../components/shared/ErrorModal';
import { useLocale } from '../context/LocaleContext';
import { useNotifications } from '../context/NotificationContext';

export default function DockDetail() {
  const { t, formatDateTime, formatRelativeTime } = useLocale();
  const { pushToast } = useNotifications();
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();

  const [dock, setDock] = useState<Dock | null>(null);
  const [captains, setCaptains] = useState<Captain[]>([]);
  const [vessels, setVessels] = useState<Vessel[]>([]);
  const [error, setError] = useState('');
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
    getDock(id).then(setDock).catch(() => setError(t('Failed to load dock.')));
    listCaptains({ pageSize: 1000 }).then(r => setCaptains(r.objects || [])).catch(() => {});
    listVessels({ pageSize: 1000 }).then(r => setVessels(r.objects || [])).catch(() => {});
  }, [id, t]);

  function handleDelete() {
    setConfirmAction({
      message: t('Delete dock {{name}}? This will clean up the git worktree and cannot be undone.', { name: id ?? '' }),
      action: async () => {
        try {
          await deleteDock(id!);
          setConfirmAction(null);
          pushToast('warning', t('Dock {{id}} deleted.', { id: id ?? '' }));
          navigate('/docks');
        } catch {
          setError(t('Delete failed.'));
          setConfirmAction(null);
        }
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
      <div style={{ marginBottom: 16, fontSize: 13 }}>
        <Link to="/docks">{t('Docks')}</Link>
        <span className="text-muted"> / </span>
        <span className="mono">{dock.id}</span>
      </div>

      <ErrorModal error={error} onClose={() => setError('')} />

      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 20 }}>
        <h2>{t('Dock Details')}</h2>
        <div style={{ display: 'flex', gap: 8 }}>
          <button className="btn-sm" onClick={() => setJsonView({ title: t('Dock: {{id}}', { id: dock.id }), data: dock })}>{t('View JSON')}</button>
          <button className="btn-sm btn-danger" onClick={handleDelete}>{t('Delete')}</button>
        </div>
      </div>

      {/* Detail grid */}
      <div className="card">
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(250px, 1fr))', gap: 16 }}>
          <div>
            <div className="text-muted" style={{ fontSize: 11, fontWeight: 600, textTransform: 'uppercase', marginBottom: 4 }}>{t('ID')}</div>
            <div className="mono" style={{ display: 'flex', alignItems: 'center', gap: 4 }}>
              {dock.id}
              <CopyButton text={dock.id} />
            </div>
          </div>
          <div>
            <div className="text-muted" style={{ fontSize: 11, fontWeight: 600, textTransform: 'uppercase', marginBottom: 4 }}>{t('Tenant ID')}</div>
            <div className="mono">{dock.tenantId || '-'}</div>
          </div>
          <div>
            <div className="text-muted" style={{ fontSize: 11, fontWeight: 600, textTransform: 'uppercase', marginBottom: 4 }}>{t('Status')}</div>
            <span className={`status ${dock.active ? 'status-active' : 'status-inactive'}`}>
              {dock.active ? t('Active') : t('Inactive')}
            </span>
          </div>
          <div>
            <div className="text-muted" style={{ fontSize: 11, fontWeight: 600, textTransform: 'uppercase', marginBottom: 4 }}>{t('Vessel')}</div>
            {dock.vesselId
              ? <Link to={`/vessels/${dock.vesselId}`}>{vesselName(dock.vesselId)}</Link>
              : <span>-</span>}
          </div>
          <div>
            <div className="text-muted" style={{ fontSize: 11, fontWeight: 600, textTransform: 'uppercase', marginBottom: 4 }}>{t('Captain')}</div>
            {dock.captainId
              ? <Link to={`/captains/${dock.captainId}`}>{captainName(dock.captainId)}</Link>
              : <span>-</span>}
          </div>
          <div>
            <div className="text-muted" style={{ fontSize: 11, fontWeight: 600, textTransform: 'uppercase', marginBottom: 4 }}>{t('Branch Name')}</div>
            <div className="mono">{dock.branchName || '-'}</div>
          </div>
          <div>
            <div className="text-muted" style={{ fontSize: 11, fontWeight: 600, textTransform: 'uppercase', marginBottom: 4 }}>{t('Worktree Path')}</div>
            <div className="mono" style={{ wordBreak: 'break-all' }}>{dock.worktreePath || '-'}</div>
          </div>
          <div>
            <div className="text-muted" style={{ fontSize: 11, fontWeight: 600, textTransform: 'uppercase', marginBottom: 4 }}>{t('Created')}</div>
            <div>{formatRelativeTime(dock.createdUtc)} <span className="text-muted">({formatDateTime(dock.createdUtc)})</span></div>
          </div>
          <div>
            <div className="text-muted" style={{ fontSize: 11, fontWeight: 600, textTransform: 'uppercase', marginBottom: 4 }}>{t('Last Updated')}</div>
            <div>{dock.lastUpdateUtc ? (<>{formatRelativeTime(dock.lastUpdateUtc)} <span className="text-muted">({formatDateTime(dock.lastUpdateUtc)})</span></>) : '-'}</div>
          </div>
        </div>
      </div>

      {/* JSON Viewer */}
      <JsonViewer open={jsonView !== null} title={jsonView?.title ?? ''} data={jsonView?.data ?? null} onClose={() => setJsonView(null)} />

      {/* Confirm Dialog */}
      <ConfirmDialog open={confirmAction !== null} message={confirmAction?.message ?? ''} onConfirm={() => confirmAction?.action()} onCancel={() => setConfirmAction(null)} />
    </div>
  );
}
