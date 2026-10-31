import { useEffect, useState, useCallback, useMemo, useRef } from 'react';
import { useNavigate } from 'react-router-dom';
import {
  getStatus,
  listMissionSummaries,
  listVessels,
  listCaptains,
  listSignals,
  listFleets,
  deleteMission,
  restartMission,
  enumerateFleetActionRuns,
  enumerateVoyageProgress,
} from '../api/client';
import { listAll } from '../api/paging';
import { useAuth } from '../context/AuthContext';
import type { MissionSummary, Vessel, Captain, Signal, Fleet } from '../types/models';
import { useWebSocket } from '../context/WebSocketContext';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import PageHeader from '../components/shared/PageHeader';
import ErrorModal from '../components/shared/ErrorModal';
import type { WebSocketMessage } from '../types/models';
import StatusBadge from '../components/shared/StatusBadge';
import ActionMenu from '../components/shared/ActionMenu';
import RefreshButton from '../components/shared/RefreshButton';
import AutoRefreshSelect from '../components/shared/AutoRefreshSelect';
import { useAutoRefresh } from '../lib/useAutoRefresh';
import CopyButton, { copyToClipboard } from '../components/shared/CopyButton';
import JsonViewer from '../components/shared/JsonViewer';
import DataTable from '../components/shared/DataTable';
import FilterBar from '../components/shared/FilterBar';
import MissionHistoryChart from '../components/MissionHistoryChart';
import HealthKpiCards from '../components/vessels/health/HealthKpiCards';
import { useLocale } from '../context/LocaleContext';
import {
  activeFleetActionRunsLink,
  canRestartFromHome,
  dashboardAlerts,
  HOME_VOYAGE_PAGE_SIZE,
  filterRecentMissions,
  RECENT_MISSION_STATUSES,
  totalMissionCount,
  voyagePercent,
  type DashboardStatusData,
  type VoyageProgress,
} from '../lib/dashboardStatus';

export default function Dashboard() {
  const { subscribe } = useWebSocket();
  const navigate = useNavigate();
  const { t, formatRelativeTime, formatDateTime } = useLocale();
  const openSetupWizard = useCallback(() => {
    window.dispatchEvent(new CustomEvent('armada:open-setup-wizard'));
  }, []);

  const { isTenantAdmin } = useAuth();
  const [activeRuns, setActiveRuns] = useState<{ pending: number; running: number } | null>(null);
  const [status, setStatus] = useState<DashboardStatusData | null>(null);
  // One page of the active voyages (InProgress, then Open) with progress, and the total across every page.
  const [activeVoyages, setActiveVoyages] = useState<{ rows: VoyageProgress[]; total: number }>({ rows: [], total: 0 });
  const [recentMissions, setRecentMissions] = useState<MissionSummary[]>([]);
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
  const [confirm, setConfirm] = useState<{
    open: boolean;
    title: string;
    message: string;
    onConfirm: () => void;
  }>({
    open: false,
    title: '',
    message: '',
    onConfirm: () => undefined,
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
    (vp: VoyageProgress) => {
      if (!vp.vesselId) return '-';
      const more = (vp.vesselCount ?? 1) - 1;
      return more > 0 ? `${vesselName(vp.vesselId)} +${more}` : vesselName(vp.vesselId);
    },
    [vesselName],
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
      // The Recent Missions filters go to the server, so a vessel or captain filter finds that vessel's or
      // captain's ten most recent missions instead of filtering only the ten most recent overall.
      const [statusRes, missionRes, pendingRuns, runningRuns, voyageRes] = await Promise.all([
        getStatus().catch(() => null),
        listMissionSummaries({ pageSize: 10, filters: { status: statusFilter, vesselId: vesselFilter, captainId: captainFilter } }).catch(() => null),
        enumerateFleetActionRuns({ status: 'Pending', pageSize: 1 }).catch(() => null),
        enumerateFleetActionRuns({ status: 'Running', pageSize: 1 }).catch(() => null),
        enumerateVoyageProgress({ pageSize: HOME_VOYAGE_PAGE_SIZE }).catch(() => null),
      ]);
      if (voyageRes) setActiveVoyages({ rows: (voyageRes.objects || []) as unknown as VoyageProgress[], total: voyageRes.totalRecords });
      setActiveRuns(pendingRuns && runningRuns ? { pending: pendingRuns.totalRecords, running: runningRuns.totalRecords } : null);
      if (statusRes) setStatus(statusRes as unknown as DashboardStatusData);
      if (missionRes) {
        setRecentMissions(missionRes.objects || []);
      }
      if (!statusRes && !missionRes) setError(t('Failed to load dashboard data.'));
    } catch {
      setError(t('Failed to load dashboard data.'));
    } finally {
      setLoading(false);
    }
  }, [t, statusFilter, vesselFilter, captainFilter]);

  const loadAllRef = useRef(loadAll);
  loadAllRef.current = loadAll;
  const homeLoadRef = useRef<Promise<void> | null>(null);
  const homeLoadPendingRef = useRef(false);
  const homeLoadStartedRef = useRef(false);
  const [historyRefresh, setHistoryRefresh] = useState(0);

  // One home load at a time. A request that arrives while a load is running schedules one follow-up.
  // The first load does not bump the history signal; the chart fetches on its own mount.
  const refreshHome = useCallback((): Promise<void> => {
    if (homeLoadRef.current) {
      homeLoadPendingRef.current = true;
      return homeLoadRef.current;
    }

    const run = async () => {
      do {
        homeLoadPendingRef.current = false;
        if (homeLoadStartedRef.current) setHistoryRefresh((value) => value + 1);
        else homeLoadStartedRef.current = true;
        await loadAllRef.current();
      } while (homeLoadPendingRef.current);
    };

    const promise = run().finally(() => {
      if (homeLoadRef.current === promise) homeLoadRef.current = null;
    });
    homeLoadRef.current = promise;
    return promise;
  }, []);

  // Name maps and filter pickers: every vessel, captain, and fleet (all pages). Loaded on mount and with the
  // 30 second poll rather than on every WebSocket message, since a large tenant needs several pages.
  const loadReference = useCallback(async () => {
    const [vesselRes, captainRes, fleetRes] = await Promise.all([
      listAll(listVessels).catch(() => null),
      listAll(listCaptains).catch(() => null),
      listAll(listFleets).catch(() => null),
    ]);
    if (vesselRes) setVessels(vesselRes.objects);
    if (fleetRes) setFleets(fleetRes.objects);
    if (captainRes) setCaptains(captainRes.objects);
  }, []);

  useEffect(() => {
    void refreshHome();
  }, [loadAll, refreshHome]);

  useEffect(() => {
    void loadReference();
    const timer = setInterval(() => { void loadReference(); }, 30000);
    return () => clearInterval(timer);
  }, [loadReference]);

  // Auto-refresh on WebSocket messages
  useEffect(() => {
    return subscribe((_msg: WebSocketMessage) => {
      void refreshHome();
    });
  }, [subscribe, refreshHome]);

  const { seconds: refreshSeconds, setSeconds: setRefreshSeconds } = useAutoRefresh('dashboard', refreshHome);

  // Compute alerts from status data
  const alerts = useMemo(() => dashboardAlerts(status), [status]);

  const totalMissions = useMemo(() => totalMissionCount(status), [status]);

  const filteredRecentMissions = useMemo(
    () => filterRecentMissions(recentMissions, { status: statusFilter, vesselId: vesselFilter, captainId: captainFilter }),
    [recentMissions, statusFilter, vesselFilter, captainFilter],
  );

  const handleDeleteMission = async (id: string) => {
    try {
      await deleteMission(id);
      void refreshHome();
    } catch {
      setError(t('Failed to delete mission.'));
    }
  };

  const copyId = (id: string) => {
    copyToClipboard(id);
  };

  const missionStatuses = RECENT_MISSION_STATUSES;

  if (loading) {
    return (
      <div>
        <h2>{t('System Status')}</h2>
        <p className="text-dim">{t('Loading dashboard...')}</p>
      </div>
    );
  }

  return (
    <div>
      <PageHeader
        title={t('System Status')}
        subtitle={t('Overview of fleet health, active missions, and recent activity.')}
        actions={(
          <>
            <button
              className="btn btn-sm"
              onClick={openSetupWizard}
              title={t('Open the setup wizard: connect a repository, add a captain and send a first mission, or see what to set up next.')}
              aria-label={t('Open Setup Wizard')}
              style={{ display: 'inline-flex', alignItems: 'center', gap: '0.4rem' }}
            >
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
                <path d="m12 3-1.9 4.8L5 9.7l4.1 2.1L12 17l2.9-5.2L19 9.7l-5.1-1.9Z" />
                <path d="M5 3v4" />
                <path d="M3 5h4" />
                <path d="M19 16v5" />
                <path d="M16.5 18.5h5" />
              </svg>
              <span>{t('Setup Wizard')}</span>
            </button>
            <button className="btn btn-primary btn-sm" onClick={() => navigate('/dispatch')} title={t('Open Dispatch to describe work in plain language and send it to a vessel as a voyage.')}>
              + {t('Dispatch')}
            </button>
            <button className="btn btn-primary btn-sm" onClick={() => navigate('/voyages/create')} title={t('Create a voyage by hand: a named batch of missions for one vessel, each with its own title and instructions.')}>
              + {t('Voyage')}
            </button>
            <AutoRefreshSelect seconds={refreshSeconds} onChange={setRefreshSeconds} />
            <RefreshButton onRefresh={refreshHome} title="Reload every panel on this page from the server now." />
          </>
        )}
      />

      {/* Ask Armada lead band -- the primary workflow interface */}
      <div className="ask-hero">
        <div className="ask-hero-copy">
          <h2 className="ask-hero-title">{t('Ask Armada')}</h2>
          <p className="ask-hero-sub">{t('Ask about fleet state in plain language and dispatch work straight from the conversation.')}</p>
        </div>
        <div className="ask-hero-actions">
          <button className="btn btn-primary" onClick={() => navigate('/ask')} title={t('Open Ask Armada to ask questions about your fleet in plain language and dispatch work from the conversation.')}>
            {t('Ask Armada')} &rarr;
          </button>
          <button className="btn btn-sm" onClick={() => navigate('/inbox')} title={t('Open Needs You: reviews, approvals, failed missions and stalled captains waiting on you.')}>
            {t('Needs You')}
          </button>
          <button className="btn btn-sm" onClick={() => navigate('/dispatch')} title={t('Open Dispatch to describe work and send it to a vessel.')}>
            {t('Dispatch')}
          </button>
          <button className="btn btn-sm" onClick={() => navigate('/server?tab=diagnostics')} title={t('Open the Server page diagnostics tab to check the Admiral\'s health and track down problems.')}>
            {t('Diagnostics')}
          </button>
        </div>
      </div>

      <ErrorModal error={error} onClose={() => setError('')} />
      <ConfirmDialog
        open={confirm.open}
        title={confirm.title}
        message={confirm.message}
        confirmLabel={t('Delete')}
        cancelLabel={t('Cancel')}
        danger
        onConfirm={confirm.onConfirm}
        onCancel={() => setConfirm((current) => ({ ...current, open: false }))}
      />

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
                <button className="btn btn-sm" onClick={() => navigate(alert.link!)} title={t('Open the page where you can look into this alert.')}>{t('View')}</button>
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
          title={t('Agents registered with Armada and what they are doing now. Stalled captains have stopped reporting progress. Click to open Captains.')}
        >
          <div className="card-label">{t('Captains')}</div>
          <div className="card-value">{status?.totalCaptains ?? 0}</div>
          <div className="card-detail">
            <span className="tag idle" title={t('Captains free to take a mission.')}>{status?.idleCaptains ?? 0} idle</span>
            <span className="tag working" title={t('Captains running a mission or session right now.')}>{status?.workingCaptains ?? 0} working</span>
            {(status?.stalledCaptains ?? 0) > 0 && (
              <span className="tag stalled" title={t('Captains that stopped reporting progress. Open Captains to recover or stop them.')}>{status?.stalledCaptains ?? 0} stalled</span>
            )}
          </div>
        </div>

        <div
          className="card clickable"
          onClick={() => navigate('/missions?tab=voyages')}
          title={t('Voyages that still have missions open or running. Click to open Voyages.')}
        >
          <div className="card-label">{t('Active Voyages')}</div>
          <div className="card-value">{status?.activeVoyages ?? 0}</div>
          {(status?.memoryPressureDeferrals ?? 0) > 0 && (
            <div className="card-detail">
              <span className="tag stalled" title={t('Times dispatch held back a captain launch because the host was low on memory. A high number means the machine is overloaded.')}>{t('{{count}} deferred for memory pressure', { count: status?.memoryPressureDeferrals ?? 0 })}</span>
            </div>
          )}
        </div>

        <div
          className="card clickable"
          onClick={() => navigate('/missions')}
          title={t('All missions, with a count for each status. Click to open Missions.')}
        >
          <div className="card-label">{t('Missions')}</div>
          <div className="card-value">{totalMissions}</div>
          <div className="card-detail">
            {status?.missionsByStatus &&
              Object.entries(status.missionsByStatus).map(([key, val]) => (
                <span key={key} className={`tag ${key.toLowerCase()}`} title={t('Missions currently in the {{status}} status.', { status: key })}>
                  {val} {key}
                </span>
              ))}
          </div>
        </div>
        <div
          className="card clickable"
          onClick={() => navigate(activeFleetActionRunsLink(activeRuns))}
          title={t('Fleet action runs (one command or mission across many vessels) that are pending or running. Click to open them.')}
        >
          <div className="card-label">{t('Active fleet action runs')}</div>
          <div className="card-value">{activeRuns ? (activeRuns.pending + activeRuns.running).toLocaleString() : '-'}</div>
          {activeRuns && (
            <div className="card-detail">
              <button type="button" className="tag working card-tag-link" onClick={(e) => { e.stopPropagation(); navigate('/fleet-actions?tab=runs&status=Running'); }} title={t('Open the fleet action runs that are running now.')}>
                {t('{{count}} running', { count: activeRuns.running.toLocaleString() })}
              </button>
              <button type="button" className="tag pending card-tag-link" onClick={(e) => { e.stopPropagation(); navigate('/fleet-actions?tab=runs&status=Pending'); }} title={t('Open the fleet action runs waiting to start.')}>
                {t('{{count}} pending', { count: activeRuns.pending.toLocaleString() })}
              </button>
            </div>
          )}
        </div>
      </div>

      {/* Bulk onboarding and fleet action shortcuts */}
      {isTenantAdmin && (
        <div className="cta-cards">
          <button type="button" className="cta-card" onClick={() => navigate('/vessels/import')} title={t('Scan a folder for git repositories and add the ones you pick as vessels in one step.')}>
            <span className="cta-card-icon" aria-hidden="true">
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M12 3v12" /><path d="m7 10 5 5 5-5" /><path d="M5 21h14" /></svg>
            </span>
            <span className="cta-card-text">
              <span className="cta-card-title">{t('Import repositories')}</span>
              <span className="cta-card-sub text-dim">{t('Discover local git repositories and onboard them as vessels in bulk.')}</span>
            </span>
          </button>
          <button type="button" className="cta-card" onClick={() => navigate('/fleet-actions?tab=actions&run=new')} title={t('Start a fleet action: run one shell command or captain mission on many vessels at once.')}>
            <span className="cta-card-icon" aria-hidden="true">
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M13 2 4 14h7l-1 8 9-12h-7l1-8z" /></svg>
            </span>
            <span className="cta-card-text">
              <span className="cta-card-title">{t('Run fleet action')}</span>
              <span className="cta-card-sub text-dim">{t('Run a command or a captain mission across many vessels at once.')}</span>
            </span>
          </button>
        </div>
      )}
      {/* Vessel health KPI tiles (deep-link into /vessels/health) */}
      <HealthKpiCards />

      {/* Mission History Chart */}
      <MissionHistoryChart vessels={vessels} fleets={fleets} onRefresh={refreshHome} refreshSignal={historyRefresh} />

      {/* Voyage Progress */}
      {activeVoyages.rows.length > 0 && (
        <div style={{ marginTop: '1.5rem' }}>
          <h3 title={t('Open voyages and how many of their missions have finished. Click a row to open the voyage.')}>{t('Voyage Progress')}</h3>
          <DataTable
            tableKey="dashboard-voyage-progress"
            className="table"
            rows={activeVoyages.rows}
            rowKey={(vp) => vp.voyage?.id ?? ''}
            recordCount={activeVoyages.total}
            onRowClick={(vp) => navigate(`/voyages/${vp.voyage?.id}`)}
            columns={[
              {
                key: 'id', label: t('ID'), required: true, headerTitle: t('Unique voyage ID (vyg_ prefix).'), cellClassName: 'mono text-dim table-id-cell',
                render: (vp) => (
                  <span className="id-display">
                    <span className="id-value" title={vp.voyage?.id}>{vp.voyage?.id}</span>
                    <CopyButton text={vp.voyage?.id || ''} onClick={e => e.stopPropagation()} />
                  </span>
                ),
              },
              {
                key: 'voyage', label: t('Voyage'), required: true, headerTitle: t('Name of the voyage. Click a row to open it.'), cellClassName: 'cell-title',
                cellTitle: (vp) => vp.voyage?.title || vp.voyage?.id,
                render: (vp) => <strong className="line-clamp-2">{vp.voyage?.title || vp.voyage?.id}</strong>,
              },
              { key: 'status', label: t('Status'), headerTitle: t('Open while missions are being set up, InProgress while any run, then Complete, Failed or Cancelled.'), cellClassName: 'cell-nowrap', render: (vp) => <StatusBadge status={vp.voyage?.status || ''} /> },
              {
                key: 'vessel', label: t('Vessel'), headerTitle: t('Repositories this voyage\'s missions work in.'), cellClassName: 'text-dim',
                render: (vp) => <span className="cell-one-line" title={voyageVesselNames(vp)}>{voyageVesselNames(vp)}</span>,
              },
              {
                key: 'progress', label: t('Progress'), headerTitle: t('Share of the voyage\'s missions that finished successfully.'),
                // Bar and percentage on one line.
                render: (vp) => (
                  <span className="dashboard-progress-cell">
                    <span className="progress-bar">
                      <span className="progress-fill" style={{ width: `${voyagePercent(vp)}%` }} />
                    </span>
                    <span className="text-dim nowrap">{voyagePercent(vp)}%</span>
                  </span>
                ),
              },
              {
                key: 'missions', label: t('Missions'), headerTitle: t('Missions done out of the total, plus how many failed.'), cellClassName: 'cell-nowrap',
                render: (vp) => (
                  <>
                    <span>{vp.completedMissions}/{vp.totalMissions} {t('done')}</span>
                    {vp.failedMissions > 0 && <span className="text-dim">, {vp.failedMissions} {t('failed')}</span>}
                  </>
                ),
              },
              {
                key: 'actions', label: t('Actions'), fixed: true, interactive: true, header: <span className="sr-only">{t('Actions')}</span>,
                render: (vp) => (
                  <ActionMenu
                    id={`voyage-action-${vp.voyage?.id}`}
                    items={[
                      {
                        label: 'View JSON',
                        title: 'Show the raw voyage record as the API returns it.',
                        onClick: () =>
                          setJsonViewer({
                            open: true,
                            title: `${t('Voyage')}: ${vp.voyage?.title || vp.voyage?.id}`,
                            data: vp.voyage,
                          }),
                      },
                    ]}
                  />
                ),
              },
            ]}
          />
        </div>
      )}

      {/* Recent Missions */}
      <div style={{ marginTop: '1.5rem' }}>
        <div className="view-header">
          <h3 title={t('The newest missions across all vessels. Use the filters to narrow them, or View All for the full list.')}>{t('Recent Missions')}</h3>
          <div className="view-actions filter-bar">
            <FilterBar
              filters={[
                {
                  key: 'status',
                  label: t('All Statuses'),
                  type: 'select',
                  options: missionStatuses.map((s) => ({ value: s, label: t(s) })),
                },
                {
                  key: 'vessel',
                  label: t('All Vessels'),
                  type: 'select',
                  options: vessels.map((v) => ({ value: v.id, label: v.name })),
                },
                {
                  key: 'captain',
                  label: t('All Captains'),
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
              title={t('Open the Missions page with every mission, paging and bulk actions.')}
            >
              {t('View All')} &rarr;
            </button>
          </div>
        </div>

        {filteredRecentMissions.length > 0 ? (
          <DataTable
            tableKey="dashboard-recent-missions"
            className="table"
            rows={filteredRecentMissions}
            rowKey={(m) => m.id}
            recordCount={null}
            onRowClick={(m) => navigate(`/missions/${m.id}`)}
            columns={[
              {
                key: 'id', label: t('ID'), required: true, headerTitle: t('Unique mission ID (msn_ prefix).'), cellClassName: 'mono text-dim table-id-cell',
                render: (m) => (
                  <span className="id-display">
                    <span className="id-value" title={m.id}>{m.id}</span>
                    <CopyButton text={m.id} onClick={e => e.stopPropagation()} />
                  </span>
                ),
              },
              {
                key: 'mission', label: t('Mission'), required: true, headerTitle: t('Mission title. Click a row to open the mission.'), cellClassName: 'cell-title',
                cellTitle: (m) => m.title,
                render: (m) => <strong className="line-clamp-2">{m.title}</strong>,
              },
              { key: 'status', label: t('Status'), headerTitle: t('Lifecycle state: Pending waits for a captain, InProgress is running, WorkProduced awaits landing, then Complete, Failed or Cancelled.'), cellClassName: 'cell-nowrap', render: (m) => <StatusBadge status={m.status} /> },
              { key: 'vessel', label: t('Vessel'), headerTitle: t('Repository the mission works in.'), render: (m) => vesselName(m.vesselId) },
              { key: 'captain', label: t('Captain'), headerTitle: t('Agent that ran or is running the mission. Shows - until dispatch assigns one.'), render: (m) => captainName(m.captainId) },
              {
                key: 'created', label: t('Created'), headerTitle: t('When the mission was created. Hover a cell for the exact time.'), cellClassName: 'text-dim cell-nowrap',
                cellTitle: (m) => formatDateTime(m.createdUtc),
                render: (m) => formatRelativeTime(m.createdUtc),
              },
              {
                key: 'actions', label: t('Actions'), fixed: true, interactive: true, header: <span className="sr-only">{t('Actions')}</span>,
                render: (m) => (
                  <ActionMenu
                    id={`mission-action-${m.id}`}
                    items={[
                      { label: 'Duplicate', title: 'Edit a fresh mission before saving.', onClick: () => navigate(`/missions?duplicate=${encodeURIComponent(m.id)}`) },
                      {
                        label: 'View Detail',
                        title: 'Open the mission page with its description, captain activity, diff and log.',
                        onClick: () => navigate(`/missions/${m.id}`),
                      },
                      {
                        label: 'View JSON',
                        title: 'Show the raw mission record as the API returns it.',
                        onClick: () =>
                          setJsonViewer({ open: true, title: `${t('Mission')}: ${m.title}`, data: m }),
                      },
                      ...(m.status === 'Pending' ? [{
                        label: 'Start',
                        title: 'Start this Pending mission now. A free captain takes it at once; if none is free it stays Pending until one is.',
                        onClick: async () => { try { await restartMission(m.id); loadAll(); } catch { /* ignore */ } },
                      }] : canRestartFromHome(m.status) ? [{
                        label: 'Restart',
                        title: 'Reset a Failed or Cancelled mission to Pending so it is dispatched again. Clears its captain, branch, pull request and timing.',
                        onClick: async () => { try { await restartMission(m.id); void refreshHome(); } catch { /* ignore */ } },
                      }] : []),
                      {
                        label: 'Delete',
                        danger: true,
                        title: 'Cancel the mission: its status becomes Cancelled and the record is kept. Use Purge on the mission page to remove it.',
                        onClick: () => setConfirm({
                          open: true,
                          title: t('Delete Mission'),
                          message: t('Delete this mission?'),
                          onConfirm: async () => {
                            setConfirm((current) => ({ ...current, open: false }));
                            await handleDeleteMission(m.id);
                          },
                        }),
                      },
                    ]}
                  />
                ),
              },
            ]}
          />
        ) : (
          <p className="text-dim">{t('No missions found.')}</p>
        )}
      </div>

      {/* Recent Signals */}
      {status?.recentSignals && status.recentSignals.length > 0 && (
        <div style={{ marginTop: '1.5rem' }}>
          <h3 title={t('The five newest signals (messages between the Admiral and captains). Open Signals for the full list.')}>{t('Recent Signals')}</h3>
          <div className="signal-list">
            {status.recentSignals.slice(0, 5).map((sig) => (
              <div key={sig.id} className="signal-item">
                <span title={t('Signal type, such as Progress, Completion, Error, Nudge or Mail.')}><StatusBadge status={sig.type || ''} /></span>
                <span>{sig.payload || sig.message}</span>
                <span className="text-dim" title={formatDateTime(sig.createdUtc)}>
                  {formatRelativeTime(sig.createdUtc)}
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
