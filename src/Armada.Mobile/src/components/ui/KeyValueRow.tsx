import type { ReactNode } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import { useTheme } from '../../theme/ThemeContext';
import { MIN_TOUCH, spacing, useLargeText } from '../../theme/typography';
import { AppText } from './AppText';
import { Icon } from './Icon';

export interface KeyValueRowProps {
  label: string;
  /** Text value; '-' is shown for empty values. Ignored when `children` is given. */
  value?: string | number | null;
  children?: ReactNode;
  /** Monospace (ids, branches, paths). */
  mono?: boolean;
  /** Makes the value a link (opens the related item). */
  onPress?: () => void;
  testID?: string;
}

/**
 * One labelled field of a detail screen. Text values are selectable (long-press to copy), which replaces the
 * dashboard's copy buttons without a clipboard dependency.
 */
export function KeyValueRow({ label, value, children, mono, onPress, testID }: KeyValueRowProps) {
  const { colors } = useTheme();
  // At large text sizes the label sits above the value: a 120 dp label column would break both into word fragments.
  const large = useLargeText();
  const text = value === null || value === undefined || value === '' ? '-' : String(value);
  const body = children ?? (
    <AppText selectable variant={mono ? 'mono' : 'body'} color={onPress ? 'primary' : 'text'} testID={testID ? `${testID}-value` : undefined}>
      {text}
    </AppText>
  );
  const content = (
    <View style={[styles.row, large ? styles.stacked : null]}>
      <AppText variant="caption" muted style={large ? null : styles.label}>{label}</AppText>
      <View style={large ? null : styles.value}>{body}</View>
      {onPress ? <Icon name="chevron-forward" size={16} color="textMuted" /> : null}
    </View>
  );
  if (onPress) {
    return (
      <Pressable
        testID={testID}
        accessibilityRole="link"
        accessibilityLabel={`${label}, ${text}`}
        onPress={onPress}
        style={({ pressed }) => [styles.base, { borderBottomColor: colors.border, opacity: pressed ? 0.7 : 1 }]}
      >
        {content}
      </Pressable>
    );
  }
  return <View testID={testID} style={[styles.base, { borderBottomColor: colors.border }]}>{content}</View>;
}

const styles = StyleSheet.create({
  base: { minHeight: MIN_TOUCH, paddingHorizontal: spacing.lg, paddingVertical: spacing.sm, justifyContent: 'center', borderBottomWidth: StyleSheet.hairlineWidth },
  row: { flexDirection: 'row', alignItems: 'center', gap: spacing.md },
  stacked: { flexDirection: 'column', alignItems: 'stretch', gap: spacing.xs },
  label: { width: 120 },
  value: { flex: 1 },
});
