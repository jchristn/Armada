import { Stack } from 'expo-router';
import { useLocale } from '../../../i18n/LocaleContext';
import { NavMenuScreen } from '../../../screens/NavMenuScreen';

/** Work tab root: Home (the dashboard overview, W2.1) and the OPERATIONS, DELIVERY, and BUILD destinations. */
export default function HomeRoute() {
  const { t } = useLocale();
  return (
    <>
      <Stack.Screen options={{ title: t('Work') }} />
      <NavMenuScreen tab="work" />
    </>
  );
}
