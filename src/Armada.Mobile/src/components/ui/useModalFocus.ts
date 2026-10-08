import { useCallback, useEffect, useRef, type RefObject } from 'react';
import type { HostInstance } from 'react-native';
import { focusElement } from '../../lib/accessibility';

/**
 * Screen-reader focus for a Modal (a sheet or a dialog): when it has appeared, VoiceOver and TalkBack start on its
 * title instead of wherever the platform lands; when it closes, focus goes back to the control that opened it (when
 * the caller passes one), so the user does not have to find their place again. Pass the result as onShow.
 */
export function useModalFocus(open: boolean, titleRef: RefObject<HostInstance | null>, returnFocusRef?: RefObject<HostInstance | null>): () => void {
  const wasOpen = useRef(open);
  useEffect(() => {
    if (wasOpen.current && !open && returnFocusRef) focusElement(returnFocusRef);
    wasOpen.current = open;
  }, [open, returnFocusRef]);
  return useCallback(() => focusElement(titleRef), [titleRef]);
}
