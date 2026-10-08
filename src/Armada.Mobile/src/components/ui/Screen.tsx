import type { ReactElement, ReactNode } from 'react';
import { ScrollView, StyleSheet, View, type RefreshControlProps } from 'react-native';
import { SafeAreaView, type Edge } from 'react-native-safe-area-context';
import { useTheme } from '../../theme/ThemeContext';
import { spacing } from '../../theme/typography';
import { KeyboardAvoidingPane } from './KeyboardAvoidingPane';
import { StickyFooter } from './StickyFooter';

export interface ScreenProps {
  children: ReactNode;
  /** Scrolls by default; pass false for screens that manage their own list. */
  scroll?: boolean;
  /** Safe-area edges to pad (screens under a navigation header skip the top edge). */
  edges?: Edge[];
  refreshControl?: ReactElement<RefreshControlProps>;
  testID?: string;
  /** Centers content in a readable column on wide screens. */
  maxWidth?: number;
  /**
   * A form's actions (usually FormActions with Cancel and the primary action), kept below the scrolling content so
   * they are reachable without scrolling, above the tab bar and the keyboard.
   */
  footer?: ReactNode;
}

export function Screen({ children, scroll = true, edges = ['bottom', 'left', 'right'], refreshControl, testID, maxWidth = 720, footer }: ScreenProps) {
  const { colors } = useTheme();
  const body = <View style={[styles.column, { maxWidth }]}>{children}</View>;
  return (
    <SafeAreaView testID={testID} edges={edges} style={[styles.fill, { backgroundColor: colors.background }]}>
      {/* The pane measures its place in the window, so under a navigation header the footer still clears the
          keyboard (a bare KeyboardAvoidingView compares its parent-relative layout with the keyboard and
          under-lifts by the header's height, leaving the footer's actions behind the keyboard). */}
      <KeyboardAvoidingPane testID={testID ? `${testID}-keyboard` : undefined}>
        {scroll ? (
          <ScrollView contentContainerStyle={styles.content} keyboardShouldPersistTaps="handled" refreshControl={refreshControl}>
            {body}
          </ScrollView>
        ) : body}
        {footer ? <StickyFooter maxWidth={maxWidth} testID={testID ? `${testID}-footer` : undefined}>{footer}</StickyFooter> : null}
      </KeyboardAvoidingPane>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  fill: { flex: 1 },
  content: { paddingVertical: spacing.lg, flexGrow: 1 },
  column: { width: '100%', alignSelf: 'center', flex: 1 },
});
