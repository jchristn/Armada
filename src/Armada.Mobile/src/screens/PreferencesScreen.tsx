import { StyleSheet, View } from 'react-native';
import Constants from 'expo-constants';
import { LocalePicker } from '../components/app/LocalePicker';
import { SignInSecuritySection } from '../components/app/SignInSecurity';
import { ThemePicker } from '../components/app/ThemePicker';
import { ListRow, Screen, Section } from '../components/ui';
import { NotificationSettingsSection } from '../push/NotificationSettingsSection';
import { useLocale } from '../i18n/LocaleContext';
import { spacing } from '../theme/typography';

/**
 * Device preferences: theme, language, sign-in and security (Face ID app lock and saved password for the connected
 * server), push notifications for the connected server, and app information. Server settings live under Settings (W4.4).
 */
export function PreferencesScreen() {
  const { t, catalogSource } = useLocale();
  const version = Constants.expoConfig?.version ?? '';
  return (
    <Screen testID="preferences">
      <Section title={t('Theme')}>
        <View style={styles.pad}>
          <ThemePicker />
        </View>
      </Section>
      <Section title={t('Language')} footer={catalogSource === 'server' ? t('Translations come from the connected server.') : undefined}>
        <LocalePicker />
      </Section>
      <SignInSecuritySection />
      <NotificationSettingsSection />
      <Section title={t('About')}>
        <ListRow title={t('Version')} accessory={null} subtitle={version} />
      </Section>
    </Screen>
  );
}

const styles = StyleSheet.create({
  pad: { padding: spacing.md, paddingBottom: 0 },
});
