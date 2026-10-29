import { describe, expect, it } from 'vitest';
import catalogSource from '../../../Armada.Server/wwwroot/i18n/armada.json?raw';
import panel from '../components/blackbox/BlackBoxPanel.tsx?raw';
import actionDialog from '../components/blackbox/BlackBoxActionDialog.tsx?raw';
import evidence from '../components/blackbox/BlackBoxEvidence.tsx?raw';
import notes from '../components/blackbox/BlackBoxNotes.tsx?raw';
import resolveDialog from '../components/blackbox/BlackBoxResolveDialog.tsx?raw';
import entriesTab from '../components/blackbox/BlackBoxEntriesTab.tsx?raw';
import fingerprintsTab from '../components/blackbox/BlackBoxFingerprintsTab.tsx?raw';
import hotspotsTab from '../components/blackbox/BlackBoxHotspotsTab.tsx?raw';
import retentionTab from '../components/blackbox/BlackBoxRetentionTab.tsx?raw';
import settings from '../components/settings/BlackBoxSettings.tsx?raw';
import blackBoxPage from '../pages/BlackBox.tsx?raw';
import entryPage from '../pages/BlackBoxEntry.tsx?raw';
import blackBoxLib from './blackbox.ts?raw';
import type { I18nCatalog } from '../i18n/runtime';

const S = "'((?:[^'\\\\]|\\\\.)*)'";
const CALL = new RegExp(`\\bt\\(\\s*${S}`, 'g');
const FIELD = new RegExp(`\\b(?:title|hint|confirmLabel|label|tooltip|help):\\s*${S}`, 'g');
const PROP = /\b(?:title|confirmLabel|ariaLabel|refreshTitle)="([^"]+)"/g;

const SOURCES = [panel, actionDialog, evidence, notes, resolveDialog, entriesTab, fingerprintsTab, hotspotsTab, retentionTab, settings, blackBoxPage, entryPage, blackBoxLib];

/** Strings passed to t() as data: the nav item, the retention stat labels and the settings field labels. */
const SHARED_KEYS = [
  'Black Box', 'Failure records for missions and voyages: where, how and why each failed, and how to recover',
  'Entries', 'Pinned', 'Expired, awaiting GC', 'Being deleted', 'Stored bytes', 'Black box',
  'Open', 'Resolved', 'Duplicate', 'WontFix', 'ArmadaFixed', 'Captured', 'Enriched', 'Analyzed', 'EnrichmentFailed', 'ObjectMissing',
  'Inline', 'Sweeper', 'Backfill', 'Database', 'Disk', 'S3', 'AzureBlob', 'GoogleCloud', 'Cifs', 'Nfs',
  'Access key', 'Secret key', 'Connection string', 'Credentials file path', 'Password', 'Hostname', 'Bucket', 'Port',
];

function blackBoxKeys(): string[] {
  const keys = new Set<string>(SHARED_KEYS);
  for (const source of SOURCES) {
    for (const re of [CALL, FIELD]) {
      for (const m of source.matchAll(re)) {
        const key = m[1].replace(/\\'/g, "'");
        if (key.trim()) keys.add(key);
      }
    }
    for (const m of source.matchAll(PROP)) keys.add(m[1]);
  }
  return [...keys].sort();
}

function placeholders(text: string): string {
  return (text.match(/\{\{[\w.]+\}\}/g) ?? []).sort().join(',');
}

describe('Black box i18n catalog', () => {
  const catalog = JSON.parse(catalogSource) as I18nCatalog;
  const keys = blackBoxKeys();

  it('finds the black box strings', () => {
    expect(keys).toContain('Copy FIX THIS');
    expect(keys).toContain('What would have prevented it');
    expect(keys).toContain('I understand that this can delete work that cannot be recovered.');
    expect(keys).toContain('Save Black Box Settings');
    expect(keys.length).toBeGreaterThan(300);
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

  it('keeps placeholders intact and uses no em-dashes', () => {
    const bad: string[] = [];
    for (const [code, pack] of Object.entries(catalog.locales)) {
      for (const key of keys) {
        const value = pack.phrases?.[key] ?? pack.terms?.[key];
        if (value && (placeholders(key) !== placeholders(value) || value.includes('—'))) bad.push(`${code}: ${key}`);
      }
    }
    expect(bad).toEqual([]);
  });
});
