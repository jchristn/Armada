import type { CaptainAssignmentOverride, Mission, Voyage } from '../types/models';

/** Pure logic of the voyage detail page, shared by the dashboard (pages/VoyageDetail.tsx) and the mobile app. */

export interface VoyageProgressSummary {
  total: number;
  completed: number;
  failed: number;
  /** Rounded percentage of completed missions (0 when there are none). */
  percent: number;
}

export function voyageProgress(missions: readonly Pick<Mission, 'status'>[]): VoyageProgressSummary {
  const completed = missions.filter((m) => m.status === 'Complete').length;
  const failed = missions.filter((m) => m.status === 'Failed').length;
  const percent = missions.length > 0 ? Math.round((completed / missions.length) * 100) : 0;
  return { total: missions.length, completed, failed, percent };
}

/** A voyage still running can be cancelled; one that is not can be deleted (purged). */
export function isVoyageActive(status: string | null | undefined): boolean {
  return status === 'Open' || status === 'InProgress';
}

/** The per-persona captain overrides recorded on the voyage (an empty list for none or unparseable JSON). */
export function parseCaptainOverrides(json: string | null | undefined): CaptainAssignmentOverride[] {
  if (!json) return [];
  try {
    const parsed: unknown = JSON.parse(json);
    return Array.isArray(parsed) ? (parsed as CaptainAssignmentOverride[]) : [];
  } catch {
    return [];
  }
}

/** A playbook delivery mode for display: 'InlineFullContent' becomes 'Inline Full Content'. */
export function formatDeliveryMode(value: string): string {
  return value.replace(/([a-z])([A-Z])/g, '$1 $2').trim();
}

/** The createMission payload that retries a failed mission with the same parameters. */
export function retryMissionPayload(m: Pick<Mission, 'title' | 'description' | 'vesselId' | 'voyageId' | 'priority'>): Partial<Mission> {
  return {
    title: m.title,
    description: m.description || undefined,
    vesselId: m.vesselId || undefined,
    voyageId: m.voyageId || undefined,
    priority: m.priority,
  } as Partial<Mission>;
}

/** getVoyage may answer `{ voyage, missions }` or the bare voyage; null missions means "load them separately". */
export interface VoyageResponseParts {
  voyage: Voyage;
  missions: Mission[] | null;
}

export function splitVoyageResponse(raw: unknown): VoyageResponseParts {
  const value = raw as { voyage?: Voyage; missions?: Mission[] } | Voyage;
  if (value && typeof value === 'object' && 'voyage' in value && value.voyage) {
    return { voyage: value.voyage, missions: value.missions || [] };
  }
  return { voyage: value as Voyage, missions: null };
}
