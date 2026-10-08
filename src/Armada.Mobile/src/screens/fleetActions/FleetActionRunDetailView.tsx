import { Stack, useRouter, type Href } from 'expo-router';
import { useCallback, useState } from 'react';
import { FlatList, Pressable, RefreshControl, StyleSheet, View } from 'react-native';
import { cancelFleetActionRun, enumerateFleetActionRunTargets, getFleetAction, getFleetActionRun } from '@dashboard/api/client';
import type { FleetActionRunTargetSummary, FleetActionTargetStatus, FleetActionUpsertRequest } from '@dashboard/types/models';
import {
  KIND_LABELS,
  TARGET_STATUSES,
  TARGET_STATUS_META,
  durationBetween,
  formatDurationMs,
  isRunActive,
  reasonLabel,
} from '@dashboard/lib/fleetActionLabels';
import { definitionFromRun } from '@dashboard/lib/fleetActionForm';
import { CodeBlock } from '../../components/ask/CodeBlock';
import { ActionRow, InfoRow, useActionRunner } from '../../build/fields';
import { usePagedList } from '../../build/usePagedList';
import { useLiveResource } from '../../build/useLiveResource';
import { useAuth } from '../../auth/AuthContext';
import { AppText, Banner, Button, ConfirmDialog, ErrorState, Icon, IconButton, LoadingState, Section, StatusBadge } from '../../components/ui';
import { Disclosure } from '../../components/ui/Disclosure';
import { useLocale } from '../../i18n/LocaleContext';
import { useNotifications } from '../../notifications/NotificationContext';
import { useTheme } from '../../theme/ThemeContext';
import { MIN_TOUCH, spacing } from '../../theme/typography';
import { JsonSheet, RunProgressBar, RunStatusBadge, TargetStatusBadge } from './common';
import { RunActionSheet } from './RunActionSheet';
import { StatusFilterSheet } from './StatusFilterSheet';
import { TargetDetailSheet } from './TargetDetailSheet';
import { RUN_REFRESH_MS, usePolling } from './usePolling';

export interface FleetActionRunDetailViewProps {
  id: string;
  /** Beside the runs list on tablets (no navigation title). */
  embedded?: boolean;
  /** The run changed here (cancelled). */
  onChanged?: () => void;
}

interface Rerun { vesselIds: string[]; actionId: string | null; definition: FleetActionUpsertRequest | null }

/**
 * One fleet action run (the dashboard's FleetActionRunDetail): status, progress and counts, the definition snapshot,
 * and its targets with a status filter and endless scroll. While the run is active it refreshes every 5 s (paused
 * while a sheet or dialog is open). Cancel run (with the dashboard's confirmation), Re-run failed targets, and View
 * JSON. A Command target opens its output; a Mission target opens its voyage.
 */
export function FleetActionRunDetailView({ id, embedded, onChanged }: FleetActionRunDetailViewProps) {
  const { t, locale, formatDateTime, formatRelativeTime } = useLocale();
  const { colors } = useTheme();
  const router = useRouter();
  const { isTenantAdmin } = useAuth();
  const { pushToast } = useNotifications();
  const { busy, run: runAction } = useActionRunner();
  const [targetStatus, setTargetStatus] = useState<FleetActionTargetStatus | ''>('');
  const [filterOpen, setFilterOpen] = useState(false);
  const [drawerTargetId, setDrawerTargetId] = useState<string | null>(null);
  const [confirmCancel, setConfirmCancel] = useState(false);
  const [showJson, setShowJson] = useState(false);
  const [rerun, setRerun] = useState<Rerun | null>(null);

  const detail = useLiveResource(() => getFleetActionRun(id), [id]);
  const targets = usePagedList(
    useCallback((pageNumber: number, pageSize: number) => enumerateFleetActionRunTargets(id, { pageNumber, pageSize, status: targetStatus }), [id, targetStatus]),
    [id, targetStatus],
  );
  const run = detail.data?.run ?? null;
  const active = isRunActive(run?.status);
  const overlayOpen = drawerTargetId !== null || confirmCancel || showJson || rerun !== null || filterOpen;
  const live = active && !overlayOpen;
  usePolling(live, () => { void detail.reload(); void targets.reload(); });

  const refreshAll = async () => { await Promise.all([detail.refresh(), targets.refresh()]); };

  async function cancel() {
    setConfirmCancel(false);
    if (!run) return;
    const updated = await runAction('cancel', () => cancelFleetActionRun(run.id));
    if (!updated) return;
    pushToast('warning', t('Run "{{name}}" cancelled.', { name: run.actionName }));
    void detail.reload();
    void targets.reload();
    onChanged?.();
  }

  async function startRerunFailed() {
    if (!run) return;
    const next = await runAction('rerun', async (): Promise<Rerun | null> => {
      const [failed, timedOut] = await Promise.all([
        enumerateFleetActionRunTargets(run.id, { pageNumber: 1, pageSize: 500, status: 'Failed' }),
        enumerateFleetActionRunTargets(run.id, { pageNumber: 1, pageSize: 500, status: 'TimedOut' }),
      ]);
      const vesselIds = Array.from(new Set([...(failed?.objects ?? []), ...(timedOut?.objects ?? [])].map((x) => x.vesselId)));
      if (vesselIds.length === 0) return null;
      let actionId: string | null = null;
      if (run.actionId) {
        try {
          const action = await getFleetAction(run.actionId);
          if (action?.active) actionId = action.id;
        } catch {
          actionId = null;
        }
      }
      return { vesselIds, actionId, definition: actionId ? null : definitionFromRun(run) };
    });
    if (next === null) pushToast('info', t('No failed targets to re-run.'));
    else if (next) setRerun(next);
  }

  function openTarget(target: FleetActionRunTargetSummary) {
    if (run?.kind === 'Mission') {
      if (target.voyageId) router.push(`/voyages/${target.voyageId}` as Href);
      return;
    }
    setDrawerTargetId(target.id);
  }

  const title = run?.actionName ?? t('Fleet action run');
  const head = <>{!embedded ? <Stack.Screen options={{ title }} /> : null}</>;

  if (detail.loading && !run) return <View style={styles.fill} testID="fleet-action-run-detail">{head}<LoadingState label={t('Loading run...')} /></View>;
  if (!run) {
    return (
      <View style={styles.fill} testID="fleet-action-run-detail">
        {head}
        <ErrorState title={t('Fleet action run')} message={detail.error || t('Run not found.')} retryLabel={t('Retry')} onRetry={() => void detail.refresh()} />
      </View>
    );
  }

  const duration = durationBetween(run.startedUtc, run.completedUtc, active);
  const refreshState = !active
    ? t('Run finished; auto-refresh stopped.')
    : live ? t('Live: refreshing every {{seconds}} s.', { seconds: RUN_REFRESH_MS / 1000 }) : t('Auto-refresh paused while a panel is open.');
  const stats: { label: string; value: string }[] = [
    { label: t('Targets'), value: run.targetCount.toLocaleString() },
    { label: t('Succeeded'), value: run.succeededCount.toLocaleString() },
    { label: t('Failed'), value: run.failedCount.toLocaleString() },
    { label: t('Skipped'), value: run.skippedCount.toLocaleString() },
    { label: t('Cancelled'), value: run.cancelledCount.toLocaleString() },
    { label: t('Concurrency'), value: run.concurrency.toLocaleString() },
    { label: t('Duration'), value: formatDurationMs(t, locale, duration) },
  ];

  const header = (
    <View style={styles.headerWrap}>
      <View style={[styles.pad, styles.headerText]}>
        <View style={styles.titleRow}>
          <AppText variant="title" accessibilityRole="header" style={styles.flex}>{run.actionName}</AppText>
          <RunStatusBadge status={run.status} />
        </View>
        <AppText variant="caption" muted selectable>
          {[t(KIND_LABELS[run.kind]), !run.actionId ? t('Ad hoc') : null, run.id].filter(Boolean).join(' \u00b7 ')}
        </AppText>
        <AppText variant="caption" muted accessibilityLiveRegion="polite" testID="fleet-action-run-refresh-state">{refreshState}</AppText>
      </View>
      {detail.error ? <Banner tone="danger" title={detail.error} /> : null}
      <ActionRow>
        {active && isTenantAdmin ? <Button label={t('Cancel run')} variant="danger" icon="stop-circle-outline" busy={busy === 'cancel'} onPress={() => setConfirmCancel(true)} testID="fleet-action-run-cancel" /> : null}
        {!active && run.failedCount > 0 && isTenantAdmin ? (
          <Button label={busy === 'rerun' ? t('Loading...') : t('Re-run failed targets')} variant="secondary" icon="refresh" busy={busy === 'rerun'} onPress={() => void startRerunFailed()} testID="fleet-action-run-rerun" />
        ) : null}
        <Button label={t('View JSON')} variant="ghost" icon="code-slash-outline" onPress={() => setShowJson(true)} testID="fleet-action-run-json" />
      </ActionRow>
      <View style={[styles.card, { backgroundColor: colors.surface, borderColor: colors.border }]}>
        <RunProgressBar run={run} />
        <View style={styles.stats}>
          {stats.map((s) => (
            <View key={s.label} style={styles.stat} accessible accessibilityLabel={`${s.label}: ${s.value}`}>
              <AppText variant="caption" muted>{s.label}</AppText>
              <AppText variant="label">{s.value}</AppText>
            </View>
          ))}
        </View>
      </View>
      <Section>
        <InfoRow label={t('Created')} value={`${formatRelativeTime(run.createdUtc)} (${formatDateTime(run.createdUtc)})`} />
        <InfoRow label={t('Started')} value={run.startedUtc ? formatRelativeTime(run.startedUtc) : '-'} />
        <InfoRow label={t('Completed')} value={run.completedUtc ? formatRelativeTime(run.completedUtc) : '-'} />
        {run.kind === 'Command' ? <InfoRow label={t('Timeout')} value={t('{{value}} s', { value: run.timeoutSeconds.toLocaleString() })} /> : null}
        {run.kind === 'Command' ? <InfoRow label={t('Clean-tree check')} value={run.requiresCleanWorkingTree ? t('On') : t('Off')} /> : null}
      </Section>
      <View style={styles.pad}>
        <Disclosure title={run.kind === 'Command' ? t('Command (snapshot)') : t('Prompt template (snapshot)')} testID="fleet-action-run-definition">
          <CodeBlock text={(run.kind === 'Command' ? run.commandText : run.promptTemplate) ?? ''} />
        </Disclosure>
      </View>
      <View style={styles.targetsHead}>
        <AppText variant="subheading" muted accessibilityRole="header" style={styles.flex}>{`${t('Targets')} (${targets.totalRecords.toLocaleString()})`}</AppText>
        {targetStatus ? <StatusBadge label={t(TARGET_STATUS_META[targetStatus].label)} tone="info" /> : null}
        <IconButton icon="filter" label={t('Filter targets by status')} badge={targetStatus ? 1 : undefined} onPress={() => setFilterOpen(true)} testID="fleet-action-run-targets-filter" />
      </View>
      {targets.error ? <Banner tone="danger" title={targets.error} /> : null}
    </View>
  );

  return (
    <View style={[styles.fill, { backgroundColor: colors.background }]} testID="fleet-action-run-detail">
      {head}
      <FlatList
        data={targets.items}
        keyExtractor={(target) => target.id}
        ListHeaderComponent={header}
        contentContainerStyle={styles.content}
        onEndReached={targets.loadMore}
        onEndReachedThreshold={0.5}
        refreshControl={<RefreshControl refreshing={detail.refreshing || targets.refreshing} onRefresh={() => void refreshAll()} tintColor={colors.primary} />}
        ListEmptyComponent={targets.loading ? <LoadingState label={t('Loading...')} /> : (
          <AppText muted style={styles.pad}>{targetStatus ? t('No targets match this status') : t('This run has no targets')}</AppText>
        )}
        renderItem={({ item }) => {
          const clickable = run.kind !== 'Mission' || Boolean(item.voyageId);
          const reason = reasonLabel(t, item.skipReason, item.failureReason);
          const facts = [
            run.kind === 'Command' && item.exitCode !== null ? t('Exit code {{code}}', { code: item.exitCode }) : null,
            formatDurationMs(t, locale, item.durationMs ?? durationBetween(item.startedUtc, item.completedUtc, item.status === 'Running')),
            item.voyageId,
            item.outputTruncated ? t('Truncated') : null,
          ].filter(Boolean).join(' \u00b7 ');
          return (
            <Pressable
              testID={`fleet-action-target-row-${item.vesselName}`}
              accessibilityRole={clickable ? 'button' : 'text'}
              accessibilityLabel={[item.vesselName, t(TARGET_STATUS_META[item.status]?.label ?? item.status), reason].filter(Boolean).join(', ')}
              accessibilityValue={facts ? { text: facts } : undefined}
              accessibilityHint={clickable ? (run.kind === 'Mission' ? t('Open the voyage') : t('View output for {{name}}', { name: item.vesselName })) : undefined}
              disabled={!clickable}
              onPress={() => openTarget(item)}
              style={({ pressed }) => [styles.target, { borderBottomColor: colors.border, backgroundColor: pressed ? colors.background : colors.surface }]}
            >
              <View style={styles.titleRow}>
                <AppText variant="label" style={styles.flex}>{item.vesselName}</AppText>
                <TargetStatusBadge status={item.status} />
                {clickable ? <Icon name="chevron-forward" size={18} color="textMuted" /> : null}
              </View>
              {reason ? <AppText variant="caption" muted>{reason}</AppText> : null}
              {facts ? <AppText variant="caption" muted>{facts}</AppText> : null}
            </Pressable>
          );
        }}
      />
      <StatusFilterSheet
        open={filterOpen}
        title={t('Filter targets by status')}
        value={targetStatus}
        options={TARGET_STATUSES.map((s) => ({ value: s, label: TARGET_STATUS_META[s].label }))}
        onChange={setTargetStatus}
        onClose={() => setFilterOpen(false)}
        testID="fleet-action-targets-status"
      />
      <TargetDetailSheet runId={run.id} targetId={drawerTargetId} onClose={() => setDrawerTargetId(null)} />
      <JsonSheet open={showJson} title={t('Fleet action run: {{name}}', { name: run.actionName })} data={run} onClose={() => setShowJson(false)} />
      <ConfirmDialog
        open={confirmCancel}
        title={t('Cancel run')}
        message={t('Cancel "{{name}}"? Pending targets are cancelled and running commands are stopped. Mission runs cancel voyages that have not finished.', { name: run.actionName })}
        confirmLabel={t('Cancel run')}
        cancelLabel={t('Keep running')}
        danger
        onConfirm={() => void cancel()}
        onCancel={() => setConfirmCancel(false)}
        testID="fleet-action-run-cancel-confirm"
      />
      <RunActionSheet
        open={rerun !== null}
        vesselIds={rerun?.vesselIds ?? []}
        initialActionId={rerun?.actionId ?? null}
        initialDefinition={rerun?.definition ?? null}
        onClose={() => setRerun(null)}
      />
    </View>
  );
}

const styles = StyleSheet.create({
  fill: { flex: 1 },
  flex: { flex: 1 },
  content: { paddingVertical: spacing.lg, paddingBottom: spacing.xxl, width: '100%', maxWidth: 820, alignSelf: 'center' },
  headerWrap: { gap: spacing.sm },
  headerText: { gap: spacing.xs },
  titleRow: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm },
  pad: { paddingHorizontal: spacing.lg },
  card: { marginHorizontal: spacing.md, padding: spacing.md, borderWidth: StyleSheet.hairlineWidth, borderRadius: 10, gap: spacing.md, marginBottom: spacing.md },
  stats: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.md },
  stat: { minWidth: 90, gap: 2 },
  targetsHead: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm, paddingLeft: spacing.lg, paddingRight: spacing.sm },
  target: { minHeight: MIN_TOUCH + 8, paddingHorizontal: spacing.lg, paddingVertical: spacing.sm, gap: 2, borderBottomWidth: StyleSheet.hairlineWidth },
});
