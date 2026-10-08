import { useContext, useEffect, useRef } from 'react';
import { BackHandler } from 'react-native';
import { NavigationContext } from 'expo-router/react-navigation';

/**
 * Android back for a pane-level step that is not a navigation step (a detail shown alone after the window
 * narrowed): while `active`, back calls `onBack` instead of leaving the screen. Only while the screen is focused
 * (another screen pushed on top keeps its own back); safe outside a navigator (tests, previews). No effect on iOS.
 */
export function usePaneBack(active: boolean, onBack: (() => void) | undefined): void {
  const navigation = useContext(NavigationContext);
  const handler = useRef(onBack);
  useEffect(() => {
    handler.current = onBack;
  }, [onBack]);
  useEffect(() => {
    if (!active) return undefined;
    const subscription = BackHandler.addEventListener('hardwareBackPress', () => {
      if (navigation && !navigation.isFocused()) return false;
      if (!handler.current) return false;
      handler.current();
      return true;
    });
    return () => subscription.remove();
  }, [active, navigation]);
}
