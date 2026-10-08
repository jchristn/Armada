import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react';
import { Keyboard, KeyboardAvoidingView, StyleSheet, View, type KeyboardAvoidingViewProps, type StyleProp, type ViewStyle } from 'react-native';

export interface KeyboardAvoidingPaneProps {
  children: ReactNode;
  /** 'padding' on both platforms by default (Android edge-to-edge no longer resizes the window for the keyboard). */
  behavior?: KeyboardAvoidingViewProps['behavior'];
  style?: StyleProp<ViewStyle>;
  testID?: string;
}

/**
 * A pane that keeps its bottom (a composer, a form's actions) above the keyboard wherever the pane sits: under a
 * header, beside a list in a split view, in a phone's landscape layout. KeyboardAvoidingView compares its own frame
 * (relative to its parent) with the keyboard's screen position, so it needs the pane's distance from the top of the
 * window as its offset; a fixed header height was wrong in split panes and in landscape (the composer stayed under
 * the keyboard). The pane measures that distance itself after every layout and before the keyboard shows.
 */
export function KeyboardAvoidingPane({ children, behavior = 'padding', style, testID }: KeyboardAvoidingPaneProps) {
  const ref = useRef<View>(null);
  const [offset, setOffset] = useState(0);
  const measure = useCallback(() => {
    ref.current?.measureInWindow((_x, y) => {
      if (Number.isFinite(y)) setOffset(Math.max(0, Math.round(y)));
    });
  }, []);
  useEffect(() => {
    const subscription = Keyboard.addListener('keyboardWillShow', measure);
    return () => subscription.remove();
  }, [measure]);
  return (
    <View ref={ref} style={[styles.fill, style]} onLayout={measure} collapsable={false} testID={testID}>
      <KeyboardAvoidingView style={styles.fill} behavior={behavior} keyboardVerticalOffset={offset} testID={testID ? `${testID}-avoider` : undefined}>
        {children}
      </KeyboardAvoidingView>
    </View>
  );
}

const styles = StyleSheet.create({ fill: { flex: 1 } });
