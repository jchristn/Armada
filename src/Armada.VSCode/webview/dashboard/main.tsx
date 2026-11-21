/*
 * A dashboard page in a VS Code tab: the dashboard's own routes, pages, charts and forms (Armada.Dashboard
 * src/AppRoutes.tsx), under a MemoryRouter and the slim EmbeddedShell, with the API client and live events bridged to
 * the extension host (bridge.ts). Built by esbuild.mjs into dist/webview/dashboard.{js,css}.
 */

import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import { BRIDGE_ORIGIN, BRIDGE_TOKEN, type DashboardInit } from '../../src/surfaces/dashboard/protocol';
import { blobToBase64, installBridge, post, savedState } from './bridge';
import '@dashboard/index.css';
import '@dashboard/a11y.css';
import '@dashboard/responsive.css';
import '@dashboard/App.css';
import './embed.css';

const root = document.getElementById('root') as HTMLElement;
const init = JSON.parse(root.dataset.armadaInit ?? '{}') as DashboardInit;

/** The dashboard theme that matches the VS Code theme (body class vscode-light, vscode-dark, vscode-high-contrast). */
function vscodeTheme(): 'dark' | 'light' {
  const classes = document.body.classList;
  return classes.contains('vscode-light') || classes.contains('vscode-high-contrast-light') ? 'light' : 'dark';
}

function applyTheme(): void {
  const theme = vscodeTheme();
  document.documentElement.setAttribute('data-theme', theme);
  try { localStorage.setItem('armada_theme', theme); } catch { /* storage can be unavailable */ }
}

applyTheme();
new MutationObserver(applyTheme).observe(document.body, { attributes: true, attributeFilter: ['class'] });
document.documentElement.setAttribute('data-embed', 'vscode');

installBridge(init.connected);
try {
  // The AuthProvider restores a session from storage; the host swaps this placeholder for the real credential.
  localStorage.setItem('armada_session_token', BRIDGE_TOKEN);
} catch { /* storage can be unavailable */ }

// The dashboard modules read storage and the client configuration as they load, so they are imported after the
// bridge, theme and session are in place.
const [{ default: AppRoutes }, { configureClient }, { configurePageSizeStore }, { installDialogEnhancer }, { AuthProvider }, { LocaleProvider }, { ThemeProvider }, { WebSocketProvider }, { NotificationProvider }, { default: ProtectedRoute }, { EmbeddedLayout }] = await Promise.all([
  import('@dashboard/AppRoutes'),
  import('@dashboard/api/client'),
  import('@dashboard/lib/pageSizePrefs'),
  import('@dashboard/lib/dialogA11y'),
  import('@dashboard/context/AuthContext'),
  import('@dashboard/context/LocaleContext'),
  import('@dashboard/context/ThemeContext'),
  import('@dashboard/context/WebSocketContext'),
  import('@dashboard/context/NotificationContext'),
  import('@dashboard/components/ProtectedRoute'),
  import('./EmbeddedShell'),
]);

configureClient({
  baseUrl: BRIDGE_ORIGIN,
  platform: { saveFile: async (file) => post({ type: 'saveFile', filename: file.filename, base64: await blobToBase64(file.body) }) },
});
configurePageSizeStore({
  get: (key) => { try { return localStorage.getItem(key); } catch { return null; } },
  set: (key, value) => { try { localStorage.setItem(key, value); } catch { /* storage can be unavailable */ } },
  scope: () => 'vscode',
});
installDialogEnhancer();

const restored = savedState<{ path?: string }>();
const initialPath = restored?.path || init.path || '/';
const [initialPathname, initialSearch] = (() => {
  const index = initialPath.indexOf('?');
  return index < 0 ? [initialPath, ''] : [initialPath.slice(0, index), initialPath.slice(index)];
})();
const initialEntry = { pathname: initialPathname, search: initialSearch, state: restored?.path ? null : (init.state ?? null) };

createRoot(root).render(
  <StrictMode>
    <LocaleProvider>
      <ThemeProvider>
        <AuthProvider>
          <WebSocketProvider>
            <NotificationProvider>
              <MemoryRouter initialEntries={[initialEntry]}>
                <AppRoutes layout={<ProtectedRoute><EmbeddedLayout /></ProtectedRoute>} />
              </MemoryRouter>
            </NotificationProvider>
          </WebSocketProvider>
        </AuthProvider>
      </ThemeProvider>
    </LocaleProvider>
  </StrictMode>,
);
post({ type: 'ready' });
