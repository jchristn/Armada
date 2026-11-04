import { useEffect, useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { usageWindowLabel } from '@dashboard/lib/usageWindowLabel';
import { ApiError, captureCaptainUsage, getUsageLatest } from '@dashboard/api/client';
import { accountAlias, captainCaptureActive, reservationEstimateFor, USAGE_KIND_HINT, USAGE_KIND_TEXT, USAGE_LEVEL_TEXT, usageLevel, viewKind, type UsageDisplayKind } from '@dashboard/lib/usage';
import type { Captain, UsageSnapshotView, UsageWindow } from '@dashboard/types/models';
import { useLiveResource } from '../../build/useLiveResource';
import { useAuth } from '../../auth/AuthContext';
import { DetailBody } from '../../components/resource/DetailParts';
import { AppText, Banner, Button, ErrorState, KeyValueRow, LoadingState, Pager, ProgressBar, Section, StatusBadge } from '../../components/ui';
import type { StatusTone } from '../../components/ui/StatusBadge';
import { useLocale } from '../../i18n/LocaleContext';
import type { Palette } from '../../theme/palette';
import { radius, spacing } from '../../theme/typography';
import { useTheme } from '../../theme/ThemeContext';

const KIND_TONE: Record<UsageDisplayKind, StatusTone> = {
  loading: 'info', error: 'error', disabled: 'skipped', empty: 'skipped', ok: 'success', stale: 'warning',
  estimated: 'info', unsupported: 'skipped', authMissing: 'warning', failed: 'error',
};

function levelTone(percent: number): { tone: StatusTone; color: keyof Palette } {
  const level = usageLevel(percent);
  if (level === 'danger') return { tone: 'error', color: 'danger' };
  if (level === 'warning') return { tone: 'warning', color: 'warning' };
  return { tone: 'success', color: 'success' };
}

/** Native account/host limits, also embedded in captain detail. Unknown observations never render as zero. */
export function UsageView({ captain }: { captain?: Captain }) {
  const { t, formatDateTime, formatRelativeTime, locale } = useLocale();
  const { colors } = useTheme();
  const formatNumber = (value: number) => new Intl.NumberFormat(locale).format(value);
  const { isAdmin, isTenantAdmin } = useAuth();
  const [page, setPage] = useState(1);
  const [busy, setBusy] = useState(false);
  const [cooldown, setCooldown] = useState(0);
  const [message, setMessage] = useState<{ error: boolean; text: string } | null>(null);
  const resource = useLiveResource(() => getUsageLatest({ captainId: captain?.id, pageNumber: page, pageSize: 25 }), [captain?.id, page], { live: ['usage.'] });
  useEffect(() => {
    if (!cooldown) return;
    const timer = setTimeout(() => setCooldown(0), Math.max(0, cooldown - Date.now()));
    return () => clearTimeout(timer);
  }, [cooldown]);
  async function capture() {
    if (!captain || busy || cooldown > Date.now()) return;
    setBusy(true); setMessage(null);
    try {
      const receipt = await captureCaptainUsage(captain.id);
      if ('nextEligibleUtc' in receipt && receipt.nextEligibleUtc) setCooldown(Date.parse(receipt.nextEligibleUtc));
      setMessage({ error: false, text: t('jobId' in receipt && receipt.jobId ? 'Capture queued' : 'Fresh') });
      await resource.reload();
    } catch (error) {
      if (error instanceof ApiError && error.status === 429) {
        const data = error.data as { nextEligibleUtc?: string; data?: { nextEligibleUtc?: string } } | undefined;
        const value = Date.parse(data?.nextEligibleUtc ?? data?.data?.nextEligibleUtc ?? '');
        setCooldown(Number.isFinite(value) ? value : Date.now() + 60000);
        setMessage({ error: false, text: t('Capture cooldown') });
      } else setMessage({ error: true, text: error instanceof Error ? error.message : t('Capture failed') });
    } finally { setBusy(false); }
  }
  const disabled = captain && !captainCaptureActive(captain);
  const admin = isAdmin || isTenantAdmin;
  const views = resource.data?.objects ?? [];

  function windowBlock(view: UsageSnapshotView, window: UsageWindow, index: number) {
    const label = usageWindowLabel(window, t);
    const known = window.usedPercent != null && Number.isFinite(window.usedPercent);
    const level = known ? levelTone(window.usedPercent as number) : null;
    const levelText = known ? t(USAGE_LEVEL_TEXT[usageLevel(window.usedPercent as number)]) : t('Unknown');
    const estimate = reservationEstimateFor(view, window);
    return (
      <View key={index} style={[styles.window, { borderTopColor: colors.border }]}>
        <View style={styles.windowHead}>
          <AppText variant="label" style={styles.flex}>{label}</AppText>
          <AppText variant="label" color={level?.color ?? 'textMuted'}>{known ? `${formatNumber(window.usedPercent as number)}%` : t('Unknown')}</AppText>
        </View>
        {known && level ? (
          <View style={styles.meter}>
            <ProgressBar percent={window.usedPercent as number} color={level.color} label={`${label}: ${formatNumber(window.usedPercent as number)}%, ${levelText}`} />
            <StatusBadge label={levelText} tone={level.tone} />
          </View>
        ) : null}
        {window.resetsUtc ? <AppText variant="caption" muted>{t('Resets')}: {formatDateTime(window.resetsUtc)}</AppText> : null}
        {window.coverageNote ? <AppText variant="caption" muted>{window.coverageNote}</AppText> : null}
        {estimate ? <AppText variant="caption" muted>{t('usageSelection.reservations.estimate', {
          active: `${formatNumber(estimate.activePercent)}%`, unsettled: `${formatNumber(estimate.unsettledPercent)}%` })}</AppText> : null}
      </View>
    );
  }

  function snapshotCard(view: UsageSnapshotView, index: number) {
    const kind = viewKind(view);
    const observed = view.observedUtc ?? view.capturedUtc;
    const account = [view.provider, accountAlias(view.accountKey)].filter(Boolean).join(' · ');
    return (
      <View key={view.id ?? index} style={styles.cardWrap} testID="usage-snapshot">
        <View style={[styles.card, { backgroundColor: colors.surface, borderColor: colors.border }]}>
          <View style={styles.cardHead}>
            <View style={styles.flex}>
              <AppText variant="label" selectable>{account || view.runtime}</AppText>
              {view.hostId ? <AppText variant="caption" muted selectable>{view.hostId}</AppText> : null}
            </View>
            <StatusBadge label={t(USAGE_KIND_TEXT[kind])} tone={KIND_TONE[kind]} />
          </View>
          <AppText variant="caption" muted style={styles.hint}>{t(USAGE_KIND_HINT[kind], { host: view.hostId ?? '-', age: formatRelativeTime(observed) })}</AppText>
          {(view.windows ?? []).map((window, i) => windowBlock(view, window, i))}
          {view.reservationUncertain ? <AppText variant="caption" muted style={styles.hint}>{t('usageSelection.reservations.uncertain')}</AppText> : null}
          {view.capabilityReason ? <AppText variant="caption" muted style={styles.hint}>{view.capabilityReason}</AppText> : null}
          <View style={[styles.facts, { borderTopColor: colors.border }]}>
            <KeyValueRow label={t('Last observed')} value={formatRelativeTime(observed)} />
            <KeyValueRow label={t('Requested interval')} value={t('{{seconds}}s', { seconds: formatNumber(view.requestedIntervalSeconds) })} />
            <KeyValueRow label={t('Effective interval')} value={t('{{seconds}}s', { seconds: formatNumber(view.effectiveIntervalSeconds) })} />
            {!captain ? <KeyValueRow label={t('Captains')} value={view.captainIds.map((id, index) => view.captainNames?.[index] || id).join(', ')} mono={!view.captainNames?.some(Boolean)} /> : null}
            {admin && view.errorCode ? <KeyValueRow label={t('Error')} value={view.errorCode} mono /> : null}
          </View>
        </View>
      </View>
    );
  }

  const body = (
    <View testID="usage-view">
      {disabled ? <Banner tone="info" title={t('Capture disabled')} message={t(USAGE_KIND_HINT.disabled)} testID="usage-disabled" /> : null}
      {resource.loading && !resource.data ? <Section title={t('Usage')}><LoadingState label={t('Loading usage...')} /></Section> : null}
      {resource.error ? <Section title={t('Usage')}><ErrorState title={t('Usage could not be loaded.')} message={resource.error} retryLabel={t('Retry')} onRetry={() => void resource.reload()} /></Section> : null}
      {resource.data && views.length === 0 ? (
        <Section title={t('Usage')}>
          <View style={styles.empty}>
            <AppText variant="label">{t('No usage observed yet')}</AppText>
            {!disabled ? <AppText variant="caption" muted>{t(USAGE_KIND_HINT.empty)}</AppText> : null}
          </View>
        </Section>
      ) : null}
      {views.length > 0 ? <AppText variant="subheading" muted accessibilityRole="header" style={styles.title}>{t('Usage')}</AppText> : null}
      {views.map(snapshotCard)}
      <View style={styles.actions}>
        {captain && admin ? <Button label={t(cooldown > 0 ? 'Capture cooldown' : 'Capture now')} icon="pulse-outline" busy={busy} disabled={!!disabled || cooldown > 0} onPress={() => void capture()} style={styles.button} /> : null}
        <Button label={t('Refresh')} icon="refresh" variant="secondary" onPress={() => void resource.reload()} style={styles.button} />
      </View>
      {message ? <AppText variant="caption" color={message.error ? 'danger' : 'textMuted'} accessibilityRole={message.error ? 'alert' : undefined} accessibilityLiveRegion="polite" style={styles.message}>{message.text}</AppText> : null}
      {!captain ? <Pager page={page} pages={resource.data?.totalPages ?? 1} onChange={setPage} testID="usage-pager" /> : null}
    </View>
  );
  return captain ? body : <DetailBody embedded refreshing={resource.refreshing} onRefresh={() => void resource.refresh()} testID="activity-usage">{body}</DetailBody>;
}

const styles = StyleSheet.create({
  flex: { flex: 1 },
  title: { marginHorizontal: spacing.lg, marginBottom: spacing.sm, textTransform: 'uppercase' },
  cardWrap: { marginBottom: spacing.lg },
  card: { borderRadius: radius.md, borderWidth: StyleSheet.hairlineWidth, overflow: 'hidden', marginHorizontal: spacing.md },
  cardHead: { flexDirection: 'row', alignItems: 'flex-start', gap: spacing.sm, paddingHorizontal: spacing.lg, paddingTop: spacing.md },
  hint: { paddingHorizontal: spacing.lg, paddingTop: spacing.xs, paddingBottom: spacing.sm },
  window: { borderTopWidth: StyleSheet.hairlineWidth, paddingHorizontal: spacing.lg, paddingVertical: spacing.md, gap: spacing.xs },
  windowHead: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm },
  meter: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm },
  facts: { borderTopWidth: StyleSheet.hairlineWidth },
  empty: { padding: spacing.lg, gap: spacing.xs },
  actions: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.sm, marginHorizontal: spacing.md, marginBottom: spacing.lg },
  button: { marginBottom: 0 },
  message: { marginHorizontal: spacing.lg, marginBottom: spacing.md },
});
