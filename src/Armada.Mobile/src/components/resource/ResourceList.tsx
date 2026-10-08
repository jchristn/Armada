import { useMemo, useState, type ReactElement, type ReactNode } from 'react';
import { ActivityIndicator, FlatList, RefreshControl, StyleSheet, View } from 'react-native';
import { useLocale } from '../../i18n/LocaleContext';
import { useTheme } from '../../theme/ThemeContext';
import { spacing } from '../../theme/typography';
import { AppText } from '../ui/AppText';
import { Banner } from '../ui/Banner';
import { BottomSheet } from '../ui/BottomSheet';
import { Button } from '../ui/Button';
import { SearchField } from '../ui/SearchField';
import { SelectField, type SelectOption } from '../ui/SelectSheet';
import { EmptyState, ErrorState, LoadingState } from '../ui/States';

/** One filter of a list (the dashboard's filter-row selects), shown in the Filters sheet. */
export interface ListFilter {
  key: string;
  label: string;
  value: string;
  /** The option whose value means "no filter" (for example 'all'); counts toward the active-filter badge otherwise. */
  allValue?: string;
  options: SelectOption<string>[];
  onChange: (value: string) => void;
}

export interface ResourceListProps<T> {
  items: T[];
  keyOf: (item: T) => string;
  renderItem: (item: T, index: number) => ReactElement;
  /** Free-text search above the list. */
  search?: { value: string; onChange: (value: string) => void; placeholder: string };
  filters?: ListFilter[];
  /** Above the search: summary cards, banners, a create button. */
  header?: ReactNode;
  loading: boolean;
  error?: string | null;
  onRetry?: () => void;
  refreshing?: boolean;
  onRefresh?: () => void;
  emptyTitle: string;
  emptyMessage?: string;
  /**
   * Client-side paging: rows are rendered this many at a time and more are added as the list nears its end (the
   * dashboard pages through the same rows with a pager). Server-paged lists pass `onEndReached` instead.
   */
  pageSize?: number;
  /** Server paging: called near the end of the list while `hasMore`. */
  onEndReached?: () => void;
  hasMore?: boolean;
  loadingMore?: boolean;
  /** Below the rows, for example "Showing 25 of 140". */
  footer?: ReactNode;
  testID?: string;
}

/**
 * The mobile form of a dashboard table page: a virtualized list with pull to refresh, search, filters in a bottom
 * sheet, incremental paging, and the loading / error / empty states. Rows are rendered by the caller (usually a
 * ListRow inside a SwipeRow for the row actions).
 */
export function ResourceList<T>({
  items, keyOf, renderItem, search, filters = [], header, loading, error, onRetry, refreshing = false, onRefresh,
  emptyTitle, emptyMessage, pageSize = 50, onEndReached, hasMore, loadingMore, footer, testID,
}: ResourceListProps<T>) {
  const { t } = useLocale();
  const { colors } = useTheme();
  const [filtersOpen, setFiltersOpen] = useState(false);
  const [paging, setPaging] = useState({ key: '', visible: pageSize });
  const serverPaged = !!onEndReached;

  const activeFilters = filters.filter((f) => f.value !== (f.allValue ?? 'all') && f.value !== '').length;
  // A new search or filter starts again from the first page.
  const resetKey = `${search?.value ?? ''}|${filters.map((f) => f.value).join('|')}`;
  const visible = paging.key === resetKey ? paging.visible : pageSize;

  const shown = useMemo(() => (serverPaged ? items : items.slice(0, visible)), [items, visible, serverPaged]);

  const listHeader = (
    <View style={styles.header}>
      {header}
      {search || filters.length > 0 ? (
        <View style={styles.searchRow}>
          {search ? (
            <View style={styles.flex}>
              <SearchField value={search.value} onChangeText={search.onChange} placeholder={search.placeholder} clearLabel={t('Clear')} testID={testID ? `${testID}-search` : undefined} />
            </View>
          ) : null}
          {filters.length > 0 ? (
            <Button
              label={activeFilters > 0 ? `${t('Filters')} (${activeFilters})` : t('Filters')}
              icon="funnel-outline"
              variant="secondary"
              onPress={() => setFiltersOpen(true)}
              style={styles.filterButton}
              testID={testID ? `${testID}-filters` : undefined}
            />
          ) : null}
        </View>
      ) : null}
      {error && items.length > 0 ? <Banner tone="danger" title={error} /> : null}
    </View>
  );

  let empty: ReactElement | null = null;
  if (loading && items.length === 0) empty = <LoadingState label={t('Loading...')} />;
  else if (error && items.length === 0) empty = <ErrorState title={t('Something went wrong')} message={error} retryLabel={t('Retry')} onRetry={onRetry} />;
  else empty = <EmptyState title={emptyTitle} message={emptyMessage} />;

  const more = !serverPaged && visible < items.length;
  const listFooter = (
    <View style={styles.footer}>
      {loadingMore ? <ActivityIndicator color={colors.primary} /> : null}
      {more ? (
        <AppText variant="caption" muted style={styles.center}>{t('Showing {{shown}} of {{total}}', { shown: shown.length, total: items.length })}</AppText>
      ) : null}
      {footer}
    </View>
  );

  return (
    <View style={styles.flex} testID={testID}>
      <FlatList
        data={shown}
        keyExtractor={keyOf}
        renderItem={({ item, index }) => renderItem(item, index)}
        ListHeaderComponent={listHeader}
        ListEmptyComponent={empty}
        ListFooterComponent={listFooter}
        contentContainerStyle={styles.content}
        keyboardShouldPersistTaps="handled"
        onEndReachedThreshold={0.4}
        onEndReached={() => {
          if (serverPaged) { if (hasMore && !loadingMore) onEndReached?.(); }
          else if (more) setPaging({ key: resetKey, visible: visible + pageSize });
        }}
        refreshControl={onRefresh ? <RefreshControl refreshing={refreshing} onRefresh={onRefresh} tintColor={colors.primary} /> : undefined}
        initialNumToRender={20}
        windowSize={11}
      />
      {filters.length > 0 ? (
        <BottomSheet open={filtersOpen} title={t('Filters')} onClose={() => setFiltersOpen(false)} closeLabel={t('Close')} testID={testID ? `${testID}-filter-sheet` : undefined}>
          {filters.map((f) => (
            <SelectField
              key={f.key}
              label={f.label}
              value={f.value}
              options={f.options}
              onChange={f.onChange}
              closeLabel={t('Close')}
              testID={testID ? `${testID}-filter-${f.key}` : undefined}
            />
          ))}
          <Button
            label={t('Clear filters')}
            variant="ghost"
            disabled={activeFilters === 0}
            onPress={() => filters.forEach((f) => f.onChange(f.allValue ?? 'all'))}
          />
        </BottomSheet>
      ) : null}
    </View>
  );
}

/** Summary cards above a list (the dashboard's overview grid: totals, active, failed, ...). */
export function StatRow({ stats }: { stats: { label: string; value: string | number; tone?: 'danger' | 'warning' | 'success' }[] }) {
  const { colors } = useTheme();
  return (
    <View style={styles.stats}>
      {stats.map((s) => (
        <View key={s.label} style={[styles.stat, { backgroundColor: colors.surface, borderColor: colors.border }]} accessible accessibilityLabel={`${s.label}: ${s.value}`}>
          <AppText variant="caption" muted numberOfLines={1}>{s.label}</AppText>
          <AppText variant="heading" color={s.tone}>{String(s.value)}</AppText>
        </View>
      ))}
    </View>
  );
}

const styles = StyleSheet.create({
  flex: { flex: 1 },
  content: { paddingBottom: spacing.xxl, flexGrow: 1 },
  header: { paddingTop: spacing.md, gap: spacing.sm },
  searchRow: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm, marginHorizontal: spacing.md, marginBottom: spacing.sm },
  filterButton: { marginBottom: 0 },
  footer: { padding: spacing.lg, gap: spacing.sm },
  center: { textAlign: 'center' },
  stats: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.sm, marginHorizontal: spacing.md, marginBottom: spacing.sm },
  stat: { flexGrow: 1, minWidth: 140, borderWidth: StyleSheet.hairlineWidth, borderRadius: 10, padding: spacing.md },
});
