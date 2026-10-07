import { Tabs } from 'expo-router/tabs';
import { Sidebar } from '../../components/app/Sidebar';
import { Icon, type IconName } from '../../components/ui';
import { useLocale } from '../../i18n/LocaleContext';
import { useLayout } from '../../navigation/useLayout';
import { useApprovals } from '../../notifications/ApprovalsContext';
import { useTheme } from '../../theme/ThemeContext';

function tabIcon(name: IconName, focusedName: IconName) {
  function TabIcon({ focused, size }: { focused: boolean; size: number }) {
    return <Icon name={focused ? focusedName : name} size={size} color={focused ? 'primary' : 'textMuted'} />;
  }
  return TabIcon;
}

/**
 * The adaptive shell. Phones: bottom tabs (Ask, Approvals, Work, More), each with its own stack. Tablets (window
 * at least 768 dp wide): the same stacks behind the dashboard-style sidebar instead of the tab bar. The switch
 * follows the window, so iPad Split View and Android multi-window change layout live.
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
        tabBarLabelStyle: { fontSize: 12 },
        tabBarAllowFontScaling: true,
        tabBarBadgeStyle: { backgroundColor: colors.badge, color: colors.badgeText },
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
          tabBarBadge: count > 0 ? (count > 99 ? '99+' : count) : undefined,
          tabBarIcon: tabIcon('checkmark-done-circle-outline', 'checkmark-done-circle'),
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
