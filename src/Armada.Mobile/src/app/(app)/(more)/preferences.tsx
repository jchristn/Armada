import { Stack } from 'expo-router';
import { useLocale } from '../../../i18n/LocaleContext';
import { PreferencesScreen } from '../../../screens/PreferencesScreen';

export default function PreferencesRoute() {
  const { t } = useLocale();
  return (
    <>
      <Stack.Screen options={{ title: t('Preferences') }} />
      <PreferencesScreen />
    </>
  );
}
