import { Stack, useRouter, type Href } from 'expo-router';
import { useCallback, useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { HubTabBar, useHubTab, type HubTab } from '../../components/app/HubTabs';
import { useSplitSelection } from '../../components/app/useSplitSelection';
import { EmptyState, IconButton, SplitView } from '../../components/ui';
import { useLocale } from '../../i18n/LocaleContext';
import { MergeEntryDetail } from './MergeEntryDetail';
import { MergeQueueList } from './MergeQueueList';
import { MissionDetail, type MissionDetailTab } from './MissionDetail';
import { MissionsList } from './MissionsList';
import { VoyageDetail } from './VoyageDetail';
import { VoyagesList } from './VoyagesList';

export type MissionsHubTab = 'missions' | 'voyages' | 'merge-queue';
export const MISSIONS_HUB_TABS: HubTab<MissionsHubTab>[] = [
  { key: 'missions', label: 'Missions' },
  { key: 'voyages', label: 'Voyages' },
  { key: 'merge-queue', label: 'Merge Queue' },
];
const TAB_KEYS = MISSIONS_HUB_TABS.map((tab) => tab.key);

const missionRoute = (id: string) => `/missions/${id}`;
const voyageRoute = (id: string) => `/voyages/${id}`;
const mergeRoute = (id: string) => `/merge-queue/${id}`;

/**
 * The Missions hub (the dashboard's MissionsHub): Missions, Voyages, and the Merge Queue as tabs bound to ?tab=.
 * Narrow panes push each item's own route; wide ones show the selected item beside the list (split view). On the Voyages
 * tab the header's "+" opens Create Voyage (the Missions list has its own "+ Mission").
 */
export function MissionsHubScreen({ initialMissionTab = 'overview' }: { initialMissionTab?: MissionDetailTab } = {}) {
  const { t } = useLocale();
  const router = useRouter();
  const [tab, setTab] = useHubTab<MissionsHubTab>(TAB_KEYS, 'missions');
  const missions = useSplitSelection(missionRoute);
  const voyages = useSplitSelection(voyageRoute);
  const merges = useSplitSelection(mergeRoute);
  const isTablet = missions.isTablet;
  const [missionTab, setMissionTab] = useState<MissionDetailTab>(initialMissionTab);

  const selectMission = useCallback((id: string) => {
    setMissionTab('overview');
    missions.select(id);
  }, [missions]);
  const openMissionTab = useCallback((id: string, detailTab: MissionDetailTab) => {
    if (isTablet) {
      setMissionTab(detailTab);
      missions.select(id);
    } else {
      router.push(`/missions/${id}?tab=${detailTab}` as Href);
    }
  }, [isTablet, missions, router]);

  const headerRight = tab === 'voyages'
    ? () => <IconButton icon="add" label={t('Create Voyage')} onPress={() => router.push('/voyages/create')} testID="voyages-create" />
    : undefined;

  let master;
  let detail;
  let clear;
  if (tab === 'missions') {
    master = <MissionsList onSelect={selectMission} selectedId={missions.selectedId} onOpenTab={openMissionTab} />;
    detail = missions.selectedId
      ? <MissionDetail key={`${missions.selectedId}-${missionTab}`} id={missions.selectedId} embedded initialTab={missionTab} onDeleted={missions.clear} />
      : isTablet ? <EmptyState icon="flag-outline" title={t('Select a mission')} /> : null;
    clear = missions.clear;
  } else if (tab === 'voyages') {
    master = <VoyagesList onSelect={voyages.select} selectedId={voyages.selectedId} />;
    detail = voyages.selectedId ? <VoyageDetail key={voyages.selectedId} id={voyages.selectedId} embedded /> : isTablet ? <EmptyState icon="boat-outline" title={t('Select a voyage')} /> : null;
    clear = voyages.clear;
  } else {
    master = <MergeQueueList onSelect={merges.select} selectedId={merges.selectedId} />;
    detail = merges.selectedId ? <MergeEntryDetail key={merges.selectedId} id={merges.selectedId} embedded /> : isTablet ? <EmptyState icon="git-merge-outline" title={t('Select a merge queue entry')} /> : null;
    clear = merges.clear;
  }

  return (
    <View style={styles.fill} testID="missions-hub">
      <Stack.Screen options={{ title: t('Missions'), headerRight }} />
      <HubTabBar tabs={MISSIONS_HUB_TABS} active={tab} onChange={setTab} label={t('Mission sections')} testID="missions-hub" />
      <SplitView master={master} detail={detail} onBack={clear} backLabel={t('Back')} />
    </View>
  );
}

const styles = StyleSheet.create({
  fill: { flex: 1 },
});
