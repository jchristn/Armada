import { Pressable, StyleSheet, View } from 'react-native';
import { useTheme } from '../../theme/ThemeContext';
import { MIN_TOUCH, radius, spacing } from '../../theme/typography';
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

/** A row of mutually exclusive choices (a tab list for screen readers). */
export function SegmentedControl<T extends string>({ options, value, onChange, label }: SegmentedControlProps<T>) {
  const { colors } = useTheme();
  return (
    <View accessibilityRole="tablist" accessibilityLabel={label} style={[styles.row, { backgroundColor: colors.surfaceRaised, borderColor: colors.border }]}>
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
            style={[styles.segment, selected ? { backgroundColor: colors.primary } : null]}
          >
            <AppText variant="label" color={selected ? 'primaryText' : 'text'} style={styles.label}>{option.label}</AppText>
          </Pressable>
        );
      })}
    </View>
  );
}

const styles = StyleSheet.create({
  row: { flexDirection: 'row', borderRadius: radius.md, borderWidth: 1, padding: 3, gap: 3, marginBottom: spacing.lg },
  segment: { flex: 1, minHeight: MIN_TOUCH - 6, borderRadius: radius.sm, alignItems: 'center', justifyContent: 'center', paddingHorizontal: spacing.sm },
  label: { textAlign: 'center' },
});
