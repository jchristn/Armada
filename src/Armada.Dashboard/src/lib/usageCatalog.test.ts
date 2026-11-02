import { describe, expect, it } from 'vitest';
import catalogSource from '../../../Armada.Server/wwwroot/i18n/armada.json?raw';
import type { I18nCatalog } from '../i18n/runtime';
import { USAGE_KIND_HINT, USAGE_KIND_TEXT, USAGE_LEVEL_TEXT } from './usage';
import { USAGE_WINDOW_LABELS } from './usageWindowLabel';
import { USAGE_CAPTURE_FORM_ERROR_TEXT } from './usageCaptureForm';

// Every usage surface file; a new usage file must be added here so its strings are gated.
const sources = import.meta.glob(['../components/usage/*.tsx', '!../components/usage/*.test.tsx'], { query: '?raw', import: 'default', eager: true }) as Record<string, string>;

/**
 * Strings this feature added to existing pages. Those pages carry older strings outside this gate, so only the
 * usage additions are listed (and asserted present in the page source so the list cannot go stale).
 */
const PAGE_KEYS: Record<string, string[]> = {
  'Captains.tsx': [
    'Usage',
    'Highest applicable account limit window for this captain, with its name and freshness. 75% and above is a warning, 90% and above is near the limit. A dash state never means zero usage.',
    'Usage shows the first account pages for these captains. Open a captain\'s usage section for every account.',
    'View Usage',
    'Open Activity > Usage Overview to view usage over time.',
  ],
  'Activity.tsx': ['Usage Overview', 'Account Limits'],
};
const pageSources = import.meta.glob(['../pages/Captains.tsx', '../pages/Activity.tsx'], { query: '?raw', import: 'default', eager: true }) as Record<string, string>;

const S = "'((?:[^'\\\\]|\\\\.)*)'";
const CALL = new RegExp(`\\bt\\(\\s*${S}`, 'g');
const FIELD = new RegExp(`(?:\\b(?:label|hint|title|description|tooltip):\\s*|ariaLabel=\\{?\\s*)${S}`, 'g');
const ARIA_DQ = /ariaLabel="([^"]*)"/g;

/** Strings looked up through tables (invisible to the t('...') scan) plus ActionMenu labels translated by the menu. */
const TABLE_KEYS = [
  ...Object.values(USAGE_KIND_TEXT),
  ...Object.values(USAGE_KIND_HINT),
  ...Object.values(USAGE_LEVEL_TEXT),
  ...USAGE_WINDOW_LABELS,
  ...Object.values(USAGE_CAPTURE_FORM_ERROR_TEXT),
  'View Usage',
];

function usageKeys(): string[] {
  const keys = new Set<string>(TABLE_KEYS);
  for (const [path, source] of Object.entries(sources)) {
    const usageFile = path.includes('/components/usage/');
    for (const m of source.matchAll(CALL)) keys.add(m[1].replace(/\\'/g, "'"));
    if (usageFile) for (const m of source.matchAll(FIELD)) keys.add(m[1].replace(/\\'/g, "'"));
    if (usageFile) for (const m of source.matchAll(ARIA_DQ)) keys.add(m[1]);
  }
  for (const list of Object.values(PAGE_KEYS)) for (const key of list) keys.add(key);
  return [...keys].filter((k) => k.trim()).sort();
}

function placeholders(text: string): string {
  return (text.match(/\{\{[\w.]+\}\}/g) ?? []).sort().join(',');
}

describe('Usage i18n catalog', () => {
  const catalog = JSON.parse(catalogSource) as I18nCatalog;
  const keys = usageKeys();

  it('finds the usage strings, including the View Usage action and state hints', () => {
    expect(Object.keys(sources).some((p) => p.endsWith('CaptainUsageCard.tsx'))).toBe(true);
    expect(keys).toContain('View Usage');
    expect(keys).toContain('Sign in to the runtime on host {{host}}. Armada never asks you to paste a credential here.');
    expect(keys).toContain('Enable account usage capture on this server');
  });

  it('lists only strings the pages really use', () => {
    for (const [file, list] of Object.entries(PAGE_KEYS)) {
      const source = Object.entries(pageSources).find(([path]) => path.endsWith(file))?.[1] ?? '';
      for (const key of list) expect(source.includes(key.replace(/'/g, "\\'")) || source.includes(key), `${file}: ${key}`).toBe(true);
    }
  });

  it('has a translation for every usage string in every non-English locale', () => {
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

  it('translates View Usage in every shipped locale (not left in English)', () => {
    for (const meta of catalog.supportedLocales) {
      if (meta.code === catalog.defaultLocale) continue;
      const value = catalog.locales[meta.code]?.phrases?.['View Usage'] ?? catalog.locales[meta.code]?.terms?.['View Usage'];
      expect(value, meta.code).toBeTruthy();
      expect(value, meta.code).not.toBe('View Usage');
    }
  });
});
