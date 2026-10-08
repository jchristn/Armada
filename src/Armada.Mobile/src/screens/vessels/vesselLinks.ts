import type { Vessel } from '@dashboard/types/models';

/**
 * App paths the vessel screens open, in one place. Where the dashboard passes router state (Dispatch, Run Check), the
 * mobile app passes the same values as query parameters on the destination route.
 */
export const vesselLinks = {
  detail: (id: string) => `/vessels/${encodeURIComponent(id)}`,
  edit: (id: string) => `/vessels/${encodeURIComponent(id)}?edit=1`,
  history: (id: string) => `/vessels/${encodeURIComponent(id)}/history`,
  onboarding: (id: string) => `/vessels/${encodeURIComponent(id)}/onboarding`,
  workspace: (id: string) => `/workspace/${encodeURIComponent(id)}`,
  fleet: (fleetId: string) => `/fleets/${encodeURIComponent(fleetId)}`,
  /** The dashboard's Dispatch "from a vessel" (router state fromVessel + vesselId). */
  dispatch: (id: string) => `/dispatch?vesselId=${encodeURIComponent(id)}`,
  /** The dashboard's Manage Objectives (/backlog?vesselId=&fleetId=, which redirects to the Backlog tab). */
  objectives: (vessel: Pick<Vessel, 'id' | 'fleetId'>) => {
    const params = new URLSearchParams({ tab: 'backlog', vesselId: vessel.id });
    if (vessel.fleetId) params.set('fleetId', vessel.fleetId);
    return `/dispatch?${params.toString()}`;
  },
  /** The dashboard's Run Check (router state prefill { vesselId, branchName } on /checks). */
  runCheck: (vessel: Pick<Vessel, 'id' | 'defaultBranch'>) => {
    const params = new URLSearchParams({ tab: 'checks', vesselId: vessel.id, branchName: vessel.defaultBranch || '' });
    return `/delivery?${params.toString()}`;
  },
  /** Repository health for one vessel (the dashboard's VesselHealthButton opens its detail). */
  health: (id: string) => `/vessels/health?vesselId=${encodeURIComponent(id)}`,
  /** Run a fleet action on these vessels (the dashboard's RunActionModal). */
  runAction: (ids: string[]) => `/fleet-actions?run=new&vessels=${ids.map(encodeURIComponent).join(',')}`,
};

/** Git sync of a vessel against its remote (GET /vessels/{id}/git-status); null counts are unknown. */
export interface GitSync {
  ahead: number | null;
  behind: number | null;
}

/**
 * The Vessels table's Sync cell as text: null when unknown, 'in sync', or the ahead / behind parts (each translated
 * by the caller from the returned keys).
 */
export function syncParts(sync: GitSync | undefined): { known: boolean; ahead: number; behind: number } {
  if (!sync || (sync.ahead === null && sync.behind === null)) return { known: false, ahead: 0, behind: 0 };
  return { known: true, ahead: sync.ahead ?? 0, behind: sync.behind ?? 0 };
}
