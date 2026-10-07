import { useLocalSearchParams } from 'expo-router';
import { useCallback, useState } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import { cancelFleetActionRun, enumerateFleetActionRuns } from '@dashboard/api/client';
import type { FleetActionRun, FleetActionRunStatus } from '@dashboard/types/models';
import { KIND_LABELS, RUN_STATUSES, RUN_STATUS_META, durationBetween, formatDurationMs, isRunActive } from '@dashboard/lib/fleetActionLabels';
import { useActionRunner } from '../../build/fields';
import { ListPane } from '../../build/ListPane';
import { MasterDetail, useSelection } from '../../build/MasterDetail';
import { usePagedList } from '../../build/usePagedList';
import { useAuth } from '../../auth/AuthContext';
import { AppText, Button, ConfirmDialog, Icon, SwipeRow, StatusBadge, type SwipeAction } from '../../components/ui';
import { useLocale } from '../../i18n/LocaleContext';
import { useNotifications } from '../../notifications/NotificationContext';
import { useTheme } from '../../theme/ThemeContext';
import { MIN_TOUCH, spacing } from '../../theme/typography';
import { JsonSheet, RunProgressBar, RunStatusBadge } from './common';
import { FleetActionRunDetailView } from './FleetActionRunDetailView';
import { StatusFilterSheet } from './StatusFilterSheet';
import { usePolling } from './usePolling';

type Order = 'CreatedDescending' | 'CreatedAscending';

/** A valid run status from `?status=`, or '' (all). */
export function runStatusParam(raw: string | string[] | undefined): FleetActionRunStatus | '' {
  const value = Array.isArray(raw) ? raw[0] : raw;
  return value && (RUN_STATUSES as string[]).includes(value) ? (value as FleetActionRunStatus) : '';
}

/**
 * The Runs tab of Fleet Actions (the dashboard's FleetActionRunsTable): every run with its status and progress,
 * endless scroll, a status filter (starting from `?status=`), and a created-date order toggle. Active runs refresh
 * every 5 s. Swipe for Cancel (active runs, tenant admins) and View JSON; tap opens the run (beside the list on
 * tablets).
 */
export function FleetActionRunsTab() {
  const { t, locale, formatRelativeTime } = useLocale();
  const { colors } = useTheme();
  const { isTenantAdmin } = useAuth();
  const { pushToast } = useNotifications();
  const { run } = useActionRunner();
  const params = useLocalSearchParams<{ status?: string }>();
  const [status, setStatus] = useState<FleetActionRunStatus | ''>(() => runStatusParam(params.status));
  const [order, setOrder] = useState<Order>('CreatedDescending');
  const [filterOpen, setFilterOpen] = useState(false);
  const [json, setJson] = useState<FleetActionRun | null>(null);
  const [confirmCancel, setConfirmCancel] = useState<FleetActionRun | null>(null);
  const selection = useSelection(useCallback((id: string) => `/fleet-actions/runs/${id}`, []));

  const list = usePagedList(
    useCallback((pageNumber: number, pageSize: number) => enumerateFleetActionRuns({ pageNumber, pageSize, status, order }), [status, order]),
    [status, order],
  );
  const anyActive = list.items.some((r) => isRunActive(r.status));
  usePolling(anyActive && json === null && confirmCancel === null, () => { void list.reload(); });

  async function cancel(r: FleetActionRun) {
    setConfirmCancel(null);
    const ok = await run('cancel', async () => { await cancelFleetActionRun(r.id); return true; });
    if (ok) pushToast('warning', t('Run "{{name}}" cancelled.', { name: r.actionName }));
    void list.reload();
  }

  const filtered = status !== '';
  const master = (
    <ListPane
      testID="fleet-action-runs-list"
      items={list.items}
      keyOf={(r) => r.id}
      loading={list.loading}
      refreshing={list.refreshing}
      error={list.error}
      onRefresh={() => void list.refresh()}
      onEndReached={list.loadMore}
      loadingMore={list.loadingMore}
      actions={[
        { key: 'filter', icon: 'filter', label: t('Filter runs by status'), badge: filtered ? 1 : undefined, onPress: () => setFilterOpen(true) },
        { key: 'order', icon: order === 'CreatedDescending' ? 'arrow-down' : 'arrow-up', label: order === 'CreatedDescending' ? t('Newest first') : t('Oldest first'), onPress: () => setOrder((o) => (o === 'CreatedDescending' ? 'CreatedAscending' : 'CreatedDescending')) },
      ]}
      header={filtered ? (
        <View style={styles.chips}>
          <StatusBadge label={`${t('Status')}: ${t(RUN_STATUS_META[status].label)}`} tone="info" />
          <Button label={t('Clear filter')} variant="ghost" onPress={() => setStatus('')} style={styles.noMargin} testID="fleet-action-runs-clear" />
        </View>
      ) : null}
      summary={list.totalRecords > 0 ? t('{{count}} runs', { count: list.totalRecords }) : null}
      emptyTitle={filtered ? t('No runs match this status') : t('No fleet action runs yet')}
      emptyMessage={filtered ? undefined : t('Select vessels on the Vessels page and choose Run action... in the selection bar, or run an action from the Actions tab.')}
      emptyAction={filtered ? { label: t('Clear filter'), onPress: () => setStatus('') } : undefined}
      renderItem={({ item }) => {
        const swipe: SwipeAction[] = [];
        if (isRunActive(item.status) && isTenantAdmin) swipe.push({ key: 'cancel', label: t('Cancel'), icon: 'stop-circle-outline', tone: 'danger', onPress: () => setConfirmCancel(item) });
        swipe.push({ key: 'json', label: t('View JSON'), icon: 'code-slash-outline', onPress: () => setJson(item) });
        const duration = formatDurationMs(t, locale, durationBetween(item.startedUtc, item.completedUtc, isRunActive(item.status)));
        const isSelected = selection.selectedId === item.id;
        return (
          <SwipeRow actions={swipe} testID={`fleet-action-run-swipe-${item.id}`}>
            <Pressable
              testID={`fleet-action-run-row-${item.id}`}
              accessibilityRole="button"
              accessibilityLabel={`${item.actionName}, ${t(RUN_STATUS_META[item.status]?.label ?? item.status)}`}
              accessibilityState={{ selected: isSelected }}
              onPress={() => selection.open(item.id)}
              style={({ pressed }) => [
                styles.row,
                { borderBottomColor: colors.border, backgroundColor: isSelected ? colors.surfaceRaised : pressed ? colors.background : colors.surface },
                isSelected ? { borderLeftWidth: 3, borderLeftColor: colors.primary } : null,
              ]}
            >
              <View style={styles.rowHead}>
                <AppText variant="label" style={styles.flex} numberOfLines={1}>{item.actionName}</AppText>
                <RunStatusBadge status={item.status} />
                <Icon name="chevron-forward" size={18} color="textMuted" />
              </View>
              <AppText variant="caption" muted>
                {[t(KIND_LABELS[item.kind]), !item.actionId ? t('Ad hoc') : null, t('Created {{when}}', { when: formatRelativeTime(item.createdUtc) }), duration].filter(Boolean).join(' \u00b7 ')}
              </AppText>
              <RunProgressBar run={item} compact />
            </Pressable>
          </SwipeRow>
        );
      }}
    />
  );

  return (
    <View style={styles.fill}>
      <MasterDetail
        selection={selection}
        master={master}
        emptyTitle={t('Select a run')}
        emptyIcon="play-circle-outline"
        renderDetail={(id) => <FleetActionRunDetailView id={id} embedded onChanged={() => void list.reload()} />}
      />
      <StatusFilterSheet
        open={filterOpen}
        title={t('Filter runs by status')}
        value={status}
        options={RUN_STATUSES.map((s) => ({ value: s, label: RUN_STATUS_META[s].label }))}
        onChange={setStatus}
        onClose={() => setFilterOpen(false)}
        testID="fleet-action-runs-status"
      />
      <JsonSheet open={json !== null} title={json ? t('Fleet action run: {{name}}', { name: json.actionName }) : ''} data={json} onClose={() => setJson(null)} />
      <ConfirmDialog
        open={confirmCancel !== null}
        title={t('Cancel run')}
        message={confirmCancel ? t('Cancel "{{name}}"? Pending targets are cancelled and running commands are stopped. Mission runs cancel voyages that have not finished.', { name: confirmCancel.actionName }) : ''}
        confirmLabel={t('Cancel run')}
        cancelLabel={t('Keep running')}
        danger
        onConfirm={() => { if (confirmCancel) void cancel(confirmCancel); }}
        onCancel={() => setConfirmCancel(null)}
        testID="fleet-action-run-cancel-confirm"
      />
    </View>
  );
}

const styles = StyleSheet.create({
  fill: { flex: 1 },
  flex: { flex: 1 },
  chips: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm, marginHorizontal: spacing.lg, marginBottom: spacing.sm },
  noMargin: { marginBottom: 0 },
  row: { minHeight: MIN_TOUCH + 8, paddingHorizontal: spacing.lg, paddingVertical: spacing.sm, gap: spacing.xs, borderBottomWidth: StyleSheet.hairlineWidth },
  rowHead: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm },
});
