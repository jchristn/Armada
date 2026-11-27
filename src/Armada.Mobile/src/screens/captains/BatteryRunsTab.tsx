import { useState } from 'react';
import { useRouter, type Href } from 'expo-router';
import { StyleSheet, View } from 'react-native';
import { apiErrorMessage, cancelBatteryRun, listBatteryRuns } from '@dashboard/api/client';
import type { BatteryRun } from '@dashboard/types/reliability';
import { useAuth } from '../../auth/AuthContext';
import { PagedList } from '../../components/app/PagedList';
import { ConfirmDialog, ListRow, SelectField, StatusBadge } from '../../components/ui';
import type { StatusTone } from '../../components/ui/StatusBadge';
import { usePagedList } from '../../data/usePagedList';
import { useLiveRefresh } from '../../data/useLiveRefresh';
import { useLocale } from '../../i18n/LocaleContext';
import { useNotifications } from '../../notifications/NotificationContext';
import { spacing } from '../../theme/typography';

/** Battery run statuses the list can filter by (the dashboard's order). */
export const BATTERY_RUN_STATUSES = ['Queued', 'Running', 'Completed', 'Failed', 'Cancelled'];

/** Pure: true while a run can still be cancelled. */
export function isBatteryRunOpen(run: Pick<BatteryRun, 'status'>): boolean {
  return run.status === 'Queued' || run.status === 'Running';
}

/** Pure: the badge tone for a run status. */
export function batteryRunTone(status: string): StatusTone {
  switch (status) {
    case 'Completed': return 'success';
    case 'Failed': return 'failed';
    case 'Running': return 'running';
    case 'Cancelled': return 'cancelled';
    default: return 'pending';
  }
}

/**
 * The Battery Runs tab of the Captains hub (the dashboard's CaptainsHub `?tab=battery-runs`): Full captain evaluations
 * and acceptance gate runs, newest first, with task counts. A row opens its captain; global and tenant administrators
 * can cancel a queued or running run from the row's menu, which stops its open missions and releases their docks.
 */
export function BatteryRunsTab() {
  const { t, formatRelativeTime } = useLocale();
  const { isAdmin, isTenantAdmin } = useAuth();
  const canCancel = isAdmin || isTenantAdmin;
  const { pushToast } = useNotifications();
  const router = useRouter();
  const [status, setStatus] = useState('');
  const [confirmRun, setConfirmRun] = useState<BatteryRun | null>(null);
  const list = usePagedList(
    (pageNumber, pageSize) => listBatteryRuns({ status: status || undefined, pageNumber, pageSize }),
    (r: BatteryRun) => r.id,
    [status],
    25,
    t('Failed to load battery runs.'),
  );
  useLiveRefresh(['mission.changed'], () => { void list.reload(); });

  async function cancel(run: BatteryRun) {
    setConfirmRun(null);
    try {
      await cancelBatteryRun(run.id, 'Cancelled from the mobile app');
      pushToast('success', t('Battery run cancelled. Its open missions are stopping.'));
      await list.reload();
    } catch (err: unknown) {
      pushToast('error', apiErrorMessage(err, t('Failed to cancel the battery run.')));
    }
  }

  const header = (
    <View style={styles.header}>
      <SelectField
        label={t('Status')}
        value={status}
        onChange={setStatus}
        options={BATTERY_RUN_STATUSES.map(s => ({ value: s, label: t(s) }))}
        placeholder={t('All')}
        allowEmpty
        closeLabel={t('Close')}
        testID="battery-runs-status"
      />
    </View>
  );

  return (
    <View style={styles.fill} testID="battery-runs">
      <PagedList
        state={list}
        header={header}
        keyExtractor={(r) => r.id}
        emptyTitle={status ? t('No battery runs match the current filters.') : t('No battery runs yet. A Full captain evaluation or a gate run starts one.')}
        loadingLabel={t('Loading...')}
        testID="battery-runs-list"
        renderItem={({ item }) => {
          const tasks = t('{{passed}} passed, {{failed}} failed of {{total}}', { passed: item.passedCount, failed: item.failedCount, total: item.totalTasks });
          const extra = item.couldNotRunCount > 0 || item.pendingEvaluationCount > 0
            ? t('{{couldNotRun}} could not run, {{pending}} pending evaluation', { couldNotRun: item.couldNotRunCount, pending: item.pendingEvaluationCount })
            : null;
          const subtitle = [item.model, tasks, extra, item.failureReason, formatRelativeTime(item.createdUtc)].filter(Boolean).join(' · ');
          const open = canCancel && isBatteryRunOpen(item);
          return (
            <ListRow
              title={item.captainName || item.captainId || item.id}
              subtitle={subtitle}
              accessory={<StatusBadge label={t(item.status)} tone={batteryRunTone(item.status)} />}
              accessibilityValue={[t(item.status)]}
              onPress={item.captainId ? () => router.push(`/captains/${encodeURIComponent(item.captainId!)}` as Href) : undefined}
              accessibilityHint={item.captainId ? t('Opens the captain') : undefined}
              menu={open ? { label: t('Cancel run'), onPress: () => setConfirmRun(item), testID: `battery-run-cancel-${item.id}` } : undefined}
              testID={`battery-run-${item.id}`}
            />
          );
        }}
      />
      <ConfirmDialog
        open={confirmRun !== null}
        title={t('Cancel battery run')}
        message={t('Cancel this battery run? Its open missions are stopped and their docks released. Finished task results are kept.')}
        confirmLabel={t('Cancel run')}
        cancelLabel={t('Keep running')}
        danger
        onConfirm={() => { if (confirmRun) void cancel(confirmRun); }}
        onCancel={() => setConfirmRun(null)}
        testID="battery-run-cancel-dialog"
      />
    </View>
  );
}

const styles = StyleSheet.create({
  fill: { flex: 1 },
  header: { paddingTop: spacing.sm, paddingHorizontal: spacing.lg },
});
