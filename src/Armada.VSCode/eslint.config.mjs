// ESLint for the extension's own code (src/ and test/). The dashboard modules it shares are linted against the
// sharing contract by eslint.dashboard-shared.config.mjs (npm run lint:shared).
import { createRequire } from 'node:module';
import tseslint from 'typescript-eslint';

const require = createRequire(import.meta.url);
// The host runs on Node, not in a browser: the same browser-global ban the mobile app applies to shared code.
const shared = require('../Armada.Mobile/eslint.shared.js');

export default tseslint.config(
  { ignores: ['dist/**', 'out/**', 'node_modules/**', '.vscode-test/**', '*.vsix'] },
  {
    files: ['src/**/*.ts', 'test/**/*.ts'],
    extends: [...tseslint.configs.recommended],
    rules: {
      '@typescript-eslint/no-unused-vars': ['error', { argsIgnorePattern: '^_', varsIgnorePattern: '^_' }],
    },
  },
  {
    files: ['src/**/*.ts'],
    rules: {
      'no-restricted-globals': shared.rules['no-restricted-globals'],
    },
  },
);
