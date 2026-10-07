import { Stack } from 'expo-router';
import { HeaderActions } from '../components/app/HeaderActions';
import { useLayout } from './useLayout';
import { useTheme } from '../theme/ThemeContext';

/**
 * The stack inside each tab. Headers follow the theme; phones show the notification bell in every header (tablets
 * have it in the sidebar). Titles come from each screen (placeholders set the route title).
 */
export function TabStack() {
  const { colors } = useTheme();
  const { isTablet } = useLayout();
  return (
    <Stack
      screenOptions={{
        headerStyle: { backgroundColor: colors.surface },
        headerTintColor: colors.primary,
        headerTitleStyle: { color: colors.text },
        headerBackButtonDisplayMode: 'minimal',
        contentStyle: { backgroundColor: colors.background },
        headerRight: isTablet ? undefined : () => <HeaderActions />,
      }}
    />
  );
}
