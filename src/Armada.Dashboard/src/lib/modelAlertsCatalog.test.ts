import { describe, expect, it } from 'vitest';
import catalogSource from '../../../Armada.Server/wwwroot/i18n/armada.json?raw';
import actionsSource from '../components/ModelAlertActions.tsx?raw';
import type { I18nCatalog } from '../i18n/runtime';

const S = "'((?:[^'\\\\]|\\\\.)*)'";
const CALL = new RegExp(`\\bt\\(\\s*${S}`, 'g');

function modelAlertKeys(): string[] {
  const keys = new Set<string>();
  for (const m of actionsSource.matchAll(CALL)) {
    const key = m[1].replace(/\\'/g, "'");
    if (key.trim()) keys.add(key);
  }
  return [...keys].sort();
}

function placeholders(text: string): string {
  return (text.match(/\{\{[\w.]+\}\}/g) ?? []).sort().join(',');
}

describe('Model alert inbox actions i18n catalog', () => {
  const catalog = JSON.parse(catalogSource) as I18nCatalog;
  const keys = modelAlertKeys();

  it('finds the model alert strings', () => {
    expect(keys).toContain('Suggested actions');
    expect(keys).toContain('Last action: {{result}}');
  });

  it('has a translation for every string in every non-English locale, with placeholders intact', () => {
    const problems: string[] = [];
    for (const meta of catalog.supportedLocales) {
      if (meta.code === catalog.defaultLocale) continue;
      const pack = catalog.locales[meta.code] ?? {};
      for (const key of keys) {
        const value = pack.phrases?.[key] ?? pack.terms?.[key];
        if (!value) problems.push(`${meta.code}: missing ${key}`);
        else if (placeholders(key) !== placeholders(value)) problems.push(`${meta.code}: placeholder ${key}`);
      }
    }
    expect(problems).toEqual([]);
  });
});
