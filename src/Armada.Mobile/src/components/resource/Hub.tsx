import { Stack, useLocalSearchParams, useRouter, type Href } from 'expo-router';
import { useCallback, useState, type ReactNode } from 'react';
import { StyleSheet, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useLocale } from '../../i18n/LocaleContext';
import { useLayout } from '../../navigation/useLayout';
import { useTheme } from '../../theme/ThemeContext';
import { SplitView } from '../ui/SplitView';
import { EmptyState } from '../ui/States';
import { TabStrip } from '../ui/TabStrip';

export interface HubTab {
  key: string;
  label: string;
  /** Hidden tabs (role-gated, like the dashboard's admin tabs) are not listed and cannot be selected by URL. */
  hidden?: boolean;
  render: () => ReactNode;
}

/** The tab a hub shows for a URL parameter: the named tab when it exists and is visible, otherwise the default. */
export function resolveHubTab(tabs: HubTab[], requested: string | string[] | undefined, defaultKey: string): string {
  const visible = tabs.filter((tab) => !tab.hidden);
  const key = Array.isArray(requested) ? requested[0] : requested;
  if (key && visible.some((tab) => tab.key === key)) return key;
  if (visible.some((tab) => tab.key === defaultKey)) return defaultKey;
  return visible[0]?.key ?? defaultKey;
}

export interface HubScreenProps {
  title: string;
  tabs: HubTab[];
  defaultKey: string;
  /** URL parameter that names the tab ('tab' for most hubs, 'source' for Activity), as on the dashboard. */
  param?: string;
  /** Spoken name of the tab list. */
  label: string;
  testID?: string;
}

/**
 * A dashboard hub page (Delivery, Configuration, Activity, Settings, CLI Tool Permissions): scrolling tabs with the
 * active tab in the URL (`?tab=` or `?source=`), so dashboard links and deep links land on the same tab.
 */
export function HubScreen({ title, tabs, defaultKey, param = 'tab', label, testID }: HubScreenProps) {
  const params = useLocalSearchParams<Record<string, string>>();
  const router = useRouter();
  const { t } = useLocale();
  const { colors } = useTheme();
  const active = resolveHubTab(tabs, params[param], defaultKey);
  const tab = tabs.find((candidate) => candidate.key === active);
  return (
    <SafeAreaView edges={['left', 'right']} style={[styles.fill, { backgroundColor: colors.background }]} testID={testID}>
      <Stack.Screen options={{ title }} />
      <TabStrip
        label={label}
        value={active}
        onChange={(key) => router.setParams({ [param]: key })}
        options={tabs.filter((candidate) => !candidate.hidden).map((candidate) => ({ value: candidate.key, label: t(candidate.label), testID: `${testID ?? 'hub'}-tab-${candidate.key}` }))}
        testID={testID ? `${testID}-tabs` : undefined}
      />
      <View style={styles.fill} key={active}>{tab?.render()}</View>
    </SafeAreaView>
  );
}

/**
 * List-detail selection. Tablets keep the selection and show it beside the list; phones push the item's own
 * route (the same URL the dashboard uses), so the detail screen and its deep link are one screen.
 */
export function useSelection(pathFor: (id: string) => string) {
  const { isTablet } = useLayout();
  const router = useRouter();
  const [selected, setSelected] = useState<string | null>(null);
  const open = useCallback((id: string) => {
    if (isTablet) setSelected(id);
    else router.push(pathFor(id) as Href);
  }, [isTablet, router, pathFor]);
  const clear = useCallback(() => setSelected(null), []);
  return { selected: isTablet ? selected : null, open, clear, isTablet };
}

/** A list with its selected item beside it on tablets (SplitView), and a hint when nothing is selected. */
export function MasterDetail({ list, detail, emptyLabel }: { list: ReactNode; detail: ReactNode | null; emptyLabel?: string }) {
  const { t } = useLocale();
  const { isTablet } = useLayout();
  if (!isTablet) return <View style={styles.fill}>{list}</View>;
  return (
    <SplitView
      masterWidth={420}
      master={list}
      detail={detail ?? <EmptyState icon="albums-outline" title={emptyLabel ?? t('Select an item to see its details.')} />}
    />
  );
}

const styles = StyleSheet.create({
  fill: { flex: 1 },
});
