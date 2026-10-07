import type { ReactElement, ReactNode } from 'react';
import { KeyboardAvoidingView, Platform, ScrollView, StyleSheet, View, type RefreshControlProps } from 'react-native';
import { SafeAreaView, type Edge } from 'react-native-safe-area-context';
import { useTheme } from '../../theme/ThemeContext';
import { spacing } from '../../theme/typography';

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
}

export function Screen({ children, scroll = true, edges = ['bottom', 'left', 'right'], refreshControl, testID, maxWidth = 720 }: ScreenProps) {
  const { colors } = useTheme();
  const body = <View style={[styles.column, { maxWidth }]}>{children}</View>;
  return (
    <SafeAreaView testID={testID} edges={edges} style={[styles.fill, { backgroundColor: colors.background }]}>
      <KeyboardAvoidingView style={styles.fill} behavior={Platform.OS === 'ios' ? 'padding' : undefined}>
        {scroll ? (
          <ScrollView contentContainerStyle={styles.content} keyboardShouldPersistTaps="handled" refreshControl={refreshControl}>
            {body}
          </ScrollView>
        ) : body}
      </KeyboardAvoidingView>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  fill: { flex: 1 },
  content: { paddingVertical: spacing.lg, flexGrow: 1 },
  column: { width: '100%', alignSelf: 'center', flex: 1 },
});
