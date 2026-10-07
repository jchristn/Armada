import { useState } from 'react';
import { Linking, StyleSheet, Switch, View } from 'react-native';
import { AppText, Button, ListRow, Section } from '../components/ui';
import { useLocale } from '../i18n/LocaleContext';
import { useTheme } from '../theme/ThemeContext';
import { spacing } from '../theme/typography';
import { CATEGORY_TEXT } from './categoryLabels';
import { usePush } from './PushContext';
import { PUSH_CATEGORIES, type PushCategory } from './types';

/**
 * Push notification settings for the active server: OS permission, this device's registration with the server, the
 * categories it receives (the server's PushCategoryEnum), and a test push.
 */
export function NotificationSettingsSection() {
  const { permission, registration, requestPermission, setCategoryEnabled, sendTest } = usePush();
  const { t } = useLocale();
  const { colors } = useTheme();
  const [busy, setBusy] = useState<PushCategory | 'test' | 'permission' | null>(null);
  const [testMessage, setTestMessage] = useState<string | null>(null);

  const registered = registration?.state === 'registered' ? registration.record : null;

  let status: string;
  if (permission === 'denied') status = t('Notifications are turned off for Armada in system settings.');
  else if (permission !== 'granted') status = t('Notifications are not turned on yet.');
  else if (registered) status = t('This device receives notifications from this server.');
  else if (registration?.state === 'unavailable' && registration.reason === 'noProject') {
    status = t('This build has no push project configured (EAS project id), so the server cannot reach this device.');
  } else if (registration?.state === 'unavailable') status = t('This device did not get a push token. Try again later.');
  else if (registration?.state === 'error') status = t('The server did not accept this device. Try again later.');
  else status = t('Registering this device with the server...');

  async function enable() {
    setBusy('permission');
    try {
      if (permission === 'denied') await Linking.openSettings();
      else await requestPermission();
    } finally {
      setBusy(null);
    }
  }

  async function toggle(category: PushCategory, enabled: boolean) {
    setBusy(category);
    try {
      await setCategoryEnabled(category, enabled);
    } finally {
      setBusy(null);
    }
  }

  async function test() {
    setBusy('test');
    setTestMessage(null);
    try {
      const result = await sendTest();
      if (!result) setTestMessage(t('The test notification could not be requested.'));
      else if (result.status === 'Sent') setTestMessage(t('Sent. It should arrive in a few seconds.'));
      else if (result.status === 'Disabled') setTestMessage(t('Push notifications are turned off on the server.'));
      else if (result.status === 'RateLimited') setTestMessage(t('Too many notifications in the last minute. Try again shortly.'));
      else setTestMessage(t('The push service did not accept the notification.'));
    } finally {
      setBusy(null);
    }
  }

  return (
    <Section title={t('Notifications')} footer={registered ? t('Choices apply to this device and this server only.') : undefined}>
      <View style={styles.pad} testID="push-settings">
        <AppText muted testID="push-status">{status}</AppText>
        {permission !== 'granted' && permission !== 'unknown' ? (
          <Button
            testID="push-enable"
            label={permission === 'denied' ? t('Open system settings') : t('Turn on notifications')}
            icon="notifications-outline"
            variant="secondary"
            busy={busy === 'permission'}
            onPress={() => void enable()}
          />
        ) : null}
      </View>
      {registered ? (
        <>
          {PUSH_CATEGORIES.map((category) => {
            const enabled = registered.categories.includes(category);
            const text = CATEGORY_TEXT[category];
            return (
              <ListRow
                key={category}
                testID={`push-category-${category}`}
                title={t(text.label)}
                subtitle={t(text.description)}
                accessory={(
                  <Switch
                    testID={`push-category-switch-${category}`}
                    value={enabled}
                    disabled={busy !== null}
                    onValueChange={(value) => void toggle(category, value)}
                    accessibilityLabel={t(text.label)}
                    trackColor={{ true: colors.primary, false: colors.border }}
                  />
                )}
              />
            );
          })}
          <View style={styles.pad}>
            <Button testID="push-test" label={t('Send a test notification')} variant="secondary" busy={busy === 'test'} onPress={() => void test()} />
            {testMessage ? <AppText muted testID="push-test-result" accessibilityLiveRegion="polite">{testMessage}</AppText> : null}
          </View>
        </>
      ) : null}
    </Section>
  );
}

const styles = StyleSheet.create({
  pad: { padding: spacing.md, gap: spacing.sm },
});
