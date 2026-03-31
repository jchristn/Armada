import { useState, useEffect, useCallback, type ReactNode } from 'react';
import { NavLink, Outlet, useLocation } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';
import { useTheme } from '../context/ThemeContext';
import { useWebSocket } from '../context/WebSocketContext';
import { useNotifications } from '../context/NotificationContext';
import { getHealth } from '../api/client';

interface NavItem {
  to: string;
  label: string;
  icon: ReactNode;
  hidden?: boolean;
}

interface NavSection {
  key: string;
  label: string;
  matchers: string[];
  items: NavItem[];
}

const dashboardItem: NavItem = {
  to: '/dashboard',
  label: 'Dashboard',
  icon: (
    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <rect x="3" y="3" width="7" height="7" />
      <rect x="14" y="3" width="7" height="7" />
      <rect x="14" y="14" width="7" height="7" />
      <rect x="3" y="14" width="7" height="7" />
    </svg>
  ),
};

const navSections: NavSection[] = [
  {
    key: 'operations',
    label: 'OPERATIONS',
    matchers: ['/dispatch', '/voyages', '/missions', '/merge-queue'],
    items: [
      {
        to: '/dispatch',
        label: 'Dispatch',
        icon: (
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
            <path d="M22 2 11 13" />
            <polygon points="22 2 15 22 11 13 2 9 22 2" />
          </svg>
        ),
      },
      {
        to: '/voyages',
        label: 'Voyages',
        icon: (
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
            <circle cx="12" cy="12" r="10" />
            <polyline points="12 6 12 12 16 14" />
          </svg>
        ),
      },
      {
        to: '/missions',
        label: 'Missions',
        icon: (
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
            <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z" />
            <polyline points="14 2 14 8 20 8" />
            <line x1="16" y1="13" x2="8" y2="13" />
            <line x1="16" y1="17" x2="8" y2="17" />
          </svg>
        ),
      },
      {
        to: '/merge-queue',
        label: 'Merge Queue',
        icon: (
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
            <circle cx="18" cy="18" r="3" />
            <circle cx="6" cy="6" r="3" />
            <path d="M6 21V9a9 9 0 0 0 9 9" />
          </svg>
        ),
      },
    ],
  },
  {
    key: 'fleet',
    label: 'FLEET',
    matchers: ['/fleets', '/vessels', '/captains', '/docks'],
    items: [
      {
        to: '/fleets',
        label: 'Fleets',
        icon: (
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
            <rect x="2" y="2" width="20" height="8" rx="2" ry="2" />
            <rect x="2" y="14" width="20" height="8" rx="2" ry="2" />
          </svg>
        ),
      },
      {
        to: '/vessels',
        label: 'Vessels',
        icon: (
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
            <path d="M22 19a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h5l2 3h9a2 2 0 0 1 2 2z" />
          </svg>
        ),
      },
      {
        to: '/captains',
        label: 'Captains',
        icon: (
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
            <path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2" />
            <circle cx="12" cy="7" r="4" />
          </svg>
        ),
      },
      {
        to: '/docks',
        label: 'Docks',
        icon: (
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
            <rect x="2" y="7" width="20" height="14" rx="2" ry="2" />
            <path d="M16 21V5a2 2 0 0 0-2-2h-4a2 2 0 0 0-2 2v16" />
          </svg>
        ),
      },
    ],
  },
  {
    key: 'activity',
    label: 'ACTIVITY',
    matchers: ['/signals', '/events', '/notifications'],
    items: [
      {
        to: '/signals',
        label: 'Signals',
        icon: (
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
            <polyline points="22 12 18 12 15 21 9 3 6 12 2 12" />
          </svg>
        ),
      },
      {
        to: '/events',
        label: 'Events',
        icon: (
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
            <path d="M12 20h9" />
            <path d="M16.5 3.5a2.121 2.121 0 0 1 3 3L7 19l-4 1 1-4L16.5 3.5z" />
          </svg>
        ),
      },
      {
        to: '/notifications',
        label: 'Notifications',
        icon: (
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
            <path d="M18 8A6 6 0 0 0 6 8c0 7-3 9-3 9h18s-3-2-3-9" />
            <path d="M13.73 21a2 2 0 0 1-3.46 0" />
          </svg>
        ),
      },
    ],
  },
  {
    key: 'system',
    label: 'SYSTEM',
    matchers: ['/server', '/doctor', '/settings', '/personas', '/pipelines', '/prompt-templates'],
    items: [
      {
        to: '/personas',
        label: 'Personas',
        icon: (
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
            <path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2" />
            <circle cx="12" cy="7" r="4" />
          </svg>
        ),
      },
      {
        to: '/pipelines',
        label: 'Pipelines',
        icon: (
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
            <polyline points="16 3 21 3 21 8" />
            <line x1="4" y1="20" x2="21" y2="3" />
            <polyline points="21 16 21 21 16 21" />
            <line x1="15" y1="15" x2="21" y2="21" />
            <line x1="4" y1="4" x2="9" y2="9" />
          </svg>
        ),
      },
      {
        to: '/prompt-templates',
        label: 'Templates',
        icon: (
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
            <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z" />
            <polyline points="14 2 14 8 20 8" />
            <line x1="16" y1="13" x2="8" y2="13" />
            <line x1="16" y1="17" x2="8" y2="17" />
            <line x1="10" y1="9" x2="8" y2="9" />
          </svg>
        ),
      },
      {
        to: '/server',
        label: 'Server',
        icon: (
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
            <rect x="2" y="2" width="20" height="8" rx="2" ry="2" />
            <rect x="2" y="14" width="20" height="8" rx="2" ry="2" />
            <line x1="6" y1="6" x2="6.01" y2="6" />
            <line x1="6" y1="18" x2="6.01" y2="18" />
          </svg>
        ),
      },
      {
        to: '/doctor',
        label: 'Doctor',
        icon: (
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
            <path d="M22 12h-4l-3 9L9 3l-3 9H2" />
          </svg>
        ),
      },
    ],
  },
  {
    key: 'admin',
    label: 'ADMINISTRATION',
    matchers: ['/admin/tenants', '/admin/users', '/admin/credentials'],
    items: [
      {
        to: '/admin/tenants',
        label: 'Tenants',
        icon: (
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
            <path d="M3 21h18" />
            <path d="M5 21V7l7-4 7 4v14" />
            <path d="M9 9h.01" />
            <path d="M9 13h.01" />
            <path d="M15 9h.01" />
            <path d="M15 13h.01" />
          </svg>
        ),
      },
      {
        to: '/admin/users',
        label: 'Users',
        icon: (
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
            <path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2" />
            <circle cx="9" cy="7" r="4" />
            <path d="M23 21v-2a4 4 0 0 0-3-3.87" />
            <path d="M16 3.13a4 4 0 0 1 0 7.75" />
          </svg>
        ),
      },
      {
        to: '/admin/credentials',
        label: 'Credentials',
        icon: (
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
            <path d="M21 2l-2 2m-7.61 7.61a5.5 5.5 0 1 1 7.78 7.78l-3.19-3.19" />
            <path d="M5.5 12.5 2 16l6 6 3.5-3.5" />
            <path d="m14.5 8.5 1 1" />
            <path d="m10.5 12.5 1 1" />
          </svg>
        ),
      },
    ],
  },
];

type HealthStatus = 'healthy' | 'warning' | 'error' | 'unknown';

export default function Layout() {
  const location = useLocation();
  const { user, isAdmin, isTenantAdmin, logout } = useAuth();
  const { darkMode, toggleTheme } = useTheme();
  const { connected } = useWebSocket();
  const { unreadCount, toasts, dismissToast } = useNotifications();
  const [collapsed, setCollapsed] = useState(() => {
    try {
      return localStorage.getItem('armada_sidebar_collapsed') === 'true';
    } catch {
      return false;
    }
  });
  const [sections, setSections] = useState<Record<string, boolean>>(() => {
    try {
      const stored = localStorage.getItem('armada_sidebar_sections');
      if (stored) return JSON.parse(stored) as Record<string, boolean>;
    } catch {
      // ignore
    }
    return {
      operations: true,
      fleet: true,
      activity: true,
      system: true,
      admin: true,
    };
  });
  const [healthStatus, setHealthStatus] = useState<HealthStatus>('unknown');

  useEffect(() => {
    try {
      localStorage.setItem('armada_sidebar_collapsed', String(collapsed));
    } catch {
      // ignore
    }
  }, [collapsed]);

  useEffect(() => {
    try {
      localStorage.setItem('armada_sidebar_sections', JSON.stringify(sections));
    } catch {
      // ignore
    }
  }, [sections]);

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

  const filteredSections = navSections
    .map((section) =>
      section.key !== 'admin'
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
    .filter((section) => section.key !== 'admin' || section.items.length > 0);

  const isSectionActive = useCallback(
    (matchers: string[]) => matchers.some((matcher) => location.pathname.startsWith(matcher)),
    [location.pathname],
  );

  return (
    <div className="app-layout" style={{ gridTemplateColumns: collapsed ? '56px 1fr' : '180px 1fr' }}>
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
            className={({ isActive }) => `sidebar-nav-item${isActive ? ' active' : ''}`}
            title={collapsed ? dashboardItem.label : undefined}
          >
            {dashboardItem.icon}
            <span className="sidebar-label">{dashboardItem.label}</span>
          </NavLink>

          {filteredSections.map((section) => (
            <div
              key={section.key}
              className={`sidebar-section${isSectionActive(section.matchers) ? ' section-active' : ''}${!sections[section.key] ? ' collapsed' : ''}`}
            >
              {!collapsed && (
                <button className="sidebar-section-header" onClick={() => toggleSection(section.key)}>
                  {section.label}
                  <span className="sidebar-section-chevron">
                    <svg viewBox="0 0 24 24">
                      <polyline points="6 9 12 15 18 9" />
                    </svg>
                  </span>
                </button>
              )}
              <div className="sidebar-section-items" style={{ display: collapsed || sections[section.key] ? undefined : 'none' }}>
                {section.items.filter((item) => !item.hidden).map((item) => (
                  <NavLink
                    key={item.to}
                    to={item.to}
                    className={({ isActive }) => `sidebar-nav-item${isActive ? ' active' : ''}`}
                    title={collapsed ? item.label : undefined}
                  >
                    {item.icon}
                    <span className="sidebar-label">{item.label}</span>
                    {item.to === '/notifications' && unreadCount > 0 && (
                      <span className="notif-badge">{unreadCount > 99 ? '99+' : unreadCount}</span>
                    )}
                  </NavLink>
                ))}
              </div>
            </div>
          ))}
        </nav>

        <div className="sidebar-footer">
          <button
            className="sidebar-toggle-btn"
            onClick={() => setCollapsed((prev) => !prev)}
            title={collapsed ? 'Expand sidebar' : 'Collapse sidebar'}
          >
            <span aria-hidden="true">{collapsed ? '\u25B6' : '\u25C0'}</span>
          </button>
          {!collapsed && (
            <>
              <button className="btn btn-sm" onClick={logout} title="Sign out">
                Logout
              </button>
              <span style={{ fontSize: '0.7em', color: 'var(--text-dim)', opacity: 0.5 }}>v{__APP_VERSION__}</span>
            </>
          )}
        </div>
      </aside>

      <div className="main-content-area">
        <div className="top-bar">
          <NavLink to="/doctor" className="top-bar-health" title={`Health: ${healthStatus}`}>
            <span
              className={`status-dot ${
                healthStatus === 'healthy' ? 'healthy' : healthStatus === 'warning' ? 'warning' : 'error'
              }`}
            />
            <span className="top-bar-status-label">
              {healthStatus === 'healthy'
                ? 'Healthy'
                : healthStatus === 'warning'
                  ? 'Degraded'
                  : healthStatus === 'unknown'
                    ? 'Checking...'
                    : 'Unhealthy'}
            </span>
          </NavLink>

          <span className="top-bar-status" title={connected ? 'Live: WebSocket connected' : 'Disconnected'}>
            <span className={`status-dot ${connected ? 'connected' : 'disconnected'}`} />
            <span className="top-bar-status-label">{connected ? 'Live' : 'Offline'}</span>
          </span>

          {user && (
            <>
              {isAdmin && <span className="auth-badge auth-badge-admin">Global Admin</span>}
              {!isAdmin && isTenantAdmin && <span className="auth-badge auth-badge-tenant-admin">Tenant Admin</span>}
              <span className="auth-badge auth-badge-tenant">{user.tenant?.name}</span>
              <span className="auth-badge auth-badge-user">{user.user?.email}</span>
            </>
          )}

          <button className="theme-toggle" onClick={toggleTheme} title={darkMode ? 'Switch to light mode' : 'Switch to dark mode'}>
            {darkMode ? '\u2600' : '\u263E'}
          </button>

          <a href="https://github.com/jchristn/Armada" target="_blank" rel="noopener noreferrer" className="github-link" title="View on GitHub">
            <svg height="18" width="18" viewBox="0 0 16 16" fill="currentColor">
              <path d="M8 0C3.58 0 0 3.58 0 8c0 3.54 2.29 6.53 5.47 7.59.4.07.55-.17.55-.38 0-.19-.01-.82-.01-1.49-2.01.37-2.53-.49-2.69-.94-.09-.23-.48-.94-.82-1.13-.28-.15-.68-.52-.01-.53.63-.01 1.08.58 1.23.82.72 1.21 1.87.87 2.33.66.07-.52.28-.87.51-1.07-1.78-.2-3.64-.89-3.64-3.95 0-.87.31-1.59.82-2.15-.08-.2-.36-1.02.08-2.12 0 0 .67-.21 2.2.82.64-.18 1.32-.27 2-.27s1.36.09 2 .27c1.53-1.04 2.2-.82 2.2-.82.44 1.1.16 1.92.08 2.12.51.56.82 1.27.82 2.15 0 3.07-1.87 3.75-3.65 3.95.29.25.54.73.54 1.48 0 1.07-.01 1.93-.01 2.2 0 .21.15.46.55.38A8.01 8.01 0 0 0 16 8c0-4.42-3.58-8-8-8z" />
            </svg>
          </a>

          {collapsed && (
            <button className="btn btn-sm" onClick={logout} title="Sign out">
              Logout
            </button>
          )}
        </div>

        <main className="main">
          <div className="view">
            <Outlet />
          </div>
        </main>
      </div>

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
    </div>
  );
}
