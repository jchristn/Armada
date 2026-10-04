import { useEffect, useRef, type RefObject } from 'react';

const FOCUSABLE = 'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

/**
 * Keeps keyboard focus inside a modal while it is open: focuses the first control on open, cycles Tab and
 * Shift+Tab within the container, closes on Escape (unless `escapeEnabled` is false, for example while a
 * nested confirm dialog owns the keyboard), and restores focus to the previously focused element on close.
 */
export function useFocusTrap(containerRef: RefObject<HTMLElement | null>, active: boolean, onEscape: () => void, escapeEnabled = true) {
  const onEscapeRef = useRef(onEscape);
  onEscapeRef.current = onEscape;
  const escapeRef = useRef(escapeEnabled);
  escapeRef.current = escapeEnabled;

  useEffect(() => {
    if (!active) return undefined;
    const previouslyFocused = document.activeElement as HTMLElement | null;
    const container = containerRef.current;
    const first = container?.querySelector<HTMLElement>(FOCUSABLE);
    (first ?? container)?.focus();

    const onKeyDown = (event: KeyboardEvent) => {
      const root = containerRef.current;
      if (!root) return;
      if (event.key === 'Escape') {
        if (!escapeRef.current) return;
        event.preventDefault();
        onEscapeRef.current();
        return;
      }
      if (event.key !== 'Tab') return;
      if (!escapeRef.current) return;
      const focusable = Array.from(root.querySelectorAll<HTMLElement>(FOCUSABLE)).filter((el) => el.offsetParent !== null || el === document.activeElement);
      if (focusable.length === 0) {
        event.preventDefault();
        return;
      }
      const firstEl = focusable[0];
      const lastEl = focusable[focusable.length - 1];
      const current = document.activeElement as HTMLElement | null;
      if (event.shiftKey && (current === firstEl || !root.contains(current))) {
        event.preventDefault();
        lastEl.focus();
      } else if (!event.shiftKey && (current === lastEl || !root.contains(current))) {
        event.preventDefault();
        firstEl.focus();
      }
    };

    document.addEventListener('keydown', onKeyDown);
    return () => {
      document.removeEventListener('keydown', onKeyDown);
      if (previouslyFocused && typeof previouslyFocused.focus === 'function') previouslyFocused.focus();
    };
  }, [active, containerRef]);
}
