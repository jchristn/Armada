import { Stack, useLocalSearchParams } from 'expo-router';
import { useLocale } from '@/i18n/LocaleContext';
import { MissionDetail, missionDetailTab } from '@/screens/operations/MissionDetail';

export default function Screen() {
  const { id, tab } = useLocalSearchParams<{ id: string; tab?: string }>();
  const { t } = useLocale();
  return (
    <>
      <Stack.Screen options={{ title: t('Mission') }} />
      <MissionDetail key={`${id}-${tab ?? ''}`} id={String(id)} initialTab={missionDetailTab(tab)} />
    </>
  );
}
