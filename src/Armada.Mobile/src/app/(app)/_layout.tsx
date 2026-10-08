import { Tabs } from 'expo-router/tabs';
import { Sidebar } from '../../components/app/Sidebar';
import { StyleSheet, View, type ColorValue } from 'react-native';
import { AppText, CountBadge, Icon, type IconName } from '../../components/ui';
import { useLocale } from '../../i18n/LocaleContext';
import { useLayout } from '../../navigation/useLayout';
import { useApprovals } from '../../notifications/ApprovalsContext';
import { useTheme } from '../../theme/ThemeContext';
import { CHROME_MAX_FONT_SCALE } from '../../theme/typography';

function tabIcon(name: IconName, focusedName: IconName, badge = 0) {
  function TabIcon({ focused, size }: { focused: boolean; size: number }) {
    return (
      <View>
        <Icon name={focused ? focusedName : name} size={size} color={focused ? 'primary' : 'textMuted'} />
        {/* Our CountBadge caps its text growth like the label; the navigator's badge grows without limit and clips. */}
        {badge > 0 ? <View style={styles.badge}><CountBadge count={badge} /></View> : null}
      </View>
    );
  }
  return TabIcon;
}

/**
 * Tab labels grow with the text size up to the chrome cap (CHROME_MAX_FONT_SCALE): at the largest accessibility sizes
 * uncapped labels were cut to "Ap..." and "Wo...". The full name is still spoken (the tab's accessibility label), and
 * iOS shows it large in the Large Content Viewer on a long press.
 */
function TabLabel({ focused, color, children }: { focused: boolean; color: ColorValue; children: string }) {
  return (
    <AppText variant="caption" maxFontSizeMultiplier={CHROME_MAX_FONT_SCALE} numberOfLines={1} style={[styles.label, { color }, focused ? styles.focused : null]}>
      {children}
    </AppText>
  );
}

/**
 * The adaptive shell. Narrow windows (under 600 dp: phones in portrait, Slide Over, narrow Split View, folded
 * foldables): bottom tabs (Ask, Approvals, Work, More), each with its own stack. Wider windows: the same stacks
 * behind the dashboard-style sidebar (an icon rail below 1024 dp) instead of the tab bar. The switch follows the
 * window (useLayout), so rotation, iPad Split View, Stage Manager, and Android multi-window re-layout live; the
 * tab navigator and its stacks stay mounted, so nothing on screen is lost.
 */
export default function AppTabsLayout() {
  const { isTablet } = useLayout();
  const { t } = useLocale();
  const { colors } = useTheme();
  const { count } = useApprovals();

  return (
    <Tabs
      initialRouteName="(ask)"
      tabBar={isTablet ? () => <Sidebar /> : undefined}
      screenOptions={{
        headerShown: false,
        tabBarPosition: isTablet ? 'left' : 'bottom',
        tabBarActiveTintColor: colors.primary,
        tabBarInactiveTintColor: colors.textMuted,
        tabBarStyle: { backgroundColor: colors.surface, borderTopColor: colors.border },
        tabBarLabel: TabLabel,
        sceneStyle: { backgroundColor: colors.background },
      }}
    >
      <Tabs.Screen
        name="(ask)"
        options={{ title: t('Ask'), tabBarButtonTestID: 'tab-ask', tabBarAccessibilityLabel: t('Ask Armada'), tabBarIcon: tabIcon('chatbubbles-outline', 'chatbubbles') }}
      />
      <Tabs.Screen
        name="(approvals)"
        options={{
          title: t('Approvals'),
          tabBarButtonTestID: 'tab-approvals',
          tabBarAccessibilityLabel: count > 0 ? `${t('Approvals')}, ${count}` : t('Approvals'),
          tabBarIcon: tabIcon('checkmark-done-circle-outline', 'checkmark-done-circle', count),
        }}
      />
      <Tabs.Screen
        name="(work)"
        options={{ title: t('Work'), tabBarButtonTestID: 'tab-work', tabBarIcon: tabIcon('layers-outline', 'layers') }}
      />
      <Tabs.Screen
        name="(more)"
        options={{ title: t('More'), tabBarButtonTestID: 'tab-more', tabBarIcon: tabIcon('menu-outline', 'menu') }}
      />
    </Tabs>
  );
}

export const unstable_settings = { initialRouteName: '(ask)' };

const styles = StyleSheet.create({
  badge: { position: 'absolute', top: -6, right: -12 },
  label: { fontSize: 12, lineHeight: 16 },
  focused: { fontWeight: '600' },
});
