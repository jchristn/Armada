import { Stack } from 'expo-router';
import { useLocale } from '../../../i18n/LocaleContext';
import { ProfilesScreen } from '../../../screens/ProfilesScreen';

export default function ProfilesRoute() {
  const { t } = useLocale();
  return (
    <>
      <Stack.Screen options={{ title: t('Servers') }} />
      <ProfilesScreen />
    </>
  );
}
