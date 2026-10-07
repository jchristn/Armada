import { describe, expect, it } from 'vitest';
import type { MissionSummary } from '../types/models';
import {
  activeFleetActionRunsLink,
  canRestartFromHome,
  dashboardAlerts,
  filterRecentMissions,
  serverHealthIndicator,
  totalMissionCount,
  voyagePercent,
  type DashboardStatusData,
} from './dashboardStatus';

function status(partial: Partial<DashboardStatusData>): DashboardStatusData {
  return {
    totalCaptains: 2, idleCaptains: 1, workingCaptains: 1, stalledCaptains: 0, activeVoyages: 0,
    missionsByStatus: {}, voyages: [], recentSignals: [], ...partial,
  };
}

describe('dashboardStatus', () => {
  it('computes voyage progress and mission totals', () => {
    const vp = { voyage: { id: 'v', title: 't', status: 'Open' }, totalMissions: 3, completedMissions: 2, failedMissions: 0, vesselIds: [] };
    expect(voyagePercent(vp)).toBe(67);
    expect(voyagePercent({ ...vp, totalMissions: 0 })).toBe(0);
    expect(totalMissionCount(status({ missionsByStatus: { Pending: 2, Complete: 3 } }))).toBe(5);
    expect(totalMissionCount(null)).toBe(0);
  });

  it('raises the Home alerts in dashboard order', () => {
    const alerts = dashboardAlerts(status({ stalledCaptains: 1, missionsByStatus: { Failed: 2, LandingFailed: 1 } }));
    expect(alerts.map((a) => [a.level, a.link])).toEqual([['error', '/captains'], ['warning', '/missions'], ['warning', '/missions']]);
    expect(alerts[0].message).toBe('1 captain(s) stalled -- recovery attempts exhausted.');
    expect(dashboardAlerts(null)).toEqual([]);
  });

  it('flags pending work nobody picks up, and pending work with no captains', () => {
    const blocked = dashboardAlerts(status({ workingCaptains: 0, idleCaptains: 2, missionsByStatus: { Pending: 3 } }));
    expect(blocked).toHaveLength(1);
    expect(blocked[0].link).toBeUndefined();
    const none = dashboardAlerts(status({ totalCaptains: 0, idleCaptains: 0, workingCaptains: 0, missionsByStatus: { Pending: 1 } }));
    expect(none.map((a) => a.level)).toEqual(['error']);
  });

  it('filters recent missions', () => {
    const missions = [
      { id: 'a', status: 'Failed', vesselId: 'v1', captainId: 'c1' },
      { id: 'b', status: 'Complete', vesselId: 'v2', captainId: 'c1' },
    ] as MissionSummary[];
    expect(filterRecentMissions(missions, { status: '', vesselId: '', captainId: '' })).toHaveLength(2);
    expect(filterRecentMissions(missions, { status: 'Failed', vesselId: '', captainId: '' }).map((m) => m.id)).toEqual(['a']);
    expect(filterRecentMissions(missions, { status: '', vesselId: 'v2', captainId: 'c1' }).map((m) => m.id)).toEqual(['b']);
  });

  it('knows restartable statuses, the fleet action runs link, and the health light', () => {
    expect(['Failed', 'Cancelled', 'LandingFailed'].every(canRestartFromHome)).toBe(true);
    expect(canRestartFromHome('Complete')).toBe(false);
    expect(activeFleetActionRunsLink({ pending: 2, running: 0 })).toBe('/fleet-actions?tab=runs&status=Pending');
    expect(activeFleetActionRunsLink({ pending: 2, running: 1 })).toBe('/fleet-actions?tab=runs&status=Running');
    expect(activeFleetActionRunsLink(null)).toBe('/fleet-actions?tab=runs&status=Running');
    expect(serverHealthIndicator({ Status: 'healthy' })).toBe('healthy');
    expect(serverHealthIndicator({ status: 'Degraded' })).toBe('warning');
    expect(serverHealthIndicator({ status: 'down' })).toBe('error');
    expect(serverHealthIndicator(null)).toBe('error');
  });
});
