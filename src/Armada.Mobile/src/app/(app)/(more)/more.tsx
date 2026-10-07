import { Stack } from 'expo-router';
import { useLocale } from '../../../i18n/LocaleContext';
import { NavMenuScreen } from '../../../screens/NavMenuScreen';

export default function MoreRoute() {
  const { t } = useLocale();
  return (
    <>
      <Stack.Screen options={{ title: t('More') }} />
      <NavMenuScreen tab="more" />
    </>
  );
}
