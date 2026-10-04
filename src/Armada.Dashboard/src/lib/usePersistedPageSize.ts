import { useCallback, useState } from 'react';

const ALLOWED = [10, 25, 50, 100, 250];

/** Page size persisted per table in localStorage (falls back to `initial` when storage is unavailable). */
export function usePersistedPageSize(key: string, initial = 25): [number, (size: number) => void] {
  const storageKey = `armada_pagesize_${key}`;
  const [size, setSize] = useState<number>(() => {
    try {
      const raw = Number(localStorage.getItem(storageKey));
      if (ALLOWED.includes(raw)) return raw;
    } catch {
      // ignore storage errors
    }
    return initial;
  });
  const update = useCallback((next: number) => {
    setSize(next);
    try {
      localStorage.setItem(storageKey, String(next));
    } catch {
      // ignore storage errors
    }
  }, [storageKey]);
  return [size, update];
}
