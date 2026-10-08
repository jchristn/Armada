import { useEffect, useRef } from 'react';
import { Pressable, ScrollView, StyleSheet, View } from 'react-native';
import { useTheme } from '../../theme/ThemeContext';
import { CHROME_MAX_FONT_SCALE, MIN_TOUCH, radius, spacing, touchSlop } from '../../theme/typography';
import { useReducedMotion } from '../../lib/accessibility';
import { AppText } from './AppText';

export interface TabStripOption<T extends string> {
  value: T;
  label: string;
  testID?: string;
}

export interface TabStripProps<T extends string> {
  options: TabStripOption<T>[];
  value: T;
  onChange: (value: T) => void;
  /** Spoken name of the tab list. */
  label: string;
  testID?: string;
}

/**
 * A horizontally scrolling row of tabs (the mobile form of the dashboard's hub tabs, which can have up to ten).
 * SegmentedControl is for two to four choices; this scrolls and keeps the selected tab in view.
 */
export function TabStrip<T extends string>({ options, value, onChange, label, testID }: TabStripProps<T>) {
  const { colors } = useTheme();
  const scrollRef = useRef<ScrollView | null>(null);
  const offsets = useRef<Record<string, number>>({});
  const reduceMotion = useReducedMotion();

  useEffect(() => {
    const x = offsets.current[value];
    if (x !== undefined) scrollRef.current?.scrollTo({ x: Math.max(0, x - spacing.lg), animated: !reduceMotion });
  }, [value, reduceMotion]);

  return (
    <View style={[styles.wrap, { borderBottomColor: colors.border, backgroundColor: colors.surface }]}>
      <ScrollView
        ref={scrollRef}
        horizontal
        showsHorizontalScrollIndicator={false}
        contentContainerStyle={styles.row}
        accessibilityRole="tablist"
        accessibilityLabel={label}
        testID={testID}
      >
        {options.map((option) => {
          const selected = option.value === value;
          return (
            <Pressable
              key={option.value}
              testID={option.testID}
              accessibilityRole="tab"
              accessibilityLabel={option.label}
              accessibilityState={{ selected }}
              onLayout={(e) => { offsets.current[option.value] = e.nativeEvent.layout.x; }}
              onPress={() => onChange(option.value)}
              hitSlop={touchSlop(MIN_TOUCH - 8)}
              style={({ pressed }) => [
                styles.tab,
                { backgroundColor: selected ? colors.primary : pressed ? colors.background : 'transparent', borderColor: selected ? colors.primary : colors.border },
              ]}
            >
              <AppText variant="label" color={selected ? 'primaryText' : 'text'} maxFontSizeMultiplier={CHROME_MAX_FONT_SCALE}>{option.label}</AppText>
            </Pressable>
          );
        })}
      </ScrollView>
    </View>
  );
}

const styles = StyleSheet.create({
  wrap: { borderBottomWidth: StyleSheet.hairlineWidth },
  row: { paddingHorizontal: spacing.md, paddingVertical: spacing.sm, gap: spacing.sm },
  tab: { minHeight: MIN_TOUCH - 8, paddingHorizontal: spacing.md, borderRadius: radius.pill, borderWidth: 1, alignItems: 'center', justifyContent: 'center' },
});
