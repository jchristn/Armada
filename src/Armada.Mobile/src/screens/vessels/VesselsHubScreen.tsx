import { Stack, useRouter, type Href } from 'expo-router';
import { StyleSheet, View } from 'react-native';
import { HubTabBar, useHubTab, type HubTab } from '../../build/HubTabs';
import { useLocale } from '../../i18n/LocaleContext';
import { useTheme } from '../../theme/ThemeContext';
import { FleetsTab } from '../fleets/FleetsTab';
import { WorkspaceTab } from '../workspace/WorkspaceTab';
import { VesselHealthTab } from './health/VesselHealthTab';
import { VesselsTab } from './VesselsTab';

type VesselsHubKey = 'vessels' | 'health' | 'fleets' | 'workspace';

/** Path of the Health tab; like the dashboard, it is a real route so its filters stay out of `?tab=`. */
export const VESSEL_HEALTH_PATH = '/vessels/health';

/**
 * The Vessels hub (the dashboard's VesselsHub): Vessels, Health, Fleets, and Workspace tabs. `/vessels?tab=health`
 * and `/vessels/health` both show Health; choosing another tab from `/vessels/health` returns to `/vessels?tab=`.
 */
export function VesselsHubScreen({ onHealthPath = false }: { onHealthPath?: boolean }) {
  const { t } = useLocale();
  const { colors } = useTheme();
  const router = useRouter();
  const tabs: HubTab<VesselsHubKey>[] = [
    { key: 'vessels', label: t('Vessels') },
    { key: 'health', label: t('Health') },
    { key: 'fleets', label: t('Fleets') },
    { key: 'workspace', label: t('Workspace') },
  ];
  const [routeTab, setRouteTab] = useHubTab(tabs, onHealthPath ? 'health' : 'vessels');
  const tab: VesselsHubKey = onHealthPath ? 'health' : routeTab;
  const select = (key: VesselsHubKey) => {
    if (onHealthPath) router.replace(`/vessels?tab=${key}` as Href);
    else setRouteTab(key);
  };
  return (
    <View style={[styles.fill, { backgroundColor: colors.background }]} testID="vessels-hub">
      <Stack.Screen options={{ title: t('Vessels') }} />
      <HubTabBar tabs={tabs} value={tab} onChange={select} label={t('Vessel sections')} />
      <View style={styles.fill}>
        {tab === 'vessels' ? <VesselsTab /> : null}
        {tab === 'health' ? <VesselHealthTab /> : null}
        {tab === 'fleets' ? <FleetsTab /> : null}
        {tab === 'workspace' ? <WorkspaceTab /> : null}
      </View>
    </View>
  );
}

const styles = StyleSheet.create({ fill: { flex: 1 } });
