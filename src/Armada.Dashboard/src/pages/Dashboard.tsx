import { useEffect, useState, useCallback, useMemo } from 'react';
import { useNavigate } from 'react-router-dom';
import {
  getStatus,
  listMissions,
  listVessels,
  listCaptains,
  listSignals,
  deleteMission,
} from '../api/client';
import type { Mission, Vessel, Captain, Signal } from '../types/models';
import { useWebSocket } from '../context/WebSocketContext';
import type { WebSocketMessage } from '../types/models';
import StatusBadge from '../components/shared/StatusBadge';
import ActionMenu from '../components/shared/ActionMenu';
import { copyToClipboard } from '../components/shared/CopyButton';
import JsonViewer from '../components/shared/JsonViewer';
import FilterBar from '../components/shared/FilterBar';

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
  const [vessels, setVessels] = useState<Vessel[]>([]);
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
      const [statusRes, missionRes, vesselRes, captainRes] = await Promise.all([
        getStatus().catch(() => null),
        listMissions({ pageSize: 9999 }).catch(() => null),
        listVessels({ pageSize: 9999 }).catch(() => null),
        listCaptains({ pageSize: 9999 }).catch(() => null),
      ]);
      if (statusRes) setStatus(statusRes as unknown as StatusData);
      if (missionRes) {
        const sorted = [...missionRes.objects].sort(
          (a, b) => new Date(b.createdUtc).getTime() - new Date(a.createdUtc).getTime(),
        );
        setRecentMissions(sorted.slice(0, 10));
      }
      if (vesselRes) setVessels(vesselRes.objects);
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
          <button className="btn btn-sm refresh-btn" onClick={loadAll} title="Refresh all dashboard data">
            &#x21bb;
          </button>
        </div>
      </div>

      {error && <p className="text-error">{error}</p>}

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
                        <button
                          className="copy-btn"
                          onClick={(e) => {
                            e.stopPropagation();
                            copyId(vp.voyage?.id);
                          }}
                          title="Copy ID"
                        />
                      </div>
                    </td>
                    <td>
                      <StatusBadge status={vp.voyage?.status || ''} />
                    </td>
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
                        <button
                          className="copy-btn"
                          onClick={(e) => {
                            e.stopPropagation();
                            copyId(m.id);
                          }}
                          title="Copy ID"
                        />
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
                            label: 'View JSON',
                            onClick: () =>
                              setJsonViewer({ open: true, title: `Mission: ${m.title}`, data: m }),
                          },
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
