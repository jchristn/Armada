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
import { useApprovals } from '../../notifications/ApprovalsContext';
import { useNotifications } from '../../notifications/NotificationContext';
import { useTheme } from '../../theme/ThemeContext';
import { CHROME_MAX_FONT_SCALE, MIN_TOUCH, radius, spacing } from '../../theme/typography';
import { AppText, CountBadge, Icon } from '../ui';
import { useSignOut } from './useSignOut';

export const SIDEBAR_WIDTH = 280;

/**
 * Tablet navigation: the dashboard sidebar (Ask Armada, Approvals, Notifications, Dashboard, then the grouped
 * sections) plus the app's own entries. Replaces the bottom tab bar when the window is at least 768 dp wide.
 */
export function Sidebar() {
  const { colors } = useTheme();
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
          styles.item,
          { backgroundColor: selected ? colors.surfaceRaised : pressed ? colors.background : 'transparent' },
          selected ? { borderLeftColor: colors.primary } : null,
        ]}
      >
        <Icon name={entry.icon} size={20} color={selected ? 'primary' : 'textMuted'} />
        <AppText variant="label" numberOfLines={1} maxFontSizeMultiplier={CHROME_MAX_FONT_SCALE} style={styles.label} color={selected ? 'primary' : 'text'}>
          {t(entry.label)}
        </AppText>
        <CountBadge count={badge} />
      </Pressable>
    );
  };

  return (
    <View testID="sidebar" style={[styles.wrap, { width: SIDEBAR_WIDTH, backgroundColor: colors.surface, borderRightColor: colors.border, paddingTop: insets.top }]}>
      <View style={styles.brand}>
        <Image source={require('../../../assets/images/icon.png')} style={styles.logo} accessible={false} />
        <View style={styles.flex}>
          <AppText variant="heading" maxFontSizeMultiplier={CHROME_MAX_FONT_SCALE}>Armada</AppText>
          {activeProfile ? <AppText variant="caption" muted numberOfLines={1}>{activeProfile.name}</AppText> : null}
        </View>
      </View>
      <ScrollView contentContainerStyle={{ paddingBottom: insets.bottom + spacing.lg }}>
        {item(ASK_ITEM)}
        {item(APPROVALS_ITEM, count)}
        {item(NOTIFICATIONS_ITEM, unreadCount)}
        {item(HOME_ITEM)}
        {NAV_SECTIONS.map((section) => (
          <View key={section.key} style={styles.section}>
            <AppText variant="subheading" muted accessibilityRole="header" maxFontSizeMultiplier={CHROME_MAX_FONT_SCALE} style={styles.sectionLabel}>
              {t(section.label)}
            </AppText>
            {section.items.map((entry) => item(entry))}
          </View>
        ))}
        <View style={styles.section}>
          <AppText variant="subheading" muted accessibilityRole="header" maxFontSizeMultiplier={CHROME_MAX_FONT_SCALE} style={styles.sectionLabel}>{t('APP')}</AppText>
          {item(PREFERENCES_ITEM)}
          {item(PROFILES_ITEM)}
          <Pressable testID="sidebar-sign-out" accessibilityRole="button" accessibilityLabel={t('Sign out')} onPress={signOut} style={styles.item}>
            <Icon name="log-out-outline" size={20} color="danger" />
            <AppText variant="label" color="danger" style={styles.label}>{t('Sign out')}</AppText>
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
});
