import { ActivityIndicator, StyleSheet, View } from 'react-native';
import { useTheme } from '../../theme/ThemeContext';
import { spacing } from '../../theme/typography';
import { AppText } from './AppText';
import { Button } from './Button';
import { Icon, type IconName } from './Icon';

export function LoadingState({ label }: { label: string }) {
  const { colors } = useTheme();
  return (
    <View style={styles.center} accessible accessibilityRole="progressbar" accessibilityLabel={label} accessibilityState={{ busy: true }}>
      <ActivityIndicator size="large" color={colors.primary} />
      <AppText muted>{label}</AppText>
    </View>
  );
}

export function EmptyState({ title, message, icon = 'file-tray-outline', actionLabel, onAction }: {
  title: string;
  message?: string;
  icon?: IconName;
  actionLabel?: string;
  onAction?: () => void;
}) {
  return (
    <View style={styles.center}>
      <Icon name={icon} size={40} color="textMuted" />
      <AppText variant="heading" accessibilityRole="header" style={styles.textCenter}>{title}</AppText>
      {message ? <AppText muted style={styles.textCenter}>{message}</AppText> : null}
      {actionLabel && onAction ? <Button label={actionLabel} onPress={onAction} variant="secondary" /> : null}
    </View>
  );
}

export function ErrorState({ title, message, retryLabel, onRetry }: {
  title: string;
  message?: string | null;
  retryLabel?: string;
  onRetry?: () => void;
}) {
  return (
    <View style={styles.center} accessibilityRole="alert">
      <Icon name="alert-circle-outline" size={40} color="danger" />
      <AppText variant="heading" style={styles.textCenter}>{title}</AppText>
      {message ? <AppText muted style={styles.textCenter}>{message}</AppText> : null}
      {retryLabel && onRetry ? <Button label={retryLabel} onPress={onRetry} /> : null}
    </View>
  );
}

const styles = StyleSheet.create({
  center: { flex: 1, alignItems: 'center', justifyContent: 'center', gap: spacing.md, padding: spacing.xl },
  textCenter: { textAlign: 'center' },
});
