import { useEffect, useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { getInbox } from '../api/client';
import type { InboxItem, InboxSeverity } from '../types/models';
import { useLocale } from '../context/LocaleContext';
import { useNotifications } from '../context/NotificationContext';
import { notificationRoute } from '../lib/routing';
import ErrorModal from '../components/shared/ErrorModal';
import RefreshButton from '../components/shared/RefreshButton';
import PageHeader from '../components/shared/PageHeader';
import AutoRefreshSelect from '../components/shared/AutoRefreshSelect';
import { useAutoRefresh } from '../lib/useAutoRefresh';
import { inboxItemTitle } from '../lib/deploymentApprovalLabel';

/** Inbox kinds that wait on a decision from the user (approve or reject), as opposed to failures to fix. */
const APPROVAL_KINDS: ReadonlySet<string> = new Set(['review', 'deployment_approval', 'ask_proposal']);

function severityColor(severity: InboxSeverity): string {
  if (severity === 'Critical') return 'var(--red)';
  if (severity === 'Warning') return 'var(--yellow)';
  return 'var(--text-dim)';
}

export default function Inbox() {
  const navigate = useNavigate();
  const { t, formatRelativeTime, formatDateTime } = useLocale();
  const { notifications, unreadCount, markRead, markAllRead } = useNotifications();
  const [items, setItems] = useState<InboxItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  const unreadAlerts = useMemo(() => notifications.filter((n) => !n.read).slice(0, 8), [notifications]);

  async function load() {
    try {
      setLoading(true);
      const result = await getInbox();
      setItems(result || []);
      setError('');
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Failed to load inbox.'));
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => { load(); }, []);

  const { seconds: refreshSeconds, setSeconds: setRefreshSeconds } = useAutoRefresh('inbox', load);

  const approvals = useMemo(() => items.filter((i) => APPROVAL_KINDS.has(i.kind)), [items]);
  const interventions = useMemo(() => items.filter((i) => !APPROVAL_KINDS.has(i.kind)), [items]);

  function actionLabel(item: InboxItem): string {
    if (item.kind === 'ask_proposal') return t('Open conversation');
    if (item.kind === 'deployment_approval') return t('Open deployment');
    if (item.kind === 'review') return t('Review mission');
    return t('Open');
  }

  function renderItem(item: InboxItem, i: number) {
    return (
      <div
        key={`${item.kind}:${item.entityId ?? i}`}
        className="card clickable"
        style={{ padding: '0.75rem 1rem', borderLeft: `3px solid ${severityColor(item.severity)}`, cursor: 'pointer' }}
        onClick={() => navigate(item.href)}
      >
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: '1rem', flexWrap: 'wrap' }}>
          <div style={{ minWidth: 0, flex: '1 1 240px' }}>
            <strong>{inboxItemTitle(t, item)}</strong>
            <div className="text-dim" style={{ marginTop: '0.2rem' }}>{item.detail}</div>
          </div>
          <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
            <span
              className="badge"
              style={{ color: severityColor(item.severity), borderColor: severityColor(item.severity), whiteSpace: 'nowrap' }}
            >
              {t(item.severity)}
            </span>
            {APPROVAL_KINDS.has(item.kind) && (
              <button
                type="button"
                className="btn btn-sm"
                onClick={(e) => { e.stopPropagation(); navigate(item.href); }}
              >
                {actionLabel(item)}
              </button>
            )}
          </div>
        </div>
      </div>
    );
  }

  const counts = useMemo(() => ({
    critical: items.filter((i) => i.severity === 'Critical').length,
    warning: items.filter((i) => i.severity === 'Warning').length,
  }), [items]);

  return (
    <div>
      <PageHeader
        title={t('Needs You')}
        subtitle={t('Everything across the fleet that is waiting on a decision or intervention from you.')}
        actions={(
          <>
            <AutoRefreshSelect seconds={refreshSeconds} onChange={setRefreshSeconds} />
            <RefreshButton onRefresh={load} title={t('Refresh inbox')} />
          </>
        )}
      />

      <ErrorModal error={error} onClose={() => setError('')} />

      <div className="playbook-overview-grid">
        <div className="card playbook-overview-card">
          <span>{t('Total')}</span>
          <strong>{items.length}</strong>
        </div>
        <div className="card playbook-overview-card">
          <span>{t('Critical')}</span>
          <strong style={{ color: severityColor('Critical') }}>{counts.critical}</strong>
        </div>
        <div className="card playbook-overview-card">
          <span>{t('Warning')}</span>
          <strong style={{ color: severityColor('Warning') }}>{counts.warning}</strong>
        </div>
        <div className="card playbook-overview-card">
          <span>{t('Unread alerts')}</span>
          <strong>{unreadCount}</strong>
        </div>
      </div>

      {loading && items.length === 0 ? (
        <p className="text-dim">{t('Loading...')}</p>
      ) : items.length === 0 ? (
        <div className="playbook-empty-state">
          <strong>{t('You are all caught up.')}</strong>
          <span>{t('Nothing needs your attention right now.')}</span>
        </div>
      ) : (
        <>
          {approvals.length > 0 && (
            <section aria-label={t('Waiting for your approval')} style={{ marginBottom: '1rem' }}>
              <h2 style={{ fontSize: '1rem', margin: '0 0 0.5rem' }}>{t('Waiting for your approval')} ({approvals.length})</h2>
              <div style={{ display: 'flex', flexDirection: 'column', gap: '0.5rem' }}>
                {approvals.map(renderItem)}
              </div>
            </section>
          )}
          {interventions.length > 0 && (
            <section aria-label={t('Needs intervention')}>
              <h2 style={{ fontSize: '1rem', margin: '0 0 0.5rem' }}>{t('Needs intervention')} ({interventions.length})</h2>
              <div style={{ display: 'flex', flexDirection: 'column', gap: '0.5rem' }}>
                {interventions.map(renderItem)}
              </div>
            </section>
          )}
        </>
      )}

      {unreadAlerts.length > 0 && (
        <div style={{ marginTop: '1.5rem' }}>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '0.5rem' }}>
            <h2 style={{ fontSize: '1rem', margin: 0 }}>{t('Recent alerts')}</h2>
            <button className="btn-sm" onClick={markAllRead} title={t('Mark all notifications as read')}>
              {t('Mark all read')}
            </button>
          </div>
          <div style={{ display: 'flex', flexDirection: 'column', gap: '0.4rem' }}>
            {unreadAlerts.map((n) => {
              const route = notificationRoute(n);
              return (
                <div
                  key={n.id}
                  className={`card${route ? ' clickable' : ''}`}
                  style={{ padding: '0.6rem 0.9rem', cursor: route ? 'pointer' : 'default' }}
                  onClick={() => { markRead(n.id); if (route) navigate(route); }}
                >
                  <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: '1rem' }}>
                    <div style={{ minWidth: 0 }}>
                      <strong>{n.title}</strong>
                      <div className="text-dim" style={{ marginTop: '0.15rem', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{n.message}</div>
                    </div>
                    <span className="text-dim" style={{ whiteSpace: 'nowrap' }} title={formatDateTime(n.timestampUtc)}>
                      {formatRelativeTime(n.timestampUtc)}
                    </span>
                  </div>
                </div>
              );
            })}
          </div>
        </div>
      )}
    </div>
  );
}
