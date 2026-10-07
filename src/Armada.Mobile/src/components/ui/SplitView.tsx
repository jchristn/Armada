import type { ReactNode } from 'react';
import { StyleSheet, View } from 'react-native';
import { useLayout } from '../../navigation/useLayout';
import { useTheme } from '../../theme/ThemeContext';

export interface SplitViewProps {
  /** The list. */
  master: ReactNode;
  /** The selected item; on phones it is shown instead of the list (the caller decides when). */
  detail: ReactNode | null;
  /** Width of the list pane on tablets. */
  masterWidth?: number;
}

/**
 * List-detail layout. Tablets (>= 768 dp wide) show both panes side by side; phones show the detail when one is
 * selected and the list otherwise, so the same screen code serves both.
 */
export function SplitView({ master, detail, masterWidth = 360 }: SplitViewProps) {
  const { isTablet } = useLayout();
  const { colors } = useTheme();
  if (!isTablet) return <View style={styles.fill}>{detail ?? master}</View>;
  return (
    <View style={styles.row} testID="split-view">
      <View style={[styles.master, { width: masterWidth, borderRightColor: colors.border }]}>{master}</View>
      <View style={styles.fill}>{detail}</View>
    </View>
  );
}

const styles = StyleSheet.create({
  fill: { flex: 1 },
  row: { flex: 1, flexDirection: 'row' },
  master: { borderRightWidth: StyleSheet.hairlineWidth },
});
