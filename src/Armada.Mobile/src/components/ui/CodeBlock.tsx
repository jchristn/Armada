import { ScrollView, StyleSheet, View } from 'react-native';
import { useTheme } from '../../theme/ThemeContext';
import { radius, spacing } from '../../theme/typography';
import { AppText } from './AppText';

/**
 * Preformatted, selectable monospace text (logs, instructions, JSON). Long lines scroll sideways unless `wrap`.
 */
export function CodeBlock({ text, wrap, testID, accessibilityLabel }: { text: string; wrap?: boolean; testID?: string; accessibilityLabel?: string }) {
  const { colors } = useTheme();
  const body = (
    <AppText selectable variant="mono" testID={testID} accessibilityLabel={accessibilityLabel}>{text}</AppText>
  );
  return (
    <View style={[styles.box, { backgroundColor: colors.surfaceRaised, borderColor: colors.border }]}>
      {wrap ? body : <ScrollView horizontal showsHorizontalScrollIndicator>{body}</ScrollView>}
    </View>
  );
}

const styles = StyleSheet.create({
  box: { borderWidth: StyleSheet.hairlineWidth, borderRadius: radius.sm, padding: spacing.md },
});
