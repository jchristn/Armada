import { useEffect, useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { apiErrorMessage, getInbox, getMissionBlackBoxBrief, retryMissionLanding } from '../api/client';
import { copyToClipboard } from '../components/shared/CopyButton';
import { blackBoxAskHref, blackBoxAskPrompt, blackBoxAskState, blackBoxErrorStatus } from '../lib/blackbox';
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
import { useLiveRefresh } from '../lib/useLiveRefresh';
import { CLI_PERMISSION_EVENT_PREFIX, isPendingRequest } from '../lib/cliPermissions';
import CliPermissionDecisionControls from '../components/cliPermissions/CliPermissionDecisionControls';
import CliPermissionCountdown from '../components/cliPermissions/CliPermissionCountdown';
import { APPROVAL_KINDS, CLI_PERMISSION_KIND, INBOX_KINDS, inboxActionLabel, inboxItemKey, splitInbox } from '../lib/inboxKinds';


function severityColor(severity: InboxSeverity): string {
  if (severity === 'Critical') return 'var(--red)';
  if (severity === 'Warning') return 'var(--yellow)';
  return 'var(--text-dim)';
}

/** Unread alerts previewed under Recent alerts; the rest are in the notification panel. */
const RECENT_ALERT_LIMIT = 8;

export default function Inbox() {
  const navigate = useNavigate();
  const { t, formatRelativeTime, formatDateTime } = useLocale();
  const { notifications, unreadCount, markRead, markAllRead, pushToast } = useNotifications();
  const [items, setItems] = useState<InboxItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  // A short preview of the newest unread alerts; the heading always says how many there are in total.
  const allUnreadAlerts = useMemo(() => notifications.filter((n) => !n.read), [notifications]);
  const unreadAlerts = useMemo(() => allUnreadAlerts.slice(0, RECENT_ALERT_LIMIT), [allUnreadAlerts]);

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

  // A CLI tool request appears or is decided elsewhere: reload so the row appears or disappears promptly.
  useLiveRefresh([CLI_PERMISSION_EVENT_PREFIX], () => { void load(); });

  const { approvals, interventions } = useMemo(() => splitInbox(items), [items]);

  // A failed landing (often the host blocking it: a dirty checkout, a held lock) is fixed outside Armada and then
  // retried, so the inbox offers the retry directly.
  async function retryLanding(missionId: string) {
    try {
      await retryMissionLanding(missionId);
      pushToast('success', t('Landing succeeded! Mission status updated.'));
    } catch (err: unknown) {
      setError(apiErrorMessage(err, t('Landing failed.')));
    }
    void load();
  }

  // Failed missions have a black box entry: copy its FIX THIS brief for a coding agent, or ask Armada about it.
  async function copyFixThis(missionId: string) {
    try {
      const brief = await getMissionBlackBoxBrief(missionId, { audience: 'agent' });
      await copyToClipboard(brief);
      pushToast('success', t('FIX THIS brief copied. Paste it into your coding agent.'));
    } catch (err: unknown) {
      pushToast('error', blackBoxErrorStatus(err) === 404
        ? t('This mission has no black box entry yet.')
        : t('The brief could not be copied: {{message}}', { message: err instanceof Error ? err.message : String(err) }));
    }
  }

  function askAboutFailure(missionId: string) {
    const prompt = blackBoxAskPrompt(missionId);
    navigate(blackBoxAskHref(prompt), { state: blackBoxAskState(prompt) });
  }

  function renderItem(item: InboxItem, i: number) {
    const cli = item.kind === CLI_PERMISSION_KIND ? item.cliPermission ?? null : null;
    const decidable = !!cli && !!cli.canDecide && isPendingRequest(cli);
    return (
      <div
        key={inboxItemKey(item, i)}
        className="card clickable"
        style={{ padding: '0.75rem 1rem', borderLeft: `3px solid ${severityColor(item.severity)}`, cursor: 'pointer' }}
        onClick={() => navigate(item.href)}
        title={t('Click to open the page where you can act on this item.')}
      >
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: '1rem', flexWrap: 'wrap' }}>
          <div style={{ minWidth: 0, flex: '1 1 240px' }}>
            <strong>{inboxItemTitle(t, item)}</strong>
            {item.kind === INBOX_KINDS.landingFailed ? (
              <pre className="text-dim inbox-failure-reason" style={{ margin: '0.2rem 0 0', whiteSpace: 'pre-wrap', wordBreak: 'break-word', fontFamily: 'monospace', fontSize: '0.85em' }}>{item.detail}</pre>
            ) : (
              <div className="text-dim" style={{ marginTop: '0.2rem' }}>{item.detail}</div>
            )}
            {cli && cli.summaryText && <code className="inbox-cli-command">{cli.summaryText}</code>}
          </div>
          <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
            <span
              className="badge"
              style={{ color: severityColor(item.severity), borderColor: severityColor(item.severity), whiteSpace: 'nowrap' }}
              title={t('How urgent this is. Critical items block work now; Warning items need attention soon.')}
            >
              {t(item.severity)}
            </span>
            {(item.kind === CLI_PERMISSION_KIND || item.timeoutDecision) && (
              <CliPermissionCountdown expiresUtc={item.expiresUtc ?? cli?.expiresUtc} active={!cli || isPendingRequest(cli)} timeoutDecision={cli?.timeoutDecision ?? item.timeoutDecision ?? undefined} />
            )}
            {decidable && cli && (
              <CliPermissionDecisionControls request={cli} onDecided={() => { void load(); }} />
            )}
            {item.kind === INBOX_KINDS.landingFailed && item.entityId && (
              <button
                type="button"
                className="btn btn-sm btn-primary"
                onClick={(e) => { e.stopPropagation(); void retryLanding(item.entityId as string); }}
                title={t('Rebase the mission branch onto its target branch and land it using the vessel\'s landing mode (merge, pull request or merge queue).')}
              >
                {t('Retry Landing')}
              </button>
            )}
            {(item.kind === INBOX_KINDS.failed || item.kind === INBOX_KINDS.landingFailed) && item.entityId && (
              <>
                <button
                  type="button"
                  className="btn btn-sm inbox-copy-fix-this"
                  onClick={(e) => { e.stopPropagation(); void copyFixThis(item.entityId as string); }}
                  title={t('Copy the FIX THIS brief for the selected audience, ready to paste into a coding agent.')}
                >
                  {t('Copy FIX THIS')}
                </button>
                <button
                  type="button"
                  className="btn btn-sm inbox-ask-armada"
                  onClick={(e) => { e.stopPropagation(); askAboutFailure(item.entityId as string); }}
                  title={t('Open Ask Armada with a prompt about this failure; nothing is sent until you press Send.')}
                >
                  {t('Ask Armada')}
                </button>
              </>
            )}
            {APPROVAL_KINDS.has(item.kind) && !decidable && (
              <button
                type="button"
                className="btn btn-sm"
                onClick={(e) => { e.stopPropagation(); navigate(item.href); }}
                title={t('Open the page where you can approve or decide this item.')}
              >
                {inboxActionLabel(t, item)}
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
            <RefreshButton onRefresh={load} title={t('Reload everything waiting on you from the server now.')} />
          </>
        )}
      />

      <ErrorModal error={error} onClose={() => setError('')} />

      <div className="playbook-overview-grid">
        <div className="card playbook-overview-card">
          <span title={t('Everything waiting on a decision or intervention from you.')}>{t('Total')}</span>
          <strong>{items.length}</strong>
        </div>
        <div className="card playbook-overview-card">
          <span title={t('Items blocking work now, such as a failed landing or merge, or an overdue review.')}>{t('Critical')}</span>
          <strong style={{ color: severityColor('Critical') }}>{counts.critical}</strong>
        </div>
        <div className="card playbook-overview-card">
          <span title={t('Items that need attention soon, such as a failed mission, a stalled captain or a review waiting for you.')}>{t('Warning')}</span>
          <strong style={{ color: severityColor('Warning') }}>{counts.warning}</strong>
        </div>
        <div className="card playbook-overview-card">
          <span title={t('Dashboard notifications you have not read yet. They are kept in this browser only.')}>{t('Unread alerts')}</span>
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
              <h2 style={{ fontSize: '1rem', margin: '0 0 0.5rem' }} title={t('Requests that cannot continue until you approve or deny them, such as review gates, deployments and captain tool permissions.')}>{t('Waiting for your approval')} ({approvals.length})</h2>
              <div style={{ display: 'flex', flexDirection: 'column', gap: '0.5rem' }}>
                {approvals.map(renderItem)}
              </div>
            </section>
          )}
          {interventions.length > 0 && (
            <section aria-label={t('Needs intervention')}>
              <h2 style={{ fontSize: '1rem', margin: '0 0 0.5rem' }} title={t('Problems Armada could not resolve on its own, such as failed missions, failed landings or merges, and stalled captains.')}>{t('Needs intervention')} ({interventions.length})</h2>
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
            <h2 style={{ fontSize: '1rem', margin: 0 }}>
              {allUnreadAlerts.length > unreadAlerts.length
                ? t('Recent alerts ({{shown}} of {{total}})', { shown: unreadAlerts.length.toLocaleString(), total: allUnreadAlerts.length.toLocaleString() })
                : t('Recent alerts ({{count}})', { count: unreadAlerts.length.toLocaleString() })}
            </h2>
            <button className="btn-sm" onClick={markAllRead} title={t('Mark every dashboard notification as read in this browser. Nothing in the Needs You list above changes.')}>
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
                  title={route ? t('Click to mark this alert read and open the related page.') : t('Click to mark this alert read.')}
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
