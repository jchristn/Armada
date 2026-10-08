import { useEffect, useState, type RefObject } from 'react';
import { AccessibilityInfo, Platform, type HostInstance } from 'react-native';

type SettingEvent = 'reduceMotionChanged' | 'screenReaderChanged' | 'darkerSystemColorsChanged' | 'highTextContrastChanged';

/** Follows one system accessibility setting: its current value, then every change. False until it is known. */
function useSetting(query: () => Promise<boolean>, event: SettingEvent | null): boolean {
  const [enabled, setEnabled] = useState(false);
  useEffect(() => {
    let cancelled = false;
    query().then((value) => { if (!cancelled) setEnabled(value === true); }).catch(() => undefined);
    const subscription = event ? AccessibilityInfo.addEventListener(event, (value: boolean) => setEnabled(value === true)) : null;
    return () => {
      cancelled = true;
      subscription?.remove();
    };
  }, [query, event]);
  return enabled;
}

const reduceMotion = () => AccessibilityInfo.isReduceMotionEnabled();
const screenReader = () => AccessibilityInfo.isScreenReaderEnabled();
const darkerColors = () => (Platform.OS === 'ios' ? AccessibilityInfo.isDarkerSystemColorsEnabled() : Promise.resolve(false));
const highTextContrast = () => (Platform.OS === 'android' ? AccessibilityInfo.isHighTextContrastEnabled() : Promise.resolve(false));

/** True while the system asks for reduced motion (iOS Reduce Motion, Android Remove animations). */
export function useReducedMotion(): boolean {
  return useSetting(reduceMotion, 'reduceMotionChanged');
}

/** True while VoiceOver or TalkBack is running. */
export function useScreenReaderEnabled(): boolean {
  return useSetting(screenReader, 'screenReaderChanged');
}

/** True while the system asks for more contrast (iOS Increase Contrast, Android High contrast text). */
export function useIncreasedContrast(): boolean {
  const ios = useSetting(darkerColors, Platform.OS === 'ios' ? 'darkerSystemColorsChanged' : null);
  const android = useSetting(highTextContrast, Platform.OS === 'android' ? 'highTextContrastChanged' : null);
  return ios || android;
}

/**
 * Speaks a message with VoiceOver or TalkBack without moving focus. On iOS it waits for the current speech instead of
 * cutting it off, so several updates in a row are all heard.
 */
export function announce(message: string): void {
  const text = message.trim();
  if (!text) return;
  if (Platform.OS === 'ios') AccessibilityInfo.announceForAccessibilityWithOptions(text, { queue: true });
  else AccessibilityInfo.announceForAccessibility(text);
}

/**
 * Moves the screen-reader focus to a rendered element (a sheet's title when it opens, its opener when it closes).
 * Only while VoiceOver or TalkBack is running: without one there is no reader focus to move, and the focus event
 * would otherwise reach the native view for nothing.
 */
export function focusElement(ref: RefObject<HostInstance | null>): void {
  AccessibilityInfo.isScreenReaderEnabled()
    .then((on) => { if (on && ref.current) AccessibilityInfo.sendAccessibilityEvent(ref.current, 'focus'); })
    .catch(() => undefined);
}
