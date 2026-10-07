import { useEffect, useRef } from 'react';
import { AppState } from 'react-native';

/**
 * Calls `fn` every `ms` while the app is in the foreground (the dashboard's polling fallback; the socket is the
 * primary source of updates). `ms` of 0 or null turns it off.
 */
export function useInterval(fn: () => void, ms: number | null | undefined): void {
  const fnRef = useRef(fn);
  useEffect(() => { fnRef.current = fn; });
  useEffect(() => {
    if (!ms || ms <= 0) return undefined;
    const timer = setInterval(() => {
      if (AppState.currentState === 'active') fnRef.current();
    }, ms);
    return () => clearInterval(timer);
  }, [ms]);
}
