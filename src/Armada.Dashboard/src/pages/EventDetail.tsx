import { useEffect, useState, useCallback } from 'react';
import { useParams, useNavigate, Link } from 'react-router-dom';
import {
  getEvent,
  deleteEventsBatch,
  listCaptains,
  listVessels,
} from '../api/client';
import type { ArmadaEvent, Captain, Vessel } from '../types/models';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import JsonViewer from '../components/shared/JsonViewer';
import CopyButton from '../components/shared/CopyButton';
import ErrorModal from '../components/shared/ErrorModal';
import { useLocale } from '../context/LocaleContext';
import { useNotifications } from '../context/NotificationContext';

/** Map entity ID prefix to a route. */
function entityRoute(entityId: string | null): string | null {
  if (!entityId) return null;
  if (entityId.startsWith('flt_')) return `/fleets/${entityId}`;
  if (entityId.startsWith('vsl_')) return `/vessels/${entityId}`;
  if (entityId.startsWith('cpt_')) return `/captains/${entityId}`;
  if (entityId.startsWith('msn_')) return `/missions/${entityId}`;
  if (entityId.startsWith('vyg_')) return `/voyages/${entityId}`;
  if (entityId.startsWith('sig_')) return `/signals/${entityId}`;
  if (entityId.startsWith('evt_')) return `/events/${entityId}`;
  if (entityId.startsWith('dck_')) return `/docks/${entityId}`;
  return null;
}

export default function EventDetail() {
  const { t, formatDateTime, formatRelativeTime } = useLocale();
  const { pushToast } = useNotifications();
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();

  const [event, setEvent] = useState<ArmadaEvent | null>(null);
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
    getEvent(id).then(setEvent).catch(() => setError(t('Failed to load event.')));
    listCaptains({ pageSize: 1000 }).then(r => setCaptains(r.objects || [])).catch(() => {});
    listVessels({ pageSize: 1000 }).then(r => setVessels(r.objects || [])).catch(() => {});
  }, [id, t]);

  function handleDelete() {
    setConfirmAction({
      message: t('Delete {{entity}} {{name}}?', { entity: t('Event').toLowerCase(), name: id ?? '' }),
      action: async () => {
        try {
          await deleteEventsBatch([id!]);
          setConfirmAction(null);
          pushToast('warning', t('Event {{id}} deleted.', { id: id ?? '' }));
          navigate('/events');
        } catch {
          setError(t('Delete failed.'));
          setConfirmAction(null);
        }
      }
    });
  }

  if (error && !event) {
    return (
      <div>
        <ErrorModal error={error} onClose={() => setError('')} />
        <button className="btn-sm" onClick={() => navigate('/events')}>&larr; {t('Back to Events')}</button>
      </div>
    );
  }

  if (!event) return <p className="text-muted">{t('Loading...')}</p>;

  const entRoute = entityRoute(event.entityId);

  // Format payload
  let payloadDisplay: string | null = null;
  if (event.payload) {
    try {
      const parsed = JSON.parse(typeof event.payload === 'object' ? JSON.stringify(event.payload) : event.payload);
      payloadDisplay = JSON.stringify(parsed, null, 2);
    } catch {
      payloadDisplay = String(event.payload);
    }
  }

  return (
    <div>
      {/* Breadcrumb */}
      <div style={{ marginBottom: 16, fontSize: 13 }}>
        <Link to="/events">{t('Events')}</Link>
        <span className="text-muted"> / </span>
        <span className="mono">{event.id}</span>
      </div>

      <ErrorModal error={error} onClose={() => setError('')} />

      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 20 }}>
        <h2>{t('Event Details')}</h2>
        <div style={{ display: 'flex', gap: 8 }}>
          <button className="btn-sm" onClick={() => setJsonView({ title: t('Event: {{id}}', { id: event.id }), data: event })}>{t('View JSON')}</button>
          <button className="btn-sm btn-danger" onClick={handleDelete}>{t('Delete')}</button>
        </div>
      </div>

      {/* Detail grid */}
      <div className="card" style={{ marginBottom: 20 }}>
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(250px, 1fr))', gap: 16 }}>
          <div>
            <div className="text-muted" style={{ fontSize: 11, fontWeight: 600, textTransform: 'uppercase', marginBottom: 4 }}>{t('ID')}</div>
            <div className="mono" style={{ display: 'flex', alignItems: 'center', gap: 4 }}>
              {event.id}
              <CopyButton text={event.id} />
            </div>
          </div>
          <div>
            <div className="text-muted" style={{ fontSize: 11, fontWeight: 600, textTransform: 'uppercase', marginBottom: 4 }}>{t('Event Type')}</div>
            <span className="status">{event.eventType}</span>
          </div>
          <div>
            <div className="text-muted" style={{ fontSize: 11, fontWeight: 600, textTransform: 'uppercase', marginBottom: 4 }}>{t('Message')}</div>
            <div style={{ whiteSpace: 'pre-wrap' }}>{event.message || '-'}</div>
          </div>
          {event.entityType && (
            <div>
              <div className="text-muted" style={{ fontSize: 11, fontWeight: 600, textTransform: 'uppercase', marginBottom: 4 }}>{t('Entity Type')}</div>
              <div>{event.entityType}</div>
            </div>
          )}
          {event.entityId && (
            <div>
              <div className="text-muted" style={{ fontSize: 11, fontWeight: 600, textTransform: 'uppercase', marginBottom: 4 }}>{t('Entity ID')}</div>
              {entRoute
                ? <Link to={entRoute} className="mono">{event.entityId}</Link>
                : <span className="mono">{event.entityId}</span>}
            </div>
          )}
          {event.captainId && (
            <div>
              <div className="text-muted" style={{ fontSize: 11, fontWeight: 600, textTransform: 'uppercase', marginBottom: 4 }}>{t('Captain')}</div>
              <Link to={`/captains/${event.captainId}`}>{captainName(event.captainId)}</Link>
            </div>
          )}
          {event.missionId && (
            <div>
              <div className="text-muted" style={{ fontSize: 11, fontWeight: 600, textTransform: 'uppercase', marginBottom: 4 }}>{t('Mission')}</div>
              <Link to={`/missions/${event.missionId}`} className="mono">{event.missionId}</Link>
            </div>
          )}
          {event.vesselId && (
            <div>
              <div className="text-muted" style={{ fontSize: 11, fontWeight: 600, textTransform: 'uppercase', marginBottom: 4 }}>{t('Vessel')}</div>
              <Link to={`/vessels/${event.vesselId}`}>{vesselName(event.vesselId)}</Link>
            </div>
          )}
          {event.voyageId && (
            <div>
              <div className="text-muted" style={{ fontSize: 11, fontWeight: 600, textTransform: 'uppercase', marginBottom: 4 }}>{t('Voyage')}</div>
              <Link to={`/voyages/${event.voyageId}`} className="mono">{event.voyageId}</Link>
            </div>
          )}
          <div>
            <div className="text-muted" style={{ fontSize: 11, fontWeight: 600, textTransform: 'uppercase', marginBottom: 4 }}>{t('Tenant ID')}</div>
            <div className="mono">{event.tenantId || '-'}</div>
          </div>
          <div>
            <div className="text-muted" style={{ fontSize: 11, fontWeight: 600, textTransform: 'uppercase', marginBottom: 4 }}>{t('Created')}</div>
            <div>{formatRelativeTime(event.createdUtc)} <span className="text-muted">({formatDateTime(event.createdUtc)})</span></div>
          </div>
        </div>
      </div>

      {/* Payload */}
      {payloadDisplay && (
        <div className="card">
          <h3>{t('Payload')}</h3>
          <pre style={{
            background: '#f5f5fa',
            padding: 16,
            borderRadius: 'var(--radius)',
            overflow: 'auto',
            fontSize: 12,
            fontFamily: 'var(--mono)',
            whiteSpace: 'pre-wrap',
            wordBreak: 'break-word',
            maxHeight: '50vh',
          }}>
            {payloadDisplay}
          </pre>
        </div>
      )}

      {/* JSON Viewer */}
      <JsonViewer open={jsonView !== null} title={jsonView?.title ?? ''} data={jsonView?.data ?? null} onClose={() => setJsonView(null)} />

      {/* Confirm Dialog */}
      <ConfirmDialog open={confirmAction !== null} message={confirmAction?.message ?? ''} onConfirm={() => confirmAction?.action()} onCancel={() => setConfirmAction(null)} />
    </div>
  );
}
