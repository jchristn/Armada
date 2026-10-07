// What the mobile app shares with the web dashboard. One list, read by Metro, Jest, ESLint, and the
// shared-module test (src/__tests__/sharedModules.test.ts).
//
// Shared: every non-test module under the dashboard's api/, types/, and lib/ folders, plus i18n/catalog.ts. Shared
// modules must be host-agnostic: no window, document, localStorage, navigator, import.meta, or react-dom.
// Dashboard-only: lib modules that are browser hooks or DOM helpers by nature; the mobile app has its own
// equivalents. Adding a module here needs a reason in the comment next to it.
const path = require('path');

const dashboardSrc = path.resolve(__dirname, '..', 'Armada.Dashboard', 'src');
const i18nDir = path.resolve(__dirname, '..', 'Armada.Server', 'wwwroot', 'i18n');

const sharedDirs = ['api', 'types', 'lib'];
const sharedFiles = ['i18n/catalog.ts'];

const dashboardOnly = [
  'lib/chartImage.ts', // canvas and clipboard export of charts
  'lib/dialogA11y.ts', // DOM focus management for HTML dialogs
  'lib/useFocusTrap.ts', // DOM focus trap (re-exports dialogA11y)
  'lib/useAutoRefresh.ts', // per-table interval persisted in localStorage
  'lib/usePersistedPageSize.ts', // page size persisted in localStorage
  'lib/useTablePrefs.ts', // table preferences persisted in localStorage
  'lib/useInboxCount.ts', // bound to the dashboard WebSocketContext; mobile uses lib/inboxSummary.ts
  'lib/useLiveRefresh.ts', // bound to the dashboard WebSocketContext
];

module.exports = {
  sharedRoots: { dashboardSrc, i18nDir },
  sharedDirs,
  sharedFiles,
  dashboardOnly,
};
