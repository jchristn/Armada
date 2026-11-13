import { describe, expect, it } from 'vitest';
import catalogSource from '../../../Armada.Server/wwwroot/i18n/armada.json?raw';
import titlesSource from './deleteProgress.ts?raw';
import hookSource from './useDeleteProgress.tsx?raw';
import dialogSource from '../components/shared/DeleteProgressDialog.tsx?raw';
import { translateTemplate, type I18nCatalog } from '../i18n/runtime';
import { deleteMatchingRequestHistoryTitle, deleteProgressMessage, deleteProgressTitle, type DeleteProgressKind } from './deleteProgress';

const S = "'((?:[^'\\\\]|\\\\.)*)'";
const CALL = new RegExp(`\\bt\\(\\s*${S}`, 'g');
const TRANSLATE_CALL = new RegExp(`\\btranslate\\(\\s*${S}`, 'g');

/** Every literal the delete progress modal passes to the translator. */
function keys(): string[] {
  const found = new Set<string>();
  for (const src of [titlesSource, hookSource, dialogSource]) {
    for (const m of src.matchAll(CALL)) found.add(m[1].replace(/\\'/g, "'"));
    for (const m of src.matchAll(TRANSLATE_CALL)) found.add(m[1].replace(/\\'/g, "'"));
  }
  return [...found].sort();
}

function placeholders(text: string): string {
  return (text.match(/\{\{[\w.]+\}\}/g) ?? []).sort().join(',');
}

const catalog = JSON.parse(catalogSource) as I18nCatalog;
const english = (text: string, params?: Record<string, string | number | null | undefined>) => translateTemplate('en', text, null, params);

describe('delete progress i18n catalog', () => {
  const all = keys();

  it('finds the delete progress strings', () => {
    expect(all).toContain('Deleting vessel');
    expect(all).toContain('{count, plural, one {Deleting # mission} other {Deleting # missions}}');
    expect(all).toContain('Purging voyage');
    expect(all).toContain('This may take some time while related records are cleaned up.');
    expect(all).toContain('Try the delete again.');
    expect(all).toContain('Dismiss');
  });

  it('has a translation for every string in every non-English locale', () => {
    const missing: string[] = [];
    for (const meta of catalog.supportedLocales) {
      if (meta.code === catalog.defaultLocale) continue;
      const pack = catalog.locales[meta.code] ?? {};
      for (const key of all) {
        if (!pack.phrases?.[key] && !pack.terms?.[key] && !pack.sections?.[key]) missing.push(`${meta.code}: ${key}`);
      }
    }
    expect(missing).toEqual([]);
  });

  it('keeps placeholders and plural blocks intact in translations', () => {
    const bad: string[] = [];
    for (const [code, pack] of Object.entries(catalog.locales)) {
      for (const key of all) {
        const value = pack.phrases?.[key];
        if (!value) continue;
        if (placeholders(key) !== placeholders(value)) bad.push(`${code}: ${key}`);
        if (key.startsWith('{count, plural,') && !(value.startsWith('{count, plural,') && value.includes('other {') && value.includes('#'))) {
          bad.push(`${code}: ${key}`);
        }
      }
    }
    expect(bad).toEqual([]);
  });

  it('formats single and bulk titles', () => {
    expect(deleteProgressTitle(english, 'vessel')).toBe('Deleting vessel');
    expect(deleteProgressTitle(english, 'mission', 12)).toBe('Deleting 12 missions');
    expect(deleteProgressTitle(english, 'mergeEntry', 1)).toBe('Deleting 1 merge queue entry');
    expect(deleteProgressTitle(english, 'missionPurge', 3)).toBe('Purging 3 missions');
    expect(deleteMatchingRequestHistoryTitle(english)).toBe('Deleting matching request history entries');
    expect(deleteProgressMessage(english)).toBe('This may take some time while related records are cleaned up.');
  });

  it('formats a bulk title with the locale plural rules', () => {
    const de = (text: string, params?: Record<string, string | number | null | undefined>) => translateTemplate('de', text, catalog, params);
    const ja = (text: string, params?: Record<string, string | number | null | undefined>) => translateTemplate('ja', text, catalog, params);
    const kinds: DeleteProgressKind[] = ['fleet', 'mission', 'credential'];
    for (const kind of kinds) {
      expect(deleteProgressTitle(de, kind, 2)).toContain('2 ');
      expect(deleteProgressTitle(ja, kind, 2)).toContain('2 ');
    }
    expect(deleteProgressTitle(de, 'mission', 1)).toBe('1 Mission wird gelöscht');
    expect(deleteProgressTitle(de, 'mission', 4)).toBe('4 Missionen werden gelöscht');
  });
});
