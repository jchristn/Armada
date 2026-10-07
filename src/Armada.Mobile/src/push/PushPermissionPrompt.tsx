import { StyleSheet, View } from 'react-native';
import { AppText, BottomSheet, Button } from '../components/ui';
import { useLocale } from '../i18n/LocaleContext';
import { spacing } from '../theme/typography';
import { usePush } from './PushContext';

/**
 * The one-time explanation before the OS notification prompt, shown once the user is signed in and in the app (never
 * at launch, never on the sign-in screen). "Not now" is remembered; Preferences can turn notifications on later.
 */
export function PushPermissionPrompt() {
  const { promptVisible, dismissPrompt, requestPermission } = usePush();
  const { t } = useLocale();
  return (
    <BottomSheet open={promptVisible} title={t('Get notified when Armada needs you')} onClose={dismissPrompt} closeLabel={t('Not now')} testID="push-prompt">
      <View style={styles.body}>
        <AppText muted>
          {t('Approvals, permission requests, reviews, and failures can reach this device even when the app is closed. Approve or deny right from the notification. Choose exactly which ones in Preferences.')}
        </AppText>
        <Button
          testID="push-prompt-enable"
          label={t('Turn on notifications')}
          icon="notifications-outline"
          onPress={() => { dismissPrompt(); void requestPermission(); }}
        />
        <Button testID="push-prompt-later" label={t('Not now')} variant="ghost" onPress={dismissPrompt} />
      </View>
    </BottomSheet>
  );
}

const styles = StyleSheet.create({
  body: { gap: spacing.md, paddingBottom: spacing.lg },
});
