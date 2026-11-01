import { useEffect, useState } from 'react';
import { ScrollView, View } from 'react-native';
import { ApiError, captureCaptainUsage, getUsageLatest } from '@dashboard/api/client';
import { accountAlias, captainCaptureActive, USAGE_KIND_HINT, USAGE_KIND_TEXT, USAGE_LEVEL_TEXT, usageLevel, viewKind } from '@dashboard/lib/usage';
import type { Captain, UsageSnapshotView } from '@dashboard/types/models';
import { useLiveResource } from '../../build/useLiveResource';
import { useAuth } from '../../auth/AuthContext';
import { AppText, Button, ErrorState, LoadingState, Section, StatusBadge } from '../../components/ui';
import { useLocale } from '../../i18n/LocaleContext';

/** Native account/host limits, also embedded in captain detail. Unknown observations never render as zero. */
export function UsageView({ captain }: { captain?: Captain }) {
  const { t, formatDateTime, formatRelativeTime, locale } = useLocale();
  const formatNumber = (value: number) => new Intl.NumberFormat(locale).format(value);
  const { isAdmin, isTenantAdmin } = useAuth();
  const [page, setPage] = useState(1);
  const [busy, setBusy] = useState(false);
  const [cooldown, setCooldown] = useState(0);
  const [message, setMessage] = useState('');
  const resource = useLiveResource(() => getUsageLatest({ captainId: captain?.id, pageNumber: page, pageSize: 25 }), [captain?.id, page], { live: ['usage.'] });
  useEffect(() => {
    if (!cooldown) return;
    const timer = setTimeout(() => setCooldown(0), Math.max(0, cooldown - Date.now()));
    return () => clearTimeout(timer);
  }, [cooldown]);
  async function capture() {
    if (!captain || busy || cooldown > Date.now()) return;
    setBusy(true); setMessage('');
    try {
      const receipt = await captureCaptainUsage(captain.id);
      if ('nextEligibleUtc' in receipt && receipt.nextEligibleUtc) setCooldown(Date.parse(receipt.nextEligibleUtc));
      setMessage(t('jobId' in receipt && receipt.jobId ? 'Capture queued' : 'Fresh'));
      await resource.reload();
    } catch (error) {
      if (error instanceof ApiError && error.status === 429) {
        const data = error.data as { nextEligibleUtc?: string; data?: { nextEligibleUtc?: string } } | undefined;
        const value = Date.parse(data?.nextEligibleUtc ?? data?.data?.nextEligibleUtc ?? '');
        setCooldown(Number.isFinite(value) ? value : Date.now() + 60000);
        setMessage(t('Capture cooldown'));
      } else setMessage(error instanceof Error ? error.message : t('Capture failed'));
    } finally { setBusy(false); }
  }
  const disabled = captain && !captainCaptureActive(captain);
  const body = <Section title={t('Usage')}>
    {disabled ? <AppText>{t('Capture disabled')}</AppText> : null}
    {resource.loading && !resource.data ? <LoadingState label={t('Loading usage...')} /> : null}
    {resource.error ? <ErrorState title={t('Usage could not be loaded.')} message={resource.error} retryLabel={t('Retry')} onRetry={() => void resource.reload()} /> : null}
    {resource.data?.objects?.length === 0 ? <AppText>{t('No usage observed yet')}</AppText> : null}
    {(resource.data?.objects ?? []).map((view: UsageSnapshotView, index: number) => {
      const kind = viewKind(view);
      return <View key={view.id ?? index}>
        <AppText variant="label">{view.provider} · {accountAlias(view.accountKey)} · {view.hostId}</AppText>
        <AppText>{t(USAGE_KIND_TEXT[kind])}</AppText>
        <AppText>{t(USAGE_KIND_HINT[kind], { host: view.hostId ?? '-', age: formatRelativeTime(view.observedUtc ?? view.capturedUtc) })}</AppText>
        <AppText>{formatRelativeTime(view.observedUtc ?? view.capturedUtc)}</AppText>
        <AppText>{t('Requested interval')}: {formatNumber(view.requestedIntervalSeconds)} · {t('Effective interval')}: {formatNumber(view.effectiveIntervalSeconds)}</AppText>
        {view.windows?.map((window, i) => <View key={i} accessible accessibilityLabel={`${window.name}: ${window.usedPercent ?? t('Unknown')}%`}>
          <AppText>{window.name} {window.model ?? ''}: {window.usedPercent == null ? t('Unknown') : `${formatNumber(window.usedPercent)}% · ${t(USAGE_LEVEL_TEXT[usageLevel(window.usedPercent)])}`}</AppText>
          {window.usedPercent != null ? <StatusBadge label={t(USAGE_LEVEL_TEXT[usageLevel(window.usedPercent)])} tone={window.usedPercent >= 90 ? 'error' : window.usedPercent >= 75 ? 'warning' : 'success'} /> : null}
          {window.resetsUtc ? <AppText>{t('Resets')}: {formatDateTime(window.resetsUtc)}</AppText> : null}
          {window.coverageNote ? <AppText>{window.coverageNote}</AppText> : null}
        </View>)}
        <AppText>{t('Captains')}: {view.captainIds.join(', ')}</AppText>
        {view.capabilityReason ? <AppText>{view.capabilityReason}</AppText> : null}
        {(isAdmin || isTenantAdmin) && view.errorCode ? <AppText>{view.errorCode}</AppText> : null}
      </View>;
    })}
    {captain && (isAdmin || isTenantAdmin) ? <Button label={t(cooldown > Date.now() ? 'Capture cooldown' : 'Capture now')} disabled={busy || !!disabled || cooldown > Date.now()} onPress={() => void capture()} /> : null}
    {message ? <AppText accessibilityLiveRegion="polite">{message}</AppText> : null}
    <Button label={t('Refresh')} onPress={() => void resource.reload()} />
    {!captain ? <View><Button label={t('Previous')} disabled={page <= 1} onPress={() => setPage(page - 1)} /><AppText>{formatNumber(page)}</AppText><Button label={t('Next')} disabled={page >= (resource.data?.totalPages ?? 1)} onPress={() => setPage(page + 1)} /></View> : null}
  </Section>;
  return captain ? body : <ScrollView>{body}</ScrollView>;
}
