import { describe, expect, it } from 'vitest';
import catalogSource from '../../../Armada.Server/wwwroot/i18n/armada.json?raw';
import browseTreeSource from '../components/vessels/import/BrowseTree.tsx?raw';
import importHistorySource from '../components/vessels/import/ImportHistory.tsx?raw';
import importResultsSource from '../components/vessels/import/ImportResultsStep.tsx?raw';
import importReviewSource from '../components/vessels/import/ImportReviewStep.tsx?raw';
import importWizardSource from '../components/vessels/import/ImportWizard.tsx?raw';
import importCategorizationSource from '../components/vessels/import/ImportCategorizationOptions.tsx?raw';
import fleetRecommendationsSource from '../components/vessels/import/FleetRecommendationsPanel.tsx?raw';
import backgroundActivitySource from '../components/shared/BackgroundActivityIndicator.tsx?raw';
import actionFormSource from '../components/fleetActions/FleetActionFormModal.tsx?raw';
import runsTableSource from '../components/fleetActions/FleetActionRunsTable.tsx?raw';
import actionsTableSource from '../components/fleetActions/FleetActionsTable.tsx?raw';
import runActionSource from '../components/fleetActions/RunActionModal.tsx?raw';
import runProgressSource from '../components/fleetActions/RunProgress.tsx?raw';
import targetDrawerSource from '../components/fleetActions/TargetDetailDrawer.tsx?raw';
import templateHelpSource from '../components/fleetActions/TemplateVariableHelp.tsx?raw';
import vesselPickerSource from '../components/fleetActions/VesselPickerModal.tsx?raw';
import settingsSource from '../components/settings/ImportFleetActionSettings.tsx?raw';
import listEditorSource from '../components/settings/ListEditor.tsx?raw';
import dialogShellSource from '../components/shared/DialogShell.tsx?raw';
import stateBlocksSource from '../components/shared/StateBlocks.tsx?raw';
import fleetActionsPageSource from '../pages/FleetActions.tsx?raw';
import runDetailSource from '../pages/FleetActionRunDetail.tsx?raw';
import fleetActionLabelsSource from './fleetActionLabels.ts?raw';
import vesselImportLabelsSource from './vesselImportLabels.ts?raw';
import dashboardSource from '../pages/Dashboard.tsx?raw';
import vesselsSource from '../pages/Vessels.tsx?raw';
import navModelSource from './navModel.ts?raw';
import type { I18nCatalog } from '../i18n/runtime';

const S = "'((?:[^'\\\\]|\\\\.)*)'";
/** Literals passed straight to the translator. */
const CALL_PATTERNS = [
  new RegExp(`\\bt\\(\\s*${S}`, 'g'),
  new RegExp(`\\bmsg\\(\\s*${S}`, 'g'),
];
/** `label: '...'` entries in option, tab, and menu arrays that are rendered through `t`. */
const LABEL_PATTERN = new RegExp(`\\blabel:\\s*${S}`, 'g');
/** Label-map files: every label, description, and `Code: 'English text'` record value goes through `t`. */
const META_PATTERN = new RegExp(`\\b(?:label|description):\\s*${S}`, 'g');
const RECORD_PATTERN = new RegExp(`^\\s*[A-Za-z]+:\\s*${S},?\\s*$`, 'gm');
/** Validation messages (`errors.field = '...'`) that callers translate when rendering. */
const ERROR_LINE = /errors\.\w+ = (.*)$/gm;
const NOT_KEYS = new Set(['unknown-variables', 'Command']);

/** Files whose every translatable literal belongs to the vessel import or fleet action features. */
const FEATURE_SOURCES = [
  browseTreeSource, importHistorySource, importResultsSource, importReviewSource, importWizardSource,
  importCategorizationSource, fleetRecommendationsSource, backgroundActivitySource,
  actionFormSource, runsTableSource, actionsTableSource, runActionSource, runProgressSource, targetDrawerSource,
  templateHelpSource, vesselPickerSource, settingsSource, listEditorSource, dialogShellSource, stateBlocksSource,
  fleetActionsPageSource, runDetailSource,
];
const LABEL_SOURCES = [fleetActionLabelsSource, vesselImportLabelsSource];

/** Strings these features added to shared pages (the pages also hold many unrelated strings). */
const SHARED_PAGE_KEYS: Array<{ source: string; keys: string[] }> = [
  {
    source: dashboardSource,
    keys: [
      'Active fleet action runs',
      'Click to view active fleet action runs',
      'Discover local git repositories and onboard them as vessels in bulk.',
      'Import repositories',
      'Run a command or a captain mission across many vessels at once.',
      'Run fleet action',
      '{{count}} pending',
      '{{count}} running',
    ],
  },
  {
    source: vesselsSource,
    keys: [
      'Add a single repository with + Vessel, or import many existing local repositories at once.',
      'Bulk actions',
      'Clear selection',
      'Delete Selected',
      'Discover and onboard many local repositories at once',
      'Import repositories',
      'No vessels configured.',
      'Run a fleet action on the selected vessels',
      'Run action...',
      '{count, plural, one {# vessel selected} other {# vessels selected}}',
    ],
  },
  {
    source: navModelSource,
    keys: ['Fleet Actions', 'Run a command or mission across many vessels and watch each run'],
  },
];

function unescape(value: string): string {
  return value.replace(/\\'/g, "'");
}

function isKey(value: string): boolean {
  return value.trim() !== '' && !value.startsWith('/') && !NOT_KEYS.has(value);
}

function extractKeys(): string[] {
  const keys = new Set<string>();
  const add = (value: string | undefined) => {
    if (value === undefined) return;
    const key = unescape(value);
    if (isKey(key)) keys.add(key);
  };
  for (const src of [...FEATURE_SOURCES, ...LABEL_SOURCES]) {
    for (const re of CALL_PATTERNS) for (const m of src.matchAll(re)) add(m[1]);
    for (const m of src.matchAll(LABEL_PATTERN)) add(m[1]);
    for (const line of src.matchAll(ERROR_LINE)) {
      for (const m of line[1].matchAll(new RegExp(S, 'g'))) add(m[1]);
    }
  }
  for (const src of LABEL_SOURCES) {
    for (const m of src.matchAll(META_PATTERN)) add(m[1]);
    for (const m of src.matchAll(RECORD_PATTERN)) add(m[1]);
  }
  for (const page of SHARED_PAGE_KEYS) for (const key of page.keys) keys.add(key);
  return [...keys];
}

function placeholders(text: string): string {
  return (text.match(/\{\{[\w.]+\}\}/g) ?? []).sort().join(',');
}

/** Plural selectors and `#` count for each ICU block, so a translation cannot drop a branch. */
function icuShape(text: string): string {
  const selectors = (text.match(/(?:=\d+|\bzero|\bone|\btwo|\bfew|\bmany|\bother)\s*\{/g) ?? []).map((s) => s.replace(/\s+/g, ''));
  const heads = text.match(/\{\s*\w+\s*,\s*plural\s*,/g) ?? [];
  return `${heads.length}|${selectors.join(',')}`;
}

describe('vessel import and fleet action i18n catalog', () => {
  const catalog = JSON.parse(catalogSource) as I18nCatalog;
  const keys = extractKeys();

  it('finds the vessel import and fleet action strings', () => {
    expect(keys.length).toBeGreaterThan(350);
    expect(keys).toContain('Re-run failed targets');
    expect(keys).toContain('Discovery through a Harbor is not supported yet.');
    expect(keys).toContain('Timeout must be a whole number of seconds from 5 to 7200.');
    expect(keys).toContain('{count, plural, one {Run on # vessel} other {Run on # vessels}}');
  });

  it('still uses every listed shared-page string', () => {
    const stale: string[] = [];
    for (const page of SHARED_PAGE_KEYS) {
      for (const key of page.keys) if (!page.source.includes(`'${key}'`)) stale.push(key);
    }
    expect(stale).toEqual([]);
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

  it('keeps placeholders and plural structure intact in translations', () => {
    const bad: string[] = [];
    for (const [code, pack] of Object.entries(catalog.locales)) {
      for (const key of keys) {
        const value = pack.phrases?.[key];
        if (!value) continue;
        if (placeholders(key) !== placeholders(value)) bad.push(`${code}: placeholders: ${key}`);
        if (key.includes('plural') && icuShape(key) !== icuShape(value)) bad.push(`${code}: plural: ${key}`);
      }
    }
    expect(bad).toEqual([]);
  });
});
