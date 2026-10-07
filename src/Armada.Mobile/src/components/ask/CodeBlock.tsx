import { ScrollView, StyleSheet } from 'react-native';
import { useTheme } from '../../theme/ThemeContext';
import { radius, spacing } from '../../theme/typography';
import { AppText } from '../ui/AppText';

/** Monospace, selectable (long-press to copy), horizontally scrollable text: arguments, results, commands. */
export function CodeBlock({ text, testID, maxHeight = 320 }: { text: string; testID?: string; maxHeight?: number }) {
  const { colors } = useTheme();
  return (
    <ScrollView
      testID={testID}
      style={[styles.box, { backgroundColor: colors.background, borderColor: colors.border, maxHeight }]}
      contentContainerStyle={styles.content}
      nestedScrollEnabled
    >
      <ScrollView horizontal nestedScrollEnabled>
        <AppText variant="mono" selectable>{text}</AppText>
      </ScrollView>
    </ScrollView>
  );
}

const styles = StyleSheet.create({
  box: { borderWidth: StyleSheet.hairlineWidth, borderRadius: radius.sm },
  content: { padding: spacing.sm },
});
