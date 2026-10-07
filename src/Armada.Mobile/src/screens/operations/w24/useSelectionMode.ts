import { useCallback, useState } from 'react';

/** Pure: toggles an id in a selection. */
export function toggleId(selected: readonly string[], id: string): string[] {
  return selected.includes(id) ? selected.filter((x) => x !== id) : [...selected, id];
}

/**
 * Bulk selection for lists (the dashboard's row checkboxes): a long press starts selecting, taps then toggle rows,
 * and selection ends when nothing is selected or the user cancels.
 */
export function useSelectionMode() {
  const [selected, setSelected] = useState<string[]>([]);
  const [active, setActive] = useState(false);
  const toggle = useCallback((id: string) => {
    setSelected((current) => {
      const next = toggleId(current, id);
      if (next.length === 0) setActive(false);
      return next;
    });
  }, []);
  const start = useCallback((id: string) => {
    setActive(true);
    setSelected((current) => (current.includes(id) ? current : [...current, id]));
  }, []);
  const clear = useCallback(() => {
    setSelected([]);
    setActive(false);
  }, []);
  const selectAll = useCallback((ids: string[]) => {
    setActive(true);
    setSelected(ids);
  }, []);
  return { selected, active, toggle, start, clear, selectAll };
}
