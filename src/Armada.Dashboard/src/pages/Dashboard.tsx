import { useEffect, useState, useCallback, useMemo } from 'react';
import { useNavigate } from 'react-router-dom';
import {
  getStatus,
  listMissions,
  listVessels,
  listCaptains,
  listSignals,
  listFleets,
  deleteMission,
  restartMission,
} from '../api/client';
import type { Mission, Vessel, Captain, Signal, Fleet } from '../types/models';
import { useWebSocket } from '../context/WebSocketContext';
import ErrorModal from '../components/shared/ErrorModal';
import type { WebSocketMessage } from '../types/models';
import StatusBadge from '../components/shared/StatusBadge';
import ActionMenu from '../components/shared/ActionMenu';
import RefreshButton from '../components/shared/RefreshButton';
import CopyButton, { copyToClipboard } from '../components/shared/CopyButton';
import JsonViewer from '../components/shared/JsonViewer';
import FilterBar from '../components/shared/FilterBar';
import MissionHistoryChart from '../components/MissionHistoryChart';

interface VoyageProgress {
  voyage: {
    id: string;
    title: string;
    status: string;
  };
  totalMissions: number;
  completedMissions: number;
  failedMissions: number;
}

interface StatusData {
  totalCaptains: number;
  idleCaptains: number;
  workingCaptains: number;
  stalledCaptains: number;
  activeVoyages: number;
  missionsByStatus: Record<string, number>;
  voyages: VoyageProgress[];
  recentSignals: Array<{
    id: string;
    type: string;
    payload?: string;
    message?: string;
    createdUtc: string;
  }>;
}

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

function voyagePercent(vp: VoyageProgress): number {
  if (!vp.totalMissions) return 0;
  return Math.round((vp.completedMissions / vp.totalMissions) * 100);
}

export default function Dashboard() {
  const { subscribe } = useWebSocket();
  const navigate = useNavigate();

  const [status, setStatus] = useState<StatusData | null>(null);
  const [recentMissions, setRecentMissions] = useState<Mission[]>([]);
  const [allMissions, setAllMissions] = useState<Mission[]>([]);
  const [vessels, setVessels] = useState<Vessel[]>([]);
  const [fleets, setFleets] = useState<Fleet[]>([]);
  const [captains, setCaptains] = useState<Captain[]>([]);
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(true);
  const [jsonViewer, setJsonViewer] = useState<{ open: boolean; title: string; data: unknown }>({
    open: false,
    title: '',
    data: null,
  });

  // Recent mission filters
  const [statusFilter, setStatusFilter] = useState('');
  const [vesselFilter, setVesselFilter] = useState('');
  const [captainFilter, setCaptainFilter] = useState('');

  const vesselName = useCallback(
    (id: string | null | undefined) => {
      if (!id) return '-';
      const v = vessels.find((x) => x.id === id);
      return v ? v.name : id.slice(0, 8);
    },
    [vessels],
  );

  const voyageVesselNames = useCallback(
    (voyageId: string | null | undefined) => {
      if (!voyageId) return '-';
      const vesselIds = [...new Set(recentMissions.filter(m => m.voyageId === voyageId).map(m => m.vesselId))];
      if (vesselIds.length === 0) return '-';
      return vesselIds.map(id => vesselName(id)).join(', ');
    },
    [recentMissions, vesselName],
  );

  const captainName = useCallback(
    (id: string | null | undefined) => {
      if (!id) return '-';
      const c = captains.find((x) => x.id === id);
      return c ? c.name : id.slice(0, 8);
    },
    [captains],
  );

  const loadAll = useCallback(async () => {
    try {
      const [statusRes, missionRes, vesselRes, captainRes, fleetRes] = await Promise.all([
        getStatus().catch(() => null),
        listMissions({ pageSize: 9999 }).catch(() => null),
        listVessels({ pageSize: 9999 }).catch(() => null),
        listCaptains({ pageSize: 9999 }).catch(() => null),
        listFleets({ pageSize: 9999 }).catch(() => null),
      ]);
      if (statusRes) setStatus(statusRes as unknown as StatusData);
      if (missionRes) {
        setAllMissions(missionRes.objects);
        const sorted = [...missionRes.objects].sort(
          (a, b) => new Date(b.createdUtc).getTime() - new Date(a.createdUtc).getTime(),
        );
        setRecentMissions(sorted.slice(0, 10));
      }
      if (vesselRes) setVessels(vesselRes.objects);
      if (fleetRes) setFleets(fleetRes.objects);
      if (captainRes) setCaptains(captainRes.objects);
      if (!statusRes && !missionRes) setError('Failed to load dashboard data.');
    } catch {
      setError('Failed to load dashboard data.');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    loadAll();
  }, [loadAll]);

  // Auto-refresh on WebSocket messages
  useEffect(() => {
    const unsubscribe = subscribe((_msg: WebSocketMessage) => {
      loadAll();
    });
    return unsubscribe;
  }, [subscribe, loadAll]);

  // Polling fallback: refresh every 30 seconds
  useEffect(() => {
    const timer = setInterval(loadAll, 30000);
    return () => clearInterval(timer);
  }, [loadAll]);

  // Compute alerts from status data
  const alerts = useMemo(() => {
    if (!status) return [];
    const result: Array<{ level: 'error' | 'warning'; message: string; action?: string; link?: string }> = [];
    const ms = status.missionsByStatus || {};
    const stalledCount = status.stalledCaptains ?? 0;
    const failedCount = (ms['Failed'] ?? 0);
    const landingFailedCount = (ms['LandingFailed'] ?? 0);
    const pendingCount = (ms['Pending'] ?? 0);
    const idleCount = status.idleCaptains ?? 0;
    const workingCount = status.workingCaptains ?? 0;
    const totalCaptains = status.totalCaptains ?? 0;

    if (stalledCount > 0) {
      result.push({
        level: 'error',
        message: `${stalledCount} captain(s) stalled -- recovery attempts exhausted.`,
        action: 'Stop and restart stalled captains to resume work.',
        link: '/captains',
      });
    }

    if (failedCount > 0) {
      result.push({
        level: 'warning',
        message: `${failedCount} mission(s) failed.`,
        action: 'Review and restart failed missions.',
        link: '/missions',
      });
    }

    if (landingFailedCount > 0) {
      result.push({
        level: 'warning',
        message: `${landingFailedCount} mission(s) failed to land -- work was produced but could not be merged.`,
        action: 'Retry landing or restart these missions.',
        link: '/missions',
      });
    }

    if (pendingCount > 0 && idleCount > 0 && workingCount === 0) {
      result.push({
        level: 'warning',
        message: `${pendingCount} pending mission(s) but no captains are working. ${idleCount} captain(s) idle.`,
        action: 'Vessels may have concurrent mission limits blocking dispatch, or missions may be assigned to a vessel with an active mission.',
      });
    }

    if (totalCaptains === 0 && pendingCount > 0) {
      result.push({
        level: 'error',
        message: `${pendingCount} pending mission(s) but no captains exist.`,
        action: 'Create a captain to start processing missions.',
        link: '/captains',
      });
    }

    return result;
  }, [status]);

  const totalMissions = useMemo(() => {
    if (!status?.missionsByStatus) return 0;
    return Object.values(status.missionsByStatus).reduce((sum, n) => sum + n, 0);
  }, [status]);

  const filteredRecentMissions = useMemo(() => {
    return recentMissions.filter((m) => {
      if (statusFilter && m.status !== statusFilter) return false;
      if (vesselFilter && m.vesselId !== vesselFilter) return false;
      if (captainFilter && m.captainId !== captainFilter) return false;
      return true;
    });
  }, [recentMissions, statusFilter, vesselFilter, captainFilter]);

  const handleDeleteMission = async (id: string) => {
    if (!window.confirm('Delete this mission?')) return;
    try {
      await deleteMission(id);
      loadAll();
    } catch {
      setError('Failed to delete mission.');
    }
  };

  const copyId = (id: string) => {
    copyToClipboard(id);
  };

  const missionStatuses = [
    'Pending',
    'Assigned',
    'InProgress',
    'Testing',
    'Review',
    'Complete',
    'Failed',
    'Cancelled',
  ];

  if (loading) {
    return (
      <div>
        <h2>System Status</h2>
        <p className="text-dim">Loading dashboard...</p>
      </div>
    );
  }

  return (
    <div>
      <div className="view-header">
        <div>
          <h2>System Status</h2>
          <p className="text-dim view-subtitle">
            Overview of fleet health, active missions, and recent activity.
          </p>
        </div>
        <div className="view-actions">
          <button className="btn btn-primary btn-sm" onClick={() => navigate('/dispatch')} title="Smart dispatch with NLP task parsing">
            + Dispatch
          </button>
          <button className="btn btn-primary btn-sm" onClick={() => navigate('/voyages/create')} title="Create a new voyage with multiple missions">
            + Voyage
          </button>
          <RefreshButton onRefresh={loadAll} title="Refresh all dashboard data" />
        </div>
      </div>

      <ErrorModal error={error} onClose={() => setError('')} />

      {/* Alert Banners */}
      {alerts.length > 0 && (
        <div className="dashboard-alerts">
          {alerts.map((alert, i) => (
            <div key={i} className={`dashboard-alert dashboard-alert-${alert.level}`}>
              <div className="dashboard-alert-content">
                <strong>{alert.message}</strong>
                {alert.action && <span className="dashboard-alert-action"> {alert.action}</span>}
              </div>
              {alert.link && (
                <button className="btn btn-sm" onClick={() => navigate(alert.link!)}>View</button>
              )}
            </div>
          ))}
        </div>
      )}

      {/* KPI Cards */}
      <div className="cards">
        <div
          className="card clickable"
          onClick={() => navigate('/captains')}
          title="Click to view all captains"
        >
          <div className="card-label">Captains</div>
          <div className="card-value">{status?.totalCaptains ?? 0}</div>
          <div className="card-detail">
            <span className="tag idle">{status?.idleCaptains ?? 0} idle</span>
            <span className="tag working">{status?.workingCaptains ?? 0} working</span>
            {(status?.stalledCaptains ?? 0) > 0 && (
              <span className="tag stalled">{status?.stalledCaptains ?? 0} stalled</span>
            )}
          </div>
        </div>

        <div
          className="card clickable"
          onClick={() => navigate('/voyages')}
          title="Click to view all voyages"
        >
          <div className="card-label">Active Voyages</div>
          <div className="card-value">{status?.activeVoyages ?? 0}</div>
        </div>

        <div
          className="card clickable"
          onClick={() => navigate('/missions')}
          title="Click to view all missions"
        >
          <div className="card-label">Missions</div>
          <div className="card-value">{totalMissions}</div>
          <div className="card-detail">
            {status?.missionsByStatus &&
              Object.entries(status.missionsByStatus).map(([key, val]) => (
                <span key={key} className={`tag ${key.toLowerCase()}`}>
                  {val} {key}
                </span>
              ))}
          </div>
        </div>
      </div>

      {/* Mission History Chart */}
      <MissionHistoryChart missions={allMissions} vessels={vessels} fleets={fleets} onRefresh={loadAll} />

      {/* Voyage Progress */}
      {status?.voyages && status.voyages.length > 0 && (
        <div style={{ marginTop: '1.5rem' }}>
          <h3>Voyage Progress</h3>
          <div className="table-wrap">
            <table className="table">
              <thead>
                <tr>
                  <th title="Voyage name and unique identifier">Voyage</th>
                  <th title="Current voyage lifecycle state">Status</th>
                  <th title="Vessel(s) targeted by this voyage's missions">Vessel</th>
                  <th title="Percentage of missions completed">Progress</th>
                  <th title="Completed vs total missions in this voyage">Missions</th>
                  <th></th>
                </tr>
              </thead>
              <tbody>
                {status.voyages.map((vp) => (
                  <tr
                    key={vp.voyage?.id}
                    className="clickable"
                    onClick={() => navigate(`/voyages/${vp.voyage?.id}`)}
                  >
                    <td>
                      <strong>{vp.voyage?.title || vp.voyage?.id}</strong>
                      <div className="text-dim id-display">
                        <span className="mono">{vp.voyage?.id}</span>
                        <CopyButton text={vp.voyage?.id || ''} onClick={e => e.stopPropagation()} />
                      </div>
                    </td>
                    <td>
                      <StatusBadge status={vp.voyage?.status || ''} />
                    </td>
                    <td className="text-dim">{voyageVesselNames(vp.voyage?.id)}</td>
                    <td>
                      <div className="progress-bar">
                        <div
                          className="progress-fill"
                          style={{ width: `${voyagePercent(vp)}%` }}
                        />
                      </div>
                      <span className="text-dim">{voyagePercent(vp)}%</span>
                    </td>
                    <td>
                      <span>
                        {vp.completedMissions}/{vp.totalMissions} done
                      </span>
                      {vp.failedMissions > 0 && (
                        <span className="text-dim">, {vp.failedMissions} failed</span>
                      )}
                    </td>
                    <td onClick={(e) => e.stopPropagation()}>
                      <ActionMenu
                        id={`voyage-action-${vp.voyage?.id}`}
                        items={[
                          {
                            label: 'View JSON',
                            onClick: () =>
                              setJsonViewer({
                                open: true,
                                title: `Voyage: ${vp.voyage?.title || vp.voyage?.id}`,
                                data: vp.voyage,
                              }),
                          },
                        ]}
                      />
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {/* Recent Missions */}
      <div style={{ marginTop: '1.5rem' }}>
        <div className="view-header">
          <h3>Recent Missions</h3>
          <div className="view-actions filter-bar">
            <FilterBar
              filters={[
                {
                  key: 'status',
                  label: 'All Statuses',
                  type: 'select',
                  options: missionStatuses.map((s) => ({ value: s, label: s })),
                },
                {
                  key: 'vessel',
                  label: 'All Vessels',
                  type: 'select',
                  options: vessels.map((v) => ({ value: v.id, label: v.name })),
                },
                {
                  key: 'captain',
                  label: 'All Captains',
                  type: 'select',
                  options: captains.map((c) => ({ value: c.id, label: c.name })),
                },
              ]}
              values={{ status: statusFilter, vessel: vesselFilter, captain: captainFilter }}
              onChange={(key, value) => {
                if (key === 'status') setStatusFilter(value);
                else if (key === 'vessel') setVesselFilter(value);
                else if (key === 'captain') setCaptainFilter(value);
              }}
            />
            <button
              className="btn btn-sm"
              onClick={() => navigate('/missions')}
              title="Go to full missions list"
            >
              View All &rarr;
            </button>
          </div>
        </div>

        {filteredRecentMissions.length > 0 ? (
          <div className="table-wrap">
            <table className="table mission-table">
              <thead>
                <tr>
                  <th title="Mission title">Mission</th>
                  <th title="Mission ID">ID</th>
                  <th title="Current mission lifecycle state">Status</th>
                  <th title="Target repository for this mission">Vessel</th>
                  <th title="AI captain assigned to execute this mission">Captain</th>
                  <th title="When this mission was created">Created</th>
                  <th></th>
                </tr>
              </thead>
              <tbody>
                {filteredRecentMissions.map((m) => (
                  <tr
                    key={m.id}
                    className="clickable"
                    onClick={() => navigate(`/missions/${m.id}`)}
                  >
                    <td className="truncate-cell" title={m.title}>
                      <strong>{m.title}</strong>
                    </td>
                    <td className="mono text-dim">
                      <span className="id-display">
                        <span>{m.id}</span>
                        <CopyButton text={m.id} onClick={e => e.stopPropagation()} />
                      </span>
                    </td>
                    <td>
                      <StatusBadge status={m.status} />
                    </td>
                    <td>{vesselName(m.vesselId)}</td>
                    <td>{captainName(m.captainId)}</td>
                    <td className="text-dim" title={formatTimeAbsolute(m.createdUtc)}>
                      {formatTime(m.createdUtc)}
                    </td>
                    <td onClick={(e) => e.stopPropagation()}>
                      <ActionMenu
                        id={`mission-action-${m.id}`}
                        items={[
                          {
                            label: 'View Detail',
                            onClick: () => navigate(`/missions/${m.id}`),
                          },
                          {
                            label: 'View JSON',
                            onClick: () =>
                              setJsonViewer({ open: true, title: `Mission: ${m.title}`, data: m }),
                          },
                          ...(m.status === 'Failed' || m.status === 'Cancelled' || m.status === 'LandingFailed' ? [{
                            label: 'Restart',
                            onClick: async () => { try { await restartMission(m.id); loadAll(); } catch { /* ignore */ } },
                          }] : []),
                          {
                            label: 'Delete',
                            danger: true,
                            onClick: () => handleDeleteMission(m.id),
                          },
                        ]}
                      />
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : (
          <p className="text-dim">No missions found.</p>
        )}
      </div>

      {/* Recent Signals */}
      {status?.recentSignals && status.recentSignals.length > 0 && (
        <div style={{ marginTop: '1.5rem' }}>
          <h3>Recent Signals</h3>
          <div className="signal-list">
            {status.recentSignals.slice(0, 5).map((sig) => (
              <div key={sig.id} className="signal-item">
                <StatusBadge status={sig.type || ''} />
                <span>{sig.payload || sig.message}</span>
                <span className="text-dim" title={formatTimeAbsolute(sig.createdUtc)}>
                  {formatTime(sig.createdUtc)}
                </span>
              </div>
            ))}
          </div>
        </div>
      )}

      {/* JSON Viewer Modal */}
      {jsonViewer.open && (
        <JsonViewer
          open={jsonViewer.open}
          title={jsonViewer.title}
          data={jsonViewer.data}
          onClose={() => setJsonViewer({ open: false, title: '', data: null })}
        />
      )}
    </div>
  );
}
