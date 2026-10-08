import type { ReactNode } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import { useTheme } from '../../theme/ThemeContext';
import { radius, spacing, useLargeText } from '../../theme/typography';
import { AppText } from './AppText';

/** A KPI tile (label, big value, optional detail chips). Tapping opens the related list. */
export function KpiCard({ label, value, children, onPress, accessibilityValue, testID }: {
  label: string;
  value: string | number;
  children?: ReactNode;
  onPress?: () => void;
  /** What the detail chips say, read after the label and value (a tappable card is one screen-reader element). */
  accessibilityValue?: string;
  testID?: string;
}) {
  const { colors } = useTheme();
  const content = (
    <View style={styles.inner}>
      <AppText variant="caption" muted>{label}</AppText>
      <AppText variant="title" testID={testID ? `${testID}-value` : undefined}>{String(value)}</AppText>
      {children ? <View style={styles.detail}>{children}</View> : null}
    </View>
  );
  const style = [styles.card, { backgroundColor: colors.surface, borderColor: colors.border }];
  if (!onPress) return <View testID={testID} style={style}>{content}</View>;
  return (
    <Pressable testID={testID} accessibilityRole="button" accessibilityLabel={`${label}, ${value}`} accessibilityValue={accessibilityValue ? { text: accessibilityValue } : undefined} onPress={onPress} style={({ pressed }) => [style, { opacity: pressed ? 0.75 : 1 }]}>
      {content}
    </Pressable>
  );
}

/**
 * Wraps KPI cards: two per row on phones, more on wide screens (each card is at least 150 dp wide). At large text
 * sizes the cards stack one per row, so labels like "Missions" are not broken mid-word in a half-width card.
 */
export function KpiGrid({ children }: { children: ReactNode }) {
  const large = useLargeText();
  return <View style={[styles.grid, large ? styles.stacked : null]} testID={large ? 'kpi-grid-stacked' : undefined}>{children}</View>;
}

const styles = StyleSheet.create({
  card: { flexGrow: 1, flexBasis: 150, minWidth: 140, borderWidth: StyleSheet.hairlineWidth, borderRadius: radius.md, padding: spacing.md },
  inner: { gap: spacing.xs },
  detail: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.xs, marginTop: spacing.xs },
  // No wrapping in a column: wrapped column lines are only as wide as their widest card, not the screen.
  stacked: { flexDirection: 'column', flexWrap: 'nowrap' },
  grid: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.sm, marginHorizontal: spacing.md, marginBottom: spacing.xl },
});
