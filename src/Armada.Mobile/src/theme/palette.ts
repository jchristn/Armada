/** Color tokens for one theme. Every component reads colors from here, never literals. */
export interface Palette {
  background: string;
  surface: string;
  surfaceRaised: string;
  border: string;
  text: string;
  textMuted: string;
  textInverse: string;
  primary: string;
  primaryText: string;
  danger: string;
  dangerText: string;
  warning: string;
  warningSurface: string;
  success: string;
  info: string;
  focus: string;
  overlay: string;
  badge: string;
  badgeText: string;
}

/** The themes the app ships. `system` is a preference, not a theme: it resolves to light or dark. */
export type ThemeName = 'light' | 'dark' | 'highContrast';

/** What the user picked in Preferences. */
export type ThemePreference = 'system' | 'light' | 'dark' | 'highContrast';

export const THEME_PREFERENCES: ThemePreference[] = ['system', 'light', 'dark', 'highContrast'];

// Contrast targets: body text >= 7:1 and muted text >= 4.5:1 on background and surface in light and dark;
// high contrast uses pure black and white with saturated accents (>= 7:1 for everything readable).
export const palettes: Record<ThemeName, Palette> = {
  light: {
    background: '#f3f4f6',
    surface: '#ffffff',
    surfaceRaised: '#ffffff',
    border: '#d1d5db',
    text: '#111827',
    textMuted: '#4b5563',
    textInverse: '#ffffff',
    primary: '#1d4ed8',
    primaryText: '#ffffff',
    danger: '#b91c1c',
    dangerText: '#ffffff',
    warning: '#92400e',
    warningSurface: '#fef3c7',
    success: '#15803d',
    info: '#1d4ed8',
    focus: '#1d4ed8',
    overlay: 'rgba(17,24,39,0.45)',
    badge: '#b91c1c',
    badgeText: '#ffffff',
  },
  dark: {
    background: '#0b1120',
    surface: '#111827',
    surfaceRaised: '#1f2937',
    border: '#374151',
    text: '#f9fafb',
    textMuted: '#9ca3af',
    textInverse: '#111827',
    primary: '#60a5fa',
    primaryText: '#0b1120',
    danger: '#f87171',
    dangerText: '#0b1120',
    warning: '#fbbf24',
    warningSurface: '#3b2a06',
    success: '#4ade80',
    info: '#60a5fa',
    focus: '#93c5fd',
    overlay: 'rgba(0,0,0,0.6)',
    badge: '#dc2626',
    badgeText: '#ffffff',
  },
  highContrast: {
    background: '#000000',
    surface: '#000000',
    surfaceRaised: '#0a0a0a',
    border: '#ffffff',
    text: '#ffffff',
    textMuted: '#e5e5e5',
    textInverse: '#000000',
    primary: '#ffd60a',
    primaryText: '#000000',
    danger: '#ff6b6b',
    dangerText: '#000000',
    warning: '#ffd60a',
    warningSurface: '#1a1400',
    success: '#7CFC00',
    info: '#7dd3fc',
    focus: '#ffd60a',
    overlay: 'rgba(0,0,0,0.85)',
    badge: '#ffd60a',
    badgeText: '#000000',
  },
};

/** Resolve a preference against the device scheme. */
export function resolveTheme(preference: ThemePreference, deviceScheme: 'light' | 'dark' | null | undefined): ThemeName {
  if (preference === 'system') return deviceScheme === 'dark' ? 'dark' : 'light';
  return preference;
}

/** True for themes drawn on a dark background (status bar and navigation chrome follow it). */
export function isDarkTheme(name: ThemeName): boolean {
  return name !== 'light';
}
