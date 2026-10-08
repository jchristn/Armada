import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react';
import {
  Keyboard,
  KeyboardAvoidingView,
  LayoutAnimation,
  Platform,
  StyleSheet,
  View,
  type KeyboardAvoidingViewProps,
  type KeyboardEvent,
  type StyleProp,
  type ViewStyle,
} from 'react-native';

export interface KeyboardAvoidingPaneProps {
  children: ReactNode;
  /** Android only: 'padding' by default (edge-to-edge no longer resizes the window for the keyboard). */
  behavior?: KeyboardAvoidingViewProps['behavior'];
  style?: StyleProp<ViewStyle>;
  testID?: string;
}

/** How far a pane at `paneY` (window coordinates) of `paneHeight` must lift its content for a keyboard whose top is at `keyboardY`. */
export function keyboardOverlap(paneY: number, paneHeight: number, keyboardY: number | null): number {
  if (keyboardY === null) return 0;
  return Math.max(0, Math.round(paneY + paneHeight - keyboardY));
}

/**
 * A pane that keeps its bottom (a composer, a form's actions) above the keyboard wherever the pane sits: under a
 * header, beside a list in a split view, in a phone's landscape layout.
 *
 * iOS: the pane measures its own place in the window and follows every keyboard frame change. React Native's
 * KeyboardAvoidingView needed a fixed offset (the header height, wrong beside a list and in landscape) and ignores
 * frame changes after the keyboard shows (the predictive bar arriving), which left the Ask composer under the
 * keyboard on an iPhone in landscape. Android keeps KeyboardAvoidingView with the measured offset.
 */
export function KeyboardAvoidingPane({ children, behavior = 'padding', style, testID }: KeyboardAvoidingPaneProps) {
  const ref = useRef<View>(null);
  const keyboardY = useRef<number | null>(null);
  const [offset, setOffset] = useState(0);
  const [lift, setLift] = useState(0);
  const liftRef = useRef(0);

  const update = useCallback((event?: KeyboardEvent) => {
    ref.current?.measureInWindow((_x, y, _width, height) => {
      if (!Number.isFinite(y) || !Number.isFinite(height)) return;
      setOffset(Math.max(0, Math.round(y)));
      const next = keyboardOverlap(y, height, keyboardY.current);
      if (next === liftRef.current) return;
      liftRef.current = next;
      if (event?.duration && Platform.OS === 'ios') {
        LayoutAnimation.configureNext({
          duration: Math.max(event.duration, 10),
          update: { duration: Math.max(event.duration, 10), type: LayoutAnimation.Types.keyboard },
        });
      }
      setLift(next);
    });
  }, []);

  useEffect(() => {
    if (Platform.OS !== 'ios') {
      const subscription = Keyboard.addListener('keyboardDidShow', () => update());
      return () => subscription.remove();
    }
    const shown = (event: KeyboardEvent) => {
      keyboardY.current = event.endCoordinates.screenY;
      update(event);
    };
    const hidden = (event: KeyboardEvent) => {
      keyboardY.current = null;
      update(event);
    };
    const subscriptions = [
      Keyboard.addListener('keyboardWillShow', shown),
      Keyboard.addListener('keyboardWillChangeFrame', shown),
      Keyboard.addListener('keyboardDidShow', shown),
      Keyboard.addListener('keyboardWillHide', hidden),
    ];
    return () => subscriptions.forEach((subscription) => subscription.remove());
  }, [update]);

  if (Platform.OS !== 'ios') {
    return (
      <View ref={ref} style={[styles.fill, style]} onLayout={() => update()} collapsable={false} testID={testID}>
        <KeyboardAvoidingView style={styles.fill} behavior={behavior} keyboardVerticalOffset={offset} testID={testID ? `${testID}-avoider` : undefined}>
          {children}
        </KeyboardAvoidingView>
      </View>
    );
  }
  return (
    <View ref={ref} style={[styles.fill, style]} onLayout={() => update()} collapsable={false} testID={testID}>
      <View style={[styles.fill, { paddingBottom: lift }]} testID={testID ? `${testID}-avoider` : undefined}>{children}</View>
    </View>
  );
}

const styles = StyleSheet.create({ fill: { flex: 1 } });
