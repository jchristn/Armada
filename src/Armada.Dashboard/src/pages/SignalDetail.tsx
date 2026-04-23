import { useEffect, useState, useCallback } from 'react';
import { useParams, useNavigate, Link } from 'react-router-dom';
import {
  getSignal,
  markSignalRead,
  deleteSignalsBatch,
  listCaptains,
} from '../api/client';
import type { Signal, Captain } from '../types/models';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import JsonViewer from '../components/shared/JsonViewer';
import CopyButton from '../components/shared/CopyButton';
import ErrorModal from '../components/shared/ErrorModal';
import { useLocale } from '../context/LocaleContext';
import { useNotifications } from '../context/NotificationContext';

// The API may return missionId on signals even though the base type doesn't include it
interface SignalWithMission extends Signal {
  missionId?: string | null;
}

function formatPayload(payload: string | null, emptyText: string): { isJson: boolean; formatted: string } {
  if (!payload) return { isJson: false, formatted: emptyText };
  try {
    const parsed = JSON.parse(payload);
    return { isJson: true, formatted: JSON.stringify(parsed, null, 2) };
  } catch {
    return { isJson: false, formatted: payload };
  }
}

export default function SignalDetail() {
  const { t, formatDateTime, formatRelativeTime } = useLocale();
  const { pushToast } = useNotifications();
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();

  const [signal, setSignal] = useState<SignalWithMission | null>(null);
  const [captains, setCaptains] = useState<Captain[]>([]);
  const [error, setError] = useState('');
  const [jsonView, setJsonView] = useState<{ title: string; data: unknown } | null>(null);
  const [confirmAction, setConfirmAction] = useState<{ message: string; action: () => void } | null>(null);

  const captainName = useCallback((captainId: string | null) => {
    if (!captainId) return t('Admiral');
    const c = captains.find(c => c.id === captainId);
    return c?.name || captainId;
  }, [captains, t]);

  useEffect(() => {
    if (!id) return;
    getSignal(id).then(setSignal).catch(() => setError(t('Failed to load signal.')));
    listCaptains({ pageSize: 1000 }).then(r => setCaptains(r.objects || [])).catch(() => {});
  }, [id, t]);

  async function handleMarkRead() {
    if (!id) return;
    try {
      await markSignalRead(id);
      const updated = await getSignal(id);
      setSignal(updated);
      pushToast('success', t('Signal marked as read.'));
    } catch {
      setError(t('Failed to mark signal as read.'));
    }
  }

  function handleDelete() {
    setConfirmAction({
      message: t('Delete {{entity}} {{name}}?', { entity: t('Signal').toLowerCase(), name: id ?? '' }),
      action: async () => {
        try {
          await deleteSignalsBatch([id!]);
          setConfirmAction(null);
          pushToast('warning', t('Signal {{id}} deleted.', { id: id ?? '' }));
          navigate('/signals');
        } catch {
          setError(t('Delete failed.'));
          setConfirmAction(null);
        }
      }
    });
  }

  if (error && !signal) {
    return (
      <div>
        <ErrorModal error={error} onClose={() => setError('')} />
        <button className="btn-sm" onClick={() => navigate('/signals')}>&larr; {t('Back to Signals')}</button>
      </div>
    );
  }

  if (!signal) return <p className="text-muted">{t('Loading...')}</p>;

  const { isJson, formatted } = formatPayload(signal.payload, t('(empty)'));

  return (
    <div>
      {/* Breadcrumb */}
      <div style={{ marginBottom: 16, fontSize: 13 }}>
        <Link to="/signals">{t('Signals')}</Link>
        <span className="text-muted"> / </span>
        <span className="mono">{signal.id}</span>
      </div>

      <ErrorModal error={error} onClose={() => setError('')} />

      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 20 }}>
        <h2>{t('Signal Details')}</h2>
        <div style={{ display: 'flex', gap: 8 }}>
          {!signal.read && <button className="btn-sm" onClick={handleMarkRead}>{t('Mark Read')}</button>}
          <button className="btn-sm" onClick={() => setJsonView({ title: t('Signal: {{id}}', { id: signal.id }), data: signal })}>{t('View JSON')}</button>
          <button className="btn-sm btn-danger" onClick={handleDelete}>{t('Delete')}</button>
        </div>
      </div>

      {/* Detail grid */}
      <div className="card" style={{ marginBottom: 20 }}>
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(250px, 1fr))', gap: 16 }}>
          <div>
            <div className="text-muted" style={{ fontSize: 11, fontWeight: 600, textTransform: 'uppercase', marginBottom: 4 }}>{t('ID')}</div>
            <div className="mono" style={{ display: 'flex', alignItems: 'center', gap: 4 }}>
              {signal.id}
              <CopyButton text={signal.id} />
            </div>
          </div>
          <div>
            <div className="text-muted" style={{ fontSize: 11, fontWeight: 600, textTransform: 'uppercase', marginBottom: 4 }}>{t('Type')}</div>
            <span className={`status status-${(signal.type || '').toLowerCase()}`}>{signal.type}</span>
          </div>
          <div>
            <div className="text-muted" style={{ fontSize: 11, fontWeight: 600, textTransform: 'uppercase', marginBottom: 4 }}>{t('Read')}</div>
            <div>{signal.read ? t('Yes') : t('No')}</div>
          </div>
          <div>
            <div className="text-muted" style={{ fontSize: 11, fontWeight: 600, textTransform: 'uppercase', marginBottom: 4 }}>{t('From')}</div>
            {signal.fromCaptainId
              ? <Link to={`/captains/${signal.fromCaptainId}`}>{captainName(signal.fromCaptainId)}</Link>
              : <span>{t('Admiral')}</span>}
          </div>
          <div>
            <div className="text-muted" style={{ fontSize: 11, fontWeight: 600, textTransform: 'uppercase', marginBottom: 4 }}>{t('To')}</div>
            {signal.toCaptainId
              ? <Link to={`/captains/${signal.toCaptainId}`}>{captainName(signal.toCaptainId)}</Link>
              : <span>{t('Admiral')}</span>}
          </div>
          {signal.missionId && (
            <div>
              <div className="text-muted" style={{ fontSize: 11, fontWeight: 600, textTransform: 'uppercase', marginBottom: 4 }}>{t('Mission')}</div>
              <Link to={`/missions/${signal.missionId}`} className="mono">{signal.missionId}</Link>
            </div>
          )}
          <div>
            <div className="text-muted" style={{ fontSize: 11, fontWeight: 600, textTransform: 'uppercase', marginBottom: 4 }}>{t('Tenant ID')}</div>
            <div className="mono">{signal.tenantId || '-'}</div>
          </div>
          <div>
            <div className="text-muted" style={{ fontSize: 11, fontWeight: 600, textTransform: 'uppercase', marginBottom: 4 }}>{t('Created')}</div>
            <div>{formatRelativeTime(signal.createdUtc)} <span className="text-muted">({formatDateTime(signal.createdUtc)})</span></div>
          </div>
        </div>
      </div>

      {/* Payload */}
      <div className="card">
        <h3>{t('Payload')}</h3>
        <pre style={{
          background: '#f5f5fa',
          padding: 16,
          borderRadius: 'var(--radius)',
          overflow: 'auto',
          fontSize: 12,
          fontFamily: isJson ? 'var(--mono)' : 'inherit',
          whiteSpace: 'pre-wrap',
          wordBreak: 'break-word',
          maxHeight: '50vh',
        }}>
          {formatted}
        </pre>
      </div>

      {/* JSON Viewer */}
      <JsonViewer open={jsonView !== null} title={jsonView?.title ?? ''} data={jsonView?.data ?? null} onClose={() => setJsonView(null)} />

      {/* Confirm Dialog */}
      <ConfirmDialog open={confirmAction !== null} message={confirmAction?.message ?? ''} onConfirm={() => confirmAction?.action()} onCancel={() => setConfirmAction(null)} />
    </div>
  );
}
