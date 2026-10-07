import type { ReactElement } from 'react';
import { ActivityIndicator, FlatList, RefreshControl, StyleSheet, View, type ListRenderItem } from 'react-native';
import type { PagedListState } from '../../data/usePagedList';
import { useLocale } from '../../i18n/LocaleContext';
import { useTheme } from '../../theme/ThemeContext';
import { spacing } from '../../theme/typography';
import { AppText, EmptyState, ErrorState, LoadingState } from '../ui';

export interface PagedListProps<T> {
  state: PagedListState<T>;
  renderItem: ListRenderItem<T>;
  keyExtractor: (item: T) => string;
  header?: ReactElement | null;
  emptyTitle: string;
  emptyMessage?: string;
  loadingLabel: string;
  testID?: string;
}

/**
 * A virtualized endless-scroll list with pull to refresh, the shared loading/empty/error states, and a record
 * count footer: the mobile form of the dashboard's paginated tables.
 */
export function PagedList<T>({ state, renderItem, keyExtractor, header, emptyTitle, emptyMessage, loadingLabel, testID }: PagedListProps<T>) {
  const { t } = useLocale();
  const { colors } = useTheme();
  const empty = state.loading
    ? <LoadingState label={loadingLabel} />
    : state.error
      ? <ErrorState title={t('Something went wrong')} message={state.error} retryLabel={t('Retry')} onRetry={() => void state.refresh()} />
      : <EmptyState title={emptyTitle} message={emptyMessage} />;
  return (
    <FlatList
      testID={testID}
      data={state.items}
      renderItem={renderItem}
      keyExtractor={keyExtractor}
      ListHeaderComponent={header}
      ListEmptyComponent={empty}
      contentContainerStyle={styles.content}
      onEndReachedThreshold={0.5}
      onEndReached={() => { if (state.hasMore) void state.loadMore(); }}
      refreshControl={<RefreshControl refreshing={state.refreshing} onRefresh={() => void state.refresh()} tintColor={colors.primary} />}
      ListFooterComponent={state.items.length > 0 ? (
        <View style={styles.footer}>
          {state.loadingMore ? <ActivityIndicator color={colors.primary} /> : null}
          <AppText variant="caption" muted testID={testID ? `${testID}-count` : undefined}>
            {t('Showing {{shown}} of {{total}}', { shown: state.items.length, total: state.totalRecords })}
          </AppText>
        </View>
      ) : null}
      keyboardShouldPersistTaps="handled"
    />
  );
}

const styles = StyleSheet.create({
  content: { flexGrow: 1, paddingBottom: spacing.xl },
  footer: { alignItems: 'center', gap: spacing.sm, padding: spacing.lg },
});
