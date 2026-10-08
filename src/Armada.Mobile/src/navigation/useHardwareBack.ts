import { useFocusEffect } from 'expo-router';
import { useCallback, useEffect, useRef } from 'react';
import { BackHandler } from 'react-native';

/**
 * Android hardware back (and the back gesture) for in-screen modes that are not a navigation step, such as a list's
 * bulk selection: while `active` and the screen is focused, back calls `onBack` instead of leaving the screen, as
 * Android's contextual action modes do. Sheets and dialogs are platform Modals and close on back by themselves.
 * No effect on iOS, which has no hardware back.
 */
export function useHardwareBack(active: boolean, onBack: () => void): void {
  const handler = useRef(onBack);
  useEffect(() => {
    handler.current = onBack;
  }, [onBack]);
  useFocusEffect(
    useCallback(() => {
      if (!active) return undefined;
      const subscription = BackHandler.addEventListener('hardwareBackPress', () => {
        handler.current();
        return true;
      });
      return () => subscription.remove();
    }, [active]),
  );
}
