import { useMemo, useState } from 'react';
import { StyleSheet, View } from 'react-native';
import {
  deleteMergeEntry, enqueueMerge, listMergeQueue, processAllMergeQueue, processMergeEntry, cancelMergeEntry,
} from '@dashboard/api/client';
import { buildEnqueueMergeRequest, emptyEnqueueMergeForm, type EnqueueMergeForm } from '@dashboard/lib/mergeQueueForm';
import type { MergeEntry } from '@dashboard/types/models';
import { EntityStatusBadge } from '../../components/app/EntityStatusBadge';
import { FilterButton, activeFilterCount } from '../../components/app/FilterSheet';
import { JsonSheet } from '../../components/app/JsonSheet';
import { PagedList } from '../../components/app/PagedList';
import { useConfirm } from '../../components/app/useConfirm';
import { BottomSheet, Button, SearchField, SelectField, TextField } from '../../components/ui';
import { errorMessage } from '../../data/errors';
import { useInterval } from '../../data/useInterval';
import { useLiveRefresh } from '../../data/useLiveRefresh';
import { useNameLookups } from '../../data/useNameLookups';
import { usePagedList } from '../../data/usePagedList';
import { useLocale } from '../../i18n/LocaleContext';
import { useNotifications } from '../../notifications/NotificationContext';
import { spacing } from '../../theme/typography';
import type { OperationsListProps } from './listTypes';
import { MissionOutputSheet, type MissionOutputRequest } from '../../components/app/MissionOutputSheet';
import { SelectableRow } from './w24/SelectableRow';
import { SelectionBar } from './w24/SelectionBar';
import { UserScopeSelect } from '../../components/app/UserScopeSelect';
import { useSelectionMode } from './w24/useSelectionMode';

/** The dashboard's default auto-refresh (15 s), the polling fallback behind live updates. */
export const MERGE_QUEUE_REFRESH_MS = 15000;

export interface MergeQueueColumnFilters {
  search: string;
  status: string;
  vesselId: string;
}

/** Pure: the dashboard's client-side column filters (branch or target text, status text, vessel). */
export function filterMergeEntries(entries: MergeEntry[], filters: MergeQueueColumnFilters): MergeEntry[] {
  const term = filters.search.trim().toLowerCase();
  const status = filters.status.trim().toLowerCase();
  return entries.filter((e) =>
    (!term || e.branchName.toLowerCase().includes(term) || e.targetBranch.toLowerCase().includes(term)) &&
    (!status || (e.status ?? '').toLowerCase().includes(status)) &&
    (!filters.vesselId || e.vesselId === filters.vesselId));
}

/**
 * The merge queue (the dashboard's MergeQueue page, the Missions hub's Merge Queue tab): endless list with the
 * user scope, vessel, and status filters, Process All, Enqueue, swipe actions (process, cancel, mission diff and
 * log, JSON, delete), and long-press bulk delete.
 */
export function MergeQueueList({ onSelect, selectedId }: OperationsListProps) {
  const { t } = useLocale();
  const { pushToast } = useNotifications();
  const [confirmElement, ask] = useConfirm('merge-confirm');
  const lookups = useNameLookups({ vessels: true });
  const [userScope, setUserScope] = useState('');
  const [filters, setFilters] = useState<MergeQueueColumnFilters>({ search: '', status: '', vesselId: '' });
  const [json, setJson] = useState<MergeEntry | null>(null);
  const [view, setView] = useState<MissionOutputRequest | null>(null);
  const [enqueueOpen, setEnqueueOpen] = useState(false);
  const selection = useSelectionMode();

  const list = usePagedList(
    (pageNumber, pageSize) => listMergeQueue({ pageNumber, pageSize, filters: userScope ? { userId: userScope } : undefined }),
    (e: MergeEntry) => e.id,
    [userScope],
    25,
    t('Failed to load merge queue.'),
  );
  const paused = selection.active;
  useLiveRefresh(['mission.'], () => { void list.reload(); }, !paused);
  useInterval(() => { void list.reload(); }, paused ? null : MERGE_QUEUE_REFRESH_MS);

  const shown = useMemo(() => filterMergeEntries(list.items, filters), [list.items, filters]);
  const state = useMemo(() => ({ ...list, items: shown }), [list, shown]);

  const run = async (action: () => Promise<unknown>, success: string, severity: 'success' | 'warning', failure: string) => {
    try {
      await action();
      pushToast(severity, success);
      await list.reload();
    } catch (e) {
      pushToast('error', errorMessage(e, failure));
    }
  };

  const processOne = (id: string) => ask({
    title: t('Process Entry'),
    message: t('Process merge entry {{id}} now?', { id }),
    confirmLabel: t('Process'),
    onConfirm: () => run(() => processMergeEntry(id), t('Merge entry {{id}} processing started.', { id }), 'success', t('Process failed.')),
  });
  const cancelOne = (id: string) => ask({
    title: t('Cancel Entry'),
    message: t('Cancel merge entry {{id}}?', { id }),
    confirmLabel: t('Cancel Entry'),
    danger: true,
    onConfirm: () => run(() => cancelMergeEntry(id), t('Merge entry {{id}} cancelled.', { id }), 'warning', t('Cancel failed.')),
  });
  const deleteOne = (id: string) => ask({
    title: t('Delete Entry'),
    message: t('Delete merge entry {{id}}? This cannot be undone.', { id }),
    confirmLabel: t('Delete'),
    danger: true,
    onConfirm: () => run(() => deleteMergeEntry(id), t('Merge entry {{id}} deleted.', { id }), 'warning', t('Delete failed.')),
  });
  const processAll = () => ask({
    title: t('Process Merge Queue'),
    message: t('Process all queued entries in the merge queue now?'),
    confirmLabel: t('Process All'),
    onConfirm: () => run(() => processAllMergeQueue(), t('Merge queue processing started.'), 'success', t('Process all failed.')),
  });
  const bulkDelete = () => {
    const ids = [...selection.selected];
    ask({
      title: t('Delete Selected Entries'),
      message: t('Delete {{count}} selected merge queue entries? This cannot be undone.', { count: ids.length }),
      confirmLabel: t('Delete'),
      danger: true,
      onConfirm: async () => {
        selection.clear();
        let failed = 0;
        for (const id of ids) {
          try { await deleteMergeEntry(id); } catch { failed++; }
        }
        const deleted = ids.length - failed;
        if (deleted > 0) {
          pushToast(failed > 0 ? 'warning' : 'success', failed > 0
            ? t('Deleted {{deleted}} merge entries. {{failed}} failed.', { deleted, failed })
            : t('Deleted {{deleted}} merge entries.', { deleted }));
        }
        if (failed > 0) pushToast('error', t('Deleted {{deleted}} entries, {{failed}} failed.', { deleted, failed }));
        await list.reload();
      },
    });
  };

  const header = (
    <View style={styles.header}>
      {selection.active ? (
        <SelectionBar
          count={selection.selected.length}
          onDelete={bulkDelete}
          onSelectAll={() => selection.selectAll(shown.map((e) => e.id))}
          onCancel={selection.clear}
          testID="merge-selection"
        />
      ) : null}
      <SearchField value={filters.search} onChangeText={(search) => setFilters((f) => ({ ...f, search }))} placeholder={t('Search branches')} clearLabel={t('Clear')} testID="merge-search" />
      <View style={styles.row}>
        <FilterButton
          count={activeFilterCount({ status: filters.status, vesselId: filters.vesselId, userScope })}
          onClear={() => { setFilters((f) => ({ ...f, status: '', vesselId: '' })); setUserScope(''); }}
          testID="merge-filters"
        >
          <TextField label={t('Status')} value={filters.status} onChangeText={(status) => setFilters((f) => ({ ...f, status }))} autoCapitalize="none" testID="merge-filter-status" />
          <SelectField
            label={t('Vessel')}
            value={filters.vesselId}
            onChange={(vesselId) => setFilters((f) => ({ ...f, vesselId }))}
            allowEmpty
            placeholder={t('All Vessels')}
            closeLabel={t('Close')}
            options={lookups.vessels.map((v) => ({ value: v.id, label: v.name }))}
            testID="merge-filter-vessel"
          />
          <UserScopeSelect value={userScope} onChange={setUserScope} testID="merge-filter-user" />
        </FilterButton>
      </View>
      <View style={styles.row}>
        <Button label={t('Process All')} variant="secondary" icon="play-forward-outline" onPress={processAll} style={styles.flex} testID="merge-process-all" />
        <Button label={t('Enqueue')} icon="add" onPress={() => setEnqueueOpen(true)} style={styles.flex} testID="merge-enqueue" />
      </View>
    </View>
  );

  return (
    <View style={styles.fill} testID="merge-queue-list">
      <PagedList
        state={state}
        header={header}
        keyExtractor={(e) => e.id}
        emptyTitle={list.items.length > 0 ? t('No entries match the current filters.') : t('Merge queue is empty.')}
        loadingLabel={t('Loading...')}
        testID="merge-queue"
        renderItem={({ item }) => (
          <SelectableRow
            id={item.id}
            testID={`merge-row-${item.id}`}
            title={`${item.branchName} -> ${item.targetBranch}`}
            subtitle={[t('Priority') + ' ' + item.priority, item.vesselId ? lookups.vesselName(item.vesselId) : null, item.missionId].filter(Boolean).join(' - ')}
            accessory={<EntityStatusBadge status={item.status} />}
            selecting={selection.active}
            checked={selection.selected.includes(item.id)}
            highlighted={selectedId === item.id}
            onOpen={onSelect}
            onToggle={selection.toggle}
            onStartSelect={selection.start}
            actions={[
              { key: 'process', label: t('Process'), icon: 'play-outline', onPress: () => processOne(item.id) },
              { key: 'cancel', label: t('Cancel'), icon: 'stop-circle-outline', onPress: () => cancelOne(item.id) },
              ...(item.missionId ? [
                { key: 'diff', label: t('Diff'), icon: 'git-compare-outline' as const, onPress: () => setView({ kind: 'diff', missionId: item.missionId!, title: t('Mission {{id}}...', { id: item.missionId!.substring(0, 8) }) }) },
                { key: 'log', label: t('Log'), icon: 'document-text-outline' as const, onPress: () => setView({ kind: 'log', missionId: item.missionId!, title: t('Mission {{id}}...', { id: item.missionId!.substring(0, 8) }) }) },
              ] : []),
              { key: 'json', label: t('JSON'), icon: 'code-outline', onPress: () => setJson(item) },
              { key: 'delete', label: t('Delete'), icon: 'trash-outline', tone: 'danger', onPress: () => deleteOne(item.id) },
            ]}
          />
        )}
      />
      {confirmElement}
      <JsonSheet open={json !== null} title={json ? `${t('Merge Entry')}: ${json.id}` : ''} data={json} onClose={() => setJson(null)} />
      <MissionOutputSheet request={view} onClose={() => setView(null)} />
      <EnqueueSheet
        open={enqueueOpen}
        vessels={lookups.vessels.map((v) => ({ value: v.id, label: v.name }))}
        onClose={() => setEnqueueOpen(false)}
        onSubmit={async (form) => {
          try {
            await enqueueMerge(buildEnqueueMergeRequest(form));
            setEnqueueOpen(false);
            pushToast('success', t('Merge entry enqueued.'));
            await list.reload();
          } catch (e) {
            pushToast('error', errorMessage(e, t('Enqueue failed.')));
          }
        }}
      />
    </View>
  );
}

/** The dashboard's Enqueue Merge modal. */
export function EnqueueSheet({ open, vessels, onClose, onSubmit }: {
  open: boolean;
  vessels: { value: string; label: string }[];
  onClose: () => void;
  onSubmit: (form: EnqueueMergeForm) => Promise<void>;
}) {
  const { t } = useLocale();
  return (
    <BottomSheet open={open} title={t('Enqueue Merge')} onClose={onClose} closeLabel={t('Close')} testID="enqueue-sheet">
      {/* Mounted only while open (the Modal renders nothing when hidden), so the form starts blank each time. */}
      <EnqueueForm vessels={vessels} onSubmit={onSubmit} />
    </BottomSheet>
  );
}

function EnqueueForm({ vessels, onSubmit }: { vessels: { value: string; label: string }[]; onSubmit: (form: EnqueueMergeForm) => Promise<void> }) {
  const { t } = useLocale();
  const [form, setForm] = useState<EnqueueMergeForm>(emptyEnqueueMergeForm);
  const [busy, setBusy] = useState(false);
  const valid = form.branchName.trim() !== '' && form.targetBranch.trim() !== '';
  return (
    <>
      <TextField label={t('Branch Name')} value={form.branchName} onChangeText={(branchName) => setForm((f) => ({ ...f, branchName }))} autoCapitalize="none" autoCorrect={false} testID="enqueue-branch" />
      <TextField label={t('Target Branch')} value={form.targetBranch} onChangeText={(targetBranch) => setForm((f) => ({ ...f, targetBranch }))} autoCapitalize="none" autoCorrect={false} testID="enqueue-target" />
      <TextField label={t('Mission ID (optional)')} value={form.missionId} placeholder="msn_..." onChangeText={(missionId) => setForm((f) => ({ ...f, missionId }))} autoCapitalize="none" autoCorrect={false} testID="enqueue-mission" />
      <SelectField label={t('Vessel')} value={form.vesselId} onChange={(vesselId) => setForm((f) => ({ ...f, vesselId }))} allowEmpty placeholder={t('(none)')} closeLabel={t('Close')} options={vessels} testID="enqueue-vessel" />
      <TextField label={t('Test Command (optional)')} value={form.testCommand} placeholder="npm test" onChangeText={(testCommand) => setForm((f) => ({ ...f, testCommand }))} autoCapitalize="none" autoCorrect={false} testID="enqueue-test" />
      <TextField label={t('Priority')} value={String(form.priority)} keyboardType="number-pad" onChangeText={(value) => setForm((f) => ({ ...f, priority: Number(value) || 0 }))} testID="enqueue-priority" />
      <Button
        label={t('Enqueue')}
        busy={busy}
        disabled={!valid}
        onPress={async () => { setBusy(true); try { await onSubmit(form); } finally { setBusy(false); } }}
        testID="enqueue-submit"
      />
    </>
  );
}

const styles = StyleSheet.create({
  fill: { flex: 1 },
  header: { paddingTop: spacing.sm },
  row: { flexDirection: 'row', gap: spacing.sm, paddingHorizontal: spacing.md },
  flex: { flex: 1 },
});
