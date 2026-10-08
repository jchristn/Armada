import { useRouter, type Href } from 'expo-router';
import { useState } from 'react';
import { FlatList, StyleSheet, View } from 'react-native';
import { AppText, Button, ConfirmDialog, EmptyState, ListRow, SegmentedControl, SplitView, StatusBadge, SwipeRow } from '../components/ui';
import { useLocale } from '../i18n/LocaleContext';
import { useLayout } from '../navigation/useLayout';
import { notificationHref, useNotifications, type Notification } from '../notifications/NotificationContext';
import { useTheme } from '../theme/ThemeContext';
import { spacing } from '../theme/typography';

/**
 * The in-app notification history (the dashboard's notification menu): newest first, unread marked, swipe to mark
 * read, tap to open the item. On tablets the selected notification shows beside the list.
 */
export function NotificationCenterScreen() {
  const { notifications, markRead, markAllRead, clearHistory } = useNotifications();
  const { t, formatRelativeTime, formatDateTime } = useLocale();
  const { colors } = useTheme();
  // List and detail side by side (the pane's width, not the device).
  const { split: isTablet } = useLayout();
  const router = useRouter();
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [confirmClear, setConfirmClear] = useState(false);
  const [filter, setFilter] = useState<'all' | 'unread'>('all');
  const selected = notifications.find((n) => n.id === selectedId) ?? null;
  const shown = filter === 'unread' ? notifications.filter((n) => !n.read) : notifications;

  function open(n: Notification) {
    markRead(n.id);
    const href = notificationHref(n);
    if (isTablet) {
      setSelectedId(n.id);
    } else if (href) {
      router.dismissTo(href as Href);
    } else {
      setSelectedId(n.id);
    }
  }

  const list = (
    <View style={[styles.fill, { backgroundColor: colors.background }]}>
      <View style={styles.toolbar}>
        <Button label={t('Mark all read')} variant="ghost" onPress={markAllRead} disabled={notifications.every((n) => n.read)} testID="notifications-mark-all" />
        <Button label={t('Clear')} variant="ghost" onPress={() => setConfirmClear(true)} disabled={notifications.length === 0} testID="notifications-clear" />
      </View>
      <View style={styles.filter}>
        <SegmentedControl
          label={t('Show')}
          value={filter}
          onChange={setFilter}
          options={[
            { value: 'all', label: t('All'), testID: 'notifications-filter-all' },
            { value: 'unread', label: t('Unread'), testID: 'notifications-filter-unread' },
          ]}
        />
      </View>
      <FlatList
        data={shown}
        keyExtractor={(n) => n.id}
        ListEmptyComponent={<EmptyState icon="notifications-off-outline" title={t('No notifications')} message={t('State changes of missions, voyages, captains, deployments, objectives, and incidents appear here.')} />}
        renderItem={({ item }) => (
          <SwipeRow
            testID={`notification-${item.id}`}
            actions={item.read ? [] : [{ key: 'read', label: t('Mark read'), icon: 'checkmark', onPress: () => markRead(item.id) }]}
          >
            <ListRow
              title={item.title}
              subtitle={`${item.message}\n${formatRelativeTime(item.timestampUtc)}`}
              icon={item.read ? 'ellipse-outline' : 'ellipse'}
              accessory={<StatusBadge label={t(item.severity)} tone={item.severity} />}
              selected={item.id === selectedId}
              accessibilityValue={[t(item.severity), item.read ? null : t('Unread')]}
              onPress={() => open(item)}
            />
          </SwipeRow>
        )}
      />
      <ConfirmDialog
        open={confirmClear}
        title={t('Clear notifications?')}
        message={t('This removes the notification history on this device.')}
        confirmLabel={t('Clear')}
        cancelLabel={t('Cancel')}
        danger
        onConfirm={() => { setConfirmClear(false); clearHistory(); setSelectedId(null); }}
        onCancel={() => setConfirmClear(false)}
      />
    </View>
  );

  const detail = selected ? (
    <View style={styles.detail} testID="notification-detail">
      <StatusBadge label={t(selected.severity)} tone={selected.severity} />
      <AppText variant="heading" accessibilityRole="header">{selected.title}</AppText>
      <AppText>{selected.message}</AppText>
      <AppText muted>{formatDateTime(selected.timestampUtc)}</AppText>
      {notificationHref(selected) ? (
        <Button label={t('Open')} onPress={() => router.dismissTo(notificationHref(selected) as Href)} />
      ) : null}
      {!isTablet ? <Button label={t('Back')} variant="ghost" onPress={() => setSelectedId(null)} /> : null}
    </View>
  ) : isTablet ? (
    <EmptyState icon="notifications-outline" title={t('Select a notification')} />
  ) : null;

  return <SplitView master={list} detail={detail} />;
}

const styles = StyleSheet.create({
  fill: { flex: 1 },
  toolbar: { flexDirection: 'row', justifyContent: 'flex-end', paddingHorizontal: spacing.sm, paddingTop: spacing.sm },
  filter: { paddingHorizontal: spacing.md },
  detail: { padding: spacing.xl, gap: spacing.md },
});
