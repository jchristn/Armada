import { Stack, useLocalSearchParams } from 'expo-router';
import { useLocale } from '@/i18n/LocaleContext';
import { ListDetailRoute } from '@/navigation/listDetail';
import { MissionDetail, missionDetailTab } from '@/screens/operations/MissionDetail';
import { MissionsHubScreen } from '@/screens/operations/MissionsHubScreen';

/**
 * /missions/:id (`?tab=` opens a detail tab). Opened on a window wide enough for list and detail, it shows the
 * Missions hub with this mission selected beside the list; narrower windows show the mission alone.
 */
export default function Screen() {
  const { id, tab } = useLocalSearchParams<{ id: string; tab?: string }>();
  const { t } = useLocale();
  const key = String(id ?? '');
  return (
    <ListDetailRoute
      path={`/missions/${key}`}
      id={key}
      tab="missions"
      list={<MissionsHubScreen initialMissionTab={missionDetailTab(tab)} />}
      detail={(
        <>
          <Stack.Screen options={{ title: t('Mission') }} />
          <MissionDetail key={`${key}-${tab ?? ''}`} id={key} initialTab={missionDetailTab(tab)} />
        </>
      )}
    />
  );
}
