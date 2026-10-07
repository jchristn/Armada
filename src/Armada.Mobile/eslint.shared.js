// The sharing contract (MOBILE_APP_PLAN.md, "Code sharing"): modules shared with the dashboard, and the app's own
// code, must be host-agnostic. Used by eslint.config.js (the app) and eslint.dashboard-shared.config.js (the
// dashboard's shared folders, linted from this project by `npm run lint:shared`).
const BROWSER_GLOBALS = ['window', 'document', 'localStorage', 'sessionStorage', 'navigator', 'location', 'history', 'DOMException', 'HTMLElement', 'MutationObserver', 'requestAnimationFrame'];

module.exports = {
  rules: {
    'no-restricted-globals': ['error', ...BROWSER_GLOBALS.map((name) => ({
      name,
      message: `${name} is a browser global; shared and mobile code must be host-agnostic (see src/Armada.Mobile/sharing.config.js).`,
    }))],
    'no-restricted-syntax': ['error', {
      selector: "MetaProperty[meta.name='import'][property.name='meta']",
      message: 'import.meta is bundler-specific; take configuration through configureClient() or parameters instead.',
    }],
    'no-restricted-imports': ['error', {
      paths: [
        { name: 'react-dom', message: 'Shared modules must not depend on react-dom.' },
        { name: 'react-router-dom', message: 'Shared modules must not depend on the dashboard router.' },
      ],
      patterns: [{ group: ['react-dom/*'], message: 'Shared modules must not depend on react-dom.' }],
    }],
  },
};
