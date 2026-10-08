import { createContext, useContext } from 'react';
import { useLayout } from '../../navigation/useLayout';

/**
 * Width of the pane a component renders in, provided by SplitView for its list and detail panes. Rows and cards
 * adapt to it (not to the window), so a list beside its detail on an iPad gets the same compact layout as a phone.
 */
export const PaneWidthContext = createContext<number | null>(null);

/** Panes narrower than this stack a row's badge and meta under its text instead of beside it. */
export const COMPACT_ROW_WIDTH = 360;

/** The current pane's width: SplitView's pane, or the content area beside the sidebar outside one. */
export function usePaneWidth(): number {
  const pane = useContext(PaneWidthContext);
  const { contentWidth } = useLayout();
  return pane ?? contentWidth;
}

/** Whether rows in the current pane use the compact (stacked) layout. */
export function useCompactRows(): boolean {
  return usePaneWidth() < COMPACT_ROW_WIDTH;
}
