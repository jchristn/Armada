import { describe, expect, it } from 'vitest';
import catalogSource from '../../../Armada.Server/wwwroot/i18n/armada.json?raw';
import { getLandingModes } from './vesselForm';
import { SETUP_LANDING_MODES, SETUP_LANDING_MODE_HINTS, setupLandingWorkingDirectoryError } from './setupLanding';
import type { I18nCatalog } from '../i18n/runtime';

/** The landing mode strings for Local Merge and Merge and Push (the dashboard and the TUI read the same catalog). */
function landingKeys(): string[] {
  const keys = new Set<string>();
  for (const m of getLandingModes((text) => text)) {
    if (m.value === 'LocalMerge' || m.value === 'MergeAndPush') {
      keys.add(m.label);
      keys.add(m.short);
      keys.add(m.description);
    }
  }
  for (const m of SETUP_LANDING_MODES) if (m.value === 'LocalMerge' || m.value === 'MergeAndPush') keys.add(m.label);
  keys.add(SETUP_LANDING_MODE_HINTS.LocalMerge);
  keys.add(SETUP_LANDING_MODE_HINTS.MergeAndPush);
  keys.add(setupLandingWorkingDirectoryError('LocalMerge', '') ?? '');
  keys.add(setupLandingWorkingDirectoryError('MergeAndPush', '') ?? '');
  keys.add("Controls how completed mission work is landed. None keeps the work on a branch for you to review; Local Merge merges it into the working directory without pushing; Merge and Push also pushes it to that checkout's origin remote.");
  keys.add('How completed mission work is integrated (LocalMerge, MergeAndPush, PullRequest, MergeQueue, None)');
  return [...keys].sort();
}

describe('Landing mode i18n catalog', () => {
  const catalog = JSON.parse(catalogSource) as I18nCatalog;
  const keys = landingKeys();

  it('collects the Local Merge and Merge and Push strings', () => {
    expect(keys).toHaveLength(14);
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

  it('drops the column tooltip that omitted MergeAndPush', () => {
    const old = 'How completed mission work is integrated (LocalMerge, PullRequest, MergeQueue, None)';
    for (const [code, pack] of Object.entries(catalog.locales)) {
      expect(pack.phrases?.[old], code).toBeUndefined();
    }
  });
});
