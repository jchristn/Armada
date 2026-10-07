/**
 * Props shared by the Operations list components (missions, voyages, merge queue, docks, signals, events). A list
 * reports the row the user opened through `onSelect`; the hosting screen decides whether that pushes the item's
 * route (phones) or shows it in the split view's detail pane (tablets), see useSplitSelection.
 */
export interface OperationsListProps {
  onSelect: (id: string) => void;
  /** Highlighted row (tablet split view). */
  selectedId?: string | null;
}

/** Props shared by the Operations detail components (used by the route screens and by tablet split views). */
export interface OperationsDetailProps {
  id: string;
  /** Rendered inside a split view pane: the component does not set the navigation title. */
  embedded?: boolean;
}
