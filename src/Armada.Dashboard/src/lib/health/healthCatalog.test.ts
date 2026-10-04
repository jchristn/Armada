import { describe, expect, it } from 'vitest';
import catalogSource from '../../../../Armada.Server/wwwroot/i18n/armada.json?raw';
import pageSource from '../../pages/VesselHealth.tsx?raw';
import modalSource from '../../components/vessels/health/VesselHealthDetailModal.tsx?raw';
import badgeSource from '../../components/vessels/health/HealthStatusBadge.tsx?raw';
import checklistSource from '../../components/vessels/health/ChecklistDropdown.tsx?raw';
import kpiSource from '../../components/vessels/health/HealthKpiCards.tsx?raw';
import settingsSource from '../../components/vessels/health/RepositoryHealthSettingsSection.tsx?raw';
import buttonSource from '../../components/vessels/health/VesselHealthButton.tsx?raw';
import textSource from './healthText.ts?raw';
import evaluationSource from './useHealthEvaluation.ts?raw';
import type { I18nCatalog } from '../../i18n/runtime';

const S = "'((?:[^'\\\\]|\\\\.)*)'";
const PATTERNS = [
  new RegExp(`\\bt\\(\\s*${S}`, 'g'),
  new RegExp(`\\bmsg\\(\\s*${S}`, 'g'),
  new RegExp(`plural\\(ctx,\\s*[^,]+,\\s*${S},\\s*${S}`, 'g'),
];

function extractKeys(sources: string[]): string[] {
  const keys = new Set<string>(['Health']);
  for (const src of sources) {
    for (const re of PATTERNS) {
      for (const m of src.matchAll(re)) {
        for (const g of m.slice(1)) if (g !== undefined) keys.add(g.replace(/\\'/g, "'"));
      }
    }
  }
  return [...keys];
}

describe('vessel health i18n catalog', () => {
  const catalog = JSON.parse(catalogSource) as I18nCatalog;
  const keys = extractKeys([pageSource, modalSource, badgeSource, checklistSource, kpiSource, settingsSource, buttonSource, textSource, evaluationSource]);

  it('finds the health strings', () => {
    expect(keys.length).toBeGreaterThan(200);
    expect(keys).toContain('Behind by {{count}} commits.');
  });

  it('has a translation for every health string in every non-English locale', () => {
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
