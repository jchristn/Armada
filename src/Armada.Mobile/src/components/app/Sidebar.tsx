import { usePathname, useRouter, type Href } from 'expo-router';
import { Image, Pressable, ScrollView, StyleSheet, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { useAuth } from '../../auth/AuthContext';
import { useLocale } from '../../i18n/LocaleContext';
import {
  APPROVALS_ITEM,
  ASK_ITEM,
  HOME_ITEM,
  NAV_SECTIONS,
  NOTIFICATIONS_ITEM,
  PREFERENCES_ITEM,
  PROFILES_ITEM,
  activeNavKey,
  type MobileNavItem,
} from '../../navigation/navItems';
import { RAIL_WIDTH, SIDEBAR_WIDTH, navModeFor, useLayout, useSidebarPreference } from '../../navigation/useLayout';
import { useApprovals } from '../../notifications/ApprovalsContext';
import { useNotifications } from '../../notifications/NotificationContext';
import { useTheme } from '../../theme/ThemeContext';
import { CHROME_MAX_FONT_SCALE, MIN_TOUCH, radius, spacing } from '../../theme/typography';
import { AppText, CountBadge, Icon } from '../ui';
import { useSignOut } from './useSignOut';
import { NotEncryptedIndicator } from './NotEncryptedIndicator';

/**
 * Tablet navigation: the dashboard sidebar (Ask Armada, Approvals, Notifications, Dashboard, then the grouped
 * sections) plus the app's own entries. Replaces the bottom tab bar when the window is at least 600 dp wide. On
 * medium windows (iPad portrait, iPad mini, phones in landscape, foldables) it is an icon rail by default, so the
 * content keeps its width; the toggle at the top expands or collapses it (the choice is remembered).
 */
export function Sidebar() {
  const { colors } = useTheme();
  const { navMode, width } = useLayout();
  const { setPreference } = useSidebarPreference();
  const collapsed = navMode === 'rail';
  // Back to 'auto' when the choice matches what this window width gets anyway, so rotating keeps the default.
  const toggle = () => {
    const target = collapsed ? 'sidebar' : 'rail';
    setPreference(navModeFor(width, 'auto') === target ? 'auto' : target === 'sidebar' ? 'expanded' : 'collapsed');
  };
  const { t } = useLocale();
  const insets = useSafeAreaInsets();
  const router = useRouter();
  const pathname = usePathname();
  const { count } = useApprovals();
  const { unreadCount } = useNotifications();
  const { activeProfile } = useAuth();
  const { signOut, signOutSheet } = useSignOut();
  const active = activeNavKey(pathname);

  const item = (entry: MobileNavItem, badge = 0) => {
    const selected = entry.key === active;
    return (
      <Pressable
        key={entry.key}
        testID={`sidebar-${entry.to}`}
        accessibilityRole="link"
        accessibilityLabel={badge ? `${t(entry.label)}, ${badge}` : t(entry.label)}
        accessibilityHint={entry.tooltip ? t(entry.tooltip) : undefined}
        accessibilityState={{ selected }}
        onPress={() => router.navigate(entry.to as Href)}
        style={({ pressed }) => [
          collapsed ? styles.railItem : styles.item,
          { backgroundColor: selected ? colors.surfaceRaised : pressed ? colors.background : 'transparent' },
          selected ? { borderLeftColor: colors.primary } : null,
        ]}
      >
        <Icon name={entry.icon} size={collapsed ? 22 : 20} color={selected ? 'primary' : 'textMuted'} />
        {collapsed ? (
          badge > 0 ? <View style={styles.railBadge} pointerEvents="none"><CountBadge count={badge} /></View> : null
        ) : (
          <>
            <AppText variant="label" numberOfLines={1} maxFontSizeMultiplier={CHROME_MAX_FONT_SCALE} style={styles.label} color={selected ? 'primary' : 'text'}>
              {t(entry.label)}
            </AppText>
            <CountBadge count={badge} />
          </>
        )}
      </Pressable>
    );
  };

  const sectionHeader = (label: string) => (collapsed ? (
    <View style={[styles.railDivider, { backgroundColor: colors.border }]} accessible={false} />
  ) : (
    <AppText variant="subheading" muted accessibilityRole="header" maxFontSizeMultiplier={CHROME_MAX_FONT_SCALE} style={styles.sectionLabel}>
      {t(label)}
    </AppText>
  ));

  return (
    <View
      testID="sidebar"
      style={[
        styles.wrap,
        {
          width: (collapsed ? RAIL_WIDTH : SIDEBAR_WIDTH) + insets.left,
          paddingLeft: insets.left,
          backgroundColor: colors.surface,
          borderRightColor: colors.border,
          paddingTop: insets.top,
        },
      ]}
    >
      <View style={collapsed ? styles.railBrand : styles.brand}>
        <Image source={require('../../../assets/images/icon.png')} style={styles.logo} accessible={false} />
        {collapsed ? null : (
          <View style={styles.flex}>
            <AppText variant="heading" maxFontSizeMultiplier={CHROME_MAX_FONT_SCALE}>Armada</AppText>
            {activeProfile ? <AppText variant="caption" muted numberOfLines={1}>{activeProfile.name}</AppText> : null}
            <NotEncryptedIndicator />
          </View>
        )}
        <Pressable
          testID="sidebar-toggle"
          accessibilityRole="button"
          accessibilityLabel={collapsed ? t('Expand sidebar') : t('Collapse sidebar')}
          accessibilityState={{ expanded: !collapsed }}
          onPress={toggle}
          hitSlop={8}
          style={({ pressed }) => [styles.toggle, pressed ? { backgroundColor: colors.background } : null]}
        >
          <Icon name={collapsed ? 'chevron-forward' : 'chevron-back'} size={18} color="textMuted" />
        </Pressable>
      </View>
      {collapsed ? <NotEncryptedIndicator /> : null}
      <ScrollView testID="sidebar-scroll" contentContainerStyle={{ paddingBottom: insets.bottom + spacing.lg }}>
        {item(ASK_ITEM)}
        {item(APPROVALS_ITEM, count)}
        {item(NOTIFICATIONS_ITEM, unreadCount)}
        {item(HOME_ITEM)}
        {NAV_SECTIONS.map((section) => (
          <View key={section.key} style={collapsed ? styles.railSection : styles.section}>
            {sectionHeader(section.label)}
            {section.items.map((entry) => item(entry))}
          </View>
        ))}
        <View style={collapsed ? styles.railSection : styles.section}>
          {sectionHeader('APP')}
          {item(PREFERENCES_ITEM)}
          {item(PROFILES_ITEM)}
          <Pressable testID="sidebar-sign-out" accessibilityRole="button" accessibilityLabel={t('Sign out')} onPress={signOut} style={collapsed ? styles.railItem : styles.item}>
            <Icon name="log-out-outline" size={collapsed ? 22 : 20} color="danger" />
            {collapsed ? null : <AppText variant="label" color="danger" style={styles.label}>{t('Sign out')}</AppText>}
          </Pressable>
        </View>
      </ScrollView>
      {signOutSheet}
    </View>
  );
}

const styles = StyleSheet.create({
  wrap: { borderRightWidth: StyleSheet.hairlineWidth, height: '100%' },
  brand: { flexDirection: 'row', alignItems: 'center', gap: spacing.md, padding: spacing.lg },
  logo: { width: 36, height: 36, borderRadius: radius.sm },
  flex: { flex: 1 },
  section: { marginTop: spacing.lg },
  sectionLabel: { paddingHorizontal: spacing.lg, marginBottom: spacing.xs },
  item: { flexDirection: 'row', alignItems: 'center', gap: spacing.md, minHeight: MIN_TOUCH, paddingHorizontal: spacing.lg, borderLeftWidth: 3, borderLeftColor: 'transparent' },
  label: { flex: 1 },
  railBrand: { alignItems: 'center', gap: spacing.sm, paddingVertical: spacing.md },
  toggle: { width: MIN_TOUCH, height: MIN_TOUCH, alignItems: 'center', justifyContent: 'center', borderRadius: radius.sm },
  railSection: { marginTop: spacing.sm },
  railDivider: { height: StyleSheet.hairlineWidth, marginHorizontal: spacing.md, marginBottom: spacing.sm },
  railItem: { alignItems: 'center', justifyContent: 'center', minHeight: MIN_TOUCH + 4, borderLeftWidth: 3, borderLeftColor: 'transparent' },
  railBadge: { position: 'absolute', top: 2, right: 8 },
});
