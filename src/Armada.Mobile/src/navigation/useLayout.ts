import { useWindowDimensions } from 'react-native';

/** Width at which the app switches from phone tabs to the tablet sidebar and split views. */
export const TABLET_MIN_WIDTH = 768;

export interface LayoutInfo {
  width: number;
  height: number;
  isTablet: boolean;
  landscape: boolean;
}

/** Pure form of the adaptive switch (unit-tested); width in dp. */
export function layoutFor(width: number, height: number): LayoutInfo {
  return { width, height, isTablet: width >= TABLET_MIN_WIDTH, landscape: width > height };
}

/**
 * Current layout class. Uses the window size (not the screen), so iPad Split View, Stage Manager, and Android
 * multi-window and foldables switch layouts as the window changes.
 */
export function useLayout(): LayoutInfo {
  const { width, height } = useWindowDimensions();
  return layoutFor(width, height);
}
