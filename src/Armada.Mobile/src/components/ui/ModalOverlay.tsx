import { useCallback, useEffect, useState, type ReactNode } from 'react';
import {
  Animated,
  Easing,
  KeyboardAvoidingView,
  Modal,
  Platform,
  Pressable,
  StyleSheet,
  View,
  useAnimatedValue,
  useWindowDimensions,
  type StyleProp,
  type ViewStyle,
} from 'react-native';
import { useReducedMotion } from '../../lib/accessibility';
import { useTheme } from '../../theme/ThemeContext';
import { MODAL_ORIENTATIONS } from './modalOrientations';

/** How a sheet or dialog's content enters: sliding up from the bottom, or fading in. */
export type ModalEntrance = 'slide' | 'fade';

/** Content entrance animation lengths (the backdrop itself never animates). */
export const MODAL_SLIDE_MS = 260;
export const MODAL_FADE_MS = 180;

/**
 * The entrance of a modal's content: sheets slide up and dialogs fade in; under Reduce Motion (iOS) or Remove
 * animations (Android) everything fades, as the platform sheets do.
 */
export function modalEntrance(kind: 'sheet' | 'dialog', reduceMotion: boolean): ModalEntrance {
  return kind === 'sheet' && !reduceMotion ? 'slide' : 'fade';
}

export interface ModalOverlayProps {
  visible: boolean;
  /** Android back (and the iOS accessibility escape gesture). */
  onRequestClose: () => void;
  /** Called once the modal has appeared (screen-reader focus moves to its title then). */
  onShow?: () => void;
  /** 'sheet' content slides up from the bottom; 'dialog' content fades in. Both fade under Reduce Motion. */
  kind: 'sheet' | 'dialog';
  /** Tapping the dimmed area outside the content. Omitted: the backdrop ignores taps (a decision the user must make). */
  onBackdropPress?: () => void;
  /** Accessible name of the pressable backdrop (for example "Close"). */
  backdropLabel?: string;
  /** Layout of the content layer, which fills the screen above the backdrop and lifts its content over the keyboard. */
  contentStyle?: StyleProp<ViewStyle>;
  children: ReactNode;
}

/**
 * The one modal frame behind every sheet and dialog (BottomSheet, ConfirmDialog, one-time secret dialogs). The dim
 * backdrop covers the whole screen, under the status bar and the Android navigation bar, appears at once with no
 * animation, and never moves: it is a sibling of the keyboard-avoiding layer, not a child of it, so only
 * the sheet or dialog content lifts when the keyboard opens and drops when it closes. The platform Modal does not
 * animate its appearance (its slide and fade moved the backdrop with the content); the content enters on its own,
 * sliding or fading (see modalEntrance). Closing is instant on Android and the platform fade on iOS (see below).
 */
export function ModalOverlay({ visible, onRequestClose, onShow, kind, onBackdropPress, backdropLabel, contentStyle, children }: ModalOverlayProps) {
  const { colors } = useTheme();
  const reduceMotion = useReducedMotion();
  const { height } = useWindowDimensions();
  const entrance = modalEntrance(kind, reduceMotion);
  // 0 = hidden (below the screen, or transparent), 1 = in place. Reset while closed so every opening starts hidden.
  const progress = useAnimatedValue(0);
  // iOS: shown without animation, but dismissed with the platform fade (see the comment on animationType below).
  const [presented, setPresented] = useState(false);
  const handleShow = useCallback(() => {
    setPresented(true);
    onShow?.();
  }, [onShow]);

  useEffect(() => {
    if (!visible) {
      progress.setValue(0);
      return undefined;
    }
    const animation = Animated.timing(progress, {
      toValue: 1,
      duration: entrance === 'slide' ? MODAL_SLIDE_MS : MODAL_FADE_MS,
      easing: Easing.out(Easing.cubic),
      useNativeDriver: true,
    });
    animation.start();
    return () => animation.stop();
  }, [visible, entrance, progress]);

  const entranceStyle = entrance === 'slide'
    ? { transform: [{ translateY: progress.interpolate({ inputRange: [0, 1], outputRange: [Math.max(height, 1), 0] }) }] }
    : { opacity: progress };

  const backdropStyle = [StyleSheet.absoluteFill, { backgroundColor: colors.overlay }];
  return (
    <Modal
      supportedOrientations={MODAL_ORIENTATIONS}
      visible={visible}
      transparent
      // Appearing never animates (UIKit's slide and fade moved or faded the backdrop with the content). On iOS the
      // dismissal still fades: whatever the caller presents next as the modal closes (a system passcode prompt, a share
      // sheet, another dialog) is presented by UIKit after the fade, as before; an instant dismissal raced it and left
      // the system passcode prompt unable to close. Android dialogs need no such ordering (and changing the animation
      // of a shown Android Modal would recreate its window), so they stay unanimated.
      animationType={Platform.OS === 'ios' && presented ? 'fade' : 'none'}
      onRequestClose={onRequestClose}
      onShow={handleShow}
      onDismiss={() => setPresented(false)}
      statusBarTranslucent
      navigationBarTranslucent
    >
      {onBackdropPress ? (
        <Pressable testID="modal-backdrop" style={backdropStyle} onPress={onBackdropPress} accessibilityRole="button" accessibilityLabel={backdropLabel} />
      ) : (
        <View testID="modal-backdrop" style={backdropStyle} />
      )}
      {/* Padding on both platforms: with Android edge-to-edge the window no longer resizes for the keyboard. */}
      <KeyboardAvoidingView style={styles.fill} behavior="padding" pointerEvents="box-none" testID="modal-keyboard-layer">
        <Animated.View style={[styles.fill, contentStyle, entranceStyle]} pointerEvents="box-none" testID="modal-content-layer">
          {children}
        </Animated.View>
      </KeyboardAvoidingView>
    </Modal>
  );
}

const styles = StyleSheet.create({ fill: { flex: 1 } });
