import { useRouter, type Href } from 'expo-router';
import { createContext, useCallback, useContext, useState, type ReactNode } from 'react';
import { splitFits, useLayout } from './useLayout';

/**
 * The item a detail route (/missions/msn_1, /deployments/dpl_1) opened, handed to the list screen it renders on
 * wide windows: the selection hook whose route matches `path` starts with `id` selected, and a hub shows `tab`.
 */
export interface InitialSelection {
  path: string;
  id: string;
  tab?: string;
}

export const InitialSelectionContext = createContext<InitialSelection | null>(null);

/** The hub tab a detail route asks for (null outside one). */
export function useInitialHubTab(): string | null {
  return useContext(InitialSelectionContext)?.tab ?? null;
}

export interface ListSelection {
  /** The item shown in the detail pane (kept when the window narrows, so the detail stays on screen). */
  selectedId: string | null;
  /** Wide panes: show the item beside the list. Narrow panes: push the item's own route. */
  select: (id: string) => void;
  clear: () => void;
  /** List and detail are side by side now. */
  split: boolean;
}

/**
 * Selection for list-detail screens, decided by the content pane's width (not the device): when list and detail fit
 * side by side, selecting keeps the list and shows the item beside it (SplitView); otherwise it pushes the item's
 * own route (for example /missions/msn_1), so back navigation and deep links behave as on the dashboard. The
 * selection survives a window resize: narrowed, the detail shows alone with a way back (SplitView onBack).
 */
export function useListSelection(routeFor: (id: string) => string, minWidth?: number): ListSelection {
  const router = useRouter();
  const { contentWidth } = useLayout();
  const split = splitFits(contentWidth, minWidth);
  const initial = useContext(InitialSelectionContext);
  const [selectedId, setSelectedId] = useState<string | null>(() => (initial && routeFor(initial.id) === initial.path ? initial.id : null));
  const select = useCallback((id: string) => {
    if (split) setSelectedId(id);
    else router.push(routeFor(id) as Href);
  }, [split, router, routeFor]);
  const clear = useCallback(() => setSelectedId(null), []);
  return { selectedId, select, clear, split };
}

export interface ListDetailRouteProps {
  /** The route's own path (what the list's routeFor returns for `id`). */
  path: string;
  id: string;
  /** Hub tab that holds the list (Delivery's environments, the Missions hub's voyages). */
  tab?: string;
  /** The list screen, rendered with `id` selected when the window is wide enough for both panes. */
  list: ReactNode;
  /** The item alone (narrow windows, as before). */
  detail: ReactNode;
}

/**
 * A detail route that, on a window wide enough for list and detail, opens as its list with the item selected in the
 * detail pane (a deep link, push notification, or in-app link lands with the list on the left). The choice is made
 * once, when the route opens: later resizes re-layout inside it (SplitView) instead of swapping screens, so the
 * item's state is kept.
 */
export function ListDetailRoute({ path, id, tab, list, detail }: ListDetailRouteProps) {
  const { contentWidth } = useLayout();
  const [asList] = useState(() => splitFits(contentWidth));
  if (!asList || !id) return <>{detail}</>;
  return <InitialSelectionContext.Provider value={{ path, id, tab }}>{list}</InitialSelectionContext.Provider>;
}
