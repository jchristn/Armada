import { Alert, Pressable, StyleSheet } from 'react-native';
import { useAuth } from '../../auth/AuthContext';
import { useLocale } from '../../i18n/LocaleContext';
import { urlSecurityLevel } from '../../profiles/serverUrl';
import { MIN_TOUCH, spacing } from '../../theme/typography';
import { AppText } from '../ui/AppText';
import { Icon } from '../ui/Icon';

export interface NotEncryptedBadgeProps {
  /** The signed-in server's URL (the proxy's for a proxy profile), or null. */
  url: string | null | undefined;
  /** Icon only (phone headers); the sidebar shows the words too. */
  compact?: boolean;
}

/**
 * A small, persistent "Not encrypted" mark for a session over plain http://, so the user is reminded after sign-in,
 * not just on the sign-in screen. Tapping it explains what travels in plain text. Nothing for https.
 */
export function NotEncryptedBadge({ url, compact = false }: NotEncryptedBadgeProps) {
  const { t } = useLocale();
  if (!url) return null;
  const level = urlSecurityLevel(url);
  if (level === 'secure') return null;
  const color = level === 'public' ? 'danger' : 'warning';
  const explain = () => Alert.alert(
    t('This connection is not encrypted'),
    level === 'public'
      ? t('Your password and session token are sent in plain text over the internet. Use an https:// address for any server outside your local network.')
      : t('Plain HTTP is acceptable only on a network you trust (for example your home or office LAN). Use https:// everywhere else.'),
  );
  return (
    <Pressable
      testID="not-encrypted-indicator"
      accessibilityRole="button"
      accessibilityLabel={t('Not encrypted')}
      accessibilityHint={t('Explains what an http:// connection exposes')}
      onPress={explain}
      hitSlop={4}
      style={[styles.base, compact ? styles.compact : null]}
    >
      <Icon name="lock-open-outline" size={compact ? 22 : 16} color={color} />
      {compact ? null : <AppText variant="caption" color={color}>{t('Not encrypted')}</AppText>}
    </Pressable>
  );
}

/** NotEncryptedBadge for the active profile while signed in. */
export function NotEncryptedIndicator({ compact = false }: { compact?: boolean }) {
  const { status, activeProfile } = useAuth();
  if (status !== 'signedIn') return null;
  return <NotEncryptedBadge url={activeProfile?.url} compact={compact} />;
}

const styles = StyleSheet.create({
  base: { flexDirection: 'row', alignItems: 'center', gap: spacing.xs },
  compact: { minWidth: MIN_TOUCH, minHeight: MIN_TOUCH, justifyContent: 'center' },
});
