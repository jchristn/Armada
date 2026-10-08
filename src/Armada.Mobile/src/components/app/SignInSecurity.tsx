import { useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { useAuth } from '../../auth/AuthContext';
import { biometricName, useBiometricSupport } from '../../auth/biometrics';
import { useLocale } from '../../i18n/LocaleContext';
import type { ServerProfile } from '../../profiles/types';
import { spacing } from '../../theme/typography';
import { AppText, Button, Section, SwitchRow } from '../ui';

/**
 * "Saved password for Face ID sign-in" for one profile: whether a password is saved (and for whom), what it does in
 * one line, and "Forget saved password". Independent of the app lock ("Unlock with Face ID").
 */
export function SavedPasswordRow({ profile, method, onForget, testID }: {
  profile: ServerProfile;
  method: string;
  onForget: () => Promise<void>;
  testID: string;
}) {
  const { t } = useLocale();
  const [busy, setBusy] = useState(false);
  const admiral = profile.savedSignIn ?? null;
  const proxy = !!profile.proxyPasswordSaved;
  const saved = !!admiral || proxy;
  const parts: string[] = [];
  if (admiral) parts.push(t('Saved for {{email}}', { email: admiral.email }));
  if (proxy) parts.push(t('Armada.Proxy password saved'));
  return (
    <View style={styles.block} testID={testID}>
      <AppText variant="label">{t('Saved password for {{method}} sign-in', { method })}</AppText>
      <AppText variant="caption" testID={`${testID}-status`}>{saved ? parts.join('; ') : t('Not saved')}</AppText>
      <AppText variant="caption" muted>
        {saved
          ? t('Signs you in with {{method}} after you sign out or your session expires.', { method })
          : t('Turn on "Save password and use {{method}}" the next time you sign in.', { method })}
      </AppText>
      {saved ? (
        <Button
          testID={`${testID}-forget`}
          label={t('Forget saved password')}
          variant="ghost"
          busy={busy}
          onPress={() => {
            setBusy(true);
            void onForget().finally(() => setBusy(false));
          }}
        />
      ) : null}
    </View>
  );
}

/** Preferences: the active profile's app lock and saved password, side by side and each explained. */
export function SignInSecuritySection() {
  const { activeProfile, saveProfile, forgetSavedPassword } = useAuth();
  const { t } = useLocale();
  const support = useBiometricSupport();
  if (!activeProfile || !support) return null;
  const method = biometricName(support.kind, t);
  const hasSaved = !!activeProfile.savedSignIn || !!activeProfile.proxyPasswordSaved;
  return (
    <Section title={t('Sign-in and security')} footer={t('These settings apply to {{name}}. Edit other servers under Servers.', { name: activeProfile.name })}>
      <View style={styles.pad} testID="sign-in-security">
        {support.enrolled ? (
          <SwitchRow
            testID="prefs-app-lock"
            label={t('Unlock with {{method}}', { method })}
            hint={t('Asks for {{method}} when Armada opens and after 5 minutes in the background.', { method })}
            value={activeProfile.biometricUnlock}
            onChange={(value) => {
              void saveProfile({ name: activeProfile.name, url: activeProfile.url, kind: activeProfile.kind, biometricUnlock: value }, activeProfile.id);
            }}
          />
        ) : (
          <AppText variant="caption" muted style={styles.note} testID="sign-in-security-unavailable">
            {t('Set up Face ID, Touch ID, or fingerprint on this device to unlock and sign in with biometrics.')}
          </AppText>
        )}
        {support.canSavePassword || hasSaved ? (
          <SavedPasswordRow
            testID="prefs-saved-password"
            profile={activeProfile}
            method={method}
            onForget={() => forgetSavedPassword(activeProfile.id)}
          />
        ) : null}
      </View>
    </Section>
  );
}

const styles = StyleSheet.create({
  pad: { padding: spacing.md, paddingBottom: spacing.sm },
  block: { gap: 2, marginBottom: spacing.md },
  note: { marginBottom: spacing.md },
});
