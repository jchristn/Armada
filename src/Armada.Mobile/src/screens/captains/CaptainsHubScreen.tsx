import { Stack } from 'expo-router';
import { StyleSheet, View } from 'react-native';
import { HubTabBar, useHubTab, type HubTab } from '../../build/HubTabs';
import { useLocale } from '../../i18n/LocaleContext';
import { useTheme } from '../../theme/ThemeContext';
import { CaptainsTab } from './CaptainsTab';
import { DocksTab } from './DocksTab';
import { ModelProfilesTab } from './ModelProfilesTab';

type CaptainsHubKey = 'captains' | 'docks' | 'profiles';

/** The Captains hub (the dashboard's CaptainsHub): Captains, Docks, and Model Profiles tabs (`/docks` and `/model-profiles` redirect to `?tab=docks` and `?tab=profiles`). */
export function CaptainsHubScreen() {
  const { t } = useLocale();
  const { colors } = useTheme();
  const tabs: HubTab<CaptainsHubKey>[] = [
    { key: 'captains', label: t('Captains') },
    { key: 'docks', label: t('Docks') },
    { key: 'profiles', label: t('Model Profiles') },
  ];
  const [tab, setTab] = useHubTab(tabs, 'captains');
  return (
    <View style={[styles.fill, { backgroundColor: colors.background }]} testID="captains-hub">
      <Stack.Screen options={{ title: t('Captains') }} />
      <HubTabBar tabs={tabs} value={tab} onChange={setTab} label={t('Captain sections')} />
      <View style={styles.fill}>
        {tab === 'captains' ? <CaptainsTab /> : tab === 'docks' ? <DocksTab /> : <ModelProfilesTab />}
      </View>
    </View>
  );
}

const styles = StyleSheet.create({ fill: { flex: 1 } });
