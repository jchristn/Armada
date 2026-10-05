import { describe, expect, it } from 'vitest';
import { translateSubtree, translateTemplate, translateText, type I18nCatalog } from './runtime';

const catalog: I18nCatalog = {
  defaultLocale: 'en',
  supportedLocales: [],
  locales: {
    de: {
      terms: { Fix: 'Beheben', Vessel: 'Schiff', Status: 'Status-DE', Failed: 'Fehlgeschlagen', login: 'Anmeldung', Back: 'Zurueck' },
      phrases: { 'Created {{entity}} "{{name}}".': '{{entity}} "{{name}}" erstellt.', 'New Fleet': 'Neue Flotte' },
    },
  },
};

describe('i18n does not rewrite user-provided data', () => {
  it('passes a vessel named "Fix: login" through unchanged', () => {
    expect(translateText('de', 'Fix: login', catalog)).toBe('Fix: login');
  });

  it('translates only the fixed label of a "Label: name" title, never the name', () => {
    expect(translateText('de', 'Vessel: Fix: login', catalog)).toBe('Schiff: Fix: login');
  });

  it('keyed messages insert parameters verbatim', () => {
    expect(translateTemplate('de', 'Created {{entity}} "{{name}}".', catalog, { entity: 'Schiff', name: 'Fix: login' }))
      .toBe('Schiff "Fix: login" erstellt.');
    expect(translateTemplate('en', 'Created {{entity}} "{{name}}".', catalog, { entity: 'vessel', name: 'Fix: login' }))
      .toBe('Created vessel "Fix: login".');
  });

  it('captured groups are not run through nested patterns', () => {
    // "+ <label>" still translates a catalog label, but a captured name with a colon is left alone.
    expect(translateText('de', '+ New Fleet', catalog)).toBe('+ Neue Flotte');
    expect(translateText('de', '+ Fix: login', catalog)).toBe('+ Fix: login');
    expect(translateText('de', 'Fix: login \u2192', catalog)).toBe('Fix: login \u2192');
  });

  it('the document translator leaves a rendered entity name alone', () => {
    const root = document.createElement('div');
    root.innerHTML = '<table><tr><td>Fix: login</td><td>Vessel</td></tr></table><span data-i18n-skip="true">Failed</span>';
    translateSubtree(root, 'de', catalog);
    const cells = root.querySelectorAll('td');
    expect(cells[0].textContent).toBe('Fix: login');
    expect(cells[1].textContent).toBe('Schiff');
    expect(root.querySelector('span')?.textContent).toBe('Failed');
  });
});
