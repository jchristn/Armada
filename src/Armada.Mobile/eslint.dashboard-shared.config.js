// Lints the dashboard modules the app shares (sharing.config.js) against the sharing contract only: no browser
// globals, no import.meta, no react-dom. Run from this folder: npm run lint:shared
const path = require('path');
const { defineConfig } = require('eslint/config');
const tsParser = require('@typescript-eslint/parser');
const tsPlugin = require('@typescript-eslint/eslint-plugin');
const shared = require('./eslint.shared.js');
const { sharedDirs, sharedFiles, dashboardOnly } = require('./sharing.config');

// Run with src/ as the working directory (npm run lint:shared does): ESLint only lints below its base path.
const dashboard = path.join('Armada.Dashboard', 'src');

module.exports = defineConfig([
  {
    basePath: dashboard,
    files: [...sharedDirs.map((d) => `${d}/**/*.ts`), ...sharedFiles],
    ignores: ['**/*.test.ts', '**/*.test.tsx', ...dashboardOnly],
    languageOptions: { parser: tsParser },
    // Registered so the dashboard's own eslint-disable comments for these rules resolve.
    plugins: { '@typescript-eslint': tsPlugin },
    linterOptions: { reportUnusedDisableDirectives: 'off' },
    rules: shared.rules,
  },
]);
