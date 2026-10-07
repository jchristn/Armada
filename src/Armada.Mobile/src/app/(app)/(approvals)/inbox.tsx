import { Stack } from 'expo-router';
import { useLocale } from '../../../i18n/LocaleContext';
import { NeedsYouScreen } from '../../../screens/NeedsYouScreen';

/** /inbox: the dashboard's Needs You page (the same approvals center as the Approvals tab root). */
export default function InboxRoute() {
  const { t } = useLocale();
  return (
    <>
      <Stack.Screen options={{ title: t('Needs You') }} />
      <NeedsYouScreen testID="inbox" />
    </>
  );
}
