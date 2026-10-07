import { useLocalSearchParams, useRouter, type Href } from 'expo-router';
import { useCallback, useMemo, useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { createBacklogItem, deleteBacklogItem, listBacklog, reorderBacklog } from '@dashboard/api/client';
import type { Objective } from '@dashboard/types/models';
import {
  applyBacklogReorder,
  BACKLOG_GROUPS,
  backlogRankSwap,
  countBacklogGroups,
  DEFAULT_BACKLOG_FILTERS,
  filterBacklog,
  hasActiveBacklogFilters,
  sortBacklog,
  type BacklogFilters,
  type BacklogGroupKey,
} from '@dashboard/lib/backlogUtils';
import { buildObjectiveDuplicatePayload } from '@dashboard/lib/duplicates';
import { useAuth } from '../../auth/AuthContext';
import { useActionRunner } from '../../build/fields';
import { ListPane, type ListPaneAction } from '../../build/ListPane';
import { MasterDetail, useSelection } from '../../build/MasterDetail';
import { useLiveResource } from '../../build/useLiveResource';
import { AppText, ConfirmDialog, ListRow, StatusBadge, SwipeRow, type SwipeAction } from '../../components/ui';
import { statusTone } from '../../components/ask/statusTone';
import { useLocale } from '../../i18n/LocaleContext';
import { spacing } from '../../theme/typography';
import { GitHubImportSheet } from './GitHubImportSheet';
import { ObjectiveDetailScreen } from './ObjectiveDetailScreen';
import { ObjectiveFilterSheet } from './ObjectiveFilterSheet';
import { backlogItemPath } from './objectiveLinks';
import { JsonSheet, PillRow, Stat, StatGrid, Tags } from './parts';
import { useBacklogReference } from './useBacklogReference';

/** Socket events that change the backlog (objective.changed and friends). */
export const BACKLOG_LIVE = ['objective.'];

/** Number of active filters shown on the filter button (search and sort are not counted). */
export function activeFilterCount(f: BacklogFilters): number {
  return [f.status, f.kind, f.priority, f.backlogState, f.effort, f.fleetId, f.vesselId].filter((v) => v !== 'all').length
    + [f.owner, f.targetVersion].filter((v) => v.trim().length > 0).length;
}

export interface ObjectivesListProps {
  /**
   * Embedded in another screen (W2's Dispatch hub Backlog tab): no split view of its own, rows always navigate.
   * The /objectives route renders it standalone.
   */
  embedded?: boolean;
  testID?: string;
}

/**
 * The Backlog list (the dashboard's Objectives page, served at /objectives and in the Dispatch hub's Backlog tab):
 * overview counts, group pills, search plus the dashboard's filters and sort in a sheet, swipe actions (move up and
 * down in rank, duplicate, View JSON, delete with confirmation; managers only for changes), Import GitHub, and
 * + Backlog Item. Tablets show the selected item beside the list. Live: reloads on objective.* events.
 */
export function ObjectivesList({ embedded = false, testID = 'objectives-list' }: ObjectivesListProps) {
  const { t, formatRelativeTime } = useLocale();
  const router = useRouter();
  const params = useLocalSearchParams<{ fleetId?: string; vesselId?: string }>();
  const { isAdmin, isTenantAdmin } = useAuth();
  const canManage = isAdmin || isTenantAdmin;
  const { run } = useActionRunner();
  const reference = useBacklogReference({ captains: false, pipelines: false, objectives: false });
  const [userScope, setUserScope] = useState('');
  const [filters, setFilters] = useState<BacklogFilters>(() => ({
    ...DEFAULT_BACKLOG_FILTERS,
    fleetId: typeof params.fleetId === 'string' && params.fleetId ? params.fleetId : 'all',
    vesselId: typeof params.vesselId === 'string' && params.vesselId ? params.vesselId : 'all',
  }));
  const [filtersOpen, setFiltersOpen] = useState(false);
  const [importOpen, setImportOpen] = useState(false);
  const [confirmDelete, setConfirmDelete] = useState<Objective | null>(null);
  const [json, setJson] = useState<Objective | null>(null);

  const selection = useSelection(useCallback((id: string) => backlogItemPath(id), []));
  const openItem = embedded ? (id: string) => router.push(backlogItemPath(id) as Href) : selection.open;

  const list = useLiveResource(
    async () => (await listBacklog({ pageSize: 9999, userId: userScope || undefined }))?.objects ?? [],
    [userScope],
    { live: BACKLOG_LIVE },
  );
  const objectives = useMemo(() => list.data ?? [], [list.data]);
  const counts = useMemo(() => countBacklogGroups(objectives), [objectives]);
  const shown = useMemo(() => sortBacklog(filterBacklog(objectives, filters), filters.sortBy), [objectives, filters]);
  const filtered = hasActiveBacklogFilters(filters);

  const move = (objective: Objective, direction: -1 | 1) => {
    const request = backlogRankSwap(objectives, objective.id, direction);
    if (!request) return;
    void run(`move-${objective.id}`, async () => {
      const updated = await reorderBacklog(request);
      list.setData((prev) => applyBacklogReorder(prev ?? [], updated ?? []));
    }, t('Backlog ranking updated.'));
  };

  const duplicate = (objective: Objective) => {
    void run(`dup-${objective.id}`, async () => {
      const created = await createBacklogItem(buildObjectiveDuplicatePayload(objective));
      openItem(created.id);
      void list.reload();
      return created;
    }, t('Backlog item "{{title}}" duplicated.', { title: objective.title }));
  };

  const remove = (objective: Objective) => {
    setConfirmDelete(null);
    void run(`del-${objective.id}`, async () => {
      await deleteBacklogItem(objective.id);
      if (selection.selectedId === objective.id) selection.clear();
      await list.reload();
    }, t('Backlog item "{{title}}" deleted.', { title: objective.title }));
  };

  const createNew = () => {
    const vesselQuery = filters.vesselId !== 'all' ? `?vesselId=${encodeURIComponent(filters.vesselId)}` : '';
    if (embedded || !selection.isTablet) router.push(`/backlog/new${vesselQuery}` as Href);
    else selection.open('new');
  };

  const actions: ListPaneAction[] = [
    { key: 'filters', icon: 'funnel-outline', label: t('Filters'), onPress: () => setFiltersOpen(true), badge: activeFilterCount(filters) },
  ];
  if (canManage) {
    actions.push({ key: 'import', icon: 'logo-github', label: t('Import GitHub'), onPress: () => setImportOpen(true) });
    actions.push({ key: 'new', icon: 'add', label: t('Backlog Item'), onPress: createNew });
  }

  const rowActions = (objective: Objective): SwipeAction[] => {
    const items: SwipeAction[] = [];
    if (canManage) {
      items.push({ key: 'up', label: t('Move Up'), icon: 'arrow-up', onPress: () => move(objective, -1) });
      items.push({ key: 'down', label: t('Move Down'), icon: 'arrow-down', onPress: () => move(objective, 1) });
      items.push({ key: 'duplicate', label: t('Duplicate'), icon: 'copy-outline', onPress: () => duplicate(objective) });
    }
    items.push({ key: 'json', label: t('View JSON'), icon: 'code-slash-outline', onPress: () => setJson(objective) });
    if (canManage) items.push({ key: 'delete', label: t('Delete'), icon: 'trash-outline', tone: 'danger', onPress: () => setConfirmDelete(objective) });
    return items;
  };

  const header = (
    <View>
      <StatGrid>
        <Stat label={t('Backlog Items')} value={objectives.length} />
        <Stat label={t('Ready For Planning')} value={objectives.filter((o) => o.backlogState === 'ReadyForPlanning').length} />
        <Stat label={t('Ready For Dispatch')} value={objectives.filter((o) => o.backlogState === 'ReadyForDispatch').length} />
        <Stat label={t('Blocked')} value={counts.blocked} />
      </StatGrid>
      <PillRow<BacklogGroupKey>
        label={t('Backlog group views')}
        options={BACKLOG_GROUPS.map((g) => ({ key: g.key, label: t(g.label), count: counts[g.key], description: t(g.description) }))}
        value={filters.group}
        onChange={(group) => setFilters((f) => ({ ...f, group }))}
        testID="objective-group"
      />
    </View>
  );

  const master = (
    <ListPane<Objective>
      testID={testID}
      items={shown}
      keyOf={(o) => o.id}
      loading={list.loading}
      refreshing={list.refreshing}
      error={list.error}
      onRefresh={() => void list.refresh()}
      search={{ value: filters.search, onChange: (search) => setFilters((f) => ({ ...f, search })), placeholder: t('Search backlog') }}
      actions={actions}
      header={header}
      summary={list.loading ? null : t('Showing {{shown}} of {{total}} backlog items.', { shown: shown.length, total: objectives.length })}
      emptyTitle={objectives.length === 0 ? t('No backlog items yet.') : t('No backlog items match the current filters.')}
      emptyMessage={canManage
        ? t('Create a backlog item to start triage, refinement, planning readiness, and delivery lineage inside Armada.')
        : t('Ask a tenant administrator to create and manage backlog items.')}
      emptyAction={filtered ? { label: t('Reset Filters'), onPress: () => setFilters(DEFAULT_BACKLOG_FILTERS) } : undefined}
      renderItem={({ item }) => {
        const vessels = item.vesselIds.map((id) => reference.vesselNames.get(id) ?? id);
        const subtitle = [
          `#${item.rank}`,
          item.owner || t('No owner'),
          vessels.length > 0 ? vessels.slice(0, 2).join(', ') + (vessels.length > 2 ? ` +${vessels.length - 2}` : '') : t('No vessel linked'),
          item.dueUtc ? `${t('Due')} ${formatRelativeTime(item.dueUtc)}` : null,
          formatRelativeTime(item.lastUpdateUtc),
        ].filter(Boolean).join(' \u00b7 ');
        return (
          <SwipeRow actions={rowActions(item)} testID={`objective-swipe-${item.title}`}>
            <ListRow
              testID={`objective-row-${item.title}`}
              title={item.title}
              subtitle={subtitle}
              selected={selection.selectedId === item.id}
              onPress={() => openItem(item.id)}
              accessibilityValue={[item.kind, item.priority, item.effort, item.status, item.backlogState].join(', ')}
              accessory={(
                <View style={styles.accessory}>
                  <StatusBadge label={item.status} tone={statusTone(item.status)} />
                  <Tags items={[item.priority, item.backlogState]} />
                  {item.blockedByObjectiveIds.length > 0 ? <AppText variant="caption" color="warning">{`${t('Blocked by')} ${item.blockedByObjectiveIds.length}`}</AppText> : null}
                </View>
              )}
            />
          </SwipeRow>
        );
      }}
    />
  );

  return (
    <View style={styles.fill}>
      {embedded ? master : (
        <MasterDetail
          selection={selection}
          master={master}
          emptyTitle={t('Select a backlog item')}
          emptyIcon="list-outline"
          renderDetail={(id) => (
            <ObjectiveDetailScreen
              id={id}
              embedded
              onCreated={(created) => { void list.reload(); selection.open(created.id); }}
              onDeleted={() => { selection.clear(); void list.reload(); }}
              onOpenItem={(itemId) => selection.open(itemId)}
            />
          )}
        />
      )}
      <ObjectiveFilterSheet
        open={filtersOpen}
        onClose={() => setFiltersOpen(false)}
        filters={filters}
        onChange={setFilters}
        userScope={userScope}
        onUserScopeChange={setUserScope}
        fleets={reference.fleets}
        vessels={reference.vessels}
      />
      <GitHubImportSheet
        open={importOpen}
        onClose={() => setImportOpen(false)}
        vessels={reference.vessels}
        onImported={(imported) => { setImportOpen(false); void list.reload(); openItem(imported.id); }}
      />
      <ConfirmDialog
        open={!!confirmDelete}
        title={t('Delete Backlog Item')}
        message={confirmDelete ? t('Delete "{{title}}"? This removes the backlog item and its objective snapshot history, but leaves linked missions, releases, deployments, and incidents intact.', { title: confirmDelete.title }) : ''}
        confirmLabel={t('Delete')}
        cancelLabel={t('Cancel')}
        danger
        onConfirm={() => { if (confirmDelete) remove(confirmDelete); }}
        onCancel={() => setConfirmDelete(null)}
        testID="objective-delete-confirm"
      />
      <JsonSheet open={!!json} title={json?.title ?? ''} data={json} onClose={() => setJson(null)} />
    </View>
  );
}

const styles = StyleSheet.create({
  fill: { flex: 1 },
  accessory: { alignItems: 'flex-end', gap: spacing.xs, maxWidth: 150 },
});
