import { useRouter, type Href } from 'expo-router';
import { useCallback, useState } from 'react';
import { useLayout } from '../../navigation/useLayout';

export interface SplitSelection {
  /** The item shown in the detail pane (tablets only; always null on phones). */
  selectedId: string | null;
  /** Phones: push the item's route. Tablets: show it in the detail pane. */
  select: (id: string) => void;
  clear: () => void;
  isTablet: boolean;
}

/**
 * Selection for list-detail screens. On tablets (>= 768 dp) a list keeps its selection and the screen renders the
 * detail beside it (SplitView); on phones selecting pushes the item's own route (for example /missions/msn_1), so
 * back navigation and deep links behave as on the dashboard.
 */
export function useSplitSelection(routeFor: (id: string) => string): SplitSelection {
  const router = useRouter();
  const { isTablet } = useLayout();
  const [selected, setSelected] = useState<string | null>(null);
  const select = useCallback((id: string) => {
    if (isTablet) setSelected(id);
    else router.push(routeFor(id) as Href);
  }, [isTablet, router, routeFor]);
  const clear = useCallback(() => setSelected(null), []);
  return { selectedId: isTablet ? selected : null, select, clear, isTablet };
}
