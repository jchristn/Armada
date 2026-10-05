import { useCallback, useEffect, useRef, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useNotifications, type Notification } from '../../context/NotificationContext';
import { useLocale } from '../../context/LocaleContext';
import { notificationRoute } from '../../lib/routing';

function severityDotClass(severity: Notification['severity']): string {
  switch (severity) {
    case 'error': return 'notif-error';
    case 'warning': return 'notif-warning';
    case 'success': return 'notif-success';
    default: return 'notif-info';
  }
}

const MAX_VISIBLE = 8;

/**
 * Top-bar notification bell. Replaces the standalone Notifications page: it
 * reads the same `NotificationContext`, shows an unread count, a recent list,
 * "mark all read", and navigates to the source record on click. Unresolved
 * items also feed Needs You (see Inbox), so no dedicated page is needed.
 */
export default function NotificationBell() {
  const navigate = useNavigate();
  const { t, formatRelativeTime, formatDateTime } = useLocale();
  const { notifications, unreadCount, markRead, markAllRead, clearHistory } = useNotifications();
  const [open, setOpen] = useState(false);
  const containerRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open) return undefined;

    function handlePointerDown(event: MouseEvent) {
      if (containerRef.current && !containerRef.current.contains(event.target as Node)) {
        setOpen(false);
      }
    }
    function handleKeyDown(event: globalThis.KeyboardEvent) {
      if (event.key === 'Escape') setOpen(false);
    }

    document.addEventListener('mousedown', handlePointerDown);
    document.addEventListener('keydown', handleKeyDown);
    return () => {
      document.removeEventListener('mousedown', handlePointerDown);
      document.removeEventListener('keydown', handleKeyDown);
    };
  }, [open]);

  const handleClick = useCallback((n: Notification) => {
    markRead(n.id);
    const route = notificationRoute(n);
    setOpen(false);
    if (route) navigate(route);
  }, [markRead, navigate]);

  const visible = notifications.slice(0, MAX_VISIBLE);

  return (
    <div className="notif-bell" ref={containerRef}>
      <button
        type="button"
        className="notif-bell-btn"
        onClick={() => setOpen((prev) => !prev)}
        title={t('Notifications')}
        aria-label={t('Notifications')}
        aria-haspopup="true"
        aria-expanded={open}
      >
        <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
          <path d="M18 8A6 6 0 0 0 6 8c0 7-3 9-3 9h18s-3-2-3-9" />
          <path d="M13.73 21a2 2 0 0 1-3.46 0" />
        </svg>
        {unreadCount > 0 && (
          <span className="notif-badge">{unreadCount > 99 ? '99+' : unreadCount}</span>
        )}
      </button>

      {open && (
        <div className="notif-dropdown" role="menu" aria-label={t('Notifications')}>
          <div className="notif-dropdown-header">
            <span className="notif-dropdown-title">{t('Notifications')}</span>
            <div className="notif-dropdown-actions">
              <button
                type="button"
                className="btn-link"
                onClick={markAllRead}
                disabled={unreadCount === 0}
              >
                {t('Mark all read')}
              </button>
              <button
                type="button"
                className="btn-link"
                onClick={clearHistory}
                disabled={notifications.length === 0}
              >
                {t('Clear')}
              </button>
            </div>
          </div>

          {visible.length > 0 ? (
            <ul className="notif-dropdown-list">
              {visible.map((n) => (
                <li key={n.id}>
                  <button
                    type="button"
                    className={`notif-dropdown-item${!n.read ? ' unread' : ''}`}
                    onClick={() => handleClick(n)}
                    role="menuitem"
                  >
                    <span className={`notif-severity-dot ${severityDotClass(n.severity)}`} />
                    <span className="notif-dropdown-item-body">
                      <span className="notif-dropdown-item-title">{n.title}</span>
                      <span className="notif-dropdown-item-message">{n.message}</span>
                    </span>
                    <span className="notif-dropdown-item-time" title={formatDateTime(n.timestampUtc)}>
                      {formatRelativeTime(n.timestampUtc)}
                    </span>
                  </button>
                </li>
              ))}
            </ul>
          ) : (
            <p className="notif-dropdown-empty">{t('No notifications yet.')}</p>
          )}
        </div>
      )}
    </div>
  );
}
