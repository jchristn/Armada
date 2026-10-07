import { useRouter, type Href } from 'expo-router';
import { useCallback, useEffect, useMemo, useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { deleteVessel, getVesselBranches, getVesselGitStatus, listFleets, listPipelines, listUsers, listVessels } from '@dashboard/api/client';
import type { Fleet, Pipeline, UserMaster, Vessel } from '@dashboard/types/models';
import { findLandingMode, getLandingModes } from '@dashboard/lib/vesselForm';
import { useResourceTable } from '@dashboard/lib/useResourceTable';
import { ListPane } from '../../build/ListPane';
import { MasterDetail, useSelection } from '../../build/MasterDetail';
import { useLiveResource } from '../../build/useLiveResource';
import { AppText, BottomSheet, Button, ConfirmDialog, ListRow, StatusBadge, SwipeRow } from '../../components/ui';
import { SelectField, type SelectOption } from '../../components/ui/SelectSheet';
import { useAuth } from '../../auth/AuthContext';
import { useLocale } from '../../i18n/LocaleContext';
import { useNotifications } from '../../notifications/NotificationContext';
import { useTheme } from '../../theme/ThemeContext';
import { spacing } from '../../theme/typography';
import { useVesselActions } from './VesselActions';
import { VesselDetailView } from './VesselDetailView';
import { syncParts, vesselLinks, type GitSync } from './vesselLinks';

/** Parallel git-status / branch requests while filling the Sync and Branches columns. */
const STATUS_CONCURRENCY = 4;

interface VesselsData {
  vessels: Vessel[];
  fleets: Fleet[];
  pipelines: Pipeline[];
}

type SortField = 'name' | 'repoUrl' | 'fleetId' | 'defaultBranch' | 'createdUtc';

/** Run `work` over `items` with at most `limit` in flight. */
async function eachLimited<T>(items: T[], limit: number, work: (item: T) => Promise<void>): Promise<void> {
  let next = 0;
  const runners = Array.from({ length: Math.min(limit, items.length) }, async () => {
    while (next < items.length) {
      const item = items[next];
      next += 1;
      await work(item);
    }
  });
  await Promise.all(runners);
}

function userLabel(user: UserMaster): string {
  const name = [user.firstName, user.lastName].filter(Boolean).join(' ').trim();
  return name ? `${name} (${user.email})` : user.email;
}

/**
 * The Vessels tab of the Vessels hub (the dashboard's Vessels page): every vessel with its fleet, repository,
 * branch, landing mode, git sync and branch count; search; filters (fleet, landing mode, and for admins the user
 * scope) and sort in a sheet; swipe actions (Edit, Branches, More, Delete) and the full action menu; long press for
 * bulk selection (Run action, Delete Selected); + Vessel and Import repositories. Tablets show the vessel page
 * beside the list.
 */
export function VesselsTab() {
  const { t } = useLocale();
  const { colors } = useTheme();
  const router = useRouter();
  const { isAdmin, isTenantAdmin } = useAuth();
  const { pushToast } = useNotifications();
  const landingModes = getLandingModes(t);

  const [userScope, setUserScope] = useState('');
  const [fleetFilter, setFleetFilter] = useState('');
  const [landingModeFilter, setLandingModeFilter] = useState('');
  const [filtersOpen, setFiltersOpen] = useState(false);
  const [selecting, setSelecting] = useState(false);
  const [confirmBulk, setConfirmBulk] = useState(false);
  const [users, setUsers] = useState<UserMaster[]>([]);
  const [gitStatus, setGitStatus] = useState<Record<string, GitSync>>({});
  const [branchCounts, setBranchCounts] = useState<Record<string, number | null>>({});

  const data = useLiveResource<VesselsData>(async () => {
    const [v, f, p] = await Promise.all([
      listVessels({ pageSize: 9999, filters: userScope ? { userId: userScope } : undefined }),
      listFleets({ pageSize: 9999 }),
      listPipelines({ pageSize: 9999 }),
    ]);
    return { vessels: v.objects, fleets: f.objects, pipelines: p.objects };
  }, [userScope]);

  const vessels = useMemo(() => data.data?.vessels ?? [], [data.data]);
  const fleets = useMemo(() => data.data?.fleets ?? [], [data.data]);

  // Sync and branch counts per vessel, in the background after each load (like the dashboard).
  useEffect(() => {
    if (vessels.length === 0) return undefined;
    let live = true;
    void eachLimited(vessels, STATUS_CONCURRENCY, async (v) => {
      let sync: GitSync = { ahead: null, behind: null };
      let count: number | null = null;
      try {
        const gs = await getVesselGitStatus(v.id);
        sync = { ahead: gs.commitsAhead, behind: gs.commitsBehind };
      } catch { /* unknown sync */ }
      try {
        count = (await getVesselBranches(v.id)).branchCount;
      } catch { /* unknown count */ }
      if (!live) return;
      setGitStatus((prev) => ({ ...prev, [v.id]: sync }));
      setBranchCounts((prev) => ({ ...prev, [v.id]: count }));
    });
    return () => { live = false; };
  }, [vessels]);

  const canScope = isAdmin || isTenantAdmin;
  useEffect(() => {
    if (!canScope) return undefined;
    let live = true;
    listUsers().then((r) => { if (live) setUsers(r.objects || []); }).catch(() => { /* the user filter stays minimal */ });
    return () => { live = false; };
  }, [canScope]);

  const fleetName = useCallback((fleetId: string | null) => {
    if (!fleetId) return '';
    return fleets.find((f) => f.id === fleetId)?.name ?? fleetId.substring(0, 8);
  }, [fleets]);

  const baseRows = useMemo(() => vessels.filter((v) =>
    (!fleetFilter || v.fleetId === fleetFilter) && (!landingModeFilter || (v.landingMode ?? '') === landingModeFilter)), [vessels, fleetFilter, landingModeFilter]);

  const table = useResourceTable<Vessel>({
    rows: baseRows,
    getId: (v) => v.id,
    columnValues: {
      name: (v) => v.name.toLowerCase(),
      repoUrl: (v) => (v.repoUrl ?? '').toLowerCase(),
      fleetId: (v) => fleetName(v.fleetId).toLowerCase(),
      defaultBranch: (v) => (v.defaultBranch ?? 'main').toLowerCase(),
      createdUtc: (v) => v.createdUtc,
    },
    searchFields: [(v) => v.name, (v) => v.repoUrl, (v) => v.id],
    initialSortField: 'name',
    initialSortDir: 'asc',
    initialPageSize: 100000,
  });

  const selection = useSelection(vesselLinks.detail);
  const actions = useVesselActions({
    fleets,
    pipelines: data.data?.pipelines ?? [],
    onChanged: () => { void data.reload(); },
    onDeleted: (vessel) => {
      if (selection.selectedId === vessel.id) selection.clear();
      void data.reload();
    },
  });

  const exitSelection = () => { table.clearSelection(); setSelecting(false); };

  async function bulkDelete() {
    setConfirmBulk(false);
    const ids = [...table.selected];
    exitSelection();
    let failed = 0;
    for (const id of ids) {
      try { await deleteVessel(id); } catch { failed += 1; }
    }
    const success = ids.length - failed;
    if (success > 0) {
      pushToast(failed > 0 ? 'warning' : 'success', failed > 0
        ? t('Deleted {{success}} vessels. {{failed}} failed.', { success, failed })
        : t('Deleted {{success}} vessels.', { success }));
    }
    if (failed > 0) pushToast('error', t('Deleted {{success}} vessels, {{failed}} failed.', { success, failed }));
    if (selection.selectedId && ids.includes(selection.selectedId)) selection.clear();
    void data.reload();
  }

  const activeFilters = (fleetFilter ? 1 : 0) + (landingModeFilter ? 1 : 0) + (userScope ? 1 : 0);

  const syncLabel = (v: Vessel): { text: string; tone: 'success' | 'warning' | 'info' } | null => {
    const s = syncParts(gitStatus[v.id]);
    if (!s.known) return null;
    if (s.ahead === 0 && s.behind === 0) return { text: t('in sync'), tone: 'success' };
    const parts: string[] = [];
    if (s.ahead > 0) parts.push(`${s.ahead} ${t('ahead')}`);
    if (s.behind > 0) parts.push(`${s.behind} ${t('behind')}`);
    return { text: parts.join(', '), tone: 'warning' };
  };

  const renderRow = ({ item: v }: { item: Vessel }) => {
    const sync = syncLabel(v);
    const count = branchCounts[v.id];
    const selected = table.selected.includes(v.id);
    const subtitle = [
      v.repoUrl || '-',
      [v.fleetId ? fleetName(v.fleetId) : null, v.defaultBranch || 'main', v.landingMode || t('Default (global)')].filter(Boolean).join(' \u00B7 '),
      count === null || count === undefined ? null : t('{{count}} branches', { count }),
    ].filter(Boolean).join('\n');
    const row = (
      <ListRow
        title={selecting ? `${selected ? '\u2611' : '\u2610'} ${v.name}` : v.name}
        subtitle={subtitle}
        accessory={sync ? <StatusBadge label={sync.text} tone={sync.tone} /> : null}
        selected={selecting ? selected : selection.selectedId === v.id}
        accessibilityHint={findLandingMode(landingModes, v.landingMode).description}
        onPress={() => (selecting ? table.toggleSelect(v.id) : selection.open(v.id))}
        onLongPress={() => { setSelecting(true); table.toggleSelect(v.id); }}
        testID={`vessel-row-${v.name}`}
      />
    );
    if (selecting) return row;
    return (
      <SwipeRow
        testID={`vessel-swipe-${v.name}`}
        actions={[
          { key: 'edit', label: t('Edit'), icon: 'create-outline', onPress: () => actions.run('edit', v) },
          { key: 'branches', label: t('Branches'), icon: 'git-branch-outline', onPress: () => actions.run('branches', v) },
          { key: 'more', label: t('More'), icon: 'ellipsis-horizontal', onPress: () => actions.openMenu(v) },
          { key: 'delete', label: t('Delete'), icon: 'trash-outline', tone: 'danger', onPress: () => actions.run('delete', v) },
        ]}
      >
        {row}
      </SwipeRow>
    );
  };

  const bulkBar = selecting ? (
    <View style={[styles.bulk, { backgroundColor: colors.surface, borderColor: colors.border }]} accessibilityLabel={t('Bulk actions')} testID="vessels-bulk">
      <AppText variant="label">{t('{count, plural, one {# vessel selected} other {# vessels selected}}', { count: table.selected.length })}</AppText>
      <View style={styles.bulkButtons}>
        {isTenantAdmin ? (
          <Button
            label={t('Run action...')}
            onPress={() => { const ids = [...table.selected]; exitSelection(); router.push(vesselLinks.runAction(ids) as Href); }}
            disabled={table.selected.length === 0}
            testID="vessels-bulk-run"
          />
        ) : null}
        <Button label={`${t('Delete Selected')} (${table.selected.length})`} variant="danger" onPress={() => setConfirmBulk(true)} disabled={table.selected.length === 0} testID="vessels-bulk-delete" />
        <Button label={t('Select all vessels')} variant="ghost" onPress={() => table.selectAll()} />
        <Button label={t('Clear selection')} variant="ghost" onPress={exitSelection} testID="vessels-bulk-clear" />
      </View>
    </View>
  ) : null;

  const fleetOptions: SelectOption<string>[] = [{ value: '', label: t('All Fleets') }, ...fleets.map((f) => ({ value: f.id, label: f.name }))];
  const modeOptions: SelectOption<string>[] = [
    { value: '', label: t('All Modes') },
    ...landingModes.filter((m) => m.value).map((m) => ({ value: m.value, label: `${m.value} -- ${m.short}`, description: m.description })),
  ];
  const userOptions: SelectOption<string>[] = [{ value: '', label: t('All users') }, ...users.map((u) => ({ value: u.id, label: userLabel(u) }))];
  const sortOptions: SelectOption<SortField>[] = [
    { value: 'name', label: t('Name') },
    { value: 'repoUrl', label: t('Repository') },
    { value: 'fleetId', label: t('Fleet') },
    { value: 'defaultBranch', label: t('Branch') },
    { value: 'createdUtc', label: t('Created') },
  ];

  const master = (
    <ListPane
      testID="vessels-list"
      items={table.sorted}
      keyOf={(v) => v.id}
      renderItem={renderRow}
      loading={data.loading}
      refreshing={data.refreshing}
      error={data.error ? t('Failed to load vessels.') : null}
      onRefresh={() => void data.refresh()}
      search={{ value: table.search, onChange: table.setSearch, placeholder: t('Search vessels') }}
      actions={[
        { key: 'filters', icon: 'filter-outline', label: t('Filters'), onPress: () => setFiltersOpen(true), badge: activeFilters || undefined },
        ...(isTenantAdmin ? [{ key: 'import', icon: 'download-outline' as const, label: t('Import repositories'), onPress: () => router.push('/vessels/import' as Href) }] : []),
        { key: 'new', icon: 'add-circle-outline', label: t('Create Vessel'), onPress: actions.create },
      ]}
      summary={vessels.length > 0 ? t('{{shown}} of {{total}} vessels', { shown: table.sorted.length, total: vessels.length }) : null}
      header={bulkBar}
      emptyTitle={vessels.length > 0 ? t('No vessels match the current filters.') : t('No vessels configured.')}
      emptyMessage={vessels.length > 0 ? undefined : t('Add a single repository with + Vessel, or import many existing local repositories at once.')}
      emptyAction={vessels.length === 0 && isTenantAdmin ? { label: t('Import repositories'), onPress: () => router.push('/vessels/import' as Href) } : undefined}
    />
  );

  return (
    <View style={styles.fill}>
      <MasterDetail
        selection={selection}
        master={master}
        emptyTitle={t('Select a vessel')}
        emptyIcon="git-network-outline"
        renderDetail={(id) => (
          <VesselDetailView id={id} embedded onDeleted={selection.clear} onChanged={() => { void data.reload(); }} />
        )}
      />
      {actions.elements}
      <BottomSheet open={filtersOpen} title={t('Filters')} onClose={() => setFiltersOpen(false)} closeLabel={t('Close')} testID="vessels-filters">
        <SelectField label={t('Fleet')} value={fleetFilter} options={fleetOptions} onChange={setFleetFilter} closeLabel={t('Close')} testID="vessels-filter-fleet" />
        <SelectField label={t('Landing Mode')} value={landingModeFilter} options={modeOptions} onChange={setLandingModeFilter} closeLabel={t('Close')} testID="vessels-filter-landing-mode" />
        {canScope ? (
          <SelectField label={t('View records for a specific user')} value={userScope} options={userOptions} onChange={setUserScope} closeLabel={t('Close')} testID="vessels-filter-user" />
        ) : null}
        <SelectField label={t('Sort by')} value={(table.sortField || 'name') as SortField} options={sortOptions} onChange={(f) => { if (f !== table.sortField) table.handleSort(f); }} closeLabel={t('Close')} testID="vessels-sort" />
        <Button
          label={table.sortDir === 'asc' ? t('Ascending') : t('Descending')}
          variant="secondary"
          icon={table.sortDir === 'asc' ? 'arrow-up' : 'arrow-down'}
          onPress={() => table.handleSort(table.sortField || 'name')}
          testID="vessels-sort-dir"
        />
        <Button
          label={t('Clear filters')}
          variant="ghost"
          onPress={() => { setFleetFilter(''); setLandingModeFilter(''); setUserScope(''); }}
          disabled={activeFilters === 0}
          testID="vessels-filters-clear"
        />
      </BottomSheet>
      <ConfirmDialog
        open={confirmBulk}
        title={t('Delete Selected Vessels')}
        message={t('Delete {{count}} selected vessel(s)? This cannot be undone.', { count: table.selected.length })}
        confirmLabel={t('Delete')}
        cancelLabel={t('Cancel')}
        danger
        onConfirm={() => void bulkDelete()}
        onCancel={() => setConfirmBulk(false)}
        testID="vessels-bulk-confirm"
      />
    </View>
  );
}

const styles = StyleSheet.create({
  fill: { flex: 1 },
  bulk: { marginHorizontal: spacing.md, marginBottom: spacing.md, padding: spacing.md, borderWidth: StyleSheet.hairlineWidth, borderRadius: 10, gap: spacing.sm },
  bulkButtons: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.sm },
});
