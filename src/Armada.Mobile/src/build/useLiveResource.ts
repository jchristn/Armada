import { useCallback, useEffect, useRef, useState } from 'react';
import type { WebSocketMessage } from '@dashboard/types/models';
import { useSocket } from '../socket/SocketContext';

/** Which WebSocket messages make a screen reload: event type prefixes ("vessel.") or a predicate. */
export type LiveMatcher = string[] | ((msg: WebSocketMessage) => boolean);

/** True when a socket message matches (prefix list: any `type` starting with one of them). */
export function matchesLive(matcher: LiveMatcher | undefined, msg: WebSocketMessage): boolean {
  if (!matcher) return false;
  if (typeof matcher === 'function') return matcher(msg);
  const type = typeof msg.type === 'string' ? msg.type : '';
  return matcher.some((prefix) => type.startsWith(prefix));
}

export interface LiveResourceOptions {
  /** Socket messages that reload the data (the dashboard's live refresh); reconnects always reload. */
  live?: LiveMatcher;
  /** Skip loading (for example while an id is missing). */
  enabled?: boolean;
}

export interface LiveResource<T> {
  data: T | null;
  /** Message of the last failed load (null after a successful one). */
  error: string | null;
  /** True during the first load (no data yet). */
  loading: boolean;
  /** True while a pull-to-refresh runs. */
  refreshing: boolean;
  /** Reload in the background (live updates, after a mutation). */
  reload: () => Promise<void>;
  /** Pull to refresh: reload with the refreshing indicator. */
  refresh: () => Promise<void>;
  /** Replace the data locally (optimistic updates). */
  setData: (next: T | null | ((prev: T | null) => T | null)) => void;
}

export function errorMessage(e: unknown): string {
  if (e instanceof Error) return e.message;
  return typeof e === 'string' ? e : 'Request failed';
}

/**
 * Loads one server resource and keeps it current: loads when `deps` change, reloads on matching WebSocket messages
 * and after every socket reconnect or return to the foreground, and offers pull to refresh. Overlapping reloads are
 * coalesced (one more load runs after the one in flight), and results from an older `deps` generation are dropped.
 */
export function useLiveResource<T>(load: () => Promise<T>, deps: unknown[], options: LiveResourceOptions = {}): LiveResource<T> {
  const { live, enabled = true } = options;
  const { subscribe, reconnectCount } = useSocket();
  const [data, setData] = useState<T | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(enabled);
  const [refreshing, setRefreshing] = useState(false);
  const loadRef = useRef(load);
  const liveRef = useRef(live);
  useEffect(() => {
    loadRef.current = load;
    liveRef.current = live;
  });
  const generation = useRef(0);
  const inFlight = useRef<Promise<void> | null>(null);
  const again = useRef(false);
  const mounted = useRef(true);

  useEffect(() => () => { mounted.current = false; }, []);

  const run = useCallback(async (): Promise<void> => {
    if (inFlight.current) {
      again.current = true;
      return inFlight.current;
    }
    const gen = generation.current;
    const task = (async () => {
      do {
        again.current = false;
        try {
          const result = await loadRef.current();
          if (!mounted.current || gen !== generation.current) return;
          setData(result);
          setError(null);
        } catch (e) {
          if (!mounted.current || gen !== generation.current) return;
          setError(errorMessage(e));
        }
      } while (again.current && mounted.current && gen === generation.current);
    })();
    inFlight.current = task;
    try {
      await task;
    } finally {
      if (inFlight.current === task) inFlight.current = null;
      if (mounted.current && gen === generation.current) setLoading(false);
    }
  }, []);

  useEffect(() => {
    generation.current += 1;
    inFlight.current = null;
    again.current = false;
    if (!enabled) {
      // eslint-disable-next-line react-hooks/set-state-in-effect -- a fetch keyed on deps; it sets loading state first
      setLoading(false);
      return;
    }
    setLoading(true);
    setData(null);
    setError(null);
    void run();
    // eslint-disable-next-line react-hooks/exhaustive-deps -- deps are the caller's (like useEffect)
  }, [enabled, run, ...deps]);

  const firstReconnect = useRef(reconnectCount);
  useEffect(() => {
    if (reconnectCount === firstReconnect.current || !enabled) return;
    void run();
  }, [reconnectCount, enabled, run]);

  useEffect(() => {
    if (!enabled) return undefined;
    return subscribe((msg) => {
      if (matchesLive(liveRef.current, msg)) void run();
    });
  }, [subscribe, enabled, run]);

  const refresh = useCallback(async () => {
    setRefreshing(true);
    try { await run(); } finally { if (mounted.current) setRefreshing(false); }
  }, [run]);

  return { data, error, loading, refreshing, reload: run, refresh, setData };
}
