import { useRouter, type Href } from 'expo-router';
import { useMemo, useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { deleteDock, listCaptains, listDocks, listVessels } from '@dashboard/api/client';
import type { Captain, Dock, Vessel } from '@dashboard/types/models';
import { ListPane } from '../../build/ListPane';
import { useLiveResource } from '../../build/useLiveResource';
import { usePagedList } from '../../build/usePagedList';
import { AppText, BottomSheet, Button, ConfirmDialog, ListRow, StatusBadge, SwipeRow } from '../../components/ui';
import { SelectField } from '../../components/ui/SelectSheet';
import { useLocale } from '../../i18n/LocaleContext';
import { useNotifications } from '../../notifications/NotificationContext';
import { useTheme } from '../../theme/ThemeContext';
import { spacing } from '../../theme/typography';
import { UserScopeField } from './UserScopeField';

export type DockActiveFilter = '' | 'active' | 'inactive';

/** The dashboard's dock column filters: branch / worktree text (case-insensitive) and active state. */
export function filterDocks(docks: Dock[], search: string, active: DockActiveFilter): Dock[] {
  const term = search.trim().toLowerCase();
  return docks.filter((d) =>
    (!term || (d.branchName ?? '').toLowerCase().includes(term) || (d.worktreePath ?? '').toLowerCase().includes(term) || d.id.toLowerCase().includes(term))
    && (active === '' || (active === 'active') === d.active));
}

/**
 * The Docks tab of the Captains hub (the dashboard's Docks page, owned by W2 for the dock detail): git worktrees with
 * their vessel, captain, branch, path, and active state, newest first with endless scroll. Search (branch, path, id),
 * a filter sheet (active state, user scope for admins), delete with the dashboard's confirmation (swipe), and bulk
 * delete after a long press. Rows open /docks/:id. Live: reloads on captain and mission events.
 */
export function DocksTab() {
  const { t, formatRelativeTime } = useLocale();
  const { colors } = useTheme();
  const router = useRouter();
  const { pushToast } = useNotifications();
  const [search, setSearch] = useState('');
  const [active, setActive] = useState<DockActiveFilter>('');
  const [userScope, setUserScope] = useState('');
  const [filtersOpen, setFiltersOpen] = useState(false);
  const [confirm, setConfirm] = useState<{ ids: string[] } | null>(null);
  const [selecting, setSelecting] = useState(false);
  const [selected, setSelected] = useState<string[]>([]);

  const list = usePagedList(
    (pageNumber, pageSize) => listDocks({ pageNumber, pageSize, filters: userScope ? { userId: userScope } : undefined }),
    [userScope],
    { live: ['captain.', 'mission.'] },
  );
  const names = useLiveResource(async () => {
    const [captains, vessels] = await Promise.all([
      listCaptains({ pageSize: 1000 }).then((r) => r?.objects ?? []).catch(() => [] as Captain[]),
      listVessels({ pageSize: 1000 }).then((r) => r?.objects ?? []).catch(() => [] as Vessel[]),
    ]);
    return { captains, vessels };
  }, []);

  const rows = useMemo(
    () => [...filterDocks(list.items, search, active)].sort((a, b) => (a.createdUtc < b.createdUtc ? 1 : a.createdUtc > b.createdUtc ? -1 : 0)),
    [list.items, search, active],
  );

  const captainName = (id: string | null) => (id ? names.data?.captains.find((c) => c.id === id)?.name || id.substring(0, 8) : '-');
  const vesselName = (id: string | null) => (id ? names.data?.vessels.find((v) => v.id === id)?.name || id.substring(0, 8) : '-');

  async function runDelete(ids: string[]) {
    setConfirm(null);
    setSelecting(false);
    setSelected([]);
    if (ids.length === 1) {
      try {
        await deleteDock(ids[0]);
        pushToast('warning', t('Dock {{id}} deleted.', { id: ids[0] }));
      } catch {
        pushToast('error', t('Delete failed.'));
      }
    } else {
      let failed = 0;
      for (const id of ids) {
        try { await deleteDock(id); } catch { failed += 1; }
      }
      const deleted = ids.length - failed;
      if (deleted > 0) {
        pushToast(failed > 0 ? 'warning' : 'success', failed > 0
          ? t('Deleted {{deleted}} docks. {{failed}} failed.', { deleted, failed })
          : t('Deleted {{deleted}} docks.', { deleted }));
      } else {
        pushToast('error', t('Deleted {{deleted}} docks, {{failed}} failed.', { deleted, failed }));
      }
    }
    void list.reload();
  }

  const toggle = (id: string) => setSelected((s) => (s.includes(id) ? s.filter((x) => x !== id) : [...s, id]));

  const renderRow = ({ item }: { item: Dock }) => {
    const isSelected = selected.includes(item.id);
    const row = (
      <ListRow
        testID={`dock-row-${item.id}`}
        title={item.branchName || item.id}
        subtitle={[
          `${t('Vessel')}: ${vesselName(item.vesselId)}`,
          `${t('Captain')}: ${captainName(item.captainId)}`,
          item.worktreePath,
          formatRelativeTime(item.createdUtc),
        ].filter(Boolean).join(' \u00b7 ')}
        icon={selecting ? (isSelected ? 'checkbox' : 'square-outline') : undefined}
        selected={selecting && isSelected}
        accessory={<StatusBadge label={item.active ? t('Active') : t('Inactive')} tone={item.active ? 'running' : 'cancelled'} />}
        onPress={() => (selecting ? toggle(item.id) : router.push(`/docks/${item.id}` as Href))}
        onLongPress={() => { setSelecting(true); setSelected([item.id]); }}
        accessibilityHint={t('Long press to select docks')}
      />
    );
    if (selecting) return row;
    return (
      <SwipeRow
        testID={`dock-swipe-${item.id}`}
        actions={[
          ...(item.vesselId ? [{ key: 'vessel', label: t('Vessel'), icon: 'git-branch-outline' as const, onPress: () => router.push(`/vessels/${item.vesselId}` as Href) }] : []),
          ...(item.captainId ? [{ key: 'captain', label: t('Captain'), icon: 'person-outline' as const, onPress: () => router.push(`/captains/${item.captainId}` as Href) }] : []),
          { key: 'delete', label: t('Delete'), icon: 'trash-outline', tone: 'danger', onPress: () => setConfirm({ ids: [item.id] }) },
        ]}
      >
        {row}
      </SwipeRow>
    );
  };

  const header = selecting ? (
    <View style={[styles.bulk, { borderColor: colors.border }]}>
      <AppText style={styles.flex}>{t('{{count}} selected', { count: selected.length })}</AppText>
      <Button label={t('Select all')} variant="ghost" onPress={() => setSelected(rows.map((d) => d.id))} style={styles.noMargin} />
      <Button label={`${t('Delete Selected')} (${selected.length})`} variant="danger" disabled={selected.length === 0} onPress={() => setConfirm({ ids: [...selected] })} style={styles.noMargin} testID="docks-bulk-delete" />
      <Button label={t('Done')} variant="ghost" onPress={() => { setSelecting(false); setSelected([]); }} style={styles.noMargin} />
    </View>
  ) : null;

  const single = confirm?.ids.length === 1;
  return (
    <View style={styles.fill}>
      <ListPane
        testID="docks-list"
        items={rows}
        keyOf={(d) => d.id}
        renderItem={renderRow}
        loading={list.loading}
        refreshing={list.refreshing}
        error={list.error}
        onRefresh={() => void list.refresh()}
        onEndReached={list.loadMore}
        loadingMore={list.loadingMore}
        search={{ value: search, onChange: setSearch, placeholder: t('Search docks') }}
        actions={[{ key: 'filters', icon: 'options-outline', label: t('Filters'), onPress: () => setFiltersOpen(true), badge: ((active ? 1 : 0) + (userScope ? 1 : 0)) || undefined }]}
        summary={list.loading ? null : t('{{count}} docks', { count: list.totalRecords })}
        header={header}
        emptyTitle={list.items.length > 0 ? t('No docks match the current filters.') : t('No docks')}
        emptyMessage={t('Docks are git worktrees captains use while working on a mission.')}
      />
      <BottomSheet open={filtersOpen} title={t('Filters')} onClose={() => setFiltersOpen(false)} closeLabel={t('Close')} testID="docks-filters">
        <SelectField<DockActiveFilter>
          label={t('Active')}
          value={active}
          options={[{ value: '', label: t('All') }, { value: 'active', label: t('Yes') }, { value: 'inactive', label: t('No') }]}
          onChange={setActive}
          closeLabel={t('Close')}
          testID="docks-filter-active"
        />
        <UserScopeField value={userScope} onChange={setUserScope} testID="docks-filter-user" />
      </BottomSheet>
      <ConfirmDialog
        open={!!confirm}
        title={single ? t('Delete Dock') : t('Delete Selected Docks')}
        message={single
          ? t('Delete dock {{id}}? This will clean up the git worktree and cannot be undone.', { id: confirm?.ids[0] ?? '' })
          : t('Delete {{count}} selected dock(s)? This will clean up the git worktrees and cannot be undone.', { count: confirm?.ids.length ?? 0 })}
        confirmLabel={t('Delete')}
        cancelLabel={t('Cancel')}
        danger
        onConfirm={() => { if (confirm) void runDelete(confirm.ids); }}
        onCancel={() => setConfirm(null)}
        testID="dock-confirm"
      />
    </View>
  );
}

const styles = StyleSheet.create({
  fill: { flex: 1 },
  flex: { flex: 1 },
  bulk: { flexDirection: 'row', flexWrap: 'wrap', alignItems: 'center', gap: spacing.xs, paddingHorizontal: spacing.lg, paddingVertical: spacing.sm, borderTopWidth: StyleSheet.hairlineWidth, borderBottomWidth: StyleSheet.hairlineWidth },
  noMargin: { marginBottom: 0 },
});
