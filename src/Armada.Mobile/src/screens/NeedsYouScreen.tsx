import { useRouter, type Href } from 'expo-router';
import { useEffect, useMemo, useState } from 'react';
import { RefreshControl, ScrollView, StyleSheet, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import type { WebSocketMessage } from '@dashboard/types/models';
import { CLI_PERMISSION_EVENT_PREFIX } from '@dashboard/lib/cliPermissions';
import { inboxItemKey, splitInbox } from '@dashboard/lib/inboxKinds';
import { ApprovalCard, InterventionCard } from '../components/approvals/ApprovalCard';
import { AppText, Button, EmptyState } from '../components/ui';
import { useLocale } from '../i18n/LocaleContext';
import { notificationHref, useNotifications } from '../notifications/NotificationContext';
import { useApprovals } from '../notifications/ApprovalsContext';
import { useSocket } from '../socket/SocketContext';
import { useTheme } from '../theme/ThemeContext';
import { radius, spacing } from '../theme/typography';

/** Recent unread alerts listed under the inbox (the dashboard's Needs You page shows eight). */
const RECENT_ALERTS = 8;

function Stat({ label, value, color }: { label: string; value: number; color?: 'danger' | 'warning' }) {
  const { colors } = useTheme();
  return (
    <View style={[styles.stat, { backgroundColor: colors.surface, borderColor: colors.border }]} accessible accessibilityLabel={`${label}: ${value}`}>
      <AppText variant="caption" muted>{label}</AppText>
      <AppText variant="heading" color={color}>{String(value)}</AppText>
    </View>
  );
}

/**
 * The approvals center: everything waiting on a decision or an intervention from the user (the dashboard's Needs
 * You page, /inbox), with the decisions made in place. Ask proposals show the confirm card with the exact
 * arguments; CLI permission requests show Allow once / Allow and remember / Deny; mission reviews open the verdict
 * sheet; deployments are approved or denied with the dashboard's confirmation. Failed landings, failed missions,
 * and stalled captains offer their fix (retry landing, restart, stop). Unread alerts follow. Live: the list
 * reloads on socket activity (immediately for CLI permission and Ask proposal events) and on pull to refresh.
 */
export function NeedsYouScreen({ testID = 'approvals' }: { testID?: string }) {
  const { t, formatRelativeTime } = useLocale();
  const { colors } = useTheme();
  const router = useRouter();
  const { items, refresh } = useApprovals();
  const { subscribe } = useSocket();
  const { notifications, unreadCount, markRead, markAllRead } = useNotifications();
  const [refreshing, setRefreshing] = useState(false);

  const { approvals, interventions } = useMemo(() => splitInbox(items), [items]);
  const counts = useMemo(() => ({
    critical: items.filter((i) => i.severity === 'Critical').length,
    warning: items.filter((i) => i.severity === 'Warning').length,
  }), [items]);
  const unreadAlerts = useMemo(() => notifications.filter((n) => !n.read).slice(0, RECENT_ALERTS), [notifications]);

  // A CLI tool request or an Ask proposal appears or is decided elsewhere: reload so the row appears or disappears now.
  useEffect(() => subscribe((msg: WebSocketMessage) => {
    if (typeof msg.type !== 'string') return;
    if (msg.type.startsWith(CLI_PERMISSION_EVENT_PREFIX) || msg.type === 'ask.proposal') void refresh();
  }), [subscribe, refresh]);

  const onChanged = () => { void refresh(); };

  return (
    <SafeAreaView edges={['left', 'right']} style={[styles.fill, { backgroundColor: colors.background }]} testID={testID}>
      <ScrollView
        contentContainerStyle={styles.content}
        keyboardShouldPersistTaps="handled"
        refreshControl={<RefreshControl refreshing={refreshing} tintColor={colors.primary} onRefresh={async () => { setRefreshing(true); await refresh(); setRefreshing(false); }} />}
      >
        <View style={styles.column}>
          <AppText muted style={styles.subtitle}>{t('Everything across the fleet that is waiting on a decision or intervention from you.')}</AppText>
          <View style={styles.stats}>
            <Stat label={t('Total')} value={items.length} />
            <Stat label={t('Critical')} value={counts.critical} color="danger" />
            <Stat label={t('Warning')} value={counts.warning} color="warning" />
            <Stat label={t('Unread alerts')} value={unreadCount} />
          </View>

          {items.length === 0 ? (
            <EmptyState icon="checkmark-done-outline" title={t('You are all caught up.')} message={t('Nothing needs your attention right now.')} />
          ) : null}

          {approvals.length > 0 ? (
            <View accessibilityLabel={t('Waiting for your approval')} testID="approvals-waiting">
              <AppText variant="subheading" muted accessibilityRole="header" style={styles.sectionTitle}>
                {`${t('Waiting for your approval')} (${approvals.length})`}
              </AppText>
              {approvals.map((item, i) => <ApprovalCard key={inboxItemKey(item, i)} item={item} onChanged={onChanged} />)}
            </View>
          ) : null}

          {interventions.length > 0 ? (
            <View accessibilityLabel={t('Needs intervention')} testID="approvals-interventions">
              <AppText variant="subheading" muted accessibilityRole="header" style={styles.sectionTitle}>
                {`${t('Needs intervention')} (${interventions.length})`}
              </AppText>
              {interventions.map((item, i) => <InterventionCard key={inboxItemKey(item, i)} item={item} onChanged={onChanged} />)}
            </View>
          ) : null}

          {unreadAlerts.length > 0 ? (
            <View testID="approvals-alerts">
              <View style={styles.alertsHead}>
                <AppText variant="subheading" muted accessibilityRole="header" style={styles.flex}>{t('Recent alerts')}</AppText>
                <Button label={t('Mark all read')} variant="ghost" onPress={markAllRead} accessibilityHint={t('Mark all notifications as read')} style={styles.noMargin} />
              </View>
              {unreadAlerts.map((n) => {
                const href = notificationHref(n);
                return (
                  <View key={n.id} style={[styles.alert, { backgroundColor: colors.surface, borderColor: colors.border }]}>
                    <AppText
                      variant="label"
                      accessibilityRole={href ? 'link' : 'text'}
                      onPress={() => { markRead(n.id); if (href) router.push(href as Href); }}
                    >
                      {n.title}
                    </AppText>
                    <AppText variant="caption" muted numberOfLines={2}>{n.message}</AppText>
                    <AppText variant="caption" muted>{formatRelativeTime(n.timestampUtc)}</AppText>
                  </View>
                );
              })}
            </View>
          ) : null}
        </View>
      </ScrollView>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  fill: { flex: 1 },
  flex: { flex: 1 },
  content: { paddingVertical: spacing.lg, flexGrow: 1 },
  column: { width: '100%', maxWidth: 820, alignSelf: 'center' },
  subtitle: { marginHorizontal: spacing.lg, marginBottom: spacing.md },
  stats: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.sm, marginHorizontal: spacing.md, marginBottom: spacing.lg },
  stat: { flexGrow: 1, minWidth: 140, borderWidth: StyleSheet.hairlineWidth, borderRadius: radius.md, padding: spacing.md },
  sectionTitle: { marginHorizontal: spacing.lg, marginBottom: spacing.sm, marginTop: spacing.sm, textTransform: 'uppercase' },
  alertsHead: { flexDirection: 'row', alignItems: 'center', marginHorizontal: spacing.lg, marginTop: spacing.md },
  alert: { borderWidth: StyleSheet.hairlineWidth, borderRadius: radius.md, padding: spacing.md, marginHorizontal: spacing.md, marginBottom: spacing.sm, gap: 2 },
  noMargin: { marginBottom: 0 },
});
