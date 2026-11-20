/*
 * The extension's display strings in VS Code's language, from the Admiral's translation catalog (the same one the
 * dashboard and the TUI use, @dashboard/i18n/catalog). English needs no catalog, so it is only loaded for other
 * languages. Labels pass through t(); anything the catalog lacks stays English. No `vscode` import.
 */

import { CATALOG_PATH, fetchCatalog, I18nCatalog, normalizeLocale, translateText } from '@dashboard/i18n/catalog';

let _Catalog: I18nCatalog | null = null;
let _Locale = 'en';

/** The active locale ("en" when untranslated). */
export function activeLocale(): string {
  return _Catalog ? _Locale : 'en';
}

/** Translates a display string (identity in English or before the catalog loads). */
export function t(text: string): string {
  if (!_Catalog || _Locale === 'en' || !text) return text;
  return translateText(_Locale, text, _Catalog);
}

/** Uses a catalog (tests, or after loading). */
export function useCatalog(catalog: I18nCatalog | null, language: string): void {
  _Catalog = catalog;
  _Locale = catalog ? normalizeLocale(language, catalog) : 'en';
}

/**
 * Loads the Admiral's catalog for a display language other than English. Resolves true when strings are now
 * translated. Never throws.
 */
export async function loadCatalog(baseUrl: string, language: string): Promise<boolean> {
  if (!language || /^en(-|$)/i.test(language)) {
    useCatalog(null, 'en');
    return false;
  }
  const catalog = await fetchCatalog(`${baseUrl.replace(/\/+$/, '')}${CATALOG_PATH}`);
  useCatalog(catalog, language);
  return activeLocale() !== 'en';
}
