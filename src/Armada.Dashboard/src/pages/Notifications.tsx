import { useNavigate } from 'react-router-dom';
import { useNotifications, type Notification } from '../context/NotificationContext';

function formatTime(utc: string | null | undefined): string {
  if (!utc) return '-';
  const d = new Date(utc);
  const now = new Date();
  const diffMs = now.getTime() - d.getTime();
  const diffMin = Math.floor(diffMs / 60000);
  if (diffMin < 1) return 'just now';
  if (diffMin < 60) return `${diffMin}m ago`;
  const diffHr = Math.floor(diffMin / 60);
  if (diffHr < 24) return `${diffHr}h ago`;
  const diffDay = Math.floor(diffHr / 24);
  return `${diffDay}d ago`;
}

function formatTimeAbsolute(utc: string | null | undefined): string {
  if (!utc) return '';
  return new Date(utc).toLocaleString();
}

function severityDotClass(severity: Notification['severity']): string {
  switch (severity) {
    case 'error': return 'notif-error';
    case 'warning': return 'notif-warning';
    case 'success': return 'notif-success';
    default: return 'notif-info';
  }
}

export default function Notifications() {
  const navigate = useNavigate();
  const { notifications, unreadCount, markRead, markAllRead, clearHistory } = useNotifications();

  const handleClick = (n: Notification) => {
    markRead(n.id);
    if (n.missionId) navigate(`/missions/${n.missionId}`);
    else if (n.voyageId) navigate(`/voyages/${n.voyageId}`);
    else if (n.captainId) navigate(`/captains/${n.captainId}`);
  };

  return (
    <div>
      <div className="page-header">
        <div>
          <h2>Notifications</h2>
          <p className="text-muted">
            State change history for missions, voyages, and captains.
            {unreadCount > 0 && (
              <span style={{ marginLeft: '0.5rem', fontWeight: 600 }}>
                ({unreadCount} unread)
              </span>
            )}
          </p>
        </div>
        <div className="page-actions">
          <button
            className="btn-sm"
            onClick={markAllRead}
            disabled={unreadCount === 0}
            title="Mark all notifications as read"
          >
            Mark All Read
          </button>
          <button
            className="btn-sm btn-danger"
            onClick={clearHistory}
            disabled={notifications.length === 0}
            title="Clear all notification history"
          >
            Clear History
          </button>
        </div>
      </div>

      {notifications.length > 0 ? (
        <div className="table-wrap">
          <table className="table">
            <thead>
              <tr>
                <th style={{ width: '1.5rem', padding: '0.25rem' }}></th>
                <th>Title</th>
                <th>Message</th>
                <th>Time</th>
              </tr>
            </thead>
            <tbody>
              {notifications.map(ntf => (
                <tr
                  key={ntf.id}
                  className={`clickable${!ntf.read ? ' notif-unread' : ''}`}
                  onClick={() => handleClick(ntf)}
                >
                  <td>
                    <span className={`notif-severity-dot ${severityDotClass(ntf.severity)}`} />
                  </td>
                  <td style={!ntf.read ? { fontWeight: 600 } : undefined}>{ntf.title}</td>
                  <td>{ntf.message}</td>
                  <td
                    className="text-muted"
                    title={formatTimeAbsolute(ntf.timestampUtc)}
                    style={{ whiteSpace: 'nowrap' }}
                  >
                    {formatTime(ntf.timestampUtc)}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : (
        <p className="text-muted" style={{ padding: '2rem', textAlign: 'center' }}>
          No notifications yet. State changes for missions, voyages, and captains will appear here.
        </p>
      )}
    </div>
  );
}
