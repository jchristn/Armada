import { useCallback, useEffect, useRef } from 'react';
import { Keyboard, Platform } from 'react-native';

/**
 * How long after the keyboard hides a Modal's back request still counts as the press that hid it. On Android a
 * single hardware back with the keyboard up inside a Modal both hides the keyboard and fires onRequestClose (measured
 * on API 36), and the keyboard's hide event can arrive just before the close request.
 */
export const KEYBOARD_BACK_WINDOW_MS = 500;

/**
 * Android back for a Modal (a sheet or a dialog) that holds text fields: the first back only closes the keyboard, as
 * everywhere else on Android, and the next one closes the Modal. Without this one press dismissed the sheet and the
 * text typed into it. Pass the result as the Modal's onRequestClose. Other platforms close at once (on iOS
 * onRequestClose is a page sheet's swipe-down, which has already dismissed it).
 */
export function useModalBack(onClose: () => void, now: () => number = Date.now): () => void {
  const keyboard = useRef({ visible: Keyboard.isVisible(), hiddenAt: Number.NEGATIVE_INFINITY });
  useEffect(() => {
    const show = Keyboard.addListener('keyboardDidShow', () => { keyboard.current.visible = true; });
    const hide = Keyboard.addListener('keyboardDidHide', () => {
      keyboard.current.visible = false;
      keyboard.current.hiddenAt = now();
    });
    return () => { show.remove(); hide.remove(); };
  }, [now]);
  return useCallback(() => {
    if (Platform.OS !== 'android') {
      onClose();
      return;
    }
    const state = keyboard.current;
    if (state.visible || now() - state.hiddenAt < KEYBOARD_BACK_WINDOW_MS) {
      // This press belonged to the keyboard; make sure it is gone and keep the Modal (and its text) open.
      Keyboard.dismiss();
      state.hiddenAt = Number.NEGATIVE_INFINITY;
      return;
    }
    onClose();
  }, [onClose, now]);
}
