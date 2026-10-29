import { useEffect, useState, useCallback, useRef } from 'react';
import { useParams, useNavigate, Link } from 'react-router-dom';
import {
  apiErrorMessage,
  getEvent,
  deleteEventsBatch,
  listCaptains,
  listVessels,
} from '../api/client';
import { listAll } from '../api/paging';
import type { ArmadaEvent, Captain, Vessel } from '../types/models';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import JsonViewer from '../components/shared/JsonViewer';
import CopyButton from '../components/shared/CopyButton';
import ErrorModal from '../components/shared/ErrorModal';
import { useLocale } from '../context/LocaleContext';
import { useNotifications } from '../context/NotificationContext';
import { entityRoute } from '../lib/routing';
import { formatEventPayload } from '../lib/eventPayload';
import { useDeleteProgress } from '../lib/useDeleteProgress';

export default function EventDetail() {
  const { t, formatDateTime, formatRelativeTime } = useLocale();
  const { pushToast } = useNotifications();
  const deletion = useDeleteProgress();
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();

  const [event, setEvent] = useState<ArmadaEvent | null>(null);
  const [captains, setCaptains] = useState<Captain[]>([]);
  const [vessels, setVessels] = useState<Vessel[]>([]);
  const [error, setError] = useState('');
  const requestGenerationRef = useRef(0);
  const loadedEventIdRef = useRef<string | null>(null);
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
    const generation = ++requestGenerationRef.current;
    const routeId = id;
    const current = () => generation === requestGenerationRef.current;
    if (loadedEventIdRef.current !== routeId) {
      setEvent(null);
      setError('');
    }
    getEvent(routeId).then((event) => {
      if (!current()) return;
      loadedEventIdRef.current = routeId;
      setEvent(event);
    }).catch(() => {
      if (!current()) return;
      setError(t('Failed to load event.'));
    });
    listAll(listCaptains).then(r => setCaptains(r.objects || [])).catch(() => {});
    listAll(listVessels).then(r => setVessels(r.objects || [])).catch(() => {});
  }, [id, t]);

  function handleDelete() {
    setConfirmAction({
      message: t('Delete {{entity}} {{name}}?', { entity: t('Event').toLowerCase(), name: id ?? '' }),
      action: async () => {
        setConfirmAction(null);
        if (!(await deletion.run({ kind: 'event', action: () => deleteEventsBatch([id!]), errorMessage: (err) => apiErrorMessage(err, t('Delete failed.')) }))) return;
        pushToast('warning', t('Event {{id}} deleted.', { id: id ?? '' }));
        navigate('/events');
      }
    });
  }

  if (error && !event) {
    return (
      <div>
        <ErrorModal error={error} onClose={() => setError('')} />
        <button className="btn-sm" onClick={() => navigate('/events')} title={t('Return to the event log.')}>&larr; {t('Back to Events')}</button>
      </div>
    );
  }

  if (!event) return <p className="text-muted">{t('Loading...')}</p>;

  const entRoute = entityRoute(event.entityType, event.entityId);

  // Format payload
  const payloadDisplay = formatEventPayload(event.payload);

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
          <button className="btn-sm" onClick={() => setJsonView({ title: t('Event: {{id}}', { id: event.id }), data: event })} title={t('Show the raw event record as the API returns it.')}>{t('View JSON')}</button>
          <button className="btn-sm btn-danger" onClick={handleDelete} title={t('Permanently delete this event from the log. You confirm first.')}>{t('Delete')}</button>
        </div>
      </div>

      {/* Detail grid */}
      <div className="card" style={{ marginBottom: 20 }}>
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(250px, 1fr))', gap: 16 }}>
          <div>
            <div className="text-muted" style={{ fontSize: 11, fontWeight: 600, textTransform: 'uppercase', marginBottom: 4 }} title={t('Unique event ID (evt_ prefix).')}>{t('ID')}</div>
            <div className="mono" style={{ display: 'flex', alignItems: 'center', gap: 4 }}>
              {event.id}
              <CopyButton text={event.id} />
            </div>
          </div>
          <div>
            <div className="text-muted" style={{ fontSize: 11, fontWeight: 600, textTransform: 'uppercase', marginBottom: 4 }} title={t('What happened, as a dotted name such as mission.completed or mission.failed.')}>{t('Event Type')}</div>
            <span className="status" title={t('Event type name. The Event Type filter on the Events list matches on it.')}>{event.eventType}</span>
          </div>
          <div>
            <div className="text-muted" style={{ fontSize: 11, fontWeight: 600, textTransform: 'uppercase', marginBottom: 4 }} title={t('Plain-language description of the event.')}>{t('Message')}</div>
            <div style={{ whiteSpace: 'pre-wrap' }}>{event.message || '-'}</div>
          </div>
          {event.entityType && (
            <div>
              <div className="text-muted" style={{ fontSize: 11, fontWeight: 600, textTransform: 'uppercase', marginBottom: 4 }} title={t('Kind of record the event is about, such as mission, captain or voyage.')}>{t('Entity Type')}</div>
              <div>{event.entityType}</div>
            </div>
          )}
          {event.entityId && (
            <div>
              <div className="text-muted" style={{ fontSize: 11, fontWeight: 600, textTransform: 'uppercase', marginBottom: 4 }} title={t('ID of the record the event is about. Click it to open that record.')}>{t('Entity ID')}</div>
              {entRoute
                ? <Link to={entRoute} className="mono">{event.entityId}</Link>
                : <span className="mono">{event.entityId}</span>}
            </div>
          )}
          {event.captainId && (
            <div>
              <div className="text-muted" style={{ fontSize: 11, fontWeight: 600, textTransform: 'uppercase', marginBottom: 4 }} title={t('Captain involved in the event.')}>{t('Captain')}</div>
              <Link to={`/captains/${event.captainId}`}>{captainName(event.captainId)}</Link>
            </div>
          )}
          {event.missionId && (
            <div>
              <div className="text-muted" style={{ fontSize: 11, fontWeight: 600, textTransform: 'uppercase', marginBottom: 4 }} title={t('Mission involved in the event.')}>{t('Mission')}</div>
              <Link to={`/missions/${event.missionId}`} className="mono">{event.missionId}</Link>
            </div>
          )}
          {event.vesselId && (
            <div>
              <div className="text-muted" style={{ fontSize: 11, fontWeight: 600, textTransform: 'uppercase', marginBottom: 4 }} title={t('Repository involved in the event.')}>{t('Vessel')}</div>
              <Link to={`/vessels/${event.vesselId}`}>{vesselName(event.vesselId)}</Link>
            </div>
          )}
          {event.voyageId && (
            <div>
              <div className="text-muted" style={{ fontSize: 11, fontWeight: 600, textTransform: 'uppercase', marginBottom: 4 }} title={t('Voyage involved in the event.')}>{t('Voyage')}</div>
              <Link to={`/voyages/${event.voyageId}`} className="mono">{event.voyageId}</Link>
            </div>
          )}
          <div>
            <div className="text-muted" style={{ fontSize: 11, fontWeight: 600, textTransform: 'uppercase', marginBottom: 4 }} title={t('Tenant (organization) that owns this event.')}>{t('Tenant ID')}</div>
            <div className="mono">{event.tenantId || '-'}</div>
          </div>
          <div>
            <div className="text-muted" style={{ fontSize: 11, fontWeight: 600, textTransform: 'uppercase', marginBottom: 4 }} title={t('When the event happened.')}>{t('Created')}</div>
            <div>{formatRelativeTime(event.createdUtc)} <span className="text-muted">({formatDateTime(event.createdUtc)})</span></div>
          </div>
        </div>
      </div>

      {/* Payload */}
      {payloadDisplay && (
        <div className="card">
          <h3 title={t('Extra structured data recorded with the event, shown as formatted JSON.')}>{t('Payload')}</h3>
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
      {deletion.dialog}
    </div>
  );
}
