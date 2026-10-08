import { useRouter, type Href } from 'expo-router';
import { useMemo, useState } from 'react';
import { StyleSheet, View } from 'react-native';
import {
  deleteRequestHistoryByFilter, deleteRequestHistoryEntries, deleteRequestHistoryEntry, getRequestHistorySummary, listRequestHistory,
} from '@dashboard/api/client';
import type { RequestHistoryEntry } from '@dashboard/types/models';
import { formatBytes } from '@dashboard/lib/format';
import {
  ACTIVITY_RANGE_OPTIONS, buildRequestHistoryDeleteQuery, buildRequestHistoryQuery, buildRequestHistorySummaryQuery,
  defaultRequestHistoryFilters, hasActiveRequestFilters, type ActivityRangeId, type RequestHistoryFilters,
} from '@dashboard/lib/requestHistory';
import { useAuth } from '../../auth/AuthContext';
import { ActionBar, FieldCard } from '../../components/resource/DetailParts';
import { FormSheet, str, type FormField, type FormValues } from '../../components/resource/FormSheet';
import { MasterDetail, useSelection } from '../../components/resource/Hub';
import { ResourceList, StatRow } from '../../components/resource/ResourceList';
import { useConfirm } from '../../components/resource/ResourceRow';
import { resourceStyles } from '../../components/resource/styles';
import { AppText } from '../../components/ui/AppText';
import { Button } from '../../components/ui/Button';
import { ListRow } from '../../components/ui/ListRow';
import { SegmentedControl } from '../../components/ui/SegmentedControl';
import { StatusBadge } from '../../components/ui/StatusBadge';
import { SwipeRow } from '../../components/ui/SwipeRow';
import { useLocale } from '../../i18n/LocaleContext';
import { useNotifications } from '../../notifications/NotificationContext';
import { errorText, useLoad, useReloadOnFocus } from '../../resource/useLoad';
import { spacing } from '../../theme/typography';
import { RequestActivityChart } from './RequestActivityChart';
import { RequestDetailView, replayHref } from './RequestDetail';
import { usePagedLoad } from './usePagedLoad';

const PAGE_SIZE = 50;
const METHODS = ['GET', 'POST', 'PUT', 'PATCH', 'DELETE'];

function filterValues(f: RequestHistoryFilters): FormValues {
  return { ...f };
}

function filtersFrom(v: FormValues): RequestHistoryFilters {
  const isSuccess = str(v, 'isSuccess');
  return {
    method: str(v, 'method'), route: str(v, 'route').trim(), statusCode: str(v, 'statusCode').trim(), principal: str(v, 'principal').trim(),
    tenantId: str(v, 'tenantId').trim(), userId: str(v, 'userId').trim(), credentialId: str(v, 'credentialId').trim(),
    isSuccess: isSuccess === 'true' || isSuccess === 'false' ? isSuccess : 'all', fromUtc: str(v, 'fromUtc').trim(), toUtc: str(v, 'toUtc').trim(),
  };
}

/**
 * Activity > API Requests (the dashboard's Request History): summary cards, the bucketed activity chart with its
 * range, filters in a sheet (tenant for admins, user for tenant admins), the server-paged list, and request detail
 * (pushed on phones, beside the list on tablets). Long-press starts bulk selection (Delete Selected); Delete
 * Filtered / Delete Visible Range removes everything the filters match. Replay opens API Explorer.
 */
export function RequestsTab() {
  const { t, formatRelativeTime } = useLocale();
  const { isAdmin, isTenantAdmin } = useAuth();
  const { pushToast } = useNotifications();
  const router = useRouter();
  const { confirm, dialog } = useConfirm('requests-confirm');
  const selection = useSelection((id) => `/requests/${id}`);
  const defaults = useMemo(() => defaultRequestHistoryFilters(), []);
  const [filters, setFilters] = useState<RequestHistoryFilters>(defaults);
  const [range, setRange] = useState<ActivityRangeId>('lastDay');
  const [filtersOpen, setFiltersOpen] = useState(false);
  const [selected, setSelected] = useState<string[]>([]);
  const key = JSON.stringify(filters);

  const paged = usePagedLoad((pageNumber) => listRequestHistory(buildRequestHistoryQuery(filters, pageNumber, PAGE_SIZE)), [key], { fallbackError: t('Failed to load request history.') });
  const summary = useLoad(() => getRequestHistorySummary(buildRequestHistorySummaryQuery(filters, range)), [key, range], { fallbackError: t('Failed to load request history summary.') });
  useReloadOnFocus(paged.reload);
  const active = hasActiveRequestFilters(filters);
  const totalRecords = paged.first?.totalRecords ?? 0;
  const selecting = selected.length > 0;
  const s = summary.data;

  async function reloadAll() {
    setSelected([]);
    await Promise.all([paged.reload(), summary.reload()]);
  }

  function toggle(id: string) {
    setSelected((cur) => (cur.includes(id) ? cur.filter((x) => x !== id) : [...cur, id]));
  }

  function removeOne(entry: RequestHistoryEntry) {
    confirm({
      title: t('Delete Request Entry'),
      message: t('Delete the stored request-history entry for {{route}}?', { route: entry.route }),
      confirmLabel: t('Delete'),
      danger: true,
      onConfirm: async () => {
        try {
          await deleteRequestHistoryEntry(entry.id);
          pushToast('warning', t('Request entry deleted.'));
          if (selection.selected === entry.id) selection.clear();
          await reloadAll();
        } catch (err: unknown) {
          pushToast('error', errorText(err, t('Failed to delete request history entry.')));
        }
      },
    });
  }

  function removeSelected() {
    const ids = selected;
    confirm({
      title: t('Delete Selected Requests'),
      message: t('Delete {{count}} selected request-history entries?', { count: ids.length }),
      confirmLabel: t('Delete Selected'),
      danger: true,
      onConfirm: async () => {
        try {
          await deleteRequestHistoryEntries(ids);
          pushToast('warning', t('Deleted {{count}} request entries.', { count: ids.length }));
          await reloadAll();
        } catch (err: unknown) {
          pushToast('error', errorText(err, t('Failed to delete selected request entries.')));
        }
      },
    });
  }

  function removeFiltered() {
    confirm({
      title: t('Delete Filtered Requests'),
      message: t('Delete all request-history entries matching the current filters?'),
      confirmLabel: t('Delete Filtered'),
      danger: true,
      onConfirm: async () => {
        try {
          await deleteRequestHistoryByFilter(buildRequestHistoryDeleteQuery(filters));
          pushToast('warning', t('Deleted the current filtered request set.'));
          await reloadAll();
        } catch (err: unknown) {
          pushToast('error', errorText(err, t('Failed to delete filtered request entries.')));
        }
      },
    });
  }

  const fields = (): FormField[] => [
    { kind: 'select', key: 'method', label: t('Method'), options: [{ value: '', label: t('All methods') }, ...METHODS.map((m) => ({ value: m, label: m }))] },
    { kind: 'integer', key: 'statusCode', label: t('Status Code'), placeholder: '200' },
    { kind: 'text', key: 'route', label: t('Route'), placeholder: '/api/v1/missions' },
    { kind: 'text', key: 'principal', label: t('Principal'), placeholder: t('user@tenant') },
    { kind: 'text', key: 'credentialId', label: t('Credential'), placeholder: 'cred_' },
    { kind: 'select', key: 'isSuccess', label: t('Result'), options: [{ value: 'all', label: t('All') }, { value: 'true', label: t('Success') }, { value: 'false', label: t('Failure') }] },
    ...(isAdmin ? [{ kind: 'text' as const, key: 'tenantId', label: t('Tenant'), placeholder: 'ten_' }] : []),
    ...(isAdmin || isTenantAdmin ? [{ kind: 'text' as const, key: 'userId', label: t('User'), placeholder: 'usr_' }] : []),
    { kind: 'text', key: 'fromUtc', label: t('From'), placeholder: 'YYYY-MM-DDTHH:mm' },
    { kind: 'text', key: 'toUtc', label: t('To'), placeholder: 'YYYY-MM-DDTHH:mm' },
  ];

  const header = (
    <>
      <StatRow stats={[
        { label: t('Total Requests'), value: s ? s.totalCount.toLocaleString() : '...' },
        { label: t('Success Rate'), value: s ? `${s.successRate.toFixed(1)}%` : '...' },
        { label: t('Failures'), value: s ? s.failureCount.toLocaleString() : '...', tone: s && s.failureCount > 0 ? 'danger' : undefined },
        { label: t('Average Duration'), value: s ? `${s.averageDurationMs.toFixed(2)} ms` : '...' },
      ]} />
      <FieldCard title={t('Activity')} testID="request-activity">
        <View style={styles.chart}>
          <AppText variant="caption" muted>{t('Bucketed request volume with success and failure breakdown.')}</AppText>
          <SegmentedControl label={t('Activity range')} value={range} onChange={setRange} options={ACTIVITY_RANGE_OPTIONS.map((o) => ({ value: o.id, label: t(o.label), testID: `request-range-${o.id}` }))} />
          {summary.loading && !s ? <AppText muted>{t('Loading summary...')}</AppText> : <RequestActivityChart summary={s} rangeId={range} />}
        </View>
      </FieldCard>
      <ActionBar>
        <Button label={active ? `${t('Filters')} (${t('Active')})` : t('Filters')} icon="funnel-outline" variant="secondary" style={resourceStyles.action} onPress={() => setFiltersOpen(true)} testID="requests-filters" />
        {active ? <Button label={t('Reset')} variant="ghost" style={resourceStyles.action} onPress={() => setFilters(defaultRequestHistoryFilters())} testID="requests-reset" /> : null}
        {selecting ? <Button label={`${t('Delete Selected')} (${selected.length})`} variant="danger" style={resourceStyles.action} onPress={removeSelected} testID="requests-delete-selected" /> : null}
        {selecting ? <Button label={t('Cancel')} variant="ghost" style={resourceStyles.action} onPress={() => setSelected([])} /> : null}
        {!selecting && totalRecords > 0 ? (
          <Button label={active ? t('Delete Filtered') : t('Delete Visible Range')} variant="ghost" style={resourceStyles.action} onPress={removeFiltered} testID="requests-delete-filtered" />
        ) : null}
      </ActionBar>
      {paged.first ? (
        <AppText variant="caption" muted style={styles.count}>{t('Showing {{shown}} of {{total}}', { shown: paged.items.length, total: totalRecords })}</AppText>
      ) : null}
    </>
  );

  const list = (
    <ResourceList
      testID="activity-requests"
      items={paged.items}
      keyOf={(e) => e.id}
      loading={paged.loading}
      error={paged.error}
      onRetry={() => void paged.reload()}
      refreshing={paged.refreshing}
      onRefresh={() => { void paged.refresh(); void summary.reload(); }}
      onEndReached={paged.loadMore}
      hasMore={paged.hasMore}
      loadingMore={paged.loadingMore}
      header={header}
      emptyTitle={t('No request history entries match the current filters.')}
      renderItem={(entry) => {
        const isSelected = selected.includes(entry.id);
        return (
          <SwipeRow
            testID={`request-row-${entry.id}-swipe`}
            actions={[
              { key: 'replay', label: t('Replay'), icon: 'play-outline', onPress: () => router.push(replayHref(entry.id) as Href) },
              { key: 'delete', label: t('Delete'), icon: 'trash-outline', tone: 'danger', onPress: () => removeOne(entry) },
            ]}
          >
            <ListRow
              testID={`request-row-${entry.id}`}
              icon={selecting ? (isSelected ? 'checkbox' : 'square-outline') : undefined}
              title={`${entry.method} ${entry.route}`}
              subtitle={[entry.principalDisplay || t('Anonymous'), `${entry.durationMs.toFixed(2)} ms`, `${formatBytes(entry.requestSizeBytes)} / ${formatBytes(entry.responseSizeBytes)}`].join(' \u2022 ')}
              accessory={(
                <View style={styles.accessory}>
                  <StatusBadge label={String(entry.statusCode)} tone={entry.isSuccess ? 'success' : 'failed'} />
                  <AppText variant="caption" muted>{formatRelativeTime(entry.createdUtc)}</AppText>
                </View>
              )}
              accessibilityValue={[String(entry.statusCode), formatRelativeTime(entry.createdUtc)]}
              selected={isSelected || selection.selected === entry.id}
              accessibilityHint={t('Long press to select')}
              onPress={() => (selecting ? toggle(entry.id) : selection.open(entry.id))}
              onLongPress={() => toggle(entry.id)}
            />
          </SwipeRow>
        );
      }}
    />
  );

  return (
    <>
      <MasterDetail
        list={list}
        detail={selection.selected ? <RequestDetailView key={selection.selected} id={selection.selected} embedded onDeleted={() => { selection.clear(); void reloadAll(); }} /> : null}
      />
      <FormSheet
        testID="requests-filter-form"
        open={filtersOpen}
        title={t('Filters')}
        initial={filterValues(filters)}
        fields={fields}
        submitLabel={t('Apply')}
        onClose={() => setFiltersOpen(false)}
        onSubmit={async (values) => {
          setFilters(filtersFrom(values));
          setFiltersOpen(false);
        }}
      />
      {dialog}
    </>
  );
}

const styles = StyleSheet.create({
  chart: { padding: spacing.md, gap: spacing.sm },
  count: { marginHorizontal: spacing.lg, marginBottom: spacing.sm },
  accessory: { alignItems: 'flex-end', gap: spacing.xs },
});
