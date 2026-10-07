import { DarkTheme, DefaultTheme } from 'expo-router';
import type { Palette } from '../theme/palette';

/** React Navigation theme (headers, tab bar, card background) from the app palette. */
export function navigationTheme(colors: Palette, dark: boolean): typeof DefaultTheme {
  const base = dark ? DarkTheme : DefaultTheme;
  return {
    ...base,
    dark,
    colors: {
      ...base.colors,
      primary: colors.primary,
      background: colors.background,
      card: colors.surface,
      text: colors.text,
      border: colors.border,
      notification: colors.badge,
    },
  };
}
