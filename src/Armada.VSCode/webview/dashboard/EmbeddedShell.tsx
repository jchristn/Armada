/*
 * The slim shell a dashboard page renders inside in a VS Code tab: back, forward and reload, the page's breadcrumbs,
 * live status and Open in Browser on one line, then the page itself, full width. The dashboard's sidebar is left out
 * (the Manage view is the navigation in VS Code).
 */

import { Component, Suspense, useEffect, useState, type ErrorInfo, type ReactNode } from 'react';
import { Link, Outlet, useLocation, useNavigate } from 'react-router-dom';
import { useLocale } from '@dashboard/context/LocaleContext';
import { useNotifications } from '@dashboard/context/NotificationContext';
import { useWebSocket } from '@dashboard/context/WebSocketContext';
import { DashboardBreadcrumbProvider, useDashboardBreadcrumbTrail } from '@dashboard/components/shared/BreadcrumbContext';
import DashboardBreadcrumbs from '@dashboard/components/shared/DashboardBreadcrumbs';
import LoadingIndicator from '@dashboard/components/shared/LoadingIndicator';
import { hostHandledRoute, type HostMessage } from '../../src/surfaces/dashboard/protocol';
import { onHostMessage, post, saveState } from './bridge';

class PageErrorBoundary extends Component<{ children: ReactNode; resetKey: string }, { error: Error | null }> {
  public state: { error: Error | null } = { error: null };

  public static getDerivedStateFromError(error: Error): { error: Error } {
    return { error };
  }

  public componentDidUpdate(previous: { resetKey: string }): void {
    if (previous.resetKey !== this.props.resetKey && this.state.error) this.setState({ error: null });
  }

  public componentDidCatch(error: Error, info: ErrorInfo): void {
    console.error('Armada page error', error, info.componentStack);
  }

  public render(): ReactNode {
    if (!this.state.error) return this.props.children;
    return (
      <div className="page-message">
        <h2>This page hit an error</h2>
        <p>{this.state.error.message}</p>
        <button className="btn" onClick={() => this.setState({ error: null })}>Try again</button>
      </div>
    );
  }
}

/** Scrolls the page or clicks an element (host automation for screenshots and tests). */
function automate(message: Extract<HostMessage, { type: 'automation' }>): void {
  const main = document.getElementById('main-content');
  const candidates = message.selector ? Array.from(document.querySelectorAll<HTMLElement>(message.selector)) : [];
  const target = message.text ? candidates.find((el) => el.textContent?.trim() === message.text) : candidates[0];
  if (message.action === 'scroll') {
    if (target) target.scrollIntoView({ block: 'start' });
    else if (main && typeof message.y === 'number') main.scrollTop = message.y;
  } else if (message.action === 'click') {
    target?.click();
  } else if (message.action === 'select' && message.value !== undefined) {
    const value = message.value;
    const select = Array.from(document.querySelectorAll<HTMLSelectElement>(message.selector ?? 'select'))
      .find((el) => Array.from(el.options).some((option) => option.value === value));
    if (!select) return;
    // React tracks the value itself: set it through the native setter, then announce the change.
    Object.getOwnPropertyDescriptor(HTMLSelectElement.prototype, 'value')?.set?.call(select, value);
    select.dispatchEvent(new Event('change', { bubbles: true }));
  }
}

/** Tells the host the page's title (the last breadcrumb) and route, so the tab title follows navigation. */
function TitleReporter() {
  const trail = useDashboardBreadcrumbTrail();
  const location = useLocation();
  const path = `${location.pathname}${location.search}`;
  // The tab title: the last two crumbs below the home crumb ("Activity · Token Usage"), or Overview at home.
  const crumbs = (trail ?? []).filter((item, index) => !(index === 0 && item.to === '/')).map((item) => item.label).filter(Boolean);
  const title = crumbs.length ? crumbs.slice(-2).join(' · ') : location.pathname === '/' ? 'Overview' : '';
  useEffect(() => {
    post({ type: 'location', path, title });
    saveState({ path });
  }, [path, title]);
  // A short summary of what rendered (charts, tables, cards), for the host's diagnostics and tests.
  useEffect(() => {
    const timer = setTimeout(() => {
      const main = document.getElementById('main-content');
      if (!main) return;
      const message = main.querySelector('.page-message, .alert-error, .error-message')?.textContent?.trim().slice(0, 200);
      post({
        type: 'snapshot',
        path,
        charts: main.querySelectorAll('[class*="chart-shell"], [class*="chart-container"], [class*="chart-card"], [class*="chart-plot"], .vhist-heatmap, svg[role="img"]').length,
        tables: main.querySelectorAll('table').length,
        cards: main.querySelectorAll('.card, [class*="-card"]').length,
        text: (main.textContent ?? '').length,
        message: message || undefined,
      });
    }, 4000);
    return () => clearTimeout(timer);
  }, [path]);
  return null;
}

export default function EmbeddedShell() {
  const location = useLocation();
  const navigate = useNavigate();
  const { t } = useLocale();
  const { toasts, dismissToast } = useNotifications();
  const { connected } = useWebSocket();
  const [revision, setRevision] = useState(0);
  const path = `${location.pathname}${location.search}`;

  useEffect(() => onHostMessage((message) => {
    if (message.type === 'navigate') navigate(message.path, { state: message.state ?? null });
    else if (message.type === 'reload') setRevision((r) => r + 1);
    else if (message.type === 'automation') automate(message);
  }), [navigate]);

  // Ask Armada has its own view in VS Code; a link to it opens that view and the page stays where it was.
  useEffect(() => {
    const command = hostHandledRoute(path);
    if (!command) return;
    post({ type: 'command', command });
    if (window.history.length > 1) navigate(-1);
    else navigate('/', { replace: true });
  }, [path, navigate]);

  return (
    <div className="vscode-shell">
      <div className="vscode-toolbar" role="toolbar" aria-label={t('Page navigation')}>
        <button className="vscode-icon-btn" onClick={() => navigate(-1)} title={t('Back')} aria-label={t('Back')}>&#x2190;</button>
        <button className="vscode-icon-btn" onClick={() => navigate(1)} title={t('Forward')} aria-label={t('Forward')}>&#x2192;</button>
        <button className="vscode-icon-btn" onClick={() => setRevision((r) => r + 1)} title={t('Reload this page')} aria-label={t('Reload')}>&#x21bb;</button>
        <Link className="vscode-icon-btn" to="/" title={t('Armada Overview')} aria-label={t('Armada Overview')}>&#x2302;</Link>
        <div className="vscode-toolbar-crumbs"><DashboardBreadcrumbs /></div>
        <span className="vscode-live" title={connected ? t('Live: pages update as things change.') : t('Offline: pages may be stale until the connection returns.')}>
          <span className={`status-dot ${connected ? 'connected' : 'disconnected'}`} />
          {connected ? t('Live') : t('Offline')}
        </span>
        <button className="vscode-icon-btn" onClick={() => post({ type: 'openPage', path })} title={t('Open this page in another tab')} aria-label={t('Open in another tab')}>&#x29c9;</button>
        <button className="vscode-icon-btn" onClick={() => post({ type: 'openInBrowser', path })} title={t('Open this page in the browser dashboard')} aria-label={t('Open in browser')}>&#x2197;</button>
      </div>
      <main className="main vscode-main" id="main-content" tabIndex={-1}>
        <div className="view">
          <PageErrorBoundary resetKey={`${path}#${revision}`}>
            <Suspense fallback={<LoadingIndicator fullHeight label={t('Loading page...')} />}>
              <div key={revision} className="vscode-page"><Outlet /></div>
            </Suspense>
          </PageErrorBoundary>
        </div>
      </main>
      <TitleReporter />
      {toasts.length > 0 && (
        <div className="toast-container">
          {toasts.map((toast) => (
            <div key={toast.id} className={`toast toast-${toast.severity}`}>
              <span className="toast-body">{toast.message}{toast.link && <>{' '}<Link className="toast-link" to={toast.link.to} onClick={() => dismissToast(toast.id)}>{toast.link.label}</Link></>}</span>
              <button className="toast-close" onClick={() => dismissToast(toast.id)} aria-label={t('Dismiss')}>&times;</button>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

/** The shell with the breadcrumb context pages register their trail in. */
export function EmbeddedLayout() {
  return (
    <DashboardBreadcrumbProvider>
      <EmbeddedShell />
    </DashboardBreadcrumbProvider>
  );
}
