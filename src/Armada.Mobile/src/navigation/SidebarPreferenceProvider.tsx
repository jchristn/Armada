import { useEffect, useMemo, useState, type ReactNode } from 'react';
import { PREF_KEYS, readPref, writePref } from '../storage/prefs';
import { SidebarPreferenceContext, type SidebarPreference } from './useLayout';

function isPreference(value: unknown): value is SidebarPreference {
  return value === 'auto' || value === 'expanded' || value === 'collapsed';
}

/**
 * The sidebar's expanded or collapsed state on wide windows, kept across launches. 'auto' (the default) shows the
 * icon rail on medium windows (iPad portrait, iPad mini, phones in landscape, foldables) and the full sidebar from
 * 1024 dp; the sidebar's toggle sets an explicit choice.
 */
export function SidebarPreferenceProvider({ children }: { children: ReactNode }) {
  const [preference, setPreferenceState] = useState<SidebarPreference>('auto');
  useEffect(() => {
    let active = true;
    void readPref<SidebarPreference>(PREF_KEYS.sidebar).then((stored) => {
      if (active && isPreference(stored)) setPreferenceState(stored);
    });
    return () => { active = false; };
  }, []);
  const value = useMemo(() => ({
    preference,
    setPreference: (next: SidebarPreference) => {
      setPreferenceState(next);
      void writePref(PREF_KEYS.sidebar, next);
    },
  }), [preference]);
  return <SidebarPreferenceContext.Provider value={value}>{children}</SidebarPreferenceContext.Provider>;
}
