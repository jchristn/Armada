import { StyleSheet, View } from 'react-native';
import { useTheme } from '../../theme/ThemeContext';
import { radius, spacing } from '../../theme/typography';
import { AppText } from './AppText';
import { Icon } from './Icon';

/** An inline notice. Warnings are announced to screen readers when they appear. */
export function Banner({ tone, title, message, testID }: { tone: 'warning' | 'info' | 'danger'; title: string; message?: string; testID?: string }) {
  const { colors } = useTheme();
  const accent = tone === 'danger' ? colors.danger : tone === 'warning' ? colors.warning : colors.info;
  return (
    <View
      testID={testID}
      accessible
      accessibilityRole={tone === 'info' ? 'text' : 'alert'}
      accessibilityLiveRegion={tone === 'info' ? 'none' : 'polite'}
      style={[styles.box, { borderColor: accent, backgroundColor: tone === 'info' ? colors.surface : colors.warningSurface }]}
    >
      <Icon name={tone === 'info' ? 'information-circle-outline' : 'warning-outline'} color={tone === 'danger' ? 'danger' : tone === 'warning' ? 'warning' : 'info'} />
      <View style={styles.text}>
        <AppText variant="label">{title}</AppText>
        {message ? <AppText variant="caption" muted>{message}</AppText> : null}
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  box: { flexDirection: 'row', gap: spacing.md, borderWidth: 1, borderLeftWidth: 4, borderRadius: radius.md, padding: spacing.md, marginHorizontal: spacing.md, marginBottom: spacing.lg },
  text: { flex: 1, gap: 2 },
});
