import { useEffect, useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { useAuth } from '../auth/AuthContext';
import { AppText, Button, Icon, Screen } from '../components/ui';
import { useLocale } from '../i18n/LocaleContext';
import { spacing } from '../theme/typography';

/** Biometric unlock of the stored session (profiles with "Require Face ID / fingerprint"). */
export function UnlockScreen() {
  const { unlock, logout, activeProfile } = useAuth();
  const { t } = useLocale();
  const [failed, setFailed] = useState(false);
  const [busy, setBusy] = useState(false);

  async function attempt() {
    setBusy(true);
    const ok = await unlock(t('Unlock Armada'), t('Cancel'));
    setBusy(false);
    setFailed(!ok);
  }

  // Prompt once on arrival; the button retries.
  useEffect(() => {
    let cancelled = false;
    void unlock(t('Unlock Armada'), t('Cancel')).then((ok) => { if (!cancelled) setFailed(!ok); });
    return () => { cancelled = true; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  return (
    <Screen edges={['top', 'bottom', 'left', 'right']} testID="unlock">
      <View style={styles.center}>
        <Icon name="lock-closed-outline" size={48} color="primary" />
        <AppText variant="heading" accessibilityRole="header">{t('Armada is locked')}</AppText>
        <AppText muted style={styles.text}>{activeProfile?.name ?? ''}</AppText>
        {failed ? <AppText color="danger" accessibilityRole="alert">{t('Unlock failed or was cancelled.')}</AppText> : null}
        <Button testID="unlock-button" label={t('Unlock')} icon="finger-print-outline" onPress={() => void attempt()} busy={busy} />
        <Button label={t('Sign out')} variant="ghost" onPress={() => void logout()} />
      </View>
    </Screen>
  );
}

/** The stored session could not be checked because the server did not answer. */
export function UnreachableScreen() {
  const { retry, logout, activeProfile } = useAuth();
  const { t } = useLocale();
  const [busy, setBusy] = useState(false);
  return (
    <Screen edges={['top', 'bottom', 'left', 'right']} testID="unreachable">
      <View style={styles.center}>
        <Icon name="cloud-offline-outline" size={48} color="warning" />
        <AppText variant="heading" accessibilityRole="header">{t('Cannot reach the server')}</AppText>
        <AppText muted style={styles.text}>{activeProfile?.url ?? ''}</AppText>
        <Button testID="unreachable-retry" label={t('Retry')} onPress={async () => { setBusy(true); await retry(); setBusy(false); }} busy={busy} />
        <Button label={t('Sign out')} variant="ghost" onPress={() => void logout()} />
      </View>
    </Screen>
  );
}

const styles = StyleSheet.create({
  center: { flex: 1, alignItems: 'center', justifyContent: 'center', gap: spacing.md, padding: spacing.xl },
  text: { textAlign: 'center' },
});
