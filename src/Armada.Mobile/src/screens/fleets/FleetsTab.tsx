import { useCallback, useMemo, useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { createFleet, deleteFleet, deleteFleetsBatch } from '@dashboard/api/client';
import type { Fleet } from '@dashboard/types/models';
import { buildFleetDuplicatePayload } from '@dashboard/lib/duplicates';
import { ListPane } from '../../build/ListPane';
import { MasterDetail, useSelection } from '../../build/MasterDetail';
import { useActionRunner } from '../../build/fields';
import { useLiveResource } from '../../build/useLiveResource';
import { AppText, Button, ConfirmDialog, ListRow, SwipeRow } from '../../components/ui';
import { useLocale } from '../../i18n/LocaleContext';
import { useNotifications } from '../../notifications/NotificationContext';
import { useTheme } from '../../theme/ThemeContext';
import { spacing } from '../../theme/typography';
import { JsonSheet } from '../fleetActions/common';
import { FleetDetailView } from './FleetDetailView';
import { FleetFormSheet } from './FleetFormSheet';
import { filterFleets, loadFleetData, vesselCounts } from './fleetData';
import { useHardwareBack } from '../../navigation/useHardwareBack';

type Confirm = { kind: 'one'; fleet: Fleet } | { kind: 'bulk'; ids: string[] } | null;

/**
 * The Fleets tab of the Vessels hub (the dashboard's Fleets page): search, create, edit, duplicate, View JSON, and
 * delete with the dashboard's confirmation; long-press starts bulk selection for Delete Selected. Tapping a fleet
 * opens its detail (beside the list on tablets).
 */
export function FleetsTab() {
  const { t, formatRelativeTime } = useLocale();
  const { colors } = useTheme();
  const { pushToast } = useNotifications();
  const { run } = useActionRunner();
  const data = useLiveResource(loadFleetData, []);
  const [search, setSearch] = useState('');
  const [form, setForm] = useState<{ open: boolean; fleet: Fleet | null }>({ open: false, fleet: null });
  const [json, setJson] = useState<Fleet | null>(null);
  const [confirm, setConfirm] = useState<Confirm>(null);
  const [selected, setSelected] = useState<string[] | null>(null);
  const selection = useSelection(useCallback((id: string) => `/fleets/${id}`, []));

  const fleets = useMemo(() => filterFleets(data.data?.fleets ?? [], search), [data.data, search]);
  const counts = useMemo(() => vesselCounts(data.data?.vessels ?? []), [data.data]);
  const selecting = selected !== null;
  useHardwareBack(selecting, () => setSelected(null));

  const toggle = (id: string) => setSelected((s) => {
    const list = s ?? [];
    return list.includes(id) ? list.filter((x) => x !== id) : [...list, id];
  });

  async function duplicate(fleet: Fleet) {
    const created = await run('duplicate', () => createFleet(buildFleetDuplicatePayload(fleet)));
    if (!created) return;
    pushToast('success', t('Fleet "{{name}}" duplicated.', { name: created.name }));
    void data.reload();
    selection.open(created.id);
  }

  async function confirmDelete() {
    const c = confirm;
    setConfirm(null);
    if (!c) return;
    if (c.kind === 'one') {
      const ok = await run('delete', async () => { await deleteFleet(c.fleet.id); return true; });
      if (ok) {
        pushToast('warning', t('Fleet "{{name}}" deleted.', { name: c.fleet.name }));
        if (selection.selectedId === c.fleet.id) selection.clear();
      }
    } else {
      setSelected(null);
      const result = await run('delete', () => deleteFleetsBatch(c.ids));
      if (result) {
        const deleted = result.deleted ?? 0;
        const failed = (result.skipped ?? []).length;
        if (failed > 0) pushToast('warning', t('Deleted {{deleted}} fleets. {{failed}} failed.', { deleted, failed }));
        else pushToast('success', t('Deleted {{deleted}} fleets.', { deleted }));
        for (const s of result.skipped ?? []) pushToast('error', `${s.id}: ${s.reason}`);
      }
    }
    void data.reload();
  }

  const master = (
    <View style={styles.fill}>
      <ListPane
        testID="fleets-list"
        items={fleets}
        keyOf={(f) => f.id}
        loading={data.loading}
        refreshing={data.refreshing}
        error={data.error}
        onRefresh={() => void data.refresh()}
        search={{ value: search, onChange: setSearch, placeholder: t('Search fleets') }}
        actions={[{ key: 'new', icon: 'add', label: t('Create Fleet'), onPress: () => setForm({ open: true, fleet: null }) }]}
        summary={data.data ? t('Fleets are groups of vessels (repositories) useful for organizing and understanding relationships amongst code assets.') : null}
        header={selecting ? (
          <View style={[styles.bulk, { backgroundColor: colors.surfaceRaised, borderColor: colors.border }]} testID="fleets-bulk-bar">
            <AppText variant="label" style={styles.fill}>{t('{{count}} selected', { count: selected.length })}</AppText>
            <Button label={`${t('Delete Selected')} (${selected.length})`} variant="danger" disabled={selected.length === 0} onPress={() => setConfirm({ kind: 'bulk', ids: [...selected] })} testID="fleets-delete-selected" style={styles.noMargin} />
            <Button label={t('Cancel')} variant="ghost" onPress={() => setSelected(null)} testID="fleets-select-cancel" style={styles.noMargin} />
          </View>
        ) : null}
        emptyTitle={(data.data?.fleets.length ?? 0) > 0 ? t('No fleets match the current filters.') : t('No fleets configured.')}
        emptyAction={{ label: t('Create Fleet'), onPress: () => setForm({ open: true, fleet: null }) }}
        renderItem={({ item }) => {
          const count = counts.get(item.id) ?? 0;
          const isSelected = selected?.includes(item.id) ?? false;
          const subtitle = [
            item.description,
            t('{{count}} vessels', { count }),
            item.active === false ? t('Inactive') : null,
            t('Created {{when}}', { when: formatRelativeTime(item.createdUtc) }),
          ].filter(Boolean).join(' \u00b7 ');
          const row = (
            <ListRow
              testID={`fleet-row-${item.name}`}
              title={item.name}
              subtitle={subtitle}
              icon={selecting ? (isSelected ? 'checkbox' : 'square-outline') : 'albums-outline'}
              selected={selecting ? isSelected : selection.selectedId === item.id}
              onPress={() => (selecting ? toggle(item.id) : selection.open(item.id))}
              onLongPress={() => (selecting ? toggle(item.id) : setSelected([item.id]))}
              accessibilityHint={selecting ? t('Select this fleet') : t('Long press to select several fleets')}
            />
          );
          if (selecting) return row;
          return (
            <SwipeRow
              testID={`fleet-swipe-${item.name}`}
              actions={[
                { key: 'edit', label: t('Edit'), icon: 'create-outline', onPress: () => setForm({ open: true, fleet: item }) },
                { key: 'duplicate', label: t('Duplicate'), icon: 'copy-outline', onPress: () => void duplicate(item) },
                { key: 'json', label: t('View JSON'), icon: 'code-slash-outline', onPress: () => setJson(item) },
                { key: 'delete', label: t('Delete'), icon: 'trash-outline', tone: 'danger', onPress: () => setConfirm({ kind: 'one', fleet: item }) },
              ]}
            >
              {row}
            </SwipeRow>
          );
        }}
      />
    </View>
  );

  return (
    <View style={styles.fill}>
      <MasterDetail
        selection={selection}
        master={master}
        emptyTitle={t('Select a fleet')}
        emptyIcon="albums-outline"
        renderDetail={(id) => (
          <FleetDetailView
            id={id}
            embedded
            onDeleted={() => { selection.clear(); void data.reload(); }}
            onChanged={() => void data.reload()}
            onOpenFleet={(next) => selection.open(next)}
          />
        )}
      />
      <FleetFormSheet
        open={form.open}
        fleet={form.fleet}
        pipelines={data.data?.pipelines ?? []}
        onClose={() => setForm({ open: false, fleet: null })}
        onSaved={(_saved, name) => {
          const editing = !!form.fleet;
          setForm({ open: false, fleet: null });
          pushToast('success', editing ? t('Fleet "{{name}}" saved.', { name }) : t('Fleet "{{name}}" created.', { name }));
          void data.reload();
        }}
      />
      <JsonSheet open={json !== null} title={json ? `${t('Fleet')}: ${json.name}` : ''} data={json} onClose={() => setJson(null)} />
      <ConfirmDialog
        open={confirm !== null}
        title={confirm?.kind === 'bulk' ? t('Delete Selected Fleets') : t('Delete Fleet')}
        message={confirm?.kind === 'bulk'
          ? t('Delete {{count}} selected fleet(s)? This cannot be undone.', { count: confirm.ids.length })
          : confirm ? t('Delete fleet "{{name}}"? This cannot be undone.', { name: confirm.fleet.name }) : ''}
        confirmLabel={t('Delete')}
        cancelLabel={t('Cancel')}
        danger
        onConfirm={() => void confirmDelete()}
        onCancel={() => setConfirm(null)}
        testID="fleet-delete-confirm"
      />
    </View>
  );
}

const styles = StyleSheet.create({
  fill: { flex: 1 },
  bulk: { flexDirection: 'row', alignItems: 'center', flexWrap: 'wrap', gap: spacing.sm, marginHorizontal: spacing.md, marginBottom: spacing.sm, padding: spacing.sm, borderWidth: StyleSheet.hairlineWidth, borderRadius: 10 },
  noMargin: { marginBottom: 0 },
});

