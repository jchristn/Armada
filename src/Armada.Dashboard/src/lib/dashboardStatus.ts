/**
 * Home (System Status) logic shared by the dashboard and the mobile app: the /api/v1/status snapshot shape the
 * Home page reads, voyage progress, the alert banners, and the recent-missions filter.
 */
import type { MissionSummary } from '../types/models';

export interface VoyageProgress {
  voyage: {
    id: string;
    title: string;
    status: string;
  };
  totalMissions: number;
  completedMissions: number;
  failedMissions: number;
  vesselIds: string[];
}

export interface DashboardStatusData {
  totalCaptains: number;
  idleCaptains: number;
  workingCaptains: number;
  stalledCaptains: number;
  activeVoyages: number;
  memoryPressureDeferrals?: number;
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

export interface DashboardAlert {
  level: 'error' | 'warning';
  message: string;
  action?: string;
  link?: string;
}

/** Statuses offered by the Home recent-missions status filter. */
export const RECENT_MISSION_STATUSES = ['Pending', 'Assigned', 'InProgress', 'Testing', 'Review', 'Complete', 'Failed', 'Cancelled'];

/** Completed share of a voyage's missions, as a whole percentage. */
export function voyagePercent(vp: VoyageProgress): number {
  if (!vp.totalMissions) return 0;
  return Math.round((vp.completedMissions / vp.totalMissions) * 100);
}

/** Sum of missions over every status. */
export function totalMissionCount(status: Pick<DashboardStatusData, 'missionsByStatus'> | null | undefined): number {
  if (!status?.missionsByStatus) return 0;
  return Object.values(status.missionsByStatus).reduce((sum, n) => sum + n, 0);
}

/** The Home alert banners for a status snapshot (stalled captains, failures, landing failures, blocked dispatch). */
export function dashboardAlerts(status: DashboardStatusData | null | undefined): DashboardAlert[] {
  if (!status) return [];
  const result: DashboardAlert[] = [];
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
}

export interface RecentMissionFilters {
  status: string;
  vesselId: string;
  captainId: string;
}

/** The Home recent-missions filter (empty values match everything). */
export function filterRecentMissions(missions: MissionSummary[], filters: RecentMissionFilters): MissionSummary[] {
  return missions.filter((m) => {
    if (filters.status && m.status !== filters.status) return false;
    if (filters.vesselId && m.vesselId !== filters.vesselId) return false;
    if (filters.captainId && m.captainId !== filters.captainId) return false;
    return true;
  });
}

/** Whether a mission can be restarted from the Home row menu. */
export function canRestartFromHome(status: string): boolean {
  return status === 'Failed' || status === 'Cancelled' || status === 'LandingFailed';
}

/** Fleet action runs link from the Home KPI: Pending when nothing runs but some wait, else Running. */
export function activeFleetActionRunsLink(runs: { pending: number; running: number } | null): string {
  return runs && runs.running === 0 && runs.pending > 0
    ? '/fleet-actions?tab=runs&status=Pending'
    : '/fleet-actions?tab=runs&status=Running';
}

export type ServerHealthIndicator = 'healthy' | 'warning' | 'error';

/** The sidebar health light for a /api/v1/status/health response (either property casing). */
export function serverHealthIndicator(data: Record<string, unknown> | null | undefined): ServerHealthIndicator {
  if (!data) return 'error';
  const status = String(data.status || data.Status || '').toLowerCase();
  if (status === 'healthy' || status === 'ok') return 'healthy';
  if (status === 'degraded' || status === 'warning') return 'warning';
  return 'error';
}
