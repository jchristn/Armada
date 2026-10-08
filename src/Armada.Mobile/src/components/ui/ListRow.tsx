import type { ReactNode } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import { useTheme } from '../../theme/ThemeContext';
import { MIN_TOUCH, spacing } from '../../theme/typography';
import { AppText } from './AppText';
import { Icon, type IconName } from './Icon';
import { useCompactRows } from './paneWidth';

export interface ListRowProps {
  title: string;
  subtitle?: string | null;
  icon?: IconName;
  /** Right-hand content (value text, badge). */
  accessory?: ReactNode;
  /** Shows a chevron and makes the row a button. */
  onPress?: () => void;
  onLongPress?: () => void;
  selected?: boolean;
  destructive?: boolean;
  accessibilityHint?: string;
  /** Extra words for screen readers (for example a badge count). */
  accessibilityValue?: string;
  testID?: string;
}

export function ListRow({ title, subtitle, icon, accessory, onPress, onLongPress, selected, destructive, accessibilityHint, accessibilityValue, testID }: ListRowProps) {
  const { colors } = useTheme();
  // Narrow panes (a phone, or a list beside its detail) put the badge under the text so the title keeps its width.
  const compact = useCompactRows();
  const content = (
    <View style={styles.row}>
      {icon ? <Icon name={icon} color={destructive ? 'danger' : 'primary'} /> : null}
      <View style={styles.text}>
        <AppText variant="label" color={destructive ? 'danger' : 'text'} numberOfLines={compact ? 3 : undefined}>{title}</AppText>
        {subtitle ? <AppText variant="caption" muted numberOfLines={compact ? 3 : 2}>{subtitle}</AppText> : null}
        {compact && accessory ? <View style={styles.stacked} testID={testID ? `${testID}-accessory-stacked` : undefined}>{accessory}</View> : null}
      </View>
      {compact ? null : accessory}
      {onPress ? <Icon name="chevron-forward" size={18} color="textMuted" /> : null}
    </View>
  );
  if (!onPress) {
    return (
      <View testID={testID} style={[styles.base, { borderBottomColor: colors.border }]} accessible accessibilityLabel={[title, subtitle, accessibilityValue].filter(Boolean).join(', ')}>
        {content}
      </View>
    );
  }
  return (
    <Pressable
      testID={testID}
      accessibilityRole="button"
      accessibilityLabel={[title, subtitle].filter(Boolean).join(', ')}
      accessibilityValue={accessibilityValue ? { text: accessibilityValue } : undefined}
      accessibilityHint={accessibilityHint}
      accessibilityState={{ selected: !!selected }}
      onPress={onPress}
      onLongPress={onLongPress}
      style={({ pressed }) => [
        styles.base,
        { borderBottomColor: colors.border, backgroundColor: selected ? colors.surfaceRaised : pressed ? colors.background : colors.surface },
        selected ? { borderLeftWidth: 3, borderLeftColor: colors.primary } : null,
      ]}
    >
      {content}
    </Pressable>
  );
}

const styles = StyleSheet.create({
  base: { minHeight: MIN_TOUCH + 8, paddingHorizontal: spacing.lg, paddingVertical: spacing.sm, justifyContent: 'center', borderBottomWidth: StyleSheet.hairlineWidth },
  row: { flexDirection: 'row', alignItems: 'center', gap: spacing.md },
  text: { flex: 1, gap: 2 },
  stacked: { alignSelf: 'flex-start', marginTop: 2 },
});
