import type { ReactNode } from 'react';
import { StyleSheet, View } from 'react-native';
import { useTheme } from '../../theme/ThemeContext';
import { radius, spacing } from '../../theme/typography';
import { AppText } from './AppText';

/** A titled group of rows (the dashboard's nav section labels: OPERATIONS, BUILD, ...). */
export function Section({ title, footer, children }: { title?: string; footer?: string; children: ReactNode }) {
  const { colors } = useTheme();
  return (
    <View style={styles.wrap}>
      {title ? <AppText variant="subheading" muted accessibilityRole="header" style={styles.title}>{title}</AppText> : null}
      <View style={[styles.card, { backgroundColor: colors.surface, borderColor: colors.border }]}>{children}</View>
      {footer ? <AppText variant="caption" muted style={styles.footer}>{footer}</AppText> : null}
    </View>
  );
}

const styles = StyleSheet.create({
  wrap: { marginBottom: spacing.xl },
  title: { marginHorizontal: spacing.lg, marginBottom: spacing.sm, textTransform: 'uppercase' },
  card: { borderRadius: radius.md, borderWidth: StyleSheet.hairlineWidth, overflow: 'hidden', marginHorizontal: spacing.md },
  footer: { marginHorizontal: spacing.lg, marginTop: spacing.sm },
});
