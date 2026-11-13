import { forwardRef } from 'react';
import { Link } from 'react-router-dom';
import { useLocale } from '../../context/LocaleContext';
import type { Captain, UsageSnapshotView } from '../../types/models';
import { CAPTAIN_USAGE_SECTION_ID, accountAlias, captainCaptureActive, viewKind } from '../../lib/usage';
import { useCaptainUsage, type CaptureFeedback } from '../../lib/useUsage';
import UsageState from './UsageState';
import UsageWindowBar from './UsageWindowBar';
import UsageCaptureFields from './UsageCaptureFields';
import UsageSparkline from './UsageSparkline';

interface CaptainUsageCardProps {
  captain: Captain;
  /** TenantAdmin or GlobalAdmin: may capture now and edit capture settings. */
  canManage: boolean;
  onCaptainSaved: (captain: Captain) => void;
}

/** Provider history link for one account, with the account/host filters preserved in the URL. */
export function usageHistoryPath(view: UsageSnapshotView): string {
  const params = new URLSearchParams({ source: 'usage-overview', section: 'account-limits' });
  if (view.provider) params.set('provider', view.provider);
  if (view.accountKey) params.set('accountKey', view.accountKey);
  if (view.hostId) params.set('hostId', view.hostId);
  return `/activity?${params.toString()}`;
}

/** The captain detail Usage section: one block per account observation, capture now and capture settings. */
const CaptainUsageCard = forwardRef<HTMLElement, CaptainUsageCardProps>(function CaptainUsageCard({ captain, canManage, onCaptainSaved }, ref) {
  const { t, formatDateTime, formatRelativeTime } = useLocale();
  const usage = useCaptainUsage(captain.id);
  const active = captainCaptureActive(captain);

  const captureBlocked = !active
    ? t('Capture is off for this captain. Enable it in the capture settings first.')
    : usage.feedback.kind === 'capturing'
      ? t('A capture request is in progress.')
      : usage.feedback.kind === 'cooldown'
        ? t('Cooling down until {{time}}.', { time: usage.feedback.nextEligibleUtc ? formatDateTime(usage.feedback.nextEligibleUtc) : t('the next eligible time') })
        : '';

  return (
    <section id={CAPTAIN_USAGE_SECTION_ID} ref={ref} tabIndex={-1} className="usage-card card" aria-labelledby={`${CAPTAIN_USAGE_SECTION_ID}-title`}>
      <div className="usage-card-head">
        <h3 id={`${CAPTAIN_USAGE_SECTION_ID}-title`} title={t('Account limits this captain draws on, read from the provider without sending a prompt.')}>{t('Usage')}</h3>
        {canManage && (
          <button type="button" className="btn btn-sm" onClick={() => void usage.capture()} disabled={!!captureBlocked} title={captureBlocked || t('Read this captain\'s account limits now. Captains that share the account share the result.')}>
            {usage.feedback.kind === 'capturing' ? t('Capturing...') : t('Capture now')}
          </button>
        )}
      </div>
      {captureBlocked && canManage && <p className="text-dim usage-capture-reason">{captureBlocked}</p>}
      <CaptureFeedbackLine feedback={usage.feedback} />

      {usage.status === 'loading' && usage.views.length === 0 && <UsageState kind="loading" />}
      {usage.status === 'error' && <UsageState kind="error" onRetry={usage.retry} />}
      {usage.status === 'ready' && usage.views.length === 0 && <UsageState kind={active ? 'empty' : 'disabled'} />}
      {usage.views.length > 0 && !active && <UsageState kind="disabled" />}

      {usage.views.map((view) => {
        const kind = viewKind(view);
        const host = view.harborId ?? view.hostId;
        const others = (view.captainIds ?? []).filter((id) => id !== captain.id);
        const degraded = kind === 'stale' || kind === 'failed';
        return (
          <div key={view.id ?? `${view.hostId}-${view.accountKey}-${view.runtime}`} className={`usage-account usage-account-${kind}`}>
            <div className="usage-account-head">
              <span className="usage-account-title">
                <strong>{view.provider ?? t('Unknown provider')}</strong>
                <span className="mono" title={t('Pseudonymous account alias. Armada never stores the account email.')}>{accountAlias(view.accountKey)}</span>
                <span className="text-dim">{t('host {{host}}', { host: host ?? '-' })}</span>
                <span className="text-dim">{view.runtime}</span>
              </span>
              <UsageState kind={kind} compact host={host} age={view.observedUtc ? formatRelativeTime(view.observedUtc) : null} />
            </div>
            <div className="usage-account-meta text-dim">
              {view.observedUtc
                ? t('Last good observation {{age}} ({{time}})', { age: formatRelativeTime(view.observedUtc), time: formatDateTime(view.observedUtc) })
                : t('No good observation yet')}
              {' - '}
              {t('Requested every {{requested}}s, effective every {{effective}}s', { requested: view.requestedIntervalSeconds, effective: view.effectiveIntervalSeconds })}
            </div>
            {kind !== 'ok' && <UsageState kind={kind} host={host} age={view.observedUtc ? formatRelativeTime(view.observedUtc) : null} />}
            {kind === 'unsupported' && view.capabilityReason && <p className="text-dim">{view.capabilityReason}</p>}
            {view.windows.length > 0
              ? view.windows.map((window) => <UsageWindowBar key={`${window.name}-${window.model ?? ''}`} window={window} stale={degraded} />)
              : (kind === 'ok' || kind === 'stale' || kind === 'estimated') && <p className="text-dim">{t('The provider reported no limit windows for this account.')}</p>}
            {others.length > 0 && (
              <p className="usage-shared text-dim">
                {t('Shared with')}{' '}
                {others.map((id, i) => (
                  <span key={id}>{i > 0 && ', '}<Link to={`/captains/${id}`} className="mono">{id}</Link></span>
                ))}
              </p>
            )}
            <div className="usage-history-row">
              <UsageSparkline view={view} historyPath={usageHistoryPath(view)} />
              <Link to={usageHistoryPath(view)} className="usage-history-link" title={t('Open Account Limits filtered to this account and host.')}>{t('Provider history')}</Link>
            </div>
          </div>
        );
      })}
      {usage.truncated && <p className="text-dim">{t('More accounts exist than this card shows. Open the Limits tab for the full list.')}</p>}

      <UsageCaptureFields captain={captain} canEdit={canManage} onSaved={onCaptainSaved} />
    </section>
  );
});

function CaptureFeedbackLine({ feedback }: { feedback: CaptureFeedback }) {
  const { t, formatDateTime } = useLocale();
  switch (feedback.kind) {
    case 'queued':
      return <p className="usage-feedback" role="status">{feedback.receipt.joinedExisting ? t('Joined a capture already in progress.') : t('Capture queued. The card refreshes when the observation is stored.')}</p>;
    case 'observed':
      return <p className="usage-feedback" role="status">{t('Latest passive observation loaded.')}</p>;
    case 'disabled':
      return <p className="usage-feedback usage-form-error" role="alert">{t('Capture is disabled: {{reason}}', { reason: feedback.message })}</p>;
    case 'cooldown':
      return <p className="usage-feedback" role="status">{t('Captured recently. Next capture allowed at {{time}}.', { time: feedback.nextEligibleUtc ? formatDateTime(feedback.nextEligibleUtc) : t('the next eligible time') })}</p>;
    case 'offline':
      return <p className="usage-feedback usage-form-error" role="alert">{t('The captain host is offline: {{reason}}', { reason: feedback.message })}</p>;
    case 'error':
      return <p className="usage-feedback usage-form-error" role="alert">{t('Capture failed to start: {{reason}}', { reason: feedback.message })}</p>;
    default:
      return null;
  }
}

export default CaptainUsageCard;
