import { Stack } from 'expo-router';
import { useLocale } from '../../../i18n/LocaleContext';
import { NeedsYouScreen } from '../../../screens/NeedsYouScreen';

/** The Approvals tab root: the approvals center (the dashboard's Needs You inbox with decisions in place). */
export default function ApprovalsRoute() {
  const { t } = useLocale();
  return (
    <>
      <Stack.Screen options={{ title: t('Approvals') }} />
      <NeedsYouScreen testID="approvals" />
    </>
  );
}
