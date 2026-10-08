import { Pressable, StyleSheet, View } from 'react-native';
import type { TurnStatistic } from '@dashboard/lib/chatMetrics';
import { useLocale } from '../../i18n/LocaleContext';
import { useTheme } from '../../theme/ThemeContext';
import { radius, spacing, touchSlop } from '../../theme/typography';
import { AppText } from '../ui/AppText';
import { Icon } from '../ui/Icon';

const ICON_SIZE = 18;

/**
 * The (i) control in a captain reply's header that shows or hides its turn statistics (the dashboard's
 * ChatMetricsInfo button). Small to sit in the header line, with a full-size touch area.
 */
export function TurnStatsToggle({ open, onToggle, testID }: { open: boolean; onToggle: () => void; testID?: string }) {
  const { t } = useLocale();
  return (
    <Pressable
      testID={testID}
      accessibilityRole="button"
      accessibilityLabel={t('Turn statistics')}
      accessibilityState={{ expanded: open }}
      onPress={onToggle}
      hitSlop={touchSlop(ICON_SIZE)}
      style={({ pressed }) => ({ opacity: pressed ? 0.6 : 1 })}
    >
      <Icon name={open ? 'information-circle' : 'information-circle-outline'} size={ICON_SIZE} color={open ? 'primary' : 'textMuted'} />
    </Pressable>
  );
}

/**
 * A reply's turn statistics (shared rows from lib/chatMetrics: time to first token, streaming, tokens/sec, tokens,
 * total, tool calls, tool time, as the turn has them), laid out for a phone: a two-column grid of value-over-label
 * cells inside the reply instead of the dashboard's hover popover. Each cell reads as "label: value".
 */
export function TurnStatsPanel({ rows, testID }: { rows: TurnStatistic[]; testID?: string }) {
  const { t } = useLocale();
  const { colors } = useTheme();
  return (
    <View
      testID={testID}
      accessibilityLabel={t('Turn statistics')}
      style={[styles.panel, { borderColor: colors.border, backgroundColor: colors.background }]}
    >
      {rows.map((row) => (
        <View key={row.key} style={styles.cell} accessible accessibilityLabel={`${row.label}: ${row.value}`} testID={testID ? `${testID}-${row.key}` : undefined}>
          <AppText variant="label" style={styles.value}>{row.value}</AppText>
          <AppText variant="caption" muted>{row.label}</AppText>
        </View>
      ))}
    </View>
  );
}

const styles = StyleSheet.create({
  panel: { flexDirection: 'row', flexWrap: 'wrap', borderWidth: StyleSheet.hairlineWidth, borderRadius: radius.md, paddingVertical: spacing.xs, paddingHorizontal: spacing.sm },
  cell: { width: '50%', paddingVertical: spacing.xs, paddingRight: spacing.sm },
  value: { fontVariant: ['tabular-nums'] },
});
