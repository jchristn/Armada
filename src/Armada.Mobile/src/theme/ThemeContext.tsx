import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import { useColorScheme } from 'react-native';
import { useIncreasedContrast } from '../lib/accessibility';
import { PREF_KEYS, readPref, writePref } from '../storage/prefs';
import { THEME_PREFERENCES, isDarkTheme, palettes, resolveTheme, type Palette, type ThemeName, type ThemePreference } from './palette';

export interface ThemeState {
  preference: ThemePreference;
  setPreference: (preference: ThemePreference) => void;
  /** The theme in effect (system resolved against the device setting). */
  name: ThemeName;
  colors: Palette;
  dark: boolean;
}

const ThemeContext = createContext<ThemeState | null>(null);

function isPreference(value: unknown): value is ThemePreference {
  return typeof value === 'string' && (THEME_PREFERENCES as string[]).includes(value);
}

export function ThemeProvider({ children }: { children: ReactNode }) {
  const scheme = useColorScheme();
  const increasedContrast = useIncreasedContrast();
  const [preference, setPreferenceState] = useState<ThemePreference>('system');

  useEffect(() => {
    let cancelled = false;
    void readPref<string>(PREF_KEYS.theme).then((stored) => {
      if (!cancelled && isPreference(stored)) setPreferenceState(stored);
    });
    return () => { cancelled = true; };
  }, []);

  const setPreference = useCallback((next: ThemePreference) => {
    setPreferenceState(next);
    void writePref(PREF_KEYS.theme, next);
  }, []);

  const value = useMemo<ThemeState>(() => {
    const name = resolveTheme(preference, scheme === 'dark' ? 'dark' : 'light', increasedContrast);
    return { preference, setPreference, name, colors: palettes[name], dark: isDarkTheme(name) };
  }, [preference, scheme, increasedContrast, setPreference]);

  return <ThemeContext.Provider value={value}>{children}</ThemeContext.Provider>;
}

export function useTheme(): ThemeState {
  const ctx = useContext(ThemeContext);
  if (!ctx) throw new Error('useTheme must be used within ThemeProvider');
  return ctx;
}
