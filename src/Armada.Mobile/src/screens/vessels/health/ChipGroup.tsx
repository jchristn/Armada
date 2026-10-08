import { Pressable, ScrollView, StyleSheet, View } from 'react-native';
import { AppText } from '../../../components/ui';
import { useTheme } from '../../../theme/ThemeContext';
import { CHROME_MAX_FONT_SCALE, radius, spacing, touchSlop } from '../../../theme/typography';

export interface Chip<K extends string> {
  key: K;
  label: string;
  /** Count shown after the label. */
  count?: number;
}

export interface ChipGroupProps<K extends string> {
  chips: Chip<K>[];
  /** Selected chip keys (multi-select) or the single selected key. */
  selected: K[];
  onToggle: (key: K) => void;
  /** Spoken name of the group. */
  label: string;
  /** Lay the chips out in one scrolling row instead of wrapping. */
  scroll?: boolean;
  testID?: string;
}

/**
 * Toggle chips: the dashboard's status chip filters and checklist dropdowns. Each chip is a checkbox for screen
 * readers; the label and count are always visible.
 */
export function ChipGroup<K extends string>({ chips, selected, onToggle, label, scroll, testID }: ChipGroupProps<K>) {
  const { colors } = useTheme();
  const body = chips.map((chip) => {
    const on = selected.includes(chip.key);
    const text = chip.count === undefined ? chip.label : `${chip.label} ${chip.count}`;
    return (
      <Pressable
        key={chip.key}
        testID={testID ? `${testID}-${chip.key}` : undefined}
        accessibilityRole="checkbox"
        accessibilityLabel={text}
        accessibilityState={{ checked: on }}
        onPress={() => onToggle(chip.key)}
        hitSlop={touchSlop(36)}
        style={[styles.chip, { borderColor: on ? colors.primary : colors.border, backgroundColor: on ? colors.primary : colors.surface }]}
      >
        <AppText variant="caption" color={on ? 'primaryText' : 'text'} maxFontSizeMultiplier={CHROME_MAX_FONT_SCALE}>{text}</AppText>
      </Pressable>
    );
  });
  if (scroll) {
    return (
      <ScrollView horizontal showsHorizontalScrollIndicator={false} accessibilityLabel={label} contentContainerStyle={styles.row}>
        {body}
      </ScrollView>
    );
  }
  return <View accessibilityLabel={label} style={[styles.row, styles.wrap]}>{body}</View>;
}

const styles = StyleSheet.create({
  row: { flexDirection: 'row', gap: spacing.sm, paddingHorizontal: spacing.md, paddingBottom: spacing.sm },
  wrap: { flexWrap: 'wrap', paddingHorizontal: 0 },
  chip: { borderWidth: 1, borderRadius: radius.pill, paddingHorizontal: spacing.md, paddingVertical: spacing.xs + 2, minHeight: 36, justifyContent: 'center' },
});
