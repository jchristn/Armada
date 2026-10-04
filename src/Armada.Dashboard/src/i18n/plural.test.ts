import { describe, expect, it } from 'vitest';
import { formatIcuPlurals, translateTemplate, type I18nCatalog } from './runtime';

describe('ICU plural formatting', () => {
  const msg = '{count, plural, =0 {Import repositories} one {Import # repository} other {Import # repositories}}';

  it('selects one/other/exact branches and formats the number', () => {
    expect(translateTemplate('en', msg, null, { count: 0 })).toBe('Import repositories');
    expect(translateTemplate('en', msg, null, { count: 1 })).toBe('Import 1 repository');
    expect(translateTemplate('en', msg, null, { count: 1234 })).toBe('Import 1,234 repositories');
  });

  it('leaves text outside the plural block and {{name}} placeholders intact', () => {
    const text = 'Run {{name}}: {count, plural, one {# vessel} other {# vessels}} queued';
    expect(translateTemplate('en', text, null, { name: 'Build', count: 3 })).toBe('Run Build: 3 vessels queued');
  });

  it('uses the locale plural rules and the catalog translation', () => {
    const catalog: I18nCatalog = {
      defaultLocale: 'en',
      supportedLocales: [],
      locales: { de: { phrases: { [msg]: '{count, plural, =0 {Repositorys importieren} one {# Repository importieren} other {# Repositorys importieren}}' } } },
    };
    expect(translateTemplate('de', msg, catalog, { count: 2500 })).toBe('2.500 Repositorys importieren');
    expect(translateTemplate('de', msg, catalog, { count: 1 })).toBe('1 Repository importieren');
  });

  it('returns malformed messages unchanged', () => {
    expect(formatIcuPlurals('en', '{count, plural, one {x}', { count: 1 })).toBe('{count, plural, one {x}');
    expect(formatIcuPlurals('en', 'no plural here', { count: 1 })).toBe('no plural here');
  });
});
