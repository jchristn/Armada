import { describe, expect, it } from 'vitest';
import catalogSource from '../../../Armada.Server/wwwroot/i18n/armada.json?raw';
import pageSource from '../pages/BatteryPacks.tsx?raw';
import type { I18nCatalog } from '../i18n/runtime';

const S = "'((?:[^'\\\\]|\\\\.)*)'";
const CALL = new RegExp(`\\bt\\(\\s*${S}`, 'g');

/** Strings the Battery packs page translates outside a t('...') literal: row action menu labels (ActionMenu) and the
 * status badge values (StatusBadge) and confirm dialog title (ConfirmDialog). */
const SHARED_KEYS = ['View/Edit', 'View', 'Validate', 'Delete', 'Valid', 'Invalid'];

function batteryKeys(): string[] {
  const keys = new Set<string>(SHARED_KEYS);
  for (const m of pageSource.matchAll(CALL)) {
    const key = m[1].replace(/\\'/g, "'");
    if (key.trim()) keys.add(key);
  }
  return [...keys].sort();
}

function placeholders(text: string): string {
  return (text.match(/\{\{[\w.]+\}\}/g) ?? []).sort().join(',');
}

describe('Battery packs i18n catalog', () => {
  const catalog = JSON.parse(catalogSource) as I18nCatalog;
  const keys = batteryKeys();

  it('finds the Battery packs page strings, including the column tooltips', () => {
    expect(keys.length).toBeGreaterThan(30);
    expect(keys).toContain('Battery packs');
    expect(keys).toContain('Result of the last validation. Hover an invalid pack for its errors.');
  });

  it('has a translation for every string in every non-English locale', () => {
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
        const value = pack.phrases?.[key] ?? pack.terms?.[key];
        if (value && placeholders(key) !== placeholders(value)) bad.push(`${code}: ${key}`);
      }
    }
    expect(bad).toEqual([]);
  });
});
