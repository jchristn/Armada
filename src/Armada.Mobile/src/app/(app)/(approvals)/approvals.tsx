import { Stack } from 'expo-router';
import { useLocale } from '../../../i18n/LocaleContext';
import { ApprovalsScreen } from '../../../screens/ApprovalsScreen';

export default function ApprovalsRoute() {
  const { t } = useLocale();
  return (
    <>
      <Stack.Screen options={{ title: t('Approvals') }} />
      <ApprovalsScreen />
    </>
  );
}
