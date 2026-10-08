import { useListSelection } from '../../navigation/listDetail';

export interface SplitSelection {
  /** The item shown in the detail pane (kept when the window narrows; then shown alone with a way back). */
  selectedId: string | null;
  /** Wide panes: show the item beside the list. Narrow panes: push the item's route. */
  select: (id: string) => void;
  clear: () => void;
  /** List and detail are side by side. */
  isTablet: boolean;
}

/**
 * Selection for list-detail screens (see useListSelection): wide content panes keep the selection and show the
 * detail beside the list (SplitView); narrow ones push the item's own route (for example /missions/msn_1).
 */
export function useSplitSelection(routeFor: (id: string) => string): SplitSelection {
  const { selectedId, select, clear, split } = useListSelection(routeFor);
  return { selectedId, select, clear, isTablet: split };
}
