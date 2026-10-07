import { describe, expect, it } from 'vitest';
import catalogSource from '../../../Armada.Server/wwwroot/i18n/armada.json?raw';
import { getLandingModes, getVoyageLandingModes } from './vesselForm';
import { SETUP_LANDING_MODES, SETUP_LANDING_MODE_HINTS, setupLandingWorkingDirectoryError } from './setupLanding';
import type { I18nCatalog } from '../i18n/runtime';

/** Every landing mode string the dashboard and the TUI show (both read the same catalog). */
function landingKeys(): string[] {
  const keys = new Set<string>();
  for (const m of [...getLandingModes((text) => text), ...getVoyageLandingModes((text) => text)]) {
    keys.add(m.label);
    keys.add(m.short);
    keys.add(m.description);
  }
  for (const m of SETUP_LANDING_MODES) keys.add(m.label);
  for (const hint of Object.values(SETUP_LANDING_MODE_HINTS)) keys.add(hint);
  keys.add(setupLandingWorkingDirectoryError('LocalMerge', '') ?? '');
  keys.add(setupLandingWorkingDirectoryError('MergeAndPush', '') ?? '');
  keys.add("Controls how completed mission work is landed. None keeps the work on a branch for you to review; Local Merge merges it into the working directory without pushing; Merge and Push also pushes it to that checkout's origin remote.");
  keys.add('How completed mission work is integrated (LocalMerge, MergeAndPush, PullRequest, MergeQueue, None)');
  keys.add('Default Landing Mode');
  keys.add('How finished missions land when neither the vessel nor the voyage sets a landing mode (default Merge and Push).');
  keys.add("How the voyage's missions land; Default uses the vessel's mode, then the global setting");
  keys.add('Settings that control captain monitoring, stalling, cleanup, and how finished missions land.');
  keys.add('Auto-Merge PRs');
  return [...keys].sort();
}

/** Legacy push and pull request flag labels that no screen shows any more. */
const REMOVED_KEYS = [
  'Auto-Create Pull Requests',
  'Auto-Push',
  'Auto-Create PRs',
  'Auto Push',
  'Auto Create PRs',
  'Automatically open pull requests when supported by the mission and vessel configuration.',
  'Settings that control captain monitoring, stalling, cleanup, and pull-request automation.',
  'How completed mission work is integrated (LocalMerge, PullRequest, MergeQueue, None)',
];

describe('Landing mode i18n catalog', () => {
  const catalog = JSON.parse(catalogSource) as I18nCatalog;
  const keys = landingKeys();

  it('collects every landing mode string', () => {
    expect(keys).toContain('Merge and Push -- local merge, then push to the remote');
    expect(keys).toContain('Default (use vessel or global setting)');
    expect(keys).toContain('None -- manual integration');
    expect(keys).toContain('Finished work is queued; processing the merge queue tests and merges it.');
    expect(keys.length).toBeGreaterThan(40);
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

  it('drops the strings for removed legacy controls', () => {
    const left: string[] = [];
    for (const [code, pack] of Object.entries(catalog.locales)) {
      for (const key of REMOVED_KEYS) if (pack.phrases?.[key] || pack.terms?.[key]) left.push(`${code}: ${key}`);
    }
    expect(left).toEqual([]);
  });
});
