// Lints the dashboard modules shared with non-browser hosts against the sharing contract, for the VS Code extension:
// the same file list (src/Armada.Mobile/sharing.config.js) and rules (src/Armada.Mobile/eslint.shared.js) the mobile
// app checks, plus the extension host's own limits (Node: no React Native or Expo either). A dashboard change that
// breaks a non-browser host therefore fails both `npm run lint:shared` here and in src/Armada.Mobile.
// Run from this folder: npm run lint:shared (ESLint runs from src/, so the base path below is relative to it).
import { createRequire } from 'node:module';
import * as path from 'node:path';

const require = createRequire(import.meta.url);
const tsParser = require('@typescript-eslint/parser');
const tsPlugin = require('@typescript-eslint/eslint-plugin');
const shared = require('../Armada.Mobile/eslint.shared.js');
const { sharedDirs, sharedFiles, dashboardOnly } = require('../Armada.Mobile/sharing.config.js');

const [level, imports] = shared.rules['no-restricted-imports'];
const nodeHostImports = {
  ...imports,
  paths: [
    ...imports.paths,
    { name: 'react-native', message: 'Shared modules must not depend on React Native; the VS Code extension host runs on Node.' },
  ],
  patterns: [
    ...imports.patterns,
    { group: ['expo', 'expo-*', '@expo/*'], message: 'Shared modules must not depend on Expo; the VS Code extension host runs on Node.' },
  ],
};

export default [
  {
    basePath: path.join('Armada.Dashboard', 'src'),
    files: [...sharedDirs.map((d) => `${d}/**/*.ts`), ...sharedFiles],
    ignores: ['**/*.test.ts', '**/*.test.tsx', ...dashboardOnly],
    languageOptions: { parser: tsParser },
    // Registered so the dashboard's own eslint-disable comments for these rules resolve.
    plugins: { '@typescript-eslint': tsPlugin },
    linterOptions: { reportUnusedDisableDirectives: 'off' },
    rules: { ...shared.rules, 'no-restricted-imports': [level, nodeHostImports] },
  },
];
