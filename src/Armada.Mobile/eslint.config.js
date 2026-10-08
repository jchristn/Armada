// ESLint (flat config): Expo's rules for the app, plus the sharing contract for dashboard modules.
const { defineConfig } = require('eslint/config');
const expoConfig = require('eslint-config-expo/flat');
const globals = require('globals');
const shared = require('./eslint.shared.js');

module.exports = defineConfig([
  expoConfig,
  {
    ignores: ['dist/*', 'ios/*', 'android/*', '.expo/*', 'coverage/*', 'node_modules/*'],
  },
  {
    files: ['*.js'],
    languageOptions: { globals: { ...globals.node, ...globals.jest } },
  },
  // Maestro runScript files run in Maestro's JavaScript engine: it provides http, json, output, and maestro, and the
  // flow's env values (HOST_SERVER_URL, ...) as globals.
  {
    files: ['e2e/scripts/**/*.js'],
    languageOptions: {
      sourceType: 'script',
      globals: { http: 'readonly', json: 'readonly', output: 'writable', maestro: 'readonly', HOST_SERVER_URL: 'readonly' },
    },
    rules: { 'no-var': 'off' },
  },
  {
    files: ['**/*.{ts,tsx}'],
    rules: {
      '@typescript-eslint/no-explicit-any': 'error',
      'no-console': ['error', { allow: ['warn', 'error'] }],
      'import/no-unresolved': 'off',
    },
  },
  // Tests capture hook values in module-level probes on purpose.
  {
    files: ['src/__tests__/**', 'src/test/**'],
    rules: { 'react-hooks/globals': 'off', '@typescript-eslint/no-require-imports': 'off' },
  },
  // The app never reaches for browser globals either: React Native has no DOM.
  {
    files: ['src/**/*.{ts,tsx}'],
    ignores: ['src/__tests__/**', 'src/test/**'],
    rules: shared.rules,
  },
]);
