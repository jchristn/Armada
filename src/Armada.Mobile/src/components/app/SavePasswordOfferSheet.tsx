import { useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { useAuth } from '../../auth/AuthContext';
import { biometricName, useBiometricSupport } from '../../auth/biometrics';
import { useLocale } from '../../i18n/LocaleContext';
import { spacing } from '../../theme/typography';
import { AppText, BottomSheet, Button } from '../ui';

/**
 * After a password sign-in without "Save password and use Face ID": asks once per profile whether to save it. "Not
 * now" (or closing the sheet) is remembered on the profile, so the offer does not come back.
 */
export function SavePasswordOfferSheet() {
  const { savePasswordOffer, acceptSavePasswordOffer, declineSavePasswordOffer } = useAuth();
  const { t } = useLocale();
  const support = useBiometricSupport();
  const [busy, setBusy] = useState(false);
  if (!savePasswordOffer || !support?.canSavePassword) return null;
  const method = biometricName(support.kind, t);

  async function accept() {
    setBusy(true);
    try {
      await acceptSavePasswordOffer(t('Save your password for {{method}} sign-in', { method }));
    } finally {
      setBusy(false);
    }
  }

  return (
    <BottomSheet
      open
      title={t('Use {{method}} next time?', { method })}
      onClose={() => { void declineSavePasswordOffer(); }}
      closeLabel={t('Not now')}
      testID="save-password-offer"
    >
      <View style={styles.body}>
        <AppText muted>
          {t('Save your password for {{name}} on this device and sign in with {{method}} after you sign out or your session expires.', { name: savePasswordOffer.profileName, method })}
        </AppText>
        <Button testID="save-password-offer-accept" label={t('Use {{method}}', { method })} onPress={() => void accept()} busy={busy} />
        <Button testID="save-password-offer-decline" label={t('Not now')} variant="ghost" onPress={() => void declineSavePasswordOffer()} />
      </View>
    </BottomSheet>
  );
}

const styles = StyleSheet.create({
  body: { gap: spacing.md, paddingBottom: spacing.md },
});
