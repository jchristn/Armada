import { describe, expect, it } from 'vitest';
import { translateSubtree, translateText, type I18nCatalog } from './runtime';

const catalog: I18nCatalog = {
  defaultLocale: 'en',
  supportedLocales: [],
  locales: {
    de: {
      phrases: {
        'Display name for this repository inside Armada.': 'Anzeigename des Repositorys.',
        'Display name for the AI agent runtime registered with Armada.': 'Anzeigename des Agenten.',
        'Register Vessel': 'Schiff registrieren',
        'Create Captain': 'Kapitaen erstellen',
      },
    },
  },
};

describe('document translator follows attributes React changes on a reused element', () => {
  it('adopts a new aria-label and title instead of restoring the first-seen text (English)', () => {
    const root = document.createElement('div');
    const input = document.createElement('input');
    input.setAttribute('aria-label', 'Display name for this repository inside Armada.');
    input.setAttribute('title', 'Display name for this repository inside Armada.');
    root.appendChild(input);
    translateSubtree(root, 'en', catalog);

    // The setup wizard reuses this input for the next step's field.
    input.setAttribute('aria-label', 'Display name for the AI agent runtime registered with Armada.');
    input.setAttribute('title', 'Display name for the AI agent runtime registered with Armada.');
    translateSubtree(root, 'en', catalog);

    expect(input.getAttribute('aria-label')).toBe('Display name for the AI agent runtime registered with Armada.');
    expect(input.getAttribute('title')).toBe('Display name for the AI agent runtime registered with Armada.');
  });

  it('translates the new attribute value in a non-English locale and keeps translating it on later passes', () => {
    const root = document.createElement('div');
    const input = document.createElement('input');
    input.setAttribute('aria-label', 'Display name for this repository inside Armada.');
    root.appendChild(input);
    translateSubtree(root, 'de', catalog);
    expect(input.getAttribute('aria-label')).toBe('Anzeigename des Repositorys.');

    input.setAttribute('aria-label', 'Display name for the AI agent runtime registered with Armada.');
    translateSubtree(root, 'de', catalog);
    expect(input.getAttribute('aria-label')).toBe('Anzeigename des Agenten.');

    translateSubtree(root, 'de', catalog);
    expect(input.getAttribute('aria-label')).toBe('Anzeigename des Agenten.');

    // Switching back to English restores the current source text, not the first-seen one.
    translateSubtree(root, 'en', catalog);
    expect(input.getAttribute('aria-label')).toBe('Display name for the AI agent runtime registered with Armada.');
  });

  it('adopts a new button value', () => {
    const root = document.createElement('div');
    const button = document.createElement('input');
    button.type = 'submit';
    button.value = 'Register Vessel';
    root.appendChild(button);
    translateSubtree(root, 'de', catalog);
    expect(button.value).toBe('Schiff registrieren');

    button.value = 'Create Captain';
    translateSubtree(root, 'de', catalog);
    expect(button.value).toBe('Kapitaen erstellen');
  });
});

describe('rendered-text patterns never recurse on their own templates', () => {
  // A locale whose catalog has none of the message templates (as the de, fr, it, ja, zh-Hant, and yue-Hant packs
  // lacked "Copy {{title}} HTTP config to clipboard", which crashed the Server settings page).
  const sparse: I18nCatalog = { defaultLocale: 'en', supportedLocales: [], locales: { de: { terms: { Delete: 'Loeschen' } } } };
  const messages = [
    'Copy Claude Code HTTP config to clipboard',
    'Copy Codex STDIO config to clipboard',
    'Delete failed: database is locked',
    'Save failed: bad value',
    'Failed to save settings: bad value',
    'Unable to load existing Armada resources: offline',
    'Vessel creation failed: name taken',
    'Dispatch failed: no captains',
    'Mission status refreshed: Complete.',
    'Stop captain "alpha"? This will halt the current mission.',
    'Recall captain "alpha"? The captain will finish current work and return to idle.',
    'Remove captain "alpha"? This cannot be undone.',
    'Delete dock dck_1? This will clean up the git worktree and cannot be undone.',
    'Signing in as admin@armada to Default Tenant',
    'Created vessel "solo-app".',
    'Using fleet "Starter".',
  ];

  for (const message of messages) {
    it(`translates "${message}" without a catalog template`, () => {
      expect(() => translateText('de', message, sparse)).not.toThrow();
      expect(translateText('de', message, sparse)).toContain(message.includes('"') ? message.split('"')[1] : message.split(' ').pop()!.replace(/\.$/, ''));
    });
  }

  it('still uses the catalog template when the locale has one', () => {
    const full: I18nCatalog = { defaultLocale: 'en', supportedLocales: [], locales: { de: { phrases: { 'Copy {{title}} HTTP config to clipboard': '{{title}} HTTP-Konfiguration kopieren' } } } };
    expect(translateText('de', 'Copy Claude Code HTTP config to clipboard', full)).toBe('Claude Code HTTP-Konfiguration kopieren');
  });
});
