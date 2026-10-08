import { createContext, useContext } from 'react';
import { useWindowDimensions } from 'react-native';

/**
 * Below this window width the app uses the phone layout: bottom tabs, one pane. iPhones in portrait, iPad Slide Over
 * and the narrow Split View / Stage Manager sizes, and folded foldables are all narrower.
 */
export const COMPACT_MAX_WIDTH = 600;
/** From this window width the full sidebar (icons and labels) shows by default; between the two it is a rail. */
export const EXPANDED_MIN_WIDTH = 1024;
/** The full sidebar. */
export const SIDEBAR_WIDTH = 248;
/** The collapsed sidebar: icons only. */
export const RAIL_WIDTH = 76;
/**
 * The content pane (window minus the sidebar or rail) needs at least this much width to show a list and its detail
 * side by side; narrower panes show one at a time.
 */
export const SPLIT_MIN_CONTENT_WIDTH = 640;
/** Narrowest list pane in a split view, and the narrowest detail pane next to it. */
export const MASTER_MIN_WIDTH = 280;
export const DETAIL_MIN_WIDTH = 360;

/** How the app navigates at the current window size. */
export type NavMode = 'tabs' | 'rail' | 'sidebar';

/** The user's choice for the sidebar on wide windows ('auto' follows the window width). */
export type SidebarPreference = 'auto' | 'expanded' | 'collapsed';

export interface LayoutInfo {
  width: number;
  height: number;
  /** Bottom tabs, the icon rail, or the full sidebar. */
  navMode: NavMode;
  /** Width taken by the sidebar or rail (0 with tabs). */
  navWidth: number;
  /** Width left for the screen beside the sidebar or rail: what list and detail panes share. */
  contentWidth: number;
  /** The sidebar (full or rail) replaces the tab bar: the window is not phone-narrow. */
  isTablet: boolean;
  /** List-detail screens show both panes side by side (the content pane is wide enough). */
  split: boolean;
  landscape: boolean;
}

/** The navigation mode for a window width and the user's sidebar preference. */
export function navModeFor(width: number, preference: SidebarPreference = 'auto'): NavMode {
  if (width < COMPACT_MAX_WIDTH) return 'tabs';
  if (preference === 'expanded') return 'sidebar';
  if (preference === 'collapsed') return 'rail';
  return width >= EXPANDED_MIN_WIDTH ? 'sidebar' : 'rail';
}

/** Whether a pane this wide shows list and detail side by side (`minWidth` lets a screen ask for more room). */
export function splitFits(paneWidth: number, minWidth: number = SPLIT_MIN_CONTENT_WIDTH): boolean {
  return paneWidth >= minWidth;
}

/**
 * Width of the list pane in a split view of `paneWidth`: 40% of the pane, at least MASTER_MIN_WIDTH, at most
 * `maxWidth`, and never so wide that the detail drops below DETAIL_MIN_WIDTH.
 */
export function masterPaneWidth(paneWidth: number, maxWidth = 360): number {
  const preferred = Math.round(paneWidth * 0.4);
  const capped = Math.min(Math.max(preferred, MASTER_MIN_WIDTH), maxWidth, paneWidth - DETAIL_MIN_WIDTH);
  return Math.max(capped, Math.min(MASTER_MIN_WIDTH, paneWidth));
}

/** Windows at least this wide and tall present sheets as centered form sheets (iPad, tablets, unfolded foldables). */
export const FORM_SHEET_MIN_WIDTH = 600;
export const FORM_SHEET_MIN_HEIGHT = 600;

/** Whether sheets show as centered form sheets: phones (and phones in landscape, which are short) keep bottom sheets. */
export function formSheetFits(width: number, height: number): boolean {
  return width >= FORM_SHEET_MIN_WIDTH && height >= FORM_SHEET_MIN_HEIGHT;
}

/** Pure form of the adaptive layout (unit-tested); width and height of the window in dp. */
export function layoutFor(width: number, height: number, preference: SidebarPreference = 'auto'): LayoutInfo {
  const navMode = navModeFor(width, preference);
  const navWidth = navMode === 'sidebar' ? SIDEBAR_WIDTH : navMode === 'rail' ? RAIL_WIDTH : 0;
  const contentWidth = Math.max(0, width - navWidth);
  return {
    width,
    height,
    navMode,
    navWidth,
    contentWidth,
    isTablet: navMode !== 'tabs',
    split: splitFits(contentWidth),
    landscape: width > height,
  };
}

export interface SidebarPreferenceState {
  preference: SidebarPreference;
  setPreference: (next: SidebarPreference) => void;
}

/** The sidebar preference, provided by SidebarPreferenceProvider at the app root ('auto' without one). */
export const SidebarPreferenceContext = createContext<SidebarPreferenceState>({ preference: 'auto', setPreference: () => undefined });

export function useSidebarPreference(): SidebarPreferenceState {
  return useContext(SidebarPreferenceContext);
}

/**
 * Current layout. Uses the window size (not the screen or the device type), so iPad Split View, Slide Over, Stage
 * Manager, Android multi-window, foldables, and rotation re-layout live as the window changes.
 */
export function useLayout(): LayoutInfo {
  const { width, height } = useWindowDimensions();
  const { preference } = useContext(SidebarPreferenceContext);
  return layoutFor(width, height, preference);
}
