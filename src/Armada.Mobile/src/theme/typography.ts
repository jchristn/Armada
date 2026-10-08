import { Platform, useWindowDimensions, type TextStyle } from 'react-native';

/**
 * Type scale. Sizes are base sizes: React Native scales them with the user's font size setting (Dynamic Type /
 * Android font scale) because every Text keeps allowFontScaling on. Chrome that cannot grow without breaking the
 * layout (tab labels, badges) caps the multiplier instead of disabling scaling.
 */
export const typography = {
  title: { fontSize: 28, fontWeight: '700', lineHeight: 34 } satisfies TextStyle,
  heading: { fontSize: 20, fontWeight: '600', lineHeight: 26 } satisfies TextStyle,
  subheading: { fontSize: 13, fontWeight: '600', letterSpacing: 0.6, lineHeight: 18 } satisfies TextStyle,
  body: { fontSize: 16, lineHeight: 22 } satisfies TextStyle,
  label: { fontSize: 15, fontWeight: '500', lineHeight: 20 } satisfies TextStyle,
  caption: { fontSize: 13, lineHeight: 18 } satisfies TextStyle,
  mono: { fontSize: 14, fontFamily: Platform.select({ ios: 'Menlo', default: 'monospace' }), lineHeight: 20 } satisfies TextStyle,
};

export type TypographyVariant = keyof typeof typography;

/** Largest font scale for compact chrome (tab bar labels, badges); body text is never capped. */
export const CHROME_MAX_FONT_SCALE = 1.6;

/**
 * Button labels grow up to this multiple of their size: at the largest accessibility sizes a fully scaled label in a
 * half-width toolbar button broke mid-word ("Missi" / "on"). Twice the size stays readable and keeps words whole.
 */
export const BUTTON_MAX_FONT_SCALE = 2;

/** From this text size on (Dynamic Type accessibility sizes, Android 150% and up), layouts stack instead of sitting side by side. */
export const LARGE_TEXT_SCALE = 1.5;

/** True when the user's text size calls for stacked layouts (see LARGE_TEXT_SCALE). */
export function useLargeText(): boolean {
  return useWindowDimensions().fontScale >= LARGE_TEXT_SCALE;
}

/** Minimum touch target (Apple HIG 44 pt, Material 48 dp). */
export const MIN_TOUCH = 48;

/**
 * Extra touch area for a compact control drawn shorter than MIN_TOUCH (a chip, a pill tab, a disclosure line), so
 * the part a finger can hit still reaches MIN_TOUCH. Pass as the Pressable's hitSlop.
 */
export function touchSlop(drawnHeight: number): { top: number; bottom: number; left: number; right: number } {
  const extra = Math.max(0, Math.ceil((MIN_TOUCH - drawnHeight) / 2));
  return { top: extra, bottom: extra, left: 0, right: 0 };
}

export const spacing = { xs: 4, sm: 8, md: 12, lg: 16, xl: 24, xxl: 32 };

export const radius = { sm: 6, md: 10, lg: 16, pill: 999 };
