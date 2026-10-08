import { useCallback, useEffect, useRef, useState, type Dispatch, type SetStateAction } from 'react';
import { errorMessage } from './errors';

export interface QueryState<T> {
  data: T | null;
  /** The last load's error message (the previous data stays shown). */
  error: string | null;
  /**
   * What the last load threw, or null after a success: the typed error (ApiError with its status, NetworkError,
   * TimeoutError) for screens that tell "not found" apart from "cannot reach the server".
   */
  failure: unknown;
  /** True until the first load settles. */
  loading: boolean;
  /** True while a pull-to-refresh is running. */
  refreshing: boolean;
  /** Pull-to-refresh: shows the spinner and resolves when the load settles. */
  refresh: () => Promise<void>;
  /** Quiet reload (live updates, after an action). */
  reload: () => Promise<void>;
  setData: Dispatch<SetStateAction<T | null>>;
}

/**
 * Loads one thing for a screen. Re-runs when `deps` change; out-of-order responses are dropped (only the latest
 * load may set state), and nothing is set after unmount. Errors keep the last good data.
 */
export function useQuery<T>(load: () => Promise<T>, deps: readonly unknown[], fallbackError = 'Request failed.'): QueryState<T> {
  const [data, setData] = useState<T | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [failure, setFailure] = useState<unknown>(null);
  // The deps the last settled load ran with: loading is true until a load for the current deps settles.
  const depsKey = JSON.stringify(deps);
  const [settledKey, setSettledKey] = useState<string | null>(null);
  const depsKeyRef = useRef(depsKey);
  useEffect(() => { depsKeyRef.current = depsKey; });
  const [refreshing, setRefreshing] = useState(false);
  const loadRef = useRef(load);
  useEffect(() => { loadRef.current = load; });
  const seqRef = useRef(0);
  const mountedRef = useRef(true);
  useEffect(() => {
    mountedRef.current = true;
    return () => { mountedRef.current = false; };
  }, []);

  const reload = useCallback(async () => {
    const seq = ++seqRef.current;
    const key = depsKeyRef.current;
    try {
      const result = await loadRef.current();
      if (!mountedRef.current || seq !== seqRef.current) return;
      setData(result);
      setError(null);
      setFailure(null);
    } catch (e) {
      if (!mountedRef.current || seq !== seqRef.current) return;
      setError(errorMessage(e, fallbackError));
      setFailure(e);
    } finally {
      if (mountedRef.current && seq === seqRef.current) setSettledKey(key);
    }
  }, [fallbackError]);

  const refresh = useCallback(async () => {
    setRefreshing(true);
    try {
      await reload();
    } finally {
      if (mountedRef.current) setRefreshing(false);
    }
  }, [reload]);

  useEffect(() => {
    depsKeyRef.current = depsKey;
    void reload();
    // eslint-disable-next-line react-hooks/exhaustive-deps -- the caller lists what the load depends on
  }, deps);

  const loading = settledKey !== depsKey;
  return { data, error, failure, loading, refreshing, refresh, reload, setData };
}
