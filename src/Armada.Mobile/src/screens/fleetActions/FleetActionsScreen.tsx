import { Stack } from 'expo-router';
import { StyleSheet, View } from 'react-native';
import { HubTabBar, useHubTab, type HubTab } from '../../build/HubTabs';
import { useLocale } from '../../i18n/LocaleContext';
import { useTheme } from '../../theme/ThemeContext';
import { FleetActionRunsTab } from './FleetActionRunsTab';
import { FleetActionsTab } from './FleetActionsTab';

type FleetActionsKey = 'actions' | 'runs';

/**
 * Fleet Actions (the dashboard's FleetActions page): define reusable commands and mission prompts (Actions) and
 * watch their runs across many vessels (Runs). `?run=new` starts the run flow (the Home and vessel shortcuts).
 */
export function FleetActionsScreen() {
  const { t } = useLocale();
  const { colors } = useTheme();
  const tabs: HubTab<FleetActionsKey>[] = [
    { key: 'actions', label: t('Actions') },
    { key: 'runs', label: t('Runs') },
  ];
  const [tab, setTab] = useHubTab(tabs, 'actions');
  return (
    <View style={[styles.fill, { backgroundColor: colors.background }]} testID="fleet-actions">
      <Stack.Screen options={{ title: t('Fleet Actions') }} />
      <HubTabBar tabs={tabs} value={tab} onChange={setTab} label={t('Fleet action sections')} />
      <View style={styles.fill}>
        {tab === 'actions' ? <FleetActionsTab /> : <FleetActionRunsTab />}
      </View>
    </View>
  );
}

const styles = StyleSheet.create({ fill: { flex: 1 } });
