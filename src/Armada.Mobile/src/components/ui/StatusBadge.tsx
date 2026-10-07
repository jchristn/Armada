import { StyleSheet, View } from 'react-native';
import type { BadgeTone } from '@dashboard/lib/badgeTypes';
import type { Severity } from '@dashboard/lib/notificationSeverity';
import { useTheme } from '../../theme/ThemeContext';
import { CHROME_MAX_FONT_SCALE, radius, spacing } from '../../theme/typography';
import type { Palette } from '../../theme/palette';
import { AppText } from './AppText';

/** Tone of a status badge: the dashboard's badge tones plus notification severities. */
export type StatusTone = BadgeTone | Severity;

export function toneColor(tone: StatusTone): keyof Palette {
  switch (tone) {
    case 'success': return 'success';
    case 'failed':
    case 'error': return 'danger';
    case 'warning': return 'warning';
    case 'running':
    case 'info': return 'info';
    default: return 'textMuted';
  }
}

/** A labelled status pill; color is never the only signal (the label is always shown and spoken). */
export function StatusBadge({ label, tone }: { label: string; tone: StatusTone }) {
  const { colors } = useTheme();
  const c = colors[toneColor(tone)];
  return (
    <View style={[styles.pill, { borderColor: c }]} accessible accessibilityLabel={label}>
      <View style={[styles.dot, { backgroundColor: c }]} />
      <AppText variant="caption" maxFontSizeMultiplier={CHROME_MAX_FONT_SCALE} style={{ color: c, fontWeight: '600' }}>{label}</AppText>
    </View>
  );
}

const styles = StyleSheet.create({
  pill: {
    flexDirection: 'row',
    alignItems: 'center',
    alignSelf: 'flex-start',
    gap: spacing.xs,
    borderWidth: 1,
    borderRadius: radius.pill,
    paddingHorizontal: spacing.sm,
    paddingVertical: 2,
  },
  dot: { width: 8, height: 8, borderRadius: 4 },
});
