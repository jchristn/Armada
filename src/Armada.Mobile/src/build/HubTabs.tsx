import { useLocalSearchParams, useRouter } from 'expo-router';
import { Pressable, ScrollView, StyleSheet, View } from 'react-native';
import { AppText } from '../components/ui';
import { useTheme } from '../theme/ThemeContext';
import { CHROME_MAX_FONT_SCALE, MIN_TOUCH, spacing } from '../theme/typography';
import { useInitialHubTab } from '../navigation/listDetail';

export interface HubTab<K extends string> {
  key: K;
  label: string;
}

/** The hub tab named by `?tab=` (the dashboard's Tabs), or the default when it names none of them. */
export function useHubTab<K extends string>(tabs: HubTab<K>[], defaultKey: K): [K, (key: K) => void] {
  const params = useLocalSearchParams<{ tab?: string }>();
  const router = useRouter();
  // A detail route opened as this hub (ListDetailRoute) names the tab that holds its item.
  const routeTab = useInitialHubTab();
  const requested = typeof params.tab === 'string' ? params.tab : '';
  const fallback = (tabs.find((t) => t.key === routeTab)?.key ?? defaultKey) as K;
  const current = (tabs.find((t) => t.key === requested)?.key ?? fallback) as K;
  const select = (key: K) => router.setParams({ tab: key });
  return [current, select];
}

export interface HubTabBarProps<K extends string> {
  tabs: HubTab<K>[];
  value: K;
  onChange: (key: K) => void;
  /** Spoken name of the tab list ("Vessel sections"). */
  label: string;
  testID?: string;
}

/**
 * The tab strip of a dashboard hub page (Vessels, Captains, Fleet Actions): an underlined, horizontally scrollable
 * row so long translations never squeeze; the selected tab is part of the URL (`?tab=`), so deep links and the
 * dashboard's redirects (/fleets -> /vessels?tab=fleets) land on the right tab.
 */
export function HubTabBar<K extends string>({ tabs, value, onChange, label, testID }: HubTabBarProps<K>) {
  const { colors } = useTheme();
  return (
    <View style={[styles.bar, { borderBottomColor: colors.border, backgroundColor: colors.surface }]}>
      <ScrollView horizontal showsHorizontalScrollIndicator={false} accessibilityRole="tablist" accessibilityLabel={label} testID={testID} contentContainerStyle={styles.row}>
        {tabs.map((tab) => {
          const selected = tab.key === value;
          return (
            <Pressable
              key={tab.key}
              testID={`hub-tab-${tab.key}`}
              accessibilityRole="tab"
              accessibilityLabel={tab.label}
              accessibilityState={{ selected }}
              onPress={() => { if (!selected) onChange(tab.key); }}
              style={[styles.tab, { borderBottomColor: selected ? colors.primary : 'transparent' }]}
            >
              <AppText variant="label" color={selected ? 'primary' : 'textMuted'} maxFontSizeMultiplier={CHROME_MAX_FONT_SCALE}>{tab.label}</AppText>
            </Pressable>
          );
        })}
      </ScrollView>
    </View>
  );
}

const styles = StyleSheet.create({
  bar: { borderBottomWidth: StyleSheet.hairlineWidth },
  row: { paddingHorizontal: spacing.sm },
  tab: { minHeight: MIN_TOUCH, justifyContent: 'center', paddingHorizontal: spacing.md, borderBottomWidth: 3 },
});
