import { Pressable, StyleSheet, View } from 'react-native';
import { useTheme } from '../../theme/ThemeContext';
import { BUTTON_MAX_FONT_SCALE, MIN_TOUCH, radius, spacing, touchSlop, useLargeText } from '../../theme/typography';
import { AppText } from './AppText';

export interface SegmentOption<T extends string> {
  value: T;
  label: string;
  testID?: string;
}

export interface SegmentedControlProps<T extends string> {
  options: SegmentOption<T>[];
  value: T;
  onChange: (value: T) => void;
  /** Spoken name of the whole control. */
  label: string;
}

/** Labels longer than this get a row of their own at large text sizes. */
const LONG_SEGMENT_LABEL = 10;

/** A row of mutually exclusive choices (a tab list for screen readers). */
export function SegmentedControl<T extends string>({ options, value, onChange, label }: SegmentedControlProps<T>) {
  const { colors } = useTheme();
  // At large text sizes the segments wrap two to a row with whole words, instead of shrinking each label to fit
  // (which left some labels tiny next to full-size ones).
  const large = useLargeText();
  // A long single word ("Armada.Proxy") cannot share a half row at large sizes without breaking mid-word.
  const fullRows = large && options.some((o) => o.label.length > LONG_SEGMENT_LABEL);
  return (
    <View accessibilityRole="tablist" accessibilityLabel={label} style={[styles.row, large ? styles.wrap : null, { backgroundColor: colors.surfaceRaised, borderColor: colors.border }]}>
      {options.map((option) => {
        const selected = option.value === value;
        return (
          <Pressable
            key={option.value}
            testID={option.testID}
            accessibilityRole="tab"
            accessibilityLabel={option.label}
            accessibilityState={{ selected }}
            onPress={() => onChange(option.value)}
            hitSlop={touchSlop(MIN_TOUCH - 6)}
            style={[styles.segment, large ? (fullRows ? styles.fullSegment : styles.wrapSegment) : null, selected ? { backgroundColor: colors.primary } : null]}
          >
            {large ? (
              <AppText variant="label" color={selected ? 'primaryText' : 'text'} style={styles.label} maxFontSizeMultiplier={BUTTON_MAX_FONT_SCALE}>{option.label}</AppText>
            ) : (
              <AppText variant="label" color={selected ? 'primaryText' : 'text'} style={styles.label} numberOfLines={1} adjustsFontSizeToFit minimumFontScale={0.7}>{option.label}</AppText>
            )}
          </Pressable>
        );
      })}
    </View>
  );
}

const styles = StyleSheet.create({
  row: { flexDirection: 'row', borderRadius: radius.md, borderWidth: 1, padding: 3, gap: 3, marginBottom: spacing.lg },
  segment: { flex: 1, minHeight: MIN_TOUCH - 6, borderRadius: radius.sm, alignItems: 'center', justifyContent: 'center', paddingHorizontal: spacing.sm },
  wrap: { flexWrap: 'wrap' },
  wrapSegment: { flexBasis: '45%', flexGrow: 1 },
  fullSegment: { flexBasis: '100%' },
  label: { textAlign: 'center' },
});
