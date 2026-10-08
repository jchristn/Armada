import { useEffect, useRef } from 'react';
import { StyleSheet, View } from 'react-native';
import { biometricName, type BiometricSupport } from '../../auth/biometrics';
import { useLocale } from '../../i18n/LocaleContext';
import { useTheme } from '../../theme/ThemeContext';
import { radius, spacing } from '../../theme/typography';
import { AppText, Button, SwitchRow } from '../ui';

/**
 * "Save password and use Face ID" on a password step. Shown only when biometrics are enrolled and the keychain /
 * keystore can protect the item with them.
 */
export function SavePasswordSwitch({ support, value, onChange, testID }: {
  support: BiometricSupport | null;
  value: boolean;
  onChange: (value: boolean) => void;
  testID: string;
}) {
  const { t } = useLocale();
  if (!support?.canSavePassword) return null;
  const method = biometricName(support.kind, t);
  return (
    <SwitchRow
      testID={testID}
      label={t('Save password and use {{method}}', { method })}
      hint={t('Next time, sign in with {{method}} instead of typing the password. It is kept in this device\'s keychain.', { method })}
      value={value}
      onChange={onChange}
    />
  );
}

/** The prominent "Sign in with Face ID" action when a password is saved for this profile. */
export function BiometricSignInCard({ support, title, subtitle, busy, onPress, testID }: {
  support: BiometricSupport;
  title: string;
  subtitle: string | null;
  busy: boolean;
  onPress: () => void;
  testID: string;
}) {
  const { colors } = useTheme();
  const { t } = useLocale();
  const method = biometricName(support.kind, t);
  return (
    <View style={[styles.card, { borderColor: colors.border, backgroundColor: colors.surface }]} testID={`${testID}-card`}>
      <Button
        testID={testID}
        label={title}
        icon={support.kind === 'faceId' ? 'scan-outline' : 'finger-print-outline'}
        onPress={onPress}
        busy={busy}
        accessibilityHint={t('Uses {{method}} to read your saved password', { method })}
      />
      {subtitle ? <AppText variant="caption" muted style={styles.subtitle}>{subtitle}</AppText> : null}
      <AppText variant="caption" muted style={styles.or}>{t('or sign in with your password below')}</AppText>
    </View>
  );
}

/**
 * Run `run` once for each distinct non-null `key` (the automatic biometric prompt when the sign-in screen opens for a
 * profile with a saved password). A null key means "do not prompt now".
 */
export function useAutoPromptOnce(key: string | null, run: () => void) {
  const done = useRef<Set<string>>(new Set());
  const runRef = useRef(run);
  useEffect(() => { runRef.current = run; });
  useEffect(() => {
    if (!key || done.current.has(key)) return;
    done.current.add(key);
    runRef.current();
  }, [key]);
}

const styles = StyleSheet.create({
  card: { borderWidth: StyleSheet.hairlineWidth, borderRadius: radius.md, padding: spacing.md, marginBottom: spacing.lg, gap: spacing.xs },
  subtitle: { textAlign: 'center' },
  or: { textAlign: 'center', marginTop: spacing.xs },
});
