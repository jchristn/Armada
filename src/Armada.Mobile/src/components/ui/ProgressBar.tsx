import { StyleSheet, View } from 'react-native';
import { useTheme } from '../../theme/ThemeContext';
import { radius } from '../../theme/typography';
import type { Palette } from '../../theme/palette';

/** A horizontal progress bar (0-100). Spoken as a percentage. */
export function ProgressBar({ percent, label, color = 'success', testID }: { percent: number; label: string; color?: keyof Palette; testID?: string }) {
  const { colors } = useTheme();
  const value = Math.max(0, Math.min(100, Math.round(percent)));
  return (
    <View
      testID={testID}
      accessible
      accessibilityRole="progressbar"
      accessibilityLabel={label}
      accessibilityValue={{ min: 0, max: 100, now: value }}
      style={[styles.track, { backgroundColor: colors.border }]}
    >
      <View style={[styles.fill, { width: `${value}%`, backgroundColor: colors[color] }]} />
    </View>
  );
}

const styles = StyleSheet.create({
  track: { height: 8, borderRadius: radius.pill, overflow: 'hidden', flex: 1 },
  fill: { height: '100%', borderRadius: radius.pill },
});
