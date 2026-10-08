import { cancelVoyage, getVoyageStatus, listVoyages, purgeVoyage } from '@dashboard/api/client';
import { findLandingMode, getVoyageLandingModes } from '@dashboard/lib/vesselForm';
import type { Voyage } from '@dashboard/types/models';
import { useRouter, type Href } from 'expo-router';
import { useMemo, useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { EntityStatusBadge } from '../../components/app/EntityStatusBadge';
import { activeFilterCount, FilterButton } from '../../components/app/FilterSheet';
import { JsonSheet } from '../../components/app/JsonSheet';
import { PagedList } from '../../components/app/PagedList';
import { useConfirm } from '../../components/app/useConfirm';
import { UserScopeSelect } from '../../components/app/UserScopeSelect';
import { ActionSheet, AppText, Button, IconButton, ListRow, SearchField } from '../../components/ui';
import { SelectField } from '../../components/ui/SelectField';
import { useLiveRefresh } from '../../data/useLiveRefresh';
import { usePagedList } from '../../data/usePagedList';
import { useLocale } from '../../i18n/LocaleContext';
import { useNotifications } from '../../notifications/NotificationContext';
import { spacing } from '../../theme/typography';
import type { OperationsListProps } from './listTypes';
import { DEFAULT_VOYAGE_SORT, filterAndSortVoyages, VOYAGE_STATUSES, type VoyageSort } from './voyage/voyageListFilters';
import { useHardwareBack } from '../../navigation/useHardwareBack';

const SORTS: { value: VoyageSort; label: string }[] = [
  { value: 'createdUtc:desc', label: 'Newest first' },
  { value: 'createdUtc:asc', label: 'Oldest first' },
  { value: 'title:asc', label: 'Title (A-Z)' },
  { value: 'title:desc', label: 'Title (Z-A)' },
  { value: 'status:asc', label: 'Status (A-Z)' },
  { value: 'status:desc', label: 'Status (Z-A)' },
];

/**
 * The Voyages tab of the Missions hub (the dashboard's Voyages page): server-paginated list with endless scroll,
 * search, status and user filters, sorting, row actions (detail, status, JSON, cancel, purge), long-press
 * multi-select with bulk cancel, and live updates.
 */
export function VoyagesList({ onSelect, selectedId }: OperationsListProps) {
  const { t, formatRelativeTime } = useLocale();
  const router = useRouter();
  const { pushToast } = useNotifications();
  const landingModes = getVoyageLandingModes(t);
  const [userScope, setUserScope] = useState('');
  const [search, setSearch] = useState('');
  const [status, setStatus] = useState('');
  const [sort, setSort] = useState<VoyageSort>(DEFAULT_VOYAGE_SORT);
  const [selected, setSelected] = useState<string[]>([]);
  const [menu, setMenu] = useState<Voyage | null>(null);
  const [json, setJson] = useState<{ title: string; data: unknown } | null>(null);
  const [error, setError] = useState('');
  const [confirmElement, confirm] = useConfirm('voyages-confirm');

  const list = usePagedList(
    (pageNumber, pageSize) => listVoyages({ pageNumber, pageSize, filters: userScope ? { userId: userScope } : undefined }),
    (v: Voyage) => v.id,
    [userScope],
    25,
    t('Failed to load voyages.'),
  );
  useLiveRefresh(['voyage.', 'mission.'], () => { void list.reload(); });

  const rows = useMemo(() => filterAndSortVoyages(list.items, { search, status, sort }), [list.items, search, status, sort]);
  const shownState = useMemo(() => ({ ...list, items: rows }), [list, rows]);
  const selecting = selected.length > 0;
  useHardwareBack(selecting, () => setSelected([]));
  const toggle = (id: string) => setSelected((s) => (s.includes(id) ? s.filter((x) => x !== id) : [...s, id]));

  const handleCancel = (v: Voyage) => confirm({
    title: t('Cancel Voyage'),
    message: t('Cancel voyage "{{title}}"? All pending missions will be cancelled.', { title: v.title }),
    confirmLabel: t('Cancel Voyage'),
    danger: true,
    onConfirm: async () => {
      try {
        await cancelVoyage(v.id);
        pushToast('warning', t('Voyage "{{title}}" cancelled.', { title: v.title }));
        void list.reload();
      } catch { setError(t('Cancel failed.')); }
    },
  });

  const handlePurge = (v: Voyage) => confirm({
    title: t('Purge Voyage'),
    message: t('Purge voyage "{{title}}"? This will permanently remove the voyage and all associated missions. This cannot be undone.', { title: v.title }),
    confirmLabel: t('Purge'),
    danger: true,
    onConfirm: async () => {
      try {
        await purgeVoyage(v.id);
        pushToast('warning', t('Voyage "{{title}}" purged.', { title: v.title }));
        void list.reload();
      } catch { setError(t('Purge failed.')); }
    },
  });

  const handleBulkCancel = () => confirm({
    title: t('Cancel Selected Voyages'),
    message: t('Cancel {{count}} selected voyage(s)?', { count: selected.length }),
    confirmLabel: t('Cancel Selected'),
    danger: true,
    onConfirm: async () => {
      const ids = [...selected];
      setSelected([]);
      let failed = 0;
      for (const id of ids) {
        try { await cancelVoyage(id); } catch { failed++; }
      }
      const success = ids.length - failed;
      if (success > 0) {
        pushToast(failed > 0 ? 'warning' : 'success', failed > 0
          ? t('Cancelled {{success}} voyages. {{failed}} failed.', { success, failed })
          : t('Cancelled {{success}} voyages.', { success }));
      }
      if (failed > 0) setError(t('Cancelled {{success}} voyages, {{failed}} failed.', { success, failed }));
      void list.reload();
    },
  });

  const viewStatus = async (v: Voyage) => {
    try {
      setJson({ title: t('Voyage Status'), data: await getVoyageStatus(v.id) });
    } catch { setError(t('Failed to load voyage status.')); }
  };

  const filterCount = activeFilterCount({ status, userScope, sort: sort === DEFAULT_VOYAGE_SORT ? '' : sort });

  const header = (
    <View style={styles.header}>
      <SearchField value={search} onChangeText={setSearch} placeholder={t('Search voyages')} clearLabel={t('Clear')} testID="voyages-search" />
      <View style={styles.buttons}>
        <FilterButton
          count={filterCount}
          onClear={() => { setStatus(''); setUserScope(''); setSort(DEFAULT_VOYAGE_SORT); }}
          testID="voyages-filters"
        >
          <SelectField
            label={t('Status')}
            value={status}
            onChange={setStatus}
            allowEmpty
            placeholder={t('All Statuses')}
            closeLabel={t('Close')}
            options={VOYAGE_STATUSES.map((s) => ({ value: s, label: t(s) }))}
            testID="voyages-filter-status"
          />
          <UserScopeSelect value={userScope} onChange={setUserScope} testID="voyages-filter-user" />
          <SelectField
            label={t('Sort')}
            value={sort}
            onChange={(v) => setSort(v as VoyageSort)}
            placeholder={t('Sort')}
            closeLabel={t('Close')}
            options={SORTS.map((s) => ({ value: s.value, label: t(s.label) }))}
            testID="voyages-sort"
          />
        </FilterButton>
        <Button testID="voyages-create" label={`+ ${t('Voyage')}`} onPress={() => router.push('/voyages/create' as Href)} style={styles.flex} />
      </View>
      {selecting ? (
        <View style={styles.buttons}>
          <Button testID="voyages-bulk-cancel" variant="danger" label={`${t('Cancel Selected')} (${selected.length})`} onPress={handleBulkCancel} style={styles.flex} />
          <Button testID="voyages-selection-clear" variant="ghost" label={t('Clear')} onPress={() => setSelected([])} />
        </View>
      ) : null}
      {error ? <AppText color="danger" accessibilityRole="alert" style={styles.error} testID="voyages-error">{error}</AppText> : null}
    </View>
  );

  return (
    <View style={styles.flex}>
      {confirmElement}
      <PagedList
        testID="voyages-list"
        state={shownState}
        header={header}
        keyExtractor={(v) => v.id}
        loadingLabel={t('Loading...')}
        emptyTitle={list.items.length > 0 ? t('No voyages match the current filters.') : t('No voyages found.')}
        renderItem={({ item: v }) => {
          const checked = selected.includes(v.id);
          const mode = findLandingMode(landingModes, v.landingMode);
          return (
            <ListRow
              testID={`voyage-row-${v.id}`}
              icon={selecting ? (checked ? 'checkbox' : 'square-outline') : undefined}
              title={v.title}
              subtitle={`${v.id}\n${t('Landing Mode')}: ${v.landingMode || t('Default')} (${mode.short}) - ${formatRelativeTime(v.createdUtc)}`}
              selected={selecting ? checked : selectedId === v.id}
              accessory={(
                <View style={styles.accessory}>
                  <EntityStatusBadge status={v.status} />
                  <IconButton icon="ellipsis-horizontal" label={t('Actions')} onPress={() => setMenu(v)} testID={`voyage-row-menu-${v.id}`} />
                </View>
              )}
              accessibilityHint={selecting ? t('Select this voyage') : undefined}
              onPress={() => (selecting ? toggle(v.id) : onSelect(v.id))}
              onLongPress={() => toggle(v.id)}
            />
          );
        }}
      />
      <ActionSheet
        open={menu !== null}
        title={menu?.title ?? ''}
        closeLabel={t('Close')}
        onClose={() => setMenu(null)}
        testID="voyage-row-actions"
        actions={menu ? [
          { key: 'detail', label: t('View Detail'), icon: 'open-outline', onPress: () => onSelect(menu.id) },
          { key: 'status', label: t('View Status'), icon: 'pulse-outline', onPress: () => void viewStatus(menu) },
          { key: 'json', label: t('View JSON'), icon: 'code-slash-outline', onPress: () => setJson({ title: `${t('Voyage')}: ${menu.title}`, data: menu }) },
          { key: 'select', label: selected.includes(menu.id) ? t('Deselect') : t('Select'), icon: 'checkbox-outline', onPress: () => toggle(menu.id) },
          { key: 'cancel', label: t('Cancel'), icon: 'close-circle-outline', danger: true, onPress: () => handleCancel(menu) },
          { key: 'purge', label: t('Purge'), icon: 'trash-outline', danger: true, onPress: () => handlePurge(menu) },
        ] : []}
      />
      <JsonSheet open={json !== null} title={json?.title ?? ''} data={json?.data} onClose={() => setJson(null)} />
    </View>
  );
}

const styles = StyleSheet.create({
  flex: { flex: 1 },
  header: { paddingTop: spacing.md },
  buttons: { flexDirection: 'row', gap: spacing.sm, paddingHorizontal: spacing.md },
  accessory: { alignItems: 'flex-end', gap: spacing.xs },
  error: { paddingHorizontal: spacing.lg, paddingBottom: spacing.sm },
});
