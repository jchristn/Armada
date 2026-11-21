// Bundles the extension into dist/extension.js (`vscode` is provided by the host at runtime). The dashboard's shared
// modules (api/, types/, lib/) are imported through the @dashboard alias, the same one Mobile uses, and bundled in.
//
//   node esbuild.mjs [--production] [--watch]   the extension
//   node esbuild.mjs --tests                    unit and integration tests into out/test (run by npm test)
import * as fs from 'node:fs';
import * as path from 'node:path';
import { fileURLToPath } from 'node:url';
import * as esbuild from 'esbuild';

const root = path.dirname(fileURLToPath(import.meta.url));
const production = process.argv.includes('--production');
const watch = process.argv.includes('--watch');
const tests = process.argv.includes('--tests');

/** The alias shared with Mobile (src/Armada.Mobile/tsconfig.json) and this project's tsconfig.json paths. */
const alias = {
  '@dashboard': path.resolve(root, '..', 'Armada.Dashboard', 'src'),
  // Shared dashboard modules that also export a React hook import react; the extension never calls the hooks.
  react: path.resolve(root, 'src', 'shims', 'react.ts'),
};

const common = {
  bundle: true,
  format: 'cjs',
  platform: 'node',
  target: 'node22',
  alias,
  logLevel: 'info',
};

/**
 * Test bundles keep `vscode` (and the fake that stands in for it, test/fakes/vscode) external, so the code under test
 * and the test itself share one fake instance; test/setup.ts resolves `vscode` to out/test/fakes/vscode.js.
 */
const fakeVscodeExternal = {
  name: 'fake-vscode-external',
  setup(build) {
    build.onResolve({ filter: /(^vscode$)|([\\/]fakes[\\/]vscode$)/ }, () => ({ path: 'vscode', external: true }));
  },
};

function listTests(dir) {
  return fs.readdirSync(dir).filter((f) => f.endsWith('.test.ts')).map((f) => path.join(dir, f));
}

if (tests) {
  await esbuild.build({
    ...common,
    entryPoints: [
      ...listTests(path.join(root, 'test', 'unit')),
      path.join(root, 'test', 'setup.ts'),
      path.join(root, 'test', 'integration', 'suite', 'index.ts'),
    ],
    outdir: path.join(root, 'out', 'test'),
    outbase: path.join(root, 'test'),
    sourcemap: true,
    logLevel: 'warning',
    plugins: [fakeVscodeExternal],
  });
  // The fake itself, and the integration launcher (which loads @vscode/test-electron from node_modules).
  await esbuild.build({ ...common, entryPoints: [path.join(root, 'test', 'fakes', 'vscode.ts')], outfile: path.join(root, 'out', 'test', 'fakes', 'vscode.js'), logLevel: 'warning' });
  await esbuild.build({ ...common, entryPoints: [path.join(root, 'test', 'integration', 'runTest.ts')], outfile: path.join(root, 'out', 'test', 'integration', 'runTest.js'), packages: 'external', logLevel: 'warning' });
} else {
  const options = {
    ...common,
    entryPoints: [path.join(root, 'src', 'extension.ts')],
    outfile: path.join(root, 'dist', 'extension.js'),
    external: ['vscode'],
    sourcemap: !production,
    minify: production,
  };
  const dashboardRoot = path.resolve(root, '..', 'Armada.Dashboard');
  const dashboardPackage = JSON.parse(fs.readFileSync(path.join(dashboardRoot, 'package.json'), 'utf8'));
  if (!fs.existsSync(path.join(dashboardRoot, 'node_modules', 'react'))) {
    throw new Error('The dashboard page tabs bundle the dashboard: run `npm ci` in src/Armada.Dashboard first.');
  }
  // Dashboard page tabs: the dashboard's own pages in a webview (webview/dashboard), with React and the other
  // dashboard dependencies taken from the dashboard's node_modules (never the extension's react shim).
  const webview = {
    bundle: true,
    format: 'esm',
    platform: 'browser',
    target: 'es2022',
    entryPoints: { dashboard: path.join(root, 'webview', 'dashboard', 'main.tsx') },
    outdir: path.join(root, 'dist', 'webview'),
    tsconfig: path.join(root, 'webview', 'tsconfig.json'),
    alias: { '@dashboard': path.join(dashboardRoot, 'src') },
    nodePaths: [path.join(dashboardRoot, 'node_modules')],
    jsx: 'automatic',
    loader: { '.png': 'dataurl', '.svg': 'dataurl', '.woff2': 'dataurl', '.woff': 'dataurl' },
    define: {
      __APP_VERSION__: JSON.stringify(dashboardPackage.version),
      'import.meta.env': '{}',
      'process.env.NODE_ENV': JSON.stringify(production ? 'production' : 'development'),
    },
    sourcemap: !production,
    minify: production,
    logLevel: 'info',
  };
  if (watch) {
    const context = await esbuild.context(options);
    await context.watch();
    const webviewContext = await esbuild.context(webview);
    await webviewContext.watch();
  } else {
    await Promise.all([esbuild.build(options), esbuild.build(webview)]);
  }
}
