import { describe, expect, it } from 'vitest';
import catalogSource from '../../../Armada.Server/wwwroot/i18n/armada.json?raw';
import pageSource from '../pages/VesselHistory.tsx?raw';
import heatmapSource from '../components/vessels/history/CommitHeatmap.tsx?raw';
import timelineSource from '../components/vessels/history/CommitTimeline.tsx?raw';
import passwordSource from '../components/PasswordChangeRequired.tsx?raw';
import type { I18nCatalog } from '../i18n/runtime';

const S = "'((?:[^'\\\\]|\\\\.)*)'";
const CALL = new RegExp(`\\bt\\(\\s*${S}`, 'g');

const SOURCES = [pageSource, heatmapSource, timelineSource, passwordSource];

/**
 * Strings the vessel history feature adds outside its own sources: the Vessels and vessel page action menu labels
 * (translated by ActionMenu), the change kinds the timeline passes to t() as data, and the TUI history screen's
 * labels, hints, columns, and messages (the TUI reads the same catalog).
 */
const SHARED_KEYS = [
  'View History',
  'Dispatch',
  'Added',
  'Deleted',
  'Modified',
  'Renamed',
  'Copied',
  'Vessel History',
  'Commit Details',
  'Copy SHA',
  'Jump to Date...',
  'Latest Commits',
  'Branch...',
  'Previous Year',
  'Next Year',
  'Open Vessel',
  'Show the commits on or before this day (yyyy-MM-dd).',
  'Enter a date as yyyy-MM-dd.',
  'Commits on {{branch}}',
  'on or before {{date}}',
  'newest first',
  '{count, plural, one {# commit loaded} other {# commits loaded}}',
  '{count, plural, one {# commit in the last year} other {# commits in the last year}}',
  '{count, plural, one {# commit} other {# commits}}',
  'from {{from}} to {{to}}',
  'Loading older commits...',
  'No commits on or before {{date}}.',
  'No commits on this branch.',
  'none (root commit)',
  'Merge commit: the file changes are against the first parent.',
  'Showing the first {{shown}} of {{total}} changed files.',
];

/** Keys some locales deliberately leave in English (see the catalog notes in the commit that added them). */
const ALLOWED_UNTRANSLATED: Record<string, string[]> = {
  ja: ['Less', 'More'],
};

function historyKeys(): string[] {
  const keys = new Set<string>(SHARED_KEYS);
  for (const src of SOURCES) {
    for (const m of src.matchAll(CALL)) {
      const key = m[1].replace(/\\'/g, "'");
      if (key.trim()) keys.add(key);
    }
  }
  return [...keys].sort();
}

function placeholders(text: string): string {
  return (text.match(/\{\{[\w.]+\}\}/g) ?? []).sort().join(',');
}

describe('Vessel history i18n catalog', () => {
  const catalog = JSON.parse(catalogSource) as I18nCatalog;
  const keys = historyKeys();

  it('finds the vessel history strings', () => {
    expect(keys.length).toBeGreaterThan(60);
    expect(keys).toContain('Commit activity and history for {{name}}.');
    expect(keys).toContain('{count, plural, =0 {No commits} one {# commit} other {# commits}} on {{date}}');
    expect(keys).toContain('Skip for now');
  });

  it('has a translation for every string in every non-English locale', () => {
    const missing: string[] = [];
    for (const meta of catalog.supportedLocales) {
      if (meta.code === catalog.defaultLocale) continue;
      const pack = catalog.locales[meta.code] ?? {};
      const allowed = ALLOWED_UNTRANSLATED[meta.code] ?? [];
      for (const key of keys) {
        if (allowed.includes(key)) continue;
        if (!pack.phrases?.[key] && !pack.terms?.[key] && !pack.sections?.[key]) missing.push(`${meta.code}: ${key}`);
      }
    }
    expect(missing).toEqual([]);
  });

  it('keeps placeholders and plural blocks intact in translations', () => {
    const bad: string[] = [];
    for (const [code, pack] of Object.entries(catalog.locales)) {
      for (const key of keys) {
        const value = pack.phrases?.[key] ?? pack.terms?.[key];
        if (!value) continue;
        if (placeholders(key) !== placeholders(value)) bad.push(`${code}: ${key}`);
        if (key.includes('{count, plural,') && (!value.includes('{count, plural,') || !value.includes('other {'))) bad.push(`${code}: ${key}`);
      }
    }
    expect(bad).toEqual([]);
  });
});
