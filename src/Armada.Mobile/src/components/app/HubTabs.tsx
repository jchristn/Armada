import { useLocalSearchParams, useRouter } from 'expo-router';
import { useCallback } from 'react';
import { StyleSheet, View } from 'react-native';
import { useLocale } from '../../i18n/LocaleContext';
import { useInitialHubTab } from '../../navigation/listDetail';
import { spacing } from '../../theme/typography';
import { SegmentedControl } from '../ui';

export interface HubTab<K extends string> {
  key: K;
  label: string;
}

/** Pure: the active tab for a query value (unknown or missing values fall back to the default, as the dashboard). */
export function activeHubTab<K extends string>(keys: readonly K[], value: string | string[] | undefined, fallback: K): K {
  const raw = Array.isArray(value) ? value[0] : value;
  return (keys as readonly string[]).includes(raw ?? '') ? (raw as K) : fallback;
}

/**
 * Hub tabs (the dashboard's Tabs with ?tab=): the active tab lives in the route's query, so links such as
 * /missions?tab=voyages land on the right tab and the back stack is unchanged when switching.
 */
export function useHubTab<K extends string>(keys: readonly K[], fallback: K, param = 'tab'): [K, (key: K) => void] {
  const params = useLocalSearchParams<Record<string, string>>();
  const router = useRouter();
  // A detail route opened as this hub (ListDetailRoute) names the tab that holds its item.
  const routeTab = useInitialHubTab();
  const active = activeHubTab(keys, params[param], activeHubTab(keys, routeTab ?? undefined, fallback));
  const setActive = useCallback((key: K) => router.setParams({ [param]: key }), [router, param]);
  return [active, setActive];
}

export function HubTabBar<K extends string>({ tabs, active, onChange, label, testID }: {
  tabs: HubTab<K>[];
  active: K;
  onChange: (key: K) => void;
  label: string;
  testID?: string;
}) {
  const { t } = useLocale();
  return (
    <View style={styles.bar}>
      <SegmentedControl
        label={label}
        value={active}
        onChange={onChange}
        options={tabs.map((tab) => ({ value: tab.key, label: t(tab.label), testID: `${testID ?? 'hub'}-tab-${tab.key}` }))}
      />
    </View>
  );
}

const styles = StyleSheet.create({
  bar: { paddingHorizontal: spacing.md, paddingTop: spacing.sm },
});
