import { useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { changePassword } from '@dashboard/api/client';
import { useAuth } from '../auth/AuthContext';
import { useSignOut } from '../components/app/useSignOut';
import { AppText, Button, ConfirmDialog, Screen, TextField } from '../components/ui';
import { useLocale } from '../i18n/LocaleContext';
import { spacing } from '../theme/typography';

/**
 * Shown instead of the app while the signed-in account still uses the default password (the server's
 * PasswordChangeRequired). Mirrors the dashboard's PasswordChangeRequired: change it here, or skip after confirming
 * the risk; the default credentials banner stays visible until the password changes.
 */
export function PasswordChangeScreen() {
  const { refresh, skipPasswordChange, updateSavedPassword } = useAuth();
  const { signOut, signOutSheet } = useSignOut();
  const { t } = useLocale();
  const [current, setCurrent] = useState('');
  const [next, setNext] = useState('');
  const [confirm, setConfirm] = useState('');
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const [confirmSkip, setConfirmSkip] = useState(false);

  async function submit() {
    setError('');
    if (next.length < 8) {
      setError(t('The new password must be at least 8 characters.'));
      return;
    }
    if (next !== confirm) {
      setError(t('The new passwords do not match.'));
      return;
    }
    if (next === 'password' || next === current) {
      setError(t('Choose a password different from the default and the current one.'));
      return;
    }
    setBusy(true);
    try {
      await changePassword({ CurrentPassword: current, NewPassword: next });
      // A password saved for Face ID sign-in would be rejected from now on: replace it with the new one.
      await updateSavedPassword(next);
      await refresh();
    } catch {
      setError(t('Password change failed. Check the current password and try again.'));
    } finally {
      setBusy(false);
    }
  }

  return (
    <Screen edges={['top', 'bottom', 'left', 'right']} testID="password-change">
      <View style={styles.pad}>
        <AppText variant="title" accessibilityRole="header" style={styles.title}>{t('Change the default password')}</AppText>
        <AppText muted style={styles.explainer}>
          {t('This account still uses the default password. Choose a new one, or skip for now. Changing it also disables the default bearer token.')}
        </AppText>
        {error ? <AppText color="danger" accessibilityRole="alert" accessibilityLiveRegion="assertive" style={styles.error} testID="password-change-error">{error}</AppText> : null}
        <TextField testID="password-current" label={t('Current password')} value={current} onChangeText={setCurrent} secret textContentType="password" autoComplete="current-password" revealLabel={t('Show password')} hideLabel={t('Hide password')} />
        <TextField testID="password-new" label={t('New password')} value={next} onChangeText={setNext} secret textContentType="newPassword" autoComplete="new-password" revealLabel={t('Show password')} hideLabel={t('Hide password')} />
        <TextField testID="password-confirm" label={t('Confirm new password')} value={confirm} onChangeText={setConfirm} secret textContentType="newPassword" autoComplete="new-password" revealLabel={t('Show password')} hideLabel={t('Hide password')} />
        <Button testID="password-change-submit" label={busy ? t('Saving...') : t('Change password')} onPress={() => void submit()} busy={busy} disabled={!current || !next || !confirm} />
        <Button testID="password-change-skip" label={t('Skip for now')} variant="ghost" onPress={() => setConfirmSkip(true)} />
        <Button testID="password-change-sign-out" label={t('Sign out')} variant="ghost" onPress={signOut} />
      </View>
      <ConfirmDialog
        testID="password-skip-confirm"
        open={confirmSkip}
        title={t('Keep the default password?')}
        message={t('Anyone who can reach this server can sign in as admin@armada with the well-known default password, and the default bearer token stays active. Only skip on a server that is reachable from this machine alone (localhost). You can change it later by editing your own account on the Users page.')}
        confirmLabel={t('Skip and continue')}
        cancelLabel={t('Cancel')}
        danger
        onConfirm={() => { setConfirmSkip(false); void skipPasswordChange(); }}
        onCancel={() => setConfirmSkip(false)}
      />
      {signOutSheet}
    </Screen>
  );
}

const styles = StyleSheet.create({
  pad: { paddingHorizontal: spacing.lg },
  title: { marginBottom: spacing.md },
  explainer: { marginBottom: spacing.lg },
  error: { marginBottom: spacing.md },
});
