import type { ReactElement, ReactNode } from 'react';
import { ActivityIndicator, FlatList, RefreshControl, StyleSheet, View, type ListRenderItem } from 'react-native';
import { AppText, Banner, EmptyState, ErrorState, IconButton, LoadingState, SearchField } from '../components/ui';
import type { IconName } from '../components/ui';
import { useLocale } from '../i18n/LocaleContext';
import { useTheme } from '../theme/ThemeContext';
import { spacing } from '../theme/typography';

export interface ListPaneAction {
  key: string;
  icon: IconName;
  label: string;
  onPress: () => void;
  /** Count shown on the icon (active filters). */
  badge?: number;
}

export interface ListPaneProps<T> {
  items: T[];
  keyOf: (item: T) => string;
  renderItem: ListRenderItem<T>;
  loading: boolean;
  refreshing: boolean;
  error: string | null;
  onRefresh: () => void;
  /** Endless scroll: called near the end of the list. */
  onEndReached?: () => void;
  loadingMore?: boolean;
  /** Free-text search box above the list (omit for none). */
  search?: { value: string; onChange: (value: string) => void; placeholder: string };
  /** Icon buttons in the toolbar row (filters, new, select). */
  actions?: ListPaneAction[];
  /** Count line ("12 vessels"). */
  summary?: string | null;
  /** Shown above the rows (bulk-selection bar, chips). */
  header?: ReactNode;
  emptyTitle: string;
  emptyMessage?: string;
  emptyAction?: { label: string; onPress: () => void };
  testID?: string;
}

/**
 * A virtualized list screen body: search and toolbar on top, pull to refresh, endless scroll, and the shared empty,
 * loading, and error states. An error after data loaded shows as a banner over the stale rows instead of hiding them.
 */
export function ListPane<T>(props: ListPaneProps<T>): ReactElement {
  const { t } = useLocale();
  const { colors } = useTheme();
  const { items, keyOf, renderItem, loading, refreshing, error, onRefresh, onEndReached, loadingMore, search, actions, summary, header, testID } = props;

  const top = (
    <View>
      {search || (actions && actions.length > 0) ? (
        <View style={styles.toolbar}>
          {search ? (
            <View style={styles.flex}>
              <SearchField value={search.value} onChangeText={search.onChange} placeholder={search.placeholder} clearLabel={t('Clear')} testID={testID ? `${testID}-search` : undefined} />
            </View>
          ) : <View style={styles.flex} />}
          {actions?.map((a) => (
            <IconButton key={a.key} icon={a.icon} label={a.label} onPress={a.onPress} badge={a.badge} testID={testID ? `${testID}-${a.key}` : undefined} />
          ))}
        </View>
      ) : null}
      {error && items.length > 0 ? <Banner tone="danger" title={error} /> : null}
      {summary ? <AppText variant="caption" muted style={styles.summary}>{summary}</AppText> : null}
      {header}
    </View>
  );

  let empty: ReactElement | null = null;
  if (loading) empty = <LoadingState label={t('Loading...')} />;
  else if (error) empty = <ErrorState title={t('Could not load')} message={error} retryLabel={t('Retry')} onRetry={onRefresh} />;
  else empty = <EmptyState title={props.emptyTitle} message={props.emptyMessage} actionLabel={props.emptyAction?.label} onAction={props.emptyAction?.onPress} />;

  return (
    <FlatList
      testID={testID}
      style={{ backgroundColor: colors.background }}
      data={loading ? [] : items}
      keyExtractor={keyOf}
      renderItem={renderItem}
      ListHeaderComponent={top}
      ListEmptyComponent={empty}
      ListFooterComponent={loadingMore ? <ActivityIndicator style={styles.more} color={colors.primary} accessibilityLabel={t('Loading...')} /> : null}
      contentContainerStyle={styles.content}
      keyboardShouldPersistTaps="handled"
      onEndReached={onEndReached}
      onEndReachedThreshold={0.5}
      refreshControl={<RefreshControl refreshing={refreshing} onRefresh={onRefresh} tintColor={colors.primary} />}
    />
  );
}

const styles = StyleSheet.create({
  flex: { flex: 1 },
  content: { paddingTop: spacing.md, paddingBottom: spacing.xxl, flexGrow: 1 },
  toolbar: { flexDirection: 'row', alignItems: 'flex-start', paddingRight: spacing.sm },
  summary: { marginHorizontal: spacing.lg, marginBottom: spacing.sm },
  more: { margin: spacing.lg },
});
