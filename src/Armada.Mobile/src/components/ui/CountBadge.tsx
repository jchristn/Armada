import { StyleSheet, View } from 'react-native';
import { useTheme } from '../../theme/ThemeContext';
import { CHROME_MAX_FONT_SCALE } from '../../theme/typography';
import { AppText } from './AppText';

/** A small count pill (unread, approvals). Hidden from screen readers: the owning control speaks the count. */
export function CountBadge({ count }: { count: number }) {
  const { colors } = useTheme();
  if (count <= 0) return null;
  return (
    <View style={[styles.pill, { backgroundColor: colors.badge }]} importantForAccessibility="no-hide-descendants" accessibilityElementsHidden>
      <AppText variant="caption" maxFontSizeMultiplier={CHROME_MAX_FONT_SCALE} style={[styles.text, { color: colors.badgeText }]}>
        {count > 99 ? '99+' : String(count)}
      </AppText>
    </View>
  );
}

const styles = StyleSheet.create({
  pill: { minWidth: 20, height: 20, borderRadius: 10, paddingHorizontal: 5, alignItems: 'center', justifyContent: 'center' },
  text: { fontWeight: '700', fontSize: 12, lineHeight: 16 },
});
