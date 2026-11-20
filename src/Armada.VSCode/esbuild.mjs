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
const alias = { '@dashboard': path.resolve(root, '..', 'Armada.Dashboard', 'src') };

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
  if (watch) {
    const context = await esbuild.context(options);
    await context.watch();
  } else {
    await esbuild.build(options);
  }
}
