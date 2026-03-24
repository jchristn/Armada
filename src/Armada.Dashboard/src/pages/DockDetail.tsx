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

function formatTimeAbsolute(utc: string): string {
  return new Date(utc).toLocaleString();
}

function formatTimeRelative(utc: string | null): string {
  if (!utc) return '';
  const d = new Date(utc);
  const diff = Date.now() - d.getTime();
  if (diff < 60000) return 'just now';
  if (diff < 3600000) return Math.floor(diff / 60000) + 'm ago';
  if (diff < 86400000) return Math.floor(diff / 3600000) + 'h ago';
  return Math.floor(diff / 86400000) + 'd ago';
}

export default function DockDetail() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();

  const [dock, setDock] = useState<Dock | null>(null);
  const [captains, setCaptains] = useState<Captain[]>([]);
  const [vessels, setVessels] = useState<Vessel[]>([]);
  const [error, setError] = useState('');
  const [jsonView, setJsonView] = useState<{ title: string; data: unknown } | null>(null);
  const [confirmAction, setConfirmAction] = useState<{ message: string; action: () => void } | null>(null);

  const captainName = useCallback((captainId: string | null) => {
    if (!captainId) return '-';
    const c = captains.find(c => c.id === captainId);
    return c?.name || captainId;
  }, [captains]);

  const vesselName = useCallback((vesselId: string | null) => {
    if (!vesselId) return '-';
    const v = vessels.find(v => v.id === vesselId);
    return v?.name || vesselId;
  }, [vessels]);

  useEffect(() => {
    if (!id) return;
    getDock(id).then(setDock).catch(() => setError('Failed to load dock.'));
    listCaptains({ pageSize: 1000 }).then(r => setCaptains(r.objects || [])).catch(() => {});
    listVessels({ pageSize: 1000 }).then(r => setVessels(r.objects || [])).catch(() => {});
  }, [id]);

  function handleDelete() {
    setConfirmAction({
      message: `Delete dock ${id}? This will clean up the git worktree and cannot be undone.`,
      action: async () => {
        try {
          await deleteDock(id!);
          setConfirmAction(null);
          navigate('/docks');
        } catch {
          setError('Delete failed.');
          setConfirmAction(null);
        }
      }
    });
  }

  if (error && !dock) {
    return (
      <div>
        <ErrorModal error={error} onClose={() => setError('')} />
        <button className="btn-sm" onClick={() => navigate('/docks')}>&larr; Back to Docks</button>
      </div>
    );
  }

  if (!dock) return <p className="text-muted">Loading...</p>;

  return (
    <div>
      {/* Breadcrumb */}
      <div style={{ marginBottom: 16, fontSize: 13 }}>
        <Link to="/docks">Docks</Link>
        <span className="text-muted"> / </span>
        <span className="mono">{dock.id}</span>
      </div>

      <ErrorModal error={error} onClose={() => setError('')} />

      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 20 }}>
        <h2>Dock Details</h2>
        <div style={{ display: 'flex', gap: 8 }}>
          <button className="btn-sm" onClick={() => setJsonView({ title: `Dock: ${dock.id}`, data: dock })}>View JSON</button>
          <button className="btn-sm btn-danger" onClick={handleDelete}>Delete</button>
        </div>
      </div>

      {/* Detail grid */}
      <div className="card">
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(250px, 1fr))', gap: 16 }}>
          <div>
            <div className="text-muted" style={{ fontSize: 11, fontWeight: 600, textTransform: 'uppercase', marginBottom: 4 }}>ID</div>
            <div className="mono" style={{ display: 'flex', alignItems: 'center', gap: 4 }}>
              {dock.id}
              <CopyButton text={dock.id} />
            </div>
          </div>
          <div>
            <div className="text-muted" style={{ fontSize: 11, fontWeight: 600, textTransform: 'uppercase', marginBottom: 4 }}>Tenant ID</div>
            <div className="mono">{dock.tenantId || '-'}</div>
          </div>
          <div>
            <div className="text-muted" style={{ fontSize: 11, fontWeight: 600, textTransform: 'uppercase', marginBottom: 4 }}>Status</div>
            <span className={`status ${dock.active ? 'status-active' : 'status-inactive'}`}>
              {dock.active ? 'Active' : 'Inactive'}
            </span>
          </div>
          <div>
            <div className="text-muted" style={{ fontSize: 11, fontWeight: 600, textTransform: 'uppercase', marginBottom: 4 }}>Vessel</div>
            {dock.vesselId
              ? <Link to={`/vessels/${dock.vesselId}`}>{vesselName(dock.vesselId)}</Link>
              : <span>-</span>}
          </div>
          <div>
            <div className="text-muted" style={{ fontSize: 11, fontWeight: 600, textTransform: 'uppercase', marginBottom: 4 }}>Captain</div>
            {dock.captainId
              ? <Link to={`/captains/${dock.captainId}`}>{captainName(dock.captainId)}</Link>
              : <span>-</span>}
          </div>
          <div>
            <div className="text-muted" style={{ fontSize: 11, fontWeight: 600, textTransform: 'uppercase', marginBottom: 4 }}>Branch Name</div>
            <div className="mono">{dock.branchName || '-'}</div>
          </div>
          <div>
            <div className="text-muted" style={{ fontSize: 11, fontWeight: 600, textTransform: 'uppercase', marginBottom: 4 }}>Worktree Path</div>
            <div className="mono" style={{ wordBreak: 'break-all' }}>{dock.worktreePath || '-'}</div>
          </div>
          <div>
            <div className="text-muted" style={{ fontSize: 11, fontWeight: 600, textTransform: 'uppercase', marginBottom: 4 }}>Created</div>
            <div>{formatTimeRelative(dock.createdUtc)} <span className="text-muted">({formatTimeAbsolute(dock.createdUtc)})</span></div>
          </div>
          <div>
            <div className="text-muted" style={{ fontSize: 11, fontWeight: 600, textTransform: 'uppercase', marginBottom: 4 }}>Last Updated</div>
            <div>{dock.lastUpdateUtc ? (<>{formatTimeRelative(dock.lastUpdateUtc)} <span className="text-muted">({formatTimeAbsolute(dock.lastUpdateUtc)})</span></>) : '-'}</div>
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
