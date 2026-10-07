import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { getLocales } from 'expo-localization';
import {
  CATALOG_PATH,
  DEFAULT_LOCALES,
  fetchCatalog,
  formatAbsoluteDateTime,
  formatDateOnly,
  formatRelativeFromUtc,
  getSupportedLocales,
  normalizeLocale,
  pickInitialLocale,
  translateTemplate,
  type I18nCatalog,
  type LocaleMeta,
} from '@dashboard/i18n/catalog';
import { PREF_KEYS, readPref, writePref } from '../storage/prefs';

/**
 * The mobile counterpart of the dashboard's LocaleContext, built on the same shared catalog logic
 * (`@dashboard/i18n/catalog`). The catalog is bundled into the app at build time (loaded lazily, only when a
 * non-English locale is in use) and replaced by the connected server's catalog after sign-in, so strings added on
 * the server arrive without an app update.
 */
export type Translate = (text: string, params?: Record<string, string | number | null | undefined>) => string;

export interface LocaleState {
  locale: string;
  setLocale: (locale: string) => void;
  supportedLocales: LocaleMeta[];
  catalog: I18nCatalog | null;
  /** Where the active catalog came from. */
  catalogSource: 'none' | 'bundled' | 'server';
  t: Translate;
  formatDateTime: (utc: string | null | undefined) => string;
  formatDate: (utc: string | null | undefined) => string;
  formatRelativeTime: (utc: string | null | undefined) => string;
}

const LocaleContext = createContext<LocaleState | null>(null);

/** The catalog shipped inside the app (src/Armada.Server/wwwroot/i18n/armada.json at build time). */
export function loadBundledCatalog(): I18nCatalog {
  // Required lazily so English-only sessions never parse the 1 MB catalog at startup.
  return require('@armada-i18n/armada.json') as I18nCatalog;
}

function deviceLocales(): string[] {
  try {
    return getLocales().map((l) => l.languageTag).filter((tag): tag is string => !!tag);
  } catch {
    return [];
  }
}

/**
 * The server sends .NET timestamps with up to seven fractional digits ("...:43.592272Z"); Hermes' Date parser only
 * accepts up to three, so trim the fraction to milliseconds before parsing. Other values pass through unchanged.
 */
export function normalizeUtc(utc: string | null | undefined): string | null | undefined {
  if (!utc) return utc;
  return utc.replace(/(T\d{2}:\d{2}:\d{2}\.\d{3})\d+/, '$1');
}

function safeFormat(format: () => string, fallback: string): string {
  try {
    return format();
  } catch {
    // Some Intl APIs are missing on older JS engines; show the raw value rather than crash a screen.
    return fallback;
  }
}

export interface LocaleProviderProps {
  children: ReactNode;
  /** Server base URL to fetch the live catalog from once signed in (null while signed out). */
  serverUrl: string | null;
  /** Injectable for tests. */
  bundledCatalog?: () => I18nCatalog;
}

export function LocaleProvider({ children, serverUrl, bundledCatalog = loadBundledCatalog }: LocaleProviderProps) {
  const [locale, setLocaleState] = useState(() => pickInitialLocale(null, deviceLocales(), null));
  const [catalog, setCatalog] = useState<I18nCatalog | null>(null);
  const [catalogSource, setCatalogSource] = useState<LocaleState['catalogSource']>('none');
  const catalogRef = useRef<I18nCatalog | null>(null);

  useEffect(() => { catalogRef.current = catalog; }, [catalog]);

  // Stored choice (async storage) wins over the device locale chosen synchronously above.
  useEffect(() => {
    let cancelled = false;
    void readPref<string>(PREF_KEYS.locale).then((stored) => {
      if (!cancelled && stored) setLocaleState(normalizeLocale(stored, catalogRef.current));
    });
    return () => { cancelled = true; };
  }, []);

  // English needs no catalog; any other locale loads the bundled one the first time it is needed.
  useEffect(() => {
    if (locale === 'en' || catalogRef.current) return;
    const bundled = bundledCatalog();
    catalogRef.current = bundled;
    setCatalog(bundled);
    setCatalogSource('bundled');
  }, [locale, bundledCatalog]);

  // After sign-in, prefer the connected server's catalog (same file the dashboard loads).
  useEffect(() => {
    if (!serverUrl) return undefined;
    let cancelled = false;
    void fetchCatalog(`${serverUrl}${CATALOG_PATH}`).then((loaded) => {
      if (cancelled || !loaded || !Array.isArray(loaded.supportedLocales) || !loaded.locales) return;
      catalogRef.current = loaded;
      setCatalog(loaded);
      setCatalogSource('server');
    });
    return () => { cancelled = true; };
  }, [serverUrl]);

  const setLocale = useCallback((next: string) => {
    const normalized = normalizeLocale(next, catalogRef.current);
    setLocaleState(normalized);
    void writePref(PREF_KEYS.locale, normalized);
  }, []);

  const value = useMemo<LocaleState>(() => {
    const supported = getSupportedLocales(catalog);
    return {
      locale,
      setLocale,
      supportedLocales: supported.length > 0 ? supported : DEFAULT_LOCALES,
      catalog,
      catalogSource,
      // A new function identity per locale/catalog so memoized consumers re-render with the new language.
      t: (text, params) => translateTemplate(locale, text, catalog, params),
      formatDateTime: (utc) => safeFormat(() => formatAbsoluteDateTime(locale, normalizeUtc(utc)), utc ?? ''),
      formatDate: (utc) => safeFormat(() => formatDateOnly(locale, normalizeUtc(utc)), utc ?? ''),
      // Without Intl.RelativeTimeFormat (some engines), fall back to the absolute local time, not the raw ISO text.
      formatRelativeTime: (utc) => safeFormat(
        () => formatRelativeFromUtc(locale, normalizeUtc(utc)),
        safeFormat(() => formatAbsoluteDateTime(locale, normalizeUtc(utc)), utc ?? '-'),
      ),
    };
  }, [catalog, catalogSource, locale, setLocale]);

  return <LocaleContext.Provider value={value}>{children}</LocaleContext.Provider>;
}

export function useLocale(): LocaleState {
  const ctx = useContext(LocaleContext);
  if (!ctx) throw new Error('useLocale must be used within LocaleProvider');
  return ctx;
}
