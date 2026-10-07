import { useRouter, type Href } from 'expo-router';
import { useCallback, useMemo, useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { deleteCaptain, listCaptains } from '@dashboard/api/client';
import { canCaptainStartPlanning } from '@dashboard/lib/captains';
import type { Captain } from '@dashboard/types/models';
import { ListPane } from '../../build/ListPane';
import { MasterDetail, useSelection } from '../../build/MasterDetail';
import { useLiveResource } from '../../build/useLiveResource';
import { statusTone } from '../../components/ask/statusTone';
import { AppText, BottomSheet, Button, ConfirmDialog, ListRow, StatusBadge, SwipeRow, type IconName } from '../../components/ui';
import { SelectField, type SelectOption } from '../../components/ui/SelectSheet';
import { useLocale } from '../../i18n/LocaleContext';
import { useNotifications } from '../../notifications/NotificationContext';
import { useTheme } from '../../theme/ThemeContext';
import { spacing } from '../../theme/typography';
import { CaptainDetailScreen } from './CaptainDetailScreen';
import { CaptainFormSheet } from './CaptainFormSheet';
import { TierBadge } from './TierBadge';
import { useCaptainActions } from './useCaptainActions';
import { UserScopeField } from './UserScopeField';

export type CaptainSort = 'name' | 'runtime' | 'state' | 'createdUtc';

export interface CaptainFilters {
  search: string;
  runtime: string;
  state: string;
  sort: CaptainSort;
  dir: 'asc' | 'desc';
}

/** The dashboard's Captains table filtering and sorting (name search, runtime and state filters, sort column). */
export function filterCaptains(captains: Captain[], f: CaptainFilters): Captain[] {
  const term = f.search.trim().toLowerCase();
  const rows = captains.filter((c) =>
    (!term || c.name.toLowerCase().includes(term))
    && (!f.runtime || c.runtime.toLowerCase().includes(f.runtime.toLowerCase()))
    && (!f.state || (c.state ?? '').toLowerCase() === f.state.toLowerCase()));
  const key = (c: Captain): string => {
    switch (f.sort) {
      case 'runtime': return c.runtime.toLowerCase();
      case 'state': return (c.state ?? '').toLowerCase();
      case 'createdUtc': return c.createdUtc;
      default: return c.name.toLowerCase();
    }
  };
  return [...rows].sort((a, b) => {
    const va = key(a);
    const vb = key(b);
    if (va < vb) return f.dir === 'asc' ? -1 : 1;
    if (va > vb) return f.dir === 'asc' ? 1 : -1;
    return 0;
  });
}

const CAPTAIN_STATES = ['Idle', 'Working', 'Planning', 'Refining', 'Stalled', 'Stopping', 'Quarantined', 'Analyzing'];
const DEFAULT_FILTERS: CaptainFilters = { search: '', runtime: '', state: '', sort: 'name', dir: 'asc' };

/**
 * The Captains tab of the Captains hub (the dashboard's Captains page): every captain with its tier, runtime, state
 * (with the quarantine end), current mission, and heartbeat. Search, a filter sheet (runtime, state, user scope for
 * admins, sort), create, stop all, swipe actions (edit, stop, delete), a long-press action sheet with the rest of the
 * row menu (start planning, duplicate, recall, restart, select), and bulk delete. Tablets show the captain beside the
 * list. Live: reloads on captain events.
 */
export function CaptainsTab() {
  const { t, formatRelativeTime } = useLocale();
  const { colors } = useTheme();
  const router = useRouter();
  const { pushToast } = useNotifications();
  const [filters, setFilters] = useState<CaptainFilters>(DEFAULT_FILTERS);
  const [userScope, setUserScope] = useState('');
  const [filtersOpen, setFiltersOpen] = useState(false);
  const [form, setForm] = useState<{ open: boolean; captain: Captain | null }>({ open: false, captain: null });
  const [menuFor, setMenuFor] = useState<Captain | null>(null);
  const [selecting, setSelecting] = useState(false);
  const [selected, setSelected] = useState<string[]>([]);
  const [confirmBulk, setConfirmBulk] = useState(false);
  const selection = useSelection(useCallback((id: string) => `/captains/${id}`, []));

  const resource = useLiveResource(
    async () => (await listCaptains({ pageSize: 9999, filters: userScope ? { userId: userScope } : undefined })).objects ?? [],
    [userScope],
    { live: ['captain.'] },
  );
  const captains = useMemo(() => resource.data ?? [], [resource.data]);
  const rows = useMemo(() => filterCaptains(captains, filters), [captains, filters]);
  const runtimes = useMemo(() => Array.from(new Set(captains.map((c) => c.runtime).filter(Boolean))).sort(), [captains]);

  const actions = useCaptainActions('list', (action, captain) => {
    if (action === 'delete' && captain && selection.selectedId === captain.id) selection.clear();
    void resource.reload();
  });

  const activeFilters = (filters.runtime ? 1 : 0) + (filters.state ? 1 : 0) + (userScope ? 1 : 0);

  async function bulkDelete() {
    setConfirmBulk(false);
    const ids = [...selected];
    setSelected([]);
    setSelecting(false);
    let failed = 0;
    for (const id of ids) {
      try { await deleteCaptain(id); } catch { failed += 1; }
    }
    const deleted = ids.length - failed;
    if (deleted > 0) {
      pushToast(failed > 0 ? 'warning' : 'success', failed > 0
        ? t('Deleted {{deleted}} captains. {{failed}} failed.', { deleted, failed })
        : t('Deleted {{deleted}} captains.', { deleted }));
    } else if (failed > 0) {
      pushToast('error', t('Deleted {{deleted}} captains, {{failed}} failed.', { deleted, failed }));
    }
    void resource.reload();
  }

  function toggle(id: string) {
    setSelected((s) => (s.includes(id) ? s.filter((x) => x !== id) : [...s, id]));
  }

  const renderRow = ({ item }: { item: Captain }) => {
    const subtitle = [
      item.runtime,
      item.state === 'Quarantined' && item.quarantineUntilUtc ? t('until {{time}}', { time: formatRelativeTime(item.quarantineUntilUtc) }) : null,
      item.currentMissionId ? `${t('Current Mission')}: ${item.currentMissionId.substring(0, 8)}...` : null,
      item.lastHeartbeatUtc ? `${t('Heartbeat')}: ${formatRelativeTime(item.lastHeartbeatUtc)}` : null,
    ].filter(Boolean).join(' \u00b7 ');
    const isSelected = selected.includes(item.id);
    const row = (
      <ListRow
        testID={`captain-row-${item.name}`}
        title={item.name}
        subtitle={subtitle}
        icon={selecting ? (isSelected ? 'checkbox' : 'square-outline') : undefined}
        accessory={(
          <View style={styles.badges}>
            <TierBadge tier={item.tier} />
            <StatusBadge label={item.state} tone={statusTone(item.state)} />
          </View>
        )}
        selected={selecting ? isSelected : selection.selectedId === item.id}
        onPress={() => (selecting ? toggle(item.id) : selection.open(item.id))}
        onLongPress={() => setMenuFor(item)}
        accessibilityHint={selecting ? t('Select this captain') : t('Long press for more actions')}
      />
    );
    if (selecting) return row;
    return (
      <SwipeRow
        testID={`captain-swipe-${item.name}`}
        actions={[
          { key: 'edit', label: t('Edit'), icon: 'create-outline', onPress: () => setForm({ open: true, captain: item }) },
          { key: 'stop', label: t('Stop'), icon: 'stop-circle-outline', onPress: () => actions.request('stop', item) },
          { key: 'delete', label: t('Delete'), icon: 'trash-outline', tone: 'danger', onPress: () => actions.request('delete', item) },
        ]}
      >
        {row}
      </SwipeRow>
    );
  };

  const header = selecting ? (
    <View style={[styles.bulk, { borderColor: colors.border }]}>
      <AppText style={styles.flex}>{t('{{count}} selected', { count: selected.length })}</AppText>
      <Button label={t('Select all')} variant="ghost" onPress={() => setSelected(rows.map((c) => c.id))} style={styles.noMargin} />
      <Button label={`${t('Delete Selected')} (${selected.length})`} variant="danger" disabled={selected.length === 0} onPress={() => setConfirmBulk(true)} style={styles.noMargin} testID="captains-bulk-delete" />
      <Button label={t('Done')} variant="ghost" onPress={() => { setSelecting(false); setSelected([]); }} style={styles.noMargin} />
    </View>
  ) : null;

  const sortOptions: SelectOption<string>[] = [
    { value: 'name:asc', label: `${t('Name')} (A-Z)` },
    { value: 'name:desc', label: `${t('Name')} (Z-A)` },
    { value: 'runtime:asc', label: t('Runtime') },
    { value: 'state:asc', label: t('State') },
    { value: 'createdUtc:desc', label: t('Newest first') },
    { value: 'createdUtc:asc', label: t('Oldest first') },
  ];

  const master = (
    <ListPane
      testID="captains-list"
      items={rows}
      keyOf={(c) => c.id}
      renderItem={renderRow}
      loading={resource.loading}
      refreshing={resource.refreshing}
      error={resource.error}
      onRefresh={() => void resource.refresh()}
      search={{ value: filters.search, onChange: (search) => setFilters((f) => ({ ...f, search })), placeholder: t('Search captains') }}
      actions={[
        { key: 'filters', icon: 'options-outline', label: t('Filters'), onPress: () => setFiltersOpen(true), badge: activeFilters || undefined },
        { key: 'stop-all', icon: 'stop-circle-outline', label: t('Stop All'), onPress: () => actions.request('stopAll', null) },
        { key: 'new', icon: 'add', label: t('Create Captain'), onPress: () => setForm({ open: true, captain: null }) },
      ]}
      summary={resource.data ? t('{{count}} captains', { count: rows.length }) : null}
      header={header}
      emptyTitle={captains.length > 0 ? t('No captains match the current filters.') : t('No captains configured.')}
      emptyMessage={t('AI agent harness processes that execute missions. Monitor state, current mission, and captain lifecycle.')}
      emptyAction={captains.length > 0 ? undefined : { label: t('Create Captain'), onPress: () => setForm({ open: true, captain: null }) }}
    />
  );

  return (
    <View style={styles.fill}>
      <MasterDetail
        selection={selection}
        master={master}
        renderDetail={(id) => <CaptainDetailScreen id={id} embedded onRemoved={selection.clear} />}
        emptyTitle={t('Select a captain')}
        emptyIcon="person-outline"
      />
      <BottomSheet open={filtersOpen} title={t('Filters')} onClose={() => setFiltersOpen(false)} closeLabel={t('Close')} testID="captains-filters">
        <SelectField
          label={t('Runtime')}
          value={filters.runtime}
          options={[{ value: '', label: t('All') }, ...runtimes.map((r) => ({ value: r, label: r }))]}
          onChange={(runtime) => setFilters((f) => ({ ...f, runtime }))}
          closeLabel={t('Close')}
          testID="captains-filter-runtime"
        />
        <SelectField
          label={t('State')}
          value={filters.state}
          options={[{ value: '', label: t('All') }, ...CAPTAIN_STATES.map((s) => ({ value: s, label: t(s) }))]}
          onChange={(state) => setFilters((f) => ({ ...f, state }))}
          closeLabel={t('Close')}
          testID="captains-filter-state"
        />
        <SelectField
          label={t('Sort')}
          value={`${filters.sort}:${filters.dir}`}
          options={sortOptions}
          onChange={(v) => { const [sort, dir] = v.split(':'); setFilters((f) => ({ ...f, sort: sort as CaptainSort, dir: dir === 'desc' ? 'desc' : 'asc' })); }}
          closeLabel={t('Close')}
          testID="captains-sort"
        />
        <UserScopeField value={userScope} onChange={setUserScope} testID="captains-filter-user" />
        <Button label={t('Clear filters')} variant="ghost" onPress={() => { setFilters((f) => ({ ...DEFAULT_FILTERS, search: f.search })); setUserScope(''); }} />
      </BottomSheet>
      <BottomSheet open={!!menuFor} title={menuFor?.name ?? ''} onClose={() => setMenuFor(null)} closeLabel={t('Close')} testID="captain-menu">
        {menuFor ? (
          <View>
            <MenuItem icon="open-outline" label={t('View Detail')} onPress={() => { const c = menuFor; setMenuFor(null); router.push(`/captains/${c.id}` as Href); }} />
            {canCaptainStartPlanning(menuFor) ? (
              <MenuItem icon="bulb-outline" label={t('Start Planning')} onPress={() => { const c = menuFor; setMenuFor(null); router.push(`/planning?captainId=${encodeURIComponent(c.id)}` as Href); }} />
            ) : null}
            <MenuItem icon="create-outline" label={t('Edit')} onPress={() => { const c = menuFor; setMenuFor(null); setForm({ open: true, captain: c }); }} />
            <MenuItem icon="copy-outline" label={t('Duplicate')} onPress={() => { const c = menuFor; setMenuFor(null); void actions.duplicate(c).then((created) => { if (created) { void resource.reload(); selection.open(created.id); } }); }} />
            <MenuItem icon="notifications-outline" label={t('View Notifications')} onPress={() => { setMenuFor(null); router.push('/inbox' as Href); }} />
            <MenuItem icon="stop-circle-outline" label={t('Stop')} onPress={() => { const c = menuFor; setMenuFor(null); actions.request('stop', c); }} />
            <MenuItem icon="return-down-back-outline" label={t('Recall')} onPress={() => { const c = menuFor; setMenuFor(null); actions.request('recall', c); }} />
            <MenuItem icon="refresh" label={t('Restart')} onPress={() => { const c = menuFor; setMenuFor(null); actions.request('restart', c); }} />
            <MenuItem icon="checkbox-outline" label={t('Select')} onPress={() => { const c = menuFor; setMenuFor(null); setSelecting(true); setSelected([c.id]); }} />
            <MenuItem icon="trash-outline" label={t('Delete')} danger onPress={() => { const c = menuFor; setMenuFor(null); actions.request('delete', c); }} />
          </View>
        ) : null}
      </BottomSheet>
      <CaptainFormSheet
        open={form.open}
        captain={form.captain}
        onClose={() => setForm({ open: false, captain: null })}
        onSaved={(saved) => {
          const created = !form.captain;
          setForm({ open: false, captain: null });
          void resource.reload();
          if (created && saved?.id) selection.open(saved.id);
        }}
      />
      <ConfirmDialog
        open={confirmBulk}
        title={t('Delete Selected Captains')}
        message={t('Delete {{count}} selected captain(s)? This cannot be undone.', { count: selected.length })}
        confirmLabel={t('Delete')}
        cancelLabel={t('Cancel')}
        danger
        onConfirm={() => void bulkDelete()}
        onCancel={() => setConfirmBulk(false)}
        testID="captains-bulk-confirm"
      />
      {actions.dialog}
    </View>
  );
}

function MenuItem({ icon, label, onPress, danger }: { icon: IconName; label: string; onPress: () => void; danger?: boolean }) {
  return <ListRow icon={icon} title={label} onPress={onPress} destructive={danger} />;
}

const styles = StyleSheet.create({
  fill: { flex: 1 },
  flex: { flex: 1 },
  badges: { alignItems: 'flex-end', gap: spacing.xs },
  bulk: { flexDirection: 'row', flexWrap: 'wrap', alignItems: 'center', gap: spacing.xs, paddingHorizontal: spacing.lg, paddingVertical: spacing.sm, borderTopWidth: StyleSheet.hairlineWidth, borderBottomWidth: StyleSheet.hairlineWidth },
  noMargin: { marginBottom: 0 },
});
