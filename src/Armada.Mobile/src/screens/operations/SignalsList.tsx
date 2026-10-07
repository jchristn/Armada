import { useCallback, useMemo, useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { deleteSignalsBatch, listSignals, markSignalRead, sendSignal } from '@dashboard/api/client';
import { SIGNAL_TYPES, buildSendSignalRequest, signalListFilters } from '@dashboard/lib/signals';
import type { SendSignalRequest, Signal } from '@dashboard/types/models';
import { EntityStatusBadge } from '../../components/app/EntityStatusBadge';
import { FilterButton, activeFilterCount } from '../../components/app/FilterSheet';
import { JsonSheet } from '../../components/app/JsonSheet';
import { PagedList } from '../../components/app/PagedList';
import { useConfirm } from '../../components/app/useConfirm';
import { BottomSheet, Button, SearchField, SelectField, SwitchRow, TextField } from '../../components/ui';
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

export const SIGNALS_REFRESH_MS = 15000;

/** Pure: the dashboard's text filter over type, sender, recipient, and payload (the Admiral when no captain). */
export function filterSignals(signals: Signal[], search: string, captainLabel: (id: string | null) => string): Signal[] {
  const term = search.trim().toLowerCase();
  if (!term) return signals;
  return signals.filter((s) =>
    (s.type || '').toLowerCase().includes(term) ||
    captainLabel(s.fromCaptainId).toLowerCase().includes(term) ||
    captainLabel(s.toCaptainId).toLowerCase().includes(term) ||
    (s.payload || '').toLowerCase().includes(term));
}

/**
 * Signals (the dashboard's Signals page, an Activity source): server-side type, recipient, unread, and user
 * filters; Send Signal; swipe actions (mark read, JSON, delete); long-press bulk delete.
 */
export function SignalsList({ onSelect, selectedId }: OperationsListProps) {
  const { t, formatRelativeTime } = useLocale();
  const { pushToast } = useNotifications();
  const [confirmElement, ask] = useConfirm('signal-confirm');
  const lookups = useNameLookups({ captains: true });
  const [type, setType] = useState('');
  const [toCaptainId, setToCaptainId] = useState('');
  const [unreadOnly, setUnreadOnly] = useState(false);
  const [userScope, setUserScope] = useState('');
  const [search, setSearch] = useState('');
  const [json, setJson] = useState<Signal | null>(null);
  const [sendOpen, setSendOpen] = useState(false);
  const selection = useSelectionMode();
  const { captains } = lookups;
  const captainLabel = useCallback((id: string | null) => (id ? captains.find((c) => c.id === id)?.name || id : t('Admiral')), [captains, t]);

  const list = usePagedList(
    (pageNumber, pageSize) => listSignals({ pageNumber, pageSize, filters: signalListFilters({ type, toCaptainId, unreadOnly, userId: userScope }) }),
    (s: Signal) => s.id,
    [type, toCaptainId, unreadOnly, userScope],
    25,
    t('Failed to load signals.'),
  );
  const paused = selection.active;
  useLiveRefresh(['captain.', 'mission.'], () => { void list.reload(); }, !paused);
  useInterval(() => { void list.reload(); }, paused ? null : SIGNALS_REFRESH_MS);
  const shown = useMemo(() => filterSignals(list.items, search, captainLabel), [list.items, search, captainLabel]);
  const state = useMemo(() => ({ ...list, items: shown }), [list, shown]);

  const markRead = async (id: string) => {
    try {
      await markSignalRead(id);
      pushToast('success', t('Signal marked as read.'));
      await list.reload();
    } catch (e) {
      pushToast('error', errorMessage(e, t('Failed to mark signal as read.')));
    }
  };
  const deleteIds = (ids: string[], message: string, success: string) => ask({
    title: t('Delete'),
    message,
    confirmLabel: t('Delete'),
    danger: true,
    onConfirm: async () => {
      try {
        await deleteSignalsBatch(ids);
        selection.clear();
        pushToast('warning', success);
        await list.reload();
      } catch (e) {
        pushToast('error', errorMessage(e, ids.length > 1 ? t('Bulk delete failed.') : t('Delete failed.')));
      }
    },
  });
  const filterCount = activeFilterCount({ type, toCaptainId, unread: unreadOnly ? 'yes' : '', userScope });

  const header = (
    <View style={styles.header}>
      {selection.active ? (
        <SelectionBar
          count={selection.selected.length}
          onDelete={() => deleteIds([...selection.selected], t('Delete {{count}} selected signal(s)?', { count: selection.selected.length }), t('Deleted {{count}} signal(s).', { count: selection.selected.length }))}
          onSelectAll={() => selection.selectAll(shown.map((s) => s.id))}
          onCancel={selection.clear}
          testID="signal-selection"
        />
      ) : null}
      <SearchField value={search} onChangeText={setSearch} placeholder={t('Search signals')} clearLabel={t('Clear')} testID="signal-search" />
      <View style={styles.row}>
        <FilterButton
          count={filterCount}
          onClear={() => { setType(''); setToCaptainId(''); setUnreadOnly(false); setUserScope(''); }}
          testID="signal-filters"
        >
          <SelectField label={t('Type')} value={type} onChange={setType} allowEmpty placeholder={t('All Types')} closeLabel={t('Close')}
            options={SIGNAL_TYPES.map((s) => ({ value: s, label: t(s) }))} testID="signal-filter-type" />
          <SelectField label={t('To')} value={toCaptainId} onChange={setToCaptainId} allowEmpty placeholder={t('All Captains')} closeLabel={t('Close')}
            options={lookups.captains.map((c) => ({ value: c.id, label: c.name || c.id }))} testID="signal-filter-captain" />
          <SwitchRow label={t('Unread Only')} value={unreadOnly} onChange={setUnreadOnly} testID="signal-filter-unread" />
          <UserScopeSelect value={userScope} onChange={setUserScope} testID="signal-filter-user" />
        </FilterButton>
        <Button label={t('Signal')} icon="add" onPress={() => setSendOpen(true)} style={styles.flex} testID="signal-send" />
      </View>
    </View>
  );

  return (
    <View style={styles.fill} testID="signals-list">
      <PagedList
        state={state}
        header={header}
        keyExtractor={(s) => s.id}
        emptyTitle={t('No signals found.')}
        loadingLabel={t('Loading...')}
        testID="signals"
        renderItem={({ item }) => (
          <SelectableRow
            id={item.id}
            testID={`signal-row-${item.id}`}
            title={item.payload || item.id}
            subtitle={[`${captainLabel(item.fromCaptainId)} -> ${captainLabel(item.toCaptainId)}`, item.read ? t('Read') : t('Unread'), formatRelativeTime(item.createdUtc)].join(' - ')}
            accessory={<EntityStatusBadge status={item.type} />}
            selecting={selection.active}
            checked={selection.selected.includes(item.id)}
            highlighted={selectedId === item.id}
            onOpen={onSelect}
            onToggle={selection.toggle}
            onStartSelect={selection.start}
            actions={[
              ...(!item.read ? [{ key: 'read', label: t('Mark Read'), icon: 'mail-open-outline' as const, onPress: () => void markRead(item.id) }] : []),
              { key: 'json', label: t('JSON'), icon: 'code-outline', onPress: () => setJson(item) },
              { key: 'delete', label: t('Delete'), icon: 'trash-outline', tone: 'danger', onPress: () => deleteIds([item.id], t('Delete signal {{id}}?', { id: item.id }), t('Signal {{id}} deleted.', { id: item.id })) },
            ]}
          />
        )}
      />
      {confirmElement}
      <JsonSheet open={json !== null} title={json ? `${t('Signal')}: ${json.id}` : ''} data={json} onClose={() => setJson(null)} />
      <SendSignalSheet
        open={sendOpen}
        captains={lookups.captains.map((c) => ({ value: c.id, label: c.name || c.id }))}
        onClose={() => setSendOpen(false)}
        onSubmit={async (form) => {
          try {
            await sendSignal(buildSendSignalRequest(form));
            setSendOpen(false);
            pushToast('success', t('Signal sent.'));
            await list.reload();
          } catch (e) {
            pushToast('error', errorMessage(e, t('Failed to send signal.')));
          }
        }}
      />
    </View>
  );
}

/** The dashboard's Send Signal modal: type, payload, recipient (the Admiral broadcast when none). */
export function SendSignalSheet({ open, captains, onClose, onSubmit }: {
  open: boolean;
  captains: { value: string; label: string }[];
  onClose: () => void;
  onSubmit: (form: SendSignalRequest) => Promise<void>;
}) {
  const { t } = useLocale();
  return (
    <BottomSheet open={open} title={t('Send Signal')} onClose={onClose} closeLabel={t('Close')} testID="send-signal-sheet">
      <SendSignalForm captains={captains} onSubmit={onSubmit} />
    </BottomSheet>
  );
}

function SendSignalForm({ captains, onSubmit }: { captains: { value: string; label: string }[]; onSubmit: (form: SendSignalRequest) => Promise<void> }) {
  const { t } = useLocale();
  const [form, setForm] = useState<SendSignalRequest>({ type: 'Nudge', payload: '', toCaptainId: '' });
  const [busy, setBusy] = useState(false);
  return (
    <>
      <SelectField label={t('Type')} value={form.type} onChange={(type) => setForm((f) => ({ ...f, type }))} placeholder={t('Type')} closeLabel={t('Close')}
        options={SIGNAL_TYPES.map((s) => ({ value: s, label: t(s) }))} testID="send-signal-type" />
      <TextField label={t('Payload')} value={form.payload ?? ''} onChangeText={(payload) => setForm((f) => ({ ...f, payload }))} multiline numberOfLines={4} testID="send-signal-payload" />
      <SelectField label={t('To Captain (optional)')} value={form.toCaptainId ?? ''} onChange={(toCaptainId) => setForm((f) => ({ ...f, toCaptainId }))}
        allowEmpty placeholder={t('Admiral (broadcast)')} closeLabel={t('Close')} options={captains} testID="send-signal-to" />
      <Button label={busy ? t('Sending...') : t('Send')} busy={busy} onPress={async () => { setBusy(true); try { await onSubmit(form); } finally { setBusy(false); } }} testID="send-signal-submit" />
    </>
  );
}

const styles = StyleSheet.create({
  fill: { flex: 1 },
  header: { paddingTop: spacing.sm },
  row: { flexDirection: 'row', gap: spacing.sm, paddingHorizontal: spacing.md },
  flex: { flex: 1 },
});
