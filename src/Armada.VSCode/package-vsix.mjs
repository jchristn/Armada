// Packages the extension into <repository root>/vsix/armada-vscode-<version>.vsix (version from package.json) and
// prints the path. Used by `npm run package`, build-vscode.sh/.bat and the vscode-extension workflow.
import { execFileSync } from 'node:child_process';
import { mkdirSync, readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const extensionDir = path.dirname(fileURLToPath(import.meta.url));
const outputDir = path.resolve(extensionDir, '..', '..', 'vsix');
const { version } = JSON.parse(readFileSync(path.join(extensionDir, 'package.json'), 'utf8'));
const vsix = path.join(outputDir, `armada-vscode-${version}.vsix`);

mkdirSync(outputDir, { recursive: true });
execFileSync(
  process.execPath,
  [path.join(extensionDir, 'node_modules', '@vscode', 'vsce', 'vsce'), 'package', '--no-dependencies', '--out', vsix],
  { cwd: extensionDir, stdio: 'inherit' },
);
console.log(vsix);
