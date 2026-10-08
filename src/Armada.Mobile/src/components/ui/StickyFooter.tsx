import type { ReactNode } from 'react';
import { StyleSheet, View } from 'react-native';
import { useTheme } from '../../theme/ThemeContext';
import { spacing } from '../../theme/typography';

export interface StickyFooterProps {
  children: ReactNode;
  /** Centers the footer's content in the same readable column as the screen body. */
  maxWidth?: number;
  /** Inside a bottom sheet: no side padding of its own beyond the sheet's. */
  inset?: 'screen' | 'sheet';
  testID?: string;
}

/**
 * A footer that stays put below a scrolling form, so its primary action (Save, Create, Dispatch) is reachable
 * without scrolling to the end, above the tab bar and, with the keyboard up, above the keyboard. Screen and
 * BottomSheet render it for their `footer` prop; a screen that manages its own ScrollView renders it after the
 * ScrollView, inside the same KeyboardAvoidingView.
 */
export function StickyFooter({ children, maxWidth = 720, inset = 'screen', testID }: StickyFooterProps) {
  const { colors } = useTheme();
  return (
    <View
      testID={testID}
      style={[
        styles.bar,
        inset === 'sheet' ? styles.sheet : styles.screen,
        { borderTopColor: colors.border, backgroundColor: colors.surface },
      ]}
    >
      <View style={[styles.column, { maxWidth }]}>{children}</View>
    </View>
  );
}

/** The action row of a form: secondary actions first, the primary action last (rightmost); wraps when tight. */
export function FormActions({ children, testID }: { children: ReactNode; testID?: string }) {
  return <View style={styles.actions} testID={testID}>{children}</View>;
}

const styles = StyleSheet.create({
  bar: { borderTopWidth: StyleSheet.hairlineWidth },
  screen: { paddingHorizontal: spacing.lg, paddingVertical: spacing.sm },
  sheet: { paddingHorizontal: spacing.lg, paddingTop: spacing.sm },
  column: { width: '100%', alignSelf: 'center' },
  actions: { flexDirection: 'row', flexWrap: 'wrap', justifyContent: 'flex-end', alignItems: 'center', gap: spacing.sm },
});
