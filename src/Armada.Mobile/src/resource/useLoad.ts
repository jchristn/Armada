import { useFocusEffect } from 'expo-router';
import { useCallback, useEffect, useRef, useState } from 'react';
import type { WebSocketMessage } from '@dashboard/types/models';
import { useSocket } from '../socket/SocketContext';

/** Coalesces a burst of WebSocket events into one reload (the dashboard's LIVE_REFRESH_DEBOUNCE_MS is 300). */
export const LIVE_RELOAD_DEBOUNCE_MS = 400;

export function errorText(err: unknown, fallback: string): string {
  return err instanceof Error && err.message ? err.message : fallback;
}

export interface LoadOptions {
  /**
   * Event type prefixes (for example 'deployment.') that make the data stale. A matching WebSocket event reloads
   * it quietly (debounced); every reconnect and return to the foreground reloads it too.
   */
  live?: readonly string[];
  /** Skip loading while false (for example until an id is known). */
  enabled?: boolean;
  /** Error shown when the loader throws something without a message. */
  fallbackError?: string;
}

export interface LoadState<T> {
  data: T | null;
  /** First load (or a reload after an error) is in flight. */
  loading: boolean;
  /** Pull to refresh is in flight. */
  refreshing: boolean;
  error: string;
  /** Reload without the refresh spinner (after an action). */
  reload: () => Promise<void>;
  /** Reload with the pull-to-refresh spinner. */
  refresh: () => Promise<void>;
  /** Replace the data locally (for example with what an update returned). */
  setData: (data: T | null) => void;
}

/**
 * Loads data for a screen: first load, pull to refresh, quiet reloads after actions, and live reloads from the
 * shared WebSocket (the dashboard's useLiveRefresh) and on reconnect. Responses that arrive after a newer load
 * started are dropped, so a slow first load never overwrites a fresh one.
 */
export function useLoad<T>(loader: () => Promise<T>, deps: readonly unknown[], options: LoadOptions = {}): LoadState<T> {
  const { live = [], enabled = true, fallbackError = 'Failed to load.' } = options;
  const { subscribe, reconnectCount } = useSocket();
  const [data, setData] = useState<T | null>(null);
  const [loading, setLoading] = useState(enabled);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState('');
  const loaderRef = useRef(loader);
  const seqRef = useRef(0);
  const mountedRef = useRef(true);

  useEffect(() => { loaderRef.current = loader; });
  useEffect(() => {
    mountedRef.current = true;
    return () => { mountedRef.current = false; };
  }, []);

  const run = useCallback(async (mode: 'initial' | 'quiet' | 'refresh') => {
    const seq = ++seqRef.current;
    if (mode === 'initial') setLoading(true);
    if (mode === 'refresh') setRefreshing(true);
    try {
      const result = await loaderRef.current();
      if (!mountedRef.current || seq !== seqRef.current) return;
      setData(result);
      setError('');
    } catch (err: unknown) {
      if (!mountedRef.current || seq !== seqRef.current) return;
      setError(errorText(err, fallbackError));
    } finally {
      if (mountedRef.current && seq === seqRef.current) {
        setLoading(false);
        setRefreshing(false);
      }
    }
  }, [fallbackError]);

  // Starting the load (and showing its spinner) is this effect's purpose; the caller lists what the loader depends on.
  // eslint-disable-next-line react-hooks/exhaustive-deps, react-hooks/set-state-in-effect
  useEffect(() => { if (enabled) void run('initial'); }, [enabled, run, ...deps]);

  const liveKey = live.join('|');
  useEffect(() => {
    const prefixes = liveKey.split('|').filter(Boolean);
    if (!enabled || prefixes.length === 0) return undefined;
    let timer: ReturnType<typeof setTimeout> | null = null;
    const unsubscribe = subscribe((message: WebSocketMessage) => {
      if (typeof message.type !== 'string' || !prefixes.some((p) => message.type.startsWith(p))) return;
      if (timer) clearTimeout(timer);
      timer = setTimeout(() => { timer = null; void run('quiet'); }, LIVE_RELOAD_DEBOUNCE_MS);
    });
    return () => {
      if (timer) clearTimeout(timer);
      unsubscribe();
    };
  }, [subscribe, liveKey, enabled, run]);

  const firstReconnect = useRef(reconnectCount);
  useEffect(() => {
    if (enabled && reconnectCount !== firstReconnect.current) void run('quiet');
  }, [reconnectCount, enabled, run]);

  const reload = useCallback(() => run('quiet'), [run]);
  const refresh = useCallback(() => run('refresh'), [run]);
  return { data, loading: enabled && loading, refreshing, error, reload, refresh, setData };
}

/**
 * Reloads quietly when the screen regains focus (back from a detail or an edit), not on the first focus. Needs an
 * expo-router screen around it.
 */
export function useReloadOnFocus(reload: () => Promise<void> | void): void {
  const first = useRef(true);
  const reloadRef = useRef(reload);
  useEffect(() => { reloadRef.current = reload; });
  useFocusEffect(useCallback(() => {
    if (first.current) { first.current = false; return; }
    void reloadRef.current();
  }, []));
}
