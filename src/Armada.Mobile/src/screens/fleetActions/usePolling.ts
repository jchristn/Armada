import { useEffect, useRef } from 'react';

/** Refresh cadence of active runs, in milliseconds (the dashboard's RUN_DETAIL_REFRESH_SECONDS). */
export const RUN_REFRESH_MS = 5000;

/**
 * Call `tick` every `ms` while `enabled` (fleet action runs have no WebSocket events; the dashboard polls active
 * runs every 5 s, and so does the app). Stops as soon as `enabled` turns false.
 */
export function usePolling(enabled: boolean, tick: () => void, ms: number = RUN_REFRESH_MS): void {
  const tickRef = useRef(tick);
  useEffect(() => { tickRef.current = tick; });
  useEffect(() => {
    if (!enabled) return undefined;
    const handle = setInterval(() => tickRef.current(), ms);
    return () => clearInterval(handle);
  }, [enabled, ms]);
}
