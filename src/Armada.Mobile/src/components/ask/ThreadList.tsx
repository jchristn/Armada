import { useState } from 'react';
import { FlatList, Pressable, StyleSheet, Switch, View } from 'react-native';
import type { AskThread } from '@dashboard/types/models';
import { isThreadReplying, isThreadWorking, type ThreadActivityMap } from '@dashboard/lib/askThreads';
import { useLocale } from '../../i18n/LocaleContext';
import { useTheme } from '../../theme/ThemeContext';
import { MIN_TOUCH, radius, spacing } from '../../theme/typography';
import { AppText } from '../ui/AppText';
import { Button } from '../ui/Button';
import { Icon } from '../ui/Icon';
import { SearchField } from '../ui/SearchField';
import { SwipeRow } from '../ui/SwipeRow';
import { DeleteConversationDialog } from './DeleteConversationDialog';
import { ThreadActionsSheet, type ThreadActions } from './ThreadActionsSheet';

export interface ThreadListProps extends Omit<ThreadActions, 'onDelete'> {
  /** Deletes a conversation the user has confirmed (the list asks first, in its own dialog). */
  onDelete: (thread: AskThread) => void;
  threads: AskThread[];
  selectedId: string | null;
  activity: ThreadActivityMap;
  loading: boolean;
  error: string | null;
  search: string;
  onSearchChange: (value: string) => void;
  includeArchived: boolean;
  onIncludeArchivedChange: (value: boolean) => void;
  hasMore: boolean;
  onLoadMore: () => void;
  onRetry: () => void;
  onSelect: (thread: AskThread) => void;
  onNew: () => void;
}

/**
 * The conversation list (the dashboard's AskThreadList): server-side search, New conversation, pinned first,
 * unread badges, a live "working" marker while tracked work runs, "Replying..." during a captain turn. Swipe a row
 * for Pin / Archive / Delete, or long-press it (or use the screen-reader actions) for every action. Delete asks for
 * confirmation in a dialog the list owns, so the dialog is presented from wherever the list is (the phone list is a
 * modal of its own).
 */
export function ThreadList(props: ThreadListProps) {
  const {
    threads, selectedId, activity, loading, error, search, onSearchChange, includeArchived, onIncludeArchivedChange,
    hasMore, onLoadMore, onRetry, onSelect, onNew, onDelete, ...rest
  } = props;
  const { t, formatRelativeTime } = useLocale();
  const { colors } = useTheme();
  const [menuThread, setMenuThread] = useState<AskThread | null>(null);
  const [deleteTarget, setDeleteTarget] = useState<AskThread | null>(null);
  const actions: ThreadActions = { ...rest, onDelete: setDeleteTarget };

  const header = (
    <View style={styles.top}>
      <Button label={`+ ${t('New conversation')}`} onPress={onNew} testID="ask-new-conversation" />
      <SearchField value={search} onChangeText={onSearchChange} placeholder={t('Search conversations')} clearLabel={t('Clear')} testID="ask-thread-search" />
      <View style={styles.archived}>
        <Switch
          value={includeArchived}
          onValueChange={onIncludeArchivedChange}
          accessibilityLabel={t('Show archived')}
          trackColor={{ true: colors.primary, false: colors.control }}
          testID="ask-show-archived"
        />
        <AppText variant="caption" muted>{t('Show archived')}</AppText>
      </View>
      {error ? (
        <View style={[styles.error, { borderColor: colors.danger }]} accessibilityRole="alert">
          <AppText color="danger" style={styles.flex}>{error}</AppText>
          <Button label={t('Retry')} variant="secondary" onPress={onRetry} style={styles.noMargin} />
        </View>
      ) : null}
    </View>
  );

  const empty = !error && !loading ? (
    <AppText muted style={styles.empty} testID="ask-threads-empty">
      {search.trim() ? t('No conversations match your search.') : t('No conversations yet. Start one to ask a question or kick off work.')}
    </AppText>
  ) : null;

  const footer = loading ? (
    <AppText muted style={styles.empty} accessibilityLiveRegion="polite">{t('Loading conversations...')}</AppText>
  ) : hasMore ? (
    <Button label={t('Load more')} variant="ghost" onPress={onLoadMore} />
  ) : null;

  return (
    <View style={[styles.fill, { backgroundColor: colors.background }]} testID="ask-thread-list" accessibilityLabel={t('Conversations')}>
      <FlatList
        data={threads}
        keyExtractor={(th) => th.id}
        ListHeaderComponent={header}
        ListEmptyComponent={empty}
        ListFooterComponent={footer}
        keyboardShouldPersistTaps="handled"
        renderItem={({ item: thread }) => {
          const working = isThreadWorking(thread, activity);
          const replying = isThreadReplying(thread, activity);
          const unread = thread.id === selectedId ? 0 : (thread.unreadCount ?? 0);
          const selected = thread.id === selectedId;
          const title = thread.title || t('New conversation');
          const status = working ? t('Working') : replying ? t('Replying...') : '';
          const when = thread.lastMessageUtc ? formatRelativeTime(thread.lastMessageUtc) : '';
          const spoken = [title, thread.pinned ? t('Pinned') : '', unread > 0 ? t('{{count}} unread', { count: unread }) : '', status, thread.archived ? t('Archived') : '', when]
            .filter(Boolean).join(', ');
          return (
            <SwipeRow
              testID={`ask-thread-${thread.id}`}
              actions={[
                { key: 'pin', label: thread.pinned ? t('Unpin') : t('Pin'), icon: 'pin-outline', onPress: () => actions.onTogglePin(thread) },
                { key: 'archive', label: thread.archived ? t('Unarchive') : t('Archive'), icon: 'archive-outline', onPress: () => actions.onToggleArchive(thread) },
                { key: 'delete', label: t('Delete'), icon: 'trash-outline', tone: 'danger', onPress: () => actions.onDelete(thread) },
              ]}
            >
              <Pressable
                accessibilityRole="button"
                accessibilityLabel={spoken}
                accessibilityHint={t('More conversation actions')}
                accessibilityState={{ selected }}
                onPress={() => onSelect(thread)}
                onLongPress={() => setMenuThread(thread)}
                testID={`ask-thread-row-${thread.id}`}
                style={({ pressed }) => [
                  styles.row,
                  { borderBottomColor: colors.border, backgroundColor: selected ? colors.surfaceRaised : pressed ? colors.background : colors.surface },
                  selected ? { borderLeftWidth: 3, borderLeftColor: colors.primary } : null,
                ]}
              >
                <View style={styles.rowTop}>
                  {thread.pinned ? <Icon name="pin" size={14} color="textMuted" /> : null}
                  <AppText variant="label" numberOfLines={1} style={[styles.flex, unread > 0 ? styles.bold : null]}>{title}</AppText>
                  {unread > 0 ? (
                    <View style={[styles.badge, { backgroundColor: colors.badge }]}>
                      <AppText variant="caption" style={{ color: colors.badgeText }}>{unread > 99 ? '99+' : unread}</AppText>
                    </View>
                  ) : null}
                </View>
                <View style={styles.rowBottom}>
                  {working ? <View style={[styles.dot, { backgroundColor: colors.info }]} /> : null}
                  {status ? <AppText variant="caption" color="info">{status}</AppText> : null}
                  {thread.archived ? <AppText variant="caption" muted>{t('Archived')}</AppText> : null}
                  <AppText variant="caption" muted style={styles.time}>{when}</AppText>
                </View>
              </Pressable>
            </SwipeRow>
          );
        }}
      />
      <ThreadActionsSheet thread={menuThread} onClose={() => setMenuThread(null)} actions={actions} />
      <DeleteConversationDialog
        target={deleteTarget}
        onCancel={() => setDeleteTarget(null)}
        onConfirm={(target) => { setDeleteTarget(null); onDelete(target); }}
      />
    </View>
  );
}

const styles = StyleSheet.create({
  fill: { flex: 1 },
  flex: { flex: 1 },
  bold: { fontWeight: '700' },
  noMargin: { marginBottom: 0 },
  top: { paddingHorizontal: spacing.md, paddingTop: spacing.md, gap: spacing.xs },
  archived: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm, marginBottom: spacing.sm, paddingHorizontal: spacing.xs },
  error: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm, borderWidth: 1, borderRadius: radius.md, padding: spacing.sm, marginBottom: spacing.sm },
  empty: { padding: spacing.lg, textAlign: 'center' },
  row: { minHeight: MIN_TOUCH + 12, paddingHorizontal: spacing.lg, paddingVertical: spacing.sm, borderBottomWidth: StyleSheet.hairlineWidth, gap: 2 },
  rowTop: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm },
  rowBottom: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm },
  time: { marginLeft: 'auto' },
  badge: { minWidth: 22, borderRadius: radius.pill, paddingHorizontal: 6, alignItems: 'center' },
  dot: { width: 8, height: 8, borderRadius: 4 },
});
