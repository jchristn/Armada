import { useMemo, useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { deleteDock, listDocks } from '@dashboard/api/client';
import type { Dock } from '@dashboard/types/models';
import { FilterButton, activeFilterCount } from '../../components/app/FilterSheet';
import { JsonSheet } from '../../components/app/JsonSheet';
import { PagedList } from '../../components/app/PagedList';
import { useConfirm } from '../../components/app/useConfirm';
import { SearchField, StatusBadge } from '../../components/ui';
import { errorMessage } from '../../data/errors';
import { useInterval } from '../../data/useInterval';
import { useLiveRefresh } from '../../data/useLiveRefresh';
import { useNameLookups } from '../../data/useNameLookups';
import { usePagedList } from '../../data/usePagedList';
import { useLocale } from '../../i18n/LocaleContext';
import { useNotifications } from '../../notifications/NotificationContext';
import { spacing } from '../../theme/typography';
import type { OperationsListProps } from './listTypes';
import { SelectableRow } from './w24/SelectableRow';
import { SelectionBar } from './w24/SelectionBar';
import { UserScopeSelect } from '../../components/app/UserScopeSelect';
import { useSelectionMode } from './w24/useSelectionMode';

export const DOCKS_REFRESH_MS = 15000;

/** Pure: the dashboard's column filters (branch or worktree path text), newest first as the dashboard sorts. */
export function filterDocks(docks: Dock[], search: string): Dock[] {
  const term = search.trim().toLowerCase();
  const rows = term
    ? docks.filter((d) => (d.branchName ?? '').toLowerCase().includes(term) || (d.worktreePath ?? '').toLowerCase().includes(term))
    : docks;
  return [...rows].sort((a, b) => (a.createdUtc < b.createdUtc ? 1 : a.createdUtc > b.createdUtc ? -1 : 0));
}

/**
 * Docks (the dashboard's Docks page, the Captains hub's Docks tab): git worktrees provisioned for captains, with the
 * user scope filter, swipe actions (JSON, delete), and long-press bulk delete.
 */
export function DocksList({ onSelect, selectedId }: OperationsListProps) {
  const { t, formatRelativeTime } = useLocale();
  const { pushToast } = useNotifications();
  const [confirmElement, ask] = useConfirm('dock-confirm');
  const lookups = useNameLookups({ vessels: true, captains: true });
  const [userScope, setUserScope] = useState('');
  const [search, setSearch] = useState('');
  const [json, setJson] = useState<Dock | null>(null);
  const selection = useSelectionMode();

  const list = usePagedList(
    (pageNumber, pageSize) => listDocks({ pageNumber, pageSize, filters: userScope ? { userId: userScope } : undefined }),
    (d: Dock) => d.id,
    [userScope],
    25,
    t('Failed to load docks.'),
  );
  const paused = selection.active;
  useLiveRefresh(['captain.', 'mission.'], () => { void list.reload(); }, !paused);
  useInterval(() => { void list.reload(); }, paused ? null : DOCKS_REFRESH_MS);
  const shown = useMemo(() => filterDocks(list.items, search), [list.items, search]);
  const state = useMemo(() => ({ ...list, items: shown }), [list, shown]);

  const deleteOne = (id: string) => ask({
    title: t('Delete Dock'),
    message: t('Delete dock {{id}}? This will clean up the git worktree and cannot be undone.', { id }),
    confirmLabel: t('Delete'),
    danger: true,
    onConfirm: async () => {
      try {
        await deleteDock(id);
        pushToast('warning', t('Dock {{id}} deleted.', { id }));
        await list.reload();
      } catch (e) {
        pushToast('error', errorMessage(e, t('Delete failed.')));
      }
    },
  });
  const bulkDelete = () => {
    const ids = [...selection.selected];
    ask({
      title: t('Delete Selected Docks'),
      message: t('Delete {{count}} selected dock(s)? This will clean up the git worktrees and cannot be undone.', { count: ids.length }),
      confirmLabel: t('Delete'),
      danger: true,
      onConfirm: async () => {
        selection.clear();
        let failed = 0;
        for (const id of ids) {
          try { await deleteDock(id); } catch { failed++; }
        }
        const deleted = ids.length - failed;
        if (deleted > 0) {
          pushToast(failed > 0 ? 'warning' : 'success', failed > 0
            ? t('Deleted {{deleted}} docks. {{failed}} failed.', { deleted, failed })
            : t('Deleted {{deleted}} docks.', { deleted }));
        }
        if (failed > 0) pushToast('error', t('Deleted {{deleted}} docks, {{failed}} failed.', { deleted, failed }));
        await list.reload();
      },
    });
  };

  const header = (
    <View style={styles.header}>
      {selection.active ? (
        <SelectionBar count={selection.selected.length} onDelete={bulkDelete} onSelectAll={() => selection.selectAll(shown.map((d) => d.id))} onCancel={selection.clear} testID="dock-selection" />
      ) : null}
      <SearchField value={search} onChangeText={setSearch} placeholder={t('Search branches and paths')} clearLabel={t('Clear')} testID="dock-search" />
      <View style={styles.row}>
        <FilterButton count={activeFilterCount({ userScope })} onClear={() => setUserScope('')} testID="dock-filters">
          <UserScopeSelect value={userScope} onChange={setUserScope} testID="dock-filter-user" />
        </FilterButton>
      </View>
    </View>
  );

  return (
    <View style={styles.fill} testID="docks-list">
      <PagedList
        state={state}
        header={header}
        keyExtractor={(d) => d.id}
        emptyTitle={list.items.length > 0 ? t('No docks match the current filters.') : t('No docks found.')}
        loadingLabel={t('Loading...')}
        testID="docks"
        renderItem={({ item }) => (
          <SelectableRow
            id={item.id}
            testID={`dock-row-${item.id}`}
            title={item.branchName || item.id}
            subtitle={[lookups.vesselName(item.vesselId), item.captainId ? lookups.captainName(item.captainId) : null, formatRelativeTime(item.createdUtc)].filter(Boolean).join(' - ')}
            accessory={<StatusBadge label={item.active ? t('Active') : t('Inactive')} tone={item.active ? 'success' : 'cancelled'} />}
            selecting={selection.active}
            checked={selection.selected.includes(item.id)}
            highlighted={selectedId === item.id}
            onOpen={onSelect}
            onToggle={selection.toggle}
            onStartSelect={selection.start}
            actions={[
              { key: 'json', label: t('JSON'), icon: 'code-outline', onPress: () => setJson(item) },
              { key: 'delete', label: t('Delete'), icon: 'trash-outline', tone: 'danger', onPress: () => deleteOne(item.id) },
            ]}
          />
        )}
      />
      {confirmElement}
      <JsonSheet open={json !== null} title={json ? `${t('Dock')}: ${json.id}` : ''} data={json} onClose={() => setJson(null)} />
    </View>
  );
}

const styles = StyleSheet.create({
  fill: { flex: 1 },
  header: { paddingTop: spacing.sm },
  row: { flexDirection: 'row', gap: spacing.sm, paddingHorizontal: spacing.md },
});
