/**
 * The sharing contract (MOBILE_APP_PLAN.md, "Code sharing"): every module the app shares with the dashboard loads
 * under React Native (no window, document, localStorage, import.meta, or react-dom at import time), and the list of
 * dashboard-only exceptions stays honest. ESLint (`npm run lint:shared`) checks the same files statically.
 */
import fs from 'fs';
import path from 'path';

const sharing = require('../../sharing.config') as {
  sharedRoots: { dashboardSrc: string };
  sharedDirs: string[];
  sharedFiles: string[];
  dashboardOnly: string[];
};

const root = sharing.sharedRoots.dashboardSrc;

function walk(dir: string): string[] {
  return fs.readdirSync(dir, { withFileTypes: true }).flatMap((entry) => {
    const full = path.join(dir, entry.name);
    return entry.isDirectory() ? walk(full) : [full];
  });
}

function sharedModules(): string[] {
  const files = sharing.sharedDirs.flatMap((d) => walk(path.join(root, d)))
    .concat(sharing.sharedFiles.map((f) => path.join(root, f)))
    .map((f) => path.relative(root, f).split(path.sep).join('/'))
    .filter((f) => /\.tsx?$/.test(f) && !/\.test\.tsx?$/.test(f) && !f.endsWith('.d.ts'));
  return files.filter((f) => !sharing.dashboardOnly.includes(f)).sort();
}

const BROWSER_ONLY = /\b(window|document|localStorage|sessionStorage|navigator)\.|import\.meta|from 'react-dom/;

describe('modules shared with the dashboard', () => {
  const modules = sharedModules();

  it('covers the client, the models, the i18n catalog, and the lib folder', () => {
    expect(modules).toEqual(expect.arrayContaining(['api/client.ts', 'types/models.ts', 'i18n/catalog.ts', 'lib/armadaSocket.ts', 'lib/navModel.ts']));
    expect(modules.length).toBeGreaterThan(40);
  });

  it.each(sharedModules())('%s loads under React Native', (file) => {
    let loaded: unknown;
    jest.isolateModules(() => {
      loaded = require(path.join(root, file));
    });
    expect(loaded).toBeTruthy();
  });

  it.each(sharedModules())('%s uses no browser globals', (file) => {
    const code = fs.readFileSync(path.join(root, file), 'utf8')
      .replace(/\/\*[\s\S]*?\*\//g, '')
      .replace(/(^|[^:])\/\/.*$/gm, '$1');
    expect(code).not.toMatch(BROWSER_ONLY);
  });

  it('every dashboard-only exception exists and really is browser-bound or tied to a dashboard context', () => {
    for (const file of sharing.dashboardOnly) {
      const full = path.join(root, file);
      expect(fs.existsSync(full)).toBe(true);
      const code = fs.readFileSync(full, 'utf8');
      expect(code).toMatch(/\b(window|document|localStorage|navigator)\b|\.\.\/context\/|from '\.\/dialogA11y'/);
    }
  });
});
