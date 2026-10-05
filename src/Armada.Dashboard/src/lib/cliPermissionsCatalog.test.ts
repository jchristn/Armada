import { describe, expect, it } from 'vitest';
import catalogSource from '../../../Armada.Server/wwwroot/i18n/armada.json?raw';
import libSource from './cliPermissions.ts?raw';
import cardSource from '../components/cliPermissions/CliPermissionCard.tsx?raw';
import countdownSource from '../components/cliPermissions/CliPermissionCountdown.tsx?raw';
import controlsSource from '../components/cliPermissions/CliPermissionDecisionControls.tsx?raw';
import policySelectSource from '../components/cliPermissions/CliPermissionPolicySelect.tsx?raw';
import settingsSource from '../components/settings/CliPermissionSettings.tsx?raw';
import pageSource from '../pages/CliPermissions.tsx?raw';
import type { I18nCatalog } from '../i18n/runtime';

const S = "'((?:[^'\\\\]|\\\\.)*)'";
const CALL = new RegExp(`\\bt\\(\\s*${S}`, 'g');

const SOURCES = [libSource, cardSource, countdownSource, controlsSource, policySelectSource, settingsSource, pageSource];

/**
 * Strings the CLI tool permission feature adds to shared pages (Inbox, captain forms, navigation, tab labels), which
 * those pages' sources mix with older strings, so they are listed here.
 */
const SHARED_PAGE_KEYS = [
  'CLI Tool Permissions',
  'Approve CLI tool requests from captains and manage allow and deny rules',
  'Requests',
  'Rules',
  'Open request',
  'CLI tool permissions',
  'How this captain handles shell commands, file edits, and fetches that need permission. Inherit: missions follow the auto-approve option when it is set, then the server default; Ask conversations use the server default (Settings > CLI Tool Permissions). A conversation can override it.',
  'Only admins can change this.',
  'Inherit (auto-approve option, then server default)',
];

function cliKeys(): string[] {
  const keys = new Set<string>(SHARED_PAGE_KEYS);
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

describe('CLI tool permissions i18n catalog', () => {
  const catalog = JSON.parse(catalogSource) as I18nCatalog;
  const keys = cliKeys();

  it('finds the CLI tool permission strings', () => {
    expect(keys.length).toBeGreaterThan(60);
    expect(keys).toContain('Approve in Armada');
    expect(keys).toContain('Waiting for an admin to decide.');
    expect(keys).toContain('Save CLI Tool Permissions');
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
