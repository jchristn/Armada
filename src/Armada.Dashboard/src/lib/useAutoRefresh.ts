import { useEffect, useRef, useState } from 'react';

/** Selectable auto-refresh intervals in seconds. 0 means "None" (no auto-refresh). */
export const AUTO_REFRESH_OPTIONS = [0, 15, 30, 60, 120, 180, 300] as const;
export const DEFAULT_AUTO_REFRESH_SECONDS = 15;

function storageKey(key: string): string {
  return `armada_autorefresh_${key}`;
}

function readStored(key: string): number {
  try {
    const raw = localStorage.getItem(storageKey(key));
    if (raw != null) {
      const parsed = Number(raw);
      if (AUTO_REFRESH_OPTIONS.includes(parsed as (typeof AUTO_REFRESH_OPTIONS)[number])) return parsed;
    }
  } catch {
    // Ignore storage access errors (private mode, disabled storage) and fall back to the default.
  }
  return DEFAULT_AUTO_REFRESH_SECONDS;
}

export interface AutoRefreshOptions {
  /**
   * Fixed interval in seconds that overrides the persisted user choice (used by live views such as a
   * fleet action run, which polls every 5 s while active). The persisted choice is left untouched.
   */
  intervalSeconds?: number;
  /** When true the timer is suspended (e.g. while a modal or drawer is open, or once a run finished). */
  paused?: boolean;
}

/**
 * Per-table auto-refresh timer. Persists the chosen interval per `key` in localStorage (default 15s) and
 * calls `onRefresh` on that cadence. Selecting "None" (0) disables the timer. The latest `onRefresh` is
 * always used, so callers can pass a fresh closure each render without resetting the interval.
 * `options.intervalSeconds` forces a fixed cadence and `options.paused` suspends the timer.
 * `active` reports whether a timer is currently running.
 */
export function useAutoRefresh(key: string, onRefresh: () => void, options?: AutoRefreshOptions) {
  const [seconds, setSeconds] = useState<number>(() => readStored(key));
  const callbackRef = useRef(onRefresh);
  callbackRef.current = onRefresh;

  const override = options?.intervalSeconds;
  const paused = options?.paused === true;
  const effective = override !== undefined ? override : seconds;

  useEffect(() => {
    if (override === undefined) {
      try {
        localStorage.setItem(storageKey(key), String(seconds));
      } catch {
        // Non-fatal: the interval still runs for this session even if persistence fails.
      }
    }
  }, [seconds, key, override]);

  useEffect(() => {
    if (paused || !effective || effective <= 0) return undefined;
    const id = window.setInterval(() => callbackRef.current(), effective * 1000);
    return () => window.clearInterval(id);
  }, [effective, paused]);

  return { seconds, setSeconds, active: !paused && effective > 0, intervalSeconds: effective };
}
