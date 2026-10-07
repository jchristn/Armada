import { describe, expect, it } from 'vitest';
import catalogSource from '../../../Armada.Server/wwwroot/i18n/armada.json?raw';
import dataTableSource from '../components/shared/DataTable.tsx?raw';
import chooserSource from '../components/shared/ColumnChooser.tsx?raw';
import paginationSource from '../components/shared/Pagination.tsx?raw';
import autoRefreshSource from '../components/shared/AutoRefreshSelect.tsx?raw';
import type { I18nCatalog } from '../i18n/runtime';

const S = "'((?:[^'\\\\]|\\\\.)*)'";
const T_CALL = new RegExp(`\\bt\\(\\s*${S}`, 'g');

/** Strings the shared table chrome and the table migrations added. */
const ADDED_KEYS = [
  'Default (global)',
  'Filter vessels by name',
  'Filter vessels by repository',
  'Filter vessels by landing mode',
];

function extractKeys(sources: string[]): string[] {
  const keys = new Set<string>(ADDED_KEYS);
  for (const src of sources) {
    for (const m of src.matchAll(T_CALL)) keys.add(m[1].replace(/\\'/g, "'"));
  }
  return [...keys];
}

describe('data table i18n catalog', () => {
  const catalog = JSON.parse(catalogSource) as I18nCatalog;
  const keys = extractKeys([dataTableSource, chooserSource, paginationSource, autoRefreshSource]);

  it('finds the table chrome strings', () => {
    expect(keys).toContain('Columns ({{count}} hidden)');
    expect(keys).toContain('Reset to default');
    expect(keys).toContain('Always shown');
    expect(keys).toContain('Auto-refresh interval');
  });

  it('has a translation for every table chrome string in every non-English locale', () => {
    const missing: string[] = [];
    for (const meta of catalog.supportedLocales) {
      if (meta.code === catalog.defaultLocale) continue;
      const pack = catalog.locales[meta.code] ?? {};
      for (const key of keys) {
        if (!pack.phrases?.[key] && !pack.terms?.[key] && !pack.sections?.[key]) missing.push(`${meta.code}: ${key}`);
      }
    }
    expect(missing).toEqual([]);
  });

  it('keeps placeholders intact in translations', () => {
    const bad: string[] = [];
    for (const [code, pack] of Object.entries(catalog.locales)) {
      for (const key of keys) {
        const value = pack.phrases?.[key];
        if (!value) continue;
        const want = (key.match(/\{\{\w+\}\}/g) ?? []).sort().join(',');
        const got = (value.match(/\{\{\w+\}\}/g) ?? []).sort().join(',');
        if (want !== got) bad.push(`${code}: ${key}`);
      }
    }
    expect(bad).toEqual([]);
  });
});
