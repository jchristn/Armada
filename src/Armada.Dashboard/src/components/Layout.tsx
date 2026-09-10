import { useState, useEffect, useCallback } from 'react';
import { NavLink, Outlet, useLocation } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';
import { useLocale } from '../context/LocaleContext';
import { useTheme } from '../context/ThemeContext';
import { useWebSocket } from '../context/WebSocketContext';
import { useNotifications } from '../context/NotificationContext';
import { clearProxySessionInstance, getHealth, getProxySessionContext, listCaptains, listFleets, listVessels, logoutProxy, type ProxySessionContext } from '../api/client';
import SetupWizard, { isSetupComplete, clearSetupComplete } from './SetupWizard';
import LanguageSelector from './shared/LanguageSelector';
import NotificationBell from './shared/NotificationBell';
import CommandPalette from './shared/CommandPalette';
import { dashboardItem, askArmadaItem, navSections, DEFAULT_EXPANDED_SECTIONS, type NavItem } from './navConfig';
import { useInboxCount } from '../lib/useInboxCount';

type HealthStatus = 'healthy' | 'warning' | 'error' | 'unknown';

export default function Layout() {
  const location = useLocation();
  const { user, isAdmin, isTenantAdmin, logout } = useAuth();
  const { t } = useLocale();
  const { darkMode, toggleTheme } = useTheme();
  const { connected } = useWebSocket();
  const inboxAttention = useInboxCount();
  const { toasts, dismissToast } = useNotifications();
  const [showWizard, setShowWizard] = useState(false);
  const [wizardHighlights, setWizardHighlights] = useState<string[]>([]);
  const [collapsed, setCollapsed] = useState(() => {
    try {
      return localStorage.getItem('armada_sidebar_collapsed') === 'true';
    } catch {
      return false;
    }
  });
  const [sections, setSections] = useState<Record<string, boolean>>({ ...DEFAULT_EXPANDED_SECTIONS });
  const [healthStatus, setHealthStatus] = useState<HealthStatus>('unknown');
  const [proxyContext, setProxyContext] = useState<ProxySessionContext | null>(null);

  useEffect(() => {
    try {
      localStorage.setItem('armada_sidebar_collapsed', String(collapsed));
    } catch {
      // ignore
    }
  }, [collapsed]);

  const toggleSection = useCallback((key: string) => {
    setSections((prev) => ({ ...prev, [key]: !prev[key] }));
  }, []);

  useEffect(() => {
    let mounted = true;

    const fetchHealth = () => {
      getHealth()
        .then((data) => {
          if (!mounted) return;
          const status = String(data.status || data.Status || '').toLowerCase();
          if (status === 'healthy' || status === 'ok') setHealthStatus('healthy');
          else if (status === 'degraded' || status === 'warning') setHealthStatus('warning');
          else setHealthStatus('error');
        })
        .catch(() => {
          if (mounted) setHealthStatus('error');
        });
    };

    fetchHealth();
    const timer = setInterval(fetchHealth, 30000);
    return () => {
      mounted = false;
      clearInterval(timer);
    };
  }, []);

  useEffect(() => {
    let mounted = true;
    async function evaluateWizardVisibility() {
      try {
        const [fleetResult, vesselResult, captainResult] = await Promise.all([
          listFleets({ pageSize: 1 }),
          listVessels({ pageSize: 1 }),
          listCaptains({ pageSize: 1 }),
        ]);

        if (!mounted) return;

        const hasFleet = (fleetResult.objects || []).length > 0;
        const hasVessel = (vesselResult.objects || []).length > 0;
        const hasCaptain = (captainResult.objects || []).length > 0;

        // A completely empty deployment (e.g. a fresh install or after factory-reset) always shows the
        // wizard, even if this browser previously finished setup against another deployment. Clear that
        // stale flag so it does not permanently suppress the wizard on the fresh deployment.
        if (!hasFleet && !hasVessel && !hasCaptain) {
          clearSetupComplete();
          setShowWizard(true);
          return;
        }

        // Otherwise honor the per-browser "completed" flag; if not set, prompt only while something is
        // still missing.
        if (isSetupComplete()) {
          setShowWizard(false);
          return;
        }
        setShowWizard(!hasFleet || !hasVessel || !hasCaptain);
      } catch {
        if (mounted) {
          setShowWizard(true);
        }
      }
    }

    evaluateWizardVisibility();
    return () => {
      mounted = false;
    };
  }, [user?.user?.id]);

  useEffect(() => {
    function handleOpenSetupWizard() {
      setShowWizard(true);
    }

    window.addEventListener('armada:open-setup-wizard', handleOpenSetupWizard);
    return () => {
      window.removeEventListener('armada:open-setup-wizard', handleOpenSetupWizard);
    };
  }, []);

  useEffect(() => {
    let mounted = true;

    getProxySessionContext()
      .then((context) => {
        if (mounted) setProxyContext(context);
      })
      .catch(() => {
        if (mounted) setProxyContext(null);
      });

    return () => {
      mounted = false;
    };
  }, [user?.user?.id]);

  const filteredSections = navSections
    .map((section) =>
      section.key !== 'security'
        ? section
        : {
            ...section,
            items: section.items.filter((item) => {
              if (item.to === '/admin/tenants') return true;
              if (item.to === '/admin/users') return true;
              if (item.to === '/admin/credentials') return true;
              return false;
            }),
          }
    )
    .filter((section) => section.key !== 'security' || section.items.length > 0);

  const isSectionActive = useCallback(
    (matchers: string[]) => matchers.some((matcher) => location.pathname.startsWith(matcher)),
    [location.pathname],
  );

  const isItemActive = useCallback((item: NavItem): boolean => {
    return location.pathname === item.to || location.pathname.startsWith(`${item.to}/`);
  }, [location.pathname]);

  const renderNavItem = useCallback((item: NavItem) => {
    if (item.hidden) return null;

    const showAttention = item.to === '/inbox' && inboxAttention.count > 0;
    const attentionClass = inboxAttention.hasCritical
      ? ' sidebar-nav-badge-critical'
      : inboxAttention.hasWarning
      ? ' sidebar-nav-badge-warning'
      : '';

    return (
      <NavLink
        key={item.key || item.to}
        to={item.to}
        className={({ isActive }) => `sidebar-nav-item${isActive || isItemActive(item) ? ' active' : ''}${wizardHighlights.includes(item.to) ? ' wizard-highlight' : ''}${showAttention ? ' has-attention' : ''}`}
        title={collapsed ? t(item.label) : t(item.tooltip || item.label)}
      >
        {item.icon}
        <span className="sidebar-label">{t(item.label)}</span>
        {showAttention && (
          <span
            className={`sidebar-nav-badge${attentionClass}`}
            title={t('{{count}} items need your attention', { count: inboxAttention.count })}
            aria-label={t('{{count}} items need your attention', { count: inboxAttention.count })}
          >
            {inboxAttention.count > 99 ? '99+' : inboxAttention.count}
          </span>
        )}
      </NavLink>
    );
  }, [collapsed, isItemActive, t, wizardHighlights, inboxAttention]);

  const handleSwitchDeployment = useCallback(async () => {
    try {
      await clearProxySessionInstance();
    } catch {
      // Best effort. The portal is still the recovery path.
    }
    window.location.assign('/');
  }, []);

  const handleProxyLogout = useCallback(async () => {
    try {
      await logoutProxy();
    } catch {
      // Best effort logout.
    }
    logout();
    window.location.assign('/');
  }, [logout]);

  const layoutClassName = [
    'app-layout',
    showWizard ? 'wizard-active' : '',
    showWizard && wizardHighlights.length > 0 ? 'wizard-spotlight-active' : '',
  ].filter(Boolean).join(' ');
  const proxyInstance = proxyContext?.selectedInstance;
  const showProxyContext = !!proxyContext?.selectedInstanceId;

  return (
    <div className="app-shell">
      {showProxyContext && (
        <div className="proxy-context-strip proxy-context-shell-strip">
          <div className="proxy-context-copy">
            <span className="proxy-context-pill">{t('Proxy Mode')}</span>
            <span>
              {t('Remote dashboard for {{instanceId}}', {
                instanceId: proxyInstance?.instanceId || proxyContext?.selectedInstanceId || '',
              })}
            </span>
            <span className="proxy-context-meta">
              {t('State: {{state}}', { state: proxyInstance?.state || 'unknown' })}
            </span>
            {proxyInstance?.armadaVersion && (
              <span className="proxy-context-meta">
                {t('Armada {{version}}', { version: proxyInstance.armadaVersion })}
              </span>
            )}
          </div>

          <div className="proxy-context-actions">
            <button className="btn btn-sm" onClick={handleSwitchDeployment}>
              {t('Switch Deployment')}
            </button>
            <button className="btn btn-sm" onClick={handleProxyLogout}>
              {t('Proxy Logout')}
            </button>
          </div>
        </div>
      )}

      <div className={layoutClassName} style={{ gridTemplateColumns: collapsed ? '56px 1fr' : '220px 1fr' }}>
        <aside className={`sidebar${collapsed ? ' sidebar-collapsed' : ''}`}>
        <div className="sidebar-brand">
          <img
            src="/img/logo-light-grey.png"
            alt="Armada"
            className="sidebar-logo"
            onError={(e) => {
              (e.target as HTMLImageElement).style.display = 'none';
            }}
          />
          <h1 className="sidebar-label">Armada</h1>
        </div>

        <nav className="sidebar-nav">
          <NavLink
            to={dashboardItem.to}
            className={({ isActive }) => `sidebar-nav-item${isActive ? ' active' : ''}${wizardHighlights.includes(dashboardItem.to) ? ' wizard-highlight' : ''}`}
            title={collapsed ? t(dashboardItem.label) : t(dashboardItem.tooltip || dashboardItem.label)}
          >
            {dashboardItem.icon}
            <span className="sidebar-label">{t(dashboardItem.label)}</span>
          </NavLink>

          <NavLink
            to={askArmadaItem.to}
            className={({ isActive }) => `sidebar-nav-item sidebar-nav-primary${isActive || location.pathname.startsWith('/ask') ? ' active' : ''}${wizardHighlights.includes(askArmadaItem.to) ? ' wizard-highlight' : ''}`}
            title={collapsed ? t(askArmadaItem.label) : t(askArmadaItem.tooltip || askArmadaItem.label)}
          >
            {askArmadaItem.icon}
            <span className="sidebar-label">{t(askArmadaItem.label)}</span>
          </NavLink>

          {filteredSections.map((section) => (
            <div
              key={section.key}
              className={`sidebar-section${isSectionActive(section.matchers) ? ' section-active' : ''}${!sections[section.key] ? ' collapsed' : ''}`}
            >
              {!collapsed && (
                <button className="sidebar-section-header" onClick={() => toggleSection(section.key)}>
                  {t(section.label)}
                  <span className="sidebar-section-chevron">
                    <svg viewBox="0 0 24 24">
                      <polyline points="6 9 12 15 18 9" />
                    </svg>
                  </span>
                </button>
              )}
              <div className="sidebar-section-items" style={{ display: collapsed || sections[section.key] ? undefined : 'none' }}>
                {section.items.filter((item) => !item.hidden).map((item) => renderNavItem(item))}
              </div>
            </div>
          ))}
        </nav>

        {!collapsed && (
          <div className="sidebar-footer">
            <span style={{ fontSize: '0.7em', color: 'var(--text-dim)', opacity: 0.5 }}>v{__APP_VERSION__}</span>
          </div>
        )}
        </aside>

        <div className="main-content-area">
          <div className="top-bar">
            <button
              className="top-bar-collapse-btn"
              onClick={() => setCollapsed((prev) => !prev)}
              title={collapsed ? t('Expand sidebar') : t('Collapse sidebar')}
              aria-label={collapsed ? t('Expand sidebar') : t('Collapse sidebar')}
            >
              <span aria-hidden="true">{collapsed ? '»' : '«'}</span>
            </button>

            <NavLink to="/server?tab=diagnostics" className="top-bar-health" title={t('Health: {{status}}', { status: t(healthStatus === 'healthy' ? 'Healthy' : healthStatus === 'warning' ? 'Degraded' : healthStatus === 'unknown' ? 'Checking...' : 'Unhealthy') })}>
              <span
                className={`status-dot ${
                  healthStatus === 'healthy' ? 'healthy' : healthStatus === 'warning' ? 'warning' : 'error'
                }`}
              />
              <span className="top-bar-status-label">
                {healthStatus === 'healthy'
                  ? t('Healthy')
                  : healthStatus === 'warning'
                    ? t('Degraded')
                    : healthStatus === 'unknown'
                      ? t('Checking...')
                      : t('Unhealthy')}
              </span>
            </NavLink>

            <span className="top-bar-status" title={connected ? t('Live: WebSocket connected') : t('Disconnected')}>
              <span className={`status-dot ${connected ? 'connected' : 'disconnected'}`} />
              <span className="top-bar-status-label">{connected ? t('Live') : t('Offline')}</span>
            </span>

            {user && (
              <>
                {isAdmin && <span className="auth-badge auth-badge-admin">{t('Global Admin')}</span>}
                {!isAdmin && isTenantAdmin && <span className="auth-badge auth-badge-tenant-admin">{t('Tenant Admin')}</span>}
                <span className="auth-badge auth-badge-tenant">{user.tenant?.name}</span>
                <span className="auth-badge auth-badge-user">{user.user?.email}</span>
              </>
            )}

            <NotificationBell />

            <LanguageSelector className="topbar-language-select" compact />

            <button className="theme-toggle" onClick={toggleTheme} title={darkMode ? t('Switch to light mode') : t('Switch to dark mode')}>
              {darkMode ? '☀' : '☾'}
            </button>

            <a href="https://discord.gg/tRAN8HgvK5" target="_blank" rel="noopener noreferrer" className="discord-link" title={t('Join us on Discord')} aria-label={t('Join us on Discord')}>
              <svg height="18" width="18" viewBox="0 0 16 16" fill="currentColor">
                <path d="M13.545 2.907a13.2 13.2 0 0 0-3.257-1.011.05.05 0 0 0-.052.025c-.141.25-.297.577-.406.833a12.2 12.2 0 0 0-3.658 0 8 8 0 0 0-.412-.833.05.05 0 0 0-.052-.025c-1.125.194-2.22.534-3.257 1.011a.04.04 0 0 0-.021.018C.356 6.024-.213 9.047.066 12.032q.003.022.021.037a13.3 13.3 0 0 0 3.995 2.02.05.05 0 0 0 .056-.019q.463-.63.818-1.329a.05.05 0 0 0-.01-.059l-.018-.011a9 9 0 0 1-1.248-.595.05.05 0 0 1-.02-.066l.015-.019q.127-.095.248-.195a.05.05 0 0 1 .051-.007c2.619 1.196 5.454 1.196 8.041 0a.05.05 0 0 1 .053.007q.121.1.248.195a.05.05 0 0 1-.004.085 8 8 0 0 1-1.249.594.05.05 0 0 0-.03.03.05.05 0 0 0 .003.041c.24.465.515.909.817 1.329a.05.05 0 0 0 .056.019 13.2 13.2 0 0 0 4.001-2.02.05.05 0 0 0 .021-.037c.334-3.451-.559-6.449-2.366-9.106a.03.03 0 0 0-.02-.019m-8.198 7.307c-.789 0-1.438-.724-1.438-1.612s.637-1.613 1.438-1.613c.807 0 1.45.73 1.438 1.613 0 .888-.637 1.612-1.438 1.612m5.316 0c-.788 0-1.438-.724-1.438-1.612s.637-1.613 1.438-1.613c.807 0 1.451.73 1.438 1.613 0 .888-.631 1.612-1.438 1.612" />
              </svg>
            </a>

            <a href="https://github.com/jchristn/Armada" target="_blank" rel="noopener noreferrer" className="github-link" title={t('View on GitHub')}>
              <svg height="18" width="18" viewBox="0 0 16 16" fill="currentColor">
                <path d="M8 0C3.58 0 0 3.58 0 8c0 3.54 2.29 6.53 5.47 7.59.4.07.55-.17.55-.38 0-.19-.01-.82-.01-1.49-2.01.37-2.53-.49-2.69-.94-.09-.23-.48-.94-.82-1.13-.28-.15-.68-.52-.01-.53.63-.01 1.08.58 1.23.82.72 1.21 1.87.87 2.33.66.07-.52.28-.87.51-1.07-1.78-.2-3.64-.89-3.64-3.95 0-.87.31-1.59.82-2.15-.08-.2-.36-1.02.08-2.12 0 0 .67-.21 2.2.82.64-.18 1.32-.27 2-.27s1.36.09 2 .27c1.53-1.04 2.2-.82 2.2-.82.44 1.1.16 1.92.08 2.12.51.56.82 1.27.82 2.15 0 3.07-1.87 3.75-3.65 3.95.29.25.54.73.54 1.48 0 1.07-.01 1.93-.01 2.2 0 .21.15.46.55.38A8.01 8.01 0 0 0 16 8c0-4.42-3.58-8-8-8z" />
              </svg>
            </a>

            <button className="top-bar-logout-btn" onClick={logout} title={t('Sign out')} aria-label={t('Sign out')}>
              <span aria-hidden="true">&#x23FB;</span>
            </button>
          </div>

          <main className="main">
            <div className="view">
              <Outlet />
            </div>
          </main>
        </div>
      </div>

      <CommandPalette />

      {toasts.length > 0 && (
        <div className="toast-container">
          {toasts.map((toast) => (
            <div key={toast.id} className={`toast toast-${toast.severity}`}>
              <span className="toast-body">{toast.message}</span>
              <button className="toast-close" onClick={() => dismissToast(toast.id)}>
                &times;
              </button>
            </div>
          ))}
        </div>
      )}

      {showWizard && (
        <SetupWizard
          onClose={() => setShowWizard(false)}
          onHighlightChange={setWizardHighlights}
        />
      )}
    </div>
  );
}
