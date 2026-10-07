import { useMemo, useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { deleteEventsBatch, listEvents } from '@dashboard/api/client';
import type { ArmadaEvent } from '@dashboard/types/models';
import { FilterButton, activeFilterCount } from '../../components/app/FilterSheet';
import { JsonSheet } from '../../components/app/JsonSheet';
import { PagedList } from '../../components/app/PagedList';
import { useConfirm } from '../../components/app/useConfirm';
import { SearchField, TextField } from '../../components/ui';
import { errorMessage } from '../../data/errors';
import { useInterval } from '../../data/useInterval';
import { useLiveRefresh } from '../../data/useLiveRefresh';
import { usePagedList } from '../../data/usePagedList';
import { useLocale } from '../../i18n/LocaleContext';
import { useNotifications } from '../../notifications/NotificationContext';
import { spacing } from '../../theme/typography';
import type { OperationsListProps } from './listTypes';
import { SelectableRow } from './w24/SelectableRow';
import { SelectionBar } from './w24/SelectionBar';
import { UserScopeSelect } from '../../components/app/UserScopeSelect';
import { useSelectionMode } from './w24/useSelectionMode';

export const EVENTS_REFRESH_MS = 15000;
/** The dashboard lists events 50 to a page. */
export const EVENTS_PAGE_SIZE = 50;

export interface EventColumnFilters {
  eventType: string;
  entityType: string;
  message: string;
}

/** Pure: the dashboard's column filters (event type, entity type, message text). */
export function filterEvents(events: ArmadaEvent[], filters: EventColumnFilters): ArmadaEvent[] {
  const has = (value: string | null | undefined, term: string) => !term || (value ?? '').toLowerCase().includes(term.toLowerCase());
  return events.filter((e) => has(e.eventType, filters.eventType) && has(e.entityType, filters.entityType) && has(e.message, filters.message));
}

/**
 * Events (the dashboard's Events page, an Activity source): the system event log with the user scope and column
 * filters, swipe actions (JSON, delete), and long-press bulk delete. Live: any socket event reloads it.
 */
export function EventsList({ onSelect, selectedId }: OperationsListProps) {
  const { t, formatRelativeTime } = useLocale();
  const { pushToast } = useNotifications();
  const [confirmElement, ask] = useConfirm('event-confirm');
  const [userScope, setUserScope] = useState('');
  const [filters, setFilters] = useState<EventColumnFilters>({ eventType: '', entityType: '', message: '' });
  const [json, setJson] = useState<ArmadaEvent | null>(null);
  const selection = useSelectionMode();

  const list = usePagedList(
    (pageNumber, pageSize) => listEvents({ pageNumber, pageSize, filters: userScope ? { userId: userScope } : undefined }),
    (e: ArmadaEvent) => e.id,
    [userScope],
    EVENTS_PAGE_SIZE,
    t('Failed to load events.'),
  );
  const paused = selection.active;
  useLiveRefresh([], () => { void list.reload(); }, !paused);
  useInterval(() => { void list.reload(); }, paused ? null : EVENTS_REFRESH_MS);
  const shown = useMemo(() => filterEvents(list.items, filters), [list.items, filters]);
  const state = useMemo(() => ({ ...list, items: shown }), [list, shown]);

  const deleteIds = (ids: string[], title: string, message: string, success: string) => ask({
    title,
    message,
    confirmLabel: t('Delete'),
    danger: true,
    onConfirm: async () => {
      try {
        await deleteEventsBatch(ids);
        selection.clear();
        pushToast('warning', success);
        await list.reload();
      } catch (e) {
        pushToast('error', errorMessage(e, ids.length > 1 ? t('Bulk delete failed.') : t('Delete failed.')));
      }
    },
  });

  const header = (
    <View style={styles.header}>
      {selection.active ? (
        <SelectionBar
          count={selection.selected.length}
          onDelete={() => deleteIds([...selection.selected], t('Delete Selected Events'),
            t('Delete {{count}} selected event(s)?', { count: selection.selected.length }), t('Deleted {{count}} event(s).', { count: selection.selected.length }))}
          onSelectAll={() => selection.selectAll(shown.map((e) => e.id))}
          onCancel={selection.clear}
          testID="event-selection"
        />
      ) : null}
      <SearchField value={filters.message} onChangeText={(message) => setFilters((f) => ({ ...f, message }))} placeholder={t('Search messages')} clearLabel={t('Clear')} testID="event-search" />
      <View style={styles.row}>
        <FilterButton
          count={activeFilterCount({ eventType: filters.eventType, entityType: filters.entityType, userScope })}
          onClear={() => { setFilters((f) => ({ ...f, eventType: '', entityType: '' })); setUserScope(''); }}
          testID="event-filters"
        >
          <TextField label={t('Event Type')} value={filters.eventType} onChangeText={(eventType) => setFilters((f) => ({ ...f, eventType }))} autoCapitalize="none" testID="event-filter-type" />
          <TextField label={t('Entity Type')} value={filters.entityType} onChangeText={(entityType) => setFilters((f) => ({ ...f, entityType }))} autoCapitalize="none" testID="event-filter-entity" />
          <UserScopeSelect value={userScope} onChange={setUserScope} testID="event-filter-user" />
        </FilterButton>
      </View>
    </View>
  );

  return (
    <View style={styles.fill} testID="events-list">
      <PagedList
        state={state}
        header={header}
        keyExtractor={(e) => e.id}
        emptyTitle={list.items.length > 0 ? t('No events match the current filters.') : t('No events found.')}
        loadingLabel={t('Loading...')}
        testID="events"
        renderItem={({ item }) => (
          <SelectableRow
            id={item.id}
            testID={`event-row-${item.id}`}
            title={item.eventType}
            subtitle={[item.message, item.entityType ? `${item.entityType} ${item.entityId ?? ''}`.trim() : null, formatRelativeTime(item.createdUtc)].filter(Boolean).join(' - ')}
            selecting={selection.active}
            checked={selection.selected.includes(item.id)}
            highlighted={selectedId === item.id}
            onOpen={onSelect}
            onToggle={selection.toggle}
            onStartSelect={selection.start}
            actions={[
              { key: 'json', label: t('JSON'), icon: 'code-outline', onPress: () => setJson(item) },
              { key: 'delete', label: t('Delete'), icon: 'trash-outline', tone: 'danger', onPress: () => deleteIds([item.id], t('Delete Event'), t('Delete event {{id}}?', { id: item.id }), t('Event {{id}} deleted.', { id: item.id })) },
            ]}
          />
        )}
      />
      {confirmElement}
      <JsonSheet open={json !== null} title={json ? `${t('Event')}: ${json.id}` : ''} data={json} onClose={() => setJson(null)} />
    </View>
  );
}

const styles = StyleSheet.create({
  fill: { flex: 1 },
  header: { paddingTop: spacing.sm },
  row: { flexDirection: 'row', gap: spacing.sm, paddingHorizontal: spacing.md },
});
